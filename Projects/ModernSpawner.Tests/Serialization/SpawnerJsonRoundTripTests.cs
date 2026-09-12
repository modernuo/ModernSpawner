using System;
using System.Collections.Generic;
using System.Text.Json;
using Server.Engines.Events;
using Server.Engines.ModernSpawner.Serialization;
using Server.Json;
using Xunit;

namespace Server.Engines.ModernSpawner.Tests.Serialization;

public class SpawnerJsonRoundTripTests
{
    private static readonly JsonSerializerOptions Options = JsonConfig.GetOptions();

    private static SpawnerExportData RoundTrip(SpawnerExportData source)
    {
        var json = JsonSerializer.Serialize(source, Options);
        return SpawnerJsonImporter.FromJson(json);
    }

    [Fact]
    public void BasicFields_RoundTrip()
    {
        var source = new SpawnerExportData
        {
            Id = "0x40000001",
            Name = "Britain Guard Spawner",
            Location = new LocationData { X = 1234, Y = 5678, Z = 0 },
            Timing = new TimingData
            {
                MinDelay = TimeSpan.FromMinutes(5),
                MaxDelay = TimeSpan.FromMinutes(10)
            },
            Area = new AreaData { HomeRange = 10, SpawnRange = 15 }
        };

        var restored = RoundTrip(source);

        Assert.Equal("0x40000001", restored.Id);
        Assert.Equal("Britain Guard Spawner", restored.Name);
        Assert.Equal(1234, restored.Location.X);
        Assert.Equal(5678, restored.Location.Y);
        Assert.Equal(TimeSpan.FromMinutes(5), restored.Timing.MinDelay);
        Assert.Equal(TimeSpan.FromMinutes(10), restored.Timing.MaxDelay);
        Assert.Equal(10, restored.Area.HomeRange);
        Assert.Equal(15, restored.Area.SpawnRange);
    }

    [Fact]
    public void SpawnArea_RoundTrip()
    {
        var source = new SpawnerExportData
        {
            Area = new AreaData
            {
                HomeRange = 8,
                SpawnRange = 8,
                SpawnArea = new SpawnAreaData { X = 100, Y = 200, Width = 50, Height = 60 }
            }
        };

        var restored = RoundTrip(source);

        Assert.NotNull(restored.Area.SpawnArea);
        Assert.Equal(100, restored.Area.SpawnArea.X);
        Assert.Equal(200, restored.Area.SpawnArea.Y);
        Assert.Equal(50, restored.Area.SpawnArea.Width);
        Assert.Equal(60, restored.Area.SpawnArea.Height);
    }

    [Fact]
    public void Triggers_ProximityAndSpeech_RoundTrip()
    {
        var source = new SpawnerExportData
        {
            Triggers = new List<TriggerData>
            {
                new()
                {
                    Type = "proximity",
                    Range = 15,
                    PlayerOnly = true,
                    Cooldown = TimeSpan.FromSeconds(30)
                },
                new()
                {
                    Type = "speech",
                    Keyword = "open sesame"
                }
            }
        };

        var restored = RoundTrip(source);

        Assert.Equal(2, restored.Triggers.Count);

        var proximity = restored.Triggers[0];
        Assert.Equal("proximity", proximity.Type);
        Assert.Equal(15, proximity.Range);
        Assert.True(proximity.PlayerOnly);
        Assert.Equal(TimeSpan.FromSeconds(30), proximity.Cooldown);

        var speech = restored.Triggers[1];
        Assert.Equal("speech", speech.Type);
        Assert.Equal("open sesame", speech.Keyword);
    }

    [Fact]
    public void Triggers_WallTimeWindow_WithDaysAndMonths_RoundTrip()
    {
        var source = new SpawnerExportData
        {
            Triggers = new List<TriggerData>
            {
                new()
                {
                    Type = "walltime",
                    StartTime = "22:00",
                    EndTime = "06:00",
                    AllowedDays = AllowedDays.Friday | AllowedDays.Saturday,
                    AllowedMonths = AllowedMonths.October,
                    TimeZone = "UTC"
                }
            }
        };

        var restored = RoundTrip(source);

        Assert.Single(restored.Triggers);
        var t = restored.Triggers[0];
        Assert.Equal("walltime", t.Type);
        Assert.Equal("22:00", t.StartTime);
        Assert.Equal("06:00", t.EndTime);
        Assert.Equal(AllowedDays.Friday | AllowedDays.Saturday, t.AllowedDays);
        Assert.Equal(AllowedMonths.October, t.AllowedMonths);
        Assert.Equal("UTC", t.TimeZone);
    }

