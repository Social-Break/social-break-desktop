using SocialBreakTray.Ui;

namespace SocialBreakTray;

/// <summary>
/// A small "what is this" window - reachable on demand from the tray menu
/// only (see TrayApplicationContext's docstring for what auto-opens on
/// launch instead). Exists so there's always a plain-language explanation
/// available without digging through the website, even though it's no
/// longer the thing shown automatically at startup.
///
/// Same app-drawn chrome as LiveTrackingForm (see DarkForm) rather than the
/// system title bar, so the one window whose entire job is to reassure a
/// user this isn't sketchy background software doesn't itself look like an
/// unstyled system dialog.
///
/// Laid out at a deliberately generous size: this is three paragraphs of
/// prose someone is expected to actually read, and squeezing it into a
/// 360px column at the default 9pt made it look like a system error dialog
/// - the opposite of reassuring. The measurements below are the reason
/// it's built by hand rather than with a FlowLayoutPanel, which can't give
/// per-paragraph spacing without a wrapper control per paragraph.
/// </summary>
internal class AboutForm : DarkForm
{
    private const int Pad = 28;
    private const int BodyWidth = 430;
    private const int IconSize = 44;
    private const int ParagraphGap = 14;

    private static readonly string[] Paragraphs =
    {
        "This app tracks time in the desktop applications you've added to your " +
        "Media List on the website - the same way the browser extension tracks " +
        "browser tabs, just for apps outside your browser.",

        "Your Media List, Plan, limits, and usage are all managed on the website, " +
        "same as always - this app's Live Tracking window just shows what's " +
        "currently being counted.",

        "Look for the Social Break icon in your system tray (you may need to click " +
        "the small arrow to show hidden icons) to pause tracking, check status, " +
        "toggle Start with Windows, or log out.",
    };

    public AboutForm() : base("About Social Break", showMinimize: false)
    {
        var icon = new Panel
        {
            Location = new Point(Pad, Pad),
            Size = new Size(IconSize, IconSize),
            BackColor = Color.Transparent,
        };
        icon.Paint += (_, e) => Theme.DrawAppIcon(e.Graphics, new Rectangle(0, 0, IconSize, IconSize));

        int textLeft = Pad + IconSize + 16;
        var heading = new Label
        {
            Text = "Social Break is running",
            ForeColor = Theme.TextPrimary,
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(textLeft, Pad + 2),
            BackColor = Color.Transparent,
        };
        var subheading = new Label
        {
            Text = "Tracking desktop apps in the background",
            ForeColor = Theme.TextMuted,
            Font = new Font("Segoe UI", 9),
            AutoSize = true,
            Location = new Point(textLeft, Pad + 29),
            BackColor = Color.Transparent,
        };

        Content.Controls.Add(icon);
        Content.Controls.Add(heading);
        Content.Controls.Add(subheading);

        // Each paragraph is its own label so the gaps between them are real
        // spacing rather than blank lines at the body font's line height,
        // which is what made the old single-label version feel dense.
        int y = Pad + IconSize + 22;
        var bodyFont = new Font("Segoe UI", 9.75f);
        foreach (var text in Paragraphs)
        {
            var paragraph = new Label
            {
                Text = text,
                ForeColor = Theme.TextSecondary,
                Font = bodyFont,
                AutoSize = true,
                // Width-only cap (0 height = unbounded): a fixed pixel height
                // silently clips wrapped text on any machine whose rendered
                // font needs more vertical space than guessed here. Same
                // reason DisclosureForm does it.
                MaximumSize = new Size(BodyWidth, 0),
                Location = new Point(Pad, y),
                BackColor = Color.Transparent,
            };
            Content.Controls.Add(paragraph);
            y += paragraph.PreferredHeight + ParagraphGap;
        }

        var divider = new Panel
        {
            Location = new Point(Pad, y + 8),
            Size = new Size(BodyWidth, 1),
            BackColor = Theme.Border,
        };
        Content.Controls.Add(divider);

        var okButton = new Button
        {
            Text = "Got it",
            Location = new Point(Pad, y + 26),
            Size = new Size(BodyWidth, 40),
            BackColor = Theme.ButtonGreen,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Cursor = Cursors.Hand,
        };
        okButton.FlatAppearance.BorderSize = 0;
        okButton.FlatAppearance.MouseOverBackColor = Theme.ButtonGreenHover;
        okButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };
        Content.Controls.Add(okButton);

        SetContentSize(BodyWidth + Pad * 2, okButton.Bottom + Pad);
        AcceptButton = okButton;
    }
}
