using SocialBreakTray.Api;

namespace SocialBreakTray.Auth;

/// <summary>
/// Username/password login, mirroring the browser extension's options.js
/// login flow exactly (same endpoint, same request/response shape) so
/// logging into the desktop app feels identical to logging into the
/// extension. Built up in code rather than a .Designer.cs/.resx pair - this
/// is a small enough form that a hand-written layout is simpler than
/// generating designer boilerplate no visual designer here can produce.
/// </summary>
public class LoginForm : Form
{
    private readonly SocialBreakApiClient _apiClient;
    private readonly TextBox _usernameBox = new();
    private readonly TextBox _passwordBox = new();
    private readonly Label _statusLabel = new();
    private readonly Button _loginButton = new();

    public string? AcquiredToken { get; private set; }

    public LoginForm(SocialBreakApiClient apiClient)
    {
        _apiClient = apiClient;

        Text = "Social Break - Log In";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(340, 230);
        BackColor = Color.FromArgb(0x1e, 0x1e, 0x2e);

        var introLabel = new Label
        {
            Text = "Log in with your Social Break account to connect this device.",
            ForeColor = Color.FromArgb(0xa6, 0xad, 0xc8),
            AutoSize = false,
            Location = new Point(16, 14),
            Size = new Size(308, 40),
        };

        var usernameLabel = MakeFieldLabel("Username", 64);
        _usernameBox.Location = new Point(16, 84);
        _usernameBox.Size = new Size(308, 24);

        var passwordLabel = MakeFieldLabel("Password", 114);
        _passwordBox.Location = new Point(16, 134);
        _passwordBox.Size = new Size(308, 24);
        _passwordBox.UseSystemPasswordChar = true;

        _loginButton.Text = "Log In";
        _loginButton.Location = new Point(16, 168);
        _loginButton.Size = new Size(308, 32);
        _loginButton.BackColor = Color.FromArgb(0x4c, 0xaf, 0x50);
        _loginButton.ForeColor = Color.White;
        _loginButton.FlatStyle = FlatStyle.Flat;
        _loginButton.FlatAppearance.BorderSize = 0;
        _loginButton.Click += async (_, _) => await OnLoginClickedAsync();

        _statusLabel.Location = new Point(16, 204);
        _statusLabel.Size = new Size(308, 20);
        _statusLabel.TextAlign = ContentAlignment.MiddleCenter;
        _statusLabel.Font = new Font(_statusLabel.Font, FontStyle.Bold);

        Controls.AddRange(new Control[] { introLabel, usernameLabel, _usernameBox, passwordLabel, _passwordBox, _loginButton, _statusLabel });
        AcceptButton = _loginButton;
    }

    private static Label MakeFieldLabel(string text, int y) => new()
    {
        Text = text,
        ForeColor = Color.FromArgb(0xa6, 0xad, 0xc8),
        Location = new Point(16, y),
        AutoSize = true,
    };

    private async Task OnLoginClickedAsync()
    {
        var username = _usernameBox.Text.Trim();
        var password = _passwordBox.Text;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            SetStatus("Please enter your username and password.", isError: true);
            return;
        }

        SetStatus("Logging in...", isError: false);
        _loginButton.Enabled = false;

        try
        {
            var result = await _apiClient.LoginAsync(username, password);
            if (result?.Token != null)
            {
                AcquiredToken = result.Token;
                _passwordBox.Text = "";
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                SetStatus(result?.Error ?? "Invalid username or password.", isError: true);
            }
        }
        catch
        {
            SetStatus("Couldn't reach the server. Please try again.", isError: true);
        }
        finally
        {
            _loginButton.Enabled = true;
        }
    }

    private void SetStatus(string text, bool isError)
    {
        _statusLabel.Text = text;
        _statusLabel.ForeColor = isError ? Color.FromArgb(0xff, 0x6b, 0x6b) : Color.FromArgb(0xaa, 0xaa, 0xaa);
    }
}
