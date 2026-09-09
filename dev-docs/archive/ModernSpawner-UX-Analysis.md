# ModernSpawner UX Analysis: Final Decision

**Date:** December 2024
**Status:** Decision Made
**Decision:** Wizard-Based UI for All Users + JSON/YAML Export for Power Users

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Design Philosophy](#2-design-philosophy)
3. [User Personas & Workflows](#3-user-personas--workflows)
4. [In-Game Wizard System](#4-in-game-wizard-system)
5. [External Tool Specification](#5-external-tool-specification)
6. [Data Export Strategy](#6-data-export-strategy)
7. [Implementation Roadmap](#7-implementation-roadmap)

---

## 1. Executive Summary

### 1.1 The Problem

Game Masters and Event Masters need to create and manage spawners without:
- Learning cryptic syntax (`Orc/Hue/0x8000/Name/Elite`)
- Making typos in text-based inputs
- Understanding programming concepts
- Having direct server file access

### 1.2 The Solution

**Wizard-based interfaces everywhere.**

| Interface | Purpose | Target User |
|-----------|---------|-------------|
| **In-Game Wizards** | Spawner placement, basic configuration | All GMs |
| **External Tool Wizards** | Complex scripts, bulk operations | Senior GMs, Developers |
| **Raw JSON/YAML** | Power user editing, version control | Developers only |

### 1.3 Key Principles

1. **No magic syntax** - Users never type expressions manually
2. **Progressive disclosure** - Simple by default, advanced options available
3. **Discoverability** - All options visible in wizard forms
4. **Validation** - Errors caught at input time, not runtime
5. **Human-readable exports** - JSON/YAML files are inspectable

---

## 2. Design Philosophy

### 2.1 Why Wizards Over Syntax

| Syntax-Based | Wizard-Based |
|--------------|--------------|
| Requires memorization | Self-documenting |
| Error-prone | Validates inputs |
| Intimidating | Approachable |
| Steep learning curve | Immediate productivity |
| Power users only | Everyone |

### 2.2 The Wizard Generates Everything

Behind every wizard interaction is structured data:

```
┌─────────────────────────────────────────────────────────────┐
│                    USER INTERACTION                          │
│                                                             │
│   "I want Str to be random between 80 and 120"             │
│                                                             │
│   [Property: Str ▼] [Value: Random ▼] Min:[80] Max:[120]   │
│                                                             │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    GENERATED DATA                            │
│                                                             │
│   {                                                         │
│     "Str": {                                                │
│       "type": "random",                                     │
│       "min": 80,                                            │
│       "max": 120                                            │
│     }                                                       │
│   }                                                         │
│                                                             │
│   Expression (if needed): random(80, 120)                   │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### 2.3 Power Users Have Escape Hatches

For those who want direct control:
- External tool can show/edit raw JSON/YAML
- Import/export functionality
- Version control friendly files
- Bulk editing via text

---

## 3. User Personas & Workflows

### 3.1 Event Master (Low Technical)

**Profile:**
- Creates temporary event spawners
- Needs quick iteration
- Minimal UO scripting knowledge
- Works in-game during events

**Workflow:**
```
1. [Place Spawner] command targets location
2. Wizard opens for basic settings
3. Add entries via "Add Creature" wizard
4. Set timing via slider/dropdown
5. Enable spawner
6. Watch and adjust
```

**Required Features:**
- ✅ One-click spawner placement
- ✅ Creature type browser/search
- ✅ Simple count/timing controls
- ✅ Start/stop toggle
- ❌ Complex scripting

### 3.2 World Builder (Medium Technical)

**Profile:**
- Populates dungeons and areas
- Understands spawner concepts
- Works on multiple spawners
- Needs templates and bulk operations

**Workflow:**
```
1. Open External Tool
2. Browse existing spawners by region
3. Use template for "Dungeon Level 1 Pack"
4. Customize creature types and counts
5. Deploy multiple spawners
6. Test in-game
```

**Required Features:**
- ✅ Template library
- ✅ Bulk spawner creation
- ✅ Region-based organization
- ✅ Copy/paste spawners
- ✅ Conditional spawns (time of day, etc.)
- ❌ Raw expression editing

### 3.3 Senior GM (Medium-High Technical)

**Profile:**
- Creates boss encounters
- Designs quest spawners
- Comfortable with conditionals
- Uses both in-game and external tools

**Workflow:**
```
1. Design encounter in External Tool
2. Use advanced wizard for multi-phase boss
3. Set up trigger conditions via builder
4. Add spawn chains (minions on boss damage)
5. Export and deploy
6. Test with players
```

**Required Features:**
- ✅ Condition builder (visual)
- ✅ Spawn chain configuration
- ✅ Event hooks (onKill, onSpawn)
- ✅ Preview/simulation mode
- ⚠️ View generated expressions (read-only)

### 3.4 Developer (High Technical)

**Profile:**
- Creates complex systems
- Prefers code/text editing
- Uses version control
- May edit files directly

**Workflow:**
```
1. Export spawners to JSON/YAML
2. Edit in VS Code with syntax highlighting
3. Bulk modifications via find/replace
4. Version control changes
5. Import back to server
6. CI/CD deployment
```

**Required Features:**
- ✅ JSON/YAML export/import
- ✅ Raw editor mode in external tool
- ✅ Schema files for IDE support
- ✅ Validation CLI tool
- ✅ Direct expression editing

---

## 4. In-Game Wizard System

### 4.1 Wizard Flow

```
┌─────────────────────────────────────────────────────────────┐
│                    SPAWNER WIZARD FLOW                       │
└─────────────────────────────────────────────────────────────┘

[Target Location] ──► [Basic Settings] ──► [Add Entries] ──► [Triggers]
        │                    │                   │              │
        ▼                    ▼                   ▼              ▼
   Place spawner       Name, timing        Creature type    Proximity
   in world           Range settings       Properties       Speech
                                           Conditions       Time of day
```

### 4.2 Basic Settings Wizard

```
┌─────────────────────────────────────────────────────────────┐
│  Spawner Settings                                     [X]   │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  Name: [Guard Post Alpha___________________________]        │
│                                                             │
│  ─── Timing ────────────────────────────────────────────   │
│  Spawn Delay:                                               │
│    Minimum: [5] minutes                                     │
│    Maximum: [10] minutes                                    │
│                                                             │
│  ─── Area ──────────────────────────────────────────────   │
│  Home Range: [10] tiles  (how far creatures wander)        │
│  Spawn Range: [5] tiles  (where creatures appear)          │
│                                                             │
│  ─── Options ───────────────────────────────────────────   │
│  □ Group Mode (respawn all when all dead)                  │
│  ☑ Currently Running                                        │
│                                                             │
│  [Entries: 3] [Triggers: 1]    [Delete Spawner]            │
│                                                             │
│                              [Cancel]  [Save]              │
└─────────────────────────────────────────────────────────────┘
```

### 4.3 Add Entry Wizard

```
┌─────────────────────────────────────────────────────────────┐
│  Add Spawn Entry                                      [X]   │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  Creature/Item Type:                                        │
│  [Orc_________________________] [Browse...] [Search...]    │
│                                                             │
│  Maximum Count: [5___]                                      │
│                                                             │
│  ─── Properties (Optional) ─────────────────────────────   │
│                                                             │
│  [+ Add Property]                                          │
│                                                             │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ Property: [Hue         ▼]                           │   │
│  │ Value:    ● Fixed  ○ Random  ○ From Player          │   │
│  │           [0x8000_______]                           │   │
│  │                                            [Remove] │   │
│  └─────────────────────────────────────────────────────┘   │
│                                                             │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ Property: [Str         ▼]                           │   │
│  │ Value:    ○ Fixed  ● Random  ○ From Player          │   │
│  │           Min: [80___] Max: [120__]                 │   │
│  │                                            [Remove] │   │
│  └─────────────────────────────────────────────────────┘   │
│                                                             │
│  ─── Spawn Condition (Optional) ────────────────────────   │
│  □ Only spawn when condition is met                        │
│                                                             │
│                              [Cancel]  [Save Entry]        │
└─────────────────────────────────────────────────────────────┘
```

### 4.4 Condition Builder Wizard

```
┌─────────────────────────────────────────────────────────────┐
│  Condition Builder                                    [X]   │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  Only spawn when ALL of these are true:                    │
│                                                             │
│  ┌─ Condition 1 ───────────────────────────────────────┐   │
│  │ [Time of Day  ▼] [is           ▼] [Night       ▼]  │   │
│  │                                            [Remove] │   │
│  └─────────────────────────────────────────────────────┘   │
│                                                             │
│  ┌─ Condition 2 ───────────────────────────────────────┐   │
│  │ [Players Nearby▼] [greater than▼] [3__] in [20] tiles│  │
│  │                                            [Remove] │   │
│  └─────────────────────────────────────────────────────┘   │
│                                                             │
│  [+ Add Condition (AND)]                                   │
│                                                             │
│  ─── OR add any of these conditions: ───────────────────   │
│  (none)                                                     │
│  [+ Add Condition (OR)]                                    │
│                                                             │
│  ─── Preview ───────────────────────────────────────────   │
│  "Spawn when it's nighttime AND more than 3 players       │
│   are within 20 tiles"                                     │
│                                                             │
│                              [Cancel]  [Save Condition]    │
└─────────────────────────────────────────────────────────────┘
```

### 4.5 Condition Options (Dropdown Values)

**Subject Options:**
- Time of Day
- Players Nearby
- Triggering Player's Karma
- Triggering Player's Fame
- Triggering Player's Skill
- Spawner's Current Count
- Custom Expression (Advanced)

**Comparison Options:**
- is / is not
- equals / not equals
- greater than / less than
- greater or equal / less or equal
- between

**Time Values:**
- Night (9pm - 5am)
- Day (5am - 9pm)
- Morning (5am - 12pm)
- Afternoon (12pm - 6pm)
- Evening (6pm - 9pm)

---

## 5. External Tool Specification

### 5.1 Tool Type Decision

| Option | Pros | Cons | Recommendation |
|--------|------|------|----------------|
| **Web App** | No install, accessible anywhere | Requires hosting | ✅ Recommended |
| Desktop App | Offline, fast | Platform-specific, updates | Backup option |
| VS Code Extension | Leverages existing IDE | Dev-only audience | For devs only |

**Primary: Web Application**
- React/Vue frontend
- REST API to game server
- Real-time sync via WebSocket

### 5.2 External Tool Features

```
┌─────────────────────────────────────────────────────────────────────┐
│  ModernSpawner Manager                              [User: Admin]   │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  ┌─ Sidebar ────────┐ ┌─ Main Panel ─────────────────────────────┐ │
│  │                  │ │                                          │ │
│  │ 🔍 Search...     │ │  Britain Guard Post                      │ │
│  │                  │ │  ══════════════════                      │ │
│  │ ▼ Felucca        │ │                                          │ │
│  │   ▼ Britain      │ │  ┌─ Wizard View ──────────────────────┐  │ │
│  │     ● Guard Post │ │  │                                    │  │ │
│  │     ○ Bank Guard │ │  │  [Basic Settings]  [Entries]       │  │ │
│  │     ○ Gate Guard │ │  │  [Triggers]        [Scripts]       │  │ │
│  │   ▼ Dungeon      │ │  │                                    │  │ │
│  │     ○ Level 1    │ │  │  ... wizard content ...            │  │ │
│  │     ○ Boss Room  │ │  │                                    │  │ │
│  │ ▼ Trammel        │ │  └────────────────────────────────────┘  │ │
│  │   ...            │ │                                          │ │
│  │                  │ │  ─── OR ───                              │ │
│  │ ─────────────────│ │                                          │ │
│  │ [+ New Spawner]  │ │  ┌─ Raw View (Power Users) ───────────┐  │ │
│  │ [Import...]      │ │  │                                    │  │ │
│  │ [Export...]      │ │  │  {                                 │  │ │
│  │                  │ │  │    "name": "Britain Guard Post",   │  │ │
│  │ ─────────────────│ │  │    "entries": [                    │  │ │
│  │ Templates        │ │  │      {                             │  │ │
│  │   Guard Pack     │ │  │        "type": "Guard",            │  │ │
│  │   Dungeon Mobs   │ │  │        "maxCount": 3               │  │ │
│  │   Boss Encounter │ │  │      }                             │  │ │
│  │                  │ │  │    ]                               │  │ │
│  └──────────────────┘ │  │  }                                 │  │ │
│                       │  └────────────────────────────────────┘  │ │
│                       │                                          │ │
│                       │  [Validate] [Preview] [Deploy] [History] │ │
│                       └──────────────────────────────────────────┘ │
│                                                                     │
│  Status: Connected to server ● | Last sync: 2 minutes ago          │
└─────────────────────────────────────────────────────────────────────┘
```

### 5.3 External Tool Exclusive Features

| Feature | Description |
|---------|-------------|
| **Bulk Operations** | Edit multiple spawners at once |
| **Template Library** | Save and reuse common configurations |
| **Import/Export** | JSON/YAML file management |
| **Raw Editor** | Direct JSON/YAML editing with syntax highlighting |
| **Version History** | Track changes, rollback mistakes |
| **Validation** | Check for errors before deployment |
| **Preview Mode** | Simulate spawner behavior |
| **Map Visualization** | See spawner locations on map |

---

## 6. Data Export Strategy

### 6.1 File Structure

```
server/
├── Data/
│   └── Spawners/
│       ├── index.json              # Master list of all spawners
│       ├── felucca/
│       │   ├── britain/
│       │   │   ├── guard-post-1.json
│       │   │   └── bank-guards.json
│       │   └── dungeon/
│       │       ├── level-1.json
│       │       └── boss-encounter.yaml   # Complex script
│       └── trammel/
│           └── ...
└── templates/
    ├── guard-pack.json
    ├── dungeon-mobs.json
    └── boss-encounter.yaml
```

### 6.2 JSON Format (Simple Spawners)

```json
{
  "$schema": "modernspawner/v1/spawner.json",
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "name": "Britain Guard Post",
  "description": "Guards at the west gate",

  "location": {
    "x": 1434,
    "y": 1696,
    "z": 0,
    "map": "Felucca"
  },

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
        "Hue": { "type": "fixed", "value": 0 },
        "Title": { "type": "fixed", "value": "the guard" }
      }
    },
    {
      "type": "ArcherGuard",
      "maxCount": 2,
      "properties": {},
      "condition": {
        "type": "timeOfDay",
        "operator": "is",
        "value": "night",
        "description": "Archers only at night"
      }
    }
  ],

  "triggers": [
    {
      "type": "proximity",
      "range": 15,
      "playerOnly": true
    }
  ],

  "metadata": {
    "createdBy": "Admin",
    "createdAt": "2024-12-15T10:30:00Z",
    "modifiedBy": "Admin",
    "modifiedAt": "2024-12-15T14:45:00Z"
  }
}
```

### 6.3 YAML Format (Complex Scripts)

```yaml
# boss-encounter.yaml
# Dragon boss with multi-phase mechanics
$schema: modernspawner/v1/script.yaml

name: Nightmare Dragon Encounter
description: |
  End-game boss encounter requiring 5+ players.
  Dragon scales with player count and spawns minions.

location:
  x: 5432
  y: 1234
  z: 0
  map: Felucca

timing:
  minDelay: "00:30:00"
  maxDelay: "01:00:00"

entries:
  - type: GreaterDragon
    maxCount: 1
    condition:
      # Only spawn with enough players
      type: playersNearby
      operator: greaterOrEqual
      value: 5
      range: 30
      description: Requires 5+ players within 30 tiles

    properties:
      Name:
        type: fixed
        value: "Nightmare"

      Hue:
        type: fixed
        value: 0x8000  # Blood red

      Str:
        type: expression
        # Scales with player count: base 500 + 20 per player
        expression: 500 + (playersNearby(30) * 20)
        description: Strength scales with player count

      Fame:
        type: fixed
        value: 25000

# Event hooks for complex behavior
onBeforeSpawn:
  - condition:
      type: spawnerCount
      operator: greaterOrEqual
      value: 1
    action: cancel
    reason: Only one dragon at a time

onAfterSpawn:
  # Visual effect
  - action: set
    target:
      type: byName
      name: DragonAltar
    properties:
      Hue: 0x4001
    description: Light up the altar when dragon spawns

  # Spawn minions if many players
  - action: spawn
    spawner: DragonMinions
    subgroup: 1
    condition:
      type: playersNearby
      operator: greaterThan
      value: 7
      range: 20
    description: Extra minions for large groups

onEntityKilled:
  - condition:
      type: targetName
      operator: equals
      value: "Nightmare"
    actions:
      - action: spawn
        spawner: TreasureChestSpawner
        subgroup: 0
        description: Spawn treasure

      - action: broadcast
        message: "The Nightmare has been defeated!"
        range: 50
        hue: 0x35  # Green

      - action: set
        target:
          type: byName
          name: DragonAltar
        properties:
          Hue: 0  # Reset altar
```

### 6.4 Why Both Formats?

| Format | Use Case | Reasoning |
|--------|----------|-----------|
| **JSON** | Most spawners | Strict syntax = fewer errors, universal parsing |
| **YAML** | Complex scripts | Comments, multi-line strings, more readable |

Power users can choose their preference in the external tool.

---

## 7. Implementation Roadmap

### 7.1 Phase 1: Foundation (Weeks 1-4)

**Expression Engine:**
- [ ] Tokenizer for expression language
- [ ] Parser to AST
- [ ] Evaluator with game context
- [ ] Built-in functions (random, playersNearby, etc.)
- [ ] Expression compilation and caching
- [ ] Unit tests

**Data Models:**
- [ ] JSON schema for spawner configuration
- [ ] YAML schema for complex scripts
- [ ] Serialization/deserialization
- [ ] Validation logic

### 7.2 Phase 2: In-Game Wizards (Weeks 5-8)

**Core Wizards:**
- [ ] Spawner placement wizard
- [ ] Basic settings wizard
- [ ] Entry wizard with property builder
- [ ] Trigger configuration wizard

**Condition Builder:**
- [ ] Condition builder UI
- [ ] Pre-built condition templates
- [ ] AND/OR logic builder
- [ ] Human-readable preview

### 7.3 Phase 3: External Tool MVP (Weeks 9-14)

**Backend:**
- [ ] REST API for spawner CRUD
- [ ] WebSocket for real-time sync
- [ ] Authentication/authorization
- [ ] File export/import endpoints

**Frontend:**
- [ ] Spawner browser/tree
- [ ] Wizard forms (React/Vue components)
- [ ] Raw editor with syntax highlighting
- [ ] Validation UI
- [ ] Deploy functionality

### 7.4 Phase 4: Advanced Features (Weeks 15-20)

- [ ] Template library system
- [ ] Version history / rollback
- [ ] Bulk operations
- [ ] Map visualization
- [ ] Preview/simulation mode
- [ ] Team collaboration

### 7.5 Phase 5: Polish & Migration (Weeks 21-24)

- [ ] XmlSpawner migration tool
- [ ] Performance optimization
- [ ] Documentation
- [ ] User training materials
- [ ] Production deployment

---

## Appendix A: Gump Technical Constraints

### A.1 Known Limitations

| Constraint | Value | Mitigation |
|------------|-------|------------|
| Max text entry length | ~200-500 chars | Use wizards, not text input |
| Single-line inputs only | Yes | Break into multiple fields |
| No syntax highlighting | N/A | Dropdowns prevent errors |
| Limited screen space | ~800x600 | Paginated wizards |
| Network latency | Variable | Optimistic UI updates |

### A.2 Gump Design Guidelines

1. **Use dropdowns** for all constrained values
2. **Use checkboxes** for boolean options
3. **Use number inputs** with min/max validation
4. **Break complex forms** into wizard steps
5. **Show previews** of what will be generated
6. **Validate on input**, not on submit

---

## Appendix B: Security Considerations

### B.1 Permission Levels

| Level | In-Game | External Tool |
|-------|---------|---------------|
| **Event Master** | Create/edit own spawners | View only |
| **World Builder** | Create/edit all spawners | Create/edit in assigned regions |
| **Senior GM** | Full spawner access | Full access except raw edit |
| **Developer** | Full access | Full access including raw edit |

### B.2 Expression Safety

The expression engine:
- Cannot access filesystem
- Cannot execute arbitrary code
- Cannot modify objects (read-only evaluation)
- Has bounded execution time
- Logs all evaluations for audit

---

*Document Version: 2.0 - Final Decision*
