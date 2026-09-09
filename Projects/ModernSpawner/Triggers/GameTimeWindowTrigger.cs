using System;
using Server.Items;

namespace Server.Engines.ModernSpawner.Triggers;

/// <summary>
/// Trigger that activates based on in-game time windows.
/// Uses transition-based timers for efficiency instead of polling.
/// </summary>
/// <remarks>
/// This trigger uses the UO in-game clock which runs faster than real time.
/// For real-world time scheduling, use <see cref="WallTimeWindowTrigger"/>.
/// </remarks>
public class GameTimeWindowTrigger : ITrigger
{
    // UO game time runs at approximately 12 real minutes per game hour
    // (24 game hours = ~288 real minutes = ~4.8 real hours)
    private static readonly TimeSpan RealTimePerGameHour = TimeSpan.FromMinutes(12);

    public string TriggerType => "game_time_window";

    /// <summary>
    /// The start hour (0-23) when the spawn window opens.
    /// </summary>
    public int StartHour { get; set; }

    /// <summary>
    /// The end hour (0-23) when the spawn window closes.
    /// If EndHour &lt; StartHour, the period spans midnight.
    /// </summary>
    public int EndHour { get; set; } = 23;

    /// <summary>
    /// Convenience mode: trigger only during nighttime (roughly 9pm-5am game time).
    /// Overrides StartHour/EndHour.
    /// </summary>
    public bool NightOnly { get; set; }

    /// <summary>
    /// Convenience mode: trigger only during daytime (roughly 5am-9pm game time).
    /// Overrides StartHour/EndHour.
    /// </summary>
    public bool DayOnly { get; set; }

    /// <summary>
    /// Whether the window is currently open.
    /// </summary>
    public bool IsWindowOpen { get; private set; }

    private ModernSpawner _spawner;
    private TimerExecutionToken _transitionTimer;

    // Night is roughly 9pm (21) to 5am (5)
    private const int NightStartHour = 21;
    private const int NightEndHour = 5;

    // Day is roughly 5am (5) to 9pm (21)
    private const int DayStartHour = 5;
    private const int DayEndHour = 21;

    public GameTimeWindowTrigger()
    {
    }

    /// <summary>
    /// Creates a trigger for a specific game-time window.
    /// </summary>
    public GameTimeWindowTrigger(int startHour, int endHour)
    {
        StartHour = Math.Clamp(startHour, 0, 23);
        EndHour = Math.Clamp(endHour, 0, 23);
    }

    /// <summary>
    /// Creates a night-only trigger.
    /// </summary>
    public static GameTimeWindowTrigger NightOnlyTrigger() => new() { NightOnly = true };

    /// <summary>
    /// Creates a day-only trigger.
    /// </summary>
    public static GameTimeWindowTrigger DayOnlyTrigger() => new() { DayOnly = true };

    public bool Evaluate(TriggerContext context)
    {
        return IsWindowOpen;
    }

    public void Activate(ModernSpawner spawner)
    {
        _spawner = spawner;

        // Get effective hours based on mode
        var (effectiveStart, effectiveEnd) = GetEffectiveHours();

        // Check current state and schedule next transition
        UpdateWindowState(effectiveStart, effectiveEnd);
        ScheduleNextTransition(effectiveStart, effectiveEnd);
    }

    public void Deactivate()
    {
        _transitionTimer.Cancel();
        _spawner = null;
        IsWindowOpen = false;
    }

    private (int start, int end) GetEffectiveHours()
    {
        if (NightOnly)
        {
            return (NightStartHour, NightEndHour);
        }

        if (DayOnly)
        {
            return (DayStartHour, DayEndHour);
        }

        return (StartHour, EndHour);
    }

