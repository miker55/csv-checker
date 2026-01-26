using CsvChecker.Data;
using CsvChecker.Data.Models;
using CvsChecker.Library.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CvsChecker.Library.Services;

public sealed class TelemetryService : ITelemetryService
{
    private readonly IDbContextFactory<TelemetryDbContext> _dbFactory;

    public TelemetryService(IDbContextFactory<TelemetryDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task TryWriteAsync(TelemetryEvent evt, CancellationToken ct)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            db.TelemetryEvents.Add(evt);
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Telemetry must never break the user flow.
        }
    }

	public async Task TryTrackAsync(
		string eventType
		, int? columnCount = null
		, long? fileSizeBytes = null
		, int? issueCount = null
		, string? message = null
		, int? rowCount = null
		, CancellationToken ct = default
	)
    {
		try
		{
			var evt = new TelemetryEvent
			{
				EventType = eventType,
				AppVersion = AppVersion.Get(),
				RowCount = rowCount,
				ColumnCount = columnCount,
				FileSizeBytes = fileSizeBytes,
				IssueCount = issueCount,
				Message = message,
				CreatedUtc = DateTime.UtcNow
			};
			await using var db = await _dbFactory.CreateDbContextAsync(ct);
			db.TelemetryEvents.Add(evt);
			await db.SaveChangesAsync(ct);
		}
		catch
		{
			// Telemetry must never break the user flow.
		}
	}

}
