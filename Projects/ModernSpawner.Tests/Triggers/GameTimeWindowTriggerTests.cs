using Server.Engines.ModernSpawner.Triggers;
using Xunit;

namespace Server.Engines.ModernSpawner.Tests.Triggers;

public class GameTimeWindowTriggerTests
{
    [Fact]
    public void Parse_DefaultValues()
    {
        var trigger = GameTimeWindowTrigger.Parse("game_time_window");

        Assert.NotNull(trigger);
        Assert.Equal("game_time_window", trigger.TriggerType);
        Assert.Equal(0, trigger.StartHour);
        Assert.Equal(23, trigger.EndHour);
        Assert.False(trigger.NightOnly);
        Assert.False(trigger.DayOnly);
    }

    [Fact]
    public void Parse_Serialize_RoundTrip()
    {
        var original = new GameTimeWindowTrigger(21, 5)
        {
            NightOnly = true
        };

        var parsed = GameTimeWindowTrigger.Parse(original.Serialize());

        Assert.Equal(original.StartHour, parsed.StartHour);
        Assert.Equal(original.EndHour, parsed.EndHour);
        Assert.Equal(original.NightOnly, parsed.NightOnly);
        Assert.Equal(original.DayOnly, parsed.DayOnly);
    }

    [Fact]
    public void NightOnlyFactory_SetsNightOnlyFlag()
    {
        var trigger = GameTimeWindowTrigger.NightOnlyTrigger();

        Assert.True(trigger.NightOnly);
        Assert.False(trigger.DayOnly);
    }

    [Fact]
    public void DayOnlyFactory_SetsDayOnlyFlag()
    {
        var trigger = GameTimeWindowTrigger.DayOnlyTrigger();

        Assert.False(trigger.NightOnly);
        Assert.True(trigger.DayOnly);
    }

    [Fact]
    public void Constructor_ClampsHours()
    {
        var trigger = new GameTimeWindowTrigger(startHour: 99, endHour: -1);

        Assert.Equal(23, trigger.StartHour);
        Assert.Equal(0, trigger.EndHour);
    }

    // IsHourInWindow pure-logic tests

    [Theory]
    [InlineData(8, 8, 17, true)]   // Exactly at start is in-window
    [InlineData(12, 8, 17, true)]  // Mid-range is in-window
    [InlineData(16, 8, 17, true)]  // Last hour before end is in-window
    [InlineData(17, 8, 17, false)] // Exactly at end is NOT in-window (half-open)
    [InlineData(7, 8, 17, false)]  // Before start
    [InlineData(20, 8, 17, false)] // After end
    public void IsHourInWindow_NormalRange(int currentHour, int start, int end, bool expected)
    {
        Assert.Equal(expected, GameTimeWindowTrigger.IsHourInWindow(currentHour, start, end));
    }

    [Theory]
    [InlineData(22, 22, 4, true)]   // At start of overnight window
    [InlineData(23, 22, 4, true)]   // Late evening
    [InlineData(0, 22, 4, true)]    // Midnight
    [InlineData(3, 22, 4, true)]    // Early morning
    [InlineData(4, 22, 4, false)]   // Exactly at end of overnight window
    [InlineData(5, 22, 4, false)]   // After overnight window
    [InlineData(12, 22, 4, false)]  // Middle of day, outside overnight window
    [InlineData(21, 22, 4, false)]  // Just before overnight window opens
    public void IsHourInWindow_OvernightRange(int currentHour, int start, int end, bool expected)
    {
        Assert.Equal(expected, GameTimeWindowTrigger.IsHourInWindow(currentHour, start, end));
    }

    // CalculateHoursUntil pure-logic tests

    [Theory]
    [InlineData(8, 17, 9)]   // Same day, forward
    [InlineData(23, 5, 6)]   // Wraps past midnight
    [InlineData(0, 23, 23)]  // Edge: start of day to end
    [InlineData(12, 12, 24)] // Same hour -> full day (we always schedule forward)
    public void CalculateHoursUntil(int currentHour, int targetHour, int expected)
    {
        Assert.Equal(expected, GameTimeWindowTrigger.CalculateHoursUntil(currentHour, targetHour));
    }
}
