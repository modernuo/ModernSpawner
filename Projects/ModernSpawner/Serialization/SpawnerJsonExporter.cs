using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Server.Engines.Events;
using Server.Json;

namespace Server.Engines.ModernSpawner.Serialization;

/// <summary>
/// Exports ModernSpawner configurations to human-readable JSON format.
/// </summary>
public static class SpawnerJsonExporter
{
    private static readonly JsonSerializerOptions JsonExportOptions = JsonConfig.GetOptions();

    /// <summary>
    /// Exports a spawner to its JSON representation.
    /// </summary>
    public static SpawnerExportData ToExportData(ModernSpawner spawner)
    {
        ArgumentNullException.ThrowIfNull(spawner);

        var export = new SpawnerExportData
        {
            Id = spawner.Serial.ToString(),
            Name = spawner.Name,
            Location = new LocationData
            {
                X = spawner.X,
                Y = spawner.Y,
                Z = spawner.Z,
                Map = spawner.Map
            },
            Timing = new TimingData
            {
                MinDelay = spawner.MinDelay,
                MaxDelay = spawner.MaxDelay
            },
            Area = new AreaData
            {
                HomeRange = spawner.HomeRange,
                SpawnRange = spawner.HomeRange
            }
        };

        // Export spawn area if defined
        var spawnArea = spawner.SpawnArea;
        if (spawnArea is { Width: > 0, Height: > 0 })
        {
            export.Area.SpawnArea = new SpawnAreaData
            {
                X = spawnArea.Start.X,
                Y = spawnArea.Start.Y,
                Width = spawnArea.Width,
                Height = spawnArea.Height
            };
        }

        // Export entries
        export.Entries = [];
        foreach (var entry in spawner.ModernEntries)
        {
            export.Entries.Add(ExportEntry(entry));
        }

        // Export triggers
        export.Triggers = ExportTriggers(spawner);

        // Export scripts
        export.Scripts = ExportScripts(spawner);

        // Export options
        export.Options = ExportSpawnerOptions(spawner);

        return export;
    }

    /// <summary>
    /// Exports a spawner directly to a JSON string.
    /// </summary>
    public static string ToJson(ModernSpawner spawner)
    {
        var exportData = ToExportData(spawner);
        return JsonSerializer.Serialize(exportData, JsonExportOptions);
    }

