using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Server.Json;

namespace Server.Engines.ModernSpawner.Serialization;

/// <summary>
/// Imports ModernSpawner configurations from human-readable JSON format.
/// </summary>
public static class SpawnerJsonImporter
{
    private static readonly JsonSerializerOptions JsonImportOptions = JsonConfig.GetOptions();

    /// <summary>
    /// Parses JSON string to export data model.
    /// </summary>
    public static SpawnerExportData FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("JSON string cannot be empty", nameof(json));
        }

        return JsonSerializer.Deserialize<SpawnerExportData>(json, JsonImportOptions);
    }

    /// <summary>
    /// Parses JSON array string to list of export data models.
    /// </summary>
    public static List<SpawnerExportData> FromJsonArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("JSON string cannot be empty", nameof(json));
        }

        return JsonSerializer.Deserialize<List<SpawnerExportData>>(json, JsonImportOptions);
    }

    /// <summary>
    /// Loads export data from a JSON file.
    /// </summary>
    public static SpawnerExportData FromFile(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return FromJson(json);
    }

    /// <summary>
    /// Loads multiple export data from a JSON array file.
    /// </summary>
    public static List<SpawnerExportData> FromArrayFile(string filePath)
    {
        var json = File.ReadAllText(filePath);

        // Check if it's an array or single object
        var span = json.AsSpan().TrimStart();
        if (span.Length > 0 && span[0] == '[')
        {
            return FromJsonArray(json);
        }

        // Single object, wrap in list
        return [FromJson(json)];
    }

    /// <summary>
    /// Creates and configures a new ModernSpawner from export data.
    /// </summary>
    /// <param name="data">The export data to import.</param>
    /// <param name="map">The map to place the spawner on. If null, uses the map from export data.</param>
    /// <returns>A configured ModernSpawner instance (not yet added to world).</returns>
    public static ModernSpawner CreateSpawner(SpawnerExportData data, Map map = null)
    {
        ArgumentNullException.ThrowIfNull(data);

        // Resolve map - use provided map, or from location data, or default to Felucca
        map ??= data.Location?.Map ?? Map.Felucca;
        if (map == null || map == Map.Internal)
        {
            throw new InvalidOperationException("Could not resolve map from export data");
        }

        var spawner = new ModernSpawner();

        // Set location
        if (data.Location != null)
        {
            spawner.MoveToWorld(new Point3D(data.Location.X, data.Location.Y, data.Location.Z), map);
        }

        // Set name
        if (!string.IsNullOrEmpty(data.Name))
        {
            spawner.Name = data.Name;
        }

        // Set timing
        if (data.Timing != null)
        {
            spawner.MinDelay = data.Timing.MinDelay;
            spawner.MaxDelay = data.Timing.MaxDelay;
        }

        // Set area
        if (data.Area != null)
        {
            spawner.HomeRange = data.Area.HomeRange;

            if (data.Area.SpawnArea != null)
            {
                var area = data.Area.SpawnArea;
                spawner.SpawnBounds = new Rectangle3D(
                    area.X, area.Y, sbyte.MinValue,
                    area.Width, area.Height, sbyte.MaxValue - sbyte.MinValue);
            }
        }

        // Import entries
        if (data.Entries != null)
        {
            foreach (var entryData in data.Entries)
            {
                ImportEntry(spawner, entryData);
            }
        }

        // Import triggers
        if (data.Triggers != null)
        {
            ImportTriggers(spawner, data.Triggers);
        }

        // Import scripts
        if (data.Scripts != null)
        {
            ImportScripts(spawner, data.Scripts);
        }

        // Import options
        if (data.Options != null)
        {
            ImportSpawnerOptions(spawner, data.Options);
        }

        // The spawner was constructed running, so OnStarted never ran for the imported definitions.
        // Options carry TriggerActivated, so this has to come after them.
        spawner.EnsureTriggersActive();

        return spawner;
    }

    /// <summary>
    /// Configures an existing ModernSpawner from export data.
    /// Does not change location.
    /// </summary>
    public static void ConfigureSpawner(ModernSpawner spawner, SpawnerExportData data)
    {
        ArgumentNullException.ThrowIfNull(spawner);

        ArgumentNullException.ThrowIfNull(data);

        // Set name if provided
        if (!string.IsNullOrEmpty(data.Name))
        {
            spawner.Name = data.Name;
        }

        // Set timing
        if (data.Timing != null)
        {
            spawner.MinDelay = data.Timing.MinDelay;
            spawner.MaxDelay = data.Timing.MaxDelay;
        }

        // Set area
        if (data.Area != null)
        {
            spawner.HomeRange = data.Area.HomeRange;

            if (data.Area.SpawnArea != null)
            {
                var area = data.Area.SpawnArea;
                spawner.SpawnBounds = new Rectangle3D(
                    area.X, area.Y, sbyte.MinValue,
                    area.Width, area.Height, sbyte.MaxValue - sbyte.MinValue);
            }
        }

        // Clear existing entries and import new ones
        spawner.RemoveAllEntries();
        if (data.Entries != null)
        {
            foreach (var entryData in data.Entries)
            {
                ImportEntry(spawner, entryData);
            }
        }

        // Import triggers (clears existing)
        if (data.Triggers != null)
        {
            // Through the wrapper so the spawner is marked dirty and the runtime state of the
            // definitions being replaced goes with them.
            spawner.ClearTriggerDefinitions();

            ImportTriggers(spawner, data.Triggers);
        }

        // Import scripts
        if (data.Scripts != null)
        {
            ImportScripts(spawner, data.Scripts);
        }

        // Import options
        if (data.Options != null)
        {
            ImportSpawnerOptions(spawner, data.Options);
        }

        // Re-register: the definitions were replaced wholesale and TriggerActivated may have changed.
        spawner.EnsureTriggersActive();
    }

    private static void ImportEntry(ModernSpawner spawner, SpawnEntryData entryData)
    {
        if (entryData.Type == null)
        {
            return;
        }

        // Convert properties back to string format
        var properties = ConvertPropertiesToString(entryData.Properties);

        // Check for despawn script in special property
        string onDespawnScript = null;
        if (entryData.Properties != null &&
            entryData.Properties.TryGetValue("__onDespawn", out var despawnProp))
        {
            onDespawnScript = despawnProp.Expression;
        }

        var entry = spawner.AddModernEntry(
            creatureName: entryData.Type.Name,
            probability: entryData.Probability > 0 ? entryData.Probability : 100,
            maxCount: entryData.MaxCount > 0 ? entryData.MaxCount : 1,
            properties: properties,
            onSpawnScript: entryData.OnAfterSpawn,
            onDespawnScript: onDespawnScript,
            dotimer: false // Don't start timer until fully configured
        );
        entry.Subgroup = entryData.Subgroup;
    }

    private static string ConvertPropertiesToString(Dictionary<string, PropertyValueData> properties)
    {
        if (properties == null || properties.Count == 0)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var (key, value) in properties)
        {
            // Skip internal properties
            if (key.AsSpan().StartsWith("__".AsSpan(), StringComparison.Ordinal))
            {
                continue;
            }

            string propString;
            var typeSpan = value.Type.AsSpan();

            if (typeSpan.InsensitiveEquals("random"))
            {
                propString = $"{key}={{{value.Min}-{value.Max}}}";
            }
            else if (typeSpan.InsensitiveEquals("expression"))
            {
                propString = $"{key}={value.Expression}";
            }
            else // "fixed" or unspecified
            {
                propString = $"{key}={value.Value}";
            }

            parts.Add(propString);
        }

        return parts.Count > 0 ? string.Join("/", parts) : null;
    }

    private static void ImportTriggers(ModernSpawner spawner, List<TriggerData> triggers)
    {
        foreach (var trigger in triggers)
        {
            var definition = BuildTriggerDefinition(trigger);
            if (!string.IsNullOrEmpty(definition))
            {
                spawner.AddTriggerDefinition(definition);
            }
        }

        // Enable trigger activation if triggers are defined
        if (triggers.Count > 0)
        {
            spawner.TriggerActivated = true;
        }
    }

    private static string BuildTriggerDefinition(TriggerData trigger)
    {
        if (string.IsNullOrEmpty(trigger.Type))
        {
            return null;
        }

        var typeSpan = trigger.Type.AsSpan();

        if (typeSpan.InsensitiveEquals("proximity"))
        {
            return $"proximity:{trigger.Range}:{trigger.PlayerOnly}";
        }
        if (typeSpan.InsensitiveEquals("speech"))
        {
            return $"speech:{trigger.Keyword}";
        }
        if (typeSpan.InsensitiveEquals("kill"))
        {
            return $"kill:{trigger.RequiredKills}:{trigger.RequireAllDead}";
        }
        if (typeSpan.InsensitiveEquals("timeofday"))
        {
            // "timeofday" is retired: it maps onto the game-time window, whose end hour is exclusive
            // where the legacy one was inclusive.
            var endHour = Math.Clamp(trigger.EndHour, 0, 23) + 1;
            return $"game_time_window:{Math.Clamp(trigger.StartHour, 0, 23)}:{endHour}:{trigger.NightOnly}:{trigger.DayOnly}";
        }
        if (typeSpan.InsensitiveEquals("game_time_window"))
        {
            return $"game_time_window:{trigger.StartHour}:{trigger.EndHour}:{trigger.NightOnly}:{trigger.DayOnly}";
        }
        if (typeSpan.InsensitiveEquals("wall_time_window"))
        {
            return BuildWallTimeDefinition(trigger);
        }

        return null;
    }

    private static string BuildWallTimeDefinition(TriggerData trigger)
    {
        var startParts = ParseTimeString(trigger.StartTime);
        var endParts = ParseTimeString(trigger.EndTime);

        return $"wall_time_window:{startParts.hour}:{startParts.minute}:{endParts.hour}:{endParts.minute}:{(int)trigger.AllowedDays}:{(int)trigger.AllowedMonths}:{trigger.TimeZone ?? "UTC"}";
    }

    private static (int hour, int minute) ParseTimeString(string time)
    {
        if (string.IsNullOrEmpty(time))
        {
            return (0, 0);
        }

        var span = time.AsSpan();
        var colonIndex = span.IndexOf(':');

        if (colonIndex <= 0)
        {
            return int.TryParse(span, out var h) ? (h, 0) : (0, 0);
        }

        var hour = int.TryParse(span[..colonIndex], out var hVal) ? hVal : 0;
        var minute = int.TryParse(span[(colonIndex + 1)..], out var mVal) ? mVal : 0;
        return (hour, minute);
    }

    private static void ImportScripts(ModernSpawner spawner, ScriptsData scripts)
    {
        if (!string.IsNullOrEmpty(scripts.OnActivate))
        {
            spawner.SetOnActivateScript(scripts.OnActivate);
        }
        if (!string.IsNullOrEmpty(scripts.OnDeactivate))
        {
            spawner.SetOnDeactivateScript(scripts.OnDeactivate);
        }
        if (!string.IsNullOrEmpty(scripts.OnBeforeSpawn))
        {
            spawner.SetOnBeforeSpawnScript(scripts.OnBeforeSpawn);
        }
        if (!string.IsNullOrEmpty(scripts.OnAfterSpawn))
        {
            spawner.SetOnAfterSpawnScript(scripts.OnAfterSpawn);
        }
    }

    private static void ImportSpawnerOptions(ModernSpawner spawner, OptionsData options)
    {
        spawner.UseSmartPositioning = options.SmartPositioning;
        spawner.ReturnToSpawnOnIdle = options.ReturnToSpawnOnIdle;
        spawner.MaxZDelta = options.MaxZDelta;
        spawner.TriggerActivated = options.TriggerActivated;
        spawner.CycleMode = options.CycleMode;
        spawner.CurrentSubgroup = options.CurrentSubgroup;
        spawner.SequentialResetTime = options.SequentialResetTime;
        spawner.SequentialResetTo = options.SequentialResetTo;
        spawner.HoldSequence = options.HoldSequence;
    }
}
