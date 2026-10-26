using static SocialBreakTray.Tr;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using SocialBreakTray.Ui;

namespace SocialBreakTray.Enforcement;

/// <summary>
/// The full-screen page shown on top of an app whose time is up - the desktop
/// twin of the browser extension's block page: the app's name, "Take a
/// breath.", why, the goal the person said comes first, one button that
/// closes the app, and - while today still has one - a quiet snooze.
///
/// A native app can't be redirected the way a tab can, so this covers it
/// instead, on top of everything. Closing posts WM_CLOSE, exactly like the
/// app's own X, so it still gets to ask about unsaved work.
///
/// No "continue for today": once an app's time is up it stays up. The only
/// way round it is a snooze - three a day across all apps, as in the
/// extension, and none in a lock or strict mode. How many are left is never
/// shown.
/// </summary>
public class BlockForm : Form
{
    [DllImport("user32.dll")]
    private static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);
    private const uint WM_CLOSE = 0x0010;

    private const int Column = 640;
    private static readonly Color Ink = Color.FromArgb(0xf2, 0xf4, 0xf8);
    private static readonly Color Soft = Color.FromArgb(0xa6, 0xad, 0xc8);
    private static readonly Color Faint = Color.FromArgb(0x6c, 0x70, 0x86);
    private static readonly Color Line = Color.FromArgb(0x2c, 0x2f, 0x42);

    /// <summary>Raised when the person snoozes; the caller spends one of
    /// today's snoozes on this app (BlockStateStore.Snooze).</summary>
    public event Action? SnoozeRequested;

    /// <summary>Raised after the app has been asked to close.</summary>
    public event Action? CloseProgramRequested;

    // The same sentences as the extension's block page (block.js) - keep the
    // two in step if a LimitEvaluator.BlockReason is ever added.
    private static readonly Dictionary<string, string> ReasonFormats = new()
    {
        [LimitEvaluator.BlockReason.CompleteBreak] = T("{0} is blocked.", "Το {0} είναι μπλοκαρισμένο."),
        [LimitEvaluator.BlockReason.BlockedDay] = T("{0} is blocked today.", "Το {0} είναι μπλοκαρισμένο σήμερα."),
        [LimitEvaluator.BlockReason.TimeWindow] = T("{0} isn't allowed at this time of day.", "Το {0} δεν επιτρέπεται αυτή την ώρα."),
        [LimitEvaluator.BlockReason.DailyLimit] = T("You've used up today's time on {0}.", "Χρησιμοποίησες όλο τον χρόνο της μέρας για το {0}."),
        [LimitEvaluator.BlockReason.WeeklyLimit] = T("You've used up this week's time on {0}.", "Χρησιμοποίησες όλο τον χρόνο της εβδομάδας για το {0}."),
    };

    public BlockForm(string appName, nint targetWindow, string blockReason, bool canSnooze, string? bigGoal = null, DateTimeOffset? lockedUntil = null)
    {
        bool locked = blockReason == LimitEvaluator.BlockReason.Locked;
        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Maximized;
        TopMost = true;
        BackColor = Color.FromArgb(0x11, 0x11, 0x1b);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        DoubleBuffered = true;

        var controls = new List<Control>();

        controls.Add(Centered(appName.ToUpperInvariant(), new Font("Segoe UI", 10f, FontStyle.Bold), Faint, spacing: 10));
        controls.Add(Centered(locked ? T("Locked.", "Κλειδωμένο.") : T("Take a breath.", "Πάρε μια ανάσα."),
            new Font("Segoe UI Light", 40f), Ink, spacing: 14));

        var reason = locked && lockedUntil is { } until
            ? T($"You locked your apps until {until.ToLocalTime():HH:mm}.", $"Κλείδωσες τις εφαρμογές σου μέχρι τις {until.ToLocalTime():HH:mm}.")
            : string.Format(ReasonFormats.GetValueOrDefault(blockReason, T("You've reached your limit for {0}.", "Έφτασες το όριο για το {0}.")), appName);
        controls.Add(Centered(reason, new Font("Segoe UI", 13f), Soft, spacing: 34));

        if (!string.IsNullOrWhiteSpace(bigGoal))
        {
            controls.Add(Rule(spacing: 30));
            controls.Add(Centered(T("YOU SAID THIS COMES FIRST", "ΕΙΠΕΣ ΟΤΙ ΑΥΤΟ ΕΡΧΕΤΑΙ ΠΡΩΤΟ"), new Font("Segoe UI", 9f, FontStyle.Bold), Faint, spacing: 10));
            controls.Add(Centered(bigGoal, new Font("Segoe UI Semibold", 22f), Ink, spacing: 30));
            controls.Add(Rule(spacing: 40));
        }

        var close = new RoundedButton
        {
            Text = T($"Close {appName}", $"Κλείσε το {appName}"),
            Size = new Size(300, 54),
            Radius = 27,
            BackColor = Theme.AccentGreen,
            HoverColor = Color.FromArgb(0xb8, 0xec, 0xb4),
            ForeColor = Color.FromArgb(0x11, 0x11, 0x1b),
            Font = new Font("Segoe UI Semibold", 12f),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 0, 18),
        };
        close.Click += (_, _) =>
        {
            if (targetWindow != nint.Zero) PostMessage(targetWindow, WM_CLOSE, nint.Zero, nint.Zero);
            CloseProgramRequested?.Invoke();
            Close();
        };
        controls.Add(close);

        if (canSnooze && !locked)
        {
            var snooze = new LinkLabel
            {
                Text = T("Snooze for 5 minutes", "Αναβολή 5 λεπτών"),
                LinkColor = Faint,
                ActiveLinkColor = Soft,
                VisitedLinkColor = Faint,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(Column, 24),
                BackColor = Color.Transparent,
            };
            snooze.LinkClicked += (_, _) =>
            {
                SnoozeRequested?.Invoke();
                Close();
            };
            controls.Add(snooze);
        }

        foreach (var control in controls) Controls.Add(control);
        AcceptButton = close;
        Resize += (_, _) => LayOut(controls);
        Load += (_, _) => LayOut(controls);
    }

    /// <summary>One column down the middle of the screen, vertically centred.</summary>
    private void LayOut(List<Control> controls)
    {
        int height = controls.Sum(c => c.Height + c.Margin.Bottom);
        int y = Math.Max(24, (ClientSize.Height - height) / 2);
        foreach (var control in controls)
        {
            control.Location = new Point((ClientSize.Width - control.Width) / 2, y);
            y += control.Height + control.Margin.Bottom;
        }
    }

    private static Label Centered(string text, Font font, Color colour, int spacing)
    {
        var size = TextRenderer.MeasureText(text, font, new Size(Column, 0), TextFormatFlags.WordBreak);
        return new Label
        {
            Text = text,
            Font = font,
            ForeColor = colour,
            BackColor = Color.Transparent,
            AutoSize = false,
            TextAlign = ContentAlignment.TopCenter,
            Size = new Size(Column, size.Height + 4),
            Margin = new Padding(0, 0, 0, spacing),
        };
    }

    private static Panel Rule(int spacing) => new()
    {
        Size = new Size(Column - 160, 1),
        BackColor = Line,
        Margin = new Padding(0, 0, 0, spacing),
    };

    /// <summary>A soft glow from the top, as on the extension's page, rather
    /// than a flat black screen.</summary>
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        var glow = new Rectangle(ClientSize.Width / 2 - 700, -500, 1400, 1100);
        using var path = new GraphicsPath();
        path.AddEllipse(glow);
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(0x26, 0x2a, 0x40),
            SurroundColors = new[] { BackColor },
        };
        e.Graphics.FillPath(brush, path);
    }
}
