# XmlSpawner → ModernSpawner Migration Requirements

Status: draft v1, 2026-09-08. Source-format facts were verified against the legacy XmlSpawner code in
`modernuo/XmlSpawner-for-Modernuo` (`XmlSpawner/XmlSpawner.cs`, `BaseXmlSpawner.cs`, `SpawnerExporter.cs`)
and the archived knowledge base (`docs/archive/XmlSpawner-Knowledge-Base.md`, gitignored). Assumes decisions D4–D6 and D9
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

`Objects2` is a colon-delimited list, one entry per `OBJ=` segment (writer: `XmlSpawner.cs:11561-11592`):

```
typestring:MX=n:SB=n:RT=d:TO=n:KL=n:RK=0|1:CA=0|1:DN=d:DX=d:SP=n:PR=n[:OBJ=typestring:MX=…]
MX max count            SB subgroup                 RT sequential reset time (min)
TO reset-to subgroup    KL kills required           RK restrict kills to subgroup (0|1)
CA clear on advance     DN/DX subgroup min/max delay (min)   SP spawns per tick   PR pack range
```

`typestring` is the spawn string (§4) and may itself contain colons and commas (substitutions such as
`{RND,4,8}`), so the parser must split on the known `:MX=`/`:OBJ=` key boundaries, not on every colon.
Legacy `<Objects>` (pre-Objects2 files) is `type=count:type=count`.

Numeric columns are written with `ToString()` in the server's culture; parse invariant with fallback.
`MinDelay`/`MaxDelay` are minutes unless `DelayInSec` is `True`. `TODStart`/`TODEnd` are `TotalMinutes`.
`Duration` is in **minutes**, `DespawnTime` in **hours** (`XmlSpawner.cs:6695,6742`). `TriggerProbability` is a
fraction compared against `RandomDouble()`, not a percentage (`XmlSpawner.cs:2324`). `WayPoint` is a name or
`SERIAL,n`. The supported dialect is the ServUO-era XmlSpawner carried in `XmlSpawner-for-Modernuo`;
older RunUO XmlSpawner2 exports may differ and are reported, not silently accepted.

## 3. Column mapping

| XmlSpawner column | ModernSpawner target | Disposition |
|---|---|---|
| `Name` | `name` | copy |
| `UniqueId` | `guid` | copy (keeps re-import idempotent) |
| `Map`, `CentreX/Y/Z` | `map`, `location` | copy; `X/Y/Width/Height` → `spawnBounds` (the spawn area); Width/Height 0 → bounds = the spawner tile |
| `Range` | `walkingRange` | XmlSpawner `HomeRange` is the creature's `RangeHome` (`XmlSpawner.cs:8604`), not the spawn area |
| `IsHomeRangeRelative` | `spawnLocationIsHome` | true = home is where it spawned; false = home is the spawner |
| `MaxCount` | `count` | copy |
| `MinDelay`, `MaxDelay`, `DelayInSec` | `minDelay`, `maxDelay` | convert to TimeSpan |
| `Team` | `team` | copy |
| `WayPoint` | `wayPoint` (base) | name or `SERIAL,n`; resolve at import; warn if missing |
| `IsGroup` | `cycleMode = Group` | XmlSpawner "group" = respawn all when all dead; maps to Group mode, base `Group` left false |
| `IsRunning` | `running` | copy (DTO needs a `running` field — add) |
| `SequentialSpawning` (≥0) | `cycleMode = Sequential`, `currentSubgroup` | value is the starting subgroup |
| `Amount` | — | stack amount for item spawns; emit `target.Amount = n` in entry script when > 1 |
| `ProximityRange` (≥0) | trigger `proximity:range` | with `AllowGhostTriggering`, `AllowNPCTriggering` → `playersOnly` = !NPC; ghost flag unsupported → warn |
| `ProximityTriggerSound`, `ProximityTriggerMessage` | trigger `onTriggered` feedback: `sound(id)`, `msg(trigMob, "text")` | fires on an *accepted* trigger with the triggering mobile (`XmlSpawner.cs:2324`), not on activate |
| `TriggerProbability` (fraction) | spawner-level trigger `chance` | one roll per accepted trigger, not per trigger type |
| `SpeechTrigger` | trigger `speech:text` | one case-insensitive substring match (`XmlSpawner.cs:2385`); do not split on commas |
| `SkillTrigger` | trigger `skill:<Skill>[+\|-]:<range>:<min>[-<max>]:<los>:<cooldown>` | XmlSpawner syntax `SkillName[+/-][,min,max]` (`+` success only, `-` failure only) maps directly onto the outcome suffix and value window; `<range>` is the node's `ProximityRange` (10 when absent); XmlSpawner's own skill trigger never fired (verified in both the ServUO sources and the ModernUO port: the parsed skill-trigger fields are declared and read, but never assigned), so there is no runtime behaviour to preserve — only the intended semantics carry over |
| `TODStart`, `TODEnd`, `TODMode` | `game_time_window` (mode 1) / `wall_time_window` (mode 0) | minutes → hour:minute |
| `MinRefractory`, `MaxRefractory` | spawner-level trigger refractory `random(min,max)` | belongs to the spawner's accepted-trigger state, not to each translated trigger |
| `KillReset` | kill trigger `resetAfterTicks` | count of spawn ticks without a kill before the kill counter resets (`XmlSpawner.cs:6735`) — add field or warn |
| `TickReset` | `disableGlobalAutoReset` | XmlSpawner semantics; drop only with a warning, never silently |
| `Duration` (minutes) | entry `despawnAfter` | per-spawn lifetime; **not in ModernSpawner today** — add per-entry despawn timer or warn |
| `DespawnTime` (hours) | spawner `despawnWhenIdle` | spawner-level despawn when no players nearby; distinct mechanism — warn in v1 |
| `ExternalTriggering` | `triggerActivated = true` with no event triggers | external only via `Trigger()` |
| `SpawnOnTrigger` | trigger fires an immediate spawn cycle | XmlSpawner conditions are **conjunctive** (running, TOD, refractory, external, speech, property all checked together, `XmlSpawner.cs:2236`); translated triggers must reproduce that with a per-spawner condition set, not independent OR triggers |
| `RegionName` | trigger/positioning `region:name` | positioning rule `region` (planned) |
| `ObjectPropertyItemName/Name`, `SetPropertyItemName`, `Item/NoItem/Mob/Player*TriggerName/PropertyName` | property triggers | **unsupported** (PropertyTrigger removed by design) → warn and drop, include the expression in the report |
| `InContainer`, `Container*` | container spawning | unsupported → warn |
| `ConfigFile`, `TickReset` | — | drop silently |

