# ModernSpawner Perf Runbook

**Status:** Living document. Update when adding counters, commands, or scenarios.

## Purpose

How to measure ModernSpawner performance end-to-end on a live ModernUO server. Used during Phase 4 stabilization to capture a baseline before tagging `v0.1-baseline`, and on an as-needed basis after perf-sensitive changes (new cycle mode, new trigger type, hot-path refactor, ModernUO submodule bumps).

This document is opinionated: it assumes a fresh world + scripted startup as the QA harness. Other setups (loading a snapshot save, hand-built scenarios) work but are not the canonical runbook.

## What the harness provides

**Counters** — `ModernSpawner/Perf/SpawnerMetrics.cs` exposes opt-in counters for the hot paths:

| Bucket | Measures |
|---|---|
| `Spawn()` | The outer per-cycle override, including pre/post scripts and mode dispatch. |
| `SpawnFromEntry()` | Per-entity construction (reaches into `base.Spawn`, so includes Mobile allocation + placement). |
| `Defrag()` | Removes dead/stale spawned entities before selection. |
| Entry selection | Just the mode-specific eligible-entry scan + probability weighting. |
| Proximity dispatch | `TriggerSystem.OnMobileProximity` call path, measured at the spawner's `OnMovement`. |
| Entities spawned | Plain counter, no timing. |

Counters are zero-overhead when disabled: the measurement helpers return a `ref struct` scope whose `Dispose` is a no-op branch the JIT inlines away.

**Admin commands** — registered via `SpawnerPerfCommands.Configure`:

| Command | What it does |
|---|---|
| `[ModernSpawnerPerfSeed <count> [spacing]` | Creates `<count>` disposable spawners in a grid around you. Each has one `Rabbit` entry (`MaxCount = 1`) and a proximity trigger. Spacing defaults to 4 tiles; max count 50000. |
| `[ModernSpawnerPerfStart` | Enables counters, resets them. |
| `[ModernSpawnerPerfStop` | Disables counters. |
| `[ModernSpawnerPerfReset` | Zeros counters without changing enabled state. |
| `[ModernSpawnerPerfDump` | Prints current snapshot to your chat and the server log. |
| `[ModernSpawnerPerfChurn <percentPerTick> [intervalSeconds]` | Starts a timer that kills `<percentPerTick>%` of seeded rabbits every `<intervalSeconds>` (default 10s), forcing spawners to keep cycling. Use `[ModernSpawnerPerfChurn 0` to stop. |
| `[ModernSpawnerPerfClear` | Deletes all spawners created by `PerfSeed` and stops any running churn. |

### Important: spawners go idle when full

Each seeded spawner has `MaxCount = 1`. Once its rabbit is alive, `BaseSpawner.Count.setter` stops the per-spawner timer and the spawner is genuinely idle — zero work per tick — until something kills the rabbit. That's the correct production behavior, but it means **a "wait and measure" scenario doesn't measure anything** unless (a) you start counters before the burst, or (b) something is continuously killing rabbits. See the two Scenario A variants below.

## Canonical scenarios

Both scenarios start with a fresh world + a Developer account logged in to avoid unrelated load.

### Scenario A1 — initial spawn burst (10k spawners loading)

**Measures:** `Spawn()` + `SpawnFromEntry()` throughput during a respawn event. This is what server boot / world-load looks like when 10k spawners all fire their first cycle at roughly the same time.

**Ordering matters:** counters must be on *before* the seed spawners fire their first tick, otherwise the burst happens before measurement starts.

1. `[ModernSpawnerPerfStart` — enable counters, reset to zero.
2. `[ModernSpawnerPerfSeed 10000` — create the grid. First ticks begin firing within `MinDelay` (5 minutes default).
3. Wait at least `MaxDelay` + a minute so every spawner has had a chance to fire once (~11 minutes for defaults). During this window every spawner's first `Spawn()` call is counted.
4. `[ModernSpawnerPerfDump`.
5. `[ModernSpawnerPerfStop`, then `[ModernSpawnerPerfClear`.

**Expected shape:** `Spawn()` call count ≈ seeded count (10000), `FromEntry` count ≈ same, `Entities spawned` ≈ same. `Defrag` and `Select` fire once per spawner as part of `Spawn()`. `Proximity` near zero.

**Note:** `PerfDump` immediately after `PerfSeed` returns will typically show `Spawn = 0` because the first ticks haven't fired yet — wait the full `MaxDelay` before dumping.

### Scenario A2 — sustained churn (steady-state per-cycle cost)

**Measures:** average per-cycle cost at steady state, amortized over many respawns. This is the number you want to compare across implementations.

