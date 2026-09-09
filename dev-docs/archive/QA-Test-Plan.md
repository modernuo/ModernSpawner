# ModernSpawner QA Test Plan

**Status:** Living document. Update this when adding tests, introducing conventions, or discovering behavior that is deliberately not tested.

## Purpose

This document describes how ModernSpawner is tested, what is covered, what is not, and where new tests go. It exists so that:

- Contributors know which test project to extend and how to keep coverage consistent.
- Reviewers can tell at a glance whether a change has adequate tests.
- Deliberate gaps (world-dependent scenarios, known serialization quirks, deferred features) are recorded and don't silently become regressions.

## Test projects

| Project | Location | Purpose |
|---|---|---|
| `ModernSpawner.Tests` | `ModernSpawner.Tests/` | All unit tests. xunit 2.9, `net10.0`, `LangVersion: latest`. |
| `ModernSpawner.Benchmarks` | `ModernSpawner.Benchmarks/` | Micro-benchmarks via BenchmarkDotNet. Not part of the test run — executed manually when tuning hot paths. |

Tests depend on `ModernSpawner.csproj` (which in turn transitively references `ModernUO/Projects/Server/Server.csproj` and `ModernUO/Projects/UOContent/UOContent.csproj`). The ModernUO submodule must be present and on its shared-fixes branch (currently `feature/abstract-spawner-entries`).

## Conventions

**Framework:** xunit. One test class per unit under test. One `[Fact]` per distinct assertion-set. Use `[Theory]` + `[InlineData]` when the same logic is parameterised.

**Namespacing:** Mirror the folder structure under `Server.Engines.ModernSpawner.Tests.<Area>`. Keep tests alongside the module they cover (e.g., `Expressions/`, `Triggers/`, `Serialization/`, `Scheduling/`, `Loot/`, `Scripting/`, `Positioning/`, etc.).

**No live World.** The test host does not start a ModernUO server, so `World`, `Map.Felucca`, `Mobile` / `Item` construction, `EventScheduler.Shared`, and any path that requires a world tick are off-limits. Tests that would need those things are either:
- scoped down to pure logic we can exercise (e.g., `TimeWindowRecurrencePattern.GetNextOccurrence` is testable; `TimeWindowScheduledEvent.Schedule` is not because it registers with `EventScheduler.Shared`), or
- skipped with `[Fact(Skip = "reason")]` that references this section.

**Deterministic time.** Anything that touches time takes an explicit `DateTime` input (UTC, `DateTimeKind.Utc`). Do not call `DateTime.UtcNow` inside tests or inside the code-under-test paths that tests exercise.

**Property access.** Use `PropertyAccessorCache` in fixtures if a test needs to set properties dynamically. Do not reintroduce reflection in tests — it would diverge from production code paths.

**Round-trip tests for serialization.** For any JSON/YAML format, the minimum bar is one round-trip test per representative shape: serialize → deserialize → assert field parity. This catches missing `[JsonPropertyName]` / `[YamlMember]` attributes and missing converters before they reach production.

**Skipped tests document quirks.** When a behavior is known to be broken or intentionally deferred, `[Fact(Skip = "...")]` rather than deleting or inverting the test. The Skip reason must reference the tracking item in `ModernSpawner-Implementation-Plan.md` (Gaps section) so the gap stays visible.

## Running tests

```
# Full suite
dotnet test ModernSpawner.Tests/ModernSpawner.Tests.csproj

# Filter by area
dotnet test ModernSpawner.Tests/ModernSpawner.Tests.csproj --filter "FullyQualifiedName~Scheduling"
dotnet test ModernSpawner.Tests/ModernSpawner.Tests.csproj --filter "FullyQualifiedName~RoundTrip"

# After a ModernSpawner-only code change (skip ModernUO rebuild)
dotnet test ModernSpawner.Tests/ModernSpawner.Tests.csproj --no-build
```

Benchmarks:

```
dotnet run -c Release --project ModernSpawner.Benchmarks -- --filter *
```

In-game perf scenarios: see `Docs/Perf-Runbook.md`. These require a running ModernUO server and a Developer-level account. Counters live in `ModernSpawner/Perf/SpawnerMetrics.cs`; admin commands in `ModernSpawner/Perf/SpawnerPerfCommands.cs`.

## Coverage inventory

Snapshot as of 2026-04-19. Update the counts and checkmarks when you add or remove tests.

