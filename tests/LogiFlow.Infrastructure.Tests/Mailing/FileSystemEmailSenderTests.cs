using LogiFlow.Application.Abstractions.Mailing;
using LogiFlow.Application.Abstractions.Services;
using LogiFlow.Domain.ValueObjects;
using LogiFlow.Infrastructure.Mailing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;

namespace LogiFlow.Infrastructure.Tests.Mailing;

/// <summary>
/// Tests for the development transport that writes <c>.eml</c> files.
/// </summary>
/// <remarks>
/// <para>
/// <b>This one does touch I/O, and that is the right call.</b> Abstracting the file system behind
/// an interface purely to avoid writing a file would add a layer whose only user is a test, and
/// would stop the test proving the thing worth proving: that the bytes on disk are a message a
/// real mail client can open. A temporary directory, deleted afterwards, costs a millisecond.
/// </para>
/// <para>
/// Knowing when NOT to mock is as much of the skill as knowing how.
/// </para>
/// Covered in: <c>course/module-12-testing/</c>
/// </remarks>
public sealed class FileSystemEmailSenderTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"logiflow-mail-tests-{Guid.CreateVersion7():N}");

    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    public FileSystemEmailSenderTests() =>
        _clock.UtcNow.Returns(new DateTimeOffset(2026, 9, 4, 10, 30, 0, TimeSpan.Zero));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private FileSystemEmailSender Sender() =>
        new(
            Options.Create(new MailingOptions
            {
                FromAddress = "no-reply@logiflow.example",
                PickupDirectory = _directory,
            }),
            _clock,
            NullLogger<FileSystemEmailSender>.Instance);

    private static EmailMessage AMessage() =>
        EmailMessage.Create(
            EmailAddress.Create("mario@rossi.it").Value,
            "Order shipped",
            "Your order is on its way.",
            "<p>Your order is on its way.</p>",
            "shipment-dispatched");

    [Fact]
    public async Task CreatesThePickupDirectoryAndWritesTheMessage()
    {
        await Sender().SendAsync(AMessage(), CancellationToken.None);

        string[] files = Directory.GetFiles(_directory, "*.eml");

        files.Length.ShouldBe(1);
    }

    /// <summary>
    /// The file has to be a real RFC 5322 message, not a log line with an extension — that is
    /// the whole reason this transport exists rather than the logging one.
    /// </summary>
    [Fact]
    public async Task WritesAFileAMailClientCanOpen()
    {
        await Sender().SendAsync(AMessage(), CancellationToken.None);

        string path = Directory.GetFiles(_directory, "*.eml").Single();

        MimeMessage loaded = await MimeMessage.LoadAsync(path, CancellationToken.None);

        loaded.Subject.ShouldBe("Order shipped");
        loaded.To.Mailboxes.Single().Address.ShouldBe("mario@rossi.it");
        loaded.TextBody.ShouldNotBeNull().ShouldContain("on its way");
        loaded.HtmlBody.ShouldNotBeNull().ShouldContain("<p>");
    }

    [Fact]
    public async Task NamesFilesSortablyAndWithoutPersonalData()
    {
        await Sender().SendAsync(AMessage(), CancellationToken.None);

        string name = Path.GetFileName(Directory.GetFiles(_directory, "*.eml").Single());

        name.ShouldStartWith("20260904-103000");
        name.ShouldContain("shipment-dispatched");

        // The recipient must not be in the name: a directory listing, a backup or a screenshot
        // would then scatter customer addresses somewhere nobody is treating as personal data.
        name.ShouldNotContain("mario");
    }

    [Fact]
    public async Task WritesOneFilePerMessage()
    {
        FileSystemEmailSender sender = Sender();

        await sender.SendAsync(AMessage(), CancellationToken.None);
        await sender.SendAsync(AMessage(), CancellationToken.None);

        // Same clock, same category: the name still has to be unique, which is what the GUID
        // suffix is for. Two messages sent in the same millisecond is not a hypothetical when a
        // batch drains after an outage.
        Directory.GetFiles(_directory, "*.eml").Length.ShouldBe(2);
    }
}
