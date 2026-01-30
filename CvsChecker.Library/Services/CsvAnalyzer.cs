using CsvChecker.Library.Models;
using CsvHelper;
using CsvHelper.Configuration;
using CvsChecker.Library.Helpers;
using CvsChecker.Library.Services.Interfaces;
using System.Globalization;
using System.Text;

namespace CvsChecker.Library.Services;

/// <summary>
/// FEATURES
///   1.  Encoding detection
///   2.  Newline detection
///   3.  Text decoding
///   4.  Delimiter detection
/// 
/// ISSUES
///   See CsvIssueType enum
/// </summary>
public sealed class CsvAnalyzer : ICsvAnalyzer
{
	private enum SimpleValueKind
	{
		Empty,
		Number,
		Date,
		Text
	}

	private static readonly string[] DateFormats =
	[
		"yyyy-MM-dd",
		"yyyy/MM/dd",
		"MM/dd/yyyy",
		"M/d/yyyy",
		"dd/MM/yyyy",
		"d/M/yyyy",
		"yyyy-MM-ddTHH:mm:ss",
		"yyyy-MM-ddTHH:mm:ssZ",
		"yyyy-MM-ddTHH:mm:ss.fffZ",
	];

	private readonly ITelemetryService _telemetry;

	public CsvAnalyzer(ITelemetryService telemetry)
	{
		_telemetry = telemetry;
	}

