# ModernSpawner — Architecture

Status: draft v1, 2026-09-08. Describes the code as it is (§2–3) and the target design the product spec
assumes (§4–9). Line references are to `8935ca4` and ModernUO `79e3a8e34`. Decisions are labelled with the
IDs from `product-spec.md` §10.

## 1. System context

```
ModernUO (submodule, main + open prerequisite PRs)        ModernSpawner (this repo)
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

`ModernSpawner` derives from `Spawner` and adds 17 serialized fields: `_spawnEntries`
(`List<ModernSpawnerEntry>`, field 0), four script serials (1–4), three positioning flags (5–7), trigger
definitions and flags (8–10), notes (11), and five fields of cycle-mode state (12–16). `ModernSpawnerEntry`
derives from `SpawnerEntry` and adds only its 11 extra fields (0–10): scripts, delays, positioning rule,
group, LOS, area offset, range, loot template and subgroup — the six `SpawnerEntry` fields and the
`Disabled` flag come from the base class.

**Entry ownership.** `ModernSpawner` owns `_spawnEntries` and implements the base contract over it:
`Entries` and `EntrySpan` (via `ReadOnlySpan<SpawnerEntry>.CastUp`) expose it to `Spawner`/`BaseSpawner`,
and `CreateEntry`, `AddEntryCore`, `RemoveEntryCore`, `ClearEntriesCore`, `AdoptEntries` (converting a
foreign entry with `CloneEntry` and carrying its live spawns over with `TransferSpawned`) and `CloneEntry`
(copying the 11 modern fields) let every base spawn path — `Spawn`, `Defrag`, `Remove`,
`RemoveAllEntries`, dupe, DTO and binary round trips — run over `ModernSpawnerEntry` with no parallel
list. Typed conveniences (`ModernEntries`, `AddModernEntry`) remain for callers that want
`ModernSpawnerEntry` directly instead of the base `SpawnerEntry` view. This replaced an earlier
"dual-list" design where the spawner kept its own `_spawnEntries` alongside an always-empty base
`_entries`; that design, and the bugs it caused, is history — see `docs/audit/core.md` §3.

Spawn flow as ported: the timer calls `OnTick` → `Spawn()`, which runs the before-spawn script veto,
defrags, then selects an entry by cycle mode (`SpawnWeightedOne` for Random/Sequential, `SpawnGroupMode`
for Group) over `_spawnEntries` with plain `for` loops and calls the base `Spawn(entry, out flags)` for the
chosen entry. That base call positions the entity through the entry-aware `GetSpawnPosition(entry, spawned,
map)` override (entry `PositioningRule`, else the entry's `SpawnAreaOffset`, else the spawner's own
positioning) and, once placed, calls `OnSpawned(entry, spawned)` to apply the entry's loot template and
`OnSpawnScript`. On death, `OnSpawnedDeath(entry, spawned, killer)` notifies `TriggerSystem` for kill
triggers and runs the entry's `OnDespawnScript`.

### 2.2 Triggers (`Triggers/`)

`TriggerSystem` is a singleton registry keyed by spawner with per-type lists. Triggers are parsed from
`type:field:field` strings stored on the spawner (`_triggerDefinitions`). Registration goes through one
guarded helper, `ModernSpawner.EnsureTriggersActive()`, the only caller of
`TriggerSystem.ActivateTriggers` outside the trigger system, within the engine project: it deactivates
first and re-registers only when the spawner is running, is `TriggerActivated` and actually has
definitions, which makes it idempotent. `ActivateTriggers` on its own is not: it *replaces*
`_allTriggers[spawner]` with the batch it just parsed while the per-type lists it feeds
(`_proximityTriggers`, `_speechTriggers`, …) *append*, so calling it twice duplicates dispatch and orphans
the first batch — those triggers are no longer reachable for `Deactivate()`. `OnStarted` and
`[AfterDeserialization]` call it, and so does every construction path that hands back an already-running
spawner — `OnAfterDuped`, `ModernSpawnerDto.ToSpawner`, both JSON importer entry points,
`XmlSpawnerImporter` and `XmlSpawnerMigrator` — because `BaseSpawner.Start()` only reaches `OnStarted`
when `Running` actually flips. The same helper is the mandatory follow-up for every other list or flag change: the
`TriggerActivated` setter calls it, and so do `TriggerConfigGump`'s add/remove handlers and the JSON
importer's clear path. Those list mutations go only through the generated
`AddToTriggerDefinitions`/`RemoveFromTriggerDefinitionsAt`/`ClearTriggerDefinitions` helpers, so the change
is tracked for serialization before triggers are re-registered. Deactivation is unconditional in
`OnStopped` (reached by `Stop()` and, through `BaseSpawner.OnDelete`, by deletion) and in `OnDelete`, and
`XmlSpawnerMigrator` honours an explicit `Running="false"` on both node forms it reads. Wiring:

| Trigger | Source event | Wired |
|---|---|---|
| proximity | `Item.OnMovement` (24-tile radius, engine-fixed) | yes |
| speech | `Item.OnSpeech` (15/18-tile radius) | yes |
| kill | `OnSpawnedDeath` via `BaseSpawner.NotifySpawnedDeath`, called from `BaseCreature.OnDeath` | yes, tested |
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
blocking, multi-Z search) with: entry rule → entry offset → spawn bounds random → "smart" random → random.
There is no separate `SpawnArea` any more: `Spawner.SpawnBounds` is the one bounds, and `HomeRange` is a
computed view over it (its setter rewrites `SpawnBounds`), so the bounds branch is the normal path.

### 2.5 Loot (`Loot/`)

`LootTemplate` (guaranteed items, weighted tables, gold, clear flag) and a static in-memory
`LootTemplateRegistry` with JSON file load/save that nothing calls. Applied in the `OnSpawned(entry,
spawned)` hook — together with the entry's `OnSpawnScript` — after the base spawn path has placed the
entity.

### 2.6 Serialization (`Serialization/`, `Core/ModernSpawner.Dto.cs`, `Migration/`)

Five formats:

| Format | Writer/Reader | Completeness |
|---|---|---|
| Binary world save | generator | complete; round trip tested (`Binary_RoundTrip_RebuildsSpawnedOverModernEntries`) |
| ModernUO `SpawnerDto` JSON | `ModernSpawner.Dto.cs` | complete: base fields, `List<ModernSpawnerEntry>` entries, scripts, options, trigger definitions and cycle state; round trip tested, and `ToSpawner` registers the imported triggers |
| Own JSON `modernspawner/v1/spawner.json` | `SpawnerJsonExporter/Importer` | drops 8 entry fields, cooldowns; property syntax unusable (V-1) |
| YAML `modernspawner/v1/script.yaml` | `ScriptYamlSerializer` | script→actions is a stub |
| XmlSpawner `.xml` | `XmlSpawnerImporter` (real layout, wrong columns), `XmlSpawnerMigrator` (imaginary layouts) | partial / dead |

### 2.7 Gumps and perf (`Gumps/`, `Perf/`)

Eight `DynamicGump`s on the current API. Four overflow their fixed heights; the trigger gump writes
unregistered keys; script fields are single-line with a server-enforced 239-char cap. `SpawnerMetrics` is
an opt-in, zero-alloc counter set with seven `[ModernSpawnerPerf*` commands.

## 3. Cross-cutting facts

- **Assembly boundary.** ModernSpawner subclasses `BaseSpawner` from another assembly. Anything the base
  keeps `private`/`private protected`/non-virtual is unreachable. ModernUO PR #2619 widened the DTO
  helpers for exactly this reason.
- **Serialization generator** must be referenced directly (`PrivateAssets="all"` upstream). Version bumps
  need `Migrations/*.vN.json` produced by `ModernUOSchemaGenerator`; the repo has none yet.
- **Bootstrap order.** `EventScheduler.Configure` runs before world load, so scheduling during
  deserialization is safe; `TriggerSystem` and `ScriptRegistry` instances are created in `Configure()`.
- **Hot paths.** `OnTick`→`Spawn()`, `OnMovement` (every step of every mobile within 24 tiles of a spawner
  with proximity triggers), `OnSpeech`, and script execution per spawn. The closure and temp-entry
  allocations the dual-list design forced on the spawn path are gone: entry selection is plain `for` loops
  over `_spawnEntries` and the base `Spawn(entry, out flags)` is handed the real entry. What still
  allocates per event includes a `TriggerContext` (a class) on every proximity/speech/kill dispatch and a
  `ScriptContext` per script execution. Trigger definition strings are `Split` only in `Parse`, once at
  activation, never per event.

## 4. Target: entry ownership (D1)

### 4.1 Options considered

| Option | ModernUO change | ModernSpawner change | Result |
|---|---|---|---|
| **A. Abstract entry ownership** (archived plan Phase 1) | `BaseSpawner` works over an `IReadOnlyList` of a shared abstract entry type provided by the subclass; `CreateEntry` factory; `Spawner`/`Proximity`/`Region` own `List<SpawnerEntry>` (v13 migration moves `_entries` down); gumps/DTO/commands use that abstract entry type | Own `List<ModernSpawnerEntry>` becomes *the* list; `ModernSpawnerEntry` implements the shared abstract entry type; delete the parallel `_modernSpawned`, temp-entry trick, `new` hides | Every base member works; stock gumps, `[SpawnAdmin`, DTO see modern entries; positioning knows the entry |
| B. Subclass entry | `AddEntry` becomes virtual/`CreateEntry`; base `_entries` deserialization must construct the subclass (generator does not support polymorphic lists) → still needs a base change to let the subclass own serialization of the list | `ModernSpawnerEntry : SpawnerEntry` | Same benefits as A once the list-serialization problem is solved, which is most of A anyway |
| C. Virtualise everything | ~12 members virtual (`Start/Stop/Defrag/Remove/RemoveSpawns/CountSpawns/RemoveEntry/RemoveSpawn/IsFull/NextSpawn/GetProperties`) | Override all of them, keep two lists | Base gumps/DTO/`[EditSpawner` still blind; every new base feature needs another override |

**Decision (D1, D11): A**, implemented as subclass-owned entries over the concrete `SpawnerEntry` base
class — Option B's shape, once B's list-serialization problem was solved, rather than a separate
abstract entry interface; see §4.2 for what actually merged. `ModernSpawner` derives from `Spawner`,
the first concrete owner of the abstract contract; C was rejected as a treadmill. Performance constraint:
entry access goes through a concrete-typed `ReadOnlySpan<SpawnerEntry>` (`EntrySpan`), not `List<T>` or
per-entry interface dispatch, so the once-per-spawn-cycle (minutes apart, not per-tick or per-movement)
entry selection loop gained no measurable cost — measured before merge. The per-entry `Enabled`/`Disabled`
flag (D12) is part of the same upstream change (`SpawnerEntry` v2).

### 4.2 Shape as merged (ModernUO side, PR #2621)

`BaseSpawner.Entries.cs` declares the abstract owner contract, typed on the concrete `SpawnerEntry` base
class throughout — no interface anywhere:

```csharp
public abstract partial class BaseSpawner
{
    public abstract IReadOnlyList<SpawnerEntry> Entries { get; }             // cold, read-only view
    protected abstract ReadOnlySpan<SpawnerEntry> EntrySpan { get; }         // hot loops, zero-alloc
    protected abstract SpawnerEntry CreateEntry(
        string name, int probability, int maxCount, string properties, string parameters);
    protected abstract void AddEntryCore(SpawnerEntry entry);
    protected abstract bool RemoveEntryCore(SpawnerEntry entry);
    protected abstract void ClearEntriesCore();
    protected abstract void AdoptEntries(IReadOnlyList<SpawnerEntry> entries); // legacy save / DTO import
    protected virtual SpawnerEntry CloneEntry(SpawnerEntry source);           // deep copy; no spawns
    protected static void TransferSpawned(SpawnerEntry source, SpawnerEntry target);
    protected void RebuildSpawned();                                          // rebuild Spawned, re-arm timer
}
```

`CreateEntry` is the one factory per owner that keeps each owner's serialized list concrete for the
generator — the rationale that survives from the interface sketch this section used to carry. `AdoptEntries`
takes ownership of entries built elsewhere (a legacy save or a DTO import); an owner that converts a
foreign entry into its own type must carry its live spawns across with the static `TransferSpawned`
helper, since `CloneEntry` deliberately does not copy them. `RebuildSpawned` rebuilds the `Spawned`
registry from `EntrySpan` and re-arms the timer; `Spawner` calls it from the base `[AfterDeserialization]`
for its own list, and any subclass owning a different list — `ModernSpawner` included (§4.3) — must call
it again from its own `[AfterDeserialization]` once that list is loaded.

Lifecycle hooks live in `BaseSpawner.Hooks.cs`, all `protected virtual`, all typed on `SpawnerEntry`:
`OnStarted()`/`OnStopped()` (after `Start()`/`Stop()`), `OnBeforeSpawn(entry) => true` (veto point before
construction), `OnConfigureSpawned(entry, spawned)` (after property application, before positioning, so
computed properties (D6) apply first), `GetSpawnPosition(entry, spawned, map)` (entry-aware, defaults to
the entry-agnostic overload), `OnSpawned(entry, spawned)` (after placement) and
`OnSpawnedDeath(entry, spawned, killer)`, reached through the public `NotifySpawnedDeath(spawned, killer)`
that `BaseCreature.OnDeath` calls while the spawner link is still intact.

`Spawner` (`[SerializationGenerator(2)]`) is the first concrete owner: it declares
`[SerializableField(2)] List<SpawnerEntry> _entryList` and implements the abstract members directly over
it; `ProximitySpawner`/`RegionSpawner` inherit that ownership unchanged since they already derive from
`Spawner`. `ModernSpawner : Spawner` (§4.3) instead declares its own `List<ModernSpawnerEntry>` and
re-implements the same members over that list — two sibling concrete owners of one abstract contract, not
an interface layer over both.

`SpawnerEntry` (`[SerializationGenerator(2, false)]`) gained a `Disabled` flag (`Enabled` is its inverted
public toggle, so the common enabled case writes nothing) that weighted selection now skips, and
`protected BaseSpawner Parent => _parent` with a public `SetParent(BaseSpawner)` so an out-of-assembly
owner can re-parent an entry it adopts in `AdoptEntries`.

Save migration: `BaseSpawner` bumped to v13. The existing `MigrateFrom(V10Content/V11Content/V12Content)`
legacy readers keep populating their own `Entries` field as before; each now finishes by calling
`AdoptEntries(content.Entries ?? [])` so the concrete owner takes the list over exactly once, instead of
`BaseSpawner` keeping its own `_entries` field for `AfterDeserialization` to rebuild from. Old readers and
their encoding are untouched.

DTO: `SpawnerDto` is an `abstract record` with `abstract IReadOnlyList<SpawnerEntry> EntryView { get; }`;
each concrete DTO owns its own typed collection and overrides the view (`SpawnerDataDto.Entries :
List<SpawnerEntry>`, `ModernSpawnerDto.Entries : List<ModernSpawnerEntry>`), and `BaseSpawner.ApplyDto`
calls `AdoptEntries(dto.EntryView)` — it never touches entries itself.

Copy operations are explicit on the base: `RemoveAllEntries()` (deletes every live spawn, then
`ClearEntriesCore()`) and `CopyEntriesTo(BaseSpawner target)` (clones this spawner's entries onto `target`
via `CreateEntry`/`CloneEntry`); `BaseSpawner.OnAfterDuped` and `SpawnerControllerGump.CopyEntry` both use
`CopyEntriesTo` so modern entry fields survive duplication and controller copies.

### 4.3 Shape of A (ModernSpawner side)

This shape is implemented (ModernSpawner main after the port PR) exactly as listed below, with two
differences from the original plan noted inline.

- `ModernSpawnerEntry : SpawnerEntry` (class inheritance; only the extra fields are declared here).
  Because it lives in another assembly, it must declare
  `[DirtyTrackingEntity] private BaseSpawner Owner => Parent;` so its generated setters mark the
  spawner dirty (see `modernuo-prerequisites.md`, generator follow-up).
- `ModernSpawner.Entries => _spawnEntries`; `CreateEntry` returns a `ModernSpawnerEntry`; the temp-entry
  path, `_modernSpawned`, `RemoveModernEntry`, `ModernSpawned`, and the `new` `AddEntry`/`Start`/`Stop`
  hides were deleted. **Difference:** `ModernEntries` and `AddModernEntry` were kept as typed
  conveniences over the base `SpawnerEntry`-typed contract, not deleted — callers that want
  `ModernSpawnerEntry` directly (tests, gumps) still use them.
- `Spawn()` override keeps cycle-mode selection and calls `base.Spawn(entry, out flags)`.
- **Difference:** `OnBeforeSpawn(entry)` is not overridden. The before-spawn script veto (`cancel()`) runs
  inline at the top of `Spawn()`, ahead of `Defrag()` and entry selection, rather than through the base
  hook. `OnSpawned(entry, spawned)` applies loot and the entry spawn script as planned.
- `GetSpawnPosition(entry, spawned, map)` applies the entry rule, else the entry's `SpawnAreaOffset`, else
  `base`.
- Start/Stop hooks: `BaseSpawner.Start/Stop` gained `protected virtual OnStarted/OnStopped` callbacks
  (upstream change), overridden here so `Running = …` reaches trigger activation and the
  activate/deactivate scripts.

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
  mobiles and clears `Spawner` on the way (`Mobile.cs:4647,4899`), so `bc.Spawner` is null by then. An
  upstream PR adds `protected virtual void OnSpawnedDeath(SpawnerEntry entry, ISpawnable spawned, Mobile killer)`
  on `BaseSpawner`, invoked from `BaseCreature.OnDeath` before base death while the link is intact. Death is
  distinct from removal (taming, pickup, delete). `RequireAllDead` is evaluated after removal against the
  entry's remaining live count.
- **Skill.** Needs a ModernUO PR: `SkillCheck` raises a generated `SkillEvents.SkillUsedEvent(Mobile,
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

- `Projects/ModernSpawner.Tests/Fixtures/ModernSpawnerTestServer.cs` boots a ModernUO test server and
  places a `ModernSpawner` on a **non-Internal** test map (`BaseSpawner.Spawn` refuses `Map.Internal`).
  ModernUO's own `TestServerInitializer` (in `UOContent.Tests`) is `internal` to that assembly, so this is
  a copy of the initializer modelled on it — not an upstream `InternalsVisibleTo` grant — that also loads
  `ModernSpawner.dll` and runs `ModernSpawnerConfiguration.Configure()`; it reuses ModernUO's own map table
  through `Server.Tests.Maps.TestMapDefinitions` (a project reference) so the two cannot drift.
  `Projects/ModernSpawner.Tests/Fixtures/ModernSpawnerFixture.cs` is the xunit `ICollectionFixture` that
  calls `Initialize()` once per process; every world-backed test carries
  `[Collection("Sequential ModernSpawner Tests")]` (`DisableParallelization = true`) since the bootstrap
  and `World` are process-global singletons.
- Known limitation: `Core._now` is `internal` to `Server.dll` with `InternalsVisibleTo` naming only
  `Server.Tests` and `UOContent.Tests`, so `ModernSpawnerTestServer` cannot seed the loop clock and
  `Core.Now` stays `DateTime.MinValue` for this host. Nothing on the spawner lifecycle paths currently
  depends on an absolute wall clock, but a future test that reads or advances the clock needs the
  prerequisite in §11.
- `Projects/ModernSpawner.Tests/Core/ModernSpawnerLifecycleTests.cs` is the current end-to-end suite over
  the fixture: spawn/kill/respawn, stop/start (with activate-script dispatch), dupe (asserts every field
  `CloneEntry` copies), DTO round trip, binary save round trip, and kill-hook dispatch (`OnSpawnedDeath`
  running the despawn script and handing the kill to `TriggerSystem`). Extend this suite as trigger gate,
  entry-rule placement and loot-application coverage is added.
- Parser tests remain; add "producer→parser" tests for every gump/importer-generated string.
- Benchmarks stay in `ModernSpawner.Benchmarks`; add a tick-loop allocation benchmark.

## 11. ModernUO prerequisites created by this design

Tracked in `modernuo-prerequisites.md`: DTO helper visibility (done), abstract entry ownership (§4.2),
`OnStarted/OnStopped` and `OnConfigureSpawned` virtuals, `OnSpawnedDeath` hook, `SkillUsedEvent`, test
initializer access, `InternalsVisibleTo("ModernSpawner.Tests")` on `Server.csproj` (so the test fixture
can seed `Core._now`), GUID-based replacement in `[ImportSpawners` (today it deletes co-located same-type
spawners and calls `Respawn()` unconditionally, `ImportSpawnersCommand.cs:259`), sector-range movement
subscription (deferred).

## 12. Review history

- 2026-09-08 Codex (gpt-6-astra) review of the audit and these specs: `docs/reviews/2026-09-08-codex-spec-review.md`
  (uncommitted). Its verified corrections are folded into §4–§10 and into `xmlspawner-migration.md`.
