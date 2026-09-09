using System;
using System.Collections.Generic;
using System.Text.Json;
using ModernUO.Serialization;
using Server.Engines.ModernSpawner.Perf;
using Server.Engines.ModernSpawner.Positioning;
using Server.Engines.ModernSpawner.Scripting;
using Server.Engines.ModernSpawner.Triggers;
using Server.Engines.Spawners;
using Server.Gumps;
using Server.Json;

namespace Server.Engines.ModernSpawner;

/// <summary>
/// Modern spawner implementation with support for scripting, triggers, and advanced positioning.
/// Extends BaseSpawner with additional capabilities beyond the standard Spawner.
/// </summary>
[SerializationGenerator(0)]
public partial class ModernSpawner : BaseSpawner
{
    [SerializableField(0)]
    private List<ModernSpawnerEntry> _spawnEntries = [];

    /// <summary>
    /// Script serial for script executed when spawner becomes active.
    /// </summary>
    [SerializableField(1)]
    private Serial _onActivateScriptSerial;

    /// <summary>
    /// Script serial for script executed when spawner becomes inactive.
    /// </summary>
    [SerializableField(2)]
    private Serial _onDeactivateScriptSerial;

    /// <summary>
    /// Script serial for script executed before each spawn attempt.
    /// </summary>
    [SerializableField(3)]
    private Serial _onBeforeSpawnScriptSerial;

    /// <summary>
    /// Script serial for script executed after a successful spawn.
    /// </summary>
    [SerializableField(4)]
    private Serial _onAfterSpawnScriptSerial;

    /// <summary>
    /// Whether this spawner uses smart positioning (avoids obstacles, respects terrain).
    /// </summary>
    [SerializableField(5)]
    [SerializedCommandProperty(AccessLevel.Developer)]
    private bool _useSmartPositioning = true;

    /// <summary>
    /// Whether spawned entities should attempt to return to their spawn point when idle.
    /// </summary>
    [SerializableField(6)]
    [SerializedCommandProperty(AccessLevel.Developer)]
    private bool _returnToSpawnOnIdle;

    /// <summary>
    /// Maximum Z delta for spawn position calculation.
    /// </summary>
    [SerializableField(7)]
    [SerializedCommandProperty(AccessLevel.Developer)]
    private int _maxZDelta = 20;

    /// <summary>
    /// List of trigger conditions that can activate this spawner.
    /// Stored as serialized trigger definitions.
    /// </summary>
    [SerializableField(8)]
    private List<string> _triggerDefinitions = [];

    /// <summary>
    /// Whether this spawner is trigger-activated (vs. timer-based).
    /// </summary>
    [SerializableField(9)]
    [SerializedCommandProperty(AccessLevel.Developer)]
    private bool _triggerActivated;

    /// <summary>
    /// External trigger state - set by trigger system.
    /// </summary>
    [SerializableField(10)]
    [SerializedCommandProperty(AccessLevel.Developer)]
    private bool _triggered;

    /// <summary>
    /// The spawn area - if set, spawns within this area instead of HomeRange from spawner.
    /// Use a Rectangle3D with Width/Height &gt; 0 to enable. Default (zero area) disables.
    /// </summary>
    [SerializableField(11)]
    [SerializedCommandProperty(AccessLevel.Developer)]
    private Rectangle3D _spawnArea;

    /// <summary>
    /// Notes field for admin documentation.
    /// </summary>
    [SerializableField(12)]
    [SerializedCommandProperty(AccessLevel.Developer)]
    private string _notes;

    /// <summary>
    /// Selection strategy used each spawn cycle. See <see cref="SpawnCycleMode"/>.
    /// </summary>
    [SerializableField(13)]
    [SerializedCommandProperty(AccessLevel.Developer)]
    private SpawnCycleMode _cycleMode = SpawnCycleMode.Random;

    /// <summary>
    /// In <see cref="SpawnCycleMode.Sequential"/> mode, only entries with
    /// <c>Subgroup == CurrentSubgroup</c> are eligible this cycle.
    /// </summary>
    [SerializableField(14)]
    [SerializedCommandProperty(AccessLevel.Developer)]
    private int _currentSubgroup;

    /// <summary>
    /// Optional safety net for <see cref="SpawnCycleMode.Sequential"/>. If greater than
    /// <see cref="TimeSpan.Zero"/>, the sequence will reset to <see cref="SequentialResetTo"/>
    /// after this much real time has elapsed without an advance. <see cref="TimeSpan.Zero"/>
    /// disables auto-reset.
    /// </summary>
    [SerializableField(15)]
    [SerializedCommandProperty(AccessLevel.Developer)]
    private TimeSpan _sequentialResetTime;

