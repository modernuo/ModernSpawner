using System;
using Server.Text;

namespace Server.Engines.ModernSpawner.Triggers;

/// <summary>Which attempt outcomes a skill trigger reacts to.</summary>
public enum SkillOutcome
{
    /// <summary>Both successes and failures.</summary>
    Any,

    /// <summary>Successful attempts only (the <c>+</c> suffix).</summary>
    Success,

    /// <summary>Failed attempts only (the <c>-</c> suffix).</summary>
    Failure
}

/// <summary>
/// Fires when a player uses a skill near the spawner.
/// Definition: <c>skill:&lt;Skill&gt;[+|-]:&lt;range&gt;:&lt;min&gt;[-&lt;max&gt;]:&lt;los&gt;:&lt;cooldownSeconds&gt;</c>
/// plus the shared <see cref="TriggerTokens" />.
/// <c>+</c> reacts to successes only, <c>-</c> to failures only; <c>Any</c> matches every skill.
/// Examples: <c>skill:Mining:10</c>, <c>skill:Magery+:5:50-90</c>, <c>skill:Any-:8</c>.
/// </summary>
public class SkillTrigger : TriggerBase
{
    // skill:skillName:range:valueWindow:requireLos:cooldownSeconds - tokens start after these.
    private const int PositionalArity = 6;

    /// <inheritdoc />
    public override string TriggerType => "skill";

    /// <inheritdoc />
    public override TriggerKind Kind => TriggerKind.Event;

    /// <summary>True when the trigger reacts to every skill; <see cref="TargetSkill"/> is then ignored.</summary>
    public bool AnySkill { get; }

    /// <summary>The skill that triggers this when <see cref="AnySkill"/> is false.</summary>
    public SkillName TargetSkill { get; }

    /// <summary>Which outcomes react.</summary>
    public SkillOutcome Outcome { get; }

    /// <summary>Range in tiles from the spawner.</summary>
    public int Range { get; }

    /// <summary>Minimum skill value required; 0 means no lower bound.</summary>
    public double MinSkillValue { get; }

    /// <summary>Maximum skill value allowed; -1 means no upper bound.</summary>
    public double MaxSkillValue { get; }

    /// <summary>Whether the user must have line of sight to the spawner.</summary>
    public bool RequireLOS { get; }

    /// <summary>Creates a skill trigger with the documented defaults.</summary>
    /// <param name="skill">The skill that fires it.</param>
    /// <param name="range">Range in tiles from the spawner.</param>
    /// <param name="minSkillValue">Minimum skill value required.</param>
    /// <param name="requireLOS">Whether the user needs line of sight to the spawner.</param>
    public SkillTrigger(SkillName skill, int range = 10, double minSkillValue = 0, bool requireLOS = false)
        : this(false, skill, SkillOutcome.Any, range, minSkillValue, -1, requireLOS, TimeSpan.FromSeconds(5))
    {
    }

    /// <summary>Creates a skill trigger with an explicit cooldown.</summary>
    /// <param name="skill">The skill that fires it.</param>
    /// <param name="range">Range in tiles from the spawner.</param>
    /// <param name="minSkillValue">Minimum skill value required.</param>
    /// <param name="requireLOS">Whether the user needs line of sight to the spawner.</param>
    /// <param name="cooldown">Minimum time between two accepted events.</param>
    public SkillTrigger(SkillName skill, int range, double minSkillValue, bool requireLOS, TimeSpan cooldown)
        : this(false, skill, SkillOutcome.Any, range, minSkillValue, -1, requireLOS, cooldown)
    {
    }

