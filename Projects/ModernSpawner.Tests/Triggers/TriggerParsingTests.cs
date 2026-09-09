using System;
using Server.Engines.ModernSpawner.Triggers;
using Xunit;

namespace Server.Engines.ModernSpawner.Tests.Triggers;

public class TriggerParsingTests
{
    #region ProximityTrigger Tests

    [Fact]
    public void ProximityTrigger_Parse_DefaultValues()
    {
        var trigger = ProximityTrigger.Parse("proximity");

        Assert.NotNull(trigger);
        Assert.Equal(8, trigger.Range);
        Assert.True(trigger.PlayersOnly);
        Assert.False(trigger.RequireLineOfSight);
        Assert.Equal(TimeSpan.FromSeconds(5), trigger.Cooldown);
        Assert.Equal(AccessLevel.Player, trigger.MinAccessLevel);
    }

    [Fact]
    public void ProximityTrigger_Parse_CustomRange()
    {
        var trigger = ProximityTrigger.Parse("proximity:15");

        Assert.NotNull(trigger);
        Assert.Equal(15, trigger.Range);
    }

    [Fact]
    public void ProximityTrigger_Parse_AllParameters()
    {
        var trigger = ProximityTrigger.Parse("proximity:20:false:true:10:2");

        Assert.NotNull(trigger);
        Assert.Equal(20, trigger.Range);
        Assert.False(trigger.PlayersOnly);
        Assert.True(trigger.RequireLineOfSight);
        Assert.Equal(TimeSpan.FromSeconds(10), trigger.Cooldown);
        Assert.Equal(AccessLevel.GameMaster, trigger.MinAccessLevel);
    }

    [Fact]
    public void ProximityTrigger_Serialize_RoundTrip()
    {
        var original = new ProximityTrigger(12, false, true)
        {
            Cooldown = TimeSpan.FromSeconds(15),
            MinAccessLevel = AccessLevel.Counselor
        };

        var serialized = original.Serialize();
        var parsed = ProximityTrigger.Parse(serialized);

        Assert.NotNull(parsed);
        Assert.Equal(original.Range, parsed.Range);
        Assert.Equal(original.PlayersOnly, parsed.PlayersOnly);
        Assert.Equal(original.RequireLineOfSight, parsed.RequireLineOfSight);
        Assert.Equal(original.Cooldown, parsed.Cooldown);
        Assert.Equal(original.MinAccessLevel, parsed.MinAccessLevel);
    }

    #endregion

    #region SpeechTrigger Tests

    [Fact]
    public void SpeechTrigger_Parse_PlainTextKeyword()
    {
        var trigger = SpeechTrigger.Parse("speech:hello");

        Assert.NotNull(trigger);
        Assert.Equal("hello", trigger.Keyword);
        Assert.True(trigger.IgnoreCase);
        Assert.False(trigger.UseRegex);
        Assert.Equal(10, trigger.Range);
    }

    [Fact]
    public void SpeechTrigger_Parse_Base64EncodedKeyword()
    {
        // "test phrase" encoded in base64
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("test phrase"));
        var trigger = SpeechTrigger.Parse($"speech:{encoded}");

