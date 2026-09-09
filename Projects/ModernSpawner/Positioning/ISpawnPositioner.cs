namespace Server.Engines.ModernSpawner.Positioning;

/// <summary>
/// Interface for spawn positioning strategies.
/// Determines where spawned entities should be placed in the world.
/// </summary>
public interface ISpawnPositioner
{
    /// <summary>
    /// Gets the unique identifier for this positioning strategy.
    /// </summary>
    string PositionerId { get; }

    /// <summary>
    /// Calculates the spawn position for an entity.
    /// </summary>
    /// <param name="context">The positioning context with spawner and entity info.</param>
    /// <returns>The calculated position, or Point3D.Zero if no valid position found.</returns>
    Point3D GetPosition(PositioningContext context);

    /// <summary>
    /// Validates whether a position is suitable for spawning.
    /// </summary>
    bool IsValidPosition(Point3D position, Map map, ISpawnable spawned);
}

/// <summary>
/// Context for position calculation.
/// </summary>
public class PositioningContext
{
    /// <summary>
    /// The spawner requesting the position.
    /// </summary>
    public ModernSpawner Spawner { get; }

    /// <summary>
    /// The entity being spawned.
    /// </summary>
    public ISpawnable Spawned { get; }

    /// <summary>
    /// The spawner entry (if applicable).
    /// </summary>
    public ModernSpawnerEntry Entry { get; }

    /// <summary>
    /// The map to spawn on.
    /// </summary>
    public Map Map { get; }

    /// <summary>
    /// Maximum number of positioning attempts.
    /// </summary>
    public int MaxAttempts { get; set; } = 20;

    /// <summary>
    /// Maximum Z delta from spawner.
    /// </summary>
    public int MaxZDelta { get; set; } = 20;

    /// <summary>
    /// Parameters passed to the positioning rule (e.g., "5,10" for relative:5,10).
    /// </summary>
    public string Parameters { get; set; }

    /// <summary>
    /// The mobile that triggered the spawn (if any).
    /// </summary>
    public Mobile TriggeringMobile { get; set; }

    public PositioningContext(ModernSpawner spawner, ISpawnable spawned, Map map, ModernSpawnerEntry entry = null)
    {
        Spawner = spawner;
        Spawned = spawned;
        Map = map;
        Entry = entry;
    }
}
