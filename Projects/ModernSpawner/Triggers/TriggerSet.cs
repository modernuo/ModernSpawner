using System.Collections.Generic;

namespace Server.Engines.ModernSpawner.Triggers;

/// <summary>
/// One spawner's parsed triggers, filed by class. The trigger system keeps exactly one of these per
/// registered spawner in a single dictionary, so a movement, speech or kill dispatch does one lookup and
/// then walks a typed list by index - no per-type dictionary chain, no enumerator, no allocation.
/// </summary>
public sealed class TriggerSet
{
    /// <summary>
    /// The registration this set belongs to. A cycle request stamped with an older generation was
    /// bought before the spawner was re-registered and is dropped rather than run against a set that no
    /// longer contains its trigger.
    /// </summary>
    public int Generation { get; set; }

    /// <summary>The map this spawner was filed under in the skill-dispatch candidate lists.</summary>
    public Map SkillMap { get; set; }

    /// <summary>Every parsed trigger, in definition order. Deactivation walks this one.</summary>
    public List<ITrigger> All { get; } = [];

    /// <summary>The proximity triggers, dispatched from <see cref="ModernSpawner.OnMovement" />.</summary>
    public List<ProximityTrigger> Proximity { get; } = [];

    /// <summary>The speech triggers, dispatched from <see cref="ModernSpawner.OnSpeech" />.</summary>
    public List<SpeechTrigger> Speech { get; } = [];

    /// <summary>The kill triggers, dispatched when one of the spawner's spawns dies.</summary>
    public List<KillTrigger> Kill { get; } = [];

    /// <summary>The skill triggers, dispatched from the server-wide skill event.</summary>
    public List<SkillTrigger> Skill { get; } = [];

    /// <summary>The gates (time windows), which open and close rather than buying cycles.</summary>
    public List<ITrigger> Gates { get; } = [];

    /// <summary>How many parsed triggers are event sources. Malformed definitions count for neither.</summary>
    public int EventCount { get; private set; }

    /// <summary>How many parsed triggers are gates. Malformed definitions count for neither.</summary>
    public int GateCount { get; private set; }

    /// <summary>
    /// Files a parsed trigger under its class and advances the event/gate counts the tick guards read.
    /// Null - a malformed definition the factory rejected - is ignored and counts for neither.
    /// </summary>
    /// <param name="trigger">The parsed trigger, or null.</param>
    public void Add(ITrigger trigger)
    {
        if (trigger == null)
        {
            return;
        }

        All.Add(trigger);

        switch (trigger)
        {
            case ProximityTrigger proximity:
                {
                    Proximity.Add(proximity);
                    break;
                }
            case SpeechTrigger speech:
                {
                    Speech.Add(speech);
                    break;
                }
            case KillTrigger kill:
                {
                    Kill.Add(kill);
                    break;
                }
            case SkillTrigger skill:
                {
                    Skill.Add(skill);
                    break;
                }
        }

        if (trigger.Kind == TriggerKind.Gate)
        {
            Gates.Add(trigger);
            GateCount++;
        }
        else
        {
            EventCount++;
        }
    }

    /// <summary>Empties every list and both counts, leaving the set reusable.</summary>
    public void Clear()
    {
        All.Clear();
        Proximity.Clear();
        Speech.Clear();
        Kill.Clear();
        Skill.Clear();
        Gates.Clear();
        EventCount = 0;
        GateCount = 0;
        SkillMap = null;
    }
}
