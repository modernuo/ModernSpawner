using System;
using System.IO;
using Server.Engines.ModernSpawner.Serialization;
using Xunit;

namespace Server.Engines.ModernSpawner.Tests;

/// <summary>
/// Covers the sentinel-based min/max delay handling in
/// <see cref="XmlSpawnerImporter"/>'s entry parsing (Task 3: no nullable value types). This needs a
/// resolvable <see cref="Map"/>, so it runs against the real world in the sequential collection
/// rather than as a pure unit test.
/// </summary>
[Collection("Sequential ModernSpawner Tests")]
public class XmlSpawnerImporterEntryDelayTests
{
    private static string BuildXml(string name, string objects2) => $"""
        <Spawns>
          <Points>
            <Name>{name}</Name>
            <Map>Felucca</Map>
            <CentreX>1500</CentreX>
            <CentreY>1500</CentreY>
            <CentreZ>0</CentreZ>
            <X>1500</X>
            <Y>1500</Y>
            <Width>0</Width>
            <Height>0</Height>
            <Range>4</Range>
            <MaxCount>5</MaxCount>
            <MinDelay>5</MinDelay>
            <MaxDelay>10</MaxDelay>
            <DelayInSec>False</DelayInSec>
            <ProximityRange>-1</ProximityRange>
            <Team>0</Team>
            <IsGroup>False</IsGroup>
            <IsRunning>False</IsRunning>
            <SmartSpawning>False</SmartSpawning>
            <Objects2>{objects2}</Objects2>
          </Points>
        </Spawns>
        """;

    private static ModernSpawner FindByName(string name)
    {
        foreach (var item in World.Items.Values)
        {
            if (item is ModernSpawner spawner && spawner.Name == name)
            {
                return spawner;
            }
        }

        Assert.Fail($"No imported ModernSpawner named '{name}' was found in the world.");
        return null;
    }

    [Fact]
    public void ParseEntry_NoDelayTokens_LeavesEntryAtSpawnerDefault()
    {
        var name = "ImporterDelayTest-" + Guid.NewGuid();
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, BuildXml(name, "Rabbit:MX=3"));

            var result = XmlSpawnerImporter.ImportFromFile(tempFile, respawn: false);
            Assert.Equal(1, result.Imported);

            var spawner = FindByName(name);
            var entry = Assert.Single(spawner.ModernEntries);

            Assert.Equal(TimeSpan.Zero, entry.MinDelay);
            Assert.Equal(spawner.MinDelay, entry.EffectiveMinDelay);

            spawner.Delete();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ParseEntry_DnToken_SetsMinDelayInMinutes()
    {
        var name = "ImporterDelayTest-" + Guid.NewGuid();
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, BuildXml(name, "Rabbit:MX=3:DN=2"));

            var result = XmlSpawnerImporter.ImportFromFile(tempFile, respawn: false);
            Assert.Equal(1, result.Imported);

            var spawner = FindByName(name);
            var entry = Assert.Single(spawner.ModernEntries);

            Assert.Equal(TimeSpan.FromMinutes(2), entry.MinDelay);

            spawner.Delete();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
