using System.Diagnostics;

namespace RdpLauncher;

/// <summary>
/// Orchestrates RDP connection: tries FreeRDP first, falls back to mstsc if configured.
/// </summary>
public static class ProcessLauncher
{
    public static string? LastError { get; private set; }

    /// <summary>
    /// Launches a connection using FreeRDP (primary). If FreeRDP is unavailable or fails
    /// and fallbackToMstsc is true, falls back to mstsc.exe with a downloaded .rdp file.
    /// Returns the exit code, or -1 if all methods failed.
    /// </summary>
    public static async Task<int> LaunchAsync(
        ConnectionInfo connection, string username, string password,
        string cacheDir, bool fallbackToMstsc)
    {
        LastError = null;

        // --- Try FreeRDP first ---
        var freeRdp = new FreeRdpLauncher();
        if (freeRdp.IsAvailable)
        {
            var exitCode = await freeRdp.LaunchAsync(connection, username, password);
            if (exitCode == 0)
                return 0;

            // FreeRDP failed — record error for diagnostics
            LastError = freeRdp.LastError;

            if (!fallbackToMstsc)
                return exitCode;
        }
        else
        {
            LastError = "FreeRDP not found.";
            if (!fallbackToMstsc)
                return -1;
        }

        // --- Fallback to mstsc.exe ---
        return await LaunchMstscFallbackAsync(connection, username, cacheDir);
    }

    /// <summary>
    /// Falls back to mstsc.exe using a downloaded .rdp file.
    /// </summary>
    private static async Task<int> LaunchMstscFallbackAsync(
        ConnectionInfo connection, string username, string cacheDir)
    {
        // Ensure cert is trusted for mstsc path
        if (!string.IsNullOrEmpty(connection.CertThumbprint) &&
            !CertificateManager.IsCertificateTrusted(connection.CertThumbprint))
        {
            if (!string.IsNullOrEmpty(connection.SigningCertUrl))
            {
                await CertificateManager.DownloadAndImportCertificateAsync(
                    connection.SigningCertUrl, cacheDir);
            }
            else
            {
                CertificateManager.ImportFromCache(cacheDir);
            }
        }

        // Download/use cached .rdp file
        var rdpManager = new RdpFileManager(cacheDir);
        var rdpPath = await rdpManager.EnsureRdpFileAsync(connection, null, username);

        if (rdpPath == null)
        {
            LastError = $"mstsc fallback: {rdpManager.LastError ?? "Unable to get .rdp file."}";
            return -1;
        }

        var tempRdpPath = RdpFileManager.PrepareForLaunch(rdpPath);
        if (tempRdpPath == null)
        {
            LastError = "mstsc fallback: Unable to prepare temp .rdp file.";
            return -1;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "mstsc.exe",
                Arguments = $"\"{tempRdpPath}\"",
                UseShellExecute = false
            };

            var process = Process.Start(startInfo);
            if (process == null)
            {
                LastError = "mstsc fallback: Failed to start mstsc.exe.";
                return -1;
            }

            await process.WaitForExitAsync();
            var exitCode = process.ExitCode;
            process.Dispose();

            RdpFileManager.CleanupTempFile(tempRdpPath);
            return exitCode;
        }
        catch (Exception ex)
        {
            LastError = $"mstsc fallback: {ex.Message}";
            return -1;
        }
    }
}
