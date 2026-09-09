using System;

namespace Server.Engines.ModernSpawner.Triggers;

/// <summary>
/// Provides custom event infrastructure for ModernSpawner triggers.
/// Server operators can call these methods from their skill/event implementations
/// to enable skill-based triggers.
/// </summary>
public static class ModernSpawnerEvents
{
    /// <summary>
    /// Event fired when a skill is used. Subscribe to receive skill use notifications.
    /// </summary>
    public static event Action<Mobile, SkillName> SkillUsed;

    /// <summary>
    /// Call this method when a player uses a skill to notify the trigger system.
    /// This should be called from SkillCheck handlers or individual skill implementations.
    ///
    /// Example integration in SkillCheck.cs:
    /// <code>
    /// public static bool CheckSkill(Mobile from, Skill skill, object amObj, double chance)
    /// {
    ///     // Notify ModernSpawner of skill use
    ///     Server.Engines.ModernSpawner.Triggers.ModernSpawnerEvents.OnSkillUsed(from, skill.SkillName);
    ///
    ///     // ... rest of existing CheckSkill code
    /// }
    /// </code>
    /// </summary>
    /// <param name="mobile">The mobile using the skill.</param>
    /// <param name="skill">The skill being used.</param>
    public static void OnSkillUsed(Mobile mobile, SkillName skill)
    {
        if (mobile == null)
        {
            return;
        }

        // Invoke any direct subscribers
        SkillUsed?.Invoke(mobile, skill);

        // Notify the trigger system
        TriggerSystem.Instance.OnSkillUse(mobile, skill);
    }

}