    /// <summary>
    /// Subgroup that <see cref="SequentialResetTime"/> rewinds to. Defaults to 0.
    /// </summary>
    [SerializableField(16)]
    [SerializedCommandProperty(AccessLevel.Developer)]
    private int _sequentialResetTo;

    /// <summary>
    /// When true, <see cref="AdvanceSequence"/> is a no-op. Lets scripts / triggers pin
    /// the spawner on a specific subgroup until explicitly released.
    /// </summary>
    [SerializableField(17)]
    [SerializedCommandProperty(AccessLevel.Developer)]
    private bool _holdSequence;

    // When the last spawn happened, used to enforce SequentialResetTime.
    private DateTime _lastSequenceAdvance = DateTime.MinValue;

    // Trigger state flags - set by TriggerSystem when triggers are registered/unregistered
    private bool _hasSpeechTriggers;
    private bool _hasProximityTriggers;

    // Extended area movement subscription tracking
    private bool _hasExtendedProximityTriggers;
    private Rectangle2D _extendedTriggerBounds;

    // Track previous map for area movement unsubscription
    private Map _previousMap;

    // Our own spawned entity tracking (maps to ModernSpawnerEntry, parallel to base Spawned)
    private Dictionary<ISpawnable, ModernSpawnerEntry> _modernSpawned = new();

    /// <summary>
    /// Gets the modern spawn entries for this spawner.
    /// </summary>
    public IReadOnlyList<ModernSpawnerEntry> ModernEntries => _spawnEntries;

    /// <summary>
    /// Gets the spawned entity to ModernSpawnerEntry mapping.
    /// </summary>
    public IReadOnlyDictionary<ISpawnable, ModernSpawnerEntry> ModernSpawned => _modernSpawned;

    /// <summary>
    /// Backs <see cref="BaseSpawner.SpawnBounds"/> with <see cref="_spawnArea"/>.
    ///
    /// Important: do NOT synthesize bounds from <see cref="BaseSpawner.HomeRange"/> when
    /// <see cref="_spawnArea"/> is empty. <see cref="BaseSpawner.HomeRange"/> is itself
    /// derived from <see cref="BaseSpawner.SpawnBounds"/> — anything that reads one and
    /// falls back to the other introduces infinite recursion. The base class treats
    /// <see cref="BaseSpawner.SpawnBounds"/> as the source of truth; this override just
    /// stores and returns it, matching the reference <c>Spawner</c> implementation.
    /// </summary>
    public override Rectangle3D SpawnBounds
    {
        get => _spawnArea;
        set
        {
            _spawnArea = value;
            InvalidateProperties();
            this.MarkDirty();
        }
    }

    /// <summary>
    /// Gets the region this spawner is in.
    /// </summary>
    public override Region Region => Region.Find(Location, Map);

    /// <summary>
    /// Returns the bounds to use for a single spawn attempt.
    /// </summary>
    protected override Rectangle3D GetBoundsForSpawnAttempt() => SpawnBounds;

    /// <summary>
    /// Returns all possible spawn bounds for cache operations.
    /// </summary>
    protected override ReadOnlySpan<Rectangle3D> GetAllSpawnBounds() => new(ref _spawnArea);

    /// <summary>
    /// Gets the compiled activate script, or null if not set.
    /// </summary>
    public CompiledScript OnActivateScript => ScriptRegistry.Get(_onActivateScriptSerial);

    /// <summary>
    /// Gets the compiled deactivate script, or null if not set.
    /// </summary>
    public CompiledScript OnDeactivateScript => ScriptRegistry.Get(_onDeactivateScriptSerial);

    /// <summary>
    /// Gets the compiled before-spawn script, or null if not set.
    /// </summary>
    public CompiledScript OnBeforeSpawnScript => ScriptRegistry.Get(_onBeforeSpawnScriptSerial);

    /// <summary>
    /// Gets the compiled after-spawn script, or null if not set.
    /// </summary>
    public CompiledScript OnAfterSpawnScript => ScriptRegistry.Get(_onAfterSpawnScriptSerial);

    /// <summary>
    /// Sets the activate script from source code.
    /// </summary>
    public void SetOnActivateScript(string source)
    {
        _onActivateScriptSerial = string.IsNullOrEmpty(source)
            ? Serial.Zero
            : ScriptRegistry.GetOrRegister($"spawner_{Serial}_activate", source);
        this.MarkDirty();
    }

    /// <summary>
    /// Sets the deactivate script from source code.
    /// </summary>
    public void SetOnDeactivateScript(string source)
    {
        _onDeactivateScriptSerial = string.IsNullOrEmpty(source)
            ? Serial.Zero
            : ScriptRegistry.GetOrRegister($"spawner_{Serial}_deactivate", source);
        this.MarkDirty();
    }

