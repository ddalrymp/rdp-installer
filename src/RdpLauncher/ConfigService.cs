using System.Text.Json;
using System.Text.Json.Serialization;

namespace RdpLauncher;

/// <summary>
/// Represents the server-hosted configuration.
/// </summary>
public sealed class LauncherConfig
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("connections")]
    public List<ConnectionInfo> Connections { get; set; } = new();

    [JsonPropertyName("launcherVersion")]
    public string LauncherVersion { get; set; } = "";

    [JsonPropertyName("launcherDownloadUrl")]
    public string LauncherDownloadUrl { get; set; } = "";

    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; set; } = "";
}

public sealed class ConnectionInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("serverAddress")]
    public string ServerAddress { get; set; } = "";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 3389;

    [JsonPropertyName("remoteAppProgram")]
    public string RemoteAppProgram { get; set; } = "";

    [JsonPropertyName("remoteAppName")]
    public string RemoteAppName { get; set; } = "";

    [JsonPropertyName("remoteAppCmdLine")]
    public string RemoteAppCmdLine { get; set; } = "";

    [JsonPropertyName("gatewayHostname")]
    public string GatewayHostname { get; set; } = "";

    [JsonPropertyName("certThumbprint")]
    public string CertThumbprint { get; set; } = "";

    [JsonPropertyName("rdpFileUrl")]
    public string RdpFileUrl { get; set; } = "";

    [JsonPropertyName("rdpFileUrlPattern")]
    public string RdpFileUrlPattern { get; set; } = "";

    [JsonPropertyName("signingCertUrl")]
    public string SigningCertUrl { get; set; } = "";
}

/// <summary>
/// Fetches and caches the launcher configuration from a remote HTTPS endpoint.
/// Falls back to a locally cached copy when the network is unavailable.
/// </summary>
public sealed class ConfigService
{
    private readonly HttpClient _httpClient;
    private readonly string _configUrl;
    private readonly string _cacheDir;
    private readonly string _cachedConfigPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public ConfigService(string configUrl, string cacheDir)
    {
        _configUrl = configUrl;
        _cacheDir = cacheDir;
        _cachedConfigPath = Path.Combine(cacheDir, "config.json");

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    /// <summary>
    /// Fetches config from the remote URL. On failure, returns the cached version.
    /// Returns null only if both remote fetch and cache fail.
    /// </summary>
    public async Task<(LauncherConfig? Config, bool FromCache)> GetConfigAsync()
    {
        try
        {
            var json = await _httpClient.GetStringAsync(_configUrl);
            var config = JsonSerializer.Deserialize<LauncherConfig>(json, JsonOptions);

            if (config != null)
            {
                SaveToCache(json);
                return (config, false);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Network or parse error — fall through to cache
        }

        return (LoadFromCache(), true);
    }

    /// <summary>
    /// Gets the connection entry matching the specified ID, or the first entry if not found.
    /// </summary>
    public static ConnectionInfo? GetConnection(LauncherConfig config, string connectionId)
    {
        return config.Connections.FirstOrDefault(c =>
            string.Equals(c.Id, connectionId, StringComparison.OrdinalIgnoreCase))
            ?? config.Connections.FirstOrDefault();
    }

    private void SaveToCache(string json)
    {
        try
        {
            Directory.CreateDirectory(_cacheDir);
            File.WriteAllText(_cachedConfigPath, json);
        }
        catch
        {
            // Cache write failure is non-fatal
        }
    }

    private LauncherConfig? LoadFromCache()
    {
        try
        {
            if (!File.Exists(_cachedConfigPath))
                return null;

            var json = File.ReadAllText(_cachedConfigPath);
            return JsonSerializer.Deserialize<LauncherConfig>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the locally cached thumbprint for a connection, or null if no cache exists.
    /// </summary>
    public string? GetCachedThumbprint(string connectionId)
    {
        var config = LoadFromCache();
        if (config == null) return null;

        var connection = GetConnection(config, connectionId);
        return connection?.CertThumbprint;
    }
}
