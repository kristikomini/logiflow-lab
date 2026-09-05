using LogiFlow.Application.Abstractions.Mailing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogiFlow.Infrastructure.Mailing;

/// <summary>
/// Wires the mailing system: options, a transport, the safety guard, the queue and the worker.
/// </summary>
/// <remarks>
/// <para>
/// <b>The entry point to this folder.</b> Six classes and an interface do not explain themselves
/// in any order; this method does, because it is the only place where they are all named at
/// once and where the lifetime of each is a decision rather than an accident.
/// </para>
/// Covered in: <c>course/module-10-cross-cutting/05-mailing.md</c>
/// </remarks>
public static class MailingServiceCollectionExtensions
{
    /// <summary>Adds the mailing system.</summary>
    /// <param name="services">The container.</param>
    /// <param name="configuration">Application configuration; the <c>Mailing</c> section.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddMailing(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // ── Options ──────────────────────────────────────────────────────────────────────────
        // ValidateOnStart is the important call. Without it, IOptions validates on first use -
        // which for a background worker means the first time a customer places an order, in a
        // log line nobody is watching. With it, a bad SMTP configuration fails the deployment.
        services
            .AddOptions<MailingOptions>()
            .Bind(configuration.GetSection(MailingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Cross-field rules that data annotations cannot express. Registered as an interface the
        // options infrastructure discovers - there can be several, and they all run.
        services.AddSingleton<IValidateOptions<MailingOptions>, MailingOptionsValidator>();

        // ── Transports ───────────────────────────────────────────────────────────────────────
        // All three are registered; exactly one is resolved, by the factory below. Registering
        // them individually rather than reading configuration here keeps the choice in one
        // place - and means a test can override IEmailSender without knowing this method exists.
        //
        // Singletons: the SMTP one holds a pooled connection (see its remarks), and the other
        // two are stateless. A transient transport that opens a TCP connection per message is
        // the performance bug this design exists to avoid.
        services.AddSingleton<SmtpEmailSender>();
        services.AddSingleton<FileSystemEmailSender>();
        services.AddSingleton<LoggingEmailSender>();

        services.AddSingleton<IEmailSender>(provider =>
        {
            IOptions<MailingOptions> options = provider.GetRequiredService<IOptions<MailingOptions>>();

            IEmailSender transport = options.Value.Transport switch
            {
                EmailTransport.Smtp => provider.GetRequiredService<SmtpEmailSender>(),
                EmailTransport.File => provider.GetRequiredService<FileSystemEmailSender>(),

                // Log is the default, and so is the fallback for a value that fails to parse.
                // A configuration typo must not decide to send real mail.
                _ => provider.GetRequiredService<LoggingEmailSender>(),
            };

            // The decorator is applied unconditionally rather than "only outside production".
            // It is inert when neither guard is configured, and an environment check here would
            // be one more thing that can be wrong in the environment it is protecting.
            return new RedirectingEmailSender(
                transport,
                options,
                provider.GetRequiredService<ILogger<RedirectingEmailSender>>());
        });

        // ── Queue ────────────────────────────────────────────────────────────────────────────
        // Scoped, because it writes through the caller's DbContext and must share the caller's
        // transaction. A singleton here would need its own context and would silently break the
        // one guarantee the queue exists to provide.
        services.AddScoped<IEmailQueue, DatabaseEmailQueue>();

        // ── Delivery ─────────────────────────────────────────────────────────────────────────
        // A hosted service is a singleton and creates its own scope per iteration.
        services.AddHostedService<EmailDeliveryService>();

        return services;
    }
}