    /// <summary>
    /// Sets the before-spawn script from source code.
    /// </summary>
    public void SetOnBeforeSpawnScript(string source)
    {
        _onBeforeSpawnScriptSerial = string.IsNullOrEmpty(source)
            ? Serial.Zero
            : ScriptRegistry.GetOrRegister($"spawner_{Serial}_before", source);
        this.MarkDirty();
    }

    /// <summary>
    /// Sets the after-spawn script from source code.
    /// </summary>
    public void SetOnAfterSpawnScript(string source)
    {
        _onAfterSpawnScriptSerial = string.IsNullOrEmpty(source)
            ? Serial.Zero
            : ScriptRegistry.GetOrRegister($"spawner_{Serial}_after", source);
        this.MarkDirty();
    }

    [Constructible(AccessLevel.Developer)]
    public ModernSpawner()
    {
    }

    [Constructible(AccessLevel.Developer)]
    public ModernSpawner(string spawnedName) : base(spawnedName)
    {
    }

    [Constructible(AccessLevel.Developer)]
    public ModernSpawner(
        int amount, int minDelay, int maxDelay, int team, Rectangle3D spawnBounds = default,
        params ReadOnlySpan<string> spawnedNames
    ) : this(
        amount,
        TimeSpan.FromMinutes(minDelay),
        TimeSpan.FromMinutes(maxDelay),
        team,
        spawnBounds,
        spawnedNames
    )
    {
    }

    [Constructible(AccessLevel.Developer)]
    public ModernSpawner(
        int amount, TimeSpan minDelay, TimeSpan maxDelay, int team = 0, Rectangle3D spawnBounds = default,
        params ReadOnlySpan<string> spawnedNames
    ) : base(amount, minDelay, maxDelay, team, spawnBounds, spawnedNames)
    {
    }

    public override string DefaultName => "Modern Spawner";

    /// <summary>
    /// Adds a spawn entry to this spawner.
    /// Note: This hides the base class AddEntry with the 'new' keyword since BaseSpawner's
    /// AddEntry is not virtual. For best results, work with ModernEntries directly.
    /// </summary>
    public new ModernSpawnerEntry AddEntry(
        string creaturename,
        int probability = 100,
        int amount = 1,
        bool dotimer = true,
        string properties = null,
        string parameters = null
    )
    {
        var entry = new ModernSpawnerEntry(this, creaturename, probability, amount, properties, parameters);
        _spawnEntries.Add(entry);
        this.MarkDirty();

        if (dotimer)
        {
            DoTimer(TimeSpan.FromSeconds(1));
        }

        return entry;
    }

    /// <summary>
    /// Adds a ModernSpawnerEntry with extended configuration options.
    /// Use TimeSpan.Zero for minDelay/maxDelay to use the spawner's default values.
    /// </summary>
    public ModernSpawnerEntry AddModernEntry(
        string creatureName,
        int probability = 100,
        int maxCount = 1,
        string properties = null,
        string parameters = null,
        string onSpawnScript = null,
        string onDespawnScript = null,
        string positioningRule = null,
        string spawnGroup = null,
        TimeSpan minDelay = default,
        TimeSpan maxDelay = default,
        bool dotimer = true
    )
    {
        var entry = new ModernSpawnerEntry(this, creatureName, probability, maxCount, properties, parameters)
        {
            OnSpawnScript = onSpawnScript,
            OnDespawnScript = onDespawnScript,
            PositioningRule = positioningRule,
            SpawnGroup = spawnGroup,
            MinDelay = minDelay,
            MaxDelay = maxDelay
        };

        _spawnEntries.Add(entry);
        this.MarkDirty();

        if (dotimer)
        {
            DoTimer(TimeSpan.FromSeconds(1));
        }

        return entry;
    }

    /// <summary>
    /// Counts the spawned entities for a specific modern entry.
    /// </summary>
    public int CountSpawns(ModernSpawnerEntry entry)
    {
        return entry?.Spawned?.Count ?? 0;
    }

    /// <summary>
    /// Removes a spawn entry from this spawner.
    /// </summary>
    public void RemoveModernEntry(ModernSpawnerEntry entry)
    {
        if (!_spawnEntries.Contains(entry))
        {
            return;
        }

        // Remove all spawned entities for this entry
        for (var i = entry.Spawned.Count - 1; i >= 0; i--)
        {
            var spawned = entry.Spawned[i];
            entry.Spawned.RemoveAt(i);
            _modernSpawned?.Remove(spawned);
            spawned?.Delete();
        }

        _spawnEntries.Remove(entry);
        this.MarkDirty();

        if (Running && !IsFull)
        {
            DoTimer();
        }

        InvalidateProperties();
    }

