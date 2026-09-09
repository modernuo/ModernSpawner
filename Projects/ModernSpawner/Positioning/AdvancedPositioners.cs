using System;
using System.Collections.Generic;

namespace Server.Engines.ModernSpawner.Positioning;

/// <summary>
/// Spawns entities at a waypoint item's location.
/// Usage: "waypoint" or "waypoint:WaypointName"
/// </summary>
public class WaypointPositioner : IPositioningRule
{
    public string RuleName => "waypoint";
    public string Description => "Spawns entities at waypoint item locations";

    public Point3D GetPosition(PositioningContext context)
    {
        var spawner = context.Spawner;
        var map = context.Map;

        if (spawner == null || map == null || map == Map.Internal)
        {
            return Point3D.Zero;
        }

        // Parse waypoint name from parameters if provided
        string waypointName = null;
        if (!string.IsNullOrEmpty(context.Parameters))
        {
            waypointName = context.Parameters;
        }

        // Find waypoint items near the spawner
        var waypoints = new List<Item>();
        var searchRange = Math.Max(spawner.HomeRange * 2, 20);

        foreach (var item in map.GetItemsInRange(spawner.Location, searchRange))
        {
            // Check if item is a waypoint (WayPoint class or named appropriately)
            if (IsWaypoint(item, waypointName))
            {
                waypoints.Add(item);
            }
        }

        if (waypoints.Count == 0)
        {
            return Point3D.Zero;
        }

        // Pick a random waypoint
        var waypoint = waypoints[Utility.Random(waypoints.Count)];
        var loc = waypoint.Location;

        // Try to find a spawnable location near the waypoint
        for (var i = 0; i < context.MaxAttempts; i++)
        {
            var x = loc.X + Utility.RandomMinMax(-2, 2);
            var y = loc.Y + Utility.RandomMinMax(-2, 2);
            var z = map.GetAverageZ(x, y);

            if (map.CanSpawnMobile(x, y, z))
            {
                return new Point3D(x, y, z);
            }
        }

        // Fall back to waypoint's exact location
        if (map.CanSpawnMobile(loc.X, loc.Y, loc.Z))
        {
            return loc;
        }

        return Point3D.Zero;
    }

