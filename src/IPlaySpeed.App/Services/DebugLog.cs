using System.IO;
using System.Text.Json;

namespace IPlaySpeed.App.Services;

/// <summary>Debug session NDJSON logger (debug-189f09).</summary>
internal static class DebugLog
{
    private static string? _logPath;

    public static void Write(string location, string message, object? data = null, string? hypothesisId = null, string runId = "pre-fix")
    {
        try
        {
            string path = ResolveLogPath();
            var payload = new Dictionary<string, object?>
            {
                ["sessionId"] = "189f09",
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["location"] = location,
                ["message"] = message,
                ["runId"] = runId,
            };
            if (hypothesisId is not null) payload["hypothesisId"] = hypothesisId;
            if (data is not null) payload["data"] = data;
            File.AppendAllText(path, JsonSerializer.Serialize(payload) + Environment.NewLine);
        }
        catch { /* debug only */ }
    }

    private static string ResolveLogPath()
    {
        if (_logPath is not null) return _logPath;
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "iPlaySpeed.sln")))
            {
                _logPath = Path.Combine(dir, "debug-189f09.log");
                return _logPath;
            }
            dir = Path.GetDirectoryName(dir);
        }
        _logPath = Path.Combine(AppPaths.DataDir, "debug-189f09.log");
        return _logPath;
    }
}