    /// <summary>
    /// Clears all entries from this spawner.
    /// </summary>
    public void ClearAllModernEntries()
    {
        for (var i = _spawnEntries.Count - 1; i >= 0; i--)
        {
            RemoveModernEntry(_spawnEntries[i]);
        }
    }

    public override void Spawn()
    {
        using var _ = SpawnerMetrics.MeasureSpawn();

        // Execute pre-spawn script if configured
        var beforeScript = OnBeforeSpawnScript;
        if (beforeScript?.IsValid == true)
        {
            var context = new ScriptContext(null, this);
            ScriptEngine.Instance.Execute(beforeScript, context);

            // Check if script cancelled the spawn
            if (context.CancelSpawn)
            {
                return;
            }
        }

        if (_spawnEntries.Count > 0)
        {
            using (SpawnerMetrics.MeasureDefrag())
            {
                Defrag();
            }

            MaybeAutoResetSequence();

            using (SpawnerMetrics.MeasureEntrySelection())
            {
                switch (_cycleMode)
                {
                    case SpawnCycleMode.Sequential:
                        SpawnSequentialMode();
                        break;
                    case SpawnCycleMode.Group:
                        SpawnGroupMode();
                        break;
                    default:
                        SpawnRandomMode();
                        break;
                }
            }
        }
        else
        {
            // Fall back to BaseSpawner behaviour for spawners that were populated
            // via the legacy AddEntry path.
            base.Spawn();
        }

        // Execute post-spawn script if configured
        var afterScript = OnAfterSpawnScript;
        if (afterScript?.IsValid == true)
        {
            var context = new ScriptContext(null, this);
            ScriptEngine.Instance.Execute(afterScript, context);
        }
    }

    /// <summary>
    /// Picks one eligible entry weighted by <see cref="ModernSpawnerEntry.SpawnedProbability"/>
    /// and spawns one entity from it. Matches classic BaseSpawner semantics but operates on
    /// <see cref="ModernEntries"/>.
    /// </summary>
    private void SpawnRandomMode()
    {
        SpawnWeightedOne(static _ => true);
    }

    /// <summary>
    /// Picks one eligible entry whose <c>Subgroup</c> equals <see cref="CurrentSubgroup"/>,
    /// weighted by probability.
    /// </summary>
    private void SpawnSequentialMode()
    {
        var currentSubgroup = _currentSubgroup;
        SpawnWeightedOne(e => e.Subgroup == currentSubgroup);
    }

    /// <summary>
    /// Spawns one entity from every non-full entry this cycle. When all entries are at
    /// their max count, no further spawns happen until the pack is cleared.
    /// </summary>
    private void SpawnGroupMode()
    {
        foreach (var entry in _spawnEntries)
        {
            if (!entry.IsFull)
            {
                SpawnFromEntry(entry, out _);
            }
        }
    }

    private void SpawnWeightedOne(Func<ModernSpawnerEntry, bool> eligible)
    {
        var probsum = 0;

        for (var i = 0; i < _spawnEntries.Count; i++)
        {
            var entry = _spawnEntries[i];
            if (!entry.IsFull && eligible(entry))
            {
                probsum += entry.SpawnedProbability;
            }
        }

        if (probsum <= 0)
        {
            return;
        }

        var rand = Utility.RandomMinMax(1, probsum);

        for (var i = 0; i < _spawnEntries.Count; i++)
        {
            var entry = _spawnEntries[i];
            if (entry.IsFull || !eligible(entry))
            {
                continue;
            }

            if (rand <= entry.SpawnedProbability)
            {
                if (SpawnFromEntry(entry, out var flags))
                {
                    entry.Valid = flags;
                }
                return;
            }

            rand -= entry.SpawnedProbability;
        }
    }

    /// <summary>
    /// Sets <see cref="CurrentSubgroup"/> for <see cref="SpawnCycleMode.Sequential"/> mode.
    /// No-op unless cycle mode is Sequential.
    /// </summary>
    public void GotoSubgroup(int subgroup)
    {
        if (_cycleMode != SpawnCycleMode.Sequential)
        {
            return;
        }

        if (_currentSubgroup == subgroup)
        {
            return;
        }

        CurrentSubgroup = subgroup;
        _lastSequenceAdvance = Core.Now;
    }

    /// <summary>
    /// Advances <see cref="CurrentSubgroup"/> by 1, unless <see cref="HoldSequence"/> is set.
    /// No-op unless cycle mode is Sequential.
    /// </summary>
    public void AdvanceSequence()
    {
        if (_cycleMode != SpawnCycleMode.Sequential || _holdSequence)
        {
            return;
        }

        CurrentSubgroup = _currentSubgroup + 1;
        _lastSequenceAdvance = Core.Now;
    }

