using Velopack;
using Velopack.Sources;

namespace CrdDwmWatchdog;

internal sealed class UpdateService
{
    public const string RepositoryUrl = "https://github.com/MattToegel/CrdDwmWatchdog";

    private readonly UpdateManager? _manager;
    private VelopackAsset? _pendingRelease;

    public UpdateService()
    {
        try
        {
            _manager = new UpdateManager(new GithubSource(
                RepositoryUrl,
                accessToken: null,
                prerelease: false));
        }
        catch (Exception ex)
        {
            // Portable/development builds are intentionally not updateable.
            FileLog.Write($"Automatic updates unavailable: {ex.Message}");
        }
    }

    public bool IsAvailable => _manager?.IsInstalled == true;

    public string? PendingVersion => _pendingRelease?.Version?.ToString();

    public async Task<string?> CheckAndDownloadAsync()
    {
        if (_pendingRelease is not null)
        {
            return PendingVersion;
        }

        if (_manager?.IsInstalled != true)
        {
            return null;
        }

        var update = await _manager.CheckForUpdatesAsync();
        if (update is null)
        {
            return null;
        }

        await _manager.DownloadUpdatesAsync(update);
        _pendingRelease = update.TargetFullRelease;
        FileLog.Write($"Downloaded watchdog update {_pendingRelease.Version}.");
        return PendingVersion;
    }

    public bool BeginApplyAndRestart()
    {
        if (_manager is null || _pendingRelease is null)
        {
            return false;
        }

        _manager.WaitExitThenApplyUpdates(
            _pendingRelease,
            silent: false,
            restart: true,
            restartArgs: Array.Empty<string>());
        return true;
    }
}
