using System;
using System.Collections.Generic;
using Server.Engines.ModernSpawner.Perf;
using Server.Engines.ModernSpawner.Scripting;
using Server.Engines.ModernSpawner.Scripting.Expressions;
using Server.Engines.ModernSpawner.Triggers;
using Server.Logging;

namespace Server.Engines.ModernSpawner;

/// <summary>
/// The D2 trigger state machine: the gate set, the bounded queue of trigger-bought cycles, the
/// acceptance order every event passes through, and the tick precedence that spends what the events
/// bought. Everything here is spawner state, so a tick compares fields and never looks anything up.
/// </summary>
public partial class ModernSpawner
{
    private static readonly ILogger TriggerLogger = LogFactory.GetLogger(typeof(ModernSpawner));

    /// <summary>
    /// How many cycles one spawner may drain inside a single outer dispatch, matching the product
    /// spec's script recursion limit. A cycle whose scripts raise further events queues them into the
    /// same drain list, so without this a self-feeding spawner would never let the loop finish.
    /// </summary>
    private const int MaxDrainsPerRound = 10;

    /// <summary>How long a failed placement parks an entry, when that is shorter than its min delay.</summary>
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The floor the timer is armed at. A deadline already in the past would otherwise arm at zero
    /// and spin the spawner through the timer wheel; one slice is soon enough for a catch-up tick.
    /// </summary>
    private static readonly TimeSpan MinimumArmDelay = TimeSpan.FromMilliseconds(100);

    // The set of open gates, by definition index. A bitmask covers the first 64 definitions - far more
    // than any real spawner carries - and the overflow list catches the rest, so the common case is a
    // single word compare with no allocation and no definition is silently ignored.
    private ulong _openGateBits;
    private List<int> _openGateOverflow;

    // Recomputed at every registration from the parsed set; malformed definitions count for neither.
    private int _gateCount;
    private int _eventCount;

    // Non-zero while this spawner has a live registration. Deleting invalidates it first, so a request
    // already in flight from a gate callback or a script is dropped rather than run against a
    // registration that no longer exists.
    private int _registrationGeneration;

    // Set while EnsureTriggersActive is (re)registering: a window that is already open reports its open
    // edge from inside Activate, and A3 says that edge only hydrates the set, it does not buy a cycle.
    private bool _hydratingGates;

    // Guards the cycle body against re-entry: an event arriving for a spawner that is already running a
    // cycle is queued, never nested.
    private bool _isRunningCycle;

    // Set while Respawn() is running the group bulk operation, so the before/after scripts run once for
    // the whole respawn instead of once per Spawn() inside it.
    private bool _inBulkRespawn;

    // MaxPendingCycles == 0 (XmlSpawner semantics) has no queue to put a slot in, so the accepted event
    // leaves this one-shot request behind instead. It never latches: the very next drain either spends
    // it or drops it, so it cannot fire on some arbitrary later one. The mobile is held by serial
    // rather than by reference, so a dropped request cannot root a deleted mobile.
    private bool _runNowRequested;
    private Serial _runNowMobile;

    // The mobile the cycle in flight belongs to, threaded into positioning (player_relative) and into
    // the script contexts the cycle runs. Saved and restored around every cycle.
    private Mobile _cycleTriggeringMobile;

    /// <summary>
    /// How many cycles this spawner has drained in the outer dispatch in progress. Reset by the trigger
    /// system when the drain list is exhausted.
    /// </summary>
    internal int DrainsThisRound { get; set; }

    /// <summary>
    /// Whether this spawner's gate lets a tick through: a spawner with no gates, or a deactivated one,
    /// is always open; otherwise at least one window must currently be open.
    /// </summary>
    public bool GateOpen =>
        !_triggerActivated || _gateCount == 0 || _openGateBits != 0 || _openGateOverflow is { Count: > 0 };

    /// <summary>How many of this spawner's registered triggers are gates (time windows).</summary>
    public int GateCount => _gateCount;

