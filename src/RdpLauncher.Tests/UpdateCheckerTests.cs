namespace RdpLauncher.Tests;

public class UpdateCheckerTests
{
    [Fact]
    public void IsUpdateAvailable_ReturnsTrueWhenServerVersionIsNewer()
    {
        var config = new LauncherConfig { LauncherVersion = "99.0.0" };

        Assert.True(UpdateChecker.IsUpdateAvailable(config));
    }

    [Fact]
    public void IsUpdateAvailable_ReturnsFalseWhenVersionIsCurrent()
    {
        var currentVersion = UpdateChecker.GetCurrentVersion();
        var config = new LauncherConfig { LauncherVersion = currentVersion.ToString() };

        Assert.False(UpdateChecker.IsUpdateAvailable(config));
    }

    [Fact]
    public void IsUpdateAvailable_ReturnsFalseWhenVersionIsOlder()
    {
        var config = new LauncherConfig { LauncherVersion = "0.0.1" };

        Assert.False(UpdateChecker.IsUpdateAvailable(config));
    }

    [Fact]
    public void IsUpdateAvailable_ReturnsFalseForEmptyVersion()
    {
        var config = new LauncherConfig { LauncherVersion = "" };

        Assert.False(UpdateChecker.IsUpdateAvailable(config));
    }

    [Fact]
    public void IsUpdateAvailable_ReturnsFalseForInvalidVersion()
    {
        var config = new LauncherConfig { LauncherVersion = "not-a-version" };

        Assert.False(UpdateChecker.IsUpdateAvailable(config));
    }

    [Fact]
    public void GetCurrentVersion_ReturnsNonNullVersion()
    {
        var version = UpdateChecker.GetCurrentVersion();

        Assert.NotNull(version);
        Assert.True(version.Major >= 0);
    }
}
