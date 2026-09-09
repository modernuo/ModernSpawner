using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Server.Items;

namespace Server.Engines.ModernSpawner.Loot;

/// <summary>
/// Defines a loot template that can be applied to spawned creatures.
/// Allows customizing drops without modifying creature classes.
/// </summary>
public class LootTemplate
{
    /// <summary>
    /// Unique name for this template.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; }

    /// <summary>
    /// Items that are always added to the creature's loot.
    /// </summary>
    [JsonPropertyName("guaranteedItems")]
    public List<LootItem> GuaranteedItems { get; set; } = new();

    /// <summary>
    /// Random loot tables to roll from.
    /// </summary>
    [JsonPropertyName("randomTables")]
    public List<LootTable> RandomTables { get; set; } = new();

    /// <summary>
    /// Whether to clear the creature's default loot before applying this template.
    /// </summary>
    [JsonPropertyName("clearDefaultLoot")]
    public bool ClearDefaultLoot { get; set; }

    /// <summary>
    /// Gold range override. If both min and max are 0, uses creature's default.
    /// </summary>
    [JsonPropertyName("goldMin")]
    public int GoldMin { get; set; }

    [JsonPropertyName("goldMax")]
    public int GoldMax { get; set; }

    /// <summary>
    /// Applies this loot template to a creature.
    /// </summary>
    public void ApplyTo(Mobile creature)
    {
        var backpack = creature?.Backpack;
        if (backpack == null)
        {
            return;
        }

        // Clear default loot if requested
        if (ClearDefaultLoot)
        {
            var toDelete = new List<Item>();
            foreach (var item in backpack.Items)
            {
                toDelete.Add(item);
            }

            foreach (var item in toDelete)
            {
                item.Delete();
            }
        }

        // Add gold override
        if (GoldMin > 0 || GoldMax > 0)
        {
            var amount = Utility.RandomMinMax(Math.Max(0, GoldMin), Math.Max(GoldMin, GoldMax));
            if (amount > 0)
            {
                backpack.DropItem(new Gold(amount));
            }
        }

        // Add guaranteed items
        foreach (var lootItem in GuaranteedItems)
        {
            var item = lootItem.CreateItem();
            if (item != null)
            {
                backpack.DropItem(item);
            }
        }

        // Roll random tables
        foreach (var table in RandomTables)
        {
            table.RollAndAdd(backpack);
        }
    }
}

/// <summary>
/// Defines a single item in a loot template.
/// </summary>
public class LootItem
{
    /// <summary>
    /// The type name of the item to create.
    /// </summary>
    [JsonPropertyName("type")]
    public string TypeName { get; set; }

    /// <summary>
    /// Minimum stack amount.
    /// </summary>
    [JsonPropertyName("minAmount")]
    public int MinAmount { get; set; } = 1;

    /// <summary>
    /// Maximum stack amount.
    /// </summary>
    [JsonPropertyName("maxAmount")]
    public int MaxAmount { get; set; } = 1;

    /// <summary>
    /// Property overrides to apply after creation.
    /// Format: "PropertyName=Value"
    /// </summary>
    [JsonPropertyName("properties")]
    public List<string> Properties { get; set; } = new();

    /// <summary>
    /// Creates an instance of this item.
    /// </summary>
    public Item CreateItem()
    {
        if (string.IsNullOrEmpty(TypeName))
        {
            return null;
        }

        var type = AssemblyHandler.FindTypeByName(TypeName);
        if (type == null || !typeof(Item).IsAssignableFrom(type))
        {
            return null;
        }

        try
        {
            var item = type.CreateInstance<Item>();
            if (item == null)
            {
                return null;
            }

            // Set amount for stackables
            if (item.Stackable && MaxAmount > 1)
            {
                item.Amount = Utility.RandomMinMax(Math.Max(1, MinAmount), Math.Max(MinAmount, MaxAmount));
            }

            // Apply property overrides
            foreach (var prop in Properties)
            {
                ApplyProperty(item, prop);
            }

            return item;
        }
        catch
        {
            return null;
        }
    }

    private static void ApplyProperty(Item item, string propertySpec)
    {
        var equalsIndex = propertySpec.IndexOf('=');
        if (equalsIndex < 0)
        {
            return;
        }

        var propName = propertySpec[..equalsIndex].Trim();
        var valueStr = propertySpec[(equalsIndex + 1)..].Trim();

        try
        {
            Spawners.PropertyAccessorCache.SetValue(item, propName, valueStr, '/');
        }
        catch
        {
            // Ignore property set errors
        }
    }
}

/// <summary>
/// A random loot table with weighted entries.
/// </summary>
public class LootTable
{
    /// <summary>
    /// Entries in this table.
    /// </summary>
    [JsonPropertyName("entries")]
    public List<LootTableEntry> Entries { get; set; } = new();

    /// <summary>
    /// Number of times to roll this table.
    /// </summary>
    [JsonPropertyName("rolls")]
    public int Rolls { get; set; } = 1;

    /// <summary>
    /// Chance (0.0 to 1.0) that each roll produces an item.
    /// </summary>
    [JsonPropertyName("dropChance")]
    public double DropChance { get; set; } = 1.0;

    /// <summary>
    /// Rolls this table and adds results to a container.
    /// </summary>
    public void RollAndAdd(Container container)
    {
        if (container == null || Entries.Count == 0)
        {
            return;
        }

        var totalWeight = 0;
        foreach (var entry in Entries)
        {
            totalWeight += entry.Weight;
        }

        if (totalWeight == 0)
        {
            return;
        }

        for (var i = 0; i < Rolls; i++)
        {
            // Check drop chance
            if (DropChance < 1.0 && Utility.RandomDouble() > DropChance)
            {
                continue;
            }

            // Weighted random selection
            var roll = Utility.Random(totalWeight);
            var accumulated = 0;

            foreach (var entry in Entries)
            {
                accumulated += entry.Weight;
                if (roll < accumulated)
                {
                    var item = entry.Item.CreateItem();
                    if (item != null)
                    {
                        container.DropItem(item);
                    }

                    break;
                }
            }
        }
    }
}

/// <summary>
/// An entry in a loot table with a weight.
/// </summary>
public class LootTableEntry
{
    /// <summary>
    /// Weight of this entry (higher = more likely).
    /// </summary>
    [JsonPropertyName("weight")]
    public int Weight { get; set; } = 1;

    /// <summary>
    /// The item definition.
    /// </summary>
    [JsonPropertyName("item")]
    public LootItem Item { get; set; }
}
