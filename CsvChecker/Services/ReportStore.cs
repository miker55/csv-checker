using CsvChecker.Models;

namespace CsvChecker.Services;

public sealed class ReportStore
{
    private readonly Dictionary<string, CsvAnalysisResult> _results = new();
    private readonly object _lock = new();

    public string Put(CsvAnalysisResult result)
    {
        lock (_lock)
        {
            _results[result.Token] = result;
            return result.Token;
        }
    }

    public bool TryGet(string token, out CsvAnalysisResult? result)
    {
        lock (_lock)
        {
            return _results.TryGetValue(token, out result);
        }
    }
}
