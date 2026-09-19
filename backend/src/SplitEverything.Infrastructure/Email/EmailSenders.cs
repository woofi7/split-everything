using Microsoft.Extensions.Logging;
using SplitEverything.Application.Abstractions;

namespace SplitEverything.Infrastructure;

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
