using System;

namespace Server.Engines.ModernSpawner.Triggers;

/// <summary>
/// Trigger that fires when a player uses a specific skill nearby.
/// Definition format: skill:SkillName:range:minSkillValue
/// Examples:
///   skill:Mining:10        - Triggers on Mining skill use within 10 tiles
///   skill:Magery:5:50.0    - Triggers on Magery use within 5 tiles if skill >= 50
///   skill:Any:8            - Triggers on any skill use within 8 tiles
/// </summary>
public class SkillTrigger : ITrigger
{
    public string TriggerType => "skill";
    /// <summary>
    /// The skill that triggers this (or SkillName.Invalid for any skill).
    /// </summary>
    public SkillName TargetSkill { get; }

    /// <summary>
    /// Range in tiles from spawner to detect skill use.
    /// </summary>
    public int Range { get; }

    /// <summary>
    /// Minimum skill value required to trigger (0 = any level).
    /// </summary>
    public double MinSkillValue { get; }

    /// <summary>
    /// Whether to require line of sight to the skill user.
    /// </summary>
    public bool RequireLOS { get; }

    /// <summary>
    /// Cooldown between triggers.
    /// </summary>
    public TimeSpan Cooldown { get; }

    private ModernSpawner _spawner;
    private DateTime _lastTriggered;

    public SkillTrigger(SkillName skill, int range = 10, double minSkillValue = 0, bool requireLOS = false)
        : this(skill, range, minSkillValue, requireLOS, TimeSpan.FromSeconds(5))
    {
    }

    public SkillTrigger(SkillName skill, int range, double minSkillValue, bool requireLOS, TimeSpan cooldown)
    {
        TargetSkill = skill;
        Range = Math.Max(1, range);
        MinSkillValue = minSkillValue;
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

        // Check skill value if required
        if (MinSkillValue > 0 && context.UsedSkill != SkillName.Alchemy) // SkillName.Alchemy is used as "any"
        {
            var skill = mobile.Skills[context.UsedSkill];
            if (skill == null || skill.Value < MinSkillValue)
            {
                return false;
            }
        }

        _lastTriggered = Core.Now;
        return true;
    }

    public string Serialize()
    {
        if ((int)TargetSkill == -1)
        {
            return $"skill:Any:{Range}:{MinSkillValue}:{RequireLOS}:{(int)Cooldown.TotalSeconds}";
        }

        return $"skill:{TargetSkill}:{Range}:{MinSkillValue}:{RequireLOS}:{(int)Cooldown.TotalSeconds}";
    }

    /// <summary>
    /// Checks if the skill matches this trigger.
    /// </summary>
    public bool MatchesSkill(SkillName skill)
    {
        // SkillName.Alchemy with value -1 means "any skill" (using a sentinel)
        if ((int)TargetSkill == -1)
        {
            return true;
        }

        return skill == TargetSkill;
    }

    /// <summary>
    /// Parses a skill trigger definition string.
    /// Format: skill:SkillName:range or skill:SkillName:range:minValue
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

        // Parse skill name
        var skillName = parts[startIndex];
        SkillName skill;

        if (skillName.Equals("any", StringComparison.OrdinalIgnoreCase))
        {
            skill = (SkillName)(-1); // Sentinel for "any skill"
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

        // Parse min skill value (default: 0)
        var minValue = 0.0;
        if (parts.Length > startIndex + 2)
        {
            double.TryParse(parts[startIndex + 2], out minValue);
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

        return new SkillTrigger(skill, range, minValue, requireLOS, cooldown);
    }
}
