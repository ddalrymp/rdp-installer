namespace RdpLauncher;

/// <summary>
/// A simple dialog that prompts the user for their user code (e.g., ORG1_U01).
/// Shown on first launch if no user code was set by the installer or found in the registry.
/// </summary>
public sealed class UserCodePrompt : Form
{
    private readonly TextBox _textBox;
    private readonly Button _okButton;
    private readonly Button _cancelButton;

    public string UserCode => _textBox.Text.Trim();

    public UserCodePrompt()
    {
        Text = "Enter Your User Code";
        Size = new System.Drawing.Size(380, 180);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var label = new Label
        {
            Text = "Enter the user code provided to you (e.g., ORG1_U01):",
            Location = new System.Drawing.Point(15, 15),
            Size = new System.Drawing.Size(340, 20),
            Font = new System.Drawing.Font("Segoe UI", 9f)
        };
        Controls.Add(label);

        _textBox = new TextBox
        {
            Location = new System.Drawing.Point(15, 45),
            Size = new System.Drawing.Size(335, 25),
            Font = new System.Drawing.Font("Segoe UI", 10f)
        };
        _textBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !string.IsNullOrWhiteSpace(_textBox.Text))
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        };
        Controls.Add(_textBox);

        _okButton = new Button
        {
            Text = "OK",
            Location = new System.Drawing.Point(165, 90),
            Size = new System.Drawing.Size(85, 30),
            DialogResult = DialogResult.OK
        };
        _okButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_textBox.Text))
            {
                MessageBox.Show("Please enter your user code.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
            }
        };
        Controls.Add(_okButton);

        _cancelButton = new Button
        {
            Text = "Cancel",
            Location = new System.Drawing.Point(265, 90),
            Size = new System.Drawing.Size(85, 30),
            DialogResult = DialogResult.Cancel
        };
        Controls.Add(_cancelButton);

        AcceptButton = _okButton;
        CancelButton = _cancelButton;
    }
}
