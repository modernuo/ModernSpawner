using Server.Engines.Spawners;
using Server.Mobiles;

namespace Server.Engines.ModernSpawner;

/// <summary>
/// ModernSpawner configuration and initialization.
/// </summary>
public static class ModernSpawnerConfiguration
{
    /// <summary>
    /// Called during server startup to initialize ModernSpawner systems.
    /// Pre-warms the property accessor cache for common types.
    /// </summary>
    public static void Configure()
    {
        // Pre-warm the property accessor cache for common types.
        // This eliminates the first-access compilation cost during gameplay.
        // Based on benchmarks, each property compilation takes ~196μs.
        PropertyAccessorCache.PrewarmCache(
            // Core game types - most commonly accessed in scripts
            typeof(Mobile),
            typeof(PlayerMobile),
            typeof(BaseCreature),
            typeof(Item),

            // Spawner types - accessed via spawner/entry references
            typeof(ModernSpawner),
            typeof(ModernSpawnerEntry),

            // Common sub-objects accessed via chained properties
            typeof(Point3D),
            typeof(Skills),
            typeof(Skill)
        );
    }
}
