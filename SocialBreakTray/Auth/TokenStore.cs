using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SocialBreakTray.Auth;

internal class StoredConfig
{
    public string? Token { get; set; }
    public bool DisclosureAcknowledged { get; set; }
    // Which wording of the notice was agreed to: a new one (2 - suggesting
    // the most-used apps) is shown once more to everyone.
    public int DisclosureVersion { get; set; }
    public bool HideWelcomeOnStartup { get; set; }
    // The tray's "Suggest my most-used apps" turned off.
    public bool SuggestAppsOff { get; set; }
}

/// <summary>
/// Persists the API token (and the one-time disclosure-acknowledged flag)
/// encrypted at rest via Windows' Data Protection API, tied to the current
/// Windows user account - deliberately stronger than the browser
/// extension's plaintext chrome.storage.local, and called out explicitly in
/// legal.html's disclosure. DPAPI has no passphrase to manage: only the same
/// Windows user (or a process running as them) can decrypt what was
/// encrypted, which is exactly the boundary we want.
/// </summary>
public static class TokenStore
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SocialBreak");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.dat");

    public static string? LoadToken() => Load().Token;

    public const int CurrentDisclosure = 2;

    public static bool IsDisclosureAcknowledged()
    {
        var config = Load();
        return config.DisclosureAcknowledged && config.DisclosureVersion >= CurrentDisclosure;
    }

    public static bool IsSuggestingApps() => !Load().SuggestAppsOff;

    public static void SetSuggestingApps(bool on)
    {
        var config = Load();
        config.SuggestAppsOff = !on;
        Save(config);
    }

    public static void SaveToken(string token)
    {
        var config = Load();
        config.Token = token;
        Save(config);
    }

    public static void MarkDisclosureAcknowledged()
    {
        var config = Load();
        config.DisclosureAcknowledged = true;
        config.DisclosureVersion = CurrentDisclosure;
        Save(config);
    }

    public static bool IsWelcomeHiddenOnStartup() => Load().HideWelcomeOnStartup;

    /// <summary>Controls only the automatic on-startup showing of AboutForm -
    /// opening it from the tray menu always shows it regardless of this
    /// setting, since that's an explicit request, not the automatic
    /// popup.</summary>
    public static void SetHideWelcomeOnStartup(bool hide)
    {
        var config = Load();
        config.HideWelcomeOnStartup = hide;
        Save(config);
    }

    /// <summary>Removes the stored token (and only the token) - used by "Log
    /// Out". Deliberately keeps DisclosureAcknowledged set, since re-showing
    /// the "what this app does" explanation on every re-login would be more
    /// annoying than reassuring.</summary>
    public static void ClearToken()
    {
        var config = Load();
        config.Token = null;
        Save(config);
    }

    private static StoredConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return new StoredConfig();
            var encrypted = File.ReadAllBytes(ConfigPath);
            var decrypted = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<StoredConfig>(Encoding.UTF8.GetString(decrypted)) ?? new StoredConfig();
        }
        catch
        {
            // Corrupt/unreadable/undecryptable file (e.g. moved to a
            // different Windows user profile) - treat as "not logged in"
            // rather than crashing the whole app on startup.
            return new StoredConfig();
        }
    }

    private static void Save(StoredConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(config);
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(ConfigPath, encrypted);
    }
}
