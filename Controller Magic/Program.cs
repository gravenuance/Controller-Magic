namespace ControllerMagic
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var context = new TrayApplicationContext();
            Application.Run(context);
        }
    }
}
