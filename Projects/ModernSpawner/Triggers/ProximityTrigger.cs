using System;
using Server.Logging;
using Server.Text;

namespace Server.Engines.ModernSpawner.Triggers;

/// <summary>
/// Trigger that activates when a mobile comes within range of the spawner.
/// Definition: <c>proximity:&lt;range&gt;:&lt;playersOnly&gt;:&lt;requireLos&gt;:&lt;cooldownSeconds&gt;:&lt;minAccess&gt;</c>
/// plus the shared <see cref="TriggerTokens" />.
/// </summary>
public class ProximityTrigger : TriggerBase
{
    private static readonly ILogger Logger = LogFactory.GetLogger(typeof(ProximityTrigger));

    // proximity:range:playersOnly:requireLos:cooldownSeconds:minAccess - tokens start after these.
    private const int PositionalArity = 6;

    /// <inheritdoc />
    public override string TriggerType => "proximity";

    /// <inheritdoc />
    public override TriggerKind Kind => TriggerKind.Event;

    private int _range = 8;

    /// <summary>
    /// The range within which the mobile must be to trigger. Clamped to
    /// <see cref="Core.GlobalMaxUpdateRange" />: movement is dispatched to an item through the sectors
    /// around it, and nothing outside that radius ever reaches <see cref="ModernSpawner.OnMovement" />,
    /// so a larger value would read as a trigger that silently never fires.
    /// </summary>
    public int Range
    {
        get => _range;
        set => _range = ClampRange(value);
    }

    /// <summary>
    /// Whether the trigger requires line of sight.
    /// </summary>
    public bool RequireLineOfSight { get; set; }

    /// <summary>
    /// Whether the trigger only activates for players (not NPCs).
    /// </summary>
    public bool PlayersOnly { get; set; } = true;

    /// <summary>
    /// Minimum access level required to trigger (for staff-only spawners).
    /// </summary>
    public AccessLevel MinAccessLevel { get; set; } = AccessLevel.Player;

    /// <summary>Creates a trigger with the documented defaults.</summary>
    public ProximityTrigger() => Cooldown = TimeSpan.FromSeconds(5);

    /// <summary>Creates a proximity trigger.</summary>
    /// <param name="range">Range in tiles, clamped to <see cref="Core.GlobalMaxUpdateRange" />.</param>
    /// <param name="playersOnly">Whether only players may trigger it.</param>
    /// <param name="requireLos">Whether the mobile must have line of sight to the spawner.</param>
    public ProximityTrigger(int range, bool playersOnly = true, bool requireLos = false) : this()
    {
        Range = range;
        PlayersOnly = playersOnly;
        RequireLineOfSight = requireLos;
    }

    private static int ClampRange(int range)
    {
        if (range <= Core.GlobalMaxUpdateRange)
        {
            return range;
        }

        // Extended (beyond the global update range) proximity needs an area-movement subscription
        // ModernUO does not expose yet, so the definition is clamped rather than quietly ignored.
        Logger.Warning(
            "Proximity trigger range {Range} exceeds the global update range {Max} and was clamped; movement is only dispatched within {Max} tiles.",
            range,
            Core.GlobalMaxUpdateRange,
            Core.GlobalMaxUpdateRange
        );

        return Core.GlobalMaxUpdateRange;
    }

    /// <inheritdoc />
    public override bool Evaluate(in TriggerContext context)
    {
        var spawner = Spawner;
        if (context.TriggeringMobile == null || spawner == null)
        {
            return false;
        }

        var mobile = context.TriggeringMobile;

        // Check access level
        if (mobile.AccessLevel < MinAccessLevel)
        {
            return false;
        }

        // Check if players only
        if (PlayersOnly && !mobile.Player)
        {
            return false;
        }

        // Check range
        if (!mobile.InRange(spawner.Location, Range))
        {
            return false;
        }

        // Check map
        if (mobile.Map != spawner.Map)
        {
            return false;
        }

        // Line of sight, not visibility: Mobile.CanSee(Item) ends in item.Visible, and a spawner is
        // Visible = false, so CanSee could never pass here for a player.
        if (RequireLineOfSight && !mobile.InLOS(spawner))
        {
            return false;
        }

        // Cooldown is a read: the spawner advances it when it accepts the event.
        return CooldownElapsed();
    }

    /// <inheritdoc />
    public override string Serialize()
    {
        // Format: proximity:range:playersOnly:requireLos:cooldownSeconds:minAccess
        var sb = ValueStringBuilder.CreateMT();
        try
        {
            sb.Append($"proximity:{Range}:{PlayersOnly}:{RequireLineOfSight}:{(int)Cooldown.TotalSeconds}:{(int)MinAccessLevel}");
            AppendTokens(ref sb);
            return sb.ToString();
        }
        finally
        {
            sb.Dispose();
        }
    }

    /// <summary>Parses a proximity trigger definition.</summary>
    /// <param name="definition">The definition text.</param>
    /// <returns>The parsed trigger.</returns>
    public static ProximityTrigger Parse(string definition)
    {
        var wake = false;
        var mode = CycleMode.Now;
        string when = null;
        var positional = TriggerTokens.Strip(definition, PositionalArity, ref wake, ref mode, ref when);

        // Skip the "proximity:" prefix
        var parts = positional.Split(':');
        var trigger = new ProximityTrigger();

        if (parts.Length > 1 && int.TryParse(parts[1], out var range))
        {
            trigger.Range = range;
        }

        if (parts.Length > 2 && bool.TryParse(parts[2], out var playersOnly))
        {
            trigger.PlayersOnly = playersOnly;
        }

        if (parts.Length > 3 && bool.TryParse(parts[3], out var requireLos))
        {
            trigger.RequireLineOfSight = requireLos;
        }

        if (parts.Length > 4 && int.TryParse(parts[4], out var cooldown))
        {
            trigger.Cooldown = TimeSpan.FromSeconds(cooldown);
        }

        if (parts.Length > 5 && int.TryParse(parts[5], out var accessLevel))
        {
            trigger.MinAccessLevel = (AccessLevel)accessLevel;
        }

        trigger.ApplyTokens(wake, mode, when);
        return trigger;
    }
}
