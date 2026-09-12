using System;
using Server.Engines.ModernSpawner.Scripting.Expressions;

namespace Server.Engines.ModernSpawner.Triggers;

/// <summary>
/// What a trigger does for the spawner it is registered on.
/// </summary>
public enum TriggerKind
{
    /// <summary>
    /// An event source: each accepted match buys one spawn cycle (proximity, speech, kill, skill).
    /// </summary>
    Event,

    /// <summary>
    /// A window: it does not buy cycles, it opens and closes the spawner's gate (the time windows).
    /// </summary>
    Gate
}

/// <summary>
/// When the cycle bought by an accepted event runs (the <c>mode:</c> token).
/// </summary>
public enum CycleMode
{
    /// <summary>
    /// Run the cycle as soon as the dispatch that raised the event returns. The default.
    /// </summary>
    Now,

    /// <summary>
    /// Arm the spawner's timer for an immediate tick and let the cycle run there, with the normal tick
    /// ordering and per-entry deadlines. This is XmlSpawner's <c>SpawnOnTrigger = false</c>.
    /// </summary>
    Tick
}

/// <summary>
/// Base interface for all trigger conditions that can activate a spawner.
/// </summary>
public interface ITrigger
{
    /// <summary>
    /// Gets the unique type identifier for serialization.
    /// </summary>
    string TriggerType { get; }

    /// <summary>
    /// Whether this trigger buys cycles (<see cref="TriggerKind.Event" />) or opens and closes the
    /// spawner's gate (<see cref="TriggerKind.Gate" />).
    /// </summary>
    TriggerKind Kind { get; }

    /// <summary>
    /// The <see cref="TriggerDefinition.Id" /> this trigger was parsed from. Bound at registration by
    /// <see cref="ITriggerSystem.ActivateTriggers" />; <see cref="Guid.Empty" /> while unbound.
    /// </summary>
    Guid Id { get; set; }

    /// <summary>
    /// The position of this trigger's definition in <see cref="ModernSpawner.TriggerDefinitions" />, or
    /// -1 while unbound. Gates report their open and close edges by this index.
    /// </summary>
    int DefinitionIndex { get; set; }

    /// <summary>
    /// The spawner-side runtime state (cooldown, kill count) for this trigger's definition, bound at
    /// registration. Null while unbound, which is how a trigger parsed outside a spawner behaves.
    /// </summary>
    TriggerRuntimeState State { get; set; }

    /// <summary>
    /// Whether an accepted event may start a stopped spawner (the <c>wake:</c> token). Gates ignore it.
    /// </summary>
    bool Wake { get; }

    /// <summary>
    /// When the cycle an accepted event buys runs (the <c>mode:</c> token). Gates ignore it.
    /// </summary>
    CycleMode Mode { get; }

    /// <summary>
    /// The per-trigger condition from the <c>when:</c> token, compiled once at parse time, or null when
    /// the definition carried no condition. Gates ignore it.
    /// </summary>
    CompiledExpression When { get; }

    /// <summary>
    /// Minimum time between two accepted events for this trigger. The spawner writes
    /// <see cref="TriggerRuntimeState.CooldownUntil" /> from it when it accepts an event;
    /// <see cref="Evaluate" /> only compares against it. Gates return <see cref="TimeSpan.Zero" />.
    /// </summary>
    TimeSpan Cooldown { get; }

    /// <summary>
    /// Evaluates whether this trigger condition is currently met. Pure: it reads
    /// <see cref="State" /> but never writes it, so an evaluation that the spawner goes on to reject
    /// leaves no trace. Cooldown, refractory and kill-counter advances belong to the spawner's
    /// acceptance path.
    /// </summary>
    /// <param name="context">The trigger evaluation context.</param>
    /// <returns>True if the trigger condition is met.</returns>
    bool Evaluate(in TriggerContext context);

    /// <summary>
    /// Called when this trigger needs to start monitoring for its condition.
    /// </summary>
    /// <param name="spawner">The spawner this trigger belongs to.</param>
    void Activate(ModernSpawner spawner);

    /// <summary>
    /// Called when this trigger should stop monitoring.
    /// </summary>
    void Deactivate();

    /// <summary>
    /// Serializes this trigger to a string for storage.
    /// </summary>
    string Serialize();
}
