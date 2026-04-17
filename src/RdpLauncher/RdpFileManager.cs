namespace RdpLauncher;

/// <summary>
/// Downloads and caches .rdp files from the server.
/// Compares thumbprints to detect when a new .rdp file is needed.
/// </summary>
public sealed class RdpFileManager
{
    private readonly string _cacheDir;
    private readonly HttpClient _httpClient;

    public RdpFileManager(string cacheDir, HttpClient? httpClient = null)
    {
        _cacheDir = cacheDir;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    /// <summary>
    /// Ensures a current .rdp file is available locally.
    /// Downloads a new one if the thumbprint has changed, otherwise uses the cache.
    /// Returns the path to the .rdp file, or null on failure.
    /// </summary>
    public async Task<string?> EnsureRdpFileAsync(ConnectionInfo connection, string? cachedThumbprint, string? userId = null)
    {
        var cacheFileName = !string.IsNullOrEmpty(userId) ? $"{userId}.rdp" : $"{connection.Id}.rdp";
        var cachedRdpPath = Path.Combine(_cacheDir, cacheFileName);
        var thumbprintChanged = !string.Equals(
            connection.CertThumbprint, cachedThumbprint, StringComparison.OrdinalIgnoreCase);

        if (thumbprintChanged || !File.Exists(cachedRdpPath))
        {
            var rdpUrl = ResolveRdpUrl(connection, userId);
            if (string.IsNullOrEmpty(rdpUrl))
                return File.Exists(cachedRdpPath) ? cachedRdpPath : null;

            var downloaded = await DownloadRdpFileAsync(rdpUrl, cachedRdpPath);
            if (!downloaded && File.Exists(cachedRdpPath))
            {
                // Download failed but we have a cached copy — use it
                return cachedRdpPath;
            }
            return downloaded ? cachedRdpPath : null;
        }

        return cachedRdpPath;
    }

    /// <summary>
    /// Resolves the .rdp download URL from the connection config.
    /// Prefers rdpFileUrlPattern (with {userId} substitution) over the legacy rdpFileUrl.
    /// </summary>
    internal static string? ResolveRdpUrl(ConnectionInfo connection, string? userId)
    {
        if (!string.IsNullOrEmpty(connection.RdpFileUrlPattern) && !string.IsNullOrEmpty(userId))
        {
            return connection.RdpFileUrlPattern.Replace("{userId}", userId, StringComparison.OrdinalIgnoreCase);
        }

        // Fall back to legacy single-file URL
        if (!string.IsNullOrEmpty(connection.RdpFileUrl))
        {
            return connection.RdpFileUrl;
        }

        return null;
    }

    /// <summary>
    /// Copies the cached .rdp file to a temp location for mstsc.exe to consume.
    /// This avoids file locking issues while mstsc.exe is running.
    /// </summary>
    public static string? PrepareForLaunch(string cachedRdpPath)
    {
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "RdpLauncher");
            Directory.CreateDirectory(tempDir);

            var tempRdpPath = Path.Combine(tempDir, $"session_{Guid.NewGuid():N}.rdp");
            File.Copy(cachedRdpPath, tempRdpPath, overwrite: true);
            return tempRdpPath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Cleans up a temporary .rdp file after the RDP session ends.
    /// </summary>
    public static void CleanupTempFile(string tempRdpPath)
    {
        try
        {
            if (File.Exists(tempRdpPath))
                File.Delete(tempRdpPath);
        }
        catch
        {
            // Cleanup failure is non-fatal
        }
    }

    /// <summary>
    /// Removes all cached files for this manager's cache directory.
    /// </summary>
    public void ClearCache()
    {
        try
        {
            if (Directory.Exists(_cacheDir))
                Directory.Delete(_cacheDir, recursive: true);
        }
        catch
        {
            // Cleanup failure is non-fatal
        }
    }

    private async Task<bool> DownloadRdpFileAsync(string rdpUrl, string destinationPath)
    {
        try
        {
            var content = await _httpClient.GetByteArrayAsync(rdpUrl);

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await File.WriteAllBytesAsync(destinationPath, content);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }
}
