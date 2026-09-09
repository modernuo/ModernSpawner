# ModernSpawner — Architecture

Status: draft v1, 2026-09-08. Describes the code as it is (§2–3) and the target design the product spec
assumes (§4–9). Line references are to `8935ca4` and ModernUO `79e3a8e34`. Decisions are labelled with the
IDs from `product-spec.md` §10.

## 1. System context

```
ModernUO (submodule, support branch)                      ModernSpawner (this repo)
┌──────────────────────────────────────────┐              ┌──────────────────────────────────────────┐
│ Server: Item, Mobile, Map, Timer, World, │◄─references──│ Projects/ModernSpawner                   │
│   AssemblyHandler, EventSink, Json       │              │   Core        spawner, entry, commands   │
│ UOContent: BaseSpawner, SpawnerEntry,    │              │   Triggers    7 triggers + TriggerSystem │
│   SpawnerDto, EventScheduler, Clock,     │              │   Scripting   expressions, DSL, registry │
│   CreatureEvents, gumps, [ExportSpawners │              │   Positioning rules + registry           │
└──────────────────────────────────────────┘              │   Loot        templates + registry       │
        ▲ Configure/Initialize discovery,                 │   Serialization DTO, JSON, YAML, XML     │
        │ [JsonDiscoverableType], CommandSystem           │   Gumps       8 DynamicGumps             │
        └─────────────────────────────────────────────────│   Perf        metrics + commands         │
                                                          └──────────────────────────────────────────┘
```

ModernSpawner is a single assembly loaded by ModernUO. It participates in the engine through five seams:

| Seam | Mechanism | Used by |
|---|---|---|
| Item lifecycle | `ModernSpawner : BaseSpawner : Item` (serialization generator, timers, OnDelete, dupe) | Core |
| Bootstrap | `AssemblyHandler.Invoke("Configure"/"Initialize")` finds static `Configure()`/`Initialize()` | `ModernSpawnerConfiguration`, commands, `ScriptRegistry`, perf |
| Events | `Item.HandlesOnMovement/OnMovement`, `HandlesOnSpeech/OnSpeech`, `Timer`, `EventScheduler`, `CreatureEvents` | Triggers |
| Persistence | Generator-attributed fields; `GenericPersistence` for the script registry; `SpawnerDto` polymorphic JSON via `[JsonDiscoverableType]` | Core, Scripting, Serialization |
| Commands and UI | `CommandSystem.Register`, `DynamicGump` | Commands, Gumps |

Everything runs on the game loop. There are no threads, locks or `Task.Run` anywhere in the assembly.

## 2. Subsystems as built

### 2.1 Core (`Core/`)

`ModernSpawner` (1,260 lines) derives from `BaseSpawner` and adds 18 serialized fields: a
`List<ModernSpawnerEntry>`, four script serials, positioning flags, trigger definitions and flags, spawn
area, notes, and cycle-mode state. `ModernSpawnerEntry` (270 lines) is a standalone generator class with 17
fields: the six `SpawnerEntry` equivalents plus scripts, delays, positioning rule, group, LOS, area offset,
range, loot template and subgroup.

