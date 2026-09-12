namespace Server.Engines.ModernSpawner.Triggers;

/// <summary>
/// Everything a trigger needs to decide whether it matches the event being dispatched.
/// </summary>
/// <remarks>
/// A <c>readonly record struct</c> so proximity, speech, kill and skill dispatch allocate nothing per
/// event. Always take it as <c>in</c> and never store it in an <see cref="object" /> or interface-typed
/// slot: that would box it and put an allocation back on the movement path.
/// </remarks>
/// <param name="Spawner">The spawner being evaluated.</param>
/// <param name="TriggeringMobile">The mobile that raised the event, or null.</param>
/// <param name="Speech">The speech that raised the event, or null.</param>
/// <param name="KilledEntity">The entity that was killed, or null.</param>
/// <param name="UsedSkill">The skill that was attempted, when this is a skill event.</param>
/// <param name="SkillValue">The user's value in <paramref name="UsedSkill" /> at the attempt.</param>
/// <param name="SkillSuccess">Whether the skill attempt succeeded.</param>
public readonly record struct TriggerContext(
    ModernSpawner Spawner,
    Mobile TriggeringMobile,
    string Speech,
    IEntity KilledEntity,
    SkillName UsedSkill,
    double SkillValue,
    bool SkillSuccess
)
{
    /// <summary>Context for a movement event near <paramref name="spawner" />.</summary>
    /// <param name="spawner">The spawner being evaluated.</param>
    /// <param name="mobile">The mobile that moved.</param>
    /// <returns>A context carrying only the mobile.</returns>
    public static TriggerContext ForProximity(ModernSpawner spawner, Mobile mobile) =>
        new(spawner, mobile, null, null, default, 0.0, false);

    /// <summary>Context for speech near <paramref name="spawner" />.</summary>
    /// <param name="spawner">The spawner being evaluated.</param>
    /// <param name="speaker">The mobile that spoke.</param>
    /// <param name="text">What was said.</param>
    /// <returns>A context carrying the speaker and the text.</returns>
    public static TriggerContext ForSpeech(ModernSpawner spawner, Mobile speaker, string text) =>
        new(spawner, speaker, text, null, default, 0.0, false);

    /// <summary>Context for the death of one of <paramref name="spawner" />'s spawns.</summary>
    /// <param name="spawner">The spawner being evaluated.</param>
    /// <param name="killed">The entity that died.</param>
    /// <param name="killer">The mobile credited with the kill, or null.</param>
    /// <returns>A context carrying the corpse and the killer.</returns>
    public static TriggerContext ForKill(ModernSpawner spawner, IEntity killed, Mobile killer) =>
        new(spawner, killer, null, killed, default, 0.0, false);

    /// <summary>Context for a skill attempt near <paramref name="spawner" />.</summary>
    /// <param name="spawner">The spawner being evaluated.</param>
    /// <param name="mobile">The mobile that attempted the skill.</param>
    /// <param name="skill">The skill attempted.</param>
    /// <param name="value">The mobile's value in that skill.</param>
    /// <param name="success">Whether the attempt succeeded.</param>
    /// <returns>A context carrying the skill, its value and the outcome.</returns>
    public static TriggerContext ForSkill(ModernSpawner spawner, Mobile mobile, SkillName skill, double value, bool success) =>
        new(spawner, mobile, null, null, skill, value, success);

    /// <summary>Context for a gate re-evaluation, which depends on the clock rather than an actor.</summary>
    /// <param name="spawner">The spawner being evaluated.</param>
    /// <returns>A context carrying only the spawner.</returns>
    public static TriggerContext ForGate(ModernSpawner spawner) =>
        new(spawner, null, null, null, default, 0.0, false);
}
