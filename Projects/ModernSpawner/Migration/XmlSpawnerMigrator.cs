using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Server.Logging;

namespace Server.Engines.ModernSpawner.Migration;

/// <summary>
/// Handles importing XmlSpawner XML save files as ModernSpawner instances.
/// Parses the XML format directly without referencing the XmlSpawner project.
/// </summary>
public static class XmlSpawnerMigrator
{
    private static readonly ILogger Logger = LogFactory.GetLogger(typeof(XmlSpawnerMigrator));

    /// <summary>
    /// Registers the migration commands.
    /// </summary>
    public static void Initialize()
    {
        CommandSystem.Register("ImportXmlSpawners", AccessLevel.Administrator, ImportXmlSpawners_OnCommand);
    }

    [Usage("ImportXmlSpawners <path to xml file>")]
    [Description("Imports XmlSpawner save files as ModernSpawner instances.")]
    private static void ImportXmlSpawners_OnCommand(CommandEventArgs e)
    {
        if (e.Arguments.Length == 0)
        {
            e.Mobile.SendMessage("Usage: [ImportXmlSpawners <path to xml file>");
            return;
        }

        var path = e.Arguments[0];
        if (!File.Exists(path))
        {
            // Try relative to base directory
            path = Path.Combine(Core.BaseDirectory, e.Arguments[0]);
            if (!File.Exists(path))
            {
                e.Mobile.SendMessage($"File not found: {e.Arguments[0]}");
                return;
            }
        }

        e.Mobile.SendMessage($"Importing XmlSpawners from {path}...");

        var (success, failed) = ImportFromFile(path);

        e.Mobile.SendMessage($"Import complete. Created: {success}, Failed: {failed}");
        Logger.Information("Imported {Success} XmlSpawners, {Failed} failures from {Path}", success, failed, path);
    }

