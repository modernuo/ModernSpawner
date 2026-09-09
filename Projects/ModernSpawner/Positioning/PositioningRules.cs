using System;
using System.Collections.Generic;

namespace Server.Engines.ModernSpawner.Positioning;

/// <summary>
/// Registry for named positioning rules.
/// Allows spawner entries to reference positioning rules by name.
/// Rules can include parameters: "relative:5,10" parses as rule="relative", params="5,10"
/// </summary>
public static class PositioningRules
{
    private static readonly Dictionary<string, IPositioningRule> _rules =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ISpawnPositioner _defaultPositioner = DefaultPositioner.Instance;

    static PositioningRules()
    {
        // Register built-in rules
        Register(new NearWaterRule());
        Register(new AvoidWaterRule());
        Register(new CenteredRule());
        Register(new PerimeterRule());

        // Advanced positioners
        Register(new WaypointPositioner());
        Register(new RelativePositioner());
        Register(new PlayerRelativePositioner());
        Register(new AbsolutePositioner());
        Register(new RowFillPositioner());
        Register(new ColumnFillPositioner());
        Register(new TileFilterPositioner());
        Register(new NoTilesPositioner());
        Register(new WaterOnlyPositioner());
        Register(new ItemLocationPositioner());
    }

    /// <summary>
    /// Registers a positioning rule.
    /// </summary>
    public static void Register(IPositioningRule rule)
    {
        _rules[rule.RuleName] = rule;
    }

    /// <summary>
    /// Gets a positioning rule by name.
    /// </summary>
    public static IPositioningRule GetRule(string ruleName)
    {
        if (string.IsNullOrEmpty(ruleName))
        {
            return null;
        }

        return _rules.TryGetValue(ruleName, out var rule) ? rule : null;
    }

    /// <summary>
    /// Gets all registered rule names.
    /// </summary>
    public static IEnumerable<string> GetRuleNames() => _rules.Keys;

    /// <summary>
    /// Parses a rule string into rule name and parameters.
    /// Format: "ruleName" or "ruleName:param1,param2"
    /// </summary>
    public static (string ruleName, string parameters) ParseRuleString(string ruleString)
    {
        if (string.IsNullOrEmpty(ruleString))
        {
            return (null, null);
        }

        var colonIndex = ruleString.IndexOf(':');
        if (colonIndex < 0)
        {
            return (ruleString.Trim(), null);
        }

        var ruleName = ruleString[..colonIndex].Trim();
        var parameters = ruleString[(colonIndex + 1)..].Trim();

        return (ruleName, parameters);
    }

    /// <summary>
    /// Gets a position using a named rule, or falls back to default positioning.
    /// Rule string can include parameters: "relative:5,10"
    /// </summary>
    public static Point3D GetPosition(string ruleString, PositioningContext context)
    {
        var (ruleName, parameters) = ParseRuleString(ruleString);

        var rule = GetRule(ruleName);
        if (rule != null)
        {
            // Set parameters on context
            context.Parameters = parameters;

            var position = rule.GetPosition(context);
            if (position != Point3D.Zero)
            {
                return position;
            }
        }

        // Fall back to default positioner
        return _defaultPositioner.GetPosition(context);
    }
}

/// <summary>
/// Positioning rule that tries to spawn near water.
/// </summary>
public class NearWaterRule : IPositioningRule
{
    public string RuleName => "near_water";
    public string Description => "Spawns entities near water tiles";

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

        for (var i = 0; i < context.MaxAttempts; i++)
        {
            var x = spawnerLoc.X + (Utility.Random(homeRange * 2 + 1) - homeRange);
            var y = spawnerLoc.Y + (Utility.Random(homeRange * 2 + 1) - homeRange);
            var z = map.GetAverageZ(x, y);

            // Check if this position is near water (within 3 tiles)
            if (IsNearWater(map, x, y, z, 3))
            {
                if (map.CanSpawnMobile(x, y, z))
                {
                    return new Point3D(x, y, z);
                }
            }
        }

