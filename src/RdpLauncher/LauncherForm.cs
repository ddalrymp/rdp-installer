using Microsoft.Win32;
using System.Text.Json;

namespace RdpLauncher;

public sealed class LauncherForm : Form
{
    private readonly Label _statusLabel;
    private readonly ProgressBar _progressBar;

    private readonly string _configUrl;
    private readonly string _connectionId;
    private readonly string _cacheDir;
    private string _userCode;

    public LauncherForm()
    {
        // --- Load settings ---
        var settings = LoadSettings();
        _configUrl = GetConfigUrl(settings);
        _connectionId = settings.GetProperty("ConnectionId").GetString() ?? "main-app";
        var appDataFolder = settings.GetProperty("AppDataFolder").GetString() ?? "RdpLauncher";
        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            appDataFolder, "cache");
        _userCode = GetUserCode();

        // --- Form setup ---
        Text = "RDP Launcher";
        Size = new System.Drawing.Size(400, 160);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;

        _statusLabel = new Label
        {
            Text = "Connecting...",
            Location = new System.Drawing.Point(20, 20),
            Size = new System.Drawing.Size(340, 25),
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
            // Step 0: Ensure we have a user code
            if (string.IsNullOrEmpty(_userCode))
            {
                using var prompt = new UserCodePrompt();
                if (prompt.ShowDialog(this) != DialogResult.OK ||
                    string.IsNullOrWhiteSpace(prompt.UserCode))
                {
                    Close();
                    return;
                }

                _userCode = prompt.UserCode;
                SaveUserCode(_userCode);
            }

            // Step 1: Fetch config
            UpdateStatus("Checking for updates...");
            var configService = new ConfigService(_configUrl, _cacheDir);
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

            // Step 2: Get the target connection
            var connection = ConfigService.GetConnection(config, _connectionId);
            if (connection == null)
            {
                ShowError($"Connection '{_connectionId}' not found in configuration.");
                return;
            }

            // Step 3: Check for launcher update
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

            // Step 4: Ensure cert is trusted
            if (!string.IsNullOrEmpty(connection.CertThumbprint) &&
                !CertificateManager.IsCertificateTrusted(connection.CertThumbprint))
            {
                UpdateStatus("Installing security certificate...");

                bool certImported;
                if (!fromCache && !string.IsNullOrEmpty(connection.SigningCertUrl))
                {
                    certImported = await CertificateManager.DownloadAndImportCertificateAsync(
                        connection.SigningCertUrl, _cacheDir);
                }
                else
                {
                    certImported = CertificateManager.ImportFromCache(_cacheDir);
                }

                if (!certImported)
                {
                    // Non-fatal: connection may still work, user will just see a trust prompt
                    UpdateStatus("Certificate import skipped. You may see a trust prompt.");
                    await Task.Delay(1500);
                }
            }

            // Step 5: Ensure .rdp file is available
            UpdateStatus($"Preparing connection to {connection.DisplayName}...");
            var cachedThumbprint = configService.GetCachedThumbprint(_connectionId);
            var rdpManager = new RdpFileManager(_cacheDir);
            var rdpPath = await rdpManager.EnsureRdpFileAsync(connection, cachedThumbprint, _userCode);

            if (rdpPath == null)
            {
                ShowError("Unable to download the connection file.\n\n" +
                    "Please check your internet connection and try again.");
                return;
            }

            // Step 6: Prepare temp copy and launch
            var tempRdpPath = RdpFileManager.PrepareForLaunch(rdpPath);
            if (tempRdpPath == null)
            {
                ShowError("Unable to prepare the connection file.");
                return;
            }

            UpdateStatus($"Launching {connection.DisplayName}...");

            // Hide the form while the RDP session is active
            Hide();

            var exitCode = await ProcessLauncher.LaunchAndWaitAsync(tempRdpPath);

            if (exitCode == -1)
            {
                Show();
                ShowError("Failed to launch Remote Desktop.\n\n" +
                    "Ensure mstsc.exe is available on this system.");
                return;
            }

            // Session ended normally — close the launcher
            Close();
        }
        catch (Exception ex)
        {
            ShowError($"An unexpected error occurred:\n\n{ex.Message}");
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

    /// <summary>
    /// Reads the user code from the registry (set by installer or first-run prompt).
    /// </summary>
    private static string GetUserCode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\RdpLauncher");
            var code = key?.GetValue("UserCode") as string;
            if (!string.IsNullOrEmpty(code))
                return code;
        }
        catch
        {
            // Registry not available — fall through
        }

        return "";
    }

    /// <summary>
    /// Saves the user code to the registry for subsequent launches.
    /// </summary>
    private static void SaveUserCode(string userCode)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\RdpLauncher");
            key.SetValue("UserCode", userCode);
        }
        catch
        {
            // Non-fatal: user will be prompted again next time
        }
    }
}
