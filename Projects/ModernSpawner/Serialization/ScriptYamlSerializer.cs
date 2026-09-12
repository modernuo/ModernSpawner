using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Server.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Server.Engines.ModernSpawner.Serialization;

/// <summary>
/// Serializes and deserializes complex spawner scripts to/from YAML format.
/// </summary>
public static class ScriptYamlSerializer
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitDefaults)
        .Build();

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Serializes script data to a YAML string.
    /// </summary>
    public static string ToYaml(ScriptExportData script)
    {
        ArgumentNullException.ThrowIfNull(script);

        return Serializer.Serialize(script);
    }

    /// <summary>
    /// Serializes script data to a YAML file.
    /// </summary>
    public static void ToFile(ScriptExportData script, string filePath)
    {
        var yaml = ToYaml(script);
        File.WriteAllText(filePath, yaml, Encoding.UTF8);
    }

    /// <summary>
    /// Deserializes a YAML string to script data.
    /// </summary>
    public static ScriptExportData FromYaml(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);

        return Deserializer.Deserialize<ScriptExportData>(yaml);
    }

    /// <summary>
    /// Deserializes a YAML file to script data.
    /// </summary>
    public static ScriptExportData FromFile(string filePath)
    {
        var yaml = File.ReadAllText(filePath, Encoding.UTF8);
        return FromYaml(yaml);
    }

    /// <summary>
    /// Creates a script export from a ModernSpawner.
    /// Extracts complex script configurations for YAML export.
    /// </summary>
    public static ScriptExportData FromSpawner(ModernSpawner spawner)
    {
        ArgumentNullException.ThrowIfNull(spawner);

        var script = new ScriptExportData
        {
            Name = spawner.Name ?? "Unnamed Script",
            Description = spawner.Notes,
            Entries = []
        };

        // Export entries
        foreach (var entry in spawner.ModernEntries)
        {
            script.Entries.Add(ExportEntry(entry));
        }

        // Export scripts as actions
        var onActivate = spawner.OnActivateScript?.Source;
        if (!string.IsNullOrEmpty(onActivate))
        {
            script.OnActivate = ParseScriptToActions(onActivate);
        }

        var onDeactivate = spawner.OnDeactivateScript?.Source;
        if (!string.IsNullOrEmpty(onDeactivate))
        {
            script.OnDeactivate = ParseScriptToActions(onDeactivate);
        }

        var onBeforeSpawn = spawner.OnBeforeSpawnScript?.Source;
        if (!string.IsNullOrEmpty(onBeforeSpawn))
        {
            script.OnBeforeSpawn = ParseScriptToActions(onBeforeSpawn);
        }

        var onAfterSpawn = spawner.OnAfterSpawnScript?.Source;
        if (!string.IsNullOrEmpty(onAfterSpawn))
        {
            script.OnAfterSpawn = ParseScriptToActions(onAfterSpawn);
        }

        return script;
    }

    /// <summary>
    /// Applies script configuration to a ModernSpawner.
    /// </summary>
    public static void ApplyToSpawner(ScriptExportData script, ModernSpawner spawner)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(spawner);

        // Set name and description
        if (!string.IsNullOrEmpty(script.Name))
        {
            spawner.Name = script.Name;
        }

        if (!string.IsNullOrEmpty(script.Description))
        {
            spawner.Notes = script.Description;
        }

        // Clear and re-add entries
        spawner.RemoveAllEntries();
        if (script.Entries != null)
        {
            foreach (var entryData in script.Entries)
            {
                ImportEntry(spawner, entryData);
            }
        }

        // Convert actions back to script expressions
        if (script.OnActivate != null)
        {
            spawner.SetOnActivateScript(ActionsToScript(script.OnActivate));
        }

        if (script.OnDeactivate != null)
        {
            spawner.SetOnDeactivateScript(ActionsToScript(script.OnDeactivate));
        }

        if (script.OnBeforeSpawn != null)
        {
            spawner.SetOnBeforeSpawnScript(ActionsToScript(script.OnBeforeSpawn));
        }

        if (script.OnAfterSpawn != null)
        {
            spawner.SetOnAfterSpawnScript(ActionsToScript(script.OnAfterSpawn));
        }
    }

    private static ScriptEntryData ExportEntry(ModernSpawnerEntry entry)
    {
        var entryData = new ScriptEntryData
        {
            Type = entry.SpawnedName,
            MaxCount = entry.SpawnedMaxCount,
            Probability = entry.SpawnedProbability
        };

        // Export properties
        if (!string.IsNullOrEmpty(entry.Properties))
        {
            entryData.Properties = ParseProperties(entry.Properties);
        }

        // Export entry scripts as actions
        if (!string.IsNullOrEmpty(entry.OnSpawnScript))
        {
            entryData.OnSpawn = ParseScriptToActions(entry.OnSpawnScript);
        }

        if (!string.IsNullOrEmpty(entry.OnDespawnScript))
        {
            entryData.OnDespawn = ParseScriptToActions(entry.OnDespawnScript);
        }

        return entryData;
    }

    private static void ImportEntry(ModernSpawner spawner, ScriptEntryData entryData)
    {
        if (string.IsNullOrEmpty(entryData.Type))
        {
            return;
        }

        var properties = ConvertProperties(entryData.Properties);
        var onSpawn = entryData.OnSpawn != null ? ActionsToScript(entryData.OnSpawn) : null;
        var onDespawn = entryData.OnDespawn != null ? ActionsToScript(entryData.OnDespawn) : null;

        spawner.AddModernEntry(
            creatureName: entryData.Type,
            probability: entryData.Probability > 0 ? entryData.Probability : 100,
            maxCount: entryData.MaxCount > 0 ? entryData.MaxCount : 1,
            properties: properties,
            onSpawnScript: onSpawn,
            onDespawnScript: onDespawn,
            dotimer: false
        );
    }

    private static Dictionary<string, ScriptPropertyData> ParseProperties(string properties)
    {
        if (string.IsNullOrWhiteSpace(properties))
        {
            return null;
        }

        var result = new Dictionary<string, ScriptPropertyData>();
        var span = properties.AsSpan();

        foreach (var pairRange in span.Split('/'))
        {
            var pair = span[pairRange].Trim();
            if (pair.IsEmpty)
            {
                continue;
            }

            var equalsIndex = pair.IndexOf('=');
            if (equalsIndex <= 0)
            {
                continue;
            }

            var propName = pair[..equalsIndex].Trim().ToString();
            var propValue = pair[(equalsIndex + 1)..].Trim();

            if (propValue.Length > 2 && propValue[0] == '{' && propValue.Contains("-".AsSpan(), StringComparison.Ordinal))
            {
                // Random range: {min-max}
                var rangeValue = propValue[1..^1]; // Remove { and }
                var dashIndex = rangeValue.IndexOf('-');
                if (dashIndex > 0 &&
                    int.TryParse(rangeValue[..dashIndex], out var min) &&
                    int.TryParse(rangeValue[(dashIndex + 1)..], out var max))
                {
                    result[propName] = new ScriptPropertyData
                    {
                        Type = "random",
                        Min = min,
                        Max = max
                    };
                    continue;
                }
            }

            result[propName] = new ScriptPropertyData
            {
                Type = "fixed",
                Value = ParseValue(propValue)
            };
        }

        return result.Count > 0 ? result : null;
    }

    private static string ConvertProperties(Dictionary<string, ScriptPropertyData> properties)
    {
        if (properties == null || properties.Count == 0)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var (key, prop) in properties)
        {
            string valueStr;
            var typeSpan = prop.Type.AsSpan();

            if (typeSpan.InsensitiveEquals("random"))
            {
                valueStr = $"{key}={{{prop.Min}-{prop.Max}}}";
            }
            else if (typeSpan.InsensitiveEquals("expression"))
            {
                valueStr = $"{key}={prop.Expression}";
            }
            else
            {
                valueStr = $"{key}={prop.Value}";
            }

            parts.Add(valueStr);
        }

        return string.Join("/", parts);
    }

    private static object ParseValue(ReadOnlySpan<char> value)
    {
        if (int.TryParse(value, out var intVal))
        {
            return intVal;
        }

        if (double.TryParse(value, out var doubleVal))
        {
            return doubleVal;
        }

        if (value.InsensitiveEquals("true"))
        {
            return true;
        }

        if (value.InsensitiveEquals("false"))
        {
            return false;
        }

        return value.ToString();
    }

    private static List<ScriptActionData> ParseScriptToActions(string script)
    {
        // Simple script-to-action parsing
        // Full implementation would analyze expression structure
        if (string.IsNullOrWhiteSpace(script))
        {
            return null;
        }

        return
        [
            new ScriptActionData
            {
                Action = "expression",
                Description = script
            }
        ];
    }

    private static string ActionsToScript(List<ScriptActionData> actions)
    {
        if (actions == null || actions.Count == 0)
        {
            return null;
        }

        var sb = ValueStringBuilder.Create();
        try
        {
            for (var i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                if (i > 0 && sb.Length > 0)
                {
                    sb.AppendLine("; ");
                }

                ActionToScript(action, ref sb);
            }

            return sb.ToString();
        }
        finally
        {
            sb.Dispose();
        }
    }

    private static void ActionToScript(ScriptActionData action, scoped ref ValueStringBuilder script)
    {
        if (action == null)
        {
            return;
        }

        // Add condition if present
        if (!string.IsNullOrEmpty(action.Condition))
        {
            script.Append("if ");
            script.Append(action.Condition);
            script.Append(" then ");
        }

        // Build action expression using span-based comparison
        var actionType = action.Action != null ? action.Action.AsSpan() : ReadOnlySpan<char>.Empty;

        if (actionType.InsensitiveEquals("cancel"))
        {
            script.Append("cancel()");
        }
        else if (actionType.InsensitiveEquals("set"))
        {
            if (action.Target != null && action.Properties != null)
            {
                foreach (var (prop, value) in action.Properties)
                {
                    if (!string.IsNullOrEmpty(action.Target.Name))
                    {
                        script.Append("set(");
                        script.Append(action.Target.Name);
                        script.Append(", \"");
                        script.Append(prop);
                        script.Append("\", ");
                        script.Append(value);
                        script.Append(')');
                    }
                }
            }
        }
        else if (actionType.InsensitiveEquals("spawn"))
        {
            if (!string.IsNullOrEmpty(action.Spawner))
            {
                script.Append("spawn(\"");
                script.Append(action.Spawner);
                script.Append('"');
                if (action.Subgroup.HasValue)
                {
                    script.Append(", ");
                    script.Append(action.Subgroup.Value);
                }
                script.Append(')');
            }
        }
        else if (actionType.InsensitiveEquals("broadcast"))
        {
            if (!string.IsNullOrEmpty(action.Message))
            {
                script.Append("broadcast(\"");
                script.Append(action.Message);
                script.Append('"');
                if (action.Range.HasValue)
                {
                    script.Append(", ");
                    script.Append(action.Range.Value);
                }
                script.Append(')');
            }
        }
        else if (actionType.InsensitiveEquals("sound"))
        {
            if (action.SoundId.HasValue)
            {
                script.Append("sound(");
                script.Append(action.SoundId.Value);
                script.Append(')');
            }
        }
        else if (actionType.InsensitiveEquals("effect"))
        {
            if (action.EffectId.HasValue)
            {
                script.Append("effect(");
                script.Append(action.EffectId.Value);
                script.Append(')');
            }
        }
        else if (actionType.InsensitiveEquals("expression"))
        {
            // Raw expression stored in description
            script.Append(action.Description ?? "");
        }
        else if (!actionType.IsEmpty)
        {
            script.Append(action.Description);
        }
    }
}
