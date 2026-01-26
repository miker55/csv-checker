using CsvChecker.Library.Models;

namespace CvsChecker.Library.Services.Interfaces;

public interface IReportStore
{
    string Put(CsvAnalysisResult result);
    
    bool TryGet(
        string token
        , out CsvAnalysisResult? result
    );
    
    DateTimeOffset? GetExpiresUtc(string token);
}
