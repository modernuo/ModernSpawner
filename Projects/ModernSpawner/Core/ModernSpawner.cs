using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ModernUO.Serialization;
using Server.Engines.ModernSpawner.Perf;
using Server.Engines.ModernSpawner.Positioning;
using Server.Engines.ModernSpawner.Scripting;
using Server.Engines.ModernSpawner.Triggers;
using Server.Engines.Spawners;
using Server.Gumps;

namespace Server.Engines.ModernSpawner;

/// <summary>
/// Modern spawner implementation with support for scripting, triggers, and advanced positioning.
/// Owns a list of <see cref="ModernSpawnerEntry"/> through ModernUO's entry-ownership contract, so
/// every base spawn path (Spawn, Defrag, Remove, RemoveAllEntries) runs over the modern entries.
/// </summary>
[SerializationGenerator(0)]
public partial class ModernSpawner : Spawner
{
    // Owned here so the base contract runs over ModernSpawnerEntry; null until the first entry.
    [SerializedIgnoreDupe]
    [SerializableField(0, getter: "private", setter: "private")]
    private List<ModernSpawnerEntry> _spawnEntries;

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
    /// Script serial for script executed after a spawn cycle was attempted. <see cref="Spawn"/>
    /// returns early when the before-spawn script cancels, when the spawner is full, or when it has
    /// no entries, so this script does not run on those cycles - and it runs whether or not the
    /// attempted cycle actually placed an entity.
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
    [SerializedIgnoreDupe]
    [SerializableField(8)]
    private List<string> _triggerDefinitions = [];

    /// <summary>
    /// Whether this spawner is trigger-activated (vs. timer-based). Master switch for this spawner's
    /// trigger definitions: setting it registers or unregisters the triggers immediately through
    /// <see cref="EnsureTriggersActive" />, so there is no window where the flag and the trigger
    /// registry disagree. The backing field is generated; serialization order 9 is unchanged.
    /// </summary>
    // Hand-written [SerializableProperty] rather than [SerializableField(9, fieldChanged:)] so the
    // registration call sits at the mutation point with this doc comment; the generated pipeline
    // (equality check -> assign -> MarkDirty -> callback) is equivalent.
    [SerializableProperty(9)]
    [CommandProperty(AccessLevel.Developer)]
    public bool TriggerActivated
    {
        get => _triggerActivated;
        set
        {
            if (_triggerActivated == value)
            {
                return;
            }

            _triggerActivated = value;
            this.MarkDirty();
            EnsureTriggersActive();
        }
    }

    /// <summary>
    /// External trigger state - set by trigger system.
    /// </summary>
    [SerializableField(10)]
    [SerializedCommandProperty(AccessLevel.Developer)]
    private bool _triggered;

    /// <summary>
    /// Notes field for admin documentation.
    /// </summary>
    [SerializableField(11)]
    [SerializedCommandProperty(AccessLevel.Developer)]
    private string _notes;

    /// <summary>
    /// Selection strategy used each spawn cycle. See <see cref="SpawnCycleMode"/>.
    /// </summary>
    [SerializableField(12)]
    [SerializedCommandProperty(AccessLevel.Developer)]
    private SpawnCycleMode _cycleMode = SpawnCycleMode.Random;

    /// <summary>
    /// In <see cref="SpawnCycleMode.Sequential"/> mode, only entries with
    /// <c>Subgroup == CurrentSubgroup</c> are eligible this cycle.
    /// </summary>
    [SerializableField(13)]
    [SerializedCommandProperty(AccessLevel.Developer)]
    private int _currentSubgroup;

    /// <summary>
    /// Optional safety net for <see cref="SpawnCycleMode.Sequential"/>. If greater than
    /// <see cref="TimeSpan.Zero"/>, the sequence will reset to <see cref="SequentialResetTo"/>
    /// after this much real time has elapsed without an advance. <see cref="TimeSpan.Zero"/>
    /// disables auto-reset.
    /// </summary>
    [SerializableField(14)]
    [SerializedCommandProperty(AccessLevel.Developer)]
    private TimeSpan _sequentialResetTime;

    /// <summary>
    /// Subgroup that <see cref="SequentialResetTime"/> rewinds to. Defaults to 0.
    /// </summary>
    [SerializableField(15)]
    [SerializedCommandProperty(AccessLevel.Developer)]
    private int _sequentialResetTo;

    /// <summary>
    /// When true, <see cref="AdvanceSequence"/> is a no-op. Lets scripts / triggers pin
    /// the spawner on a specific subgroup until explicitly released.
    /// </summary>
    [SerializableField(16)]
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