| Area | Tests | Covers | Notes |
|---|---|---|---|
| Expressions — Lexer | `Expressions/ExpressionLexerTests.cs` | Tokenization, operators, literals, edge cases | Part of 131+ expression-engine tests |
| Expressions — Parser | `Expressions/ExpressionParserTests.cs` | Operator precedence, AST shape, syntax errors | |
| Expressions — Engine | `Expressions/ExpressionEngineTests.cs` | Compilation, evaluation, caching | |
| Expressions — Built-ins | `Expressions/BuiltInFunctionsTests.cs` | `random`, `isNight`, `playersNearby`, etc. | |
| Scripting — Engine | `Scripting/ScriptEngineTests.cs` | Script execution, action dispatch | |
| Triggers — Parsing (existing) | `Triggers/TriggerParsingTests.cs` | Proximity / Speech / Skill parse + serialize round-trips | |
| Triggers — Kill | `Triggers/KillTriggerTests.cs` | Parse, serialize round-trip, filter `any`, required-kills clamp, ctor validation | Evaluate path needs live spawner — covered by in-game |
| Triggers — TimeOfDay | `Triggers/TimeOfDayTriggerTests.cs` | Parse, serialize, hour clamping, Night/Day mode flags | Evaluate path calls `Clock.GetTime` — needs Map |
| Triggers — GameTimeWindow | `Triggers/GameTimeWindowTriggerTests.cs` | Parse + round-trip, factory methods, `IsHourInWindow` (normal + overnight), `CalculateHoursUntil` (day-wrap) | `IsHourInWindow` / `CalculateHoursUntil` made `public static` for unit testing |
| Triggers — WallTimeWindow | `Triggers/WallTimeWindowTriggerTests.cs` | Parse + round-trip, factory methods (WeekendEvenings / Seasonal / Halloween / Christmas), TZ fallback to UTC, hour/min clamping | Evaluate path registers with EventScheduler — skipped |
| Positioning — Rules | `Positioning/PositioningRulesTests.cs` | Rule registry, case-insensitive lookup, `ParseRuleString`, every built-in has non-empty Description, Register overwrite + restore | `GetPosition` paths need Map — covered by in-game |
| Serialization — XmlSpawnerImporter | `Serialization/XmlSpawnerImporterTests.cs` | ServUO / Sno XML format parsing, error paths | `Map.Parse` returns null in tests; assertions are structural |
| Serialization — Spawner JSON round-trip | `Serialization/SpawnerJsonRoundTripTests.cs` | `SpawnerExportData` schema: fields, spawn area, triggers (proximity/speech/walltime/kill), entries, properties, conditions, hooks, scripts, options | 2 tests `Skip`ped for known `OptionsData` quirks |
| Serialization — Script YAML round-trip | `Serialization/ScriptYamlRoundTripTests.cs` | `ScriptExportData` schema: actions (spawn/broadcast/effect/sound/set), nested, conditions, subgroup, entries, all event handlers | |
| Serialization — Legacy flag mapping | `Serialization/XmlSpawnerLegacyMappingTests.cs` | `XmlSpawnerImporter.MapLegacyCycleMode` pure helper: `IsGroup` / `SequentialSpawn` → `SpawnCycleMode` | |
| Core — Cycle mode round-trip | `Serialization/SpawnerJsonRoundTripTests.cs` (Phase 3a additions) | `SpawnCycleMode.Sequential` / `Group` options, `CurrentSubgroup`, `SequentialResetTime/To`, `HoldSequence`, entry-level `Subgroup` | |
| Scheduling — Recurrence | `Scheduling/TimeWindowRecurrencePatternTests.cs` | `GetNextOccurrence` day/month filters, `None` → `All` normalization, default base pattern | |
| Scheduling — Event | `Scheduling/TimeWindowRecurrencePatternTests.cs` (same file) | `TimeWindowScheduledEvent` ctor validation, `OnEvent` alternation | `Schedule()` not exercised — requires `EventScheduler.Shared` |
| Loot — Template model | `Loot/LootTemplateTests.cs` | `LootTemplate` / `LootItem` / `LootTable` construction, registry, factory methods, JSON round-trip, file loading | `LootTemplate.ApplyTo(Mobile)` not tested — needs live Mobile + Backpack |

## Deliberate gaps (not tests we have to write)

These are documented on purpose. Don't add tests for them unless the environment changes:

