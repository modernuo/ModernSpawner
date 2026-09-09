using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Server.Engines.Events;

namespace Server.Engines.ModernSpawner.Serialization;

/// <summary>
/// Human-readable JSON export format for spawner configuration.
/// This is used for file-based export/import, not internal serialization.
/// </summary>
public class SpawnerExportData
{
    [JsonPropertyName("$schema")]
    public string Schema { get; set; } = "modernspawner/v1/spawner.json";

    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("location")]
    public LocationData Location { get; set; }

    [JsonPropertyName("timing")]
    public TimingData Timing { get; set; }

    [JsonPropertyName("area")]
    public AreaData Area { get; set; }

    [JsonPropertyName("entries")]
    public List<SpawnEntryData> Entries { get; set; } = [];

    [JsonPropertyName("triggers")]
    public List<TriggerData> Triggers { get; set; } = [];

    [JsonPropertyName("scripts")]
    public ScriptsData Scripts { get; set; }

    [JsonPropertyName("options")]
    public OptionsData Options { get; set; }
}

public class LocationData
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("z")]
    public int Z { get; set; }

    [JsonPropertyName("map")]
    public Map Map { get; set; }
}

public class TimingData
{
    [JsonPropertyName("minDelay")]
    public TimeSpan MinDelay { get; set; }

    [JsonPropertyName("maxDelay")]
    public TimeSpan MaxDelay { get; set; }
}

public class AreaData
{
    [JsonPropertyName("homeRange")]
    public int HomeRange { get; set; }

    [JsonPropertyName("spawnRange")]
    public int SpawnRange { get; set; }

    [JsonPropertyName("spawnArea")]
    public SpawnAreaData SpawnArea { get; set; }
}

public class SpawnAreaData
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }
}

public class SpawnEntryData
{
    [JsonPropertyName("type")]
    public Type Type { get; set; }

    [JsonPropertyName("maxCount")]
    public int MaxCount { get; set; }

    [JsonPropertyName("probability")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Probability { get; set; } = 100;

    [JsonPropertyName("subgroup")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Subgroup { get; set; }

    [JsonPropertyName("properties")]
    public Dictionary<string, PropertyValueData> Properties { get; set; }

    [JsonPropertyName("condition")]
    public ConditionData Condition { get; set; }

    [JsonPropertyName("onBeforeSpawn")]
    public string OnBeforeSpawn { get; set; }

    [JsonPropertyName("onAfterSpawn")]
    public string OnAfterSpawn { get; set; }
}

public class PropertyValueData
{
    [JsonPropertyName("type")]
    public string Type { get; set; } // "fixed", "random", "expression"

    [JsonPropertyName("value")]
    public object Value { get; set; }

    [JsonPropertyName("min")]
    public int? Min { get; set; }

    [JsonPropertyName("max")]
    public int? Max { get; set; }

    [JsonPropertyName("expression")]
    public string Expression { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }
}

public class ConditionData
{
    [JsonPropertyName("expression")]
    public string Expression { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }
}

public class TriggerData
{
    [JsonPropertyName("type")]
    public string Type { get; set; }

    // Proximity trigger
    [JsonPropertyName("range")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Range { get; set; }

    [JsonPropertyName("playerOnly")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool PlayerOnly { get; set; }

    // Speech trigger
    [JsonPropertyName("keyword")]
    public string Keyword { get; set; }

    // Time window triggers
    [JsonPropertyName("startTime")]
    public string StartTime { get; set; }

    [JsonPropertyName("endTime")]
    public string EndTime { get; set; }

    [JsonPropertyName("startHour")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int StartHour { get; set; }

    [JsonPropertyName("endHour")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int EndHour { get; set; }

    [JsonPropertyName("nightOnly")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool NightOnly { get; set; }

    [JsonPropertyName("dayOnly")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool DayOnly { get; set; }

    [JsonPropertyName("allowedDays")]
    [JsonConverter(typeof(FlagsArrayConverter<AllowedDays>))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public AllowedDays AllowedDays { get; set; }

    [JsonPropertyName("allowedMonths")]
    [JsonConverter(typeof(FlagsArrayConverter<AllowedMonths>))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public AllowedMonths AllowedMonths { get; set; }

    [JsonPropertyName("timeZone")]
    public string TimeZone { get; set; }

    // Kill trigger
    [JsonPropertyName("requiredKills")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int RequiredKills { get; set; }

    [JsonPropertyName("requireAllDead")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RequireAllDead { get; set; }

    // Common
    [JsonPropertyName("cooldown")]
    public TimeSpan? Cooldown { get; set; }
}

public class ScriptsData
{
    [JsonPropertyName("onActivate")]
    public string OnActivate { get; set; }

    [JsonPropertyName("onDeactivate")]
    public string OnDeactivate { get; set; }

    [JsonPropertyName("onBeforeSpawn")]
    public string OnBeforeSpawn { get; set; }

    [JsonPropertyName("onAfterSpawn")]
    public string OnAfterSpawn { get; set; }
}

public class OptionsData
{
    // Always serialised: `WhenWritingDefault` would compare against `default(bool)` (false),
    // so setting `SmartPositioning = false` would be skipped and round-trip back to the
    // initializer value (`true`). Writing the field unconditionally guarantees round-trip.
    [JsonPropertyName("smartPositioning")]
    public bool SmartPositioning { get; set; } = true;

    [JsonPropertyName("returnToSpawnOnIdle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ReturnToSpawnOnIdle { get; set; }

    // Always serialised for the same reason as `SmartPositioning`: `MaxZDelta = 0` is
    // a meaningful "no Z check" value distinct from the initializer default of 20.
    [JsonPropertyName("maxZDelta")]
    public int MaxZDelta { get; set; } = 20;

    [JsonPropertyName("triggerActivated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool TriggerActivated { get; set; }

    [JsonPropertyName("cycleMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public SpawnCycleMode CycleMode { get; set; }

    [JsonPropertyName("currentSubgroup")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int CurrentSubgroup { get; set; }

    [JsonPropertyName("sequentialResetTime")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public TimeSpan SequentialResetTime { get; set; }

    [JsonPropertyName("sequentialResetTo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int SequentialResetTo { get; set; }

    [JsonPropertyName("holdSequence")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HoldSequence { get; set; }
}
