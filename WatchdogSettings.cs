using System.Text.Json;

namespace CrdDwmWatchdog;

internal sealed class WatchdogSettings
{
    public int PollIntervalSeconds { get; set; } = 60;
    public int ConsecutiveAlertSamples { get; set; } = 3;
    public int AlertCooldownMinutes { get; set; } = 60;
    public int SnoozeMinutes { get; set; } = 60;
    public double DwmPrivateMbThreshold { get; set; } = 2500;
    public double DwmGrowthOverBaselineMb { get; set; } = 1200;
    public double IdleCrdPrivateMbThreshold { get; set; } = 200;
    public double IdleCrdCpuCorePercentThreshold { get; set; } = 15;
    public int IdleCrdHandleThreshold { get; set; } = 900;
    public bool StartWithWindows { get; set; } = true;

    public static WatchdogSettings Load()
    {
        AppPaths.EnsureDataDirectory();
        try
        {
            if (File.Exists(AppPaths.SettingsPath))
            {
                var settings = JsonSerializer.Deserialize<WatchdogSettings>(
                    File.ReadAllText(AppPaths.SettingsPath), AppPaths.JsonOptions);
                if (settings is not null)
                {
                    settings.Normalize();
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            FileLog.Write($"Could not load settings: {ex.Message}");
        }

        var defaults = new WatchdogSettings();
        defaults.Save();
        return defaults;
    }

    public void Save()
    {
        Normalize();
        AppPaths.EnsureDataDirectory();
        File.WriteAllText(AppPaths.SettingsPath, JsonSerializer.Serialize(this, AppPaths.JsonOptions));
    }

    private void Normalize()
    {
        PollIntervalSeconds = Math.Clamp(PollIntervalSeconds, 15, 3600);
        ConsecutiveAlertSamples = Math.Clamp(ConsecutiveAlertSamples, 1, 20);
        AlertCooldownMinutes = Math.Clamp(AlertCooldownMinutes, 5, 1440);
        SnoozeMinutes = Math.Clamp(SnoozeMinutes, 5, 1440);
        DwmPrivateMbThreshold = Math.Clamp(DwmPrivateMbThreshold, 512, 32768);
        DwmGrowthOverBaselineMb = Math.Clamp(DwmGrowthOverBaselineMb, 256, 16384);
        IdleCrdPrivateMbThreshold = Math.Clamp(IdleCrdPrivateMbThreshold, 32, 8192);
        IdleCrdCpuCorePercentThreshold = Math.Clamp(IdleCrdCpuCorePercentThreshold, 1, 100);
        IdleCrdHandleThreshold = Math.Clamp(IdleCrdHandleThreshold, 100, 100000);
    }
}
