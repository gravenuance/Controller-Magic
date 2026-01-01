namespace ControllerMagic
{
    internal static class Program
    {
        private static readonly Mutex _mutex = new Mutex(true, "ControllerMagic-69F2B9E1-7C2E-4C11-9C1A-ABCDEF123456", out _);
        [STAThread]
        static void Main()
        {
            if (!_mutex.WaitOne(0, false))
            {
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var context = new TrayApplicationContext();
            Application.Run(context);
        }
    }
}