    /// <summary>
    /// Exports a spawner to a JSON file.
    /// </summary>
    public static void ToFile(ModernSpawner spawner, string filePath)
    {
        var json = ToJson(spawner);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Exports multiple spawners to a JSON array file.
    /// </summary>
    public static void ToFile(IEnumerable<ModernSpawner> spawners, string filePath)
    {
        var exports = new List<SpawnerExportData>();
        foreach (var spawner in spawners)
        {
            exports.Add(ToExportData(spawner));
        }

        var json = JsonSerializer.Serialize(exports, JsonExportOptions);
        File.WriteAllText(filePath, json);
    }

    private static SpawnEntryData ExportEntry(ModernSpawnerEntry entry)
    {
        var entryData = new SpawnEntryData
        {
            Type = AssemblyHandler.FindTypeByName(entry.SpawnedName),
            MaxCount = entry.SpawnedMaxCount,
            Probability = entry.SpawnedProbability,
            Subgroup = entry.Subgroup
        };

        // Parse and export properties if present
        if (!string.IsNullOrEmpty(entry.Properties))
        {
            entryData.Properties = ParsePropertiesToDictionary(entry.Properties);
        }

        // Export entry-level scripts
        if (!string.IsNullOrEmpty(entry.OnSpawnScript))
        {
            entryData.OnAfterSpawn = entry.OnSpawnScript;
        }

        if (!string.IsNullOrEmpty(entry.OnDespawnScript))
        {
            // Store despawn script in a special property since it's not in the base model
            entryData.Properties ??= new Dictionary<string, PropertyValueData>();
            entryData.Properties["__onDespawn"] = new PropertyValueData
            {
                Type = "expression",
                Expression = entry.OnDespawnScript
            };
        }

        return entryData;
    }

    private static Dictionary<string, PropertyValueData> ParsePropertiesToDictionary(string properties)
    {
        if (string.IsNullOrWhiteSpace(properties))
        {
            return null;
        }

        var result = new Dictionary<string, PropertyValueData>();
        var span = properties.AsSpan();

        // Properties are typically in format: "PropName=Value/PropName2=Value2"
        foreach (var pairRange in span.Split('/'))
        {
            var pair = span[pairRange].Trim();
            if (pair.IsEmpty)
            {
                continue;
            }

            var equalsIndex = pair.IndexOf('=');
            if (equalsIndex <= 0)
            {
                continue;
            }

            var propName = pair[..equalsIndex].Trim().ToString();
            var propValue = pair[(equalsIndex + 1)..].Trim();

            // Determine property type based on value format
            if (propValue.Length > 2 && propValue[0] == '{' && propValue.Contains("-".AsSpan(), StringComparison.Ordinal))
            {
                // Random range format: {min-max}
                var rangeValue = propValue[1..^1]; // Remove { and }
                var dashIndex = rangeValue.IndexOf('-');
                if (dashIndex > 0 &&
                    int.TryParse(rangeValue[..dashIndex], out var min) &&
                    int.TryParse(rangeValue[(dashIndex + 1)..], out var max))
                {
                    result[propName] = new PropertyValueData
                    {
                        Type = "random",
                        Min = min,
                        Max = max
                    };
                    continue;
                }
            }

            // Fixed value
            result[propName] = new PropertyValueData
            {
                Type = "fixed",
                Value = ParsePropertyValue(propValue)
            };
        }

        return result.Count > 0 ? result : null;
    }

    private static object ParsePropertyValue(ReadOnlySpan<char> value)
    {
        // Try to parse as int
        if (int.TryParse(value, out var intValue))
        {
            return intValue;
        }

        // Try to parse as double
        if (double.TryParse(value, out var doubleValue))
        {
            return doubleValue;
        }

        // Try to parse as bool
        if (value.InsensitiveEquals("true"))
        {
            return true;
        }

        if (value.InsensitiveEquals("false"))
        {
            return false;
        }

        // Return as string
        return value.ToString();
    }

    private static List<TriggerData> ExportTriggers(ModernSpawner spawner)
    {
        var triggerDefs = spawner.TriggerDefinitions;
        if (triggerDefs == null || triggerDefs.Count == 0)
        {
            return null;
        }

        var triggers = new List<TriggerData>();
        foreach (var definition in triggerDefs)
        {
            var triggerData = ParseTriggerDefinition(definition);
            if (triggerData != null)
            {
                triggers.Add(triggerData);
            }
        }

        return triggers.Count > 0 ? triggers : null;
    }

    private static TriggerData ParseTriggerDefinition(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
        {
            return null;
        }

        var span = definition.AsSpan();

        // Find first colon to get trigger type
        var colonIndex = span.IndexOf(':');
        var triggerTypeSpan = colonIndex > 0 ? span[..colonIndex] : span;

        // Stack-allocate for small trigger types
        Span<char> lowerBuffer = stackalloc char[triggerTypeSpan.Length];
        triggerTypeSpan.ToLowerInvariant(lowerBuffer);
        var triggerType = lowerBuffer.ToString();

        var triggerData = new TriggerData { Type = triggerType };

        // Parse remaining parts using span-based splitting
        Span<Range> partRanges = stackalloc Range[8]; // Max 8 parts
        var partCount = span.Split(partRanges, ':');

        if (triggerType.InsensitiveEquals("proximity"))
        {
            if (partCount > 1 && int.TryParse(span[partRanges[1]], out var range))
            {
                triggerData.Range = range;
            }
            if (partCount > 2 && bool.TryParse(span[partRanges[2]], out var playerOnly))
            {
                triggerData.PlayerOnly = playerOnly;
            }
        }
        else if (triggerType.InsensitiveEquals("speech"))
        {
            if (partCount > 1)
            {
                triggerData.Keyword = span[partRanges[1]].ToString();
            }
        }
        else if (triggerType.InsensitiveEquals("kill"))
        {
            if (partCount > 1 && int.TryParse(span[partRanges[1]], out var kills))
            {
                triggerData.RequiredKills = kills;
            }
            if (partCount > 2 && bool.TryParse(span[partRanges[2]], out var allDead))
            {
                triggerData.RequireAllDead = allDead;
            }
        }
        else if (triggerType.InsensitiveEquals("timeofday"))
        {
            if (partCount > 1 && int.TryParse(span[partRanges[1]], out var startHour))
            {
                triggerData.StartHour = startHour;
            }
            if (partCount > 2 && int.TryParse(span[partRanges[2]], out var endHour))
            {
                triggerData.EndHour = endHour;
            }
        }
        else if (triggerType.InsensitiveEquals("game_time_window"))
        {
            if (partCount > 1 && int.TryParse(span[partRanges[1]], out var gameStart))
            {
                triggerData.StartHour = gameStart;
            }
            if (partCount > 2 && int.TryParse(span[partRanges[2]], out var gameEnd))
            {
                triggerData.EndHour = gameEnd;
            }
            if (partCount > 3 && bool.TryParse(span[partRanges[3]], out var nightOnly))
            {
                triggerData.NightOnly = nightOnly;
            }
            if (partCount > 4 && bool.TryParse(span[partRanges[4]], out var dayOnly))
            {
                triggerData.DayOnly = dayOnly;
            }
        }
        else if (triggerType.InsensitiveEquals("wall_time_window"))
        {
            if (partCount > 2 &&
                int.TryParse(span[partRanges[1]], out var wallStartHour) &&
                int.TryParse(span[partRanges[2]], out var wallStartMin))
            {
                triggerData.StartTime = $"{wallStartHour:D2}:{wallStartMin:D2}";
            }
            if (partCount > 4 &&
                int.TryParse(span[partRanges[3]], out var wallEndHour) &&
                int.TryParse(span[partRanges[4]], out var wallEndMin))
            {
                triggerData.EndTime = $"{wallEndHour:D2}:{wallEndMin:D2}";
            }
            if (partCount > 5 && int.TryParse(span[partRanges[5]], out var days))
            {
                triggerData.AllowedDays = (AllowedDays)days;
            }
            if (partCount > 6 && int.TryParse(span[partRanges[6]], out var months))
            {
                triggerData.AllowedMonths = (AllowedMonths)months;
            }
            if (partCount > 7)
            {
                triggerData.TimeZone = span[partRanges[7]].ToString();
            }
        }

        return triggerData;
    }

    private static ScriptsData ExportScripts(ModernSpawner spawner)
    {
        var scripts = new ScriptsData();
        var hasScripts = false;

        var onActivate = spawner.OnActivateScript?.Source;
        if (!string.IsNullOrEmpty(onActivate))
        {
            scripts.OnActivate = onActivate;
            hasScripts = true;
        }

        var onDeactivate = spawner.OnDeactivateScript?.Source;
        if (!string.IsNullOrEmpty(onDeactivate))
        {
            scripts.OnDeactivate = onDeactivate;
            hasScripts = true;
        }

        var onBeforeSpawn = spawner.OnBeforeSpawnScript?.Source;
        if (!string.IsNullOrEmpty(onBeforeSpawn))
        {
            scripts.OnBeforeSpawn = onBeforeSpawn;
            hasScripts = true;
        }

        var onAfterSpawn = spawner.OnAfterSpawnScript?.Source;
        if (!string.IsNullOrEmpty(onAfterSpawn))
        {
            scripts.OnAfterSpawn = onAfterSpawn;
            hasScripts = true;
        }

        return hasScripts ? scripts : null;
    }

    private static OptionsData ExportSpawnerOptions(ModernSpawner spawner)
    {
        var options = new OptionsData
        {
            SmartPositioning = spawner.UseSmartPositioning,
            ReturnToSpawnOnIdle = spawner.ReturnToSpawnOnIdle,
            MaxZDelta = spawner.MaxZDelta,
            TriggerActivated = spawner.TriggerActivated,
            CycleMode = spawner.CycleMode,
            CurrentSubgroup = spawner.CurrentSubgroup,
            SequentialResetTime = spawner.SequentialResetTime,
            SequentialResetTo = spawner.SequentialResetTo,
            HoldSequence = spawner.HoldSequence
        };

        // Only return options if any non-initializer-default value exists.
        // Note: `SmartPositioning = true` and `MaxZDelta = 20` are the initializer
        // defaults — those are the values we treat as "unset", not the CLR `default(T)`.
        if (options.SmartPositioning &&
            !options.ReturnToSpawnOnIdle &&
            options.MaxZDelta == 20 &&
            !options.TriggerActivated &&
            options.CycleMode == SpawnCycleMode.Random &&
            options.CurrentSubgroup == 0 &&
            options.SequentialResetTime == TimeSpan.Zero &&
            options.SequentialResetTo == 0 &&
            !options.HoldSequence)
        {
            return null;
        }

        return options;
    }
}
