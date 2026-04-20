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
        Logger.Info("ProcessLauncher.LaunchAsync started");
        Logger.Debug($"  Connection: {connection.ServerAddress}:{connection.Port}");
        Logger.Debug($"  FallbackToMstsc: {fallbackToMstsc}");

        // --- Try FreeRDP first ---
        var freeRdp = new FreeRdpLauncher();
        if (freeRdp.IsAvailable)
        {
            Logger.Info("FreeRDP is available, attempting launch...");
            var exitCode = await freeRdp.LaunchAsync(connection, username, password);
            if (exitCode == 0)
            {
                Logger.Info("FreeRDP session completed successfully.");
                return 0;
            }

            // FreeRDP failed — record error for diagnostics
            LastError = freeRdp.LastError;
            Logger.Warn($"FreeRDP failed (exit code {exitCode}). LastError: {LastError}");

            if (!fallbackToMstsc)
            {
                Logger.Info("Fallback to mstsc is disabled. Returning failure.");
                return exitCode;
            }

            Logger.Info("Falling back to mstsc.exe...");
        }
        else
        {
            LastError = "FreeRDP not found.";
            Logger.Warn(LastError);
            if (!fallbackToMstsc)
            {
                Logger.Info("Fallback to mstsc is disabled. Returning failure.");
                return -1;
            }
            Logger.Info("Falling back to mstsc.exe...");
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
        Logger.Info("LaunchMstscFallbackAsync started");

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
            Logger.Error(LastError);
            return -1;
        }

        Logger.Debug($"  RDP file path: {rdpPath}");
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

            Logger.Info($"Starting mstsc.exe with: {tempRdpPath}");
            var process = Process.Start(startInfo);
            if (process == null)
            {
                LastError = "mstsc fallback: Failed to start mstsc.exe.";
                Logger.Error(LastError);
                return -1;
            }

            Logger.Debug($"  mstsc.exe PID: {process.Id}");
            await process.WaitForExitAsync();
            var exitCode = process.ExitCode;
            Logger.Info($"  mstsc.exe exited with code: {exitCode}");
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