    /// <summary>Typed view of the entries; the base <see cref="BaseSpawner.Entries"/> is the same list.</summary>
    public IReadOnlyList<ModernSpawnerEntry> ModernEntries =>
        _spawnEntries ?? (IReadOnlyList<ModernSpawnerEntry>)Array.Empty<ModernSpawnerEntry>();

    /// <inheritdoc />
    public override IReadOnlyList<SpawnerEntry> Entries =>
        _spawnEntries ?? (IReadOnlyList<SpawnerEntry>)Array.Empty<SpawnerEntry>();

    /// <inheritdoc />
    protected override ReadOnlySpan<SpawnerEntry> EntrySpan =>
        ReadOnlySpan<SpawnerEntry>.CastUp(CollectionsMarshal.AsSpan(_spawnEntries));

    /// <inheritdoc />
    protected override SpawnerEntry CreateEntry(
        string name,
        int probability,
        int maxCount,
        string properties,
        string parameters
    ) => new ModernSpawnerEntry(this, name, probability, maxCount, properties, parameters);

    /// <inheritdoc />
    protected override void AddEntryCore(SpawnerEntry entry)
    {
        SpawnEntries ??= [];
        AddToSpawnEntries((ModernSpawnerEntry)entry);
    }

    /// <inheritdoc />
    protected override bool RemoveEntryCore(SpawnerEntry entry)
    {
        if (entry is not ModernSpawnerEntry modern || _spawnEntries?.Contains(modern) != true)
        {
            return false;
        }

        RemoveFromSpawnEntries(modern);
        return true;
    }

    /// <inheritdoc />
    protected override void ClearEntriesCore()
    {
        if (_spawnEntries?.Count > 0)
        {
            ClearSpawnEntries();
        }
    }

    /// <inheritdoc />
    protected override void AdoptEntries(IReadOnlyList<SpawnerEntry> entries)
    {
        // Adopting our own list would clear the entries we are about to copy out of it.
        if (ReferenceEquals(entries, _spawnEntries))
        {
            return;
        }

        ClearEntriesCore();
        for (var i = 0; i < entries.Count; i++)
        {
            var source = entries[i];
            ModernSpawnerEntry entry;
            if (source is ModernSpawnerEntry modern)
            {
                entry = modern;
            }
            else
            {
                // A stock entry (legacy save or stock DTO) becomes a modern one; keep its live spawns.
                entry = (ModernSpawnerEntry)CloneEntry(source);
                TransferSpawned(source, entry);
            }

            entry.SetParent(this);
            AddEntryCore(entry);
        }
    }

    /// <inheritdoc />
    protected override SpawnerEntry CloneEntry(SpawnerEntry source)
    {
        var clone = (ModernSpawnerEntry)base.CloneEntry(source);
        if (source is ModernSpawnerEntry modern)
        {
            clone.OnSpawnScript = modern.OnSpawnScript;
            clone.OnDespawnScript = modern.OnDespawnScript;
            clone.MinDelay = modern.MinDelay;
            clone.MaxDelay = modern.MaxDelay;
            clone.PositioningRule = modern.PositioningRule;
            clone.SpawnGroup = modern.SpawnGroup;
            clone.RequireLOS = modern.RequireLOS;
            clone.SpawnAreaOffset = modern.SpawnAreaOffset;
            clone.SpawnRange = modern.SpawnRange;
            clone.LootTemplate = modern.LootTemplate;
            clone.Subgroup = modern.Subgroup;
        }

        return clone;
    }

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
    /// Gets the compiled after-spawn script, or null if not set. It runs at the end of a spawn cycle
    /// that was actually attempted: <see cref="Spawn"/> returns before it when the before-spawn
    /// script cancels, when the spawner is full, or when it has no entries.
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

