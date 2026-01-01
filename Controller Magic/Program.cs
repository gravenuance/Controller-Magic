namespace ControllerMagic
{
    internal static class Program
    {
        private static Mutex? _mutex;
        [STAThread]
        static void Main()
        {
            bool isNew;
            _mutex = new Mutex(true, "ControllerMagic-69F2B9E1-7C2E-4C11-9C1A-ABCDEF123456", out isNew);
            if (!isNew)
            {
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var context = new TrayApplicationContext();
            Application.Run(context);

            GC.KeepAlive(_mutex);
        }
    }
}