    private static bool IsWaypoint(Item item, string name)
    {
        if (item.Deleted)
        {
            return false;
        }

        // Check by type name
        var typeName = item.GetType().Name;
        if (typeName.Contains("waypoint", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(name))
            {
                return true;
            }

            return item.Name?.Contains(name, StringComparison.OrdinalIgnoreCase) == true;
        }

        // Check by item name
        if (!string.IsNullOrEmpty(name) && item.Name?.Contains(name, StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        return false;
    }
}

/// <summary>
/// Spawns entities at a fixed offset from the spawner.
/// Usage: "relative:x,y" or "relative:x,y,z"
/// </summary>
public class RelativePositioner : IPositioningRule
{
    public string RuleName => "relative";
    public string Description => "Spawns entities at fixed offset from spawner (relative:x,y or relative:x,y,z)";

    public Point3D GetPosition(PositioningContext context)
    {
        var spawner = context.Spawner;
        var map = context.Map;

        if (spawner == null || map == null || map == Map.Internal)
        {
            return Point3D.Zero;
        }

        // Parse offset from parameters: "x,y" or "x,y,z"
        var offset = ParseOffset(context.Parameters);
        if (offset == Point3D.Zero && string.IsNullOrEmpty(context.Parameters))
        {
            return Point3D.Zero;
        }

        var targetX = spawner.Location.X + offset.X;
        var targetY = spawner.Location.Y + offset.Y;
        var targetZ = offset.Z != 0 ? spawner.Location.Z + offset.Z : map.GetAverageZ(targetX, targetY);

        // Try exact location first
        if (map.CanSpawnMobile(targetX, targetY, targetZ))
        {
            return new Point3D(targetX, targetY, targetZ);
        }

        // Try nearby locations
        for (var i = 0; i < context.MaxAttempts; i++)
        {
            var x = targetX + Utility.RandomMinMax(-1, 1);
            var y = targetY + Utility.RandomMinMax(-1, 1);
            var z = map.GetAverageZ(x, y);

            if (map.CanSpawnMobile(x, y, z))
            {
                return new Point3D(x, y, z);
            }
        }

        return Point3D.Zero;
    }

    private static Point3D ParseOffset(string parameters)
    {
        if (string.IsNullOrEmpty(parameters))
        {
            return Point3D.Zero;
        }

        var parts = parameters.Split(',');
        if (parts.Length < 2)
        {
            return Point3D.Zero;
        }

        if (!int.TryParse(parts[0].Trim(), out var x) || !int.TryParse(parts[1].Trim(), out var y))
        {
            return Point3D.Zero;
        }

        var z = 0;
        if (parts.Length >= 3)
        {
            int.TryParse(parts[2].Trim(), out z);
        }

        return new Point3D(x, y, z);
    }
}

/// <summary>
/// Spawns entities relative to the triggering mobile's location.
/// Usage: "player_relative:x,y" or "player_relative:x,y,z"
/// </summary>
public class PlayerRelativePositioner : IPositioningRule
{
    public string RuleName => "player_relative";
    public string Description => "Spawns entities relative to triggering player (player_relative:x,y)";

    public Point3D GetPosition(PositioningContext context)
    {
        var spawner = context.Spawner;
        var map = context.Map;
        var triggerMob = context.TriggeringMobile;

        if (spawner == null || map == null || map == Map.Internal)
        {
            return Point3D.Zero;
        }

        // If no triggering mobile, fall back to spawner location
        var baseLoc = triggerMob?.Location ?? spawner.Location;

        // Parse offset from parameters
        var offset = ParseOffset(context.Parameters);

        var targetX = baseLoc.X + offset.X;
        var targetY = baseLoc.Y + offset.Y;
        var targetZ = offset.Z != 0 ? baseLoc.Z + offset.Z : map.GetAverageZ(targetX, targetY);

        // Validate position is within spawner's allowed range
        var distFromSpawner = Math.Max(
            Math.Abs(targetX - spawner.Location.X),
            Math.Abs(targetY - spawner.Location.Y)
        );

        if (distFromSpawner > spawner.HomeRange * 2)
        {
            // Too far from spawner, clamp to range
            return Point3D.Zero;
        }

        if (map.CanSpawnMobile(targetX, targetY, targetZ))
        {
            return new Point3D(targetX, targetY, targetZ);
        }

        // Try nearby locations
        for (var i = 0; i < context.MaxAttempts; i++)
        {
            var x = targetX + Utility.RandomMinMax(-2, 2);
            var y = targetY + Utility.RandomMinMax(-2, 2);
            var z = map.GetAverageZ(x, y);

            if (map.CanSpawnMobile(x, y, z))
            {
                return new Point3D(x, y, z);
            }
        }

        return Point3D.Zero;
    }

    private static Point3D ParseOffset(string parameters)
    {
        if (string.IsNullOrEmpty(parameters))
        {
            return Point3D.Zero;
        }

        var parts = parameters.Split(',');
        if (parts.Length < 2)
        {
            return Point3D.Zero;
        }

        if (!int.TryParse(parts[0].Trim(), out var x) || !int.TryParse(parts[1].Trim(), out var y))
        {
            return Point3D.Zero;
        }

        var z = 0;
        if (parts.Length >= 3)
        {
            int.TryParse(parts[2].Trim(), out z);
        }

        return new Point3D(x, y, z);
    }
}

/// <summary>
/// Spawns entities at absolute world coordinates.
/// Usage: "absolute:x,y,z" or "absolute:x,y"
/// </summary>
public class AbsolutePositioner : IPositioningRule
{
    public string RuleName => "absolute";
    public string Description => "Spawns entities at absolute world coordinates (absolute:x,y,z)";

    public Point3D GetPosition(PositioningContext context)
    {
        var map = context.Map;

        if (map == null || map == Map.Internal)
        {
            return Point3D.Zero;
        }

        // Parse absolute coordinates from parameters
        var coords = ParseCoordinates(context.Parameters);
        if (coords == Point3D.Zero)
        {
            return Point3D.Zero;
        }

        var targetX = coords.X;
        var targetY = coords.Y;
        var targetZ = coords.Z != 0 ? coords.Z : map.GetAverageZ(targetX, targetY);

        if (map.CanSpawnMobile(targetX, targetY, targetZ))
        {
            return new Point3D(targetX, targetY, targetZ);
        }

        // Try nearby locations
        for (var i = 0; i < context.MaxAttempts; i++)
        {
            var x = targetX + Utility.RandomMinMax(-2, 2);
            var y = targetY + Utility.RandomMinMax(-2, 2);
            var z = map.GetAverageZ(x, y);

            if (map.CanSpawnMobile(x, y, z))
            {
                return new Point3D(x, y, z);
            }
        }

        return Point3D.Zero;
    }

    private static Point3D ParseCoordinates(string parameters)
    {
        if (string.IsNullOrEmpty(parameters))
        {
            return Point3D.Zero;
        }

        var parts = parameters.Split(',');
        if (parts.Length < 2)
        {
            return Point3D.Zero;
        }

        if (!int.TryParse(parts[0].Trim(), out var x) || !int.TryParse(parts[1].Trim(), out var y))
        {
            return Point3D.Zero;
        }

        var z = 0;
        if (parts.Length >= 3)
        {
            int.TryParse(parts[2].Trim(), out z);
        }

        return new Point3D(x, y, z);
    }
}

/// <summary>
/// Spawns entities in a row pattern (horizontal fill).
/// Usage: "row_fill" or "row_fill:spacing"
/// </summary>
public class RowFillPositioner : IPositioningRule
{
    public string RuleName => "row_fill";
    public string Description => "Spawns entities in a horizontal row pattern";

    // Track spawn index per spawner for sequential placement
    private static readonly Dictionary<Serial, int> _spawnIndices = new();

    public Point3D GetPosition(PositioningContext context)
    {
        var spawner = context.Spawner;
        var map = context.Map;

        if (spawner == null || map == null || map == Map.Internal)
        {
            return Point3D.Zero;
        }

        // Parse spacing from parameters (default: 2)
        var spacing = 2;
        if (!string.IsNullOrEmpty(context.Parameters) && int.TryParse(context.Parameters, out var parsed))
        {
            spacing = Math.Max(1, parsed);
        }

        // Get current spawn index for this spawner
        if (!_spawnIndices.TryGetValue(spawner.Serial, out var index))
        {
            index = 0;
        }

        var homeRange = spawner.HomeRange;
        var spawnerLoc = spawner.Location;

        // Calculate position in row
        var maxPerRow = (homeRange * 2) / spacing + 1;
        var xOffset = (index % maxPerRow) * spacing - homeRange;
        var yOffset = (index / maxPerRow) * spacing - homeRange;

        // Wrap if we've filled the area
        if (yOffset > homeRange)
        {
            index = 0;
            xOffset = -homeRange;
            yOffset = -homeRange;
        }

        var targetX = spawnerLoc.X + xOffset;
        var targetY = spawnerLoc.Y + yOffset;
        var targetZ = map.GetAverageZ(targetX, targetY);

        // Increment index for next spawn
        _spawnIndices[spawner.Serial] = index + 1;

        if (map.CanSpawnMobile(targetX, targetY, targetZ))
        {
            return new Point3D(targetX, targetY, targetZ);
        }

        // Try nearby if exact spot is blocked
        for (var i = 0; i < 5; i++)
        {
            var x = targetX + Utility.RandomMinMax(-1, 1);
            var y = targetY + Utility.RandomMinMax(-1, 1);
            var z = map.GetAverageZ(x, y);

            if (map.CanSpawnMobile(x, y, z))
            {
                return new Point3D(x, y, z);
            }
        }

        return Point3D.Zero;
    }

    /// <summary>
    /// Resets the spawn index for a spawner.
    /// </summary>
    public static void ResetIndex(Serial spawnerSerial)
    {
        _spawnIndices.Remove(spawnerSerial);
    }
}

/// <summary>
/// Spawns entities in a column pattern (vertical fill).
/// Usage: "column_fill" or "column_fill:spacing"
/// </summary>
public class ColumnFillPositioner : IPositioningRule
{
    public string RuleName => "column_fill";
    public string Description => "Spawns entities in a vertical column pattern";

    private static readonly Dictionary<Serial, int> _spawnIndices = new();

    public Point3D GetPosition(PositioningContext context)
    {
        var spawner = context.Spawner;
        var map = context.Map;

        if (spawner == null || map == null || map == Map.Internal)
        {
            return Point3D.Zero;
        }

        // Parse spacing from parameters (default: 2)
        var spacing = 2;
        if (!string.IsNullOrEmpty(context.Parameters) && int.TryParse(context.Parameters, out var parsed))
        {
            spacing = Math.Max(1, parsed);
        }

        // Get current spawn index
        if (!_spawnIndices.TryGetValue(spawner.Serial, out var index))
        {
            index = 0;
        }

        var homeRange = spawner.HomeRange;
        var spawnerLoc = spawner.Location;

        // Calculate position in column (Y first, then X)
        var maxPerColumn = (homeRange * 2) / spacing + 1;
        var yOffset = (index % maxPerColumn) * spacing - homeRange;
        var xOffset = (index / maxPerColumn) * spacing - homeRange;

        // Wrap if we've filled the area
        if (xOffset > homeRange)
        {
            index = 0;
            xOffset = -homeRange;
            yOffset = -homeRange;
        }

        var targetX = spawnerLoc.X + xOffset;
        var targetY = spawnerLoc.Y + yOffset;
        var targetZ = map.GetAverageZ(targetX, targetY);

        _spawnIndices[spawner.Serial] = index + 1;

        if (map.CanSpawnMobile(targetX, targetY, targetZ))
        {
            return new Point3D(targetX, targetY, targetZ);
        }

        for (var i = 0; i < 5; i++)
        {
            var x = targetX + Utility.RandomMinMax(-1, 1);
            var y = targetY + Utility.RandomMinMax(-1, 1);
            var z = map.GetAverageZ(x, y);

            if (map.CanSpawnMobile(x, y, z))
            {
                return new Point3D(x, y, z);
            }
        }

        return Point3D.Zero;
    }

    public static void ResetIndex(Serial spawnerSerial)
    {
        _spawnIndices.Remove(spawnerSerial);
    }
}

/// <summary>
/// Filters spawn positions by allowed tile IDs.
/// Usage: "tiles:id1,id2,id3" - only spawn on these land tile IDs
/// </summary>
public class TileFilterPositioner : IPositioningRule
{
    public string RuleName => "tiles";
    public string Description => "Spawns only on specific land tile IDs (tiles:id1,id2,id3)";

    public Point3D GetPosition(PositioningContext context)
    {
        var spawner = context.Spawner;
        var map = context.Map;

        if (spawner == null || map == null || map == Map.Internal)
        {
            return Point3D.Zero;
        }

        // Parse allowed tile IDs
        var allowedTiles = ParseTileIds(context.Parameters);
        if (allowedTiles.Count == 0)
        {
            return Point3D.Zero;
        }

        var homeRange = spawner.HomeRange;
        var spawnerLoc = spawner.Location;

        for (var i = 0; i < context.MaxAttempts * 2; i++)
        {
            var x = spawnerLoc.X + (Utility.Random(homeRange * 2 + 1) - homeRange);
            var y = spawnerLoc.Y + (Utility.Random(homeRange * 2 + 1) - homeRange);

            var landTile = map.Tiles.GetLandTile(x, y);
            var tileId = landTile.ID & TileData.MaxLandValue;

            if (allowedTiles.Contains(tileId))
            {
                var z = map.GetAverageZ(x, y);
                if (map.CanSpawnMobile(x, y, z))
                {
                    return new Point3D(x, y, z);
                }
            }
        }

        return Point3D.Zero;
    }

    private static HashSet<int> ParseTileIds(string parameters)
    {
        var result = new HashSet<int>();

        if (string.IsNullOrEmpty(parameters))
        {
            return result;
        }

        foreach (var part in parameters.Split(','))
        {
            var trimmed = part.Trim();

            // Support hex (0x123) or decimal
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(trimmed[2..], System.Globalization.NumberStyles.HexNumber, null, out var hexId))
                {
                    result.Add(hexId);
                }
            }
            else if (int.TryParse(trimmed, out var id))
            {
                result.Add(id);
            }
        }

        return result;
    }
}

