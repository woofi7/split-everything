using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using SplitEverything.Application.Abstractions;

namespace SplitEverything.Infrastructure;

public sealed class SmtpOptions
{
    public const string SectionName = "Email";

    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "split-everything@localhost";
    public string FromName { get; set; } = "Split Everything";
}

public sealed class SmtpEmailSender(SmtpOptions options) : IEmailSender
{
    public async Task SendAsync(
        string toEmail, string subject, string htmlBody, string textBody, CancellationToken ct = default)
    {
        using var client = new SmtpClient(options.SmtpHost, options.SmtpPort)
        {
            EnableSsl = options.UseStartTls,
            Credentials = string.IsNullOrWhiteSpace(options.Username)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(options.Username, options.Password)
        };

        using var message = new MailMessage
        {
            From = new MailAddress(options.FromAddress, options.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        message.To.Add(toEmail);
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(textBody, null, "text/plain"));

        await client.SendMailAsync(message, ct);
    }
}

/// <summary>
/// Used when no SMTP host is configured. Invites still work by link and QR, and the
/// body is logged so a self-hosted install can copy it out rather than wonder where
/// the email went.
/// </summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(
        string toEmail, string subject, string htmlBody, string textBody, CancellationToken ct = default)
    {
        logger.LogInformation("Email not sent (no SMTP configured). To: {To}, subject: {Subject}\n{Body}",
            toEmail, subject, textBody);
        return Task.CompletedTask;
    }
}
