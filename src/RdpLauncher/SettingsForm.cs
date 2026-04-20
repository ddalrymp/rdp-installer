namespace RdpLauncher;

/// <summary>
/// Settings form: allows users to change Organization, Username, Password.
/// Accessed via a gear icon on the main form.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly TextBox _orgBox;
    private readonly TextBox _usernameBox;
    private readonly TextBox _passwordBox;
    private readonly CheckBox _saveAllCheckBox;
    private readonly Button _saveButton;
    private readonly Button _cancelButton;
    private readonly Button _clearPasswordButton;
    private readonly LinkLabel _logLink;
    private readonly Label _statusLabel;

    private readonly CredentialManager _credentials;

    public bool CredentialsChanged { get; private set; }

    public SettingsForm(CredentialManager credentials)
    {
        _credentials = credentials;

        Text = "Settings";
        Size = new System.Drawing.Size(380, 340);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var orgLabel = new Label
        {
            Text = "Organization:",
            Location = new System.Drawing.Point(15, 15),
            Size = new System.Drawing.Size(120, 20),
            Font = new System.Drawing.Font("Segoe UI", 9f)
        };
        Controls.Add(orgLabel);

        _orgBox = new TextBox
        {
            Text = credentials.Organization,
            Location = new System.Drawing.Point(15, 37),
            Size = new System.Drawing.Size(335, 25),
            Font = new System.Drawing.Font("Segoe UI", 10f)
        };
        Controls.Add(_orgBox);

        var userLabel = new Label
        {
            Text = "Username:",
            Location = new System.Drawing.Point(15, 70),
            Size = new System.Drawing.Size(120, 20),
            Font = new System.Drawing.Font("Segoe UI", 9f)
        };
        Controls.Add(userLabel);

        _usernameBox = new TextBox
        {
            Text = credentials.Username,
            Location = new System.Drawing.Point(15, 92),
            Size = new System.Drawing.Size(335, 25),
            Font = new System.Drawing.Font("Segoe UI", 10f)
        };
        Controls.Add(_usernameBox);

        var passwordLabel = new Label
        {
            Text = "Password:",
            Location = new System.Drawing.Point(15, 125),
            Size = new System.Drawing.Size(335, 20),
            Font = new System.Drawing.Font("Segoe UI", 9f)
        };
        Controls.Add(passwordLabel);

        _passwordBox = new TextBox
        {
            Location = new System.Drawing.Point(15, 147),
            Size = new System.Drawing.Size(335, 25),
            Font = new System.Drawing.Font("Segoe UI", 10f),
            UseSystemPasswordChar = true
        };
        if (credentials.HasPassword)
            _passwordBox.PlaceholderText = "(saved - leave blank to keep)";
        Controls.Add(_passwordBox);

        _saveAllCheckBox = new CheckBox
        {
            Text = "Save all credentials",
            Location = new System.Drawing.Point(15, 182),
            Size = new System.Drawing.Size(200, 20),
            Font = new System.Drawing.Font("Segoe UI", 9f),
            Checked = true
        };
        Controls.Add(_saveAllCheckBox);

        _clearPasswordButton = new Button
        {
            Text = "Clear Saved Password",
            Location = new System.Drawing.Point(15, 210),
            Size = new System.Drawing.Size(150, 28),
            Font = new System.Drawing.Font("Segoe UI", 8.5f)
        };
        _clearPasswordButton.Click += OnClearPassword;
        Controls.Add(_clearPasswordButton);

        _statusLabel = new Label
        {
            Text = "",
            Location = new System.Drawing.Point(175, 215),
            Size = new System.Drawing.Size(175, 20),
            Font = new System.Drawing.Font("Segoe UI", 8.5f),
            ForeColor = System.Drawing.Color.Green
        };
        Controls.Add(_statusLabel);

        _logLink = new LinkLabel
        {
            Text = "Open debug log",
            Location = new System.Drawing.Point(15, 245),
            Size = new System.Drawing.Size(200, 20),
            Font = new System.Drawing.Font("Segoe UI", 8.5f)
        };
        _logLink.LinkClicked += (_, _) =>
        {
            try
            {
                var logPath = Logger.LogFilePath;
                if (File.Exists(logPath))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(logPath) { UseShellExecute = true });
                else
                    MessageBox.Show($"Log file not found:\n{logPath}", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch { }
        };
        Controls.Add(_logLink);

        _saveButton = new Button
        {
            Text = "Save",
            Location = new System.Drawing.Point(165, 268),
            Size = new System.Drawing.Size(85, 30),
            DialogResult = DialogResult.OK
        };
        _saveButton.Click += OnSave;
        Controls.Add(_saveButton);

        _cancelButton = new Button
        {
            Text = "Cancel",
            Location = new System.Drawing.Point(260, 268),
            Size = new System.Drawing.Size(85, 30),
            DialogResult = DialogResult.Cancel
        };
        Controls.Add(_cancelButton);

        AcceptButton = _saveButton;
        CancelButton = _cancelButton;
    }

    private void OnSave(object? sender, EventArgs e)
    {
        var org = _orgBox.Text.Trim();
        var username = _usernameBox.Text.Trim();

        if (string.IsNullOrEmpty(org) || string.IsNullOrEmpty(username))
        {
            MessageBox.Show("Organization and Username are required.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        var identityChanged = !string.Equals(org, _credentials.Organization, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(username, _credentials.Username, StringComparison.OrdinalIgnoreCase);

        _credentials.SaveIdentity(org, username);

        if (!string.IsNullOrEmpty(_passwordBox.Text))
        {
            _credentials.SetPassword(_passwordBox.Text);
            if (_saveAllCheckBox.Checked)
                _credentials.SavePassword();
            CredentialsChanged = true;
        }
        else if (identityChanged)
        {
            // Identity changed but no new password — clear old one since it's for a different user
            _credentials.ClearPassword();
            CredentialsChanged = true;
        }

        if (identityChanged)
            CredentialsChanged = true;
    }

    private void OnClearPassword(object? sender, EventArgs e)
    {
        _credentials.ClearPassword();
        _statusLabel.Text = "Password cleared.";
        _passwordBox.PlaceholderText = "";
        CredentialsChanged = true;
    }
}
