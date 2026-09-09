# XmlSpawner → ModernSpawner Migration Requirements

Status: draft v1, 2026-09-08. Source-format facts were verified against the legacy XmlSpawner code in
`modernuo/XmlSpawner-for-Modernuo` (`XmlSpawner/XmlSpawner.cs`, `BaseXmlSpawner.cs`, `SpawnerExporter.cs`)
and the archived knowledge base (`archive/XmlSpawner-Knowledge-Base.md`). Assumes decisions D4–D6 and D9
from `product-spec.md`.

## 1. Scope

**In:** converting XmlSpawner `.xml` files (produced by `[XmlSave`, `[XmlSaveAll`, `XmlSaveSingle`) into
ModernUO `SpawnerDto` JSON files that `[ImportSpawners` loads, with a per-file, per-spawner report.

**Out:** reading XmlSpawner binary world saves; in-world replacement of live `XmlSpawner` items;
XmlAttachments, XmlQuests, XmlItems, talking NPCs; the RunUO distro `<spawners>` format (ModernUO's own
`[ImportSpawners` reads it already); the invented `//XmlSpawner` and `//SpawnPoint` layouts that
`Migration/XmlSpawnerMigrator.cs` targets (that class is removed).

## 2. Source format

XmlSpawner saves a `DataSet("Spawns")` with one table `Points` via `DataSet.WriteXml`:

```xml
<Spawns>
  <Points>
    <Name>…</Name><UniqueId>guid</UniqueId><Map>Felucca</Map>
    <X>…</X><Y>…</Y><Width>…</Width><Height>…</Height>
    <CentreX/><CentreY/><CentreZ/>
    <ContainerX/><ContainerY/><ContainerZ/><InContainer>False</InContainer>
    <Range/><MaxCount/><DelayInSec>False</DelayInSec><MinDelay/><MaxDelay/>
    <TODStart/><TODEnd/><TODMode/>            <!-- TotalMinutes; 0 = Realtime, 1 = Gametime -->
    <KillReset/><MinRefractory/><MaxRefractory/><Duration/><DespawnTime/>
    <ExternalTriggering/><ProximityRange/><ProximityTriggerSound/><ProximityTriggerMessage/>
    <ObjectPropertyItemName/><ObjectPropertyName/><SetPropertyItemName/>
    <ItemTriggerName/><NoItemTriggerName/><MobTriggerName/><MobPropertyName/><PlayerPropertyName/>
    <TriggerProbability/><SequentialSpawning/><RegionName/>
    <AllowGhostTriggering/><AllowNPCTriggering/><SpawnOnTrigger/><ConfigFile/><TickReset/>
    <SpeechTrigger/><SkillTrigger/><Amount/><Team/><WayPoint/>
    <IsGroup/><IsRunning/><IsHomeRangeRelative/>
    <Objects2>…</Objects2>                     <!-- legacy files may have <Objects> instead -->
  </Points>
</Spawns>
```

`Objects2` is a colon-delimited list, one entry per `OBJ=` segment:

```
typestring:MX=n:SB=n:RT=d:TO=n:KL=n:RK=0|1:CA=0|1:DN=d:DX=d:SP=n:PR=n[:OBJ=typestring:MX=…]
MX max count   SB subgroup   RT restrict-kills-to-subgroup   TO timeout (min)   KL kill-count
RK reset-kills   CA clear-on-advance   DN/DX subgroup min/max delay (min)   SP spawns-per-tick   PR pack range
```

`typestring` is the spawn string (§4). Legacy `<Objects>` is `type:MX=n` only.

Numeric columns are written with `ToString()` in the server's culture; parse invariant with fallback.
`MinDelay`/`MaxDelay` are minutes unless `DelayInSec` is `True`. `TODStart`/`TODEnd` are `TotalMinutes`.

## 3. Column mapping

