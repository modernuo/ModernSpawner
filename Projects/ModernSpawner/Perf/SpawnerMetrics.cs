using System.Diagnostics;
using System.Threading;

namespace Server.Engines.ModernSpawner.Perf;

/// <summary>
/// Lightweight, opt-in counters for ModernSpawner hot paths.
///
/// Design goals:
/// - Zero overhead when <see cref="Enabled"/> is false (the measurement helpers
///   return a disposable struct whose Dispose body is a no-op; the JIT inlines
///   the whole thing away).
/// - Low overhead when enabled: two <c>Stopwatch.GetTimestamp()</c> reads
///   (~20ns each) plus one <c>Interlocked.Add</c> per measurement.
/// - Single-threaded game loop: counters use <c>Interlocked</c> for safety
///   against perf-dump calls from a separate admin thread, but the hot-path
///   increments are uncontended in practice.
/// - No allocations: measurement returns a <c>ref struct</c> scope.
///
/// Enable with <see cref="Enable"/> before a scenario run, dump with
/// <see cref="Snapshot"/>, reset with <see cref="Reset"/>. See
/// <c>Docs/Perf-Runbook.md</c> for scenario instructions.
/// </summary>
public static class SpawnerMetrics
{
    // Single cached ticks-per-second inverse so we convert once in the snapshot.
    private static readonly double TicksPerMicrosecond = Stopwatch.Frequency / 1_000_000.0;

    public static bool Enabled { get; private set; }

    public static void Enable() => Enabled = true;
    public static void Disable() => Enabled = false;

    // Each bucket tracks call count and accumulated ticks.
    // Keeping them in paired long fields (cache-line-aligned in practice) keeps
    // the hot path small. Add a bucket here + a corresponding Measure* method
    // when instrumenting a new hot path.

    private static long _spawnCalls;
    private static long _spawnTicks;

    private static long _spawnFromEntryCalls;
    private static long _spawnFromEntryTicks;

    private static long _defragCalls;
    private static long _defragTicks;

    private static long _selectCalls;
    private static long _selectTicks;

    private static long _proximityDispatchCalls;
    private static long _proximityDispatchTicks;

    private static long _entitiesSpawned;

    /// <summary>
    /// Opens a measurement scope for <see cref="ModernSpawner.Spawn"/>.
    /// </summary>
    public static SpawnerMetricsScope MeasureSpawn() =>
        new(Enabled, ref _spawnTicks, ref _spawnCalls);

    /// <summary>
    /// Opens a measurement scope for <see cref="ModernSpawner.SpawnFromEntry"/>.
    /// </summary>
    public static SpawnerMetricsScope MeasureSpawnFromEntry() =>
        new(Enabled, ref _spawnFromEntryTicks, ref _spawnFromEntryCalls);

    /// <summary>
    /// Opens a measurement scope for <see cref="ModernUO.Engines.Spawners.BaseSpawner.Defrag"/>.
    /// </summary>
    public static SpawnerMetricsScope MeasureDefrag() =>
        new(Enabled, ref _defragTicks, ref _defragCalls);

    /// <summary>
    /// Opens a measurement scope for per-cycle entry selection (all three cycle modes).
    /// </summary>
    public static SpawnerMetricsScope MeasureEntrySelection() =>
        new(Enabled, ref _selectTicks, ref _selectCalls);

    /// <summary>
    /// Opens a measurement scope for proximity trigger dispatch.
    /// </summary>
    public static SpawnerMetricsScope MeasureProximityDispatch() =>
        new(Enabled, ref _proximityDispatchTicks, ref _proximityDispatchCalls);

    /// <summary>
    /// Records a successful entity spawn. Not timed — cheap counter only.
    /// </summary>
    public static void RecordEntitySpawned()
    {
        if (Enabled)
        {
            Interlocked.Increment(ref _entitiesSpawned);
        }
    }

