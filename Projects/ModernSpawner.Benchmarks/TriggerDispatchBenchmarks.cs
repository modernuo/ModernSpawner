using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;

namespace ModernSpawner.Benchmarks;

/// <summary>
/// Mock trigger shapes for the D2 dispatch benchmarks. They mirror the real ones closely enough to be
/// honest about cost - a typed list walked by index, a virtual <c>Evaluate</c> per trigger, a bounded
/// <see cref="List{T}" /> of queued cycles, a reused drain list and a bitmask gate - without dragging
/// the ModernUO engine into this project, which deliberately has no reference to it.
/// </summary>
public abstract class MockTrigger
{
    /// <summary>Definition index, the position the gate bitmask keys off.</summary>
    public int DefinitionIndex { get; set; }

    /// <summary>The range a proximity-shaped trigger matches within.</summary>
    public int Range { get; set; } = 8;

    /// <summary>Evaluates the trigger against a point, the way dispatch does per event.</summary>
    /// <param name="x">Event X.</param>
    /// <param name="y">Event Y.</param>
    /// <returns>True when the trigger matches.</returns>
    public abstract bool Evaluate(int x, int y);
}

/// <summary>Stands in for <c>ProximityTrigger</c>: a range compare against the spawner location.</summary>
public sealed class MockProximityTrigger : MockTrigger
{
    /// <summary>Spawner X.</summary>
    public int SpawnerX { get; set; }

    /// <summary>Spawner Y.</summary>
    public int SpawnerY { get; set; }

    /// <inheritdoc />
    public override bool Evaluate(int x, int y)
    {
        var dx = x - SpawnerX;
        var dy = y - SpawnerY;

        if (dx < 0)
        {
            dx = -dx;
        }

        if (dy < 0)
        {
            dy = -dy;
        }

        return (dx > dy ? dx : dy) <= Range;
    }
}

/// <summary>Stands in for a non-proximity trigger class, so the typed lists are not all one type.</summary>
public sealed class MockOtherTrigger : MockTrigger
{
    /// <inheritdoc />
    public override bool Evaluate(int x, int y) => false;
}

/// <summary>
/// Stands in for <c>TriggerSet</c>: one object per spawner holding the per-class typed lists plus the
/// counts the tick guards read.
/// </summary>
public sealed class MockTriggerSet
{
    /// <summary>The registration generation a queued cycle is stamped with.</summary>
    public int Generation { get; set; }

    /// <summary>Every parsed trigger in definition order.</summary>
    public List<MockTrigger> All { get; } = [];

    /// <summary>The proximity triggers, walked by index on every movement event.</summary>
    public List<MockProximityTrigger> Proximity { get; } = [];

    /// <summary>The speech triggers.</summary>
    public List<MockOtherTrigger> Speech { get; } = [];

    /// <summary>The kill triggers.</summary>
    public List<MockOtherTrigger> Kill { get; } = [];

    /// <summary>The skill triggers.</summary>
    public List<MockOtherTrigger> Skill { get; } = [];

    /// <summary>The gates.</summary>
    public List<MockOtherTrigger> Gates { get; } = [];

    /// <summary>How many parsed triggers are event sources.</summary>
    public int EventCount { get; set; }

    /// <summary>How many parsed triggers are gates.</summary>
    public int GateCount { get; set; }
}

/// <summary>
/// Stands in for the spawner half of the D2 state machine: the bitmask gate, the bounded pending-slot
/// list and the drain bookkeeping the trigger system drives.
/// </summary>
public sealed class MockTriggerSpawner
{
    /// <summary>Spawner X, for the mock proximity evaluation.</summary>
    public int X { get; set; }

    /// <summary>Spawner Y, for the mock proximity evaluation.</summary>
    public int Y { get; set; }

    /// <summary>The open-gate bitmask: gates 0-63 by definition index.</summary>
    public ulong OpenGateBits { get; set; }

    /// <summary>How many gates this spawner carries.</summary>
    public int GateCount { get; set; }

    /// <summary>How many event sources this spawner carries.</summary>
    public int EventCount { get; set; }

