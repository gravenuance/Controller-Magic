namespace ControllerMagic
{
    internal static class Program
    {
        private static readonly Mutex _mutex = new(true, "ControllerMagic-69F2B9E1-7C2E-4C11-9C1A-ABCDEF123456", out _);
        [STAThread]
        static void Main(string[] args)
        {
            // A second launch (double-clicked by hand, triggered by the startup task, whatever)
            // just quietly exits - the app is already running, and a "did you know" dialog isn't
            // worth interrupting whatever the user's doing for.
            if (!_mutex.WaitOne(0, false))
                return;

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => HandleFatalException(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => HandleFatalException(e.ExceptionObject as Exception);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var context = new TrayApplicationContext();
            Application.Run(context);
        }

        // Background threads (e.g. the controller poll loop) crash the whole process on an
        // unhandled exception with no way to recover; this at least logs and tells the user
        // instead of leaving them with a raw .NET fault dialog or a silent disappearance from
        // the tray.
        private static void HandleFatalException(Exception? ex)
        {
            try
            {
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ControllerMagic");
                Directory.CreateDirectory(logDir);
                string logPath = Path.Combine(logDir, "crash.log");
                File.AppendAllText(logPath, $"{DateTime.Now:u}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");

                MessageBox.Show(
                    $"Controller Magic hit an unexpected error and needs to close.\n\nDetails were saved to:\n{logPath}",
                    "Controller Magic",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch
            {
                // Logging or showing the dialog itself failed; nothing more we can safely do.
            }
        }
    }
}
