using System;
using System.Text.Json.Serialization;
using ModernUO.Serialization;
using Server.Engines.Spawners;
using Server.Json;

namespace Server.Engines.ModernSpawner;

/// <summary>
/// Spawner entry with scripting, per-entry timing, positioning rule, loot template and subgroup.
/// The six stock fields (name, probability, max count, properties, parameters, spawned) and the
/// <c>Disabled</c> flag come from <see cref="SpawnerEntry"/>.
/// </summary>
[SerializationGenerator(1)]
public partial class ModernSpawnerEntry : SpawnerEntry
{
    // The generator resolves dirty tracking on the declared type only (SerializationGenerator #58).
    [DirtyTrackingEntity]
    private BaseSpawner Owner => Parent;

    /// <summary>Script executed on the spawned entity right after it is placed.</summary>
    [SerializableField(0)]
    [SerializedJsonPropertyName("onSpawnScript")]
    private string _onSpawnScript;

    /// <summary>Script executed when a spawned creature dies.</summary>
    [SerializableField(1)]
    [SerializedJsonPropertyName("onDespawnScript")]
    private string _onDespawnScript;

    /// <summary>Per-entry minimum delay; <see cref="TimeSpan.Zero"/> means use the spawner's.</summary>
    [SerializableField(2)]
    [SerializedJsonPropertyName("minDelay")]
    private TimeSpan _minDelay;

    /// <summary>Per-entry maximum delay; <see cref="TimeSpan.Zero"/> means use the spawner's.</summary>
    [SerializableField(3)]
    [SerializedJsonPropertyName("maxDelay")]
    private TimeSpan _maxDelay;

    /// <summary>Positioning rule name (see <c>PositioningRules</c>); null uses the spawner's positioning.</summary>
    [SerializableField(4)]
    [SerializedJsonPropertyName("positioningRule")]
    private string _positioningRule;

    /// <summary>Organisational group tag.</summary>
    [SerializableField(5)]
    [SerializedJsonPropertyName("spawnGroup")]
    private string _spawnGroup;

    /// <summary>Whether this entry requires line of sight to the spawn location.</summary>
    [SerializableField(6)]
    [SerializedJsonPropertyName("requireLOS")]
    private bool _requireLOS;

    /// <summary>Offset from the spawner location used when no positioning rule applies.</summary>
    [SerializableField(7)]
    [SerializedJsonPropertyName("spawnAreaOffset")]
    private Point3D _spawnAreaOffset;

    /// <summary>Per-entry spawn range; -1 uses the spawner's <c>HomeRange</c>.</summary>
    [SerializableField(8)]
    [SerializedJsonPropertyName("spawnRange")]
    private int _spawnRange = -1;

    /// <summary>Loot template name applied on spawn; null or empty keeps default creature loot.</summary>
    [SerializableField(9)]
    [SerializedJsonPropertyName("lootTemplate")]
    private string _lootTemplate;

    /// <summary>
    /// Subgroup identifier. In <see cref="SpawnCycleMode.Sequential"/> mode this selects which entries are
    /// eligible in the current phase; otherwise it is an organisational tag used by inter-spawner commands.
    /// </summary>
    [SerializableField(10)]
    [SerializedJsonPropertyName("subgroup")]
    private int _subgroup;

    /// <summary>
    /// Absolute instant before which this entry is not selectable by a timer cycle. Trigger cycles in
    /// <c>mode:now</c> bypass it. World-save only: it is runtime state, so it never reaches the DTO.
    /// </summary>
    [SerializableField(11)]
    [SerializedJsonIgnore]
    [SaveFlag(nameof(ShouldSerializeNextEligible))]
    private DateTime _nextEligible;

    private bool ShouldSerializeNextEligible() => _nextEligible != default;

    /// <summary>
    /// v0 -> v1. Every v0 field is carried over unchanged; <see cref="NextEligible"/> is new and starts
    /// at its default, so a migrated entry is immediately selectable.
    /// </summary>
    /// <param name="content">The v0 payload.</param>
    private void MigrateFrom(V0Content content)
    {
        _onSpawnScript = content.OnSpawnScript;
        _onDespawnScript = content.OnDespawnScript;
        _minDelay = content.MinDelay;
        _maxDelay = content.MaxDelay;
        _positioningRule = content.PositioningRule;
        _spawnGroup = content.SpawnGroup;
        _requireLOS = content.RequireLOS;
        _spawnAreaOffset = content.SpawnAreaOffset;
        _spawnRange = content.SpawnRange;
        _lootTemplate = content.LootTemplate;
        _subgroup = content.Subgroup;
        _nextEligible = default;
    }

    public ModernSpawnerEntry(BaseSpawner parent) : base(parent)
    {
    }

    [JsonConstructor]
    public ModernSpawnerEntry(
        string spawnedName,
        int spawnedProbability = 100,
        int spawnedMaxCount = 1,
        string properties = null,
        string parameters = null
    ) : base(spawnedName, spawnedProbability, spawnedMaxCount, properties, parameters)
    {
    }

    public ModernSpawnerEntry(
        BaseSpawner parent,
        string name,
        int probability = 100,
        int maxCount = 1,
        string properties = null,
        string parameters = null
    ) : base(parent, name, probability, maxCount, properties, parameters)
    {
    }

    /// <summary>Effective minimum delay: the entry's override, else the spawner's.</summary>
    [JsonIgnore]
    public TimeSpan EffectiveMinDelay =>
        _minDelay > TimeSpan.Zero ? _minDelay : Parent != null ? Parent.MinDelay : TimeSpan.FromMinutes(5);

    /// <summary>Effective maximum delay: the entry's override, else the spawner's.</summary>
    [JsonIgnore]
    public TimeSpan EffectiveMaxDelay =>
        _maxDelay > TimeSpan.Zero ? _maxDelay : Parent != null ? Parent.MaxDelay : TimeSpan.FromMinutes(10);

    /// <summary>Effective spawn range: the entry's override, else the spawner's home range.</summary>
    [JsonIgnore]
    public int EffectiveSpawnRange => _spawnRange >= 0 ? _spawnRange : Parent != null ? Parent.HomeRange : 4;

    /// <summary>
    /// Whether a timer cycle may select this entry at <paramref name="now"/>. An entry that has never
    /// spawned carries no deadline (the default) and is always due.
    /// </summary>
    /// <param name="now">The instant the cycle is running at.</param>
    /// <returns>True when the entry's own deadline has passed.</returns>
    public bool IsDue(DateTime now) => _nextEligible == default || _nextEligible <= now;
}
