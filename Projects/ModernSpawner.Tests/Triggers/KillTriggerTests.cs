using System;
using Server.Engines.ModernSpawner.Triggers;
using Xunit;

namespace Server.Engines.ModernSpawner.Tests.Triggers;

public class KillTriggerTests
{
    [Fact]
    public void Parse_DefaultValues_WhenOnlyType()
    {
        var trigger = KillTrigger.Parse("kill");

        Assert.NotNull(trigger);
        Assert.Equal("kill", trigger.TriggerType);
        Assert.Equal(1, trigger.RequiredKills);
        Assert.False(trigger.RequireAllDead);
        Assert.True(trigger.ResetOnTrigger);
        Assert.Null(trigger.FilterType);
        Assert.False(trigger.RequirePlayerKiller);
        Assert.Equal(TimeSpan.FromSeconds(5), trigger.Cooldown);
    }

    [Fact]
    public void Parse_RequiredKillsAndFlags()
    {
        var trigger = KillTrigger.Parse("kill:3:true:false:Dragon:true:30");

        Assert.NotNull(trigger);
        Assert.Equal(3, trigger.RequiredKills);
        Assert.True(trigger.RequireAllDead);
        Assert.False(trigger.ResetOnTrigger);
        Assert.Equal("Dragon", trigger.FilterType);
        Assert.True(trigger.RequirePlayerKiller);
        Assert.Equal(TimeSpan.FromSeconds(30), trigger.Cooldown);
    }

    [Fact]
    public void Parse_FilterAny_ProducesNullFilter()
    {
        var trigger = KillTrigger.Parse("kill:1:false:true:any:false:5");

        Assert.NotNull(trigger);
        Assert.Null(trigger.FilterType);
    }

    [Fact]
    public void Parse_RequiredKillsClampedToOne()
    {
        var trigger = KillTrigger.Parse("kill:0");

        Assert.Equal(1, trigger.RequiredKills);
    }

    [Fact]
    public void Serialize_RoundTrip_PreservesAllFields()
    {
        var original = new KillTrigger(5, requireAllDead: true)
        {
            ResetOnTrigger = false,
            FilterType = "Skeleton",
            RequirePlayerKiller = true,
            Cooldown = TimeSpan.FromSeconds(120)
        };

        var parsed = KillTrigger.Parse(original.Serialize());

        Assert.NotNull(parsed);
        Assert.Equal(original.RequiredKills, parsed.RequiredKills);
        Assert.Equal(original.RequireAllDead, parsed.RequireAllDead);
        Assert.Equal(original.ResetOnTrigger, parsed.ResetOnTrigger);
        Assert.Equal(original.FilterType, parsed.FilterType);
        Assert.Equal(original.RequirePlayerKiller, parsed.RequirePlayerKiller);
        Assert.Equal(original.Cooldown, parsed.Cooldown);
    }

    [Fact]
    public void ResetKillCount_IsCallable_WithoutActivation()
    {
        // Kill count is private state but we can at least verify the API is safe
        // to call on an unactivated trigger.
        var trigger = new KillTrigger();
        trigger.ResetKillCount();
    }

    [Fact]
    public void Constructor_NegativeRequiredKills_ClampedToOne()
    {
        var trigger = new KillTrigger(requiredKills: -5);

        Assert.Equal(1, trigger.RequiredKills);
    }
}
