using System;

namespace Server.Engines.ModernSpawner.Positioning;

/// <summary>
/// Default spawn positioner that uses intelligent positioning with terrain awareness.
/// </summary>
public class DefaultPositioner : ISpawnPositioner
{
    public string PositionerId => "default";

    /// <summary>
    /// Singleton instance for convenience.
    /// </summary>
    public static DefaultPositioner Instance { get; } = new();

    public Point3D GetPosition(PositioningContext context)
    {
        if (context.Map == null || context.Map == Map.Internal)
        {
            return context.Spawner?.Location ?? Point3D.Zero;
        }

        var spawner = context.Spawner;
        if (spawner == null)
        {
            return Point3D.Zero;
        }

        // Check for spawn area first
        if (spawner.SpawnBounds is { Width: > 0, Height: > 0 })
        {
            var areaPos = GetPositionInArea(context, spawner.SpawnBounds);
            if (areaPos != Point3D.Zero)
            {
                return areaPos;
            }
        }

        // Determine mobile characteristics
        bool waterMob = false, waterOnlyMob = false;
        if (context.Spawned is Mobile mob)
        {
            waterMob = mob.CanSwim;
            waterOnlyMob = mob.CanSwim && mob.CantWalk;
        }

        var homeRange = spawner.HomeRange;
        var map = context.Map;
        var spawnerLoc = spawner.Location;

        // Try multiple times to find a valid position
        for (var i = 0; i < context.MaxAttempts; i++)
        {
            var x = spawnerLoc.X + (Utility.Random(homeRange * 2 + 1) - homeRange);
            var y = spawnerLoc.Y + (Utility.Random(homeRange * 2 + 1) - homeRange);
            var mapZ = map.GetAverageZ(x, y);

            // Check Z delta
            if (Math.Abs(mapZ - spawnerLoc.Z) > context.MaxZDelta)
            {
                continue;
            }

            // Try water positions for water mobs
            if (waterMob)
            {
                if (IsValidWater(map, x, y, spawnerLoc.Z))
                {
                    return new Point3D(x, y, spawnerLoc.Z);
                }

                if (IsValidWater(map, x, y, mapZ))
                {
                    return new Point3D(x, y, mapZ);
                }
            }

            // Try land positions for non-water-only mobs
            if (!waterOnlyMob)
            {
                if (map.CanSpawnMobile(x, y, spawnerLoc.Z))
                {
                    return new Point3D(x, y, spawnerLoc.Z);
                }

                if (map.CanSpawnMobile(x, y, mapZ))
                {
                    return new Point3D(x, y, mapZ);
                }
            }
        }

        // Return spawner location as fallback
        return spawner.HomeLocation;
    }

    private Point3D GetPositionInArea(PositioningContext context, Rectangle3D area)
    {
        var map = context.Map;

        for (var i = 0; i < context.MaxAttempts / 2; i++)
        {
            var x = Utility.RandomMinMax(area.Start.X, area.End.X - 1);
            var y = Utility.RandomMinMax(area.Start.Y, area.End.Y - 1);
            var z = map.GetAverageZ(x, y);

            // Respect Z constraints if set
            if (area.Depth > 0 && (z < area.Start.Z || z >= area.End.Z))
            {
                continue;
            }

            if (IsValidPosition(new Point3D(x, y, z), map, context.Spawned))
            {
                return new Point3D(x, y, z);
            }
        }

        return Point3D.Zero;
    }

    public bool IsValidPosition(Point3D position, Map map, ISpawnable spawned)
    {
        if (map == null || map == Map.Internal)
        {
            return false;
        }

        if (spawned is Mobile mob)
        {
            if (mob.CanSwim && mob.CantWalk)
            {
                return IsValidWater(map, position.X, position.Y, position.Z);
            }

            if (mob.CanSwim)
            {
                return map.CanSpawnMobile(position.X, position.Y, position.Z) ||
                       IsValidWater(map, position.X, position.Y, position.Z);
            }
        }

        return map.CanSpawnMobile(position.X, position.Y, position.Z);
    }

    private static bool IsValidWater(Map map, int x, int y, int z)
    {
        if (!Region.Find(new Point3D(x, y, z), map).AllowSpawn() ||
            !map.CanFit(x, y, z, 16, false, true, false))
        {
            return false;
        }

        var landTile = map.Tiles.GetLandTile(x, y);

        if (landTile.Z == z && (TileData.LandTable[landTile.ID & TileData.MaxLandValue].Flags & TileFlag.Wet) != 0)
        {
            return true;
        }

        foreach (var staticTile in map.Tiles.GetStaticAndMultiTiles(x, y))
        {
            if (staticTile.Z == z && TileData.ItemTable[staticTile.ID & TileData.MaxItemValue].Wet)
            {
                return true;
            }
        }

        return false;
    }
}