	public async Task<CsvAnalysisResult> AnalyzeAsync(
		string fileName
		, byte[] bytes
		, CancellationToken ct)
	{
		var issues = new List<CsvIssue>();

		Encoding? encoding = null;
		string? newline = null;
		char? delimiter = null;

		// Parse CSV using CsvHelper
		// We'll count row/column consistency and detect ragged rows.
		int? columnCount = null;
		int rowCount = 0;

		var trailingDelimiterHeader = false;

		string[] headerRecord = Array.Empty<string>();
		bool[] seenQuoted = Array.Empty<bool>();
		bool[] seenUnquoted = Array.Empty<bool>();
		int[] firstQuotedRow = Array.Empty<int>();
		int[] firstUnquotedRow = Array.Empty<int>();

		// Type tracking arrays (declared here for broader scope)
		int[] emptyCounts = Array.Empty<int>();
		int[] numberCounts = Array.Empty<int>();
		int[] dateCounts = Array.Empty<int>();
		int[] textCounts = Array.Empty<int>();
		int[] firstNonEmptyRow = Array.Empty<int>();
		int[] firstTextRow = Array.Empty<int>();
		int[] firstNumberRow = Array.Empty<int>();
		int[] firstDateRow = Array.Empty<int>();
		string?[] exampleText = Array.Empty<string?>();
		string?[] exampleNumber = Array.Empty<string?>();
		string?[] exampleDate = Array.Empty<string?>();

		try
		{
			// (simple v1): UTF-8 with BOM vs without, fallback to UTF-8
			encoding = DetectEncoding(bytes, out var hadBom);
			newline = DetectNewlines(bytes);

			var text = encoding.GetString(bytes);

			// (simple heuristic)
			delimiter = DetectDelimiter(text);

			if (hadBom)
			{
				issues.Add(new CsvIssue
				{
					IssueType = CsvIssueType.UTF8_BOM,
					Severity = CsvIssueSeverity.Info,
					Message = "File appears to contain a UTF-8 BOM. Some importers may mis-handle BOM in headers."
				});
			}

			if (newline == "Mixed")
			{
				issues.Add(new CsvIssue
				{
					IssueType = CsvIssueType.MIXED_LINE_ENDINGS,
					Severity = CsvIssueSeverity.Warning,
					Message = "File contains mixed line endings (LF/CRLF). This can confuse some parsers."
				});
			}

			// look for an unclosed quote first
			(bool flowControl, CsvAnalysisResult value) = DetectUnclosedQuote(fileName, bytes, encoding, newline, text, delimiter.Value, issues);
			if (!flowControl) return value;

			var config = new CsvConfiguration(CultureInfo.InvariantCulture)
			{
				HasHeaderRecord = true,
				Delimiter = delimiter.Value.ToString(),
				BadDataFound = null, // we'll handle issues ourselves later
				MissingFieldFound = null,
				DetectDelimiter = false
			};

			using var reader = new StringReader(text);
			using var csv = new CsvReader(reader, config);

			// Read header
			if (await csv.ReadAsync() && csv.ReadHeader())
			{
				rowCount++;
				var header = csv.HeaderRecord ?? Array.Empty<string>();
				headerRecord = header;
				columnCount = header.Length;

				// Initialize quoting trackers
				seenQuoted = new bool[columnCount.Value];
				seenUnquoted = new bool[columnCount.Value];
				firstQuotedRow = Enumerable.Repeat(-1, columnCount.Value).ToArray();
				firstUnquotedRow = Enumerable.Repeat(-1, columnCount.Value).ToArray();

				// Initialize type tracking arrays
				emptyCounts = new int[columnCount.Value];
				numberCounts = new int[columnCount.Value];
				dateCounts = new int[columnCount.Value];
				textCounts = new int[columnCount.Value];

				firstNonEmptyRow = Enumerable.Repeat(-1, columnCount.Value).ToArray();
				firstTextRow = Enumerable.Repeat(-1, columnCount.Value).ToArray();
				firstNumberRow = Enumerable.Repeat(-1, columnCount.Value).ToArray();
				firstDateRow = Enumerable.Repeat(-1, columnCount.Value).ToArray();

				exampleText = new string?[columnCount.Value];
				exampleNumber = new string?[columnCount.Value];
				exampleDate = new string?[columnCount.Value];

				// Header checks
				var dupes = header
					.Where(h => !string.IsNullOrWhiteSpace(h))
					.GroupBy(h => h.Trim(), StringComparer.OrdinalIgnoreCase)
					.Where(g => g.Count() > 1)
					.Select(g => g.Key)
					.ToList();

				foreach (var d in dupes)
				{
					issues.Add(new CsvIssue
					{
						IssueType = CsvIssueType.DUPLICATE_HEADER,
						Severity = CsvIssueSeverity.Error,
						Message = $"Duplicate header column name: '{d}'. Many importers require unique column names."
					});
				}

				// Empty header names and whitespace detection
				for (var i = 0; i < header.Length; i++)
				{
					if (string.IsNullOrWhiteSpace(header[i]))
					{
						if (i == header.Length - 1)
						{
							// Trailing delimiter case
							columnCount--;
							issues.Add(new CsvIssue
							{
								IssueType = CsvIssueType.TRAILING_DELIMITER_HEADER,
								Severity = CsvIssueSeverity.Warning,
								Message = "Header row appears to have a trailing delimiter, resulting in an empty final column.",
							});
						}
						else
						{
							issues.Add(new CsvIssue
							{
								IssueType = CsvIssueType.HEADER_EMPTY,
								Severity = CsvIssueSeverity.Warning,
								Message = $"Header column {i + 1} is empty.",
								RowNumber = 1
							});
						}
					}
					else if (!string.IsNullOrEmpty(header[i]) && header[i] != header[i].Trim())
					{
						// Header has leading or trailing whitespace
						issues.Add(new CsvIssue
						{
							IssueType = CsvIssueType.WHITESPACE_IN_HEADERS,
							Severity = CsvIssueSeverity.Warning,
							Message = $"Header column '{header[i]}' contains leading/trailing whitespace that may cause duplicate or mismatched columns.",
							RowNumber = 1,
							ColumnName = header[i].Trim()
						});
					}
				}
			}

			// Read records
			while (await csv.ReadAsync())
			{
				ct.ThrowIfCancellationRequested();
				rowCount++;

				// CsvHelper exposes current record fields via Parser.Record
				var record = csv.Parser.Record ?? Array.Empty<string>();
				var recordLength = trailingDelimiterHeader ? record.Length - 1 : record.Length;

				// Track quoting patterns per column
				if (columnCount is not null)
				{
					var quotedFlags = GetQuotedFlagsFromRawRecord(csv.Parser.RawRecord, delimiter.Value);

					// Only compare overlapping columns
					int n = Math.Min(Math.Min(record.Length, quotedFlags.Length), columnCount.Value);

					for (int c = 0; c < n; c++)
					{
						if (quotedFlags[c])
						{
							if (!seenQuoted[c])
							{
								seenQuoted[c] = true;
								firstQuotedRow[c] = rowCount;
							}
						}
						else
						{
							if (!seenUnquoted[c])
							{
								seenUnquoted[c] = true;
								firstUnquotedRow[c] = rowCount;
							}
						}
					}

					// Track value types per column
					for (int c = 0; c < n; c++)
					{
						var kind = ClassifyValue(record[c]);

						if (kind != SimpleValueKind.Empty && firstNonEmptyRow[c] < 0)
							firstNonEmptyRow[c] = rowCount;

						switch (kind)
						{
							case SimpleValueKind.Empty:
								emptyCounts[c]++;
								break;

							case SimpleValueKind.Number:
								numberCounts[c]++;
								if (firstNumberRow[c] < 0) firstNumberRow[c] = rowCount;
								exampleNumber[c] ??= record[c];
								break;

							case SimpleValueKind.Date:
								dateCounts[c]++;
								if (firstDateRow[c] < 0) firstDateRow[c] = rowCount;
								exampleDate[c] ??= record[c];
								break;

							case SimpleValueKind.Text:
								textCounts[c]++;
								if (firstTextRow[c] < 0) firstTextRow[c] = rowCount;
								exampleText[c] ??= record[c];
								break;
						}

						// Check for invalid dates
						if (!string.IsNullOrWhiteSpace(record[c]) && IsInvalidDate(record[c]))
						{
							string? colName = null;
							if (c < headerRecord.Length)
							{
								var name = headerRecord[c]?.Trim();
								if (!string.IsNullOrWhiteSpace(name))
									colName = name;
							}

							issues.Add(new CsvIssue
							{
								IssueType = CsvIssueType.INVALID_DATE,
								Severity = CsvIssueSeverity.Warning,
								Message = colName is not null
									? $"Column '{colName}' contains an invalid date value: '{record[c]}'."
									: $"Column {c + 1} contains an invalid date value: '{record[c]}'.",
								RowNumber = rowCount,
								ColumnName = colName
							});
						}
					}
				}

				// Whitespace-only row
				if (record.All(f => string.IsNullOrWhiteSpace(f)))
				{
					issues.Add(new CsvIssue
					{
						IssueType = CsvIssueType.BLANK_ROW,
						Severity = CsvIssueSeverity.Info, // or Warning if you want more visibility
						Message = "Row contains only whitespace and will be ignored by most importers.",
						RowNumber = rowCount
					});

					continue;
				}

				if (string.IsNullOrWhiteSpace(record[^1]))
				{
					issues.Add(new CsvIssue
					{
						IssueType = CsvIssueType.TRAILING_DELIMITER_ROW,
						Severity = CsvIssueSeverity.Warning,
						Message = "Row appears to have a trailing delimiter, resulting in an extra empty field.",
						RowNumber = rowCount
					});
				}
				else if (columnCount is not null && record.Length != columnCount.Value)
				{
					issues.Add(new CsvIssue
					{
						IssueType = CsvIssueType.ROW_WIDTH_MISMATCH,
						Severity = CsvIssueSeverity.Error,
						Message = $"Row has {record.Length} fields but header has {columnCount.Value}.",
						RowNumber = rowCount,
						Sample = SampleRow(record)
					});
				}
			}

			// Check for inconsistent quoting after all records are read
			if (columnCount is not null)
			{
				for (int c = 0; c < columnCount.Value; c++)
				{
					if (seenQuoted[c] && seenUnquoted[c])
					{
						string? colName = null;
						if (c < headerRecord.Length)
						{
							var name = headerRecord[c]?.Trim();
							if (!string.IsNullOrWhiteSpace(name))
								colName = name;
						}

						// pick a row number that points to the "second style" we discovered
						var qRow = firstQuotedRow[c];
						var uRow = firstUnquotedRow[c];
						var rowForIssue = (qRow > 0 && uRow > 0) ? Math.Max(qRow, uRow) : (qRow > 0 ? qRow : uRow);

						issues.Add(new CsvIssue
						{
							IssueType = CsvIssueType.INCONSISTENT_QUOTING,
							Severity = CsvIssueSeverity.Warning,
							Message = colName is not null
								? $"Column '{colName}' is inconsistently quoted (quoted at row {qRow}, unquoted at row {uRow}). Some importers may treat values inconsistently."
								: $"Column {c + 1} is inconsistently quoted (quoted at row {qRow}, unquoted at row {uRow}). Some importers may treat values inconsistently.",
							RowNumber = rowForIssue,
							ColumnName = colName
						});
					}
				}
			}

			// Check for column type instability
			if (columnCount is not null)
			{
				for (int c = 0; c < columnCount.Value; c++)
				{
					int nonEmpty = numberCounts[c] + dateCounts[c] + textCounts[c];
					if (nonEmpty < 10)
						continue;

					double pNum = (double)numberCounts[c] / nonEmpty;
					double pDate = (double)dateCounts[c] / nonEmpty;
					double pText = (double)textCounts[c] / nonEmpty;

					// "Significant" presence threshold
					bool hasNum = pNum >= 0.10;
					bool hasDate = pDate >= 0.10;
					bool hasText = pText >= 0.10;

					int kinds = (hasNum ? 1 : 0) + (hasDate ? 1 : 0) + (hasText ? 1 : 0);

					// Strong signal: mixed major kinds
					bool chaotic = kinds >= 2;

					// Also flag: mostly numeric/date but some text sneaks in
					if (!chaotic)
					{
						bool mostlyNumOrDate = (pNum >= 0.80) || (pDate >= 0.80);
						bool meaningfulText = pText >= 0.05;
						chaotic = mostlyNumOrDate && meaningfulText;
					}

					if (!chaotic)
						continue;

					string? colName = null;
					if (c < headerRecord.Length)
					{
						var name = headerRecord[c]?.Trim();
						if (!string.IsNullOrWhiteSpace(name))
							colName = name;
					}

					// Pick a row to point at: first "minority" kind if possible
					int rowHint = firstNonEmptyRow[c];
					if (pText > 0 && (pNum >= 0.80 || pDate >= 0.80) && firstTextRow[c] > 0)
						rowHint = firstTextRow[c];
					else if (pNum > 0 && pText >= 0.80 && firstNumberRow[c] > 0)
						rowHint = firstNumberRow[c];
					else if (pDate > 0 && pText >= 0.80 && firstDateRow[c] > 0)
						rowHint = firstDateRow[c];

					// Build a readable message
					string Describe(double p) => $"{Math.Round(p * 100)}%";
					var parts = new List<string>();
					if (numberCounts[c] > 0) parts.Add($"numbers ({Describe(pNum)})");
					if (dateCounts[c] > 0) parts.Add($"dates ({Describe(pDate)})");
					if (textCounts[c] > 0) parts.Add($"text ({Describe(pText)})");

					string example =
						exampleText[c] is not null ? $" Example text: '{Truncate(exampleText[c], 32)}'." :
						exampleDate[c] is not null ? $" Example date: '{Truncate(exampleDate[c], 32)}'." :
						exampleNumber[c] is not null ? $" Example number: '{Truncate(exampleNumber[c], 32)}'." :
						"";

					issues.Add(new CsvIssue
					{
						IssueType = CsvIssueType.COLUMN_TYPE_INSTABILITY,
						Severity = CsvIssueSeverity.Warning,
						Message = colName is not null
							? $"Column '{colName}' contains mixed value types: {string.Join(", ", parts)}.{example} This can cause importers to treat values inconsistently."
							: $"Column {c + 1} contains mixed value types: {string.Join(", ", parts)}.{example} This can cause importers to treat values inconsistently.",
						RowNumber = rowHint > 0 ? rowHint : null,
						ColumnName = colName
					});
				}
			}
		}
		catch (Exception ex)
		{
			await _telemetry.TryTrackAsync(
				eventType: TelemetryEventType.AnalysisFailed
				, rowCount: null
				, columnCount: null
				, fileSizeBytes: bytes.LongLength
				, issueCount: issues.Count
				, message: ex.Message
				, ct: ct
			);

			issues.Add(new CsvIssue
			{
				IssueType = CsvIssueType.CSV_PARSE_FAILED,
				Severity = CsvIssueSeverity.Error,
				Message = "The CSV could not be fully parsed due to a structural error. This file may be malformed or use features not supported by the current rules."
			});
		}

		var token = Guid.NewGuid().ToString("N");

		return new CsvAnalysisResult
		{
			Token = token,
			FileName = fileName,
			FileSizeBytes = bytes.LongLength,
			DetectedEncoding = encoding?.WebName.ToUpperInvariant(),
			DetectedNewline = newline,
			DetectedDelimiter = delimiter,
			RowCount = rowCount == 0 ? null : rowCount,
			ColumnCount = columnCount,
			Issues = issues.OrderBy(i => i.Severity).ThenBy(i => i.RowNumber).ToList()
		};
	}

