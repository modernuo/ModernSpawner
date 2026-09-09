# ModernSpawner Implementation Plan

## Status — 2026-04-19

The plan below remains authoritative for design *intent*. Actual delivery status (not visible from the checkbox sections alone):

- **Phase 1 (BaseSpawner refactor):** Landed in ModernUO `feature/abstract-spawner-entries`.
- **Phase 2 (ModernSpawner core):** Implemented and tested. Core spawner, entries, gumps, commands all in place.
- **Phase 3 (Scripting / expressions):** Implemented with `Expression<T>.Compile()` accessors. `PropertyTrigger` was removed (commit `1c8286b`) in favor of expression-based conditions. 131+ expression-engine tests passing.
- **Phase 4 (Migration system):** Implemented via `XmlSpawnerImporter` (ServUO + Sno formats) and `XmlSpawnerMigrator`.
- **Triggers:** Proximity, Kill, Speech, Skill, TimeOfDay, WallTimeWindow, GameTimeWindow implemented with `TriggerSystem` orchestration.
- **Scheduling:** Implemented via `TimeWindowRecurrencePattern : IRecurrencePattern` and `TimeWindowScheduledEvent`, integrated with ModernUO's `EventScheduler`.
- **Loot system:** `LootTemplate` + `LootTemplateRegistry` implemented; wired at `Core/ModernSpawner.cs:486-488` to apply on creature spawn.
- **Export/import formats:** Binary (ModernUO serializer), JSON (round-trip verified), YAML for complex scripts (round-trip verified). 343 tests passing as of 2026-04-19.

Out-of-scope (confirmed): XmlAttachments, XmlQuests, web stats / remote API, external web config tool. These are in the original design as explicit non-goals and remain so.

Known quirks (Phase 3 follow-up work):
- `SpawnerExportData.Options.SmartPositioning` (default `true`) and `MaxZDelta` (default `20`) are not round-trippable to their "natural" off values (`false`/`0`) because `JsonIgnoreCondition.WhenWritingDefault` uses `default(T)`, not the initializer. Two xunit tests are `Skip`ped with this note. Fix options: drop `WhenWritingDefault`, flip defaults, or use `[JsonInclude]`.

## Overview

This document outlines the implementation plan for ModernSpawner, a modern replacement for XmlSpawner built on ModernUO's spawner infrastructure. The approach involves co-developing with ModernUO to create a cleaner, more extensible architecture.

## Design Decisions

### 1. Extension Approach: Hybrid (Virtual + Composition)
- **Virtual methods** for core spawn lifecycle (simple, performant, familiar)
- **Composition with interfaces** for complex subsystems:
  - `IScriptEngine` - AST compilation and execution
  - `ITriggerSystem` - Trigger evaluation
  - `ISpawnPositioner` - Positioning strategies

### 2. Entry Strategy: Co-develop with BaseSpawner Abstraction
- Refactor BaseSpawner to NOT own entries directly
- Define `ISpawnerEntry` interface for common operations
- Each spawner implementation owns its specific entry type
- Enables clean inheritance without workarounds

### 3. Distribution Model: DLL-First
- Design as drop-in DLL/project
- Requires ModernUO feature branch until changes merge to main
- No awkward workarounds needed

### 4. Property Access Strategy: Typed Expression<T>.Compile() (BENCHMARK VALIDATED)

Based on comprehensive benchmarks (see `PerformanceAnalysis.md`), we will use **typed Expression Tree accessors** for property access:

| Approach | 12k Spawners | vs Direct C# | Memory |
|----------|-------------|--------------|--------|
| Direct C# | 47 μs | 1.00x | 0 B |
| **Expression<T> typed** | **50 μs** | **1.06x** | **0 B** |
| IL Emit typed | 73 μs | 1.54x | 0 B |
| Reflection (current AST) | 893 μs | 18.9x | 2.11 MB |

**Key Findings:**
1. **Typed Expression<T> achieves near-native speed** - only 6% slower than direct C#
2. **IL Emit is slower than Expression Trees** - contrary to expectations, .NET's Expression compiler is highly optimized
3. **Boxing is the performance killer** - not the accessor mechanism; avoid `object` in hot paths
4. **Zero GC pressure** - typed accessors have zero memory allocation

**Decision:** Use `Expression<Func<TSource, TProperty>>.Compile()` for all property access. Do NOT use IL Emit - it adds complexity without performance benefit.

---

