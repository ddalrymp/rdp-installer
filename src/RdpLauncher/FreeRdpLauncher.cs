using System.Diagnostics;
using System.Text;

namespace RdpLauncher;

/// <summary>
/// Launches wfreerdp.exe with RAIL (RemoteApp) support.
/// Passes credentials via stdin to avoid exposure in process command line.
/// </summary>
public sealed class FreeRdpLauncher
{
    private readonly string _wfreerdpPath;

    public string? LastError { get; private set; }

    public FreeRdpLauncher(string? wfreerdpPath = null)
    {
        _wfreerdpPath = wfreerdpPath
            ?? Path.Combine(AppContext.BaseDirectory, "freerdp", "wfreerdp.exe");
    }

    /// <summary>
    /// Returns true if the wfreerdp.exe binary is present.
    /// </summary>
    public bool IsAvailable => File.Exists(_wfreerdpPath);

    /// <summary>
    /// Launches a FreeRDP RemoteApp session.
    /// Returns the process exit code, or -1 if launch failed.
    /// </summary>
    public async Task<int> LaunchAsync(ConnectionInfo connection, string username, string password)
    {
        LastError = null;

        if (!IsAvailable)
        {
            LastError = $"FreeRDP not found at: {_wfreerdpPath}";
            return -1;
        }

        var args = BuildArguments(connection, username);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _wfreerdpPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var process = Process.Start(startInfo);
            if (process == null)
            {
                LastError = "Failed to start wfreerdp.exe process.";
                return -1;
            }

            // Pass password via stdin to avoid command-line exposure
            await process.StandardInput.WriteLineAsync(password);
            process.StandardInput.Close();

            await process.WaitForExitAsync();
            var exitCode = process.ExitCode;

            if (exitCode != 0)
            {
                var stderr = await process.StandardError.ReadToEndAsync();
                if (!string.IsNullOrWhiteSpace(stderr))
                    LastError = $"FreeRDP exited with code {exitCode}: {stderr.Trim()}";
                else
                    LastError = $"FreeRDP exited with code {exitCode}";
            }

            process.Dispose();
            return exitCode;
        }
        catch (Exception ex)
        {
            LastError = $"Failed to launch FreeRDP: {ex.Message}";
            return -1;
        }
    }

    /// <summary>
    /// Builds the wfreerdp command-line arguments for a RemoteApp (RAIL) connection.
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

        // RemoteApp (RAIL)
        if (!string.IsNullOrEmpty(connection.RemoteAppProgram))
        {
            sb.Append($" /app:\"{connection.RemoteAppProgram}\"");

            if (!string.IsNullOrEmpty(connection.RemoteAppName))
                sb.Append($" /app-name:\"{connection.RemoteAppName}\"");

            if (!string.IsNullOrEmpty(connection.RemoteAppCmdLine))
                sb.Append($" /app-cmd:\"{connection.RemoteAppCmdLine}\"");
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
}
