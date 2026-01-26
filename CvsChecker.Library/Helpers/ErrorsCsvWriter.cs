using CsvChecker.Library.Models;
using System.Text;

namespace CvsChecker.Library.Helpers;

public static class ErrorsCsvWriter
{
    public static byte[] ToCsvBytes(CsvAnalysisResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("code,severity,message,rowNumber,columnName,sample");

        foreach (var i in result.Issues)
        {
            sb.AppendLine(string.Join(",",
                Csv(i.Code),
                Csv(i.Severity.ToString()),
                Csv(i.Message),
                Csv(i.RowNumber?.ToString() ?? ""),
                Csv(i.ColumnName ?? ""),
                Csv(i.Sample ?? "")
            ));
        }

        return Encoding.UTF8.GetBytes(sb.ToString());

        static string Csv(string s)
        {
            // minimal CSV escaping
            if (s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
