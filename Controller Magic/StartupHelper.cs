using Microsoft.Win32;
using System.Security.Principal;

namespace ControllerMagic
{
    internal static class StartupHelper
    {
        private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "ControllerMagic";

        public static bool IsEnabled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            if (key == null) return false;
            return key.GetValue(AppName) is string;
        }

        public static void SetEnabled(bool enabled)
        {
            string exe = Application.ExecutablePath;

            if (IsElevated())
            {
                using var key = Registry.LocalMachine.OpenSubKey(RunKey, writable: true);
                if (key == null) return;

                if (enabled)
                    key.SetValue(AppName, exe);
                else if (key.GetValue(AppName) != null)
                    key.DeleteValue(AppName);
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                if (key == null) return;

                if (enabled)
                    key.SetValue(AppName, exe);
                else if (key.GetValue(AppName) != null)
                    key.DeleteValue(AppName);
            }
        }
        public static bool IsElevated()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
