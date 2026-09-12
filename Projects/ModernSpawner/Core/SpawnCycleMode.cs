using System;

namespace Server.Engines.ModernSpawner;

/// <summary>
/// Controls how a <see cref="ModernSpawner"/> selects entries for each spawn cycle.
/// </summary>
public enum SpawnCycleMode
{
    /// <summary>
    /// Entries spawn independently. Each cycle picks one entry weighted by its probability.
    /// Subgroup is an organisational tag used by triggers and inter-spawner commands
    /// but does not affect selection. This is the default and matches classic ambient
    /// world spawners.
    /// </summary>
    Random = 0,

    /// <summary>
    /// The spawner behaves as a state machine. Each cycle picks one entry weighted by
    /// probability from entries whose <c>Subgroup</c> matches <c>CurrentSubgroup</c>.
    /// Transitions happen via <c>GotoSubgroup</c>, <c>AdvanceSequence</c>, kill triggers,
    /// or the <c>GOTO</c> / <c>SPAWN/N</c> inter-spawner commands. Suitable for scripted
    /// waves and multi-phase encounters.
    /// </summary>
    Sequential = 1,

    /// <summary>
    /// Every non-full entry spawns each cycle until all entries are at their max count.
    /// No further spawning happens until every spawned entity has been removed, at which
    /// point the whole group respawns. Suitable for monster camps and pack spawns that
    /// should appear and respawn as a unit.
    /// </summary>
    AllEntries = 2,

    /// <summary>
    /// Former name of <see cref="AllEntries"/>, kept for one release so exported JSON written
    /// before the rename still parses. It shares <see cref="AllEntries"/>' numeric value, so
    /// binary saves are unaffected. Do not use in new code.
    /// </summary>
    [Obsolete("Use AllEntries. Kept so JSON written as \"Group\" still parses.")]
    Group = AllEntries
}
