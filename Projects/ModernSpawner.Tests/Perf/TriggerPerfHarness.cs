using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Server.Engines.ModernSpawner.Perf;
using Server.Engines.ModernSpawner.Tests.Fixtures;
using Server.Mobiles;
using Xunit;
using Xunit.Abstractions;

namespace Server.Engines.ModernSpawner.Tests.Perf;

/// <summary>
/// The D2 performance gate (design §9), as a world-backed timing harness rather than a lap walked on a
/// live shard: 12,000 <see cref="ModernSpawner" />s on Felucca, a player walked along a fixed lap, and
/// a tick over the whole population with the gate closed and again with it open.
///
/// <para>
/// It is opt-in. The whole suite runs it as a no-op unless <c>MODERNSPAWNER_PERF=1</c> is set, so the
/// default run stays sub-second:
/// </para>
/// <code>
/// MODERNSPAWNER_PERF=1 dotnet test Projects/ModernSpawner.Tests \
///   --filter "FullyQualifiedName~TriggerPerfHarness" --logger "console;verbosity=detailed"
/// </code>
/// <para>
/// Results are printed and written to <c>perf-results.json</c> beside the test binaries, so a run on
/// <c>main</c> and a run on the branch can be diffed field by field.
/// </para>
/// </summary>
/// <remarks>
/// What each phase measures, and why the population is shaped the way it is:
/// <list type="bullet">
/// <item>
/// Each spawner carries a closed wall-time gate at definition index 0 and a proximity trigger at index
/// 1, plus one Rabbit entry capped at a single spawn. The gate is what makes a "gate closed" tick a
/// real state rather than a hypothetical, and while it is closed <c>DrainOne</c> refuses every queued
/// cycle, so no entity is ever spawned and the numbers are not swamped by creature construction.
/// The spawn path has its own instrumentation (<see cref="SpawnerMetrics.MeasureSpawn" />) and its own
/// scenario commands; this harness is about the per-event and per-tick overhead §9 bounds.
/// </item>
/// <item>
/// Movement dispatch is what the engine's sector pass would do: for every step, every spawner within
/// <see cref="Core.GlobalMaxUpdateRange" /> of the player gets <see cref="ModernSpawner.OnMovement" />.
/// The candidate set is computed from the grid index (<see cref="SpawnerPerfCommands.SeedGrid" />
/// documents the layout), never by scanning all 12,000. Setting the player's location is outside the
/// measured window: <c>Mobile.Location</c> is sector bookkeeping the engine would have paid anyway,
/// and it does not itself dispatch to items (<c>Mobile.Move</c> does).
/// </item>
/// <item>
/// The lap is measured twice. On the first pass each spawner the player reaches accepts one event and
/// allocates the single queued cycle its bound allows, so that pass is first contact. By the second
/// pass every one of them is holding that slot and the event is refused at the bound, which is the
/// steady state a shard actually sees - lookup, evaluate, refuse - and the path §9 requires to be
/// allocation-free. Both are reported: a regression could land in either.
/// </item>
/// <item>
/// Both tick phases run with the queues cleared, so each tick exits in the authorization row rather
/// than running a cycle: closed exits on the gate, open exits on "an event source with nothing queued".
/// A tick that reaches the cycle body is a spawn measurement, not a tick measurement.
/// </item>
/// </list>
/// </remarks>
[Collection("Sequential ModernSpawner Tests")]
[Trait("Category", "Perf")]
public class TriggerPerfHarness
{
    /// <summary>Set this to <c>1</c> to actually run the harness.</summary>
    private const string PerfEnvironmentVariable = "MODERNSPAWNER_PERF";

    private const int SpawnerCount = 12_000;
    private const int LapSteps = 2_000;

    // Enough to JIT every path before the first measured pass; the rest of the warming is the first
    // measured pass itself, which is deliberately the cold-state one.
    private const int JitWarmupSteps = 100;
    private const int Spacing = 4;

    // A December-only window while the suite clock sits at 2020-01-01 noon: closed, and it cannot open
    // under the harness. Definition index 0, which is the index OnGateOpened is given below.
    private const string ClosedGate = "wall_time_window:0:0:23:59:127:2048";

    // proximity:range:playersOnly:requireLos:cooldownSeconds:minAccess. Access level 0 is Player; the
    // positional field is parsed as an int, so the enum name is not a spelling it accepts.
    private const string Proximity = "proximity:8:true:false:0:0";

    private static readonly Point3D Origin = new(1000, 1000, 0);

