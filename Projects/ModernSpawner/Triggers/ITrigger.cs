namespace Server.Engines.ModernSpawner.Triggers;

/// <summary>
/// Base interface for all trigger conditions that can activate a spawner.
/// </summary>
public interface ITrigger
{
    /// <summary>
    /// Gets the unique type identifier for serialization.
    /// </summary>
    string TriggerType { get; }

    /// <summary>
    /// Evaluates whether this trigger condition is currently met.
    /// </summary>
    /// <param name="context">The trigger evaluation context.</param>
    /// <returns>True if the trigger condition is met.</returns>
    bool Evaluate(TriggerContext context);

    /// <summary>
    /// Called when this trigger needs to start monitoring for its condition.
    /// </summary>
    void Activate(ModernSpawner spawner);

    /// <summary>
    /// Called when this trigger should stop monitoring.
    /// </summary>
    void Deactivate();

    /// <summary>
    /// Serializes this trigger to a string for storage.
    /// </summary>
    string Serialize();
}

/// <summary>
/// Context provided during trigger evaluation.
/// </summary>
public class TriggerContext
{
    /// <summary>
    /// The spawner being evaluated.
    /// </summary>
    public ModernSpawner Spawner { get; }

    /// <summary>
    /// The mobile that potentially triggered this spawner (if any).
    /// </summary>
    public Mobile TriggeringMobile { get; set; }

    /// <summary>
    /// Speech text that triggered this spawner (if any).
    /// </summary>
    public string Speech { get; set; }

    /// <summary>
    /// The entity that was killed (for kill triggers).
    /// </summary>
    public IEntity KilledEntity { get; set; }

    /// <summary>
    /// The skill that was used (for skill triggers).
    /// </summary>
    public SkillName UsedSkill { get; set; }

    /// <summary>Outcome of the skill attempt that raised a skill trigger.</summary>
    public bool SkillSuccess { get; set; }

    /// <summary>Skill value of the user at the time of the attempt.</summary>
    public double SkillValue { get; set; }

    /// <summary>
    /// Custom data that can be passed by trigger sources.
    /// </summary>
    public object CustomData { get; set; }

    public TriggerContext(ModernSpawner spawner)
    {
        Spawner = spawner;
    }
}