    /// <summary>
    /// How many of this spawner's registered triggers are event sources. When this is greater than zero
    /// the spawner never spawns on its own timer: each cycle has to be bought by an accepted event.
    /// </summary>
    public int EventCount => _eventCount;

    /// <summary>
    /// Whether a tick would be allowed to run a cycle right now (the design's tick authorization).
    /// </summary>
    public bool IsAuthorizedForTick =>
        Running && !Deleted && GateOpen && !IsFull && (_eventCount == 0 || PendingCycleCount > 0);

    /// <summary>
    /// Binds this spawner to a fresh trigger registration. Called by the trigger system once the parsed
    /// set exists and before any trigger is activated, because an activating gate runs spawner code.
    /// </summary>
    /// <param name="generation">The registration's generation number.</param>
    /// <param name="eventCount">How many parsed triggers are event sources.</param>
    /// <param name="gateCount">How many parsed triggers are gates.</param>
    internal void SetRegistration(int generation, int eventCount, int gateCount)
    {
        _registrationGeneration = generation;
        _eventCount = eventCount;
        _gateCount = gateCount;
    }

    /// <summary>
    /// Drops this spawner's registration bookkeeping. The gate set goes with it: gates are recomputed
    /// from the clock every time the definitions are parsed again.
    /// </summary>
    internal void ClearRegistration()
    {
        _registrationGeneration = 0;
        _eventCount = 0;
        _gateCount = 0;
        ClearGates();
    }

    /// <inheritdoc />
    /// <remarks>
    /// The D2 tick precedence, top to bottom, first match wins. A row that parks deliberately leaves
    /// the timer unarmed: the next state transition - a gate opening, an accepted event, a spawn being
    /// removed, <see cref="BaseSpawner.Start" /> - is what brings it back.
    /// </remarks>
    public override void OnTick()
    {
        // The whole precedence is measured, parked rows included: the D2 condition is about what a
        // tick costs when it does nothing, and a scope that only wrapped the cycle would miss that.
        using var _ = SpawnerMetrics.MeasureTick();

        // T0
        if (Deleted || !Running)
        {
            return;
        }

        var group = Group;

        // T1: base Group is "all dead, then respawn", so a populated pack parks until it is cleared.
        if (group)
        {
            Defrag();

            if (Spawned.Count > 0)
            {
                return;
            }
        }

        // T2, T3, T4
        if (!IsAuthorizedForTick)
        {
            return;
        }

        var now = Core.Now;

        // T5. A group respawn is a bulk operation with its own removal semantics, so per-entry
        // deadlines do not hold it back.
        if (!group && !HasDueEntry(now))
        {
            ArmAtEarliestDeadline(now);
            return;
        }

        // T6, and T1's bulk branch: the cycle source is the same, only the body differs, and
        // RunCycle picks the group body when base Group is set. A queued slot is spent whether or not
        // this spawner has event definitions: an external Trigger() queues one on a spawner that has
        // none, and nothing else would ever pop it.
        var slot = PopOldestSlot();
        if (!RunCycle(slot, false) && slot != null)
        {
            // The cycle could not run after all (a cycle is already in flight on this spawner), so the
            // slot goes back at the head of the queue rather than being spent on nothing.
            InsertIntoPendingSlots(0, slot);
            return;
        }

        if (group)
        {
            // Respawn arms the timer itself.
            return;
        }

        ArmAtEarliestDeadline(Core.Now);
    }

