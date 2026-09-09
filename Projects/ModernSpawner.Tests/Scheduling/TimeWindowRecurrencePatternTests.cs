using System;
using Server.Engines.Events;
using Server.Engines.ModernSpawner.Triggers.Scheduling;
using Xunit;

namespace Server.Engines.ModernSpawner.Tests.Scheduling;

public class TimeWindowRecurrencePatternTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static DateTime UtcAt(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void GetNextOccurrence_SameDay_ReturnsTodayAtTargetTime()
    {
        var pattern = new TimeWindowRecurrencePattern(
            new TimeOnly(22, 0),
            new TimeOnly(6, 0));

        // Reference: 2026-04-19 10:00 UTC; asking for 22:00 should return same day 22:00.
        var after = UtcAt(2026, 4, 19, 10, 0);
        var next = pattern.GetNextOccurrence(after, new TimeOnly(22, 0), Utc);

        Assert.Equal(UtcAt(2026, 4, 19, 22, 0), next);
    }

    [Fact]
    public void GetNextOccurrence_PastTime_RollsToNextDay()
    {
        var pattern = new TimeWindowRecurrencePattern(
            new TimeOnly(22, 0),
            new TimeOnly(6, 0));

        // Reference: 2026-04-19 23:00 UTC; asking for 22:00 should roll to next day.
        var after = UtcAt(2026, 4, 19, 23, 0);
        var next = pattern.GetNextOccurrence(after, new TimeOnly(22, 0), Utc);

        Assert.Equal(UtcAt(2026, 4, 20, 22, 0), next);
    }

    [Fact]
    public void GetNextOccurrence_DayOfWeekFilter_SkipsExcludedDays()
    {
        // Allow only Monday. 2026-04-19 is a Sunday.
        var pattern = new TimeWindowRecurrencePattern(
            new TimeOnly(12, 0),
            new TimeOnly(14, 0),
            allowedDays: AllowedDays.Monday);

        var after = UtcAt(2026, 4, 19, 10, 0); // Sunday 10:00
        var next = pattern.GetNextOccurrence(after, new TimeOnly(12, 0), Utc);

        // Should land on Monday 2026-04-20 at 12:00.
        Assert.Equal(UtcAt(2026, 4, 20, 12, 0), next);
        Assert.Equal(DayOfWeek.Monday, next.DayOfWeek);
    }

    [Fact]
    public void GetNextOccurrence_MultipleDaysFilter_PicksEarliestMatch()
    {
        // Allow Fridays and Saturdays.
        var pattern = new TimeWindowRecurrencePattern(
            new TimeOnly(20, 0),
            new TimeOnly(22, 0),
            allowedDays: AllowedDays.Friday | AllowedDays.Saturday);

        // Start from Monday 2026-04-20 — next Friday is 2026-04-24.
        var after = UtcAt(2026, 4, 20, 10, 0);
        var next = pattern.GetNextOccurrence(after, new TimeOnly(20, 0), Utc);

        Assert.Equal(UtcAt(2026, 4, 24, 20, 0), next);
        Assert.Equal(DayOfWeek.Friday, next.DayOfWeek);
    }

    [Fact]
    public void GetNextOccurrence_MonthFilter_SkipsExcludedMonths()
    {
        // Allow only October.
        var pattern = new TimeWindowRecurrencePattern(
            new TimeOnly(0, 0),
            new TimeOnly(23, 59),
            allowedMonths: AllowedMonths.October);

        var after = UtcAt(2026, 4, 19, 12, 0);
        var next = pattern.GetNextOccurrence(after, new TimeOnly(12, 0), Utc);

        Assert.Equal(10, next.Month);
        Assert.Equal(2026, next.Year);
    }

    [Fact]
    public void GetNextOccurrence_DayAndMonthFilters_CombineAsIntersection()
    {
        // First Friday of October.
        var pattern = new TimeWindowRecurrencePattern(
            new TimeOnly(21, 0),
            new TimeOnly(23, 0),
            allowedDays: AllowedDays.Friday,
            allowedMonths: AllowedMonths.October);

        var after = UtcAt(2026, 1, 1, 0, 0);
        var next = pattern.GetNextOccurrence(after, new TimeOnly(21, 0), Utc);

        Assert.Equal(10, next.Month);
        Assert.Equal(DayOfWeek.Friday, next.DayOfWeek);
        // First Friday of October 2026 is October 2.
        Assert.Equal(2, next.Day);
    }

    [Fact]
    public void GetNextOccurrence_NoneDaysFilter_TreatedAsAll()
    {
        // Passing AllowedDays.None should be normalized to AllowedDays.All.
        var pattern = new TimeWindowRecurrencePattern(
            new TimeOnly(12, 0),
            new TimeOnly(14, 0),
            allowedDays: AllowedDays.None);

        Assert.Equal(AllowedDays.All, pattern.AllowedDays);

        var after = UtcAt(2026, 4, 19, 10, 0); // Sunday
        var next = pattern.GetNextOccurrence(after, new TimeOnly(12, 0), Utc);

        // Any day is valid, so should land on same day.
        Assert.Equal(UtcAt(2026, 4, 19, 12, 0), next);
    }

    [Fact]
    public void GetNextOccurrence_NoneMonthsFilter_TreatedAsAll()
    {
        var pattern = new TimeWindowRecurrencePattern(
            new TimeOnly(12, 0),
            new TimeOnly(14, 0),
            allowedMonths: AllowedMonths.None);

        Assert.Equal(AllowedMonths.All, pattern.AllowedMonths);
    }

    [Fact]
    public void Constructor_StoresWindowBoundaries()
    {
        var pattern = new TimeWindowRecurrencePattern(
            new TimeOnly(8, 30),
            new TimeOnly(17, 15));

        Assert.Equal(new TimeOnly(8, 30), pattern.WindowStart);
        Assert.Equal(new TimeOnly(17, 15), pattern.WindowEnd);
    }

    [Fact]
    public void Constructor_DefaultBasePattern_IsDaily()
    {
        var pattern = new TimeWindowRecurrencePattern(
            new TimeOnly(0, 0),
            new TimeOnly(23, 59));

        Assert.NotNull(pattern.BasePattern);
        Assert.Equal(EventScheduler.Daily.GetType(), pattern.BasePattern.GetType());
    }
}

