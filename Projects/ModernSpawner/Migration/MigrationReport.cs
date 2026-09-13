using System.Collections.Generic;

namespace Server.Engines.ModernSpawner.Migration;

/// <summary>
/// Aggregates the outcome of an <see cref="XmlSpawnerMigrator.ImportFromFile" /> run: how many spawners
/// were created or failed, plus one advisory line per spawner for anything the migration approximated or
/// dropped (a time-of-day window that no longer despawns on close, a <c>Duration</c> with no despawn timer
/// to carry it, an untranslatable <c>PlayerPropertyName</c>, ...).
/// </summary>
public sealed class MigrationReport
{
    private readonly List<(string SpawnerName, string Note)> _notes = [];

    /// <summary>Spawners successfully created.</summary>
    public int Success { get; private set; }

    /// <summary>Nodes that failed to parse or produce a spawner.</summary>
    public int Failed { get; private set; }

    /// <summary>One advisory line per spawner; the migration otherwise succeeded for that spawner.</summary>
    public IReadOnlyList<(string SpawnerName, string Note)> Notes => _notes;

    /// <summary>Records one successfully created spawner.</summary>
    internal void RecordSuccess() => Success++;

    /// <summary>Records one node that failed to parse or produce a spawner.</summary>
    internal void RecordFailure() => Failed++;

    /// <summary>Adds one advisory line for <paramref name="spawnerName" />.</summary>
    internal void AddNote(string spawnerName, string note) => _notes.Add((spawnerName, note));
}
