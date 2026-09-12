using System;
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
        spawner.AddToTriggerDefinitions(Proximity);
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

        spawner.AddToTriggerDefinitions(Proximity);
        spawner.EnsureTriggersActive();
        Assert.True(spawner.HandlesOnMovement);

        spawner.RemoveFromTriggerDefinitions(Proximity);
        spawner.EnsureTriggersActive();
        Assert.False(spawner.HandlesOnMovement);
        spawner.Delete();
    }

    [Fact]
    public void DeletingAnActivatedSpawner_LeavesNothingRegistered()
    {
        var spawner = Place();
        spawner.AddToTriggerDefinitions(Proximity);
        spawner.TriggerActivated = true;
        Assert.True(spawner.HandlesOnMovement);

        spawner.TriggerActivated = false;
        spawner.TriggerActivated = true;
        spawner.Delete();

        Assert.False(TriggerSystem.Instance.IsRegistered(spawner));
    }

    [Fact]
    public void Stop_UnregistersEvenWhenFlagWasClearedAfterRegistration()
    {
        var spawner = Place();
        spawner.AddToTriggerDefinitions(Proximity);
        spawner.TriggerActivated = true;
        spawner.Stop();
        Assert.False(TriggerSystem.Instance.IsRegistered(spawner));

        // A registration that outlived its flag - the state a raw field write or a pre-fix gump edit
        // could leave behind. Teardown does not consult the flag, so it still has to be cleaned up.
        spawner.TriggerActivated = false;
        spawner.Start();
        Assert.False(TriggerSystem.Instance.IsRegistered(spawner));

        TriggerSystem.Instance.ActivateTriggers(spawner);
        Assert.True(TriggerSystem.Instance.IsRegistered(spawner));

        spawner.Stop();
        Assert.False(TriggerSystem.Instance.IsRegistered(spawner));
        spawner.Delete();
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
        spawner.AddToTriggerDefinitions(wallTime);
        spawner.AddToTriggerDefinitions(gameTime);
        spawner.TriggerActivated = true;

        Assert.True(TriggerSystem.Instance.IsRegistered(spawner));
        spawner.Delete();
    }

    [Fact]
    public void DeletingAStoppedSpawner_WithStaleRegistration_Unregisters()
    {
        var spawner = Place();
        spawner.AddToTriggerDefinitions(Proximity);
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
        Assert.False(TriggerSystem.Instance.IsRegistered(stopped));
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
    public void Migrator_MapsSkillTriggerAttribute(string xml, string expected)
    {
        var node = ParseNode($"<Point X=\"1500\" Y=\"1500\" Z=\"0\" Map=\"Felucca\" Running=\"false\" ProximityRange=\"8\" SkillTrigger=\"{xml}\" />");
        var spawner = XmlSpawnerMigrator.ParseXmlSpawnerNode(node);
        try
        {
            Assert.Contains(expected, spawner.TriggerDefinitions);
        }
        finally
        {
            spawner.Delete();
        }
    }
}