    /// <summary>
    /// Snapshot of counter state for logging / reporting. Reading is lock-free;
    /// values may be very slightly stale vs. a concurrent increment but are
    /// internally consistent for a single field (no tearing on 64-bit).
    /// </summary>
    public readonly struct Snapshot
    {
        public long SpawnCalls { get; init; }
        public double SpawnTotalUs { get; init; }
        public double SpawnAvgUs => SpawnCalls == 0 ? 0 : SpawnTotalUs / SpawnCalls;

        public long SpawnFromEntryCalls { get; init; }
        public double SpawnFromEntryTotalUs { get; init; }
        public double SpawnFromEntryAvgUs =>
            SpawnFromEntryCalls == 0 ? 0 : SpawnFromEntryTotalUs / SpawnFromEntryCalls;

        public long DefragCalls { get; init; }
        public double DefragTotalUs { get; init; }
        public double DefragAvgUs => DefragCalls == 0 ? 0 : DefragTotalUs / DefragCalls;

        public long SelectCalls { get; init; }
        public double SelectTotalUs { get; init; }
        public double SelectAvgUs => SelectCalls == 0 ? 0 : SelectTotalUs / SelectCalls;

        public long ProximityDispatchCalls { get; init; }
        public double ProximityDispatchTotalUs { get; init; }
        public double ProximityDispatchAvgUs =>
            ProximityDispatchCalls == 0 ? 0 : ProximityDispatchTotalUs / ProximityDispatchCalls;

        public long EntitiesSpawned { get; init; }
    }

    public static Snapshot Capture() =>
        new()
        {
            SpawnCalls = Volatile.Read(ref _spawnCalls),
            SpawnTotalUs = Volatile.Read(ref _spawnTicks) / TicksPerMicrosecond,
            SpawnFromEntryCalls = Volatile.Read(ref _spawnFromEntryCalls),
            SpawnFromEntryTotalUs = Volatile.Read(ref _spawnFromEntryTicks) / TicksPerMicrosecond,
            DefragCalls = Volatile.Read(ref _defragCalls),
            DefragTotalUs = Volatile.Read(ref _defragTicks) / TicksPerMicrosecond,
            SelectCalls = Volatile.Read(ref _selectCalls),
            SelectTotalUs = Volatile.Read(ref _selectTicks) / TicksPerMicrosecond,
            ProximityDispatchCalls = Volatile.Read(ref _proximityDispatchCalls),
            ProximityDispatchTotalUs = Volatile.Read(ref _proximityDispatchTicks) / TicksPerMicrosecond,
            EntitiesSpawned = Volatile.Read(ref _entitiesSpawned)
        };

    public static void Reset()
    {
        Interlocked.Exchange(ref _spawnCalls, 0);
        Interlocked.Exchange(ref _spawnTicks, 0);
        Interlocked.Exchange(ref _spawnFromEntryCalls, 0);
        Interlocked.Exchange(ref _spawnFromEntryTicks, 0);
        Interlocked.Exchange(ref _defragCalls, 0);
        Interlocked.Exchange(ref _defragTicks, 0);
        Interlocked.Exchange(ref _selectCalls, 0);
        Interlocked.Exchange(ref _selectTicks, 0);
        Interlocked.Exchange(ref _proximityDispatchCalls, 0);
        Interlocked.Exchange(ref _proximityDispatchTicks, 0);
        Interlocked.Exchange(ref _entitiesSpawned, 0);
    }
}

/// <summary>
/// Scope returned by <see cref="SpawnerMetrics"/>.Measure* helpers. <c>Dispose</c>
/// accumulates elapsed ticks and increments the call counter when enabled;
/// otherwise does nothing (JIT inlines the branch away).
/// </summary>
public ref struct SpawnerMetricsScope
{
    private readonly bool _enabled;
    private readonly long _startTicks;
    private readonly ref long _totalTicks;
    private readonly ref long _callCount;

    internal SpawnerMetricsScope(bool enabled, ref long totalTicks, ref long callCount)
    {
        _enabled = enabled;
        _totalTicks = ref totalTicks;
        _callCount = ref callCount;
        _startTicks = enabled ? Stopwatch.GetTimestamp() : 0;
    }

    public void Dispose()
    {
        if (!_enabled)
        {
            return;
        }

        var elapsed = Stopwatch.GetTimestamp() - _startTicks;
        Interlocked.Add(ref _totalTicks, elapsed);
        Interlocked.Increment(ref _callCount);
    }
}
