using LogiFlow.Infrastructure.Mailing;
using Microsoft.Extensions.Options;

namespace LogiFlow.Infrastructure.Tests.Mailing;

/// <summary>
/// Tests for the startup validation of the mailing configuration.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every case here is a real deployment failure caught at the right moment.</b> With
/// <c>ValidateOnStart</c>, a missing SMTP host fails the deployment in front of the person who
/// caused it; without it, the same typo is discovered by the first customer who does not receive
/// an order confirmation, hours later, in a background worker's log.
/// </para>
/// <para>
/// <b>Configuration is code that nobody compiles.</b> These tests are the compiler.
/// </para>
/// Covered in: <c>course/module-10-cross-cutting/05-mailing.md</c> and
/// <c>course/module-13-deployment/02-configuration.md</c>
/// </remarks>
public sealed class MailingOptionsValidatorTests
{
    private readonly MailingOptionsValidator _validator = new();

    private static MailingOptions Valid() => new()
    {
        Transport = EmailTransport.Log,
        FromAddress = "no-reply@logiflow.example",
    };

    [Fact]
    public void AcceptsTheDefaults()
    {
        // The out-of-the-box configuration must be valid, or `git clone && dotnet run` fails
        // at startup with an options exception - the worst possible first impression.
        ValidateOptionsResult result = _validator.Validate(null, new MailingOptions());

        result.Succeeded.ShouldBeTrue(result.FailureMessage);
    }

    [Fact]
    public void RejectsAMalformedFromAddress()
    {
        MailingOptions options = Valid();
        options.FromAddress = "not-an-address";

        ValidateOptionsResult result = _validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Mailing:FromAddress");
    }

    [Fact]
    public void RejectsAMalformedRedirectAddress()
    {
        // The guard that prevents an incident must not itself be misconfigured into silence.
        MailingOptions options = Valid();
        options.RedirectAllTo = "qa@";

        _validator.Validate(null, options).Failed.ShouldBeTrue();
    }

    [Fact]
    public void RequiresAnSmtpHostWhenTheTransportIsSmtp()
    {
        MailingOptions options = Valid();
        options.Transport = EmailTransport.Smtp;
        options.Smtp.Host = "  ";

        ValidateOptionsResult result = _validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Mailing:Smtp:Host");
    }

    [Fact]
    public void RequiresAPasswordAlongsideAUserName()
    {
        // A user name with no password is the shape of a half-finished secret rotation, and the
        // runtime symptom is an opaque "535 authentication failed" hours later.
        MailingOptions options = Valid();
        options.Smtp.UserName = "logiflow";
        options.Smtp.Password = null;

        _validator.Validate(null, options).Failed.ShouldBeTrue();
    }

    /// <summary>
    /// The lease is what stops two workers delivering the same message. If it can expire while
    /// a send is still in flight, the queue produces duplicates under exactly the conditions —
    /// a slow mail server — where duplicates are most likely to be noticed.
    /// </summary>
    [Fact]
    public void RejectsALeaseShorterThanTheSendTimeout()
    {
        MailingOptions options = Valid();
        options.Smtp.TimeoutSeconds = 120;
        options.Delivery.LeaseSeconds = 60;

        ValidateOptionsResult result = _validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("LeaseSeconds");
    }

    [Fact]
    public void ReportsEveryProblemAtOnce()
    {
        // Not "fix one, redeploy, discover the next". A validator that stops at the first
        // failure turns a five-minute fix into five deployments.
        MailingOptions options = new()
        {
            Transport = EmailTransport.Smtp,
            FromAddress = "nonsense",
            RedirectAllTo = "also-nonsense",
        };

        options.Smtp.Host = string.Empty;

        ValidateOptionsResult result = _validator.Validate(null, options);

        result.Failures!.Count().ShouldBeGreaterThanOrEqualTo(3);
    }
}