    /// <summary>Adds an entry with the modern extras set. Zero delays mean "use the spawner's".</summary>
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
        var entry = (ModernSpawnerEntry)AddEntry(creatureName, probability, maxCount, dotimer, properties, parameters);
        entry.OnSpawnScript = onSpawnScript;
        entry.OnDespawnScript = onDespawnScript;
        entry.PositioningRule = positioningRule;
        entry.SpawnGroup = spawnGroup;
        entry.MinDelay = minDelay;
        entry.MaxDelay = maxDelay;
        return entry;
    }

    public override void Spawn()
    {
        using var _ = SpawnerMetrics.MeasureSpawn();

        var beforeScript = OnBeforeSpawnScript;
        if (beforeScript?.IsValid == true)
        {
            var context = new ScriptContext(null, this);
            ScriptEngine.Instance.Execute(beforeScript, context);

            // Check if the script cancelled the spawn
            if (context.CancelSpawn)
            {
                return;
            }
        }

        using (SpawnerMetrics.MeasureDefrag())
        {
            Defrag();
        }

        if (_spawnEntries is not { Count: > 0 } || IsFull)
        {
            return;
        }

        MaybeAutoResetSequence();

        using (SpawnerMetrics.MeasureEntrySelection())
        {
            switch (_cycleMode)
            {
                case SpawnCycleMode.Sequential:
                    SpawnWeightedOne(_currentSubgroup);
                    break;
                case SpawnCycleMode.Group:
                    SpawnGroupMode();
                    break;
                default:
                    SpawnWeightedOne(-1);
                    break;
            }
        }

        var afterScript = OnAfterSpawnScript;
        if (afterScript?.IsValid == true)
        {
            var context = new ScriptContext(null, this);
            ScriptEngine.Instance.Execute(afterScript, context);
        }
    }

    /// <summary>
    /// Spawns one entity from every eligible entry this cycle. When all entries are at
    /// their max count, no further spawns happen until the pack is cleared.
    /// </summary>
    private void SpawnGroupMode()
    {
        var entries = _spawnEntries;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (!entry.IsFull && !entry.Disabled)
            {
                SpawnEntry(entry);
            }
        }
    }

    /// <summary>Weighted pick over eligible entries; <paramref name="subgroup"/> -1 means any subgroup.</summary>
    private void SpawnWeightedOne(int subgroup)
    {
        var entries = _spawnEntries;
        var probsum = 0;

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (IsEligible(entry, subgroup))
            {
                probsum += entry.SpawnedProbability;
            }
        }

        if (probsum <= 0)
        {
            return;
        }

        var rand = Utility.RandomMinMax(1, probsum);

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (!IsEligible(entry, subgroup))
            {
                continue;
            }

            if (rand <= entry.SpawnedProbability)
            {
                SpawnEntry(entry);
                return;
            }

            rand -= entry.SpawnedProbability;
        }
    }

    private static bool IsEligible(ModernSpawnerEntry entry, int subgroup) =>
        !entry.IsFull && !entry.Disabled && (subgroup < 0 || entry.Subgroup == subgroup);

    /// <summary>Spawns one entity from <paramref name="entry"/> and records the attempt's flags.</summary>
    private void SpawnEntry(ModernSpawnerEntry entry)
    {
        using var _ = SpawnerMetrics.MeasureSpawnFromEntry();

        Spawn(entry, out var flags);
        entry.Valid = flags;
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
    /// Brings this spawner's trigger registrations in line with its current state, and is the only
    /// caller of <see cref="ITriggerSystem.ActivateTriggers"/> outside the trigger system itself.
    /// <see cref="ITriggerSystem.ActivateTriggers"/> appends rather than replaces, so this
    /// deactivates first and is therefore safe to call any number of times. Every construction path
    /// that can leave a spawner running with triggers already set - start, deserialization, dupe,
    /// import, migration - ends here, because <see cref="BaseSpawner.Start"/> only reaches
    /// <see cref="OnStarted"/> when <see cref="BaseSpawner.Running"/> actually flips and a
    /// constructed spawner is already running.
    /// </summary>
    internal void EnsureTriggersActive()
    {
        TriggerSystem.Instance.DeactivateTriggers(this);

        if (Running && _triggerActivated && _triggerDefinitions is { Count: > 0 })
        {
            TriggerSystem.Instance.ActivateTriggers(this);
        }
    }

    /// <inheritdoc />
    protected override void OnStarted()
    {
        EnsureTriggersActive();

        var activateScript = OnActivateScript;
        if (activateScript?.IsValid == true)
        {
            ScriptEngine.Instance.Execute(activateScript, new ScriptContext(null, this));
        }
    }

    /// <summary>
    /// Runs the deactivate script and unregisters this spawner's triggers. Deleting a running
    /// spawner reaches here as well, through <see cref="BaseSpawner.OnDelete"/> calling
    /// <see cref="BaseSpawner.Stop"/>, so the deactivate script runs on deletion too:
    /// <see cref="Item.Delete"/> sets <see cref="Item.Deleted"/> only after <see cref="Item.OnDelete"/>
    /// has returned, so there is no state here that distinguishes a stop from a deletion.
    /// </summary>
    protected override void OnStopped()
    {
        var deactivateScript = OnDeactivateScript;
        if (deactivateScript?.IsValid == true)
        {
            ScriptEngine.Instance.Execute(deactivateScript, new ScriptContext(null, this));
        }

        // DeactivateTriggers is a no-op when nothing is registered, so no flag check: the flag can be
        // cleared after registration and must not leave a stale entry behind.
        TriggerSystem.Instance.DeactivateTriggers(this);
    }

    /// <inheritdoc />
    protected override Point3D GetSpawnPosition(SpawnerEntry entry, ISpawnable spawned, Map map)
    {
        if (map == null || map == Map.Internal)
        {
            return Location;
        }

        if (entry is ModernSpawnerEntry modern)
        {
            if (!string.IsNullOrEmpty(modern.PositioningRule))
            {
                var posContext = new PositioningContext(this, spawned, map, modern)
                {
                    MaxZDelta = _maxZDelta
                };

                var position = PositioningRules.GetPosition(modern.PositioningRule, posContext);
                if (position != Point3D.Zero)
                {
                    return position;
                }
            }

            if (modern.SpawnAreaOffset != Point3D.Zero)
            {
                var offset = modern.SpawnAreaOffset;
                return new Point3D(Location.X + offset.X, Location.Y + offset.Y, Location.Z + offset.Z);
            }
        }

        return GetSpawnPosition(spawned, map);
    }

    /// <inheritdoc />
    protected override void OnSpawned(SpawnerEntry entry, ISpawnable spawned)
    {
        SpawnerMetrics.RecordEntitySpawned();

        if (entry is not ModernSpawnerEntry modern)
        {
            return;
        }

        if (spawned is Mobile spawnedMobile && !string.IsNullOrEmpty(modern.LootTemplate))
        {
            Loot.LootTemplateRegistry.ApplyTemplate(modern.LootTemplate, spawnedMobile);
        }

        if (!string.IsNullOrEmpty(modern.OnSpawnScript))
        {
            var compiledScript = ScriptEngine.Instance.Compile(modern.OnSpawnScript);
            if (compiledScript?.IsValid == true)
            {
                ScriptEngine.Instance.Execute(compiledScript, new ScriptContext(spawned, this));
            }
        }
    }

    /// <inheritdoc />
    protected override void OnSpawnedDeath(SpawnerEntry entry, ISpawnable spawned, Mobile killer)
    {
        // Notify the trigger system for kill triggers
        TriggerSystem.Instance.OnEntityKilled(this, spawned, killer);

        if (entry is ModernSpawnerEntry modern && !string.IsNullOrEmpty(modern.OnDespawnScript))
        {
            var compiledScript = ScriptEngine.Instance.Compile(modern.OnDespawnScript);
            if (compiledScript?.IsValid == true)
            {
                var context = new ScriptContext(spawned, this)
                {
                    TriggeringMobile = killer
                };
                ScriptEngine.Instance.Execute(compiledScript, context);
            }
        }
    }

    public override Point3D GetSpawnPosition(ISpawnable spawned, Map map)
    {
        if (map == null || map == Map.Internal)
        {
            return Location;
        }

        // Check for spawn area definition (Width and Height > 0 means it's set)
        if (SpawnBounds is { Width: > 0, Height: > 0 })
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
        var bounds = SpawnBounds;

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return Point3D.Zero;
        }

        // Try 10 times to find a valid location within the spawn area
        for (var i = 0; i < 10; i++)
        {
            var x = Utility.RandomMinMax(bounds.Start.X, bounds.End.X - 1);
            var y = Utility.RandomMinMax(bounds.Start.Y, bounds.End.Y - 1);
            var z = map.GetAverageZ(x, y);

            // If Rectangle3D has Z constraints, respect them
            if (bounds.Depth > 0 && (z < bounds.Start.Z || z >= bounds.End.Z))
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
        // Spawner's rebuild ran before _spawnEntries was read; rebuild over the modern list.
        RebuildSpawned();

        // Activate triggers if spawner is running
        EnsureTriggersActive();
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
    /// Copies the modern fields the dupe contract cannot reach. The base override copies the entries;
    /// <see cref="TriggerDefinitions"/> is <c>[SerializedIgnoreDupe]</c> because the copy must own its
    /// own list rather than share this one, so it is copied here and then registered.
    /// </summary>
    /// <param name="newItem">The freshly duped item.</param>
    public override void OnAfterDuped(Item newItem)
    {
        base.OnAfterDuped(newItem);

        if (newItem is not ModernSpawner copy)
        {
            return;
        }

        // Through the generated setter so the copy is marked dirty.
        copy.TriggerDefinitions = new List<string>(_triggerDefinitions);
        copy.EnsureTriggersActive();
    }

    /// <summary>
    /// Called when this spawner is deleted.
    /// </summary>
    public override void OnDelete()
    {
        // Unsubscribe from extended area movement before deletion
        UnsubscribeFromExtendedAreaMovement();

        // Deactivate triggers before deletion. DeactivateTriggers is a no-op when nothing is registered,
        // so no flag check: the flag can be cleared after registration and must not leave a stale entry behind.
        TriggerSystem.Instance.DeactivateTriggers(this);

        base.OnDelete();
    }

    /// <summary>
    /// Called when the spawner's map changes.
    /// </summary>
    public override void OnMapChange()
    {
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
