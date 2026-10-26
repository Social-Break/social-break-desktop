using SocialBreakTray.Enforcement;
using Xunit;

namespace SocialBreakTray.Tests;

/// <summary>Three snoozes a day across all apps, as in the extension.</summary>
public class SnoozeTests
{
    private static BlockStateStore Store() =>
        new(resetHour: 0, statePath: Path.Combine(Path.GetTempPath(), $"sb-block-{Guid.NewGuid():N}.json"));

    [Fact]
    public void Three_snoozes_then_none()
    {
        var store = Store();
        Assert.True(store.Snooze("discord.exe", TimeSpan.FromMinutes(5)));
        Assert.True(store.Snooze("vivaldi.exe", TimeSpan.FromMinutes(5)));
        Assert.True(store.HasSnoozeLeft);
        Assert.True(store.Snooze("discord.exe", TimeSpan.FromMinutes(5)));
        Assert.False(store.HasSnoozeLeft);
        Assert.False(store.Snooze("steam.exe", TimeSpan.FromMinutes(5)));
        Assert.False(store.IsSnoozed("steam.exe"));
    }

    [Fact]
    public void A_snooze_lets_the_app_through_for_its_minutes()
    {
        var store = Store();
        store.Snooze("discord.exe", TimeSpan.FromMinutes(5));
        Assert.True(store.IsSnoozed("discord.exe"));
        Assert.False(store.IsSnoozed("vivaldi.exe"));
    }
}
