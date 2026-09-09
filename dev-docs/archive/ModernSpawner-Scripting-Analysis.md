# ModernSpawner Scripting Analysis: Final Decision

**Date:** December 2024
**Status:** Decision Made
**Decision:** Custom Expression Engine + Wizard-Based UI + JSON/YAML Export

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Background Analysis](#2-background-analysis)
3. [Decision: No Magic Syntax](#3-decision-no-magic-syntax)
4. [Architecture Overview](#4-architecture-overview)
5. [Expression Language Specification](#5-expression-language-specification)
6. [Data Format Specification](#6-data-format-specification)
7. [Implementation Plan](#7-implementation-plan)

---

## 1. Executive Summary

### 1.1 The Decision

| Aspect | Decision | Reasoning |
|--------|----------|-----------|
| **Expression Engine** | Custom (not CEL) | Simpler, GM-friendly, no dependencies |
| **User Interface** | Wizard-based | No syntax learning curve |
| **Spawner Storage** | JSON | Human-readable, universal |
| **Script Storage** | YAML | More readable for complex logic |
| **Power Users** | Raw JSON/YAML access | Import/export via external tool |

### 1.2 Key Principles

1. **No magic syntax for non-technical users** - Wizards guide all interactions
2. **Human-readable exports** - JSON/YAML files on server, inspectable and version-controllable
3. **Power user escape hatch** - External tool exposes raw formats for advanced users
4. **Progressive complexity** - Simple tasks are simple, complex tasks are possible

---

## 2. Background Analysis

### 2.1 XmlSpawner's Problem

XmlSpawner uses a terse but cryptic syntax:
```
#CONDITION,TrigMob.Karma<0&TrigMob.Fame>5000/Daemon/Hue/0x4001/Str/INC,50,100
```

**Issues:**
- Requires memorizing syntax rules
- Easy to make typos
- Hard to read and debug
- No discoverability
- Intimidating for non-technical staff

### 2.2 Why Not CEL?

We evaluated [CEL (Common Expression Language)](https://cel.dev/) but decided against it:

| Factor | CEL | Our Needs |
|--------|-----|-----------|
| Syntax | Programmer-oriented (`&&`, `\|\|`) | GM-friendly |
| Capabilities | Full expression language | Simple conditions |
| Dependencies | NuGet + protobuf preference | Self-contained |
| Complexity | Overkill | Right-sized |

**Verdict:** Build a simple custom expression evaluator tailored to UO concepts.

### 2.3 Why Not Terse Syntax?

We considered a simplified terse syntax:
```
Orc x5 | Hue=0x8000 | when: isNight()
```

**Issues:**
- Still requires learning syntax
- Still prone to typos
- Still not discoverable
- "Simple" syntax becomes complex for complex cases

**Verdict:** Any text-based syntax has a learning curve. Wizards eliminate it.

---

## 3. Decision: No Magic Syntax

### 3.1 The Wizard Approach

Instead of users typing syntax, they interact with guided forms:

```
┌─────────────────────────────────────────────────────────────┐
│  Add Spawn Entry                                      [X]   │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  Type: [Orc_____________] [Browse...]                      │
│                                                             │
│  Count: [5___]  □ Unlimited                                │
│                                                             │
│  ─── Properties ───────────────────────────────────────    │
│  │ Property    │ Value Type      │ Value              │    │
│  │─────────────│─────────────────│────────────────────│    │
│  │ Hue         │ ● Fixed ○ Random│ [0x8000__________] │    │
│  │ Name        │ [Text_______]   │ [Elite Orc_______] │    │
│  │ Str         │ ○ Fixed ● Random│ Min:[80] Max:[120] │    │
│  │             │                 │                    │    │
│  │ [+ Add Property]                                   │    │
│  └─────────────────────────────────────────────────────    │
│                                                             │
│  ─── Condition (Optional) ─────────────────────────────    │
│  □ Only spawn when condition is met                        │
│                                                             │
│    ┌─ Condition Builder ─────────────────────────────┐     │
│    │ [Time of Day    ▼] [is        ▼] [Night     ▼] │     │
│    │ [+ Add condition (AND)]  [+ Add condition (OR)] │     │
│    └─────────────────────────────────────────────────┘     │
│                                                             │
│                              [Cancel]  [Save Entry]        │
└─────────────────────────────────────────────────────────────┘
```

### 3.2 Benefits of Wizard Approach

| Benefit | Description |
|---------|-------------|
| **Zero learning curve** | Forms are self-explanatory |
| **Discoverability** | Users see all options |
| **Validation** | Invalid inputs prevented at entry time |
| **Type safety** | Dropdowns prevent typos |
| **Accessibility** | Works for all skill levels |
| **Consistency** | Everyone uses the same interface |

### 3.3 The Expression Language Still Exists

The wizard *generates* expressions behind the scenes:

```
User selects:  "Time of Day" "is" "Night"
Generates:     isNight()

User selects:  "Triggering Player" "Karma" "less than" "0"
Generates:     trigMob.Karma < 0

User selects:  "Random between" "80" "and" "120"
Generates:     random(80, 120)
```

Power users can view/edit the raw expression in the external tool.

---

## 4. Architecture Overview

### 4.1 System Layers

```
┌─────────────────────────────────────────────────────────────────────┐
│                         USER INTERFACES                              │
├──────────────────────────────┬──────────────────────────────────────┤
│      IN-GAME (Gumps)         │         EXTERNAL TOOL                │
│  ┌────────────────────────┐  │  ┌────────────────────────────────┐  │
│  │ • Spawner Wizard       │  │  │ • Advanced Spawner Wizard      │  │
│  │ • Entry Wizard         │  │  │ • Raw JSON/YAML Editor         │  │
│  │ • Condition Builder    │  │  │ • Bulk Operations              │  │
│  │ • Quick Actions        │  │  │ • Import/Export                │  │
│  │ • Status Viewer        │  │  │ • Template Library             │  │
│  └────────────────────────┘  │  │ • Validation & Testing         │  │
│                              │  │ • Version History              │  │
│                              │  └────────────────────────────────┘  │
├──────────────────────────────┴──────────────────────────────────────┤
│                         DATA LAYER                                   │
│  ┌────────────────────────────────────────────────────────────────┐ │
│  │                    ModernSpawner Engine                        │ │
│  │  • Expression Evaluator (custom, simple)                       │ │
│  │  • Trigger System                                              │ │
│  │  • Positioning System                                          │ │
│  │  • Script Execution                                            │ │
│  └────────────────────────────────────────────────────────────────┘ │
├─────────────────────────────────────────────────────────────────────┤
│                        STORAGE LAYER                                 │
│  ┌─────────────────────┐  ┌─────────────────────────────────────┐  │
│  │  Binary Serialization│  │  Human-Readable Export              │  │
│  │  (Runtime/Saves)     │  │  • spawners/*.json (spawner config) │  │
│  │                      │  │  • scripts/*.yaml (complex scripts) │  │
│  └─────────────────────┘  └─────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
```

### 4.2 Data Flow

```
                    IN-GAME WIZARD
                          │
                          ▼
              ┌───────────────────────┐
              │   Wizard generates    │
              │   structured data     │
              └───────────────────────┘
                          │
          ┌───────────────┼───────────────┐
          ▼               ▼               ▼
    ┌──────────┐   ┌──────────────┐   ┌──────────────┐
    │  Binary  │   │  JSON Export │   │  YAML Export │
    │  (saves) │   │  (spawners)  │   │  (scripts)   │
    └──────────┘   └──────────────┘   └──────────────┘
                          │               │
                          └───────┬───────┘
                                  ▼
                    ┌───────────────────────┐
                    │    EXTERNAL TOOL      │
                    │  • View/edit raw      │
                    │  • Import/export      │
                    │  • Bulk operations    │
                    └───────────────────────┘
```

---

## 5. Expression Language Specification

### 5.1 Design Goals

1. **Human-readable** - Even raw expressions should be understandable
2. **GM-friendly** - Use words over symbols where practical
3. **UO-specific** - Built-in functions for game concepts
4. **Safe** - No side effects, no dangerous operations

### 5.2 Syntax

#### Property Access
```
trigMob.Karma
trigMob.Str
target.Hits
target.MaxHits
spawner.SpawnedCount
spawner.Running
```

#### Comparisons
```
trigMob.Karma < 0
target.Hits >= 100
trigMob.Name == "Admin"
target.Hue != 0
```

#### Boolean Logic (word-based for readability)
```
trigMob.Criminal and trigMob.Karma < -100
trigMob.Player or trigMob.AccessLevel > 0
not trigMob.Alive
```

#### Arithmetic
```
target.Str + 50
target.MaxHits * 2
(target.Str + target.Dex) / 2
```

#### Built-in Functions
```
// Random values
random(50, 100)              // Random integer between 50-100
randomFrom(1, 5, 10, 25)     // Random pick from list

// Game state
playersNearby(20)            // Count players within 20 tiles
isNight()                    // True if nighttime in game
isDay()                      // True if daytime in game
gameHour()                   // Current game hour (0-23)

// Checks
hasItem(trigMob, "Gold")     // Check if mobile has item type
hasSkill(trigMob, "Magery", 80)  // Check skill level
inRegion("Britain")          // Check if spawner is in region
```

#### Conditional Values
```
if trigMob.Karma < 0 then "Evil" else "Good"
if isNight() then 0x8000 else 0x8001
```

### 5.3 What Wizards Generate

| Wizard Selection | Generated Expression |
|------------------|---------------------|
| Time is Night | `isNight()` |
| Time is Day | `isDay()` |
| Player Karma < 0 | `trigMob.Karma < 0` |
| Players nearby > 5 (range 20) | `playersNearby(20) > 5` |
| Random 50-100 | `random(50, 100)` |
| Fixed value 500 | `500` |
| Copy from player's Str | `trigMob.Str` |

---

## 6. Data Format Specification

### 6.1 Spawner Configuration (JSON)

```json
{
  "$schema": "modernspawner/v1/spawner.json",
  "id": "guid-here",
  "name": "Britain Guard Spawner",
  "location": { "x": 1234, "y": 5678, "z": 0, "map": "Felucca" },

  "timing": {
    "minDelay": "00:05:00",
    "maxDelay": "00:10:00"
  },

  "area": {
    "homeRange": 10,
    "spawnRange": 5
  },

  "entries": [
    {
      "type": "Guard",
      "maxCount": 3,
      "properties": {
        "Hue": { "type": "fixed", "value": "0x8000" },
        "Name": { "type": "fixed", "value": "Britain Guard" },
        "Str": { "type": "random", "min": 80, "max": 120 }
      },
      "condition": null
    },
    {
      "type": "ArcherGuard",
      "maxCount": 2,
      "properties": {},
      "condition": {
        "expression": "playersNearby(30) > 3",
        "description": "Only when more than 3 players nearby"
      }
    }
  ],

  "triggers": [
    {
      "type": "proximity",
      "range": 15,
      "playerOnly": true
    }
  ]
}
```

### 6.2 Complex Scripts (YAML)

For spawners with complex logic (boss encounters, events):

```yaml
# dragon-boss-encounter.yaml
$schema: modernspawner/v1/script.yaml

name: Dragon Boss Encounter
description: Multi-phase dragon fight with minion spawns

entries:
  - type: GreaterDragon
    maxCount: 1
    condition:
      expression: playersNearby(30) >= 5
      description: Requires 5+ players nearby
    properties:
      Hue:
        type: fixed
        value: 0x8000
      Name:
        type: fixed
        value: "Nightmare"
      Str:
        type: expression
        expression: 500 + (playersNearby(30) * 20)
        description: Scales with player count
      Fame:
        type: fixed
        value: 25000

onBeforeSpawn:
  - condition: spawner.SpawnedCount >= 1
    action: cancel
    reason: Only one dragon at a time

onAfterSpawn:
  - action: set
    target:
      name: DragonAltar
    properties:
      Hue: 0x4001
    description: Light up the altar

  - action: spawn
    spawner: DragonMinions
    subgroup: 1
    condition: playersNearby(20) > 3
    description: Spawn minions if enough players

onEntityKilled:
  - condition: target.Name == "Nightmare"
    actions:
      - action: spawn
        spawner: TreasureChestSpawner
        subgroup: 0
      - action: broadcast
        message: "The Nightmare has been defeated!"
        range: 50
```

### 6.3 Why JSON for Spawners, YAML for Scripts?

| Format | Used For | Reasoning |
|--------|----------|-----------|
| **JSON** | Spawner config | Simple structure, wide tool support, strict syntax prevents errors |
| **YAML** | Complex scripts | More readable for multi-line content, supports comments, less visual noise |

---

## 7. Implementation Plan

### 7.1 Phase 1: Expression Engine ✅ COMPLETE

- [x] Implement expression tokenizer/lexer (numbers, strings, hex, keywords)
- [x] Implement expression parser with correct operator precedence
- [x] Implement property access (`trigMob.Karma`, `target.Hits`)
- [x] Implement comparison operators (`<`, `<=`, `>`, `>=`, `==`, `!=`)
- [x] Implement boolean operators (`and`, `or`, `not`)
- [x] Implement arithmetic operators (`+`, `-`, `*`, `/`)
- [x] Implement built-in functions (`random()`, `isNight()`, `playersNearby()`, etc.)
- [x] Add expression compilation and caching
- [x] Unit tests (131 tests passing)

### 7.2 Phase 2: Scheduling Integration ✅ COMPLETE

*See [ModernSpawner-Scheduling-Analysis.md](ModernSpawner-Scheduling-Analysis.md) for details.*

- [x] Add `TimeWindowRecurrencePattern` to EventScheduler
- [x] Create `WallTimeWindowTrigger` using EventScheduler
- [x] Optimize `GameTimeWindowTrigger` with transition-based timers
- [x] Add seasonal support (`AllowedMonths`)
- [x] Add day-of-week support (`AllowedDays`)
- [x] Time zone configuration

### 7.3 Phase 3: Data Formats ✅ COMPLETE

- [x] Define JSON schema for spawner configuration (`SpawnerExportModels.cs`)
- [x] Define YAML schema for complex scripts (`ScriptExportModels.cs`)
- [x] Implement JSON serializer/deserializer (`SpawnerJsonExporter.cs`, `SpawnerJsonImporter.cs`)
- [x] Implement YAML serializer/deserializer (`ScriptYamlSerializer.cs`)
- [x] File-based export/import system (`SpawnerFileManager.cs`)
- [ ] Migration tool from current format

### 7.4 Phase 4: In-Game Wizards ✅ COMPLETE

- [x] Basic settings wizard (timing, range) - `SpawnerSettingsGump.cs`
- [x] Spawn entry wizard with property builder - `SpawnerEntryWizardGump.cs`, `PropertyBuilderGump.cs`
- [x] Condition builder wizard - `ConditionBuilderGump.cs`
- [x] Trigger/schedule configuration wizard - `TriggerConfigGump.cs`
- [x] Script configuration wizard - `ScriptsConfigGump.cs`
- [x] "Export to File" functionality - `SpawnerExportGump.cs`, `SpawnerImportGump.cs`
- [x] Integration with main spawner gump (Settings button, Entry Wizard button)

### 7.5 Phase 5: External Tool

- [ ] Architecture decision (Web vs Desktop)
- [ ] Advanced spawner wizard
- [ ] Raw JSON/YAML editor with syntax highlighting
- [ ] Validation engine
- [ ] Template library system
- [ ] Bulk operations
- [ ] Import/export interface

### 7.6 Phase 6: Advanced Features

- [ ] Version history / rollback
- [ ] Visual map editor
- [ ] Team collaboration features
- [ ] Script debugging tools

---

## Appendix A: Migration from XmlSpawner

### A.1 Syntax Mapping

| XmlSpawner | ModernSpawner Expression |
|------------|-------------------------|
| `TrigMob.Karma` | `trigMob.Karma` |
| `TrigMob.Karma<0` | `trigMob.Karma < 0` |
| `PLAYERSINRANGE,20` | `playersNearby(20)` |
| `{TrigMob.Str}` | `trigMob.Str` |
| `INC,50,100` | `random(50, 100)` |
| `Karma<0&Fame>5000` | `trigMob.Karma < 0 and trigMob.Fame > 5000` |

### A.2 Automatic Migration

The XML importer will:
1. Parse XmlSpawner format
2. Convert to internal representation
3. Export as JSON/YAML
4. Flag any unsupported features for manual review

---

## Appendix B: Comparison Summary

| Approach | Pros | Cons | Verdict |
|----------|------|------|---------|
| XmlSpawner syntax | Familiar | Cryptic, error-prone | ❌ |
| CEL | Industry standard | Overkill, dependencies | ❌ |
| Terse custom syntax | Shorter than JSON | Still requires learning | ❌ |
| JSON everywhere | Universal | Verbose, hard to read | ❌ |
| **Wizard + JSON/YAML** | Zero learning curve, readable exports | More dev work | ✅ |

---

*Document Version: 2.0 - Final Decision*
