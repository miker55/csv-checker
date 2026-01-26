namespace CsvChecker.Library.Models;

public sealed class CsvAnalysisResult
{
    /// <summary>
    /// Used for downloads
    /// </summary>
    public required string Token { get; init; }
    public required string FileName { get; init; }
    public required long FileSizeBytes { get; init; }

	/// <summary>
	/// e.g. "UTF-8"
	/// </summary>
	public string? DetectedEncoding { get; init; }
	/// <summary>
	/// "LF", "CRLF", "Mixed"
	/// </summary>
	public string? DetectedNewline { get; init; }
	/// <summary>
	/// ',', '\t', ';'
	/// </summary>
	public char? DetectedDelimiter { get; init; }

	/// <summary>
	/// Includes header row if present
	/// </summary>
	public int? RowCount { get; init; }
    public int? ColumnCount { get; init; }

    public required List<CsvIssue> Issues { get; init; } = new();
}