	#region Error Checker Methods

	/// <summary>
	/// Structural validation: unclosed quoted field at EOF
	/// </summary>
	private (bool flowControl, CsvAnalysisResult value) DetectUnclosedQuote(
		string fileName
		, byte[] bytes
		, Encoding encoding
		, string newline
		, string text
		, char delimiter
		, List<CsvIssue> issues
	)
	{
		var unclosed = GetUnclosedQuoteStart(text, delimiter);
		if (unclosed.HasValue)
		{
			string? columnName = null;

			var headers = TryGetHeadersWithCsvHelper(text, delimiter);
			if (headers is not null)
			{
				var idx = unclosed.Value.FieldIndex - 1;
				if (idx >= 0 && idx < headers.Length)
				{
					var name = headers[idx]?.Trim();
					if (!string.IsNullOrWhiteSpace(name))
						columnName = name;
				}
			}

			issues.Add(new CsvIssue
			{
				IssueType = CsvIssueType.UNCLOSED_QUOTE,
				Severity = CsvIssueSeverity.Error,
				Message = "File ends with an unclosed quoted field. This CSV is malformed and may not import correctly.",
				RowNumber = unclosed.Value.Row,
				ColumnName = columnName
			});

			// Stop here: further parsing may be misleading for malformed CSV.
			var tokenEarly = Guid.NewGuid().ToString("N");
			return (flowControl: false, value: new CsvAnalysisResult
			{
				Token = tokenEarly,
				FileName = fileName,
				FileSizeBytes = bytes.LongLength,
				DetectedEncoding = encoding.WebName.ToUpperInvariant(),
				DetectedNewline = newline,
				DetectedDelimiter = delimiter,
				RowCount = null,       // unknown / unreliable
				ColumnCount = null,    // unknown / unreliable
				Issues = issues
			});
		}

		return (flowControl: true, value: new CsvAnalysisResult
		{
			Token = string.Empty,
			FileName = string.Empty,
			FileSizeBytes = 0,
		});
	}

