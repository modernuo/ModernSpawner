using System;
using System.Collections.Generic;
using Server.Collections;
using Server.Commands;
using Server.Logging;

namespace Server.Engines.ModernSpawner.Perf;

/// <remarks>
/// A note on seed + counters ordering: each seeded spawner has <c>MaxCount = 1</c>,
/// so after its first rabbit spawns the spawner is full and <c>BaseSpawner</c> stops
/// its timer. If you want to measure the *initial spawn burst*, call
/// <c>[ModernSpawnerPerfStart</c> BEFORE <c>PerfSeed</c> so the 10k first-cycle
/// <c>Spawn()</c> calls are captured. For *sustained churn* (steady-state cost across
/// many respawn cycles), run <c>[ModernSpawnerPerfChurn</c> after seeding so a
/// fraction of rabbits is killed at a fixed interval, forcing spawners to keep
/// cycling. See <c>Docs/Perf-Runbook.md</c>.
/// </remarks>

/// <summary>
/// Admin commands for driving ModernSpawner perf scenarios.
///
/// Typical run:
///   [ModernSpawnerPerfSeed 10000      - create 10k spawners in a grid around me
///   [ModernSpawnerPerfStart           - enable counters + reset
///   ...let the world run for N minutes, optionally walk around...
///   [ModernSpawnerPerfDump            - print counters to server log
///   [ModernSpawnerPerfStop            - disable counters
///   [ModernSpawnerPerfClear           - delete the seed spawners
///
/// See <c>Docs/Perf-Runbook.md</c> for the canonical scenario steps.
/// </summary>
public static class SpawnerPerfCommands
{
    private static readonly ILogger Logger = LogFactory.GetLogger(typeof(SpawnerPerfCommands));

    // Soft cap so a typo can't bring down the server trying to create 10 million items.
    private const int MaxSeedCount = 50_000;

    // Tracks spawners created by perfseed so perfclear can remove them without
    // risk of deleting real spawners.
    private static readonly List<ModernSpawner> _seeded = [];

    // Churn timer state — stopped by default. Started by PerfChurn with a non-zero
    // percent; PerfChurn 0 (or PerfClear) stops it.
    private static TimerExecutionToken _churnToken;
    private static double _churnPercent;
    private static readonly System.Random _churnRng = new();

    public static void Configure()
    {
        CommandSystem.Register("ModernSpawnerPerfSeed", AccessLevel.Developer, PerfSeed_OnCommand);
        CommandSystem.Register("ModernSpawnerPerfClear", AccessLevel.Developer, PerfClear_OnCommand);
        CommandSystem.Register("ModernSpawnerPerfStart", AccessLevel.Developer, PerfStart_OnCommand);
        CommandSystem.Register("ModernSpawnerPerfStop", AccessLevel.Developer, PerfStop_OnCommand);
        CommandSystem.Register("ModernSpawnerPerfDump", AccessLevel.Developer, PerfDump_OnCommand);
        CommandSystem.Register("ModernSpawnerPerfReset", AccessLevel.Developer, PerfReset_OnCommand);
        CommandSystem.Register("ModernSpawnerPerfChurn", AccessLevel.Developer, PerfChurn_OnCommand);
    }

    [Usage("ModernSpawnerPerfSeed <count> [spacing]")]
    [Description("Creates <count> disposable ModernSpawner instances in a grid around you. " +
                 "Each has one Rabbit entry and a proximity trigger so player sweeps exercise dispatch. " +
                 "Default spacing is 4 tiles. Max count is 50000.")]
    private static void PerfSeed_OnCommand(CommandEventArgs e)
    {
        if (e.Arguments.Length == 0 || !int.TryParse(e.Arguments[0], out var count) || count <= 0)
        {
            e.Mobile.SendMessage("Usage: [ModernSpawnerPerfSeed <count> [spacing]");
            return;
        }

        if (count > MaxSeedCount)
        {
            e.Mobile.SendMessage($"Refusing to seed more than {MaxSeedCount} spawners. Use a smaller count or raise MaxSeedCount.");
            return;
        }

        var spacing = 4;
        if (e.Arguments.Length > 1 && int.TryParse(e.Arguments[1], out var parsedSpacing) && parsedSpacing > 0)
        {
            spacing = parsedSpacing;
        }

        var map = e.Mobile.Map;
        if (map == null || map == Map.Internal)
        {
            e.Mobile.SendMessage("Cannot seed on the internal map. Move to a real map first.");
            return;
        }

        var gridSide = (int)Math.Ceiling(Math.Sqrt(count));
        var origin = e.Mobile.Location;
        var created = 0;

        for (var i = 0; i < gridSide && created < count; i++)
        {
            for (var j = 0; j < gridSide && created < count; j++)
            {
                var location = new Point3D(
                    origin.X + i * spacing,
                    origin.Y + j * spacing,
                    origin.Z);

                var spawner = new ModernSpawner
                {
                    Name = $"perfseed-{created}",
                    MinDelay = TimeSpan.FromMinutes(5),
                    MaxDelay = TimeSpan.FromMinutes(10),
                    HomeRange = 4,
                    Running = true
                };

                spawner.MoveToWorld(location, map);
                spawner.AddModernEntry(
                    creatureName: "Rabbit",
                    probability: 100,
                    maxCount: 1,
                    dotimer: false);

                // Add a proximity trigger so player sweeps exercise the dispatch path. The flag is set
                // after the definition exists: its setter registers whatever is in the list at that moment.
                spawner.AddTriggerDefinition("proximity:8:true");
                spawner.TriggerActivated = true;

                _seeded.Add(spawner);
                created++;
            }
        }

        e.Mobile.SendMessage($"Seeded {created} ModernSpawner instances in a {gridSide}x{gridSide} grid at spacing {spacing}.");
        Logger.Information("Perf seed: created {Count} spawners at {Location} on {Map}", created, origin, map);
    }

