using System;
using ModernUO.Serialization;

namespace Server.Engines.ModernSpawner.Triggers;

/// <summary>
/// One trigger definition on a <see cref="ModernSpawner"/>: the parse text plus a stable identity.
/// The <see cref="Id"/> is generated once, when the definition is created, and never changes - it
/// survives reordering and edits of <see cref="Text"/>, so per-trigger runtime state
/// (<see cref="TriggerRuntimeState"/>) and queued cycles (<see cref="PendingCycle"/>) can name the
/// definition they belong to across saves, gump edits and DTO round trips.
/// </summary>
[SerializationGenerator(0, false)]
public partial class TriggerDefinition
{
    // Editing a definition dirties the spawner that owns it; the generator resolves this on the
    // declared type only, so it is declared here rather than inherited.
    [DirtyTrackingEntity]
    private ModernSpawner _spawner;

    /// <summary>Stable identity of this definition. Version 7 so ids sort by creation time.</summary>
    [SerializableField(0, setter: "private")]
    private Guid _id;

    /// <summary>The definition text the trigger system parses, e.g. <c>proximity:8:true</c>.</summary>
    [SerializableField(1)]
    private string _text;

    /// <summary>
    /// Constructor used by the serialization generator when reading a spawner's definition list.
    /// The fields are overwritten by <c>Deserialize</c> immediately afterwards.
    /// </summary>
    /// <param name="spawner">The spawner that owns this definition.</param>
    public TriggerDefinition(ModernSpawner spawner) => _spawner = spawner;

    /// <summary>Creates a definition with a freshly generated id.</summary>
    /// <param name="spawner">The spawner that owns this definition.</param>
    /// <param name="text">The definition text the trigger system parses.</param>
    public TriggerDefinition(ModernSpawner spawner, string text) : this(spawner, Guid.Empty, text)
    {
    }

    /// <summary>
    /// Creates a definition with an explicit id. Import paths use this so ids survive an export and
    /// re-import; <see cref="Guid.Empty"/> asks for a fresh id.
    /// </summary>
    /// <param name="spawner">The spawner that owns this definition.</param>
    /// <param name="id">The id to keep, or <see cref="Guid.Empty"/> to generate one.</param>
    /// <param name="text">The definition text the trigger system parses.</param>
    public TriggerDefinition(ModernSpawner spawner, Guid id, string text)
    {
        _spawner = spawner;
        _id = id == Guid.Empty ? Guid.CreateVersion7() : id;
        _text = text;
    }

    /// <summary>Re-parents this definition, e.g. after a dupe copies the list.</summary>
    /// <param name="spawner">The spawner that now owns this definition.</param>
    public void SetParent(ModernSpawner spawner) => _spawner = spawner;
}
