using System;
using Server.Engines.Events;
using Server.Engines.ModernSpawner.Triggers;
using Xunit;

namespace Server.Engines.ModernSpawner.Tests.Triggers;

public class WallTimeWindowTriggerTests
{
    [Fact]
    public void Parse_DefaultValues()
    {
        var trigger = WallTimeWindowTrigger.Parse("wall_time_window");

        Assert.NotNull(trigger);
        Assert.Equal("wall_time_window", trigger.TriggerType);
        Assert.Equal(new TimeOnly(0, 0), trigger.StartTime);
        Assert.Equal(new TimeOnly(23, 59, 59), trigger.EndTime);
        Assert.Equal(AllowedDays.All, trigger.AllowedDays);
        Assert.Equal(AllowedMonths.All, trigger.AllowedMonths);
        Assert.Equal(TimeZoneInfo.Utc, trigger.TimeZone);
    }

    [Fact]
    public void Parse_Serialize_RoundTrip()
    {
        var original = new WallTimeWindowTrigger(new TimeOnly(22, 0), new TimeOnly(6, 0))
        {
            AllowedDays = AllowedDays.Friday | AllowedDays.Saturday,
            AllowedMonths = AllowedMonths.October,
            TimeZone = TimeZoneInfo.Utc
        };

        var parsed = WallTimeWindowTrigger.Parse(original.Serialize());

        Assert.NotNull(parsed);
        Assert.Equal(original.StartTime, parsed.StartTime);
        Assert.Equal(original.EndTime, parsed.EndTime);
        Assert.Equal(original.AllowedDays, parsed.AllowedDays);
        Assert.Equal(original.AllowedMonths, parsed.AllowedMonths);
        Assert.Equal(original.TimeZone, parsed.TimeZone);
    }

    [Fact]
    public void Parse_HoursAndMinutes_ClampedIntoLegalRanges()
    {
        var trigger = WallTimeWindowTrigger.Parse("wall_time_window:99:99:-5:80");

        Assert.Equal(new TimeOnly(23, 59), trigger.StartTime);
        Assert.Equal(new TimeOnly(0, 59), trigger.EndTime);
    }

    [Fact]
    public void Parse_UnknownTimeZoneId_FallsBackToUtc()
    {
        var trigger = WallTimeWindowTrigger.Parse("wall_time_window:8:0:17:0:0:0:Mars/Standard_Time");

        Assert.Equal(TimeZoneInfo.Utc, trigger.TimeZone);
    }

    [Fact]
    public void WeekendEveningsFactory_SetsFridaySaturdaySunday()
    {
        var trigger = WallTimeWindowTrigger.WeekendEvenings(new TimeOnly(19, 0), new TimeOnly(23, 0));

        Assert.Equal(new TimeOnly(19, 0), trigger.StartTime);
        Assert.Equal(new TimeOnly(23, 0), trigger.EndTime);
        Assert.Equal(
            AllowedDays.Friday | AllowedDays.Saturday | AllowedDays.Sunday,
            trigger.AllowedDays);
    }

    [Fact]
    public void SeasonalFactory_AppliesMonthsMask()
    {
        var trigger = WallTimeWindowTrigger.Seasonal(
            AllowedMonths.June | AllowedMonths.July | AllowedMonths.August,
            new TimeOnly(0, 0),
            new TimeOnly(23, 59));

        Assert.Equal(
            AllowedMonths.June | AllowedMonths.July | AllowedMonths.August,
            trigger.AllowedMonths);
    }

    [Fact]
    public void HalloweenFactory_IsOctoberEvenings()
    {
        var trigger = WallTimeWindowTrigger.Halloween();

        Assert.Equal(AllowedMonths.October, trigger.AllowedMonths);
        Assert.Equal(new TimeOnly(18, 0), trigger.StartTime);
        Assert.Equal(new TimeOnly(23, 59), trigger.EndTime);
    }

    [Fact]
    public void ChristmasFactory_IsDecemberAllDay()
    {
        var trigger = WallTimeWindowTrigger.Christmas();

        Assert.Equal(AllowedMonths.December, trigger.AllowedMonths);
        Assert.Equal(new TimeOnly(0, 0), trigger.StartTime);
        Assert.Equal(new TimeOnly(23, 59), trigger.EndTime);
    }

    [Fact]
    public void IsWindowOpen_FalseWhenUnactivated()
    {
        var trigger = new WallTimeWindowTrigger(new TimeOnly(0, 0), new TimeOnly(23, 59));

        Assert.False(trigger.IsWindowOpen);
    }
}