    [Fact]
    public void Triggers_Kill_RoundTrip()
    {
        var source = new SpawnerExportData
        {
            Triggers = new List<TriggerData>
            {
                new()
                {
                    Type = "kill",
                    RequiredKills = 5,
                    RequireAllDead = true
                }
            }
        };

        var restored = RoundTrip(source);

        Assert.Single(restored.Triggers);
        Assert.Equal(5, restored.Triggers[0].RequiredKills);
        Assert.True(restored.Triggers[0].RequireAllDead);
    }

    [Fact]
    public void Entries_WithProperties_RoundTrip()
    {
        var source = new SpawnerExportData
        {
            Entries = new List<SpawnEntryData>
            {
                new()
                {
                    MaxCount = 3,
                    Probability = 75,
                    Properties = new Dictionary<string, PropertyValueData>
                    {
                        ["Hue"] = new() { Type = "fixed", Value = 0x8000 },
                        ["Str"] = new() { Type = "random", Min = 80, Max = 100 },
                        ["Name"] = new() { Type = "expression", Expression = "\"Guard-\" + Random(1, 999)" }
                    }
                }
            }
        };

        var restored = RoundTrip(source);

        Assert.Single(restored.Entries);
        var entry = restored.Entries[0];
        Assert.Equal(3, entry.MaxCount);
        Assert.Equal(75, entry.Probability);
        Assert.Equal(3, entry.Properties.Count);
        Assert.Equal("fixed", entry.Properties["Hue"].Type);
        Assert.Equal("random", entry.Properties["Str"].Type);
        Assert.Equal(80, entry.Properties["Str"].Min);
        Assert.Equal(100, entry.Properties["Str"].Max);
        Assert.Equal("expression", entry.Properties["Name"].Type);
        Assert.Equal("\"Guard-\" + Random(1, 999)", entry.Properties["Name"].Expression);
    }

    [Fact]
    public void Entries_WithConditionAndHooks_RoundTrip()
    {
        var source = new SpawnerExportData
        {
            Entries = new List<SpawnEntryData>
            {
                new()
                {
                    MaxCount = 1,
                    Condition = new ConditionData
                    {
                        Expression = "isNight() && playersNearby(15) > 0",
                        Description = "Only at night with players"
                    },
                    OnBeforeSpawn = "Loot.Clear()",
                    OnAfterSpawn = "Loot.Add(\"Gold\", 500)"
                }
            }
        };

        var restored = RoundTrip(source);

        Assert.Single(restored.Entries);
        var entry = restored.Entries[0];
        Assert.NotNull(entry.Condition);
        Assert.Equal("isNight() && playersNearby(15) > 0", entry.Condition.Expression);
        Assert.Equal("Only at night with players", entry.Condition.Description);
        Assert.Equal("Loot.Clear()", entry.OnBeforeSpawn);
        Assert.Equal("Loot.Add(\"Gold\", 500)", entry.OnAfterSpawn);
    }

    [Fact]
    public void Scripts_RoundTrip()
    {
        var source = new SpawnerExportData
        {
            Scripts = new ScriptsData
            {
                OnActivate = "broadcast(\"Spawner active\")",
                OnDeactivate = "broadcast(\"Spawner inactive\")",
                OnBeforeSpawn = "Hue = 500",
                OnAfterSpawn = "Say(\"Hello\")"
            }
        };

        var restored = RoundTrip(source);

        Assert.NotNull(restored.Scripts);
        Assert.Equal("broadcast(\"Spawner active\")", restored.Scripts.OnActivate);
        Assert.Equal("broadcast(\"Spawner inactive\")", restored.Scripts.OnDeactivate);
        Assert.Equal("Hue = 500", restored.Scripts.OnBeforeSpawn);
        Assert.Equal("Say(\"Hello\")", restored.Scripts.OnAfterSpawn);
    }

    [Fact]
    public void Options_NonDefaults_RoundTrip()
    {
        var source = new SpawnerExportData
        {
            Options = new OptionsData
            {
                SmartPositioning = false,
                ReturnToSpawnOnIdle = true,
                MaxZDelta = 8,
                TriggerActivated = true
            }
        };

        var restored = RoundTrip(source);

        Assert.NotNull(restored.Options);
        Assert.False(restored.Options.SmartPositioning);
        Assert.True(restored.Options.ReturnToSpawnOnIdle);
        Assert.Equal(8, restored.Options.MaxZDelta);
        Assert.True(restored.Options.TriggerActivated);
    }

