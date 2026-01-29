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
		// (simple v1): UTF-8 with BOM vs without, fallback to UTF-8
		var encoding = DetectEncoding(bytes, out var hadBom);

		var newline = DetectNewlines(bytes);

		var text = encoding.GetString(bytes);

		// (simple heuristic)
		var delimiter = DetectDelimiter(text);

		var issues = new List<CsvIssue>();

		// Structural validation: unclosed quoted field at EOF
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
			return new CsvAnalysisResult
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
			};
		}

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

		// Parse CSV using CsvHelper
		// We'll count row/column consistency and detect ragged rows.
		int? columnCount = null;
		int rowCount = 0;

		var config = new CsvConfiguration(CultureInfo.InvariantCulture)
		{
			HasHeaderRecord = true,
			Delimiter = delimiter.ToString(),
			BadDataFound = null, // we'll handle issues ourselves later
			MissingFieldFound = null,
			DetectDelimiter = false
		};

		using var reader = new StringReader(text);
		using var csv = new CsvReader(reader, config);
		var trailingDelimiterHeader = false;

		string[] headerRecord = Array.Empty<string>();
		bool[] seenQuoted = Array.Empty<bool>();
		bool[] seenUnquoted = Array.Empty<bool>();
		int[] firstQuotedRow = Array.Empty<int>();
		int[] firstUnquotedRow = Array.Empty<int>();

		try
		{
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
					var quotedFlags = GetQuotedFlagsFromRawRecord(csv.Parser.RawRecord, delimiter);

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
			DetectedEncoding = encoding.WebName.ToUpperInvariant(),
			DetectedNewline = newline,
			DetectedDelimiter = delimiter,
			RowCount = rowCount == 0 ? null : rowCount,
			ColumnCount = columnCount,
			Issues = issues
		};
	}

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
}
