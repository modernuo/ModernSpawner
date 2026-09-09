using System;
using System.Buffers;
using System.Collections.Generic;
using Server.Engines.ModernSpawner.Scripting.Ast;

namespace Server.Engines.ModernSpawner.Scripting;

/// <summary>
/// Default implementation of the script engine.
/// Supports a simple command syntax similar to XmlSpawner but compiled to AST for performance.
/// </summary>
public class ScriptEngine : IScriptEngine
{
    /// <summary>
    /// Singleton instance for convenience.
    /// </summary>
    public static ScriptEngine Instance { get; } = new();

    /// <summary>
    /// Cache of compiled scripts to avoid re-parsing.
    /// </summary>
    private readonly Dictionary<string, CompiledScript> _cache = new();

    private static readonly SearchValues<char> CommandSeparators = SearchValues.Create([';', '\n']);

    public CompiledScript Compile(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return CompiledScript.Empty;
        }

        // Check cache
        if (_cache.TryGetValue(script, out var cached))
        {
            return cached;
        }

        var compiled = CompileInternal(script.AsSpan());
        _cache[script] = compiled;
        return compiled;
    }

    public void Execute(CompiledScript script, ScriptContext context)
    {
        if (script?.IsValid != true || context == null)
        {
            return;
        }

        try
        {
            script.Root.Execute(context);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Script execution error: {ex.Message}");
        }
    }

    public void ExecuteImmediate(string script, ScriptContext context)
    {
        var compiled = Compile(script);
        Execute(compiled, context);
    }

    private CompiledScript CompileInternal(ReadOnlySpan<char> script)
    {
        var errors = new List<string>();
        var nodes = new List<ScriptNode>();

        // Split by semicolons or newlines for multiple commands
        var remaining = script;
        while (!remaining.IsEmpty)
        {
            var separatorIndex = remaining.IndexOfAny(CommandSeparators);
            ReadOnlySpan<char> command;

            if (separatorIndex < 0)
            {
                command = remaining;
                remaining = [];
            }
            else
            {
                command = remaining[..separatorIndex];
                remaining = remaining[(separatorIndex + 1)..];
            }

            var trimmed = command.Trim();
            if (trimmed.IsEmpty)
            {
                continue;
            }

            var node = ParseCommand(trimmed, errors);
            if (node != null)
            {
                nodes.Add(node);
            }
        }

        if (errors.Count > 0)
        {
            return CompiledScript.Failed(script.ToString(), errors.ToArray());
        }

        var root = nodes.Count switch
        {
            0 => new NoOpNode(),
            1 => nodes[0],
            _ => new SequenceNode(nodes)
        };

        return new CompiledScript(script.ToString(), root);
    }

    private ScriptNode ParseCommand(ReadOnlySpan<char> command, List<string> errors)
    {
        // Format: COMMAND/arg1/arg2/...
        // Examples:
        //   SET/Hits/100
        //   SET/Name/A Fierce Orc
        //   SETVAR/myvar/123
        //   CANCEL

        var slashIndex = command.IndexOf('/');
        ReadOnlySpan<char> cmd;
        ReadOnlySpan<char> args;

        if (slashIndex < 0)
        {
            cmd = command;
            args = [];
        }
        else
        {
            cmd = command[..slashIndex];
            args = command[(slashIndex + 1)..];
        }

        if (cmd.Equals("SET", StringComparison.OrdinalIgnoreCase))
        {
            return ParseSetCommand(args, errors);
        }

        if (cmd.Equals("SETVAR", StringComparison.OrdinalIgnoreCase))
        {
            return ParseSetVarCommand(args, errors);
        }

        if (cmd.Equals("CANCEL", StringComparison.OrdinalIgnoreCase))
        {
            return new CancelSpawnNode();
        }

        if (cmd.Equals("MSG", StringComparison.OrdinalIgnoreCase))
        {
            return ParseMessageCommand(args, errors);
        }

        // Inter-spawner commands
        if (cmd.Equals("SPAWN", StringComparison.OrdinalIgnoreCase))
        {
            return ParseSpawnCommand(args, errors);
        }

        if (cmd.Equals("DESPAWN", StringComparison.OrdinalIgnoreCase))
        {
            return ParseDespawnCommand(args, errors);
        }

        if (cmd.Equals("GOTO", StringComparison.OrdinalIgnoreCase))
        {
            return ParseGotoCommand(args, errors);
        }

        if (cmd.Equals("ACTIVATE", StringComparison.OrdinalIgnoreCase))
        {
            return ParseActivateCommand(args, true, errors);
        }

        if (cmd.Equals("DEACTIVATE", StringComparison.OrdinalIgnoreCase))
        {
            return ParseActivateCommand(args, false, errors);
        }

        if (cmd.Equals("BROADCAST", StringComparison.OrdinalIgnoreCase))
        {
            return ParseBroadcastCommand(args, errors);
        }

        if (cmd.Equals("SOUND", StringComparison.OrdinalIgnoreCase))
        {
            return ParseSoundCommand(args, errors);
        }

        if (cmd.Equals("EFFECT", StringComparison.OrdinalIgnoreCase))
        {
            return ParseEffectCommand(args, errors);
        }

        errors.Add($"Unknown command: {cmd}");
        return null;
    }

    private SetPropertyNode ParseSetCommand(ReadOnlySpan<char> args, List<string> errors)
    {
        // SET/PropertyPath/Value -> args = "PropertyPath/Value"
        if (args.IsEmpty)
        {
            errors.Add("SET command requires property path and value: SET/PropertyName/Value");
            return null;
        }

        var slashIndex = args.IndexOf('/');
        if (slashIndex < 0)
        {
            errors.Add("SET command requires property path and value: SET/PropertyName/Value");
            return null;
        }

        var propertyPath = args[..slashIndex].ToString();
        var valueSpan = args[(slashIndex + 1)..];
        var value = ParseValue(valueSpan);

        return new SetPropertyNode(propertyPath, value);
    }

    private SetVariableNode ParseSetVarCommand(ReadOnlySpan<char> args, List<string> errors)
    {
        // SETVAR/varname/value -> args = "varname/value"
        if (args.IsEmpty)
        {
            errors.Add("SETVAR command requires variable name and value: SETVAR/name/value");
            return null;
        }

        var slashIndex = args.IndexOf('/');
        if (slashIndex < 0)
        {
            errors.Add("SETVAR command requires variable name and value: SETVAR/name/value");
            return null;
        }

        var varName = args[..slashIndex].ToString();
        var valueSpan = args[(slashIndex + 1)..];
        var value = ParseValue(valueSpan);

        return new SetVariableNode(varName, value);
    }

    private static SendMessageNode ParseMessageCommand(ReadOnlySpan<char> args, List<string> errors)
    {
        // MSG/message text -> args = "message text"
        if (args.IsEmpty)
        {
            errors.Add("MSG command requires message text: MSG/message");
            return null;
        }

        return new SendMessageNode(args.ToString());
    }

    private static ValueExpression ParseValue(ReadOnlySpan<char> valueSpan)
    {
        if (valueSpan.IsEmpty)
        {
            return new LiteralValue(null);
        }

        // Check for random range: {min,max}
        if (valueSpan[0] == '{' && valueSpan[^1] == '}')
        {
            var inner = valueSpan[1..^1];
            var commaIndex = inner.IndexOf(',');
            if (commaIndex > 0 &&
                int.TryParse(inner[..commaIndex], out var min) &&
                int.TryParse(inner[(commaIndex + 1)..], out var max))
            {
                return new RandomValue(min, max);
            }
        }

        // Check for variable reference: $varname
        if (valueSpan[0] == '$')
        {
            return new VariableReference(valueSpan[1..].ToString());
        }

        // Check for property reference: @source.property
        if (valueSpan[0] == '@')
        {
            var refSpan = valueSpan[1..];
            var dotIndex = refSpan.IndexOf('.');
            if (dotIndex > 0)
            {
                return new PropertyReference(refSpan[..dotIndex].ToString(), refSpan[(dotIndex + 1)..].ToString());
            }

            return new PropertyReference("target", refSpan.ToString());
        }

        // Try to parse as number
        if (int.TryParse(valueSpan, out var intVal))
        {
            return new LiteralValue(intVal);
        }

        if (double.TryParse(valueSpan, out var doubleVal))
        {
            return new LiteralValue(doubleVal);
        }

        // Check for boolean
        if (bool.TryParse(valueSpan, out var boolVal))
        {
            return new LiteralValue(boolVal);
        }

        // Default to string literal
        return new LiteralValue(valueSpan.ToString());
    }

    private static SpawnCommandNode ParseSpawnCommand(ReadOnlySpan<char> args, List<string> errors)
    {
        // SPAWN/spawnerName or SPAWN/spawnerName/entryIndex
        if (args.IsEmpty)
        {
            errors.Add("SPAWN command requires spawner identifier: SPAWN/spawnerName");
            return null;
        }

        var slashIndex = args.IndexOf('/');
        if (slashIndex < 0)
        {
            return new SpawnCommandNode(args.ToString());
        }

        var spawnerName = args[..slashIndex].ToString();
        var entryIndex = -1;

        var indexSpan = args[(slashIndex + 1)..];
        if (!indexSpan.IsEmpty)
        {
            int.TryParse(indexSpan, out entryIndex);
        }

        return new SpawnCommandNode(spawnerName, entryIndex);
    }

    private static DespawnCommandNode ParseDespawnCommand(ReadOnlySpan<char> args, List<string> errors)
    {
        // DESPAWN/spawnerName or DESPAWN/spawnerName/entryIndex or DESPAWN/spawnerName/entryIndex/count
        if (args.IsEmpty)
        {
            errors.Add("DESPAWN command requires spawner identifier: DESPAWN/spawnerName");
            return null;
        }

        var parts = args.ToString().Split('/');
        var spawnerName = parts[0];
        var entryIndex = -1;
        var count = -1;

        if (parts.Length > 1)
        {
            int.TryParse(parts[1], out entryIndex);
        }

        if (parts.Length > 2)
        {
            int.TryParse(parts[2], out count);
        }

        return new DespawnCommandNode(spawnerName, entryIndex, count);
    }

    private static GotoCommandNode ParseGotoCommand(ReadOnlySpan<char> args, List<string> errors)
    {
        // GOTO/x,y,z or GOTO/spawnerName or GOTO/x,y,z,mapName
        if (args.IsEmpty)
        {
            errors.Add("GOTO command requires location: GOTO/x,y,z or GOTO/spawnerName");
            return null;
        }

        return new GotoCommandNode(args.ToString());
    }

    private static ActivateSpawnerNode ParseActivateCommand(ReadOnlySpan<char> args, bool activate, List<string> errors)
    {
        // ACTIVATE/spawnerName or DEACTIVATE/spawnerName
        if (args.IsEmpty)
        {
            var cmd = activate ? "ACTIVATE" : "DEACTIVATE";
            errors.Add($"{cmd} command requires spawner identifier: {cmd}/spawnerName");
            return null;
        }

        return new ActivateSpawnerNode(args.ToString(), activate);
    }

    private static BroadcastNode ParseBroadcastCommand(ReadOnlySpan<char> args, List<string> errors)
    {
        // BROADCAST/message or BROADCAST/message/range or BROADCAST/message/range/hue
        if (args.IsEmpty)
        {
            errors.Add("BROADCAST command requires message: BROADCAST/message");
            return null;
        }

        Span<Range> splitRange = stackalloc Range[3];
        var parts = args.Split(splitRange, '/');
        var range = 10;
        var hue = 0x3B2;

        if (parts > 1)
        {
            int.TryParse(args[splitRange[1]], out range);
        }

        if (parts > 2)
        {
            int.TryParse(args[splitRange[2]], out hue);
        }

        return new BroadcastNode(args[splitRange[0]].ToString(), range, hue);
    }

    private static PlaySoundNode ParseSoundCommand(ReadOnlySpan<char> args, List<string> errors)
    {
        // SOUND/soundId or SOUND/soundId/range
        if (args.IsEmpty)
        {
            errors.Add("SOUND command requires sound ID: SOUND/soundId");
            return null;
        }

        Span<Range> splitRange = stackalloc Range[2];
        var parts = args.Split(splitRange, '/');
        if (!int.TryParse(args[splitRange[0]], out var soundId))
        {
            errors.Add("SOUND command requires numeric sound ID");
            return null;
        }

        var range = 10;
        if (parts > 1)
        {
            int.TryParse(args[splitRange[1]], out range);
        }

        return new PlaySoundNode(soundId, range);
    }

    private static PlayEffectNode ParseEffectCommand(ReadOnlySpan<char> args, List<string> errors)
    {
        // EFFECT/effectId or EFFECT/effectId/speed/duration
        if (args.IsEmpty)
        {
            errors.Add("EFFECT command requires effect ID: EFFECT/effectId");
            return null;
        }

        Span<Range> splitRange = stackalloc Range[2];
        var parts = args.Split(splitRange, '/');
        if (!int.TryParse(args[splitRange[0]], out var effectId))
        {
            errors.Add("EFFECT command requires numeric effect ID");
            return null;
        }

        var speed = 10;
        var duration = 30;

        if (parts > 1)
        {
            int.TryParse(args[splitRange[1]], out speed);
        }

        if (parts > 2)
        {
            int.TryParse(args[splitRange[2]], out duration);
        }

        return new PlayEffectNode(effectId, speed, duration);
    }

    /// <summary>
    /// Clears the script cache.
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
    }
}

/// <summary>
/// AST node that cancels the current spawn.
/// </summary>
public class CancelSpawnNode : ScriptNode
{
    public override void Execute(ScriptContext context)
    {
        context.CancelSpawn = true;
    }
}

/// <summary>
/// AST node that sets a context variable.
/// </summary>
public class SetVariableNode : ScriptNode
{
    public string VariableName { get; }
    public ValueExpression Value { get; }

    public SetVariableNode(string variableName, ValueExpression value)
    {
        VariableName = variableName;
        Value = value;
    }

    public override void Execute(ScriptContext context)
    {
        var value = Value.Evaluate(context);
        context.SetVariable(VariableName, value);
    }
}

/// <summary>
/// AST node that sends a message to the triggering mobile.
/// </summary>
public class SendMessageNode : ScriptNode
{
    public string Message { get; }

    public SendMessageNode(string message)
    {
        Message = message;
    }

    public override void Execute(ScriptContext context)
    {
        context.TriggeringMobile?.SendMessage(Message);
    }
}