    [Fact]
    public void Options_SmartPositioningFalse_RoundTrip()
    {
        var source = new SpawnerExportData
        {
            Options = new OptionsData { SmartPositioning = false }
        };

        var restored = RoundTrip(source);

        Assert.False(restored.Options.SmartPositioning);
    }

    [Fact]
    public void Options_MaxZDeltaZero_RoundTrip()
    {
        var source = new SpawnerExportData
        {
            Options = new OptionsData { MaxZDelta = 0 }
        };

        var restored = RoundTrip(source);

        Assert.Equal(0, restored.Options.MaxZDelta);
    }

    [Fact]
    public void Schema_IsPreserved()
    {
        var source = new SpawnerExportData { Name = "schema test" };

        var json = JsonSerializer.Serialize(source, Options);
        Assert.Contains("modernspawner/v1/spawner.json", json);

        var restored = SpawnerJsonImporter.FromJson(json);
        Assert.Equal("modernspawner/v1/spawner.json", restored.Schema);
    }

    [Fact]
    public void Entry_Subgroup_RoundTrip()
    {
        var source = new SpawnerExportData
        {
            Entries = new List<SpawnEntryData>
            {
                new() { MaxCount = 1, Subgroup = 0 },
                new() { MaxCount = 2, Subgroup = 3 },
                new() { MaxCount = 1, Subgroup = 7 }
            }
        };

        var restored = RoundTrip(source);

        Assert.Equal(3, restored.Entries.Count);
        Assert.Equal(0, restored.Entries[0].Subgroup);
        Assert.Equal(3, restored.Entries[1].Subgroup);
        Assert.Equal(7, restored.Entries[2].Subgroup);
    }

    [Fact]
    public void Options_SequentialCycleMode_RoundTrip()
    {
        var source = new SpawnerExportData
        {
            Options = new OptionsData
            {
                CycleMode = SpawnCycleMode.Sequential,
                CurrentSubgroup = 2,
                SequentialResetTime = TimeSpan.FromMinutes(10),
                SequentialResetTo = 1,
                HoldSequence = true
            }
        };

        var restored = RoundTrip(source);

        Assert.NotNull(restored.Options);
        Assert.Equal(SpawnCycleMode.Sequential, restored.Options.CycleMode);
        Assert.Equal(2, restored.Options.CurrentSubgroup);
        Assert.Equal(TimeSpan.FromMinutes(10), restored.Options.SequentialResetTime);
        Assert.Equal(1, restored.Options.SequentialResetTo);
        Assert.True(restored.Options.HoldSequence);
    }

    [Fact]
    public void Options_AllEntriesCycleMode_RoundTrip()
    {
        var source = new SpawnerExportData
        {
            Options = new OptionsData
            {
                CycleMode = SpawnCycleMode.AllEntries
            }
        };

        var restored = RoundTrip(source);

        Assert.NotNull(restored.Options);
        Assert.Equal(SpawnCycleMode.AllEntries, restored.Options.CycleMode);
    }

    [Fact]
    public void Options_RandomCycleMode_Is_Default_And_Omitted_When_Alone()
    {
        var source = new SpawnerExportData
        {
            Options = new OptionsData { CycleMode = SpawnCycleMode.Random }
        };

        var json = JsonSerializer.Serialize(source, Options);

        // Random == default == omitted per WhenWritingDefault.
        Assert.DoesNotContain("cycleMode", json);

        var restored = SpawnerJsonImporter.FromJson(json);
        Assert.Equal(SpawnCycleMode.Random, restored.Options?.CycleMode ?? SpawnCycleMode.Random);
    }

    [Fact]
    public void Empty_Entries_And_Triggers_Default_To_Empty_Lists()
    {
        var source = new SpawnerExportData { Name = "empty" };

        var restored = RoundTrip(source);

        Assert.NotNull(restored.Entries);
        Assert.Empty(restored.Entries);
        Assert.NotNull(restored.Triggers);
        Assert.Empty(restored.Triggers);
    }
    [Fact]
    public void SpawnCycleMode_Group_AliasParses()
    {
        // "Group" was the exported name before the rename; saved files keep parsing into AllEntries.
        Assert.Equal(
            SpawnCycleMode.AllEntries,
            JsonSerializer.Deserialize<SpawnCycleMode>("\"Group\"", Options));

        // ...and the canonical name is what gets written back out.
        Assert.Equal("\"AllEntries\"", JsonSerializer.Serialize(SpawnCycleMode.AllEntries, Options));
    }
}
