using CsvChecker.Library.Models;

namespace CvsChecker.Library.Services.Interfaces;

public interface ICsvAnalyzer
{
    Task<CsvAnalysisResult> AnalyzeAsync(
        string fileName
        , byte[] bytes
        , CancellationToken ct
    );
}
