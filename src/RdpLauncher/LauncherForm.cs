using Microsoft.Win32;
using System.Text.Json;

namespace RdpLauncher;

public sealed class LauncherForm : Form
{
    private readonly Label _statusLabel;
    private readonly ProgressBar _progressBar;
    private readonly Button _settingsButton;

    private readonly string _configUrl;
    private readonly string _connectionId;
    private readonly string _cacheDir;
    private readonly int _cacheTtlMinutes;
    private readonly CredentialManager _credentials;

    public LauncherForm()
    {
        // --- Load settings ---
        var settings = LoadSettings();
        _configUrl = GetConfigUrl(settings);
        _connectionId = settings.TryGetProperty("ConnectionId", out var connProp)
            ? connProp.GetString() ?? "main-app" : "main-app";
        var appDataFolder = settings.TryGetProperty("AppDataFolder", out var folderProp)
            ? folderProp.GetString() ?? "RdpLauncher" : "RdpLauncher";
        _cacheTtlMinutes = settings.TryGetProperty("ConfigCacheTtlMinutes", out var ttlProp)
            ? ttlProp.GetInt32() : 60;
        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            appDataFolder, "cache");

        Logger.Info("=== RDP Launcher starting ===");
        Logger.Debug($"ConfigUrl: {_configUrl}");
        Logger.Debug($"ConnectionId: {_connectionId}");
        Logger.Debug($"CacheDir: {_cacheDir}");
        Logger.Debug($"CacheTTL: {_cacheTtlMinutes} min");

        // --- Load credentials ---
        _credentials = new CredentialManager();
        _credentials.Load();

        // --- Form setup ---
        Text = "RDP Launcher";
        Size = new System.Drawing.Size(400, 160);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;

        _settingsButton = new Button
        {
            Text = "\u2699",  // gear icon
            Location = new System.Drawing.Point(350, 5),
            Size = new System.Drawing.Size(30, 30),
            FlatStyle = FlatStyle.Flat,
            Font = new System.Drawing.Font("Segoe UI", 12f)
        };
        _settingsButton.FlatAppearance.BorderSize = 0;
        _settingsButton.Click += OnSettingsClick;
        Controls.Add(_settingsButton);

        _statusLabel = new Label
        {
            Text = "Connecting...",
            Location = new System.Drawing.Point(20, 20),
            Size = new System.Drawing.Size(320, 25),
            Font = new System.Drawing.Font("Segoe UI", 10f)
        };
        Controls.Add(_statusLabel);

        _progressBar = new ProgressBar
        {
            Location = new System.Drawing.Point(20, 55),
            Size = new System.Drawing.Size(340, 25),
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 30
        };
        Controls.Add(_progressBar);

