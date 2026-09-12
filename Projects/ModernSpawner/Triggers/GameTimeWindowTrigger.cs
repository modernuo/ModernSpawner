using System;
using Server.Items;

namespace Server.Engines.ModernSpawner.Triggers;

/// <summary>
/// Gate that opens and closes on in-game time windows.
/// Uses transition-based timers for efficiency instead of polling.
/// </summary>
/// <remarks>
/// This trigger uses the UO in-game clock which runs faster than real time.
/// For real-world time scheduling, use <see cref="WallTimeWindowTrigger"/>.
/// </remarks>
public class GameTimeWindowTrigger : TriggerBase
{
    // UO game time runs at approximately 12 real minutes per game hour
    // (24 game hours = ~288 real minutes = ~4.8 real hours)
    private static readonly TimeSpan RealTimePerGameHour = TimeSpan.FromMinutes(12);

    /// <summary>The largest value <see cref="EndHour" /> may take: the exclusive end of a whole day.</summary>
    public const int EndOfDay = 24;

    /// <inheritdoc />
    public override string TriggerType => "game_time_window";

    /// <inheritdoc />
    public override TriggerKind Kind => TriggerKind.Gate;

    /// <summary>
    /// The start hour (0-23) when the spawn window opens.
    /// </summary>
    public int StartHour { get; set; }

    /// <summary>
    /// The exclusive end hour (0-24) when the spawn window closes: a window ending at 18 covers up to
    /// 17:59, and <see cref="EndOfDay" /> means "to midnight".
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

    private TimerExecutionToken _transitionTimer;

    // Night is roughly 9pm (21) to 5am (5)
    private const int NightStartHour = 21;
    private const int NightEndHour = 5;

    // Day is roughly 5am (5) to 9pm (21)
    private const int DayStartHour = 5;
    private const int DayEndHour = 21;

    /// <summary>Creates a trigger with the documented defaults.</summary>
    public GameTimeWindowTrigger()
    {
    }

    /// <summary>
    /// Creates a trigger for a specific game-time window.
    /// </summary>
    /// <param name="startHour">Inclusive start hour, clamped to 0-23.</param>
    /// <param name="endHour">Exclusive end hour, clamped to 0-24.</param>
    public GameTimeWindowTrigger(int startHour, int endHour)
    {
        StartHour = Math.Clamp(startHour, 0, 23);
        EndHour = Math.Clamp(endHour, 0, EndOfDay);
    }

    /// <summary>
    /// Creates a night-only trigger.
    /// </summary>
    /// <returns>A trigger open only during game night.</returns>
    public static GameTimeWindowTrigger NightOnlyTrigger() => new() { NightOnly = true };

    /// <summary>
    /// Creates a day-only trigger.
    /// </summary>
    /// <returns>A trigger open only during game day.</returns>
    public static GameTimeWindowTrigger DayOnlyTrigger() => new() { DayOnly = true };

    /// <inheritdoc />
    public override bool Evaluate(in TriggerContext context) => IsWindowOpen;

    /// <inheritdoc />
    public override void Activate(ModernSpawner spawner)
    {
        base.Activate(spawner);

        // Get effective hours based on mode
        var (effectiveStart, effectiveEnd) = GetEffectiveHours();

        // Check current state and schedule next transition
        UpdateWindowState(effectiveStart, effectiveEnd);
        ScheduleNextTransition(effectiveStart, effectiveEnd);
    }

    /// <inheritdoc />
    public override void Deactivate()
    {
        _transitionTimer.Cancel();
        base.Deactivate();
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
        var spawner = Spawner;
        if (spawner?.Map == null)
        {
            IsWindowOpen = false;
            return;
        }

        Clock.GetTime(spawner.Map, spawner.X, spawner.Y, out int currentHour, out int _);

        var wasOpen = IsWindowOpen;
        IsWindowOpen = IsHourInWindow(currentHour, effectiveStart, effectiveEnd);

        // Gates report their edges by definition index, so the spawner can keep a set of open gates
        // without holding trigger references.
        if (IsWindowOpen && !wasOpen)
        {
            spawner.OnGateOpened(DefinitionIndex);
        }
        else if (!IsWindowOpen && wasOpen)
        {
            spawner.OnGateClosed(DefinitionIndex);
        }
    }

    private void ScheduleNextTransition(int effectiveStart, int effectiveEnd)
    {
        var spawner = Spawner;
        if (spawner?.Map == null)
        {
            return;
        }

        Clock.GetTime(spawner.Map, spawner.X, spawner.Y, out int currentHour, out int currentMinute);

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
        if (Spawner == null)
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
    /// <param name="currentHour">The hour to test.</param>
    /// <param name="startHour">Inclusive start hour.</param>
    /// <param name="endHour">Exclusive end hour.</param>
    /// <returns>True when the hour is inside the window.</returns>
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
    /// <param name="currentHour">The current hour.</param>
    /// <param name="targetHour">The hour being scheduled for.</param>
    /// <returns>Whole hours until the target.</returns>
    public static int CalculateHoursUntil(int currentHour, int targetHour)
    {
        if (targetHour > currentHour)
        {
            return targetHour - currentHour;
        }

        // Target is tomorrow (or we're calculating across midnight)
        return 24 - currentHour + targetHour;
    }

    /// <inheritdoc />
    public override string Serialize() =>
        // Format: game_time_window:startHour:endHour:nightOnly:dayOnly
        $"game_time_window:{StartHour}:{EndHour}:{NightOnly}:{DayOnly}";

    /// <summary>Parses a game-time window definition.</summary>
    /// <param name="definition">The definition text.</param>
    /// <returns>The parsed gate.</returns>
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
            trigger.EndHour = Math.Clamp(endHour, 0, EndOfDay);
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

    /// <summary>
    /// Parses a retired <c>timeofday:&lt;start&gt;:&lt;end&gt;[:nightOnly:dayOnly:cooldown]</c> definition
    /// into this trigger. Registered as the <c>timeofday</c> factory so saved worlds, exports and
    /// XmlSpawner imports that still carry the old text keep working.
    /// </summary>
    /// <remarks>
    /// The legacy end hour was inclusive (<c>timeofday:8:17</c> covered 08:00-17:59), so it becomes the
    /// window grammar's exclusive end: 17 becomes 18, and the legacy whole-day default 23 becomes
    /// <see cref="EndOfDay" />. The legacy polling cooldown has no counterpart on a gate and is dropped.
    /// </remarks>
    /// <param name="definition">The legacy definition text.</param>
    /// <returns>An equivalent game-time window.</returns>
    public static GameTimeWindowTrigger ParseLegacyTimeOfDay(string definition)
    {
        var parts = definition.Split(':');
        var trigger = new GameTimeWindowTrigger
        {
            // The legacy default range was 0..23 inclusive, i.e. the whole day.
            EndHour = EndOfDay
        };

        if (parts.Length > 1 && int.TryParse(parts[1], out var startHour))
        {
            trigger.StartHour = Math.Clamp(startHour, 0, 23);
        }

        if (parts.Length > 2 && int.TryParse(parts[2], out var endHour))
        {
            trigger.EndHour = Math.Clamp(endHour, 0, 23) + 1;
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
