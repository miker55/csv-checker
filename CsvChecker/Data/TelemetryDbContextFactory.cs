using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CsvChecker.Data;

public sealed class TelemetryDbContextFactory : IDesignTimeDbContextFactory<TelemetryDbContext>
{
    public TelemetryDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TelemetryDbContext>()
            .UseSqlite("Data Source=telemetry.sqlite")
            .Options;

        return new TelemetryDbContext(options);
    }
}
