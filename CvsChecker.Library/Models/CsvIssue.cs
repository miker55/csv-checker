namespace CsvChecker.Library.Models;

public sealed class CsvIssue
{
    /// <summary>
    /// e.g. "ROW_WIDTH_MISMATCH"
    /// </summary>
    public required string Code { get; init; }
    public required CsvIssueSeverity Severity { get; init; }
    /// <summary>
    /// human readable
    /// </summary>
    public required string Message { get; init; }
    /// <summary>
    /// 1-based data row (including header row if you want)
    /// </summary>
    public int? RowNumber { get; init; }
    public string? ColumnName { get; init; }
    /// <summary>
    /// short snippet, not full row
    /// </summary>
    public string? Sample { get; init; }
}
