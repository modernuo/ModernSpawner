# ModernSpawner — Product Specification

Status: draft v1, 2026-09-08. Rebuilt from a code audit (`docs/feature-audit.md`, uncommitted) after the
original specs were lost. Sections marked **Decision** need a maintainer ruling; each carries the
recommended default the rest of this document assumes.

## 1. What it is

ModernSpawner is the advanced spawner for [ModernUO](https://github.com/modernuo/ModernUO). It replaces
XmlSpawner for shards that need more than the stock `Spawner`: scripted spawns, triggers, positioning rules,
sequenced encounters, loot overrides, and a migration path for existing XmlSpawner content.

It is a separate repository from ModernUO and consumes ModernUO as a submodule. It will not ship inside
ModernUO for the foreseeable future.

## 2. Who it is for

| Persona | Needs | Today's tool |
|---|---|---|
| **Shard owner / developer** (AccessLevel.Developer) | Deploy once, migrate an XmlSpawner world, keep it working across ModernUO upgrades | XmlSpawner fork, hand-patched |
| **World builder / GM** (GameMaster) | Place spawners, define entries and triggers, tune without code, export/import spawn sets | XmlSpawner gumps + books |
| **Event staff** (Counselor/GM) | Turn encounters on/off, respawn, inspect status | `[SpawnAdmin`, XmlSpawner gump |

Players never interact with it directly; they experience its output.

## 3. Positioning

| | ModernUO `Spawner` | XmlSpawner (legacy) | ModernSpawner (target) |
|---|---|---|---|
| Entries with props/params | yes | yes (slash DSL) | yes |
| Per-entry scripts, conditions | no | keyword DSL, runtime string parsing | compiled scripts, one language |
| Triggers | proximity only | proximity, speech, skill, TOD, property, external | proximity, speech, kill, skill, time windows, external |
| Positioning | random in bounds, sector cache | 15 prefixes | base positioning + named rules |
| Sequencing | no | subgroups, GOTO, hold | subgroups, sequential/group cycle |
| Loot overrides | no | via SET on Backpack | loot templates |
| Persistence | binary + JSON DTO | binary + XML DataSet | binary + ModernUO JSON DTO |
| Staff UI | one gump | gumps + books | gumps for structure; external editor for scripts (**Decision D7**) |
| Performance | hot-path clean | reflection per spawn | compiled accessors, zero-alloc paths |

## 4. Goals and non-goals

### Goals

- **G1** Spawn any `Mobile` or `Item` type with constructor parameters and property overrides, using ModernUO's own entry semantics.
- **G2** Triggers: proximity, speech, kill, skill use, game-time window, wall-clock window, external (`Trigger()` from scripts/commands). Triggers can *gate* a spawner (no spawning until triggered) or *fire* a spawn cycle.
- **G3** Positioning: keep every stock ModernUO positioning guarantee (sector cache, spiral scan, house blocking, multi-Z) and add named rules per entry (waypoint, relative, player-relative, absolute, perimeter, row/column fill, tile filters, water, near/avoid water, at-item, region).
- **G4** One script language for spawner hooks (activate/deactivate/before/after spawn), entry hooks (spawn/despawn), and entry conditions, with compile-time validation and errors visible to staff.
- **G5** Sequenced encounters: subgroups, Sequential and Group cycle modes, hold/advance/reset, per-entry delays.
- **G6** Loot templates applied at spawn, loaded from data files, editable without code.
- **G7** Import XmlSpawner `.xml` saves with a written report of what converted, what was approximated, and what was dropped.
- **G8** Match or beat XmlSpawner runtime cost; zero allocations on the per-tick and per-movement paths.
- **G9** Staff can do everything structural in-game; long scripts have a first-class authoring path that is not a 239-character text box.
- **G10** Round-trip a ModernSpawner losslessly through ModernUO's `[ExportSpawners`/`[ImportSpawners` so spawn sets live in `Distribution/Data/Spawns` like stock ones.

### Non-goals (v1)

- XmlSpawner attachments (XmlAttach), quests (XmlQuest), talking NPCs, XmlItems switches/traps. Separate projects if ever.
- 100% XmlSpawner keyword compatibility. Keywords are translated where a clean equivalent exists and reported otherwise.
- Reading XmlSpawner *binary world saves*. Migration is from XmlSpawner's `.xml` export only.
- Web/REST stats. Any external tool talks to the server through a dedicated, authenticated channel designed later (**D7**).
- In-world side-by-side replacement of live XmlSpawner items.

## 5. Feature scope for v1

Status columns reflect the audit at `8935ca4`. "Target" is the v1 commitment.

### 5.1 Spawner and entries

| Feature | Audit status | v1 target |
|---|---|---|
| Entries with name, probability, max count, properties, parameters | Partial (dual list) | Single entry list shared with ModernUO base (**D1**) |
| Per-entry min/max delay override | Stubbed | Implemented |
| Per-entry subgroup, Sequential/Group cycle, hold, advance, reset, auto-reset | Implemented (logic) | Kept; auto-reset survives restart |
| Group mode "whole group respawns together" | Partial | Implemented as documented |
| Count/IsFull semantics | Missing on modern path | `Count` = total live spawns cap, honoured by both timer and `Spawn()` |
| Start/Stop/Running/Reset/Respawn/Delete lifecycle | Broken | All paths correct, including trigger and script hooks |
| Dupe | Missing | Supported |
| Property list (tooltip) shows entries | Bypassed | Shows first N entries like stock |
| `[SpawnAdmin` / stock gumps see entries | Bypassed | Stock gumps, updated on the support branch to the entry interface, list and copy modern entries without losing modern fields (consequence of **D1**) |
| Notes | Implemented | Kept |

### 5.2 Triggers

| Feature | Audit status | v1 target |
|---|---|---|
| Proximity (≤24 tiles) | Implemented | Kept |
| Proximity beyond 24 tiles | Stubbed | Range clamped with a warning; wider ranges need a ModernUO area-subscription API (tracked in `modernuo-prerequisites.md`) |
| Speech | Implemented | Kept; regex timeout; whether it may wake a stopped spawner is per-trigger (`wake:`) under **D2** |
| Kill | Stubbed | Wired via a new `BaseSpawner.OnSpawnedDeath` hook on the support branch (the creature-death event fires after the spawner link is cleared) |
| Skill | Stubbed | Wired via a ModernUO hook on the support branch; until then `skill:` definitions are rejected at parse time with a visible error (**D3**) |
| Game-time window | Partial | Constant derived from `Clock.SecondsPerUOMinute`; recomputed on map change |
| Wall-clock window | Partial | Day/month filters apply to the open edge only; weekly/monthly recurrence exposed |
| Legacy `timeofday` | Implemented | Retired in favour of `game_time_window` (importer maps to it) |
| Gate vs fire semantics | Incoherent | Defined in **D2** as a state machine (gate set + pending cycles), transition table approved before implementation |
| Triggers on stopped spawners | Missing | Registered whenever `TriggerActivated`, independent of `Running`; events on a stopped spawner queue a cycle and only start the timer if the trigger says `wake:true` |
| Composition (AND/OR) | Missing | v1: implicit OR across triggers plus a per-trigger `when:` expression; explicit AND groups deferred |
| Definition grammar | Three producers disagree | One grammar; gumps and importers construct trigger objects and call `Serialize()` |

### 5.3 Scripting

| Feature | Audit status | v1 target |
|---|---|---|
| Language | Two disconnected (slash DSL runs; expression language unreachable) | One language: statements over the expression engine (**D4**) |
| Hooks | 4 spawner + 2 entry | Same, plus entry `condition` evaluated before each spawn of that entry |
| Actions | SET, SETVAR, CANCEL, MSG, SPAWN, DESPAWN, GOTO(teleport), ACTIVATE, DEACTIVATE, BROADCAST, SOUND, EFFECT | Same set as functions, plus `goto(subgroup)`, `give(item)`, `setOn(target, …)`; `COMMAND` deliberately excluded |
| Conditionals | None in runtime | `if … then … [else …]` statements |
| Values | literals, `{min,max}`, `$var`, `@path` | expression values everywhere; hex literals; random(); property paths with `.` |
| Coercion | Throws on value types | Full matrix incl. hex, `TimeSpan`, `Point3D`, enums, serial refs; failures are errors, not exceptions |
| Validation and errors | Silent | Compile errors returned to the caller (gump shows them); runtime errors logged and broadcast to online GMs once per spawner per minute |
| Re-entrancy | Unbounded | Depth limit 10 (XmlSpawner parity) |
| Persistence | Spawner scripts in registry, entry scripts inline | Registry with removal on delete; entry scripts cached by source |

### 5.4 Positioning

| Feature | Audit status | v1 target |
|---|---|---|
| Base behaviour | Reimplemented weaker | Delegate to `BaseSpawner.GetSpawnPosition` unless an entry rule applies |
| Per-entry rule reachable | Dead | Entry known before positioning (**D1** makes this natural) |
| Rule parameters | Re-parsed per spawn | Parsed once, cached on the entry, validated in gumps/importers |
| `player_relative` | Stubbed | Receives the triggering mobile |
| Region rule, `#ITEMID`/`#NOITEMID`, no-surface `*` | Missing | Region rule yes; item-id filters yes; `*` no |
| Row/column fill state | Static dictionary | Stored on the spawner, reset on Reset/Delete |
| `SpawnArea` vs `SpawnBounds` | Same field, two names | One concept: `SpawnBounds` |
| `UseSmartPositioning`, `MaxZDelta`, `DefaultPositioner` | Misleading | Removed; base positioning covers it |

### 5.5 Loot

| Feature | Audit status | v1 target |
|---|---|---|
| Template format | Implemented | Kept (JSON) |
| Loading | Never | `Distribution/Data/ModernSpawner/Loot/*.json` loaded at Configure; `[LootTemplates reload` |
| Application | At spawn, additive | Kept; `ClearDefaultLoot` documented as spawn-time only |
| UI | None | Entry gump exposes template name with validation; template editing stays file-based |

### 5.6 Persistence and export

| Feature | Audit status | v1 target |
|---|---|---|
| Binary world save | Implemented, untested | Tested; `Migrations/*.v0.json` generated; tool manifest in repo |
| ModernUO DTO (`[ExportSpawners`/`[ImportSpawners`) | Drops modern entries | Canonical, lossless (**D5**) |
| Own JSON (`modernspawner/v1/spawner.json`) | Partial | Removed |
| YAML script format | Stubbed | Removed; scripts are strings in the DTO |
| Entry `Properties` syntax | Mismatch (V-1) | One syntax: ModernUO's `Name Value` pairs; random ranges move to entry scripts (**D6**) |
| Export/import gump | Works, traversal risk | Uses the DTO path, rooted under `Core.BaseDirectory`, rejects rooted/`..` names |

### 5.7 XmlSpawner migration

See `xmlspawner-migration.md`. v1 target: offline conversion of `<Spawns><Points>` files into DTO JSON with
a report; live ModernUO import of the result.

### 5.8 Staff UI

| Feature | Audit status | v1 target |
|---|---|---|
| Main entry gump | Works except dead buttons | Fixed; opens from double-click, `[ModernSpawner` command, and context menu |
| Settings / Scripts / Export / Trigger / Condition gumps | Overflow | Heights computed from content; pagination where unbounded |
| Trigger editor | Writes bad keys | Builds trigger objects; offers all trigger types |
| Script fields | 239-char single line | Show read-only preview + compile status; edit via **D7** path |
| Condition builder | Stub | Removed until the external editor exists |
| Property builder | Works | Emits the canonical `Properties` syntax |
| Access level | Developer | GameMaster for open/edit; Developer for import/export/perf (**D8**) |

## 6. Distribution

**Decision D0 — how a shard consumes ModernSpawner.** Recommended: source-level. The shard adds this
repository as a submodule beside their ModernUO checkout (or inside it under `Projects/ModernSpawner`) and
adds the project to their solution. Reasons: ModernSpawner requires engine changes that live on the ModernUO
support branch until the first release; ModernUO's `Directory.Build.props` pins RIDs and the serialization
generator per-project; and code-generated events and `protected` hooks do not survive a DLL boundary well.
A prebuilt DLL into `Distribution/Assemblies` remains possible for shards on a ModernUO release that already
contains every prerequisite, and `AssemblyHandler` discovers `Configure`/`Initialize`, commands and
`[JsonDiscoverableType]` in it, but it is not the primary path for v1.

## 7. Compatibility

- Requires ModernUO `feat/spawner-stj-migration` (main + the changes listed in `modernuo-prerequisites.md`) until those merge.
- .NET 10, C# 14, same analyzers and rules as ModernUO.
- Era-agnostic: nothing in ModernSpawner checks `Core.AOS` etc.; loot behaviour differs by era only through `BaseCreature`.
- World saves: serialization versions start at 0 with `MigrateFrom` on every bump. Saves produced by the pre-rebuild code (`XmlSpawner-for-Modernuo`) are **not** supported; there are no known deployments.

## 8. Quality bar

- Every spawn-path behaviour has an xunit test that constructs a real `ModernSpawner` on a test map (ModernUO's `TestServerInitializer` pattern). No feature is "covered by in-game testing" alone.
- Hot paths (tick, movement dispatch, spawn) add no allocations beyond constructing the spawned entity itself; engine overhead per spawn and per movement event has an allocation and CPU budget measured by benchmark during the phase that implements each path, not at the end.
- `dotnet build -c Analyze` clean.
- Every string a gump or importer produces is parsed by a test through the same parser the runtime uses.
- Every ModernUO change is on the support branch with a line in `modernuo-prerequisites.md`.

## 9. Release criteria (v1)

1. P0 and P1 items in `docs/feature-audit.md` §3 resolved.
2. A stock-shard smoke script: place, add entries, spawn, kill, respawn, stop, start, export, import, restart server, repeat.
3. An XmlSpawner sample save (≥ 200 spawners) migrates with a report and runs.
4. ModernUO support branch merged or a tagged compatible ModernUO release exists.
5. README, this spec, architecture and migration docs current.

## 10. Decisions required

| ID | Question | Recommended default (assumed by all docs) |
|---|---|---|
| **D0** | Distribution: source submodule vs DLL | Source submodule for v1; DLL later |
| **D1** | Entry ownership: change ModernUO so `BaseSpawner` is entry-type-agnostic, or make `ModernSpawnerEntry : SpawnerEntry` and let the base own the list | Change ModernUO: abstract entry ownership (`architecture.md` §4) |
| **D2** | Trigger semantics | State machine: gate set (windows) + bounded pending-cycle queue (events); timer spawns only when the gate is open and, if `TriggerActivated`, a cycle is pending; `architecture.md` §5 |
| **D3** | Skill trigger source | Add a `SkillCheck` hook to the ModernUO support branch |
| **D4** | Script language | One statement language over the expression engine; slash DSL retired (importer translates) |
| **D5** | Canonical export format | ModernUO `SpawnerDto`; own JSON and YAML removed |
| **D6** | Entry `Properties` syntax | ModernUO's `Name Value` pairs; ranges/expressions live in entry scripts |
| **D7** | Long-script authoring | Out of scope for this spec; discussed separately (external staff tool vs in-game book/chunked gump). Gumps show read-only previews meanwhile |
| **D8** | Gump access level | GameMaster |
| **D9** | XmlSpawner migration scope | Offline `.xml` → DTO JSON with report; keyword translation where lossless; spawners whose encounter logic cannot be reproduced (`WAIT`, serial targets, property gates) are imported **stopped** with a report line rather than run as fragments |
| **D10** | Action entries and per-spawn lifetimes | XmlSpawner keyword entries (execute instead of spawn) and `Duration` need runtime concepts ModernSpawner lacks; decide before Phase 1 whether entries may be entity-free "action entries" and whether entries carry a despawn timer |
| **D11** | Base class | Derive `ModernSpawner` from `Spawner` (inherits spiral scan, stock list shape) instead of `BaseSpawner`; interacts with D1 |