/// <summary>
/// Filters spawn positions to exclude specific tile IDs.
/// Usage: "notiles:id1,id2,id3" - avoid these land tile IDs
/// </summary>
public class NoTilesPositioner : IPositioningRule
{
    public string RuleName => "notiles";
    public string Description => "Avoids spawning on specific land tile IDs (notiles:id1,id2,id3)";

    public Point3D GetPosition(PositioningContext context)
    {
        var spawner = context.Spawner;
        var map = context.Map;

        if (spawner == null || map == null || map == Map.Internal)
        {
            return Point3D.Zero;
        }

        // Parse excluded tile IDs
        var excludedTiles = ParseTileIds(context.Parameters);

        var homeRange = spawner.HomeRange;
        var spawnerLoc = spawner.Location;

        for (var i = 0; i < context.MaxAttempts; i++)
        {
            var x = spawnerLoc.X + (Utility.Random(homeRange * 2 + 1) - homeRange);
            var y = spawnerLoc.Y + (Utility.Random(homeRange * 2 + 1) - homeRange);

            var landTile = map.Tiles.GetLandTile(x, y);
            var tileId = landTile.ID & TileData.MaxLandValue;

            if (excludedTiles.Count == 0 || !excludedTiles.Contains(tileId))
            {
                var z = map.GetAverageZ(x, y);
                if (map.CanSpawnMobile(x, y, z))
                {
                    return new Point3D(x, y, z);
                }
            }
        }

        return Point3D.Zero;
    }

