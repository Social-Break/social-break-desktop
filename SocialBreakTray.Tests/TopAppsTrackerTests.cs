using SocialBreakTray.Tracking;
using Xunit;

namespace SocialBreakTray.Tests;

public class TopAppsTrackerTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"topapps-{Guid.NewGuid()}.json");

    [Fact]
    public void Most_used_first_and_only_a_minute_or_more()
    {
        var tracker = new TopAppsTracker(TempPath(), () => new DateTime(2026, 10, 21));
        tracker.Add("discord.exe", null, 600);
        tracker.Add("steam.exe", null, 1200);
        tracker.Add("notepad.exe", null, 30);
        var top = tracker.Top();
        Assert.Equal(new[] { "steam.exe", "discord.exe" }, top.Select(a => a.Exe));
        Assert.Equal(20, top[0].Minutes);
        Assert.Equal("Discord", top[1].Name);
    }

    [Fact]
    public void Windows_own_parts_are_not_counted()
    {
        var tracker = new TopAppsTracker(TempPath(), () => new DateTime(2026, 10, 21));
        tracker.Add("explorer.exe", null, 3600);
        tracker.Add("socialbreaktray.exe", null, 3600);
        Assert.Empty(tracker.Top());
    }

    [Fact]
    public void A_new_week_starts_over()
    {
        var now = new DateTime(2026, 10, 25);
        var path = TempPath();
        var tracker = new TopAppsTracker(path, () => now);
        tracker.Add("discord.exe", null, 600);
        now = new DateTime(2026, 10, 26);
        Assert.Empty(tracker.Top());
    }

    [Fact]
    public void Sent_again_only_when_changed()
    {
        var tracker = new TopAppsTracker(TempPath(), () => new DateTime(2026, 10, 21));
        tracker.Add("discord.exe", null, 600);
        Assert.NotNull(tracker.ChangedTop());
        Assert.Null(tracker.ChangedTop());
        tracker.Add("discord.exe", null, 60);
        Assert.NotNull(tracker.ChangedTop());
    }
}