    /// <summary>
    /// Asks this spawner for one spawn cycle on behalf of a trigger that just matched. Dispatch never
    /// calls <see cref="Spawn" /> itself: it evaluates, calls this, and lets the outermost dispatch
    /// drain what was bought.
    /// </summary>
    /// <remarks>
    /// The acceptance order is fixed and every check runs before any state changes: registration live,
    /// the trigger matches, its cooldown has elapsed, the spawner-wide refractory has elapsed, the
    /// <c>when:</c> condition passes, and the queue has room (or, with
    /// <see cref="MaxPendingCycles" /> zero, the cycle can run right now). Only then do the cooldown,
    /// the refractory and the kill counter move, together.
    /// <para>
    /// Proximity, speech and skill triggers arrive here <em>already evaluated</em>: their dispatcher
    /// has to call <see cref="ITrigger.Evaluate" /> anyway to pick which of a spawner's triggers is
    /// firing, so this does not evaluate them a second time. Kill triggers are the exception - their
    /// match depends on a counter that lives on this spawner - and they are evaluated below.
    /// </para>
    /// </remarks>
    /// <param name="trigger">The trigger that matched.</param>
    /// <param name="generation">
    /// The <see cref="TriggerSet.Generation" /> the request was raised from. A request stamped with a
    /// registration this spawner has since replaced names triggers that are no longer bound to its
    /// state, and is dropped.
    /// </param>
    /// <param name="context">The event being dispatched.</param>
    /// <returns>True when the event was accepted and bought a cycle.</returns>
    internal bool RequestCycle(ITrigger trigger, int generation, in TriggerContext context)
    {
        if (trigger == null || Deleted)
        {
            return false;
        }

        // The registration stamp replaces a TriggerActivated test: a definition-backed request can
        // only carry a live generation while this spawner is registered, and an external one carries
        // whatever generation the spawner has right now - which is how a script or command Trigger()
        // is honoured on a spawner whose triggers are deactivated, or that has none at all.
        if (generation != _registrationGeneration)
        {
            return false;
        }

        // A kill trigger's threshold is spawner state, so the kill dispatch hands every kill that
        // passed the trigger's filters to this method and the evaluation happens here: a kill below
        // the threshold still counts, it just does not buy anything.
        var kill = trigger as KillTrigger;
        if (kill != null && !kill.Evaluate(in context))
        {
            kill.AdvanceKillCount(false);
            return false;
        }

        var now = Core.Now;
        var state = trigger.State;
        var wake = trigger.Wake;
        var latched = _maxPendingCycles > 0;

        // The acceptance gates, in the design's order and short-circuiting, so nothing past the first
        // refusal is even evaluated - `when:` in particular only builds a context once the cooldown
        // and the refractory have let the event through.
        var refused =
            state != null && now < state.CooldownUntil ||
            now < _refractoryUntil ||
            !WhenPasses(trigger, context.TriggeringMobile);

        if (!refused)
        {
            refused = latched
                // E6: the queue is the only thing between this event and a cycle, and it is full.
                ? PendingCycleCount >= _maxPendingCycles
                // MaxPendingCycles == 0 reproduces XmlSpawner: run now or drop, never latch. Nothing
                // can run now, so the event is refused rather than quietly eaten.
                : !GateOpen || IsFull || !(Running || (wake && Entries.Count > 0));
        }

        if (refused)
        {
            // A kill that reached the threshold and was then refused still counts: the cooldown, the
            // refractory and the queue gate the trigger firing, not the kills that build toward it.
            // Exactly one advance per dispatch, here or on acceptance below.
            kill?.AdvanceKillCount(false);
            return false;
        }

        // ---- accepted: every side effect of acceptance happens here, and only here ----
        if (state != null && trigger.Cooldown > TimeSpan.Zero)
        {
            state.CooldownUntil = now + trigger.Cooldown;
        }

        ApplyRefractory(now);
        kill?.AdvanceKillCount(true);

        var mobile = context.TriggeringMobile;
        var serial = mobile == null ? Serial.Zero : mobile.Serial;

        // E4: a wake trigger may start a stopped spawner.
        if (!Running && wake)
        {
            Start();
        }

        // E5, or an E4 whose Start() found no entries to run: hold the cycle if the queue allows it.
        if (!Running)
        {
            if (latched)
            {
                EnqueuePendingCycle(trigger.Id, serial);
            }

            return true;
        }

        // E3: mode:tick only arms the timer, so the cycle runs with the normal tick ordering.
        // Qualified: the spawner's own CycleMode property would otherwise win the name lookup here.
        if (trigger.Mode == Triggers.CycleMode.Tick && latched)
        {
            EnqueuePendingCycle(trigger.Id, serial);
            DoTimer(TimeSpan.Zero);
            return true;
        }

        if (latched)
        {
            EnqueuePendingCycle(trigger.Id, serial);
        }
        else
        {
            _runNowRequested = true;
            _runNowMobile = serial;
        }

        // E1: run it as soon as the dispatch that raised the event returns. E2 (gate closed or full)
        // leaves the slot waiting for T2 / T3 to clear instead.
        if (GateOpen && !IsFull)
        {
            TriggerSystem.Instance.RequestDrain(this);
        }

        return true;
    }

