using System;
using Server.Engines.Events;
using Server.Engines.ModernSpawner.Triggers.Scheduling;

namespace Server.Engines.ModernSpawner.Triggers;

/// <summary>
/// Trigger that activates based on real-world (wall clock) time windows.
/// Uses ModernUO's EventScheduler for efficient event-based scheduling.
/// </summary>
/// <remarks>
/// This trigger is for wall-clock time (real time). For in-game time
/// (day/night cycles), use <see cref="GameTimeWindowTrigger"/>.
/// </remarks>
public class WallTimeWindowTrigger : ITrigger
{
    public string TriggerType => "wall_time_window";

    /// <summary>
    /// The time when the spawn window opens (in the specified timezone).
    /// </summary>
    public TimeOnly StartTime { get; set; } = new(0, 0);

    /// <summary>
    /// The time when the spawn window closes (in the specified timezone).
    /// </summary>
    public TimeOnly EndTime { get; set; } = new(23, 59, 59);

    /// <summary>
    /// Days of the week when this trigger is active.
    /// Default is all days.
    /// </summary>
    public AllowedDays AllowedDays { get; set; } = AllowedDays.All;

    /// <summary>
    /// Months when this trigger is active.
    /// Default is all months.
    /// </summary>
    public AllowedMonths AllowedMonths { get; set; } = AllowedMonths.All;

    /// <summary>
    /// The timezone for interpreting StartTime and EndTime.
    /// Default is UTC.
    /// </summary>
    public TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Utc;

    /// <summary>
    /// Whether the window is currently open.
    /// </summary>
    public bool IsWindowOpen => _scheduledEvent?.IsWindowOpen ?? false;

    private ModernSpawner _spawner;
    private TimeWindowScheduledEvent _scheduledEvent;

    public WallTimeWindowTrigger()
    {
    }

    /// <summary>
    /// Creates a trigger for a specific time window.
    /// </summary>
    public WallTimeWindowTrigger(TimeOnly startTime, TimeOnly endTime)
    {
        StartTime = startTime;
        EndTime = endTime;
    }

    /// <summary>
    /// Creates a trigger for weekend evenings only.
    /// </summary>
    public static WallTimeWindowTrigger WeekendEvenings(TimeOnly startTime, TimeOnly endTime) =>
        new(startTime, endTime)
        {
            AllowedDays = AllowedDays.Friday | AllowedDays.Saturday | AllowedDays.Sunday
        };

    /// <summary>
    /// Creates a trigger for a specific month (seasonal events).
    /// </summary>
    public static WallTimeWindowTrigger Seasonal(AllowedMonths months, TimeOnly startTime, TimeOnly endTime) =>
        new(startTime, endTime)
        {
            AllowedMonths = months
        };

    /// <summary>
    /// Creates a Halloween event trigger (October evenings).
    /// </summary>
    public static WallTimeWindowTrigger Halloween() =>
        new(new TimeOnly(18, 0), new TimeOnly(23, 59))
        {
            AllowedMonths = AllowedMonths.October
        };

    /// <summary>
    /// Creates a Christmas event trigger (December).
    /// </summary>
    public static WallTimeWindowTrigger Christmas() =>
        new(new TimeOnly(0, 0), new TimeOnly(23, 59))
        {
            AllowedMonths = AllowedMonths.December
        };

    public bool Evaluate(TriggerContext context)
    {
        return IsWindowOpen;
    }

    public void Activate(ModernSpawner spawner)
    {
        _spawner = spawner;

        // Create and schedule the time window event
        _scheduledEvent = new TimeWindowScheduledEvent(
            StartTime,
            EndTime,
            OnWindowOpen,
            OnWindowClose,
            AllowedDays,
            AllowedMonths);

        _scheduledEvent.Schedule(Core.Now, TimeZone);

        // If we're already in the window, trigger immediately
        if (_scheduledEvent.IsWindowOpen)
        {
            OnWindowOpen();
        }
    }

    public void Deactivate()
    {
        _scheduledEvent?.Cancel();
        _scheduledEvent = null;
        _spawner = null;
    }

    private void OnWindowOpen()
    {
        // Notify the spawner that the time window is now active
        _spawner?.OnTriggerActivated(this);
    }

    private void OnWindowClose()
    {
        // Notify the spawner that the time window has closed
        _spawner?.OnTriggerDeactivated(this);
    }

    public string Serialize()
    {
        // Format: wall_time_window:startHour:startMin:endHour:endMin:allowedDays:allowedMonths:timezone
        return $"wall_time_window:{StartTime.Hour}:{StartTime.Minute}:{EndTime.Hour}:{EndTime.Minute}:{(int)AllowedDays}:{(int)AllowedMonths}:{TimeZone.Id}";
    }

    public static WallTimeWindowTrigger Parse(string definition)
    {
        var parts = definition.Split(':');
        var trigger = new WallTimeWindowTrigger();

        if (parts.Length > 2 &&
            int.TryParse(parts[1], out var startHour) &&
            int.TryParse(parts[2], out var startMin))
        {
            trigger.StartTime = new TimeOnly(
                Math.Clamp(startHour, 0, 23),
                Math.Clamp(startMin, 0, 59));
        }

        if (parts.Length > 4 &&
            int.TryParse(parts[3], out var endHour) &&
            int.TryParse(parts[4], out var endMin))
        {
            trigger.EndTime = new TimeOnly(
                Math.Clamp(endHour, 0, 23),
                Math.Clamp(endMin, 0, 59));
        }

        if (parts.Length > 5 && int.TryParse(parts[5], out var days))
        {
            trigger.AllowedDays = (AllowedDays)days;
        }

        if (parts.Length > 6 && int.TryParse(parts[6], out var months))
        {
            trigger.AllowedMonths = (AllowedMonths)months;
        }

        if (parts.Length > 7)
        {
            try
            {
                trigger.TimeZone = TimeZoneInfo.FindSystemTimeZoneById(parts[7]);
            }
            catch
            {
                trigger.TimeZone = TimeZoneInfo.Utc;
            }
        }

        return trigger;
    }
}
