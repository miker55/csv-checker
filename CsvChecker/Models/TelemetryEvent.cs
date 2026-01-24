namespace CsvChecker.Models;

public sealed class TelemetryEvent
{
    public long Id { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    // Keep it anonymous + minimal
    public string EventType { get; set; } = default!;  // e.g. "upload", "analysis_completed"
    public int? RowCount { get; set; }
    public int? ColumnCount { get; set; }
    public long? FileSizeBytes { get; set; }
    public int? IssueCount { get; set; }

    public string? AppVersion { get; set; }
}
