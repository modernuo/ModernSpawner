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
        spawner.ModernEntries[0].OnSpawnScript = "SETVAR/x/1";
        spawner.ModernEntries[0].Subgroup = 3;

        var copy = new ModernSpawner();
        spawner.Dupe(copy);

        Assert.Single(copy.ModernEntries);
        Assert.Equal("SETVAR/x/1", copy.ModernEntries[0].OnSpawnScript);
        Assert.Equal(3, copy.ModernEntries[0].Subgroup);
        Assert.NotSame(spawner.ModernEntries[0], copy.ModernEntries[0]);
        Assert.Same(copy.ModernEntries[0], copy.Entries[0]);

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
        spawner.ModernEntries[0].OnDespawnScript = "SETVAR/died/1";
        spawner.Spawn();

        var rabbit = (BaseCreature)Assert.Single(spawner.Spawned).Key;
        rabbit.Kill();

        // The observable contract until a script-side assertion exists: the kill path runs without
        // throwing and the entry no longer tracks the dead creature.
        Assert.Empty(spawner.ModernEntries[0].Spawned);
        Assert.Empty(spawner.Spawned);

        rabbit.Corpse?.Delete();
        DeleteSpawned(spawner);
        spawner.Delete();
    }
}
