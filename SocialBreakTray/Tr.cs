using System.Globalization;

namespace SocialBreakTray;

/// <summary>
/// The app's words in Greek on a Windows set to Greek, English otherwise -
/// the same two languages as the website, the phone and the extension.
/// Each sentence is written out in both, where it's used.
/// </summary>
internal static class Tr
{
    public static readonly bool Greek = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "el";

    public static string T(string en, string el) => Greek ? el : en;

    /// <summary>Hours, minutes and seconds as the app writes them: 1h 5m, in Greek 1ω 5λ.</summary>
    public static string H => Greek ? "ω" : "h";
    public static string M => Greek ? "λ" : "m";
    public static string S => Greek ? "δ" : "s";
}
