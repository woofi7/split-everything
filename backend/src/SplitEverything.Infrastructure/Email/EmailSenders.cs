using Microsoft.Extensions.Logging;
using SplitEverything.Application.Abstractions;

namespace SplitEverything.Infrastructure;

/// <summary>
/// Where an invite email goes: the log, and nowhere else.
///
/// This app sends no mail. It used to be able to, through an SMTP server somebody
/// had to configure, and nobody wanted to run one for a link that a phone can show
/// as a QR code across a table. Invites work by link and by QR, and the body is
/// still written to the log so a self-hosted install can copy it out rather than
/// wonder where it went.
/// </summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(
        string toEmail, string subject, string htmlBody, string textBody, CancellationToken ct = default)
    {
        logger.LogInformation("Invite email not sent; this app sends no mail. To: {To}, subject: {Subject}\n{Body}",
            toEmail, subject, textBody);
        return Task.CompletedTask;
    }
}
