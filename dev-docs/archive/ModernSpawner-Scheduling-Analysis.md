# ModernSpawner Scheduling Analysis: EventScheduler Integration

**Date:** December 2024
**Status:** Analysis Complete - Pending Implementation

---

## 1. Executive Summary

### The Question
Should ModernSpawner's TimeOfDayTrigger integrate with ModernUO's EventScheduler system instead of using its own polling-based approach?

### The Answer: Hybrid Approach
**Yes, but selectively.** Use EventScheduler for wall-time scheduling while keeping game-time checks separate.

| Scheduling Type | Mechanism | Reasoning |
|-----------------|-----------|-----------|
| **Wall Time Windows** | EventScheduler | Efficient event-based, avoids polling |
| **Complex Recurrence** | EventScheduler | Daily, weekly, monthly, seasonal |
| **Game Time Windows** | Custom Logic | UO-specific, not in EventScheduler |
| **Simple Time Gates** | Poll-based | Lightweight for basic cases |

---

## 2. System Comparison

### 2.1 XmlSpawner's TOD System

**Purpose:** Gate spawning based on time-of-day windows

**Features:**
- `TODStart` / `TODEnd` - Define active time window
- `TODMode` - Realtime (wall clock) or Gametime (UO clock)
- `TODInRange` - Boolean check evaluated on each spawn attempt

**Example Usage:**
```csharp
// Spawn only at night (9pm-5am game time)
spawner.TODStart = TimeSpan.FromHours(21);
spawner.TODEnd = TimeSpan.FromHours(5);
spawner.TODMode = TODModeType.Gametime;
```

**Limitations:**
- Requires polling on every spawn tick
- No complex recurrence (e.g., "weekends only")
- No seasonal support (e.g., "October only")
- No event-based activation

### 2.2 ModernUO's EventScheduler

**Purpose:** Schedule one-time or recurring events at specific wall times

**Features:**
- `IRecurrencePattern` interface for custom patterns
- Built-in patterns: Hourly, Daily, Weekly, Biweekly, Monthly, Yearly
- `AllowedDays` - Filter by day of week
- `AllowedMonths` - Filter by month
- `MonthlyOrdinalRecurrencePattern` - "First Monday of month" style
- Time zone aware
- Priority queue with efficient tick processing

**Example Usage:**
```csharp
// Run every day at 8pm EST
EventScheduler.DailyAt(new DateTime(2024, 1, 1, 20, 0, 0), () => {
    spawner.Start();
}, TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"));
```

**Limitations:**
- Wall time only (no game time support)
- Designed for callbacks, not gating
- No built-in time window concept

---

## 3. Key Insight: Different Problems

The two systems solve **fundamentally different problems**:

| Aspect | TOD System | EventScheduler |
|--------|-----------|----------------|
| **Question Answered** | "Can we spawn NOW?" | "When should we DO X?" |
| **Execution Model** | Gating/filtering | Callback execution |
| **Time Model** | Window (start-end) | Point (specific moment) |
| **Game Time** | Supported | Not applicable |

### 3.1 Where They Overlap

For **wall-time scheduling**, EventScheduler is more efficient:

| Current Approach | Better Approach |
|------------------|-----------------|
| Poll every tick: "Is it 8pm yet?" | Schedule event: "Call me at 8pm" |
| Check: "Are we in Oct 1-31?" | Schedule: "Oct 1 activate, Nov 1 deactivate" |

### 3.2 Where TOD is Unique

**Game time** is UO-specific and not something EventScheduler should handle:
- Game time moves faster than real time
- Depends on map location
- Night/day cycles are game mechanics

---

## 4. Proposed Architecture

### 4.1 Scheduling Types

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         SCHEDULING SYSTEM                                    │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │                    WALL TIME SCHEDULING                              │    │
│  │                    (EventScheduler)                                  │    │
│  │                                                                      │    │
│  │  • Daily/Weekly/Monthly recurrence                                  │    │
│  │  • Seasonal events (AllowedMonths)                                  │    │
│  │  • Day-of-week filtering (AllowedDays)                              │    │
│  │  • Complex patterns (first Friday, etc)                             │    │
│  │  • Time zone support                                                │    │
│  │                                                                      │    │
│  │  NEW: TimeWindowRecurrencePattern                                   │    │
│  │  • Fires at window start AND window end                             │    │
│  │  • Enables efficient "8pm-6am daily" without polling                │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │                    GAME TIME SCHEDULING                              │    │
│  │                    (ModernSpawner Custom)                            │    │
│  │                                                                      │    │
│  │  • Night-only / Day-only spawns                                     │    │
│  │  • Game hour windows (e.g., 9pm-5am game time)                      │    │
│  │  • Map-specific time                                                │    │
│  │                                                                      │    │
│  │  OPTIMIZED: GameTimeScheduler                                       │    │
│  │  • Calculates next transition point                                 │    │
│  │  • Schedules timer for that moment                                  │    │
│  │  • Avoids per-tick polling                                          │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 4.2 Trigger Configuration

The new scheduling system would be configured via the trigger system:

```csharp
// Simple game-time window (night only)
new GameTimeWindowTrigger { NightOnly = true }

// Specific game-time hours
new GameTimeWindowTrigger { StartHour = 21, EndHour = 5 }

// Wall-time daily window (8pm-6am real time)
new WallTimeWindowTrigger {
    StartTime = new TimeOnly(20, 0),
    EndTime = new TimeOnly(6, 0),
    Recurrence = EventScheduler.Daily
}

// Halloween event (October only, evenings)
new WallTimeWindowTrigger {
    StartTime = new TimeOnly(18, 0),
    EndTime = new TimeOnly(23, 59),
    AllowedMonths = AllowedMonths.October
}

// Weekend boss spawn (Fri-Sun, 8pm)
new WallTimeWindowTrigger {
    StartTime = new TimeOnly(20, 0),
    EndTime = new TimeOnly(23, 0),
    AllowedDays = AllowedDays.Friday | AllowedDays.Saturday | AllowedDays.Sunday
}
```

