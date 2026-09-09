using System.Collections.Generic;

namespace Server.Engines.ModernSpawner.Scripting.Ast;

/// <summary>
/// Base class for all AST nodes in the scripting system.
/// </summary>
public abstract class ScriptNode
{
    /// <summary>
    /// Executes this node in the given context.
    /// </summary>
    public abstract void Execute(ScriptContext context);
}

/// <summary>
/// A no-operation node that does nothing.
/// </summary>
public class NoOpNode : ScriptNode
{
    public override void Execute(ScriptContext context)
    {
        // No operation
    }
}

/// <summary>
/// A sequence of nodes to execute in order.
/// </summary>
public class SequenceNode : ScriptNode
{
    public IReadOnlyList<ScriptNode> Children { get; }

    public SequenceNode(IReadOnlyList<ScriptNode> children)
    {
        Children = children;
    }

    public SequenceNode(params ScriptNode[] children)
    {
        Children = children;
    }

    public override void Execute(ScriptContext context)
    {
        foreach (var child in Children)
        {
            child.Execute(context);

            // Check if spawn was cancelled
            if (context.CancelSpawn)
            {
                break;
            }
        }
    }
}