    /// <summary>The queue bound; zero is the XmlSpawner run-now-or-drop shape.</summary>
    public int MaxPendingCycles { get; set; } = 1;

    /// <summary>Whether this spawner is already queued for a drain in the outer dispatch.</summary>
    public bool DrainRequested { get; set; }

    /// <summary>How many cycles this spawner has drained in the outer dispatch in progress.</summary>
    public int DrainsThisRound { get; set; }

    /// <summary>The queued cycles, allocated lazily exactly as the spawner does.</summary>
    public List<MockPendingCycle>? PendingSlots { get; set; }

    /// <summary>Number of queued cycles.</summary>
    public int PendingCycleCount => PendingSlots?.Count ?? 0;

    /// <summary>A spawner with no gates is always open; otherwise one window must be open.</summary>
    public bool GateOpen => GateCount == 0 || OpenGateBits != 0;
}

/// <summary>Stands in for <c>PendingCycle</c>: the queued slot allocated on acceptance.</summary>
/// <param name="spawner">The spawner that owns the slot.</param>
/// <param name="triggerId">The definition that bought the cycle.</param>
/// <param name="mobileSerial">Serial of the mobile that raised the event.</param>
public sealed class MockPendingCycle(MockTriggerSpawner spawner, Guid triggerId, int mobileSerial)
{
    /// <summary>The spawner that owns this slot.</summary>
    public MockTriggerSpawner Spawner { get; } = spawner;

    /// <summary>The definition that bought this cycle.</summary>
    public Guid TriggerId { get; } = triggerId;

    /// <summary>Serial of the mobile that raised the event.</summary>
    public int MobileSerial { get; } = mobileSerial;
}

