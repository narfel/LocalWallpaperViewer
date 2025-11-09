namespace LocalWallpaperViewer
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            
            // Show splash screen
            var splash = new SplashForm();
            splash.Show();
            Application.DoEvents(); // Force the splash to render
            
            // Create and initialize main form
            var mainForm = new Form1(splash);
            
            // Close splash and show main form
            splash.Close();
            Application.Run(mainForm);
        }
    }
}