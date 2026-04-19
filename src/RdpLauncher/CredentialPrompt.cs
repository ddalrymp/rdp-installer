namespace RdpLauncher;

/// <summary>
/// Dialog that prompts for password on first launch or when password is not saved.
/// </summary>
public sealed class CredentialPrompt : Form
{
    private readonly TextBox _passwordBox;
    private readonly CheckBox _rememberCheckBox;
    private readonly Button _okButton;
    private readonly Button _cancelButton;

    public string Password => _passwordBox.Text;
    public bool RememberPassword => _rememberCheckBox.Checked;

    public CredentialPrompt(string username)
    {
        Text = "Enter Password";
        Size = new System.Drawing.Size(380, 210);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var userLabel = new Label
        {
            Text = $"User: {username}",
            Location = new System.Drawing.Point(15, 15),
            Size = new System.Drawing.Size(340, 20),
            Font = new System.Drawing.Font("Segoe UI", 9f)
        };
        Controls.Add(userLabel);

        var passwordLabel = new Label
        {
            Text = "Password:",
            Location = new System.Drawing.Point(15, 45),
            Size = new System.Drawing.Size(340, 20),
            Font = new System.Drawing.Font("Segoe UI", 9f)
        };
        Controls.Add(passwordLabel);

        _passwordBox = new TextBox
        {
            Location = new System.Drawing.Point(15, 67),
            Size = new System.Drawing.Size(335, 25),
            Font = new System.Drawing.Font("Segoe UI", 10f),
            UseSystemPasswordChar = true
        };
        _passwordBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !string.IsNullOrEmpty(_passwordBox.Text))
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        };
        Controls.Add(_passwordBox);

        _rememberCheckBox = new CheckBox
        {
            Text = "Remember password",
            Location = new System.Drawing.Point(15, 100),
            Size = new System.Drawing.Size(200, 20),
            Font = new System.Drawing.Font("Segoe UI", 9f),
            Checked = true
        };
        Controls.Add(_rememberCheckBox);

        _okButton = new Button
        {
            Text = "Connect",
            Location = new System.Drawing.Point(165, 130),
            Size = new System.Drawing.Size(85, 30),
            DialogResult = DialogResult.OK
        };
        _okButton.Click += (_, _) =>
        {
            if (string.IsNullOrEmpty(_passwordBox.Text))
            {
                MessageBox.Show("Please enter your password.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
            }
        };
        Controls.Add(_okButton);

        _cancelButton = new Button
        {
            Text = "Cancel",
            Location = new System.Drawing.Point(260, 130),
            Size = new System.Drawing.Size(85, 30),
            DialogResult = DialogResult.Cancel
        };
        Controls.Add(_cancelButton);

        AcceptButton = _okButton;
        CancelButton = _cancelButton;
    }
}
