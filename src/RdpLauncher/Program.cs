namespace RdpLauncher;

static class Program
{
    [STAThread]
    static void Main()
    {
        try
        {
            ApplicationConfiguration.Initialize();

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) =>
            {
                Logger.Error("Unhandled UI thread exception", e.Exception);
                MessageBox.Show(
                    $"An unexpected error occurred:\n\n{e.Exception.Message}",
                    "RDP Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    Logger.Error("Unhandled exception", ex);
            };

            Logger.Info("=== RDP Launcher process starting ===");
            Application.Run(new LauncherForm());
        }
        catch (Exception ex)
        {
            Logger.Error("Fatal startup exception", ex);
            MessageBox.Show(
                $"RDP Launcher failed to start:\n\n{ex.Message}\n\n" +
                $"Log: {Logger.LogFilePath}",
                "RDP Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
