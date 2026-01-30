using CvsChecker.Library.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Mail;

namespace CvsChecker.Library.Services;

public sealed class EmailHelper : IEmailHelper
{
	private readonly IConfiguration _configuration;

	public EmailHelper(IConfiguration configuration)
	{
		_configuration = configuration;
	}

	public async Task SendAsync(
		string subject
		, string body
		, bool isHtml = false
		, CancellationToken ct = default
	)
	{
		var smtpHost = _configuration["Email:SmtpHost"] ?? "smtp.gmail.com";
		var smtpPort = int.Parse(_configuration["Email:SmtpPort"] ?? "587");
		var toEmail = _configuration["Email:ToEmail"]
			?? throw new InvalidOperationException("Email:ToEmail is not configured.");
		var fromEmail = _configuration["Email:FromEmail"]
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
}
