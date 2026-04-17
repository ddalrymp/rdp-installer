using System.Text.Json;

namespace RdpLauncher.Tests;

public class ConfigServiceTests
{
    private readonly string _testCacheDir;

    public ConfigServiceTests()
    {
        _testCacheDir = Path.Combine(Path.GetTempPath(), "RdpLauncher_Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testCacheDir);
    }

    [Fact]
    public void GetConnection_ReturnsMatchingConnection()
    {
        var config = CreateSampleConfig();

        var result = ConfigService.GetConnection(config, "main-app");

        Assert.NotNull(result);
        Assert.Equal("main-app", result.Id);
        Assert.Equal("rds.example.com", result.ServerAddress);
    }

    [Fact]
    public void GetConnection_ReturnsFirstWhenIdNotFound()
    {
        var config = CreateSampleConfig();

        var result = ConfigService.GetConnection(config, "nonexistent");

        Assert.NotNull(result);
        Assert.Equal("main-app", result.Id);
    }

    [Fact]
    public void GetConnection_ReturnsNullForEmptyConnections()
    {
        var config = new LauncherConfig { Connections = new List<ConnectionInfo>() };

        var result = ConfigService.GetConnection(config, "main-app");

        Assert.Null(result);
    }

    [Fact]
    public void GetConnection_IsCaseInsensitive()
    {
        var config = CreateSampleConfig();

        var result = ConfigService.GetConnection(config, "MAIN-APP");

        Assert.NotNull(result);
        Assert.Equal("main-app", result.Id);
    }

    [Fact]
    public async Task GetConfigAsync_FallsBackToCache_WhenServerUnreachable()
    {
        // Pre-populate the cache
        var cacheConfig = CreateSampleConfig();
        var cacheJson = JsonSerializer.Serialize(cacheConfig);
        Directory.CreateDirectory(_testCacheDir);
        File.WriteAllText(Path.Combine(_testCacheDir, "config.json"), cacheJson);

        // Use a URL that will fail
        var service = new ConfigService("https://localhost:1/nonexistent", _testCacheDir);
        var (config, fromCache) = await service.GetConfigAsync();

        Assert.NotNull(config);
        Assert.True(fromCache);
        Assert.Equal("main-app", config!.Connections[0].Id);
    }

    [Fact]
    public async Task GetConfigAsync_ReturnsNull_WhenNoCacheAndServerUnreachable()
    {
        var emptyCache = Path.Combine(_testCacheDir, "empty");
        var service = new ConfigService("https://localhost:1/nonexistent", emptyCache);

        var (config, fromCache) = await service.GetConfigAsync();

        Assert.Null(config);
        Assert.True(fromCache);
    }

    [Fact]
    public void GetCachedThumbprint_ReturnsThumbprint_WhenCacheExists()
    {
        var config = CreateSampleConfig();
        var json = JsonSerializer.Serialize(config);
        Directory.CreateDirectory(_testCacheDir);
        File.WriteAllText(Path.Combine(_testCacheDir, "config.json"), json);

        var service = new ConfigService("https://localhost:1/unused", _testCacheDir);
        var thumbprint = service.GetCachedThumbprint("main-app");

        Assert.Equal("AB12CD34EF56789012345678901234567890ABCD", thumbprint);
    }

    [Fact]
    public void GetCachedThumbprint_ReturnsNull_WhenNoCacheExists()
    {
        var emptyCache = Path.Combine(_testCacheDir, "empty");
        var service = new ConfigService("https://localhost:1/unused", emptyCache);

        var thumbprint = service.GetCachedThumbprint("main-app");

        Assert.Null(thumbprint);
    }

    [Fact]
    public void LauncherConfig_DeserializesCorrectly()
    {
        var json = """
        {
          "version": "1.0",
          "connections": [
            {
              "id": "test",
              "displayName": "Test App",
              "serverAddress": "server.example.com",
              "port": 3389,
              "remoteAppProgram": "||TestApp",
              "remoteAppName": "Test Application",
              "remoteAppCmdLine": "",
              "gatewayHostname": "",
              "certThumbprint": "AABBCCDD",
              "rdpFileUrl": "https://example.com/app.rdp",
              "rdpFileUrlPattern": "https://example.com/users/{userId}.rdp",
              "signingCertUrl": "https://example.com/cert.cer"
            }
          ],
          "launcherVersion": "2.0.0",
          "launcherDownloadUrl": "https://example.com/setup.exe",
          "updatedAt": "2026-04-17T00:00:00Z"
        }
        """;

        var config = JsonSerializer.Deserialize<LauncherConfig>(json);

        Assert.NotNull(config);
        Assert.Equal("1.0", config!.Version);
        Assert.Single(config.Connections);
        Assert.Equal("test", config.Connections[0].Id);
        Assert.Equal("Test App", config.Connections[0].DisplayName);
        Assert.Equal("server.example.com", config.Connections[0].ServerAddress);
        Assert.Equal(3389, config.Connections[0].Port);
        Assert.Equal("||TestApp", config.Connections[0].RemoteAppProgram);
        Assert.Equal("AABBCCDD", config.Connections[0].CertThumbprint);
        Assert.Equal("2.0.0", config.LauncherVersion);
    }

    private static LauncherConfig CreateSampleConfig()
    {
        return new LauncherConfig
        {
            Version = "1.0",
            LauncherVersion = "1.0.0",
            LauncherDownloadUrl = "https://example.com/setup.exe",
            UpdatedAt = "2026-04-17T00:00:00Z",
            Connections = new List<ConnectionInfo>
            {
                new()
                {
                    Id = "main-app",
                    DisplayName = "My RemoteApp",
                    ServerAddress = "rds.example.com",
                    Port = 3389,
                    RemoteAppProgram = "||MyApp",
                    RemoteAppName = "My Application",
                    CertThumbprint = "AB12CD34EF56789012345678901234567890ABCD",
                    RdpFileUrl = "https://example.com/app.rdp",
                    RdpFileUrlPattern = "https://example.com/users/{userId}.rdp",
                    SigningCertUrl = "https://example.com/cert.cer"
                }
            }
        };
    }
}
