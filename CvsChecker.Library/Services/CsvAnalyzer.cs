using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Text;
using CsvChecker.Library.Models;
using CvsChecker.Library.Services.Interfaces;

namespace CvsChecker.Library.Services;

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
		// Encoding detection (simple v1): UTF-8 with BOM vs without, fallback to UTF-8
		var encoding = DetectEncoding(bytes, out var hadBom);

		// Newline detection
		var newline = DetectNewlines(bytes);

		// Decode text
		var text = encoding.GetString(bytes);

		// Delimiter detection (simple heuristic)
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
				Code = "UNCLOSED_QUOTE",
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
				Code = "UTF8_BOM",
				Severity = CsvIssueSeverity.Info,
				Message = "File appears to contain a UTF-8 BOM. Some importers may mis-handle BOM in headers."
			});
		}

		if (newline == "Mixed")
		{
			issues.Add(new CsvIssue
			{
				Code = "LINE_ENDINGS_MIXED",
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

		try
		{
			// Read header
			if (await csv.ReadAsync() && csv.ReadHeader())
			{
				rowCount++;
				var header = csv.HeaderRecord ?? Array.Empty<string>();
				columnCount = header.Length;

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
						Code = "HEADER_DUPLICATE",
						Severity = CsvIssueSeverity.Error,
						Message = $"Duplicate header column name: '{d}'. Many importers require unique column names."
					});
				}

				// Empty header names
				for (var i = 0; i < header.Length; i++)
				{
					if (string.IsNullOrWhiteSpace(header[i]))
					{
						issues.Add(new CsvIssue
						{
							Code = "HEADER_EMPTY",
							Severity = CsvIssueSeverity.Warning,
							Message = $"Header column {i + 1} is empty.",
							RowNumber = 1
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

				// Whitespace-only row
				if (record.All(f => string.IsNullOrWhiteSpace(f)))
				{
					issues.Add(new CsvIssue
					{
						Code = "BLANK_ROW",
						Severity = CsvIssueSeverity.Info, // or Warning if you want more visibility
						Message = "Row contains only whitespace and will be ignored by most importers.",
						RowNumber = rowCount
					});

					continue;
				}

				if (columnCount is not null && record.Length != columnCount.Value)
				{
					issues.Add(new CsvIssue
					{
						Code = "ROW_WIDTH_MISMATCH",
						Severity = CsvIssueSeverity.Error,
						Message = $"Row has {record.Length} fields but header has {columnCount.Value}.",
						RowNumber = rowCount,
						Sample = SampleRow(record)
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
				Code = "CSV_PARSE_FAILED",
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
}
