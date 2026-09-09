using System;
using Server.Items;

namespace Server.Engines.ModernSpawner.Triggers;

/// <summary>
/// Trigger that activates based on the in-game time of day.
/// Supports specifying active hours during which spawning can occur.
/// </summary>
public class TimeOfDayTrigger : ITrigger
{
    public string TriggerType => "timeofday";

    /// <summary>
    /// The start hour (0-23) when this trigger becomes active.
    /// </summary>
    public int StartHour { get; set; }

    /// <summary>
    /// The end hour (0-23) when this trigger becomes inactive.
    /// If EndHour < StartHour, the period spans midnight.
    /// </summary>
    public int EndHour { get; set; } = 23;

    /// <summary>
    /// Whether to trigger only during nighttime (roughly 9pm - 5am game time).
    /// This is a convenience mode that overrides StartHour/EndHour.
    /// </summary>
    public bool NightOnly { get; set; }

    /// <summary>
    /// Whether to trigger only during daytime (roughly 5am - 9pm game time).
    /// This is a convenience mode that overrides StartHour/EndHour.
    /// </summary>
    public bool DayOnly { get; set; }

    /// <summary>
    /// Cooldown between trigger activations.
    /// </summary>
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromMinutes(1);

    private ModernSpawner _spawner;
    private DateTime _lastTriggered = DateTime.MinValue;

    public TimeOfDayTrigger()
    {
    }

    public TimeOfDayTrigger(int startHour, int endHour)
    {
        StartHour = Math.Clamp(startHour, 0, 23);
        EndHour = Math.Clamp(endHour, 0, 23);
    }

    public bool Evaluate(TriggerContext context)
    {
        if (_spawner == null)
        {
            return false;
        }

        // Check cooldown
        if (Core.Now - _lastTriggered < Cooldown)
        {
            return false;
        }

        // Get current game time
        Clock.GetTime(_spawner.Map, _spawner.X, _spawner.Y, out var hours, out int _);

        bool isActiveTime;

        if (NightOnly)
        {
            // Night is roughly 9pm (21) to 5am (5)
            isActiveTime = hours >= 21 || hours < 5;
        }
        else if (DayOnly)
        {
            // Day is roughly 5am (5) to 9pm (21)
            isActiveTime = hours is >= 5 and < 21;
        }
        else if (EndHour >= StartHour)
        {
            // Normal time range (e.g., 8 to 17 means 8am to 5pm)
            isActiveTime = hours >= StartHour && hours <= EndHour;
        }
        else
        {
            // Time range spans midnight (e.g., 22 to 4 means 10pm to 4am)
            isActiveTime = hours >= StartHour || hours <= EndHour;
        }

        if (isActiveTime)
        {
            _lastTriggered = Core.Now;
            return true;
        }

        return false;
    }

    public void Activate(ModernSpawner spawner)
    {
        _spawner = spawner;
        TriggerSystem.Instance?.RegisterTimeOfDayTrigger(spawner, this);
    }

    public void Deactivate()
    {
        if (_spawner != null)
        {
            TriggerSystem.Instance?.UnregisterTimeOfDayTrigger(_spawner, this);
        }
        _spawner = null;
    }

    public string Serialize()
    {
        // Format: timeofday:startHour:endHour:nightOnly:dayOnly:cooldownSeconds
        return $"timeofday:{StartHour}:{EndHour}:{NightOnly}:{DayOnly}:{(int)Cooldown.TotalSeconds}";
    }

    public static TimeOfDayTrigger Parse(string definition)
    {
        var parts = definition.Split(':');
        var trigger = new TimeOfDayTrigger();

        if (parts.Length > 1 && int.TryParse(parts[1], out var startHour))
        {
            trigger.StartHour = Math.Clamp(startHour, 0, 23);
        }

        if (parts.Length > 2 && int.TryParse(parts[2], out var endHour))
        {
            trigger.EndHour = Math.Clamp(endHour, 0, 23);
        }

        if (parts.Length > 3 && bool.TryParse(parts[3], out var nightOnly))
        {
            trigger.NightOnly = nightOnly;
        }

        if (parts.Length > 4 && bool.TryParse(parts[4], out var dayOnly))
        {
            trigger.DayOnly = dayOnly;
        }

        if (parts.Length > 5 && int.TryParse(parts[5], out var cooldown))
        {
            trigger.Cooldown = TimeSpan.FromSeconds(cooldown);
        }

        return trigger;
    }
}
