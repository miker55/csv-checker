using CsvChecker.Data;
using CsvChecker.Models.Data;
using CsvChecker.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CsvChecker.Services;

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
}
