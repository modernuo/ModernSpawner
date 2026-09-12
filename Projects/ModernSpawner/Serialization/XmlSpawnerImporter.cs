using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Server.Logging;

namespace Server.Engines.ModernSpawner.Serialization;

/// <summary>
/// Imports XmlSpawner configurations from XML format (both ServUO and Sno's export format).
/// </summary>
public static class XmlSpawnerImporter
{
    private static readonly ILogger Logger = LogFactory.GetLogger(typeof(XmlSpawnerImporter));

    /// <summary>
    /// Import results from an XML file.
    /// </summary>
    public record ImportResult(int Imported, int Failed, List<string> Errors);

    /// <summary>
    /// Imports XmlSpawners from an XML file and creates ModernSpawner instances.
    /// Supports both ServUO XmlSpawner format (Points with Objects2) and Sno's export format.
    /// </summary>
    public static ImportResult ImportFromFile(string filePath, bool respawn = true)
    {
        if (!File.Exists(filePath))
        {
            return new ImportResult(0, 0, [$"File not found: {filePath}"]);
        }

        var doc = new XmlDocument();
        try
        {
            doc.Load(filePath);
        }
        catch (Exception ex)
        {
            return new ImportResult(0, 0, [$"Failed to load XML: {ex.Message}"]);
        }

        var errors = new List<string>();
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
                    var spawner = ImportXmlSpawnerPoint(point);
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
                        errors.Add($"Failed to create spawner from point: {GetText(point["Name"], "unknown")}");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add($"Error importing point: {ex.Message}");
                }
            }

            return new ImportResult(imported, failed, errors);
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

            return new ImportResult(imported, failed, errors);
        }

        return new ImportResult(0, 0, ["Unrecognized XML format. Expected <Spawns> or <spawners> root element."]);
    }

    /// <summary>
    /// Imports a ServUO XmlSpawner Point element.
    /// </summary>
    private static ModernSpawner ImportXmlSpawnerPoint(XmlElement point)
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

        // Parse proximity trigger
        var proximityRange = int.Parse(GetText(point["ProximityRange"], "-1"));

        // Parse time of day
        var todStart = double.Parse(GetText(point["TODStart"], "0"));
        var todEnd = double.Parse(GetText(point["TODEnd"], "0"));
        var todMode = int.Parse(GetText(point["TODMode"], "0"));

        // Map legacy flag combinations onto SpawnCycleMode.
        var cycleMode = MapLegacyCycleMode(isGroup, sequentialSpawn);

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
            HoldSequence = holdSequence
        };

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

        // Add proximity trigger if specified
        if (proximityRange > 0)
        {
            spawner.AddTriggerDefinition($"proximity:{proximityRange}:true");
            spawner.TriggerActivated = true;
        }

        // Add time of day trigger if specified
        if (todMode > 0 && (todStart > 0 || todEnd > 0))
        {
            var startHour = (int)todStart;
            var endHour = (int)todEnd;
            spawner.AddTriggerDefinition($"game_time_window:{startHour}:{endHour}:false:false");
            spawner.TriggerActivated = true;
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
    /// Maps the legacy <c>IsGroup</c> / <c>SequentialSpawn</c> flag pair onto a
    /// <see cref="SpawnCycleMode"/>. Public so it can be used by other importers
    /// and exercised directly by unit tests.
    /// </summary>
    public static SpawnCycleMode MapLegacyCycleMode(bool isGroup, int sequentialSpawn)
    {
        if (isGroup)
        {
            return SpawnCycleMode.AllEntries;
        }

        if (sequentialSpawn >= 0)
        {
            return SpawnCycleMode.Sequential;
        }

        return SpawnCycleMode.Random;
    }

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
}
