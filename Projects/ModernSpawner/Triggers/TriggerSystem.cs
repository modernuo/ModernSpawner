using System;
using System.Collections.Generic;
using Server.Buffers;
using Server.Logging;

namespace Server.Engines.ModernSpawner.Triggers;

/// <summary>
/// Default implementation of the trigger system.
/// Manages trigger registration, parsing, and event routing.
/// </summary>
/// <remarks>
/// Registration is one <see cref="TriggerSet" /> per spawner in one dictionary, so a dispatch does a
/// single lookup and then walks a typed list by index. Dispatch never runs a spawn cycle inline: a
/// matching trigger asks the spawner for a cycle, and the <em>outermost</em> dispatch drains the
/// requests once it returns, so a cycle can never re-enter an enumeration that is still running.
/// </remarks>
public class TriggerSystem : ITriggerSystem
{
    /// <summary>
    /// Singleton instance for convenience.
    /// </summary>
    public static TriggerSystem Instance { get; } = new();

    private static readonly ILogger Logger = LogFactory.GetLogger(typeof(TriggerSystem));

    private readonly Dictionary<string, Func<string, ITrigger>> _factories = new(StringComparer.OrdinalIgnoreCase);

    // One set per registered spawner, replacing the six per-type dictionaries.
    private readonly Dictionary<ModernSpawner, TriggerSet> _sets = new();

    // Skill attempts are dispatched server-wide, so skill triggers keep a candidate list per map rather
    // than making every attempt scan the whole registry. Maintained on registration and map change.
    private readonly Dictionary<Map, List<ModernSpawner>> _skillCandidates = new();

    // Reused across drains: the outer dispatch clears it rather than releasing it, so the steady state
    // allocates nothing.
    private readonly List<ModernSpawner> _drainList = [];

    private int _dispatchDepth;
    private bool _draining;
    private int _generation;

    /// <summary>Registers the built-in trigger factories.</summary>
    public TriggerSystem()
    {
        // Register built-in trigger types
        RegisterTriggerType("proximity", ProximityTrigger.Parse);
        RegisterTriggerType("speech", SpeechTrigger.Parse);
        RegisterTriggerType("kill", KillTrigger.Parse);
        RegisterTriggerType("skill", SkillTrigger.Parse);

        // Event-based time gates
        RegisterTriggerType("wall_time_window", WallTimeWindowTrigger.Parse);
        RegisterTriggerType("game_time_window", GameTimeWindowTrigger.Parse);

        // "timeofday" is retired as a class and survives only as an alias onto the game-time window, so
        // saved worlds, exports and XmlSpawner imports carrying the old text keep parsing.
        RegisterTriggerType("timeofday", GameTimeWindowTrigger.ParseLegacyTimeOfDay);
    }

    /// <inheritdoc />
    public void RegisterTriggerType(string triggerType, Func<string, ITrigger> factory)
    {
        _factories[triggerType] = factory;
    }

    /// <inheritdoc />
    public ITrigger ParseTrigger(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
        {
            return null;
        }

        // Get the trigger type from the definition (first part before ':')
        var colonIndex = definition.IndexOf(':');
        var triggerType = colonIndex > 0 ? definition[..colonIndex] : definition;

        if (_factories.TryGetValue(triggerType, out var factory))
        {
            try
            {
                var trigger = factory(definition);
                if (trigger == null)
                {
                    // A registered factory returning null means a malformed definition, which used to be
                    // swallowed: the spawner silently lost the trigger with nothing in the log.
                    Logger.Warning("Malformed {TriggerType} trigger definition: {Definition}", triggerType, definition);
                }

                return trigger;
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Failed to parse trigger definition: {Definition}", definition);
                return null;
            }
        }

        Logger.Warning("Unknown trigger type: {TriggerType}", triggerType);
        return null;
    }

    /// <inheritdoc />
    public TriggerSet GetSet(ModernSpawner spawner)
    {
        if (spawner == null)
        {
            return null;
        }

        return _sets.GetValueOrDefault(spawner);
    }

    /// <inheritdoc />
    public void ActivateTriggers(ModernSpawner spawner)
    {
        // Parse and activate all trigger definitions
        var definitions = spawner?.TriggerDefinitions;
        if (definitions == null || definitions.Count == 0)
        {
            return;
        }

        var set = new TriggerSet { Generation = ++_generation };

        for (var i = 0; i < definitions.Count; i++)
        {
            var definition = definitions[i];
            var trigger = ParseTrigger(definition.Text);
            if (trigger == null)
            {
                continue;
            }

            // Bind identity before anything can fire: a gate's Activate can open its window inside this
            // call, and it reports that edge by definition index.
            trigger.Id = definition.Id;
            trigger.DefinitionIndex = i;
            trigger.State = spawner.GetOrCreateTriggerState(definition.Id);

            set.Add(trigger);
        }

        if (set.All.Count == 0)
        {
            return;
        }

        // Register before activating: a gate that opens during Activate runs spawner code, and that code
        // must see a consistent registry.
        _sets[spawner] = set;
        spawner.SetHasProximityTriggers(set.Proximity.Count > 0);
        spawner.SetHasSpeechTriggers(set.Speech.Count > 0);

        if (set.Skill.Count > 0)
        {
            FileSkillCandidate(spawner, set);
        }

        var triggers = set.All;
        for (var i = 0; i < triggers.Count; i++)
        {
            // Activating one trigger can re-enter and replace this whole registration (a gate opening
            // runs a cycle, a script deletes or reconfigures the spawner). Once that has happened the
            // rest of this batch belongs to a registration that no longer exists.
            if (!_sets.TryGetValue(spawner, out var current) || current != set)
            {
                return;
            }

            triggers[i].Activate(spawner);
        }
    }