## 4. Spawn-string grammar → entry

XmlSpawner: `[#prefix[,args]/][#CONDITION,expr/]TypeName[,arg,…]/Prop/Value[/Prop/Value…]`.
Keyword entries (`SET/…`, `SPAWN,…`, `GOTO/…`, `DESPAWN,…`, `COMMAND/…`, `GIVE/…`, `SETON*`) are entries whose
"type" is a keyword; they execute instead of spawning.

Parsing must follow the source (`XmlSpawner.cs:8397-8420`): first perform `{…}` substitutions on the whole
string, then peel `#PREFIX` directives repeatedly (they are separated by `;` and may stack, e.g.
`#XY,1,2;#WET/Type`), then split the remainder on `/`, then the first segment on `,` for constructor
arguments. Constructor arguments can themselves be substitutions with commas (`Phantom,{RND,4,8},{RND,1,5}`
appears in distributed spawn files), so substitution runs before any splitting. The current importer splits
on `,` first and swallows property tails containing commas — a bug to fix. Prefixes are a *set*, not a
single slot.

| Element | Target | Disposition |
|---|---|---|
| `TypeName` | `entries[].name` | validate with `AssemblyHandler.FindTypeByName`; unresolved → error row |
| `,arg1,arg2` | `entries[].parameters` (space-separated) | copy; arguments containing `{RND,…}`/`{…}` substitutions must be evaluated per spawn → needs an entry-level `parametersExpression` or warn |
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
| `#RELXY,x,y` | `relative_to_last:x,y` | relative to the **previous spawn position** (`XmlSpawner.cs:9998`); needs ordered placement state — rule to add |
| `#DXY,x,y` | `relative:x,y` | relative to the **spawner**, not the player |
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
| `&`, `\|` | `and`, `or` | translate **preserving XmlSpawner's grouping**: mixed operators nest right-recursively (`A & B \| C` = `A & (B \| C)`, `BaseXmlSpawner.cs:1752`), so emit explicit parentheses |
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
| `SET,0x40001234/prop/val` (serial target) | `setOn(findSerial(0x40001234), …)` | serials rarely survive a migration; resolve at import, report unresolved, and **disable the entry** rather than run a fragment |
| `GUMP,…`, `WAIT,…` | — | `WAIT` holds the sequence at runtime (`XmlSpawner.cs:8050`); dropping it and running the rest changes the encounter. Mark the spawner `running=false` with a report line unless the operator opts into approximation |
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

- Fixture corpus: a real `[XmlSaveAll` export from a partner shard (promised; the shard is not named in
  this repo or the fixtures) plus synthetic files covering every row in §3–§5. The real export includes
  XmlAttach, XmlSockets and XmlQuest content, which is out of scope: the converter must classify those
  spawners/entries and report them rather than fail, and the fixture may be filtered to the in-scope subset.
  Fixtures are anonymised (spawner names and notes that identify the shard are scrubbed) before commit.
- Golden tests: fixture → DTO JSON → import → assert spawner fields, entries, triggers, scripts compile.
- Report tests: every unsupported construct yields exactly one warning with the original text.
- Round trip: DTO JSON → `[ExportSpawners` → identical JSON.

## 8. Known gaps in the current importer to fix or replace

From `docs/audit/serialization-migration.md` §2e: wrong column names (`SequentialSpawn`, `SmartSpawning`,
`HoldSequence`), TOD minutes read as hours, realtime TOD dropped, `SP` treated as probability, comma-before-
slash split, culture-sensitive `Parse`, no type validation, `IsHomeRangeRelative` ignored, legacy
`<Objects>` ignored, no keyword handling, and `XmlSpawnerMigrator` reading formats nobody writes.
