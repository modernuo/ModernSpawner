using System;
using Server.Engines.ModernSpawner.Scripting.Expressions;
using Server.Logging;
using Server.Text;

namespace Server.Engines.ModernSpawner.Triggers;

/// <summary>
/// The registration-bound state every trigger carries, so the six trigger classes only implement what is
/// actually different about them: their grammar and their <see cref="ITrigger.Evaluate" />.
/// </summary>
public abstract class TriggerBase : ITrigger
{
    private static readonly ILogger Logger = LogFactory.GetLogger(typeof(TriggerBase));

    /// <inheritdoc />
    public abstract string TriggerType { get; }

    /// <inheritdoc />
    public abstract TriggerKind Kind { get; }

    /// <inheritdoc />
    public Guid Id { get; set; }

    /// <inheritdoc />
    public int DefinitionIndex { get; set; } = -1;

    /// <inheritdoc />
    public TriggerRuntimeState State { get; set; }

    /// <inheritdoc />
    public bool Wake { get; set; }

    /// <inheritdoc />
    public CycleMode Mode { get; set; } = CycleMode.Now;

    /// <inheritdoc />
    public CompiledExpression When { get; private set; }

    /// <summary>
    /// The raw text of the <c>when:</c> token, kept so <see cref="Serialize" /> can write back exactly
    /// what was parsed, or null when the definition carried no condition.
    /// </summary>
    public string WhenSource { get; private set; }

    /// <inheritdoc />
    public TimeSpan Cooldown { get; set; }

    /// <summary>The spawner this trigger is registered on, or null while unbound.</summary>
    protected ModernSpawner Spawner { get; private set; }

    /// <inheritdoc />
    public abstract bool Evaluate(in TriggerContext context);

    /// <inheritdoc />
    public virtual void Activate(ModernSpawner spawner) => Spawner = spawner;

    /// <inheritdoc />
    public virtual void Deactivate() => Spawner = null;

    /// <inheritdoc />
    public abstract string Serialize();

    /// <summary>
    /// Applies the values <see cref="TriggerTokens.Strip" /> pulled out of a definition and compiles the
    /// <c>when:</c> expression once, at parse time. Nothing on a dispatch path ever compiles.
    /// </summary>
    /// <param name="wake">The <c>wake:</c> value.</param>
    /// <param name="mode">The <c>mode:</c> value.</param>
    /// <param name="when">The raw <c>when:</c> expression, or null.</param>
    protected void ApplyTokens(bool wake, CycleMode mode, string when)
    {
        Wake = wake;
        Mode = mode;
        WhenSource = string.IsNullOrEmpty(when) ? null : when;

        if (WhenSource == null)
        {
            When = null;
            return;
        }

        var compiled = ExpressionEngine.Instance.Compile(WhenSource);
        if (compiled?.IsValid == true)
        {
            When = compiled;
            return;
        }

        // A condition that will not compile would otherwise be a trigger that silently never fires:
        // an invalid expression evaluates to false on every event. Report it once, at parse time, and
        // let the trigger run unconditioned rather than dead.
        Logger.Warning(
            "Dropping the when: condition on a {TriggerType} trigger: {Expression} did not compile.",
            TriggerType,
            WhenSource
        );

        When = null;
        WhenSource = null;
    }

    /// <summary>
    /// Appends this trigger's tokens to a definition being serialized.
    /// </summary>
    /// <param name="sb">The buffer holding the positional part of the definition.</param>
    protected void AppendTokens(scoped ref ValueStringBuilder sb) =>
        TriggerTokens.Append(ref sb, Wake, Mode, WhenSource);

    /// <summary>
    /// Whether this trigger's cooldown has elapsed. An unbound trigger has no state to compare against
    /// and is always eligible; the advance itself belongs to the spawner's acceptance path, so this only
    /// ever reads.
    /// </summary>
    /// <returns>True when another event may be accepted.</returns>
    protected bool CooldownElapsed()
    {
        var state = State;
        return state == null || Core.Now >= state.CooldownUntil;
    }
}
