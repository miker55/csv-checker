using CsvChecker.Data;
using CsvChecker.Data.Models;
using CvsChecker.Helpers;
using CvsChecker.Library.Helpers;
using CvsChecker.Library.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CvsChecker.Library.Services;

public sealed class TelemetryService : ITelemetryService
{
	private readonly IDbContextFactory<TelemetryDbContext> _dbFactory;
	private readonly IEmailHelper _emailHelper;

	public TelemetryService(IDbContextFactory<
		TelemetryDbContext> dbFactory
		, IEmailHelper emailHelper
	)
	{
		_dbFactory = dbFactory;
		_emailHelper = emailHelper;
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
			if (eventType == TelemetryEventType.AnalysisFailed)
			{
				try
				{
					var emailBody = $@"Telemetry Event

EventType: {eventType}
Message: {message ?? "N/A"}
Row Count: {rowCount?.ToString("N0") ?? "N/A"}
Column Count: {columnCount?.ToString("N0") ?? "N/A"}
File Size: {(fileSizeBytes.HasValue ? FormatBytes(fileSizeBytes.Value) : "N/A")}
Issue Count: {issueCount?.ToString("N0") ?? "N/A"}";

					await _emailHelper.SendAsync(
						"Analysis Failed",
						emailBody,
						false,
						ct);
				}
				catch
				{
					// V2?
				}
			}

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

	private static string FormatBytes(long bytes)
	{
		var units = new[] { "B", "KB", "MB", "GB" };
		double size = bytes;
		int unit = 0;

		while (size >= 1024 && unit < units.Length - 1)
		{
			size /= 1024;
			unit++;
		}

		return unit == 0 ? $"{bytes} {units[unit]}" : $"{size:0.#} {units[unit]}";
	}

}
