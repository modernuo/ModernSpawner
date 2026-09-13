# ModernSpawner

Advanced spawner engine for [ModernUO](https://github.com/modernuo/ModernUO), built to replace XmlSpawner.
.NET 10, single-threaded game loop, same rules and conventions as ModernUO.

**This repository requires ModernUO.** It is consumed as the `ModernUO/` git submodule, and the build,
style rules, dev-docs, and Claude skills all come from there. Run `git submodule update --init` after
cloning. The submodule tracks ModernUO `main`. Engine changes ModernSpawner needs go upstream as ModernUO
pull requests; until a PR merges, the submodule may be pinned to that PR's head commit (see
`dev-docs/modernuo-prerequisites.md`).

## Layout

- `Projects/ModernSpawner/` — the engine (namespace `Server.Engines.ModernSpawner`). Primary editing target.
- `Projects/ModernSpawner.Tests/` — xunit tests. Run after every change.
- `Projects/ModernSpawner.Benchmarks/` — BenchmarkDotNet; standalone, no ModernUO reference.
- `ModernUO/` — submodule. Do NOT edit files inside it from this repo. Engine changes go upstream as
  ModernUO pull requests from branches off `main` (see "ModernUO changes" below).
- `dev-docs/` — committed, **living** specs and design docs for this project. Only current documents live
  here; nothing historical.
- `docs/` — gitignored. Working notes (implementation guide, audits, reviews) and anything historical:
  `docs/archive/` holds the pre-rebuild documents, which are **not** authoritative — verify any claim there
  against the code. Proposals, decisions and history logs also go here, never in `dev-docs/`.

## Build and test

```sh
dotnet build ModernSpawner.slnx            # builds ModernUO Server/UOContent from the submodule too
dotnet test Projects/ModernSpawner.Tests   # 587 tests; the lifecycle collection boots a ModernUO test server
dotnet build -c Analyze                    # analyzers + Rules.ruleset
```

`Directory.Build.props` here applies only to `Projects/**`; the submodule keeps its own.
`TreatWarningsAsErrors` is on. The ModernUO serialization generator is referenced directly by
`ModernSpawner.csproj` (it is a private asset in ModernUO and does not flow through project references).
World-backed tests (`Projects/ModernSpawner.Tests/Core/ModernSpawnerLifecycleTests.cs`) share a
process-wide ModernUO bootstrap in `Projects/ModernSpawner.Tests/Fixtures/ModernSpawnerTestServer.cs`
and run in a `DisableParallelization` xunit collection; use that fixture for any new test that needs a
live spawner rather than standing up World/Core state by hand. The 12k-spawner trigger perf harness
(`Projects/ModernSpawner.Tests/Perf/TriggerPerfHarness.cs`) is part of that count but is a no-op unless
`MODERNSPAWNER_PERF=1` is set, so the default run stays sub-second.

## Rules

All ModernUO rules apply verbatim. Read and follow the **Code Audit Rules** in `ModernUO/CLAUDE.md` for every
`.cs` edit under `Projects/`. The ones that bite most often here:

- Single-threaded game loop: no threads, locks, or `Task.Run` touching game state (`ModernUO/dev-docs/threading-model.md`).
- Serialization: `[SerializationGenerator]` partial classes, `MigrateFrom` on version bumps, never edit legacy
  `Deserialize` (`ModernUO/dev-docs/serialization.md`). Version bumps need a schema JSON in `Projects/ModernSpawner/Migrations/`
  produced by `dotnet tool run ModernUOSchemaGenerator -- ModernSpawner.slnx`.
- No gump may render empty; validate before constructing (`ModernUO/dev-docs/gump-system.md`).
- Hot paths: no LINQ tier 3, no reflection, no `StringBuilder`, no `new List<T>` — use compiled expression
  accessors (`PropertyAccessorCache`), `ValueStringBuilder`, `PooledRefList<T>`, `STArrayPool<T>`.
- PropertyList literals are holes: `$"{"label"}\t{value}"`.
- Braces on all control flow; `_camelCase` private fields; switch expressions where readable.

ModernSpawner-specific:

- Scripts and expressions parse once and execute many times. Never re-parse a script per spawn tick.
- `ModernSpawner : Spawner` owns `List<ModernSpawnerEntry>` where `ModernSpawnerEntry : SpawnerEntry`; the
  base contract (`Entries`, `EntrySpan`, `CreateEntry`, `AddEntryCore`, …) runs over it and the lifecycle
  hooks (`OnStarted`, `OnSpawned`, `OnSpawnedDeath`, entry-aware `GetSpawnPosition`) carry the modern
  behaviour. Never add a parallel entry list or hide base members with `new`.