- **Live spawn path.** `ModernSpawner.Spawn()` (and the per-mode selection in `SpawnRandomMode` / `SpawnSequentialMode` / `SpawnGroupMode`) calls `SpawnFromEntry` which constructs a `Mobile` via `base.Spawn` — requires a running server. Covered by in-game smoke testing during manual verification, not by xunit. Pure mapping logic is extracted into public static helpers (e.g. `XmlSpawnerImporter.MapLegacyCycleMode`) so it is unit-tested independently.
- **ModernSpawner construction in tests.** `new ModernSpawner()` fails early because `Item..ctor` → `DecayScheduler` needs an initialized `World`. State-machine methods (`GotoSubgroup`, `AdvanceSequence`, `ResetSequence`, `MaybeAutoResetSequence`) and the `SpawnBounds` / `HomeRange` interaction are therefore not unit-tested — they are verified in-game via `[ModernSpawnerPerfSeed`. When extracting new pure helpers, follow the `MapLegacyCycleMode` pattern: a `public static` method whose inputs and outputs are primitives or serializable data, and which lives beside the production call site.
- **`SpawnBounds` / `HomeRange` recursion regression.** The canonical reproducer is `[ModernSpawnerPerfSeed 1` — if the fix in `Core/ModernSpawner.cs` regresses, seeding a single spawner will `StackOverflowException`. A unit test can't reliably catch this because `StackOverflowException` is not a catchable exception in .NET. Treat PerfSeed smoke as the gate.
- **Gump rendering.** ModernUO gumps depend on `NetState` + `Mobile`. Gump structural tests would need heavy fakes. Not planned for xunit; covered by in-game testing.
- **EventScheduler integration end-to-end.** `BaseScheduledEvent.Schedule` registers with `EventScheduler.Shared`. We test the pure logic (`GetNextOccurrence`, `OnEvent` state machine) and skip the registration path.
- **`Map.Parse` / real-world coordinate resolution.** The test host has no maps loaded; tests assert structural parse correctness, not geographic correctness.
- **Network / packet / render-side concerns.** Out of ModernSpawner's surface area.

## Skipped tests

Tracked with `[Fact(Skip = "...")]`. Review this list when running the suite.

None currently. The previous entries (`Options_SmartPositioningFalse_ShouldRoundTrip` / `Options_MaxZDeltaZero_ShouldRoundTrip`) were closed in Phase 3d by dropping `WhenWritingDefault` on the two outlier fields and fixing a sign bug in `ExportSpawnerOptions`'s "all defaults → omit" short-circuit.

### Guidance for future `JsonIgnore` usage

`JsonIgnoreCondition.WhenWritingDefault` compares to `default(T)`, not the property's initializer. Two rules when applying it to `OptionsData`-style config:

1. If the initializer value equals `default(T)` (e.g. `bool false`, `int 0`, `enum member at 0`), `WhenWritingDefault` is safe.
2. If the initializer differs from `default(T)` (e.g. `SmartPositioning = true`, `MaxZDelta = 20`), either drop `WhenWritingDefault` entirely or switch to a nullable property with explicit presence semantics. Round-trip tests catch this — always add one when introducing a new option.

## Where to put new tests

| Adding... | Goes in | Name |
|---|---|---|
| New trigger type | `Triggers/<TypeName>Tests.cs` | One fixture per trigger class |
| New positioner | `Positioning/<RuleName>Tests.cs` | One fixture per positioner |
| New serialization format / converter | `Serialization/<Format>RoundTripTests.cs` | Follow the round-trip convention |
| New script built-in | `Expressions/BuiltInFunctionsTests.cs` | Add `[Theory]` cases |
| New gump (behavioral logic only) | `Gumps/<GumpName>Tests.cs` | Test only pure helpers; skip rendering |
| Subgroup / spawn-cycle logic (Phase 3a) | `Core/SpawnCycleModeTests.cs` | One fixture per mode |
| Benchmark for a hot path | `ModernSpawner.Benchmarks/<Name>Benchmarks.cs` | Not xunit — use BenchmarkDotNet `[Benchmark]` |

## Adding a new test — checklist

1. Pick the right folder (see table above).
2. Mirror the namespace of the code under test (prefix with `Server.Engines.ModernSpawner.Tests.`).
3. Use `[Fact]` for single cases, `[Theory]` + `[InlineData]` for parameterised cases.
4. Round-trip tests: serialize → deserialize → assert field parity.
5. If a test would need a live World, scope it down to a pure helper or add it to "Deliberate gaps" above.
6. Run locally: `dotnet test ModernSpawner.Tests/ModernSpawner.Tests.csproj --filter "FullyQualifiedName~<MyTest>"`.
7. Run the full suite before commit: `dotnet test ModernSpawner.Tests/ModernSpawner.Tests.csproj`.
8. Update the Coverage inventory table above when adding a new test file or area.

## Release gates

No release / baseline tag is cut without:

- `dotnet build Xmlspawner.sln` → 0 errors.
- `dotnet test ModernSpawner.Tests` → 0 failed (skipped tests are acceptable if they reference a tracked item).
- `dotnet build ModernUO.slnx` → 0 errors (sanity check that shared-fixes branch still compiles standalone).

Phase 4 stabilization adds one more gate: the end-to-end spawning benchmark (Phase 3c) must produce results within the documented perf envelope before tagging `v0.1-baseline`.
