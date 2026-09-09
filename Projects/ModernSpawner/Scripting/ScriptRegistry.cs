using System.Collections.Generic;

namespace Server.Engines.ModernSpawner.Scripting;

/// <summary>
/// Global registry and persistence for spawner scripts.
/// Scripts are serialized once here and referenced by serial from spawners.
/// Priority 1 ensures scripts load before Items (priority 2) which include spawners.
/// </summary>
public class ScriptRegistry : GenericPersistence
{
    private static ScriptRegistry _instance;

    /// <summary>
    /// Scripts indexed by serial for O(1) lookup.
    /// </summary>
    private readonly Dictionary<Serial, CompiledScript> _bySerial = new();

    /// <summary>
    /// Scripts indexed by name for registration/lookup.
    /// </summary>
    private readonly Dictionary<string, CompiledScript> _byName = new();

    /// <summary>
    /// Next serial to assign. Serial.Zero is reserved for "no script".
    /// </summary>
    private uint _nextSerial = 1;

    public static void Configure()
    {
        _instance = new ScriptRegistry();
    }

    private ScriptRegistry() : base("SpawnerScripts", 1)
    {
    }

    /// <summary>
    /// Gets a script by serial. Returns null if serial is Zero or not found.
    /// </summary>
    public static CompiledScript Get(Serial serial)
    {
        if (serial == Serial.Zero)
        {
            return null;
        }

        _instance._bySerial.TryGetValue(serial, out var script);
        return script;
    }

    /// <summary>
    /// Gets a script by name. Returns null if not found.
    /// </summary>
    public static CompiledScript GetByName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        _instance._byName.TryGetValue(name, out var script);
        return script;
    }

    /// <summary>
    /// Registers a new script or returns existing one if name already registered.
    /// Returns the serial of the script.
    /// </summary>
    public static Serial Register(string name, string source)
    {
        if (string.IsNullOrEmpty(name))
        {
            return Serial.Zero;
        }

        // Check if already registered
        if (_instance._byName.TryGetValue(name, out var existing))
        {
            return existing.Serial;
        }

        // Compile and register
        var compiled = ScriptEngine.Instance.Compile(source);
        var serial = (Serial)_instance._nextSerial++;

        compiled.Serial = serial;
        compiled.Name = name;

        _instance._bySerial[serial] = compiled;
        _instance._byName[name] = compiled;

        return serial;
    }

    /// <summary>
    /// Gets or registers a script. If source differs from existing, updates the script.
    /// </summary>
    public static Serial GetOrRegister(string name, string source)
    {
        if (string.IsNullOrEmpty(name))
        {
            return Serial.Zero;
        }

        if (_instance._byName.TryGetValue(name, out var existing))
        {
            // If source changed, recompile
            if (existing.Source != source)
            {
                var recompiled = ScriptEngine.Instance.Compile(source);
                recompiled.Serial = existing.Serial;
                recompiled.Name = name;

                _instance._bySerial[existing.Serial] = recompiled;
                _instance._byName[name] = recompiled;
            }

            return existing.Serial;
        }

        return Register(name, source);
    }

    /// <summary>
    /// Checks if a script with the given serial exists.
    /// </summary>
    public static bool Exists(Serial serial) =>
        serial != Serial.Zero && _instance._bySerial.ContainsKey(serial);

    /// <summary>
    /// Gets all registered scripts.
    /// </summary>
    public static IEnumerable<CompiledScript> GetAll() => _instance._bySerial.Values;

    public override void Serialize(IGenericWriter writer)
    {
        writer.WriteEncodedInt(0); // version

        writer.WriteEncodedInt(_bySerial.Count);
        writer.Write(_nextSerial);

        foreach (var script in _bySerial.Values)
        {
            writer.Write((uint)script.Serial);
            writer.Write(script.Name);
            writer.Write(script.Source);
        }
    }

    public override void Deserialize(IGenericReader reader)
    {
        var version = reader.ReadEncodedInt();

        var count = reader.ReadEncodedInt();
        _nextSerial = reader.ReadUInt();

        for (var i = 0; i < count; i++)
        {
            var serial = (Serial)reader.ReadUInt();
            var name = reader.ReadString();
            var source = reader.ReadString();

            // Compile the script
            var compiled = ScriptEngine.Instance.Compile(source);
            compiled.Serial = serial;
            compiled.Name = name;

            _bySerial[serial] = compiled;

            if (!string.IsNullOrEmpty(name))
            {
                _byName[name] = compiled;
            }
        }
    }
}
