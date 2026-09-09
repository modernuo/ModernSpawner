using System;

namespace Server.Engines.ModernSpawner.Triggers;

/// <summary>
/// Trigger that activates when a spawned entity from this spawner is killed.
/// Useful for respawn-on-kill mechanics or boss encounter progression.
/// </summary>
public class KillTrigger : ITrigger
{
    public string TriggerType => "kill";

    /// <summary>
    /// The number of kills required before triggering.
    /// Set to 1 for immediate trigger on any kill.
    /// </summary>
    public int RequiredKills { get; set; } = 1;

    /// <summary>
    /// Whether to trigger only when all spawned entities are dead.
    /// </summary>
    public bool RequireAllDead { get; set; }

    /// <summary>
    /// Whether to reset the kill count after triggering.
    /// </summary>
    public bool ResetOnTrigger { get; set; } = true;

    /// <summary>
    /// Optional type name to filter which kills count.
    /// If empty, all kills count.
    /// </summary>
    public string FilterType { get; set; }

    /// <summary>
    /// Whether the killer must be a player.
    /// </summary>
    public bool RequirePlayerKiller { get; set; }

    /// <summary>
    /// Cooldown between trigger activations.
    /// </summary>
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromSeconds(5);

    private ModernSpawner _spawner;
    private int _currentKillCount;
    private DateTime _lastTriggered = DateTime.MinValue;

    public KillTrigger()
    {
    }

    public KillTrigger(int requiredKills, bool requireAllDead = false)
    {
        RequiredKills = Math.Max(1, requiredKills);
        RequireAllDead = requireAllDead;
    }

    public bool Evaluate(TriggerContext context)
    {
        if (_spawner == null || context.KilledEntity == null)
        {
            return false;
        }

        // Check cooldown
        if (Core.Now - _lastTriggered < Cooldown)
        {
            return false;
        }

        // Check type filter
        if (!string.IsNullOrEmpty(FilterType))
        {
            var entityType = context.KilledEntity.GetType();
            if (!entityType.Name.Equals(FilterType, StringComparison.OrdinalIgnoreCase) &&
                !entityType.FullName?.Equals(FilterType, StringComparison.OrdinalIgnoreCase) == true)
            {
                return false;
            }
        }

        // Check player killer requirement
        if (RequirePlayerKiller)
        {
            if (context.TriggeringMobile == null || !context.TriggeringMobile.Player)
            {
                return false;
            }
        }

        // Increment kill count
        _currentKillCount++;

        // Check if all dead is required
        if (RequireAllDead)
        {
            // Check if spawner has any remaining spawned entities
            if (_spawner.Spawned.Count > 0)
            {
                return false;
            }
        }

        // Check if we've reached required kills
        if (_currentKillCount >= RequiredKills)
        {
            _lastTriggered = Core.Now;

            if (ResetOnTrigger)
            {
                _currentKillCount = 0;
            }

            return true;
        }

        return false;
    }

    public void Activate(ModernSpawner spawner)
    {
        _spawner = spawner;
        _currentKillCount = 0;
        TriggerSystem.Instance?.RegisterKillTrigger(spawner, this);
    }

    public void Deactivate()
    {
        if (_spawner != null)
        {
            TriggerSystem.Instance?.UnregisterKillTrigger(_spawner, this);
        }
        _spawner = null;
        _currentKillCount = 0;
    }

    /// <summary>
    /// Resets the kill counter. Call this to manually reset progress.
    /// </summary>
    public void ResetKillCount()
    {
        _currentKillCount = 0;
    }

    public string Serialize()
    {
        // Format: kill:requiredKills:requireAllDead:resetOnTrigger:filterType:requirePlayerKiller:cooldownSeconds
        var filter = string.IsNullOrEmpty(FilterType) ? "any" : FilterType;
        return $"kill:{RequiredKills}:{RequireAllDead}:{ResetOnTrigger}:{filter}:{RequirePlayerKiller}:{(int)Cooldown.TotalSeconds}";
    }

    public static KillTrigger Parse(string definition)
    {
        var parts = definition.Split(':');
        var trigger = new KillTrigger();

        if (parts.Length > 1 && int.TryParse(parts[1], out var requiredKills))
        {
            trigger.RequiredKills = Math.Max(1, requiredKills);
        }

        if (parts.Length > 2 && bool.TryParse(parts[2], out var requireAllDead))
        {
            trigger.RequireAllDead = requireAllDead;
        }

        if (parts.Length > 3 && bool.TryParse(parts[3], out var resetOnTrigger))
        {
            trigger.ResetOnTrigger = resetOnTrigger;
        }

        if (parts.Length > 4)
        {
            var filter = parts[4];
            if (!string.Equals(filter, "any", StringComparison.OrdinalIgnoreCase))
            {
                trigger.FilterType = filter;
            }
        }

        if (parts.Length > 5 && bool.TryParse(parts[5], out var requirePlayerKiller))
        {
            trigger.RequirePlayerKiller = requirePlayerKiller;
        }

        if (parts.Length > 6 && int.TryParse(parts[6], out var cooldown))
        {
            trigger.Cooldown = TimeSpan.FromSeconds(cooldown);
        }

        return trigger;
    }
}
