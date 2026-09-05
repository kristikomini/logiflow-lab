using LogiFlow.Infrastructure.Mailing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LogiFlow.Infrastructure.Tests.Mailing;

/// <summary>
/// Tests that the configuration in appsettings actually reaches the options object.
/// </summary>
/// <remarks>
/// <para>
/// <b>Binding is the step everybody assumes works.</b> It usually does, and when it does not it
/// fails silently: the property keeps its default, the application starts, and the behaviour is
/// wrong in a way no exception describes. A misspelled section name, a property with no setter, a
/// value the enum parser does not recognise — each produces a working process doing the wrong
/// thing.
/// </para>
/// <para>
/// <b>The allow-list is the case worth pinning.</b> <c>AllowedRecipientDomains</c> is a
/// get-only property with an initialised list, which the binder populates by calling
/// <c>Add</c> rather than by assigning. That works — and it is exactly the shape somebody
/// "simplifies" into an array with a setter, at which point a safety guard configured in staging
/// quietly does nothing.
/// </para>
/// Covered in: <c>course/module-10-cross-cutting/05-mailing.md</c> and
/// <c>course/module-13-deployment/02-configuration.md</c>
/// </remarks>
public sealed class MailingOptionsBindingTests
{
    private static MailingOptions Bind(Dictionary<string, string?> settings)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        ServiceProvider provider = new ServiceCollection()
            .AddMailingOptionsForTest(configuration)
            .BuildServiceProvider();

        return provider.GetRequiredService<IOptions<MailingOptions>>().Value;
    }

    [Fact]
    public void BindsTheTransportEnumFromItsName()
    {
        MailingOptions options = Bind(new Dictionary<string, string?>
        {
            ["Mailing:Transport"] = "File",
        });

        options.Transport.ShouldBe(EmailTransport.File);
    }

    [Fact]
    public void BindsNestedSections()
    {
        MailingOptions options = Bind(new Dictionary<string, string?>
        {
            ["Mailing:Smtp:Host"] = "mail.example.com",
            ["Mailing:Smtp:Port"] = "587",
            ["Mailing:Smtp:UseTls"] = "true",
            ["Mailing:Delivery:BatchSize"] = "50",
        });

        options.Smtp.Host.ShouldBe("mail.example.com");
        options.Smtp.Port.ShouldBe(587);
        options.Smtp.UseTls.ShouldBeTrue();
        options.Delivery.BatchSize.ShouldBe(50);
    }

    [Fact]
    public void PopulatesTheGetOnlyAllowListFromAnArray()
    {
        MailingOptions options = Bind(new Dictionary<string, string?>
        {
            ["Mailing:AllowedRecipientDomains:0"] = "logiflow.example",
            ["Mailing:AllowedRecipientDomains:1"] = "qa.logiflow.example",
        });

        options.AllowedRecipientDomains.ShouldBe(["logiflow.example", "qa.logiflow.example"]);
    }

    [Fact]
    public void LeavesTheSafeDefaultsWhenTheSectionIsMissing()
    {
        MailingOptions options = Bind([]);

        // No configuration must never mean "send real mail to real people".
        options.Transport.ShouldBe(EmailTransport.Log);
        options.AllowedRecipientDomains.ShouldBeEmpty();
        options.RedirectAllTo.ShouldBeNull();
    }
}

/// <summary>
/// The options half of <c>AddMailing</c>, without the transports and the hosted service.
/// </summary>
/// <remarks>
/// Registering the full mailing stack here would drag in a <c>DbContext</c> and a background
/// worker to test a call to <c>Bind</c>. This is the same three lines the real registration uses
/// — kept beside the test that needs them rather than making the production method take a flag
/// that exists only for tests.
/// </remarks>
internal static class MailingOptionsTestRegistration
{
    public static IServiceCollection AddMailingOptionsForTest(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<MailingOptions>()
            .Bind(configuration.GetSection(MailingOptions.SectionName))
            .ValidateDataAnnotations();

        services.AddSingleton<IValidateOptions<MailingOptions>, MailingOptionsValidator>();

        return services;
    }
}