    [Usage("ModernSpawnerPerfClear")]
    [Description("Deletes ModernSpawner instances created by [ModernSpawnerPerfSeed.")]
    private static void PerfClear_OnCommand(CommandEventArgs e)
    {
        StopChurn();

        var removed = 0;
        foreach (var spawner in _seeded)
        {
            if (spawner?.Deleted == false)
            {
                spawner.Delete();
                removed++;
            }
        }

        _seeded.Clear();
        e.Mobile.SendMessage($"Removed {removed} seeded spawners.");
        Logger.Information("Perf seed: cleared {Count} spawners", removed);
    }

    [Usage("ModernSpawnerPerfChurn <percentPerTick> [intervalSeconds]")]
    [Description("Periodically kills <percentPerTick>% of spawned entities from the seed set " +
                 "so spawners keep cycling. Default interval is 10 seconds. " +
                 "Use [ModernSpawnerPerfChurn 0 to stop. Has no effect without an active seed.")]
    private static void PerfChurn_OnCommand(CommandEventArgs e)
    {
        if (e.Arguments.Length == 0 || !double.TryParse(e.Arguments[0], out var percent))
        {
            e.Mobile.SendMessage("Usage: [ModernSpawnerPerfChurn <percentPerTick> [intervalSeconds]");
            return;
        }

        if (percent <= 0)
        {
            StopChurn();
            e.Mobile.SendMessage("Churn stopped.");
            return;
        }

        if (percent > 100)
        {
            percent = 100;
        }

        var intervalSeconds = 10;
        if (e.Arguments.Length > 1 && int.TryParse(e.Arguments[1], out var parsedInterval) && parsedInterval > 0)
        {
            intervalSeconds = parsedInterval;
        }

        StopChurn();
        _churnPercent = percent;
        Timer.StartTimer(
            TimeSpan.FromSeconds(intervalSeconds),
            TimeSpan.FromSeconds(intervalSeconds),
            ChurnTick,
            out _churnToken);

        e.Mobile.SendMessage($"Churn started: killing {percent:F1}% of seeded rabbits every {intervalSeconds}s.");
        Logger.Information("Perf churn started: percent={Percent} interval={IntervalSeconds}s", percent, intervalSeconds);
    }

    private static void StopChurn()
    {
        _churnToken.Cancel();
        _churnPercent = 0;
    }

    private static void ChurnTick()
    {
        if (_seeded.Count == 0 || _churnPercent <= 0)
        {
            return;
        }

        var killed = 0;
        var scale = _churnPercent / 100.0;

        // Killing an entity routes through BaseSpawner.Remove, which mutates both the spawner
        // registry and the owning entry's Spawned list, so the candidates are snapshotted first.
        using var candidates = PooledRefList<ISpawnable>.Create();

        foreach (var spawner in _seeded)
        {
            if (spawner?.Deleted != false)
            {
                continue;
            }

            candidates.Clear();

            var entries = spawner.ModernEntries;
            for (var i = 0; i < entries.Count; i++)
            {
                var spawned = entries[i].Spawned;
                for (var j = 0; j < spawned.Count; j++)
                {
                    var entity = spawned[j];
                    if (entity != null)
                    {
                        candidates.Add(entity);
                    }
                }
            }

            for (var i = 0; i < candidates.Count; i++)
            {
                if (_churnRng.NextDouble() >= scale)
                {
                    continue;
                }

                if (candidates[i] is Mobile mobile && !mobile.Deleted && mobile.Alive)
                {
                    mobile.Kill();
                    killed++;
                }
                else if (candidates[i] is Item item && !item.Deleted)
                {
                    item.Delete();
                    killed++;
                }
            }
        }

        if (killed > 0)
        {
            Logger.Debug("Perf churn tick: killed {Killed} entities", killed);
        }
    }

