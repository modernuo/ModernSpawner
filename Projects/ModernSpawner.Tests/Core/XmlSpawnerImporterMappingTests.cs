using System;
using System.IO;
using Server.Engines.ModernSpawner.Serialization;
using Server.Engines.ModernSpawner.Triggers;
using Xunit;

namespace Server.Engines.ModernSpawner.Tests;

/// <summary>
/// Task 4: <see cref="XmlSpawnerImporter" />'s ServUO Points ingestion gains the same D2 mappings as
/// <see cref="Server.Engines.ModernSpawner.Migration.XmlSpawnerMigrator" /> - refractory, SpawnOnTrigger,
/// the conjunctive proximity+speech+property trigger, IsGroup and time-of-day. This needs a resolvable
/// <see cref="Map" />, so it runs against the real world in the sequential collection (see
/// <see cref="XmlSpawnerImporterEntryDelayTests" />).
/// </summary>
[Collection("Sequential ModernSpawner Tests")]
public class XmlSpawnerImporterMappingTests
{
    private static string BuildXml(string name, string extraElements) => $"""
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
            <Team>0</Team>
            <IsRunning>False</IsRunning>
            <SmartSpawning>False</SmartSpawning>
            {extraElements}
          </Points>
        </Spawns>
        """;

    private static (ModernSpawner Spawner, XmlSpawnerImporter.ImportResult Result) Import(string extraElements)
    {
        var name = "ImporterMappingTest-" + Guid.NewGuid();
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, BuildXml(name, extraElements));
            var result = XmlSpawnerImporter.ImportFromFile(tempFile, respawn: false);
            Assert.Equal(1, result.Imported);
            return (FindByName(name), result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

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
    public void MinMaxRefractory_MapToRefractoryMinMax_AsMinutes()
    {
        var (spawner, _) = Import("<MinRefractory>2</MinRefractory><MaxRefractory>5</MaxRefractory>");
        try
        {
            Assert.Equal(TimeSpan.FromMinutes(2), spawner.RefractoryMin);
            Assert.Equal(TimeSpan.FromMinutes(5), spawner.RefractoryMax);
        }
        finally
        {
            spawner.Delete();
        }
    }

    [Fact]
    public void SpawnOnTriggerAbsent_IsRunNowOrDrop()
    {
        var (spawner, _) = Import("<ProximityRange>8</ProximityRange>");
        try
        {
            Assert.Equal(0, spawner.MaxPendingCycles);
            Assert.Contains(spawner.TriggerDefinitions, d => d.Text == "proximity:8:true:false:5:0");
        }
        finally
        {
            spawner.Delete();
        }
    }

    [Fact]
    public void SpawnOnTriggerFalse_DefersToNextTickWithOneSlot()
    {
        var (spawner, _) = Import("<ProximityRange>8</ProximityRange><SpawnOnTrigger>False</SpawnOnTrigger>");
        try
        {
            Assert.Equal(1, spawner.MaxPendingCycles);
            Assert.Contains(spawner.TriggerDefinitions, d => d.Text == "proximity:8:true:false:5:0:mode:tick");
        }
        finally
        {
            spawner.Delete();
        }
    }

    [Fact]
    public void ProximityAndSpeech_CollapseIntoOneConjunctiveSpeechTrigger()
    {
        var (spawner, _) = Import("<ProximityRange>12</ProximityRange><SpeechTrigger>open</SpeechTrigger>");
        try
        {
            Assert.DoesNotContain(spawner.TriggerDefinitions, d => d.Text.StartsWith("proximity:", StringComparison.Ordinal));
            var speech = Assert.Single(spawner.TriggerDefinitions, d => d.Text.StartsWith("speech:", StringComparison.Ordinal));
            var parsed = Server.Engines.ModernSpawner.Triggers.SpeechTrigger.Parse(speech.Text);
            Assert.Equal("open", parsed.Keyword);
            Assert.Equal(12, parsed.Range);
        }
        finally
        {
            spawner.Delete();
        }
    }

    [Fact]
    public void PlayerPropertyName_WithoutSpeech_AttachesWhenToProximity()
    {
        var (spawner, _) = Import("<ProximityRange>8</ProximityRange><PlayerPropertyName>Karma&gt;0</PlayerPropertyName>");
        try
        {
            var proximity = Assert.Single(spawner.TriggerDefinitions, d => d.Text.StartsWith("proximity:", StringComparison.Ordinal));
            Assert.EndsWith(":when:trigMob.Karma > 0", proximity.Text);
        }
        finally
        {
            spawner.Delete();
        }
    }

    // Fix round 1: mode:tick must precede when: in the emitted definition text - TriggerTokens.Strip
    // treats when: as consuming everything after it, so a token appended past it is swallowed into the
    // expression source and never parses, silently dropping the deferral and leaving the when: dead.

    [Fact]
    public void SpawnOnTriggerFalseWithPlayerPropertyAndSpeech_ParsesModeTickWithACompilingWhen()
    {
        var (spawner, _) = Import(
            "<ProximityRange>12</ProximityRange><SpeechTrigger>open</SpeechTrigger>" +
            "<PlayerPropertyName>Karma&gt;0</PlayerPropertyName><SpawnOnTrigger>False</SpawnOnTrigger>");
        try
        {
            var speech = Assert.Single(spawner.TriggerDefinitions, d => d.Text.StartsWith("speech:", StringComparison.Ordinal));
            var trigger = TriggerSystem.Instance.ParseTrigger(speech.Text);
            Assert.Equal(CycleMode.Tick, trigger.Mode);
            Assert.NotNull(trigger.When);
            Assert.True(trigger.When.IsValid);
        }
        finally
        {
            spawner.Delete();
        }
    }

    [Fact]
    public void SpawnOnTriggerFalseWithPlayerPropertyAlone_ParsesModeTickWithACompilingWhen()
    {
        var (spawner, _) = Import(
            "<PlayerPropertyName>Karma&gt;0</PlayerPropertyName><SpawnOnTrigger>False</SpawnOnTrigger>");
        try
        {
            var proximity = Assert.Single(spawner.TriggerDefinitions, d => d.Text.StartsWith("proximity:", StringComparison.Ordinal));
            var trigger = TriggerSystem.Instance.ParseTrigger(proximity.Text);
            Assert.Equal(CycleMode.Tick, trigger.Mode);
            Assert.NotNull(trigger.When);
            Assert.True(trigger.When.IsValid);
        }
        finally
        {
            spawner.Delete();
        }
    }

    [Fact]
    public void SpawnOnTriggerFalseWithUnrepresentablePlayerPropertyAndSpeech_ParsesModeTickWithNoWhen()
    {
        var (spawner, result) = Import(
            "<ProximityRange>8</ProximityRange><SpeechTrigger>open</SpeechTrigger>" +
            "<PlayerPropertyName>GETONTHIS,Karma&gt;0</PlayerPropertyName><SpawnOnTrigger>False</SpawnOnTrigger>");
        try
        {
            var speech = Assert.Single(spawner.TriggerDefinitions, d => d.Text.StartsWith("speech:", StringComparison.Ordinal));
            Assert.DoesNotContain(":when:", speech.Text);
            Assert.EndsWith(":mode:tick", speech.Text);

            var trigger = TriggerSystem.Instance.ParseTrigger(speech.Text);
            Assert.Equal(CycleMode.Tick, trigger.Mode);
            Assert.Null(trigger.When);
            Assert.Contains(result.Notes, n => n.Contains("PlayerPropertyName") && n.Contains("GETONTHIS,Karma>0"));
        }
        finally
        {
            spawner.Delete();
        }
    }

    [Fact]
    public void IsGroup_SetsBaseGroupOnly_NotAllEntriesCycleMode()
    {
        var (spawner, _) = Import("<IsGroup>True</IsGroup>");
        try
        {
            Assert.True(spawner.Group);
            Assert.NotEqual(SpawnCycleMode.AllEntries, spawner.CycleMode);
        }
        finally
        {
            spawner.Delete();
        }
    }

    [Theory]
    [InlineData("0", "wall_time_window:")] // Realtime
    [InlineData("1", "game_time_window:")] // Gametime
    public void TodMode_MapsToTheMatchingGateAndAddsDespawnNote(string todMode, string expectedPrefix)
    {
        // TODStart/TODEnd are TotalMinutes (dev-docs §2): 480 = 8:00, 1020 = 17:00.
        var (spawner, result) = Import(
            $"<TODStart>480</TODStart><TODEnd>1020</TODEnd><TODMode>{todMode}</TODMode>");
        try
        {
            Assert.Contains(spawner.TriggerDefinitions, d => d.Text.StartsWith(expectedPrefix, StringComparison.Ordinal));
            Assert.Contains(result.Notes, n => n.Contains("despawned live spawns") && n.Contains("D10"));
        }
        finally
        {
            spawner.Delete();
        }
    }

    [Fact]
    public void Duration_AddsReportNoteOnly()
    {
        var (spawner, result) = Import("<Duration>30</Duration>");
        try
        {
            Assert.Contains(result.Notes, n => n.Contains("Duration") && n.Contains("D10"));
        }
        finally
        {
            spawner.Delete();
        }
    }
}
