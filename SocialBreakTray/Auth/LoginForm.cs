using System.Diagnostics;

using SocialBreakTray.Api;
using SocialBreakTray.Ui;

namespace SocialBreakTray.Auth;

/// <summary>
/// Three ways in, in descending order of how much the person has to do.
///
/// The first is the one to reach for: the app opens the browser, they press
/// one button on a page they are already signed into, and the app collects a
/// token by itself. It works the same whether the account has a password or
/// signed up through Google - which matters, because a Google account has no
/// password at all and a tray app has nowhere to put a "Continue with Google"
/// button, so without something like this those accounts simply could not get
/// in. Then a username-or-email and password, for anyone who would rather
/// type. Then a one-time code, for when the browser cannot be reached from
/// here at all.
///
/// Built up in code rather than a .Designer.cs/.resx pair - this is a small
/// enough form that a hand-written layout is simpler than generating designer
/// boilerplate no visual designer here can produce.
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
    // 34 was the height of the text and nothing else. These are the first
    // things anyone sees of the app, and they read as cramped.
    private const int FieldHeight = 44;

    private readonly SocialBreakApiClient _apiClient;
    private readonly RoundedField _usernameField = new("Username or email", FieldWidth, FieldHeight);
    private readonly RoundedField _passwordField = new("Password", FieldWidth, FieldHeight);
    private readonly RoundedField _codeField = new("Connect code", FieldWidth, FieldHeight);
    private readonly LinkLabel _codeHelp = new();
    private readonly LinkLabel _modeLink = new();
    private readonly Label _statusLabel = new();
    private readonly Label _dividerLabel = new();
    private readonly RoundedButton _browserButton = new();
    private readonly RoundedButton _loginButton = new();
    private bool _codeMode;
    private CancellationTokenSource? _waiting;

    public string? AcquiredToken { get; private set; }

    public LoginForm(SocialBreakApiClient apiClient) : base("Social Break", showMinimize: false)
    {
        _apiClient = apiClient;
        // Tall enough for the status line's three wrapped lines - the
        // server's explanations are sentences, not single words, and a
        // clipped explanation is no better than none.
        SetContentSize(FieldWidth + Pad * 2, 532);
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

        _browserButton.Text = "Sign in with your browser";
        _browserButton.Location = new Point(Pad, y);
        _browserButton.Size = new Size(FieldWidth, 40);
        _browserButton.BackColor = Theme.ButtonGreen;
        _browserButton.ForeColor = Color.White;
        _browserButton.HoverColor = Theme.ButtonGreenHover;
        _browserButton.Font = new Font(Theme.UiFont.FontFamily, 10f, FontStyle.Bold);
        _browserButton.Cursor = Cursors.Hand;
        _browserButton.Click += async (_, _) => await OnBrowserSignInAsync();
        y += 40 + 16;

        _dividerLabel.Text = "or type your details";
        _dividerLabel.ForeColor = Theme.TextMuted;
        _dividerLabel.AutoSize = false;
        _dividerLabel.TextAlign = ContentAlignment.MiddleCenter;
        _dividerLabel.Location = new Point(Pad, y);
        _dividerLabel.Size = new Size(FieldWidth, 18);
        y += 18 + 14;

        // Placeholder text instead of separate labels: two field captions on a
        // two-field form is more furniture than information.
        // Either identifier is accepted server-side (see core/auth_backends.py
        // in the API): a Google signup is never shown the username that was
        // generated for it, so the email is all some people have.
        int fieldsTop = y;
        _usernameField.Location = new Point(Pad, y);
        int secondRowTop = y + FieldHeight + 12;
        _passwordField.Location = new Point(Pad, secondRowTop);
        _passwordField.Box.UseSystemPasswordChar = true;
        y = secondRowTop + FieldHeight + 20;

        // Sits where the username field does, since the two ways in are
        // alternatives rather than one form with an extra box.
        _codeField.Location = new Point(Pad, fieldsTop);
        _codeField.Box.CharacterCasing = CharacterCasing.Upper;
        _codeField.Box.TextAlign = HorizontalAlignment.Center;
        _codeField.Box.Font = new Font(Theme.UiFont.FontFamily, 12f, FontStyle.Bold);
        _codeField.LayoutBox();
        _codeField.Visible = false;

        // A code box with no idea where the code comes from is a dead end,
        // and telling someone to go and find a page is only half an answer
        // when the app can just open it. This takes the space the password
        // field leaves behind, so it costs no height at all.
        const string helpText = "Codes are made on the website. Open the page that makes one";
        const string helpLink = "Open the page that makes one";
        _codeHelp.Text = helpText;
        _codeHelp.LinkArea = new LinkArea(helpText.IndexOf(helpLink, StringComparison.Ordinal), helpLink.Length);
        _codeHelp.ForeColor = Theme.TextSecondary;
        _codeHelp.LinkColor = Theme.AccentGreen;
        _codeHelp.ActiveLinkColor = Theme.TextPrimary;
        _codeHelp.VisitedLinkColor = Theme.AccentGreen;
        _codeHelp.LinkBehavior = LinkBehavior.HoverUnderline;
        _codeHelp.AutoSize = false;
        _codeHelp.TextAlign = ContentAlignment.MiddleCenter;
        _codeHelp.Font = new Font(Theme.UiFont.FontFamily, 8.5f);
        _codeHelp.Location = new Point(Pad, secondRowTop);
        _codeHelp.Size = new Size(FieldWidth, FieldHeight);
        _codeHelp.Cursor = Cursors.Hand;
        _codeHelp.Visible = false;
        _codeHelp.LinkClicked += (_, _) =>
        {
            if (!TryOpenUrl(SocialBreakApiClient.ConnectCodePageUrl))
            {
                SetStatus("Couldn't open your browser. Go to social-break.com and open Trackers.", isError: true);
            }
        };

        // Quieter than the browser button on purpose: both work, one of them
        // is the one to try first.
        _loginButton.Text = "Log In";
        _loginButton.Location = new Point(Pad, y);
        _loginButton.Size = new Size(FieldWidth, 40);
        _loginButton.BackColor = Theme.Card;
        _loginButton.ForeColor = Theme.TextPrimary;
        _loginButton.OutlineColor = Theme.Border;
        _loginButton.HoverColor = Theme.MenuHover;
        _loginButton.Font = new Font(Theme.UiFont.FontFamily, 10f, FontStyle.Bold);
        _loginButton.Cursor = Cursors.Hand;
        _loginButton.Click += async (_, _) => await OnSubmitAsync();
        y += 40 + 12;

        _modeLink.AutoSize = false;
        _modeLink.Location = new Point(Pad, y);
        _modeLink.Size = new Size(FieldWidth, 20);
        _modeLink.TextAlign = ContentAlignment.MiddleCenter;
        _modeLink.LinkColor = Theme.TextSecondary;
        _modeLink.ActiveLinkColor = Theme.TextPrimary;
        _modeLink.VisitedLinkColor = Theme.TextSecondary;
        _modeLink.LinkBehavior = LinkBehavior.HoverUnderline;
        _modeLink.Font = new Font(Theme.UiFont.FontFamily, 8.5f);
        _modeLink.Cursor = Cursors.Hand;
        _modeLink.LinkClicked += (_, _) =>
        {
            // Doubles as the way out of the waiting state - there is nothing
            // else on screen to press while a browser approval is pending.
            if (_waiting != null) { _waiting.Cancel(); return; }
            SetCodeMode(!_codeMode);
        };
        y += 20 + 10;

        _statusLabel.Location = new Point(Pad, y);
        _statusLabel.Size = new Size(FieldWidth, 68);
        _statusLabel.TextAlign = ContentAlignment.TopCenter;
        _statusLabel.ForeColor = Theme.TextMuted;

        Content.Controls.AddRange(new Control[]
        {
            icon, heading, subheading, _browserButton, _dividerLabel,
            _usernameField, _passwordField, _codeField, _codeHelp,
            _loginButton, _modeLink, _statusLabel,
        });
        AcceptButton = _loginButton;
        SetCodeMode(false);
    }

    /// <summary>Swaps which of the two ways in is on screen. They share the
    /// button and the status line, so only the fields and the wording move.</summary>
    private void SetCodeMode(bool codeMode)
    {
        _codeMode = codeMode;
        _usernameField.Visible = !codeMode;
        _passwordField.Visible = !codeMode;
        _codeField.Visible = codeMode;
        _codeHelp.Visible = codeMode;

        _loginButton.Text = codeMode ? "Connect" : "Log In";
        _dividerLabel.Text = codeMode ? "or paste a connect code" : "or type your details";
        _modeLink.Text = ModeLinkText();

        SetStatus("", isError: false);

        // Only when the user actually switched - never while the window is
        // still being built. WinForms hides a placeholder the moment its box
        // has focus, so focusing a field on open leaves it blank and
        // unlabelled, which is worse than no focus at all.
        if (!IsHandleCreated) return;
        if (codeMode) _codeField.Box.Focus(); else _usernameField.Box.Focus();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Left to itself WinForms focuses the first control in tab order - the
        // username box - and hides its placeholder along with it, so the
        // window opened showing an empty rectangle above a labelled Password.
        // Parking focus on the button keeps both captions readable, and
        // AcceptButton already means Enter submits from anywhere.
        ActiveControl = _browserButton;
    }

    /// <summary>UseShellExecute is what hands a URL to the default browser;
    /// without it .NET tries to run the address as a program.</summary>
    private static bool TryOpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string ModeLinkText() => _codeMode
        ? "Sign in with a password instead"
        : "Can't open a browser? Use a connect code";

    /// <summary>Everything off while a browser approval is outstanding: two
    /// half-finished sign-ins racing each other is nobody's idea of a login
    /// window, and the link becomes the way to abandon the one in flight.</summary>
    private void SetBusy(bool busy)
    {
        _browserButton.Enabled = !busy;
        _loginButton.Enabled = !busy;
        _usernameField.Enabled = !busy;
        _passwordField.Enabled = !busy;
        _codeField.Enabled = !busy;
        _modeLink.Text = busy ? "Cancel" : ModeLinkText();
    }

    /// <summary>
    /// The path that asks the least of the person: open their browser, wait
    /// for them to press one button on a page they are already signed into,
    /// and pick the token up on the next poll. Nothing is typed or copied,
    /// and it is identical for a password account and a Google one.
    /// </summary>
    private async Task OnBrowserSignInAsync()
    {
        SetBusy(true);
        SetStatus("Opening your browser...", isError: false);

        DeviceAuthStartResponse? start;
        try
        {
            start = await _apiClient.StartDeviceAuthAsync();
        }
        catch
        {
            SetStatus("Couldn't reach the server. Try again.", isError: true);
            SetBusy(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(start?.DeviceCode) || string.IsNullOrWhiteSpace(start?.ApprovalUrl))
        {
            SetStatus("Couldn't start that just now. Try your password instead.", isError: true);
            SetBusy(false);
            return;
        }

        if (!TryOpenUrl(start.ApprovalUrl))
        {
            SetStatus("Couldn't open your browser. Use your password, or a connect code.", isError: true);
            SetBusy(false);
            return;
        }

        string? token = null;
        _waiting = new CancellationTokenSource();
        SetStatus("Waiting for you to approve it in your browser...", isError: false);

        try
        {
            token = await WaitForApprovalAsync(start, _waiting.Token);
        }
        catch (OperationCanceledException)
        {
            SetStatus("", isError: false);
        }
        finally
        {
            _waiting.Dispose();
            _waiting = null;
            SetBusy(false);
        }

        if (token == null) return;

        AcquiredToken = token;
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>Polls until somebody answers, the link runs out, or the person
    /// gives up. A failed poll is not an answer - the browser tab is still
    /// open and a dropped packet should not end the attempt - so only a real
    /// verdict from the server stops the loop.</summary>
    private async Task<string?> WaitForApprovalAsync(DeviceAuthStartResponse start, CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, start.Interval));
        var deadline = DateTime.UtcNow.AddSeconds(start.ExpiresIn > 0 ? start.ExpiresIn : 600);

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(interval, ct);

            DeviceAuthPollResponse? poll;
            try
            {
                poll = await _apiClient.PollDeviceAuthAsync(start.DeviceCode!, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                continue;
            }

            switch (poll?.Status)
            {
                case "approved":
                    if (!string.IsNullOrWhiteSpace(poll.Token)) return poll.Token;
                    return null;
                case "denied":
                    SetStatus("That was turned down in the browser. Nothing is connected.", isError: true);
                    return null;
                case null:
                case "pending":
                    continue;
                default:
                    SetStatus("That approval link ran out. Press the button again for a fresh one.", isError: true);
                    return null;
            }
        }

        SetStatus("Nobody approved it in time. Press the button again for a fresh link.", isError: true);
        return null;
    }

    private async Task OnSubmitAsync()
    {
        if (_codeMode)
        {
            await OnConnectClickedAsync();
            return;
        }

        await OnLoginClickedAsync();
    }

    private async Task OnConnectClickedAsync()
    {
        var code = _codeField.Box.Text.Trim();
        if (string.IsNullOrEmpty(code))
        {
            SetStatus("Enter the code from the website.", isError: true);
            return;
        }

        SetStatus("Connecting...", isError: false);
        _loginButton.Enabled = false;

        try
        {
            // Dashes and casing are normalised server-side, so a pasted
            // "ABCD-EFGH" is as good as a typed one.
            var result = await _apiClient.ConnectWithCodeAsync(code);
            if (result?.Token != null)
            {
                AcquiredToken = result.Token;
                _codeField.Box.Text = "";
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                SetStatus(result?.Error ?? "That code didn't work. Get a fresh one from the website.", isError: true);
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

    private async Task OnLoginClickedAsync()
    {
        var username = _usernameField.Box.Text.Trim();
        var password = _passwordField.Box.Text;

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
                _passwordField.Box.Text = "";
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
