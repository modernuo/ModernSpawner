using System;
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
}
