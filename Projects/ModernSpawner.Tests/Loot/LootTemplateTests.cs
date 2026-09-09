using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Server.Engines.ModernSpawner.Loot;
using Xunit;

namespace Server.Engines.ModernSpawner.Tests.Loot;

public class LootTemplateTests : IDisposable
{
    public LootTemplateTests()
    {
        // Clear registry before each test
        LootTemplateRegistry.Clear();
    }

    public void Dispose()
    {
        // Clean up registry after each test
        LootTemplateRegistry.Clear();
    }

    #region LootTemplate Construction Tests

    [Fact]
    public void LootTemplate_DefaultValues()
    {
        var template = new LootTemplate();

        Assert.Null(template.Name);
        Assert.NotNull(template.GuaranteedItems);
        Assert.Empty(template.GuaranteedItems);
        Assert.NotNull(template.RandomTables);
        Assert.Empty(template.RandomTables);
        Assert.False(template.ClearDefaultLoot);
        Assert.Equal(0, template.GoldMin);
        Assert.Equal(0, template.GoldMax);
    }

    [Fact]
    public void LootItem_DefaultValues()
    {
        var item = new LootItem();

        Assert.Null(item.TypeName);
        Assert.Equal(1, item.MinAmount);
        Assert.Equal(1, item.MaxAmount);
        Assert.NotNull(item.Properties);
        Assert.Empty(item.Properties);
    }

    [Fact]
    public void LootTable_DefaultValues()
    {
        var table = new LootTable();

        Assert.NotNull(table.Entries);
        Assert.Empty(table.Entries);
        Assert.Equal(1, table.Rolls);
        Assert.Equal(1.0, table.DropChance);
    }

    [Fact]
    public void LootTableEntry_DefaultValues()
    {
        var entry = new LootTableEntry();

        Assert.Equal(1, entry.Weight);
        Assert.Null(entry.Item);
    }

    #endregion

    #region LootTemplateRegistry Tests

    [Fact]
    public void Register_ValidTemplate_CanBeRetrieved()
    {
        var template = new LootTemplate { Name = "test_template" };

        LootTemplateRegistry.Register(template);
        var retrieved = LootTemplateRegistry.GetTemplate("test_template");

        Assert.Same(template, retrieved);
    }

    [Fact]
    public void Register_NullTemplate_DoesNotThrow()
    {
        LootTemplateRegistry.Register(null);

        Assert.Empty(LootTemplateRegistry.GetTemplateNames());
    }

    [Fact]
    public void Register_TemplateWithNullName_DoesNotRegister()
    {
        var template = new LootTemplate { Name = null };

        LootTemplateRegistry.Register(template);

        Assert.Empty(LootTemplateRegistry.GetTemplateNames());
    }

    [Fact]
    public void Register_TemplateWithEmptyName_DoesNotRegister()
    {
        var template = new LootTemplate { Name = "" };

        LootTemplateRegistry.Register(template);

        Assert.Empty(LootTemplateRegistry.GetTemplateNames());
    }

    [Fact]
    public void GetTemplate_CaseInsensitive()
    {
        var template = new LootTemplate { Name = "TestTemplate" };
        LootTemplateRegistry.Register(template);

        Assert.Same(template, LootTemplateRegistry.GetTemplate("TestTemplate"));
        Assert.Same(template, LootTemplateRegistry.GetTemplate("testtemplate"));
        Assert.Same(template, LootTemplateRegistry.GetTemplate("TESTTEMPLATE"));
        Assert.Same(template, LootTemplateRegistry.GetTemplate("testTEMPLATE"));
    }

    [Fact]
    public void GetTemplate_NonExistent_ReturnsNull()
    {
        Assert.Null(LootTemplateRegistry.GetTemplate("nonexistent"));
    }

