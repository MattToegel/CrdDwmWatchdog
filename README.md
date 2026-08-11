# CRD & DWM Health Watchdog

A Windows notification-area utility that watches Chrome Remote Desktop (CRD) and Desktop Window Manager (DWM) for sustained resource growth. It offers guarded, user-approved component restarts without logging out of Windows or targeting VS Code, Docker, terminals, or other workload processes.

## Install

Download `CrdDwmWatchdog-win-Setup.exe` from the [latest release](https://github.com/MattToegel/CrdDwmWatchdog/releases/latest) and run it. The installer is per-user and does not require a separate .NET installation.

The installed watchdog checks stable GitHub Releases after startup and every six hours. It downloads an available Velopack update, then asks before restarting only the watchdog to apply it. Portable or development builds do not self-update.

> The current release pipeline is unsigned. Windows SmartScreen can warn on initial installation until code signing is configured. Do not bypass a warning if the download did not come from this repository's Releases page.

## Safety model

- Monitoring runs with normal user permissions.
- CRD or DWM is never restarted automatically.
- A component restart requires explicit confirmation and a Windows UAC prompt.
- Active CRD media sessions receive an additional interruption warning.
- The elevated helper accepts only `crd`, `dwm`, or `both` as fixed actions.
- Restarting DWM can briefly blank or flicker the display.
- Restarting CRD disconnects any active remote connection.

Active-session detection is best effort. Before approving a restart, confirm that no remote connection needs to remain active.

## Default monitoring

- Samples every 60 seconds.
- Requires three consecutive abnormal samples before prompting.
- Warns at 2,500 MB of DWM private memory or 1,200 MB above a clean baseline.
- Evaluates idle CRD growth separately from active streaming load.
- Supports snoozing and deferring a reminder until CRD appears idle.

Use the tray menu to open status, capture a healthy baseline, change thresholds, inspect logs, check for updates, or request a guarded restart.

## Local data

- Settings, learned baseline, and bounded logs: `%LOCALAPPDATA%\CrdDwmWatchdogData`
- Last elevated restart result: `%PROGRAMDATA%\CrdDwmWatchdog\last-restart.json`

Application updates preserve the data directory.

## Development

Requirements: Windows and the .NET 10 SDK.

```powershell
dotnet restore --locked-mode
dotnet build -c Release --no-restore
dotnet .\bin\Release\net10.0-windows\win-x64\CrdDwmWatchdog.dll --self-test
dotnet .\bin\Release\net10.0-windows\win-x64\CrdDwmWatchdog.dll --dry-run-restart both
```

Diagnostic modes never request elevation:

```text
CrdDwmWatchdog.exe --sample-once
CrdDwmWatchdog.exe --self-test
CrdDwmWatchdog.exe --dry-run-restart both
```

Official installers are produced only by the tag-triggered GitHub Actions release workflow. See [RELEASING.md](RELEASING.md).

## Source use

This repository currently has no software license. Its source is publicly visible, but no permission to copy, modify, or redistribute is granted unless a license is added later.
