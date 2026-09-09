using System;
using System.Collections.Generic;
using Server.Engines.ModernSpawner.Scripting.Ast;

namespace Server.Engines.ModernSpawner.Scripting;

/// <summary>
/// A compiled script containing an AST that can be executed multiple times.
/// </summary>
public class CompiledScript
{
    /// <summary>
    /// Unique serial identifier for this script. Serial.Zero means unregistered.
    /// </summary>
    public Serial Serial { get; internal set; }

    /// <summary>
    /// Human-readable name for this script (used for registration/lookup).
    /// </summary>
    public string Name { get; internal set; }

    /// <summary>
    /// The root node of the compiled AST.
    /// </summary>
    public ScriptNode Root { get; }

    /// <summary>
    /// The original source code (for debugging/display purposes).
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Whether this script compiled successfully.
    /// </summary>
    public bool IsValid => Root != null && CompileErrors.Count == 0;

    /// <summary>
    /// Any errors that occurred during compilation.
    /// </summary>
    public IReadOnlyList<string> CompileErrors { get; }

    public CompiledScript(string source, ScriptNode root, IReadOnlyList<string> errors = null)
    {
        Source = source;
        Root = root;
        CompileErrors = errors ?? Array.Empty<string>();
    }

    /// <summary>
    /// Creates a failed compilation result.
    /// </summary>
    public static CompiledScript Failed(string source, params string[] errors)
    {
        return new CompiledScript(source, null, errors);
    }

    /// <summary>
    /// Creates an empty/no-op script.
    /// </summary>
    public static CompiledScript Empty { get; } = new(string.Empty, new NoOpNode(), Array.Empty<string>());
}
