namespace Server.Engines.ModernSpawner.Scripting;

/// <summary>
/// Interface for the script compilation and execution engine.
/// </summary>
public interface IScriptEngine
{
    /// <summary>
    /// Compiles a script string into an executable form.
    /// </summary>
    /// <param name="script">The script source code.</param>
    /// <returns>A compiled script that can be executed multiple times.</returns>
    CompiledScript Compile(string script);

    /// <summary>
    /// Executes a compiled script in the given context.
    /// </summary>
    /// <param name="script">The compiled script to execute.</param>
    /// <param name="context">The execution context containing target entity and spawner.</param>
    void Execute(CompiledScript script, ScriptContext context);

    /// <summary>
    /// Compiles and immediately executes a script.
    /// </summary>
    /// <param name="script">The script source code.</param>
    /// <param name="context">The execution context.</param>
    void ExecuteImmediate(string script, ScriptContext context);
}
