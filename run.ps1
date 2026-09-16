<#
.SYNOPSIS
    Runs the LogiFlow PLATFORM - the API and the Blazor UI - and nothing else.

.DESCRIPTION
    This is the application, not the tutorial. It starts src/LogiFlow.Api and
    src/LogiFlow.Web, each in its own window so each keeps its own console log and
    Ctrl+C stops one without stopping the other. It deliberately does NOT start
    src/LogiFlow.Academy.Api, which is the site's account service and references
    none of the layers these two are built on.

    Two things this machine needs that the README's commands assume:

      - A SQL Server the connection string can actually reach. appsettings.Development.json
        says `Server=localhost`, the DEFAULT instance. If that instance is not running but a
        named one is, this script finds it and overrides ConnectionStrings__SqlServer as an
        environment variable - which sits above appsettings in configuration precedence, so
        nothing in the repository is edited. See course/module-13-deployment/02-configuration.md.

      - An SDK that satisfies global.json.

    Both are checked before anything starts, because a failure here is far cheaper to read
    than the same failure forty lines into EF Core's startup migration.

.PARAMETER SqlInstance
    Override instance detection. 'localhost' for the default instance, or
    'localhost\MSSQLSERVER01' for a named one.

.PARAMETER ApiOnly
    Start the API and skip the Blazor UI. The UI is a CLIENT of the API over HTTP,
    so the API alone is a complete, usable system - browse it at /scalar/v1.

.PARAMETER NoBrowser
    Do not open a browser when the API reports ready.

.EXAMPLE
    .\run.ps1
.EXAMPLE
    .\run.ps1 -SqlInstance 'localhost\SQLEXPRESS'
.EXAMPLE
    .\run.ps1 -ApiOnly -NoBrowser
#>
[CmdletBinding()]
param(
    [string] $SqlInstance,
    [switch] $ApiOnly,
    [switch] $NoBrowser
)

$ErrorActionPreference = 'Stop'

$root   = $PSScriptRoot
$apiUrl = 'http://localhost:5199'
$webUrl = 'http://localhost:5280'

function Write-Step ($m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Write-Note ($m) { Write-Host "    $m" -ForegroundColor DarkGray }
function Write-Fail ($m) { Write-Host "!!! $m" -ForegroundColor Red }

# -- 1. The SDK ---------------------------------------------------------------------------
# `dotnet --version` resolves global.json, so a pin this machine cannot satisfy fails here
# rather than inside the build.
Write-Step 'Checking the .NET SDK'
$sdk = & dotnet --version 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Fail 'No installed SDK satisfies global.json.'
    Write-Host $sdk
    Write-Note 'Either install the pinned version, or relax "rollForward" in global.json.'
    exit 1
}
Write-Note ".NET SDK $sdk"

# -- 2. SQL Server ------------------------------------------------------------------------
# The default instance is registered as the service MSSQLSERVER; named instances as
# MSSQL$<NAME>. Prefer the default, because then the repository's own connection string is
# already correct and this script has nothing to override.
if (-not $SqlInstance) {
    Write-Step 'Looking for a running SQL Server'
    $services = @(Get-Service -Name 'MSSQLSERVER', 'MSSQL$*' -ErrorAction SilentlyContinue |
                  Where-Object { $_.Status -eq 'Running' })

    if ($services.Count -eq 0) {
        Write-Fail 'No SQL Server instance is running.'
        Write-Note 'Start one (as administrator), then run this again:'
        Write-Note '    Start-Service MSSQLSERVER'
        Write-Note 'Or use the container stack instead:'
        Write-Note '    docker compose up -d'
        Write-Note '    cd src\LogiFlow.Api; dotnet run --launch-profile docker'
        exit 1
    }

    $default = $services | Where-Object { $_.Name -eq 'MSSQLSERVER' } | Select-Object -First 1
    if ($default) {
        $SqlInstance = 'localhost'
    }
    else {
        # 'MSSQL$MSSQLSERVER01' -> 'localhost\MSSQLSERVER01'
        $named = $services[0].Name -replace '^MSSQL\$', ''
        $SqlInstance = "localhost\$named"
        if ($services.Count -gt 1) {
            Write-Note "$($services.Count) instances running; using the first. -SqlInstance overrides."
        }
    }
}

