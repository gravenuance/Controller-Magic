# Changelog

All notable changes to this project are documented here. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning is
[SemVer](https://semver.org/).

Tags before `1.3` predate this file, so their contents aren't reconstructed here.

## [Unreleased]

### Added
- A test project (`ControllerMagic.Tests`, xUnit v3 on Microsoft.Testing.Platform) covering
  `AppSettings`' load/migrate/save behavior, `AppLog`'s formatting and rotation, and
  `ControllerPoller.ComputeHoldRamp`'s S-curve.
- A persistent, leveled application log (`%LocalAppData%\ControllerMagic\app.log`, rotated at 5MB)
  replacing `Debug.WriteLine` calls that were compiled out of Release builds entirely.
- A settings schema-version field, so a future format change can migrate old `settings.json`
  files forward instead of misreading or dropping them.
- `AccessibleObject`s for the custom `Slider` and `ToggleSwitch` controls, and accessible
  names/roles throughout Settings, so screen readers can navigate and report values there.

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
