using System.Text.Json;

namespace CrdDwmWatchdog;

internal static class AppPaths
{
    public static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CrdDwmWatchdogData");

    public static readonly string SettingsPath = Path.Combine(DataDirectory, "settings.json");
    public static readonly string BaselinePath = Path.Combine(DataDirectory, "baseline.json");
    public static readonly string LogPath = Path.Combine(DataDirectory, "watchdog.log");
    public static readonly string HistoryPath = Path.Combine(DataDirectory, "health-history.jsonl");

    public static readonly string SharedDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "CrdDwmWatchdog");

    public static readonly string RestartResultPath = Path.Combine(SharedDirectory, "last-restart.json");

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void EnsureDataDirectory() => Directory.CreateDirectory(DataDirectory);
}