    private static HashSet<int> ParseTileIds(string parameters)
    {
        var result = new HashSet<int>();

        if (string.IsNullOrEmpty(parameters))
        {
            return result;
        }

        foreach (var part in parameters.Split(','))
        {
            var trimmed = part.Trim();

            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(trimmed[2..], System.Globalization.NumberStyles.HexNumber, null, out var hexId))
                {
                    result.Add(hexId);
                }
            }
            else if (int.TryParse(trimmed, out var id))
            {
                result.Add(id);
            }
        }

        return result;
    }
}

/// <summary>
/// Spawns entities only on water tiles.
/// Usage: "water"
/// </summary>
public class WaterOnlyPositioner : IPositioningRule
{
    public string RuleName => "water";
    public string Description => "Spawns entities only on water tiles";

    public Point3D GetPosition(PositioningContext context)
    {
        var spawner = context.Spawner;
        var map = context.Map;

        if (spawner == null || map == null || map == Map.Internal)
        {
            return Point3D.Zero;
        }

        var homeRange = spawner.HomeRange;
        var spawnerLoc = spawner.Location;

        for (var i = 0; i < context.MaxAttempts * 2; i++)
        {
            var x = spawnerLoc.X + (Utility.Random(homeRange * 2 + 1) - homeRange);
            var y = spawnerLoc.Y + (Utility.Random(homeRange * 2 + 1) - homeRange);
            var z = spawnerLoc.Z;

            if (IsWaterTile(map, x, y, ref z))
            {
                if (map.CanFit(x, y, z, 16, false, true, false))
                {
                    return new Point3D(x, y, z);
                }
            }
        }

        return Point3D.Zero;
    }

