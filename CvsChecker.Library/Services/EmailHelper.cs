using CvsChecker.Library.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;

namespace CvsChecker.Library.Services;

public sealed class EmailHelper : IEmailHelper
{
	private readonly IConfiguration _configuration;
    private readonly ILogger<EmailHelper> _logger;

    public EmailHelper(
        IConfiguration configuration
        , ILogger<EmailHelper> logger
    )
	{
		_configuration = configuration;
        _logger = logger;
	}

	public async Task SendAsync(
		string subject
		, string body
		, bool isHtml = false
		, CancellationToken ct = default
	)
	{
        string? smtpHost = null;
        int smtpPort = 0;
        string? toEmail = null;
        string? fromEmail = null;
        try
		{
			smtpHost = _configuration["Email:SmtpHost"] ?? "smtp.gmail.com";
			smtpPort = int.Parse(_configuration["Email:SmtpPort"] ?? "587");
			toEmail = _configuration["Email:ToEmail"]
				?? throw new InvalidOperationException("Email:ToEmail is not configured.");
			fromEmail = _configuration["Email:FromEmail"]
				?? throw new InvalidOperationException("Email:FromEmail is not configured.");
			var fromPassword = _configuration["Email:FromPassword"]
				?? throw new InvalidOperationException("Email:FromPassword is not configured.");
			var fromName = _configuration["Email:FromName"] ?? fromEmail;

			using var message = new MailMessage
			{
				From = new MailAddress(fromEmail, fromName),
				Subject = subject,
				Body = body,
				IsBodyHtml = isHtml
			};

			message.To.Add(toEmail);

			using var smtpClient = new SmtpClient(smtpHost, smtpPort)
			{
				EnableSsl = true,
				Credentials = new NetworkCredential(fromEmail, fromPassword)
			};

			await smtpClient.SendMailAsync(message, ct);
		}
        catch (SmtpException ex)
        {
            // Azure will see this in Log Stream; users won't.
            _logger.LogError(ex,
                "Failed to send report email via {SmtpHost}:{SmtpPort}. StatusCode={StatusCode}. To={ToEmail}. From={FromEmail}",
                smtpHost, smtpPort, ex.StatusCode, toEmail, fromEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error sending report email. To={ToEmail}, From={FromEmail}, Host={SmtpHost}:{SmtpPort}",
                toEmail, fromEmail, smtpHost, smtpPort);
        }
    }
}
