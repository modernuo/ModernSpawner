using System;
using Server.Engines.ModernSpawner.Tests.Fixtures;
using Server.Engines.ModernSpawner.Triggers;
using Server.Misc;
using Server.Mobiles;
using Xunit;

namespace Server.Engines.ModernSpawner.Tests;

/// <summary>
/// End-to-end cover for skill triggers: a real <see cref="SkillCheck" /> handler raises
/// <see cref="SkillEvents.SkillUsed" />, <see cref="ModernSpawnerEvents" /> forwards it, and the spawner
/// fires only for players, only in range, only for the configured outcome, and only once per cooldown.
/// An accepted event buys one cycle that the outermost dispatch runs on its way out (E1), so what an
/// accepted skill use leaves behind is a spawn, not a queued slot.
/// </summary>
[Collection("Sequential ModernSpawner Tests")]
public class SkillTriggerTests
{
    private static ModernSpawner Place(string definition)
    {
        // Room for several cycles: the spawner must never fill up, or a later event would be held as
        // a queued slot (E2) instead of running.
        var spawner = new ModernSpawner(5, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), 0, default, "Rabbit");
        spawner.MoveToWorld(new Point3D(1500, 1500, 0), Map.Felucca);
        spawner.AddTriggerDefinition(definition);
        spawner.TriggerActivated = true;
        return spawner;
    }

    private static PlayerMobile PlacePlayer(Point3D at)
    {
        // Mobile.Player is not set by the PlayerMobile constructor (production sets it on login), and the
        // bridge filters on it, so the test host sets it the way ModernUO's own mobile tests do.
        var player = new PlayerMobile { Name = "Miner", Player = true };
        player.MoveToWorld(at, Map.Felucca);
        return player;
    }

    [Fact]
    public void PlayerSkillUse_InRange_FiresTrigger()
    {
        var spawner = Place("skill:Mining:10:0:false:0");
        var player = PlacePlayer(new Point3D(1503, 1500, 0));
        try
        {
            Assert.Empty(spawner.Spawned);
            SkillCheck.Mobile_SkillCheckDirectTarget(player, SkillName.Mining, null, 1.0);
            Assert.Single(spawner.Spawned);
            Assert.Equal(0, spawner.PendingCycleCount);
        }
        finally
        {
            player.Delete();
            spawner.Delete();
        }
    }

    [Fact]
    public void PlayerSkillUse_OutOfRange_DoesNotFire()
    {
        var spawner = Place("skill:Mining:5:0:false:0");
        var player = PlacePlayer(new Point3D(1520, 1500, 0));
        try
        {
            SkillCheck.Mobile_SkillCheckDirectTarget(player, SkillName.Mining, null, 1.0);
            Assert.Empty(spawner.Spawned);
        }
        finally
        {
            player.Delete();
            spawner.Delete();
        }
    }

    [Fact]
    public void FailureOnlyTrigger_IgnoresSuccess()
    {
        var spawner = Place("skill:Mining-:10:0:false:0");
        var player = PlacePlayer(new Point3D(1503, 1500, 0));
        try
        {
            SkillCheck.Mobile_SkillCheckDirectTarget(player, SkillName.Mining, null, 1.0);
            Assert.Empty(spawner.Spawned);
            SkillCheck.Mobile_SkillCheckDirectTarget(player, SkillName.Mining, null, -0.1);
            Assert.Single(spawner.Spawned);
        }
        finally
        {
            player.Delete();
            spawner.Delete();
        }
    }

    [Fact]
    public void CreatureSkillUse_DoesNotFire()
    {
        var spawner = Place("skill:Any:10:0:false:0");
        var rabbit = new Rabbit();
        rabbit.MoveToWorld(new Point3D(1503, 1500, 0), Map.Felucca);
        try
        {
            SkillCheck.Mobile_SkillCheckDirectTarget(rabbit, SkillName.Mining, null, 1.0);
            Assert.Empty(spawner.Spawned);
        }
        finally
        {
            rabbit.Delete();
            spawner.Delete();
        }
    }

    [Fact]
    public void Cooldown_SuppressesSecondFiringUntilElapsed()
    {
        var spawner = Place("skill:Mining:10:0:false:5");
        var player = PlacePlayer(new Point3D(1503, 1500, 0));
        try
        {
            SkillCheck.Mobile_SkillCheckDirectTarget(player, SkillName.Mining, null, 1.0);
            Assert.Single(spawner.Spawned);

            // The cooldown is trigger state, so it survives ResetTrigger and refuses the next event.
            spawner.ResetTrigger();
            SkillCheck.Mobile_SkillCheckDirectTarget(player, SkillName.Mining, null, 1.0);
            Assert.Single(spawner.Spawned);

            ModernSpawnerTestServer.AdvanceClock(TimeSpan.FromSeconds(6));
            SkillCheck.Mobile_SkillCheckDirectTarget(player, SkillName.Mining, null, 1.0);
            Assert.Equal(2, spawner.Spawned.Count);
        }
        finally
        {
            player.Delete();
            spawner.Delete();
        }
    }
}
