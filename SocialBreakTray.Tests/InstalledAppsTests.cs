using SocialBreakTray.Tracking;
using Xunit;

namespace SocialBreakTray.Tests;

public class InstalledAppsTests
{
    private static string Folder()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"apps-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string Exe(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public void A_shortcut_to_a_program_is_offered_by_its_own_name()
    {
        var dir = Folder();
        var app = InstalledApps.FromShortcut("Visual Studio Code", Exe(dir, "Code.exe"), "", @"C:\Windows");
        Assert.NotNull(app);
        Assert.Equal("code.exe", app!.Exe);
        Assert.Equal("Visual Studio Code", app.Name);
    }

    [Fact]
    public void Programs_that_are_not_there_are_left_out()
    {
        var dir = Folder();
        Assert.Null(InstalledApps.FromShortcut("Ghost", Path.Combine(dir, "ghost.exe"), "", @"C:\Windows"));
    }

    [Fact]
    public void Uninstallers_help_files_and_windows_are_left_out()
    {
        var dir = Folder();
        Assert.Null(InstalledApps.FromShortcut("Uninstall Steam", Exe(dir, "uninstall.exe"), "", @"C:\Windows"));
        Assert.Null(InstalledApps.FromShortcut("Steam", Exe(dir, "unins000.exe"), "", @"C:\Windows"));
        Assert.Null(InstalledApps.FromShortcut("Read me", Path.Combine(dir, "readme.txt"), "", @"C:\Windows"));
        Assert.Null(InstalledApps.FromShortcut("Notepad", Exe(dir, "notepad.exe"), "", dir));
    }

    [Fact]
    public void A_program_started_through_update_exe_is_offered_as_itself()
    {
        var dir = Folder();
        var update = Exe(dir, "Update.exe");
        var real = Exe(dir, Path.Combine("app-1.0.9", "Discord.exe"));
        var app = InstalledApps.FromShortcut("Discord", update, "--processStart Discord.exe", @"C:\Windows");
        Assert.NotNull(app);
        Assert.Equal("discord.exe", app!.Exe);
        Assert.Equal(real, app.Path, ignoreCase: true);
        Assert.Null(InstalledApps.FromShortcut("Something", update, "", @"C:\Windows"));
    }
}