| XmlSpawner column | ModernSpawner target | Disposition |
|---|---|---|
| `Name` | `name` | copy |
| `UniqueId` | `guid` | copy (keeps re-import idempotent) |
| `Map`, `CentreX/Y/Z` | `map`, `location` | copy; `X/Y/Width/Height` → `spawnBounds` when Width/Height > 0, else `homeRange = Range` |
| `IsHomeRangeRelative` | — | if false and bounds absent, `homeRange` is absolute around Centre (same thing); warn if bounds *and* relative |
| `Range` | `homeRange` | copy |
| `MaxCount` | `count` | copy |
| `MinDelay`, `MaxDelay`, `DelayInSec` | `minDelay`, `maxDelay` | convert to TimeSpan |
| `Team` | `team` | copy |
| `WayPoint` | `wayPoint` (base) | resolve by name at import; warn if missing |
| `IsGroup` | `cycleMode = Group` | XmlSpawner "group" = respawn all when all dead; maps to Group mode, base `Group` left false |
| `IsRunning` | `running` | copy (DTO needs a `running` field — add) |
| `SequentialSpawning` (≥0) | `cycleMode = Sequential`, `currentSubgroup` | value is the starting subgroup |
| `Amount` | — | stack amount for item spawns; emit `target.Amount = n` in entry script when > 1 |
| `ProximityRange` (≥0) | trigger `proximity:range` | with `AllowGhostTriggering`, `AllowNPCTriggering` → `playersOnly` = !NPC; ghost flag unsupported → warn |
| `ProximityTriggerSound`, `ProximityTriggerMessage` | `onActivateScript`: `sound(id); broadcast("msg")` | translate |
| `TriggerProbability` (<100) | trigger `chance` field | add to every trigger type |
| `SpeechTrigger` | trigger `speech:keyword` | comma-separated keywords → one trigger per keyword |
| `SkillTrigger` | trigger `skill:name[:min]` | XmlSpawner format `SkillName,min,max`; max unsupported → warn |
| `TODStart`, `TODEnd`, `TODMode` | `game_time_window` (mode 1) / `wall_time_window` (mode 0) | minutes → hour:minute |
| `KillReset`, `MinRefractory`, `MaxRefractory` | trigger `cooldown` = random(min,max) refractory; `KillReset` → kill trigger `resetOnTrigger` | approximate → warn once per file |
| `Duration`, `DespawnTime` | entry `despawnAfter` | **not in ModernSpawner today** — add per-entry despawn timer, or warn and drop (decide) |
| `ExternalTriggering` | `triggerActivated = true` with no event triggers | external only via `Trigger()` |
| `SpawnOnTrigger` | `triggerActivated = true` | gate semantics (D2) |
| `RegionName` | trigger/positioning `region:name` | positioning rule `region` (planned) |
| `ObjectPropertyItemName/Name`, `SetPropertyItemName`, `Item/NoItem/Mob/Player*TriggerName/PropertyName` | property triggers | **unsupported** (PropertyTrigger removed by design) → warn and drop, include the expression in the report |
| `InContainer`, `Container*` | container spawning | unsupported → warn |
| `ConfigFile`, `TickReset` | — | drop silently |

## 4. Spawn-string grammar → entry

XmlSpawner: `[#prefix[,args]/][#CONDITION,expr/]TypeName[,arg,…]/Prop/Value[/Prop/Value…]`.
Keyword entries (`SET/…`, `SPAWN,…`, `GOTO/…`, `DESPAWN,…`, `COMMAND/…`, `GIVE/…`, `SETON*`) are entries whose
"type" is a keyword; they execute instead of spawning.

Parsing order matters: split on `/` first (property separator), then the first segment on `,`
(constructor args), then strip prefixes. The current importer splits on `,` first and swallows property
tails containing commas — a bug to fix.

| Element | Target | Disposition |
|---|---|---|
| `TypeName` | `entries[].name` | validate with `AssemblyHandler.FindTypeByName`; unresolved → error row |
| `,arg1,arg2` | `entries[].parameters` (space-separated) | copy |
| `/Prop/Value` literal | `properties` = `Prop Value …` (D6) | values with spaces are quoted per `CommandSystem.Split` |
| `/Prop/@literal` | `Prop literal` | strip `@` |
| `/Prop/0x..` | keep hex | ModernUO property parser accepts hex |
| `/Prop/RND,a,b` or `INC,a,b` | entry script `target.Prop = random(a,b)` / `target.Prop = target.Prop + random(a,b)` | translate |
| `/Prop/{expr}` substitution | entry script assignment with translated expr | translate where §5 allows, else warn |
| `/Prop/TRIGMOB` | `target.Prop = trigMob` | translate (needs entity assignment in coercion) |
| `/Prop/MOB,name` | `target.Prop = findMobile("name")` | add built-in; translate |
| `/Prop/PLAYERSINRANGE,r` | `playersNearby(r)` | translate |
| `/Prop/RANDNAME,type` | — | warn; ModernUO has `NameList.RandomName` — add `randomName("type")` built-in |
| `Skills[0]/Value/100` | `target.Skills[0].Value = 100`? | indexers unsupported → translate `Skills.Name/Value` form (`target.Skills.Swordsmanship.Base = 100`); numeric indexers warn |
| `Backpack.AddItem/Gold,1000` | loot template or `give(...)` | warn; suggest loot template |
| `#CONDITION,expr/Type` | `entries[].condition` (D4) | translate expr per §5 |
| `#RANDOM`, `*` | default / warn | `*` (ignore surface) unsupported |
| `#ROWFILL/#XFILL`, `#COLFILL/#YFILL` | `row_fill`, `column_fill` | translate |
| `#EDGE/#PERIMETER` | `perimeter` | translate (no edge cycling) |
| `#PLAYER` | `player_relative:0,0` | translate |
| `#WAYPOINT,name` | `waypoint:name` | translate |
| `#RELXY,x,y` | `relative:x,y` | translate |
| `#DXY,x,y` | `player_relative:x,y` | translate |
| `#XY,x,y[,z]` | `absolute:x,y,z` | translate |
| `#WET` | `water` | translate |
| `#TILES,…` / `#NOTILES,…` | `tiles:…` / `notiles:…` | translate |
| `#ITEMID,…` / `#NOITEMID,…` | `itemid:…` / `noitemid:…` | rules to add |
| `MX=` | `maxCount` | copy |
| `SB=` | `subgroup` | copy |
| `SP=` | spawns per tick | ModernSpawner has none; `SP>1` → warn (or add per-entry `spawnsPerTick`) |
| `DN=`/`DX=` | entry `minDelay`/`maxDelay` | copy (per-subgroup in XmlSpawner; per-entry here — note in report) |
| `RT=`, `KL=`, `RK=`, `CA=`, `TO=`, `PR=` | kill-tracking, timeout, pack range | warn and drop in v1 |

