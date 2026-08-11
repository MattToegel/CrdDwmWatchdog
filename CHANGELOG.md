# Changelog

## 0.2.1 - 2026-08-11

- Restore the `win-x64` runtime packs explicitly in clean release runners before self-contained publishing.

## 0.2.0 - 2026-08-11

- Add Velopack installation and stable GitHub Releases self-update support.
- Check for updates after startup and every six hours.
- Download updates in the background and ask before restarting only the watchdog.
- Add reproducible CI, tag-triggered packaging, and release documentation.

## 0.1.0 - 2026-08-10

- Initial tray monitor, baseline capture, guarded CRD/DWM restarts, diagnostics, and self-tests.