        Assert.NotNull(trigger);
        Assert.Equal("test phrase", trigger.Keyword);
    }

    [Fact]
    public void SpeechTrigger_Parse_AllParameters()
    {
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("dragon"));
        var trigger = SpeechTrigger.Parse($"speech:{encoded}:false:true:15:false:30");

        Assert.NotNull(trigger);
        Assert.Equal("dragon", trigger.Keyword);
        Assert.False(trigger.IgnoreCase);
        Assert.True(trigger.UseRegex);
        Assert.Equal(15, trigger.Range);
        Assert.False(trigger.PlayersOnly);
        Assert.Equal(TimeSpan.FromSeconds(30), trigger.Cooldown);
    }

    [Fact]
    public void SpeechTrigger_Serialize_RoundTrip()
    {
        var original = new SpeechTrigger("summon beast", false, 20)
        {
            UseRegex = true,
            PlayersOnly = false,
            Cooldown = TimeSpan.FromSeconds(60)
        };

        var serialized = original.Serialize();
        var parsed = SpeechTrigger.Parse(serialized);

        Assert.NotNull(parsed);
        Assert.Equal(original.Keyword, parsed.Keyword);
        Assert.Equal(original.IgnoreCase, parsed.IgnoreCase);
        Assert.Equal(original.UseRegex, parsed.UseRegex);
        Assert.Equal(original.Range, parsed.Range);
        Assert.Equal(original.PlayersOnly, parsed.PlayersOnly);
        Assert.Equal(original.Cooldown, parsed.Cooldown);
    }

    #endregion

    #region SkillTrigger Tests

    [Fact]
    public void SkillTrigger_Parse_SimpleSkill()
    {
        var trigger = SkillTrigger.Parse("skill:Mining:10");

        Assert.NotNull(trigger);
        Assert.Equal(SkillName.Mining, trigger.TargetSkill);
        Assert.Equal(10, trigger.Range);
        Assert.Equal(0, trigger.MinSkillValue);
    }

    [Fact]
    public void SkillTrigger_Parse_WithMinSkillValue()
    {
        var trigger = SkillTrigger.Parse("skill:Magery:5:50.0");

        Assert.NotNull(trigger);
        Assert.Equal(SkillName.Magery, trigger.TargetSkill);
        Assert.Equal(5, trigger.Range);
        Assert.Equal(50.0, trigger.MinSkillValue);
    }

    [Fact]
    public void SkillTrigger_Parse_AnySkill()
    {
        var trigger = SkillTrigger.Parse("skill:Any:8");

        Assert.NotNull(trigger);
        Assert.Equal((SkillName)(-1), trigger.TargetSkill);
        Assert.Equal(8, trigger.Range);
    }

    [Fact]
    public void SkillTrigger_Parse_AllParameters()
    {
        var trigger = SkillTrigger.Parse("skill:Blacksmith:15:80.0:true:10");

        Assert.NotNull(trigger);
        Assert.Equal(SkillName.Blacksmith, trigger.TargetSkill);
        Assert.Equal(15, trigger.Range);
        Assert.Equal(80.0, trigger.MinSkillValue);
        Assert.True(trigger.RequireLOS);
        Assert.Equal(TimeSpan.FromSeconds(10), trigger.Cooldown);
    }

    [Fact]
    public void SkillTrigger_Parse_CaseInsensitiveSkillName()
    {
        var trigger1 = SkillTrigger.Parse("skill:mining:10");
        var trigger2 = SkillTrigger.Parse("skill:MINING:10");
        var trigger3 = SkillTrigger.Parse("skill:Mining:10");

        Assert.NotNull(trigger1);
        Assert.NotNull(trigger2);
        Assert.NotNull(trigger3);
        Assert.Equal(SkillName.Mining, trigger1.TargetSkill);
        Assert.Equal(SkillName.Mining, trigger2.TargetSkill);
        Assert.Equal(SkillName.Mining, trigger3.TargetSkill);
    }

    [Fact]
    public void SkillTrigger_Parse_InvalidSkillName_ReturnsNull()
    {
        var trigger = SkillTrigger.Parse("skill:InvalidSkillName:10");

        Assert.Null(trigger);
    }

    [Fact]
    public void SkillTrigger_Parse_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(SkillTrigger.Parse(null));
        Assert.Null(SkillTrigger.Parse(""));
        Assert.Null(SkillTrigger.Parse("skill"));
    }

    [Fact]
    public void SkillTrigger_MatchesSkill_SpecificSkill()
    {
        var trigger = SkillTrigger.Parse("skill:Mining:10");

        Assert.True(trigger.MatchesSkill(SkillName.Mining));
        Assert.False(trigger.MatchesSkill(SkillName.Magery));
    }

    [Fact]
    public void SkillTrigger_MatchesSkill_AnySkill()
    {
        var trigger = SkillTrigger.Parse("skill:Any:10");

        Assert.True(trigger.MatchesSkill(SkillName.Mining));
        Assert.True(trigger.MatchesSkill(SkillName.Magery));
        Assert.True(trigger.MatchesSkill(SkillName.Swords));
    }

    [Fact]
    public void SkillTrigger_Range_MinimumIsOne()
    {
        var trigger = new SkillTrigger(SkillName.Mining, range: 0);

        Assert.Equal(1, trigger.Range);
    }

    #endregion
}
