using System;
using Server.Text;

namespace Server.Engines.ModernSpawner.Triggers;

/// <summary>
/// Trigger that activates when a spawned entity from this spawner is killed.
/// Useful for respawn-on-kill mechanics or boss encounter progression.
/// Definition: <c>kill:&lt;requiredKills&gt;:&lt;requireAllDead&gt;:&lt;resetOnTrigger&gt;:&lt;filterType&gt;:&lt;requirePlayerKiller&gt;:&lt;cooldownSeconds&gt;</c>
/// plus the shared <see cref="TriggerTokens" />.
/// </summary>
public class KillTrigger : TriggerBase
{
    // kill:requiredKills:requireAllDead:resetOnTrigger:filterType:requirePlayerKiller:cooldownSeconds -
    // tokens start after these, so a filter type named "Wake" stays a filter type.
    private const int PositionalArity = 7;

    /// <inheritdoc />
    public override string TriggerType => "kill";

    /// <inheritdoc />
    public override TriggerKind Kind => TriggerKind.Event;

    /// <summary>
    /// The number of kills required before triggering.
    /// Set to 1 for immediate trigger on any kill.
    /// </summary>
    public int RequiredKills { get; set; } = 1;

    /// <summary>
    /// Whether to trigger only when all spawned entities are dead.
    /// </summary>
    public bool RequireAllDead { get; set; }

    /// <summary>
    /// Whether to reset the kill count after triggering.
    /// </summary>
    public bool ResetOnTrigger { get; set; } = true;

    /// <summary>
    /// Optional type name to filter which kills count.
    /// If empty, all kills count.
    /// </summary>
    public string FilterType { get; set; }

    /// <summary>
    /// Whether the killer must be a player.
    /// </summary>
    public bool RequirePlayerKiller { get; set; }

    /// <summary>Creates a trigger with the documented defaults.</summary>
    public KillTrigger() => Cooldown = TimeSpan.FromSeconds(5);

    /// <summary>Creates a kill trigger.</summary>
    /// <param name="requiredKills">Kills needed before the trigger fires; at least one.</param>
    /// <param name="requireAllDead">Whether the spawner must hold no live spawns when it fires.</param>
    public KillTrigger(int requiredKills, bool requireAllDead = false) : this()
    {
        RequiredKills = Math.Max(1, requiredKills);
        RequireAllDead = requireAllDead;
    }

    /// <summary>
    /// Whether this kill passes the trigger's filters at all, and therefore counts toward
    /// <see cref="RequiredKills" /> whether or not the resulting event is accepted.
    /// </summary>
    /// <param name="context">The kill being dispatched.</param>
    /// <returns>True when the kill counts.</returns>
    public bool CountsKill(in TriggerContext context)
    {
        if (context.KilledEntity == null)
        {
            return false;
        }

        // Check cooldown
        if (!CooldownElapsed())
        {
            return false;
        }

        // Check type filter
        if (!string.IsNullOrEmpty(FilterType))
        {
            var entityType = context.KilledEntity.GetType();
            if (!entityType.Name.Equals(FilterType, StringComparison.OrdinalIgnoreCase) &&
                !entityType.FullName?.Equals(FilterType, StringComparison.OrdinalIgnoreCase) == true)
            {
                return false;
            }
        }

        // Check player killer requirement
        if (RequirePlayerKiller)
        {
            if (context.TriggeringMobile == null || !context.TriggeringMobile.Player)
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Pure: the counter advance and the <see cref="ResetOnTrigger" /> reset belong to the spawner's
    /// acceptance path, so this reports whether <em>this</em> kill reaches the threshold by reading
    /// <see cref="TriggerRuntimeState.KillCount" /> and adding the kill in hand.
    /// </remarks>
    public override bool Evaluate(in TriggerContext context)
    {
        if (!CountsKill(in context))
        {
            return false;
        }

        // Check if all dead is required. This is the only part of a kill trigger that needs the
        // spawner, so an unbound trigger fails it rather than failing every kill.
        if (RequireAllDead)
        {
            var spawner = Spawner;
            if (spawner == null || spawner.Spawned.Count > 0)
            {
                return false;
            }
        }

        var state = State;
        var reached = state == null ? 1 : state.KillCount + 1;
        return reached >= RequiredKills;
    }

    /// <summary>
    /// Counts one kill that passed <see cref="CountsKill" />, and clears the counter when the kill was
    /// accepted and <see cref="ResetOnTrigger" /> is set.
    /// </summary>
    /// <remarks>
    /// Interim: a kill advances the counter whether or not the spawner accepts the resulting event, and
    /// D2's acceptance path (<c>ModernSpawner.RequestCycle</c>) is where that advance belongs. Until
    /// that lands, the kill dispatch calls this so multi-kill thresholds keep working.
    /// </remarks>
    /// <param name="accepted">Whether <see cref="Evaluate" /> matched for this kill.</param>
    public void AdvanceKillCount(bool accepted)
    {
        var state = State;
        if (state == null)
        {
            return;
        }

        state.KillCount++;

        if (accepted && ResetOnTrigger)
        {
            state.KillCount = 0;
        }
    }

    /// <summary>
    /// Resets the kill counter. Call this to manually reset progress.
    /// </summary>
    public void ResetKillCount()
    {
        var state = State;
        if (state != null)
        {
            state.KillCount = 0;
        }
    }

    /// <inheritdoc />
    public override string Serialize()
    {
        // Format: kill:requiredKills:requireAllDead:resetOnTrigger:filterType:requirePlayerKiller:cooldownSeconds
        var filter = string.IsNullOrEmpty(FilterType) ? "any" : FilterType;

        var sb = ValueStringBuilder.CreateMT();
        try
        {
            sb.Append($"kill:{RequiredKills}:{RequireAllDead}:{ResetOnTrigger}:{filter}:{RequirePlayerKiller}:{(int)Cooldown.TotalSeconds}");
            AppendTokens(ref sb);
            return sb.ToString();
        }
        finally
        {
            sb.Dispose();
        }
    }

    /// <summary>Parses a kill trigger definition.</summary>
    /// <param name="definition">The definition text.</param>
    /// <returns>The parsed trigger.</returns>
    public static KillTrigger Parse(string definition)
    {
        var wake = false;
        var mode = CycleMode.Now;
        string when = null;
        var positional = TriggerTokens.Strip(definition, PositionalArity, ref wake, ref mode, ref when);

        var parts = positional.Split(':');
        var trigger = new KillTrigger();

        if (parts.Length > 1 && int.TryParse(parts[1], out var requiredKills))
        {
            trigger.RequiredKills = Math.Max(1, requiredKills);
        }

        if (parts.Length > 2 && bool.TryParse(parts[2], out var requireAllDead))
        {
            trigger.RequireAllDead = requireAllDead;
        }

        if (parts.Length > 3 && bool.TryParse(parts[3], out var resetOnTrigger))
        {
            trigger.ResetOnTrigger = resetOnTrigger;
        }

        if (parts.Length > 4)
        {
            var filter = parts[4];
            if (!string.Equals(filter, "any", StringComparison.OrdinalIgnoreCase))
            {
                trigger.FilterType = filter;
            }
        }

        if (parts.Length > 5 && bool.TryParse(parts[5], out var requirePlayerKiller))
        {
            trigger.RequirePlayerKiller = requirePlayerKiller;
        }

        if (parts.Length > 6 && int.TryParse(parts[6], out var cooldown))
        {
            trigger.Cooldown = TimeSpan.FromSeconds(cooldown);
        }

        trigger.ApplyTokens(wake, mode, when);
        return trigger;
    }
}