- Triggers are a state machine, not a bool: dispatch (`OnMovement`/`OnSpeech`/kill/skill) never spawns —
  a match calls `spawner.RequestCycle(trigger, in context)`, which only mutates spawner state (cooldown,
  refractory, the pending-cycle queue) and asks `TriggerSystem` for a drain; the outermost dispatch runs
  the cycle once it returns. All of that state (the gate set, the queue, cooldowns, kill counts, per-entry
  deadlines) lives on the spawner, so a tick never looks anything up. See `dev-docs/architecture.md` §5 for
  the tick-precedence and event transition table.
- Trigger definitions are `TriggerDefinition { Id, Text }` with a stable id generated once; mutate the list
  only through `AddTriggerDefinition`/`RemoveTriggerDefinitionAt`/`ClearTriggerDefinitions` — they assign
  the id and call `EnsureTriggersActive()` for you. Never call `TriggerSystem.ActivateTriggers` directly:
  it is not idempotent on its own, and within `Projects/ModernSpawner`, `ModernSpawner.EnsureTriggersActive`
  is its only caller (tests call it deliberately, to build the stale-registration cases teardown has to
  survive). Registration follows `TriggerActivated` and the definition list, never `Running` — `Start()`/
  `Stop()` only arm or disarm the timer.
- Proximity uses `Item.HandlesOnMovement`/`OnMovement`, speech uses `HandlesOnSpeech`, skill uses
  `Server.Misc.SkillEvents.SkillUsed` (players only). Extended (beyond 24-tile) proximity is clamped to
  `Core.GlobalMaxUpdateRange` with a warning.
- Per-event-trigger tokens (`wake:`, `mode:`, `when:`) are a suffix, recognised only after a grammar's full
  positional list — never write one where a positional field could be misread as a token name.

## ModernUO changes

When ModernSpawner needs an engine change (a `protected` member, a new hook), make it in the ModernUO repo on
a branch off `main` (use a worktree under `C:\Repositories\ModernUO`), open a ModernUO pull request, then pin
the submodule here to the PR head until it merges and to `main` afterwards. Record the change, its reason and
the PR number in `dev-docs/modernuo-prerequisites.md`. Do not fork engine code into this repo. Performance is
the overriding requirement for any ModernUO change: no new allocations or virtual dispatch on per-tick or
per-movement paths without a measurement.

## Dev-docs and skills

ModernUO reference docs live at `ModernUO/dev-docs/` (see the table in `ModernUO/CLAUDE.md`). Most relevant here:
`serialization.md`, `gump-system.md`, `timers.md`, `event-scheduler.md`, `commands-targeting.md`,
`string-handling.md`, `threading-model.md`, `property-lists.md`, `code-standards.md`.

Claude skills are **opt-in** and live in `ModernUO/dev-docs/claude-skills/`. They are not auto-loaded.
Enable one with:

```sh
mkdir -p .claude/skills/<name> && cp ModernUO/dev-docs/claude-skills/<name>.md .claude/skills/<name>/SKILL.md
```

`.claude/` is gitignored, so skills are enabled per clone. Offer the relevant skills when the task matches:

| Task | Skills |
|---|---|
| Any `.cs` edit | `modernuo-code-audit` (keep this one enabled) |
| Spawner / entry / serialization work | `modernuo-serialization`, `modernuo-content-patterns`, `modernuo-timers` |
| Gumps | `modernuo-gump-system`, `modernuo-commands-targeting` |
| Triggers, events, scheduling | `modernuo-events`, `modernuo-event-scheduler`, `modernuo-timers` |
| Scripting / expressions / string work | `modernuo-string-handling` |
| Commands | `modernuo-commands-targeting` |
| XmlSpawner import / migration | `migrate-from-runuo/migrate-foundation`, `migrate-from-runuo/migrate-systems` |

## Project docs

| Doc | Purpose |
|---|---|
| `dev-docs/product-spec.md` | What ModernSpawner is for, who uses it, feature scope |
| `dev-docs/architecture.md` | Subsystems, data flow, ModernUO integration points |
| `dev-docs/xmlspawner-migration.md` | Requirements for importing XmlSpawner files |
| `dev-docs/modernuo-prerequisites.md` | Engine changes needed upstream, with their PRs |
| `docs/archive/` (gitignored) | Historical, untrusted |
