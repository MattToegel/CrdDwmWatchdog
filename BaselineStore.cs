using System.Text.Json;

namespace CrdDwmWatchdog;

internal static class BaselineStore
{
    public static BaselineState? Load()
    {
        try
        {
            if (!File.Exists(AppPaths.BaselinePath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<BaselineState>(
                File.ReadAllText(AppPaths.BaselinePath), AppPaths.JsonOptions);
        }
        catch (Exception ex)
        {
            FileLog.Write($"Could not load baseline: {ex.Message}");
            return null;
        }
    }

    public static BaselineState Capture(HealthSample sample)
    {
        var baseline = new BaselineState(
            DateTimeOffset.Now,
            sample.Dwm?.PrivateMb,
            sample.Dwm?.Handles,
            sample.CrdActivity.IsLikelyActive ? null : sample.MainCrdProcess?.PrivateMb,
            sample.CrdActivity.IsLikelyActive ? null : sample.MainCrdProcess?.Handles);

        AppPaths.EnsureDataDirectory();
        File.WriteAllText(AppPaths.BaselinePath,
            JsonSerializer.Serialize(baseline, AppPaths.JsonOptions));
        FileLog.Write("Captured a new clean baseline.");
        return baseline;
    }
}