/// <summary>
/// D2 condition 1: a movement, speech or kill dispatch does <em>one</em> dictionary lookup and then
/// walks a typed list, instead of the pre-D2 shape where the dispatcher probed one dictionary per
/// trigger class until it found the spawner.
///
/// N = 12,000 registered spawners, random key per invocation so the lookup pays a real cache miss.
/// </summary>
[RankColumn]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class TriggerDispatchLookupBenchmarks
{
    private const int SpawnerCount = 12_000;

    // Power-of-two probe order so advancing the cursor is a mask, not a modulo or an RNG call.
    private const int ProbeCount = 16_384;
    private const int ProbeMask = ProbeCount - 1;

    private MockTriggerSpawner[] _probes = null!;
    private int _cursor;

    // The D2 shape: one set per spawner in one dictionary.
    private Dictionary<MockTriggerSpawner, MockTriggerSet> _sets = null!;

    // The pre-D2 shape: one dictionary per trigger class, probed in turn.
    private Dictionary<MockTriggerSpawner, List<MockProximityTrigger>> _proximity = null!;
    private Dictionary<MockTriggerSpawner, List<MockOtherTrigger>> _speech = null!;
    private Dictionary<MockTriggerSpawner, List<MockOtherTrigger>> _kill = null!;
    private Dictionary<MockTriggerSpawner, List<MockOtherTrigger>> _skill = null!;
    private Dictionary<MockTriggerSpawner, List<MockOtherTrigger>> _gates = null!;
    private Dictionary<MockTriggerSpawner, List<MockTrigger>> _all = null!;

    /// <summary>Builds both registries over the same 12,000 spawners and the probe order.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var spawners = new MockTriggerSpawner[SpawnerCount];

        _sets = new Dictionary<MockTriggerSpawner, MockTriggerSet>(SpawnerCount);
        _proximity = new Dictionary<MockTriggerSpawner, List<MockProximityTrigger>>(SpawnerCount);
        _speech = new Dictionary<MockTriggerSpawner, List<MockOtherTrigger>>(SpawnerCount);
        _kill = new Dictionary<MockTriggerSpawner, List<MockOtherTrigger>>(SpawnerCount);
        _skill = new Dictionary<MockTriggerSpawner, List<MockOtherTrigger>>(SpawnerCount);
        _gates = new Dictionary<MockTriggerSpawner, List<MockOtherTrigger>>(SpawnerCount);
        _all = new Dictionary<MockTriggerSpawner, List<MockTrigger>>(SpawnerCount);

        var gridSide = (int)Math.Ceiling(Math.Sqrt(SpawnerCount));

        for (var i = 0; i < SpawnerCount; i++)
        {
            var spawner = new MockTriggerSpawner
            {
                X = 1000 + i / gridSide * 4,
                Y = 1000 + i % gridSide * 4,
                EventCount = 1,
                MaxPendingCycles = 1
            };

            var trigger = new MockProximityTrigger { SpawnerX = spawner.X, SpawnerY = spawner.Y };

            var set = new MockTriggerSet { Generation = i + 1, EventCount = 1 };
            set.All.Add(trigger);
            set.Proximity.Add(trigger);
            _sets[spawner] = set;

            // Same triggers, filed the pre-D2 way: only the class that has any gets a list, so the
            // probe chain has to ask the other five before it knows there is nothing there.
            _proximity[spawner] = [trigger];
            _all[spawner] = [trigger];

            spawners[i] = spawner;
        }

        // A fixed seed so the probe order is the same on main and on the branch.
        var random = new Random(20260912);
        _probes = new MockTriggerSpawner[ProbeCount];
        for (var i = 0; i < ProbeCount; i++)
        {
            _probes[i] = spawners[random.Next(SpawnerCount)];
        }
    }

    /// <summary>One lookup into the single set dictionary, then the typed proximity walk.</summary>
    /// <returns>How many triggers matched, so nothing is optimized away.</returns>
    [Benchmark(Baseline = true, Description = "One TriggerSet dictionary")]
    public int OneSetDictionary()
    {
        var spawner = _probes[_cursor = (_cursor + 1) & ProbeMask];

        if (!_sets.TryGetValue(spawner, out var set) || set.Proximity.Count == 0)
        {
            return 0;
        }

        var matched = 0;
        var triggers = set.Proximity;
        for (var i = 0; i < triggers.Count; i++)
        {
            if (triggers[i].Evaluate(spawner.X, spawner.Y))
            {
                matched++;
                break;
            }
        }

        return matched;
    }

    /// <summary>The pre-D2 shape: probe six dictionaries, then the same walk.</summary>
    /// <returns>How many triggers matched, so nothing is optimized away.</returns>
    [Benchmark(Description = "Six per-class dictionaries")]
    public int SixClassDictionaries()
    {
        var spawner = _probes[_cursor = (_cursor + 1) & ProbeMask];

        // What a dispatcher without a per-spawner set has to do: it does not know which classes this
        // spawner registered, so every class is asked.
        _all.TryGetValue(spawner, out _);
        _speech.TryGetValue(spawner, out _);
        _kill.TryGetValue(spawner, out _);
        _skill.TryGetValue(spawner, out _);
        _gates.TryGetValue(spawner, out _);

        if (!_proximity.TryGetValue(spawner, out var triggers) || triggers.Count == 0)
        {
            return 0;
        }

        var matched = 0;
        for (var i = 0; i < triggers.Count; i++)
        {
            if (triggers[i].Evaluate(spawner.X, spawner.Y))
            {
                matched++;
                break;
            }
        }

        return matched;
    }
}

