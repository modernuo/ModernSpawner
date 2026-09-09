using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace Server.Engines.ModernSpawner.Serialization;

/// <summary>
/// YAML export format for complex spawner scripts.
/// Used for multi-phase encounters, boss fights, and event-driven spawning.
/// </summary>
public class ScriptExportData
{
    [YamlMember(Alias = "$schema")]
    public string Schema { get; set; } = "modernspawner/v1/script.yaml";

    [YamlMember(Alias = "name")]
    public string Name { get; set; }

    [YamlMember(Alias = "description")]
    public string Description { get; set; }

    [YamlMember(Alias = "entries")]
    public List<ScriptEntryData> Entries { get; set; } = [];

    [YamlMember(Alias = "onActivate")]
    public List<ScriptActionData> OnActivate { get; set; }

    [YamlMember(Alias = "onDeactivate")]
    public List<ScriptActionData> OnDeactivate { get; set; }

    [YamlMember(Alias = "onBeforeSpawn")]
    public List<ScriptActionData> OnBeforeSpawn { get; set; }

    [YamlMember(Alias = "onAfterSpawn")]
    public List<ScriptActionData> OnAfterSpawn { get; set; }

    [YamlMember(Alias = "onEntityKilled")]
    public List<ScriptActionData> OnEntityKilled { get; set; }
}

/// <summary>
/// Entry configuration within a script.
/// </summary>
public class ScriptEntryData
{
    [YamlMember(Alias = "type")]
    public string Type { get; set; }

    [YamlMember(Alias = "maxCount")]
    public int MaxCount { get; set; } = 1;

    [YamlMember(Alias = "probability")]
    public int Probability { get; set; } = 100;

    [YamlMember(Alias = "condition")]
    public ScriptConditionData Condition { get; set; }

    [YamlMember(Alias = "properties")]
    public Dictionary<string, ScriptPropertyData> Properties { get; set; }

    [YamlMember(Alias = "onSpawn")]
    public List<ScriptActionData> OnSpawn { get; set; }

    [YamlMember(Alias = "onDespawn")]
    public List<ScriptActionData> OnDespawn { get; set; }
}

/// <summary>
/// Condition configuration for entries and actions.
/// </summary>
public class ScriptConditionData
{
    [YamlMember(Alias = "expression")]
    public string Expression { get; set; }

    [YamlMember(Alias = "description")]
    public string Description { get; set; }
}

/// <summary>
/// Property value configuration supporting fixed, random, and expression values.
/// </summary>
public class ScriptPropertyData
{
    [YamlMember(Alias = "type")]
    public string Type { get; set; } // "fixed", "random", "expression"

    [YamlMember(Alias = "value")]
    public object Value { get; set; }

    [YamlMember(Alias = "min")]
    public int? Min { get; set; }

    [YamlMember(Alias = "max")]
    public int? Max { get; set; }

    [YamlMember(Alias = "expression")]
    public string Expression { get; set; }

    [YamlMember(Alias = "description")]
    public string Description { get; set; }
}

/// <summary>
/// Script action configuration.
/// </summary>
public class ScriptActionData
{
    /// <summary>
    /// Optional condition that must be true for this action to execute.
    /// </summary>
    [YamlMember(Alias = "condition")]
    public string Condition { get; set; }

    /// <summary>
    /// The action type to perform.
    /// Supported: cancel, set, spawn, despawn, broadcast, effect, sound, message
    /// </summary>
    [YamlMember(Alias = "action")]
    public string Action { get; set; }

    /// <summary>
    /// Description of what this action does (for documentation).
    /// </summary>
    [YamlMember(Alias = "description")]
    public string Description { get; set; }

    /// <summary>
    /// Reason for the action (used with cancel).
    /// </summary>
    [YamlMember(Alias = "reason")]
    public string Reason { get; set; }

    // Target configuration (for set action)

    /// <summary>
    /// Target specification for set actions.
    /// </summary>
    [YamlMember(Alias = "target")]
    public ActionTargetData Target { get; set; }

    /// <summary>
    /// Properties to set on the target.
    /// </summary>
    [YamlMember(Alias = "properties")]
    public Dictionary<string, object> Properties { get; set; }

    // Spawn configuration

    /// <summary>
    /// Spawner name or ID to trigger (for spawn action).
    /// </summary>
    [YamlMember(Alias = "spawner")]
    public string Spawner { get; set; }

    /// <summary>
    /// Subgroup to spawn from.
    /// </summary>
    [YamlMember(Alias = "subgroup")]
    public int? Subgroup { get; set; }

    // Broadcast/message configuration

    /// <summary>
    /// Message text to broadcast or send.
    /// </summary>
    [YamlMember(Alias = "message")]
    public string Message { get; set; }

    /// <summary>
    /// Range for broadcast messages.
    /// </summary>
    [YamlMember(Alias = "range")]
    public int? Range { get; set; }

    /// <summary>
    /// Hue for messages.
    /// </summary>
    [YamlMember(Alias = "hue")]
    public int? Hue { get; set; }

    // Effect/sound configuration

    /// <summary>
    /// Effect ID for visual effects.
    /// </summary>
    [YamlMember(Alias = "effectId")]
    public int? EffectId { get; set; }

    /// <summary>
    /// Sound ID to play.
    /// </summary>
    [YamlMember(Alias = "soundId")]
    public int? SoundId { get; set; }

    /// <summary>
    /// Duration in seconds.
    /// </summary>
    [YamlMember(Alias = "duration")]
    public double? Duration { get; set; }

    /// <summary>
    /// Delay before action executes (in seconds).
    /// </summary>
    [YamlMember(Alias = "delay")]
    public double? Delay { get; set; }

    /// <summary>
    /// Nested actions (for complex sequences).
    /// </summary>
    [YamlMember(Alias = "actions")]
    public List<ScriptActionData> Actions { get; set; }
}

/// <summary>
/// Target specification for set actions.
/// </summary>
public class ActionTargetData
{
    /// <summary>
    /// Target by name.
    /// </summary>
    [YamlMember(Alias = "name")]
    public string Name { get; set; }

    /// <summary>
    /// Target by type.
    /// </summary>
    [YamlMember(Alias = "type")]
    public string Type { get; set; }

    /// <summary>
    /// Target the spawned entity.
    /// </summary>
    [YamlMember(Alias = "spawned")]
    public bool? Spawned { get; set; }

    /// <summary>
    /// Target the triggering mobile.
    /// </summary>
    [YamlMember(Alias = "trigMob")]
    public bool? TrigMob { get; set; }

    /// <summary>
    /// Target the spawner itself.
    /// </summary>
    [YamlMember(Alias = "spawner")]
    public bool? SpawnerTarget { get; set; }

    /// <summary>
    /// Range to search for target.
    /// </summary>
    [YamlMember(Alias = "range")]
    public int? Range { get; set; }
}