public class TimeWindowScheduledEventTests
{
    [Fact]
    public void Constructor_NullOnWindowOpen_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TimeWindowScheduledEvent(
                new TimeOnly(22, 0),
                new TimeOnly(6, 0),
                onWindowOpen: null,
                onWindowClose: () => { }));
    }

    [Fact]
    public void Constructor_NullOnWindowClose_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TimeWindowScheduledEvent(
                new TimeOnly(22, 0),
                new TimeOnly(6, 0),
                onWindowOpen: () => { },
                onWindowClose: null));
    }

    [Fact]
    public void Constructor_WindowStartsClosed()
    {
        var ev = new TimeWindowScheduledEvent(
            new TimeOnly(22, 0),
            new TimeOnly(6, 0),
            onWindowOpen: () => { },
            onWindowClose: () => { });

        Assert.False(ev.IsWindowOpen);
    }

    [Fact]
    public void OnEvent_TogglesOnEachCall()
    {
        // Note: without Schedule(), _nextIsStart defaults to false, so the first OnEvent
        // fires the close branch. The test validates strict alternation from that baseline.
        var opens = 0;
        var closes = 0;
        var ev = new TimeWindowScheduledEvent(
            new TimeOnly(22, 0),
            new TimeOnly(6, 0),
            onWindowOpen: () => opens++,
            onWindowClose: () => closes++);

        Assert.False(ev.IsWindowOpen);

        ev.OnEvent();
        Assert.Equal(1, closes);
        Assert.Equal(0, opens);

        ev.OnEvent();
        Assert.True(ev.IsWindowOpen);
        Assert.Equal(1, opens);
        Assert.Equal(1, closes);

        ev.OnEvent();
        Assert.False(ev.IsWindowOpen);
        Assert.Equal(1, opens);
        Assert.Equal(2, closes);
    }

    [Fact]
    public void OnEvent_AlternatesAcrossMultipleCycles()
    {
        var sequence = new System.Collections.Generic.List<string>();
        var ev = new TimeWindowScheduledEvent(
            new TimeOnly(22, 0),
            new TimeOnly(6, 0),
            onWindowOpen: () => sequence.Add("open"),
            onWindowClose: () => sequence.Add("close"));

        for (var i = 0; i < 6; i++)
        {
            ev.OnEvent();
        }

        Assert.Equal(new[] { "close", "open", "close", "open", "close", "open" }, sequence);
    }
}
