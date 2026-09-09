using System.Collections.Generic;

namespace Server.Engines.ModernSpawner.Scripting;

/// <summary>
/// Context for script execution containing the target entity, spawner, and execution state.
/// </summary>
public class ScriptContext
{
    /// <summary>
    /// The spawner that triggered this script execution.
    /// </summary>
    public ModernSpawner Spawner { get; }

    /// <summary>
    /// The spawner entry associated with this execution.
    /// </summary>
    public ModernSpawnerEntry Entry { get; }

    /// <summary>
    /// The entity being spawned or targeted by the script.
    /// </summary>
    public IEntity Target { get; set; }

    /// <summary>
    /// The mobile that triggered the spawn (if any, e.g., for proximity triggers).
    /// </summary>
    public Mobile TriggeringMobile { get; set; }

    /// <summary>
    /// Variables that can be set and read during script execution.
    /// </summary>
    public Dictionary<string, object> Variables { get; } = new();

    /// <summary>
    /// Numeric variables stored without boxing.
    /// </summary>
    private Dictionary<string, double> _numericVariables;

    /// <summary>
    /// Boolean variables stored without boxing.
    /// </summary>
    private Dictionary<string, bool> _booleanVariables;

    /// <summary>
    /// String variables stored directly.
    /// </summary>
    private Dictionary<string, string> _stringVariables;

    /// <summary>
    /// Gets or sets whether the spawn should be cancelled.
    /// Scripts can set this to true to prevent the entity from being placed in the world.
    /// </summary>
    public bool CancelSpawn { get; set; }

    /// <summary>
    /// Gets or sets custom spawn location override.
    /// If set, the entity will be placed at this location instead of the calculated position.
    /// </summary>
    public Point3D? LocationOverride { get; set; }

    public ScriptContext(ModernSpawner spawner, ModernSpawnerEntry entry = null, IEntity target = null)
    {
        Spawner = spawner;
        Entry = entry;
        Target = target;
    }

    /// <summary>
    /// Convenience constructor for spawner-level scripts.
    /// </summary>
    public ScriptContext(IEntity target, ModernSpawner spawner) : this(spawner, null, target)
    {
    }

    /// <summary>
    /// Gets a variable value, returning default if not set.
    /// </summary>
    public T GetVariable<T>(string name, T defaultValue = default)
    {
        if (Variables.TryGetValue(name, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        return defaultValue;
    }

    /// <summary>
    /// Sets a variable value.
    /// </summary>
    public void SetVariable(string name, object value)
    {
        Variables[name] = value;
    }

    /// <summary>
    /// Gets a numeric variable without boxing.
    /// </summary>
    public double GetNumericVariable(string name, double defaultValue = 0)
    {
        if (_numericVariables?.TryGetValue(name, out var value) == true)
        {
            return value;
        }

        // Fall back to regular variables
        if (Variables.TryGetValue(name, out var objValue))
        {
            return objValue switch
            {
                double d => d,
                int i => i,
                float f => f,
                long l => l,
                _ => defaultValue
            };
        }

        return defaultValue;
    }

    /// <summary>
    /// Sets a numeric variable without boxing.
    /// </summary>
    public void SetNumericVariable(string name, double value)
    {
        _numericVariables ??= new Dictionary<string, double>();
        _numericVariables[name] = value;
    }

    /// <summary>
    /// Gets a boolean variable without boxing.
    /// </summary>
    public bool GetBooleanVariable(string name, bool defaultValue = false)
    {
        if (_booleanVariables?.TryGetValue(name, out var value) == true)
        {
            return value;
        }

        // Fall back to regular variables
        if (Variables.TryGetValue(name, out var objValue) && objValue is bool b)
        {
            return b;
        }

        return defaultValue;
    }

    /// <summary>
    /// Sets a boolean variable without boxing.
    /// </summary>
    public void SetBooleanVariable(string name, bool value)
    {
        _booleanVariables ??= new Dictionary<string, bool>();
        _booleanVariables[name] = value;
    }

    /// <summary>
    /// Gets a string variable directly.
    /// </summary>
    public string GetStringVariable(string name, string defaultValue = "")
    {
        if (_stringVariables?.TryGetValue(name, out var value) == true)
        {
            return value;
        }

        // Fall back to regular variables
        if (Variables.TryGetValue(name, out var objValue) && objValue is string s)
        {
            return s;
        }

        return defaultValue;
    }

    /// <summary>
    /// Sets a string variable directly.
    /// </summary>
    public void SetStringVariable(string name, string value)
    {
        _stringVariables ??= new Dictionary<string, string>();
        _stringVariables[name] = value;
    }

    /// <summary>
    /// Checks if a variable exists in any storage.
    /// </summary>
    public bool HasVariable(string name)
    {
        return Variables.ContainsKey(name) ||
               _numericVariables?.ContainsKey(name) == true ||
               _booleanVariables?.ContainsKey(name) == true ||
               _stringVariables?.ContainsKey(name) == true;
    }
}
