using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Server.Engines.ModernSpawner.Loot;

/// <summary>
/// Registry for managing named loot templates.
/// Templates can be loaded from JSON files and assigned to spawner entries by name.
/// </summary>
public static class LootTemplateRegistry
{
    private static readonly Dictionary<string, LootTemplate> _templates =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Registers a loot template.
    /// </summary>
    public static void Register(LootTemplate template)
    {
        if (template == null || string.IsNullOrEmpty(template.Name))
        {
            return;
        }

        _templates[template.Name] = template;
    }

    /// <summary>
    /// Gets a loot template by name.
    /// </summary>
    public static LootTemplate GetTemplate(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        return _templates.GetValueOrDefault(name);
    }

    /// <summary>
    /// Gets all registered template names.
    /// </summary>
    public static IEnumerable<string> GetTemplateNames() => _templates.Keys;

    /// <summary>
    /// Removes a template by name.
    /// </summary>
    public static bool Unregister(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        return _templates.Remove(name);
    }

    /// <summary>
    /// Clears all registered templates.
    /// </summary>
    public static void Clear()
    {
        _templates.Clear();
    }

    /// <summary>
    /// Loads templates from a JSON file.
    /// </summary>
    public static int LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            return 0;
        }

        try
        {
            var json = File.ReadAllText(path);
            var templates = JsonSerializer.Deserialize<List<LootTemplate>>(json, _jsonOptions);

            if (templates == null)
            {
                return 0;
            }

            var count = 0;
            foreach (var template in templates)
            {
                if (!string.IsNullOrEmpty(template.Name))
                {
                    Register(template);
                    count++;
                }
            }

            return count;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading loot templates from {path}: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Loads all JSON template files from a directory.
    /// </summary>
    public static int LoadFromDirectory(string path, bool recursive = true)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var count = 0;

        foreach (var file in Directory.GetFiles(path, "*.json", searchOption))
        {
            count += LoadFromFile(file);
        }

        return count;
    }

    /// <summary>
    /// Saves all registered templates to a JSON file.
    /// </summary>
    public static void SaveToFile(string path)
    {
        try
        {
            var templates = new List<LootTemplate>(_templates.Values);
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(templates, options);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving loot templates to {path}: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies a named template to a creature.
    /// </summary>
    public static bool ApplyTemplate(string templateName, Mobile creature)
    {
        var template = GetTemplate(templateName);
        if (template == null)
        {
            return false;
        }

        template.ApplyTo(creature);
        return true;
    }

    /// <summary>
    /// Creates a simple template programmatically.
    /// </summary>
    public static LootTemplate CreateSimpleTemplate(
        string name,
        bool clearDefault = false,
        int goldMin = 0,
        int goldMax = 0,
        params (string typeName, int minAmount, int maxAmount)[] items)
    {
        var template = new LootTemplate
        {
            Name = name,
            ClearDefaultLoot = clearDefault,
            GoldMin = goldMin,
            GoldMax = goldMax
        };

        foreach (var (typeName, minAmount, maxAmount) in items)
        {
            template.GuaranteedItems.Add(new LootItem
            {
                TypeName = typeName,
                MinAmount = minAmount,
                MaxAmount = maxAmount
            });
        }

        return template;
    }

    /// <summary>
    /// Creates a template with random drops.
    /// </summary>
    public static LootTemplate CreateRandomTemplate(
        string name,
        double dropChance,
        int rolls,
        params (string typeName, int weight)[] items)
    {
        var template = new LootTemplate { Name = name };

        var table = new LootTable
        {
            DropChance = dropChance,
            Rolls = rolls
        };

        foreach (var (typeName, weight) in items)
        {
            table.Entries.Add(new LootTableEntry
            {
                Weight = weight,
                Item = new LootItem { TypeName = typeName }
            });
        }

        template.RandomTables.Add(table);
        return template;
    }
}
