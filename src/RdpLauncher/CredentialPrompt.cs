namespace RdpLauncher;

/// <summary>
/// Dialog that prompts for Organization, Username, and Password on first launch
/// or when credentials are not saved. All three fields can be pre-populated and saved.
/// </summary>
public sealed class CredentialPrompt : Form
{
    private readonly TextBox _orgBox;
    private readonly TextBox _usernameBox;
    private readonly TextBox _passwordBox;
    private readonly CheckBox _rememberCheckBox;
    private readonly Button _okButton;
    private readonly Button _cancelButton;

    public string Organization => _orgBox.Text.Trim();
    public string EnteredUsername => _usernameBox.Text.Trim();
    public string Password => _passwordBox.Text;
    public bool RememberAll => _rememberCheckBox.Checked;

    public CredentialPrompt(string? organization = null, string? username = null)
    {
        Text = "Sign In";
        Size = new System.Drawing.Size(380, 310);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var orgLabel = new Label
        {
            Text = "Organization:",
            Location = new System.Drawing.Point(15, 15),
            Size = new System.Drawing.Size(340, 20),
            Font = new System.Drawing.Font("Segoe UI", 9f)
        };
        Controls.Add(orgLabel);

        _orgBox = new TextBox
        {
            Text = organization ?? "",
            Location = new System.Drawing.Point(15, 37),
            Size = new System.Drawing.Size(335, 25),
            Font = new System.Drawing.Font("Segoe UI", 10f)
        };
        Controls.Add(_orgBox);

        var userLabel = new Label
        {
            Text = "Username:",
            Location = new System.Drawing.Point(15, 70),
            Size = new System.Drawing.Size(340, 20),
            Font = new System.Drawing.Font("Segoe UI", 9f)
        };
        Controls.Add(userLabel);

        _usernameBox = new TextBox
        {
            Text = username ?? "",
            Location = new System.Drawing.Point(15, 92),
            Size = new System.Drawing.Size(335, 25),
            Font = new System.Drawing.Font("Segoe UI", 10f)
        };
        Controls.Add(_usernameBox);

        var passwordLabel = new Label
        {
            Text = "Password:",
            Location = new System.Drawing.Point(15, 125),
            Size = new System.Drawing.Size(340, 20),
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
        _passwordBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && ValidateInput())
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        };
        Controls.Add(_passwordBox);

        _rememberCheckBox = new CheckBox
        {
            Text = "Save all credentials",
            Location = new System.Drawing.Point(15, 182),
            Size = new System.Drawing.Size(200, 20),
            Font = new System.Drawing.Font("Segoe UI", 9f),
            Checked = true
        };
        Controls.Add(_rememberCheckBox);

        _okButton = new Button
        {
            Text = "Connect",
            Location = new System.Drawing.Point(165, 215),
            Size = new System.Drawing.Size(85, 30),
            DialogResult = DialogResult.OK
        };
        _okButton.Click += (_, _) =>
        {
            if (!ValidateInput())
                DialogResult = DialogResult.None;
        };
        Controls.Add(_okButton);

        _cancelButton = new Button
        {
            Text = "Cancel",
            Location = new System.Drawing.Point(260, 215),
            Size = new System.Drawing.Size(85, 30),
            DialogResult = DialogResult.Cancel
        };
        Controls.Add(_cancelButton);

        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        // Focus the first empty field
        Shown += (_, _) =>
        {
            if (string.IsNullOrEmpty(_orgBox.Text))
                _orgBox.Focus();
            else if (string.IsNullOrEmpty(_usernameBox.Text))
                _usernameBox.Focus();
            else
                _passwordBox.Focus();
        };
    }

    private bool ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(_orgBox.Text))
        {
            MessageBox.Show("Please enter your organization.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _orgBox.Focus();
            return false;
        }
        if (string.IsNullOrWhiteSpace(_usernameBox.Text))
        {
            MessageBox.Show("Please enter your username.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _usernameBox.Focus();
            return false;
        }
        if (string.IsNullOrEmpty(_passwordBox.Text))
        {
            MessageBox.Show("Please enter your password.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _passwordBox.Focus();
            return false;
        }
        return true;
    }
}