    /// <summary>
    /// Resets the sequence to <see cref="SequentialResetTo"/>. No-op unless cycle mode
    /// is Sequential.
    /// </summary>
    public void ResetSequence()
    {
        if (_cycleMode != SpawnCycleMode.Sequential)
        {
            return;
        }

        CurrentSubgroup = _sequentialResetTo;
        _lastSequenceAdvance = Core.Now;
    }

    private void MaybeAutoResetSequence()
    {
        if (_cycleMode != SpawnCycleMode.Sequential ||
            _holdSequence ||
            _sequentialResetTime <= TimeSpan.Zero)
        {
            return;
        }

        if (_lastSequenceAdvance == DateTime.MinValue)
        {
            _lastSequenceAdvance = Core.Now;
            return;
        }

        if (Core.Now - _lastSequenceAdvance >= _sequentialResetTime &&
            _currentSubgroup != _sequentialResetTo)
        {
            CurrentSubgroup = _sequentialResetTo;
            _lastSequenceAdvance = Core.Now;
        }
    }

    /// <summary>
    /// Spawns from a specific modern entry and executes entry-level scripts.
    /// </summary>
    public bool SpawnFromEntry(ModernSpawnerEntry entry, out EntryFlags flags)
    {
        using var _ = SpawnerMetrics.MeasureSpawnFromEntry();

        flags = EntryFlags.None;

        if (entry == null)
        {
            flags = EntryFlags.InvalidEntry;
            return false;
        }

        // Create a temporary SpawnerEntry to pass to base.Spawn
        var tempEntry = new SpawnerEntry(
            this,
            entry.SpawnedName,
            entry.SpawnedProbability,
            entry.SpawnedMaxCount,
            entry.Properties,
            entry.Parameters
        );

        // Track spawn count before
        var countBefore = entry.Spawned.Count;

        var result = base.Spawn(tempEntry, out flags);

        if (result)
        {
            SpawnerMetrics.RecordEntitySpawned();
            // Transfer spawned entity from temp entry to modern entry
            foreach (var spawned in tempEntry.Spawned)
            {
                entry.AddToSpawned(spawned);
                _modernSpawned[spawned] = entry;
            }

            // Find the just-spawned entity (most recently added)
            IEntity spawnedEntity = null;
            if (entry.Spawned.Count > countBefore)
            {
                spawnedEntity = entry.Spawned[entry.Spawned.Count - 1];
            }

            // Apply loot template if configured
            if (spawnedEntity is Mobile spawnedMobile && !string.IsNullOrEmpty(entry.LootTemplate))
            {
                Loot.LootTemplateRegistry.ApplyTemplate(entry.LootTemplate, spawnedMobile);
            }

            // Execute OnSpawn script if configured
            if (spawnedEntity != null && !string.IsNullOrEmpty(entry.OnSpawnScript))
            {
                var compiledScript = ScriptEngine.Instance.Compile(entry.OnSpawnScript);
                if (compiledScript?.IsValid == true)
                {
                    var context = new ScriptContext(spawnedEntity, this);
                    ScriptEngine.Instance.Execute(compiledScript, context);
                }
            }
        }

        return result;
    }

    public override Point3D GetSpawnPosition(ISpawnable spawned, Map map)
    {
        if (map == null || map == Map.Internal)
        {
            return Location;
        }

        // Check for entry-specific positioning rule
        if (_modernSpawned.TryGetValue(spawned, out var modernEntry))
        {
            if (!string.IsNullOrEmpty(modernEntry.PositioningRule))
            {
                // Use the positioning rules system
                var posContext = new PositioningContext(this, spawned, map, modernEntry)
                {
                    MaxZDelta = _maxZDelta
                };

                var position = PositioningRules.GetPosition(modernEntry.PositioningRule, posContext);
                if (position != Point3D.Zero)
                {
                    return position;
                }
            }

            // Use entry-specific spawn offset if set
            if (modernEntry.SpawnAreaOffset != Point3D.Zero)
            {
                var offset = modernEntry.SpawnAreaOffset;
                return new Point3D(Location.X + offset.X, Location.Y + offset.Y, Location.Z + offset.Z);
            }
        }

        // Check for spawn area definition (Width and Height > 0 means it's set)
        if (_spawnArea is { Width: > 0, Height: > 0 })
        {
            var pos = GetPositionInSpawnArea(map);
            if (pos != Point3D.Zero)
            {
                return pos;
            }
        }

        // Use smart positioning if enabled
        if (_useSmartPositioning)
        {
            return GetSmartSpawnPosition(spawned, map);
        }

        // Fall back to standard positioning
        return GetStandardSpawnPosition(spawned, map);
    }