**The dual-list problem.** `BaseSpawner` owns `List<SpawnerEntry> _entries` and `Dictionary<ISpawnable,
SpawnerEntry> Spawned`, and its `AddEntry`, `Start`, `Defrag`, `Remove`, `CountSpawns`, `RemoveEntry`,
`RemoveSpawn(s)`, `Respawn`, `Reset`, `NextSpawn`, `GetProperties`, `OnAfterDuped`, `AfterDeserialization`,
`ToDto`/`ApplyDto`, the stock gumps and `[EditSpawner` all operate on that list. `ModernSpawner` overrides
only `Spawn()`, `GetSpawnPosition`, `GetSpawnerProperties`, `OnDelete`, `OnDoubleClick`, hides `AddEntry`/
`Start`/`Stop` with `new`, and keeps its own `_spawnEntries` and `_modernSpawned`. The base list is empty
for any spawner built through the modern API, so every base member above is a no-op or acts on stale
state. Consequences are itemised in `docs/audit/core.md` §3 and summarised in `docs/feature-audit.md` §3
(#1–#3, #11, #24).

Spawn flow today: `OnTick` → `Spawn()` (override) → select entry by cycle mode → build a throw-away
`SpawnerEntry` → `base.Spawn(tempEntry)` (creates, positions, places) → copy entity into the modern entry
and `_modernSpawned` → apply loot and entry script. Positioning runs inside `base.Spawn`, before the modern
entry is known.

### 2.2 Triggers (`Triggers/`)

`TriggerSystem` is a singleton registry keyed by spawner with per-type lists. Triggers are parsed from
`type:field:field` strings stored on the spawner (`_triggerDefinitions`). Activation happens in
`ModernSpawner.Start()` (the `new` one) and in the synchronous `[AfterDeserialization]`; deactivation in
`Stop()`/`OnDelete()`. Wiring:

| Trigger | Source event | Wired |
|---|---|---|
| proximity | `Item.OnMovement` (24-tile radius, engine-fixed) | yes |
| speech | `Item.OnSpeech` (15/18-tile radius) | yes |
| kill | `ModernSpawner.OnSpawnedEntityKilled` | no caller |
| skill | `ModernSpawnerEvents.OnSkillUsed` | no caller |
| timeofday | 2.5 s polling timer | yes |
| game_time_window | one transition timer | yes (wrong clock constant) |
| wall_time_window | `EventScheduler` + `BaseScheduledEvent` subclass | yes (close-edge filter bug) |

`Trigger()` sets `_triggered` and forces a `Spawn()`; nothing reads `_triggered` to gate the timer.

### 2.3 Scripting (`Scripting/`)

Two engines share `PropertyAccessorCache` (compiled `Expression<T>` getters/setters keyed by `(Type, path)`,
zero-alloc on hit) but nothing else:

- **Command DSL** (`ScriptEngine`, `Ast/`): `KEYWORD/arg/arg`, statements split on `;`/newline, 12
  keywords, interpreted AST. This is what the six hooks execute.
- **Expression language** (`Expressions/`): infix grammar with `and/or/not`, comparisons, arithmetic,
  `if…then…else` values, dotted property paths over `trigMob/target/spawner/entry`, 20 built-in functions,
  typed non-boxing evaluation. Complete and tested; **no production caller**.

Spawner-level scripts are stored in `ScriptRegistry` (a `GenericPersistence` blob) and referenced by
`Serial`; entry-level scripts are stored as source on the entry and compiled through a source-keyed cache.

### 2.4 Positioning (`Positioning/`)

`PositioningRules` is a name→`IPositioningRule` registry with 14 rules. `ModernSpawner.GetSpawnPosition`
replaces the base implementation entirely (losing `SpawnPositionMode`, sector cache, spiral scan, house
blocking, multi-Z search) with: entry rule → entry offset → spawn area random → "smart" random → random.
Because `HomeRange` writes into the same field as `SpawnArea`, the area branch is the normal path.

### 2.5 Loot (`Loot/`)

`LootTemplate` (guaranteed items, weighted tables, gold, clear flag) and a static in-memory
`LootTemplateRegistry` with JSON file load/save that nothing calls. Applied in `SpawnFromEntry` after the
entity exists.

### 2.6 Serialization (`Serialization/`, `Core/ModernSpawner.Dto.cs`, `Migration/`)

Five formats:

| Format | Writer/Reader | Completeness |
|---|---|---|
| Binary world save | generator | complete, untested |
| ModernUO `SpawnerDto` JSON | `ModernSpawner.Dto.cs` | base fields + scripts/options; **no modern entries or triggers** |
| Own JSON `modernspawner/v1/spawner.json` | `SpawnerJsonExporter/Importer` | drops 8 entry fields, cooldowns; property syntax unusable (V-1) |
| YAML `modernspawner/v1/script.yaml` | `ScriptYamlSerializer` | script→actions is a stub |
| XmlSpawner `.xml` | `XmlSpawnerImporter` (real layout, wrong columns), `XmlSpawnerMigrator` (imaginary layouts) | partial / dead |

### 2.7 Gumps and perf (`Gumps/`, `Perf/`)

Eight `DynamicGump`s on the current API. Four overflow their fixed heights; the trigger gump writes
unregistered keys; script fields are single-line with a server-enforced 239-char cap. `SpawnerMetrics` is
an opt-in, zero-alloc counter set with seven `[ModernSpawnerPerf*` commands.

## 3. Cross-cutting facts

- **Assembly boundary.** ModernSpawner subclasses `BaseSpawner` from another assembly. Anything the base
  keeps `private`/`private protected`/non-virtual is unreachable. The first support-branch change
  (`79e3a8e34`) widened the DTO helpers for exactly this reason.
- **Serialization generator** must be referenced directly (`PrivateAssets="all"` upstream). Version bumps
  need `Migrations/*.vN.json` produced by `ModernUOSchemaGenerator`; the repo has none yet.
- **Bootstrap order.** `EventScheduler.Configure` runs before world load, so scheduling during
  deserialization is safe; `TriggerSystem` and `ScriptRegistry` instances are created in `Configure()`.
- **Hot paths.** `OnTick`→`Spawn()`, `OnMovement` (every step of every mobile within 24 tiles of a spawner
  with proximity triggers), `OnSpeech`, and script execution per spawn. Today each allocates (closure,
  temp entry, `TriggerContext`, `Split`/`ToLower`).

## 4. Target: entry ownership (D1)

### 4.1 Options considered

| Option | ModernUO change | ModernSpawner change | Result |
|---|---|---|---|
| **A. Abstract entry ownership** (archived plan Phase 1) | `BaseSpawner` works over `IReadOnlyList<ISpawnerEntry>` provided by the subclass; `CreateEntry` factory; `Spawner`/`Proximity`/`Region` own `List<SpawnerEntry>` (v13 migration moves `_entries` down); gumps/DTO/commands use `ISpawnerEntry` | Own `List<ModernSpawnerEntry>` becomes *the* list; `ModernSpawnerEntry : ISpawnerEntry`; delete the parallel `_modernSpawned`, temp-entry trick, `new` hides | Every base member works; stock gumps, `[SpawnAdmin`, DTO see modern entries; positioning knows the entry |
| B. Subclass entry | `AddEntry` becomes virtual/`CreateEntry`; base `_entries` deserialization must construct the subclass (generator does not support polymorphic lists) → still needs a base change to let the subclass own serialization of the list | `ModernSpawnerEntry : SpawnerEntry` | Same benefits as A once the list-serialization problem is solved, which is most of A anyway |
| C. Virtualise everything | ~12 members virtual (`Start/Stop/Defrag/Remove/RemoveSpawns/CountSpawns/RemoveEntry/RemoveSpawn/IsFull/NextSpawn/GetProperties`) | Override all of them, keep two lists | Base gumps/DTO/`[EditSpawner` still blind; every new base feature needs another override |

**Recommendation: A.** The user owns both repos and the support branch exists for this. B collapses into A;
C is a treadmill.

### 4.2 Shape of A (ModernUO side)

```csharp
public interface ISpawnerEntry
{
    string SpawnedName { get; set; }
    int SpawnedProbability { get; set; }
    int SpawnedMaxCount { get; set; }
    string Properties { get; set; }
    string Parameters { get; set; }
    List<ISpawnable> Spawned { get; }
    EntryFlags Valid { get; set; }
    bool IsFull => Spawned.Count >= SpawnedMaxCount;
    void Defrag(BaseSpawner parent);
    void AddToSpawned(ISpawnable s); void RemoveFromSpawned(ISpawnable s);
}

public abstract partial class BaseSpawner
{
    public abstract IReadOnlyList<ISpawnerEntry> Entries { get; }        // subclass-owned, serialized there
    protected abstract ISpawnerEntry CreateEntry(string name, int prob, int max, string props, string args);
    protected abstract void AddToEntries(ISpawnerEntry e);
    protected abstract bool RemoveFromEntries(ISpawnerEntry e);
    public Dictionary<ISpawnable, ISpawnerEntry> Spawned { get; }         // unchanged shape, interface-typed
    // AddEntry/RemoveEntry/Defrag/Remove/RemoveSpawns/Start/CountSpawns unchanged logic, interface-typed
    protected virtual void OnSpawned(ISpawnerEntry entry, ISpawnable spawned) { }   // after placement
    protected virtual bool OnBeforeSpawn(ISpawnerEntry entry) => true;             // veto
    public virtual Point3D GetSpawnPosition(ISpawnerEntry entry, ISpawnable spawned, Map map)
        => GetSpawnPosition(spawned, map);                                          // entry-aware overload
}
```

Constraints the generator imposes (verified in the Codex review against `ModernUO.Serialization.Generator`
4.1.0): a serialized list is constructed from its *declared* element type with no discriminator, so the
serialized field must be a **concrete** list per ownership branch and the abstract `Entries` is an
unannotated interface view over it. `Spawner` owns `[SerializableField] List<SpawnerEntry> _entries` and
`ProximitySpawner`/`RegionSpawner` inherit it (they already derive from `Spawner`); `ModernSpawner` owns
`List<ModernSpawnerEntry>`.

Save migration is more than "v13 hands the list down": generated deserialization runs `base.Deserialize`
before the derived version is read, older `MigrateFrom(V10/V11)` handlers and the pre-generator reader assign
`_entries` directly, and `BaseSpawner.AfterDeserialization` rebuilds `Spawned` and arms the timer before
derived data exists. The sequence must be: base keeps a transient legacy-entry carrier populated by every
old reader; the concrete owner adopts it exactly once in its own deserialization; registry reconstruction and
timer start move to a deferred hook that runs after the concrete list is loaded. Old readers and their
encoding stay untouched; every stock subclass is tested against saves from v10, v11, v12 and the new format.

Mutation and copy operations become explicit on the base (`ClearEntries`, `ReplaceEntries`, `CopyEntriesTo`
with dirty tracking); `OnAfterDuped` and `SpawnerControllerGump.CopyEntry` use them so modern entry fields
survive duplication and controller copies. `SpawnerEntry : ISpawnerEntry`.

DTO: keep the stock JSON shape (root `$type`, undiscriminated `entries`) byte-for-byte. Each root DTO subtype
owns a concrete entry-DTO collection (`SpawnerDataDto.Entries : List<SpawnerEntryDto>`,
`ModernSpawnerDto.Entries : List<ModernSpawnerEntryDto>`) and converts through `CreateEntry`; `ApplyDto` no
longer touches entries itself. `SpawnerGump`, `SpawnerControllerGump`, `EditSpawnerCommand` read
`ISpawnerEntry`. Estimated size: ~30 files in UOContent, one save migration with a transient carrier.

A pre-placement hook is also needed so computed properties (D6) apply before positioning:
`protected virtual void OnConfigureSpawned(ISpawnerEntry entry, ISpawnable spawned)` called after
construction and property application, before `GetSpawnPosition`.

### 4.3 Shape of A (ModernSpawner side)

- `ModernSpawnerEntry : ISpawnerEntry` (keeps its own generator class; the six base fields stay).
- `ModernSpawner.Entries => _spawnEntries`; `CreateEntry` returns a `ModernSpawnerEntry`; delete
  `_modernSpawned`, `AddModernEntry`, `RemoveModernEntry`, `ModernEntries`, `ModernSpawned`, the temp-entry
  path, and the `new` `AddEntry/Start/Stop`.
- `Spawn()` override keeps cycle-mode selection and calls `base.Spawn(entry, out flags)`.
- `OnBeforeSpawn(entry)` runs the entry condition and before-spawn script (veto); `OnSpawned(entry, spawned)`
  applies loot and the entry spawn script.
- `GetSpawnPosition(entry, spawned, map)` applies the entry rule, else `base`.
- Start/Stop hooks: `BaseSpawner.Start/Stop` become `protected virtual OnStarted/OnStopped` callbacks (tiny
  upstream change) so `Running = …` reaches trigger activation and the activate/deactivate scripts.

## 5. Target: triggers (D2, D3)

- **Semantics.** Two classes: *event* triggers (proximity, speech, kill, skill, external) and *gate*
  triggers (game/wall time windows). A single `_triggered` bool cannot represent this (overlapping windows,
  an event reopening a closed window, fullness closing a still-open window). Target state is a small
  state machine, to be written as a transition table and approved before Phase 2: `GateSet` (which gate
  triggers are currently open; the gate is open when the set is non-empty or there are no gate triggers),
  `PendingCycles` (event requests, bounded), and the timer. Timer ticks spawn only when the gate is open
  and (`!TriggerActivated` or `PendingCycles > 0`); an event trigger enqueues one cycle (D2's "one cycle")
  and does not latch. Manual `Spawn()`, `Respawn()`, indexed spawn and inter-spawner `spawn()` bypass the
  gate explicitly and say so. Persisted: gate-trigger definitions, `PendingCycles`, cooldown deadlines and
  kill counters as absolute timestamps/ints; never timer tokens. Recomputed on load: window open/closed
  state (including overnight, day/month filters, time zone and DST).
- **Liveness.** Three independent things: administrative enablement (`Running`, controls the timer),
  trigger registration (whenever `TriggerActivated` and not deleted, independent of `Running`), and the gate.
  A stopped spawner keeps its triggers registered; an event on a stopped spawner enqueues a cycle but does not
  start the timer unless the trigger definition says `wake:true`. Registration moves to the deferred
  `[AfterDeserialization(false)]` hook.
- **Kill.** `CreatureEvents.CreatureDeathEvent` fires *after* `Mobile.OnDeath`, which deletes non-player
  mobiles and clears `Spawner` on the way (`Mobile.cs:4647,4899`), so `bc.Spawner` is null by then. The
  support branch adds `protected virtual void OnSpawnedDeath(ISpawnerEntry entry, ISpawnable spawned, Mobile killer)`
  on `BaseSpawner`, invoked from `BaseCreature.OnDeath` before base death while the link is intact. Death is
  distinct from removal (taming, pickup, delete). `RequireAllDead` is evaluated after removal against the
  entry's remaining live count.
- **Skill.** Support-branch change: `SkillCheck` raises a generated `SkillEvents.SkillUsedEvent(Mobile,
  SkillName, double value, bool success)`; ModernSpawner subscribes. Until merged, `skill:` definitions are
  rejected at parse time with a visible error (never accepted as inert).
- **Grammar.** One definition grammar owned by each trigger's `Serialize()`. Gumps and importers construct
  trigger objects. `TriggerContext` becomes a `readonly record struct`.
- **Extended proximity.** Clamp to `Core.GlobalMaxUpdateRange` with a warning; the sector-range
  subscription API stays a tracked ModernUO prerequisite.
- **Clock.** `RealTimePerGameHour = TimeSpan.FromSeconds(Clock.SecondsPerUOMinute * 60)`; recompute on map
  change. `timeofday` retired.

## 6. Target: one script language (D4)

Statements on top of the existing expression lexer/parser:

```
script      := statement*                      (';' or newline separated)
statement   := assignment | call | conditional
conditional := 'if' expr 'then' block ('elseif' expr 'then' block)* ('else' block)? 'end'
block       := statement*                      // 'end' is mandatory; no dangling-else ambiguity
assignment  := path '=' expr                   // target.Hue = 0x8000 ; spawner.Notes = "x" ; vars.n = 3
call        := IDENT '(' args ')'              // cancel(), spawn("Guards", 2), broadcast("…", 20, 0x22)
```

Execution contract (must be specified before Phase 3): which context objects each hook provides and
which are writable; `vars` lifetime (per execution; persisted variables are a separate, explicit
`spawner.state.x` namespace); null targets abort the statement with an error, not an exception;
property writes are checked against `[CommandProperty]` access levels, not CLR writability; entry
conditions and movement-trigger predicates are compiled in a *pure* mode that rejects action functions;
accessors and function dispatch are bound at compile time (no `Split`/`ToLower` on the hot path);
budgets: nesting depth 10, per-execution work budget, bounded deferred action queue with causal depth
carried across `Timer.DelayCall`; mutation-safe iteration and registration before callbacks.

- Actions become functions in a second `BuiltInFunctions` table (side-effecting): `cancel, spawn, despawn,
  goto (subgroup), teleport (today's GOTO), activate, deactivate, broadcast, msg, sound, effect, give,
  setOn(obj, "path", value)`. `COMMAND` is excluded on purpose.
- Contexts: `spawner, entry, target/spawned, trigMob`, plus `vars` for `$name`.
- Compile once per source; `CompiledScript.Errors` returned to callers; runtime exceptions logged through
  `LogFactory` and rate-limited to online GMs.
- Re-entrancy: depth counter on `ScriptEngine`, limit 10.
- Entry `condition` (expression) evaluated in `OnBeforeSpawn`.
- Coercion: `ConvertValue` gains hex ints, `TimeSpan`, `Point3D`, enums, `Serial`→entity, and returns a
  typed failure rather than `null`.
- The slash DSL is retired; the XmlSpawner importer emits the new syntax.

## 7. Target: persistence and export (D5, D6)

- **One export format**: ModernUO `SpawnerDto`. `ModernSpawnerDto` gains `List<ModernSpawnerEntryDto>`
  (all entry fields), `triggers: List<string>`, `cycleMode`, `currentSubgroup`, `sequentialResetTime/To`,
  `holdSequence`. `WhenWritingDefault` defaults handled with `ShouldSerialize` modifiers like upstream
  `homeRange`. Own JSON and YAML removed with their gumps' import/export switched to the DTO path.
- **Entry `Properties`** use ModernUO's `Name Value` syntax everywhere. `PropertyBuilderGump` emits it;
  `{min-max}` ranges become `target.Str = random(80, 120)` in the entry spawn script.
- **Script registry**: remove-on-delete; entry scripts cached by source hash.
- **Migrations**: `Projects/ModernSpawner/Migrations/` with v0 schemas; `.config/dotnet-tools.json` in this
  repo pointing at `ModernUOSchemaGenerator`; CI asserts schemas are current.

## 8. Target: positioning and loot

- `GetSpawnPosition(entry, spawned, map)`: if `entry.PositioningRule` is set, run the parsed rule (cached
  `ParsedRule` on the entry) with a `PositioningContext` carrying the triggering mobile; otherwise
  `base.GetSpawnPosition`. Spiral scanning is a `Spawner` capability (`SupportsSpiralScan`), not a
  `BaseSpawner` one; `ModernSpawner` opts in explicitly (or derives from `Spawner`, to be decided with D1).
  Remove `UseSmartPositioning`, `MaxZDelta`, `DefaultPositioner`, `_spawnArea` (use `SpawnBounds`), and the
  two duplicate `IsValidWater` copies.
- Rules validate their parameters at parse time; row/column indices live on the spawner.
- Loot templates load from `Distribution/Data/ModernSpawner/Loot/` in `Configure()` (files are small;
  synchronous is acceptable at startup) and via `[LootTemplates reload` (off-loop parse, on-loop swap).

## 9. Target: staff UI

- Gumps compute height from content and paginate unbounded lists.
- `TriggerConfigGump` builds trigger objects; supports all types with typed fields.
- Script fields become read-only previews with compile status; editing goes through the D7 path.
- `[ModernSpawner` command (open by target, create at location, list nearby) and a context menu entry.
- Condition builder removed.

## 10. Testing architecture

- A `SpawnerTestFixture` boots a ModernUO test server and places a `ModernSpawner` on a **non-Internal**
  test map (`BaseSpawner.Spawn` refuses `Map.Internal`, `BaseSpawner.cs:997`). ModernUO's
  `TestServerInitializer` is `internal` and loads only `Server`/`UOContent`, so the fixture either gets an
  `InternalsVisibleTo` + assembly-list parameter on the support branch or a copy of the initializer here that
  also registers the ModernSpawner assembly and runs its `Configure`. It exposes `Tick()` to advance timers.
- Every subsystem gets an end-to-end test that goes through the fixture: spawn/kill/respawn, stop/start,
  trigger fire and gate, entry rule placement, loot application, script hooks, DTO round trip, binary save
  round trip (`Serialize` to a buffer and `Deserialize` back).
- Parser tests remain; add "producer→parser" tests for every gump/importer-generated string.
- Benchmarks stay in `ModernSpawner.Benchmarks`; add a tick-loop allocation benchmark.

## 11. ModernUO prerequisites created by this design

Tracked in `modernuo-prerequisites.md`: DTO helper visibility (done), abstract entry ownership (§4.2),
`OnStarted/OnStopped` and `OnConfigureSpawned` virtuals, `OnSpawnedDeath` hook, `SkillUsedEvent`, test
initializer access, GUID-based replacement in `[ImportSpawners` (today it deletes co-located same-type
spawners and calls `Respawn()` unconditionally, `ImportSpawnersCommand.cs:259`), sector-range movement
subscription (deferred).

## 12. Review history

- 2026-09-08 Codex (gpt-6-astra) review of the audit and these specs: `docs/reviews/2026-09-08-codex-spec-review.md`
  (uncommitted). Its verified corrections are folded into §4–§10 and into `xmlspawner-migration.md`.
