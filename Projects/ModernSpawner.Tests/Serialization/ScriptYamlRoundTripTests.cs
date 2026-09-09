using System.Collections.Generic;
using Server.Engines.ModernSpawner.Serialization;
using Xunit;

namespace Server.Engines.ModernSpawner.Tests.Serialization;

public class ScriptYamlRoundTripTests
{
    private static ScriptExportData RoundTrip(ScriptExportData source)
    {
        var yaml = ScriptYamlSerializer.ToYaml(source);
        return ScriptYamlSerializer.FromYaml(yaml);
    }

    [Fact]
    public void TopLevelFields_RoundTrip()
    {
        var source = new ScriptExportData
        {
            Name = "Boss Encounter",
            Description = "Multi-phase dragon fight"
        };

        var restored = RoundTrip(source);

        Assert.Equal("Boss Encounter", restored.Name);
        Assert.Equal("Multi-phase dragon fight", restored.Description);
    }

    [Fact]
    public void Schema_DefaultIsPreserved()
    {
        var source = new ScriptExportData { Name = "schema test" };

        var yaml = ScriptYamlSerializer.ToYaml(source);
        Assert.Contains("modernspawner/v1/script.yaml", yaml);

        var restored = ScriptYamlSerializer.FromYaml(yaml);
        Assert.Equal("modernspawner/v1/script.yaml", restored.Schema);
    }

    [Fact]
    public void ActionData_SpawnWithSubgroup_RoundTrip()
    {
        var source = new ScriptExportData
        {
            Name = "phase one",
            OnActivate = new List<ScriptActionData>
            {
                new()
                {
                    Action = "spawn",
                    Spawner = "dragon_minions",
                    Subgroup = 2,
                    Description = "Call in the first wave"
                }
            }
        };

        var restored = RoundTrip(source);

        Assert.Single(restored.OnActivate);
        var action = restored.OnActivate[0];
        Assert.Equal("spawn", action.Action);
        Assert.Equal("dragon_minions", action.Spawner);
        Assert.Equal(2, action.Subgroup);
        Assert.Equal("Call in the first wave", action.Description);
    }

    [Fact]
    public void ActionData_BroadcastWithRangeAndHue_RoundTrip()
    {
        var source = new ScriptExportData
        {
            OnAfterSpawn = new List<ScriptActionData>
            {
                new()
                {
                    Action = "broadcast",
                    Message = "A dragon has appeared!",
                    Range = 50,
                    Hue = 33
                }
            }
        };

        var restored = RoundTrip(source);

        Assert.Single(restored.OnAfterSpawn);
        var action = restored.OnAfterSpawn[0];
        Assert.Equal("broadcast", action.Action);
        Assert.Equal("A dragon has appeared!", action.Message);
        Assert.Equal(50, action.Range);
        Assert.Equal(33, action.Hue);
    }

    [Fact]
    public void ActionData_EffectAndSoundAndDelay_RoundTrip()
    {
        var source = new ScriptExportData
        {
            OnActivate = new List<ScriptActionData>
            {
                new()
                {
                    Action = "effect",
                    EffectId = 0x373A,
                    Duration = 2.5,
                    Delay = 1.0
                },
                new()
                {
                    Action = "sound",
                    SoundId = 0x208
                }
            }
        };

        var restored = RoundTrip(source);

        Assert.Equal(2, restored.OnActivate.Count);
        Assert.Equal(0x373A, restored.OnActivate[0].EffectId);
        Assert.Equal(2.5, restored.OnActivate[0].Duration);
        Assert.Equal(1.0, restored.OnActivate[0].Delay);
        Assert.Equal(0x208, restored.OnActivate[1].SoundId);
    }

    [Fact]
    public void ActionData_SetWithTargetAndProperties_RoundTrip()
    {
        var source = new ScriptExportData
        {
            OnAfterSpawn = new List<ScriptActionData>
            {
                new()
                {
                    Action = "set",
                    Target = new ActionTargetData { Spawned = true, Range = 5 },
                    Properties = new Dictionary<string, object>
                    {
                        ["Hue"] = 1150,
                        ["Name"] = "Elite Guard"
                    }
                }
            }
        };

        var restored = RoundTrip(source);

        Assert.Single(restored.OnAfterSpawn);
        var action = restored.OnAfterSpawn[0];
        Assert.Equal("set", action.Action);
        Assert.NotNull(action.Target);
        Assert.True(action.Target.Spawned);
        Assert.Equal(5, action.Target.Range);
        Assert.NotNull(action.Properties);
        Assert.Equal(2, action.Properties.Count);
    }

