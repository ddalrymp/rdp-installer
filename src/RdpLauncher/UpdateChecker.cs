using System.Diagnostics;
using System.Reflection;

namespace RdpLauncher;

/// <summary>
/// Compares the local launcher version with the server-reported latest version.
/// Prompts the user to download an update if a newer version is available.
/// </summary>
public sealed class UpdateChecker
{
    /// <summary>
    /// Gets the current launcher version from the assembly metadata.
    /// </summary>
    public static Version GetCurrentVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version ?? new Version(1, 0, 0);
    }

    /// <summary>
    /// Checks whether the server config indicates a newer launcher version is available.
    /// </summary>
    public static bool IsUpdateAvailable(LauncherConfig config)
    {
        if (string.IsNullOrEmpty(config.LauncherVersion))
            return false;

        if (!Version.TryParse(config.LauncherVersion, out var serverVersion))
            return false;

        var currentVersion = GetCurrentVersion();
        return serverVersion > currentVersion;
    }

    /// <summary>
    /// Opens the launcher download URL in the default browser.
    /// </summary>
    public static void OpenDownloadPage(string downloadUrl)
    {
        if (string.IsNullOrEmpty(downloadUrl))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = downloadUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            // Browser launch failure is non-fatal
        }
    }
}
