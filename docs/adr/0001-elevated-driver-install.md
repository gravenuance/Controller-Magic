# 0001: Install drivers from an elevated copy of the app
**Status:** Accepted · **Date:** 2026-09-25

## Context
"Use HidHide" needs the HidHide and ViGEmBus drivers, which the app downloads and installs behind
one UAC prompt. The installers were downloaded to `%LocalAppData%\ControllerMagic\drivers`,
checked with Authenticode, then run by an elevated `cmd.exe` script written to the same folder.
That folder is writable by every process running as the user, so any of them could swap an
installer or the script between the check and the elevated run. The UAC prompt only showed
Microsoft-signed `cmd.exe`, so the user had no way to notice. The script was also written as
UTF-8 without a BOM, so `cmd.exe` misread profile paths containing non-ASCII characters.

Constraints: one UAC prompt for both drivers; downloads stay unelevated; over-the-shoulder
elevation (a standard user typing an admin's password) must keep working, which means the
elevated process cannot find the downloads through its own `%LocalAppData%`.

## Options
- **Keep the script, verify inside it** (e.g. PowerShell `Get-AuthenticodeSignature` before each
  run). Small change, but the check and the run still read a user-writable path, so the race stays.
- **Elevated helper process shipped alongside the app.** Clean separation, but a second executable
  to build, sign and ship, against the single-file packaging rule.
- **Relaunch the app itself elevated in a dedicated install mode.** No new artifact, and the UAC
  prompt names Controller Magic. Needs a strictly validated command line and must not start the
  tray UI or trip the single-instance check.
- **Download elevated.** Removes the race, but puts networking and a third-party HTTP client in
  an admin process for no benefit.

## Decision
The app relaunches itself with `Verb=runas` and
`--install-drivers <hidhide|vigem|both> --source <download folder>`. `Program.Main` recognises that
mode before taking the single-instance mutex and never shows the tray UI. The elevated copy:

1. Accepts only that exact argument shape; the source must be an absolute, already-normalised
   path, and file names are fixed constants, never taken from the command line.
2. Creates or reuses `%ProgramData%\ControllerMagic\drivers`, refusing any level that is a link
   or owned by anyone but Administrators, SYSTEM or the elevated user. It then resets each level's
   ACL to Administrators and SYSTEM full control, with inheritance blocked.
3. Copies each installer into a new file there, holds it open to block writes and deletes,
   verifies that open copy with WinVerifyTrust against the expected signer, and runs it directly
   with the silent arguments.
4. Returns one exit code carrying each installer's result, which the unelevated app decodes into
   its existing outcomes. Any exit code outside that scheme counts as a failure.

The unelevated app still verifies the downloads first, but only so it doesn't show a UAC prompt
for a download that would be rejected anyway. The check that protects the install is the one in
step 3. This option was chosen because it closes the race without shipping a second executable.

## Consequences
- The file that gets verified is the file that runs, and no script is involved, so the
  non-ASCII path problem goes away too.
- Only an administrator can tamper with the staging folder, and an administrator doesn't need to.
  A standard user who creates `%ProgramData%\ControllerMagic` first can block driver installs
  (the ownership check refuses it), but can't get their own code run.
- The elevated process logs to the elevating account's `%LocalAppData%\ControllerMagic\app.log`.
  With over-the-shoulder elevation that is the admin's profile, not the user's.
- The installers are Advanced Installer bootstrappers that unpack into the elevated user's temp
  folder. How safely they do that is up to them and outside this app's control.
- Revisit if the app ever ships an installer or MSIX package: a service or a packaged elevated
  helper could replace the self-relaunch.
