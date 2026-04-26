using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace RdpLauncher;

/// <summary>
/// Launches sdl3-freerdp.exe with RAIL (RemoteApp) support.
/// Passes credentials via stdin to avoid exposure in process command line.
/// </summary>
public sealed class FreeRdpLauncher
{
    private readonly string _freerdpPath;

    public string? LastError { get; private set; }

    public FreeRdpLauncher(string? freerdpPath = null)
    {
        _freerdpPath = freerdpPath
            ?? Path.Combine(AppContext.BaseDirectory, "freerdp", "sdl3-freerdp.exe");
        Logger.Debug($"FreeRdpLauncher initialized. Path: {_freerdpPath}");
        Logger.Debug($"  Exists: {File.Exists(_freerdpPath)}");
    }

    /// <summary>
    /// Returns true if the sdl3-freerdp.exe binary is present.
    /// </summary>
    public bool IsAvailable => File.Exists(_freerdpPath);

    /// <summary>
    /// Launches a FreeRDP RemoteApp session.
    /// Returns the process exit code, or -1 if launch failed.
    /// </summary>
    public async Task<int> LaunchAsync(ConnectionInfo connection, string username, string password)
    {
        LastError = null;
        Logger.Info($"FreeRDP LaunchAsync called for server: {connection.ServerAddress}:{connection.Port}");
        Logger.Debug($"  RemoteApp: {connection.RemoteAppProgram}");
        Logger.Debug($"  Domain: {connection.Domain}");
        Logger.Debug($"  Username: {username}");

        if (!IsAvailable)
        {
            LastError = $"FreeRDP not found at: {_freerdpPath}";
            Logger.Error(LastError);
            return -1;
        }

        // Remove Mark of the Web (MOTW) from all files in the freerdp folder.
        // Downloaded files carry a Zone.Identifier stream that causes SmartScreen
        // to block them when launched programmatically (no UI for the trust dialog).
        UnblockFreeRdpFiles();

        var args = BuildArguments(connection, username);
        Logger.Debug($"  Command: {_freerdpPath} {args}");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _freerdpPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            Logger.Info("Starting sdl3-freerdp.exe process...");
            var process = Process.Start(startInfo);
            if (process == null)
            {
                LastError = "Failed to start sdl3-freerdp.exe process.";
                Logger.Error(LastError);
                return -1;
            }

            Logger.Debug($"  Process started, PID: {process.Id}");

            // Pass password via stdin to avoid command-line exposure
            await process.StandardInput.WriteLineAsync(password);
            process.StandardInput.Close();
            Logger.Debug("  Password sent via stdin, waiting for exit...");

            // Read stderr before WaitForExit to avoid deadlocks
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var exitCode = process.ExitCode;
            var stderr = await stderrTask;

            Logger.Info($"  sdl3-freerdp.exe exited with code: {exitCode}");

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                Logger.Debug($"  stderr output:\n{stderr.Trim()}");
            }

            if (exitCode != 0)
            {
                if (!string.IsNullOrWhiteSpace(stderr))
                    LastError = $"FreeRDP exited with code {exitCode}: {stderr.Trim()}";
                else
                    LastError = $"FreeRDP exited with code {exitCode}";
                Logger.Error(LastError);
            }

            process.Dispose();
            return exitCode;
        }
        catch (Exception ex)
        {
            LastError = $"Failed to launch FreeRDP: {ex.Message}";
            Logger.Error("FreeRDP launch exception", ex);
            return -1;
        }
    }

    /// <summary>
    /// Builds the sdl3-freerdp command-line arguments for a RemoteApp (RAIL) connection.
    /// Password is NOT included — it's passed via stdin using /from-stdin.
    /// </summary>
    internal static string BuildArguments(ConnectionInfo connection, string username)
    {
        var sb = new StringBuilder();

        // Server
        sb.Append($"/v:{connection.ServerAddress}");
        if (connection.Port != 3389)
            sb.Append($":{connection.Port}");

        // Credentials (password via stdin)
        sb.Append($" /u:{username}");
        if (!string.IsNullOrEmpty(connection.Domain))
            sb.Append($" /d:{connection.Domain}");
        sb.Append(" /from-stdin");

        // RemoteApp (RAIL) — FreeRDP 3.x uses /app:program:,name:,cmd: sub-options
        if (!string.IsNullOrEmpty(connection.RemoteAppProgram))
        {
            var appParts = new List<string> { $"program:{connection.RemoteAppProgram}" };

            if (!string.IsNullOrEmpty(connection.RemoteAppName))
                appParts.Add($"name:{connection.RemoteAppName}");

            if (!string.IsNullOrEmpty(connection.RemoteAppCmdLine))
                appParts.Add($"cmd:{connection.RemoteAppCmdLine}");

            sb.Append($" /app:{string.Join(",", appParts)}");
        }

        // Resource sharing
        sb.Append(" +clipboard");
        sb.Append(" /drive:Home,$USERPROFILE");
        sb.Append(" /printer");

        // Certificate handling — trust on first use
        sb.Append(" /cert:tofu");

        // Disable wallpaper for performance
        sb.Append(" -wallpaper");

        return sb.ToString();
    }

    /// <summary>
    /// Removes the Zone.Identifier alternate data stream (Mark of the Web) from
    /// sdl3-freerdp.exe and all files in its directory. Files downloaded from the
    /// internet carry this stream, which causes SmartScreen to block them when
    /// launched programmatically with no UI for the trust prompt.
    /// Uses DeleteFile on the ":Zone.Identifier" ADS — this is the same thing
    /// PowerShell's Unblock-File does under the hood.
    /// </summary>
    private void UnblockFreeRdpFiles()
    {
        try
        {
            var dir = Path.GetDirectoryName(_freerdpPath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

            var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
            var unblocked = 0;

            foreach (var file in files)
            {
                var zoneStream = file + ":Zone.Identifier";
                if (DeleteFile(zoneStream))
                    unblocked++;
            }

            if (unblocked > 0)
                Logger.Info($"Unblocked {unblocked} file(s) in {dir} (removed MOTW Zone.Identifier).");
            else
                Logger.Debug("No MOTW Zone.Identifier streams found on FreeRDP files.");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to unblock FreeRDP files: {ex.Message}");
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool DeleteFile(string lpFileName);
}
