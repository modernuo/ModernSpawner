using System;

namespace Server.Engines.ModernSpawner.Triggers;

/// <summary>Which attempt outcomes a skill trigger reacts to.</summary>
public enum SkillOutcome
{
    Any,
    Success,
    Failure
}

/// <summary>
/// Fires when a player uses a skill near the spawner.
/// Definition: <c>skill:&lt;Skill&gt;[+|-]:&lt;range&gt;:&lt;min&gt;[-&lt;max&gt;]:&lt;los&gt;:&lt;cooldownSeconds&gt;</c>.
/// <c>+</c> reacts to successes only, <c>-</c> to failures only; <c>Any</c> matches every skill.
/// Examples: <c>skill:Mining:10</c>, <c>skill:Magery+:5:50-90</c>, <c>skill:Any-:8</c>.
/// </summary>
public class SkillTrigger : ITrigger
{
    public string TriggerType => "skill";

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

    /// <summary>Minimum time between firings.</summary>
    public TimeSpan Cooldown { get; }

    private ModernSpawner _spawner;
    private DateTime _lastTriggered;

    public SkillTrigger(SkillName skill, int range = 10, double minSkillValue = 0, bool requireLOS = false)
        : this(false, skill, SkillOutcome.Any, range, minSkillValue, -1, requireLOS, TimeSpan.FromSeconds(5))
    {
    }

    public SkillTrigger(SkillName skill, int range, double minSkillValue, bool requireLOS, TimeSpan cooldown)
        : this(false, skill, SkillOutcome.Any, range, minSkillValue, -1, requireLOS, cooldown)
    {
    }

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

    public void Activate(ModernSpawner spawner)
    {
        _spawner = spawner;
        TriggerSystem.Instance.RegisterSkillTrigger(spawner, this);
    }

    public void Deactivate()
    {
        if (_spawner != null)
        {
            TriggerSystem.Instance.UnregisterSkillTrigger(_spawner, this);
            _spawner = null;
        }
    }

    public bool Evaluate(TriggerContext context)
    {
        if (_spawner == null || _spawner.Deleted || !_spawner.Running)
        {
            return false;
        }

        // Check cooldown
        if (Core.Now - _lastTriggered < Cooldown)
        {
            return false;
        }

        var mobile = context.TriggeringMobile;
        if (mobile == null || mobile.Map != _spawner.Map)
        {
            return false;
        }

        // Check range
        if (!mobile.InRange(_spawner.Location, Range))
        {
            return false;
        }

        // Check LOS if required
        if (RequireLOS && !mobile.CanSee(_spawner))
        {
            return false;
        }

        if (!MatchesContext(context.UsedSkill, context.SkillValue, context.SkillSuccess))
        {
            return false;
        }

        _lastTriggered = Core.Now;
        return true;
    }

    /// <summary>Whether this trigger reacts to <paramref name="skill"/> at all.</summary>
    public bool MatchesSkill(SkillName skill) => AnySkill || skill == TargetSkill;

    /// <summary>The pure part of <see cref="Evaluate"/>: skill, outcome and value window.</summary>
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

    public string Serialize()
    {
        var skill = AnySkill ? "Any" : TargetSkill.ToString();
        var suffix = Outcome switch
        {
            SkillOutcome.Success => "+",
            SkillOutcome.Failure => "-",
            _ => ""
        };
        var window = MaxSkillValue < 0 ? $"{MinSkillValue}" : $"{MinSkillValue}-{MaxSkillValue}";
        return $"skill:{skill}{suffix}:{Range}:{window}:{RequireLOS}:{(int)Cooldown.TotalSeconds}";
    }

    /// <summary>
    /// Parses a skill trigger definition string.
    /// Format: <c>skill:&lt;Skill&gt;[+|-]:&lt;range&gt;:&lt;min&gt;[-&lt;max&gt;]:&lt;los&gt;:&lt;cooldownSeconds&gt;</c>.
    /// </summary>
    public static SkillTrigger Parse(string definition)
    {
        if (string.IsNullOrEmpty(definition))
        {
            return null;
        }

        var parts = definition.Split(':');
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
        else if (!Enum.TryParse(skillName, true, out skill))
        {
            return null;
        }

        // Parse range (default: 10)
        var range = 10;
        if (parts.Length > startIndex + 1)
        {
            int.TryParse(parts[startIndex + 1], out range);
        }

        // Parse min/max skill value window (default: 0 / -1)
        var minValue = 0.0;
        var maxValue = -1.0;
        if (parts.Length > startIndex + 2)
        {
            var value = parts[startIndex + 2];
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

        return new SkillTrigger(anySkill, skill, outcome, range, minValue, maxValue, requireLOS, cooldown);
    }
}