**Ordering matters:** seed first so spawners have something to churn, then start counters so the initial burst doesn't dominate the numbers.

1. `[ModernSpawnerPerfSeed 10000` — create the grid.
2. Wait `MaxDelay` + a minute so the initial burst completes (every spawner is now full, all timers stopped).
3. `[ModernSpawnerPerfStart` — enable counters with zero initial-burst noise.
4. `[ModernSpawnerPerfChurn 10 10` — every 10 seconds, kill 10% of seeded rabbits. This restarts ~1000 spawners' timers per churn tick, giving sustained per-cycle measurement.
5. Log out or `[go` far away so no proximity dispatch pollutes numbers.
6. Let it run for at least 15 real minutes (longer is better; JIT tier-up settles within the first minute, the rest is steady state).
7. Return, `[ModernSpawnerPerfChurn 0` to stop churn, `[ModernSpawnerPerfDump`.
8. `[ModernSpawnerPerfStop`, `[ModernSpawnerPerfClear`.

**Expected shape:** `Spawn()` avg us/call is the metric of interest. `FromEntry` avg reflects entity construction (mostly ModernUO's cost, not ModernSpawner's — don't regress-hunt there unless the ratio changes). `Defrag` avg should be small and stable. `Proximity` should be zero.

### Scenario B — player sweeping through full grid

**Measures:** proximity trigger dispatch throughput. This is the critical path when players move through densely spawned areas.

1. `[ModernSpawnerPerfSeed 10000` — create the grid.
2. Wait `MaxDelay` + a minute so the initial spawn burst completes and every spawner is full.
3. `[ModernSpawnerPerfStart` — counters start clean.
4. Walk or `[goto` across the grid at a steady pace. Aim for roughly 30 seconds of continuous movement through the spawner field. Moving in a straight line is enough — every step fires `OnMovement` for every nearby spawner with a proximity trigger.
5. `[ModernSpawnerPerfDump`.
6. `[ModernSpawnerPerfStop`, `[ModernSpawnerPerfClear`.

**Expected shape:** `Proximity dispatch` count rises with every step, each call should be microseconds. `Spawn()` count also rises as triggers fire and spawner cycles activate.

## Capturing and comparing runs

`PerfDump` logs one structured line to the server log via `LogFactory.GetLogger`. Grep server stdout / log file for `SpawnerMetrics snapshot:` to get a clean record. Paste the resulting line into `Docs/Perf-Baseline.md` (create on demand) alongside:

- Date
- Commit hash
- .NET runtime version (`dotnet --info | head -5`)
- Hardware summary (CPU model, cores, clock)
- Scenario label (A or B, duration)

When comparing two runs, compare **average time per call** across each bucket. Total time varies with scenario duration; averages are stable.

## Caveats and known gaps

- **Counters are enabled manually.** They default off so production saves pay no cost. Don't forget `[ModernSpawnerPerfStop` at the end.
- **First run after seeding is noisy.** ArrayPool warmup, JIT tiered compilation settle in within the first minute. For clean numbers, wait a minute after `PerfStart` before trusting averages.
- **No histogram.** We capture total time + call count, so we have averages but not p99. If tail latency matters, extend `SpawnerMetrics` to hold a reservoir or fixed-bucket histogram per counter.
- **Single-threaded assumption.** Counters use `Interlocked` for safety, but all meaningful updates happen on the game loop thread. Concurrent admin calls during a scenario will not corrupt counters but may slightly perturb measurements.
- **Mobile construction cost dominates `SpawnFromEntry`.** That cost is mostly ModernUO's, not ModernSpawner's. Don't chase it as a ModernSpawner regression unless the ratio changes.
- **Proximity dispatch count includes the extended-trigger short-circuit.** If `_hasExtendedProximityTriggers` is true for a spawner and the mobile is outside the bounds, the bucket doesn't tick. This is correct — it's measuring actual dispatch work, not the early-out.

## Extending the harness

To add a new counter:

1. In `SpawnerMetrics.cs`, add a paired `_xxxCalls` / `_xxxTicks` field plus a `MeasureXxx()` helper that returns a `SpawnerMetricsScope`.
2. Add the pair to the `Snapshot` struct + `Capture()` method + `Reset()`.
3. Wrap the hot path with `using var _ = SpawnerMetrics.MeasureXxx();` at the call site.
4. Update `PerfDump_OnCommand` to print the new bucket.
5. Update this runbook's counter table.

Keep the total number of buckets small (target under a dozen). Each bucket adds a tiny amount to the enabled-path cost and a row to the dump output — bloating either makes the harness worse for its primary use case.