## Phase 1: ModernUO BaseSpawner Refactoring

### 1.1 Create Feature Branch

```bash
cd ModernUO
git checkout -b feature/abstract-spawner-entries
```

### 1.2 Define ISpawnerEntry Interface

Create new file: `Engines/Spawners/ISpawnerEntry.cs`

```csharp
namespace Server.Engines.Spawners;

public interface ISpawnerEntry
{
    // Core spawn identification
    string SpawnedName { get; set; }
    int SpawnedProbability { get; set; }
    int SpawnedMaxCount { get; set; }

    // Construction parameters
    string Properties { get; set; }
    string Parameters { get; set; }

    // Spawned entity tracking
    IReadOnlyList<ISpawnable> Spawned { get; }
    bool IsFull { get; }

    // Entity management
    void AddToSpawned(ISpawnable spawnable);
    void RemoveFromSpawned(ISpawnable spawnable);

    // Validation state
    EntryFlags Valid { get; set; }
}
```

### 1.3 Update SpawnerEntry to Implement Interface

Modify: `Engines/Spawners/SpawnerEntry.cs`

```csharp
[SerializationGenerator(1, false)]
public partial class SpawnerEntry : ISpawnerEntry
{
    // Existing implementation remains largely unchanged
    // Add explicit interface implementations where needed

    public void AddToSpawned(ISpawnable spawnable)
    {
        _spawned.Add(spawnable);
        _parent?.MarkDirty();
    }

    public void RemoveFromSpawned(ISpawnable spawnable)
    {
        _spawned.Remove(spawnable);
        _parent?.MarkDirty();
    }
}
```

### 1.4 Refactor BaseSpawner

Key changes to `Engines/Spawners/BaseSpawner.cs`:

#### Remove Entry Field
```csharp
// REMOVE this field - entries now owned by derived classes
// [SerializableField(2, setter: "private")]
// private List<SpawnerEntry> _entries;
```

#### Add Abstract Entry Access
```csharp
/// <summary>
/// Gets the entries for this spawner. Each derived class owns its specific entry type.
/// </summary>
public abstract IReadOnlyList<ISpawnerEntry> Entries { get; }

/// <summary>
/// Gets the count of entries.
/// </summary>
public int EntryCount => Entries?.Count ?? 0;
```

#### Abstract Entry Management Methods
```csharp
/// <summary>
/// Adds an entry to this spawner.
/// </summary>
public abstract ISpawnerEntry AddEntry(
    string name,
    int probability = 100,
    int maxCount = 1,
    bool doTimer = true,
    string properties = null,
    string parameters = null
);

/// <summary>
/// Removes an entry from this spawner.
/// </summary>
public abstract void RemoveEntry(ISpawnerEntry entry);

/// <summary>
/// Removes an entry at the specified index.
/// </summary>
public abstract void RemoveEntryAt(int index);

/// <summary>
/// Clears all entries.
/// </summary>
public abstract void ClearEntries();
```

#### Update Spawn Method Signature
```csharp
// Change from SpawnerEntry to ISpawnerEntry
public bool Spawn(ISpawnerEntry entry, out EntryFlags flags)
{
    // Implementation remains similar but uses interface
}
```

### 1.5 Generated Helper Method Strategy

The SerializationGenerator creates these methods for `List<T>` fields:
- `AddToEntries(entry)` - adds and marks dirty
- `RemoveFromEntries(entry)` - removes and marks dirty
- `InsertIntoEntries(index, entry)` - inserts and marks dirty
- `RemoveFromEntriesAt(index)` - removes at index and marks dirty
- `ClearEntries()` - clears and marks dirty

**Current Issue Found:** `BaseSpawner.RemoveEntry()` at line 807 uses `Entries.Remove(entry)` directly instead of `RemoveFromEntries(entry)`, potentially not marking dirty properly.

**Solution:** When entries move to derived classes, each class gets its own generated helpers:

```csharp
// In Spawner class
[SerializableField(0)]
private List<SpawnerEntry> _entries;

// Generated: AddToEntries, RemoveFromEntries, etc.

public override void RemoveEntry(ISpawnerEntry entry)
{
    if (entry is SpawnerEntry spawnerEntry)
    {
        // Use generated helper for proper dirty tracking
        RemoveFromEntries(spawnerEntry);
    }
}
```

### 1.6 Update Spawner Class

Modify: `Engines/Spawners/Spawner.cs`

