namespace RdpLauncher;

/// <summary>
/// Simple file-based debug logger. Writes timestamped entries to
/// %LOCALAPPDATA%\RdpLauncher\logs\debug.log with automatic rotation.
/// </summary>
public static class Logger
{
    private static readonly string LogDir;
    private static readonly string LogPath;
    private static readonly object Lock = new();
    private const long MaxLogSize = 2 * 1024 * 1024; // 2 MB

    static Logger()
    {
        LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RdpLauncher", "logs");
        LogPath = Path.Combine(LogDir, "debug.log");
    }

    public static string LogFilePath => LogPath;

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);
    public static void Error(string message, Exception ex) =>
        Write("ERROR", $"{message} | {ex.GetType().Name}: {ex.Message}");

    public static void Debug(string message) => Write("DEBUG", message);

    private static void Write(string level, string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(LogDir);
                RotateIfNeeded();

                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never crash the app
        }
    }

    private static void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(LogPath)) return;
            var info = new FileInfo(LogPath);
            if (info.Length <= MaxLogSize) return;

            var backup = Path.Combine(LogDir, "debug.previous.log");
            if (File.Exists(backup)) File.Delete(backup);
            File.Move(LogPath, backup);
        }
        catch
        {
            // Rotation failure is non-fatal
        }
    }
}