    [Fact]
    public void ActionData_WithCondition_RoundTrip()
    {
        var source = new ScriptExportData
        {
            OnActivate = new List<ScriptActionData>
            {
                new()
                {
                    Condition = "isNight()",
                    Action = "spawn",
                    Spawner = "nocturnal_pack"
                }
            }
        };

        var restored = RoundTrip(source);

        Assert.Single(restored.OnActivate);
        Assert.Equal("isNight()", restored.OnActivate[0].Condition);
        Assert.Equal("spawn", restored.OnActivate[0].Action);
    }

    [Fact]
    public void ActionData_NestedActions_RoundTrip()
    {
        var source = new ScriptExportData
        {
            OnActivate = new List<ScriptActionData>
            {
                new()
                {
                    Action = "sequence",
                    Actions = new List<ScriptActionData>
                    {
                        new() { Action = "sound", SoundId = 0x100 },
                        new() { Action = "effect", EffectId = 0x373A }
                    }
                }
            }
        };

        var restored = RoundTrip(source);

        Assert.Single(restored.OnActivate);
        var outer = restored.OnActivate[0];
        Assert.Equal("sequence", outer.Action);
        Assert.NotNull(outer.Actions);
        Assert.Equal(2, outer.Actions.Count);
        Assert.Equal(0x100, outer.Actions[0].SoundId);
        Assert.Equal(0x373A, outer.Actions[1].EffectId);
    }

    [Fact]
    public void ScriptEntry_WithPropertiesAndHooks_RoundTrip()
    {
        var source = new ScriptExportData
        {
            Entries = new List<ScriptEntryData>
            {
                new()
                {
                    Type = "Dragon",
                    MaxCount = 1,
                    Probability = 100,
                    Condition = new ScriptConditionData
                    {
                        Expression = "playersNearby(30) >= 5",
                        Description = "Only spawn with enough players"
                    },
                    Properties = new Dictionary<string, ScriptPropertyData>
                    {
                        ["Hue"] = new() { Type = "fixed", Value = 1150 },
                        ["Hits"] = new() { Type = "random", Min = 5000, Max = 8000 }
                    },
                    OnSpawn = new List<ScriptActionData>
                    {
                        new() { Action = "broadcast", Message = "The dragon awakens!" }
                    }
                }
            }
        };

        var restored = RoundTrip(source);

        Assert.Single(restored.Entries);
        var entry = restored.Entries[0];
        Assert.Equal("Dragon", entry.Type);
        Assert.Equal(1, entry.MaxCount);
        Assert.NotNull(entry.Condition);
        Assert.Equal("playersNearby(30) >= 5", entry.Condition.Expression);
        Assert.Equal(2, entry.Properties.Count);
        Assert.Equal("fixed", entry.Properties["Hue"].Type);
        Assert.Equal("random", entry.Properties["Hits"].Type);
        Assert.Equal(5000, entry.Properties["Hits"].Min);
        Assert.NotNull(entry.OnSpawn);
        Assert.Single(entry.OnSpawn);
        Assert.Equal("The dragon awakens!", entry.OnSpawn[0].Message);
    }

    [Fact]
    public void AllEventHandlers_RoundTrip()
    {
        var source = new ScriptExportData
        {
            OnActivate = new List<ScriptActionData> { new() { Action = "broadcast", Message = "activate" } },
            OnDeactivate = new List<ScriptActionData> { new() { Action = "broadcast", Message = "deactivate" } },
            OnBeforeSpawn = new List<ScriptActionData> { new() { Action = "broadcast", Message = "before" } },
            OnAfterSpawn = new List<ScriptActionData> { new() { Action = "broadcast", Message = "after" } },
            OnEntityKilled = new List<ScriptActionData> { new() { Action = "broadcast", Message = "killed" } }
        };

        var restored = RoundTrip(source);

        Assert.Equal("activate", restored.OnActivate[0].Message);
        Assert.Equal("deactivate", restored.OnDeactivate[0].Message);
        Assert.Equal("before", restored.OnBeforeSpawn[0].Message);
        Assert.Equal("after", restored.OnAfterSpawn[0].Message);
        Assert.Equal("killed", restored.OnEntityKilled[0].Message);
    }
}