    /// <summary>Creates a fully specified skill trigger.</summary>
    /// <param name="anySkill">Whether every skill fires it.</param>
    /// <param name="skill">The skill that fires it when <paramref name="anySkill" /> is false.</param>
    /// <param name="outcome">Which attempt outcomes react.</param>
    /// <param name="range">Range in tiles from the spawner.</param>
    /// <param name="minSkillValue">Minimum skill value required.</param>
    /// <param name="maxSkillValue">Maximum skill value allowed, or -1 for no upper bound.</param>
    /// <param name="requireLOS">Whether the user needs line of sight to the spawner.</param>
    /// <param name="cooldown">Minimum time between two accepted events.</param>
    public SkillTrigger(
        bool anySkill,
        SkillName skill,
        SkillOutcome outcome,
        int range,
        double minSkillValue,
        double maxSkillValue,
        bool requireLOS,
        TimeSpan cooldown
    )
    {
        AnySkill = anySkill;
        TargetSkill = skill;
        Outcome = outcome;
        Range = Math.Max(1, range);
        MinSkillValue = minSkillValue;
        MaxSkillValue = maxSkillValue;
        RequireLOS = requireLOS;
        Cooldown = cooldown;
    }

    /// <inheritdoc />
    public override bool Evaluate(in TriggerContext context)
    {
        // A2: a stopped spawner still evaluates - a wake: trigger has to be able to start it, and a
        // non-wake one queues a cycle for its first tick. Only deletion takes a trigger out.
        var spawner = Spawner;
        if (spawner == null || spawner.Deleted)
        {
            return false;
        }

        // Skill, outcome and value window first: a skill attempt that this trigger does not react to
        // is the common case, and it must not pay for the cooldown, map, range and LOS checks below.
        if (!MatchesContext(context.UsedSkill, context.SkillValue, context.SkillSuccess))
        {
            return false;
        }

        // Cooldown is a read: the spawner advances it when it accepts the event.
        if (!CooldownElapsed())
        {
            return false;
        }

        var mobile = context.TriggeringMobile;
        if (mobile == null || mobile.Map != spawner.Map)
        {
            return false;
        }

        // Check range
        if (!mobile.InRange(spawner.Location, Range))
        {
            return false;
        }

        // Line of sight, not visibility: Mobile.CanSee(Item) ends in item.Visible, and a spawner is
        // Visible = false, so CanSee could never pass here for a player.
        return !RequireLOS || mobile.InLOS(spawner);
    }

    /// <summary>Whether this trigger reacts to <paramref name="skill"/> at all.</summary>
    /// <param name="skill">The skill attempted.</param>
    /// <returns>True when the trigger reacts to it.</returns>
    public bool MatchesSkill(SkillName skill) => AnySkill || skill == TargetSkill;

    /// <summary>The pure part of <see cref="Evaluate"/>: skill, outcome and value window.</summary>
    /// <param name="skill">The skill attempted.</param>
    /// <param name="value">The user's value in that skill.</param>
    /// <param name="success">Whether the attempt succeeded.</param>
    /// <returns>True when skill, outcome and value all match.</returns>
    public bool MatchesContext(SkillName skill, double value, bool success)
    {
        if (!MatchesSkill(skill))
        {
            return false;
        }

        if (Outcome == SkillOutcome.Success && !success || Outcome == SkillOutcome.Failure && success)
        {
            return false;
        }

        if (MinSkillValue > 0 && value < MinSkillValue)
        {
            return false;
        }

        return MaxSkillValue < 0 || value <= MaxSkillValue;
    }

    /// <inheritdoc />
    public override string Serialize()
    {
        var skill = AnySkill ? "Any" : TargetSkill.ToString();
        var suffix = Outcome switch
        {
            SkillOutcome.Success => "+",
            SkillOutcome.Failure => "-",
            _ => ""
        };

        var sb = ValueStringBuilder.CreateMT();
        try
        {
            sb.Append($"skill:{skill}{suffix}:{Range}:");

            if (MaxSkillValue < 0)
            {
                sb.Append($"{MinSkillValue}");
            }
            else
            {
                sb.Append($"{MinSkillValue}-{MaxSkillValue}");
            }

            sb.Append($":{RequireLOS}:{(int)Cooldown.TotalSeconds}");
            AppendTokens(ref sb);
            return sb.ToString();
        }
        finally
        {
            sb.Dispose();
        }
    }

