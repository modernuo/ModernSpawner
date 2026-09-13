using System;
using System.Collections.Generic;
using Server.Engines.ModernSpawner.Positioning;
using Server.Engines.ModernSpawner.Tests.Fixtures;
using Server.Engines.ModernSpawner.Triggers;
using Server.Misc;
using Server.Mobiles;
using Xunit;

namespace Server.Engines.ModernSpawner.Tests;

/// <summary>
/// The D2 transition table, one test per row group of the design's §11 matrix. Everything is driven
/// through the production entry points - <see cref="ModernSpawner.OnTick" />,
/// <see cref="ModernSpawner.OnMovement" />, <see cref="SkillCheck.Mobile_SkillCheckDirectTarget" />,
/// <see cref="BaseSpawner.NotifySpawnedDeath" />, the gates' <see cref="ModernSpawner.OnGateOpened" />
/// edge callbacks - against a live world with a seeded clock.
/// </summary>
[Collection("Sequential ModernSpawner Tests")]
public class TriggerStateMachineTests
{
    // The seeded clock sits at noon on 2020-01-01 (a Wednesday in January), so a window restricted to
    // December can never be open and an all-day window always is. AdvanceClock only ever moves the
    // suite forward by minutes, so neither can flip under another test.
    private const string ClosedWindow = "wall_time_window:0:0:23:59:127:2048";
    private const string OpenWindow = "wall_time_window:0:0:23:59:127:4095";

    // proximity:range:playersOnly:requireLos:cooldownSeconds:minAccess
    private const string Proximity = "proximity:8:true:false:0:0";

    private static readonly Point3D SpawnerLocation = new(1500, 1500, 0);

    private static ModernSpawner Place(int count, params string[] definitions)
    {
        var spawner = new ModernSpawner(
            count,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(10),
            0,
            default,
            "Rabbit"
        );

        spawner.MoveToWorld(SpawnerLocation, Map.Felucca);

        for (var i = 0; i < definitions.Length; i++)
        {
            spawner.AddTriggerDefinition(definitions[i]);
        }

        if (definitions.Length > 0)
        {
            spawner.TriggerActivated = true;
        }

        return spawner;
    }

    private static PlayerMobile PlacePlayer(int x = 1503, int y = 1500)
    {
        // Mobile.Player is not set by the constructor (production sets it at login) and every event
        // trigger filters on it, so the test host sets it the way ModernUO's own mobile tests do.
        var player = new PlayerMobile { Name = "Walker", Player = true };
        player.MoveToWorld(new Point3D(x, y, 0), Map.Felucca);
        return player;
    }

    private static void Move(ModernSpawner spawner, Mobile player) =>
        spawner.OnMovement(player, new Point3D(player.X + 1, player.Y, player.Z));

    private static void DeleteSpawned(ModernSpawner spawner)
    {
        foreach (var spawned in new List<ISpawnable>(spawner.Spawned.Keys))
        {
            spawned.Delete();
        }
    }

    #region T2, G1-G4 - the gate set