    private static readonly double NanosecondsPerTick = 1_000_000_000.0 / Stopwatch.Frequency;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the harness.</summary>
    /// <param name="output">xUnit's output sink; the harness prints its table through it.</param>
    public TriggerPerfHarness(ITestOutputHelper output)
    {
        _output = output;
        ModernSpawnerTestServer.Initialize();
    }

    /// <summary>
    /// Seeds 12,000 spawners, walks the lap, ticks them twice and reports. A no-op without
    /// <c>MODERNSPAWNER_PERF=1</c>.
    /// </summary>
    [Fact]
    public void TwelveThousandSpawners_MovementDispatchAndTickCost()
    {
        if (Environment.GetEnvironmentVariable(PerfEnvironmentVariable) != "1")
        {
            _output.WriteLine(
                $"Skipped: the 12k trigger perf harness only runs with {PerfEnvironmentVariable}=1 set, " +
                "so the default suite stays fast.");
            return;
        }

        RunHarness();
    }

    private void RunHarness()
    {
        var spawners = new List<ModernSpawner>(SpawnerCount);
        PlayerMobile player = null;

        try
        {
            var seedStart = Stopwatch.GetTimestamp();
            var seeded = SpawnerPerfCommands.SeedGrid(
                Map.Felucca,
                Origin,
                SpawnerCount,
                Spacing,
                spawners,
                ClosedGate,
                Proximity);
            var seedMs = (Stopwatch.GetTimestamp() - seedStart) * NanosecondsPerTick / 1_000_000.0;

            Assert.Equal(SpawnerCount, seeded);
            Assert.Equal(SpawnerCount, spawners.Count);
            Assert.False(spawners[0].GateOpen);
            Assert.Equal(1, spawners[0].GateCount);
            Assert.Equal(1, spawners[0].EventCount);

            var gridSide = SpawnerPerfCommands.GridSide(SpawnerCount);
            var lap = BuildLap(LapSteps, gridSide);

            // Mobile.Player is not set by the constructor - production sets it at login - and the
            // proximity trigger filters on it, so the harness sets it the way the trigger tests do.
            player = new PlayerMobile { Name = "PerfWalker", Player = true };
            player.MoveToWorld(lap[0], Map.Felucca);

            // Warm up once, so nothing below is paying for JIT.
            WalkLap(spawners, player, lap, gridSide, JitWarmupSteps);
            TickAll(spawners);

            // First contact: every spawner the lap reaches accepts one event and allocates the single
            // queued cycle its bound allows.
            var dispatchFirstContact = WalkLap(spawners, player, lap, gridSide, LapSteps);

            // Steady state: the same lap again with every one of those slots still queued, so each
            // event is refused at the bound.
            var dispatchSteadyState = WalkLap(spawners, player, lap, gridSide, LapSteps);

            ClearQueues(spawners);
            var tickGateClosed = TickAll(spawners);

            for (var i = 0; i < spawners.Count; i++)
            {
                // Definition index 0 is the gate. With an event source registered and nothing queued
                // this only records the open edge and arms the timer; it runs no cycle.
                spawners[i].OnGateOpened(0);
            }

            Assert.True(spawners[0].GateOpen);

            ClearQueues(spawners);
            var tickGateOpen = TickAll(spawners);

            // One more pass with the counters on, to show what the SpawnerMetrics scope costs and to
            // prove MeasureTick is wired into OnTick.
            SpawnerMetrics.Reset();
            SpawnerMetrics.Enable();
            ClearQueues(spawners);
            var tickInstrumented = TickAll(spawners);
            var snapshot = SpawnerMetrics.Capture();
            SpawnerMetrics.Disable();
            SpawnerMetrics.Reset();

            Assert.True(dispatchSteadyState.Operations > 0);
            Assert.Equal(SpawnerCount, snapshot.TickCalls);

            Report(
                seedMs,
                dispatchFirstContact,
                dispatchSteadyState,
                tickGateClosed,
                tickGateOpen,
                tickInstrumented,
                snapshot);
        }
        finally
        {
            player?.Delete();

            for (var i = 0; i < spawners.Count; i++)
            {
                var spawner = spawners[i];
                if (spawner.Deleted)
                {
                    continue;
                }

                foreach (var spawned in new List<ISpawnable>(spawner.Spawned.Keys))
                {
                    spawned.Delete();
                }

                spawner.Delete();
            }

            spawners.Clear();
        }
    }

