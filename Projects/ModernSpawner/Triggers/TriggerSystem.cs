using System;
using System.Collections.Generic;

namespace Server.Engines.ModernSpawner.Triggers;

/// <summary>
/// Default implementation of the trigger system.
/// Manages trigger registration, parsing, and event routing.
/// </summary>
public class TriggerSystem : ITriggerSystem
{
    /// <summary>
    /// Singleton instance for convenience.
    /// </summary>
    public static TriggerSystem Instance { get; } = new();

    private readonly Dictionary<string, Func<string, ITrigger>> _factories = new(StringComparer.OrdinalIgnoreCase);

    // Registered triggers by type for event routing
    private readonly Dictionary<ModernSpawner, List<ProximityTrigger>> _proximityTriggers = new();
    private readonly Dictionary<ModernSpawner, List<SpeechTrigger>> _speechTriggers = new();
    private readonly Dictionary<ModernSpawner, List<TimeOfDayTrigger>> _timeOfDayTriggers = new();
    private readonly Dictionary<ModernSpawner, List<KillTrigger>> _killTriggers = new();
    private readonly Dictionary<ModernSpawner, List<SkillTrigger>> _skillTriggers = new();
    private readonly Dictionary<ModernSpawner, List<ITrigger>> _allTriggers = new();

    private Timer _timeOfDayTimer;

    public TriggerSystem()
    {
        // Register built-in trigger types
        RegisterTriggerType("proximity", ProximityTrigger.Parse);
        RegisterTriggerType("speech", SpeechTrigger.Parse);
        RegisterTriggerType("kill", KillTrigger.Parse);
        RegisterTriggerType("skill", SkillTrigger.Parse);

        // Time-based triggers (legacy polling-based)
        RegisterTriggerType("timeofday", TimeOfDayTrigger.Parse);

        // New efficient event-based time triggers
        RegisterTriggerType("wall_time_window", WallTimeWindowTrigger.Parse);
        RegisterTriggerType("game_time_window", GameTimeWindowTrigger.Parse);
    }

