using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Server.Engines.Events;
using Server.Engines.ModernSpawner.Migration;
using Server.Logging;

namespace Server.Engines.ModernSpawner.Serialization;

/// <summary>
/// Imports XmlSpawner configurations from XML format (both ServUO and Sno's export format).
/// </summary>
public static class XmlSpawnerImporter
{
    private static readonly ILogger Logger = LogFactory.GetLogger(typeof(XmlSpawnerImporter));

    /// <summary>
    /// Range used for a speech or property trigger when the point carries no <c>ProximityRange</c> of
    /// its own.
    /// </summary>
    private const int DefaultTriggerRange = 10;

    /// <summary>
    /// Import results from an XML file. <paramref name="Notes"/> carries one advisory line per spawner
    /// for anything the import approximated or dropped (a TOD window that no longer despawns on close, a
    /// <c>Duration</c> with no despawn timer to carry it, an untranslatable <c>PlayerPropertyName</c>,
    /// ...), the same mechanism <see cref="Errors"/> already uses rather than a second report type.
    /// </summary>
    public record ImportResult(int Imported, int Failed, List<string> Errors, List<string> Notes);

    /// <summary>
    /// Imports XmlSpawners from an XML file and creates ModernSpawner instances.
    /// Supports both ServUO XmlSpawner format (Points with Objects2) and Sno's export format.
    /// </summary>
    public static ImportResult ImportFromFile(string filePath, bool respawn = true)
    {
        if (!File.Exists(filePath))
        {
            return new ImportResult(0, 0, [$"File not found: {filePath}"], []);
        }

        var doc = new XmlDocument();
        try
        {
            doc.Load(filePath);
        }
        catch (Exception ex)
        {
            return new ImportResult(0, 0, [$"Failed to load XML: {ex.Message}"], []);
        }

        var errors = new List<string>();
        var notes = new List<string>();
        var imported = 0;
        var failed = 0;

        // Try ServUO XmlSpawner format first (Spawns/Points)
        var spawnsRoot = doc["Spawns"];
        if (spawnsRoot != null)
        {
            var points = spawnsRoot.GetElementsByTagName("Points");
            foreach (XmlElement point in points)
            {
                try
                {
                    var pointNotes = new List<string>();
                    var spawner = ImportXmlSpawnerPoint(point, pointNotes);
                    if (spawner != null)
                    {
                        if (respawn)
                        {
                            spawner.Respawn();
                        }
                        imported++;
                        foreach (var note in pointNotes)
                        {
                            notes.Add($"{spawner.Name}: {note}");
                        }
                    }
                    else
                    {
                        failed++;
                        errors.Add($"Failed to create spawner from point: {GetText(point["Name"], "unknown")}");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add($"Error importing point: {ex.Message}");
                }
            }

            return new ImportResult(imported, failed, errors, notes);
        }

        // Try Sno's export format (spawners/spawner)
        var spawnersRoot = doc["spawners"];
        if (spawnersRoot != null)
        {
            var spawners = spawnersRoot.GetElementsByTagName("spawner");
            foreach (XmlElement spawnerNode in spawners)
            {
                try
                {
                    var spawner = ImportSnoSpawner(spawnerNode);
                    if (spawner != null)
                    {
                        if (respawn)
                        {
                            spawner.Respawn();
                        }
                        imported++;
                    }
                    else
                    {
                        failed++;
                        errors.Add($"Failed to create spawner: {GetText(spawnerNode["name"], "unknown")}");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add($"Error importing spawner: {ex.Message}");
                }
            }

            return new ImportResult(imported, failed, errors, notes);
        }

        return new ImportResult(0, 0, ["Unrecognized XML format. Expected <Spawns> or <spawners> root element."], []);
    }

    /// <summary>
    /// Imports a ServUO XmlSpawner Point element.
    /// </summary>
    /// <param name="point">The Point element.</param>
    /// <param name="notes">Receives one advisory line per approximated or dropped attribute.</param>
    private static ModernSpawner ImportXmlSpawnerPoint(XmlElement point, List<string> notes)
    {
        // Parse location
        var centreX = int.Parse(GetText(point["CentreX"], "0"));
        var centreY = int.Parse(GetText(point["CentreY"], "0"));
        var centreZ = int.Parse(GetText(point["CentreZ"], "0"));
        var location = new Point3D(centreX, centreY, centreZ);

        // Parse map
        var mapName = GetText(point["Map"], "Felucca");
        var map = Map.Parse(mapName);
        if (map == null || map == Map.Internal)
        {
            Logger.Warning("Invalid map '{MapName}' for spawner, skipping", mapName);
            return null;
        }

        // Parse spawn area
        var x = int.Parse(GetText(point["X"], centreX.ToString()));
        var y = int.Parse(GetText(point["Y"], centreY.ToString()));
        var width = int.Parse(GetText(point["Width"], "0"));
        var height = int.Parse(GetText(point["Height"], "0"));
        var range = int.Parse(GetText(point["Range"], "4"));

        // Parse timing
        var minDelayMins = double.Parse(GetText(point["MinDelay"], "5"));
        var maxDelayMins = double.Parse(GetText(point["MaxDelay"], "10"));
        var delayInSec = bool.Parse(GetText(point["DelayInSec"], "False"));

        TimeSpan minDelay, maxDelay;
        if (delayInSec)
        {
            minDelay = TimeSpan.FromSeconds(minDelayMins);
            maxDelay = TimeSpan.FromSeconds(maxDelayMins);
        }
        else
        {
            minDelay = TimeSpan.FromMinutes(minDelayMins);
            maxDelay = TimeSpan.FromMinutes(maxDelayMins);
        }

        // Parse other properties
        var name = GetText(point["Name"], "XmlSpawner Import");
        var maxCount = int.Parse(GetText(point["MaxCount"], "1"));
        var team = int.Parse(GetText(point["Team"], "0"));
        var isGroup = bool.Parse(GetText(point["IsGroup"], "False"));
        var isRunning = bool.Parse(GetText(point["IsRunning"], "True"));
        var smartSpawning = bool.Parse(GetText(point["SmartSpawning"], "False"));
        var sequentialSpawn = int.Parse(GetText(point["SequentialSpawn"], "-1"));
        var holdSequence = bool.Parse(GetText(point["HoldSequence"], "False"));

        // Parse proximity/speech/property triggers
        var proximityRange = int.Parse(GetText(point["ProximityRange"], "-1"));
        var speechTrigger = GetText(point["SpeechTrigger"], null);
        var playerPropertyName = GetText(point["PlayerPropertyName"], null);

        // Refractory lockout: MinRefractory/MaxRefractory are minutes (dev-docs §2/§3).
        var minRefractory = double.Parse(GetText(point["MinRefractory"], "0"));
        var maxRefractory = double.Parse(GetText(point["MaxRefractory"], "0"));

        // SpawnOnTrigger=False defers the accepted event to the next tick (mode:tick) behind a one-slot
        // queue; SpawnOnTrigger=True or absent reproduces XmlSpawner's own run-now-or-drop semantics
        // (ruling §13.2).
        var spawnOnTrigger = bool.Parse(GetText(point["SpawnOnTrigger"], "True"));

        // Parse time of day. TODStart/TODEnd are TotalMinutes; TODMode 0 = Realtime (wall clock), 1 =
        // Gametime (dev-docs §2/§3).
        var todStart = double.Parse(GetText(point["TODStart"], "-1"));
        var todEnd = double.Parse(GetText(point["TODEnd"], "-1"));
        var todMode = int.Parse(GetText(point["TODMode"], "0"));

        var duration = double.Parse(GetText(point["Duration"], "-1"));

        // IsGroup maps to base Group only, never to the AllEntries cycle mode (design §7); only
        // SequentialSpawn drives the cycle mode now.
        var cycleMode = MapLegacyCycleMode(sequentialSpawn);

        // Create spawner
        var spawner = new ModernSpawner
        {
            Name = name,
            MinDelay = minDelay,
            MaxDelay = maxDelay,
            Team = team,
            Group = isGroup,
            Running = isRunning,
            UseSmartPositioning = smartSpawning,
            CycleMode = cycleMode,
            CurrentSubgroup = sequentialSpawn > 0 ? sequentialSpawn : 0,
            HoldSequence = holdSequence,
            MaxPendingCycles = spawnOnTrigger ? 0 : 1
        };

        if (minRefractory > 0 || maxRefractory > 0)
        {
            spawner.RefractoryMin = TimeSpan.FromMinutes(minRefractory);
            spawner.RefractoryMax = TimeSpan.FromMinutes(Math.Max(maxRefractory, minRefractory));
        }

        spawner.MoveToWorld(location, map);

        // Set spawn bounds
        if (width > 0 && height > 0)
        {
            spawner.SpawnBounds = new Rectangle3D(x, y, -128, width, height, 256);
        }
        else
        {
            spawner.HomeRange = range;
        }

        // Parse entries from Objects2
        var objects2 = GetText(point["Objects2"], "");
        if (!string.IsNullOrEmpty(objects2))
        {
            ParseObjects2(spawner, objects2, maxCount);
        }

        // XmlSpawner tests proximity, speech and the player property conjunctively (dev-docs §8), so a
        // SpeechTrigger folds the proximity range and the property test into one speech trigger rather
        // than independent (effectively OR'd) triggers.
        if (!string.IsNullOrEmpty(speechTrigger))
        {
            var trigRange = proximityRange >= 0 ? proximityRange : DefaultTriggerRange;
            var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(speechTrigger));
            var positional = $"speech:{encoded}:true:false:{trigRange}:true:5";

            spawner.AddTriggerDefinition(BuildEventDefinition(positional, spawnOnTrigger, playerPropertyName, notes));
            spawner.TriggerActivated = true;
        }
        else if (proximityRange >= 0)
        {
            var positional = $"proximity:{proximityRange}:true:false:5:0";

            spawner.AddTriggerDefinition(BuildEventDefinition(positional, spawnOnTrigger, playerPropertyName, notes));
            spawner.TriggerActivated = true;
        }
        else if (!string.IsNullOrEmpty(playerPropertyName))
        {
            var positional = $"proximity:{DefaultTriggerRange}:true:false:5:0";

            spawner.AddTriggerDefinition(BuildEventDefinition(positional, spawnOnTrigger, playerPropertyName, notes));
            spawner.TriggerActivated = true;
        }

        // Time-of-day gate. XmlSpawner despawned live spawns when the window closed; D2 keeps them
        // running, since per-spawn lifetimes are tracked separately under D10.
        if (todStart >= 0 || todEnd >= 0)
        {
            AddTimeOfDayGate(spawner, todMode, Math.Max(todStart, 0), Math.Max(todEnd, 0));
            notes?.Add("XmlSpawner despawned live spawns when the window closed; D2 keeps them (lifetimes are D10).");
        }

        // Duration is a per-spawn lifetime XmlSpawner enforced; ModernSpawner has no entry despawn timer
        // yet (D10), so the value is reported and dropped rather than approximated.
        if (duration > 0)
        {
            notes?.Add($"Duration ({duration} min) is a per-spawn lifetime; ModernSpawner has no entry despawn timer yet (D10) and the value was dropped.");
        }

        // The spawner was constructed running, so OnStarted never ran for these definitions.
        spawner.EnsureTriggersActive();

        return spawner;
    }

    /// <summary>
    /// Parses the Objects2 format used by XmlSpawner.
    /// Format: TypeName:MX=maxcount:SB=subgroup:SP=probability:DN=mindelay:DX=maxdelay:OBJ=NextType...
    /// </summary>
    /// <summary>
    /// Maps the legacy <c>SequentialSpawn</c> flag onto a <see cref="SpawnCycleMode"/>. Public so it can
    /// be used by other importers and exercised directly by unit tests.
    /// </summary>
    /// <remarks>
    /// <c>IsGroup</c> used to force <see cref="SpawnCycleMode.AllEntries"/> here, but XmlSpawner's
    /// "group" is respawn-all-when-all-dead - base <see cref="ModernSpawner.Group"/> - not the
    /// per-cycle-per-entry <c>AllEntries</c> mode (design §7); it is mapped there instead, and no longer
    /// affects the cycle mode at all.
    /// </remarks>
    public static SpawnCycleMode MapLegacyCycleMode(int sequentialSpawn) =>
        sequentialSpawn >= 0 ? SpawnCycleMode.Sequential : SpawnCycleMode.Random;

    private static void ParseObjects2(ModernSpawner spawner, string objects2, int defaultMaxCount)
    {
        // Split by :OBJ= to get individual entries
        var entries = objects2.Split(":OBJ=", StringSplitOptions.RemoveEmptyEntries);

        foreach (var entryStr in entries)
        {
            ParseEntry(spawner, entryStr, defaultMaxCount);
        }
    }

    /// <summary>
    /// Parses a single entry from the Objects2 format.
    /// </summary>
    private static void ParseEntry(ModernSpawner spawner, string entryStr, int defaultMaxCount)
    {
        var parts = entryStr.Split(':');
        if (parts.Length == 0)
        {
            return;
        }

        var typeName = parts[0].Trim();
        if (string.IsNullOrEmpty(typeName))
        {
            return;
        }

        // Parse entry properties
        var maxCount = defaultMaxCount;
        var probability = 100;
        var subgroup = 0;
        var minDelay = TimeSpan.Zero;
        var maxDelay = TimeSpan.Zero;
        string properties = null;

        for (var i = 1; i < parts.Length; i++)
        {
            var part = parts[i];
            var eqIndex = part.IndexOf('=');
            if (eqIndex <= 0)
            {
                continue;
            }

            var key = part[..eqIndex].ToUpperInvariant();
            var value = part[(eqIndex + 1)..];

            switch (key)
            {
                case "MX": // MaxCount
                    if (int.TryParse(value, out var mx))
                    {
                        maxCount = mx;
                    }
                    break;
                case "SB": // Subgroup
                    if (int.TryParse(value, out var sb))
                    {
                        subgroup = sb;
                    }
                    break;
                case "SP": // Spawn probability (1 = 100%)
                    if (double.TryParse(value, out var sp))
                    {
                        probability = (int)(sp * 100);
                        if (probability <= 0)
                        {
                            probability = 100;
                        }
                    }
                    break;
                case "DN": // Min delay
                    if (double.TryParse(value, out var dn) && dn > 0)
                    {
                        minDelay = TimeSpan.FromMinutes(dn);
                    }
                    break;
                case "DX": // Max delay
                    if (double.TryParse(value, out var dx) && dx > 0)
                    {
                        maxDelay = TimeSpan.FromMinutes(dx);
                    }
                    break;
                // Additional properties could be parsed here (RT, TO, KL, RK, CA, PR)
            }
        }

        // Handle type name with optional parameters (e.g., "Gold,100,500" for Gold(100,500))
        string parameters = null;
        var commaIndex = typeName.IndexOf(',');
        if (commaIndex > 0)
        {
            parameters = typeName[(commaIndex + 1)..].Replace(',', ' ');
            typeName = typeName[..commaIndex];
        }

        // Handle property assignments in type name (e.g., "Orc/Hue/33")
        var slashIndex = typeName.IndexOf('/');
        if (slashIndex > 0)
        {
            var propStr = typeName[(slashIndex + 1)..];
            typeName = typeName[..slashIndex];
            properties = ParseXmlSpawnerProperties(propStr);
        }

        var entry = spawner.AddModernEntry(
            creatureName: typeName,
            probability: probability,
            maxCount: maxCount,
            properties: properties,
            parameters: parameters,
            minDelay: minDelay,
            maxDelay: maxDelay,
            dotimer: false
        );
        entry.Subgroup = subgroup;
    }

    /// <summary>
    /// Converts XmlSpawner property format (Prop/Value/Prop2/Value2) to ModernSpawner format (Prop=Value/Prop2=Value2).
    /// </summary>
    private static string ParseXmlSpawnerProperties(string propStr)
    {
        var parts = propStr.Split('/');
        if (parts.Length < 2)
        {
            return null;
        }

        var result = new List<string>();
        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            var propName = parts[i].Trim();
            var propValue = parts[i + 1].Trim();
            if (!string.IsNullOrEmpty(propName))
            {
                result.Add($"{propName}={propValue}");
            }
        }

        return result.Count > 0 ? string.Join("/", result) : null;
    }

