namespace RmCloud;

static class Program
{
    [STAThread]
    static void Main(string[] args)
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
                    "RM Cloud", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    Logger.Error("Unhandled exception", ex);
            };

            Logger.Info("=== RM Cloud process starting ===");

            if (args.Length > 0 && args[0].Equals("--settings", StringComparison.OrdinalIgnoreCase))
            {
                var credentials = new CredentialManager();
                credentials.Load();
                Application.Run(new SettingsForm(credentials));
                return;
            }

            Application.Run(new LauncherForm());
        }
        catch (Exception ex)
        {
            Logger.Error("Fatal startup exception", ex);
            MessageBox.Show(
                $"RM Cloud failed to start:\n\n{ex.Message}\n\n" +
                $"Log: {Logger.LogFilePath}",
                "RM Cloud", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