```csharp
[SerializationGenerator(1)]  // Bump version
public partial class Spawner : BaseSpawner
{
    [SerializableField(0)]
    private List<SpawnerEntry> _entries;

    public override IReadOnlyList<ISpawnerEntry> Entries => _entries;

    public Spawner() : base()
    {
        _entries = [];
    }

    // Implement abstract methods using generated helpers
    public override ISpawnerEntry AddEntry(
        string name,
        int probability = 100,
        int maxCount = 1,
        bool doTimer = true,
        string properties = null,
        string parameters = null)
    {
        var entry = new SpawnerEntry(this, name, probability, maxCount, properties, parameters);
        AddToEntries(entry);

        if (doTimer)
        {
            DoTimer(TimeSpan.FromSeconds(1));
        }

        return entry;
    }

    public override void RemoveEntry(ISpawnerEntry entry)
    {
        if (entry is SpawnerEntry spawnerEntry && _entries.Contains(spawnerEntry))
        {
            Defrag();

            for (var i = spawnerEntry.Spawned.Count - 1; i >= 0; i--)
            {
                var e = spawnerEntry.Spawned[i];
                spawnerEntry.RemoveFromSpawned(e);
                e?.Delete();
            }

            RemoveFromEntries(spawnerEntry);

            if (Running && !IsFull && !IsTimerRunning)
            {
                DoTimer();
            }

            InvalidateProperties();
        }
    }

    public override void RemoveEntryAt(int index)
    {
        if (index >= 0 && index < _entries.Count)
        {
            RemoveEntry(_entries[index]);
        }
    }

    public override void ClearEntries()
    {
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            RemoveEntry(_entries[i]);
        }
    }
}
```

### 1.7 Update SpawnerGump

The SpawnerGump currently casts to `SpawnerEntry`. Options:
1. Keep SpawnerGump specific to Spawner (it already takes BaseSpawner but accesses SpawnerEntry)
2. Create ISpawnerGump interface for different spawner types
3. Make SpawnerGump generic

**Recommendation:** Keep SpawnerGump as-is for Spawner. ModernSpawner will have its own gump.

```csharp
// SpawnerGump continues to work with SpawnerEntry
// Cast is safe because SpawnerGump is only opened from Spawner
var entry = _spawner.Entries[entryIndex] as SpawnerEntry;
```

### 1.8 Update Other Spawner Types

- **ProximitySpawner**: Inherits from Spawner, no changes needed
- **RegionSpawner**: Inherits from BaseSpawner, needs entry ownership

---

## Phase 2: ModernSpawner Core Implementation

### 2.1 Project Structure

```
XmlSpawner-for-Modernuo/
├── ModernSpawner/
│   ├── ModernSpawner.csproj
│   ├── Core/
│   │   ├── ModernSpawner.cs
│   │   ├── ModernSpawnerEntry.cs
│   │   └── ModernSpawnerGump.cs
│   ├── Scripting/
│   │   ├── IScriptEngine.cs
│   │   ├── ScriptEngine.cs
│   │   ├── Ast/
│   │   │   ├── ScriptNode.cs
│   │   │   ├── SetNode.cs
│   │   │   ├── SpawnNode.cs
│   │   │   └── ...
│   │   └── Parser/
│   │       └── ScriptParser.cs
│   ├── Triggers/
│   │   ├── ITriggerSystem.cs
│   │   ├── TriggerSystem.cs
│   │   └── Triggers/
│   │       ├── ProximityTrigger.cs
│   │       ├── TimeTrigger.cs
│   │       └── ...
│   ├── Positioning/
│   │   ├── ISpawnPositioner.cs
│   │   └── Positioners/
│   │       ├── RandomPositioner.cs
│   │       ├── WaypointPositioner.cs
│   │       └── ...
│   └── Migration/
│       └── XmlSpawnerImporter.cs
```

### 2.2 ModernSpawnerEntry