    /// <summary>
    /// Imports a spawner from Sno's export format.
    /// </summary>
    private static ModernSpawner ImportSnoSpawner(XmlElement node)
    {
        var count = int.Parse(GetText(node["count"], "1"));
        var homeRange = int.Parse(GetText(node["homerange"], "4"));
        var walkingRange = int.Parse(GetText(node["walkingrange"], "-1"));
        var team = int.Parse(GetText(node["team"], "0"));
        var group = bool.Parse(GetText(node["group"], "False"));

        var maxDelay = TimeSpan.Parse(GetText(node["maxdelay"], "10:00"));
        var minDelay = TimeSpan.Parse(GetText(node["mindelay"], "05:00"));

        var name = GetText(node["name"], "Spawner Import");
        var locationStr = GetText(node["location"], "");
        var mapName = GetText(node["map"], "Felucca");

        if (string.IsNullOrEmpty(locationStr))
        {
            Logger.Warning("No location specified for spawner '{Name}', skipping", name);
            return null;
        }

        var location = Point3D.Parse(locationStr);
        var map = Map.Parse(mapName);
        if (map == null || map == Map.Internal)
        {
            Logger.Warning("Invalid map '{MapName}' for spawner '{Name}', skipping", mapName, name);
            return null;
        }

        var spawner = new ModernSpawner
        {
            Name = name,
            MinDelay = minDelay,
            MaxDelay = maxDelay,
            Team = team,
            Group = group,
            HomeRange = homeRange
        };

        if (walkingRange >= 0)
        {
            spawner.WalkingRange = walkingRange;
        }

        spawner.MoveToWorld(location, map);

        // Parse creature names
        var creaturesNode = node["creaturesname"];
        if (creaturesNode != null)
        {
            foreach (XmlElement ele in creaturesNode.GetElementsByTagName("creaturename"))
            {
                var creatureName = ele?.InnerText?.Trim();
                if (!string.IsNullOrEmpty(creatureName))
                {
                    spawner.AddModernEntry(
                        creatureName: creatureName,
                        probability: 100,
                        maxCount: count,
                        dotimer: false
                    );
                }
            }
        }

        return spawner;
    }