    /// <summary>
    /// The lap: a one-tile-per-step serpentine across the middle of the grid, so the candidate counts
    /// per step are representative rather than all edge cases, and identical on every run.
    /// </summary>
    /// <param name="steps">How many steps the lap has.</param>
    /// <param name="gridSide">The grid side the spawners were seeded in.</param>
    /// <returns>The lap, one point per step.</returns>
    private static Point3D[] BuildLap(int steps, int gridSide)
    {
        var extent = (gridSide - 1) * Spacing;
        var lap = new Point3D[steps];

        var x = 0;
        var y = extent / 2;
        var dx = 1;

        for (var i = 0; i < steps; i++)
        {
            lap[i] = new Point3D(Origin.X + x, Origin.Y + y, Origin.Z);

            x += dx;

            if (x > extent)
            {
                x = extent;
                dx = -1;
                y++;
            }
            else if (x < 0)
            {
                x = 0;
                dx = 1;
                y++;
            }

            if (y > extent)
            {
                y = 0;
            }
        }

        return lap;
    }

    private static PhaseResult WalkLap(
        List<ModernSpawner> spawners,
        Mobile player,
        Point3D[] lap,
        int gridSide,
        int steps
    )
    {
        var range = Core.GlobalMaxUpdateRange;
        var previous = player.Location;

        var dispatches = 0L;
        var elapsed = 0L;
        var allocated = 0L;

        for (var step = 0; step < steps; step++)
        {
            var point = lap[step];

            // Outside the measured window: this is the engine's own sector bookkeeping, and unlike
            // Mobile.Move it does not dispatch OnMovement to items, so it cannot double-count.
            player.Location = point;

            var bytesBefore = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();

            dispatches += DispatchStep(spawners, gridSide, range, player, previous);

            elapsed += Stopwatch.GetTimestamp() - start;
            allocated += GC.GetAllocatedBytesForCurrentThread() - bytesBefore;

            previous = point;
        }

        return new PhaseResult
        {
            Operations = dispatches,
            ElapsedTicks = elapsed,
            AllocatedBytes = allocated
        };
    }

    /// <summary>
    /// Dispatches one step to every spawner the engine's sector pass would reach, found by grid index.
    /// </summary>
    /// <returns>How many spawners were dispatched to.</returns>
    private static int DispatchStep(
        List<ModernSpawner> spawners,
        int gridSide,
        int range,
        Mobile player,
        Point3D oldLocation
    )
    {
        var location = player.Location;

        var minI = LowIndex(location.X - range - Origin.X);
        var maxI = HighIndex(location.X + range - Origin.X, gridSide);
        var minJ = LowIndex(location.Y - range - Origin.Y);
        var maxJ = HighIndex(location.Y + range - Origin.Y, gridSide);

        var dispatched = 0;

        for (var i = minI; i <= maxI; i++)
        {
            var rowStart = i * gridSide;

            for (var j = minJ; j <= maxJ; j++)
            {
                var index = rowStart + j;
                if (index >= spawners.Count)
                {
                    // The last grid row is only partly filled when the count is not a square.
                    break;
                }

                spawners[index].OnMovement(player, oldLocation);
                dispatched++;
            }
        }

        return dispatched;
    }

    /// <summary>The first grid index at or past <paramref name="offset" /> tiles from the origin.</summary>
    /// <param name="offset">Tile offset from the grid origin on one axis.</param>
    /// <returns>The index, never below zero.</returns>
    private static int LowIndex(int offset) => offset <= 0 ? 0 : (offset + Spacing - 1) / Spacing;

    /// <summary>The last grid index at or before <paramref name="offset" /> tiles from the origin.</summary>
    /// <param name="offset">Tile offset from the grid origin on one axis.</param>
    /// <param name="gridSide">The grid side, which bounds the index.</param>
    /// <returns>The index, or -1 when the offset is behind the origin.</returns>
    private static int HighIndex(int offset, int gridSide)
    {
        if (offset < 0)
        {
            return -1;
        }

        var index = offset / Spacing;
        return index >= gridSide ? gridSide - 1 : index;
    }

    private static PhaseResult TickAll(List<ModernSpawner> spawners)
    {
        var bytesBefore = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();

        for (var i = 0; i < spawners.Count; i++)
        {
            spawners[i].OnTick();
        }

        var elapsed = Stopwatch.GetTimestamp() - start;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;

        return new PhaseResult
        {
            Operations = spawners.Count,
            ElapsedTicks = elapsed,
            AllocatedBytes = allocated
        };
    }

    /// <summary>
    /// Empties every queue so a tick phase measures the authorization rows rather than a cycle.
    /// </summary>
    /// <param name="spawners">The seeded population.</param>
    private static void ClearQueues(List<ModernSpawner> spawners)
    {
        for (var i = 0; i < spawners.Count; i++)
        {
            spawners[i].ResetTrigger();
        }
    }

