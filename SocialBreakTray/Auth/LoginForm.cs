using SocialBreakTray.Api;
using SocialBreakTray.Ui;

namespace SocialBreakTray.Auth;

/// <summary>
/// Username-or-email/password login, mirroring the browser extension's options.js login
/// flow exactly (same endpoint, same request/response shape) so logging into
/// the desktop app feels identical to logging into the extension. Built up in
/// code rather than a .Designer.cs/.resx pair - this is a small enough form
/// that a hand-written layout is simpler than generating designer boilerplate
/// no visual designer here can produce.
///
/// Built on DarkForm like About and Live Tracking. It previously derived from
/// Form with the system title bar merely recoloured, on the reasoning that a
/// short-lived pre-login dialog didn't need the custom chrome - but this is the
/// very first window anyone sees, and it looked like a different application to
/// the one it opens. internal, like its siblings, because DarkForm is.
/// </summary>
internal class LoginForm : DarkForm
{
    private const int Pad = 30;
    private const int FieldWidth = 320;
    private const int FieldHeight = 34;

    private readonly SocialBreakApiClient _apiClient;
    private readonly TextBox _usernameBox = new();
    private readonly TextBox _passwordBox = new();
    private readonly Label _statusLabel = new();
    private readonly Button _loginButton = new();

    public string? AcquiredToken { get; private set; }

    public LoginForm(SocialBreakApiClient apiClient) : base("Social Break", showMinimize: false)
    {
        _apiClient = apiClient;
        SetContentSize(FieldWidth + Pad * 2, 348);
        StartPosition = FormStartPosition.CenterScreen;

        int y = 26;

        // The mark, so the first window carries the same identity as the tray
        // icon the user is about to start looking for.
        var icon = new Panel
        {
            Location = new Point((FieldWidth + Pad * 2 - 40) / 2, y),
            Size = new Size(40, 40),
            BackColor = Color.Transparent,
        };
        icon.Paint += (_, e) => Theme.DrawAppIcon(e.Graphics, new Rectangle(0, 0, 40, 40));
        y += 40 + 18;

        var heading = new Label
        {
            Text = "Log in to continue",
            ForeColor = Theme.TextPrimary,
            Font = new Font(Theme.UiFont.FontFamily, 14f, FontStyle.Regular),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(Pad, y),
            Size = new Size(FieldWidth, 26),
        };
        y += 26 + 6;

        var subheading = new Label
        {
            Text = "Use the same account as the website.",
            ForeColor = Theme.TextMuted,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(Pad, y),
            Size = new Size(FieldWidth, 20),
        };
        y += 20 + 26;

        // Placeholder text instead of separate labels: two field captions on a
        // two-field form is more furniture than information.
        // Either identifier is accepted server-side (see core/auth_backends.py
        // in the API): a Google signup is never shown the username that was
        // generated for it, so the email is all some people have.
        StyleField(_usernameBox, "Username or email", new Point(Pad, y));
        y += FieldHeight + 12;
        StyleField(_passwordBox, "Password", new Point(Pad, y));
        _passwordBox.UseSystemPasswordChar = true;
        y += FieldHeight + 20;

        _loginButton.Text = "Log In";
        _loginButton.Location = new Point(Pad, y);
        _loginButton.Size = new Size(FieldWidth, 40);
        _loginButton.BackColor = Theme.ButtonGreen;
        _loginButton.ForeColor = Color.White;
        _loginButton.FlatStyle = FlatStyle.Flat;
        _loginButton.FlatAppearance.BorderSize = 0;
        _loginButton.FlatAppearance.MouseOverBackColor = Theme.ButtonGreenHover;
        _loginButton.Font = new Font(Theme.UiFont.FontFamily, 10f, FontStyle.Bold);
        _loginButton.Cursor = Cursors.Hand;
        _loginButton.Click += async (_, _) => await OnLoginClickedAsync();
        y += 40 + 14;

        _statusLabel.Location = new Point(Pad, y);
        _statusLabel.Size = new Size(FieldWidth, 34);
        _statusLabel.TextAlign = ContentAlignment.TopCenter;
        _statusLabel.ForeColor = Theme.TextMuted;

        Content.Controls.AddRange(new Control[]
        {
            icon, heading, subheading, _usernameBox, _passwordBox, _loginButton, _statusLabel,
        });
        AcceptButton = _loginButton;
    }

    /// <summary>Flat dark inputs with an inset well, rather than the bright
    /// white rectangles WinForms draws by default on a dark background.</summary>
    private void StyleField(TextBox box, string placeholder, Point location)
    {
        box.Location = location;
        box.Size = new Size(FieldWidth, FieldHeight);
        box.BackColor = Theme.Well;
        box.ForeColor = Theme.TextPrimary;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = new Font(Theme.UiFont.FontFamily, 10.5f);
        box.PlaceholderText = placeholder;
    }

    private async Task OnLoginClickedAsync()
    {
        var username = _usernameBox.Text.Trim();
        var password = _passwordBox.Text;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            SetStatus("Enter your username or email, and your password.", isError: true);
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
                SetStatus(result?.Error ?? "That didn't match. Check your details and try again.", isError: true);
            }
        }
        catch
        {
            SetStatus("Couldn't reach the server. Try again.", isError: true);
        }
        finally
        {
            _loginButton.Enabled = true;
        }
    }

    private void SetStatus(string text, bool isError)
    {
        _statusLabel.Text = text;
        _statusLabel.ForeColor = isError ? Theme.AccentRed : Theme.TextMuted;
    }
}
