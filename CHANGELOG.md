# Changelog

All notable changes to this project are documented here. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning is
[SemVer](https://semver.org/).

Tags before `1.3` predate this file, so their contents aren't reconstructed here.

## [Unreleased]

## [1.9.2] - 2026-09-26

### Fixed
- If the "Start with Windows" task was deleted while the setting was on, the app no longer
  started with Windows and never noticed. It now registers the task again on launch.
- A startup task from before 1.9.0, which covers every user, made each launch try to replace it
  and log three warnings. Only an administrator can replace it, and it still starts the app, so
  it's now left alone. Why Windows refused a startup task is now written to the log.

## [1.9.1] - 2026-09-26

### Fixed
- Starting 1.9.0 reset the stick, scroll and keyboard deadzones and the touchpad speed to 0 in
  settings.json, which made the page scroll down on its own. Saved values now load unchanged, and
  settings damaged this way get those four back at their defaults (the old file is kept as
  `settings.json.v1.bak`). A settings file from this version makes 1.9.0 fall back to defaults.
- With a deadzone set to 0, a resting right stick no longer scrolls and a resting left stick no
  longer picks a keyboard letter.
- A right stick resting just outside its deadzone (common with stick drift) scrolled a full notch
  every 200ms, faster than a deliberate light push. Scroll speed now rises smoothly from the edge.
- When a settings correction couldn't be saved, every launch left another identical
  `settings.json.bad-*` copy. The same damage is now backed up once.

## [1.9.0] - 2026-09-25

### Changed
- Less CPU use when no Xbox controller is plugged in: empty controller slots are checked once a
  second instead of 125 times. A newly plugged Xbox controller is picked up within a second.
- While no PlayStation, Switch or other SDL controller is connected, the app no longer lists all
  game devices 125 times a second; it looks when Windows reports a new one.
- With no controller connected, the app checks for one 10 times a second instead of 125. It no
  longer raises Windows' timer resolution at all, which lets the PC save more power, and the
  cursor keeps a steadier pace on Windows 11 when the app has no visible window.
- With "Use HidHide" on, the virtual Xbox pad is only updated when the controller's state changes
  instead of 125 times a second.
- While a fullscreen app is in front, the app looks up which program it is once instead of ten
  times a second. If that program can't be identified, the reason is logged.
- Moving the cursor with the stick or touchpad now counts as using the PC, so the screen no
  longer dims or sleeps while you do it. Pointer speed and "Enhance pointer precision" still
  don't affect it.
- Builds no longer pull in an unused native SDL2 package, and the copyright year is fixed, so a
  release built from its tag always produces the same file.
- The on-screen keyboard is only redrawn when the highlighted letter or layer changes, instead of
  60 times a second, so it uses less CPU while open.
- Releases now include the standalone exe, SHA-256 checksums (`SHA256SUMS.txt`) and signed
  build provenance, so a download can be checked against the tagged build.
- Closing Settings now hides it instead of discarding it, so reopening is instant and it
  returns where you left it. Values changed elsewhere meanwhile are shown when it reopens.
- A Switch Pro Controller or Joy-Con over Bluetooth no longer has its motion sensor switched on
  and its reports forced to 60 a second, which drained its battery. The Bluetooth range guard now
  covers PlayStation controllers only.
- The lightbar's orange and green are dimmed to the brightness of SDL's own player colours, so a
  PlayStation controller's battery lasts longer. The colours themselves are unchanged.
- To save its battery, a DualSense's battery lights are only re-sent during the first 15 seconds
  after it connects and when the level changes, instead of every 3 seconds for good.

### Fixed
- With "Use HidHide" on, a fullscreen game kept the real controller hidden and saw only a frozen
  virtual pad. The controller is now handed back to the game while it's in the foreground.
- Buttons held while leaving a fullscreen game no longer fire as fresh presses (B sending
  Backspace, for example), and a held left click or touchpad drag is released when the game takes
  over.
- If the virtual Xbox pad failed to connect or dropped out, the real controller stayed hidden with
  nothing replacing it until the app restarted. The real controller now stays visible while the
  virtual pad is retried a few times, and it's hidden again once the virtual pad is up.
- Exiting while "Use HidHide" was switching the controller over could leave it hidden after the
  app closed, and a rare race between sending input and switching could crash the app.
- Removing the virtual Xbox pad could crash the app later during cleanup, and a failed attempt to
  create one leaked its driver handles.
- Right after "Use HidHide" plugged in its virtual Xbox pad, the app could briefly read that pad
  back as the real controller, freezing or latching input from a DualSense or other non-Xbox pad.
- An unexpected error while reading the controller closed the whole app. It's now logged and the
  controller keeps working. Pressing the stick right as the app started could also crash it.
- The on-screen keyboard typed `=`, `'` and `;` when `+`, `"` and `:` were picked.
- In the on-screen keyboard, moving the stick to a group with fewer letters than the one picked
  before highlighted an empty slot and A typed nothing. It now picks that group's last letter.
- After the controller dropped out and came back, a button held when it dropped could fire once
  more (a stray click from A, for example), and a stick held through the reconnect moved the
  cursor at full speed straight away. Buttons still held on reconnect now wait to be let go.
- Pushing the left stick fully into the lower-left corner froze the cursor on some controllers,
  and in the on-screen keyboard selected nothing.
- When Windows refused the app's input (with an app running as administrator in front), a press or
  release of the left button was lost for good, leaving it stuck up or down. It's now retried.
- Shift+key and Ctrl+click are sent in one piece, so other input can no longer land in between
  with Shift or Ctrl still held.
- If SDL failed to start, PlayStation, Switch and other non-Xbox controllers silently did nothing.
  The reason is now written to the log.
- Setting the PC's clock back paused the "Use HidHide" safety cutoff, and the check that keeps the
  app from reading its own virtual pad, until the clock caught up again.
- The app's own resource check opened a process handle twice a second and left it for the garbage
  collector to close.
- Installing the HidHide drivers failed when the Windows user folder had non-ASCII characters in
  its name.
- Checking a driver installer's signature leaked a little memory each time.
- A full disk or locked file while downloading the HidHide drivers was reported as "No network".
  It now says the drivers couldn't be saved. A download that stalls now gives up after a few
  minutes instead of waiting forever, and an unexpectedly large one is refused.
- "Start with Windows" registered a task for every user who signs in, which usually needed an
  admin prompt. It also didn't start on battery power, stopped when the laptop was unplugged, and
  was ended after three days. The task now covers only your account, never needs a prompt, and
  keeps running. After the app is moved, the task is pointed at the new location on next launch.
- Old log files piled up forever. Only the three most recent archived logs are now kept.
- Exit could hang forever if reading the controller got stuck. It now gives up after a few
  seconds and still unhides the controller.
- Changing settings in the first moments after the very first launch could clash with the app
  turning on "Start with Windows" in the background. A failure there is now logged.
- An error the app recovers from was reported as a crash, and one that kept repeating (in the
  on-screen keyboard, for example) could stack up error boxes. It now shows one short notice per
  run and keeps going; a real crash is still reported once.
- Signing out or shutting down with "Use HidHide" on could leave the controller hidden until the
  app next started. The app now unhides it and removes the virtual pad before Windows closes it.
- Restart from the tray menu usually just closed the app: the new copy started while the old one
  was still closing and took it for a second instance. It now starts once the old one is done.
- Double-clicking the tray icon while Settings was open opened a second Settings window, and the
  two could overwrite each other's changes. The open window is now brought to the front instead.

- A settings file with one bad value or a typo was replaced by defaults, losing every setting.
  Only the bad values are reset now, and a copy of the damaged file is kept next to it
  (`settings.json.bad-<date>`). Settings are also copied before being upgraded to a new format.
- After going back to an older version of the app, its defaults were saved over the newer
  version's settings and "Start with Windows" was switched back on. The older version now leaves
  that file and the startup setting alone.

- An empty list or an out-of-range number in the settings file kept Settings from opening or
  made the cursor misbehave. Such values are now reset or brought into the range the Settings
  sliders allow, and the log says which ones. The corrected file is saved once, with the
  original kept as `settings.json.bad-<date>`, so the warning doesn't repeat at every launch.

- A setting that changed after the Settings window closed, such as "Use HidHide" once its
  driver setup finished, relied on the closed window to be saved.
- Opening Settings could switch "Start with Windows" off, or show an admin prompt, when the
  background check of its real state failed or disagreed. That check now only updates the switch.
  Quick repeated clicks no longer run overlapping changes; the switch waits for each to finish.
- An error while checking or installing the HidHide drivers in Settings brought up the crash
  dialog. It's now logged with a short message under the switch. Closing Settings during a
  driver download stops it, and a second click can't start a second install.
- Each time Settings was opened, a font was created and never freed.
- In the "Full-screen apps" lists, the add box lost focus after each entry, so every new entry
  needed another click. Entries can now also be removed from the keyboard: Tab to one and press
  Delete, Backspace, Enter or Space. Screen readers announce each as "Remove <name>".
- Settings' close button couldn't be reached from the keyboard, and Esc did nothing. Tab now
  reaches it, Enter or Space presses it, Esc closes Settings, and screen readers call it "Close".

### Security
- Driver installers could be swapped by another program between the signature check and the
  admin prompt. The admin prompt now names Controller Magic itself, which copies the installers
  into a folder only administrators can change, checks the signature there, and runs them.

## [1.8.0] - 2026-09-24

### Added
- Touchpad mouse (DualSense, DualShock 4): slide to move the cursor, tap to left-click (tap twice
  to double-click), hold a finger still to right-click, and press the pad down to hold the left
  button for dragging. Speed is set in Settings under "Touchpad speed". In the on-screen keyboard
  the touchpad still drives the wheel.
- On-screen keyboard: clicking the touchpad types the letter picked with the stick, like A. While
  the stick is pushed it keeps the selection, so the finger pressing the pad can't move it.
- Bluetooth range guard (DualSense, DualShock 4, Switch Pro): when the controller stops sending
  reports, typically from being too far away, it is treated as centred with every button released
  instead of repeating its last state. The cursor no longer drifts and a held button no longer
  sticks; control returns as soon as reports resume.

### Fixed
- A Bluetooth DualSense that reconnected while "Use HidHide" was on could be ignored until the app
  restarted: no lightbar change and no input. SDL had latched onto the app's own virtual Xbox pad,
  which then kept itself alive. SDL now leaves every XInput pad to the XInput reader.
- Disconnecting the controller while holding A (or a drag) left the left mouse button held down.

## [1.7.0] - 2026-09-23

### Added
- Touchpad typing on the on-screen keyboard (DualSense, DualShock 4): the touchpad is the wheel -
  the finger's direction from the centre picks the letter group, its distance out picks the
  letter, and clicking the touchpad (or A) types it. The stick still works as before.
- DualSense/DualShock 4 lightbar shows the current mode: orange for mouse, green for the on-screen
  keyboard, dim blue while standing down for a fullscreen game. Over Bluetooth this switches a
  DualSense into its enhanced report mode (as Steam does) until it reconnects.
- DualSense player LEDs (under the touchpad) show the battery: 1 to 5 lit, all 5 while on USB.

### Fixed
- "Use HidHide" didn't actually hide the controller from Steam or Windows. HidHide only filters
  handles opened after hiding starts, and hiding only switched on once a controller was already
  connected - by which point Steam had always opened it. Hiding now stays on whenever the setting
  is, so a controller arrives already hidden; if one was connected before hiding started, a
  notification asks you to turn it off and on once.
- "Use HidHide" never hid Xbox controllers: SDL's XInput backend reports a placeholder path
  (`XInput#0`) instead of a real device path. Physical XInput controllers are now located
  directly; ViGEmBus's own virtual pad is left visible.
- Running the test suite appended warnings to the real `app.log`; tests now log to a temp file.
- The USER-object safety cutoff turned "Use HidHide" off silently; it now shows a notification.
- The on-screen keyboard could stop appearing after the app had run for many hours, while the
  controller kept working. Each time it opens, it now checks that it's actually visible (painted,
  shown, not hidden by Windows, on-screen, on top) and repairs itself only if not, logging which
  check failed.

### Changed
- The keyboard overlay window now only exists on screen while the keyboard is open, instead of
  sitting invisibly over the desktop all the time, and it never takes keyboard focus.

## [1.6.0] - 2026-09-14

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