        // Start the connection workflow after the form is shown
        Shown += async (_, _) => await RunConnectionWorkflowAsync();
    }

    private async Task RunConnectionWorkflowAsync()
    {
        try
        {
            Logger.Info("Starting connection workflow...");

            // Step 0: Ensure we have all credentials (Organization + Username + Password)
            if (!_credentials.HasIdentity || !_credentials.HasPassword)
            {
                Logger.Debug($"Credentials incomplete. HasIdentity: {_credentials.HasIdentity}, HasPassword: {_credentials.HasPassword}");
                using var prompt = new CredentialPrompt(_credentials.Organization, _credentials.Username);
                if (prompt.ShowDialog(this) != DialogResult.OK)
                {
                    Logger.Info("User cancelled credential prompt. Closing.");
                    Close();
                    return;
                }

                _credentials.SaveIdentity(prompt.Organization, prompt.EnteredUsername);
                _credentials.SetPassword(prompt.Password);

                if (prompt.RememberAll)
                    _credentials.SavePassword();

                Logger.Info($"Credentials entered. Organization: {_credentials.Organization}, Username: {_credentials.Username}");
            }
            else
            {
                Logger.Info($"Using saved credentials. Organization: {_credentials.Organization}, Username: {_credentials.Username}");
            }

            // Step 1: Fetch config
            UpdateStatus("Checking for updates...");
            Logger.Info($"Fetching config from: {_configUrl}");
            var configService = new ConfigService(_configUrl, _cacheDir, _cacheTtlMinutes);
            var (config, fromCache) = await configService.GetConfigAsync();

            if (config == null)
            {
                Logger.Error("Config is null. Unable to load connection settings.");
                ShowError("Unable to load connection settings.\n\n" +
                    "Please check your internet connection and try again.");
                return;
            }

            Logger.Info($"Config loaded (fromCache: {fromCache}). Version: {config.Version}, Connections: {config.Connections.Count}");

            if (fromCache)
            {
                UpdateStatus("Using cached settings (offline mode)...");
            }

            // Step 2: Get the target connection (with template resolution)
            var connection = ConfigService.GetConnection(
                config, _connectionId, _credentials.Organization, _credentials.Username);
            if (connection == null)
            {
                Logger.Error($"Connection '{_connectionId}' not found in configuration.");
                ShowError($"Connection '{_connectionId}' not found in configuration.");
                return;
            }

            Logger.Info($"Connection resolved: {connection.DisplayName} -> {connection.ServerAddress}:{connection.Port}");
            Logger.Debug($"  RemoteApp: {connection.RemoteAppProgram}");
            Logger.Debug($"  Domain: {connection.Domain}");
            Logger.Debug($"  Gateway: {connection.GatewayHostname}");

            // Step 3: Check for launcher update
            if (!fromCache && UpdateChecker.IsUpdateAvailable(config))
            {
                Logger.Info($"Update available: {config.LauncherVersion}");
                var result = MessageBox.Show(
                    $"A new version of the launcher is available.\n\n" +
                    $"Current: {UpdateChecker.GetCurrentVersion()}\n" +
                    $"Available: {config.LauncherVersion}\n\n" +
                    "Would you like to download it now?",
                    "Update Available",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);

                if (result == DialogResult.Yes)
                {
                    UpdateChecker.OpenDownloadPage(config.LauncherDownloadUrl);
                    Close();
                    return;
                }
            }

            // Step 4: Launch mstsc.exe with stored credential
            UpdateStatus($"Launching {connection.DisplayName}...");
            Logger.Info($"Launching connection: {connection.DisplayName}");
            Hide();

            var password = _credentials.GetPassword()!;
            var exitCode = await ProcessLauncher.LaunchAsync(
                connection, _credentials.Username, password,
                _cacheDir, config.FallbackToMstsc);

            if (exitCode == -1)
            {
                Logger.Error($"Launch failed. LastError: {ProcessLauncher.LastError}");
                Show();
                ShowError("Failed to launch Remote Desktop.\n\n" +
                    ProcessLauncher.LastError);
                return;
            }

            Logger.Info($"Session completed with exit code: {exitCode}");

            // Successful connection — save password if not already saved
            _credentials.SavePassword();

            // Session ended normally — close the launcher
            Close();
        }
        catch (Exception ex)
        {
            Logger.Error("Unexpected error in connection workflow", ex);
            ShowError($"An unexpected error occurred:\n\n{ex.Message}");
        }
    }

    private void OnSettingsClick(object? sender, EventArgs e)
    {
        using var settingsForm = new SettingsForm(_credentials);
        if (settingsForm.ShowDialog(this) == DialogResult.OK && settingsForm.CredentialsChanged)
        {
            // Re-run workflow with new credentials
            _ = RunConnectionWorkflowAsync();
        }
    }

    private void UpdateStatus(string message)
    {
        if (InvokeRequired)
        {
            Invoke(() => UpdateStatus(message));
            return;
        }
        _statusLabel.Text = message;
    }

    private void ShowError(string message)
    {
        if (InvokeRequired)
        {
            Invoke(() => ShowError(message));
            return;
        }

        _progressBar.Visible = false;
        MessageBox.Show(message, "RDP Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Close();
    }

    private static JsonElement LoadSettings()
    {
        try
        {
            var exeDir = AppContext.BaseDirectory;
            var settingsPath = Path.Combine(exeDir, "appsettings.json");

            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                return JsonDocument.Parse(json).RootElement;
            }
        }
        catch
        {
            // Fall through to defaults
        }

        return JsonDocument.Parse("{}").RootElement;
    }

    /// <summary>
    /// Reads the config URL from (in priority order):
    /// 1. Registry (set by installer, allows per-machine override)
    /// 2. appsettings.json (embedded default)
    /// </summary>
    private static string GetConfigUrl(JsonElement settings)
    {
        // Try registry first
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\RdpLauncher");
            var registryUrl = key?.GetValue("ConfigUrl") as string;
            if (!string.IsNullOrEmpty(registryUrl))
                return registryUrl;
        }
        catch
        {
            // Registry not available — fall through
        }

        // Fall back to appsettings.json
        if (settings.TryGetProperty("ConfigUrl", out var urlProp))
        {
            var url = urlProp.GetString();
            if (!string.IsNullOrEmpty(url))
                return url;
        }

        return "";
    }
}