    [Usage("ModernSpawnerPerfStart")]
    [Description("Enables SpawnerMetrics counters and resets them to zero.")]
    private static void PerfStart_OnCommand(CommandEventArgs e)
    {
        SpawnerMetrics.Reset();
        SpawnerMetrics.Enable();
        e.Mobile.SendMessage("SpawnerMetrics enabled (reset). Run your scenario, then [ModernSpawnerPerfDump.");
        Logger.Information("SpawnerMetrics enabled");
    }

    [Usage("ModernSpawnerPerfStop")]
    [Description("Disables SpawnerMetrics counters. Safe to call at any time.")]
    private static void PerfStop_OnCommand(CommandEventArgs e)
    {
        SpawnerMetrics.Disable();
        e.Mobile.SendMessage("SpawnerMetrics disabled.");
        Logger.Information("SpawnerMetrics disabled");
    }

    [Usage("ModernSpawnerPerfReset")]
    [Description("Zeros SpawnerMetrics counters without changing enabled/disabled state.")]
    private static void PerfReset_OnCommand(CommandEventArgs e)
    {
        SpawnerMetrics.Reset();
        e.Mobile.SendMessage("SpawnerMetrics counters reset.");
    }

    [Usage("ModernSpawnerPerfDump")]
    [Description("Dumps the current SpawnerMetrics snapshot to the server log and your chat.")]
    private static void PerfDump_OnCommand(CommandEventArgs e)
    {
        var snapshot = SpawnerMetrics.Capture();

        e.Mobile.SendMessage("--- ModernSpawner perf snapshot ---");
        e.Mobile.SendMessage($"Enabled: {SpawnerMetrics.Enabled}");
        e.Mobile.SendMessage($"Spawn():          {snapshot.SpawnCalls,8} calls, {snapshot.SpawnTotalUs,10:F1} us total, {snapshot.SpawnAvgUs,8:F2} us/call");
        e.Mobile.SendMessage($"SpawnFromEntry(): {snapshot.SpawnFromEntryCalls,8} calls, {snapshot.SpawnFromEntryTotalUs,10:F1} us total, {snapshot.SpawnFromEntryAvgUs,8:F2} us/call");
        e.Mobile.SendMessage($"Defrag():         {snapshot.DefragCalls,8} calls, {snapshot.DefragTotalUs,10:F1} us total, {snapshot.DefragAvgUs,8:F2} us/call");
        e.Mobile.SendMessage($"Entry selection:  {snapshot.SelectCalls,8} calls, {snapshot.SelectTotalUs,10:F1} us total, {snapshot.SelectAvgUs,8:F2} us/call");
        e.Mobile.SendMessage($"Proximity disp:   {snapshot.ProximityDispatchCalls,8} calls, {snapshot.ProximityDispatchTotalUs,10:F1} us total, {snapshot.ProximityDispatchAvgUs,8:F2} us/call");
        e.Mobile.SendMessage($"Entities spawned: {snapshot.EntitiesSpawned}");

        Logger.Information(
            "SpawnerMetrics snapshot (enabled={Enabled}): Spawn {SpawnCalls}/{SpawnAvgUs:F2}us, FromEntry {FromEntryCalls}/{FromEntryAvgUs:F2}us, " +
            "Defrag {DefragCalls}/{DefragAvgUs:F2}us, Select {SelectCalls}/{SelectAvgUs:F2}us, " +
            "Proximity {ProxCalls}/{ProxAvgUs:F2}us, Entities {EntitiesSpawned}",
            SpawnerMetrics.Enabled,
            snapshot.SpawnCalls, snapshot.SpawnAvgUs,
            snapshot.SpawnFromEntryCalls, snapshot.SpawnFromEntryAvgUs,
            snapshot.DefragCalls, snapshot.DefragAvgUs,
            snapshot.SelectCalls, snapshot.SelectAvgUs,
            snapshot.ProximityDispatchCalls, snapshot.ProximityDispatchAvgUs,
            snapshot.EntitiesSpawned);

        // Also emit a self-diagnosis hint if the user called PerfDump without PerfStart —
        // trying to debug a blank counter dump 20 minutes after a scenario is painful.
        if (!SpawnerMetrics.Enabled && snapshot.SpawnCalls == 0)
        {
            e.Mobile.SendMessage("(counters disabled — did you forget [ModernSpawnerPerfStart?)");
        }
    }
}
