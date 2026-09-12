using System;
using ModernUO.Serialization;

namespace Server.Engines.ModernSpawner.Triggers;

/// <summary>
/// One queued spawn cycle bought by an accepted trigger event. The queue on the spawner holds at most
/// <see cref="ModernSpawner.MaxPendingCycles"/> of these; each carries the mobile that caused it so a
/// deferred drain can still position relative to that player.
/// </summary>
[SerializationGenerator(0, false)]
public partial class PendingCycle
{
    [DirtyTrackingEntity]
    private ModernSpawner _spawner;

    /// <summary>
    /// The <see cref="TriggerDefinition.Id"/> that bought this cycle, or <see cref="Guid.Empty"/> for
    /// an external source (a script or command calling <see cref="ModernSpawner.Trigger"/>).
    /// </summary>
    [SerializableField(0, setter: "private")]
    private Guid _triggerId;

    /// <summary>
    /// Serial of the mobile that raised the event, or <see cref="Serial.Zero"/> when there was none.
    /// Resolved at drain time; the mobile may be gone by then.
    /// </summary>
    [SerializableField(1, setter: "private")]
    private Serial _triggeringMobile;

    /// <summary>
    /// Constructor used by the serialization generator when reading a spawner's pending list.
    /// The fields are overwritten by <c>Deserialize</c> immediately afterwards.
    /// </summary>
    /// <param name="spawner">The spawner that owns this slot.</param>
    public PendingCycle(ModernSpawner spawner) => _spawner = spawner;

    /// <summary>Creates a queued cycle for a trigger and the mobile that raised it.</summary>
    /// <param name="spawner">The spawner that owns this slot.</param>
    /// <param name="triggerId">The definition that bought the cycle, or <see cref="Guid.Empty"/>.</param>
    /// <param name="triggeringMobile">The mobile that raised the event, or <see cref="Serial.Zero"/>.</param>
    public PendingCycle(ModernSpawner spawner, Guid triggerId, Serial triggeringMobile)
    {
        _spawner = spawner;
        _triggerId = triggerId;
        _triggeringMobile = triggeringMobile;
    }

    /// <summary>Re-parents this slot, e.g. after a dupe copies the list.</summary>
    /// <param name="spawner">The spawner that now owns this slot.</param>
    public void SetParent(ModernSpawner spawner) => _spawner = spawner;
}
