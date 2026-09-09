using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using ModernUO.Serialization;
using Server.Engines.Spawners;
using Server.Json;

namespace Server.Engines.ModernSpawner;

/// <summary>
/// Entry for ModernSpawner with support for scripting, triggers, and advanced positioning.
/// </summary>
[SerializationGenerator(0, false)]
public partial class ModernSpawnerEntry
{
    [DirtyTrackingEntity]
    private ModernSpawner _parent;

    [SerializableField(0)]
    [SerializedJsonPropertyName("name")]
    private string _spawnedName;

    [SerializableField(1)]
    [SerializedJsonPropertyName("probability")]
    private int _spawnedProbability = 100;

    [SerializableField(2)]
    [SerializedJsonPropertyName("maxCount")]
    private int _spawnedMaxCount = 1;

    [SerializableField(3)]
    [SerializedJsonPropertyName("properties")]
    private string _properties;

    [SerializableField(4)]
    [SerializedJsonPropertyName("parameters")]
    private string _parameters;

    [Tidy]
    [SerializedJsonIgnore]
    [SerializableField(5)]
    private List<ISpawnable> _spawned;

    // ModernSpawner-specific fields

    /// <summary>
    /// Script to execute when this entry spawns a creature/item.
    /// Uses AST-compiled expressions for performance.
    /// </summary>
    [SerializableField(6)]
    [SerializedJsonPropertyName("onSpawnScript")]
    private string _onSpawnScript;

    /// <summary>
    /// Script to execute when a spawned creature/item is despawned.
    /// </summary>
    [SerializableField(7)]
    [SerializedJsonPropertyName("onDespawnScript")]
    private string _onDespawnScript;

    /// <summary>
    /// Minimum delay override for this specific entry.
    /// If &lt;= TimeSpan.Zero, uses the parent spawner's MinDelay.
    /// </summary>
    [SerializableField(8)]
    [SerializedJsonPropertyName("minDelay")]
    private TimeSpan _minDelay;

    /// <summary>
    /// Maximum delay override for this specific entry.
    /// If &lt;= TimeSpan.Zero, uses the parent spawner's MaxDelay.
    /// </summary>
    [SerializableField(9)]
    [SerializedJsonPropertyName("maxDelay")]
    private TimeSpan _maxDelay;

    /// <summary>
    /// Positioning rule name for spawn location calculation.
    /// </summary>
    [SerializableField(10)]
    [SerializedJsonPropertyName("positioningRule")]
    private string _positioningRule;

    /// <summary>
    /// Group identifier for grouped spawning behavior.
    /// Entries with the same group spawn/despawn together.
    /// </summary>
    [SerializableField(11)]
    [SerializedJsonPropertyName("spawnGroup")]
    private string _spawnGroup;

    /// <summary>
    /// Whether this entry requires line of sight to spawn location.
    /// </summary>
    [SerializableField(12)]
    [SerializedJsonPropertyName("requireLOS")]
    private bool _requireLOS;

    /// <summary>
    /// Custom spawn area offset from spawner location.
    /// </summary>
    [SerializableField(13)]
    [SerializedJsonPropertyName("spawnAreaOffset")]
    private Point3D _spawnAreaOffset;

    /// <summary>
    /// Custom spawn range override for this entry.
    /// If -1, uses the parent spawner's HomeRange.
    /// </summary>
    [SerializableField(14)]
    [SerializedJsonPropertyName("spawnRange")]
    private int _spawnRange = -1;

    /// <summary>
    /// Name of the loot template to apply to spawned creatures.
    /// If null or empty, uses default creature loot.
    /// </summary>
    [SerializableField(15)]
    [SerializedJsonPropertyName("lootTemplate")]
    private string _lootTemplate;

