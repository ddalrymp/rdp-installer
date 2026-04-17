namespace RdpLauncher.Tests;

public class RdpFileManagerTests
{
    private readonly string _testCacheDir;

    public RdpFileManagerTests()
    {
        _testCacheDir = Path.Combine(Path.GetTempPath(), "RdpLauncher_Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testCacheDir);
    }

    [Fact]
    public void PrepareForLaunch_CreatesTempCopy()
    {
        // Create a fake cached .rdp file
        var cachedPath = Path.Combine(_testCacheDir, "test.rdp");
        File.WriteAllText(cachedPath, "full address:s:test.example.com:3389");

        var tempPath = RdpFileManager.PrepareForLaunch(cachedPath);

        Assert.NotNull(tempPath);
        Assert.True(File.Exists(tempPath));
        Assert.Contains("session_", tempPath);
        Assert.Equal("full address:s:test.example.com:3389", File.ReadAllText(tempPath!));

        // Cleanup
        RdpFileManager.CleanupTempFile(tempPath!);
    }

    [Fact]
    public void CleanupTempFile_DeletesFile()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.rdp");
        File.WriteAllText(tempPath, "test content");

        RdpFileManager.CleanupTempFile(tempPath);

        Assert.False(File.Exists(tempPath));
    }

    [Fact]
    public void CleanupTempFile_DoesNotThrowForMissingFile()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "nonexistent.rdp");

        var ex = Record.Exception(() => RdpFileManager.CleanupTempFile(nonExistentPath));

        Assert.Null(ex);
    }

    [Fact]
    public async Task EnsureRdpFileAsync_UsesCachedFile_WhenThumbprintUnchanged()
    {
        // Pre-create a cached .rdp file named by userId
        var cachedPath = Path.Combine(_testCacheDir, "ORG1_U01.rdp");
        File.WriteAllText(cachedPath, "full address:s:test.example.com:3389");

        var connection = new ConnectionInfo
        {
            Id = "main-app",
            CertThumbprint = "AABB",
            RdpFileUrlPattern = "https://localhost:1/users/{userId}.rdp"
        };

        var manager = new RdpFileManager(_testCacheDir);
        var result = await manager.EnsureRdpFileAsync(connection, cachedThumbprint: "AABB", userId: "ORG1_U01");

        Assert.NotNull(result);
        Assert.Equal(cachedPath, result);
    }

    [Fact]
    public async Task EnsureRdpFileAsync_ReturnsCachedFile_WhenDownloadFailsAndCacheExists()
    {
        // Pre-create a cached .rdp file
        var cachedPath = Path.Combine(_testCacheDir, "ORG1_U01.rdp");
        File.WriteAllText(cachedPath, "full address:s:old-server:3389");

        var connection = new ConnectionInfo
        {
            Id = "main-app",
            CertThumbprint = "NEWTHUMB",
            RdpFileUrlPattern = "https://localhost:1/users/{userId}.rdp"
        };

        var manager = new RdpFileManager(_testCacheDir);
        // Thumbprint differs, so it will try to download, fail, and fall back to cache
        var result = await manager.EnsureRdpFileAsync(connection, cachedThumbprint: "OLDTHUMB", userId: "ORG1_U01");

        Assert.NotNull(result);
        Assert.Equal(cachedPath, result);
    }

    [Fact]
    public async Task EnsureRdpFileAsync_ReturnsNull_WhenDownloadFailsAndNoCacheExists()
    {
        var emptyCache = Path.Combine(_testCacheDir, "empty");
        Directory.CreateDirectory(emptyCache);

        var connection = new ConnectionInfo
        {
            Id = "main-app",
            CertThumbprint = "AABB",
            RdpFileUrlPattern = "https://localhost:1/users/{userId}.rdp"
        };

        var manager = new RdpFileManager(emptyCache);
        var result = await manager.EnsureRdpFileAsync(connection, cachedThumbprint: null, userId: "ORG1_U01");

        Assert.Null(result);
    }

    [Fact]
    public async Task EnsureRdpFileAsync_FallsBackToLegacyUrl_WhenNoUserId()
    {
        // Pre-create a cached .rdp file named by connection id (legacy behavior)
        var cachedPath = Path.Combine(_testCacheDir, "main-app.rdp");
        File.WriteAllText(cachedPath, "full address:s:test.example.com:3389");

        var connection = new ConnectionInfo
        {
            Id = "main-app",
            CertThumbprint = "AABB",
            RdpFileUrl = "https://localhost:1/app.rdp"
        };

        var manager = new RdpFileManager(_testCacheDir);
        var result = await manager.EnsureRdpFileAsync(connection, cachedThumbprint: "AABB");

        Assert.NotNull(result);
        Assert.Equal(cachedPath, result);
    }

    [Fact]
    public void ResolveRdpUrl_UsesPatternWithUserId()
    {
        var connection = new ConnectionInfo
        {
            RdpFileUrlPattern = "https://example.com/users/{userId}.rdp",
            RdpFileUrl = "https://example.com/app.rdp"
        };

        var url = RdpFileManager.ResolveRdpUrl(connection, "ORG1_U01");

        Assert.Equal("https://example.com/users/ORG1_U01.rdp", url);
    }

    [Fact]
    public void ResolveRdpUrl_FallsBackToLegacyUrl_WhenNoUserId()
    {
        var connection = new ConnectionInfo
        {
            RdpFileUrlPattern = "https://example.com/users/{userId}.rdp",
            RdpFileUrl = "https://example.com/app.rdp"
        };

        var url = RdpFileManager.ResolveRdpUrl(connection, null);

        Assert.Equal("https://example.com/app.rdp", url);
    }

    [Fact]
    public void ResolveRdpUrl_FallsBackToLegacyUrl_WhenNoPattern()
    {
        var connection = new ConnectionInfo
        {
            RdpFileUrl = "https://example.com/app.rdp"
        };

        var url = RdpFileManager.ResolveRdpUrl(connection, "ORG1_U01");

        Assert.Equal("https://example.com/app.rdp", url);
    }

    [Fact]
    public void ResolveRdpUrl_ReturnsNull_WhenNoUrlsConfigured()
    {
        var connection = new ConnectionInfo();

        var url = RdpFileManager.ResolveRdpUrl(connection, "ORG1_U01");

        Assert.Null(url);
    }

    [Fact]
    public void ClearCache_RemovesCacheDirectory()
    {
        var cacheToDelete = Path.Combine(_testCacheDir, "deleteme");
        Directory.CreateDirectory(cacheToDelete);
        File.WriteAllText(Path.Combine(cacheToDelete, "test.rdp"), "test");

        var manager = new RdpFileManager(cacheToDelete);
        manager.ClearCache();

        Assert.False(Directory.Exists(cacheToDelete));
    }
}
