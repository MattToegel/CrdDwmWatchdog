using System.Text.Json;

namespace CrdDwmWatchdog;

internal static class FileLog
{
    private static readonly object Sync = new();
    private const long MaxLogBytes = 5 * 1024 * 1024;
    private const long MaxHistoryBytes = 10 * 1024 * 1024;

    public static void Write(string message)
    {
        lock (Sync)
        {
            try
            {
                AppPaths.EnsureDataDirectory();
                RotateIfNeeded(AppPaths.LogPath, MaxLogBytes, 3);
                File.AppendAllText(AppPaths.LogPath,
                    $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
            catch
            {
            }
        }
    }

    public static void AppendSample(HealthSample sample)
    {
        lock (Sync)
        {
            try
            {
                AppPaths.EnsureDataDirectory();
                RotateIfNeeded(AppPaths.HistoryPath, MaxHistoryBytes, 2);
                var compactOptions = new JsonSerializerOptions(AppPaths.JsonOptions) { WriteIndented = false };
                File.AppendAllText(AppPaths.HistoryPath,
                    JsonSerializer.Serialize(sample, compactOptions) + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Write($"Could not append health sample: {ex.Message}");
            }
        }
    }

    private static void RotateIfNeeded(string path, long maxBytes, int generations)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < maxBytes)
        {
            return;
        }

        for (var index = generations; index >= 1; index--)
        {
            var source = index == 1 ? path : $"{path}.{index - 1}";
            var destination = $"{path}.{index}";
            if (!File.Exists(source))
            {
                continue;
            }

            File.Move(source, destination, true);
        }
    }
}
