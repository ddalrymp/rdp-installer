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
            // Step 0: Ensure we have identity (OrgId + UserId)
            if (!_credentials.HasCredentials)
            {
                using var settingsForm = new SettingsForm(_credentials);
                if (settingsForm.ShowDialog(this) != DialogResult.OK || !_credentials.HasCredentials)
                {
                    Close();
                    return;
                }
            }

            // Step 1: Ensure we have a password
            if (!_credentials.HasPassword)
            {
                using var prompt = new CredentialPrompt(_credentials.Username);
                if (prompt.ShowDialog(this) != DialogResult.OK)
                {
                    Close();
                    return;
                }
                _credentials.SetPassword(prompt.Password);
            }

            // Step 2: Fetch config
            UpdateStatus("Checking for updates...");
            var configService = new ConfigService(_configUrl, _cacheDir, _cacheTtlMinutes);
            var (config, fromCache) = await configService.GetConfigAsync();

            if (config == null)
            {
                ShowError("Unable to load connection settings.\n\n" +
                    "Please check your internet connection and try again.");
                return;
            }

            if (fromCache)
            {
                UpdateStatus("Using cached settings (offline mode)...");
            }

            // Step 3: Get the target connection (with template resolution)
            var connection = ConfigService.GetConnection(
                config, _connectionId, _credentials.OrgId, _credentials.UserId);
            if (connection == null)
            {
                ShowError($"Connection '{_connectionId}' not found in configuration.");
                return;
            }

            // Step 4: Check for launcher update
            if (!fromCache && UpdateChecker.IsUpdateAvailable(config))
            {
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

            // Step 5: Launch via FreeRDP (primary) or mstsc (fallback)
            UpdateStatus($"Launching {connection.DisplayName}...");
            Hide();

            var password = _credentials.GetPassword()!;
            var exitCode = await ProcessLauncher.LaunchAsync(
                connection, _credentials.Username, password,
                _cacheDir, config.FallbackToMstsc);

            if (exitCode == -1)
            {
                Show();
                ShowError("Failed to launch Remote Desktop.\n\n" +
                    ProcessLauncher.LastError);
                return;
            }

            // Successful connection — save password if not already saved
            _credentials.SavePassword();

            // Session ended normally — close the launcher
            Close();
        }
        catch (Exception ex)
        {
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
