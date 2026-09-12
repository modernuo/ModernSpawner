using System;

namespace Server.Engines.ModernSpawner.Triggers;

/// <summary>
/// Trigger that activates when a mobile comes within range of the spawner.
/// </summary>
public class ProximityTrigger : ITrigger
{
    public string TriggerType => "proximity";

    /// <summary>
    /// The range within which the mobile must be to trigger.
    /// </summary>
    public int Range { get; set; } = 8;

    /// <summary>
    /// Whether the trigger requires line of sight.
    /// </summary>
    public bool RequireLineOfSight { get; set; }

    /// <summary>
    /// Whether the trigger only activates for players (not NPCs).
    /// </summary>
    public bool PlayersOnly { get; set; } = true;

    /// <summary>
    /// Minimum access level required to trigger (for staff-only spawners).
    /// </summary>
    public AccessLevel MinAccessLevel { get; set; } = AccessLevel.Player;

    /// <summary>
    /// Cooldown between trigger activations.
    /// </summary>
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromSeconds(5);

    private ModernSpawner _spawner;
    private DateTime _lastTriggered = DateTime.MinValue;

    public ProximityTrigger()
    {
    }

    public ProximityTrigger(int range, bool playersOnly = true, bool requireLos = false)
    {
        Range = range;
        PlayersOnly = playersOnly;
        RequireLineOfSight = requireLos;
    }

    public bool Evaluate(TriggerContext context)
    {
        if (context.TriggeringMobile == null || _spawner == null)
        {
            return false;
        }

        var mobile = context.TriggeringMobile;

        // Check access level
        if (mobile.AccessLevel < MinAccessLevel)
        {
            return false;
        }

        // Check if players only
        if (PlayersOnly && !mobile.Player)
        {
            return false;
        }

        // Check range
        if (!mobile.InRange(_spawner.Location, Range))
        {
            return false;
        }

        // Check map
        if (mobile.Map != _spawner.Map)
        {
            return false;
        }

        // Line of sight, not visibility: Mobile.CanSee(Item) ends in item.Visible, and a spawner is
        // Visible = false, so CanSee could never pass here for a player.
        if (RequireLineOfSight && !mobile.InLOS(_spawner))
        {
            return false;
        }

        // Check cooldown
        if (Core.Now - _lastTriggered < Cooldown)
        {
            return false;
        }

        _lastTriggered = Core.Now;
        return true;
    }

    public void Activate(ModernSpawner spawner)
    {
        _spawner = spawner;
        // Register with the trigger system for proximity events
        TriggerSystem.Instance?.RegisterProximityTrigger(spawner, this);
    }

    public void Deactivate()
    {
        if (_spawner != null)
        {
            TriggerSystem.Instance?.UnregisterProximityTrigger(_spawner, this);
        }
        _spawner = null;
    }

    public string Serialize()
    {
        // Format: proximity:range:playersOnly:requireLos:cooldownSeconds:minAccess
        return $"proximity:{Range}:{PlayersOnly}:{RequireLineOfSight}:{(int)Cooldown.TotalSeconds}:{(int)MinAccessLevel}";
    }

    public static ProximityTrigger Parse(string definition)
    {
        // Skip the "proximity:" prefix
        var parts = definition.Split(':');
        var trigger = new ProximityTrigger();

        if (parts.Length > 1 && int.TryParse(parts[1], out var range))
        {
            trigger.Range = range;
        }

        if (parts.Length > 2 && bool.TryParse(parts[2], out var playersOnly))
        {
            trigger.PlayersOnly = playersOnly;
        }

        if (parts.Length > 3 && bool.TryParse(parts[3], out var requireLos))
        {
            trigger.RequireLineOfSight = requireLos;
        }

        if (parts.Length > 4 && int.TryParse(parts[4], out var cooldown))
        {
            trigger.Cooldown = TimeSpan.FromSeconds(cooldown);
        }

        if (parts.Length > 5 && int.TryParse(parts[5], out var accessLevel))
        {
            trigger.MinAccessLevel = (AccessLevel)accessLevel;
        }

        return trigger;
    }
}