```csharp
[SerializationGenerator(0, false)]
public partial class ModernSpawnerEntry : ISpawnerEntry
{
    [DirtyTrackingEntity]
    private ModernSpawner _parent;

    // ISpawnerEntry implementation
    [SerializableField(0)]
    private string _spawnedName;

    [SerializableField(1)]
    private int _spawnedProbability;

    [SerializableField(2)]
    private int _spawnedMaxCount;

    [SerializableField(3)]
    private string _properties;

    [SerializableField(4)]
    private string _parameters;

    [SerializableField(5)]
    private List<ISpawnable> _spawned;

    // ModernSpawner-specific fields
    [SerializableField(6)]
    private CompiledScript _compiledScript;  // AST for properties/parameters

    [SerializableField(7)]
    private TriggerConfiguration _triggers;

    [SerializableField(8)]
    private PositioningRules _positioning;

    [SerializableField(9)]
    private LootTableReference _lootTable;

    // Script compilation happens on deserialize or when properties change
    [AfterDeserialization]
    private void CompileScripts()
    {
        if (!string.IsNullOrEmpty(_properties) || !string.IsNullOrEmpty(_parameters))
        {
            _compiledScript = ScriptEngine.Compile(_properties, _parameters);
        }
    }
}
```

### 2.3 ModernSpawner Core

```csharp
[SerializationGenerator(0, false)]
public partial class ModernSpawner : BaseSpawner
{
    [SerializableField(0)]
    private List<ModernSpawnerEntry> _entries;

    // Composed subsystems
    private readonly IScriptEngine _scriptEngine;
    private readonly ITriggerSystem _triggerSystem;

    public override IReadOnlyList<ISpawnerEntry> Entries => _entries;

    public ModernSpawner()
    {
        _entries = [];
        _scriptEngine = new ScriptEngine();
        _triggerSystem = new TriggerSystem();
    }

    public override void Spawn()
    {
        Defrag();

        if (_entries.Count <= 0 || IsFull)
        {
            return;
        }

        // Evaluate triggers
        var triggeredEntries = _entries
            .Where(e => !e.IsFull && _triggerSystem.Evaluate(e.Triggers, this))
            .ToList();

        if (triggeredEntries.Count == 0)
        {
            return;
        }

        // Weighted random selection
        var entry = SelectEntry(triggeredEntries);
        if (entry != null)
        {
            SpawnFromEntry(entry);
        }
    }

    private void SpawnFromEntry(ModernSpawnerEntry entry)
    {
        // Create entity
        var entity = CreateEntity(entry);
        if (entity == null) return;

        // Execute compiled scripts (AST-based, not string parsing)
        if (entry.CompiledScript != null)
        {
            _scriptEngine.Execute(entry.CompiledScript, entity, this);
        }

        // Position entity
        var position = GetSpawnPosition(entity as ISpawnable, Map);

        // Finalize spawn
        FinalizeSpawn(entity, entry, position);
    }
}
```

---

## Phase 3: Scripting System (AST-Based)

### 3.1 Script AST Nodes

```csharp
public abstract class ScriptNode
{
    public abstract void Execute(ScriptContext context);
}

public class SetPropertyNode : ScriptNode
{
    public PropertyPath Target { get; }
    public ValueExpression Value { get; }

    public override void Execute(ScriptContext context)
    {
        var target = Target.Resolve(context);
        var value = Value.Evaluate(context);
        target.SetValue(value);
    }
}

public class SpawnNode : ScriptNode
{
    public string TypeName { get; }
    public ScriptNode[] ChildScripts { get; }
}

public class ConditionalNode : ScriptNode
{
    public Expression Condition { get; }
    public ScriptNode TrueBlock { get; }
    public ScriptNode FalseBlock { get; }
}
```

### 3.2 Compilation Pipeline

```
Input String → Tokenizer → Parser → AST → Optimizer → CompiledScript
     ↓              ↓          ↓        ↓           ↓
"SET/Hits/100"  [SET][/][Hits]  SetNode  (cached)   Executable
                [/][100]
```

---

## Phase 4: Migration System

### 4.1 XmlSpawner Importer

```csharp
public class XmlSpawnerImporter
{
    public ModernSpawner Import(XmlSpawner source)
    {
        var modern = new ModernSpawner
        {
            Location = source.Location,
            Map = source.Map,
            HomeRange = source.HomeRange,
            // ... other properties
        };

        foreach (var obj in source.SpawnObjects)
        {
            var entry = ConvertEntry(obj);
            modern.AddEntry(entry);
        }

        // Convert triggers
        if (source.ProximityRange > 0)
        {
            modern.AddTrigger(new ProximityTrigger(source.ProximityRange));
        }

        return modern;
    }

    private ModernSpawnerEntry ConvertEntry(XmlSpawner.SpawnObject obj)
    {
        var entry = new ModernSpawnerEntry
        {
            SpawnedName = obj.TypeName,
            SpawnedMaxCount = obj.MaxCount,
            // ... other properties
        };

        // Parse XmlSpawner keywords into AST
        if (!string.IsNullOrEmpty(obj.SpawnString))
        {
            entry.CompiledScript = ParseXmlSpawnerScript(obj.SpawnString);
        }

        return entry;
    }
}
```