    private static bool IsWaterTile(Map map, int x, int y, ref int z)
    {
        var landTile = map.Tiles.GetLandTile(x, y);

        if ((TileData.LandTable[landTile.ID & TileData.MaxLandValue].Flags & TileFlag.Wet) != 0)
        {
            z = landTile.Z;
            return true;
        }

        foreach (var staticTile in map.Tiles.GetStaticAndMultiTiles(x, y))
        {
            if (TileData.ItemTable[staticTile.ID & TileData.MaxItemValue].Wet)
            {
                z = staticTile.Z;
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Spawns entities at the location of a specific item by ID or type.
/// Usage: "at_item:itemId" or "at_item:TypeName"
/// </summary>
public class ItemLocationPositioner : IPositioningRule
{
    public string RuleName => "at_item";
    public string Description => "Spawns at location of specific item (at_item:itemId or at_item:TypeName)";

    public Point3D GetPosition(PositioningContext context)
    {
        var spawner = context.Spawner;
        var map = context.Map;

        if (spawner == null || map == null || map == Map.Internal || string.IsNullOrEmpty(context.Parameters))
        {
            return Point3D.Zero;
        }

        Item targetItem = null;

        // Try parsing as serial first
        if (Serial.TryParse(context.Parameters, null, out var serial))
        {
            targetItem = World.FindItem(serial);
        }
        else
        {
            // Search by type name in range
            var searchRange = Math.Max(spawner.HomeRange * 2, 20);
            var targetTypeName = context.Parameters.ToLowerInvariant();

            foreach (var item in map.GetItemsInRange(spawner.Location, searchRange))
            {
                if (item.GetType().Name.Equals(targetTypeName, StringComparison.OrdinalIgnoreCase) ||
                    (item.Name?.Contains(targetTypeName, StringComparison.OrdinalIgnoreCase) == true))
                {
                    targetItem = item;
                    break;
                }
            }
        }

        if (targetItem == null || targetItem.Deleted || targetItem.Map != map)
        {
            return Point3D.Zero;
        }

        var loc = targetItem.Location;

        // Try at item location
        if (map.CanSpawnMobile(loc.X, loc.Y, loc.Z))
        {
            return loc;
        }

        // Try nearby
        for (var i = 0; i < context.MaxAttempts; i++)
        {
            var x = loc.X + Utility.RandomMinMax(-2, 2);
            var y = loc.Y + Utility.RandomMinMax(-2, 2);
            var z = map.GetAverageZ(x, y);

            if (map.CanSpawnMobile(x, y, z))
            {
                return new Point3D(x, y, z);
            }
        }

        return Point3D.Zero;
    }
}
