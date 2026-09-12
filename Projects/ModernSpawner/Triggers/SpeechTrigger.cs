using System;
using System.Text.RegularExpressions;
using Server.Text;

namespace Server.Engines.ModernSpawner.Triggers;

/// <summary>
/// Trigger that activates when specific speech is detected near the spawner.
/// Definition: <c>speech:&lt;base64 keyword&gt;:&lt;ignoreCase&gt;:&lt;useRegex&gt;:&lt;range&gt;:&lt;playersOnly&gt;:&lt;cooldownSeconds&gt;</c>
/// plus the shared <see cref="TriggerTokens" />.
/// </summary>
public class SpeechTrigger : TriggerBase
{
    /// <summary>
    /// How long a regex keyword may spend on one line of speech before the match is abandoned. Keywords
    /// are authored by staff but run against player speech, so a catastrophically backtracking pattern
    /// would otherwise stall the game loop for every line spoken near the spawner.
    /// </summary>
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);

    /// <inheritdoc />
    public override string TriggerType => "speech";

    /// <inheritdoc />
    public override TriggerKind Kind => TriggerKind.Event;

    /// <summary>
    /// The keyword or phrase to match.
    /// </summary>
    public string Keyword { get; set; }

    /// <summary>
    /// Whether the match should be case-insensitive.
    /// </summary>
    public bool IgnoreCase { get; set; } = true;

    /// <summary>
    /// Whether to use regex matching instead of contains.
    /// </summary>
    public bool UseRegex { get; set; }

    /// <summary>
    /// Range within which the speech must occur.
    /// </summary>
    public int Range { get; set; } = 10;

    /// <summary>
    /// Whether only players can trigger (not NPCs).
    /// </summary>
    public bool PlayersOnly { get; set; } = true;

    private Regex _compiledRegex;

    /// <summary>Creates a trigger with the documented defaults.</summary>
    public SpeechTrigger() => Cooldown = TimeSpan.FromSeconds(5);

    /// <summary>Creates a speech trigger.</summary>
    /// <param name="keyword">The keyword, phrase or regex to match.</param>
    /// <param name="ignoreCase">Whether the match ignores case.</param>
    /// <param name="range">Range in tiles from the spawner.</param>
    public SpeechTrigger(string keyword, bool ignoreCase = true, int range = 10) : this()
    {
        Keyword = keyword;
        IgnoreCase = ignoreCase;
        Range = range;
    }

    /// <inheritdoc />
    public override bool Evaluate(in TriggerContext context)
    {
        var spawner = Spawner;
        if (string.IsNullOrEmpty(context.Speech) || spawner == null)
        {
            return false;
        }

        var mobile = context.TriggeringMobile;
        if (mobile == null)
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

        // Cooldown is a read: the spawner advances it when it accepts the event.
        if (!CooldownElapsed())
        {
            return false;
        }

        return MatchesSpeech(context.Speech);
    }

    private Regex BuildRegex() =>
        new(
            Keyword ?? string.Empty,
            RegexOptions.Compiled | (IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None),
            RegexTimeout
        );

    private bool MatchesSpeech(string speech)
    {
        if (string.IsNullOrEmpty(Keyword))
        {
            return false;
        }

        if (UseRegex)
        {
            // Memoization only - Activate builds this up front, so the dispatch path normally finds it
            // already there. It carries no evaluation state, so Evaluate stays pure.
            _compiledRegex ??= BuildRegex();

            try
            {
                return _compiledRegex.IsMatch(speech);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        var comparison = IgnoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return speech.Contains(Keyword, comparison);
    }

    /// <inheritdoc />
    public override void Activate(ModernSpawner spawner)
    {
        base.Activate(spawner);

        // Compile once per registration so no dispatch pays for it, and so a keyword edited through the
        // gump cannot leave a stale pattern behind.
        _compiledRegex = UseRegex && !string.IsNullOrEmpty(Keyword) ? BuildRegex() : null;
    }

    /// <inheritdoc />
    public override void Deactivate()
    {
        base.Deactivate();
        _compiledRegex = null;
    }

    /// <inheritdoc />
    public override string Serialize()
    {
        // Format: speech:keyword:ignoreCase:useRegex:range:playersOnly:cooldownSeconds
        // Keyword is base64 encoded to handle special characters
        var encodedKeyword = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Keyword ?? ""));

        var sb = ValueStringBuilder.CreateMT();
        try
        {
            sb.Append($"speech:{encodedKeyword}:{IgnoreCase}:{UseRegex}:{Range}:{PlayersOnly}:{(int)Cooldown.TotalSeconds}");
            AppendTokens(ref sb);
            return sb.ToString();
        }
        finally
        {
            sb.Dispose();
        }
    }

    /// <summary>Parses a speech trigger definition.</summary>
    /// <param name="definition">The definition text.</param>
    /// <returns>The parsed trigger.</returns>
    public static SpeechTrigger Parse(string definition)
    {
        var wake = false;
        var mode = CycleMode.Now;
        string when = null;
        var positional = TriggerTokens.Strip(definition, ref wake, ref mode, ref when);

        var parts = positional.Split(':');
        var trigger = new SpeechTrigger();

        if (parts.Length > 1)
        {
            try
            {
                trigger.Keyword = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));
            }
            catch
            {
                trigger.Keyword = parts[1]; // Fallback to plain text
            }
        }

        if (parts.Length > 2 && bool.TryParse(parts[2], out var ignoreCase))
        {
            trigger.IgnoreCase = ignoreCase;
        }

        if (parts.Length > 3 && bool.TryParse(parts[3], out var useRegex))
        {
            trigger.UseRegex = useRegex;
        }

        if (parts.Length > 4 && int.TryParse(parts[4], out var range))
        {
            trigger.Range = range;
        }

        if (parts.Length > 5 && bool.TryParse(parts[5], out var playersOnly))
        {
            trigger.PlayersOnly = playersOnly;
        }

        if (parts.Length > 6 && int.TryParse(parts[6], out var cooldown))
        {
            trigger.Cooldown = TimeSpan.FromSeconds(cooldown);
        }

        trigger.ApplyTokens(wake, mode, when);
        return trigger;
    }
}
