using static SocialBreakTray.Tr;
using System.Runtime.InteropServices;

namespace SocialBreakTray.Enforcement;

/// <summary>
/// The full-screen page shown on top of an app whose time is up: a title,
/// one sentence, and the buttons beside that sentence. Kept plain on purpose.
///
/// A native app can't be redirected the way a tab can, so this covers it
/// instead, on top of everything. Close posts WM_CLOSE, exactly like the app's
/// own X, so it still gets to ask about unsaved work - and the caller then
/// holds off for a few seconds so that question can be answered
/// (TrayApplicationContext's closing grace).
///
/// No "continue for today": once an app's time is up it stays up. The only
/// way round it is a snooze - three a day across all apps, as in the
/// extension, and none in a lock or strict mode.
/// </summary>
public class BlockForm : Form
{
    [DllImport("user32.dll")]
    private static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);
    private const uint WM_CLOSE = 0x0010;

    /// <summary>Raised when the person snoozes; the caller spends one of
    /// today's snoozes on this app (BlockStateStore.Snooze).</summary>
    public event Action? SnoozeRequested;

    /// <summary>Raised once the app has been asked to close.</summary>
    public event Action? CloseProgramRequested;

    // The same sentences as the extension's block page (block.js) - keep the
    // two in step if a LimitEvaluator.BlockReason is ever added.
    private static readonly Dictionary<string, string> ReasonFormats = new()
    {
        [LimitEvaluator.BlockReason.CompleteBreak] = T("{0} is blocked.", "Το {0} είναι μπλοκαρισμένο."),
        [LimitEvaluator.BlockReason.BlockedDay] = T("{0} is blocked today.", "Το {0} είναι μπλοκαρισμένο σήμερα."),
        [LimitEvaluator.BlockReason.TimeWindow] = T("{0} isn't allowed at this time of day.", "Το {0} δεν επιτρέπεται αυτή την ώρα."),
        [LimitEvaluator.BlockReason.DailyLimit] = T("You've used up your daily time limit for {0}.", "Χρησιμοποίησες όλο τον χρόνο της μέρας για το {0}."),
        [LimitEvaluator.BlockReason.WeeklyLimit] = T("You've used up your weekly time limit for {0}.", "Χρησιμοποίησες όλο τον χρόνο της εβδομάδας για το {0}."),
    };

    public BlockForm(string appName, nint targetWindow, string blockReason, bool canSnooze, DateTimeOffset? lockedUntil = null)
    {
        bool locked = blockReason == LimitEvaluator.BlockReason.Locked;
        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Maximized;
        TopMost = true;
        BackColor = Color.FromArgb(0x11, 0x11, 0x1b);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;

        var title = new Label
        {
            Text = locked ? T("Locked.", "Κλειδωμένο.") : T("Time's up.", "Ο χρόνος τελείωσε."),
            ForeColor = Color.FromArgb(0xf3, 0x8b, 0xa8),
            Font = new Font("Segoe UI", 32, FontStyle.Bold),
            AutoSize = true,
        };

        var reason = locked && lockedUntil is { } until
            ? T($"You locked your apps until {until.ToLocalTime():HH:mm}.", $"Κλείδωσες τις εφαρμογές σου μέχρι τις {until.ToLocalTime():HH:mm}.")
            : string.Format(ReasonFormats.GetValueOrDefault(blockReason, T("You've reached your limit for {0}.", "Έφτασες το όριο για το {0}.")), appName);
        var body = new Label
        {
            Text = reason + T(" Take a moment before switching back.", " Πάρε μια στιγμή πριν γυρίσεις πίσω."),
            ForeColor = Color.FromArgb(0xa6, 0xad, 0xc8),
            Font = new Font("Segoe UI", 14),
            AutoSize = true,
            MaximumSize = new Size(600, 0),
        };

        var buttons = new List<Button>();
        if (canSnooze && !locked)
        {
            var snooze = Flat(T("Snooze 5 minutes", "Αναβολή 5 λεπτά"),
                Color.FromArgb(0x31, 0x32, 0x44), Color.FromArgb(0xcd, 0xd6, 0xf4), Color.FromArgb(0x45, 0x47, 0x5a));
            snooze.Click += (_, _) =>
            {
                SnoozeRequested?.Invoke();
                Close();
            };
            buttons.Add(snooze);
        }
        var close = Flat(T("Close Program", "Κλείσιμο προγράμματος"),
            Color.FromArgb(0x2a, 0x1b, 0x22), Color.FromArgb(0xf3, 0x8b, 0xa8), Color.FromArgb(0x5a, 0x2f, 0x3a));
        close.Click += (_, _) =>
        {
            if (targetWindow != nint.Zero) PostMessage(targetWindow, WM_CLOSE, nint.Zero, nint.Zero);
            CloseProgramRequested?.Invoke();
            Close();
        };
        buttons.Add(close);

        Controls.Add(title);
        Controls.Add(body);
        foreach (var button in buttons) Controls.Add(button);

        // Title on top; the sentence under it, with the buttons beside the
        // sentence, centred on it - all of it centred on the screen.
        void LayOut()
        {
            const int gap = 12, beside = 36;
            int buttonsWidth = buttons.Sum(b => b.Width) + gap * (buttons.Count - 1);
            int buttonHeight = buttons.Max(b => b.Height);
            int rowHeight = Math.Max(body.Height, buttonHeight);
            int width = Math.Max(title.Width, body.Width + beside + buttonsWidth);
            int height = title.Height + 6 + rowHeight;
            int left = (ClientSize.Width - width) / 2;
            // A little above the middle, where the eye lands first.
            int top = (int)((ClientSize.Height - height) * 0.38);

            title.Location = new Point(left - 4, top);
            int rowTop = top + title.Height + 6;
            body.Location = new Point(left, rowTop + (rowHeight - body.Height) / 2);
            int x = left + body.Width + beside;
            foreach (var button in buttons)
            {
                button.Location = new Point(x, rowTop + (rowHeight - button.Height) / 2);
                x += button.Width + gap;
            }
        }
        Load += (_, _) => LayOut();
        Resize += (_, _) => LayOut();
    }

    private static Button Flat(string text, Color back, Color fore, Color border)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Padding = new Padding(20, 10, 20, 10),
            BackColor = back,
            ForeColor = fore,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderColor = border;
        return button;
    }
}