/// <summary>
/// D2 condition 2: request and drain. An accepted event queues a bounded slot and asks the trigger
/// system for a drain; the outermost dispatch walks the drain list once and spends one slot per
/// spawner. The comparison is against the naive shape the design rejected - an unbounded queue and a
/// fresh drain list per round - so the allocation column shows what the bound and the reuse buy.
///
/// <c>Pending</c> is the queue depth (<c>MaxPendingCycles</c>) and also how many events each of the
/// 12,000 spawners raises in a round, so the accepted-request count scales with it.
/// </summary>
[RankColumn]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class TriggerDispatchRequestDrainBenchmarks
{
    private const int SpawnerCount = 12_000;

    private MockTriggerSpawner[] _spawners = null!;
    private Guid _triggerId;

    // Reused across drains exactly as TriggerSystem does: the outer dispatch clears it rather than
    // releasing it, so the steady state allocates nothing for the list itself.
    private readonly List<MockTriggerSpawner> _drainList = [];

    /// <summary>Queue depth, and how many events each spawner raises per round.</summary>
    [Params(1, 8, 64)]
    public int Pending { get; set; }

    /// <summary>Builds the 12,000 spawners with the parameterised queue bound.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _triggerId = Guid.NewGuid();
        _spawners = new MockTriggerSpawner[SpawnerCount];

        for (var i = 0; i < SpawnerCount; i++)
        {
            _spawners[i] = new MockTriggerSpawner
            {
                X = 1000 + i,
                Y = 1000,
                EventCount = 1,
                GateCount = 1,
                OpenGateBits = 1UL,
                MaxPendingCycles = Pending
            };
        }
    }

    /// <summary>
    /// The D2 shape: bounded queue, idempotent drain request, one reused drain list, one slot spent
    /// per spawner per drain. Leaves every spawner back at empty, so invocations do not accumulate.
    /// </summary>
    /// <returns>How many cycles were drained.</returns>
    [Benchmark(Baseline = true, Description = "Bounded queue + reused drain list")]
    public int RequestAndDrain()
    {
        var spawners = _spawners;

        for (var i = 0; i < spawners.Length; i++)
        {
            var spawner = spawners[i];

            for (var e = 0; e < Pending; e++)
            {
                // The acceptance order, shortened to the parts that cost anything per event: the gate
                // word compare, then the queue bound (E6), then the slot.
                if (!spawner.GateOpen || spawner.PendingCycleCount >= spawner.MaxPendingCycles)
                {
                    continue;
                }

                spawner.PendingSlots ??= [];
                spawner.PendingSlots.Add(new MockPendingCycle(spawner, _triggerId, i));

                if (!spawner.DrainRequested)
                {
                    spawner.DrainRequested = true;
                    _drainList.Add(spawner);
                }
            }
        }

        var drained = 0;

        // Count re-read: a cycle can append to the list while it runs.
        for (var i = 0; i < _drainList.Count; i++)
        {
            var spawner = _drainList[i];
            if (spawner == null)
            {
                continue;
            }

            spawner.DrainRequested = false;

            var slots = spawner.PendingSlots;
            if (slots is not { Count: > 0 })
            {
                continue;
            }

            // DrainOne spends exactly one slot per queued spawner per round.
            slots.RemoveAt(0);
            spawner.DrainsThisRound++;
            drained++;

            // The rest of the queue survives the round; this benchmark returns the spawners to empty
            // so repeated invocations measure the same work.
            slots.Clear();
            spawner.DrainsThisRound = 0;
        }

        _drainList.Clear();
        return drained;
    }

    /// <summary>
    /// The shape D2 rejected: no queue bound, and a fresh drain list every round. Same events, same
    /// spawners, so the delta is the bound and the reuse.
    /// </summary>
    /// <returns>How many cycles were drained.</returns>
    [Benchmark(Description = "Unbounded queue + fresh drain list")]
    public int RequestAndDrainUnbounded()
    {
        var spawners = _spawners;
        var drainList = new List<MockTriggerSpawner>();

        for (var i = 0; i < spawners.Length; i++)
        {
            var spawner = spawners[i];

            for (var e = 0; e < Pending; e++)
            {
                spawner.PendingSlots ??= [];
                spawner.PendingSlots.Add(new MockPendingCycle(spawner, _triggerId, i));
                drainList.Add(spawner);
            }
        }

        var drained = 0;

        for (var i = 0; i < drainList.Count; i++)
        {
            var spawner = drainList[i];
            var slots = spawner.PendingSlots;
            if (slots is not { Count: > 0 })
            {
                continue;
            }

            slots.RemoveAt(0);
            drained++;
        }

        for (var i = 0; i < spawners.Length; i++)
        {
            spawners[i].PendingSlots?.Clear();
        }

        return drained;
    }
}
