namespace CsvChecker.Library.Models;

public sealed class CsvAnalysisResult
{
    public required string Token { get; init; }                 // used for downloads
    public required string FileName { get; init; }
    public required long FileSizeBytes { get; init; }

    public string? DetectedEncoding { get; init; }              // e.g. "UTF-8"
    public string? DetectedNewline { get; init; }               // "LF", "CRLF", "Mixed"
    public char? DetectedDelimiter { get; init; }               // ',', '\t', ';'

    public int? RowCount { get; init; }                         // includes header row if present
    public int? ColumnCount { get; init; }

    public required List<CsvIssue> Issues { get; init; } = new();
}
