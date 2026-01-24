namespace CsvChecker.Services;

public static class AppVersion
{
    public static string? Get()
        => typeof(AppVersion).Assembly.GetName().Version?.ToString();
}
