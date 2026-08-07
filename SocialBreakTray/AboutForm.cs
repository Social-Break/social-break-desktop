namespace SocialBreakTray;

/// <summary>
/// A small "what is this and where did it go" window - shown automatically
/// on every launch, and also reachable from the tray menu at any time.
/// Exists purely so the app isn't a pure background process with zero
/// visible confirmation it's running or what it does; it is deliberately
/// not a dashboard - just a pointer back to the tray icon and the website,
/// matching the "no separate dashboard" design (see
/// TrayApplicationContext's docstring).
/// </summary>
public class AboutForm : Form
{
    /// <summary>Whether the "Don't show this automatically on startup"
    /// checkbox was checked when the dialog closed - read by the caller
    /// after ShowDialog() returns. Only ever affects the automatic
    /// on-startup showing; opening this window from the tray menu always
    /// shows it regardless of this setting.</summary>
    public bool HideOnStartup { get; private set; }

    public AboutForm(bool hideOnStartupChecked)
    {
        Text = "About Social Break";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(400, 330);
        BackColor = Color.FromArgb(0x1e, 0x1e, 0x2e);

        var heading = new Label
        {
            Text = "Social Break is running",
            ForeColor = Color.FromArgb(0xcd, 0xd6, 0xf4),
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(20, 18),
        };

        var body = new Label
        {
            Text =
                "This app tracks time in the desktop applications you've added to your " +
                "Media List on the website - the same way the browser extension tracks " +
                "browser tabs, just for apps outside your browser.\r\n\r\n" +
                "It has no dashboard of its own. Your Media List, Plan, limits, and usage " +
                "are all managed on the website, same as always.\r\n\r\n" +
                "Look for the Social Break icon in your system tray (you may need to click " +
                "the small arrow to show hidden icons) to pause tracking, check status, " +
                "toggle Start with Windows, or log out.",
            ForeColor = Color.FromArgb(0xa6, 0xad, 0xc8),
            Location = new Point(20, 54),
            Size = new Size(360, 190),
        };

        var hideCheckbox = new CheckBox
        {
            Text = "Don't show this automatically on startup",
            ForeColor = Color.FromArgb(0xa6, 0xad, 0xc8),
            AutoSize = true,
            Location = new Point(20, 250),
            Checked = hideOnStartupChecked,
        };

        var okButton = new Button
        {
            Text = "Got it",
            Location = new Point(20, 278),
            Size = new Size(360, 34),
            BackColor = Color.FromArgb(0x4c, 0xaf, 0x50),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        okButton.FlatAppearance.BorderSize = 0;
        okButton.Click += (_, _) =>
        {
            HideOnStartup = hideCheckbox.Checked;
            DialogResult = DialogResult.OK;
            Close();
        };

        Controls.AddRange(new Control[] { heading, body, hideCheckbox, okButton });
        AcceptButton = okButton;
    }
}