    [Fact]
    public void GetTemplate_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(LootTemplateRegistry.GetTemplate(null));
        Assert.Null(LootTemplateRegistry.GetTemplate(""));
    }

    [Fact]
    public void Unregister_ExistingTemplate_ReturnsTrue()
    {
        var template = new LootTemplate { Name = "test" };
        LootTemplateRegistry.Register(template);

        var result = LootTemplateRegistry.Unregister("test");

        Assert.True(result);
        Assert.Null(LootTemplateRegistry.GetTemplate("test"));
    }

    [Fact]
    public void Unregister_NonExistent_ReturnsFalse()
    {
        var result = LootTemplateRegistry.Unregister("nonexistent");

        Assert.False(result);
    }

    [Fact]
    public void Unregister_NullOrEmpty_ReturnsFalse()
    {
        Assert.False(LootTemplateRegistry.Unregister(null));
        Assert.False(LootTemplateRegistry.Unregister(""));
    }

    [Fact]
    public void Clear_RemovesAllTemplates()
    {
        LootTemplateRegistry.Register(new LootTemplate { Name = "template1" });
        LootTemplateRegistry.Register(new LootTemplate { Name = "template2" });
        LootTemplateRegistry.Register(new LootTemplate { Name = "template3" });

        LootTemplateRegistry.Clear();

        Assert.Empty(LootTemplateRegistry.GetTemplateNames());
    }

    [Fact]
    public void GetTemplateNames_ReturnsAllRegistered()
    {
        LootTemplateRegistry.Register(new LootTemplate { Name = "alpha" });
        LootTemplateRegistry.Register(new LootTemplate { Name = "beta" });
        LootTemplateRegistry.Register(new LootTemplate { Name = "gamma" });

        var names = LootTemplateRegistry.GetTemplateNames();

        Assert.Contains("alpha", names);
        Assert.Contains("beta", names);
        Assert.Contains("gamma", names);
    }

    [Fact]
    public void Register_DuplicateName_OverwritesPrevious()
    {
        var template1 = new LootTemplate { Name = "test", GoldMin = 100 };
        var template2 = new LootTemplate { Name = "test", GoldMin = 200 };

        LootTemplateRegistry.Register(template1);
        LootTemplateRegistry.Register(template2);

        var retrieved = LootTemplateRegistry.GetTemplate("test");
        Assert.Equal(200, retrieved.GoldMin);
    }

    #endregion

    #region Template Factory Methods Tests

    [Fact]
    public void CreateSimpleTemplate_SetsBasicProperties()
    {
        var template = LootTemplateRegistry.CreateSimpleTemplate(
            "simple_test",
            clearDefault: true,
            goldMin: 100,
            goldMax: 500
        );

        Assert.Equal("simple_test", template.Name);
        Assert.True(template.ClearDefaultLoot);
        Assert.Equal(100, template.GoldMin);
        Assert.Equal(500, template.GoldMax);
        Assert.Empty(template.GuaranteedItems);
    }

    [Fact]
    public void CreateSimpleTemplate_WithItems()
    {
        var template = LootTemplateRegistry.CreateSimpleTemplate(
            "with_items",
            items: new[]
            {
                ("Gold", 10, 50),
                ("Arrow", 5, 20)
            }
        );

        Assert.Equal(2, template.GuaranteedItems.Count);
        Assert.Equal("Gold", template.GuaranteedItems[0].TypeName);
        Assert.Equal(10, template.GuaranteedItems[0].MinAmount);
        Assert.Equal(50, template.GuaranteedItems[0].MaxAmount);
        Assert.Equal("Arrow", template.GuaranteedItems[1].TypeName);
    }

    [Fact]
    public void CreateRandomTemplate_SetsProperties()
    {
        var template = LootTemplateRegistry.CreateRandomTemplate(
            "random_test",
            dropChance: 0.5,
            rolls: 3,
            ("Sword", 10),
            ("Shield", 5)
        );

        Assert.Equal("random_test", template.Name);
        Assert.Single(template.RandomTables);

        var table = template.RandomTables[0];
        Assert.Equal(0.5, table.DropChance);
        Assert.Equal(3, table.Rolls);
        Assert.Equal(2, table.Entries.Count);

        Assert.Equal("Sword", table.Entries[0].Item.TypeName);
        Assert.Equal(10, table.Entries[0].Weight);
        Assert.Equal("Shield", table.Entries[1].Item.TypeName);
        Assert.Equal(5, table.Entries[1].Weight);
    }

    #endregion

    #region JSON Serialization Tests

    [Fact]
    public void LootTemplate_JsonRoundTrip()
    {
        var original = new LootTemplate
        {
            Name = "json_test",
            ClearDefaultLoot = true,
            GoldMin = 100,
            GoldMax = 500,
            GuaranteedItems = new List<LootItem>
            {
                new() { TypeName = "Sword", MinAmount = 1, MaxAmount = 1 }
            },
            RandomTables = new List<LootTable>
            {
                new()
                {
                    Rolls = 2,
                    DropChance = 0.75,
                    Entries = new List<LootTableEntry>
                    {
                        new() { Weight = 10, Item = new LootItem { TypeName = "Arrow", MinAmount = 5, MaxAmount = 20 } }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<LootTemplate>(json);

        Assert.NotNull(restored);
        Assert.Equal(original.Name, restored.Name);
        Assert.Equal(original.ClearDefaultLoot, restored.ClearDefaultLoot);
        Assert.Equal(original.GoldMin, restored.GoldMin);
        Assert.Equal(original.GoldMax, restored.GoldMax);
        Assert.Single(restored.GuaranteedItems);
        Assert.Equal("Sword", restored.GuaranteedItems[0].TypeName);
        Assert.Single(restored.RandomTables);
        Assert.Equal(2, restored.RandomTables[0].Rolls);
    }

    [Fact]
    public void LootItem_WithProperties_JsonRoundTrip()
    {
        var original = new LootItem
        {
            TypeName = "Sword",
            MinAmount = 1,
            MaxAmount = 1,
            Properties = new List<string> { "Name=Excalibur", "Hue=1150" }
        };

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<LootItem>(json);

        Assert.NotNull(restored);
        Assert.Equal(original.TypeName, restored.TypeName);
        Assert.Equal(2, restored.Properties.Count);
        Assert.Contains("Name=Excalibur", restored.Properties);
        Assert.Contains("Hue=1150", restored.Properties);
    }

    #endregion

    #region File Loading Tests

    [Fact]
    public void LoadFromFile_NonExistentFile_ReturnsZero()
    {
        var count = LootTemplateRegistry.LoadFromFile("nonexistent_file.json");

        Assert.Equal(0, count);
    }

    [Fact]
    public void LoadFromDirectory_NonExistentDirectory_ReturnsZero()
    {
        var count = LootTemplateRegistry.LoadFromDirectory("nonexistent_directory");

        Assert.Equal(0, count);
    }

    [Fact]
    public void LoadFromFile_ValidJson_LoadsTemplates()
    {
        var templates = new List<LootTemplate>
        {
            new() { Name = "template1", GoldMin = 100 },
            new() { Name = "template2", GoldMax = 500 }
        };

        var tempFile = Path.GetTempFileName();
        try
        {
            var json = JsonSerializer.Serialize(templates);
            File.WriteAllText(tempFile, json);

            var count = LootTemplateRegistry.LoadFromFile(tempFile);

            Assert.Equal(2, count);
            Assert.NotNull(LootTemplateRegistry.GetTemplate("template1"));
            Assert.NotNull(LootTemplateRegistry.GetTemplate("template2"));
            Assert.Equal(100, LootTemplateRegistry.GetTemplate("template1").GoldMin);
            Assert.Equal(500, LootTemplateRegistry.GetTemplate("template2").GoldMax);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadFromFile_InvalidJson_ReturnsZero()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "not valid json {{{");

            var count = LootTemplateRegistry.LoadFromFile(tempFile);

            Assert.Equal(0, count);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    #endregion
}
