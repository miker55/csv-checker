namespace CvsChecker.Library.Services.Interfaces;

public interface ITelemetryService
{
	Task TryTrackAsync(
		string eventType
		, int? columnCount = null
		, long? fileSizeBytes = null
		, int? issueCount = null
		, string? message = null
		, int? rowCount = null
		, CancellationToken ct = default
	);
}