    /// <summary>
    /// Whether <paramref name="spawner" /> currently has triggers registered with this system, i.e. whether
    /// <see cref="ActivateTriggers" /> has run for it without a matching <see cref="DeactivateTriggers" />.
    /// </summary>
    /// <param name="spawner">The spawner to check.</param>
    /// <returns>True when a set is registered for it.</returns>
    internal bool IsRegistered(ModernSpawner spawner) => spawner != null && _sets.ContainsKey(spawner);

    /// <inheritdoc />
    public void DeactivateTriggers(ModernSpawner spawner)
    {
        if (spawner == null || !_sets.Remove(spawner, out var set))
        {
            return;
        }

        UnfileSkillCandidate(spawner, set);

        var triggers = set.All;
        for (var i = 0; i < triggers.Count; i++)
        {
            triggers[i].Deactivate();
        }

        // The set is not cleared: a dispatch further up the stack may still hold one of its typed lists,
        // and dropping it from the dictionary is enough to retire it.
        spawner.SetHasProximityTriggers(false);
        spawner.SetHasSpeechTriggers(false);
    }

    private void FileSkillCandidate(ModernSpawner spawner, TriggerSet set)
    {
        var map = spawner.Map;
        if (map == null || map == Map.Internal)
        {
            return;
        }

        if (!_skillCandidates.TryGetValue(map, out var candidates))
        {
            candidates = [];
            _skillCandidates[map] = candidates;
        }

        if (!candidates.Contains(spawner))
        {
            candidates.Add(spawner);
        }

        set.SkillMap = map;
    }

    private void UnfileSkillCandidate(ModernSpawner spawner, TriggerSet set)
    {
        var map = set.SkillMap;
        if (map == null || !_skillCandidates.TryGetValue(map, out var candidates))
        {
            return;
        }

        candidates.Remove(spawner);
        if (candidates.Count == 0)
        {
            _skillCandidates.Remove(map);
        }

        set.SkillMap = null;
    }

    /// <summary>
    /// Re-files a registered spawner's skill triggers after its map changed, so skill dispatch never has
    /// to scan the whole registry to find the candidates on one map.
    /// </summary>
    /// <param name="spawner">The spawner whose map changed.</param>
    internal void OnSpawnerMapChanged(ModernSpawner spawner)
    {
        if (spawner == null || !_sets.TryGetValue(spawner, out var set) || set.Skill.Count == 0)
        {
            return;
        }

        if (set.SkillMap == spawner.Map)
        {
            return;
        }

        UnfileSkillCandidate(spawner, set);
        FileSkillCandidate(spawner, set);
    }

    /// <summary>
    /// Queues <paramref name="spawner" /> for a drain once the outermost dispatch returns. Idempotent
    /// within one outer dispatch: a spawner already queued is not queued twice.
    /// </summary>
    /// <param name="spawner">The spawner that wants to run a queued cycle.</param>
    public void RequestDrain(ModernSpawner spawner)
    {
        if (spawner == null || spawner.Deleted || spawner.DrainRequested)
        {
            return;
        }

        spawner.DrainRequested = true;
        _drainList.Add(spawner);
    }

    /// <summary>
    /// Runs every queued drain. Only ever called from the outermost dispatch, so a cycle that raises
    /// further events queues into the same list and is picked up by this same loop rather than nesting.
    /// </summary>
    private void DrainAll()
    {
        if (_drainList.Count == 0)
        {
            return;
        }

        _draining = true;
        try
        {
            // Count is re-read: a cycle can append to the list while it runs.
            for (var i = 0; i < _drainList.Count; i++)
            {
                var spawner = _drainList[i];
                spawner.DrainRequested = false;

                if (spawner.Deleted)
                {
                    continue;
                }

                spawner.DrainOne();
            }
        }
        finally
        {
            for (var i = 0; i < _drainList.Count; i++)
            {
                _drainList[i].DrainRequested = false;
            }

            _drainList.Clear();
            _draining = false;
        }
    }

    private void EndDispatch()
    {
        // A drain runs cycles, and a cycle can raise events that come back through a dispatch entry
        // point; those must not start a second drain from inside the first.
        if (--_dispatchDepth == 0 && !_draining)
        {
            DrainAll();
        }
    }