    /// <summary>
    /// Imports XmlSpawner data from an XML file.
    /// </summary>
    public static (int success, int failed) ImportFromFile(string path)
    {
        var success = 0;
        var failed = 0;

        try
        {
            var doc = new XmlDocument();
            doc.Load(path);

            // Try XmlSpawner save format first
            var spawners = doc.SelectNodes("//XmlSpawner");
            if (spawners is { Count: > 0 })
            {
                foreach (XmlNode node in spawners)
                {
                    try
                    {
                        var spawner = ParseXmlSpawnerNode(node);
                        if (spawner != null)
                        {
                            success++;
                        }
                        else
                        {
                            failed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning(ex, "Failed to parse XmlSpawner node");
                        failed++;
                    }
                }
            }

            // Also try SpawnPoint format (another common XmlSpawner export format)
            var spawnPoints = doc.SelectNodes("//SpawnPoint");
            if (spawnPoints is { Count: > 0 })
            {
                foreach (XmlNode node in spawnPoints)
                {
                    try
                    {
                        var spawner = ParseSpawnPointNode(node);
                        if (spawner != null)
                        {
                            success++;
                        }
                        else
                        {
                            failed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning(ex, "Failed to parse SpawnPoint node");
                        failed++;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to load XML file: {Path}", path);
        }

        return (success, failed);
    }

    /// <summary>
    /// Parses an XmlSpawner node from the save format.
    /// </summary>
    internal static ModernSpawner ParseXmlSpawnerNode(XmlNode node)
    {
        // Parse location
        var x = GetIntAttribute(node, "X", 0);
        var y = GetIntAttribute(node, "Y", 0);
        var z = GetIntAttribute(node, "Z", 0);
        var mapName = GetAttribute(node, "Map", "Felucca");
        var map = Map.Parse(mapName);

        if (map == null || map == Map.Internal)
        {
            return null;
        }

        // Create the spawner
        var spawner = new ModernSpawner
        {
            Name = GetAttribute(node, "Name", "Imported Spawner")
        };

        // Parse timing
        var minDelayStr = GetAttribute(node, "MinDelay", "00:05:00");
        var maxDelayStr = GetAttribute(node, "MaxDelay", "00:10:00");

        if (TimeSpan.TryParse(minDelayStr, out var minDelay))
        {
            spawner.MinDelay = minDelay;
        }

        if (TimeSpan.TryParse(maxDelayStr, out var maxDelay))
        {
            spawner.MaxDelay = maxDelay;
        }

        // Parse range settings
        spawner.HomeRange = GetIntAttribute(node, "HomeRange", 5);
        spawner.WalkingRange = GetIntAttribute(node, "WalkingRange", -1);
        spawner.Team = GetIntAttribute(node, "Team", 0);

        // Parse spawn area
        var spawnRange = GetIntAttribute(node, "SpawnRange", -1);
        if (spawnRange > 0)
        {
            spawner.SpawnBounds = new Rectangle3D(
                new Point3D(x - spawnRange, y - spawnRange, z - 20),
                new Point3D(x + spawnRange, y + spawnRange, z + 20)
            );
        }

        // Parse trigger settings
        var proximityRange = GetIntAttribute(node, "ProximityRange", -1);
        if (proximityRange >= 0)
        {
            spawner.TriggerActivated = true;
            spawner.AddTriggerDefinition($"proximity:{proximityRange}:true:false:5:0");
        }

        var speechTrigger = GetAttribute(node, "SpeechTrigger", null);
        if (!string.IsNullOrEmpty(speechTrigger))
        {
            spawner.TriggerActivated = true;
            var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(speechTrigger));
            spawner.AddTriggerDefinition($"speech:{encoded}:true:false:10:true:5");
        }

        var skillTrigger = GetAttribute(node, "SkillTrigger", null);
        if (!string.IsNullOrWhiteSpace(skillTrigger))
        {
            var definition = MapSkillTrigger(skillTrigger, proximityRange < 0 ? 10 : proximityRange);
            if (definition != null)
            {
                spawner.AddTriggerDefinition(definition);
                spawner.TriggerActivated = true;
            }
        }

        // Parse spawn objects
        var objectsNode = node.SelectSingleNode("SpawnObjects") ?? node.SelectSingleNode("Objects");
        if (objectsNode != null)
        {
            foreach (XmlNode objNode in objectsNode.ChildNodes)
            {
                if (objNode.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                var typeName = GetAttribute(objNode, "Type", null) ?? GetAttribute(objNode, "Name", null) ?? objNode.InnerText;
                if (string.IsNullOrEmpty(typeName))
                {
                    continue;
                }

                var maxCount = GetIntAttribute(objNode, "MaxCount", 1);
                var subGroup = GetIntAttribute(objNode, "SubGroup", 0);

                // Parse XmlSpawner-style type string (TypeName/prop/value/prop/value)
                string properties = null;
                string parameters = null;

                var slashIndex = typeName.IndexOf('/');
                if (slashIndex > 0)
                {
                    properties = typeName[(slashIndex + 1)..];
                    typeName = typeName[..slashIndex];
                }

                // Check for constructor params (comma separated)
                var commaIndex = typeName.IndexOf(',');
                if (commaIndex > 0)
                {
                    parameters = typeName[(commaIndex + 1)..];
                    typeName = typeName[..commaIndex];
                }

                spawner.AddModernEntry(
                    typeName,
                    probability: 100,
                    maxCount: maxCount,
                    properties: ConvertPropertiesToModernFormat(properties),
                    parameters: parameters,
                    spawnGroup: subGroup > 0 ? $"group_{subGroup}" : null,
                    dotimer: false
                );
            }
        }

        // Place the spawner
        spawner.MoveToWorld(new Point3D(x, y, z), map);

        // Start if it was running, stop otherwise - the spawner is constructed already running, so
        // "Running=false" (or no entries to run with) has to be applied explicitly.
        var running = GetBoolAttribute(node, "Running", true);
        if (running && spawner.Entries.Count > 0)
        {
            spawner.Start();
        }
        else
        {
            spawner.Stop();
        }

        // Start()/Stop() only reach OnStarted/OnStopped when Running flips; the spawner was constructed
        // running, so register (or unregister) explicitly for the state the file asked for.
        spawner.EnsureTriggersActive();

        return spawner;
    }

    /// <summary>
    /// Parses a SpawnPoint node (alternative XmlSpawner export format).
    /// </summary>
    internal static ModernSpawner ParseSpawnPointNode(XmlNode node)
    {
        var x = GetIntAttribute(node, "X", 0);
        var y = GetIntAttribute(node, "Y", 0);
        var z = GetIntAttribute(node, "Z", 0);
        var mapName = GetAttribute(node, "Map", "Felucca");
        var map = Map.Parse(mapName);

        if (map == null || map == Map.Internal)
        {
            return null;
        }

        var spawner = new ModernSpawner
        {
            Name = GetAttribute(node, "SpawnerName", "Imported Spawner"),
            HomeRange = GetIntAttribute(node, "HomeRange", 5)
        };

        // Parse creatures/items
        var creatures = GetAttribute(node, "Creatures", null);
        if (!string.IsNullOrEmpty(creatures))
        {
            foreach (var creature in creatures.Split(':'))
            {
                if (!string.IsNullOrWhiteSpace(creature))
                {
                    spawner.AddModernEntry(creature.Trim(), dotimer: false);
                }
            }
        }

        spawner.MoveToWorld(new Point3D(x, y, z), map);

        // This node form has no dedicated attribute for stopped spawners in the wild, but honour one if
        // present; default true preserves the historical "always start" behavior when it is absent.
        var running = GetBoolAttribute(node, "Running", true);
        if (running && spawner.Entries.Count > 0)
        {
            spawner.Start();
        }
        else
        {
            spawner.Stop();
        }

        // Start()/Stop() only reach OnStarted/OnStopped when Running flips; the spawner was constructed
        // running, so register (or unregister) explicitly for the state the file asked for.
        spawner.EnsureTriggersActive();

        return spawner;
    }

    /// <summary>
    /// XmlSpawner <c>SkillTrigger</c> is <c>SkillName[+|-][,min[,max]]</c>; the modern grammar keeps the
    /// suffix and folds min/max into the value window. An absent or unparseable bound is dropped rather
    /// than rejected, so <c>"Mining,,90"</c> is min 0 / max 90 - the same reading XmlSpawner gave it.
    /// Returns null, after a warning, when the skill name is unknown or the window is inverted
    /// (<c>max &lt; min</c>); the caller then adds no definition at all.
    /// </summary>
    internal static string MapSkillTrigger(string xml, int range)
    {
        var parts = xml.Split(',');
        var name = parts[0].Trim();
        var suffix = "";
        if (name.EndsWith('+') || name.EndsWith('-'))
        {
            suffix = name[^1..];
            name = name[..^1];
        }

        // TryParse accepts any numeric string ("99") as a SkillName, so the value has to be checked
        // against the enum as well.
        if (!name.Equals("Any", StringComparison.OrdinalIgnoreCase) &&
            (!Enum.TryParse<SkillName>(name, true, out var parsedSkill) || !Enum.IsDefined(parsedSkill)))
        {
            Logger.Warning("Skipping SkillTrigger with unknown skill name: {SkillName}", name);
            return null;
        }

        var min = parts.Length > 1 && double.TryParse(parts[1], out var parsedMin) && parsedMin > 0 ? parsedMin : 0;
        var max = parts.Length > 2 && double.TryParse(parts[2], out var parsedMax) && parsedMax > 0 ? parsedMax : -1;

        // An inverted window can never match, and SkillTrigger.Parse rejects it too; drop the whole
        // trigger down the same path as an unknown name rather than emitting a definition that dies later.
        if (max >= 0 && max < min)
        {
            Logger.Warning("Skipping SkillTrigger with inverted value window: {SkillTrigger}", xml);
            return null;
        }

        var window = max < 0 ? $"{min}" : $"{min}-{max}";
        return $"skill:{name}{suffix}:{range}:{window}:False:5";
    }

    /// <summary>
    /// Converts XmlSpawner property format (prop/value/prop/value) to ModernSpawner format.
    /// </summary>
    private static string ConvertPropertiesToModernFormat(string xmlSpawnerProps)
    {
        if (string.IsNullOrEmpty(xmlSpawnerProps))
        {
            return null;
        }

        // XmlSpawner format: prop/value/prop/value
        // ModernSpawner format: prop value prop value (space separated pairs)
        var parts = xmlSpawnerProps.Split('/');
        if (parts.Length < 2)
        {
            return null;
        }

        var result = new List<string>();
        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            result.Add($"{parts[i]} {parts[i + 1]}");
        }

        return string.Join(" ", result);
    }

    private static string GetAttribute(XmlNode node, string name, string defaultValue)
    {
        var attr = node.Attributes?[name];
        if (attr != null)
        {
            return attr.Value;
        }

        // Also check child elements
        var child = node.SelectSingleNode(name);
        return child?.InnerText ?? defaultValue;
    }

    private static int GetIntAttribute(XmlNode node, string name, int defaultValue)
    {
        var value = GetAttribute(node, name, null);
        return int.TryParse(value, out var result) ? result : defaultValue;
    }

    private static bool GetBoolAttribute(XmlNode node, string name, bool defaultValue)
    {
        var value = GetAttribute(node, name, null);
        return bool.TryParse(value, out var result) ? result : defaultValue;
    }
}
