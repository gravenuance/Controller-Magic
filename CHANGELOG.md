# Changelog

All notable changes to this project are documented here. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning is
[SemVer](https://semver.org/).

Tags before `1.3` predate this file, so their contents aren't reconstructed here.

## [Unreleased]

### Changed
- Renamed the "Suppress Guide button & focus jumps" setting to "Use HidHide" and removed its
  in-app anti-cheat caveat label (still noted in the README).

### Fixed
- "Use HidHide" crashed when it needed to install HidHide, because `HidHideSetupProvider` needs
  an `HttpClient` pre-configured with a specific base address and headers (normally wired up by
  HidHide's own DI registration) that this app never set up; now configured correctly, and driver
  download failures fail soft instead of crashing regardless of the specific exception type.
- The crash-recovery uncloak on every app startup logged a Warning even though "HidHide isn't
  installed" is the normal state for everyone who hasn't turned the setting on - now silent for
  that expected case.
- "Use HidHide" still failed to install HidHide even after the above fix: `Nefarius.Vicius.
  Abstractions` references `NJsonSchema.Annotations` and `Newtonsoft.Json` attribute types on its
  DTOs without declaring either as a runtime dependency, so `System.Text.Json`'s reflection-based
  type-info building threw a `FileNotFoundException` the first time it needed to inspect one of
  those types (surfaced one at a time, since the first fix only got as far as the next missing
  assembly). Both added directly as dependencies, confirmed against `Nefarius.Vicius.
  Abstractions.dll`'s own `GetReferencedAssemblies()` to be the complete set, and verified against
  the real update-check and download endpoints end-to-end before shipping.
- Installing HidHide silently rebooted the machine immediately and without warning: the silent-
  install flags were missing `/norestart`. Also fixed two related issues found alongside it: the
  installer's own exit code was never actually checked (only whether the process exited at all),
  so a real per-installer failure could have been reported as success; and the setting could
  silently revert to off with the drivers never fully installed, because an unprompted reboot
  killed the app mid-install before it could save anything or install the second driver. Now:
  `/norestart` is set, each installer's real exit code is captured and checked individually (0 =
  success, 3010 = needs a reboot to finish - shown to the user instead of guessed at, anything
  else = a genuine failure), and only whichever driver isn't already installed is re-downloaded
  and re-run.
- With "Use HidHide" active and a real controller connected, the stick stopped moving the mouse
  entirely (though the controller still showed as connected). Cause: XInput's public API exposes
  no device identity, only a slot number, so once ViGEmBus's virtual pad claimed an XInput slot
  this app's own slot-scanning could end up reading that virtual pad back instead of the real
  controller - and since the virtual pad's stick is deliberately kept neutral (see the focus-jump
  fix above), that read back as "connected, but never moves." Fixed by having the virtual pad's
  own XInput slot excluded from this app's read scan.
- `IXbox360Controller.UserIndex` (used for the fix above) throws until ViGEmBus reports the
  assigned slot back asynchronously, which isn't necessarily immediate after `Connect()` - this
  app queried it unguarded on every poll tick. Now caught and treated as "not yet known" rather
  than left to propagate as an unhandled exception on the poll thread.
- A severe Windows USER-object leak could climb to the ~10,000-per-process ceiling within seconds
  of a controller being connected, severely enough to break the tray icon's menu and the Settings
  dialog with no in-app way left to recover short of killing the process. Initially suspected to
  be tied to "Use HidHide" (HidHide/ViGEmBus), but isolated testing showed it happened with that
  feature fully off too. Root cause: SDL's `rawinput` joystick driver unconditionally correlates
  with the Windows.Gaming.Input API for extended Xbox-controller features whenever it's the active
  backend for a connected device, and that WinRT activation was what leaked - unrelated to this
  app's own drivers entirely. Fixed by disabling that one SDL driver (`SDL_HINT_JOYSTICK_RAWINPUT`
  in `Sdl2PadReader`'s constructor); this app's own XInput read path, and SDL's separate `HIDAPI`
  driver used for non-Xbox controllers (PS4/PS5, Switch Pro), are unaffected.

### Added
- Periodic (10-minute) logging of the process's Windows USER/GDI object counts, and a proactive
  safety cutoff that turns "Use HidHide" off automatically if the count ever climbs too far while
  active - added while chasing the leak above, kept afterward as a general defense-in-depth
  measure against any future leak of the same kind.

## [1.5.0] - 2026-09-13

### Added
- "Suppress Guide button & focus jumps" setting: hides the physical controller from the rest of
  the system (via HidHide) and re-emits it through a virtual controller (via ViGEmBus) so the
  Guide/Home/Steam button can no longer open Xbox Game Bar or Steam's Big Picture mode, and the
  left stick/D-pad can no longer drive Windows' built-in gamepad UI focus navigation while
  they're being used for mouse/keyboard emulation instead. Off by default; downloads and
  silently installs the two required drivers behind a single admin prompt the first time it's
  turned on, and automatically stands down while a non-excluded app is fullscreen or if the app
  doesn't exit cleanly.

## [1.4.1] - 2026-09-12

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