    /// <inheritdoc />
    public void OnSkillUse(Mobile mobile, Skill skill, bool success)
    {
        if (mobile == null || skill == null || mobile.Map == null || mobile.Map == Map.Internal)
        {
            return;
        }

        if (!_skillCandidates.TryGetValue(mobile.Map, out var candidates) || candidates.Count == 0)
        {
            return;
        }

        var skillName = skill.SkillName;

        // Skill.Value re-derives the stat-scaled value plus the racial bonus on every read, so it is
        // read once for the whole dispatch.
        var skillValue = skill.Value;

        // A cycle can delete or re-register a spawner that carries a skill trigger, which would
        // invalidate this list mid-loop; dispatch off a pooled snapshot instead.
        var pool = STArrayPool<ModernSpawner>.Shared;
        var snapshot = pool.Rent(candidates.Count);
        var taken = candidates.Count;

        _dispatchDepth++;
        try
        {
            for (var i = 0; i < taken; i++)
            {
                snapshot[i] = candidates[i];
            }

            for (var s = 0; s < taken; s++)
            {
                var spawner = snapshot[s];

                // The snapshot can name a spawner an earlier iteration of this dispatch deleted or
                // unregistered.
                if (spawner.Deleted || spawner.Map != mobile.Map || !_sets.TryGetValue(spawner, out var set))
                {
                    continue;
                }

                var triggers = set.Skill;

                // Cheap pre-scan: most spawners hold triggers for other skills, and those must not pay
                // for a context. Indexed loops so this path has no enumerator and no closure.
                var firstMatch = -1;
                for (var i = 0; i < triggers.Count; i++)
                {
                    if (triggers[i].MatchesSkill(skillName))
                    {
                        firstMatch = i;
                        break;
                    }
                }

                if (firstMatch < 0)
                {
                    continue;
                }

                var context = TriggerContext.ForSkill(spawner, mobile, skillName, skillValue, success);

                // Everything before firstMatch is already known not to match this skill.
                for (var i = firstMatch; i < triggers.Count; i++)
                {
                    var trigger = triggers[i];
                    if (trigger.MatchesSkill(skillName) && trigger.Evaluate(in context))
                    {
                        spawner.RequestCycle(trigger, in context);
                        break; // Only one cycle per spawner per skill use
                    }
                }
            }
        }
        finally
        {
            // Clear only the entries written: the buffer outlives this call inside the pool and holds
            // spawner references, but the bucket-sized array can be far larger than `taken`.
            snapshot.AsSpan(0, taken).Clear();
            pool.Return(snapshot);
            EndDispatch();
        }
    }

    /// <inheritdoc />
    public void OnMobileProximity(Mobile mobile, Point3D location, Map map, ModernSpawner spawner)
    {
        if (mobile == null || map == null || map == Map.Internal || spawner == null)
        {
            return;
        }

        if (!_sets.TryGetValue(spawner, out var set) || set.Proximity.Count == 0)
        {
            return;
        }

        _dispatchDepth++;
        try
        {
            var context = TriggerContext.ForProximity(spawner, mobile);
            var triggers = set.Proximity;

            for (var i = 0; i < triggers.Count; i++)
            {
                var trigger = triggers[i];
                if (trigger.Evaluate(in context))
                {
                    spawner.RequestCycle(trigger, in context);
                    break; // Only one cycle per spawner per proximity event
                }
            }
        }
        finally
        {
            EndDispatch();
        }
    }

    /// <inheritdoc />
    public void OnSpeech(Mobile speaker, string text, Point3D location, Map map, ModernSpawner spawner)
    {
        if (speaker == null || string.IsNullOrEmpty(text) || map == null || map == Map.Internal || spawner == null)
        {
            return;
        }

        if (!_sets.TryGetValue(spawner, out var set) || set.Speech.Count == 0)
        {
            return;
        }

        _dispatchDepth++;
        try
        {
            var context = TriggerContext.ForSpeech(spawner, speaker, text);
            var triggers = set.Speech;

            for (var i = 0; i < triggers.Count; i++)
            {
                var trigger = triggers[i];
                if (trigger.Evaluate(in context))
                {
                    spawner.RequestCycle(trigger, in context);
                    break; // Only one cycle per spawner per speech event
                }
            }
        }
        finally
        {
            EndDispatch();
        }
    }

    /// <inheritdoc />
    public void OnEntityKilled(ModernSpawner spawner, IEntity killed, Mobile killer)
    {
        if (spawner == null || killed == null)
        {
            return;
        }

        if (!_sets.TryGetValue(spawner, out var set) || set.Kill.Count == 0)
        {
            return;
        }

        _dispatchDepth++;
        try
        {
            var context = TriggerContext.ForKill(spawner, killed, killer);
            var triggers = set.Kill;

            for (var i = 0; i < triggers.Count; i++)
            {
                var trigger = triggers[i];
                var accepted = trigger.Evaluate(in context);

                // Interim: a kill that passes the trigger's filters advances its counter whether or not
                // the resulting event is accepted, and Evaluate is pure, so the advance happens here
                // until the spawner's acceptance path owns it.
                if (trigger.CountsKill(in context))
                {
                    trigger.AdvanceKillCount(accepted);
                }

                if (accepted)
                {
                    spawner.RequestCycle(trigger, in context);
                    break;
                }
            }
        }
        finally
        {
            EndDispatch();
        }
    }
}