---

## 5. New Components

### 5.1 TimeWindowRecurrencePattern (Add to EventScheduler)

A new recurrence pattern that tracks both start and end of time windows:

```csharp
public class TimeWindowRecurrencePattern : IRecurrencePattern
{
    public TimeOnly WindowStart { get; }
    public TimeOnly WindowEnd { get; }
    public IRecurrencePattern BasePattern { get; } // Daily, Weekly, etc.
    public bool IsStartEvent { get; private set; } // Alternates

    // GetNextOccurrence returns either window start or window end
}
```

### 5.2 WallTimeWindowTrigger (ModernSpawner)

Integrates with EventScheduler for wall-time windows:

```csharp
public class WallTimeWindowTrigger : ITrigger
{
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public AllowedDays AllowedDays { get; set; } = AllowedDays.All;
    public AllowedMonths AllowedMonths { get; set; } = AllowedMonths.All;
    public TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Utc;

    private ScheduledEvent _startEvent;
    private ScheduledEvent _endEvent;
    private bool _inWindow;

    public bool Evaluate(TriggerContext context) => _inWindow;

    public void Activate(ModernSpawner spawner)
    {
        // Schedule window start event
        _startEvent = EventScheduler.Shared.ScheduleEvent(..., OnWindowStart, pattern);
        // Schedule window end event
        _endEvent = EventScheduler.Shared.ScheduleEvent(..., OnWindowEnd, pattern);
    }

    private void OnWindowStart() => _inWindow = true;
    private void OnWindowEnd() => _inWindow = false;
}
```

### 5.3 GameTimeWindowTrigger (Optimized)

Keep for game-time with optimized timer scheduling:

```csharp
public class GameTimeWindowTrigger : ITrigger
{
    // Calculate when the next game-time transition will occur
    // Schedule a single timer for that moment instead of polling

    private void ScheduleNextTransition()
    {
        var currentGameHour = GetGameHour();
        var nextTransitionHour = CalculateNextTransition(currentGameHour);
        var realTimeUntilTransition = GameTimeToRealTime(nextTransitionHour - currentGameHour);

        Timer.DelayCall(realTimeUntilTransition, OnTransition);
    }
}
```

---

## 6. Migration from XmlSpawner

### 6.1 TODMode Mapping

| XmlSpawner | ModernSpawner |
|------------|---------------|
| `TODMode = Realtime` | `WallTimeWindowTrigger` |
| `TODMode = Gametime` | `GameTimeWindowTrigger` |
| `TODStart/TODEnd (Realtime)` | `StartTime/EndTime` |
| `TODStart/TODEnd (Gametime)` | `StartHour/EndHour` |

### 6.2 New Capabilities

ModernSpawner will support scenarios XmlSpawner cannot:

| Scenario | XmlSpawner | ModernSpawner |
|----------|------------|---------------|
| Night-only spawns | TODStart/TODEnd | GameTimeWindowTrigger.NightOnly |
| Weekends only | Not supported | AllowedDays |
| October only | Not supported | AllowedMonths.October |
| First Friday of month | Not supported | MonthlyOrdinalRecurrencePattern |
| Every 2 weeks | Not supported | Biweekly |
| Time zone aware | Not supported | TimeZoneInfo |

---

## 7. Implementation Plan

### Phase 1: Core Integration (Current)
- [ ] Add `TimeWindowRecurrencePattern` to EventScheduler
- [ ] Create `WallTimeWindowTrigger` using EventScheduler
- [ ] Optimize `GameTimeWindowTrigger` with transition-based timers

### Phase 2: Enhanced Scheduling
- [ ] Add seasonal support (AllowedMonths)
- [ ] Add day-of-week support (AllowedDays)
- [ ] Add ordinal patterns (first Monday, etc.)
- [ ] Time zone configuration

### Phase 3: Wizard Integration
- [ ] Schedule configuration in spawner wizard
- [ ] Visual timeline preview
- [ ] Template schedules (Night, Weekend, Halloween, etc.)

### Phase 4: Migration
- [ ] Import XmlSpawner TOD settings
- [ ] Convert TODMode to appropriate trigger type

---

## 8. Example Configurations

### 8.1 Night-Only Monster Spawns (Game Time)
```yaml
triggers:
  - type: game_time_window
    nightOnly: true
```

### 8.2 Weekend Boss Event (Wall Time)
```yaml
triggers:
  - type: wall_time_window
    startTime: "20:00"
    endTime: "23:00"
    allowedDays: [Friday, Saturday, Sunday]
```

### 8.3 Halloween Event (Wall Time + Seasonal)
```yaml
triggers:
  - type: wall_time_window
    startTime: "18:00"
    endTime: "23:59"
    allowedMonths: [October]
```

### 8.4 Daily Reset at Midnight (Wall Time)
```yaml
triggers:
  - type: wall_time_point
    time: "00:00"
    recurrence: daily
    timeZone: "America/Los_Angeles"
```

---

## 9. Decision Summary

| Question | Decision |
|----------|----------|
| Use EventScheduler for wall time? | **Yes** - More efficient, more features |
| Keep game time separate? | **Yes** - UO-specific, not in EventScheduler scope |
| Add new patterns to EventScheduler? | **Yes** - TimeWindowRecurrencePattern |
| Support complex recurrence? | **Yes** - Via EventScheduler patterns |
| Backwards compatible with XmlSpawner? | **Yes** - Migration maps TODMode appropriately |

---

*Document Version: 1.0*
