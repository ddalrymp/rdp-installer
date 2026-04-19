namespace RdpLauncher;

/// <summary>
/// Settings form: allows users to change OrgId, UserId, password, and test the connection.
/// Accessed via a gear icon on the main form.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly TextBox _orgIdBox;
    private readonly TextBox _userIdBox;
    private readonly TextBox _passwordBox;
    private readonly Button _saveButton;
    private readonly Button _cancelButton;
    private readonly Button _clearPasswordButton;
    private readonly Label _statusLabel;

    private readonly CredentialManager _credentials;

    public bool CredentialsChanged { get; private set; }

    public SettingsForm(CredentialManager credentials)
    {
        _credentials = credentials;

        Text = "Settings";
        Size = new System.Drawing.Size(380, 300);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var orgLabel = new Label
        {
            Text = "Organization ID:",
            Location = new System.Drawing.Point(15, 15),
            Size = new System.Drawing.Size(120, 20),
            Font = new System.Drawing.Font("Segoe UI", 9f)
        };
        Controls.Add(orgLabel);

        _orgIdBox = new TextBox
        {
            Text = credentials.OrgId,
            Location = new System.Drawing.Point(15, 37),
            Size = new System.Drawing.Size(335, 25),
            Font = new System.Drawing.Font("Segoe UI", 10f),
            CharacterCasing = CharacterCasing.Upper
        };
        Controls.Add(_orgIdBox);

        var userLabel = new Label
        {
            Text = "User ID:",
            Location = new System.Drawing.Point(15, 70),
            Size = new System.Drawing.Size(120, 20),
            Font = new System.Drawing.Font("Segoe UI", 9f)
        };
        Controls.Add(userLabel);

        _userIdBox = new TextBox
        {
            Text = credentials.UserId,
            Location = new System.Drawing.Point(15, 92),
            Size = new System.Drawing.Size(335, 25),
            Font = new System.Drawing.Font("Segoe UI", 10f),
            CharacterCasing = CharacterCasing.Upper
        };
        Controls.Add(_userIdBox);

        var passwordLabel = new Label
        {
            Text = "New Password (leave blank to keep current):",
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
        Controls.Add(_passwordBox);

        _clearPasswordButton = new Button
        {
            Text = "Clear Saved Password",
            Location = new System.Drawing.Point(15, 185),
            Size = new System.Drawing.Size(150, 28),
            Font = new System.Drawing.Font("Segoe UI", 8.5f)
        };
        _clearPasswordButton.Click += OnClearPassword;
        Controls.Add(_clearPasswordButton);

        _statusLabel = new Label
        {
            Text = "",
            Location = new System.Drawing.Point(175, 190),
            Size = new System.Drawing.Size(175, 20),
            Font = new System.Drawing.Font("Segoe UI", 8.5f),
            ForeColor = System.Drawing.Color.Green
        };
        Controls.Add(_statusLabel);

        _saveButton = new Button
        {
            Text = "Save",
            Location = new System.Drawing.Point(165, 225),
            Size = new System.Drawing.Size(85, 30),
            DialogResult = DialogResult.OK
        };
        _saveButton.Click += OnSave;
        Controls.Add(_saveButton);

        _cancelButton = new Button
        {
            Text = "Cancel",
            Location = new System.Drawing.Point(260, 225),
            Size = new System.Drawing.Size(85, 30),
            DialogResult = DialogResult.Cancel
        };
        Controls.Add(_cancelButton);

        AcceptButton = _saveButton;
        CancelButton = _cancelButton;
    }

    private void OnSave(object? sender, EventArgs e)
    {
        var orgId = _orgIdBox.Text.Trim();
        var userId = _userIdBox.Text.Trim();

        if (string.IsNullOrEmpty(orgId) || string.IsNullOrEmpty(userId))
        {
            MessageBox.Show("Organization ID and User ID are required.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        var identityChanged = !string.Equals(orgId, _credentials.OrgId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(userId, _credentials.UserId, StringComparison.OrdinalIgnoreCase);

        _credentials.SaveIdentity(orgId, userId);

        if (!string.IsNullOrEmpty(_passwordBox.Text))
        {
            _credentials.SetPassword(_passwordBox.Text);
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
        CredentialsChanged = true;
    }
}
