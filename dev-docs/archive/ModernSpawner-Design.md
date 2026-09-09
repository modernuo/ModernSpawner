# ModernSpawner Design Document

## Table of Contents
1. [Executive Summary](#1-executive-summary)
2. [Goals and Non-Goals](#2-goals-and-non-goals)
3. [Architecture Overview](#3-architecture-overview)
4. [Core Spawner System](#4-core-spawner-system)
5. [Trigger System](#5-trigger-system)
6. [Positioning System](#6-positioning-system)
7. [Scripting System](#7-scripting-system)
8. [Serialization Strategy](#8-serialization-strategy)
9. [Migration from XmlSpawner](#9-migration-from-xmlspawner)
10. [Implementation Phases](#10-implementation-phases)
11. [ModernUO Integration Strategy](#11-modernuo-integration-strategy)

---

## 1. Executive Summary

ModernSpawner is a complete reimplementation of spawning functionality for ModernUO, designed to replace XmlSpawner with a high-performance, maintainable, and extensible system.

### Why Rebuild?

| XmlSpawner | ModernSpawner |
|------------|---------------|
| 54,000 lines, 98 files | Target: <8,000 lines |
| 11,880-line monolithic class | Focused single-responsibility classes |
| Runtime string parsing | Compiled AST with one-time parse cost |
| Legacy .NET 1.1 patterns | Modern .NET 10, spans, pooling |
| Tightly coupled features | Modular, pluggable architecture |

### Key Design Principles

1. **Performance First** - Parse once, execute many times via compiled expressions
2. **Separation of Concerns** - Spawning, triggers, positioning, and scripting are independent
3. **ModernUO Alignment** - Follow established patterns from `UOContent/Engines/Spawners`
4. **Extensibility** - New triggers, positioners, and actions via interfaces
5. **Migration Path** - Import existing XmlSpawner configurations

---

## 2. Goals and Non-Goals

### Goals

- **G1**: Spawn any Mobile or Item type with property customization
- **G2**: Support proximity, speech, skill, time-of-day, and property-based triggers
- **G3**: Provide 10+ positioning strategies (random, waypoint, region, tiles, etc.)
- **G4**: Enable loot template overrides and backpack population without code
- **G5**: Allow conditional spawning based on world state
- **G6**: Support inter-spawner communication (spawn chains, despawn commands)
- **G7**: Import XmlSpawner XML export format
- **G8**: Match or exceed XmlSpawner runtime performance
- **G9**: Provide intuitive in-game editing via gumps

### Non-Goals

- **NG1**: Quest system (separate project)
- **NG2**: Attachment/effect system (separate project)
- **NG3**: Talking NPCs (separate project)
- **NG4**: 100% XmlSpawner keyword compatibility (we will modernize the syntax)
- **NG5**: Backward-compatible serialization with XmlSpawner saves

---

## 3. Architecture Overview

```
ModernSpawner/
├── Core/
│   ├── ModernSpawner.cs              # Main spawner item
│   ├── SpawnEntry.cs                 # Individual spawn definition
│   ├── SpawnResult.cs                # Result of spawn attempt
│   └── SpawnerState.cs               # Runtime state tracking
│
├── Positioning/
│   ├── ISpawnPositioner.cs           # Interface for all positioners
│   ├── PositionerRegistry.cs         # Discovery and lookup
│   └── Positioners/
│       ├── RandomPositioner.cs
│       ├── WaypointPositioner.cs
│       ├── RegionPositioner.cs
│       ├── RelativePositioner.cs
│       ├── TileFilterPositioner.cs
│       └── PerimeterPositioner.cs
│
├── Triggers/
│   ├── ITrigger.cs                   # Interface for all triggers
│   ├── TriggerContext.cs             # Context passed to triggers
│   ├── TriggerRegistry.cs            # Discovery and lookup
│   └── Triggers/
│       ├── ProximityTrigger.cs
│       ├── SpeechTrigger.cs
│       ├── SkillTrigger.cs
│       ├── TimeOfDayTrigger.cs
│       ├── PropertyTrigger.cs
│       └── ExternalTrigger.cs
│
├── Scripting/
│   ├── AST/
│   │   ├── SpawnScript.cs            # Root AST node
│   │   ├── Nodes/
│   │   │   ├── PropertySetNode.cs
│   │   │   ├── ConditionalNode.cs
│   │   │   ├── SpawnCommandNode.cs
│   │   │   ├── DespawnCommandNode.cs
│   │   │   └── GotoCommandNode.cs
│   │   └── Values/
│   │       ├── LiteralValue.cs
│   │       ├── PropertyReference.cs
│   │       ├── RandomRangeValue.cs
│   │       └── PlayerCountValue.cs
│   ├── Parser/
│   │   ├── ScriptParser.cs           # String -> AST
│   │   ├── ScriptTokenizer.cs        # Lexical analysis
│   │   └── ParseException.cs
│   ├── Compiler/
│   │   ├── ScriptCompiler.cs         # AST -> Executable
│   │   └── CompiledScript.cs         # Cached executable form
│   └── Runtime/
│       ├── ScriptExecutor.cs         # Executes compiled scripts
│       └── ExecutionContext.cs       # Runtime state
│
├── Serialization/
│   ├── SpawnerSerializer.cs          # Binary save/load
│   └── SpawnerJsonConverter.cs       # JSON import/export
│
├── Migration/
│   ├── XmlSpawnerImporter.cs         # Import XmlSpawner format
│   └── SyntaxConverter.cs            # Convert old syntax to new
│
├── Gumps/
│   ├── SpawnerEditorGump.cs          # Main editor
│   └── ScriptEditorGump.cs           # Script editing
│
└── Commands/
    ├── SpawnerCommands.cs            # [modernspawner commands
    ├── ImportCommand.cs
    └── ExportCommand.cs
```

---

## 4. Core Spawner System

### 4.1 ModernSpawner Class

```csharp
[SerializationGenerator(1, false)]
public partial class ModernSpawner : Item, ISpawner
{
    // Identity
    [SerializableField(0)] private string _name;
    [SerializableField(1)] private Guid _guid;

    // Timing
    [SerializableField(2)] private TimeSpan _minDelay;
    [SerializableField(3)] private TimeSpan _maxDelay;
    [SerializableField(4)] private TimeSpan _despawnTime;

    // Spatial
    [SerializableField(5)] private int _homeRange;
    [SerializableField(6)] private int _spawnRange;
    [SerializableField(7)] private int _walkingRange;

    // Configuration
    [SerializableField(8)] private bool _isGroup;
    [SerializableField(9)] private int _team;
    [SerializableField(10)] private bool _isRunning;

    // Entries and State
    [SerializableField(11)] private List<SpawnEntry> _entries;
    [SerializableField(12)] private SpawnerState _state;

    // Triggers (optional, can be null)
    [SerializableField(13)] private List<ITrigger>? _triggers;

    // Positioner (defaults to RandomPositioner)
    [SerializableField(14)] private ISpawnPositioner _positioner;

    // Spawned entity tracking
    private Dictionary<ISpawnable, SpawnEntry> _spawned;

    // Timer (not serialized)
    private SpawnerTimer? _timer;
}
```

### 4.2 SpawnEntry Class

```csharp
[SerializationGenerator(1, false)]
public partial class SpawnEntry
{
    [SerializableField(0)] private string _typeName;
    [SerializableField(1)] private int _maxCount;
    [SerializableField(2)] private int _probability;
    [SerializableField(3)] private int _subGroup;

    // Compiled script (parsed once, executed on each spawn)
    [SerializableField(4)] private CompiledScript? _spawnScript;

    // Optional positioner override
    [SerializableField(5)] private ISpawnPositioner? _positionerOverride;

    // Runtime state
    [SerializableFieldSaveFlag(6)] private List<ISpawnable> _spawnedObjects;

    // Cached type (not serialized, resolved on first use)
    private Type? _resolvedType;

    public int CurrentCount => _spawnedObjects.Count;
    public bool IsFull => CurrentCount >= _maxCount;
}
```

### 4.3 Spawn Flow

```
1. Timer Tick / Trigger Activated
         │
         ▼
2. ModernSpawner.Spawn()
         │
         ├─► Check if running, not full
         │
         ▼
3. Select SpawnEntry (weighted probability)
         │
         ▼
4. entry.TrySpawn(context)
         │
         ├─► Evaluate conditions (if any)
         │
         ├─► Get position from Positioner
         │
         ├─► Create entity instance
         │
         ▼
5. Execute CompiledScript (property sets, etc.)
         │
         ▼
6. Place entity in world
         │
         ▼
7. Track in _spawned dictionary
```

---

## 5. Trigger System

### 5.1 ITrigger Interface

```csharp
public interface ITrigger
{
    /// <summary>
    /// Unique identifier for this trigger type (for serialization)
    /// </summary>
    string TypeId { get; }

    /// <summary>
    /// Called when trigger should start monitoring
    /// </summary>
    void Activate(ModernSpawner spawner);

    /// <summary>
    /// Called when trigger should stop monitoring
    /// </summary>
    void Deactivate();

    /// <summary>
    /// Check if trigger conditions are currently met
    /// </summary>
    bool Evaluate(TriggerContext context);

    /// <summary>
    /// Serialize trigger-specific data
    /// </summary>
    void Serialize(IGenericWriter writer);

    /// <summary>
    /// Deserialize trigger-specific data
    /// </summary>
    void Deserialize(IGenericReader reader);
}
```

### 5.2 TriggerContext

```csharp
public readonly struct TriggerContext
{
    public Mobile? TriggeringMobile { get; init; }
    public Item? TriggeringItem { get; init; }
    public string? Speech { get; init; }
    public SkillName? Skill { get; init; }
    public Point3D Location { get; init; }
    public Map Map { get; init; }
    public DateTime Timestamp { get; init; }
}
```

### 5.3 Trigger Implementations

| Trigger | Purpose | Key Properties |
|---------|---------|----------------|
| ProximityTrigger | Player enters range | Range, PlayerOnly, GhostAllowed |
| SpeechTrigger | Keyword spoken | Keywords[], CaseSensitive, Range |
| SkillTrigger | Skill used nearby | SkillName, MinValue, MaxValue, Range |
| TimeOfDayTrigger | Time-based activation | StartHour, EndHour, UseGameTime |
| PropertyTrigger | Object property changes | TargetSerial, PropertyPath, Condition |
| ExternalTrigger | Programmatic activation | (none - triggered via code) |

### 5.4 Trigger Composition

Spawners can have **multiple triggers** with AND/OR logic:

```csharp
public enum TriggerMode
{
    Any,  // OR - any trigger fires the spawner
    All   // AND - all triggers must be satisfied
}
```

---

## 6. Positioning System

### 6.1 ISpawnPositioner Interface

```csharp
public interface ISpawnPositioner
{
    /// <summary>
    /// Unique identifier for serialization
    /// </summary>
    string TypeId { get; }

    /// <summary>
    /// Attempt to find a valid spawn location
    /// </summary>
    /// <param name="spawner">The spawner requesting position</param>
    /// <param name="context">Trigger context (may have TriggeringMobile)</param>
    /// <param name="location">Output location if successful</param>
    /// <returns>True if valid location found</returns>
    bool TryGetSpawnPosition(
        ModernSpawner spawner,
        TriggerContext context,
        out Point3D location);

    void Serialize(IGenericWriter writer);
    void Deserialize(IGenericReader reader);
}
```

### 6.2 Positioner Implementations

| Positioner | XmlSpawner Equivalent | Description |
|------------|----------------------|-------------|
| RandomPositioner | #RANDOM (default) | Random within SpawnRange |
| WaypointPositioner | #WAYPOINT | At specific waypoint item |
| RelativePositioner | #RELXY | Offset from spawner |
| PlayerRelativePositioner | #PLAYER, #DXY | Relative to triggering player |
| AbsolutePositioner | #XY | Fixed world coordinates |
| PerimeterPositioner | #EDGE, #PERIMETER | Edge of spawn area |
| RowFillPositioner | #ROWFILL, #XFILL | Fill horizontally |
| ColumnFillPositioner | #COLFILL, #YFILL | Fill vertically |
| TileFilterPositioner | #TILES, #NOTILES | Filter by tile IDs |
| ItemFilterPositioner | #ITEMID, #NOITEMID | Filter by item presence |
| WaterPositioner | #WET | Water tiles only |
| RegionPositioner | (new) | Weighted by region rectangles |

### 6.3 Positioner Composition

Positioners can be **chained** for complex logic:

```csharp
public class CompositePositioner : ISpawnPositioner
{
    private ISpawnPositioner _primary;
    private ISpawnPositioner? _fallback;
    private ITileFilter? _filter;

    public bool TryGetSpawnPosition(...)
    {
        if (_primary.TryGetSpawnPosition(spawner, context, out var loc))
        {
            if (_filter == null || _filter.IsValid(loc, spawner.Map))
                return true;
        }
        return _fallback?.TryGetSpawnPosition(spawner, context, out loc) ?? false;
    }
}
```

---

## 7. Scripting System

This is the most critical design decision. XmlSpawner parses strings at runtime on every spawn, which is inefficient. After comprehensive benchmarking (see `PerformanceAnalysis.md`), we have validated the optimal approach.

### 7.0 BENCHMARK-VALIDATED DECISION: Typed Expression<T>.Compile()

Our benchmarks with 12,000 spawners definitively show:

| Approach | Execution Time | vs Direct C# | Memory/Cycle |
|----------|---------------|--------------|--------------|
| Direct C# | 47 μs | 1.00x | 0 B |
| **Expression<T> typed** | **50 μs** | **1.06x** | **0 B** |
| IL Emit typed | 73 μs | 1.54x | 0 B |
| Reflection (no cache) | 893 μs | 18.9x | 2.11 MB |

**Critical Findings:**
1. **Typed Expression<T> is near-native speed** - only 6% overhead
2. **IL Emit is SLOWER than Expression Trees** - .NET's Expression compiler is highly optimized
3. **Boxing is the killer** - both Expression and IL Emit boxed are 11x slower than typed
4. **Zero allocation** - typed accessors produce no GC pressure

**DECISION:** Use `Expression<Func<TSource, TProperty>>.Compile()` for all property access. Avoid IL Emit - it adds complexity without benefit.

---

### 7.1 Option A: Improved String Parsing (Not Recommended)

Keep the string-based approach but optimize parsing.

**Pros:**
- Familiar to XmlSpawner users
- Simple migration

**Cons:**
- Still parsing strings on every spawn
- String manipulation allocates memory
- Hard to validate at edit time
- No compile-time error checking

**Verdict:** Rejected - doesn't solve the core performance problem.

---

### 7.2 Option B: Compiled AST (Recommended)

Parse strings **once** into an Abstract Syntax Tree, then compile to executable form.

```
String Input                     AST                         Compiled
─────────────                   ─────                       ──────────
"Hue/500/Name/Guard"    ──►    PropertySetNode[]    ──►    Action<object, ctx>
                                  ├─ Hue = 500
                                  └─ Name = "Guard"
```

#### 7.2.1 AST Node Hierarchy

```csharp
// Base node
public abstract class ScriptNode
{
    public abstract void Execute(ExecutionContext ctx);
}

// Property assignment
public class PropertySetNode : ScriptNode
{
    public PropertyPath Target { get; }      // e.g., "Backpack.MaxItems"
    public IValueNode Value { get; }         // Literal, reference, or computed

    // Cached reflection info (resolved once)
    private PropertyInfo? _cachedProperty;
    private Action<object, object?>? _compiledSetter;
}

// Value nodes
public interface IValueNode
{
    object? Evaluate(ExecutionContext ctx);
}

public class LiteralValue : IValueNode { ... }           // "500", "Guard"
public class PropertyReference : IValueNode { ... }       // {Spawner.Name}
public class RandomRangeValue : IValueNode { ... }        // Random(10, 20)
public class PlayerCountValue : IValueNode { ... }        // PlayersInRange(15)
public class IncrementValue : IValueNode { ... }          // INC(5) or INC(5, 10)

// Conditional
public class ConditionalNode : ScriptNode
{
    public ICondition Condition { get; }
    public ScriptNode[] ThenBranch { get; }
    public ScriptNode[]? ElseBranch { get; }
}

// Commands
public class SpawnCommandNode : ScriptNode { ... }        // Spawn other spawner
public class DespawnCommandNode : ScriptNode { ... }      // Clear subgroup
public class GotoCommandNode : ScriptNode { ... }         // Jump to subgroup
```

#### 7.2.2 Compilation Process

```csharp
public class ScriptCompiler
{
    public CompiledScript Compile(SpawnScript ast)
    {
        var actions = new List<Action<object, ExecutionContext>>();

        foreach (var node in ast.Nodes)
        {
            switch (node)
            {
                case PropertySetNode propSet:
                    // Pre-resolve property info
                    var setter = CreateOptimizedSetter(propSet);
                    actions.Add(setter);
                    break;

                case ConditionalNode cond:
                    // Compile condition and branches
                    var compiled = CompileConditional(cond);
                    actions.Add(compiled);
                    break;
                // ... other node types
            }
        }

        return new CompiledScript(actions);
    }

    private Action<object, ExecutionContext> CreateOptimizedSetter(PropertySetNode node)
    {
        // Use expression trees for maximum performance
        var targetParam = Expression.Parameter(typeof(object));
        var ctxParam = Expression.Parameter(typeof(ExecutionContext));

        // Build expression tree for property access
        var propertyAccess = BuildPropertyAccess(node.Target, targetParam);
        var valueExpr = BuildValueExpression(node.Value, ctxParam);
        var assignment = Expression.Assign(propertyAccess, valueExpr);

        return Expression.Lambda<Action<object, ExecutionContext>>(
            assignment, targetParam, ctxParam).Compile();
    }
}
```

#### 7.2.3 CompiledScript

```csharp
public class CompiledScript
{
    private readonly Action<object, ExecutionContext>[] _actions;

    // Source preserved for editing/export
    public string OriginalSource { get; }

    // Validation errors found during compilation
    public IReadOnlyList<CompilationError> Errors { get; }

    public bool IsValid => Errors.Count == 0;

    public void Execute(object target, ExecutionContext ctx)
    {
        foreach (var action in _actions)
        {
            action(target, ctx);
        }
    }
}
```

**Pros:**
- Parse once, execute many times (O(1) per spawn after initial parse)
- Expression trees compile to near-native performance
- Validation at edit time with meaningful errors
- Can cache PropertyInfo resolution
- Memory efficient (no string allocations during spawn)

**Cons:**
- More complex implementation
- Initial parse is slower (acceptable - happens once)
- Debugging compiled expressions is harder

**Verdict:** Recommended - best balance of compatibility and performance.

---

### 7.3 Option C: Source Generators (Alternative)

Use C# source generators to compile scripts at build time.

```csharp
// User defines in data file:
// SpawnScript: "Hue/500/Name/Guard"

// Source generator produces:
public static class GeneratedSpawnScripts
{
    public static void Script_ABC123(object target, ExecutionContext ctx)
    {
        if (target is Mobile m)
        {
            m.Hue = 500;
            m.Name = "Guard";
        }
    }
}
```

**Pros:**
- True compile-time validation
- Native code performance
- No runtime parsing at all

**Cons:**
- Scripts must be known at build time (no runtime editing)
- Requires recompilation to change scripts
- Doesn't support hot-reloading configurations
- Complex generator implementation

**Verdict:** Not suitable - we need runtime editability.

---

### 7.4 Option D: Embedded Scripting Language (Future)

Integrate Lua, JavaScript (Jint), or C# scripting (Roslyn).

```lua
-- Example Lua script
function onSpawn(entity, ctx)
    entity.Hue = 500
    entity.Name = "Guard"

    if ctx.PlayersInRange(15) > 5 then
        entity.Hits = entity.HitsMax
    end
end
```

**Pros:**
- Full programming language power
- Familiar to scripters
- Rich debugging tools available

**Cons:**
- Additional dependency (Lua runtime, Roslyn, etc.)
- Security concerns (sandboxing required)
- Steeper learning curve for simple tasks
- Interop overhead between .NET and script

**Verdict:** Consider for v2.0 as an advanced option.

---

### 7.5 Recommended Approach: Hybrid AST with Improved Syntax

We'll implement **Option B (Compiled AST)** with a **modernized syntax** that's cleaner than XmlSpawner's slash-delimited format.

#### New Syntax Design

```
// Simple property assignment
Hue = 500
Name = "Royal Guard"

// Nested property (dot notation)
Backpack.MaxItems = 50

// Random range
Str = Random(80, 100)
Dex = Random(60, 80)

// Increment existing value
Hits = Inc(50)
Gold = Inc(100, 500)

// Dynamic values
Team = Spawner.Team
Hue = TriggerMob.Hue

// Players in range
Damage = PlayersInRange(15) * 10

// Conditional
If TriggerMob.Karma > 1000 Then
    Hue = 0x21
    Title = "Blessed"
Else
    Hue = 0x22
    Title = "Cursed"
End

// Loot template (special syntax)
Loot.Clear()
Loot.Add("Gold", Random(100, 500))
Loot.Add("Bandage", 5)
Loot.Add("RandomWeapon", If(Luck > 50, 1, 0))

// Backpack population
Backpack.Add("Gold", 1000)
Backpack.Add("IronIngot", 50)

// Commands
Spawn("OtherSpawner", SubGroup: 2)
Despawn("CleanupSpawner", SubGroup: 1)
Goto(5)
```

#### Syntax Comparison

| XmlSpawner | ModernSpawner |
|------------|---------------|
| `Hue/500` | `Hue = 500` |
| `Name/@Guard` | `Name = "Guard"` |
| `Str/INC,10,20` | `Str = Inc(10, 20)` |
| `Damage/PLAYERSINRANGE,15` | `Damage = PlayersInRange(15)` |
| `Master/TRIGMOB` | `Master = TriggerMob` |
| `Backpack.MaxItems/50` | `Backpack.MaxItems = 50` |
| `#CONDITION,Gold>100/Dragon` | `If Gold > 100 Then ...` |
| `SPAWN,Other,2` | `Spawn("Other", SubGroup: 2)` |

---

### 7.6 Loot System Design

One of XmlSpawner's powerful features is overriding creature loot. We'll provide first-class support:

```csharp
public class LootTemplate
{
    public bool ClearDefaultLoot { get; set; }
    public List<LootEntry> Entries { get; }
}

public class LootEntry
{
    public string TypeName { get; }           // Item type to spawn
    public IValueNode Count { get; }          // Can be literal or Random()
    public ICondition? Condition { get; }     // Optional spawn condition
    public CompiledScript? ItemScript { get; } // Properties to set on item
}
```

Script syntax:
```
// Clear existing loot and add custom
Loot.Clear()
Loot.Add("Gold", Random(500, 1000))
Loot.Add("Diamond", 1, Condition: "Luck > 100")
Loot.Add("MagicWeapon", 1) {
    Hue = Random(1, 1000)
    Identified = true
}
```

---

## 8. Serialization Strategy

### 8.1 Binary Serialization (Save/Load)

Use ModernUO's `[SerializationGenerator]` pattern for efficient binary saves:

```csharp
[SerializationGenerator(1, false)]
public partial class ModernSpawner : Item
{
    // Fields marked with [SerializableField] auto-serialize
}
```

### 8.2 JSON Serialization (Import/Export)

For human-readable configs and migration:

```json
{
    "type": "ModernSpawner",
    "guid": "550e8400-e29b-41d4-a716-446655440000",
    "name": "Guard Spawner",
    "location": { "x": 1234, "y": 5678, "z": 0 },
    "map": "Felucca",
    "minDelay": "00:05:00",
    "maxDelay": "00:10:00",
    "homeRange": 10,
    "spawnRange": 5,
    "isGroup": false,
    "team": 0,
    "entries": [
        {
            "typeName": "WarriorGuard",
            "maxCount": 3,
            "probability": 70,
            "script": "Hue = 0x8000\nName = \"Royal Guard\""
        },
        {
            "typeName": "ArcherGuard",
            "maxCount": 2,
            "probability": 30,
            "script": "Hue = 0x8001"
        }
    ],
    "triggers": [
        {
            "type": "Proximity",
            "range": 8,
            "playerOnly": true
        }
    ],
    "positioner": {
        "type": "Random"
    }
}
```

### 8.3 Script Storage

Scripts are stored as **source strings** but **compiled on first access**:

```csharp
public partial class SpawnEntry
{
    [SerializableField(4)]
    private string? _scriptSource;

    // Compiled form - not serialized, lazy-initialized
    private CompiledScript? _compiledScript;

    public CompiledScript? Script
    {
        get
        {
            if (_compiledScript == null && _scriptSource != null)
            {
                _compiledScript = ScriptCompiler.Compile(_scriptSource);
            }
            return _compiledScript;
        }
    }
}
```

---

## 9. Migration from XmlSpawner

### 9.1 Import Process

```
XmlSpawner XML File
        │
        ▼
XmlSpawnerImporter.Parse()
        │
        ├─► Extract spawner metadata (location, delays, ranges)
        │
        ├─► Convert spawn entries
        │   │
        │   └─► SyntaxConverter.Convert(oldSyntax)
        │       │
        │       └─► Parse XmlSpawner format
        │           Convert to new syntax
        │           Return ModernSpawner script
        │
        ├─► Map triggers (proximity, speech, skill, TOD)
        │
        ├─► Map positioners (#RANDOM → RandomPositioner, etc.)
        │
        ▼
ModernSpawner instance
```

### 9.2 Syntax Conversion Examples

```csharp
public class SyntaxConverter
{
    public string Convert(string xmlSpawnerSyntax)
    {
        // "Orc/Hue/500/Name/@Guard/Str/INC,10,20"
        // becomes:
        // "Hue = 500\nName = \"Guard\"\nStr = Inc(10, 20)"
    }
}
```

| XmlSpawner Input | ModernSpawner Output |
|------------------|---------------------|
| `Orc/Hue/500` | `Hue = 500` |
| `Dragon/Str/INC,50,100` | `Str = Inc(50, 100)` |
| `Guard/Master/TRIGMOB` | `Master = TriggerMob` |
| `#CONDITION,Gold>100/Dragon` | Entry condition: `Gold > 100` |
| `SET,MyItem/Hue/500` | `Set("MyItem", "Hue", 500)` |
| `SPAWN,Other,2` | `Spawn("Other", SubGroup: 2)` |

### 9.3 Migration Command

```
[modernspawner import <filename> [options]
    --format=xml|map|json     Input format (auto-detected if omitted)
    --overwrite               Replace existing spawners at same locations
    --validate                Validate only, don't create spawners
    --report=<file>           Write conversion report
```

### 9.4 Handling Unsupported Features

Some XmlSpawner features may not have direct equivalents:

| XmlSpawner Feature | Migration Strategy |
|-------------------|-------------------|
| COMMAND keyword | Log warning, require manual review |
| Complex nested SET | Convert to multi-line script |
| Custom enum types | Preserve as string, validate at runtime |
| SmartSpawning | Ignore (was removed) |

---

## 10. Implementation Phases

### Phase 1: Core Foundation (Week 1-2)
- [ ] Project setup following ModernUO patterns
- [ ] ModernSpawner base class with serialization
- [ ] SpawnEntry with basic spawning
- [ ] RandomPositioner (default)
- [ ] Timer and spawn lifecycle
- [ ] Basic gump for editing

### Phase 2: Triggers (Week 2-3)
- [ ] ITrigger interface and registry
- [ ] ProximityTrigger
- [ ] SpeechTrigger
- [ ] TimeOfDayTrigger
- [ ] SkillTrigger
- [ ] Trigger composition (AND/OR)

### Phase 3: Positioners (Week 3-4)
- [ ] ISpawnPositioner interface
- [ ] WaypointPositioner
- [ ] RelativePositioner
- [ ] TileFilterPositioner
- [ ] PerimeterPositioner
- [ ] CompositePositioner

### Phase 4: Scripting Engine (Week 4-6)
- [ ] Script tokenizer
- [ ] Script parser (string → AST)
- [ ] AST node types
- [ ] Script compiler (AST → executable)
- [ ] Property resolution and caching
- [ ] Value nodes (literals, references, random, etc.)
- [ ] Conditional execution
- [ ] Loot template system

### Phase 5: Commands & Inter-Spawner (Week 6-7)
- [ ] Spawn command (trigger other spawners)
- [ ] Despawn command
- [ ] Goto command (sequential spawning)
- [ ] Spawner registry for lookups

### Phase 6: Migration (Week 7-8)
- [ ] XmlSpawner XML parser
- [ ] Syntax converter
- [ ] Positioner mapping
- [ ] Trigger mapping
- [ ] Import command
- [ ] Conversion report generation

### Phase 7: Polish & Testing (Week 8-9)
- [ ] Complete gump interface
- [ ] Export command (JSON)
- [ ] Performance benchmarking
- [ ] Edge case handling
- [ ] Documentation

---

## 11. ModernUO Integration Strategy

Since the ModernSpawner author is the ModernUO maintainer, we have the unique opportunity to leverage and potentially enhance the existing spawner infrastructure rather than rebuilding from scratch.

### 11.1 What BaseSpawner Already Provides

ModernUO's `BaseSpawner` (945 lines) is a well-designed abstract base class that provides:

| Feature | Implementation | Can Reuse? |
|---------|---------------|-----------|
| **Serialization** | `[SerializationGenerator(10)]` with 12 fields | Yes |
| **Entry Management** | `List<SpawnerEntry>` with add/remove | Yes |
| **Spawn Tracking** | `Dictionary<ISpawnable, SpawnerEntry>` | Yes |
| **Timer System** | `InternalTimer` with min/max delay | Yes |
| **Weighted Selection** | Probability-based entry selection | Yes |
| **Property Setting** | Reflection-based via `Properties` string | Extend |
| **Constructor Params** | `Parameters` string parsing | Yes |
| **Group Mode** | Spawn all at once, wait for all to die | Yes |
| **Defragmentation** | Remove dead/invalid spawns | Yes |
| **JSON Import/Export** | `DynamicJson` constructor + `ToJson()` | Extend |
| **ISpawner Interface** | Full implementation | Yes |
| **Gump Integration** | `SpawnerGump` on double-click | Replace |

**Key Extension Points:**
- `GetSpawnPosition(ISpawnable, Map)` - abstract, must override
- `OnDefragSpawn(ISpawnable, bool)` - virtual, customize removal
- `GetSpawnerProperties(IPropertyList)` - virtual, custom display
- `GetWalkingRange()` / `GetSpawnMap()` - virtual overrides
- `DoTimer(TimeSpan)` - virtual, customize timing
- `Spawn()` - virtual, can intercept spawn logic

### 11.2 What SpawnerEntry Already Provides

```csharp
public partial class SpawnerEntry
{
    string SpawnedName;        // Type to spawn
    int SpawnedProbability;    // Weight (1-100)
    int SpawnedMaxCount;       // Max instances
    string Properties;         // "Hue 500 Name Guard" format
    string Parameters;         // Constructor args
    List<ISpawnable> Spawned;  // Currently alive spawns
}
```

**Limitation:** Properties/Parameters are parsed at spawn time, not compiled.

### 11.3 Recommended Integration Approach

Rather than replacing BaseSpawner, **ModernSpawner will inherit from Spawner** and add advanced features:

```
                    Item
                      │
                      ▼
                 BaseSpawner (abstract)
                      │
          ┌───────────┴───────────┐
          │                       │
          ▼                       ▼
       Spawner              ProximitySpawner
          │                       │
          ▼                       ▼
    ModernSpawner          (inherits from Spawner)
```

**Rationale:**
- Spawner provides random positioning and water detection
- We override `Spawn()` to inject our scripting engine
- We add triggers as a composable layer on top
- We extend entry with compiled scripts

### 11.4 ModernSpawner Class Design (Updated)

```csharp
[SerializationGenerator(1, false)]
public partial class ModernSpawner : Spawner
{
    // === NEW: Advanced Triggers ===
    [SerializableField(0)]
    private List<ITrigger>? _triggers;

    [SerializableField(1)]
    private TriggerMode _triggerMode;  // Any or All

    // === NEW: Compiled Scripts per Entry ===
    // Stored as extension data on entries (see 11.5)

    // === NEW: Subgroup Support ===
    [SerializableField(2)]
    private int _sequentialGroup;

    [SerializableField(3)]
    private bool _sequentialMode;

    // === NEW: Custom Positioner ===
    [SerializableField(4)]
    private ISpawnPositioner? _positioner;

    // === OVERRIDE: Inject scripting into spawn ===
    public override void Spawn()
    {
        // Check triggers first
        if (_triggers != null && !EvaluateTriggers())
            return;

        base.Spawn();  // Use BaseSpawner's weighted selection
    }

    // === OVERRIDE: Custom positioning ===
    public override Point3D GetSpawnPosition(ISpawnable spawned, Map map)
    {
        if (_positioner != null)
            return _positioner.GetPosition(this, spawned, map);

        return base.GetSpawnPosition(spawned, map);  // Random fallback
    }

    // === NEW: Execute scripts after spawn ===
    protected override void OnAfterSpawn(ISpawnable spawned, SpawnerEntry entry)
    {
        if (entry is ModernSpawnerEntry modern && modern.CompiledScript != null)
        {
            var ctx = new ExecutionContext(this, spawned, _lastTriggerContext);
            modern.CompiledScript.Execute(spawned, ctx);
        }
    }
}
```

### 11.5 Extended Entry Design

We'll create `ModernSpawnerEntry` extending `SpawnerEntry`:

```csharp
[SerializationGenerator(1, false)]
public partial class ModernSpawnerEntry : SpawnerEntry
{
    // Script source (serialized)
    [SerializableField(0)]
    private string? _scriptSource;

    // Compiled form (lazy, not serialized)
    private CompiledScript? _compiledScript;

    // Subgroup for sequential spawning
    [SerializableField(1)]
    private int _subGroup;

    // Condition for conditional spawning
    [SerializableField(2)]
    private string? _conditionSource;

    private CompiledCondition? _compiledCondition;

    // Per-entry positioner override
    [SerializableField(3)]
    private ISpawnPositioner? _positionerOverride;

    public CompiledScript? CompiledScript
    {
        get
        {
            if (_compiledScript == null && _scriptSource != null)
                _compiledScript = ScriptCompiler.Compile(_scriptSource);
            return _compiledScript;
        }
    }
}
```

### 11.6 Potential ModernUO Base Class Enhancements

These modifications to ModernUO could benefit **both** the standard Spawner and ModernSpawner:

#### Enhancement 1: Virtual Hook After Spawn

```csharp
// In BaseSpawner.cs, after line 705:
protected virtual void OnAfterEntitySpawn(ISpawnable spawned, SpawnerEntry entry)
{
    // Override point for post-spawn customization
}
```

**Benefit:** Allows ModernSpawner to execute scripts without overriding entire `Spawn()` method.

#### Enhancement 2: Trigger Context on ISpawner

```csharp
// In Server/Interfaces.cs
public interface ISpawner : IEntity
{
    // ... existing ...

    // NEW: Last trigger context (for TriggerMob reference)
    TriggerContext? LastTriggerContext { get; }
}
```

**Benefit:** Scripts can reference the triggering mobile consistently.

#### Enhancement 3: Entry Type Flexibility

```csharp
// In BaseSpawner.cs
protected virtual SpawnerEntry CreateEntry(string name, int prob, int max, string props, string parms)
{
    return new SpawnerEntry(this, name, prob, max, props, parms);
}
```

**Benefit:** ModernSpawner can return `ModernSpawnerEntry` instead.

#### Enhancement 4: Positioner Interface in Base

```csharp
// In Server/Engines/Spawners/ISpawnPositioner.cs (NEW)
public interface ISpawnPositioner
{
    bool TryGetSpawnPosition(BaseSpawner spawner, ISpawnable spawned, Map map, out Point3D location);
}

// In BaseSpawner.cs
public ISpawnPositioner? Positioner { get; set; }

public override Point3D GetSpawnPosition(ISpawnable spawned, Map map)
{
    if (Positioner?.TryGetSpawnPosition(this, spawned, map, out var loc) == true)
        return loc;

    return GetDefaultSpawnPosition(spawned, map);
}

protected abstract Point3D GetDefaultSpawnPosition(ISpawnable spawned, Map map);
```

**Benefit:** Standard Spawner gains positioner support; RegionSpawner becomes a positioner rather than subclass.

### 11.7 What Stays in ModernUO vs ModernSpawner Project

| Component | Location | Rationale |
|-----------|----------|-----------|
| `ISpawnPositioner` | ModernUO Server | Core interface, benefits all spawners |
| `ITrigger` | ModernSpawner | Advanced feature, not needed for basic spawners |
| `BaseSpawner` hooks | ModernUO UOContent | Minimal changes, backward compatible |
| Scripting engine | ModernSpawner | Complex, specific to advanced spawning |
| Standard positioners | ModernUO UOContent | RandomPositioner, RegionPositioner useful for all |
| Advanced positioners | ModernSpawner | TileFilter, Perimeter, etc. are advanced |
| Migration tools | ModernSpawner | XmlSpawner-specific |

### 11.8 Compatibility Matrix

| Spawner Type | Basic | Properties | Scripts | Triggers | Positioners |
|--------------|-------|------------|---------|----------|-------------|
| Spawner | Yes | String-based | No | No | Random only |
| ProximitySpawner | Yes | String-based | No | Proximity | Random only |
| RegionSpawner | Yes | String-based | No | No | Region-weighted |
| **ModernSpawner** | Yes | **Compiled** | **Yes** | **All types** | **Pluggable** |

### 11.9 Migration Path for Existing Spawners

Existing ModernUO `Spawner` instances can be **upgraded** to `ModernSpawner`:

```csharp
[Usage("UpgradeSpawner")]
public static void UpgradeSpawner_OnCommand(CommandEventArgs e)
{
    // Target a Spawner, convert to ModernSpawner preserving all settings
    // Properties string -> Compiled script
    // Location, entries, timing all preserved
}
```

### 11.10 Revised Architecture Overview

```
ModernSpawner/
├── Core/
│   ├── ModernSpawner.cs              # Extends Spawner
│   ├── ModernSpawnerEntry.cs         # Extends SpawnerEntry
│   └── TriggerContext.cs             # Trigger state
│
├── Triggers/
│   ├── ITrigger.cs                   # Trigger interface
│   ├── TriggerRegistry.cs            # Discovery/lookup
│   ├── ProximityTrigger.cs           # (enhanced from ProximitySpawner)
│   ├── SpeechTrigger.cs
│   ├── SkillTrigger.cs
│   ├── TimeOfDayTrigger.cs
│   └── PropertyTrigger.cs
│
├── Scripting/
│   ├── ScriptCompiler.cs             # Parse + compile
│   ├── CompiledScript.cs             # Executable form
│   ├── ExecutionContext.cs           # Runtime state
│   └── Nodes/                        # AST nodes
│
├── Positioners/                      # Advanced positioners only
│   ├── TileFilterPositioner.cs
│   ├── PerimeterPositioner.cs
│   ├── RelativePositioner.cs
│   └── CompositePositioner.cs
│
├── Migration/
│   ├── XmlSpawnerImporter.cs
│   └── SyntaxConverter.cs
│
└── Gumps/
    └── ModernSpawnerGump.cs          # Enhanced editor
```

**ModernUO Changes (Optional Enhancements):**
```
Server/
└── Interfaces.cs                     # Add ISpawnPositioner

UOContent/Engines/Spawners/
├── BaseSpawner.cs                    # Add OnAfterEntitySpawn hook
├── ISpawnPositioner.cs               # NEW: Interface
├── RandomPositioner.cs               # Extract from Spawner
└── RegionPositioner.cs               # Refactor from RegionSpawner
```

---

## Appendix A: Performance Targets (BENCHMARK VALIDATED)

### Property Access Performance (12k spawners, 2 properties each)

| Approach | Measured | Memory/Cycle |
|----------|----------|--------------|
| Direct C# | 47 μs | 0 B |
| **Expression<T> typed (target)** | **50 μs** | **0 B** |
| Reflection (XmlSpawner-style) | 893 μs | 2.11 MB |

### Compilation Cost (One-Time)

| Approach | Compile Time | Memory |
|----------|-------------|--------|
| Expression<T>.Compile() | 196 μs | 10.4 KB |
| IL Emit | 96 μs | 1.9 KB |

*Note: Compilation is one-time per property path. For 12k spawners, total startup overhead is ~2.4 seconds.*

### Projected Performance

| Metric | XmlSpawner (est.) | ModernSpawner Target |
|--------|-------------------|---------------------|
| Spawn cycle (12k spawners) | ~893 μs | < 60 μs |
| Memory per cycle | 2.11 MB | 0 B |
| GC pressure | High (134 Gen0/cycle) | None |
| Improvement | Baseline | **~18x faster** |

*See `PerformanceAnalysis.md` for full benchmark methodology and results.*

---

## Appendix B: Glossary

- **AST**: Abstract Syntax Tree - structured representation of script
- **Positioner**: Component that determines spawn location
- **Trigger**: Component that activates spawner
- **SpawnEntry**: Definition of what to spawn and how many
- **CompiledScript**: Executable form of spawn script
- **ExecutionContext**: Runtime state during script execution

---

## Appendix C: Open Questions

1. **Should we support the COMMAND keyword?**
   - Security concern - arbitrary server commands
   - Recommendation: Omit initially, add with strict access control later

2. **How to handle XmlSpawner's SmartSpawning?**
   - Was removed from XmlSpawner in previous work
   - Recommendation: Don't implement, use Region-based instead

3. **Should scripts support loops?**
   - XmlSpawner doesn't have loops
   - Recommendation: No loops initially, prevents infinite loops

4. **Multi-map spawner support?**
   - XmlSpawner supports spawning to different maps
   - Recommendation: Support via AbsolutePositioner with map parameter
