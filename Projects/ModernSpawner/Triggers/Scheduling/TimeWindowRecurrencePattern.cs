using System;
using Server.Engines.Events;

namespace Server.Engines.ModernSpawner.Triggers.Scheduling;

/// <summary>
/// A recurrence pattern that manages time windows with start and end times.
/// This pattern alternates between firing at window start and window end times.
/// </summary>
public class TimeWindowRecurrencePattern : IRecurrencePattern
{
    /// <summary>
    /// The time when the window opens.
    /// </summary>
    public TimeOnly WindowStart { get; }

    /// <summary>
    /// The time when the window closes.
    /// </summary>
    public TimeOnly WindowEnd { get; }

    /// <summary>
    /// The base recurrence pattern (e.g., Daily, Weekly).
    /// </summary>
    public IRecurrencePattern BasePattern { get; }

    /// <summary>
    /// Filter by allowed days of the week.
    /// </summary>
    public AllowedDays AllowedDays { get; }

    /// <summary>
    /// Filter by allowed months.
    /// </summary>
    public AllowedMonths AllowedMonths { get; }

    public TimeWindowRecurrencePattern(
        TimeOnly windowStart,
        TimeOnly windowEnd,
        IRecurrencePattern basePattern = null,
        AllowedDays allowedDays = AllowedDays.All,
        AllowedMonths allowedMonths = AllowedMonths.All)
    {
        WindowStart = windowStart;
        WindowEnd = windowEnd;
        BasePattern = basePattern ?? EventScheduler.Daily;
        AllowedDays = allowedDays == AllowedDays.None ? AllowedDays.All : allowedDays;
        AllowedMonths = allowedMonths == AllowedMonths.None ? AllowedMonths.All : allowedMonths;
    }

    public DateTime GetNextOccurrence(DateTime afterUtc, TimeOnly time, TimeZoneInfo timeZone)
    {
        // Delegate to base pattern but respect our day/month filters
        var local = TimeZoneInfo.ConvertTimeFromUtc(afterUtc, timeZone);

        // Search up to 400 days ahead (covers leap years + buffer)
        for (var dayOffset = 0; dayOffset < 400; dayOffset++)
        {
            var candidateDate = local.Date.AddDays(dayOffset);

            // Check month filter
            var monthFlag = (AllowedMonths)(1 << (candidateDate.Month - 1));
            if ((AllowedMonths & monthFlag) == 0)
            {
                continue;
            }

            // Check day of week filter
            var dayFlag = (AllowedDays)(1 << (int)candidateDate.DayOfWeek);
            if ((AllowedDays & dayFlag) == 0)
            {
                continue;
            }

            // Create candidate datetime with the requested time
            var candidate = new DateTime(
                candidateDate.Year,
                candidateDate.Month,
                candidateDate.Day,
                time.Hour,
                time.Minute,
                time.Second);

            // Must be after the reference time and valid in the timezone
            if (candidate > local && !timeZone.IsInvalidTime(candidate))
            {
                return candidate.LocalToUtc(timeZone);
            }
        }

        return DateTime.MaxValue;
    }
}

/// <summary>
/// Scheduled event that manages a time window (start and end).
/// Fires callbacks when the window opens and closes.
/// </summary>
public class TimeWindowScheduledEvent : BaseScheduledEvent
{
    private readonly Action _onWindowOpen;
    private readonly Action _onWindowClose;
    private readonly TimeOnly _windowStart;
    private readonly TimeOnly _windowEnd;
    private readonly AllowedDays _allowedDays;
    private readonly AllowedMonths _allowedMonths;

    private bool _isWindowOpen;
    private bool _nextIsStart;

    public bool IsWindowOpen => _isWindowOpen;

    public TimeWindowScheduledEvent(
        TimeOnly windowStart,
        TimeOnly windowEnd,
        Action onWindowOpen,
        Action onWindowClose,
        AllowedDays allowedDays = AllowedDays.All,
        AllowedMonths allowedMonths = AllowedMonths.All)
    {
        _windowStart = windowStart;
        _windowEnd = windowEnd;
        _onWindowOpen = onWindowOpen ?? throw new ArgumentNullException(nameof(onWindowOpen));
        _onWindowClose = onWindowClose ?? throw new ArgumentNullException(nameof(onWindowClose));
        _allowedDays = allowedDays == AllowedDays.None ? AllowedDays.All : allowedDays;
        _allowedMonths = allowedMonths == AllowedMonths.None ? AllowedMonths.All : allowedMonths;
    }

    /// <summary>
    /// Schedules the event and determines initial window state.
    /// </summary>
    public new void Schedule(DateTime startAfter, TimeZoneInfo timeZone = null)
    {
        var tz = timeZone ?? TimeZoneInfo.Utc;
        var local = TimeZoneInfo.ConvertTimeFromUtc(
            startAfter.Kind == DateTimeKind.Utc ? startAfter : startAfter.LocalToUtc(tz),
            tz);

        // Determine if we're currently in a valid window
        _isWindowOpen = IsInWindow(local);
        _nextIsStart = !_isWindowOpen;

        base.Schedule(startAfter, tz);
    }

    protected override DateTime GetNextOccurrence(DateTime afterUtc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(afterUtc, TimeZone);
        var targetTime = _nextIsStart ? _windowStart : _windowEnd;

        // Search for the next valid occurrence
        for (var dayOffset = 0; dayOffset < 400; dayOffset++)
        {
            var candidateDate = local.Date.AddDays(dayOffset);

            // Check month filter
            var monthFlag = (AllowedMonths)(1 << (candidateDate.Month - 1));
            if ((_allowedMonths & monthFlag) == 0)
            {
                continue;
            }

            // Check day of week filter
            var dayFlag = (AllowedDays)(1 << (int)candidateDate.DayOfWeek);
            if ((_allowedDays & dayFlag) == 0)
            {
                continue;
            }

            var candidate = new DateTime(
                candidateDate.Year,
                candidateDate.Month,
                candidateDate.Day,
                targetTime.Hour,
                targetTime.Minute,
                targetTime.Second);

            if (candidate > local && !TimeZone.IsInvalidTime(candidate))
            {
                return candidate.LocalToUtc(TimeZone);
            }
        }

        return DateTime.MaxValue;
    }

    public override void OnEvent()
    {
        if (_nextIsStart)
        {
            _isWindowOpen = true;
            _onWindowOpen();
        }
        else
        {
            _isWindowOpen = false;
            _onWindowClose();
        }

        // Toggle for next occurrence
        _nextIsStart = !_nextIsStart;
    }

    private bool IsInWindow(DateTime local)
    {
        // Check day/month filters first
        var monthFlag = (AllowedMonths)(1 << (local.Month - 1));
        if ((_allowedMonths & monthFlag) == 0)
        {
            return false;
        }

        var dayFlag = (AllowedDays)(1 << (int)local.DayOfWeek);
        if ((_allowedDays & dayFlag) == 0)
        {
            return false;
        }

        var currentTime = TimeOnly.FromDateTime(local);

        // Handle windows that span midnight
        if (_windowEnd < _windowStart)
        {
            // Window spans midnight (e.g., 22:00 to 06:00)
            return currentTime >= _windowStart || currentTime < _windowEnd;
        }

        // Normal window (e.g., 08:00 to 17:00)
        return currentTime >= _windowStart && currentTime < _windowEnd;
    }
}