    private void Report(
        double seedMs,
        PhaseResult dispatchFirstContact,
        PhaseResult dispatchSteadyState,
        PhaseResult tickGateClosed,
        PhaseResult tickGateOpen,
        PhaseResult tickInstrumented,
        SpawnerMetrics.Snapshot snapshot
    )
    {
        _output.WriteLine("--- ModernSpawner D2 trigger perf harness ---");
        _output.WriteLine(
            $"spawners={SpawnerCount} lapSteps={LapSteps} spacing={Spacing} " +
            $"updateRange={Core.GlobalMaxUpdateRange} seedMs={Format(seedMs)}");
        _output.WriteLine(Line("dispatch, first contact", dispatchFirstContact));
        _output.WriteLine(Line("dispatch, steady state", dispatchSteadyState));
        _output.WriteLine(Line("tick, gate closed", tickGateClosed));
        _output.WriteLine(Line("tick, gate open", tickGateOpen));
        _output.WriteLine(
            Line("tick, instrumented", tickInstrumented) +
            $"  (SpawnerMetrics reports {Format(snapshot.TickAvgUs * 1000.0)} ns/call over {snapshot.TickCalls} calls)");

        var path = Path.Combine(AppContext.BaseDirectory, "perf-results.json");
        File.WriteAllText(
            path,
            BuildJson(
                seedMs,
                dispatchFirstContact,
                dispatchSteadyState,
                tickGateClosed,
                tickGateOpen,
                tickInstrumented,
                snapshot));
        _output.WriteLine($"results written to {path}");
    }

    private static string Line(string label, PhaseResult result) =>
        $"{label,-23}: {result.Operations,10} calls  {Format(result.NanosecondsPerOperation),10} ns/call  " +
        $"{Format(result.BytesPerOperation),8} bytes/call  ({Format(result.TotalMilliseconds)} ms total)";

    private static string BuildJson(
        double seedMs,
        PhaseResult dispatchFirstContact,
        PhaseResult dispatchSteadyState,
        PhaseResult tickGateClosed,
        PhaseResult tickGateOpen,
        PhaseResult tickInstrumented,
        SpawnerMetrics.Snapshot snapshot
    ) =>
        $$"""
          {
            "harness": "ModernSpawner.Tests/Perf/TriggerPerfHarness",
            "utc": "{{DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)}}",
            "spawnerCount": {{SpawnerCount}},
            "lapSteps": {{LapSteps}},
            "jitWarmupSteps": {{JitWarmupSteps}},
            "spacing": {{Spacing}},
            "globalMaxUpdateRange": {{Core.GlobalMaxUpdateRange}},
            "seedMilliseconds": {{Format(seedMs)}},
            "movementDispatchFirstContact": {{Json(dispatchFirstContact)}},
            "movementDispatchSteadyState": {{Json(dispatchSteadyState)}},
            "tickGateClosed": {{Json(tickGateClosed)}},
            "tickGateOpen": {{Json(tickGateOpen)}},
            "tickInstrumented": {{Json(tickInstrumented)}},
            "spawnerMetricsTickAvgNs": {{Format(snapshot.TickAvgUs * 1000.0)}},
            "spawnerMetricsTickCalls": {{snapshot.TickCalls}},
            "entitiesSpawned": {{snapshot.EntitiesSpawned}}
          }

          """;

    private static string Json(PhaseResult result) =>
        $$"""
          { "calls": {{result.Operations}}, "nsPerCall": {{Format(result.NanosecondsPerOperation)}}, "bytesPerCall": {{Format(result.BytesPerOperation)}}, "totalMs": {{Format(result.TotalMilliseconds)}}, "totalBytes": {{result.AllocatedBytes}} }
          """;

    private static string Format(double value) => value.ToString("F2", CultureInfo.InvariantCulture);

    /// <summary>One measured phase: how many operations, how long they took, what they allocated.</summary>
    private readonly struct PhaseResult
    {
        /// <summary>How many calls the phase made.</summary>
        public long Operations { get; init; }

        /// <summary><see cref="Stopwatch" /> ticks spent inside the measured calls.</summary>
        public long ElapsedTicks { get; init; }

        /// <summary>Managed bytes allocated on this thread inside the measured calls.</summary>
        public long AllocatedBytes { get; init; }

        /// <summary>Nanoseconds per call.</summary>
        public double NanosecondsPerOperation =>
            Operations == 0 ? 0 : ElapsedTicks * NanosecondsPerTick / Operations;

        /// <summary>Bytes allocated per call.</summary>
        public double BytesPerOperation => Operations == 0 ? 0 : (double)AllocatedBytes / Operations;

        /// <summary>Total milliseconds the phase spent in the measured calls.</summary>
        public double TotalMilliseconds => ElapsedTicks * NanosecondsPerTick / 1_000_000.0;
    }
}