    /// <summary>
    /// Parses a skill trigger definition string.
    /// Format: <c>skill:&lt;Skill&gt;[+|-]:&lt;range&gt;:&lt;min&gt;[-&lt;max&gt;]:&lt;los&gt;:&lt;cooldownSeconds&gt;</c>.
    /// </summary>
    /// <param name="definition">The definition text.</param>
    /// <returns>The parsed trigger, or null when the definition is malformed.</returns>
    public static SkillTrigger Parse(string definition)
    {
        if (string.IsNullOrEmpty(definition))
        {
            return null;
        }

        var wake = false;
        var mode = CycleMode.Now;
        string when = null;
        var positional = TriggerTokens.Strip(definition, PositionalArity, ref wake, ref mode, ref when);

        var parts = positional.Split(':');
        if (parts.Length < 2)
        {
            return null;
        }

        // Skip "skill" prefix if present
        var startIndex = parts[0].Equals("skill", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        if (parts.Length <= startIndex)
        {
            return null;
        }

        // Parse skill name, optional +/- outcome suffix
        var skillName = parts[startIndex];
        var outcome = SkillOutcome.Any;

        if (skillName.EndsWith('+'))
        {
            outcome = SkillOutcome.Success;
            skillName = skillName[..^1];
        }
        else if (skillName.EndsWith('-'))
        {
            outcome = SkillOutcome.Failure;
            skillName = skillName[..^1];
        }

        var anySkill = false;
        var skill = default(SkillName);

        if (skillName.Equals("any", StringComparison.OrdinalIgnoreCase))
        {
            anySkill = true;
        }
        // TryParse accepts any numeric string ("99") as a SkillName, so the value has to be checked
        // against the enum as well.
        else if (!Enum.TryParse(skillName, true, out skill) || !Enum.IsDefined(skill))
        {
            return null;
        }

        // Parse range (default: 10). int.TryParse writes 0 on failure, so only a successful parse
        // may replace the default.
        var range = 10;
        if (parts.Length > startIndex + 1 && int.TryParse(parts[startIndex + 1], out var parsedRange))
        {
            range = parsedRange;
        }

        // Parse min/max skill value window (default: 0 / -1)
        var minValue = 0.0;
        var maxValue = -1.0;
        if (parts.Length > startIndex + 2)
        {
            var value = parts[startIndex + 2];

            // An empty window segment ("skill:Mining:10::false:5") is malformed rather than a default:
            // it is also what made IndexOf(char, 1) throw, since startIndex 1 is past the end of "".
            if (value.Length == 0)
            {
                return null;
            }

            var dashIndex = value.IndexOf('-', 1);
            if (dashIndex > 0)
            {
                var minPart = value[..dashIndex];
                var maxPart = value[(dashIndex + 1)..];
                if (!double.TryParse(minPart, out minValue) || !double.TryParse(maxPart, out maxValue) || maxValue < minValue)
                {
                    return null;
                }
            }
            else
            {
                double.TryParse(value, out minValue);
                maxValue = -1.0;
            }
        }

        // Parse require LOS (default: false)
        var requireLOS = false;
        if (parts.Length > startIndex + 3)
        {
            bool.TryParse(parts[startIndex + 3], out requireLOS);
        }

        // Parse cooldown (default: 5 seconds)
        var cooldown = TimeSpan.FromSeconds(5);
        if (parts.Length > startIndex + 4 && int.TryParse(parts[startIndex + 4], out var cooldownSeconds))
        {
            cooldown = TimeSpan.FromSeconds(cooldownSeconds);
        }

        var trigger = new SkillTrigger(anySkill, skill, outcome, range, minValue, maxValue, requireLOS, cooldown);
        trigger.ApplyTokens(wake, mode, when);
        return trigger;
    }
}
