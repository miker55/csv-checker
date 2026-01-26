namespace CvsChecker.Helpers;

public static class PathProvider
{
    public static string GetTelemetryDbPath()
    {
        // Azure App Service (Linux) persistent storage is under /home
        var home = Environment.GetEnvironmentVariable("HOME");

        if (!string.IsNullOrEmpty(home))
        {
            var dir = Path.Combine(home, "site", "data");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "telemetry.sqlite");
        }

        // Local fallback
        return Path.Combine(AppContext.BaseDirectory, "telemetry.sqlite");
    }
}
