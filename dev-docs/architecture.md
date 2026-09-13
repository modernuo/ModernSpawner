# ModernSpawner — Architecture

Status: draft v1, 2026-09-08. Describes the code as it is (§2–3) and the target design the product spec
assumes (§4–9). Decisions are labelled with the IDs from `product-spec.md` §10.

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

`ModernSpawner` derives from `Spawner` and adds 22 serialized fields (v1, orders 0–21): `_spawnEntries`
(`List<ModernSpawnerEntry>`, field 0), four script serials (1–4), three positioning flags (5–7), the
trigger definition list and the `TriggerActivated` flag (8–9), `_notes` (10), five fields of cycle-mode
state (11–15), and the D2 trigger runtime — the pending-cycle queue (16), `MaxPendingCycles` (17), the
refractory range and its absolute deadline (18–20) and the per-definition state list (21).
`ModernSpawnerEntry` derives from `SpawnerEntry` and adds 12 extra fields (0–11): scripts, delays,
positioning rule, group, LOS, area offset, range, loot template, subgroup and `NextEligible` — the six
`SpawnerEntry` fields and the `Disabled` flag come from the base class.

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

Spawn flow: the timer calls the overridden `OnTick`, which applies the T0–T6 precedence of §5 and either
parks (leaving the timer unarmed) or runs one cycle. `Spawn()` is no longer the tick path — it is the
manual API (M1), trigger-bypassing and deadline-bypassing, and both it and the tick end in the same
private cycle body. That body runs the before-spawn script veto, defrags, then selects an entry by cycle
mode (`SpawnWeightedOne` for Random/Sequential, `SpawnAllEntries` for `AllEntries`) over `_spawnEntries`
with plain `for` loops, filtering on `IsEligible && (bypassDeadlines || entry.IsDue(now))`, and calls the
base `Spawn(entry, out flags)` for the chosen entry. That base call positions the entity through the
entry-aware `GetSpawnPosition(entry, spawned, map)` override (entry `PositioningRule`, else the entry's
`SpawnAreaOffset`, else the spawner's own positioning) and, once placed, calls `OnSpawned(entry, spawned)`
to apply the entry's loot template and `OnSpawnScript`. Each attempt then moves the entry's own
`NextEligible`: its full delay after a placement, `min(30s, MinDelay)` after a failure. On death,
`OnSpawnedDeath(entry, spawned, killer)` notifies `TriggerSystem` for kill triggers and runs the entry's
`OnDespawnScript`.

### 2.2 Triggers (`Triggers/`)

`TriggerSystem` keeps one `TriggerSet` per registered spawner in a single dictionary, so a movement,
speech or kill dispatch does one lookup and then walks a typed list by index. Triggers are parsed from
`type:field:field` strings held as `List<TriggerDefinition>` (`{ Id, Text }`, stable `Guid.CreateVersion7`
ids). Registration goes through one guarded helper, `ModernSpawner.EnsureTriggersActive()`, the only
caller of `TriggerSystem.ActivateTriggers` outside the trigger system, within the engine project (tests
call it deliberately too, to build the stale-registration cases teardown has to survive): it deactivates
first — retiring the previous `TriggerSet`'s window timers and skill candidacy instead of leaving them
live alongside the replacement — rebinds trigger state by id, then re-registers only when the spawner is
not deleted, is `TriggerActivated` and actually has definitions. **Registration is independent of
`Running`** (A1/A2 of §5): `Start()`/`Stop()` only arm or disarm the timer and never call
`EnsureTriggersActive`, so a stopped spawner keeps hearing its own triggers and a `wake:` one can restart
it. The mandatory callers are the `TriggerActivated` setter; `AddTriggerDefinition`/
`RemoveTriggerDefinitionAt`/`ClearTriggerDefinitions` (the only way to mutate the definition list — they
wrap the generated collection helpers so the change is tracked for serialization first); the deferred
`[AfterDeserialization(false)]` load hook; and every construction path that hands back an already-running
spawner (`OnAfterDuped`, `ModernSpawnerDto.ToSpawner`, both JSON importer entry points,
`XmlSpawnerImporter`, `XmlSpawnerMigrator`). Wiring:

| Trigger | Source event | Wired |
|---|---|---|
| proximity | `Item.OnMovement` (24-tile radius, engine-fixed; wider ranges clamp to `Core.GlobalMaxUpdateRange`) | yes |
| speech | `Item.OnSpeech` (15/18-tile radius); regex match has a timeout | yes |
| kill | `OnSpawnedDeath` via `BaseSpawner.NotifySpawnedDeath`, called from `BaseCreature.OnDeath` | yes, tested |
| skill | `SkillEvents.SkillUsed` → `ModernSpawnerEvents.OnSkillUsed` (players only) → `TriggerSystem.OnSkillUse` | yes, tested |
| game_time_window | one transition timer; `timeofday` is retired as a trigger class and parses only as an alias onto this one | yes; real time per game hour is derived from `Clock.SecondsPerUOMinute` (5 s per UO minute → 5 real minutes per game hour) |
| wall_time_window | `EventScheduler` + `BaseScheduledEvent` subclass; day/month filters apply to the open edge only, by design | yes |

Dispatch never calls `Spawn()` directly: a match calls
`spawner.RequestCycle(trigger, set.Generation, in context)`, which only mutates spawner state (cooldown,
refractory, the pending-cycle queue) and asks `TriggerSystem` for a drain; the outermost dispatch runs the
actual cycle once it returns (§5). The generation stamp is what makes a request from a registration the
spawner has since replaced — a definition edit, a map move, a deactivation — a no-op instead of a cycle
run against state its trigger is no longer bound to. A trigger that matches but is refused does not end
the dispatch: the loop moves on to the next trigger until one is accepted.

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
| ModernUO `SpawnerDto` JSON | `ModernSpawner.Dto.cs` | complete: base fields (base `Group` included), `List<ModernSpawnerEntry>` entries, scripts, options, trigger definitions as `{ id, text }`, `maxPendingCycles`, the refractory range and cycle state; round trip tested, and `ToSpawner` registers the imported triggers. Runtime state — queued cycles, cooldowns, kill counts, `RefractoryUntil`, entry deadlines — is world-save only and never exported |
| Own JSON `modernspawner/v1/spawner.json` | `SpawnerJsonExporter/Importer` | drops 8 entry fields, cooldowns, and the D2 fields the DTO carries (`maxPendingCycles`, the refractory range, trigger definition ids — triggers are bare strings, so ids are minted fresh on import); property syntax unusable (V-1) |
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
  need `Migrations/*.vN.json` produced by `ModernUOSchemaGenerator`; the repo has seven: `ModernSpawner`
  v0 and v1, `ModernSpawnerEntry` v0 and v1, and v0 for `PendingCycle`, `TriggerDefinition` and
  `TriggerRuntimeState`.
- **Bootstrap order.** `EventScheduler.Configure` runs before world load, so scheduling during
  deserialization is safe; `TriggerSystem` and `ScriptRegistry` instances are created in `Configure()`.
- **Hot paths.** `OnTick`→`Spawn()`, `OnMovement` (every step of every mobile within 24 tiles of a spawner
  with proximity triggers), `OnSpeech`, and script execution per spawn. The closure and temp-entry
  allocations the dual-list design forced on the spawn path are gone: entry selection is plain `for` loops
  over `_spawnEntries` and the base `Spawn(entry, out flags)` is handed the real entry. What still
  allocates per event: `TriggerContext` is a `readonly record struct` passed `in`, so proximity, speech,
  kill and skill dispatch allocate nothing, and the skill candidate list carries each spawner's
  `TriggerSet` so an attempt costs one dictionary lookup for the map and none per candidate. What still
  allocates is a `ScriptContext` per script execution, and one per candidate event for a trigger that
  carries a `when:` condition — bounded in practice by that trigger's cooldown, since an event inside it
  never reaches the `when:` check. Trigger definition strings are `Split` only in `Parse`, once at
  activation, never per event; `when:` expressions are compiled there too.

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

## 5. Triggers (D2, D3) — as built

- **Two trigger classes.** *Gate* triggers (`wall_time_window`, `game_time_window`) open and close a
  spawner's gate; `timeofday` is retired as a class and parses only as an alias onto `game_time_window`.
  *Event* triggers (proximity, speech, kill, skill, and the external `Trigger()` source) each buy at most
  one spawn cycle per accepted match and never latch.
- **State lives on the spawner**, not the trigger system, so a tick compares fields and does no lookup:
  the open-gate set (a `ulong` bitmask over definition index, with a `List<int>` overflow past 64), a
  bounded FIFO of pending cycles (`PendingCycle { TriggerId, TriggeringMobile }`, `0..MaxPendingCycles`
  entries, default 1), a spawner-wide `RefractoryUntil`, per-definition `TriggerRuntimeState
  { CooldownUntil, KillCount }` keyed by the definition's stable `Id`, and a per-entry `NextEligible`
  deadline with failure backoff.
- **Tick authorization** (`IsAuthorizedForTick`): `Running && !Deleted && GateOpen && !IsFull &&
  (EventCount == 0 || PendingCycleCount > 0)`. `GateOpen` is true whenever the spawner has no gates, is not
  `TriggerActivated`, or at least one window is currently open.
- **Tick precedence** (`OnTick`, evaluated top to bottom, first match wins) and the rest of the state
  machine, by row id (T = tick, E = event acceptance, D = drain, G = gate edge, A = administration,
  M = manual API, L = load, X = delete, F = base arms a tick):

  | Row | Meaning |
  |---|---|
  | T0 | Deleted or not running: nothing, never re-arm |
  | T1 | Base `Group`: any spawn alive → park until removed; else if authorized → consume one pending slot (if any), run the bulk group respawn once, re-arm |
  | T2 | Gate closed: park until a gate opens |
  | T3 | Full: park until a spawn is removed |
  | T4 | Event-sourced with nothing queued: park until an event |
  | T5 | No entry due: arm at the earliest future entry deadline |
  | T6 | Otherwise: pop one slot if event-sourced, run the cycle, re-arm at the earliest deadline |
  | E0 | Event candidate not accepted: no state change |
  | E1 | Accepted, `mode:now`, running/open/not full: push a slot; drain once the dispatch that raised it returns |
  | E2 | Accepted, `mode:now`, gate closed or full: `Max > 0` → push and wait (T2/T3 arm later); `Max = 0` → drop |
  | E3 | Accepted, `mode:tick`: push a slot; arm an immediate tick so it runs with normal T0–T6 ordering |
  | E4 | Accepted, stopped, `wake:true`: push a slot, `Start()`; run as E1/E2 if it started, else keep the slot |
  | E5 | Accepted, stopped, `wake:false`: `Max > 0` → push; `Max = 0` → drop |
  | E6 | Queue already at `Max`: reject before any acceptance side effect |
  | D1 | Drain requested but deleted/stopped/deactivated/gate-closed/full: discard the slot if deleted/deactivated, else keep it |
  | G1 | Gate opens, `0 → 1`, running: add the id; run a cycle now (event-free) or drain one queued slot (event-sourced); re-arm |
  | G2 | Gate opens, already open or the set stays non-empty: add the id only |
  | G3 | Gate closes, `1 → 0`: remove the id; live spawns and queued cycles are untouched |
  | G4 | Gate closes, id absent/stale: no-op |
  | A1 | `Start()`: arms the timer only; never reparses or re-registers |
  | A2 | `Stop()`: stops the timer only; registration, cooldowns, counters and gate schedulers stay live |
  | A3 | `TriggerActivated` false→true, definitions change, or map/location change: (re)register, hydrate the gate set and counts silently, rebind state by id |
  | A4 | `TriggerActivated` true→false: unregister; clears the gate set, the queue and any pending drain |
  | A5 | `MaxPendingCycles` changed: clamp ≥ 0; lowering trims the oldest queued slots |
  | M1 | Manual `Spawn()`/indexed `Spawn(entry)`/script `spawn()`: bypasses triggers and entry deadlines; queue and cooldowns untouched |
  | M2 | `Respawn()`: trigger-bypassing bulk op; queue untouched; re-arms only if running |
  | M3 | `Reset()`: stops and clears spawns; clears the queue, cooldowns, refractory and kill counters; keeps registration |
  | M4 | `ResetTrigger()`: clears the queue and cancels any pending drain |
  | L1 | Load, activated and not deleted: rebind state by id, clamp the queue, hydrate the gate set from the clock, register; no spawn during load |
  | L2 | Load, deactivated or deleted: no registration; queue cleared |
  | X1 | Delete: invalidate the registration first, cancel timers and drains, unregister after lifecycle scripts |
  | F1 | A spawn is removed or `Count` changes: the resulting tick follows T0–T6 as usual |

- **Acceptance order** (`RequestCycle`, every check runs before any state change): registration live →
  the trigger matches (kill triggers evaluate here, since their threshold is spawner state; every other
  class arrives already evaluated by its dispatcher) → the trigger's own `CooldownUntil` has passed → the
  spawner's `RefractoryUntil` has passed → the `when:` expression passes → the queue has room, or
  (`MaxPendingCycles == 0`) the cycle can run right now. Only an accepted event moves the cooldown and the
  refractory, together.
- **Drain after dispatch.** A trigger match never calls `Spawn()`. `RequestCycle` mutates state and asks
  `TriggerSystem` to drain the spawner; the outermost dispatch (proximity, speech, kill, skill, or a
  script's `spawn()`) runs the actual cycle once it returns. A cycle's own scripts can raise further
  events; those queue into the same drain list rather than nesting, bounded by 10 drains per spawner per
  outer round (matching the script recursion limit) and by a per-spawner re-entry guard.
- **Registration is independent of `Running`.** `Start()`/`Stop()` only arm or disarm the timer; the
  `TriggerActivated` setter, the definition-list mutators (`AddTriggerDefinition`/
  `RemoveTriggerDefinitionAt`/`ClearTriggerDefinitions` — the only way to change the list), load and
  delete are what (re)register. A stopped spawner keeps hearing its own triggers, and an event with
  `wake:true` can restart it.
- **Rulings that affect semantics.** The kill counter advances on every counted kill, including while its
  trigger is on cooldown, and resets only when a cycle is accepted (otherwise `kill:N>1` could never fire).
  `mode:` is ignored when `MaxPendingCycles = 0` — there is no slot to defer into, so the event runs now or
  is dropped; the migrator compensates by mapping XmlSpawner's `SpawnOnTrigger=false` to
  `MaxPendingCycles = 1` plus `mode:tick`. Only the `mode:now` drain path bypasses per-entry deadlines — a
  `mode:tick` slot later consumed by a `mode:now` drain still gets the bypass, and a gate's window-open
  cycle (G1) honours deadlines like a timer cycle. On a base-`Group` spawner, T1's "any spawn alive → park,
  else one bulk respawn" applies identically whether the cycle source is the tick, a drained slot, or a
  gate opening. `wake:`/`mode:`/`when:` are recognised only as a suffix after each grammar's full
  positional list, so a positional value spelled like a token is never misread.
- **Shape.** `TriggerSet` (one per registered spawner, in one dictionary keyed by spawner) replaces six
  per-type dictionaries; gate and pending-cycle state stay on the spawner so ticks do no lookup.
  `TriggerContext` is a `readonly record struct` passed `in`; `ITrigger.Evaluate` is pure (reads
  `TriggerRuntimeState` but never writes it) and contains no iterator (`yield`). Extended proximity clamps
  to `Core.GlobalMaxUpdateRange` with a warning.
- **Kill.** `CreatureEvents.CreatureDeathEvent` fires *after* `Mobile.OnDeath`, which deletes non-player
  mobiles and clears `Spawner` on the way, so `bc.Spawner` is null by then. `BaseSpawner.OnSpawnedDeath(entry,
  spawned, killer)` (an upstream hook, merged) is invoked from `BaseCreature.OnDeath` before base death
  runs, while the link is intact. Death is distinct from removal (taming, pickup, delete). Because the
  notification precedes removal, `RequireAllDead` is evaluated against the spawner's live count with the
  dying spawn excluded — otherwise the kill that actually clears the pack could never satisfy it.
- **Skill.** `SkillCheck`'s four `Mobile_SkillCheck*` handlers raise `SkillEvents.SkillUsed(Mobile, Skill,
  bool success)` once per attempt (short-circuited attempts included; not raised when the mobile lacks the
  skill). `ModernSpawnerEvents.OnSkillUsed` forwards only players (`mobile is { Player: true }`) to
  `TriggerSystem.OnSkillUse`, which pre-scans `SkillTrigger.MatchesSkill` before allocating a
  `TriggerContext` so spawners with no matching trigger allocate nothing. `SkillTrigger` adds an outcome
  filter (any/success/failure) and a min/max skill-value window on top of range and line-of-sight. Line of
  sight is `Mobile.InLOS`: `CanSee` ends in `Item.Visible`, which a spawner never is. Dispatch iterates a
  pooled snapshot of the registration map, because a script reached from `Trigger()`'s drain can delete or
  register a spawner.
- **Grammar.** One definition grammar per trigger's `Serialize()`; gumps and importers construct trigger
  objects and call it. The per-event-trigger tokens (`wake:true|false`, `mode:now|tick`, `when:<expr>`) are
  a suffix recognised only after the grammar's full positional list.
- **Clock.** `timeofday` is retired and parses only as an alias onto `game_time_window`. The game-time
  window derives real time per game hour from `Clock.SecondsPerUOMinute` (5 s per UO minute, so a game
  hour is 5 real minutes and a game day two real hours) rather than restating it. A map change
  re-registers a spawner's triggers, so a gate recomputes its window against the new map's clock and
  skill candidacy moves with it.
- **Performance** (D2's own condition: no per-tick/per-movement regression at 12k+ spawners). Measured in
  `Release`, 12,000 spawners on one map, one player walking a 2,000-step lap: movement-dispatch steady
  state was ~144–162 ns/call and 72 B/call before D2, ~131–133 ns/call and 0 B/call after; ticking the
  whole population (gate closed or open, queues empty) costs roughly 49–70 ns/call, 0 B/call either way.
  BenchmarkDotNet micro-benchmarks (mock types, no ModernUO reference) cover `TriggerSet` lookup and the
  request/drain path in isolation. Run the world-backed harness with:
  ```sh
  MODERNSPAWNER_PERF=1 dotnet test Projects/ModernSpawner.Tests \
    --filter "FullyQualifiedName~TriggerPerfHarness" --logger "console;verbosity=detailed"
  ```
  and the micro-benchmarks with `dotnet run -c Release --project Projects/ModernSpawner.Benchmarks --triggers`.

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
  (all entry fields), `triggers: List<{ id, text }>` (the pre-id shape, a bare definition string, still
  reads), `maxPendingCycles`, `refractoryMin`/`refractoryMax`, `cycleMode`, `currentSubgroup`,
  `sequentialResetTime/To`, `holdSequence`. `WhenWritingDefault` defaults handled with `ShouldSerialize`
  modifiers like upstream `homeRange`. Own JSON and YAML removed with their gumps' import/export switched
  to the DTO path.
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

Tracked in `modernuo-prerequisites.md`: DTO helper visibility (done), abstract entry ownership (§4.2,
done), `OnStarted/OnStopped` and `OnConfigureSpawned` virtuals (done), `OnSpawnedDeath` hook (done),
`SkillEvents.SkillUsed` (done, #2636) with `InternalsVisibleTo("ModernSpawner.Tests")` on `Server.csproj`
(so the test fixture can seed `Core._now`), virtual `BaseSpawner.OnTick` and `group` in the stock DTO
(done, #2640 — §5 needs a tick gate that does not also gate manual `Spawn()`), GUID-based replacement in
`[ImportSpawners` (today it deletes co-located same-type spawners and calls `Respawn()` unconditionally,
`ImportSpawnersCommand.cs:259`), sector-range movement subscription (deferred).

## 12. Review history

- 2026-09-08 Codex (gpt-6-astra) review of the audit and these specs: `docs/reviews/2026-09-08-codex-spec-review.md`
  (uncommitted). Its verified corrections are folded into §4–§10 and into `xmlspawner-migration.md`.
