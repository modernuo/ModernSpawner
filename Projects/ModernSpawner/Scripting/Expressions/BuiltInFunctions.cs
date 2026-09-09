using System;
using System.Collections.Generic;
using Server.Items;

namespace Server.Engines.ModernSpawner.Scripting.Expressions;

/// <summary>
/// Implementation of built-in functions for the expression language.
/// </summary>
public static class BuiltInFunctions
{
    /// <summary>
    /// Returns true if the function returns a numeric value.
    /// </summary>
    public static bool IsNumericFunction(string functionName)
    {
        return functionName.ToLowerInvariant() switch
        {
            "random" or "gamehour" or "gameminute" or "playersnearby" or "mobilesnearby"
                or "min" or "max" or "abs" or "floor" or "ceiling" or "round" or "length" => true,
            _ => false
        };
    }

    /// <summary>
    /// Returns true if the function returns a boolean value.
    /// </summary>
    public static bool IsBooleanFunction(string functionName)
    {
        return functionName.ToLowerInvariant() switch
        {
            "isnight" or "isday" or "hasitem" or "hasskill" or "inregion" => true,
            _ => false
        };
    }

    /// <summary>
    /// Returns true if the function returns a string value.
    /// </summary>
    public static bool IsStringFunction(string functionName)
    {
        return functionName.ToLowerInvariant() switch
        {
            "lower" or "upper" => true,
            _ => false
        };
    }

    /// <summary>
    /// Invokes a built-in function and returns the result as a double.
    /// Use this for numeric functions to avoid boxing.
    /// </summary>
    public static double InvokeNumeric(string functionName, List<ExpressionNode> arguments, ScriptContext context)
    {
        return functionName.ToLowerInvariant() switch
        {
            "random" => Random(arguments, context),
            "gamehour" => GameHour(context),
            "gameminute" => GameMinute(context),
            "playersnearby" => PlayersNearby(arguments, context),
            "mobilesnearby" => MobilesNearby(arguments, context),
            "min" => Min(arguments, context),
            "max" => Max(arguments, context),
            "abs" => Abs(arguments, context),
            "floor" => Floor(arguments, context),
            "ceiling" => Ceiling(arguments, context),
            "round" => Round(arguments, context),
            "length" => Length(arguments, context),
            _ => throw new InvalidOperationException($"Function {functionName} is not numeric")
        };
    }

    /// <summary>
    /// Invokes a built-in function and returns the result as a boolean.
    /// Use this for boolean functions to avoid boxing.
    /// </summary>
    public static bool InvokeBoolean(string functionName, List<ExpressionNode> arguments, ScriptContext context)
    {
        return functionName.ToLowerInvariant() switch
        {
            "isnight" => IsNight(context),
            "isday" => IsDay(context),
            "hasitem" => HasItem(arguments, context),
            "hasskill" => HasSkill(arguments, context),
            "inregion" => InRegion(arguments, context),
            _ => throw new InvalidOperationException($"Function {functionName} is not boolean")
        };
    }

    public static string InvokeString(string functionName, List<ExpressionNode> arguments, ScriptContext context)
    {
        return functionName.ToLowerInvariant() switch
        {
            "lower" => Lower(arguments, context),
            "upper" => Upper(arguments, context),
            _ => throw new InvalidOperationException($"Function {functionName} is not string")
        };
    }

    /// <summary>
    /// Invokes a built-in function by name.
    /// </summary>
    public static object Invoke(string functionName, List<ExpressionNode> arguments, ScriptContext context)
    {
        return functionName.ToLowerInvariant() switch
        {
            // Random functions
            "randomfrom" => RandomFrom(arguments, context),
            _ => throw new InvalidOperationException($"Unknown function: {functionName}")
        };
    }

    /// <summary>
    /// random(min, max) - Returns a random integer between min and max (inclusive).
    /// </summary>
    private static int Random(List<ExpressionNode> args, ScriptContext context)
    {
        if (args.Count < 2)
        {
            throw new ArgumentException("random() requires 2 arguments: min, max");
        }

        var min = (int)args[0].EvaluateNumber(context);
        var max = (int)args[1].EvaluateNumber(context);

        return Utility.RandomMinMax(min, max);
    }

    /// <summary>
    /// randomFrom(value1, value2, ...) - Returns a random value from the provided list.
    /// </summary>
    private static object RandomFrom(List<ExpressionNode> args, ScriptContext context)
    {
        if (args.Count == 0)
        {
            throw new ArgumentException("randomFrom() requires at least 1 argument");
        }

