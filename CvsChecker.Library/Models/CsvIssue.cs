namespace CsvChecker.Library.Models;

public sealed class CsvIssue
{
    public required string Code { get; init; }          // e.g. "ROW_WIDTH_MISMATCH"
    public required CsvIssueSeverity Severity { get; init; }
    public required string Message { get; init; }       // human readable
    public int? RowNumber { get; init; }                // 1-based data row (including header row if you want)
    public string? ColumnName { get; init; }
    public string? Sample { get; init; }                // short snippet, not full row
}
