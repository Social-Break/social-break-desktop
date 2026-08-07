namespace SocialBreakTray.Onboarding;

/// <summary>
/// One-time, plain-language explanation of what this app does and doesn't
/// do - shown once, before the first login, alongside the same commitments
/// documented in legal.html's "Desktop App: What It Can Access" section.
/// This exists specifically because a background process that watches
/// which app is in focus looks exactly like spyware if it isn't upfront
/// about it - the whole point of showing this before asking for credentials
/// is that a user should never be surprised by what this app is doing.
/// </summary>
public class DisclosureForm : Form
{
    public DisclosureForm()
    {
        Text = "Social Break Desktop - Before You Continue";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(440, 380);
        BackColor = Color.FromArgb(0x1e, 0x1e, 0x2e);

        var heading = new Label
        {
            Text = "What this app does",
            ForeColor = Color.FromArgb(0xcd, 0xd6, 0xf4),
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(20, 18),
        };

        var body = new Label
        {
            Text =
                "Social Break Desktop tracks time in native applications - the same way the browser " +
                "extension tracks browser tabs, just for apps outside your browser.\r\n\r\n" +
                "It reads only the name of whichever application currently has focus, to check it against " +
                "the desktop-app entries on your Media List (managed on the website). It does not read " +
                "keystrokes, screen contents, file contents, clipboard data, or anything else - and it " +
                "never touches apps you haven't explicitly added.\r\n\r\n" +
                "It stays visible as an icon in your system tray at all times, pauses automatically while " +
                "you're away from your computer, and your login is encrypted on this device and never " +
                "leaves it in plain text.\r\n\r\n" +
                "You can pause tracking, log out, or quit entirely at any time from the tray icon.",
            ForeColor = Color.FromArgb(0xa6, 0xad, 0xc8),
            Location = new Point(20, 54),
            Size = new Size(400, 260),
        };

        var continueButton = new Button
        {
            Text = "I Understand, Continue",
            Location = new Point(20, 328),
            Size = new Size(400, 34),
            BackColor = Color.FromArgb(0x4c, 0xaf, 0x50),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        continueButton.FlatAppearance.BorderSize = 0;
        continueButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        Controls.AddRange(new Control[] { heading, body, continueButton });
        AcceptButton = continueButton;

        // Deliberately no close/cancel path that lets someone skip past this
        // without acknowledging it and still end up logged in - closing the
        // form via the X is the only other way out, and Program.cs treats
        // anything other than DialogResult.OK as "don't proceed to login."
    }
}
