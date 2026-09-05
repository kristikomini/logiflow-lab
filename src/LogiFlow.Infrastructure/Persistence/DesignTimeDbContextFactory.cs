using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LogiFlow.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build a <see cref="LogiFlowDbContext"/> without starting the application.
/// </summary>
/// <remarks>
/// <para>
/// <b>What problem this solves.</b> The EF tools need a <c>DbContext</c> instance to read your
/// model. By default they try to start the startup project's host to get one from DI — which
/// means <c>dotnet ef migrations add</c> boots your web application, connects to Redis, and runs
/// your startup migrations. If any of that is unavailable you get the notoriously unhelpful:
/// </para>
/// <code>Unable to create an object of type 'LogiFlowDbContext'.</code>
/// <para>
/// When this interface is implemented, the tools use it instead and skip the host entirely.
/// Migrations then work from a laptop with nothing running, and in CI with no infrastructure at
/// all.
/// </para>
/// <para>
/// <b>The connection string here is never used to connect.</b> Generating a migration only needs
/// the provider (so EF knows SQL Server's type mappings); it issues no queries. That is why a
/// placeholder is acceptable — and why an environment variable overrides it for the times you
/// genuinely do want to point the tools at a real database.
/// </para>
/// <para>
/// Usage:
/// </para>
/// <code>
/// dotnet ef migrations add InitialCreate --project src/LogiFlow.Infrastructure --startup-project src/LogiFlow.Api
/// dotnet ef database update            --project src/LogiFlow.Infrastructure --startup-project src/LogiFlow.Api
/// </code>
/// Covered in: <c>course/module-06-efcore/09-migrations.md</c>
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<LogiFlowDbContext>
{
    private const string FallbackConnectionString =
        "Server=localhost,1433;Database=LogiFlow;User Id=sa;Password=Str0ng!Passw0rd;TrustServerCertificate=True";

    /// <inheritdoc />
    public LogiFlowDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__SqlServer")
            ?? FallbackConnectionString;

        DbContextOptions<LogiFlowDbContext> options =
            new DbContextOptionsBuilder<LogiFlowDbContext>()
                .UseSqlServer(connectionString, sql =>
                    sql.MigrationsHistoryTable("__EFMigrationsHistory", "logiflow"))
                .Options;

        return new LogiFlowDbContext(options);
    }
}