---

## Verification Checklist

### After BaseSpawner Refactoring
- [x] Spawner creates and spawns entities correctly
- [x] SpawnerGump displays and edits entries
- [x] ProximitySpawner proximity detection works
- [x] RegionSpawner region spawning works
- [x] Serialization/deserialization works
- [x] JSON import/export works (round-trip tested 2026-04-19)
- [x] All existing spawner commands work

### After ModernSpawner Implementation
- [x] ModernSpawner compiles and loads (0 errors on .NET 10, 343 tests passing 2026-04-19)
- [x] Basic spawn functionality works
- [x] Entry management (add/remove) works
- [x] Serialization works
- [x] ModernSpawnerGump displays correctly

### Gaps tracked for follow-up
- [x] Subgroup-aware spawn selection (Phase 3a, 2026-04-19): `SpawnCycleMode` (Random/Sequential/Group), `Subgroup` on `ModernSpawnerEntry`, state fields on `ModernSpawner`, `Spawn()` override iterating `_spawnEntries`, Sequential control API (`GotoSubgroup` / `AdvanceSequence` / `ResetSequence`), auto-reset via `SequentialResetTime`, legacy `IsGroup`/`SequentialSpawn` → cycle mode mapping in importer, JSON round-trip.
- [x] Per-trigger / per-positioner unit tests (Phase 3b, 2026-04-19): Kill / TimeOfDay / GameTimeWindow / WallTimeWindow triggers now have parse + round-trip + factory + pure-logic coverage (60 new tests). `GameTimeWindowTrigger.IsHourInWindow` and `CalculateHoursUntil` exposed as `public static` for direct testing. Positioner registry tests now enforce Description on every built-in rule. Live-Map-dependent `GetPosition` paths remain covered by in-game smoke testing only — this is documented in `QA-Test-Plan.md`.
- [x] End-to-end spawning perf harness (Phase 3c, 2026-04-19): `ModernSpawner/Perf/SpawnerMetrics.cs` exposes opt-in counters (Spawn, SpawnFromEntry, Defrag, entry selection, proximity dispatch, entities spawned) via zero-overhead-when-disabled `ref struct` scopes. `ModernSpawner/Perf/SpawnerPerfCommands.cs` adds admin commands (`[ModernSpawnerPerfSeed`, `PerfStart/Stop/Reset/Dump/Clear`) for driving scenarios on a live server. `Docs/Perf-Runbook.md` documents the two canonical scenarios (idle 10k, idle 10k + player sweep) end-to-end. Synthetic BenchmarkDotNet is kept for expression/property-access micro benchmarks; end-to-end throughput is measured in-game because realistic sector conditions dominate the picture.
- [x] Fix `OptionsData.SmartPositioning` / `MaxZDelta` round-trip quirks (Phase 3d, 2026-04-19): dropped `JsonIgnoreCondition.WhenWritingDefault` on the two outlier fields (their initializer values differ from `default(T)`, so the condition was skipping the *meaningful* values). Also fixed an inverted `!options.SmartPositioning` check in `ExportSpawnerOptions`'s "all defaults → return null" short-circuit. QA plan updated with guidance for future `JsonIgnore` usage.

---

## Timeline

| Phase | Description | Dependencies |
|-------|-------------|--------------|
| 1.1-1.4 | ISpawnerEntry + BaseSpawner refactor | None |
| 1.5-1.8 | Update Spawner + verify | 1.1-1.4 |
| 2.1-2.3 | ModernSpawner core | 1.x complete |
| 3.x | Scripting system | 2.x complete |
| 4.x | Migration system | 3.x complete |

---

## Open Questions Resolved

1. **Extension approach**: Hybrid - virtual for lifecycle, interfaces for subsystems
2. **Entry strategy**: Co-develop with BaseSpawner abstraction using ISpawnerEntry
3. **Distribution**: DLL-first, requires ModernUO feature branch

## Notes

- SpawnerEntry dirty tracking issue found at BaseSpawner.cs:807 - `Entries.Remove()` should use `RemoveFromEntries()`
- SerializationGenerator requires concrete types, so each spawner owns its entry list
- Generated helper methods (AddToX, RemoveFromX, etc.) move to derived classes with entries
