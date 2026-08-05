using System.Diagnostics;
using Microsoft.Win32;

namespace ControllerMagic
{
    // Runs the app via a Task Scheduler logon trigger instead of the classic
    // HKCU...\Run key. Windows deliberately staggers Run-key apps by several
    // seconds after logon to keep Explorer responsive; a scheduled task fires
    // directly off the logon event instead, so it starts noticeably sooner.
    internal static class StartupHelper
    {
        private const string TaskName = "ControllerMagic";
        private const string LegacyRunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string LegacyAppName = "ControllerMagic";

        public static bool IsEnabled()
        {
            MigrateLegacyIfNeeded();
            return RunSchtasks("/Query", "/TN", TaskName) == 0;
        }

        // Call once at app startup so users who had the old Run-key entry get
        // moved onto the faster scheduled task without needing to reopen Settings.
        public static void EnsureMigrated() => MigrateLegacyIfNeeded();

        public static void SetEnabled(bool enabled)
        {
            if (enabled)
                CreateTask();
            else
                RunSchtasks("/Delete", "/TN", TaskName, "/F");
        }

        private static void CreateTask()
        {
            string exe = Application.ExecutablePath;
            int exitCode = RunSchtasks(
                "/Create", "/TN", TaskName,
                "/TR", $"\"{exe}\"",
                "/SC", "ONLOGON",
                "/RL", "LIMITED",
                "/F");

            if (exitCode != 0)
                Debug.WriteLine($"[StartupHelper] Failed to create scheduled task (exit {exitCode}).");
        }

        // One-time upgrade path for users who had startup enabled via the old Run key.
        private static void MigrateLegacyIfNeeded()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(LegacyRunKey, true);
                if (key?.GetValue(LegacyAppName) is not string) return;

                key.DeleteValue(LegacyAppName, throwOnMissingValue: false);
                CreateTask();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StartupHelper] Legacy Run-key migration failed: {ex}");
            }
        }

        private static int RunSchtasks(params string[] args)
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
                proc.WaitForExit();
                return proc.ExitCode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StartupHelper] schtasks invocation failed: {ex}");
                return -1;
            }
        }
    }
}
