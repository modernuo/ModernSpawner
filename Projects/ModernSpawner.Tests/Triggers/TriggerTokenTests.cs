using System;
using Server.Engines.ModernSpawner.Triggers;
using Xunit;

namespace Server.Engines.ModernSpawner.Tests.Triggers;

/// <summary>
/// The shared per-event-trigger tokens (<c>wake:</c>, <c>mode:</c>, <c>when:</c>) parse out of any event
/// trigger definition regardless of where they sit among the positional arguments, and survive a
/// <c>Serialize</c> round trip. Defaults stay off the wire so pre-token definitions round-trip unchanged.
/// </summary>
public class TriggerTokenTests
{
    [Fact]
    public void Proximity_TokensAfterPositionalArguments()
    {
        var trigger = ProximityTrigger.Parse("proximity:8:true:false:5:Player:wake:true:mode:tick");

        Assert.NotNull(trigger);
        Assert.Equal(8, trigger.Range);
        Assert.True(trigger.PlayersOnly);
        Assert.False(trigger.RequireLineOfSight);
        Assert.Equal(TimeSpan.FromSeconds(5), trigger.Cooldown);
        Assert.True(trigger.Wake);
        Assert.Equal(CycleMode.Tick, trigger.Mode);
        Assert.Equal(TriggerKind.Event, trigger.Kind);
    }

    [Fact]
    public void Tokens_ParseInAnyOrder()
    {
        var wakeFirst = ProximityTrigger.Parse("proximity:8:wake:true:mode:tick");
        var modeFirst = ProximityTrigger.Parse("proximity:8:mode:tick:wake:true");

        Assert.Equal(8, wakeFirst.Range);
        Assert.True(wakeFirst.Wake);
        Assert.Equal(CycleMode.Tick, wakeFirst.Mode);

        Assert.Equal(8, modeFirst.Range);
        Assert.True(modeFirst.Wake);
        Assert.Equal(CycleMode.Tick, modeFirst.Mode);
    }

    [Fact]
    public void Tokens_DoNotDisplacePositionalArguments()
    {
        // The tokens sit between positional arguments: stripping them must leave 12/false/true/30 in place.
        var trigger = ProximityTrigger.Parse("proximity:12:wake:true:false:mode:tick:true:30");

        Assert.Equal(12, trigger.Range);
        Assert.False(trigger.PlayersOnly);
        Assert.True(trigger.RequireLineOfSight);
        Assert.Equal(TimeSpan.FromSeconds(30), trigger.Cooldown);
        Assert.True(trigger.Wake);
        Assert.Equal(CycleMode.Tick, trigger.Mode);
    }

    [Fact]
    public void Tokens_RoundTripThroughSerialize()
    {
        var first = ProximityTrigger.Parse("proximity:12:false:true:30:2:wake:true:mode:tick:when:trigMob.Karma > 0");
        Assert.NotNull(first);

        var second = ProximityTrigger.Parse(first.Serialize());

        Assert.NotNull(second);
        Assert.Equal(first.Range, second.Range);
        Assert.Equal(first.PlayersOnly, second.PlayersOnly);
        Assert.Equal(first.RequireLineOfSight, second.RequireLineOfSight);
        Assert.Equal(first.Cooldown, second.Cooldown);
        Assert.Equal(first.MinAccessLevel, second.MinAccessLevel);
        Assert.True(second.Wake);
        Assert.Equal(CycleMode.Tick, second.Mode);
        Assert.Equal("trigMob.Karma > 0", second.WhenSource);
    }

    [Fact]
    public void When_CompilesOnceAtParseTime()
    {
        var trigger = ProximityTrigger.Parse("proximity:8:when:1 + 1");

        Assert.NotNull(trigger.When);
        Assert.True(trigger.When.IsValid);
        Assert.Same(trigger.When, trigger.When);
    }

    [Fact]
    public void Defaults_StayOffTheWire()
    {
        var trigger = ProximityTrigger.Parse("proximity:8");

        Assert.False(trigger.Wake);
        Assert.Equal(CycleMode.Now, trigger.Mode);
        Assert.Null(trigger.When);
        Assert.Null(trigger.WhenSource);

        var serialized = trigger.Serialize();
        Assert.DoesNotContain("wake", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("mode", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("when", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Speech_CarriesTokens()
    {
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("open sesame"));
        var trigger = SpeechTrigger.Parse($"speech:{encoded}:false:true:15:false:30:wake:true");

        Assert.Equal("open sesame", trigger.Keyword);
        Assert.False(trigger.IgnoreCase);
        Assert.True(trigger.UseRegex);
        Assert.Equal(15, trigger.Range);
        Assert.False(trigger.PlayersOnly);
        Assert.Equal(TimeSpan.FromSeconds(30), trigger.Cooldown);
        Assert.True(trigger.Wake);
        Assert.Equal(CycleMode.Now, trigger.Mode);
    }

    [Fact]
    public void Kill_CarriesTokens()
    {
        var trigger = KillTrigger.Parse("kill:3:true:false:Dragon:true:30:mode:tick");

        Assert.Equal(3, trigger.RequiredKills);
        Assert.True(trigger.RequireAllDead);
        Assert.False(trigger.ResetOnTrigger);
        Assert.Equal("Dragon", trigger.FilterType);
        Assert.True(trigger.RequirePlayerKiller);
        Assert.Equal(TimeSpan.FromSeconds(30), trigger.Cooldown);
        Assert.Equal(CycleMode.Tick, trigger.Mode);
        Assert.False(trigger.Wake);
    }

    [Fact]
    public void Skill_CarriesTokens()
    {
        var trigger = SkillTrigger.Parse("skill:Mining+:15:50-90:true:10:wake:true:mode:tick");

        Assert.NotNull(trigger);
        Assert.Equal(SkillName.Mining, trigger.TargetSkill);
        Assert.Equal(SkillOutcome.Success, trigger.Outcome);
        Assert.Equal(15, trigger.Range);
        Assert.Equal(50.0, trigger.MinSkillValue);
        Assert.Equal(90.0, trigger.MaxSkillValue);
        Assert.True(trigger.RequireLOS);
        Assert.Equal(TimeSpan.FromSeconds(10), trigger.Cooldown);
        Assert.True(trigger.Wake);
        Assert.Equal(CycleMode.Tick, trigger.Mode);
    }

    [Fact]
    public void UnknownTokenValue_KeepsTheDefault()
    {
        var trigger = ProximityTrigger.Parse("proximity:8:mode:sideways:wake:maybe");

        Assert.Equal(8, trigger.Range);
        Assert.Equal(CycleMode.Now, trigger.Mode);
        Assert.False(trigger.Wake);
    }

    [Fact]
    public void Gates_IgnoreTokensAndReportGateKind()
    {
        var game = GameTimeWindowTrigger.Parse("game_time_window:8:17");
        var wall = WallTimeWindowTrigger.Parse("wall_time_window:18:0:23:0");

        Assert.Equal(TriggerKind.Gate, game.Kind);
        Assert.Equal(TriggerKind.Gate, wall.Kind);
        Assert.False(game.Wake);
        Assert.False(wall.Wake);
        Assert.Equal(CycleMode.Now, game.Mode);
        Assert.Equal(CycleMode.Now, wall.Mode);
        Assert.Null(game.When);
        Assert.Null(wall.When);
    }
}
