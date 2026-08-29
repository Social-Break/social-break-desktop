namespace SocialBreakTray;

/// <summary>
/// A small "what is this" window - reachable on demand from the tray menu
/// only (see TrayApplicationContext's docstring for what auto-opens on
/// launch instead). Exists so there's always a plain-language explanation
/// available without digging through the website, even though it's no
/// longer the thing shown automatically at startup.
/// </summary>
public class AboutForm : Form
{
    public AboutForm()
    {
        Text = "About Social Break";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(0x1e, 0x1e, 0x2e);

        var heading = new Label
        {
            Text = "Social Break is running",
            ForeColor = Color.FromArgb(0xcd, 0xd6, 0xf4),
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            AutoSize = true,
        };

        // AutoSize + MaximumSize (wrap width only) rather than a hardcoded
        // Size - see DisclosureForm.cs for why a fixed pixel height risks
        // silently clipping this text on machines needing more vertical
        // space per line than originally guessed.
        var body = new Label
        {
            Text =
                "This app tracks time in the desktop applications you've added to your " +
                "Media List on the website - the same way the browser extension tracks " +
                "browser tabs, just for apps outside your browser.\r\n\r\n" +
                "Your Media List, Plan, limits, and usage are all managed on the website, " +
                "same as always - this app's Live Tracking window just shows what's " +
                "currently being counted.\r\n\r\n" +
                "Look for the Social Break icon in your system tray (you may need to click " +
                "the small arrow to show hidden icons) to pause tracking, check status, " +
                "toggle Start with Windows, or log out.",
            ForeColor = Color.FromArgb(0xa6, 0xad, 0xc8),
            AutoSize = true,
            MaximumSize = new Size(360, 0),
        };

        var okButton = new Button
        {
            Text = "Got it",
            AutoSize = false,
            Size = new Size(360, 34),
            BackColor = Color.FromArgb(0x4c, 0xaf, 0x50),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 20, 0, 0),
        };
        okButton.FlatAppearance.BorderSize = 0;
        okButton.Click += (_, _) =>
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
        panel.Controls.Add(okButton);

        Controls.Add(panel);
        Load += (_, _) =>
        {
            ClientSize = new Size(panel.Width + 40, panel.Height + 40);
        };

        AcceptButton = okButton;
    }
}