$connectionString = "Server=$SqlInstance;Database=LogiFlow;Integrated Security=True;TrustServerCertificate=True;Encrypt=True"

# Prove the credential works BEFORE starting the API. Reaching the instance is not the same
# as having permission to CREATE the database, but this catches the common failure - a wrong
# instance name - in five seconds instead of at the startup migration.
Write-Step "Testing $SqlInstance"
try {
    $probe = New-Object System.Data.SqlClient.SqlConnection("Server=$SqlInstance;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=5")
    $probe.Open()
    Write-Note "Connected. SQL Server $($probe.ServerVersion), Windows authentication."
    $probe.Close()
}
catch {
    Write-Fail "Cannot reach $SqlInstance."
    Write-Note $_.Exception.Message
    exit 1
}

# Only override when the repository's own string would be wrong. Setting it needlessly would
# hide the fact that appsettings.Development.json works as written.
if ($SqlInstance -eq 'localhost') {
    Write-Note 'Default instance: appsettings.Development.json is correct as written, nothing overridden.'
    $apiPrelude = ''
}
else {
    Write-Note 'Named instance: overriding ConnectionStrings__SqlServer, for the API window only.'
    $apiPrelude = "`$env:ConnectionStrings__SqlServer = '$connectionString'; "
}

# -- 3. Start ------------------------------------------------------------------------------
# Each project gets its own window: two long-running foreground processes cannot share one
# console, and interleaved Serilog output from an API and its own client is unreadable.
function Start-InWindow ($title, $workingDirectory, $prelude) {
    $command = "`$host.UI.RawUI.WindowTitle = '$title'; $prelude dotnet run"
    Start-Process -FilePath 'powershell.exe' `
                  -ArgumentList '-NoExit', '-NoProfile', '-Command', $command `
                  -WorkingDirectory $workingDirectory
}

Write-Step 'Starting LogiFlow.Api'
Start-InWindow 'LogiFlow.Api' (Join-Path $root 'src\LogiFlow.Api') $apiPrelude

# On a fresh database the API creates, migrates and seeds before it serves anything, so the
# first start is slow. Poll readiness rather than guess at a Start-Sleep.
Write-Step 'Waiting for the API to report ready'
Write-Note 'First run creates, migrates and seeds the database - allow a minute.'
$ready = $false
foreach ($attempt in 1..120) {
    Start-Sleep -Seconds 1
    try {
        $response = Invoke-WebRequest -Uri "$apiUrl/health/ready" -UseBasicParsing -TimeoutSec 3
        if ($response.StatusCode -eq 200) { $ready = $true; break }
    }
    catch { }   # not up yet; if it never comes up, the API window says why
}

if (-not $ready) {
    Write-Fail 'The API did not become ready within two minutes - see the LogiFlow.Api window.'
    exit 1
}
Write-Note 'Ready.'

if (-not $ApiOnly) {
    Write-Step 'Starting LogiFlow.Web'
    Start-InWindow 'LogiFlow.Web' (Join-Path $root 'src\LogiFlow.Web') ''
    Start-Sleep -Seconds 3
}

# -- 4. Where to go -------------------------------------------------------------------------
Write-Host ''
Write-Host '  LogiFlow is running' -ForegroundColor Green
Write-Host ''
if (-not $ApiOnly) {
    Write-Host "  UI              $webUrl"
}
Write-Host "  API (Scalar)    $apiUrl/scalar/v1"
Write-Host "  Health          $apiUrl/health/ready"
Write-Host '  Mail drop       src\LogiFlow.Api\App_Data\mail   (.eml - Mailing:Transport is File)'
Write-Host ''
Write-Host '  A fresh database has reference data but NO orders - orders are something the' -ForegroundColor DarkGray
Write-Host '  domain creates. requests\logiflow.http walks through making one.' -ForegroundColor DarkGray
Write-Host ''
Write-Host '  Stop it by closing the two windows, or Ctrl+C in each.' -ForegroundColor DarkGray
Write-Host ''

if (-not $NoBrowser) {
    if ($ApiOnly) { Start-Process "$apiUrl/scalar/v1" } else { Start-Process $webUrl }
}
