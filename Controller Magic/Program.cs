using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

// Every P/Invoke this project declares itself (ControllerPoller, InputEmulator,
// KeyboardOverlayForm) targets a real Windows system DLL (user32.dll, winmm.dll, dwmapi.dll) by
// bare name. Without this, the default P/Invoke search order checks the app's own directory
// before System32 - and since this app ships as a self-contained single-file exe that people may
// run from anywhere (a Downloads folder, a shared drive), a same-named malicious DLL planted
// alongside it would load instead of the real one. This only affects P/Invokes declared in this
// assembly; SDL2-CS's own P/Invokes to its bundled native SDL2.dll are unaffected.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace ControllerMagic
{
    internal static class Program
    {
        private const string SingleInstanceMutexName = "ControllerMagic-69F2B9E1-7C2E-4C11-9C1A-ABCDEF123456";

        [STAThread]
        static int Main(string[] args)
        {
            // Checked before the single-instance mutex: the elevated driver installer is a second
            // copy of the app launched by the running one, and must neither be blocked by it nor
            // start a tray icon of its own.
            if (DriverInstallProtocol.IsInstallCommand(args))
                return ElevatedDriverInstall.Run(args);

            // A second launch (double-clicked by hand, triggered by the startup task, whatever)
            // just quietly exits - the app is already running, and a "did you know" dialog isn't
            // worth interrupting whatever the user's doing for.
            bool restart;
            using (var instance = SingleInstanceLock.TryAcquire(SingleInstanceMutexName))
            {
                if (instance is null)
                    return 0;

                restart = RunTray();
            }

            // Only once the lock is released, or the new copy exits as a second instance.
            if (restart)
                Relaunch();
            return 0;
        }

        // Returns whether the user asked to restart.
        private static bool RunTray()
        {
            var reporter = new UnhandledExceptionReporter(AppLog.Default, TimeProvider.System, ShowExceptionNotice);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => reporter.ReportSurvivable(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => reporter.ReportFatal(e.ExceptionObject as Exception);

            // Not currently reachable (nothing here fires-and-forgets a Task without awaiting or
            // observing it), but StartupHelper's process-spawning calls are moving to async - this
            // is the safety net for whenever a future one is left unobserved, so a faulted
            // background Task doesn't silently vanish instead of being logged.
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                AppLog.Default.Error("Unobserved task exception", e.Exception);
                e.SetObserved();
            };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // The app's owner-drawn UI has always been dark regardless of the Windows theme (see
            // Theme.cs); this extends that same intent to the pieces WinForms still renders itself
            // - the tray's right-click menu and the crash dialog - instead of leaving them light.
            // Stable as of .NET 10 (preview-only, behind WFO5001, on .NET 9).
            Application.SetColorMode(SystemColorMode.Dark);

            // Application.Run(ApplicationContext) takes ownership of disposing context once the
            // message loop it drives has ended - the canonical WinForms tray-app pattern, not a
            // leak.
#pragma warning disable CA2000
            var context = new TrayApplicationContext();
#pragma warning restore CA2000
            Application.Run(context);
            return context.RestartRequested;
        }

        private static void Relaunch()
        {
            string exe = Application.ExecutablePath;
            try
            {
                Process.Start(exe)?.Dispose();
            }
            catch (Win32Exception ex)
            {
                AppLog.Default.Warning($"Failed to relaunch {exe} for restart", ex);
            }
        }

        // Background threads (e.g. the controller poll loop) crash the whole process with no way
        // to recover; UI-thread exceptions are caught by WinForms and the app keeps running.
        private static void ShowExceptionNotice(ExceptionNotice notice)
        {
            var (text, icon) = notice switch
            {
                ExceptionNotice.Survived => (
                    "Something went wrong; Controller Magic kept running. Details are in the log.",
                    MessageBoxIcon.Warning),
                ExceptionNotice.Crashed => (
                    $"Controller Magic crashed. Details saved to:\n{AppLog.Default.FilePath}",
                    MessageBoxIcon.Error),
                _ => throw new ArgumentOutOfRangeException(nameof(notice), notice, null),
            };

            try
            {
                MessageBox.Show(text, "Controller Magic", MessageBoxButtons.OK, icon);
            }
            catch (Exception ex)
            {
                // Already logged by the reporter; a failing dialog has nowhere left to go.
                AppLog.Default.Warning("Could not show the exception notice", ex);
            }
        }
    }
}