	#endregion Error Checker Methods

	#region Helper Methods

	private Encoding DetectEncoding(byte[] bytes, out bool hadBom)
	{
		hadBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
		return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
	}

	private string DetectNewlines(byte[] bytes)
	{
		bool hasLf = false;
		bool hasCrlf = false;

		for (int i = 0; i < bytes.Length; i++)
		{
			if (bytes[i] == (byte)'\n')
			{
				if (i > 0 && bytes[i - 1] == (byte)'\r') hasCrlf = true;
				else hasLf = true;
			}
		}

		if (hasLf && hasCrlf) return "Mixed";
		if (hasCrlf) return "CRLF";
		if (hasLf) return "LF";
		return "Unknown";
	}

	private char DetectDelimiter(string text)
	{
		// Super simple heuristic: count first non-empty line occurrences
		var firstLine = text.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? "";
		var candidates = new[] { ',', '\t', ';', '|' };

		return candidates
			.Select(c => (c, count: firstLine.Count(ch => ch == c)))
			.OrderByDescending(x => x.count)
			.First().c;
	}

	private string[]? TryGetHeadersWithCsvHelper(string text, char delimiter)
	{
		try
		{
			var config = new CsvConfiguration(CultureInfo.InvariantCulture)
			{
				HasHeaderRecord = true,
				Delimiter = delimiter.ToString(),
				BadDataFound = null, // header parse best-effort
				MissingFieldFound = null,
				HeaderValidated = null
			};

			using var reader = new StringReader(text);
			using var csv = new CsvReader(reader, config);

			// Read first record and header
			if (!csv.Read())
				return null;

			if (!csv.ReadHeader())
				return null;

			return csv.HeaderRecord;
		}
		catch
		{
			return null;
		}
	}

