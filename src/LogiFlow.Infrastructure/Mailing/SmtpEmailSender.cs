using System.Globalization;
using System.Net.Sockets;
using LogiFlow.Application.Abstractions.Mailing;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace LogiFlow.Infrastructure.Mailing;

/// <summary>
/// Delivers mail over SMTP, reusing one connection across messages.
/// </summary>
/// <remarks>
/// <para>
/// <b>Registered as a singleton, and holding a connection, which needs justifying.</b> The naive
/// implementation opens a client, connects, authenticates, sends one message and disconnects.
/// For a batch of twenty that is twenty TCP handshakes, twenty TLS negotiations and twenty
/// <c>AUTH</c> exchanges to deliver twenty small messages — and many relays rate-limit
/// connections per minute far more aggressively than messages, so the naive version is also the
/// one that gets throttled first.
/// </para>
/// <para>
/// <b>MailKit's <see cref="SmtpClient"/> is explicitly not thread-safe</b> — its documentation
/// says so in the first paragraph. A singleton holding one therefore needs a lock, and it must
/// be an async-friendly one: <c>lock</c> cannot be held across an <c>await</c>, and trying is
/// either a compile error or, with a manual monitor, a lock released on a different thread than
/// took it. <see cref="SemaphoreSlim"/> with <c>WaitAsync</c> is the tool for this.
/// </para>
/// <para>
/// <b>What the lock costs:</b> sends are serialised. That is correct for one connection, and it
/// is the reason the delivery worker sends a batch sequentially rather than with
/// <c>Task.WhenAll</c> — parallelism that immediately queues on a semaphore is parallelism you
/// paid for and did not get. A system that genuinely needs concurrent SMTP needs a pool of
/// clients, and that is a different class with a different name.
/// </para>
/// Covered in: <c>course/module-10-cross-cutting/05-mailing.md</c> and
/// <c>course/module-21-threading-and-memory-model/</c>
/// </remarks>
/// <param name="options">Connection settings, snapshotted at startup.</param>
/// <param name="logger">Logger.</param>
internal sealed class SmtpEmailSender(IOptions<MailingOptions> options, ILogger<SmtpEmailSender> logger)
    : IEmailSender, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly MailingOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    private SmtpClient? _client;
    private bool _disposed;

    /// <inheritdoc />
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ObjectDisposedException.ThrowIf(_disposed, this);

        MimeMessage mime = MimeMessageFactory.Create(message, _options);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            SmtpClient client = await ConnectedClientAsync(cancellationToken).ConfigureAwait(false);

            await client.SendAsync(mime, cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Sent {Category} email to {Recipient} via {Host}:{Port}",
                message.Category,
                message.To.Value,
                _options.Smtp.Host,
                _options.Smtp.Port);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not EmailDeliveryException)
        {
            // The connection is suspect after any failure - a half-completed command leaves the
            // session in a state the next send cannot rely on. Drop it; the next attempt builds
            // a fresh one. Reusing a broken connection produces the maddening failure mode where
            // one bad message poisons every message after it.
            await DiscardClientAsync().ConfigureAwait(false);

            throw Translate(ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // QUIT politely rather than dropping the socket. A relay that sees a connection vanish
        // mid-session logs it as an error and, on some servers, counts it towards an abuse
        // threshold.
        await DiscardClientAsync().ConfigureAwait(false);

        _gate.Dispose();
    }

    /// <summary>Returns a connected, authenticated client, reconnecting if the last one dropped.</summary>
    /// <remarks>
    /// Called only with the gate held, so it needs no locking of its own. Servers close idle
    /// sessions — RFC 5321 permits it after ten minutes, and most relays are far less patient —
    /// so <c>IsConnected</c> has to be checked on every send rather than once at startup.
    /// </remarks>
    private async Task<SmtpClient> ConnectedClientAsync(CancellationToken cancellationToken)
    {
        if (_client is { IsConnected: true })
        {
            return _client;
        }

        await DiscardClientAsync().ConfigureAwait(false);

        SmtpOptions smtp = _options.Smtp;

        var client = new SmtpClient
        {
            // MailKit's timeout is in milliseconds and covers each socket operation.
            Timeout = (int)TimeSpan.FromSeconds(smtp.TimeoutSeconds).TotalMilliseconds,
        };

        // StartTlsWhenAvailable, not StartTls: the local capture server used in development
        // advertises no TLS at all, and demanding it would fail to connect. In production
        // UseTls is on and this becomes a hard requirement - which is why it is a setting with
        // an environment-specific value rather than a constant somebody has to remember.
        SecureSocketOptions security = smtp.UseTls
            ? SecureSocketOptions.StartTls
            : SecureSocketOptions.StartTlsWhenAvailable;

        await client.ConnectAsync(smtp.Host, smtp.Port, security, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(smtp.UserName))
        {
            // Password is guaranteed non-null alongside a user name by MailingOptionsValidator;
            // the coalesce is for the compiler's benefit, not the server's.
            await client
                .AuthenticateAsync(smtp.UserName, smtp.Password ?? string.Empty, cancellationToken)
                .ConfigureAwait(false);
        }

        logger.LogInformation(
            "Connected to SMTP {Host}:{Port} (tls: {Tls}, authenticated: {Authenticated})",
            smtp.Host,
            smtp.Port,
            client.IsSecure,
            client.IsAuthenticated);

        _client = client;

        return client;
    }

    private async Task DiscardClientAsync()
    {
        if (_client is null)
        {
            return;
        }

        try
        {
            if (_client.IsConnected)
            {
                // No cancellation token: this runs on the failure path and during shutdown,
                // where the caller's token is usually already cancelled. Passing it would skip
                // the QUIT exactly when tidiness matters most.
                await _client.DisconnectAsync(quit: true).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Failing to hang up cleanly is not worth failing a send over, and this method is
            // itself called from a catch block - throwing here would replace the real error.
            logger.LogDebug(ex, "Failed to disconnect from SMTP cleanly; dropping the connection");
        }
        finally
        {
            _client.Dispose();
            _client = null;
        }
    }

    /// <summary>
    /// Maps a transport failure onto "worth retrying" or "never going to work".
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The SMTP reply code is the whole answer, and it is one digit.</b> A 4xx reply is a
    /// temporary negative — greylisting, rate limiting, mailbox full, server restarting — and
    /// the sender is expected to try again. A 5xx reply is permanent: the address does not
    /// exist, the message was rejected as spam, the relay refuses to carry it. Retrying a 5xx
    /// is how you get your sending domain onto a blocklist.
    /// </para>
    /// <para>
    /// <b>Authentication failures are treated as transient</b>, which is a judgement call worth
    /// being able to defend. Bad credentials will not fix themselves, so "permanent" looks
    /// right — but the same exception is thrown when a provider rate-limits authentication or
    /// when a rotation is halfway through, and marking those permanent dead-letters every
    /// message in the queue within one poll. Retrying costs a handful of attempts and an error
    /// in the log; not retrying loses the mail.
    /// </para>
    /// </remarks>
    private static EmailDeliveryException Translate(Exception ex) => ex switch
    {
        SmtpCommandException smtp => new EmailDeliveryException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"SMTP {(int)smtp.StatusCode} on {smtp.ErrorCode}: {smtp.Message}"),
            isTransient: (int)smtp.StatusCode is >= 400 and < 500,
            ex),

        // A protocol violation means the server said something SMTP does not allow. Usually a
        // proxy or an antivirus product mangling the session, and usually intermittent.
        SmtpProtocolException => new EmailDeliveryException("SMTP protocol error.", isTransient: true, ex),

        MailKit.Security.AuthenticationException => new EmailDeliveryException(
            "SMTP authentication failed. Check Mailing:Smtp credentials.",
            isTransient: true,
            ex),

        SslHandshakeException => new EmailDeliveryException(
            "TLS handshake with the SMTP server failed.",
            isTransient: true,
            ex),

        SocketException or IOException or TimeoutException => new EmailDeliveryException(
            "Could not reach the SMTP server.",
            isTransient: true,
            ex),

        // Anything else is a bug rather than a delivery problem - a malformed address that got
        // past validation, a null reference in this class. Retrying a bug five times just puts
        // it in the log five times, so it is permanent and loud.
        _ => new EmailDeliveryException($"Unexpected mail failure: {ex.Message}", isTransient: false, ex),
    };
}