    /// <summary>
    /// Subgroup identifier. In <see cref="SpawnCycleMode.Sequential"/> mode this selects
    /// which entries are eligible in the current phase. In other modes it's an
    /// organisational tag used by triggers and inter-spawner commands
    /// (e.g. <c>SPAWN/2</c>, <c>DESPAWN/0</c>, <c>GOTO/3</c>).
    /// </summary>
    [SerializableField(16)]
    [SerializedJsonPropertyName("subgroup")]
    private int _subgroup;

    public ModernSpawnerEntry(ModernSpawner parent)
    {
        _parent = parent;
        _spawned = [];
    }

    [JsonConstructor]
    public ModernSpawnerEntry(
        string spawnedName,
        int spawnedProbability = 100,
        int spawnedMaxCount = 1,
        string properties = null,
        string parameters = null
    ) : this(null, spawnedName, spawnedProbability, spawnedMaxCount, properties, parameters)
    {
    }

    public ModernSpawnerEntry(
        ModernSpawner parent,
        string name,
        int probability = 100,
        int maxCount = 1,
        string properties = null,
        string parameters = null
    ) : this(parent)
    {
        SpawnedName = name;
        SpawnedProbability = probability;
        SpawnedMaxCount = maxCount;
        Properties = properties;
        Parameters = parameters;
    }

    [JsonIgnore]
    public EntryFlags Valid { get; set; }

    [JsonIgnore]
    public bool IsFull => Spawned.Count >= SpawnedMaxCount;

    /// <summary>
    /// Gets the effective minimum delay for this entry.
    /// </summary>
    [JsonIgnore]
    public TimeSpan EffectiveMinDelay => _minDelay > TimeSpan.Zero ? _minDelay : _parent?.MinDelay ?? TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets the effective maximum delay for this entry.
    /// </summary>
    [JsonIgnore]
    public TimeSpan EffectiveMaxDelay => _maxDelay > TimeSpan.Zero ? _maxDelay : _parent?.MaxDelay ?? TimeSpan.FromMinutes(10);

    /// <summary>
    /// Gets the effective spawn range for this entry.
    /// </summary>
    [JsonIgnore]
    public int EffectiveSpawnRange => _spawnRange >= 0 ? _spawnRange : _parent?.HomeRange ?? 4;

    /// <summary>
    /// Gets the list of spawned entities for this entry as a read-only list.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<ISpawnable> SpawnedList => _spawned;

    // Note: AddToSpawned and RemoveFromSpawned are generated by SerializationGenerator
    // for the _spawned List field and already handle dirty tracking

    public void Defrag(BaseSpawner parent)
    {
        for (var i = 0; i < Spawned.Count; ++i)
        {
            var spawned = Spawned[i];

            if (parent.OnDefragSpawn(spawned, false))
            {
                Spawned.RemoveAt(i--);
                _parent?.MarkDirty();
            }
        }
    }

    /// <summary>
    /// Sets the parent spawner reference. Called during deserialization.
    /// </summary>
    internal void SetParent(ModernSpawner parent)
    {
        _parent = parent;

        // Re-parent any spawned entities
        foreach (var spawned in _spawned)
        {
            spawned?.Spawner = parent;
        }
    }

    /// <summary>
    /// Adds an already-spawned entity to this entry.
    /// Used during migration from other spawner types.
    /// </summary>
    public void AddSpawnedEntity(ISpawnable entity)
    {
        if (entity == null || _spawned.Contains(entity))
        {
            return;
        }

        AddToSpawned(entity);
        entity.Spawner = _parent;
    }

    /// <summary>
    /// Removes a spawned entity from this entry.
    /// </summary>
    public void RemoveSpawnedEntity(ISpawnable entity)
    {
        if (entity != null && _spawned.Contains(entity))
        {
            RemoveFromSpawned(entity);
        }
    }

    [AfterDeserialization]
    private void AfterDeserialization()
    {
        for (var i = Spawned.Count - 1; i >= 0; i--)
        {
            var e = Spawned[i];
            if (e == null)
            {
                Spawned.RemoveAt(i);
            }
            else
            {
                e.Spawner = _parent;
            }
        }

        Spawned.TrimExcess();
    }
}
