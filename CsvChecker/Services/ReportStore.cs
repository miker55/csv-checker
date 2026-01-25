using CsvChecker.Models;

namespace CsvChecker.Services;

public sealed class ReportStore
{
	// Reports expire 1 hour after being stored
	private static readonly TimeSpan ReportTtl = TimeSpan.FromHours(1);

	private sealed record StoredReport(CsvAnalysisResult Result, DateTimeOffset CreatedUtc);

	private readonly Dictionary<string, StoredReport> _results = new();
	private readonly object _lock = new();

	public string Put(CsvAnalysisResult result)
	{
		lock (_lock)
		{
			_results[result.Token] = new StoredReport(result, DateTimeOffset.UtcNow);
			return result.Token;
		}
	}

	public bool TryGet(string token, out CsvAnalysisResult? result)
	{
		lock (_lock)
		{
			if (!_results.TryGetValue(token, out var stored))
			{
				result = null;
				return false;
			}

			if (IsExpired(stored))
			{
				_results.Remove(token);
				result = null;
				return false;
			}

			result = stored.Result;
			return true;
		}
	}

	/// <summary>
	/// Returns the UTC expiration time for the report token, or null if missing/expired.
	/// Useful for UI messaging ("expires at ...").
	/// </summary>
	public DateTimeOffset? GetExpiresUtc(string token)
	{
		lock (_lock)
		{
			if (!_results.TryGetValue(token, out var stored))
				return null;

			if (IsExpired(stored))
			{
				_results.Remove(token);
				return null;
			}

			return stored.CreatedUtc + ReportTtl;
		}
	}

	private static bool IsExpired(StoredReport stored) =>
		DateTimeOffset.UtcNow - stored.CreatedUtc > ReportTtl;
}
