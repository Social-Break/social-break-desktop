using static SocialBreakTray.Tr;
using SocialBreakTray.Ui;

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
        Text = T("Social Break Desktop - Before You Continue", "Social Break για υπολογιστή - Πριν συνεχίσεις");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        // Keeps the system title bar (this is a short-lived
        // pre-login dialog, not a main window), but recolors it -
        // Windows draws that bar light regardless of the client
        // area behind it unless asked otherwise. See Theme.
        Theme.ApplySystemDarkTitleBar(this);

        var heading = new Label
        {
            Text = T("What this app does", "Τι κάνει αυτή η εφαρμογή"),
            ForeColor = Theme.TextPrimary,
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
            Text = T(
                "Social Break Desktop tracks time in native applications - the same way the browser " +
                "extension tracks browser tabs, just for apps outside your browser.\r\n\r\n" +
                "It reads only the name of whichever application currently has focus, to check it against " +
                "the desktop-app entries on your Media List (managed on the website). It does not read " +
                "keystrokes, screen contents, file contents, clipboard data, or anything else.\r\n\r\n" +
                "To suggest apps to add on the website, it also counts which programs you use most and " +
                "sends the names of your top ten, with their minutes. You can turn that off from the tray " +
                "icon (\"Suggest my most-used apps\").\r\n\r\n" +
                "It stays visible as an icon in your system tray at all times, pauses automatically while " +
                "you're away from your computer, and your login is encrypted on this device and never " +
                "leaves it in plain text.\r\n\r\n" +
                "You can pause tracking, log out, or quit entirely at any time from the tray icon.",
                "Το Social Break για υπολογιστή μετράει τον χρόνο σε εφαρμογές - όπως η επέκταση " +
                "μετράει τις καρτέλες του browser, απλώς για εφαρμογές έξω από τον browser.\r\n\r\n" +
                "Διαβάζει μόνο το όνομα της εφαρμογής που είναι μπροστά σου, για να το συγκρίνει με τις " +
                "εφαρμογές υπολογιστή της λίστας σου (που ρυθμίζεις στον ιστότοπο). Δεν διαβάζει " +
                "πλήκτρα, ό,τι δείχνει η οθόνη, αρχεία, το πρόχειρο ή οτιδήποτε άλλο.\r\n\r\n" +
                "Για να σου προτείνει εφαρμογές στον ιστότοπο, μετράει επίσης ποια προγράμματα " +
                "χρησιμοποιείς περισσότερο και στέλνει τα ονόματα των δέκα πρώτων, με τα λεπτά τους. " +
                "Μπορείς να το κλείσεις από το εικονίδιο («Πρότεινε τις εφαρμογές που χρησιμοποιώ»).\r\n\r\n" +
                "Φαίνεται πάντα ως εικονίδιο στη γραμμή εργασιών, σταματάει μόνο του όταν λείπεις " +
                "από τον υπολογιστή, και η σύνδεσή σου είναι κρυπτογραφημένη σε αυτή τη συσκευή και " +
                "δεν φεύγει ποτέ από αυτήν ως απλό κείμενο.\r\n\r\n" +
                "Μπορείς να σταματήσεις τη μέτρηση, να αποσυνδεθείς ή να το κλείσεις όποτε θες από το εικονίδιο."),
            ForeColor = Theme.TextSecondary,
            AutoSize = true,
            MaximumSize = new Size(400, 0),
        };

        var continueButton = new Button
        {
            Text = T("I Understand, Continue", "Κατάλαβα, συνέχεια"),
            AutoSize = false,
            Size = new Size(400, 34),
            BackColor = Theme.ButtonGreen,
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
