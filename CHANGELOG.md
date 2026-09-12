# Changelog

All notable changes to this project are documented here. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning is
[SemVer](https://semver.org/).

Tags before `1.3` predate this file, so their contents aren't reconstructed here.

## [Unreleased]

### Changed
- Upgraded `xunit.v3` to 4.0.0 and `Microsoft.Testing.Extensions.CodeCoverage` to 18.11.2, now that
  both resolve onto the same `Microsoft.Testing.Platform` 2.x line.

### Fixed
- `AppSettings.Save()` could race with itself when a background continuation and the Settings save
  debounce timer wrote `settings.json` at the same time, occasionally dropping an edit; saves are
  now serialized.
- `ControllerPoller`'s keyboard-mode/layer/sector/slot fields, read from the UI thread while written
  on the poll thread, are now `volatile` like the rest of the poller's cross-thread state.

## [1.4.0] - 2026-09-02

### Added
- Icons on the tray's right-click menu, and double-clicking the tray icon now opens Settings
  (same as the menu item).
- A test project (`ControllerMagic.Tests`, xUnit v3 on Microsoft.Testing.Platform) covering
  `AppSettings`' load/migrate/save behavior, `AppLog`'s formatting and rotation,
  `ControllerPoller`'s hold-ramp curve, deadzone/sector math, and button debounce.
- A persistent, leveled application log (`%LocalAppData%\ControllerMagic\app.log`, rotated at 5MB)
  replacing `Debug.WriteLine` calls that were compiled out of Release builds entirely.
- A settings schema-version field, so a future format change can migrate old `settings.json`
  files forward instead of misreading or dropping them.
- `AccessibleObject`s for the custom `Slider` and `ToggleSwitch` controls, and accessible
  names/roles throughout Settings, so screen readers can navigate and report values there.
- CI (build + test on every push/PR) and tag-triggered release automation
  (publish, package, create the GitHub Release with that version's changelog section as notes).

### Changed
- `settings.json` is now written atomically (temp file + rename) so a crash or power loss
  mid-save can't corrupt it.
- `StartupHelper`'s Task Scheduler / registry calls are now fully async, so enabling or disabling
  "Start with Windows" (including a UAC prompt on locked-down machines) no longer blocks the UI
  thread.
- Removed explanatory note text from Settings sliders and the startup toggle - the visualizations
  and control names already carry that meaning.
- Analyzer configuration moved from the `.csproj` into a repo-root `Directory.Build.props` and
  `.editorconfig`, so every project in the repo inherits the same settings.
- Upgraded to .NET 10.

### Fixed
- A native icon handle leak (`Properties.Resources.Controller` was re-deserializing a fresh icon
  on every access, never disposed) that could exhaust the process's Windows handle allowance after
  days of uptime, breaking the tray menu and Settings.
- A newly connected controller not being detected until an app restart, when a second
  (non-XInput) controller was plugged in while an XInput one was already active.
- An `SDL` event-queue leak and several `Enum.HasFlag` boxing hotspots in the controller poll
  loop.

## [1.3] - 2026-09-01
## [1.2] - 2026-08-26
## [1.1] - 2026-08-06
## [1.0] - 2026-08-05
