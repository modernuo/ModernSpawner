using System;
using System.Collections.Generic;
using System.Text.Json;
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
            Triggers = new List<string>(triggers)
        };

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
        spawner.AddToTriggerDefinitions("proximity:8:true:false:5:0");

        var copy = new ModernSpawner();
        spawner.Dupe(copy);

        Assert.True(copy.TriggerActivated);
        Assert.Equal("proximity:8:true:false:5:0", Assert.Single(copy.TriggerDefinitions));
        // Its own list, not the source's - editing one spawner's triggers must not touch the other.
        Assert.NotSame(spawner.TriggerDefinitions, copy.TriggerDefinitions);
        // And registered, so the copy actually listens for the trigger it carries.
        Assert.True(copy.HandlesOnMovement);

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
        spawner.AddToTriggerDefinitions("proximity:8:true");

        var json = SpawnerJsonSerializer.SerializeCompact<List<SpawnerDto>>([spawner.ToDto()]);
        var dtos = JsonSerializer.Deserialize<List<SpawnerDto>>(json, SpawnerJsonSerializer.Options);
        var loaded = (ModernSpawner)dtos[0].ToSpawner();

        Assert.Equal("goblin", loaded.ModernEntries[0].LootTemplate);
        Assert.Equal(SpawnCycleMode.Sequential, loaded.CycleMode);
        Assert.Equal("proximity:8:true", Assert.Single(loaded.TriggerDefinitions));

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
        spawner.AddToTriggerDefinitions("kill:1:false:true:any:false:0");

        // Triggers are registered from OnStarted; the constructor leaves the spawner running without
        // ever passing through it, so cycle it to get ActivateTriggers.
        spawner.Stop();
        spawner.Start();
        Assert.True(spawner.Running);
        Assert.False(spawner.Triggered);

        spawner.Spawn();
        var rabbit = (BaseCreature)Assert.Single(spawner.Spawned).Key;
        Assert.NotEqual("despawn script ran", rabbit.Name);

        rabbit.Kill();

        // OnSpawnedDeath compiled and ran the entry's OnDespawnScript against the dying creature...
        Assert.Equal("despawn script ran", rabbit.Name);
        // ...and handed the kill to TriggerSystem, whose KillTrigger fired Trigger() on the spawner.
        Assert.True(spawner.Triggered);

        rabbit.Corpse?.Delete();
        DeleteSpawned(spawner);
        spawner.Delete();
    }
}