    private Point3D GetPositionInSpawnArea(Map map)
    {
        if (_spawnArea.Width <= 0 || _spawnArea.Height <= 0)
        {
            return Point3D.Zero;
        }

        // Try 10 times to find a valid location within the spawn area
        for (var i = 0; i < 10; i++)
        {
            var x = Utility.RandomMinMax(_spawnArea.Start.X, _spawnArea.End.X - 1);
            var y = Utility.RandomMinMax(_spawnArea.Start.Y, _spawnArea.End.Y - 1);
            var z = map.GetAverageZ(x, y);

            // If Rectangle3D has Z constraints, respect them
            if (_spawnArea.Depth > 0 && (z < _spawnArea.Start.Z || z >= _spawnArea.End.Z))
            {
                continue;
            }

            if (map.CanSpawnMobile(x, y, z))
            {
                return new Point3D(x, y, z);
            }
        }

        return Point3D.Zero;
    }

    private Point3D GetSmartSpawnPosition(ISpawnable spawned, Map map)
    {
        bool waterMob, waterOnlyMob;

        if (spawned is Mobile mob)
        {
            waterMob = mob.CanSwim;
            waterOnlyMob = mob.CanSwim && mob.CantWalk;
        }
        else
        {
            waterMob = false;
            waterOnlyMob = false;
        }

        var homeRange = HomeRange;

        // Try 20 times for smart positioning (more attempts than standard)
        for (var i = 0; i < 20; i++)
        {
            var x = Location.X + (Utility.Random(homeRange * 2 + 1) - homeRange);
            var y = Location.Y + (Utility.Random(homeRange * 2 + 1) - homeRange);

            var mapZ = map.GetAverageZ(x, y);

            // Check Z delta is within acceptable range
            if (Math.Abs(mapZ - Location.Z) > _maxZDelta)
            {
                continue;
            }

            if (waterMob)
            {
                if (IsValidWater(map, x, y, Z))
                {
                    return new Point3D(x, y, Z);
                }

                if (IsValidWater(map, x, y, mapZ))
                {
                    return new Point3D(x, y, mapZ);
                }
            }

            if (!waterOnlyMob)
            {
                if (map.CanSpawnMobile(x, y, Z))
                {
                    return new Point3D(x, y, Z);
                }

                if (map.CanSpawnMobile(x, y, mapZ))
                {
                    return new Point3D(x, y, mapZ);
                }
            }
        }

        return HomeLocation;
    }

    private Point3D GetStandardSpawnPosition(ISpawnable spawned, Map map)
    {
        bool waterMob, waterOnlyMob;

        if (spawned is Mobile mob)
        {
            waterMob = mob.CanSwim;
            waterOnlyMob = mob.CanSwim && mob.CantWalk;
        }
        else
        {
            waterMob = false;
            waterOnlyMob = false;
        }

        // Standard 10-attempt positioning
        for (var i = 0; i < 10; i++)
        {
            var x = Location.X + (Utility.Random(HomeRange * 2 + 1) - HomeRange);
            var y = Location.Y + (Utility.Random(HomeRange * 2 + 1) - HomeRange);

            var mapZ = map.GetAverageZ(x, y);

            if (waterMob)
            {
                if (IsValidWater(map, x, y, Z))
                {
                    return new Point3D(x, y, Z);
                }

                if (IsValidWater(map, x, y, mapZ))
                {
                    return new Point3D(x, y, mapZ);
                }
            }

            if (!waterOnlyMob)
            {
                if (map.CanSpawnMobile(x, y, Z))
                {
                    return new Point3D(x, y, Z);
                }

                if (map.CanSpawnMobile(x, y, mapZ))
                {
                    return new Point3D(x, y, mapZ);
                }
            }
        }

        return HomeLocation;
    }

    private static bool IsValidWater(Map map, int x, int y, int z)
    {
        if (!Region.Find(new Point3D(x, y, z), map).AllowSpawn() || !map.CanFit(x, y, z, 16, false, true, false))
        {
            return false;
        }

        var landTile = map.Tiles.GetLandTile(x, y);

        if (landTile.Z == z && (TileData.LandTable[landTile.ID & TileData.MaxLandValue].Flags & TileFlag.Wet) != 0)
        {
            return true;
        }

        foreach (var staticTile in map.Tiles.GetStaticAndMultiTiles(x, y))
        {
            if (staticTile.Z == z && TileData.ItemTable[staticTile.ID & TileData.MaxItemValue].Wet)
            {
                return true;
            }
        }

        return false;
    }

    public override void GetSpawnerProperties(IPropertyList list)
    {
        base.GetSpawnerProperties(list);

        if (_triggerActivated)
        {
            list.Add(1050039, $"{"trigger:"}\t{(_triggered ? "active" : "waiting")}");
        }

        if (_useSmartPositioning)
        {
            list.Add(1050039, $"{"smart pos:"}\tyes");
        }
    }

