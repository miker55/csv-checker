using CsvChecker.Models.Data;

namespace CsvChecker.Services.Interfaces;

public interface ITelemetryService
{
	Task TryWriteAsync(
		TelemetryEvent evt
		, CancellationToken ct
	);
}
