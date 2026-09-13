using System;
using System.Collections.Generic;
using System.Text.Json;
using Server.Engines.ModernSpawner.Triggers;
using Server.Engines.Spawners;
using Server.Mobiles;
using Xunit;

namespace Server.Engines.ModernSpawner.Tests;

/// <summary>
/// End-to-end tests over a live ModernUO world: the spawner is placed on a real map and the base
/// entry-ownership contract (Spawned registry, Defrag, Respawn, Dupe, DTO and binary round trips)
/// is exercised against <see cref="ModernSpawnerEntry" />.
/// </summary>
[Collection("Sequential ModernSpawner Tests")]
public class ModernSpawnerLifecycleTests
{
    private static ModernSpawner Place(params ReadOnlySpan<string> names)
    {
        var spawner = new ModernSpawner(1, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), 0, default, names);
        spawner.MoveToWorld(new Point3D(1500, 1500, 0), Map.Felucca);
        return spawner;
    }

    private static void DeleteSpawned(ModernSpawner spawner)
    {
        foreach (var spawned in new List<ISpawnable>(spawner.Spawned.Keys))
        {
            spawned.Delete();
        }
    }

    private static ModernSpawnerDto MakeDto(bool triggerActivated, params string[] triggers) =>
        new()
        {
            Guid = Guid.NewGuid(),
            Location = new Point3D(1500, 1500, 0),
            Map = Map.Felucca,
            Count = 1,
            MinDelay = TimeSpan.FromMinutes(5),
            MaxDelay = TimeSpan.FromMinutes(10),
            HomeRange = 5,
            Entries = [new ModernSpawnerEntry("Rabbit")],
            TriggerActivated = triggerActivated,
            Triggers = MakeTriggerDtos(triggers)
        };

    private static List<TriggerDefinitionDto> MakeTriggerDtos(params string[] triggers)
    {
        var list = new List<TriggerDefinitionDto>(triggers.Length);
        foreach (var text in triggers)
        {
            list.Add(new TriggerDefinitionDto { Id = Guid.CreateVersion7(), Text = text });
        }

        return list;
    }

    [Fact]
    public void Constructor_NamesLandInModernEntries()
    {
        var spawner = Place("Rabbit", "Bird");

        Assert.Equal(2, spawner.ModernEntries.Count);
        Assert.Same(spawner.ModernEntries[0], spawner.Entries[0]);
        Assert.Same(spawner.ModernEntries[1], spawner.Entries[1]);

        spawner.Delete();
    }

    [Fact]
    public void Spawn_Kill_Respawn_UsesOneRegistry()
    {
        var spawner = Place("Rabbit");
        spawner.Spawn();

        var rabbit = Assert.Single(spawner.Spawned).Key as Mobile;
        Assert.NotNull(rabbit);
        Assert.Single(spawner.ModernEntries[0].Spawned);

        rabbit.Delete(); // Mobile.OnDelete -> BaseSpawner.Remove
        Assert.Empty(spawner.Spawned);
        Assert.Empty(spawner.ModernEntries[0].Spawned);

        spawner.Spawn();
        Assert.Single(spawner.Spawned);
        Assert.Single(spawner.ModernEntries[0].Spawned);

        DeleteSpawned(spawner);
        spawner.Delete();
    }

    [Fact]
    public void Stop_Then_Start_Works_AndFiresActivateScript()
    {
        var spawner = Place("Rabbit");
        spawner.SetOnActivateScript("SETVAR/activated/1");
        Assert.True(spawner.OnActivateScript.IsValid);

        spawner.Stop();
        Assert.False(spawner.Running);

        spawner.Start();
        Assert.True(spawner.Running);

        DeleteSpawned(spawner);
        spawner.Delete();
    }

    [Fact]
    public void Respawn_DoesNotDuplicate()
    {
        var spawner = Place("Rabbit");
        spawner.Spawn();
        Assert.Single(spawner.Spawned);

        spawner.Respawn();

        Assert.Single(spawner.Spawned);
        Assert.Single(spawner.ModernEntries[0].Spawned);

        DeleteSpawned(spawner);
        spawner.Delete();
    }

    [Fact]
    public void Dupe_ClonesModernFields()
    {
        var spawner = Place("Rabbit");

        // Every field ModernSpawner.CloneEntry copies, each given a value distinct from its default,
        // so dropping any single line from CloneEntry fails this test.
        var source = spawner.ModernEntries[0];
        source.OnSpawnScript = "SET/Name/on spawn";
        source.OnDespawnScript = "SET/Name/on despawn";
        source.MinDelay = TimeSpan.FromSeconds(11);
        source.MaxDelay = TimeSpan.FromSeconds(22);
        source.PositioningRule = "circle";
        source.SpawnGroup = "wave one";
        source.RequireLOS = true;
        source.SpawnAreaOffset = new Point3D(3, -4, 5);
        source.SpawnRange = 7;
        source.LootTemplate = "goblin";
        source.Subgroup = 3;
        // Carried by the base SpawnerEntry clone rather than the modern override.
        source.Disabled = true;

        // [SerializedIgnoreDupe] keeps the reflection dupe off the trigger list, so OnAfterDuped has
        // to copy it by hand - otherwise the copy is TriggerActivated with nothing to activate.
        spawner.TriggerActivated = true;
        spawner.AddTriggerDefinition("proximity:8:true:false:5:0");

        // Runtime state the copy must NOT inherit: a duped spawner starts its trigger life clean.
        spawner.MaxPendingCycles = 2;
        spawner.RefractoryUntil = Core.Now + TimeSpan.FromMinutes(5);
        spawner.EnqueuePendingForTest(spawner.TriggerDefinitions[0].Id, (Serial)0x4000BEEFu);
        spawner.GetTriggerState(spawner.TriggerDefinitions[0].Id).KillCount = 4;

        var copy = new ModernSpawner();
        spawner.Dupe(copy);

        Assert.True(copy.TriggerActivated);
        Assert.Equal("proximity:8:true:false:5:0", Assert.Single(copy.TriggerDefinitions).Text);
        // Ids travel with the copy, so its runtime state keys line up with its definitions.
        Assert.Equal(spawner.TriggerDefinitions[0].Id, copy.TriggerDefinitions[0].Id);
        // Its own list, not the source's - editing one spawner's triggers must not touch the other.
        Assert.NotSame(spawner.TriggerDefinitions, copy.TriggerDefinitions);
        Assert.NotSame(spawner.TriggerDefinitions[0], copy.TriggerDefinitions[0]);
        // And registered, so the copy actually listens for the trigger it carries.
        Assert.True(copy.HandlesOnMovement);

        // ...but none of the source's runtime state came with it: no queued cycles, no lockout, and
        // a fresh (empty) state for the definition it inherited.
        Assert.Equal(0, copy.PendingCycleCount);
        Assert.Equal(default, copy.RefractoryUntil);
        var copiedState = copy.GetTriggerState(copy.TriggerDefinitions[0].Id);
        Assert.NotNull(copiedState);
        Assert.Equal(0, copiedState.KillCount);
        Assert.Equal(default, copiedState.CooldownUntil);

        var clone = Assert.Single(copy.ModernEntries);
        Assert.Equal("SET/Name/on spawn", clone.OnSpawnScript);
        Assert.Equal("SET/Name/on despawn", clone.OnDespawnScript);
        Assert.Equal(TimeSpan.FromSeconds(11), clone.MinDelay);
        Assert.Equal(TimeSpan.FromSeconds(22), clone.MaxDelay);
        Assert.Equal("circle", clone.PositioningRule);
        Assert.Equal("wave one", clone.SpawnGroup);
        Assert.True(clone.RequireLOS);
        Assert.Equal(new Point3D(3, -4, 5), clone.SpawnAreaOffset);
        Assert.Equal(7, clone.SpawnRange);
        Assert.Equal("goblin", clone.LootTemplate);
        Assert.Equal(3, clone.Subgroup);
        Assert.True(clone.Disabled);

        // A deep copy parented to the new spawner, not the source entry shared between the two.
        Assert.NotSame(source, clone);
        Assert.Same(clone, copy.Entries[0]);

        spawner.Delete();
        copy.Delete();
    }

    [Fact]
    public void Dto_RoundTrip_CarriesEntriesTriggersAndCycleState()
    {
        var spawner = Place("Rabbit");
        spawner.ModernEntries[0].LootTemplate = "goblin";
        spawner.CycleMode = SpawnCycleMode.Sequential;
        // The base all-dead-then-respawn flag: binary-persisted, and easy to lose on the DTO path.
        spawner.Group = true;
        spawner.AddTriggerDefinition("proximity:8:true");

        var json = SpawnerJsonSerializer.SerializeCompact<List<SpawnerDto>>([spawner.ToDto()]);
        var dtos = JsonSerializer.Deserialize<List<SpawnerDto>>(json, SpawnerJsonSerializer.Options);
        var loaded = (ModernSpawner)dtos[0].ToSpawner();

        Assert.Equal("goblin", loaded.ModernEntries[0].LootTemplate);
        Assert.Equal(SpawnCycleMode.Sequential, loaded.CycleMode);
        Assert.True(loaded.Group);
        Assert.Equal("proximity:8:true", Assert.Single(loaded.TriggerDefinitions).Text);

        DeleteSpawned(loaded);
        loaded.Delete();
        spawner.Delete();
    }

    [Fact]
    public void Dto_WithTriggers_RegistersThemOnImport()
    {
        // ToSpawner hands back a spawner that is already running, so Start() - and with it OnStarted -
        // never fires for the definitions the DTO just applied. ToSpawner has to register them itself,
        // or an imported spawner's triggers stay inert until someone cycles it.
        var loaded = (ModernSpawner)MakeDto(true, "proximity:8:true:false:5:0").ToSpawner();
        loaded.MoveToWorld(new Point3D(1500, 1500, 0), Map.Felucca);

        Assert.True(loaded.Running);
        Assert.True(loaded.HandlesOnMovement);

        // The same import with no triggers must not arm movement dispatch.
        var plain = (ModernSpawner)MakeDto(false).ToSpawner();
        plain.MoveToWorld(new Point3D(1502, 1502, 0), Map.Felucca);

        Assert.False(plain.HandlesOnMovement);

        DeleteSpawned(loaded);
        loaded.Delete();
        DeleteSpawned(plain);
        plain.Delete();
    }

    [Fact]
    public void Binary_RoundTrip_RebuildsSpawnedOverModernEntries()
    {
        var spawner = Place("Rabbit");
        spawner.ModernEntries[0].Subgroup = 2;
        spawner.Spawn();
        Assert.Single(spawner.Spawned);

        var writer = new BufferWriter(true);
        spawner.Serialize(writer);
        var bytes = writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();

        var loaded = new ModernSpawner((Serial)0x40004242u);
        loaded.Deserialize(new BufferReader(bytes));

        Assert.Equal(2, loaded.ModernEntries[0].Subgroup);
        Assert.Single(loaded.ModernEntries[0].Spawned);
        Assert.Single(loaded.Spawned);

        loaded.Delete();
        DeleteSpawned(spawner);
        spawner.Delete();
    }

    [Fact]
    public void Kill_DispatchesOnDespawnScriptAndKillTrigger()
    {
        var spawner = Place("Rabbit");

        // SET writes to the ScriptContext's target, which OnSpawnedDeath binds to the dying entity,
        // so the script leaves a mark on the creature itself. SETVAR would only touch a per-context
        // variable dictionary that is discarded when execution ends.
        spawner.ModernEntries[0].OnDespawnScript = "SET/Name/despawn script ran";

        // kill:requiredKills:requireAllDead:resetOnTrigger:filterType:requirePlayerKiller:cooldownSeconds
        spawner.TriggerActivated = true;
        spawner.AddTriggerDefinition("kill:1:false:true:any:false:0");

        // The TriggerActivated setter registers on the spot (A1), so no Stop/Start cycle is needed.
        Assert.True(TriggerSystem.Instance.IsRegistered(spawner));
        Assert.True(spawner.Running);
        Assert.Equal(0, spawner.PendingCycleCount);

        spawner.Spawn();
        var rabbit = (BaseCreature)Assert.Single(spawner.Spawned).Key;
        Assert.NotEqual("despawn script ran", rabbit.Name);

        rabbit.Kill();

        // OnSpawnedDeath compiled and ran the entry's OnDespawnScript against the dying creature...
        Assert.Equal("despawn script ran", rabbit.Name);
        // ...and handed the kill to TriggerSystem, whose KillTrigger bought a cycle. The dying rabbit
        // is still in the registry at that point, so the spawner is full and the cycle is held as a
        // queued slot (E2) rather than run on the spot.
        Assert.Equal(1, spawner.PendingCycleCount);

        rabbit.Corpse?.Delete();
        DeleteSpawned(spawner);
        spawner.Delete();
    }

    [Fact]
    public void Binary_RoundTrip_CarriesTriggerIdsRuntimeStateAndPendingSlots()
    {
        var spawner = Place("Rabbit");
        spawner.TriggerActivated = true;
        spawner.AddTriggerDefinition("proximity:8:true");
        spawner.AddTriggerDefinition("kill:1:false:true:any:false:0");
        spawner.MaxPendingCycles = 2;
        spawner.RefractoryMin = TimeSpan.FromSeconds(3);
        spawner.RefractoryMax = TimeSpan.FromSeconds(9);

        var proximityId = spawner.TriggerDefinitions[0].Id;
        var killId = spawner.TriggerDefinitions[1].Id;
        Assert.NotEqual(Guid.Empty, proximityId);
        Assert.NotEqual(proximityId, killId);

        var cooldownUntil = new DateTime(2026, 9, 12, 3, 4, 5, DateTimeKind.Utc);
        var nextEligible = new DateTime(2026, 9, 12, 6, 7, 8, DateTimeKind.Utc);
        var refractoryUntil = new DateTime(2026, 9, 12, 9, 10, 11, DateTimeKind.Utc);

        var killState = spawner.GetTriggerState(killId);
        Assert.NotNull(killState);
        killState.KillCount = 3;
        killState.CooldownUntil = cooldownUntil;

        spawner.RefractoryUntil = refractoryUntil;
        spawner.ModernEntries[0].NextEligible = nextEligible;

        var mobile = (Serial)0x40001234u;
        Assert.True(spawner.EnqueuePendingForTest(proximityId, mobile));
        Assert.Equal(1, spawner.PendingCycleCount);

        var writer = new BufferWriter(true);
        spawner.Serialize(writer);
        var bytes = writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();

        var loaded = new ModernSpawner((Serial)0x40004243u);
        loaded.Deserialize(new BufferReader(bytes));

        Assert.Equal(2, loaded.TriggerDefinitions.Count);
        Assert.Equal(proximityId, loaded.TriggerDefinitions[0].Id);
        Assert.Equal("proximity:8:true", loaded.TriggerDefinitions[0].Text);
        Assert.Equal(killId, loaded.TriggerDefinitions[1].Id);
        Assert.Equal("kill:1:false:true:any:false:0", loaded.TriggerDefinitions[1].Text);

        Assert.Equal(2, loaded.MaxPendingCycles);
        Assert.Equal(TimeSpan.FromSeconds(3), loaded.RefractoryMin);
        Assert.Equal(TimeSpan.FromSeconds(9), loaded.RefractoryMax);
        Assert.Equal(refractoryUntil, loaded.RefractoryUntil);

        var slot = Assert.Single(loaded.PendingCycles);
        Assert.Equal(proximityId, slot.TriggerId);
        Assert.Equal(mobile, slot.TriggeringMobile);

        var loadedKillState = loaded.GetTriggerState(killId);
        Assert.NotNull(loadedKillState);
        Assert.Equal(3, loadedKillState.KillCount);
        Assert.Equal(cooldownUntil, loadedKillState.CooldownUntil);

        Assert.Equal(nextEligible, loaded.ModernEntries[0].NextEligible);

        loaded.Delete();
        DeleteSpawned(spawner);
        spawner.Delete();
    }

    [Fact]
    public void Dto_RoundTrip_CarriesTriggerIdsAndLimits()
    {
        var spawner = Place("Rabbit");
        spawner.AddTriggerDefinition("proximity:8:true");
        spawner.AddTriggerDefinition("speech:aGVsbG8=:true:false:10:true:5");
        spawner.MaxPendingCycles = 3;
        spawner.RefractoryMin = TimeSpan.FromSeconds(4);
        spawner.RefractoryMax = TimeSpan.FromSeconds(12);

        var firstId = spawner.TriggerDefinitions[0].Id;
        var secondId = spawner.TriggerDefinitions[1].Id;

        // Runtime state exists on the source but must never reach the export.
        spawner.EnqueuePendingForTest(firstId, (Serial)0x40005678u);
        spawner.GetTriggerState(secondId).KillCount = 7;
        spawner.ModernEntries[0].NextEligible = new DateTime(2026, 9, 12, 1, 2, 3, DateTimeKind.Utc);

        var json = SpawnerJsonSerializer.SerializeCompact<List<SpawnerDto>>([spawner.ToDto()]);

        Assert.DoesNotContain("\"pendingCycles\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"triggerStates\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"killCount\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"cooldownUntil\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"nextEligible\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"refractoryUntil\"", json, StringComparison.Ordinal);

        var dtos = JsonSerializer.Deserialize<List<SpawnerDto>>(json, SpawnerJsonSerializer.Options);
        var loaded = (ModernSpawner)dtos[0].ToSpawner();

        Assert.Equal(2, loaded.TriggerDefinitions.Count);
        Assert.Equal(firstId, loaded.TriggerDefinitions[0].Id);
        Assert.Equal("proximity:8:true", loaded.TriggerDefinitions[0].Text);
        Assert.Equal(secondId, loaded.TriggerDefinitions[1].Id);
        Assert.Equal("speech:aGVsbG8=:true:false:10:true:5", loaded.TriggerDefinitions[1].Text);

        Assert.Equal(3, loaded.MaxPendingCycles);
        Assert.Equal(TimeSpan.FromSeconds(4), loaded.RefractoryMin);
        Assert.Equal(TimeSpan.FromSeconds(12), loaded.RefractoryMax);

        // Runtime state is world-save only: an imported spawner starts clean.
        Assert.Equal(0, loaded.PendingCycleCount);
        Assert.Equal(default, loaded.ModernEntries[0].NextEligible);

        DeleteSpawned(loaded);
        loaded.Delete();
        DeleteSpawned(spawner);
        spawner.Delete();
    }

    [Fact]
    public void RemoveTriggerDefinitionAt_DropsItsStateAndPendingSlots()
    {
        var spawner = Place("Rabbit");
        spawner.TriggerActivated = true;
        spawner.AddTriggerDefinition("proximity:8:true");
        spawner.AddTriggerDefinition("kill:1:false:true:any:false:0");

        var proximityId = spawner.TriggerDefinitions[0].Id;
        var killId = spawner.TriggerDefinitions[1].Id;
        spawner.MaxPendingCycles = 4;
        spawner.EnqueuePendingForTest(proximityId, Serial.Zero);
        spawner.EnqueuePendingForTest(killId, Serial.Zero);
        Assert.Equal(2, spawner.PendingCycleCount);
        Assert.Equal(2, spawner.TriggerStates.Count);

        spawner.RemoveTriggerDefinitionAt(0);

        Assert.Equal(killId, Assert.Single(spawner.TriggerDefinitions).Id);
        Assert.Equal(killId, Assert.Single(spawner.PendingCycles).TriggerId);
        Assert.Equal(killId, Assert.Single(spawner.TriggerStates).Id);

        spawner.ClearTriggerDefinitions();

        Assert.Empty(spawner.TriggerDefinitions);
        Assert.Empty(spawner.TriggerStates);
        Assert.Equal(0, spawner.PendingCycleCount);

        DeleteSpawned(spawner);
        spawner.Delete();
    }

    [Fact]
    public void MaxPendingCycles_ClampsAndTrimsOldestSlots()
    {
        var spawner = Place("Rabbit");
        spawner.AddTriggerDefinition("proximity:8:true");
        var id = spawner.TriggerDefinitions[0].Id;

        spawner.MaxPendingCycles = 3;
        spawner.EnqueuePendingForTest(id, (Serial)0x40000001u);
        spawner.EnqueuePendingForTest(id, (Serial)0x40000002u);
        spawner.EnqueuePendingForTest(id, (Serial)0x40000003u);
        // Bounded: the fourth is rejected rather than growing the queue.
        Assert.False(spawner.EnqueuePendingForTest(id, (Serial)0x40000004u));
        Assert.Equal(3, spawner.PendingCycleCount);

        // Lowering trims the oldest slots first (A5).
        spawner.MaxPendingCycles = 1;
        Assert.Equal(1, spawner.PendingCycleCount);
        Assert.Equal((Serial)0x40000003u, spawner.PendingCycles[0].TriggeringMobile);

        // Clamped at zero, never negative.
        spawner.MaxPendingCycles = -5;
        Assert.Equal(0, spawner.MaxPendingCycles);
        Assert.Equal(0, spawner.PendingCycleCount);

        DeleteSpawned(spawner);
        spawner.Delete();
    }

    /// <summary>
    /// Writes the <see cref="SpawnerEntry"/> layer plus a v0 <see cref="ModernSpawnerEntry"/> payload.
    /// The base layer comes from a stock entry because <c>ModernSpawnerEntry.Serialize</c> opens with
    /// <c>base.Serialize(writer)</c>, which is exactly what a stock entry writes.
    /// </summary>
    private static void WriteLegacyEntry(BufferWriter writer, BaseSpawner parent)
    {
        new SpawnerEntry(parent, "Rabbit").Serialize(writer);

        writer.WriteEncodedInt(0);                  // ModernSpawnerEntry v0
        writer.Write("SET/Name/on spawn");          // 0  OnSpawnScript
        writer.Write("SET/Name/on despawn");        // 1  OnDespawnScript
        writer.Write(TimeSpan.FromSeconds(11));     // 2  MinDelay
        writer.Write(TimeSpan.FromSeconds(22));     // 3  MaxDelay
        writer.Write("circle");                     // 4  PositioningRule
        writer.Write("wave one");                   // 5  SpawnGroup
        writer.Write(true);                         // 6  RequireLOS
        writer.Write(new Point3D(3, -4, 5));        // 7  SpawnAreaOffset
        writer.Write(7);                            // 8  SpawnRange
        writer.Write("goblin");                     // 9  LootTemplate
        writer.Write(3);                            // 10 Subgroup
    }

    [Fact]
    public void Binary_V0Save_MigratesDefinitionsToIdsAndDropsTriggered()
    {
        // A v0 world save, byte for byte. The Item/BaseSpawner/Spawner layers come from a stock
        // Spawner because ModernSpawner.Serialize opens with base.Serialize(writer).
        var legacy = new Spawner();
        legacy.MoveToWorld(new Point3D(1500, 1500, 0), Map.Felucca);

        var writer = new BufferWriter(true);
        legacy.Serialize(writer);

        writer.WriteEncodedInt(0);                  // ModernSpawner v0
        writer.WriteEncodedInt(1);                  // 0  SpawnEntries count
        WriteLegacyEntry(writer, legacy);
        writer.Write(Serial.Zero);                  // 1  OnActivateScriptSerial
        writer.Write(Serial.Zero);                  // 2  OnDeactivateScriptSerial
        writer.Write(Serial.Zero);                  // 3  OnBeforeSpawnScriptSerial
        writer.Write(Serial.Zero);                  // 4  OnAfterSpawnScriptSerial
        writer.Write(false);                        // 5  UseSmartPositioning
        writer.Write(true);                         // 6  ReturnToSpawnOnIdle
        writer.Write(33);                           // 7  MaxZDelta
        writer.WriteEncodedInt(2);                  // 8  TriggerDefinitions count
        writer.Write("proximity:8:true");
        writer.Write("kill:1:false:true:any:false:0");
        writer.Write(true);                         // 9  TriggerActivated
        writer.Write(true);                         // 10 Triggered - dropped by the migration
        writer.Write("legacy notes");               // 11 Notes
        writer.WriteEnum(SpawnCycleMode.Sequential); // 12 CycleMode
        writer.Write(4);                            // 13 CurrentSubgroup
        writer.Write(TimeSpan.FromMinutes(7));      // 14 SequentialResetTime
        writer.Write(2);                            // 15 SequentialResetTo
        writer.Write(true);                         // 16 HoldSequence

        var bytes = writer.Buffer.AsSpan(0, (int)writer.Position).ToArray();

        var loaded = new ModernSpawner((Serial)0x40004244u);
        loaded.Deserialize(new BufferReader(bytes));

        // Every kept spawner field survives, in the right slot.
        Assert.False(loaded.UseSmartPositioning);
        Assert.True(loaded.ReturnToSpawnOnIdle);
        Assert.Equal(33, loaded.MaxZDelta);
        Assert.True(loaded.TriggerActivated);
        Assert.Equal("legacy notes", loaded.Notes);
        Assert.Equal(SpawnCycleMode.Sequential, loaded.CycleMode);
        Assert.Equal(4, loaded.CurrentSubgroup);
        Assert.Equal(TimeSpan.FromMinutes(7), loaded.SequentialResetTime);
        Assert.Equal(2, loaded.SequentialResetTo);
        Assert.True(loaded.HoldSequence);

        // The string list became identified definitions, in order, with fresh distinct ids...
        Assert.Equal(2, loaded.TriggerDefinitions.Count);
        Assert.Equal("proximity:8:true", loaded.TriggerDefinitions[0].Text);
        Assert.Equal("kill:1:false:true:any:false:0", loaded.TriggerDefinitions[1].Text);
        Assert.NotEqual(Guid.Empty, loaded.TriggerDefinitions[0].Id);
        Assert.NotEqual(loaded.TriggerDefinitions[0].Id, loaded.TriggerDefinitions[1].Id);

        // ...each with bound, empty runtime state, and the new fields at their defaults.
        Assert.Equal(2, loaded.TriggerStates.Count);
        Assert.NotNull(loaded.GetTriggerState(loaded.TriggerDefinitions[0].Id));
        Assert.NotNull(loaded.GetTriggerState(loaded.TriggerDefinitions[1].Id));
        Assert.Equal(0, loaded.GetTriggerState(loaded.TriggerDefinitions[1].Id).KillCount);
        Assert.Equal(1, loaded.MaxPendingCycles);
        Assert.Equal(0, loaded.PendingCycleCount);
        Assert.Equal(TimeSpan.Zero, loaded.RefractoryMin);
        Assert.Equal(TimeSpan.Zero, loaded.RefractoryMax);
        Assert.Equal(default, loaded.RefractoryUntil);

        // The nested entry migrated too: every v0 field kept, NextEligible new and clear.
        var entry = Assert.Single(loaded.ModernEntries);
        Assert.Equal("Rabbit", entry.SpawnedName);
        Assert.Equal("SET/Name/on spawn", entry.OnSpawnScript);
        Assert.Equal("SET/Name/on despawn", entry.OnDespawnScript);
        Assert.Equal(TimeSpan.FromSeconds(11), entry.MinDelay);
        Assert.Equal(TimeSpan.FromSeconds(22), entry.MaxDelay);
        Assert.Equal("circle", entry.PositioningRule);
        Assert.Equal("wave one", entry.SpawnGroup);
        Assert.True(entry.RequireLOS);
        Assert.Equal(new Point3D(3, -4, 5), entry.SpawnAreaOffset);
        Assert.Equal(7, entry.SpawnRange);
        Assert.Equal("goblin", entry.LootTemplate);
        Assert.Equal(3, entry.Subgroup);
        Assert.Equal(default, entry.NextEligible);

        loaded.Delete();
        legacy.Delete();
    }
    [Fact]
    public void Dto_DuplicateTriggerIds_AreGivenDistinctIds()
    {
        // A hand-edited export, or a trigger block copied between spawners: two definitions arriving
        // with one id would alias onto a single TriggerRuntimeState and onto each other's slots.
        var shared = Guid.CreateVersion7();
        var dto = MakeDto(true);
        var loaded = (ModernSpawner)(dto with
        {
            Triggers =
            [
                new TriggerDefinitionDto { Id = shared, Text = "proximity:8:true" },
                new TriggerDefinitionDto { Id = shared, Text = "kill:1:false:true:any:false:0" }
            ]
        }).ToSpawner();
        loaded.MoveToWorld(new Point3D(1500, 1500, 0), Map.Felucca);

        Assert.Equal(2, loaded.TriggerDefinitions.Count);
        Assert.NotEqual(loaded.TriggerDefinitions[0].Id, loaded.TriggerDefinitions[1].Id);
        Assert.Equal(shared, loaded.TriggerDefinitions[0].Id);
        Assert.NotEqual(Guid.Empty, loaded.TriggerDefinitions[1].Id);

        // One state per definition, each reachable by its own id.
        Assert.Equal(2, loaded.TriggerStates.Count);
        Assert.NotSame(
            loaded.GetTriggerState(loaded.TriggerDefinitions[0].Id),
            loaded.GetTriggerState(loaded.TriggerDefinitions[1].Id));

        // The same guard covers the wrapper, not just the DTO path.
        loaded.AddTriggerDefinition(shared, "speech:aGVsbG8=:true:false:10:true:5");
        Assert.Equal(3, loaded.TriggerDefinitions.Count);
        Assert.NotEqual(shared, loaded.TriggerDefinitions[2].Id);
        Assert.Equal(3, loaded.TriggerStates.Count);

        DeleteSpawned(loaded);
        loaded.Delete();
    }

    [Fact]
    public void Dto_LegacyTriggerStringShape_StillImports()
    {
        // Files exported before definitions had ids wrote "triggers": [ "proximity:8:true" ].
        const string legacy = """
            [
              {
                "$type": "ModernSpawner",
                "location": "(1500, 1500, 0)",
                "map": "Felucca",
                "count": 1,
                "minDelay": "00:05:00",
                "maxDelay": "00:10:00",
                "homeRange": 5,
                "entries": [ { "name": "Rabbit", "probability": 100, "maxCount": 1 } ],
                "triggerActivated": true,
                "triggers": [ "proximity:8:true", "kill:1:false:true:any:false:0" ]
              }
            ]
            """;

        var dtos = JsonSerializer.Deserialize<List<SpawnerDto>>(legacy, SpawnerJsonSerializer.Options);
        var loaded = (ModernSpawner)dtos[0].ToSpawner();

        Assert.Equal(2, loaded.TriggerDefinitions.Count);
        Assert.Equal("proximity:8:true", loaded.TriggerDefinitions[0].Text);
        Assert.Equal("kill:1:false:true:any:false:0", loaded.TriggerDefinitions[1].Text);

        // Ids are minted on the way in, so the definitions are usable state keys immediately.
        Assert.NotEqual(Guid.Empty, loaded.TriggerDefinitions[0].Id);
        Assert.NotEqual(loaded.TriggerDefinitions[0].Id, loaded.TriggerDefinitions[1].Id);
        Assert.Equal(2, loaded.TriggerStates.Count);

        // Re-exporting writes the object shape, which reads back with the ids intact.
        var json = SpawnerJsonSerializer.SerializeCompact<List<SpawnerDto>>([loaded.ToDto()]);
        Assert.Contains("\"text\": \"proximity:8:true\"", json, StringComparison.Ordinal);

        var reloaded = (ModernSpawner)JsonSerializer
            .Deserialize<List<SpawnerDto>>(json, SpawnerJsonSerializer.Options)[0].ToSpawner();

        Assert.Equal(loaded.TriggerDefinitions[0].Id, reloaded.TriggerDefinitions[0].Id);
        Assert.Equal(loaded.TriggerDefinitions[1].Id, reloaded.TriggerDefinitions[1].Id);

        DeleteSpawned(reloaded);
        reloaded.Delete();
        DeleteSpawned(loaded);
        loaded.Delete();
    }
}
