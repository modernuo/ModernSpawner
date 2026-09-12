using System;
using System.Collections.Generic;
using Server.Engines.ModernSpawner.Triggers;
using Xunit;

namespace Server.Engines.ModernSpawner.Tests.Triggers;

/// <summary>
/// The kill counter was split in two when <see cref="ITrigger.Evaluate" /> became pure:
/// <see cref="KillTrigger.Evaluate" /> reads <see cref="TriggerRuntimeState.KillCount" /> and reports
/// whether <em>this</em> kill reaches the threshold, and
/// <see cref="KillTrigger.AdvanceKillCount" /> does the writing. These pin the two halves against each
/// other above threshold 1, and pin that the count now lives on the spawner rather than on the parsed
/// trigger object.
/// </summary>
[Collection("Sequential ModernSpawner Tests")]
public class KillTriggerCounterTests
{
    private static KillTrigger Unbound(string definition)
    {
        var trigger = KillTrigger.Parse(definition);
        Assert.NotNull(trigger);

        // Registration is what normally binds this; assigning it by hand keeps the counter tests pure.
        trigger.State = new TriggerRuntimeState(null, Guid.NewGuid());
        return trigger;
    }

    private static TriggerContext AnyKill(ModernSpawner spawner = null) =>
        TriggerContext.ForKill(spawner, new Entity(Serial.Zero), null);

    private static ModernSpawner Place(string definition)
    {
        var spawner = new ModernSpawner(1, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), 0, default, "Rabbit");
        spawner.MoveToWorld(new Point3D(1500, 1500, 0), Map.Felucca);
        spawner.AddTriggerDefinition(definition);
        spawner.TriggerActivated = true;
        return spawner;
    }

    private static void DeleteSpawned(ModernSpawner spawner)
    {
        foreach (var spawned in new List<ISpawnable>(spawner.Spawned.Keys))
        {
            spawned.Delete();
        }
    }

    // One dispatch's worth of bookkeeping, in the order TriggerSystem.OnEntityKilled uses it: Evaluate
    // first (it reads KillCount + 1), then the advance, which resets only on an accepted kill.
    private static bool Kill(KillTrigger trigger, in TriggerContext context)
    {
        var accepted = trigger.Evaluate(in context);
        if (trigger.CountsKill(in context))
        {
            trigger.AdvanceKillCount(accepted);
        }

        return accepted;
    }

    [Fact]
    public void ThresholdOfThree_FiresOnlyOnTheThirdKill()
    {
        var trigger = Unbound("kill:3");
        var context = AnyKill();

        Assert.False(Kill(trigger, in context));
        Assert.Equal(1, trigger.State.KillCount);

        Assert.False(Kill(trigger, in context));
        Assert.Equal(2, trigger.State.KillCount);

        Assert.True(Kill(trigger, in context));
    }

    [Fact]
    public void ResetOnTrigger_ZeroesTheCountOnlyWhenTheKillWasAccepted()
    {
        var trigger = Unbound("kill:2");
        Assert.True(trigger.ResetOnTrigger);

        var context = AnyKill();

        Assert.False(Kill(trigger, in context));
        Assert.Equal(1, trigger.State.KillCount);

        // The accepted kill is the one that clears the counter, so the next threshold starts over.
        Assert.True(Kill(trigger, in context));
        Assert.Equal(0, trigger.State.KillCount);

        Assert.False(Kill(trigger, in context));
        Assert.Equal(1, trigger.State.KillCount);
    }

    [Fact]
    public void ResetOnTriggerFalse_KeepsCountingPastTheThreshold()
    {
        // kill:requiredKills:requireAllDead:resetOnTrigger:filterType:requirePlayerKiller:cooldownSeconds
        var trigger = Unbound("kill:2:false:false:any:false:0");
        Assert.False(trigger.ResetOnTrigger);

        var context = AnyKill();

        Assert.False(Kill(trigger, in context));
        Assert.True(Kill(trigger, in context));
        Assert.Equal(2, trigger.State.KillCount);

        Assert.True(Kill(trigger, in context));
        Assert.Equal(3, trigger.State.KillCount);
    }

    [Fact]
    public void FilteredOutKill_DoesNotCount()
    {
        var trigger = Unbound("kill:2:false:true:Dragon:false:0");
        var context = AnyKill();

        Assert.False(Kill(trigger, in context));
        Assert.False(trigger.CountsKill(in context));
        Assert.Equal(0, trigger.State.KillCount);
    }

    [Fact]
    public void RequireAllDead_BlocksTheCycleButTheKillStillCounts()
    {
        var spawner = Place("kill:1:true:true:any:false:0");

        try
        {
            spawner.Spawn();
            Assert.Single(spawner.Spawned);

            var trigger = Assert.Single(TriggerSystem.Instance.GetSet(spawner).Kill);
            Assert.NotNull(trigger.State);

            var context = AnyKill(spawner);

            // A spawn is still alive, so the threshold cannot fire - but the kill is not thrown away.
            Assert.False(Kill(trigger, in context));
            Assert.Equal(1, trigger.State.KillCount);

            DeleteSpawned(spawner);
            Assert.Empty(spawner.Spawned);

            Assert.True(Kill(trigger, in context));
        }
        finally
        {
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    [Fact]
    public void KillProgress_SurvivesDeactivateAndActivate()
    {
        var spawner = Place("kill:3");

        try
        {
            var first = Assert.Single(TriggerSystem.Instance.GetSet(spawner).Kill);
            var context = AnyKill(spawner);

            Assert.False(Kill(first, in context));
            Assert.False(Kill(first, in context));
            Assert.Equal(2, first.State.KillCount);

            // Re-registration parses a fresh trigger object; the count is spawner state keyed by the
            // definition id, so it has to come back bound to the new object.
            spawner.TriggerActivated = false;
            Assert.False(TriggerSystem.Instance.IsRegistered(spawner));

            spawner.TriggerActivated = true;
            var second = Assert.Single(TriggerSystem.Instance.GetSet(spawner).Kill);

            Assert.NotSame(first, second);
            Assert.Equal(2, second.State.KillCount);
            Assert.Equal(spawner.TriggerDefinitions[0].Id, second.Id);

            Assert.True(Kill(second, in context));
        }
        finally
        {
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }
}
