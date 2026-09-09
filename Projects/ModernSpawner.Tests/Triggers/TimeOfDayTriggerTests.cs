using System;
using Server.Engines.ModernSpawner.Triggers;
using Xunit;

namespace Server.Engines.ModernSpawner.Tests.Triggers;

public class TimeOfDayTriggerTests
{
    [Fact]
    public void Parse_DefaultValues_WhenOnlyType()
    {
        var trigger = TimeOfDayTrigger.Parse("timeofday");

        Assert.NotNull(trigger);
        Assert.Equal("timeofday", trigger.TriggerType);
        Assert.Equal(0, trigger.StartHour);
        Assert.Equal(23, trigger.EndHour);
        Assert.False(trigger.NightOnly);
        Assert.False(trigger.DayOnly);
        Assert.Equal(TimeSpan.FromMinutes(1), trigger.Cooldown);
    }

    [Fact]
    public void Parse_HoursClampedInto_0_23()
    {
        var trigger = TimeOfDayTrigger.Parse("timeofday:-5:30");

        Assert.Equal(0, trigger.StartHour);
        Assert.Equal(23, trigger.EndHour);
    }

    [Fact]
    public void Parse_NightOnlyAndDayOnlyFlags()
    {
        var nightTrigger = TimeOfDayTrigger.Parse("timeofday:0:23:true:false:60");
        var dayTrigger = TimeOfDayTrigger.Parse("timeofday:0:23:false:true:60");

        Assert.True(nightTrigger.NightOnly);
        Assert.False(nightTrigger.DayOnly);

        Assert.False(dayTrigger.NightOnly);
        Assert.True(dayTrigger.DayOnly);
    }

    [Fact]
    public void Parse_AllParameters_RoundTrip()
    {
        var original = new TimeOfDayTrigger(8, 17)
        {
            NightOnly = false,
            DayOnly = true,
            Cooldown = TimeSpan.FromMinutes(2)
        };

        var parsed = TimeOfDayTrigger.Parse(original.Serialize());

        Assert.NotNull(parsed);
        Assert.Equal(original.StartHour, parsed.StartHour);
        Assert.Equal(original.EndHour, parsed.EndHour);
        Assert.Equal(original.NightOnly, parsed.NightOnly);
        Assert.Equal(original.DayOnly, parsed.DayOnly);
        Assert.Equal(original.Cooldown, parsed.Cooldown);
    }

    [Fact]
    public void Constructor_ClampsHours()
    {
        var trigger = new TimeOfDayTrigger(startHour: 99, endHour: -1);

        Assert.Equal(23, trigger.StartHour);
        Assert.Equal(0, trigger.EndHour);
    }
}