    private void UpdateWindowState(int effectiveStart, int effectiveEnd)
    {
        if (_spawner?.Map == null)
        {
            IsWindowOpen = false;
            return;
        }

        Clock.GetTime(_spawner.Map, _spawner.X, _spawner.Y, out int currentHour, out int _);

        var wasOpen = IsWindowOpen;
        IsWindowOpen = IsHourInWindow(currentHour, effectiveStart, effectiveEnd);

        // Notify spawner of state change
        if (IsWindowOpen && !wasOpen)
        {
            _spawner.OnTriggerActivated(this);
        }
        else if (!IsWindowOpen && wasOpen)
        {
            _spawner.OnTriggerDeactivated(this);
        }
    }

    private void ScheduleNextTransition(int effectiveStart, int effectiveEnd)
    {
        if (_spawner?.Map == null)
        {
            return;
        }

        Clock.GetTime(_spawner.Map, _spawner.X, _spawner.Y, out int currentHour, out int currentMinute);

        // Calculate hours until next transition
        int hoursUntilTransition;

        if (IsWindowOpen)
        {
            // Window is open, calculate time until it closes
            hoursUntilTransition = CalculateHoursUntil(currentHour, effectiveEnd);
        }
        else
        {
            // Window is closed, calculate time until it opens
            hoursUntilTransition = CalculateHoursUntil(currentHour, effectiveStart);
        }

        // Account for current minutes within the hour
        var gameMinutesRemaining = 60 - currentMinute;
        var realTimeUntilTransition = TimeSpan.FromMinutes(
            (hoursUntilTransition - 1) * RealTimePerGameHour.TotalMinutes +
            (gameMinutesRemaining * RealTimePerGameHour.TotalMinutes / 60));

        // Ensure minimum delay to prevent tight loops
        if (realTimeUntilTransition < TimeSpan.FromSeconds(10))
        {
            realTimeUntilTransition = TimeSpan.FromSeconds(10);
        }

        // Cancel any existing timer and schedule new one
        _transitionTimer.Cancel();
        Timer.StartTimer(realTimeUntilTransition, OnTransition, out _transitionTimer);
    }

    private void OnTransition()
    {
        if (_spawner == null)
        {
            return;
        }

        var (effectiveStart, effectiveEnd) = GetEffectiveHours();

        // Update state
        UpdateWindowState(effectiveStart, effectiveEnd);

        // Schedule next transition
        ScheduleNextTransition(effectiveStart, effectiveEnd);
    }

    /// <summary>
    /// Pure helper: returns true if <paramref name="currentHour"/> falls inside the
    /// [start, end) window, correctly handling ranges that cross midnight. Public so
    /// it can be exercised by unit tests without a live <see cref="Map"/>.
    /// </summary>
    public static bool IsHourInWindow(int currentHour, int startHour, int endHour)
    {
        if (endHour >= startHour)
        {
            // Normal window (e.g., 8 to 17 means 8am to 5pm)
            return currentHour >= startHour && currentHour < endHour;
        }

        // Window spans midnight (e.g., 22 to 4 means 10pm to 4am)
        return currentHour >= startHour || currentHour < endHour;
    }

    /// <summary>
    /// Pure helper: number of whole hours from <paramref name="currentHour"/> until
    /// <paramref name="targetHour"/>, wrapping across midnight if needed. Public so
    /// it can be exercised by unit tests.
    /// </summary>
    public static int CalculateHoursUntil(int currentHour, int targetHour)
    {
        if (targetHour > currentHour)
        {
            return targetHour - currentHour;
        }

        // Target is tomorrow (or we're calculating across midnight)
        return 24 - currentHour + targetHour;
    }

    public string Serialize()
    {
        // Format: game_time_window:startHour:endHour:nightOnly:dayOnly
        return $"game_time_window:{StartHour}:{EndHour}:{NightOnly}:{DayOnly}";
    }

    public static GameTimeWindowTrigger Parse(string definition)
    {
        var parts = definition.Split(':');
        var trigger = new GameTimeWindowTrigger();

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

        return trigger;
    }
}
