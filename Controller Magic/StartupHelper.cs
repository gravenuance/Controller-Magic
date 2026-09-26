using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace ControllerMagic
{
    // Starts the app at logon. Prefers a Task Scheduler logon trigger over the classic
    // HKCU...\Run key, since Windows deliberately staggers Run-key apps by several seconds after
    // logon to keep Explorer responsive first, while a scheduled task fires directly off the
    // logon event. The task is registered from XML with a trigger for the current user only, which
    // a standard user may create; if that still fails (e.g. Group Policy blocks it) the Run key is
    // used instead of leaving the toggle silently non-functional.
    //
    // Every schtasks.exe invocation here is async: it's a subprocess spawn plus a wait - none of
    // that belongs on the UI thread. The registry reads/writes stay synchronous; they're
    // near-instant local calls, not worth the ceremony.
    internal static class StartupHelper
    {
        private const string TaskName = "ControllerMagic";
        private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "ControllerMagic";

        // Tags launches that came from the task/Run-key so Program.cs can tell an automatic
        // startup attempt apart from the user manually double-clicking the exe, and skip the
        // "already running" dialog for the former.
        private const string StartupArg = " " + StartupTaskDefinition.StartupArgument;

        private readonly record struct SchtasksResult(int ExitCode, string Output, string Error);

        public static async Task<bool> IsEnabledAsync(CancellationToken ct = default) =>
            (await RunSchtasksAsync(ct, "/Query", "/TN", TaskName).ConfigureAwait(false)).ExitCode == 0 || GetRunKeyValue() != null;

        public static Task SetEnabledAsync(bool enabled, CancellationToken ct = default) =>
            enabled ? EnableAsync(ct) : DisableAsync(ct);

        private static async Task EnableAsync(CancellationToken ct)
        {
            string exe = Application.ExecutablePath;

            if (await TryCreateTaskAsync(exe, ct).ConfigureAwait(false))
            {
                RemoveRunKeyValue();
                return;
            }

            AppLog.Default.Warning("StartupHelper: using the Run key for \"Start with Windows\" instead.");
            SetRunKeyValue(exe);
        }

        // A task an older version registered elevated can only be deleted elevated; the delete's
        // arguments are fixed, so the elevated schtasks reads nothing a user could have altered.
        private static async Task DisableAsync(CancellationToken ct)
        {
            if ((await RunSchtasksAsync(ct, "/Query", "/TN", TaskName).ConfigureAwait(false)).ExitCode == 0)
            {
                if ((await RunSchtasksAsync(ct, "/Delete", "/TN", TaskName, "/F").ConfigureAwait(false)).ExitCode != 0)
                    await RunSchtasksElevatedAsync(new[] { "/Delete", "/TN", TaskName, "/F" }, ct).ConfigureAwait(false);
            }

            RemoveRunKeyValue();
        }

        // Runs silently at every launch, so it never prompts for elevation. Moves users of the old
        // Run-key-only version onto the task, re-registers a task (or rewrites a Run key) that no
        // longer starts this exe - e.g. after the exe was moved - and registers one that went
        // missing while the setting is on, so "on" stays true.
        public static async Task EnsureMigratedAsync(bool runAtStartup, CancellationToken ct = default)
        {
            string exe = Application.ExecutablePath;

            string? stored = GetRunKeyValue();
            if (stored != null)
            {
                if (await TryCreateTaskAsync(exe, ct).ConfigureAwait(false))
                    RemoveRunKeyValue();
                else if (!StartupTaskDefinition.IsSameExecutable(ExtractExePath(stored), exe))
                    SetRunKeyValue(exe);
                return;
            }

            var query = await RunSchtasksAsync(ct, "/Query", "/TN", TaskName, "/XML").ConfigureAwait(false);
            switch (StartupTaskDefinition.Plan(runAtStartup, query.ExitCode == 0 ? query.Output : null, exe))
            {
                case StartupTaskAction.Register:
                    AppLog.Default.Info("StartupHelper: \"Start with Windows\" is on but nothing is registered; registering it.");
                    await EnableAsync(ct).ConfigureAwait(false);
                    break;
                case StartupTaskAction.Reregister:
                    AppLog.Default.Info("StartupHelper: the startup task doesn't start this exe; re-registering it.");
                    await EnableAsync(ct).ConfigureAwait(false);
                    break;
                case StartupTaskAction.None:
                    break;
            }
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

        // No elevated retry: an elevated schtasks reading an XML file from a user-writable temp
        // folder could be fed a swapped file that registers something to run with admin rights.
        private static async Task<bool> TryCreateTaskAsync(string exe, CancellationToken ct)
        {
            string? userSid;
            using (var identity = WindowsIdentity.GetCurrent())
                userSid = identity.User?.Value;
            if (userSid == null)
            {
                AppLog.Default.Warning("StartupHelper: current user has no SID; can't register the startup task.");
                return false;
            }

            string? xmlPath = await WriteTaskXmlAsync(StartupTaskDefinition.BuildXml(exe, userSid), ct).ConfigureAwait(false);
            if (xmlPath == null)
                return false;

            try
            {
                var create = await RunSchtasksAsync(ct, "/Create", "/TN", TaskName, "/XML", xmlPath, "/F").ConfigureAwait(false);
                if (create.ExitCode == 0)
                    return true;

                AppLog.Default.Warning($"StartupHelper: schtasks couldn't create the startup task (exit {create.ExitCode}): {create.Error.Trim()}");
                return false;
            }
            finally
            {
                TryDelete(xmlPath);
            }
        }

        // Written under a temporary name and renamed, so schtasks never reads a half-written file.
        // UTF-16 with a BOM is the encoding Task Scheduler itself exports and reads most reliably.
        private static async Task<string?> WriteTaskXmlAsync(string xml, CancellationToken ct)
        {
            string finalPath = Path.Combine(Path.GetTempPath(), $"ControllerMagic-startup-{Guid.NewGuid():N}.xml");
            string tempPath = finalPath + ".tmp";
            try
            {
                await File.WriteAllTextAsync(tempPath, xml, Encoding.Unicode, ct).ConfigureAwait(false);
                File.Move(tempPath, finalPath);
                return finalPath;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Default.Warning("StartupHelper: could not write the startup task definition", ex);
                TryDelete(tempPath);
                return null;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Default.Warning($"StartupHelper: failed to remove {path}", ex);
            }
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

        // Both pipes are drained while waiting: a redirected stream nobody reads blocks schtasks
        // once its buffer fills, which /Query /XML's output can do.
        private static async Task<SchtasksResult> RunSchtasksAsync(CancellationToken ct, params string[] args)
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
                if (proc == null) return new SchtasksResult(-1, string.Empty, string.Empty);

                var output = proc.StandardOutput.ReadToEndAsync(ct);
                var error = proc.StandardError.ReadToEndAsync(ct);
                await Task.WhenAll(output, error, proc.WaitForExitAsync(ct)).ConfigureAwait(false);
                return new SchtasksResult(proc.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
            {
                AppLog.Default.Warning("StartupHelper: schtasks invocation failed", ex);
                return new SchtasksResult(-1, string.Empty, string.Empty);
            }
        }

        // Retries an operation with a UAC consent prompt. If the user declines the prompt, this
        // just fails closed.
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
