using Server.Misc;

namespace Server.Engines.ModernSpawner.Triggers;

/// <summary>
/// Bridges engine events to the trigger system.
/// </summary>
public static class ModernSpawnerEvents
{
    private static bool _configured;

    /// <summary>
    /// Subscribes to <see cref="SkillEvents.SkillUsed" />. Idempotent: repeated calls subscribe once.
    /// </summary>
    public static void Configure()
    {
        if (_configured)
        {
            return;
        }

        SkillEvents.SkillUsed += OnSkillUsed;
        _configured = true;
    }

    /// <summary>
    /// Forwards a player's skill attempt to the trigger system; creatures are ignored.
    /// Runs on every skill attempt server-wide, so it must stay allocation-free.
    /// </summary>
    /// <param name="mobile">The mobile that attempted the skill.</param>
    /// <param name="skill">The skill attempted.</param>
    /// <param name="success">Whether the attempt succeeded.</param>
    public static void OnSkillUsed(Mobile mobile, Skill skill, bool success)
    {
        if (mobile?.Player != true || skill == null)
        {
            return;
        }

        TriggerSystem.Instance.OnSkillUse(mobile, skill, success);
    }
}
