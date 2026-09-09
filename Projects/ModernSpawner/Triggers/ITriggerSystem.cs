using System;

namespace Server.Engines.ModernSpawner.Triggers;

/// <summary>
/// Interface for the trigger management system.
/// Handles registration, parsing, and evaluation of spawner triggers.
/// </summary>
public interface ITriggerSystem
{
    /// <summary>
    /// Registers a trigger factory for a specific trigger type.
    /// </summary>
    void RegisterTriggerType(string triggerType, Func<string, ITrigger> factory);

    /// <summary>
    /// Parses a trigger definition string into an ITrigger instance.
    /// </summary>
    ITrigger ParseTrigger(string definition);

    /// <summary>
    /// Activates all triggers for a spawner.
    /// </summary>
    void ActivateTriggers(ModernSpawner spawner);

    /// <summary>
    /// Deactivates all triggers for a spawner.
    /// </summary>
    void DeactivateTriggers(ModernSpawner spawner);

    /// <summary>
    /// Called when a mobile enters proximity of a specific spawner.
    /// Used by Item.OnMovement for optimized sector-based dispatch.
    /// </summary>
    void OnMobileProximity(Mobile mobile, Point3D location, Map map, ModernSpawner spawner);

    /// <summary>
    /// Called when speech is detected near a specific spawner.
    /// Used by Item.OnSpeech for optimized sector-based dispatch.
    /// </summary>
    void OnSpeech(Mobile speaker, string text, Point3D location, Map map, ModernSpawner spawner);

    /// <summary>
    /// Called when a spawned entity is killed.
    /// </summary>
    void OnEntityKilled(ModernSpawner spawner, IEntity killed, Mobile killer);
}
