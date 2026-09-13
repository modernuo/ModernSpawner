using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Server.Engines.Events;
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
    /// Range used for a speech or property trigger when the node carries no <c>ProximityRange</c> of its
    /// own - the same fallback <see cref="MapSkillTrigger" /> already used for <c>SkillTrigger</c>.
    /// </summary>
    private const int DefaultTriggerRange = 10;

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

        var report = ImportFromFile(path);

        e.Mobile.SendMessage($"Import complete. Created: {report.Success}, Failed: {report.Failed}");

        foreach (var (spawnerName, note) in report.Notes)
        {
            var line = $"{spawnerName}: {note}";
            if (e.Mobile != null)
            {
                e.Mobile.SendMessage(line);
            }
            else
            {
                Logger.Information("{Note}", line);
            }
        }

        Logger.Information(
            "Imported {Success} XmlSpawners, {Failed} failures from {Path}",
            report.Success,
            report.Failed,
            path
        );
    }

    /// <summary>
    /// Imports XmlSpawner data from an XML file.
    /// </summary>
    public static MigrationReport ImportFromFile(string path)
    {
        var report = new MigrationReport();

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
                        var notes = new List<string>();
                        var spawner = ParseXmlSpawnerNode(node, notes);
                        if (spawner != null)
                        {
                            report.RecordSuccess();
                            foreach (var note in notes)
                            {
                                report.AddNote(spawner.Name, note);
                            }
                        }
                        else
                        {
                            report.RecordFailure();
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning(ex, "Failed to parse XmlSpawner node");
                        report.RecordFailure();
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
                            report.RecordSuccess();
                        }
                        else
                        {
                            report.RecordFailure();
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning(ex, "Failed to parse SpawnPoint node");
                        report.RecordFailure();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to load XML file: {Path}", path);
        }

        return report;
    }

    /// <summary>
    /// Parses an XmlSpawner node from the save format.
    /// </summary>
    internal static ModernSpawner ParseXmlSpawnerNode(XmlNode node) => ParseXmlSpawnerNode(node, null);

    /// <summary>
    /// Parses an XmlSpawner node from the save format, collecting one advisory line per approximated or
    /// dropped attribute into <paramref name="notes" /> (e.g. a TOD window that D2 no longer despawns on
    /// close, or a <c>PlayerPropertyName</c> that could not be translated into a <c>when:</c> expression).
    /// </summary>
    /// <param name="node">The XmlSpawner node.</param>
    /// <param name="notes">Receives report lines for this spawner; pass null to discard them.</param>
    internal static ModernSpawner ParseXmlSpawnerNode(XmlNode node, List<string> notes)
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

        // XmlSpawner "group" is respawn-all-when-all-dead; that is base Group, not the AllEntries cycle
        // mode (design §7).
        spawner.Group = GetBoolAttribute(node, "IsGroup", false);

        // Refractory lockout: MinRefractory/MaxRefractory are minutes (dev-docs §2/§3).
        var minRefractory = GetDoubleAttribute(node, "MinRefractory", 0);
        var maxRefractory = GetDoubleAttribute(node, "MaxRefractory", 0);
        if (minRefractory > 0 || maxRefractory > 0)
        {
            spawner.RefractoryMin = TimeSpan.FromMinutes(minRefractory);
            spawner.RefractoryMax = TimeSpan.FromMinutes(Math.Max(maxRefractory, minRefractory));
        }

        // SpawnOnTrigger=False defers the accepted event to the next tick (mode:tick) behind a one-slot
        // queue; SpawnOnTrigger=True or absent reproduces XmlSpawner's own run-now-or-drop semantics
        // (ruling §13.2).
        var spawnOnTrigger = GetBoolAttribute(node, "SpawnOnTrigger", true);
        spawner.MaxPendingCycles = spawnOnTrigger ? 0 : 1;

        // Parse trigger settings. XmlSpawner tests proximity, speech and the player property
        // conjunctively (dev-docs §8), so a SpeechTrigger folds the proximity range and the property
        // test into one speech trigger rather than three independent (effectively OR'd) triggers.
        var proximityRange = GetIntAttribute(node, "ProximityRange", -1);
        var speechTrigger = GetAttribute(node, "SpeechTrigger", null);
        var playerPropertyName = GetAttribute(node, "PlayerPropertyName", null);

        if (!string.IsNullOrEmpty(speechTrigger))
        {
            var range = proximityRange >= 0 ? proximityRange : DefaultTriggerRange;
            var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(speechTrigger));
            var positional = $"speech:{encoded}:true:false:{range}:true:5";

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
            // PlayerPropertyName alone still needs a trigger to hang the when: expression on; XmlSpawner
            // had no minimum proximity for this case either, so it gets the same default range.
            var positional = $"proximity:{DefaultTriggerRange}:true:false:5:0";

            spawner.AddTriggerDefinition(BuildEventDefinition(positional, spawnOnTrigger, playerPropertyName, notes));
            spawner.TriggerActivated = true;
        }

        var skillTrigger = GetAttribute(node, "SkillTrigger", null);
        if (!string.IsNullOrWhiteSpace(skillTrigger))
        {
            var positional = MapSkillTrigger(skillTrigger, proximityRange < 0 ? DefaultTriggerRange : proximityRange);
            if (positional != null)
            {
                spawner.AddTriggerDefinition(BuildEventDefinition(positional, spawnOnTrigger, null, notes));
                spawner.TriggerActivated = true;
            }
        }

        // Time-of-day gate. TODStart/TODEnd are TotalMinutes; TODMode 0 = Realtime (wall clock), 1 =
        // Gametime (dev-docs §2/§3). XmlSpawner despawned live spawns when the window closed; D2 keeps
        // them running, since per-spawn lifetimes are tracked separately under D10.
        var todStart = GetDoubleAttribute(node, "TODStart", -1);
        var todEnd = GetDoubleAttribute(node, "TODEnd", -1);
        var todMode = GetIntAttribute(node, "TODMode", 0);
        if (todStart >= 0 || todEnd >= 0)
        {
            AddTimeOfDayGate(spawner, todMode, Math.Max(todStart, 0), Math.Max(todEnd, 0));
            notes?.Add("XmlSpawner despawned live spawns when the window closed; D2 keeps them (lifetimes are D10).");
        }

        // Duration is a per-spawn lifetime XmlSpawner enforced; ModernSpawner has no entry despawn timer
        // yet (D10), so the value is reported and dropped rather than approximated.
        var duration = GetDoubleAttribute(node, "Duration", -1);
        if (duration > 0)
        {
            notes?.Add($"Duration ({duration} min) is a per-spawn lifetime; ModernSpawner has no entry despawn timer yet (D10) and the value was dropped.");
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

    private static double GetDoubleAttribute(XmlNode node, string name, double defaultValue)
    {
        var value = GetAttribute(node, name, null);
        return double.TryParse(value, out var result) ? result : defaultValue;
    }

    /// <summary>
    /// Builds the final trigger definition text from <paramref name="positional" />: the shared
    /// <c>mode:tick</c> token first, then <c>when:</c> last - <see cref="TriggerTokens" /> documents
    /// <c>when:</c> as consuming everything after it, so a token appended past it would be swallowed into
    /// the expression source and never parsed, leaving the trigger permanently inert. The single writer
    /// for every event trigger definition this migrator emits, so the ordering cannot drift between call
    /// sites.
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
