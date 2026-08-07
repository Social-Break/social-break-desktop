using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SocialBreakTray.Auth;

internal class StoredConfig
{
    public string? Token { get; set; }
    public bool DisclosureAcknowledged { get; set; }
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

    public static bool IsDisclosureAcknowledged() => Load().DisclosureAcknowledged;

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
