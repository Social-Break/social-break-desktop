using System.Runtime.InteropServices;

namespace SocialBreakTray.Enforcement;

/// <summary>
/// Topmost fullscreen overlay shown when a tracked app hits its limit -
/// mirrors the browser extension's block.html + 5-minute snooze, adapted
/// for a native context: a browser tab can be redirected to a local page,
/// but a native app can't, so instead this shows on top of everything.
/// Deliberately does not touch the tracked app's window state (no
/// minimizing) - dismissing the overlay via Continue should return the user
/// to exactly what they were doing, not shuffle their windows around.
///
/// Deliberately not a hard block: the user always has both an "I'll keep
/// going" path (Snooze/Continue) and an explicit "close the app" path with
/// its own confirmation, rather than trapping them with no way out short of
/// Task Manager. <paramref name="isRepeatPrompt"/> switches both the copy
/// and the Continue button's meaning: the first time an app hits its limit
/// today it offers a real 5-minute snooze (another prompt later); every
/// subsequent time (after that snooze expires, or after being closed via
/// "Close Program" and reopened) it offers "Continue nonetheless" instead,
/// which stops the overlay from reappearing for that app for the rest of
/// the day - one nag is a reminder, repeating the same nag every 5 minutes
/// forever is just noise. The caller tracks the "already shown today" state
/// and what Continue should mean at that point, not this class.
/// </summary>
public class BlockForm : Form
{
    [DllImport("user32.dll")]
    private static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);
    private const uint WM_CLOSE = 0x0010;

    /// <summary>Raised when the user clicks "Snooze 5 minutes" or "Continue
    /// nonetheless" - the bool is true for the latter (permanent-for-today
    /// dismissal) and false for the former (temporary 5-minute exemption).
    /// The caller (TrayApplicationContext) owns recording whichever
    /// exemption that implies, mirroring the extension's
    /// snoozeExemptions.</summary>
    public event Action<bool>? ContinueRequested;

    /// <summary>Raised after the user confirms closing the tracked app -
    /// purely informational for the caller (e.g. logging); the actual close
    /// request is already sent by this class via WM_CLOSE, same as clicking
    /// the app's own title-bar X, so the app gets a normal chance to prompt
    /// for unsaved changes rather than being forcibly killed.</summary>
    public event Action? CloseProgramRequested;

    // Mirrors block.html's REASON_MESSAGES in the extension - keep both in
    // sync if a new LimitEvaluator.BlockReason code is ever added. {0} is
    // the app's display name - spelled out explicitly here since this is a
    // fullscreen native takeover, unlike a browser tab where the URL bar
    // already shows what site you're on.
    private static readonly Dictionary<string, string> ReasonSentenceFormats = new()
    {
        [LimitEvaluator.BlockReason.CompleteBreak] = "You're on a Complete Break, so {0} is fully paused.",
        [LimitEvaluator.BlockReason.BlockedDay] = "{0} is blocked today, based on your Custom Schedule.",
        [LimitEvaluator.BlockReason.TimeWindow] = "{0} is restricted right now, based on the time window you set for it.",
        [LimitEvaluator.BlockReason.DailyLimit] = "You've used up your daily time limit for {0}.",
        [LimitEvaluator.BlockReason.WeeklyLimit] = "You've used up your weekly time limit for {0}.",
    };

    public BlockForm(string appDisplayName, nint targetWindowHandle, string blockReason, bool isRepeatPrompt = false)
    {
        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Maximized;
        TopMost = true;
        BackColor = Color.FromArgb(0x11, 0x11, 0x1b);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;

        var titleLabel = new Label
        {
            Text = "Time's up.",
            ForeColor = Color.FromArgb(0xf3, 0x8b, 0xa8),
            Font = new Font("Segoe UI", 32, FontStyle.Bold),
            AutoSize = true,
        };

        var reasonFormat = ReasonSentenceFormats.GetValueOrDefault(blockReason, "You've reached your limit for {0}.");
        var reasonSentence = string.Format(reasonFormat, appDisplayName);
        var bodyText = isRepeatPrompt
            ? $"{reasonSentence} You can keep going, or close it now."
            : $"{reasonSentence} Take a moment before switching back.";
        var bodyLabel = new Label
        {
            Text = bodyText,
            ForeColor = Color.FromArgb(0xa6, 0xad, 0xc8),
            Font = new Font("Segoe UI", 14),
            AutoSize = true,
            MaximumSize = new Size(600, 0),
        };

        var continueButton = new Button
        {
            Text = isRepeatPrompt ? "Continue nonetheless" : "Snooze 5 minutes",
            AutoSize = true,
            Padding = new Padding(20, 10, 20, 10),
            BackColor = Color.FromArgb(0x31, 0x32, 0x44),
            ForeColor = Color.FromArgb(0xcd, 0xd6, 0xf4),
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 0, 12, 0),
        };
        continueButton.FlatAppearance.BorderColor = Color.FromArgb(0x45, 0x47, 0x5a);
        continueButton.Click += (_, _) =>
        {
            ContinueRequested?.Invoke(isRepeatPrompt);
            Close();
        };

        var closeButton = new Button
        {
            Text = "Close Program",
            AutoSize = true,
            Padding = new Padding(20, 10, 20, 10),
            BackColor = Color.FromArgb(0x2a, 0x1b, 0x22),
            ForeColor = Color.FromArgb(0xf3, 0x8b, 0xa8),
            FlatStyle = FlatStyle.Flat,
        };
        closeButton.FlatAppearance.BorderColor = Color.FromArgb(0x5a, 0x2f, 0x3a);
        closeButton.Click += (_, _) =>
        {
            var confirm = MessageBox.Show(
                this,
                $"This will close {appDisplayName}. Continue?",
                "Close Program",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes) return;

            if (targetWindowHandle != nint.Zero)
            {
                // Posted, not sent - same as clicking the app's own title-bar
                // X, so it gets a normal (non-blocking) chance to prompt for
                // unsaved changes rather than being forcibly killed.
                PostMessage(targetWindowHandle, WM_CLOSE, nint.Zero, nint.Zero);
            }
            CloseProgramRequested?.Invoke();
            Close();
        };

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Anchor = AnchorStyles.None,
        };
        buttonPanel.Controls.Add(continueButton);
        buttonPanel.Controls.Add(closeButton);

        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            Anchor = AnchorStyles.None,
        };
        panel.Controls.Add(titleLabel);
        panel.Controls.Add(bodyLabel);
        panel.SetFlowBreak(bodyLabel, true);
        panel.Controls.Add(buttonPanel);

        Controls.Add(panel);
        Load += (_, _) =>
        {
            panel.Location = new Point((ClientSize.Width - panel.Width) / 2, (ClientSize.Height - panel.Height) / 2);
        };
    }
}
