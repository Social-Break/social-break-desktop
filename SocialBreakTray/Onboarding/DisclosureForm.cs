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
        BackColor = Color.FromArgb(0x1e, 0x1e, 0x2e);

        var heading = new Label
        {
            Text = "What this app does",
            ForeColor = Color.FromArgb(0xcd, 0xd6, 0xf4),
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            AutoSize = true,
        };

        // AutoSize + MaximumSize (wrap width only, 0 = unbounded height)
        // rather than a hardcoded Size - a fixed pixel height silently
        // clips this text on any machine where the actual rendered font/DPI
        // needs more vertical space than originally guessed, which is
        // exactly what happened here (verified: text was being cut off
        // mid-sentence on a real Windows machine, not just a theoretical
        // risk). Matches the pattern already used in AboutForm/BlockForm.
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
            AutoSize = true,
            MaximumSize = new Size(400, 0),
        };

        var continueButton = new Button
        {
            Text = "I Understand, Continue",
            AutoSize = false,
            Size = new Size(400, 34),
            BackColor = Color.FromArgb(0x4c, 0xaf, 0x50),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 20, 0, 0),
        };
        continueButton.FlatAppearance.BorderSize = 0;
        continueButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            WrapContents = false,
            Location = new Point(20, 20),
        };
        panel.Controls.Add(heading);
        panel.Controls.Add(body);
        panel.SetFlowBreak(body, true);
        panel.Controls.Add(continueButton);

        Controls.Add(panel);
        Load += (_, _) =>
        {
            ClientSize = new Size(panel.Width + 40, panel.Height + 40);
        };

        AcceptButton = continueButton;

        // Deliberately no close/cancel path that lets someone skip past this
        // without acknowledging it and still end up logged in - closing the
        // form via the X is the only other way out, and Program.cs treats
        // anything other than DialogResult.OK as "don't proceed to login."
    }
}
