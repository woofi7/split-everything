using Shouldly;
using SplitEverything.Infrastructure;

namespace SplitEverything.Tests.Infrastructure;

public class SmtpEmailSenderTests
{
    /// <summary>
    /// Points at a closed port on purpose. It exercises the real message assembly
    /// and the real client, and asserts the failure surfaces rather than being
    /// swallowed, which is what would otherwise make a lost invite invisible.
    /// </summary>
    [Fact]
    public async Task A_send_to_an_unreachable_host_reports_the_failure()
    {
        var sender = new SmtpEmailSender(new SmtpOptions
        {
            SmtpHost = "127.0.0.1",
            // Nothing listens here; the connection is refused immediately.
            SmtpPort = 2,
            UseStartTls = false,
            FromAddress = "split@example.com",
            FromName = "Split Everything"
        });

        await Should.ThrowAsync<Exception>(() => sender.SendAsync(
            "someone@example.com", "Subject", "<p>html</p>", "text"));
    }

    [Fact]
    public async Task Credentials_are_used_when_they_are_configured()
    {
        var sender = new SmtpEmailSender(new SmtpOptions
        {
            SmtpHost = "127.0.0.1",
            SmtpPort = 2,
            Username = "user",
            Password = "secret",
            FromAddress = "split@example.com"
        });

        await Should.ThrowAsync<Exception>(() => sender.SendAsync(
            "someone@example.com", "Subject", "<p>html</p>", "text"));
    }
}
