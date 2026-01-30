namespace CvsChecker.Library.Services.Interfaces;

public interface IEmailHelper
{
	Task SendAsync(
		string subject
		, string body
		, bool isHtml = false
		, CancellationToken ct = default
	);
}