    /// <summary>
    /// Runs one bought cycle, called by the trigger system once the outermost dispatch has returned.
    /// Re-validates the spawner first (D1): a cycle bought a moment ago must not run into a spawner
    /// that has since been deleted, stopped, deactivated, filled up or had its window close.
    /// </summary>
    internal void DrainOne()
    {
        if (DrainsThisRound >= MaxDrainsPerRound)
        {
            // A run-now request never latches, not even past the budget: it is spent by the very next
            // drain or it is gone.
            ClearRunNow();

            if (DrainsThisRound == MaxDrainsPerRound)
            {
                DrainsThisRound++;
                TriggerLogger.Warning(
                    "Spawner {Serial} hit the {Limit} cycle recursion limit in one dispatch; the rest of its queued cycles wait for the next tick.",
                    Serial,
                    MaxDrainsPerRound
                );
            }

            // "The next tick" has to actually come: nothing else is going to arm the timer for a
            // spawner whose queue is what the budget refused.
            if (PendingCycleCount > 0)
            {
                DoTimer(TimeSpan.Zero);
            }

            return;
        }

        DrainsThisRound++;

        // D1: a deleted spawner discards what it was holding. Deactivation is the other discarding
        // case and it already emptied the queue on its way through EnsureTriggersActive (A4), so this
        // does not test the flag - an external Trigger() is a legitimate source on a spawner that has
        // no definitions and therefore no activation at all.
        if (Deleted)
        {
            ClearPendingCycles();
            ClearRunNow();
            return;
        }

        if (!Running || !GateOpen || IsFull)
        {
            // A run-now request never latches, so it is the one thing that is dropped here.
            ClearRunNow();
            return;
        }

        // Queued, never nested: the queued slots stay where they are and the cycle in flight drains
        // them when it exits. A run-now request is one-shot and never latches, so it is dropped here
        // rather than left to fire on some arbitrary later drain.
        if (_isRunningCycle)
        {
            ClearRunNow();
            return;
        }

        if (PendingCycleCount > 0)
        {
            var slot = PopOldestSlot();
            if (!RunCycle(slot, true))
            {
                // T1 on a base-Group spawner: the pack is not dead yet, so the cycle keeps waiting.
                InsertIntoPendingSlots(0, slot);
                return;
            }
        }
        else if (_runNowRequested)
        {
            var mobile = _runNowMobile;
            ClearRunNow();
            RunCycleCore(ResolveMobile(mobile), true);
        }

        // A cycle can buy more cycles through its scripts; RunCycleCore has already asked for the
        // follow-up drain, and it goes round this same bounded loop.
    }

    /// <summary>
    /// Triggers the spawner from a script or a command. This is an event source of its own - it counts
    /// even on a spawner with no event definitions at all - and it runs through the same acceptance
    /// path as any other event, so the queue bound and the refractory apply to it too.
    /// </summary>
    public void Trigger()
    {
        var context = TriggerContext.ForProximity(this, null);
        RequestCycle(ExternalTrigger.Instance, _registrationGeneration, in context);
    }

