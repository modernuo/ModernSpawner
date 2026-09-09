# ModernSpawner

Advanced spawner engine for [ModernUO](https://github.com/modernuo/ModernUO), built to replace XmlSpawner.
.NET 10, single-threaded game loop, same rules and conventions as ModernUO.

**This repository requires ModernUO.** It is consumed as the `ModernUO/` git submodule, and the build,
style rules, dev-docs, and Claude skills all come from there. Run `git submodule update --init` after
cloning. The submodule tracks the `feat/spawner-stj-migration` support branch of ModernUO, which carries the
small engine changes ModernSpawner needs until they merge into ModernUO main.

## Layout

- `Projects/ModernSpawner/` — the engine (namespace `Server.Engines.ModernSpawner`). Primary editing target.
- `Projects/ModernSpawner.Tests/` — xunit tests. Run after every change.
- `Projects/ModernSpawner.Benchmarks/` — BenchmarkDotNet; standalone, no ModernUO reference.
- `ModernUO/` — submodule. Do NOT edit files inside it from this repo. Engine changes go on the support
  branch in the ModernUO repository (see "ModernUO changes" below).
- `dev-docs/` — committed specs and design docs for this project. `dev-docs/archive/` holds pre-rebuild
  documents that are historical and **not** authoritative; verify any claim there against the code.
- `docs/` — gitignored working notes (implementation guide, audits, scratch).

## Build and test

```sh
dotnet build ModernSpawner.slnx            # builds ModernUO Server/UOContent from the submodule too
dotnet test Projects/ModernSpawner.Tests   # 400+ tests, sub-second
dotnet build -c Analyze                    # analyzers + Rules.ruleset
```

`Directory.Build.props` here applies only to `Projects/**`; the submodule keeps its own.
`TreatWarningsAsErrors` is on. The ModernUO serialization generator is referenced directly by
`ModernSpawner.csproj` (it is a private asset in ModernUO and does not flow through project references).

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
- `ModernSpawnerEntry` is separate from `BaseSpawner.SpawnerEntry`. The spawner keeps its own `_spawnEntries`;
  the base `Entries` list should stay empty. Treat this as a known design tension (see the architecture spec).
- Triggers register through `TriggerSystem`; proximity uses `Item.HandlesOnMovement`/`OnMovement`, speech
  uses `HandlesOnSpeech`. Extended (beyond 24-tile) proximity is stubbed pending a ModernUO area-movement API.

## ModernUO changes

When ModernSpawner needs an engine change (a `protected` member, a new hook), make it in the ModernUO repo on
`feat/spawner-stj-migration`, commit there, then update the submodule pointer here. Record the change and its
reason in `dev-docs/modernuo-prerequisites.md`. Do not fork engine code into this repo.

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
| `dev-docs/modernuo-prerequisites.md` | Engine changes carried on the support branch |
| `dev-docs/archive/` | Historical, untrusted |