    [Fact]
    public void T2_GateClosed_TickSpawnsNothing()
    {
        var spawner = Place(3, ClosedWindow);
        try
        {
            Assert.Equal(1, spawner.GateCount);
            Assert.False(spawner.GateOpen);

            spawner.OnTick();
            Assert.Empty(spawner.Spawned);
        }
        finally
        {
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    [Fact]
    public void G1_GateOpens_RunsTheWindowOpenCycle()
    {
        var spawner = Place(3, ClosedWindow);
        try
        {
            Assert.False(spawner.GateOpen);

            // The open edge a gate reports when its window starts.
            spawner.OnGateOpened(0);

            Assert.True(spawner.GateOpen);
            Assert.Single(spawner.Spawned);

            // And the tick is authorized again.
            spawner.OnTick();
            Assert.True(spawner.IsAuthorizedForTick);
        }
        finally
        {
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    [Fact]
    public void G2_G3_G4_OverlappingGates_TrackTheOpenSetById()
    {
        var spawner = Place(4, ClosedWindow, ClosedWindow);
        try
        {
            Assert.Equal(2, spawner.GateCount);

            spawner.OnGateOpened(0);
            Assert.True(spawner.GateOpen);
            var afterFirstOpen = spawner.Spawned.Count;
            Assert.Equal(1, afterFirstOpen);

            // G2: the set was already non-empty, so the second open edge has no side effects.
            spawner.OnGateOpened(1);
            Assert.True(spawner.GateOpen);
            Assert.Equal(afterFirstOpen, spawner.Spawned.Count);

            // G2 again for an id already present.
            spawner.OnGateOpened(1);
            Assert.Equal(afterFirstOpen, spawner.Spawned.Count);

            // G3: one of two gates closing leaves the spawner open, and live spawns stay.
            spawner.OnGateClosed(0);
            Assert.True(spawner.GateOpen);
            Assert.Equal(afterFirstOpen, spawner.Spawned.Count);

            spawner.OnGateClosed(1);
            Assert.False(spawner.GateOpen);
            Assert.Equal(afterFirstOpen, spawner.Spawned.Count);

            // G4: a stale close edge for an id that is not in the set is a no-op.
            spawner.OnGateClosed(1);
            Assert.False(spawner.GateOpen);
            Assert.Equal(afterFirstOpen, spawner.Spawned.Count);
        }
        finally
        {
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    [Fact]
    public void G1_RepeatedGateOpenings_KeepDrainingPastTheRecursionBudget()
    {
        // Eleven E2 -> G1 sequences on one spawner. The per-dispatch recursion budget is ten, so a
        // gate opening that spent it without ever handing the drain list back would stop draining on
        // the eleventh window.
        const int openings = 11;

        var spawner = Place(openings + 5, Proximity, ClosedWindow);
        var player = PlacePlayer();
        try
        {
            for (var i = 0; i < openings; i++)
            {
                Assert.False(spawner.GateOpen);

                Move(spawner, player);
                Assert.Equal(1, spawner.PendingCycleCount);

                spawner.OnGateOpened(1);
                Assert.Equal(0, spawner.PendingCycleCount);
                Assert.Equal(i + 1, spawner.Spawned.Count);

                spawner.OnGateClosed(1);
            }
        }
        finally
        {
            player.Delete();
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    #endregion

    #region E1, E0 and the acceptance order

    [Fact]
    public void E1_EventOnRunningSpawner_RunsOneCycleAfterDispatch()
    {
        var spawner = Place(3, Proximity);
        var player = PlacePlayer();
        spawner.ModernEntries[0].PositioningRule = DispatchProbeRule.Name;
        DispatchProbeRule.Watch(spawner);
        try
        {
            Move(spawner, player);

            // The cycle ran...
            Assert.Single(spawner.Spawned);
            Assert.Equal(0, spawner.PendingCycleCount);

            // ...and it ran with the dispatch already unwound: the trigger system was not inside a
            // dispatch when the cycle body asked for a spawn position.
            Assert.Equal(1, DispatchProbeRule.Calls);
            Assert.False(DispatchProbeRule.WasDispatching);
        }
        finally
        {
            DispatchProbeRule.Reset();
            player.Delete();
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    [Fact]
    public void E0_SecondEventWithinCooldown_DoesNothing()
    {
        var spawner = Place(4, "proximity:8:true:false:5:0");
        var player = PlacePlayer();
        try
        {
            Move(spawner, player);
            Assert.Single(spawner.Spawned);

            Move(spawner, player);
            Assert.Single(spawner.Spawned);

            ModernSpawnerTestServer.AdvanceClock(TimeSpan.FromSeconds(6));
            Move(spawner, player);
            Assert.Equal(2, spawner.Spawned.Count);
        }
        finally
        {
            player.Delete();
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    [Fact]
    public void E0_Refractory_BlocksADifferentTriggerType()
    {
        var spawner = Place(4, Proximity, "skill:Mining:10:0:false:0");
        var player = PlacePlayer();
        spawner.RefractoryMin = TimeSpan.FromSeconds(10);
        spawner.RefractoryMax = TimeSpan.FromSeconds(10);
        try
        {
            Move(spawner, player);
            Assert.Single(spawner.Spawned);

            // The spawner-wide lockout is not per trigger: a skill event is refused too.
            SkillCheck.Mobile_SkillCheckDirectTarget(player, SkillName.Mining, null, 1.0);
            Assert.Single(spawner.Spawned);

            ModernSpawnerTestServer.AdvanceClock(TimeSpan.FromSeconds(11));
            SkillCheck.Mobile_SkillCheckDirectTarget(player, SkillName.Mining, null, 1.0);
            Assert.Equal(2, spawner.Spawned.Count);
        }
        finally
        {
            player.Delete();
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    #endregion

    #region E2, T3, T6 - an event while full

    [Fact]
    public void E2_EventWhileFull_KeepsTheSlotAndTheNextTickConsumesIt()
    {
        var spawner = Place(1, Proximity);
        var player = PlacePlayer();
        try
        {
            // Manual spawn fills the spawner without touching the trigger machinery (M1).
            spawner.Spawn();
            Assert.True(spawner.IsFull);

            Move(spawner, player);

            // E2: the cycle is bought and kept, not run and not dropped.
            Assert.Equal(1, spawner.PendingCycleCount);
            Assert.Single(spawner.Spawned);

            // T3: a tick while full parks rather than consuming the slot.
            spawner.OnTick();
            Assert.Equal(1, spawner.PendingCycleCount);

            DeleteSpawned(spawner);
            Assert.Empty(spawner.Spawned);

            // The entry's own deadline was set by the manual spawn; it has to elapse before a tick
            // cycle is allowed to select it again (T5).
            ModernSpawnerTestServer.AdvanceClock(TimeSpan.FromMinutes(11));

            // T6: the tick pops the queued slot and runs it.
            spawner.OnTick();
            Assert.Single(spawner.Spawned);
            Assert.Equal(0, spawner.PendingCycleCount);
        }
        finally
        {
            player.Delete();
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    #endregion

    #region E4, E5 - events on a stopped spawner

    [Fact]
    public void E5_EventOnStoppedSpawner_QueuesASlotThatTheFirstTickRuns()
    {
        var spawner = Place(3, Proximity);
        var player = PlacePlayer();
        try
        {
            spawner.Stop();
            Assert.False(spawner.Running);

            // A2: registration, and with it movement dispatch, survives Stop().
            Assert.True(spawner.HandlesOnMovement);
            Assert.True(TriggerSystem.Instance.IsRegistered(spawner));

            Move(spawner, player);

            Assert.Equal(1, spawner.PendingCycleCount);
            Assert.Empty(spawner.Spawned);
            Assert.False(spawner.Running);

            // T0: a tick on a stopped spawner does nothing at all, slot or no slot.
            spawner.OnTick();
            Assert.Empty(spawner.Spawned);
            Assert.Equal(1, spawner.PendingCycleCount);

            spawner.Start();
            spawner.OnTick();

            Assert.Single(spawner.Spawned);
            Assert.Equal(0, spawner.PendingCycleCount);
        }
        finally
        {
            player.Delete();
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    [Fact]
    public void E4_WakeTrigger_StartsTheStoppedSpawnerAndRunsTheCycle()
    {
        var spawner = Place(3, Proximity + ":wake:true");
        var player = PlacePlayer();
        try
        {
            spawner.Stop();
            Assert.False(spawner.Running);

            Move(spawner, player);

            Assert.True(spawner.Running);
            Assert.Single(spawner.Spawned);
            Assert.Equal(0, spawner.PendingCycleCount);
        }
        finally
        {
            player.Delete();
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    #endregion

    #region E6, Max = 0 - the queue bound

    [Fact]
    public void E6_QueueFull_NoCooldownAdvance()
    {
        var spawner = Place(3, "proximity:8:true:false:5:0", ClosedWindow);
        var player = PlacePlayer();
        try
        {
            Assert.False(spawner.GateOpen);
            Assert.Equal(1, spawner.MaxPendingCycles);

            var id = spawner.TriggerDefinitions[0].Id;

            Move(spawner, player);
            Assert.Equal(1, spawner.PendingCycleCount);

            var cooldownUntil = spawner.GetTriggerState(id).CooldownUntil;
            Assert.NotEqual(default, cooldownUntil);

            ModernSpawnerTestServer.AdvanceClock(TimeSpan.FromSeconds(6));

            // The queue is full, so the event is refused before any acceptance side effect: the
            // cooldown does not move, so it is not silently eaten either.
            Move(spawner, player);
            Assert.Equal(1, spawner.PendingCycleCount);
            Assert.Equal(cooldownUntil, spawner.GetTriggerState(id).CooldownUntil);
        }
        finally
        {
            player.Delete();
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    [Fact]
    public void E6_QueueBound_ThreeEventsBuyOneCycle()
    {
        var spawner = Place(4, Proximity, ClosedWindow);
        var player = PlacePlayer();
        try
        {
            Move(spawner, player);
            Move(spawner, player);
            Move(spawner, player);

            Assert.Equal(1, spawner.PendingCycleCount);
            Assert.Empty(spawner.Spawned);

            // The window opening spends exactly the one cycle that was bought.
            spawner.OnGateOpened(1);
            Assert.Single(spawner.Spawned);
            Assert.Equal(0, spawner.PendingCycleCount);
        }
        finally
        {
            player.Delete();
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    [Fact]
    public void Max0_LegacyDrop_NoCooldownAdvance()
    {
        var spawner = Place(3, "proximity:8:true:false:5:0", ClosedWindow);
        var player = PlacePlayer();
        spawner.MaxPendingCycles = 0;
        try
        {
            var id = spawner.TriggerDefinitions[0].Id;
            Assert.False(spawner.GateOpen);

            // Nothing can run now and nothing may latch, so the event is refused outright.
            Move(spawner, player);
            Assert.Equal(0, spawner.PendingCycleCount);
            Assert.Equal(default, spawner.GetTriggerState(id).CooldownUntil);

            // ...and the window opening finds nothing waiting for it.
            spawner.OnGateOpened(1);
            Assert.Empty(spawner.Spawned);

            // With the gate open the same event runs immediately and does advance the cooldown.
            Move(spawner, player);
            Assert.Single(spawner.Spawned);
            Assert.Equal(0, spawner.PendingCycleCount);
            Assert.NotEqual(default, spawner.GetTriggerState(id).CooldownUntil);
        }
        finally
        {
            player.Delete();
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    [Fact]
    public void Max0_NestedEventMidCycle_LeavesNoLatchedRunNow()
    {
        // MaxPendingCycles == 0 has no queue, so an accepted event leaves a one-shot run-now request.
        // One raised from inside a tick cycle cannot run - a cycle is already in flight - and it must
        // be dropped there and then, not left sitting on the spawner for some later drain to spend.
        var spawner = Place(10);
        spawner.MaxPendingCycles = 0;
        spawner.ModernEntries[0].PositioningRule = ExternalTriggerProbeRule.Name;
        ExternalTriggerProbeRule.Watch(spawner);
        try
        {
            spawner.OnTick();

            // The tick's own cycle ran; the nested request did not nest.
            Assert.Single(spawner.Spawned);

            // Nothing is left for a later drain to find.
            ExternalTriggerProbeRule.Reset();
            TriggerSystem.Instance.RequestDrain(spawner);
            Assert.Single(spawner.Spawned);
        }
        finally
        {
            ExternalTriggerProbeRule.Reset();
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    #endregion

    #region E3 - mode:tick and mode:now

    [Fact]
    public void E3_ModeTick_RunsAtNextTickHonouringEntryDeadlines()
    {
        var spawner = Place(3, Proximity + ":mode:tick");
        var player = PlacePlayer();
        try
        {
            spawner.ModernEntries[0].NextEligible = Core.Now + TimeSpan.FromHours(1);

            Move(spawner, player);

            // mode:tick only buys the cycle; nothing runs inside or right after the dispatch.
            Assert.Equal(1, spawner.PendingCycleCount);
            Assert.Empty(spawner.Spawned);

            // The tick honours the entry deadline, so the slot waits.
            spawner.OnTick();
            Assert.Empty(spawner.Spawned);
            Assert.Equal(1, spawner.PendingCycleCount);

            ModernSpawnerTestServer.AdvanceClock(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(1));

            spawner.OnTick();
            Assert.Single(spawner.Spawned);
            Assert.Equal(0, spawner.PendingCycleCount);
        }
        finally
        {
            player.Delete();
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    [Fact]
    public void E3_ModeNow_BypassesEntryDeadlines()
    {
        var spawner = Place(3, Proximity);
        var player = PlacePlayer();
        try
        {
            spawner.ModernEntries[0].NextEligible = Core.Now + TimeSpan.FromHours(1);

            Move(spawner, player);

            // The drained event cycle ignores the per-entry deadline (XmlSpawner resets entry timers
            // on a trigger), so the spawn happens despite the entry being parked.
            Assert.Single(spawner.Spawned);
        }
        finally
        {
            player.Delete();
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    #endregion

    #region The deferred slot carries the triggering mobile

    [Fact]
    public void PlayerRelative_UsesDeferredSlotMobile()
    {
        var spawner = Place(3, Proximity, ClosedWindow);
        var player = PlacePlayer();
        spawner.ModernEntries[0].PositioningRule = DispatchProbeRule.Name;
        DispatchProbeRule.Watch(spawner);
        try
        {
            // The gate is closed, so the event is queued and the cycle runs later (E2 -> G1).
            Move(spawner, player);
            Assert.Equal(1, spawner.PendingCycleCount);
            Assert.Equal(player.Serial, spawner.PendingCycles[0].TriggeringMobile);

            spawner.OnGateOpened(1);

            Assert.Single(spawner.Spawned);
            Assert.Equal(1, DispatchProbeRule.Calls);

            // The slot's serial was resolved back into the mobile positioning sees, which is what
            // player_relative reads off PositioningContext.
            Assert.Same(player, DispatchProbeRule.LastTriggeringMobile);
        }
        finally
        {
            DispatchProbeRule.Reset();
            player.Delete();
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    #endregion

    #region A3, A4 - definition edits, activation and hydration

    [Fact]
    public void A3_Reorder_KeepsStateById()
    {
        var spawner = Place(3, Proximity, "kill:3:false:true:any:false:0");
        try
        {
            var proximityId = spawner.TriggerDefinitions[0].Id;
            var killId = spawner.TriggerDefinitions[1].Id;

            var killState = spawner.GetTriggerState(killId);
            killState.KillCount = 2;
            var proximityState = spawner.GetTriggerState(proximityId);
            proximityState.CooldownUntil = Core.Now + TimeSpan.FromMinutes(30);
            var cooldownUntil = proximityState.CooldownUntil;

            // A gump delete of the first definition, which is also what a reorder does to the list.
            spawner.RemoveTriggerDefinitionAt(0);

            Assert.Equal(killId, Assert.Single(spawner.TriggerDefinitions).Id);
            Assert.Equal(2, spawner.GetTriggerState(killId).KillCount);
            Assert.Null(spawner.GetTriggerState(proximityId));

            // Putting it back with the same id gives it fresh state, and the kill state is untouched.
            spawner.AddTriggerDefinition(proximityId, Proximity);
            Assert.Equal(2, spawner.TriggerDefinitions.Count);
            Assert.Equal(proximityId, spawner.TriggerDefinitions[1].Id);
            Assert.Equal(default, spawner.GetTriggerState(proximityId).CooldownUntil);
            Assert.NotEqual(cooldownUntil, spawner.GetTriggerState(proximityId).CooldownUntil);
            Assert.Equal(2, spawner.GetTriggerState(killId).KillCount);

            // The parsed triggers are bound to the state by id, not by position.
            var set = TriggerSystem.Instance.GetSet(spawner);
            var kill = Assert.Single(set.Kill);
            Assert.Equal(killId, kill.Id);
            Assert.Equal(2, kill.State.KillCount);
        }
        finally
        {
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    [Fact]
    public void A4_Deactivate_ClearsPendingAndGates()
    {
        var spawner = Place(3, Proximity, ClosedWindow);
        var player = PlacePlayer();
        try
        {
            // Open the window, spend one event through it, then close it again and buy a cycle that
            // has to wait: the spawner now holds both an open-gate history and a queued slot.
            spawner.OnGateOpened(1);
            Assert.True(spawner.GateOpen);

            Move(spawner, player);
            Assert.Single(spawner.Spawned);
            DeleteSpawned(spawner);

            spawner.OnGateClosed(1);
            Assert.False(spawner.GateOpen);

            Move(spawner, player);
            Assert.Equal(1, spawner.PendingCycleCount);

            spawner.TriggerActivated = false;

            // A4: the queue is dropped and the registration is gone; a deactivated spawner is a plain
            // timer spawner, so its gate reads open.
            Assert.Equal(0, spawner.PendingCycleCount);
            Assert.False(TriggerSystem.Instance.IsRegistered(spawner));
            Assert.True(spawner.GateOpen);
            Assert.Equal(0, spawner.GateCount);

            // Re-activating must not resurrect the old open bit: the window is closed, so the gate is.
            spawner.TriggerActivated = true;
            Assert.Equal(1, spawner.GateCount);
            Assert.False(spawner.GateOpen);
            Assert.Equal(0, spawner.PendingCycleCount);
        }
        finally
        {
            player.Delete();
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    [Fact]
    public void A3_Activate_HydratesGatesSilently()
    {
        var spawner = Place(3);
        try
        {
            spawner.AddTriggerDefinition(OpenWindow);
            spawner.TriggerActivated = true;

            // The window is already open at registration: the bit is set, but the open edge that
            // registration itself produced must not buy a cycle.
            Assert.True(spawner.GateOpen);
            Assert.Equal(1, spawner.GateCount);
            Assert.Empty(spawner.Spawned);
            Assert.Equal(0, spawner.PendingCycleCount);
        }
        finally
        {
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    #endregion

    #region L1 - world load

    [Fact]
    public void L1_Restart_NoSpawnDuringLoad()
    {
        var spawner = Place(3, Proximity, OpenWindow);
        try
        {
            var proximityId = spawner.TriggerDefinitions[0].Id;
            spawner.MaxPendingCycles = 2;
            spawner.RefractoryMin = TimeSpan.FromSeconds(7);
            spawner.RefractoryMax = TimeSpan.FromSeconds(7);
            spawner.RefractoryUntil = Core.Now + TimeSpan.FromSeconds(7);
            spawner.GetTriggerState(proximityId).CooldownUntil = Core.Now + TimeSpan.FromMinutes(3);
            spawner.EnqueuePendingForTest(proximityId, (Serial)0x40001111u);

            var writer = new BufferWriter(true);
            spawner.Serialize(writer);
            var bytes = writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();

            var loaded = new ModernSpawner((Serial)0x40004321u);
            loaded.Deserialize(new BufferReader(bytes));
            try
            {
                // Nothing may spawn while the world is loading, before or after the deferred pass.
                Assert.Empty(loaded.Spawned);

                loaded.OnWorldLoaded();

                Assert.Empty(loaded.Spawned);
                Assert.True(TriggerSystem.Instance.IsRegistered(loaded));

                // Runtime state survived the restart...
                Assert.Equal(1, loaded.PendingCycleCount);
                Assert.Equal(proximityId, loaded.PendingCycles[0].TriggerId);
                Assert.NotEqual(default, loaded.RefractoryUntil);
                Assert.NotEqual(default, loaded.GetTriggerState(proximityId).CooldownUntil);

                // ...and the window was recomputed from the clock rather than persisted.
                Assert.Equal(1, loaded.GateCount);
                Assert.True(loaded.GateOpen);
            }
            finally
            {
                DeleteSpawned(loaded);
                loaded.Delete();
            }
        }
        finally
        {
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    [Fact]
    public void L2_Restart_DeactivatedSpawnerRegistersNothingAndHoldsNoCycles()
    {
        var spawner = Place(3, Proximity);
        try
        {
            var id = spawner.TriggerDefinitions[0].Id;
            spawner.EnqueuePendingForTest(id, Serial.Zero);
            Assert.Equal(1, spawner.PendingCycleCount);

            // Saved with the master switch off, but still carrying the slot the save was taken with.
            spawner.TriggerActivated = false;
            spawner.EnqueuePendingForTest(id, Serial.Zero);

            var writer = new BufferWriter(true);
            spawner.Serialize(writer);
            var bytes = writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();

            var loaded = new ModernSpawner((Serial)0x40004322u);
            loaded.Deserialize(new BufferReader(bytes));
            try
            {
                loaded.OnWorldLoaded();

                Assert.False(loaded.TriggerActivated);
                Assert.False(TriggerSystem.Instance.IsRegistered(loaded));
                Assert.Equal(0, loaded.PendingCycleCount);
                Assert.Empty(loaded.Spawned);
            }
            finally
            {
                DeleteSpawned(loaded);
                loaded.Delete();
            }
        }
        finally
        {
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    #endregion

    #region M1, M3, M4 - manual operations

    [Fact]
    public void M1_ManualSpawn_BypassesClosedGate()
    {
        var spawner = Place(3, ClosedWindow);
        try
        {
            spawner.OnTick();
            Assert.Empty(spawner.Spawned);

            spawner.Spawn();
            Assert.Single(spawner.Spawned);
        }
        finally
        {
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    [Fact]
    public void M3_Reset_ClearsRuntimeStateButKeepsRegistration()
    {
        var spawner = Place(3, Proximity, "kill:3:false:true:any:false:0");
        try
        {
            var proximityId = spawner.TriggerDefinitions[0].Id;
            var killId = spawner.TriggerDefinitions[1].Id;

            spawner.Spawn();
            spawner.EnqueuePendingForTest(proximityId, Serial.Zero);
            spawner.GetTriggerState(proximityId).CooldownUntil = Core.Now + TimeSpan.FromMinutes(5);
            spawner.GetTriggerState(killId).KillCount = 2;
            spawner.RefractoryUntil = Core.Now + TimeSpan.FromMinutes(5);

            spawner.Reset();

            Assert.Equal(0, spawner.PendingCycleCount);
            Assert.Equal(default, spawner.GetTriggerState(proximityId).CooldownUntil);
            Assert.Equal(0, spawner.GetTriggerState(killId).KillCount);
            Assert.Equal(default, spawner.RefractoryUntil);
            Assert.Empty(spawner.Spawned);
            Assert.False(spawner.Running);

            // M3 keeps the registration: a stopped registration is live.
            Assert.True(TriggerSystem.Instance.IsRegistered(spawner));
        }
        finally
        {
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    [Fact]
    public void M4_ResetTrigger_DropsPendingButKeepsCooldowns()
    {
        var spawner = Place(3, Proximity);
        try
        {
            var id = spawner.TriggerDefinitions[0].Id;
            spawner.EnqueuePendingForTest(id, Serial.Zero);
            spawner.GetTriggerState(id).CooldownUntil = Core.Now + TimeSpan.FromMinutes(5);
            var cooldownUntil = spawner.GetTriggerState(id).CooldownUntil;

            spawner.ResetTrigger();

            Assert.Equal(0, spawner.PendingCycleCount);
            Assert.Equal(cooldownUntil, spawner.GetTriggerState(id).CooldownUntil);
        }
        finally
        {
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    [Fact]
    public void M_ExternalTrigger_RunsACycleWithoutDefinitions()
    {
        var spawner = Place(3);
        try
        {
            // The script / command entry point is an event source of its own, even with no
            // definitions at all.
            spawner.Trigger();

            Assert.Single(spawner.Spawned);
            Assert.Equal(0, spawner.PendingCycleCount);
        }
        finally
        {
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    #endregion

    #region T1 - group mode

    [Fact]
    public void T1_Group_PartialDead_NoRespawn()
    {
        var spawner = Place(2);
        spawner.Group = true;
        try
        {
            spawner.Respawn();
            Assert.Equal(2, spawner.Spawned.Count);

            var first = new List<ISpawnable>(spawner.Spawned.Keys)[0];
            first.Delete();
            Assert.Single(spawner.Spawned);

            // The group is not all dead, so the tick parks rather than topping the pack back up.
            spawner.OnTick();
            Assert.Single(spawner.Spawned);

            DeleteSpawned(spawner);
            ModernSpawnerTestServer.AdvanceClock(TimeSpan.FromMinutes(11));

            spawner.OnTick();
            Assert.Equal(2, spawner.Spawned.Count);
        }
        finally
        {
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    [Fact]
    public void T1_Group_AllDead_OneBulkRespawnWithScriptsOnce()
    {
        var helper = Place(10);
        helper.Name = "D2BulkHelper";

        var spawner = Place(3);
        spawner.Group = true;
        spawner.SetOnBeforeSpawnScript($"SPAWN/{helper.Name}");
        try
        {
            Assert.True(spawner.OnBeforeSpawnScript.IsValid);

            spawner.OnTick();

            // Three spawns from one bulk respawn...
            Assert.Equal(3, spawner.Spawned.Count);

            // ...and the before-spawn script ran exactly once for the whole respawn, not once per
            // spawn, which the helper spawner counts for us.
            Assert.Single(helper.Spawned);
        }
        finally
        {
            DeleteSpawned(spawner);
            spawner.Delete();
            DeleteSpawned(helper);
            helper.Delete();
        }
    }

    [Fact]
    public void T1_Group_EventOnPopulatedPack_KeepsTheSlot()
    {
        var spawner = Place(3, Proximity);
        spawner.Group = true;
        var player = PlacePlayer();
        try
        {
            spawner.Respawn();
            Assert.Equal(3, spawner.Spawned.Count);

            // Room for one more, so the event is accepted rather than held by E2's IsFull branch...
            var first = new List<ISpawnable>(spawner.Spawned.Keys)[0];
            first.Delete();
            Assert.Equal(2, spawner.Spawned.Count);
            Assert.False(spawner.IsFull);

            Move(spawner, player);

            // ...and T1 applies to the event's cycle exactly as it does to a tick: the pack is not
            // dead, so nothing is respawned and the cycle stays bought.
            Assert.Equal(2, spawner.Spawned.Count);
            Assert.Equal(1, spawner.PendingCycleCount);

            DeleteSpawned(spawner);

            // With the pack dead the same slot buys the one bulk respawn.
            TriggerSystem.Instance.RequestDrain(spawner);
            Assert.Equal(3, spawner.Spawned.Count);
            Assert.Equal(0, spawner.PendingCycleCount);
        }
        finally
        {
            player.Delete();
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    [Fact]
    public void T1_Group_EventOnDeadPack_RunsOneBulkRespawnWithScriptsOnce()
    {
        var helper = Place(10);
        helper.Name = "D2GroupEventHelper";

        var spawner = Place(3, Proximity);
        spawner.Group = true;
        spawner.SetOnBeforeSpawnScript($"SPAWN/{helper.Name}");
        var player = PlacePlayer();
        try
        {
            Assert.Empty(spawner.Spawned);

            Move(spawner, player);

            // One bulk respawn from the event's cycle...
            Assert.Equal(3, spawner.Spawned.Count);
            Assert.Equal(0, spawner.PendingCycleCount);

            // ...with the before-spawn script run once for the whole respawn, which the helper counts.
            Assert.Single(helper.Spawned);
        }
        finally
        {
            player.Delete();
            DeleteSpawned(spawner);
            spawner.Delete();
            DeleteSpawned(helper);
            helper.Delete();
        }
    }

    #endregion

    #region T5 - per-entry deadlines

    [Fact]
    public void T5_EntryDeadlines_OnlyDueSelectable()
    {
        var spawner = new ModernSpawner(
            4,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(10),
            0,
            default,
            "Rabbit",
            "Bird"
        );
        spawner.MoveToWorld(SpawnerLocation, Map.Felucca);
        try
        {
            var rabbit = spawner.ModernEntries[0];
            var bird = spawner.ModernEntries[1];

            rabbit.NextEligible = Core.Now + TimeSpan.FromHours(1);

            spawner.OnTick();

            Assert.Empty(rabbit.Spawned);
            Assert.Single(bird.Spawned);

            // Both parked: the tick arms at the earliest deadline instead of spawning.
            rabbit.NextEligible = Core.Now + TimeSpan.FromHours(1);
            bird.NextEligible = Core.Now + TimeSpan.FromHours(2);
            DeleteSpawned(spawner);

            spawner.OnTick();
            Assert.Empty(spawner.Spawned);
        }
        finally
        {
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    [Fact]
    public void T5_FailedPlacement_BacksOff()
    {
        var spawner = new ModernSpawner(
            2,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(10),
            0,
            default,
            "ThisTypeDoesNotExist"
        );
        spawner.MoveToWorld(SpawnerLocation, Map.Felucca);
        try
        {
            var entry = spawner.ModernEntries[0];
            Assert.Equal(default, entry.NextEligible);

            var now = Core.Now;
            spawner.OnTick();

            Assert.Empty(spawner.Spawned);

            // A failed placement backs the entry off by min(30s, MinDelay) rather than the full delay.
            Assert.Equal(now + TimeSpan.FromSeconds(30), entry.NextEligible);
            Assert.False(entry.IsDue(now));
        }
        finally
        {
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    [Fact]
    public void T5_SuccessfulSpawn_SetsTheEntryDeadline()
    {
        var spawner = Place(4);
        try
        {
            var entry = spawner.ModernEntries[0];
            entry.MinDelay = TimeSpan.FromSeconds(30);
            entry.MaxDelay = TimeSpan.FromSeconds(30);

            var now = Core.Now;
            spawner.OnTick();

            Assert.Single(spawner.Spawned);
            Assert.Equal(now + TimeSpan.FromSeconds(30), entry.NextEligible);

            // The entry is parked, so the next tick spawns nothing until the delay elapses.
            spawner.OnTick();
            Assert.Single(spawner.Spawned);

            ModernSpawnerTestServer.AdvanceClock(TimeSpan.FromSeconds(31));
            spawner.OnTick();
            Assert.Equal(2, spawner.Spawned.Count);
        }
        finally
        {
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    #endregion

    #region Kill triggers through the real death hook

    [Fact]
    public void Kill_ThresholdCountsThroughTheAcceptancePath()
    {
        var spawner = Place(4, "kill:2:false:true:any:false:0");
        try
        {
            var id = spawner.TriggerDefinitions[0].Id;
            spawner.Spawn();
            var rabbit = (BaseCreature)Assert.Single(spawner.Spawned).Key;

            // The first kill counts but does not reach the threshold, so it buys nothing.
            spawner.NotifySpawnedDeath(rabbit, null);
            Assert.Equal(1, spawner.GetTriggerState(id).KillCount);
            Assert.Single(spawner.Spawned);

            // The second reaches it, runs a cycle, and resets the counter.
            spawner.NotifySpawnedDeath(rabbit, null);
            Assert.Equal(0, spawner.GetTriggerState(id).KillCount);
            Assert.Equal(2, spawner.Spawned.Count);

            rabbit.Corpse?.Delete();
        }
        finally
        {
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    [Fact]
    public void Kill_BelowThresholdKillCountsWhileTheTriggerIsOnCooldown()
    {
        // kill:requiredKills:requireAllDead:resetOnTrigger:filterType:requirePlayerKiller:cooldownSeconds
        var spawner = Place(6, "kill:3:false:true:any:false:60");
        try
        {
            var id = spawner.TriggerDefinitions[0].Id;
            var state = spawner.GetTriggerState(id);

            spawner.Spawn();
            var rabbit = (BaseCreature)Assert.Single(spawner.Spawned).Key;
            var spawnedBefore = spawner.Spawned.Count;

            state.CooldownUntil = Core.Now + TimeSpan.FromSeconds(60);

            // The cooldown gates the trigger firing, not the kills that build up to it.
            spawner.NotifySpawnedDeath(rabbit, null);
            spawner.NotifySpawnedDeath(rabbit, null);
            Assert.Equal(2, state.KillCount);
            Assert.Equal(spawnedBefore, spawner.Spawned.Count);

            // The kill that reaches the threshold is refused by the cooldown, and a refusal after the
            // evaluation moves nothing - so the threshold stays reached for the next kill.
            spawner.NotifySpawnedDeath(rabbit, null);
            Assert.Equal(2, state.KillCount);
            Assert.Equal(spawnedBefore, spawner.Spawned.Count);

            ModernSpawnerTestServer.AdvanceClock(TimeSpan.FromSeconds(61));

            spawner.NotifySpawnedDeath(rabbit, null);
            Assert.Equal(0, state.KillCount);
            Assert.Equal(spawnedBefore + 1, spawner.Spawned.Count);

            rabbit.Corpse?.Delete();
        }
        finally
        {
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    #endregion

    #region The when: condition

    [Fact]
    public void When_FailingConditionRejectsTheEventAndLeavesTheCooldown()
    {
        var spawner = Place(4, "proximity:8:true:false:5:0:when:trigmob.Fame > 100");
        var player = PlacePlayer();
        try
        {
            var id = spawner.TriggerDefinitions[0].Id;

            player.Fame = 0;
            Move(spawner, player);

            // Rejected before any acceptance side effect: no cycle, and the cooldown never moved.
            Assert.Empty(spawner.Spawned);
            Assert.Equal(0, spawner.PendingCycleCount);
            Assert.Equal(default, spawner.GetTriggerState(id).CooldownUntil);

            player.Fame = 500;
            Move(spawner, player);

            Assert.Single(spawner.Spawned);
            Assert.NotEqual(default, spawner.GetTriggerState(id).CooldownUntil);
        }
        finally
        {
            player.Delete();
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    #endregion

    #region §5 - reentrancy, the drain list and the recursion bound

    [Fact]
    public void Script_SPAWN_InsideCycle_DrainsAfter()
    {
        var helper = Place(10, Proximity);
        helper.Name = "D2ScriptHelper";

        var spawner = Place(3, Proximity);
        spawner.ModernEntries[0].OnSpawnScript = $"SPAWN/{helper.Name}";
        var player = PlacePlayer();
        try
        {
            Move(spawner, player);

            // The trigger-bought cycle ran, and the script it executed spawned through the helper.
            Assert.Single(spawner.Spawned);
            Assert.Single(helper.Spawned);

            // The helper's own spawn was manual (M1): it bypassed the helper's trigger machinery
            // rather than buying a cycle there.
            Assert.Equal(0, helper.PendingCycleCount);
        }
        finally
        {
            player.Delete();
            DeleteSpawned(spawner);
            spawner.Delete();
            DeleteSpawned(helper);
            helper.Delete();
        }
    }

    [Fact]
    public void Script_ReentrantEvent_IsQueuedAndBoundedByTheRecursionLimit()
    {
        var spawner = Place(50, Proximity);
        var player = PlacePlayer();
        spawner.ModernEntries[0].PositioningRule = ReentrantProbeRule.Name;
        ReentrantProbeRule.Watch(spawner, player);
        try
        {
            Move(spawner, player);

            // Each cycle raises another event from inside itself. Those are queued, never nested, and
            // the drain stops at the product spec's recursion limit of 10 per spawner per round.
            Assert.Equal(10, spawner.Spawned.Count);

            // The event raised by the tenth cycle is still waiting rather than lost.
            Assert.Equal(1, spawner.PendingCycleCount);

            // The nested event never ran inside the cycle that raised it: every probe call saw a
            // spawn count one lower than the cycle it belonged to.
            Assert.True(ReentrantProbeRule.NeverNested);
        }
        finally
        {
            ReentrantProbeRule.Reset();
            player.Delete();
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    [Fact]
    public void Script_TickInitiatedCycle_DrainsFollowUpsInTheSameTick()
    {
        // A cycle started by the tick is not inside a drain, so its follow-ups have no outer loop
        // waiting for them: the cycle itself has to hand them to the drain list on the way out, or
        // they would sit until the next tick.
        var spawner = Place(50);
        spawner.ModernEntries[0].PositioningRule = ExternalTriggerProbeRule.Name;
        ExternalTriggerProbeRule.Watch(spawner);
        try
        {
            spawner.OnTick();

            // The tick's own cycle plus ten drained follow-ups: the eleventh request is refused by the
            // recursion limit and waits for the next tick rather than running away.
            Assert.Equal(11, spawner.Spawned.Count);
            Assert.Equal(1, spawner.PendingCycleCount);
        }
        finally
        {
            ExternalTriggerProbeRule.Reset();
            DeleteSpawned(spawner);
            spawner.Delete();
        }
    }

    #endregion

    /// <summary>
    /// A positioning rule that records what the cycle body could see when it asked for a position.
    /// Registered like any other rule, so it runs exactly where <c>player_relative</c> would.
    /// </summary>
    private sealed class DispatchProbeRule : IPositioningRule
    {
        /// <summary>The rule name entries reference.</summary>
        public static readonly string Name = "d2_dispatch_probe";

        private static ModernSpawner _watched;

        static DispatchProbeRule() => PositioningRules.Register(new DispatchProbeRule());

        /// <inheritdoc />
        public string RuleName => Name;

        /// <inheritdoc />
        public string Description => "Test probe: records dispatch state and the triggering mobile.";

        /// <summary>How many times the probe ran for the watched spawner.</summary>
        public static int Calls { get; private set; }

        /// <summary>Whether the trigger system was still dispatching when the cycle body ran.</summary>
        public static bool WasDispatching { get; private set; }

        /// <summary>The triggering mobile the cycle threaded into positioning.</summary>
        public static Mobile LastTriggeringMobile { get; private set; }

        /// <summary>Starts recording for one spawner.</summary>
        /// <param name="spawner">The spawner whose cycles are observed.</param>
        public static void Watch(ModernSpawner spawner)
        {
            Reset();
            _watched = spawner;
        }

        /// <summary>Stops recording and clears what was recorded.</summary>
        public static void Reset()
        {
            _watched = null;
            Calls = 0;
            WasDispatching = false;
            LastTriggeringMobile = null;
        }

        /// <inheritdoc />
        public Point3D GetPosition(PositioningContext context)
        {
            if (context.Spawner == _watched)
            {
                Calls++;
                WasDispatching |= TriggerSystem.Instance.IsDispatching;
                LastTriggeringMobile = context.TriggeringMobile;
            }

            // Point3D.Zero falls through to the default positioner, so the spawn still lands.
            return Point3D.Zero;
        }
    }

    /// <summary>
    /// A positioning rule that raises a real proximity event from inside the cycle it is positioning
    /// for, which is the reentrancy §5 bounds: the event must queue rather than nest.
    /// </summary>
    private sealed class ReentrantProbeRule : IPositioningRule
    {
        /// <summary>The rule name entries reference.</summary>
        public static readonly string Name = "d2_reentrant_probe";

        private static ModernSpawner _watched;
        private static Mobile _mover;

        static ReentrantProbeRule() => PositioningRules.Register(new ReentrantProbeRule());

        /// <inheritdoc />
        public string RuleName => Name;

        /// <inheritdoc />
        public string Description => "Test probe: raises a proximity event from inside a cycle.";

        /// <summary>Whether every nested event stayed queued instead of spawning inside its cycle.</summary>
        public static bool NeverNested { get; private set; } = true;

        /// <summary>Starts raising nested events for one spawner.</summary>
        /// <param name="spawner">The spawner whose cycles raise the nested event.</param>
        /// <param name="mover">The mobile the nested movement event names.</param>
        public static void Watch(ModernSpawner spawner, Mobile mover)
        {
            Reset();
            _watched = spawner;
            _mover = mover;
        }

        /// <summary>Stops raising events and clears what was recorded.</summary>
        public static void Reset()
        {
            _watched = null;
            _mover = null;
            NeverNested = true;
        }

        /// <inheritdoc />
        public Point3D GetPosition(PositioningContext context)
        {
            var spawner = context.Spawner;
            if (spawner != _watched || _mover == null)
            {
                return Point3D.Zero;
            }

            // The spawn this cycle is positioning is already in the registry; anything more would be a
            // cycle that ran nested inside this one.
            var before = spawner.Spawned.Count;

            spawner.OnMovement(_mover, new Point3D(_mover.X + 1, _mover.Y, _mover.Z));

            NeverNested &= spawner.Spawned.Count == before;

            return Point3D.Zero;
        }
    }

    /// <summary>
    /// A positioning rule that raises an <em>external</em> event - the script / command entry point -
    /// from inside the cycle it is positioning for. Unlike the proximity probe this needs no trigger
    /// definitions, so the spawner it watches ticks on its own timer.
    /// </summary>
    private sealed class ExternalTriggerProbeRule : IPositioningRule
    {
        /// <summary>The rule name entries reference.</summary>
        public static readonly string Name = "d2_external_trigger_probe";

        private static ModernSpawner _watched;

        static ExternalTriggerProbeRule() => PositioningRules.Register(new ExternalTriggerProbeRule());

        /// <inheritdoc />
        public string RuleName => Name;

        /// <inheritdoc />
        public string Description => "Test probe: calls Trigger() from inside a cycle.";

        /// <summary>Starts raising external events for one spawner.</summary>
        /// <param name="spawner">The spawner whose cycles raise the event.</param>
        public static void Watch(ModernSpawner spawner)
        {
            Reset();
            _watched = spawner;
        }

        /// <summary>Stops raising events.</summary>
        public static void Reset() => _watched = null;

        /// <inheritdoc />
        public Point3D GetPosition(PositioningContext context)
        {
            if (context.Spawner == _watched)
            {
                context.Spawner.Trigger();
            }

            return Point3D.Zero;
        }
    }
}
