using Server.Engines.ModernSpawner.Triggers;
using Xunit;

namespace Server.Engines.ModernSpawner.Tests.Triggers;

/// <summary>
/// <c>timeofday</c> is retired as a trigger class and survives only as a factory alias: saved worlds,
/// exports and XmlSpawner imports that still carry the old definition text parse into a
/// <see cref="GameTimeWindowTrigger" />. The legacy end hour was inclusive, so it maps to the window
/// grammar's exclusive end (<c>timeofday:8:17</c> covers 08:00-17:59, i.e. <c>[8, 18)</c>).
/// </summary>
public class TimeOfDayAliasTests
{
    private static GameTimeWindowTrigger Parse(string definition) =>
        Assert.IsType<GameTimeWindowTrigger>(TriggerSystem.Instance.ParseTrigger(definition));

    [Theory]
    [InlineData("timeofday:8:17", 8, 18)]
    [InlineData("timeofday:20:6", 20, 7)]
    [InlineData("timeofday:0:23", 0, 24)]
    public void LegacyDefinition_ParsesToAGameTimeWindow_WithAnExclusiveEnd(string definition, int start, int end)
    {
        var trigger = Parse(definition);

        Assert.Equal(start, trigger.StartHour);
        Assert.Equal(end, trigger.EndHour);
        Assert.Equal("game_time_window", trigger.TriggerType);
        Assert.Equal(TriggerKind.Gate, trigger.Kind);
    }

    [Theory]
    // The legacy inclusive test (hours >= start && hours <= end) and the window's half-open test must
    // agree on every hour of the day.
    [InlineData(8, 17)]
    [InlineData(20, 6)]
    [InlineData(0, 23)]
    [InlineData(12, 12)]
    // end == start - 1 (mod 24) wrapped all the way round in the legacy grammar and meant every hour;
    // mapping it to [start, start) would have produced the empty window, the exact opposite.
    [InlineData(10, 9)]
    [InlineData(23, 22)]
    [InlineData(1, 0)]
    public void EveryHour_MatchesTheLegacyInclusiveTest(int legacyStart, int legacyEnd)
    {
        var trigger = Parse($"timeofday:{legacyStart}:{legacyEnd}");

        for (var hour = 0; hour < 24; hour++)
        {
            var legacy = legacyEnd >= legacyStart
                ? hour >= legacyStart && hour <= legacyEnd
                : hour >= legacyStart || hour <= legacyEnd;

            Assert.Equal(legacy, GameTimeWindowTrigger.IsHourInWindow(hour, trigger.StartHour, trigger.EndHour));
        }
    }

    [Fact]
    public void LegacyDefaults_CoverTheWholeDay()
    {
        var trigger = Parse("timeofday");

        Assert.Equal(0, trigger.StartHour);
        Assert.Equal(24, trigger.EndHour);

        for (var hour = 0; hour < 24; hour++)
        {
            Assert.True(GameTimeWindowTrigger.IsHourInWindow(hour, trigger.StartHour, trigger.EndHour));
        }
    }

    [Fact]
    public void LegacyHours_AreClampedBeforeTheyAreConverted()
    {
        var trigger = Parse("timeofday:-5:30");

        Assert.Equal(0, trigger.StartHour);
        Assert.Equal(24, trigger.EndHour);
    }

    [Fact]
    public void LegacyNightAndDayFlags_Survive()
    {
        var night = Parse("timeofday:0:23:true:false:60");
        var day = Parse("timeofday:0:23:false:true:60");

        Assert.True(night.NightOnly);
        Assert.False(night.DayOnly);

        Assert.False(day.NightOnly);
        Assert.True(day.DayOnly);
    }
}
