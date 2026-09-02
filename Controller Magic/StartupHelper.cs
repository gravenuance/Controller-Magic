using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;

namespace ControllerMagic
{
    // Starts the app at logon. Prefers a Task Scheduler logon trigger over the classic
    // HKCU...\Run key, since Windows deliberately staggers Run-key apps by several seconds after
    // logon to keep Explorer responsive first, while a scheduled task fires directly off the
    // logon event. Some locked-down (e.g. Group Policy managed / Enterprise) machines reject
    // unelevated task creation outright; when the user explicitly flips the Settings toggle we
    // retry once with a UAC prompt (many such policies only block the unelevated path), and if
    // that's declined or still fails we fall back to the Run key instead of leaving the toggle
    // silently non-functional.
    //
    // Every schtasks.exe invocation here is async: it's a subprocess spawn plus a wait, and the
    // elevated variant can block for as long as the user takes to respond to (or ignore) a UAC
    // prompt - none of that belongs on the UI thread. The registry reads/writes stay synchronous;
    // they're near-instant local calls, not worth the ceremony.
    internal static class StartupHelper
    {
        private const string TaskName = "ControllerMagic";
        private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "ControllerMagic";

        // Tags launches that came from the task/Run-key so Program.cs can tell an automatic
        // startup attempt apart from the user manually double-clicking the exe, and skip the
        // "already running" dialog for the former.
        private const string StartupArg = " --startup";

        public static async Task<bool> IsEnabledAsync(CancellationToken ct = default) =>
            await RunSchtasksAsync(ct, "/Query", "/TN", TaskName).ConfigureAwait(false) == 0 || GetRunKeyValue() != null;

        public static Task SetEnabledAsync(bool enabled, CancellationToken ct = default) =>
            enabled ? EnableAsync(ct) : DisableAsync(ct);

        private static async Task EnableAsync(CancellationToken ct)
        {
            string exe = Application.ExecutablePath;

            if (await TryCreateTaskAsync(exe, allowElevation: true, ct).ConfigureAwait(false))
                RemoveRunKeyValue();
            else
                SetRunKeyValue(exe);
        }

        private static async Task DisableAsync(CancellationToken ct)
        {
            if (await RunSchtasksAsync(ct, "/Query", "/TN", TaskName).ConfigureAwait(false) == 0)
            {
                if (await RunSchtasksAsync(ct, "/Delete", "/TN", TaskName, "/F").ConfigureAwait(false) != 0)
                    await RunSchtasksElevatedAsync(new[] { "/Delete", "/TN", TaskName, "/F" }, ct).ConfigureAwait(false);
            }

            RemoveRunKeyValue();
        }

        // One-time upgrade path for users who had startup enabled via the old Run-key-only
        // version. Runs silently at app launch, so it never prompts for elevation - it only
        // removes the Run key once the scheduled task actually took unelevated; on machines that
        // block that, the existing Run key is left alone so startup keeps working.
        public static async Task EnsureMigratedAsync(CancellationToken ct = default)
        {
            string? stored = GetRunKeyValue();
            if (stored == null) return;

            if (await TryCreateTaskAsync(ExtractExePath(stored), allowElevation: false, ct).ConfigureAwait(false))
                RemoveRunKeyValue();
        }

        private static string BuildCommand(string exe) => $"\"{exe}\"{StartupArg}";

        // Reverses BuildCommand(). Also handles values written by pre-refactor versions, which
        // stored a bare, unquoted path with no arguments at all.
        private static string ExtractExePath(string storedCommand)
        {
            string value = storedCommand.EndsWith(StartupArg, StringComparison.OrdinalIgnoreCase)
                ? storedCommand[..^StartupArg.Length]
                : storedCommand;
            return value.Trim('"');
        }

        private static async Task<bool> TryCreateTaskAsync(string exe, bool allowElevation, CancellationToken ct)
        {
            string[] createArgs =
            {
                "/Create", "/TN", TaskName,
                "/TR", BuildCommand(exe),
                "/SC", "ONLOGON",
                "/RL", "LIMITED",
                "/F"
            };

            if (await RunSchtasksAsync(ct, createArgs).ConfigureAwait(false) == 0)
                return true;

            if (!allowElevation)
            {
                AppLog.Default.Warning("StartupHelper: unelevated scheduled task creation failed; skipping the elevation prompt during background migration.");
                return false;
            }

            AppLog.Default.Warning("StartupHelper: unelevated scheduled task creation failed; retrying with a UAC prompt.");
            return await RunSchtasksElevatedAsync(createArgs, ct).ConfigureAwait(false);
        }

        private static string? GetRunKeyValue()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
                return key?.GetValue(AppName) as string;
            }
            catch (Exception ex)
            {
                AppLog.Default.Warning("StartupHelper: failed to read Run key", ex);
                return null;
            }
        }

        private static void SetRunKeyValue(string exe)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
                key?.SetValue(AppName, BuildCommand(exe));
            }
            catch (Exception ex)
            {
                AppLog.Default.Warning("StartupHelper: failed to write Run key", ex);
            }
        }

        private static void RemoveRunKeyValue()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
                if (key?.GetValue(AppName) != null)
                    key.DeleteValue(AppName, throwOnMissingValue: false);
            }
            catch (Exception ex)
            {
                AppLog.Default.Warning("StartupHelper: failed to remove Run key", ex);
            }
        }

        private static async Task<int> RunSchtasksAsync(CancellationToken ct, params string[] args)
        {
            var psi = new ProcessStartInfo("schtasks.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            try
            {
                using var proc = Process.Start(psi);
                if (proc == null) return -1;
                await proc.WaitForExitAsync(ct).ConfigureAwait(false);
                return proc.ExitCode;
            }
            catch (Exception ex)
            {
                AppLog.Default.Warning("StartupHelper: schtasks invocation failed", ex);
                return -1;
            }
        }

        // Retries an operation with a UAC consent prompt. Some locked-down (Group Policy managed)
        // machines only block *unelevated* task operations; an elevated token can often still
        // succeed. If the user declines the prompt, this just fails closed.
        private static async Task<bool> RunSchtasksElevatedAsync(string[] args, CancellationToken ct)
        {
            var psi = new ProcessStartInfo("schtasks.exe")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            try
            {
                using var proc = Process.Start(psi);
                if (proc == null) return false;
                await proc.WaitForExitAsync(ct).ConfigureAwait(false);
                return proc.ExitCode == 0;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED
            {
                AppLog.Default.Warning("StartupHelper: user declined the elevation prompt.");
                return false;
            }
            catch (Exception ex)
            {
                AppLog.Default.Warning("StartupHelper: elevated schtasks invocation failed", ex);
                return false;
            }
        }
    }
}