        var index = Utility.Random(args.Count);
        return args[index].Evaluate(context);
    }

    /// <summary>
    /// isNight() - Returns true if it's currently nighttime in the game.
    /// </summary>
    private static bool IsNight(ScriptContext context)
    {
        var hour = GetGameHour(context);
        // Night is between 8 PM (20:00) and 6 AM (06:00)
        return hour is >= 20 or < 6;
    }

    /// <summary>
    /// isDay() - Returns true if it's currently daytime in the game.
    /// </summary>
    private static bool IsDay(ScriptContext context)
    {
        var hour = GetGameHour(context);
        // Day is between 6 AM (06:00) and 8 PM (20:00)
        return hour is >= 6 and < 20;
    }

    /// <summary>
    /// gameHour() - Returns the current game hour (0-23).
    /// </summary>
    private static int GameHour(ScriptContext context) => GetGameHour(context);

    /// <summary>
    /// gameMinute() - Returns the current game minute (0-59).
    /// </summary>
    private static int GameMinute(ScriptContext context)
    {
        var spawner = context.Spawner;
        var map = spawner?.Map ?? Map.Felucca;
        var x = spawner?.X ?? 0;
        var y = spawner?.Y ?? 0;
        Clock.GetTime(map, x, y, out int _, out int min);
        return min;
    }

    private static int GetGameHour(ScriptContext context)
    {
        var spawner = context.Spawner;
        var map = spawner?.Map ?? Map.Felucca;
        var x = spawner?.X ?? 0;
        var y = spawner?.Y ?? 0;
        Clock.GetTime(map, x, y, out int hour, out int _);
        return hour;
    }

