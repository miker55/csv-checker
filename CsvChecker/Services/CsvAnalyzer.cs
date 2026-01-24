using System.Text;
using CsvChecker.Models;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;

namespace CsvChecker.Services;

public sealed class CsvAnalyzer
{
    public async Task<CsvAnalysisResult> AnalyzeAsync(
        string fileName,
        byte[] bytes,
        CancellationToken ct)
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
        // We’ll count row/column consistency and detect ragged rows.
        int? columnCount = null;
        int rowCount = 0;

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            Delimiter = delimiter.ToString(),
            BadDataFound = null, // we’ll handle issues ourselves later
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
            issues.Add(new CsvIssue
            {
                Code = "CSV_PARSE_FAILED",
                Severity = CsvIssueSeverity.Error,
                Message = $"CSV parsing failed: {ex.Message}"
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

    private static Encoding DetectEncoding(byte[] bytes, out bool hadBom)
    {
        hadBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
    }

    private static string DetectNewlines(byte[] bytes)
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

    private static char DetectDelimiter(string text)
    {
        // Super simple heuristic: count first non-empty line occurrences
        var firstLine = text.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? "";
        var candidates = new[] { ',', '\t', ';', '|' };

        return candidates
            .Select(c => (c, count: firstLine.Count(ch => ch == c)))
            .OrderByDescending(x => x.count)
            .First().c;
    }

    private static string SampleRow(string[] fields)
    {
        var joined = string.Join(" | ", fields.Select(f => f.Length > 30 ? f[..30] + "…" : f));
        return joined.Length > 200 ? joined[..200] + "…" : joined;
    }
}