    /// <summary>
    /// Resets the trigger state: every queued cycle is dropped and any queued drain is cancelled (M4).
    /// Registrations, cooldowns and kill counters survive.
    /// </summary>
    public void ResetTrigger()
    {
        ClearPendingCycles();
        ClearRunNow();
        TriggerSystem.Instance.CancelDrain(this);
    }

    /// <summary>
    /// Called by a gate when its window opens, naming the gate by its position in
    /// <see cref="TriggerDefinitions" /> (-1 while unbound). Adding an id to an empty set is the open
    /// edge that authorizes the spawner again (G1); every other open edge only records the id (G2).
    /// </summary>
    /// <param name="definitionIndex">Position of the gate's definition, or -1.</param>
    public void OnGateOpened(int definitionIndex)
    {
        if (Deleted || !_triggerActivated)
        {
            return;
        }

        var wasOpen = _openGateBits != 0 || _openGateOverflow is { Count: > 0 };

        if (!AddGate(definitionIndex) || wasOpen)
        {
            // G2: already present, or the set was not empty, so nothing changes for the spawner.
            return;
        }

        // A3: registration hydrates the set from the clock without running G1's cycle.
        if (_hydratingGates || !Running)
        {
            return;
        }

        // G1. The window-open cycle is a timer cycle, not an event drain, so it honours per-entry
        // deadlines; only a mode:now event drain bypasses them.
        if (_eventCount == 0)
        {
            if (!RunCycleCore(null, false))
            {
                // A group spawner whose pack is still alive parks until removal, exactly as T1 does,
                // rather than arming a timer that would only park again.
                return;
            }
        }
        else if (PendingCycleCount > 0)
        {
            // Through the drain list rather than calling DrainOne directly: the recursion budget is
            // only reset when the list is exhausted, so a direct call would leak one drain per gate
            // opening and eventually stop the gate from draining at all.
            TriggerSystem.Instance.RequestDrain(this);
        }

        ArmAtEarliestDeadline(Core.Now);
    }

    /// <summary>
    /// Called by a gate when its window closes, naming the gate by its position in
    /// <see cref="TriggerDefinitions" /> (-1 while unbound). Live spawns and queued cycles both stay:
    /// D10 owns spawn lifetimes, and a cycle that was already bought is not refunded (G3, G4).
    /// </summary>
    /// <param name="definitionIndex">Position of the gate's definition, or -1.</param>
    public void OnGateClosed(int definitionIndex)
    {
        if (Deleted || !_triggerActivated)
        {
            return;
        }

        RemoveGate(definitionIndex);
    }

