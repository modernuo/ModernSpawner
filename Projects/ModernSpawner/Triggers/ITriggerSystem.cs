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
    /// <param name="triggerType">The definition prefix the factory answers to.</param>
    /// <param name="factory">Builds a trigger from a definition, or returns null when it is malformed.</param>
    void RegisterTriggerType(string triggerType, Func<string, ITrigger> factory);

    /// <summary>
    /// Parses a trigger definition string into an ITrigger instance.
    /// </summary>
    /// <param name="definition">The definition text.</param>
    /// <returns>The parsed trigger, or null when the type is unknown or the text malformed.</returns>
    ITrigger ParseTrigger(string definition);

    /// <summary>
    /// The parsed triggers registered for <paramref name="spawner" />, or null when it has none. One
    /// dictionary lookup: dispatch takes the set once and then walks a typed list by index.
    /// </summary>
    /// <param name="spawner">The spawner to look up.</param>
    /// <returns>Its registered set, or null.</returns>
    TriggerSet GetSet(ModernSpawner spawner);

    /// <summary>
    /// Queues <paramref name="spawner" /> for a drain once the outermost dispatch returns, so a spawn
    /// cycle never runs inside an enumeration that is still in progress.
    /// </summary>
    /// <param name="spawner">The spawner that wants to run a queued cycle.</param>
    void RequestDrain(ModernSpawner spawner);

    /// <summary>
    /// Activates all triggers for a spawner.
    /// </summary>
    /// <param name="spawner">The spawner to register.</param>
    void ActivateTriggers(ModernSpawner spawner);

    /// <summary>
    /// Deactivates all triggers for a spawner.
    /// </summary>
    /// <param name="spawner">The spawner to unregister.</param>
    void DeactivateTriggers(ModernSpawner spawner);

    /// <summary>
    /// Called when a mobile enters proximity of a specific spawner.
    /// Used by Item.OnMovement for optimized sector-based dispatch.
    /// </summary>
    /// <param name="mobile">The mobile that moved.</param>
    /// <param name="location">Where it moved to.</param>
    /// <param name="map">The map it moved on.</param>
    /// <param name="spawner">The spawner the movement was dispatched to.</param>
    void OnMobileProximity(Mobile mobile, Point3D location, Map map, ModernSpawner spawner);

    /// <summary>
    /// Called when speech is detected near a specific spawner.
    /// Used by Item.OnSpeech for optimized sector-based dispatch.
    /// </summary>
    /// <param name="speaker">The mobile that spoke.</param>
    /// <param name="text">What was said.</param>
    /// <param name="location">Where it was said.</param>
    /// <param name="map">The map it was said on.</param>
    /// <param name="spawner">The spawner the speech was dispatched to.</param>
    void OnSpeech(Mobile speaker, string text, Point3D location, Map map, ModernSpawner spawner);

    /// <summary>
    /// Called when a mobile attempts a skill. Dispatched from <c>SkillEvents.SkillUsed</c> through
    /// <see cref="ModernSpawnerEvents" />, so it runs for every player skill attempt server-wide.
    /// </summary>
    /// <param name="mobile">The mobile that attempted the skill.</param>
    /// <param name="skill">The skill attempted.</param>
    /// <param name="success">Whether the attempt succeeded.</param>
    void OnSkillUse(Mobile mobile, Skill skill, bool success);

    /// <summary>
    /// Called when a spawned entity is killed.
    /// </summary>
    /// <param name="spawner">The spawner that owned the spawn.</param>
    /// <param name="killed">The entity that died.</param>
    /// <param name="killer">The mobile credited with the kill, or null.</param>
    void OnEntityKilled(ModernSpawner spawner, IEntity killed, Mobile killer);
}