    private static string GetText(XmlElement node, string defaultValue)
    {
        return node?.InnerText ?? defaultValue;
    }

    /// <summary>
    /// Builds the final trigger definition text from <paramref name="positional" />: the shared
    /// <c>mode:tick</c> token first, then <c>when:</c> last - <see cref="Triggers.TriggerTokens" />
    /// documents <c>when:</c> as consuming everything after it, so a token appended past it would be
    /// swallowed into the expression source and never parsed, leaving the trigger permanently inert. The
    /// single writer for every event trigger definition this importer emits, so the ordering cannot drift
    /// between call sites.
    /// </summary>
    /// <param name="positional">The trigger's positional argument list, with no tokens yet.</param>
    /// <param name="spawnOnTrigger">XmlSpawner's <c>SpawnOnTrigger</c>; false appends <c>mode:tick</c>.</param>
    /// <param name="playerPropertyName">The raw <c>PlayerPropertyName</c> test, or null/empty for none.</param>
    /// <param name="notes">Receives a report line when the property test cannot be translated.</param>
    private static string BuildEventDefinition(string positional, bool spawnOnTrigger, string playerPropertyName, List<string> notes)
    {
        var definition = spawnOnTrigger ? positional : positional + ":mode:tick";

        if (string.IsNullOrEmpty(playerPropertyName))
        {
            return definition;
        }

        if (XmlSpawnerPropertyExpression.TryTranslate(playerPropertyName, out var expression, out var reason))
        {
            return $"{definition}:when:{expression}";
        }

        notes?.Add(
            $"PlayerPropertyName '{playerPropertyName}' could not be translated ({reason}); the trigger was migrated without its when: condition."
        );
        return definition;
    }

