using System.Diagnostics;

namespace RdpLauncher;

/// <summary>
/// Orchestrates RDP connection using mstsc.exe with a downloaded .rdp file.
/// </summary>
public static class ProcessLauncher
{
    public static string? LastError { get; private set; }

    /// <summary>
    /// Launches a connection using mstsc.exe with a downloaded .rdp file.
    /// Returns the exit code, or -1 if launch failed.
    /// </summary>
    public static async Task<int> LaunchAsync(
        ConnectionInfo connection, string username, string password,
        string cacheDir, bool fallbackToMstsc)
    {
        LastError = null;
        Logger.Info("ProcessLauncher.LaunchAsync started");
        Logger.Debug($"  Connection: {connection.ServerAddress}:{connection.Port}");

        return await LaunchMstscAsync(connection, username, password, cacheDir);
    }

    /// <summary>
    /// Launches mstsc.exe using a downloaded .rdp file.
    /// </summary>
    private static async Task<int> LaunchMstscAsync(
        ConnectionInfo connection, string username, string password, string cacheDir)
    {
        Logger.Info("LaunchMstscAsync started");

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
            LastError = $"mstsc: {rdpManager.LastError ?? "Unable to get .rdp file."}";
            Logger.Error(LastError);
            return -1;
        }

        Logger.Debug($"  RDP file path: {rdpPath}");
        var tempRdpPath = RdpFileManager.PrepareForLaunch(rdpPath);
        if (tempRdpPath == null)
        {
            LastError = "mstsc: Unable to prepare temp .rdp file.";
            return -1;
        }

        // Store credential via cmdkey so mstsc auto-authenticates
        var credTarget = $"TERMSRV/{connection.ServerAddress}";
        try
        {
            StoreCmdKeyCredential(credTarget, username, password);
        }
        catch (Exception ex)
        {
            Logger.Error($"cmdkey store failed: {ex.Message}");
            // Continue anyway — user will get a password prompt from mstsc
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
                LastError = "mstsc: Failed to start mstsc.exe.";
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
            LastError = $"mstsc: {ex.Message}";
            return -1;
        }
        finally
        {
            // Always clean up the stored credential
            DeleteCmdKeyCredential(credTarget);
        }
    }

    private static void StoreCmdKeyCredential(string target, string username, string password)
    {
        Logger.Debug($"Storing credential for {target}");
        var psi = new ProcessStartInfo
        {
            FileName = "cmdkey.exe",
            Arguments = $"/generic:{target} /user:{username} /pass:{password}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var proc = Process.Start(psi);
        proc?.WaitForExit(5000);
        if (proc?.ExitCode != 0)
            Logger.Warn($"cmdkey store returned exit code {proc?.ExitCode}");
        else
            Logger.Debug("Credential stored successfully");
    }

    private static void DeleteCmdKeyCredential(string target)
    {
        try
        {
            Logger.Debug($"Removing credential for {target}");
            var psi = new ProcessStartInfo
            {
                FileName = "cmdkey.exe",
                Arguments = $"/delete:{target}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            Logger.Warn($"cmdkey delete failed: {ex.Message}");
        }
    }
}
