using CsvChecker.Models;
using Microsoft.EntityFrameworkCore;

namespace CsvChecker.Data;

public sealed class TelemetryDbContext : DbContext
{
    public TelemetryDbContext(DbContextOptions<TelemetryDbContext> options) : base(options) { }

    public DbSet<TelemetryEvent> TelemetryEvents => Set<TelemetryEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<TelemetryEvent>();
        e.ToTable("telemetry_events");
        e.HasKey(x => x.Id);

        e.Property(x => x.EventType).HasMaxLength(64).IsRequired();
        e.Property(x => x.AppVersion).HasMaxLength(32);
        e.Property(x => x.Message).HasMaxLength(512);
    }
}
