using System;
using System.Text.RegularExpressions;

namespace Server.Engines.ModernSpawner.Triggers;

/// <summary>
/// Trigger that activates when specific speech is detected near the spawner.
/// </summary>
public class SpeechTrigger : ITrigger
{
    public string TriggerType => "speech";

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

    /// <summary>
    /// Cooldown between trigger activations.
    /// </summary>
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromSeconds(5);

    private ModernSpawner _spawner;
    private Regex _compiledRegex;
    private DateTime _lastTriggered = DateTime.MinValue;

    public SpeechTrigger()
    {
    }

    public SpeechTrigger(string keyword, bool ignoreCase = true, int range = 10)
    {
        Keyword = keyword;
        IgnoreCase = ignoreCase;
        Range = range;
    }

    public bool Evaluate(TriggerContext context)
    {
        if (string.IsNullOrEmpty(context.Speech) || _spawner == null)
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
        if (!mobile.InRange(_spawner.Location, Range))
        {
            return false;
        }

        // Check map
        if (mobile.Map != _spawner.Map)
        {
            return false;
        }

        // Check cooldown
        if (Core.Now - _lastTriggered < Cooldown)
        {
            return false;
        }

        // Match the speech
        if (!MatchesSpeech(context.Speech))
        {
            return false;
        }

        _lastTriggered = Core.Now;
        return true;
    }

    private bool MatchesSpeech(string speech)
    {
        if (string.IsNullOrEmpty(Keyword))
        {
            return false;
        }

        if (UseRegex)
        {
            _compiledRegex ??= new Regex(
                Keyword,
                IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None
            );
            return _compiledRegex.IsMatch(speech);
        }

        var comparison = IgnoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return speech.Contains(Keyword, comparison);
    }

    public void Activate(ModernSpawner spawner)
    {
        _spawner = spawner;
        _compiledRegex = null; // Reset compiled regex
        TriggerSystem.Instance?.RegisterSpeechTrigger(spawner, this);
    }

    public void Deactivate()
    {
        if (_spawner != null)
        {
            TriggerSystem.Instance?.UnregisterSpeechTrigger(_spawner, this);
        }
        _spawner = null;
    }

    public string Serialize()
    {
        // Format: speech:keyword:ignoreCase:useRegex:range:playersOnly:cooldownSeconds
        // Keyword is base64 encoded to handle special characters
        var encodedKeyword = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Keyword ?? ""));
        return $"speech:{encodedKeyword}:{IgnoreCase}:{UseRegex}:{Range}:{PlayersOnly}:{(int)Cooldown.TotalSeconds}";
    }

    public static SpeechTrigger Parse(string definition)
    {
        var parts = definition.Split(':');
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

        return trigger;
    }
}
