namespace CsvChecker.Models.Data;

public sealed class TelemetryEvent
{
    public long Id { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    // Keep it anonymous + minimal
    public string EventType { get; set; } = default!;  // see TelemetryEventType
    public string? Message { get; set; }
    public int? RowCount { get; set; }
    public int? ColumnCount { get; set; }
    public long? FileSizeBytes { get; set; }
    public int? IssueCount { get; set; }

    public string? AppVersion { get; set; }
}
