using Server.Engines.ModernSpawner.Triggers;
using Xunit;

namespace Server.Engines.ModernSpawner.Tests.Triggers;

/// <summary>
/// The one <see cref="TriggerSet" /> per spawner that replaced the six per-type dictionaries: adding a
/// parsed trigger files it under its class and advances the event/gate counts the tick guards read.
/// Pure - no world, no registration.
/// </summary>
public class TriggerSetTests
{
    private static TriggerSet Populated()
    {
        var set = new TriggerSet();
        set.Add(ProximityTrigger.Parse("proximity:8"));
        set.Add(SpeechTrigger.Parse("speech:hello"));
        set.Add(KillTrigger.Parse("kill:1"));
        set.Add(SkillTrigger.Parse("skill:Mining:10"));
        set.Add(GameTimeWindowTrigger.Parse("game_time_window:8:17"));
        set.Add(WallTimeWindowTrigger.Parse("wall_time_window:18:0:23:0"));
        return set;
    }

    [Fact]
    public void Add_FilesEachTriggerUnderItsClass()
    {
        var set = Populated();

        Assert.Equal(6, set.All.Count);
        Assert.Single(set.Proximity);
        Assert.Single(set.Speech);
        Assert.Single(set.Kill);
        Assert.Single(set.Skill);
        Assert.Equal(2, set.Gates.Count);
    }

    [Fact]
    public void Add_CountsEventsAndGatesSeparately()
    {
        var set = Populated();

        Assert.Equal(4, set.EventCount);
        Assert.Equal(2, set.GateCount);
    }

    [Fact]
    public void Add_IgnoresNull()
    {
        var set = new TriggerSet();
        set.Add(null);

        Assert.Empty(set.All);
        Assert.Equal(0, set.EventCount);
        Assert.Equal(0, set.GateCount);
    }

    [Fact]
    public void Clear_EmptiesEveryListAndCount()
    {
        var set = Populated();
        set.Clear();

        Assert.Empty(set.All);
        Assert.Empty(set.Proximity);
        Assert.Empty(set.Speech);
        Assert.Empty(set.Kill);
        Assert.Empty(set.Skill);
        Assert.Empty(set.Gates);
        Assert.Equal(0, set.EventCount);
        Assert.Equal(0, set.GateCount);
    }

    [Fact]
    public void TriggerContext_IsAStructWithValueEquality()
    {
        Assert.True(typeof(TriggerContext).IsValueType);

        var a = new TriggerContext(null, null, "open sesame", null, SkillName.Mining, 55.0, true);
        var b = new TriggerContext(null, null, "open sesame", null, SkillName.Mining, 55.0, true);
        var c = a with { SkillSuccess = false };

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void TriggerContext_FactoriesFillOnlyTheirOwnFields()
    {
        var speech = TriggerContext.ForSpeech(null, null, "hail");
        Assert.Equal("hail", speech.Speech);
        Assert.Null(speech.KilledEntity);
        Assert.False(speech.SkillSuccess);
        Assert.Equal(0.0, speech.SkillValue);

        var proximity = TriggerContext.ForProximity(null, null);
        Assert.Null(proximity.Speech);
        Assert.Null(proximity.KilledEntity);
    }
}