## 5. Keyword and condition translation

Condition grammar (`BaseXmlSpawner.TestItemProperty` family): `prop op value` with `=`, `!=`/`!`, `<`, `>`,
`<=`, `>=`, `~` (not), joined by `&` and `|`, operands `prop`, `TRIGMOB.prop`, `GETONTHIS,…`, literals.

| XmlSpawner | ModernSpawner | Disposition |
|---|---|---|
| `A=B`, `A!=B`, `A<B`… | `A == B`, `A != B`, … | translate |
| `~expr` | `not expr` | translate |
| `&`, `\|` | `and`, `or` | translate |
| `TRIGMOB.Karma` | `trigMob.Karma` | translate |
| `GETONTHIS,prop` | `spawner.prop` | translate |
| `GETONMOB,name,prop` | `findMobile("name").prop` | translate with built-in |
| `PLAYERSINRANGE,r` | `playersNearby(r)` | translate |
| `RND,a,b` | `random(a,b)` | translate |
| Keyword entries: `SET/prop/val` | entry script `setOn(spawner, …)`? — XmlSpawner `SET` targets the *spawner's SetItem*/`SETONTHIS` | translate `SETONTHIS`→`spawner.prop = …`, `SETONTRIGMOB`→`trigMob.prop = …`, `SETONMOB,name`→`setOn(findMobile("name"), …)`; bare `SET` → warn |
| `SPAWN,spawner,sub` | `spawn("spawner", sub)` | translate (subgroup-addressed after D4) |
| `DESPAWN,spawner,sub` | `despawn("spawner", sub)` | translate |
| `GOTO/sub` | `goto(sub)` | translate |
| `GIVE/type[/props]` | `give("type")` | add built-in; translate |
| `GUMP,…`, `WAIT,…` | — | warn and drop |
| `COMMAND/…` | — | reject (security), listed in report |

Keyword *entries* become script-only entries: entry with `name = ""`, `maxCount = 0`, and the translated
statements in `onSpawnScript`. Requires the runtime to support "action entries" that execute without
spawning (ModernSpawner has none today; add or reject). **Decision needed.**

## 6. Tool shape

- Command: `[ImportXmlSpawners <glob> [--dry-run] [--out <dir>]` (Administrator). Parses off-loop (files
  can be tens of MB), writes DTO JSON to `Distribution/Data/Spawns/imported/<file>.json` and a report to
  `<file>.report.md`, then (unless dry-run) invokes the same path as `[ImportSpawners`.
- Report per spawner: `guid`, name, location, status (`ok`/`approximated`/`partial`/`skipped`), and one line
  per warning with the original text and what was done.
- Summary: counts by status, unresolved types, unsupported keywords by frequency.
- Idempotent: re-importing a file with the same `UniqueId`s replaces spawners with matching `guid`.
- Also usable offline (a console entry point in the test/benchmark project or a `dotnet run` tool) so
  staff can validate a file without a live server. **Decision needed** whether this is the seed of the D7
  external tool.

## 7. Acceptance

- Fixture corpus: at least one real `[XmlSaveAll` output from a populated shard (needs a volunteer file),
  plus synthetic files covering every row in §3–§5.
- Golden tests: fixture → DTO JSON → import → assert spawner fields, entries, triggers, scripts compile.
- Report tests: every unsupported construct yields exactly one warning with the original text.
- Round trip: DTO JSON → `[ExportSpawners` → identical JSON.

## 8. Known gaps in the current importer to fix or replace

From `docs/audit/serialization-migration.md` §2e: wrong column names (`SequentialSpawn`, `SmartSpawning`,
`HoldSequence`), TOD minutes read as hours, realtime TOD dropped, `SP` treated as probability, comma-before-
slash split, culture-sensitive `Parse`, no type validation, `IsHomeRangeRelative` ignored, legacy
`<Objects>` ignored, no keyword handling, and `XmlSpawnerMigrator` reading formats nobody writes.