    /// <summary>
    /// playersNearby(range) - Returns the count of players within range of the spawner.
    /// </summary>
    private static int PlayersNearby(List<ExpressionNode> args, ScriptContext context)
    {
        var range = args.Count > 0 ? (int)args[0].EvaluateNumber(context) : 20;
        var spawner = context.Spawner;

        if (spawner?.Map == null || spawner.Map == Map.Internal)
        {
            return 0;
        }

        var count = 0;
        foreach (var mob in spawner.Map.GetMobilesInRange(spawner.Location, range))
        {
            if (mob.Player && mob.Alive && !mob.Hidden)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// mobilesNearby(range) - Returns the count of all mobiles within range of the spawner.
    /// </summary>
    private static int MobilesNearby(List<ExpressionNode> args, ScriptContext context)
    {
        var range = args.Count > 0 ? (int)args[0].EvaluateNumber(context) : 20;
        var spawner = context.Spawner;

        if (spawner?.Map == null || spawner.Map == Map.Internal)
        {
            return 0;
        }

        var count = 0;
        foreach (var mob in spawner.Map.GetMobilesInRange(spawner.Location, range))
        {
            if (mob.Alive)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// hasItem(mobile, itemType) - Checks if a mobile has an item of the specified type.
    /// </summary>
    private static bool HasItem(List<ExpressionNode> args, ScriptContext context)
    {
        if (args.Count < 2)
        {
            throw new ArgumentException("hasItem() requires 2 arguments: mobile reference, item type name");
        }

        var mobileRef = args[0].Evaluate(context);
        var itemTypeName = args[1].Evaluate(context)?.ToString();

        if (mobileRef is not Mobile mobile || string.IsNullOrEmpty(itemTypeName))
        {
            return false;
        }

        var itemType = ScriptHelpers.FindType(itemTypeName);
        return itemType != null && mobile.Backpack?.FindItemByType(itemType) != null;
    }

    /// <summary>
    /// hasSkill(mobile, skillName, minValue) - Checks if a mobile has a skill at or above the specified value.
    /// </summary>
    private static bool HasSkill(List<ExpressionNode> args, ScriptContext context)
    {
        if (args.Count < 3)
        {
            throw new ArgumentException("hasSkill() requires 3 arguments: mobile reference, skill name, minimum value");
        }

        var mobileRef = args[0].Evaluate(context);
        var skillName = args[1].Evaluate(context)?.ToString();
        var minValue = args[2].EvaluateNumber(context);

        if (mobileRef is not Mobile mobile || string.IsNullOrEmpty(skillName))
        {
            return false;
        }

        // Try to find the skill
        if (!Enum.TryParse<SkillName>(skillName, true, out var skill))
        {
            return false;
        }

        return mobile.Skills[skill].Value >= minValue;
    }

    /// <summary>
    /// inRegion(regionName) - Checks if the spawner is in a region with the specified name.
    /// </summary>
    private static bool InRegion(List<ExpressionNode> args, ScriptContext context)
    {
        if (args.Count < 1)
        {
            throw new ArgumentException("inRegion() requires 1 argument: region name");
        }

        var regionName = args[0].Evaluate(context)?.ToString();
        var spawner = context.Spawner;

        if (spawner?.Map == null || string.IsNullOrEmpty(regionName))
        {
            return false;
        }

        var region = Region.Find(spawner.Location, spawner.Map);

        while (region != null)
        {
            if (region.Name?.Contains(regionName, StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }
            region = region.Parent;
        }

        return false;
    }

    private static double Min(List<ExpressionNode> args, ScriptContext context)
    {
        if (args.Count < 2)
        {
            throw new ArgumentException("min() requires at least 2 arguments");
        }

        var result = args[0].EvaluateNumber(context);
        for (var i = 1; i < args.Count; i++)
        {
            result = Math.Min(result, args[i].EvaluateNumber(context));
        }
        return result;
    }

    private static double Max(List<ExpressionNode> args, ScriptContext context)
    {
        if (args.Count < 2)
        {
            throw new ArgumentException("max() requires at least 2 arguments");
        }

        var result = args[0].EvaluateNumber(context);
        for (var i = 1; i < args.Count; i++)
        {
            result = Math.Max(result, args[i].EvaluateNumber(context));
        }
        return result;
    }

    private static double Abs(List<ExpressionNode> args, ScriptContext context)
    {
        if (args.Count < 1)
        {
            throw new ArgumentException("abs() requires 1 argument");
        }
        return Math.Abs(args[0].EvaluateNumber(context));
    }

    private static double Floor(List<ExpressionNode> args, ScriptContext context)
    {
        if (args.Count < 1)
        {
            throw new ArgumentException("floor() requires 1 argument");
        }
        return Math.Floor(args[0].EvaluateNumber(context));
    }

    private static double Ceiling(List<ExpressionNode> args, ScriptContext context)
    {
        if (args.Count < 1)
        {
            throw new ArgumentException("ceiling() requires 1 argument");
        }
        return Math.Ceiling(args[0].EvaluateNumber(context));
    }

    private static double Round(List<ExpressionNode> args, ScriptContext context)
    {
        if (args.Count < 1)
        {
            throw new ArgumentException("round() requires 1 argument");
        }
        return Math.Round(args[0].EvaluateNumber(context));
    }

    private static string Lower(List<ExpressionNode> args, ScriptContext context)
    {
        if (args.Count < 1)
        {
            throw new ArgumentException("lower() requires 1 argument");
        }
        return args[0].Evaluate(context)?.ToString()?.ToLowerInvariant() ?? string.Empty;
    }

    private static string Upper(List<ExpressionNode> args, ScriptContext context)
    {
        if (args.Count < 1)
        {
            throw new ArgumentException("upper() requires 1 argument");
        }
        return args[0].Evaluate(context)?.ToString()?.ToUpperInvariant() ?? string.Empty;
    }

    private static int Length(List<ExpressionNode> args, ScriptContext context)
    {
        if (args.Count < 1)
        {
            throw new ArgumentException("length() requires 1 argument");
        }
        return args[0].Evaluate(context)?.ToString()?.Length ?? 0;
    }
}

/// <summary>
/// Helper methods for script execution.
/// </summary>
internal static class ScriptHelpers
{
    private static readonly Dictionary<string, Type> TypeCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Finds a type by name, checking common assemblies.
    /// </summary>
    public static Type FindType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
        {
            return null;
        }

        if (TypeCache.TryGetValue(typeName, out var cached))
        {
            return cached;
        }

        // Try Server assembly first
        var type = Type.GetType($"Server.{typeName}") ??
                   Type.GetType($"Server.Items.{typeName}") ??
                   Type.GetType($"Server.Mobiles.{typeName}") ??
                   Type.GetType(typeName);

        // Search all loaded assemblies
        if (type == null)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType($"Server.{typeName}") ??
                       assembly.GetType($"Server.Items.{typeName}") ??
                       assembly.GetType($"Server.Mobiles.{typeName}") ??
                       assembly.GetType(typeName);

                if (type != null)
                {
                    break;
                }
            }
        }

        if (type != null)
        {
            TypeCache[typeName] = type;
        }

        return type;
    }
}
