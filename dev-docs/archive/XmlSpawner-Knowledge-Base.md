# XmlSpawner Complete Knowledge Base

**Purpose:** This document captures all knowledge and understanding of the XmlSpawner codebase for ModernUO. It serves as a comprehensive reference for anyone needing to understand, maintain, migrate from, or replace XmlSpawner.

**Last Updated:** December 2024
**Analyzed Version:** Post-.NET 10 modernization (ArrayList/Hashtable replaced with generics)

---

## Table of Contents

1. [Project Overview](#1-project-overview)
2. [File Structure and Statistics](#2-file-structure-and-statistics)
3. [Core XmlSpawner Class](#3-core-xmlspawner-class)
4. [BaseXmlSpawner - Property and Keyword Engine](#4-basexmlspawner---property-and-keyword-engine)
5. [The Keyword/Scripting System](#5-the-keywordscripting-system)
6. [Spawn Entry and Type Parsing](#6-spawn-entry-and-type-parsing)
7. [Trigger System](#7-trigger-system)
8. [Positioning System](#8-positioning-system)
9. [Attachment System](#9-attachment-system)
10. [Quest System](#10-quest-system)
11. [NPC System (XmlMobiles)](#11-npc-system-xmlmobiles)
12. [Items System (XmlItems)](#12-items-system-xmlitems)
13. [Gump System](#13-gump-system)
14. [Serialization and Persistence](#14-serialization-and-persistence)
15. [Commands and Administration](#15-commands-and-administration)
16. [Code Patterns and Conventions](#16-code-patterns-and-conventions)
17. [Dependencies and Integration Points](#17-dependencies-and-integration-points)
18. [Known Issues and Limitations](#18-known-issues-and-limitations)
19. [Glossary](#19-glossary)

---

## 1. Project Overview

### 1.1 What is XmlSpawner?

XmlSpawner is a comprehensive spawning system originally developed for RunUO and ported to ModernUO. It extends far beyond basic mob/item spawning to include:

- **Advanced Spawning**: Conditional spawning, sequential groups, triggers
- **Scripting System**: Property modification via a custom DSL (Domain-Specific Language)
- **Attachment System**: 38 plugin effects attachable to any entity
- **Quest System**: Complete quest framework with objectives, rewards, leaderboards
- **NPC System**: Talking creatures, vendors, escorts with dialogue
- **Interactive Items**: Switches, traps, tokens, notes

### 1.2 Scale and Complexity

| Metric | Value |
|--------|-------|
| Total C# Files | 98 |
| Total Lines of Code | ~54,000 |
| Main Class (XmlSpawner.cs) | 11,880 lines |
| Namespace | `Server.Engines.XmlSpawner2` |
| Target Framework | .NET 10.0 |

### 1.3 Origin and History

- Originally written for RunUO (circa 2004-2010)
- Used legacy .NET patterns (ArrayList, Hashtable)
- Recently modernized for ModernUO compatibility
- SmartSpawning feature was removed (sector-based optimization deemed unnecessary)

---

## 2. File Structure and Statistics

### 2.1 Directory Layout

```
XmlSpawner/
├── Root Files (Core Engine)
│   ├── XmlSpawner.cs              # 11,880 lines - Main spawner class
│   ├── BaseXmlSpawner.cs          # 3,963 lines - Property/keyword engine
│   ├── XmlSpawnerGumps.cs         # 1,307 lines - Spawner UI
│   ├── XmlSpawnerSkillCheck.cs    # 8,969 lines - Skill trigger system
│   ├── SpawnerExporter.cs         # 8,829 lines - Import/export utilities
│   ├── XmlTextEntryBook.cs        # 2,098 lines - Text entry system
│   ├── ItemFlags.cs               # 3,617 lines - Item property flags
│   └── ExceptionLogging.cs        # 75 lines - Error logging
│
├── XmlAttach/                     # Attachment framework (3 files, 3,393 lines)
│   ├── XmlAttach.cs               # 2,588 lines - Attachment manager
│   ├── XmlAttachment.cs           # 515 lines - Base attachment class
│   └── XmlGetAttachGump.cs        # 790 lines - Attachment viewer
│
├── XmlAttachments/                # 38 attachment plugins
│   ├── XmlStr.cs, XmlDex.cs, XmlInt.cs
│   ├── XmlFire.cs, XmlFreeze.cs, XmlLightning.cs, XmlPoison.cs
│   ├── XmlDialog.cs, XmlMessage.cs, XmlSound.cs
│   ├── XmlMinionStrike.cs, XmlWeaponAbility.cs
│   └── ... (38 total)
│
├── XmlItems/                      # Interactive items (11 files, 5,149 lines)
│   ├── SimpleSwitches.cs          # 1,133 lines
│   ├── TimedSwitches.cs           # 1,259 lines
│   ├── QuestNote.cs, QuestHolder.cs
│   └── ...
│
├── XmlMobiles/                    # NPC types (8 files, 7,363 lines)
│   ├── TalkingBaseCreature.cs     # 929 lines
│   ├── TalkingBaseVendor.cs       # 956 lines
│   ├── TalkingBaseEscortable.cs   # 646 lines
│   └── ...
│
├── XmlPropsGumps/                 # Property editing UI (9 files)
│   ├── XmlPropsGump.cs            # 683 lines
│   ├── XmlSetGump.cs
│   └── ...
│
├── XmlQuest/                      # Quest system (17 files, ~10,000 lines)
│   ├── XmlQuest.cs                # 1,677 lines - Core quest logic
│   ├── XmlQuestToken.cs           # 1,875 lines
│   ├── XmlQuestHolder.cs          # 1,825 lines
│   ├── XmlQuestLeaders.cs         # Leaderboard system
│   └── ...
│
└── XmlUtils/                      # Admin tools (4 files)
    ├── XmlAdd.cs                  # 1,748 lines - Add spawner UI
    ├── XmlEdit.cs                 # 1,401 lines - Edit spawner UI
    ├── XmlCategorizedAddGump.cs   # 477 lines
    └── XmlPartialCategorizedAddGump.cs
```

### 2.2 Lines of Code by Module

| Module | Files | Lines | Purpose |
|--------|-------|-------|---------|
| Core Engine | 8 | ~41,000 | Spawning, keywords, skills |
| Attachments | 41 | ~8,400 | Effect plugins |
| Quest System | 17 | ~10,000 | Quests, rewards, leaderboards |
| NPC System | 8 | ~7,400 | Talking NPCs |
| Items | 11 | ~5,100 | Interactive items |
| Gumps | 9 | ~3,000 | UI dialogs |
| Utils | 4 | ~4,000 | Admin tools |

---

## 3. Core XmlSpawner Class

### 3.1 Class Definition

**File:** `XmlSpawner.cs` (11,880 lines)
**Inherits:** `Item`
**Implements:** Custom spawner logic (not ISpawner - predates ModernUO's interface)

```csharp
namespace Server.Engines.XmlSpawner2;

public class XmlSpawner : Item
{
    // ~50 serialized fields
    // ~100+ methods
    // Multiple nested classes
}
```

### 3.2 Key Fields

#### Identity and Configuration
```csharp
private Guid m_UniqueId;                    // Unique identifier
private string m_Name;                      // Spawner name
private int m_Team;                         // Team assignment for spawned mobs
private int m_HomeRange;                    // How far mobs can wander
private int m_SpawnRange;                   // Spawn placement radius
private bool m_Group;                       // Group spawn mode
private bool m_Running;                     // Is spawner active
```

#### Timing
```csharp
private TimeSpan m_MinDelay;                // Minimum spawn delay
private TimeSpan m_MaxDelay;                // Maximum spawn delay
private TimeSpan m_RefractoryMin;           // Min refractory period after trigger
private TimeSpan m_RefractoryMax;           // Max refractory period
private TimeSpan m_TODStart;                // Time-of-day start
private TimeSpan m_TODEnd;                  // Time-of-day end
private TimeSpan m_Duration;                // Spawn duration (0 = permanent)
private TimeSpan m_DespawnTime;             // Time before despawn
```

#### Triggers
```csharp
private int m_ProximityRange;               // Range for proximity trigger (-1 = disabled)
private string m_SpeechTrigger;             // Speech keyword trigger
private string m_SkillTrigger;              // Skill-based trigger
private int m_ProximityTriggerSound;        // Sound when proximity triggered
private string m_ProximityTriggerMessage;   // Message when triggered
private bool m_ExternalTriggering;          // Allow external trigger calls
private bool m_AllowGhostTriggering;        // Ghosts can trigger
private bool m_AllowNPCTriggering;          // NPCs can trigger
private double m_TriggerProbability;        // Chance trigger fires (0-1)
```

#### State Tracking
```csharp
private bool m_FirstModified;               // Has been modified since creation
private DateTime m_FirstModifiedBy;         // When first modified
private string m_LastModifiedBy;            // Who last modified
private bool m_IsInactivated;               // Temporarily disabled
private int m_KillReset;                    // Kills needed to reset
private int m_killcount;                    // Current kill count
```

#### Sequential Spawning
```csharp
private int m_SequentialSpawn;              // Current sequential group (-1 = random)
private bool m_HoldSequence;                // Pause sequential advancement
```

### 3.3 Nested Classes

#### SpawnObject
Represents a single spawn entry in the spawner.

```csharp
public class SpawnObject
{
    public string TypeName;                 // Full spawn string with properties
    public int ActualMaxCount;              // Max instances to spawn
    public int SpawnsPerTick;               // How many to spawn per tick
    public int SubGroup;                    // Subgroup ID for sequential spawning
    public bool IgnoreSpawnerDefaults;      // Ignore spawner-level settings
    public double SequentialResetTime;      // Reset time for sequential
    public int SequentialResetTo;           // Reset to this group
    public int KillsNeeded;                 // Kills before next spawn
    public bool RequireSurface;             // Must spawn on surface
    public bool Disabled;                   // Entry disabled
    public bool PackRange;                  // Spawn as pack
    public string SpawnedNamePrefix;        // Prefix for spawned names

    public List<object> SpawnedObjects;     // Currently spawned entities

    // Restriction properties
    public bool RestrictKillsToSubgroup;
    public bool ClearOnAdvance;
    public double MinDelay;
    public double MaxDelay;
    public DateTime NextSpawn;
}
```

#### MovementInfo
Tracks player movement for proximity triggers.

```csharp
private class MovementInfo
{
    public Mobile Trigger;
    public DateTime Time;
}
```

#### SpawnPositionInfo
Defines spawn location constraints.

```csharp
public class SpawnPositionInfo
{
    public Mobile TrigMob;                  // Triggering mobile
    public int PositionType;                // Position strategy
    public string PositionArgs;             // Strategy arguments
    public int SpawnIndex;                  // Current spawn index
}
```

### 3.4 Spawn Position Types (Enum Values)

| Value | Name | Description |
|-------|------|-------------|
| 0 | Random | Random within SpawnRange |
| 1 | RowFill | Fill horizontally |
| 2 | ColFill | Fill vertically |
| 3 | Perimeter | Spawn on edge |
| 4 | Player | At triggering player |
| 5 | Waypoint | At waypoint item |
| 6 | RelXY | Relative X,Y offset |
| 7 | DeltaXY | Delta from trigger mob |
| 8 | XY | Absolute coordinates |
| 9 | Wet | Water tiles only |
| 10 | Tiles | Specific tile IDs |
| 11 | NoTiles | Avoid tile IDs |
| 12 | ItemID | On specific items |
| 13 | NoItemID | Avoid items |

### 3.5 TOD (Time of Day) Modes

```csharp
public enum TODModeType
{
    Realtime,   // Uses server real time
    Gametime    // Uses UO game time
}
```

### 3.6 Key Methods

#### Spawning
```csharp
public void Spawn(string typename, string substitution, int subgroup)
public void SpawnSubGroup(int subgroup, int loops)
public void Respawn()
public void DoReset()
public void RemoveSpawns(int subgroup)
public void ClearSubgroup(int subgroup)
```

#### Triggering
```csharp
public void CheckTrigger(Mobile mob)
public bool CheckProximityTrigger(Mobile mob)
public bool CheckSpeechTrigger(Mobile mob, string speech)
public bool CheckSkillTrigger(Mobile mob, SkillName skill)
public void ExternalTrigger(Mobile mob)
```

#### State Management
```csharp
public void Start()
public void Stop()
public void DoTimer()
public void RefreshTimer()
public void OnTick()
public void Defrag()
```

### 3.7 Important Constants

```csharp
public const int MaxLoops = 10;             // Max recursive spawn depth
```

---

## 4. BaseXmlSpawner - Property and Keyword Engine

### 4.1 Overview

**File:** `BaseXmlSpawner.cs` (3,963 lines)

This is the **heart of the scripting system**. It provides:
- Reflection-based property getting/setting
- Keyword parsing and execution
- Type conversion and validation
- Property path traversal (dot notation)

### 4.2 Type Enumerations

#### typeKeyword - Command Keywords
```csharp
public enum typeKeyword
{
    SET,        // Set property on object
    SPAWN,      // Trigger another spawner
    DESPAWN,    // Clear spawns from spawner
    GOTO,       // Jump to subgroup
    COMMAND     // Execute server command
}
```

#### valuemodKeyword - Value Modifiers
```csharp
public enum valuemodKeyword
{
    INC,            // Increment value
    MOB,            // Find mobile by name
    TRIGMOB,        // Triggering mobile reference
    PLAYERSINRANGE  // Count players in range
}
```

#### valueKeyword - Value Functions
```csharp
public enum valueKeyword
{
    PLAYERSINRANGE, // Count players
    RANDNAME        // Random name generator
}
```

### 4.3 KeywordTag Class

Tracks keyword execution state for persistence and flow control.

```csharp
public class KeywordTag
{
    public KeywordFlags Flags;          // HoldSpawn, HoldSequence, Serialize, Defrag
    public int Type;                    // 0=WAIT, 1=GUMP, 2=GOTO
    public string m_Condition;          // Condition expression
    public int m_Goto;                  // Target group for GOTO
    public TimeSpan m_Delay;            // Execution delay
    public TimeSpan m_Timeout;          // Timeout duration
    public DateTime m_TimeoutEnd;       // When timeout expires
    public int Serial;                  // Unique ID
    public Mobile m_TrigMob;            // Triggering mobile
}

[Flags]
public enum KeywordFlags
{
    HoldSpawn = 0x01,
    HoldSequence = 0x02,
    Serialize = 0x04,
    Defrag = 0x08
}
```

### 4.4 TypeInfo Class

Caches property information for performance.

```csharp
public class TypeInfo
{
    public Type Type;
    public PropertyInfo[] PropertyInfoArray;
}
```

### 4.5 Core Methods

#### Property Setting
```csharp
// Main entry point for setting properties
public static string SetPropertyValue(
    XmlSpawner spawner,
    object target,
    string propertyName,
    string value)

// Internal property setter
private static string InternalSetValue(
    Mobile from,
    object target,
    PropertyInfo property,
    string value,
    bool shouldLog)
```

#### Property Getting
```csharp
public static string GetPropertyValue(
    XmlSpawner spawner,
    object target,
    string propertyName)
```

#### Property Application
```csharp
// Apply /prop/value/prop/value string to object
public static void ApplyObjectStringProperties(
    XmlSpawner spawner,
    string propstring,
    object target,
    Mobile trigmob,
    object refobject,
    out string status)
```

#### Type Construction
```csharp
// Convert string to typed value
public static object ConstructFromString(
    Type type,
    object target,
    string value,
    ref bool failed)
```

### 4.6 Property Path Traversal

Supports nested property access via dot notation:

```csharp
// Example: "Backpack.MaxItems" or "Stats.Str"
// Implementation splits on "." and recursively traverses
```

**Code Location:** Lines 745-768 in `SetPropertyValue()`

### 4.7 Supported Type Conversions

| Target Type | Conversion Method |
|-------------|-------------------|
| Primitives (int, double, bool) | Direct parse |
| Enums | `Enum.Parse()` |
| Custom Enums | `CustomEnum.Parse()` |
| Type | `AssemblyHandler.FindTypeByName()` |
| Mobile/Item | Serial lookup (0x format) |
| TimeSpan | `TimeSpan.Parse()` |
| Point2D/Point3D | "x,y" or "x,y,z" format |
| IList elements | Array indexer support |

---

## 5. The Keyword/Scripting System

This section provides an **exhaustive reference** to XmlSpawner's scripting capabilities.

### 5.1 Spawn String Format

The basic format for a spawn entry is:

```
TypeName[,arg1,arg2,...]/property1/value1/property2/value2/...
```

**Examples:**
```
Orc                           # Simple spawn
Orc/Hue/500                   # With property
Orc/Hue/500/Name/Big Orc      # Multiple properties
Orc,arg1,arg2/Hue/500         # With constructor args
```

### 5.2 Position Prefixes

Position modifiers come **before** the type name:

| Prefix | Syntax | Description |
|--------|--------|-------------|
| `#RANDOM` | `#RANDOM/Orc` | Random position (default) |
| `#ROWFILL` or `#XFILL` | `#ROWFILL/Orc` | Fill horizontally |
| `#COLFILL` or `#YFILL` | `#COLFILL/Orc` | Fill vertically |
| `#EDGE` or `#PERIMETER` | `#EDGE/Orc` | Spawn on perimeter |
| `#PLAYER` | `#PLAYER/Orc` | At trigger mob location |
| `#WAYPOINT` | `#WAYPOINT,WaypointName/Orc` | At named waypoint |
| `#RELXY` | `#RELXY,5,10/Orc` | Relative offset from spawner |
| `#DXY` | `#DXY,5,10/Orc` | Delta from trigger mob |
| `#XY` | `#XY,1234,5678,0/Orc` | Absolute coordinates |
| `#WET` | `#WET/SeaSerpent` | Water tiles only |
| `#TILES` | `#TILES,0x1234,0x1235/Orc` | Specific tile IDs |
| `#NOTILES` | `#NOTILES,0x1234/Orc` | Avoid tile IDs |
| `#ITEMID` | `#ITEMID,0x1234/Orc` | On specific items |
| `#NOITEMID` | `#NOITEMID,0x1234/Orc` | Avoid items |
| `*` | `*/Orc` | Ignore surface requirement |

### 5.3 Condition Prefix

```
#CONDITION,expression/TypeName
```

**Expression Operators:**
- `=` - Equality
- `!` - Inequality
- `>` - Greater than (numeric)
- `<` - Less than (numeric)
- `&` - AND
- `|` - OR
- `~` - NOT (prefix)

**Examples:**
```
#CONDITION,Gold>1000/Dragon           # Spawn dragon if Gold > 1000
#CONDITION,Level=10&Karma>0/Angel     # Level 10 AND positive karma
#CONDITION,~IsEvil/Guard              # NOT evil
```

### 5.4 Type Keywords (Commands)

#### SET - Modify Existing Object
```
SET[,targetname[,targettype]]/property/value/property/value...
```

**Examples:**
```
SET/Hue/500                           # Set on spawner's SetItem
SET,MyWeapon/Hue/500                  # Find item named "MyWeapon"
SET,MyWeapon,BaseWeapon/Damage/50     # With type filter
SET,0x40001234/Name/NewName           # By serial number
```

**Code Location:** Lines 3686-3735 in BaseXmlSpawner.cs

#### SPAWN - Trigger Another Spawner
```
SPAWN[,spawnerName],subgroup
SPAWN/subgroup
```

**Examples:**
```
SPAWN,GuardSpawner,2                  # Trigger subgroup 2 of GuardSpawner
SPAWN/3                               # Trigger own subgroup 3
```

**Loop Protection:** Max 10 recursive calls (MaxLoops constant)

**Code Location:** Lines 3790-3859 in BaseXmlSpawner.cs

#### DESPAWN - Clear Spawns
```
DESPAWN[,spawnerName],subgroup
```

**Examples:**
```
DESPAWN,CleanupSpawner,1              # Clear subgroup 1
DESPAWN/0                             # Clear own subgroup 0
```

**Code Location:** Lines 3737-3788 in BaseXmlSpawner.cs

#### GOTO - Sequential Control Flow
```
GOTO/subgroup
```

**Example:**
```
GOTO/5                                # Jump to subgroup 5
```

Sets `spawner.SequentialSpawn = group` and `HoldSequence = true`

**Code Location:** Lines 3861-3895 in BaseXmlSpawner.cs

#### COMMAND - Execute Server Command
```
COMMAND/commandstring
```

**Example:**
```
COMMAND/ban PlayerName
```

**Security:** Uses triggering mobile's access level

**Code Location:** Lines 3897-3925 in BaseXmlSpawner.cs

### 5.5 Value Modifiers

Applied in property values with format: `property/MODIFIER,args`

#### INC - Increment
```
property/INC,value
property/INC,min,max           # Random range
```

**Examples:**
```
Hits/INC,50                    # Add 50 to Hits
Str/INC,10,20                  # Add random 10-20 to Str
Gold/INC,-100                  # Subtract 100 (negative increment)
```

#### MOB - Mobile Lookup
```
property/MOB,mobilename[,type]
```

**Examples:**
```
Master/MOB,GuardCaptain
Leader/MOB,King,PlayerMobile
```

#### TRIGMOB - Trigger Mobile Reference
```
property/TRIGMOB
```

**Example:**
```
Master/TRIGMOB                 # Set Master to triggering mobile
Owner/TRIGMOB
```

#### PLAYERSINRANGE - Player Count
```
property/PLAYERSINRANGE,range
```

**Example:**
```
Damage/PLAYERSINRANGE,15       # Set Damage to player count
```

### 5.6 Value Functions

Used in property values:

#### PLAYERSINRANGE
```
PLAYERSINRANGE,range
```

Returns integer count of players within range.

#### RANDNAME
```
RANDNAME,nametype
```

**Name Types:** "Male", "Female", "Last", etc.

### 5.7 Special Value Syntax

#### Literal Values (@ prefix)
Prevents keyword interpretation:
```
Name/@MOB_Guard                # Literal string "MOB_Guard"
Title/@SET                     # Literal string "SET"
```

#### Hex Values
```
Hue/0x8000                     # Hex format
ItemID/0x1234
```

#### Array Indexing
```
Skills[0]/Value/100            # Set first skill value
Items[2]/Hue/500               # Set third item's hue
```

#### Property Path (Dot Notation)
```
Backpack.MaxItems/50
Stats.Str/100
Equipment.Weapon.Hue/500
```

### 5.8 Substitution Patterns

Format: `{expression}`

**Examples:**
```
Name/{Spawner.Name} Guard      # Include spawner name
Hue/{TrigMob.Hue}              # Copy trigger mob's hue
Title/{RANDNAME,Male}          # Random male name
```

**Code Location:** `ApplySubstitution()` method

### 5.9 Complete Parsing Flow

```
1. Raw Spawn String
   "Orc/Hue/500/Name/@Guard"
         │
         ▼
2. ApplySubstitution()
   - Replace {keyword} patterns
         │
         ▼
3. ParseObjectType()
   - Extract "Orc" as type name
   - Extract constructor args if any
         │
         ▼
4. IsTypeKeyword() check
   - If SET/SPAWN/DESPAWN/GOTO/COMMAND
   - Call SpawnTypeKeyword()
         │
         ▼
5. OR: AssemblyHandler.FindTypeByName()
   - Resolve type
   - Create instance
         │
         ▼
6. ApplyObjectStringProperties()
   - Parse /property/value pairs
   - For each pair:
     - Check for value modifier (INC, MOB, etc.)
     - OR direct assignment
     - Call SetPropertyValue()
         │
         ▼
7. SetPropertyValue()
   - Handle dot notation (nested properties)
   - Resolve PropertyInfo via reflection
   - Convert value via ConstructFromString()
   - Set value via InternalSetValue()
```

### 5.10 Scripting Examples

#### Example 1: Basic Property Setting
```
Orc/Hue/0x8000/Name/Elite Orc/Title/The Destroyer
```

#### Example 2: Conditional Spawn with Loot
```
#CONDITION,TrigMob.Karma<0/Daemon/Hue/0x4001/Fame/15000
```

#### Example 3: Trigger Chain
```
SPAWN,Phase2Spawner,1
```

#### Example 4: Dynamic Properties
```
Guard/Master/TRIGMOB/Team/{Spawner.Team}/Str/INC,10,30
```

#### Example 5: Loot Override (Backpack Population)
```
Merchant/Backpack.AddItem/Gold,1000/Backpack.AddItem/IronIngot,50
```

Note: The exact syntax for loot/backpack manipulation involves the AddItem method calls which are processed through the property system.

---

## 6. Spawn Entry and Type Parsing

### 6.1 SpawnObject Processing

When a spawn occurs:

```csharp
// In XmlSpawner.Spawn()
1. Get SpawnObject from m_SpawnObjects array
2. TypeName contains full spawn string
3. Call BaseXmlSpawner.ParseObjectType(TypeName)
4. Get base type and constructor arguments
5. Create instance via Activator or constructor
6. Apply properties via ApplyObjectStringProperties()
7. Position via GetSpawnPosition()
8. Add to world
9. Track in SpawnedObjects list
```

### 6.2 Type Resolution

```csharp
// Parsing "Orc,arg1,arg2/Hue/500"
Type type = AssemblyHandler.FindTypeByName("Orc");
// Constructor args: ["arg1", "arg2"]
// Property string: "/Hue/500"
```

### 6.3 Constructor Parameter Handling

Comma-separated after type name:
```
TypeName,param1,param2,param3/properties...
```

Used for types requiring constructor arguments.

---

## 7. Trigger System

### 7.1 Trigger Types

| Trigger | Property | Description |
|---------|----------|-------------|
| **Timer** | MinDelay/MaxDelay | Standard timed spawning |
| **Proximity** | ProximityRange | Player enters range |
| **Speech** | SpeechTrigger | Keyword spoken |
| **Skill** | SkillTrigger | Skill used nearby |
| **External** | ExternalTriggering | API call |
| **TOD** | TODStart/TODEnd | Time-of-day window |

### 7.2 Proximity Trigger Details

**File:** Handled in `XmlSpawner.cs`

```csharp
public int ProximityRange = -1;             // -1 = disabled
public string ProximityTriggerMessage;      // Message to player
public int ProximityTriggerSound = 0x1F4;   // Sound effect
public bool AllowGhostTriggering = false;
public bool AllowNPCTriggering = false;
```

**Trigger Flow:**
1. Mobile moves near spawner
2. `OnMovement()` called
3. Check `ProximityRange > 0`
4. Validate mobile (player, not ghost unless allowed)
5. Check refractory period
6. Check TOD constraints
7. Fire trigger
8. Play sound, send message
9. Call `Spawn()`

### 7.3 Speech Trigger Details

```csharp
public string SpeechTrigger;                // Keyword(s) to match
```

**Trigger Flow:**
1. Player speaks nearby
2. `OnSpeech()` called
3. Check speech contains trigger keyword
4. Validate speaker
5. Fire trigger

**Multiple Keywords:** Comma-separated in SpeechTrigger string

### 7.4 Skill Trigger Details

**File:** `XmlSpawnerSkillCheck.cs` (8,969 lines)

```csharp
public string SkillTrigger;                 // Format: "SkillName,min,max"
```

**Components:**
- Skill name
- Minimum skill value
- Maximum skill value
- Optional success requirement

**Code Structure:**
```csharp
private static List<RegisteredSkill>[] m_FeluccaSkillList;
private static List<RegisteredSkill>[] m_TrammelSkillList;
// ... per-map skill trigger lists
```

### 7.5 TOD (Time of Day) Constraints

```csharp
public TimeSpan TODStart;
public TimeSpan TODEnd;
public TODModeType TODMode;                 // Realtime or Gametime
```

Spawner only active within time window.

### 7.6 Refractory Period

```csharp
public TimeSpan RefractMin;
public TimeSpan RefractMax;
private DateTime m_RefractoryEnd;
```

Cooldown period after trigger fires, preventing rapid re-triggering.

### 7.7 Trigger Probability

```csharp
public double TriggerProbability = 1.0;     // 0.0 to 1.0
```

Random chance trigger actually fires.

---

## 8. Positioning System

### 8.1 Position Resolution

```csharp
public Point3D GetSpawnPosition(
    SpawnPositionInfo positionInfo,
    Map map,
    Mobile trigmob,
    ref bool requiresurface)
```

### 8.2 Position Types Detail

#### Random (Default)
```csharp
// Random point within SpawnRange of spawner location
int x = Location.X + Utility.RandomMinMax(-SpawnRange, SpawnRange);
int y = Location.Y + Utility.RandomMinMax(-SpawnRange, SpawnRange);
```

#### RowFill / ColFill
```csharp
// Sequential grid placement
// Tracks spawn index to place in row/column order
```

#### Perimeter
```csharp
// Place on edge of spawn range rectangle
// Cycles through edges
```

#### Player (#PLAYER)
```csharp
// Use trigger mobile's location
return trigmob.Location;
```

#### Waypoint (#WAYPOINT,name)
```csharp
// Find WayPoint item by name
// Return its location
```

#### RelXY (#RELXY,x,y)
```csharp
// Offset from spawner
Point3D loc = new Point3D(
    Location.X + offsetX,
    Location.Y + offsetY,
    Location.Z);
```

#### DeltaXY (#DXY,x,y)
```csharp
// Offset from trigger mob
Point3D loc = new Point3D(
    trigmob.X + deltaX,
    trigmob.Y + deltaY,
    trigmob.Z);
```

#### Absolute (#XY,x,y,z)
```csharp
// Fixed world coordinates
return new Point3D(x, y, z);
```

#### Wet (#WET)
```csharp
// Find water tile within range
// Check TileData for Wet flag
```

#### Tile Filters (#TILES, #NOTILES)
```csharp
// Filter spawn location by tile ID
// Include or exclude specific tiles
```

### 8.3 Surface Requirement

```csharp
public bool RequireSurface = true;
```

Can be disabled with `*` prefix: `*/Orc`

---

## 9. Attachment System

### 9.1 Overview

**Framework Files:**
- `XmlAttach.cs` (2,588 lines) - Manager
- `XmlAttachment.cs` (515 lines) - Base class
- `XmlGetAttachGump.cs` (790 lines) - UI

**Plugin Files:** 38 attachment types in `XmlAttachments/`

### 9.2 IXmlAttachment Interface

```csharp
public interface IXmlAttachment
{
    ASerial Serial { get; }
    string Name { get; set; }
    object AttachedTo { get; set; }
    bool Deleted { get; }

    void Delete();
    void OnAttach();
    void OnDelete();
    void Serialize(IGenericWriter writer);
    void Deserialize(IGenericReader reader);
    string OnIdentify(Mobile from);
}
```

### 9.3 XmlAttachment Base Class

```csharp
public abstract class XmlAttachment : IXmlAttachment
{
    private ASerial m_Serial;
    private object m_AttachedTo;
    private string m_Name;
    private TimeSpan m_Expiration;
    private DateTime m_ExpirationEnd;

    // Virtual event hooks
    public virtual void OnAttach() { }
    public virtual void OnDelete() { }
    public virtual void OnTrigger(object activator, Mobile mob) { }
    public virtual void OnWeaponHit(Mobile attacker, Mobile defender, BaseWeapon weapon, int damage) { }
    public virtual void OnKill(Mobile killed) { }
    public virtual void OnKilled(Mobile killer) { }
    public virtual bool OnMovement(Mobile mob, Point3D oldLocation) { return false; }
    public virtual void OnSpeech(SpeechEventArgs args) { }
}
```

### 9.4 Attachment Registration

Attachments use `[Attachable]` attribute for discovery:

```csharp
[Attachable]
public XmlFire(int damage)
{
    m_Damage = damage;
}
```

### 9.5 Attachment Types Reference

#### Stat Modifiers
| Attachment | Effect |
|------------|--------|
| XmlStr | Modify Strength |
| XmlDex | Modify Dexterity |
| XmlInt | Modify Intelligence |
| XmlSkill | Modify skill value |
| XmlAddFame | Add fame |
| XmlAddKarma | Add karma |

#### Combat Effects
| Attachment | Effect |
|------------|--------|
| XmlFire | Fire damage on hit |
| XmlFreeze | Cold damage/effect |
| XmlLightning | Lightning effect |
| XmlPoison | Poison on hit |
| XmlLifeDrain | Steal HP |
| XmlManaDrain | Steal mana |
| XmlStamDrain | Steal stamina |
| XmlMinionStrike | Summon minion on hit |
| XmlWeaponAbility | Grant weapon ability |
| XmlEnemyMastery | Damage bonus vs type |

#### Interactive
| Attachment | Effect |
|------------|--------|
| XmlDialog | Conversation system |
| XmlMessage | Send message |
| XmlSound | Play sound |
| XmlAnimate | Play animation |
| XmlMagicWord | Trigger on keyword |
| XmlUse | Item use handler |

#### Utility
| Attachment | Effect |
|------------|--------|
| XmlData | Store arbitrary data |
| XmlValue | Store numeric value |
| XmlDate | Store date |
| XmlLocalVariable | Local variable storage |
| XmlSaveItem | Persistence helper |
| XmlHue | Modify hue |
| XmlAosAttributes | AOS attribute mods |

### 9.6 Attachment Manager (XmlAttach)

```csharp
public static class XmlAttach
{
    // Attachment storage (Dictionary per object serial)
    private static Dictionary<Serial, List<XmlAttachment>> m_Attachments;

    // Core methods
    public static XmlAttachment FindAttachment(object target, Type type);
    public static XmlAttachment FindAttachment(object target, Type type, string name);
    public static List<XmlAttachment> FindAttachments(object target);
    public static void AttachTo(object target, XmlAttachment attachment);
    public static void RemoveAttachment(object target, XmlAttachment attachment);

    // Event routing
    public static void OnWeaponHit(Mobile attacker, Mobile defender, BaseWeapon weapon, int damage);
    public static void OnKill(Mobile killer, Mobile killed);
    public static void OnSpeech(SpeechEventArgs args);
}
```

---

## 10. Quest System

### 10.1 Overview

**Files:** 17 files in `XmlQuest/` (~10,000 lines)

**Key Components:**
- `XmlQuest.cs` - Core quest logic and IXmlQuest interface
- `XmlQuestToken.cs` - Quest reward/completion item
- `XmlQuestHolder.cs` - Quest container
- `XmlQuestBook.cs` - Quest display book
- `XmlQuestLeaders.cs` - Leaderboard system
- Various gumps for UI

### 10.2 IXmlQuest Interface

```csharp
public interface IXmlQuest
{
    string Name { get; set; }
    string Description { get; set; }

    // 5 objectives
    string Objective1 { get; set; }
    string Objective2 { get; set; }
    string Objective3 { get; set; }
    string Objective4 { get; set; }
    string Objective5 { get; set; }

    // 5 descriptions
    string Description1 { get; set; }
    // ... through Description5

    // 5 completion states
    bool Completed1 { get; set; }
    // ... through Completed5

    // State strings
    string State1 { get; set; }
    // ... through State5

    // Journal
    List<JournalEntry> Journal { get; set; }

    // Properties
    Mobile Owner { get; set; }
    Mobile Creator { get; set; }
    PlayerMobile PartyLeader { get; set; }
    int PartyRange { get; set; }
    bool IsCompleted { get; }
    bool IsValid { get; }
    int Difficulty { get; set; }
    TimeSpan Expiration { get; set; }

    void Invalidate();
}
```

### 10.3 JournalEntry

```csharp
public class JournalEntry
{
    public string EntryText;
    public DateTime TimeStamp;
}
```

### 10.4 Quest Points System

```csharp
// XmlQuestPoints.cs
public class QuestEntry
{
    public string QuestName;
    public int Points;
    public DateTime WhenCompleted;
}

public static List<QuestEntry> GetQuestList(PlayerMobile player);
public static int GetCredits(PlayerMobile player);
public static void AddCredits(PlayerMobile player, int credits);
```

### 10.5 Quest Leaderboard

```csharp
// XmlQuestLeaders.cs
public class QuestRankEntry
{
    public string PlayerName;
    public string GuildName;
    public int Points;
    public int QuestsCompleted;
}

public static List<QuestRankEntry> QuestRankList;
```

---

## 11. NPC System (XmlMobiles)

### 11.1 Overview

**Files:** 8 files in `XmlMobiles/` (7,363 lines)

Provides talking NPCs with customizable dialogue and appearance.

### 11.2 TalkingBaseCreature

```csharp
public class TalkingBaseCreature : BaseCreature
{
    // Speech
    private string m_TalkText;              // What to say
    private int m_SpeechHue = 0x3B2;        // Text color

    // Appearance
    private int m_Gender = -1;              // -1=random, 0=female, 1=male
    private int m_TitleHue;
    private int[] m_FaceHueArray;
    private int[] m_HairHueArray;

    // Equipment templates
    private Type[] m_EquipmentTemplates;

    public override void OnSpeech(SpeechEventArgs e)
    {
        // Handle keyword triggers
        // Respond with m_TalkText
    }
}
```

### 11.3 TalkingBaseVendor

Extends vendor functionality with speech:

```csharp
public class TalkingBaseVendor : BaseVendor
{
    // Similar speech and appearance properties
    // Plus vendor-specific functionality
}
```

### 11.4 TalkingBaseEscortable

For escort quests:

```csharp
public class TalkingBaseEscortable : BaseEscortable
{
    // Escort destination
    // Speech properties
    // Quest integration
}
```

---

## 12. Items System (XmlItems)

### 12.1 Overview

**Files:** 11 files in `XmlItems/` (5,149 lines)

### 12.2 SimpleSwitches

```csharp
// ILinkable interface for switch chains
public interface ILinkable
{
    Item Link { get; set; }
    void Activate(Mobile from, int state, List<ILinkable> links);
}

public class SimpleSwitch : Item, ILinkable
{
    private Item m_Link;
    private int m_SwitchState;

    public void Activate(Mobile from, int state, List<ILinkable> links)
    {
        // Toggle state
        // Propagate to linked switches
    }
}
```

### 12.3 TimedSwitches

Switches with automatic reset:

```csharp
public class TimedSwitch : SimpleSwitch
{
    private TimeSpan m_ResetDelay;
    private Timer m_ResetTimer;
}
```

### 12.4 Quest Items

| Item | Purpose |
|------|---------|
| QuestNote | Quest tracking note |
| QuestHolder | Quest container |
| SimpleNote | Basic note item |
| SimpleMap | Map display |
| XmlQuestMaker | Quest creation tool |

### 12.5 SimpleTileTrap

Floor-based trap trigger:

```csharp
public class SimpleTileTrap : Item
{
    private int m_TrapDamage;
    private Poison m_TrapPoison;

    public override bool OnMoveOver(Mobile m)
    {
        // Trigger trap effect
    }
}
```

---

## 13. Gump System

### 13.1 Main Gumps

| Gump | File | Purpose |
|------|------|---------|
| XmlSpawnerGump | XmlSpawnerGumps.cs | Main spawner editor |
| XmlAddGump | XmlAdd.cs | Add spawner UI |
| XmlPropsGump | XmlPropsGump.cs | Property editor |
| XmlCategorizedAddGump | XmlCategorizedAddGump.cs | Categorized type browser |
| QuestLogGump | QuestLogGump.cs | Quest display |
| XmlGetAttachGump | XmlGetAttachGump.cs | Attachment viewer |

### 13.2 XmlSpawnerGump Structure

```csharp
public class XmlSpawnerGump : Gump
{
    private XmlSpawner m_Spawner;
    private int m_Page;
    private SpawnEntry[] m_Entries;

    // Max 40 entries across 2 pages
    private const int MaxEntriesPerPage = 20;

    // Creates extensive UI with:
    // - Entry list with text entries
    // - Property fields
    // - Trigger settings
    // - Control buttons
}
```

### 13.3 TextEntryGump

For multi-line text entry (spawn definitions):

```csharp
public class TextEntryGump : Gump
{
    // Book-style interface for long spawn strings
}
```

### 13.4 XmlSimpleGump

Generic gump for quest/script interactions:

```csharp
public class XmlSimpleGump : Gump
{
    private int m_GumpType;
    // 0 = message
    // 1 = yes/no
    // 2 = text entry
    // 3 = accept/decline (quest)
    // 4 = multiple selection
    // 5 = custom layout

    private XmlGumpCallback m_Callback;
}
```

---

## 14. Serialization and Persistence

### 14.1 XmlSpawner Serialization

```csharp
public override void Serialize(IGenericWriter writer)
{
    base.Serialize(writer);

    writer.Write(version);              // Current version: varies

    // Write all fields
    writer.Write(m_UniqueId.ToString());
    writer.Write(m_Name);
    writer.Write(m_MinDelay);
    writer.Write(m_MaxDelay);
    // ... 40+ more fields

    // Write spawn objects
    writer.Write(m_SpawnObjects.Length);
    foreach (var obj in m_SpawnObjects)
    {
        writer.Write(obj.TypeName);
        writer.Write(obj.ActualMaxCount);
        // ... all SpawnObject fields

        // Write spawned entity references
        writer.Write(obj.SpawnedObjects.Count);
        foreach (var spawned in obj.SpawnedObjects)
        {
            writer.Write(spawned);      // Serial reference
        }
    }
}
```

### 14.2 Deserialization Versioning

Multiple version handlers for backward compatibility:

```csharp
public override void Deserialize(IGenericReader reader)
{
    base.Deserialize(reader);

    int version = reader.ReadInt();

    switch (version)
    {
        case 30:
            // Latest version fields
            break;
        case 29:
            // Previous version
            break;
        // ... back to version 0
    }
}
```

### 14.3 XML Export Format

Via `SpawnerExporter.cs`:

```xml
<Spawner>
    <Name>GuardSpawner</Name>
    <UniqueId>guid</UniqueId>
    <Map>Felucca</Map>
    <X>1234</X>
    <Y>5678</Y>
    <Z>0</Z>
    <MinDelay>00:05:00</MinDelay>
    <MaxDelay>00:10:00</MaxDelay>
    <HomeRange>10</HomeRange>
    <SpawnRange>5</SpawnRange>
    <Objects>
        <Object>
            <Type>Guard/Hue/500</Type>
            <MaxCount>3</MaxCount>
            <SubGroup>0</SubGroup>
        </Object>
    </Objects>
</Spawner>
```

### 14.4 Attachment Serialization

Attachments serialize to their attached object:

```csharp
// In XmlAttachment
public virtual void Serialize(IGenericWriter writer)
{
    writer.Write(m_Serial.Value);
    writer.Write(m_Name);
    writer.Write(m_Expiration);
    // Subclass-specific data
}
```

---

## 15. Commands and Administration

### 15.1 Commands

| Command | Usage | Description |
|---------|-------|-------------|
| `[XmlSpawner` | `[XmlSpawner` | Opens spawner UI on target |
| `[XmlAdd` | `[XmlAdd [-defaults]` | Open add spawner gump |
| `[XmlLoad` | `[XmlLoad filename` | Load spawners from file |
| `[XmlSave` | `[XmlSave filename` | Save spawners to file |
| `[XmlFind` | `[XmlFind name` | Find spawner by name |

### 15.2 XmlAdd Features

- Save/load default settings per user
- Categorized type browser
- Multiple spawn entries
- Auto-numbering

### 15.3 SpawnerExporter

```csharp
public class SpawnerExporter
{
    public static void ExportSpawners(string filename);
    public static void ImportSpawners(string filename);
    public static void ExportToXml(List<XmlSpawner> spawners, string filename);
}
```

---

## 16. Code Patterns and Conventions

### 16.1 Naming Conventions

- Private fields: `m_FieldName`
- Public properties: `PropertyName`
- Constants: `MaxLoops`, `MaxEntries`
- Enums: `TypeKeyword`, `TODModeType`

### 16.2 Common Patterns

#### Null Checks
```csharp
if (spawner == null || spawner.Deleted)
    return;
```

#### Try-Catch Wrapping
```csharp
try
{
    // Operation
}
catch (Exception e)
{
    Diagnostics.ExceptionLogging.LogException(e);
}
```

#### Property Caching
```csharp
// In BaseXmlSpawner
private static List<TypeInfo> m_TypeInfoCache;
```

### 16.3 Magic Values

| Value | Meaning |
|-------|---------|
| -1 | Disabled/None (e.g., ProximityRange, SequentialSpawn) |
| 0x1F4 | Default proximity sound |
| 10 | MaxLoops for recursion protection |

### 16.4 Error Handling

Errors are typically:
1. Logged via `Diagnostics.ExceptionLogging.LogException()`
2. Returned as status strings
3. Silently ignored in some cases

---

## 17. Dependencies and Integration Points

### 17.1 ModernUO Dependencies

```csharp
using Server;
using Server.Items;
using Server.Mobiles;
using Server.Gumps;
using Server.Network;
using Server.Commands;
using Server.Targeting;
using Server.Accounting;
```

### 17.2 Key Integration Points

| System | Integration |
|--------|-------------|
| Mobile | Speech events, movement, death |
| Item | OnMovement, OnDoubleClick |
| Timer | Spawn timing |
| Region | Spawn location validation |
| Commands | Admin commands |
| Gumps | UI system |

### 17.3 Event Hooks

XmlSpawner hooks into:
- `Mobile.OnMovement` - Proximity triggers
- `Mobile.OnSpeech` - Speech triggers
- `Mobile.OnDeath` - Kill tracking
- `Item.OnMovement` - Item proximity

### 17.4 Reflection Usage

Heavy use of reflection for:
- Property getting/setting
- Type instantiation
- Constructor invocation
- Method discovery

---

## 18. Known Issues and Limitations

### 18.1 Performance Issues

1. **String Parsing Per Spawn**: Every spawn parses the type string, no caching
2. **Reflection Overhead**: PropertyInfo lookup on every property set
3. **No Expression Compilation**: Conditions evaluated via string parsing each time

### 18.2 Code Quality Issues

1. **Monolithic Class**: XmlSpawner.cs is 11,880 lines
2. **Mixed Concerns**: Spawning, triggers, positioning, serialization in one class
3. **Limited Abstraction**: No interfaces for triggers/positioners
4. **Tight Coupling**: Hard to test or modify subsystems independently

### 18.3 Missing Features

1. **No Async Support**: Everything synchronous
2. **Limited Validation**: Many operations fail silently
3. **No Logging Framework**: Uses Console.WriteLine in some places

### 18.4 Removed Features

- **SmartSpawning**: Sector-based optimization removed (deemed unnecessary)

### 18.5 Compatibility Notes

- Requires ModernUO-specific types (IGenericWriter, etc.)
- Uses AssemblyHandler for type resolution
- Depends on ModernUO's Timer system

---

## 19. Glossary

| Term | Definition |
|------|------------|
| **Spawn Entry** | A single line in the spawner defining what to spawn |
| **Spawn Object** | Runtime representation of a spawn entry |
| **Subgroup** | Numbered group for sequential/conditional spawning |
| **Trigger** | Condition that causes spawner to activate |
| **Refractory** | Cooldown period after trigger |
| **TOD** | Time of Day - time-based spawn window |
| **Keyword** | Command in spawn string (SET, SPAWN, etc.) |
| **Value Modifier** | Property value transformation (INC, MOB, etc.) |
| **Attachment** | Plugin effect attached to entity |
| **Defrag** | Remove dead/invalid spawn references |
| **Sequential Spawning** | Ordered group-based spawning |
| **Group Mode** | Spawn all at once, respawn when all dead |
| **Property Path** | Dot-notation for nested properties |
| **Substitution** | {expression} replacement in strings |

---

## Appendix A: File Quick Reference

| File | Lines | Primary Purpose |
|------|-------|-----------------|
| XmlSpawner.cs | 11,880 | Main spawner class |
| BaseXmlSpawner.cs | 3,963 | Property/keyword engine |
| XmlSpawnerSkillCheck.cs | 8,969 | Skill trigger system |
| SpawnerExporter.cs | 8,829 | Import/export |
| ItemFlags.cs | 3,617 | Item property flags |
| XmlTextEntryBook.cs | 2,098 | Text entry UI |
| XmlAttach.cs | 2,588 | Attachment manager |
| XmlAdd.cs | 1,748 | Add spawner UI |
| XmlQuest.cs | 1,677 | Quest core |
| XmlQuestToken.cs | 1,875 | Quest tokens |
| XmlQuestHolder.cs | 1,825 | Quest container |

---

## Appendix B: Spawn String Quick Reference

```
# Basic
TypeName

# With properties
TypeName/Property/Value/Property/Value

# With constructor args
TypeName,arg1,arg2/Property/Value

# Position modifiers
#RANDOM/TypeName
#PLAYER/TypeName
#WAYPOINT,WaypointName/TypeName
#XY,1234,5678,0/TypeName
#RELXY,5,10/TypeName
#WET/TypeName

# Conditional
#CONDITION,expression/TypeName

# Commands
SET/Property/Value
SET,ItemName/Property/Value
SPAWN,SpawnerName,Subgroup
DESPAWN,SpawnerName,Subgroup
GOTO/Subgroup
COMMAND/commandstring

# Value modifiers
Property/INC,value
Property/INC,min,max
Property/MOB,mobilename
Property/TRIGMOB
Property/PLAYERSINRANGE,range

# Special
Property/@LiteralValue          # Literal (no keyword processing)
Property/0x1234                 # Hex value
Array[0]/Property/Value         # Array indexing
Object.SubObject.Property/Value # Nested properties
```

---

*End of XmlSpawner Knowledge Base*