    public void RegisterTriggerType(string triggerType, Func<string, ITrigger> factory)
    {
        _factories[triggerType] = factory;
    }

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
                return factory(definition);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to parse trigger '{definition}': {ex.Message}");
            }
        }

        Console.WriteLine($"Unknown trigger type: {triggerType}");
        return null;
    }

    public void ActivateTriggers(ModernSpawner spawner)
    {
        // Parse and activate all trigger definitions
        var definitions = spawner?.TriggerDefinitions;
        if (definitions == null || definitions.Count == 0)
        {
            return;
        }

        var triggers = new List<ITrigger>();

        foreach (var definition in definitions)
        {
            var trigger = ParseTrigger(definition);
            if (trigger != null)
            {
                trigger.Activate(spawner);
                triggers.Add(trigger);
            }
        }

        if (triggers.Count > 0)
        {
            _allTriggers[spawner] = triggers;
        }
    }

    /// <summary>
    /// Whether <paramref name="spawner" /> currently has triggers registered with this system, i.e. whether
    /// <see cref="ActivateTriggers" /> has run for it without a matching <see cref="DeactivateTriggers" />.
    /// </summary>
    internal bool IsRegistered(ModernSpawner spawner) => spawner != null && _allTriggers.ContainsKey(spawner);

    public void DeactivateTriggers(ModernSpawner spawner)
    {
        if (spawner == null)
        {
            return;
        }

        if (_allTriggers.TryGetValue(spawner, out var triggers))
        {
            foreach (var trigger in triggers)
            {
                trigger.Deactivate();
            }

            _allTriggers.Remove(spawner);
        }

        // Clean up specific trigger type registrations and notify spawner
        if (_proximityTriggers.Remove(spawner))
        {
            spawner.SetHasProximityTriggers(false);
            spawner.UnsubscribeFromExtendedAreaMovement();
        }

        if (_speechTriggers.Remove(spawner))
        {
            spawner.SetHasSpeechTriggers(false);
        }

        _timeOfDayTriggers.Remove(spawner);
        _killTriggers.Remove(spawner);
        _skillTriggers.Remove(spawner);

        // Stop time-of-day timer if no more time-based triggers
        if (_timeOfDayTriggers.Count == 0)
        {
            _timeOfDayTimer?.Stop();
            _timeOfDayTimer = null;
        }
    }

    public void RegisterProximityTrigger(ModernSpawner spawner, ProximityTrigger trigger)
    {
        if (!_proximityTriggers.TryGetValue(spawner, out var list))
        {
            list = [];
            _proximityTriggers[spawner] = list;
        }

        if (!list.Contains(trigger))
        {
            list.Add(trigger);
        }

        // Notify spawner it now has proximity triggers (enables HandlesOnMovement)
        spawner.SetHasProximityTriggers(true);

        // Check if any trigger requires extended range (beyond 24 tiles)
        UpdateExtendedProximityBounds(spawner, list);
    }

    public void UnregisterProximityTrigger(ModernSpawner spawner, ProximityTrigger trigger)
    {
        if (_proximityTriggers.TryGetValue(spawner, out var list))
        {
            list.Remove(trigger);
            if (list.Count == 0)
            {
                _proximityTriggers.Remove(spawner);
                // Notify spawner it no longer has proximity triggers
                spawner.SetHasProximityTriggers(false);
                // Unsubscribe from extended area movement
                spawner.UnsubscribeFromExtendedAreaMovement();
            }
            else
            {
                // Recalculate extended bounds with remaining triggers
                UpdateExtendedProximityBounds(spawner, list);
            }
        }
    }

    /// <summary>
    /// Updates extended proximity bounds for a spawner based on its proximity triggers.
    /// Sets up area movement subscription if any trigger range exceeds 24 tiles.
    /// </summary>
    private static void UpdateExtendedProximityBounds(ModernSpawner spawner, List<ProximityTrigger> triggers)
    {
        // Find the maximum trigger range
        var maxRange = 0;
        for (var i = 0; i < triggers.Count; i++)
        {
            var trigger = triggers[i];
            if (trigger.Range > maxRange)
            {
                maxRange = trigger.Range;
            }
        }

        // If max range exceeds the normal OnMovement range (24 tiles), use extended area subscriptions
        if (maxRange > Core.GlobalMaxUpdateRange)
        {
            // Calculate bounds centered on spawner location with the max range
            var location = spawner.Location;
            var bounds = new Rectangle2D(
                location.X - maxRange,
                location.Y - maxRange,
                maxRange * 2 + 1,
                maxRange * 2 + 1
            );

            spawner.SetExtendedTriggerBounds(bounds);
        }
        else
        {
            // No extended range needed - unsubscribe if previously subscribed
            spawner.UnsubscribeFromExtendedAreaMovement();
        }
    }

    public void RegisterSpeechTrigger(ModernSpawner spawner, SpeechTrigger trigger)
    {
        if (!_speechTriggers.TryGetValue(spawner, out var list))
        {
            list = [];
            _speechTriggers[spawner] = list;
        }

        if (!list.Contains(trigger))
        {
            list.Add(trigger);
        }

        // Notify spawner it now has speech triggers (enables HandlesOnSpeech)
        spawner.SetHasSpeechTriggers(true);
    }

    public void UnregisterSpeechTrigger(ModernSpawner spawner, SpeechTrigger trigger)
    {
        if (_speechTriggers.TryGetValue(spawner, out var list))
        {
            list.Remove(trigger);
            if (list.Count == 0)
            {
                _speechTriggers.Remove(spawner);
                // Notify spawner it no longer has speech triggers
                spawner.SetHasSpeechTriggers(false);
            }
        }
    }

    public void RegisterTimeOfDayTrigger(ModernSpawner spawner, TimeOfDayTrigger trigger)
    {
        if (!_timeOfDayTriggers.TryGetValue(spawner, out var list))
        {
            list = [];
            _timeOfDayTriggers[spawner] = list;
        }

        if (!list.Contains(trigger))
        {
            list.Add(trigger);
        }

        // Start the time-of-day timer if not already running
        StartTimeOfDayTimer();
    }

    public void UnregisterTimeOfDayTrigger(ModernSpawner spawner, TimeOfDayTrigger trigger)
    {
        if (_timeOfDayTriggers.TryGetValue(spawner, out var list))
        {
            list.Remove(trigger);
            if (list.Count == 0)
            {
                _timeOfDayTriggers.Remove(spawner);
            }
        }
    }

    public void RegisterKillTrigger(ModernSpawner spawner, KillTrigger trigger)
    {
        if (!_killTriggers.TryGetValue(spawner, out var list))
        {
            list = [];
            _killTriggers[spawner] = list;
        }

        if (!list.Contains(trigger))
        {
            list.Add(trigger);
        }
    }

    public void UnregisterKillTrigger(ModernSpawner spawner, KillTrigger trigger)
    {
        if (_killTriggers.TryGetValue(spawner, out var list))
        {
            list.Remove(trigger);
            if (list.Count == 0)
            {
                _killTriggers.Remove(spawner);
            }
        }
    }

    public void RegisterSkillTrigger(ModernSpawner spawner, SkillTrigger trigger)
    {
        if (!_skillTriggers.TryGetValue(spawner, out var list))
        {
            list = [];
            _skillTriggers[spawner] = list;
        }

        if (!list.Contains(trigger))
        {
            list.Add(trigger);
        }
    }

    public void UnregisterSkillTrigger(ModernSpawner spawner, SkillTrigger trigger)
    {
        if (_skillTriggers.TryGetValue(spawner, out var list))
        {
            list.Remove(trigger);
            if (list.Count == 0)
            {
                _skillTriggers.Remove(spawner);
            }
        }
    }

    public void OnSkillUse(Mobile mobile, SkillName skill)
    {
        if (mobile == null || mobile.Map == null || mobile.Map == Map.Internal)
        {
            return;
        }

        // Check all registered skill triggers
        foreach (var (spawner, triggers) in _skillTriggers)
        {
            if (spawner.Map != mobile.Map)
            {
                continue;
            }

            var context = new TriggerContext(spawner)
            {
                TriggeringMobile = mobile,
                UsedSkill = skill
            };

            foreach (var trigger in triggers)
            {
                if (trigger.MatchesSkill(skill) && trigger.Evaluate(context))
                {
                    spawner.Trigger();
                    break; // Only trigger once per spawner per skill use
                }
            }
        }
    }

    private void StartTimeOfDayTimer()
    {
        if (_timeOfDayTimer != null)
        {
            return;
        }

        // Check time-based triggers every game minute (roughly every 2.5 real seconds)
        _timeOfDayTimer = Timer.DelayCall(TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(2.5), CheckTimeOfDayTriggers);
    }

    private void CheckTimeOfDayTriggers()
    {
        if (_timeOfDayTriggers.Count == 0)
        {
            _timeOfDayTimer?.Stop();
            _timeOfDayTimer = null;
            return;
        }

        foreach (var (spawner, triggers) in _timeOfDayTriggers)
        {
            if (spawner.Deleted || !spawner.Running)
            {
                continue;
            }

            var context = new TriggerContext(spawner);

            foreach (var trigger in triggers)
            {
                if (trigger.Evaluate(context))
                {
                    spawner.Trigger();
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Called when a mobile moves near a specific spawner (via Item.OnMovement).
    /// This is the optimized path using ModernUO's built-in sector-based dispatch.
    /// </summary>
    public void OnMobileProximity(Mobile mobile, Point3D location, Map map, ModernSpawner spawner)
    {
        if (mobile == null || map == null || map == Map.Internal || spawner == null)
        {
            return;
        }

        if (!_proximityTriggers.TryGetValue(spawner, out var triggers))
        {
            return;
        }

        var context = new TriggerContext(spawner)
        {
            TriggeringMobile = mobile
        };

        foreach (var trigger in triggers)
        {
            if (trigger.Evaluate(context))
            {
                spawner.Trigger();
                break; // Only trigger once per spawner per proximity event
            }
        }
    }

    /// <summary>
    /// Called when speech occurs near a specific spawner (via Item.OnSpeech).
    /// This is the optimized path using ModernUO's built-in sector-based dispatch.
    /// </summary>
    public void OnSpeech(Mobile speaker, string text, Point3D location, Map map, ModernSpawner spawner)
    {
        if (speaker == null || string.IsNullOrEmpty(text) || map == null || map == Map.Internal || spawner == null)
        {
            return;
        }

        if (!_speechTriggers.TryGetValue(spawner, out var triggers))
        {
            return;
        }

        var context = new TriggerContext(spawner)
        {
            TriggeringMobile = speaker,
            Speech = text
        };

        foreach (var trigger in triggers)
        {
            if (trigger.Evaluate(context))
            {
                spawner.Trigger();
                break; // Only trigger once per spawner per speech event
            }
        }
    }

    public void OnEntityKilled(ModernSpawner spawner, IEntity killed, Mobile killer)
    {
        if (spawner == null || killed == null)
        {
            return;
        }

        if (!_killTriggers.TryGetValue(spawner, out var triggers))
        {
            return;
        }

        var context = new TriggerContext(spawner)
        {
            KilledEntity = killed,
            TriggeringMobile = killer
        };

        foreach (var trigger in triggers)
        {
            if (trigger.Evaluate(context))
            {
                spawner.Trigger();
                break;
            }
        }
    }

    /// <summary>
    /// Gets all active spawners with proximity triggers in the given region.
    /// Used for optimized proximity checking.
    /// </summary>
    public IEnumerable<ModernSpawner> GetSpawnersWithProximityTriggers(Map map)
    {
        foreach (var spawner in _proximityTriggers.Keys)
        {
            if (spawner.Map == map)
            {
                yield return spawner;
            }
        }
    }

    /// <summary>
    /// Gets all active spawners with speech triggers in the given region.
    /// Used for optimized speech checking.
    /// </summary>
    public IEnumerable<ModernSpawner> GetSpawnersWithSpeechTriggers(Map map)
    {
        foreach (var spawner in _speechTriggers.Keys)
        {
            if (spawner.Map == map)
            {
                yield return spawner;
            }
        }
    }

}
