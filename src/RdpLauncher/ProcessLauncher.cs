using System.Diagnostics;

namespace RdpLauncher;

/// <summary>
/// Launches mstsc.exe with an .rdp file and monitors the process lifecycle.
/// </summary>
public sealed class ProcessLauncher
{
    /// <summary>
    /// Launches mstsc.exe with the specified .rdp file path.
    /// Returns the started Process, or null if launch failed.
    /// </summary>
    public static Process? LaunchMstsc(string rdpFilePath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "mstsc.exe",
                Arguments = $"\"{rdpFilePath}\"",
                UseShellExecute = false
            };

            return Process.Start(startInfo);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Launches mstsc.exe and waits for it to exit.
    /// Cleans up the temp .rdp file after the session ends.
    /// Returns the exit code, or -1 if launch failed.
    /// </summary>
    public static async Task<int> LaunchAndWaitAsync(string tempRdpPath)
    {
        var process = LaunchMstsc(tempRdpPath);
        if (process == null)
            return -1;

        await process.WaitForExitAsync();
        var exitCode = process.ExitCode;
        process.Dispose();

        RdpFileManager.CleanupTempFile(tempRdpPath);

        return exitCode;
    }
}