    /// <inheritdoc />
    /// <remarks>
    /// M3: the runtime state a restart would not carry either - queued cycles, cooldowns, the
    /// refractory and the kill counters - is cleared, while the registration stays, because a stopped
    /// registration is a live one.
    /// </remarks>
    public override void Reset()
    {
        base.Reset();

        ClearPendingCycles();
        ClearRunNow();
        TriggerSystem.Instance.CancelDrain(this);
        RefractoryUntil = default;

        var states = _triggerStateList;
        if (states != null)
        {
            for (var i = 0; i < states.Count; i++)
            {
                states[i].Reset();
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// M2: a bulk operation that bypasses the triggers entirely. The before and after scripts wrap the
    /// whole respawn rather than every <see cref="Spawn" /> inside it, so a group respawn runs them
    /// once (§7).
    /// </remarks>
    public override void Respawn()
    {
        if (_inBulkRespawn)
        {
            base.Respawn();
            return;
        }

        _inBulkRespawn = true;
        try
        {
            var beforeScript = OnBeforeSpawnScript;
            if (beforeScript?.IsValid == true)
            {
                var context = new ScriptContext(null, this)
                {
                    TriggeringMobile = _cycleTriggeringMobile
                };

                ScriptEngine.Instance.Execute(beforeScript, context);

                if (context.CancelSpawn)
                {
                    DoTimer();
                    return;
                }
            }

            base.Respawn();

            var afterScript = OnAfterSpawnScript;
            if (afterScript?.IsValid == true)
            {
                ScriptEngine.Instance.Execute(
                    afterScript,
                    new ScriptContext(null, this)
                    {
                        TriggeringMobile = _cycleTriggeringMobile
                    }
                );
            }
        }
        finally
        {
            _inBulkRespawn = false;
        }
    }

    /// <summary>
    /// Restores this spawner once the world has finished loading (L1/L2). Registration is deferred to
    /// here because a gate has to hydrate against the clock with every other spawner already read, and
    /// nothing on this path may spawn.
    /// </summary>
    internal void OnWorldLoaded()
    {
        if (Deleted)
        {
            return;
        }

        if (!_triggerActivated)
        {
            // L2: a deactivated spawner holds no cycles.
            ClearPendingCycles();
            return;
        }

        // L1: a save written before MaxPendingCycles was lowered can carry more slots than the bound
        // now allows.
        TrimPendingCycles();
        EnsureTriggersActive();
    }

    /// <summary>
    /// Runs the queued cycle in <paramref name="slot" />, resolving the mobile that bought it so
    /// positioning and scripts still see the player even though the dispatch is long gone.
    /// </summary>
    /// <param name="slot">The queued cycle, or null for a cycle no event named a mobile for.</param>
    /// <param name="bypassDeadlines">Whether the cycle ignores per-entry deadlines.</param>
    /// <returns>True when the cycle ran; false when the caller must keep the slot.</returns>
    private bool RunCycle(PendingCycle slot, bool bypassDeadlines) =>
        RunCycleCore(slot == null ? null : ResolveMobile(slot.TriggeringMobile), bypassDeadlines);

    /// <summary>
    /// Runs one cycle body with <paramref name="triggeringMobile" /> bound to it, then drains whatever
    /// that cycle's scripts bought.
    /// </summary>
    /// <remarks>
    /// Two things stop the cycle before it starts, and both mean "the caller keeps what it was going to
    /// spend": a cycle already in flight on this spawner (queued, never nested), and base
    /// <see cref="BaseSpawner.Group" /> with the pack still alive, because on a group spawner
    /// <em>every</em> cycle source - tick, event drain, window opening - is the same bulk respawn and
    /// T1 applies to all of them.
    /// </remarks>
    /// <param name="triggeringMobile">The mobile the cycle belongs to, or null.</param>
    /// <param name="bypassDeadlines">Whether the cycle ignores per-entry deadlines.</param>
    /// <returns>True when the cycle ran.</returns>
    private bool RunCycleCore(Mobile triggeringMobile, bool bypassDeadlines)
    {
        if (_isRunningCycle)
        {
            return false;
        }

        var group = Group;
        if (group)
        {
            Defrag();

            if (Spawned.Count > 0)
            {
                return false;
            }
        }

        var previous = _cycleTriggeringMobile;
        _cycleTriggeringMobile = triggeringMobile;
        _isRunningCycle = true;
        try
        {
            if (group)
            {
                // One bulk operation with the before/after scripts once around it (§7).
                Respawn();
            }
            else
            {
                SpawnCore(bypassDeadlines);
            }
        }
        finally
        {
            _isRunningCycle = false;
            _cycleTriggeringMobile = previous;
        }

        // Anything the cycle's scripts bought while it was running was queued rather than nested, so
        // it is drained now that the cycle has returned - through the same bounded drain list, which
        // is what keeps a self-feeding spawner from running away.
        if (PendingCycleCount > 0 || _runNowRequested)
        {
            TriggerSystem.Instance.RequestDrain(this);
        }

        return true;
    }

    /// <summary>The mobile a queued cycle named, or null when it is gone (or none was named).</summary>
    /// <param name="serial">The serial the slot carried.</param>
    /// <returns>The live mobile, or null.</returns>
    private static Mobile ResolveMobile(Serial serial) =>
        serial == Serial.Zero ? null : World.FindMobile(serial);

    /// <summary>Removes and returns the oldest queued cycle, or null when there is none.</summary>
    /// <returns>The oldest slot, or null.</returns>
    private PendingCycle PopOldestSlot()
    {
        if (_pendingSlots is not { Count: > 0 })
        {
            return null;
        }

        var slot = _pendingSlots[0];
        RemoveFromPendingSlotsAt(0);
        return slot;
    }

    /// <summary>Drops the one-shot run-now request left by an accepted event with no queue.</summary>
    private void ClearRunNow()
    {
        _runNowRequested = false;
        _runNowMobile = Serial.Zero;
    }

    /// <summary>Rolls and applies the spawner-wide lockout after an accepted event.</summary>
    /// <param name="now">The instant the event was accepted.</param>
    private void ApplyRefractory(DateTime now)
    {
        if (_refractoryMin <= TimeSpan.Zero && _refractoryMax <= TimeSpan.Zero)
        {
            return;
        }

        var lockout = _refractoryMax > _refractoryMin
            ? Utility.RandomMinMax(_refractoryMin, _refractoryMax)
            : _refractoryMin;

        if (lockout > TimeSpan.Zero)
        {
            RefractoryUntil = now + lockout;
        }
    }

    /// <summary>
    /// Evaluates a trigger's <c>when:</c> condition against the mobile that raised the event. The
    /// expression was compiled at parse time; only the context is built here, and only when a
    /// condition exists at all, so the common case allocates nothing.
    /// </summary>
    /// <param name="trigger">The trigger that matched.</param>
    /// <param name="mobile">The mobile that raised the event, or null.</param>
    /// <returns>True when the trigger carries no condition or the condition holds.</returns>
    private bool WhenPasses(ITrigger trigger, Mobile mobile)
    {
        var when = trigger.When;
        if (when == null)
        {
            return true;
        }

        var context = new ScriptContext(null, this)
        {
            TriggeringMobile = mobile
        };

        return ExpressionEngine.Instance.EvaluateBoolean(when, context);
    }

    #region The gate set

    /// <summary>Records a gate as open. Returns false when it was already in the set (G2, G4).</summary>
    /// <param name="definitionIndex">Position of the gate's definition.</param>
    /// <returns>True when the set actually grew.</returns>
    private bool AddGate(int definitionIndex)
    {
        if (definitionIndex < 0)
        {
            return false;
        }

        if (definitionIndex < 64)
        {
            var mask = 1UL << definitionIndex;
            if ((_openGateBits & mask) != 0)
            {
                return false;
            }

            _openGateBits |= mask;
            return true;
        }

        _openGateOverflow ??= [];
        if (_openGateOverflow.Contains(definitionIndex))
        {
            return false;
        }

        _openGateOverflow.Add(definitionIndex);
        return true;
    }

    /// <summary>Records a gate as closed. Returns false for a stale edge (G4).</summary>
    /// <param name="definitionIndex">Position of the gate's definition.</param>
    /// <returns>True when the set actually shrank.</returns>
    private bool RemoveGate(int definitionIndex)
    {
        if (definitionIndex < 0)
        {
            return false;
        }

        if (definitionIndex < 64)
        {
            var mask = 1UL << definitionIndex;
            if ((_openGateBits & mask) == 0)
            {
                return false;
            }

            _openGateBits &= ~mask;
            return true;
        }

        return _openGateOverflow?.Remove(definitionIndex) == true;
    }

    /// <summary>Empties the gate set; the next registration hydrates it from the clock again.</summary>
    private void ClearGates()
    {
        _openGateBits = 0;
        _openGateOverflow?.Clear();
    }

    #endregion

    #region Per-entry deadlines

    /// <summary>The subgroup entry selection is restricted to this cycle, or -1 for any.</summary>
    private int SelectionSubgroup => _cycleMode == SpawnCycleMode.Sequential ? _currentSubgroup : -1;

    /// <summary>Whether any entry this cycle could select is past its deadline (T5 / T6).</summary>
    /// <param name="now">The instant the tick is running at.</param>
    /// <returns>True when at least one selectable entry is due.</returns>
    private bool HasDueEntry(DateTime now)
    {
        var entries = _spawnEntries;
        if (entries == null)
        {
            return false;
        }

        var subgroup = SelectionSubgroup;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (IsEligible(entry, subgroup) && entry.IsDue(now))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Arms the timer for the next moment an entry could be selected: the earliest of the selectable
    /// entries' own deadlines, with an entry that carries none contributing the spawner's random
    /// delay rather than being ignored. A deadline already in the past arms at the floor, so a tick
    /// that could not spend a due entry comes back promptly instead of after a full delay.
    /// </summary>
    /// <remarks>
    /// Deliberately not clamped to <see cref="BaseSpawner.MaxDelay" />: a per-entry delay is allowed
    /// to be longer than the spawner's, and clamping would wake the spawner repeatedly for an entry
    /// that is not due for hours.
    /// </remarks>
    /// <param name="now">The instant the tick is running at.</param>
    private void ArmAtEarliestDeadline(DateTime now)
    {
        var entries = _spawnEntries;
        var subgroup = SelectionSubgroup;
        var found = false;
        var earliest = TimeSpan.Zero;
        var haveRandom = false;
        var randomDelay = TimeSpan.Zero;

        if (entries != null)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (!IsEligible(entry, subgroup))
                {
                    continue;
                }

                TimeSpan delay;
                if (entry.NextEligible == default)
                {
                    // No deadline of its own, so it is due whenever the spawner's own delay says so.
                    // Rolled once for the whole scan: a tick must not pay a roll per entry.
                    if (!haveRandom)
                    {
                        randomDelay = RandomSpawnerDelay();
                        haveRandom = true;
                    }

                    delay = randomDelay;
                }
                else
                {
                    delay = entry.NextEligible - now;
                }

                if (delay < MinimumArmDelay)
                {
                    delay = MinimumArmDelay;
                }

                if (!found || delay < earliest)
                {
                    earliest = delay;
                    found = true;
                }
            }
        }

        if (!found)
        {
            // Nothing selectable at all - every entry full or disabled. The base delay keeps the
            // spawner alive so it notices when that changes.
            DoTimer();
            return;
        }

        DoTimer(earliest);
    }

    /// <summary>One roll of the spawner's own delay, the same one <see cref="BaseSpawner.DoTimer()" /> uses.</summary>
    /// <returns>A delay between <see cref="BaseSpawner.MinDelay" /> and <see cref="BaseSpawner.MaxDelay" />.</returns>
    private TimeSpan RandomSpawnerDelay() =>
        TimeSpan.FromMilliseconds(
            Utility.RandomMinMax((long)MinDelay.TotalMilliseconds, (long)MaxDelay.TotalMilliseconds)
        );

    #endregion

    /// <summary>
    /// The synthetic trigger behind <see cref="ModernSpawner.Trigger" />: an event source that is not
    /// one of the spawner's definitions, so it carries no id, no state, no cooldown and no tokens. It
    /// holds nothing per call, so one instance serves every spawner.
    /// </summary>
    private sealed class ExternalTrigger : TriggerBase
    {
        /// <summary>The shared instance; external events carry no per-call state.</summary>
        public static ExternalTrigger Instance { get; } = new();

        /// <inheritdoc />
        public override string TriggerType => "external";

        /// <inheritdoc />
        public override TriggerKind Kind => TriggerKind.Event;

        /// <inheritdoc />
        public override bool Evaluate(in TriggerContext context) => true;

        /// <inheritdoc />
        public override string Serialize() => "external";
    }
}