        return Point3D.Zero;
    }

    private static bool IsNearWater(Map map, int x, int y, int z, int range)
    {
        for (var dx = -range; dx <= range; dx++)
        {
            for (var dy = -range; dy <= range; dy++)
            {
                var checkX = x + dx;
                var checkY = y + dy;

                var landTile = map.Tiles.GetLandTile(checkX, checkY);
                if ((TileData.LandTable[landTile.ID & TileData.MaxLandValue].Flags & TileFlag.Wet) != 0)
                {
                    return true;
                }

                foreach (var staticTile in map.Tiles.GetStaticAndMultiTiles(checkX, checkY))
                {
                    if (TileData.ItemTable[staticTile.ID & TileData.MaxItemValue].Wet)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}

/// <summary>
/// Positioning rule that avoids spawning in or near water.
/// </summary>
public class AvoidWaterRule : IPositioningRule
{
    public string RuleName => "avoid_water";
    public string Description => "Spawns entities away from water tiles";

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

        for (var i = 0; i < context.MaxAttempts; i++)
        {
            var x = spawnerLoc.X + (Utility.Random(homeRange * 2 + 1) - homeRange);
            var y = spawnerLoc.Y + (Utility.Random(homeRange * 2 + 1) - homeRange);
            var z = map.GetAverageZ(x, y);

            // Check if position and surrounding area are not water
            if (!HasNearbyWater(map, x, y, 2) && map.CanSpawnMobile(x, y, z))
            {
                return new Point3D(x, y, z);
            }
        }

        return Point3D.Zero;
    }

    private static bool HasNearbyWater(Map map, int x, int y, int range)
    {
        for (var dx = -range; dx <= range; dx++)
        {
            for (var dy = -range; dy <= range; dy++)
            {
                var checkX = x + dx;
                var checkY = y + dy;

                var landTile = map.Tiles.GetLandTile(checkX, checkY);
                if ((TileData.LandTable[landTile.ID & TileData.MaxLandValue].Flags & TileFlag.Wet) != 0)
                {
                    return true;
                }
            }
        }

        return false;
    }
}

/// <summary>
/// Positioning rule that spawns at or very close to the spawner's location.
/// </summary>
public class CenteredRule : IPositioningRule
{
    public string RuleName => "centered";
    public string Description => "Spawns entities at or very close to spawner location";

    public Point3D GetPosition(PositioningContext context)
    {
        var spawner = context.Spawner;
        var map = context.Map;

        if (spawner == null || map == null || map == Map.Internal)
        {
            return Point3D.Zero;
        }

        var spawnerLoc = spawner.Location;

        // Try spawner location first
        if (map.CanSpawnMobile(spawnerLoc.X, spawnerLoc.Y, spawnerLoc.Z))
        {
            return spawnerLoc;
        }

        // Try within 2 tiles
        for (var i = 0; i < 10; i++)
        {
            var x = spawnerLoc.X + Utility.RandomMinMax(-2, 2);
            var y = spawnerLoc.Y + Utility.RandomMinMax(-2, 2);
            var z = map.GetAverageZ(x, y);

            if (map.CanSpawnMobile(x, y, z))
            {
                return new Point3D(x, y, z);
            }
        }

        return Point3D.Zero;
    }
}

/// <summary>
/// Positioning rule that spawns entities around the perimeter of the home range.
/// </summary>
public class PerimeterRule : IPositioningRule
{
    public string RuleName => "perimeter";
    public string Description => "Spawns entities around the edge of home range";

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

        for (var i = 0; i < context.MaxAttempts; i++)
        {
            int x, y;

            // Pick a random side of the perimeter
            switch (Utility.Random(4))
            {
                case 0: // Top
                    {
                        x = spawnerLoc.X + Utility.RandomMinMax(-homeRange, homeRange);
                        y = spawnerLoc.Y - homeRange;
                        break;
                    }
                case 1: // Bottom
                    {
                        x = spawnerLoc.X + Utility.RandomMinMax(-homeRange, homeRange);
                        y = spawnerLoc.Y + homeRange;
                        break;
                    }
                case 2:  // Left
                    {
                        x = spawnerLoc.X - homeRange;
                        y = spawnerLoc.Y + Utility.RandomMinMax(-homeRange, homeRange);
                        break;
                    }
                default: // Right
                    {
                        x = spawnerLoc.X + homeRange;
                        y = spawnerLoc.Y + Utility.RandomMinMax(-homeRange, homeRange);
                        break;
                    }
            }

            var z = map.GetAverageZ(x, y);

            if (map.CanSpawnMobile(x, y, z))
            {
                return new Point3D(x, y, z);
            }
        }

        return Point3D.Zero;
    }
}