    /// <summary>
    /// Triggers the spawner from an external source (trigger system).
    /// </summary>
    public void Trigger()
    {
        if (!_triggerActivated)
        {
            return;
        }

        _triggered = true;
        this.MarkDirty();

        if (!Running)
        {
            Start();
        }
        else
        {
            // Force an immediate spawn
            Spawn();
        }
    }

    /// <summary>
    /// Resets the trigger state.
    /// </summary>
    public void ResetTrigger()
    {
        _triggered = false;
        this.MarkDirty();
    }

    /// <summary>
    /// Called by a time-window trigger when its window opens.
    /// This enables spawning during the trigger's active window.
    /// </summary>
    /// <param name="trigger">The trigger that activated.</param>
    public void OnTriggerActivated(ITrigger trigger)
    {
        if (!_triggerActivated || !Running)
        {
            return;
        }

        _triggered = true;
        this.MarkDirty();

        // Force an immediate spawn check when trigger activates
        Spawn();
    }

    /// <summary>
    /// Called by a time-window trigger when its window closes.
    /// This disables spawning until the trigger reactivates.
    /// </summary>
    /// <param name="trigger">The trigger that deactivated.</param>
    public void OnTriggerDeactivated(ITrigger trigger)
    {
        if (!_triggerActivated)
        {
            return;
        }

        _triggered = false;
        this.MarkDirty();
    }

    [AfterDeserialization]
    private void AfterDeserializationModernSpawner()
    {
        // Re-parent all entries after deserialization
        foreach (var entry in _spawnEntries)
        {
            entry.SetParent(this);
        }

        // Rebuild the modern spawned dictionary from entries
        _modernSpawned = new Dictionary<ISpawnable, ModernSpawnerEntry>();
        foreach (var entry in _spawnEntries)
        {
            foreach (var spawned in entry.Spawned)
            {
                _modernSpawned[spawned] = entry;
            }
        }

        // Initialize map tracking
        _previousMap = Map;

        // Activate triggers if spawner is running
        if (Running && _triggerActivated && _triggerDefinitions.Count > 0)
        {
            TriggerSystem.Instance.ActivateTriggers(this);
        }
    }

    /// <summary>
    /// Called after world load completes.
    /// Subscribes to extended area movement if needed.
    /// </summary>
    [AfterDeserialization(false)]
    private void AfterWorldLoad()
    {
        // Extended area movement subscription is not yet supported in ModernUO
        // TODO: Implement extended proximity trigger support when Map APIs are available
    }

    /// <summary>
    /// Called when the spawner starts running. Activates triggers.
    /// </summary>
    public new void Start()
    {
        base.Start();
        OnSpawnerStarted();
    }

    /// <summary>
    /// Called when the spawner stops running. Deactivates triggers.
    /// </summary>
    public new void Stop()
    {
        OnSpawnerStopping();
        base.Stop();
    }

    /// <summary>
    /// Hook called after the spawner has started.
    /// </summary>
    private void OnSpawnerStarted()
    {
        // Activate triggers when spawner starts
        if (_triggerActivated && _triggerDefinitions.Count > 0)
        {
            TriggerSystem.Instance.ActivateTriggers(this);
        }

        // Execute activate script
        var activateScript = OnActivateScript;
        if (activateScript?.IsValid == true)
        {
            var context = new ScriptContext(null, this);
            ScriptEngine.Instance.Execute(activateScript, context);
        }
    }

    /// <summary>
    /// Hook called before the spawner stops.
    /// </summary>
    private void OnSpawnerStopping()
    {
        // Execute deactivate script
        var deactivateScript = OnDeactivateScript;
        if (deactivateScript?.IsValid == true)
        {
            var context = new ScriptContext(null, this);
            ScriptEngine.Instance.Execute(deactivateScript, context);
        }

        // Deactivate triggers when spawner stops
        if (_triggerActivated)
        {
            TriggerSystem.Instance.DeactivateTriggers(this);
        }
    }

    /// <summary>
    /// Called when this spawner is deleted.
    /// </summary>
    public override void OnDelete()
    {
        // Unsubscribe from extended area movement before deletion
        UnsubscribeFromExtendedAreaMovement();

        // Deactivate triggers before deletion
        if (_triggerActivated)
        {
            TriggerSystem.Instance.DeactivateTriggers(this);
        }

        base.OnDelete();
    }

    /// <summary>
    /// Called when the spawner's map changes.
    /// </summary>
    public override void OnMapChange()
    {
        _previousMap = Map;
        base.OnMapChange();
        // Extended area movement subscription is not yet supported in ModernUO
    }