    /// <summary>
    /// Adds the time-of-day gate matching <paramref name="todMode" />: 0 (Realtime) is a wall-clock
    /// window, 1 (Gametime) is an in-game-hour window (dev-docs §2/§3). <paramref name="todStartMinutes" />
    /// and <paramref name="todEndMinutes" /> are <c>TotalMinutes</c>, as XmlSpawner wrote them.
    /// </summary>
    private static void AddTimeOfDayGate(ModernSpawner spawner, int todMode, double todStartMinutes, double todEndMinutes)
    {
        var startHour = (int)(todStartMinutes / 60) % 24;
        var startMinute = (int)(todStartMinutes % 60);
        var endHour = (int)(todEndMinutes / 60) % 24;
        var endMinute = (int)(todEndMinutes % 60);

        if (todMode == 1)
        {
            // GameTimeWindowTrigger only carries whole hours; the minute component is dropped.
            spawner.AddTriggerDefinition($"game_time_window:{startHour}:{endHour}:false:false");
        }
        else
        {
            spawner.AddTriggerDefinition(
                $"wall_time_window:{startHour}:{startMinute}:{endHour}:{endMinute}:{(int)AllowedDays.All}:{(int)AllowedMonths.All}:{TimeZoneInfo.Utc.Id}"
            );
        }

        spawner.TriggerActivated = true;
    }
}
