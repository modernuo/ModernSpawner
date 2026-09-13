using System;
using System.Collections.Generic;
using System.Xml;
using Server.Engines.ModernSpawner.Migration;
using Server.Engines.ModernSpawner.Triggers;
using Xunit;

namespace Server.Engines.ModernSpawner.Tests;

/// <summary>
/// Trigger registration follows every flag and list change: the <see cref="ModernSpawner.TriggerActivated" />
/// setter registers and unregisters immediately, and stopping or deleting a spawner tears the registration
/// down regardless of what the flag says at that moment.
/// </summary>
[Collection("Sequential ModernSpawner Tests")]
public class ModernSpawnerTriggerRegistrationTests
{
    private const string Proximity = "proximity:8:true";

    private static ModernSpawner Place()
    {
        var spawner = new ModernSpawner(1, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), 0, default, "Rabbit");
        spawner.MoveToWorld(new Point3D(1500, 1500, 0), Map.Felucca);
        return spawner;
    }

    [Fact]
    public void TogglingTriggerActivated_RegistersAndUnregisters()
    {
        var spawner = Place();
        spawner.AddTriggerDefinition(Proximity);
        Assert.False(spawner.HandlesOnMovement);

        spawner.TriggerActivated = true;
        Assert.True(spawner.HandlesOnMovement);

        spawner.TriggerActivated = false;
        Assert.False(spawner.HandlesOnMovement);
        spawner.Delete();
    }

    [Fact]
    public void AddingDefinitionToActivatedRunningSpawner_RegistersImmediately()
    {
        var spawner = Place();
        spawner.TriggerActivated = true;
        Assert.False(spawner.HandlesOnMovement);

        spawner.AddTriggerDefinition(Proximity);
        spawner.EnsureTriggersActive();
        Assert.True(spawner.HandlesOnMovement);

        spawner.RemoveTriggerDefinitionAt(0);
        spawner.EnsureTriggersActive();
        Assert.False(spawner.HandlesOnMovement);
        spawner.Delete();
    }

    [Fact]
    public void DeletingAnActivatedSpawner_LeavesNothingRegistered()
    {
        var spawner = Place();
        spawner.AddTriggerDefinition(Proximity);
        spawner.TriggerActivated = true;
        Assert.True(spawner.HandlesOnMovement);

        spawner.TriggerActivated = false;
        spawner.TriggerActivated = true;
        spawner.Delete();

        Assert.False(TriggerSystem.Instance.IsRegistered(spawner));
    }

    [Fact]
    public void Stop_KeepsTheRegistrationAndDispatchAlive()
    {
        var spawner = Place();
        spawner.AddTriggerDefinition(Proximity);
        spawner.TriggerActivated = true;

        // A2: stopping stops the timer and nothing else. The registration, and with it movement
        // dispatch, has to survive - a wake trigger can only start a stopped spawner if it still
        // hears the event that would wake it.
        spawner.Stop();
        Assert.False(spawner.Running);
        Assert.True(TriggerSystem.Instance.IsRegistered(spawner));
        Assert.True(spawner.HandlesOnMovement);

        spawner.Start();
        Assert.True(TriggerSystem.Instance.IsRegistered(spawner));

        // Clearing the flag is what unregisters, whether the spawner is running or not.
        spawner.TriggerActivated = false;
        Assert.False(TriggerSystem.Instance.IsRegistered(spawner));

        spawner.TriggerActivated = true;
        Assert.True(TriggerSystem.Instance.IsRegistered(spawner));

        // ...and so does deletion, whatever the flag says at that moment.
        spawner.Delete();
        Assert.False(TriggerSystem.Instance.IsRegistered(spawner));
    }

    [Fact]
    public void GumpTimeTriggerDefinitions_ParseAndRegister()
    {
        // Built exactly as TriggerConfigGump builds them from its hour fields: the registered type
        // names, in the argument format each Parse accepts.
        const int startHour = 18;
        const int endHour = 6;
        var wallTime = $"wall_time_window:{startHour}:0:{endHour}:0";
        const string gameTime = "game_time_window:21:5:true";

        // ParseTrigger returns null for an unrecognised type (it only logs), so this pins the branch.
        Assert.NotNull(TriggerSystem.Instance.ParseTrigger(wallTime));
        Assert.NotNull(TriggerSystem.Instance.ParseTrigger(gameTime));

        var spawner = Place();
        spawner.AddTriggerDefinition(wallTime);
        spawner.AddTriggerDefinition(gameTime);
        spawner.TriggerActivated = true;

        Assert.True(TriggerSystem.Instance.IsRegistered(spawner));
        spawner.Delete();
    }

    [Fact]
    public void DeletingAStoppedSpawner_WithStaleRegistration_Unregisters()
    {
        var spawner = Place();
        spawner.AddTriggerDefinition(Proximity);
        spawner.Stop();                                   // Running false: OnStopped is out of the picture
        TriggerSystem.Instance.ActivateTriggers(spawner); // stale registration behind a false flag
        Assert.True(TriggerSystem.Instance.IsRegistered(spawner));

        spawner.Delete();                                 // only OnDelete can clean this up
        Assert.False(TriggerSystem.Instance.IsRegistered(spawner));
    }

    private static XmlNode ParseNode(string xml)
    {
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        return doc.DocumentElement;
    }

    // ProximityRange is the attribute ParseXmlSpawnerNode maps to a proximity trigger definition plus
    // TriggerActivated = true, so this form exercises trigger (de)registration alongside Running.
    private static string XmlSpawnerNode(string running)
    {
        var runningAttribute = running != null ? $" Running=\"{running}\"" : string.Empty;
        return "<XmlSpawner X=\"1500\" Y=\"1500\" Z=\"0\" Map=\"Felucca\"" + runningAttribute +
               " ProximityRange=\"8\"><SpawnObjects><Object Type=\"Rabbit\" MaxCount=\"1\" /></SpawnObjects></XmlSpawner>";
    }

    // The SpawnPoint form has no trigger-mapped attribute, so these tests assert Running only.
    private static string SpawnPointNode(string running)
    {
        var runningAttribute = running != null ? $" Running=\"{running}\"" : string.Empty;
        return "<SpawnPoint X=\"1500\" Y=\"1500\" Z=\"0\" Map=\"Felucca\"" + runningAttribute +
               " Creatures=\"Rabbit\" />";
    }

    [Fact]
    public void Migrator_RunningFalse_ProducesStoppedSpawnerWithNoRegisteredTriggers()
    {
        var stopped = XmlSpawnerMigrator.ParseXmlSpawnerNode(ParseNode(XmlSpawnerNode("false")));
        Assert.False(stopped.Running);

        // A1/A2: registration follows TriggerActivated, not Running, so an imported spawner that
        // arrives stopped still listens - it just does not spawn on a timer until it is started.
        Assert.True(stopped.TriggerActivated);
        Assert.True(TriggerSystem.Instance.IsRegistered(stopped));
        stopped.Delete();

        // Running="true" (the same construction path) must still register and run, so the fix for the
        // false case did not just make everything stop.
        var running = XmlSpawnerMigrator.ParseXmlSpawnerNode(ParseNode(XmlSpawnerNode("true")));
        Assert.True(running.Running);
        Assert.True(TriggerSystem.Instance.IsRegistered(running));
        running.Delete();
    }

    [Fact]
    public void SpawnPointMigrator_RunningFalse_ProducesStoppedSpawner()
    {
        var stopped = XmlSpawnerMigrator.ParseSpawnPointNode(ParseNode(SpawnPointNode("false")));
        Assert.False(stopped.Running);
        stopped.Delete();

        // Running absent defaults to true - the historical "always start" behavior for a form that had
        // no Running attribute before this fix.
        var running = XmlSpawnerMigrator.ParseSpawnPointNode(ParseNode(SpawnPointNode(null)));
        Assert.True(running.Running);
        running.Delete();
    }

    [Theory]
    [InlineData("Mining", "skill:Mining:8:0:False:5")]
    [InlineData("Mining+", "skill:Mining+:8:0:False:5")]
    [InlineData("Magery-,50,90", "skill:Magery-:8:50-90:False:5")]
    // An absent min is 0, the reading XmlSpawner itself gave "SkillName,,max".
    [InlineData("Magery,,90", "skill:Magery:8:0-90:False:5")]
    public void Migrator_MapsSkillTriggerAttribute(string xml, string expected)
    {
        var node = ParseNode($"<Point X=\"1500\" Y=\"1500\" Z=\"0\" Map=\"Felucca\" Running=\"false\" ProximityRange=\"8\" SkillTrigger=\"{xml}\" />");
        var spawner = XmlSpawnerMigrator.ParseXmlSpawnerNode(node);
        try
        {
            Assert.Contains(spawner.TriggerDefinitions, d => d.Text == expected);
        }
        finally
        {
            spawner.Delete();
        }
    }

    [Theory]
    // An unknown skill name, and a window whose max is below its min: both are logged and dropped, so no
    // skill definition reaches the spawner. TriggerActivated still comes from ProximityRange on the node,
    // which proves the rejected attribute neither set it nor cleared it.
    [InlineData("NotASkill")]
    [InlineData("99")]
    [InlineData("Magery,90,50")]
    public void Migrator_RejectsMalformedSkillTriggerAttribute(string xml)
    {
        var node = ParseNode($"<Point X=\"1500\" Y=\"1500\" Z=\"0\" Map=\"Felucca\" Running=\"false\" ProximityRange=\"8\" SkillTrigger=\"{xml}\" />");
        var spawner = XmlSpawnerMigrator.ParseXmlSpawnerNode(node);
        try
        {
            Assert.DoesNotContain(spawner.TriggerDefinitions, d => d.Text.StartsWith("skill:", StringComparison.Ordinal));

            // The proximity definition from the same node is untouched, so this is a targeted rejection
            // rather than the whole trigger block being lost.
            Assert.Contains(spawner.TriggerDefinitions, d => d.Text == "proximity:8:true:false:5:0");
            Assert.True(spawner.TriggerActivated);
        }
        finally
        {
            spawner.Delete();
        }
    }

    // --- Task 4: refractory, SpawnOnTrigger, conjunctive proximity+speech+property, IsGroup and TOD ---

    private static string BasePointAttributes =>
        "X=\"1500\" Y=\"1500\" Z=\"0\" Map=\"Felucca\" Running=\"false\"";

    [Fact]
    public void Migrator_MapsMinMaxRefractoryAttributes_AsMinutes()
    {
        var node = ParseNode($"<Point {BasePointAttributes} MinRefractory=\"2\" MaxRefractory=\"5\" />");
        var spawner = XmlSpawnerMigrator.ParseXmlSpawnerNode(node);
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
    public void Migrator_SpawnOnTriggerAbsent_IsRunNowOrDrop()
    {
        // Default (and SpawnOnTrigger="True") reproduce XmlSpawner: no queue, no mode:tick suffix.
        var node = ParseNode($"<Point {BasePointAttributes} ProximityRange=\"8\" />");
        var spawner = XmlSpawnerMigrator.ParseXmlSpawnerNode(node);
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
    public void Migrator_SpawnOnTriggerFalse_DefersToNextTickWithOneSlot()
    {
        var node = ParseNode($"<Point {BasePointAttributes} ProximityRange=\"8\" SpawnOnTrigger=\"False\" />");
        var spawner = XmlSpawnerMigrator.ParseXmlSpawnerNode(node);
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
    public void Migrator_ProximityAndSpeech_CollapseIntoOneConjunctiveSpeechTrigger()
    {
        var node = ParseNode(
            $"<Point {BasePointAttributes} ProximityRange=\"12\" SpeechTrigger=\"open\" />");
        var spawner = XmlSpawnerMigrator.ParseXmlSpawnerNode(node);
        try
        {
            // One speech trigger carrying the proximity range - not a separate proximity trigger too.
            Assert.DoesNotContain(spawner.TriggerDefinitions, d => d.Text.StartsWith("proximity:", StringComparison.Ordinal));
            var speech = Assert.Single(spawner.TriggerDefinitions, d => d.Text.StartsWith("speech:", StringComparison.Ordinal));
            var parsed = SpeechTrigger.Parse(speech.Text);
            Assert.Equal("open", parsed.Keyword);
            Assert.Equal(12, parsed.Range);
        }
        finally
        {
            spawner.Delete();
        }
    }

    [Fact]
    public void Migrator_ProximitySpeechAndPlayerProperty_AttachesWhenExpression()
    {
        var node = ParseNode(
            $"<Point {BasePointAttributes} ProximityRange=\"12\" SpeechTrigger=\"open\" PlayerPropertyName=\"Karma&gt;0\" />");
        var spawner = XmlSpawnerMigrator.ParseXmlSpawnerNode(node);
        try
        {
            var speech = Assert.Single(spawner.TriggerDefinitions, d => d.Text.StartsWith("speech:", StringComparison.Ordinal));
            Assert.EndsWith(":when:trigMob.Karma > 0", speech.Text);
        }
        finally
        {
            spawner.Delete();
        }
    }

    [Fact]
    public void Migrator_PlayerPropertyNameAlone_AttachesWhenToAProximityTrigger()
    {
        var node = ParseNode($"<Point {BasePointAttributes} PlayerPropertyName=\"Karma&gt;0\" />");
        var spawner = XmlSpawnerMigrator.ParseXmlSpawnerNode(node);
        try
        {
            var proximity = Assert.Single(spawner.TriggerDefinitions, d => d.Text.StartsWith("proximity:", StringComparison.Ordinal));
            Assert.EndsWith(":when:trigMob.Karma > 0", proximity.Text);
            Assert.True(spawner.TriggerActivated);
        }
        finally
        {
            spawner.Delete();
        }
    }

    [Fact]
    public void Migrator_UnrepresentablePlayerProperty_EmitsTriggerWithoutWhenAndAddsNote()
    {
        var node = ParseNode(
            $"<Point {BasePointAttributes} ProximityRange=\"8\" PlayerPropertyName=\"GETONTHIS,Karma&gt;0\" />");
        var notes = new List<string>();
        var spawner = XmlSpawnerMigrator.ParseXmlSpawnerNode(node, notes);
        try
        {
            var proximity = Assert.Single(spawner.TriggerDefinitions, d => d.Text.StartsWith("proximity:", StringComparison.Ordinal));
            Assert.DoesNotContain(":when:", proximity.Text);
            Assert.Contains(notes, n => n.Contains("PlayerPropertyName") && n.Contains("GETONTHIS,Karma>0"));
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
    public void Migrator_SpawnOnTriggerFalseWithPlayerPropertyAndSpeech_ParsesModeTickWithACompilingWhen()
    {
        var node = ParseNode(
            $"<Point {BasePointAttributes} ProximityRange=\"12\" SpeechTrigger=\"open\" PlayerPropertyName=\"Karma&gt;0\" SpawnOnTrigger=\"False\" />");
        var spawner = XmlSpawnerMigrator.ParseXmlSpawnerNode(node);
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
    public void Migrator_SpawnOnTriggerFalseWithPlayerPropertyAlone_ParsesModeTickWithACompilingWhen()
    {
        var node = ParseNode(
            $"<Point {BasePointAttributes} PlayerPropertyName=\"Karma&gt;0\" SpawnOnTrigger=\"False\" />");
        var spawner = XmlSpawnerMigrator.ParseXmlSpawnerNode(node);
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
    public void Migrator_SpawnOnTriggerFalseWithUnrepresentablePlayerPropertyAndSpeech_ParsesModeTickWithNoWhen()
    {
        var node = ParseNode(
            $"<Point {BasePointAttributes} ProximityRange=\"8\" SpeechTrigger=\"open\" PlayerPropertyName=\"GETONTHIS,Karma&gt;0\" SpawnOnTrigger=\"False\" />");
        var notes = new List<string>();
        var spawner = XmlSpawnerMigrator.ParseXmlSpawnerNode(node, notes);
        try
        {
            var speech = Assert.Single(spawner.TriggerDefinitions, d => d.Text.StartsWith("speech:", StringComparison.Ordinal));
            Assert.DoesNotContain(":when:", speech.Text);
            Assert.EndsWith(":mode:tick", speech.Text);

            var trigger = TriggerSystem.Instance.ParseTrigger(speech.Text);
            Assert.Equal(CycleMode.Tick, trigger.Mode);
            Assert.Null(trigger.When);
            Assert.Contains(notes, n => n.Contains("PlayerPropertyName") && n.Contains("GETONTHIS,Karma>0"));
        }
        finally
        {
            spawner.Delete();
        }
    }

    [Fact]
    public void Migrator_IsGroup_SetsBaseGroupOnly_NotAllEntriesCycleMode()
    {
        var node = ParseNode($"<Point {BasePointAttributes} IsGroup=\"True\" />");
        var spawner = XmlSpawnerMigrator.ParseXmlSpawnerNode(node);
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
    public void Migrator_MapsTodModeToTheMatchingGateAndAddsDespawnNote(string todMode, string expectedPrefix)
    {
        // TODStart/TODEnd are TotalMinutes (dev-docs §2): 480 = 8:00, 1020 = 17:00.
        var node = ParseNode(
            $"<Point {BasePointAttributes} TODStart=\"480\" TODEnd=\"1020\" TODMode=\"{todMode}\" />");
        var notes = new List<string>();
        var spawner = XmlSpawnerMigrator.ParseXmlSpawnerNode(node, notes);
        try
        {
            Assert.Contains(spawner.TriggerDefinitions, d => d.Text.StartsWith(expectedPrefix, StringComparison.Ordinal));
            Assert.Contains(notes, n => n.Contains("despawned live spawns") && n.Contains("D10"));
        }
        finally
        {
            spawner.Delete();
        }
    }

    [Fact]
    public void Migrator_Duration_AddsReportNoteOnly()
    {
        var node = ParseNode($"<Point {BasePointAttributes} Duration=\"30\" />");
        var notes = new List<string>();
        var spawner = XmlSpawnerMigrator.ParseXmlSpawnerNode(node, notes);
        try
        {
            Assert.Contains(notes, n => n.Contains("Duration") && n.Contains("D10"));
        }
        finally
        {
            spawner.Delete();
        }
    }
}