    /// <summary>
    /// Called when the spawner's location changes.
    /// </summary>
    public override void OnLocationChange(Point3D oldLocation)
    {
        base.OnLocationChange(oldLocation);
        // Extended area movement subscription is not yet supported in ModernUO
    }

    /// <summary>
    /// Called when a spawned entity is killed. Override to handle death events.
    /// This should be called from the spawned mobile's OnDeath handler.
    /// </summary>
    public void OnSpawnedEntityKilled(IEntity killed, Mobile killer)
    {
        // Notify the trigger system for kill triggers
        TriggerSystem.Instance.OnEntityKilled(this, killed, killer);

        // Execute entry-level OnDespawn script if configured
        if (killed is ISpawnable spawnable && _modernSpawned.TryGetValue(spawnable, out var modernEntry))
        {
            if (!string.IsNullOrEmpty(modernEntry.OnDespawnScript))
            {
                var compiledScript = ScriptEngine.Instance.Compile(modernEntry.OnDespawnScript);
                if (compiledScript?.IsValid == true)
                {
                    var context = new ScriptContext(killed, this)
                    {
                        TriggeringMobile = killer
                    };
                    ScriptEngine.Instance.Execute(compiledScript, context);
                }
            }
        }
    }

    public override void OnDoubleClick(Mobile from)
    {
        if (from.AccessLevel >= AccessLevel.Developer)
        {
            from.SendGump(new ModernSpawnerGump(this));
        }
    }

    /// <summary>
    /// Returns true when this spawner has active speech triggers.
    /// This enables ModernUO's built-in speech dispatch to call OnSpeech.
    /// </summary>
    public override bool HandlesOnSpeech => _hasSpeechTriggers;

    /// <summary>
    /// Returns true when this spawner has active proximity triggers.
    /// This enables ModernUO's built-in movement dispatch to call OnMovement.
    /// </summary>
    public override bool HandlesOnMovement => _hasProximityTriggers;

    /// <summary>
    /// Called by ModernUO when speech occurs within 15 tiles of this spawner.
    /// Routes to the trigger system for evaluation.
    /// </summary>
    public override void OnSpeech(SpeechEventArgs e)
    {
        if (e.Mobile?.Map == null || e.Mobile.Map == Map.Internal || e.Handled)
        {
            return;
        }

        // Route to the trigger system
        TriggerSystem.Instance.OnSpeech(e.Mobile, e.Speech, e.Mobile.Location, e.Mobile.Map, this);
    }

    /// <summary>
    /// Called by ModernUO when a mobile moves near this spawner.
    /// For normal proximity (within 24 tiles): called via Item.HandlesOnMovement.
    /// For extended proximity (beyond 24 tiles): called via area movement subscription.
    /// Routes to the trigger system for evaluation.
    /// </summary>
    public override void OnMovement(Mobile m, Point3D oldLocation)
    {
        if (m?.Map == null || m.Map == Map.Internal || !Running || Deleted)
        {
            return;
        }

        // For extended proximity triggers, check if mobile is within trigger bounds
        if (!_hasExtendedProximityTriggers || _extendedTriggerBounds.Contains(new Point2D(m.Location.X, m.Location.Y)))
        {
            using var _ = SpawnerMetrics.MeasureProximityDispatch();
            TriggerSystem.Instance.OnMobileProximity(m, m.Location, m.Map, this);
        }
    }

    /// <summary>
    /// Updates whether this spawner has active speech triggers.
    /// Called by TriggerSystem when speech triggers are registered/unregistered.
    /// </summary>
    internal void SetHasSpeechTriggers(bool value)
    {
        _hasSpeechTriggers = value;
    }

    /// <summary>
    /// Updates whether this spawner has active proximity triggers.
    /// Called by TriggerSystem when proximity triggers are registered/unregistered.
    /// </summary>
    internal void SetHasProximityTriggers(bool value)
    {
        _hasProximityTriggers = value;
    }

    /// <summary>
    /// Sets up extended area movement trigger bounds.
    /// Called by TriggerSystem when proximity triggers with extended range are registered.
    /// Note: Extended area movement is not yet supported in ModernUO.
    /// </summary>
    internal void SetExtendedTriggerBounds(Rectangle2D bounds)
    {
        _extendedTriggerBounds = bounds;
        _hasExtendedProximityTriggers = true;
        // Extended area movement subscription is not yet supported in ModernUO
    }

    /// <summary>
    /// Unsubscribes from extended area movement notifications.
    /// Called by TriggerSystem when all extended proximity triggers are unregistered.
    /// </summary>
    internal void UnsubscribeFromExtendedAreaMovement()
    {
        _hasExtendedProximityTriggers = false;
        _extendedTriggerBounds = default;
        // Extended area movement subscription is not yet supported in ModernUO
    }
}
