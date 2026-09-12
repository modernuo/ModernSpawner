using System;
using ModernUO.Serialization;

namespace Server.Engines.ModernSpawner.Triggers;

/// <summary>
/// Per-definition runtime state, keyed by <see cref="TriggerDefinition.Id"/>. Lives on the spawner
/// rather than in the trigger system so a tick never has to look anything up; the parsed trigger
/// object is bound to its state at registration.
/// </summary>
[SerializationGenerator(0, false)]
public partial class TriggerRuntimeState
{
    [DirtyTrackingEntity]
    private ModernSpawner _spawner;

    /// <summary>The <see cref="TriggerDefinition.Id"/> this state belongs to.</summary>
    [SerializableField(0, setter: "private")]
    private Guid _id;

    /// <summary>
    /// Absolute instant before which this trigger cannot accept another event. Default
    /// (<see cref="DateTime.MinValue"/>) means "no cooldown pending".
    /// </summary>
    [SerializableField(1)]
    private DateTime _cooldownUntil;

    /// <summary>Kills counted toward this trigger's threshold since it last fired or was reset.</summary>
    [SerializableField(2)]
    private int _killCount;

    /// <summary>
    /// Constructor used by the serialization generator when reading a spawner's state list.
    /// The fields are overwritten by <c>Deserialize</c> immediately afterwards.
    /// </summary>
    /// <param name="spawner">The spawner that owns this state.</param>
    public TriggerRuntimeState(ModernSpawner spawner) => _spawner = spawner;

    /// <summary>Creates empty state bound to a definition id.</summary>
    /// <param name="spawner">The spawner that owns this state.</param>
    /// <param name="id">The <see cref="TriggerDefinition.Id"/> this state belongs to.</param>
    public TriggerRuntimeState(ModernSpawner spawner, Guid id)
    {
        _spawner = spawner;
        _id = id;
    }

    /// <summary>Re-parents this state, e.g. after a dupe copies the list.</summary>
    /// <param name="spawner">The spawner that now owns this state.</param>
    public void SetParent(ModernSpawner spawner) => _spawner = spawner;

    /// <summary>Clears the cooldown and the kill counter, leaving the binding intact.</summary>
    public void Reset()
    {
        CooldownUntil = default;
        KillCount = 0;
    }
}