	private (int Row, int FieldIndex)? GetUnclosedQuoteStart(string text, char delimiter)
	{
		bool inQuotes = false;
		int currentRow = 1;
		int fieldIndex = 1;

		int? quoteRow = null;
		int? quoteFieldIndex = null;

		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];

			if (c == '\r') continue;

			if (c == '\n')
			{
				currentRow++;
				fieldIndex = 1;
				continue;
			}

			if (c == delimiter && !inQuotes)
			{
				fieldIndex++;
				continue;
			}

			if (c == '"')
			{
				if (inQuotes && i + 1 < text.Length && text[i + 1] == '"')
				{
					i++;
					continue;
				}

				if (!inQuotes)
				{
					inQuotes = true;
					quoteRow = currentRow;
					quoteFieldIndex = fieldIndex;
				}
				else
				{
					inQuotes = false;
					quoteRow = null;
					quoteFieldIndex = null;
				}
			}
		}

		return inQuotes && quoteRow.HasValue && quoteFieldIndex.HasValue
			? (quoteRow.Value, quoteFieldIndex.Value)
			: null;
	}

	private string SampleRow(string[] fields)
	{
		var joined = string.Join(" | ", fields.Select(f => f.Length > 30 ? f[..30] + "…" : f));
		return joined.Length > 200 ? joined[..200] + "…" : joined;
	}

	private static bool[] GetQuotedFlagsFromRawRecord(
		string? rawRecord
		, char delimiter)
	{
		if (string.IsNullOrEmpty(rawRecord))
			return Array.Empty<bool>();

		// CsvHelper RawRecord usually includes line ending; trim it.
		var s = rawRecord.TrimEnd('\r', '\n');

		var flags = new List<bool>();
		int i = 0;

		while (true)
		{
			bool isQuoted = false;

			// Detect quoting only if the field begins with a quote
			if (i < s.Length && s[i] == '"')
			{
				isQuoted = true;
				i++; // consume opening quote

				// consume until closing quote, handling escaped quotes ("")
				while (i < s.Length)
				{
					if (s[i] == '"')
					{
						// Escaped quote
						if (i + 1 < s.Length && s[i + 1] == '"')
						{
							i += 2;
							continue;
						}

						// Closing quote
						i++;
						break;
					}

					i++;
				}

				// After a quoted field, consume until delimiter or end
				while (i < s.Length && s[i] != delimiter)
					i++;
			}
			else
			{
				// Unquoted field: consume until delimiter or end
				while (i < s.Length && s[i] != delimiter)
					i++;
			}

			flags.Add(isQuoted);

			if (i >= s.Length)
				break;

			// Consume delimiter and continue
			if (s[i] == delimiter)
				i++;
		}

		return flags.ToArray();
	}

	private static SimpleValueKind ClassifyValue(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return SimpleValueKind.Empty;

		var v = value.Trim();

		// Number (Invariant)
		if (decimal.TryParse(
			v
			, NumberStyles.Number
			, CultureInfo.InvariantCulture
			, out _))
		{
			return SimpleValueKind.Number;
		}

		// Date (strict-ish formats, invariant)
		if (DateTime.TryParseExact(
			v
			, DateFormats
			, CultureInfo.InvariantCulture
			, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal
			, out _))
		{
			return SimpleValueKind.Date;
		}

		return SimpleValueKind.Text;
	}

	private static string Truncate(
		string? s
		, int max)
	{
		if (string.IsNullOrEmpty(s) || s.Length <= max)
			return s ?? string.Empty;

		return s[..(max - 1)] + "…";
	}

	private static bool IsInvalidDate(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return false;

		var v = value.Trim();

		// Pattern: looks like a date (contains slashes or hyphens and digits)
		if (!v.Any(c => c == '/' || c == '-'))
			return false;

		// Split by common date separators
		var parts = v.Split(['/', '-'], StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length < 2 || parts.Length > 3)
			return false;

		// All parts should be numeric
		if (!parts.All(p => int.TryParse(p, out _)))
			return false;

		// Try parsing as various date formats
		var formats = new[]
		{
			"M/d/yyyy", "MM/dd/yyyy", "M/d/yy", "MM/dd/yy",
			"d/M/yyyy", "dd/MM/yyyy", "d/M/yy", "dd/MM/yy",
			"yyyy/M/d", "yyyy/MM/dd", "yy/M/d", "yy/MM/dd",
			"M-d-yyyy", "MM-dd-yyyy", "M-d-yy", "MM-dd-yy",
			"d-M-yyyy", "dd-MM-yyyy", "d-M-yy", "dd-MM-yy",
			"yyyy-M-d", "yyyy-MM-dd", "yy-M-d", "yy-MM-dd",
		};

		// If it parses successfully in ANY reasonable format, it's valid
		if (DateTime.TryParseExact(
			v
			, formats
			, CultureInfo.InvariantCulture
			, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal
			, out _))
		{
			return false;
		}

		// Check for objectively invalid parts
		// Parse the individual parts to check for impossible values
		var numbers = parts.Select(p => int.Parse(p)).ToArray();

		// Check for impossible month (>12) or day (>31)
		bool hasImpossibleValue = numbers.Any(n => n > 31 && n < 100) || // day/month range but invalid
								  numbers.Any(n => n > 12 && n <= 31); // likely month>12 or day>31

		// If it looks like a date pattern but has impossible values, it's invalid
		return hasImpossibleValue;
	}

	#endregion Helper Methods

}
