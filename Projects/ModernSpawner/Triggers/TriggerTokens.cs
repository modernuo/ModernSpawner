using System;
using Server.Text;

namespace Server.Engines.ModernSpawner.Triggers;

/// <summary>
/// The per-event-trigger tokens shared by every event trigger grammar: <c>wake:true|false</c>,
/// <c>mode:now|tick</c> and <c>when:&lt;expression&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// Tokens are name/value pairs that sit anywhere among a definition's positional arguments and in any
/// order, so <c>proximity:8:true:false:5:Player:wake:true:mode:tick</c> and
/// <c>proximity:8:mode:tick:wake:true</c> both parse. <see cref="Strip" /> pulls them out and hands the
/// caller back the positional-only definition, which each trigger's <c>Parse</c> then splits as before.
/// </para>
/// <para>
/// <c>when:</c> takes the rest of the definition, so the expression may itself contain <c>:</c>; it is
/// therefore always the last token in a definition. Parsing happens once per registration, never on a
/// dispatch path, so <see cref="Strip" /> is free to allocate the stripped string.
/// </para>
/// </remarks>
public static class TriggerTokens
{
    private const string WakeToken = "wake";
    private const string ModeToken = "mode";
    private const string WhenToken = "when";

    /// <summary>
    /// Reads one <c>name:value</c> segment as a token.
    /// </summary>
    /// <remarks>
    /// A recognised name is consumed even when its value is malformed - <c>mode:sideways</c> leaves
    /// <paramref name="mode" /> at its default rather than dropping <c>sideways</c> into the positional
    /// arguments, where it would silently shift every later argument.
    /// </remarks>
    /// <param name="segment">A <c>name:value</c> slice of a trigger definition.</param>
    /// <param name="wake">Receives the <c>wake:</c> value; untouched for other tokens.</param>
    /// <param name="mode">Receives the <c>mode:</c> value; untouched for other tokens.</param>
    /// <param name="when">Receives the raw <c>when:</c> expression; untouched for other tokens.</param>
    /// <returns>True when <paramref name="segment" /> was a token and has been consumed.</returns>
    public static bool TryParseToken(ReadOnlySpan<char> segment, ref bool wake, ref CycleMode mode, ref string when)
    {
        var separator = segment.IndexOf(':');
        if (separator <= 0)
        {
            return false;
        }

        var name = segment[..separator];
        var value = segment[(separator + 1)..];

        if (name.InsensitiveEquals(WakeToken))
        {
            if (bool.TryParse(value, out var parsed))
            {
                wake = parsed;
            }

            return true;
        }

        if (name.InsensitiveEquals(ModeToken))
        {
            if (value.InsensitiveEquals("tick"))
            {
                mode = CycleMode.Tick;
            }
            else if (value.InsensitiveEquals("now"))
            {
                mode = CycleMode.Now;
            }

            return true;
        }

        if (name.InsensitiveEquals(WhenToken))
        {
            when = value.Length == 0 ? null : value.ToString();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="name" /> starts a token. <c>when</c> is excluded: it swallows the rest of
    /// the definition and is handled separately by <see cref="Strip" />.
    /// </summary>
    /// <param name="name">A single segment of a trigger definition.</param>
    /// <returns>True for <c>wake</c> and <c>mode</c>.</returns>
    private static bool IsPairToken(ReadOnlySpan<char> name) =>
        name.InsensitiveEquals(WakeToken) || name.InsensitiveEquals(ModeToken);

    /// <summary>
    /// Splits <paramref name="definition" /> into its token values and the positional-only definition the
    /// caller's own grammar parses.
    /// </summary>
    /// <param name="definition">The full trigger definition text.</param>
    /// <param name="wake">Receives the <c>wake:</c> value, left alone when the token is absent.</param>
    /// <param name="mode">Receives the <c>mode:</c> value, left alone when the token is absent.</param>
    /// <param name="when">Receives the raw <c>when:</c> expression, left alone when the token is absent.</param>
    /// <returns>
    /// <paramref name="definition" /> with every token removed, or <paramref name="definition" /> itself
    /// when it carried none.
    /// </returns>
    public static string Strip(string definition, ref bool wake, ref CycleMode mode, ref string when)
    {
        if (string.IsNullOrEmpty(definition))
        {
            return definition;
        }

        var span = definition.AsSpan();
        if (span.IndexOf(':') < 0)
        {
            return definition;
        }

        var sb = ValueStringBuilder.CreateMT(definition.Length);
        var stripped = false;
        var wrote = false;

        try
        {
            var index = 0;
            while (index < span.Length)
            {
                var remaining = span[index..];
                var separator = remaining.IndexOf(':');
                var segment = separator < 0 ? remaining : remaining[..separator];

                if (segment.InsensitiveEquals(WhenToken))
                {
                    // when: takes everything that is left, colons included.
                    TryParseToken(remaining, ref wake, ref mode, ref when);
                    stripped = true;
                    break;
                }

                if (IsPairToken(segment) && separator >= 0)
                {
                    // The value is the next segment; the pair is contiguous in the source, so the token
                    // text is a single slice.
                    var afterName = remaining[(separator + 1)..];
                    var valueEnd = afterName.IndexOf(':');
                    var pairLength = valueEnd < 0 ? remaining.Length : separator + 1 + valueEnd;

                    TryParseToken(remaining[..pairLength], ref wake, ref mode, ref when);
                    stripped = true;
                    index += pairLength + 1;
                    continue;
                }

                if (wrote)
                {
                    sb.Append(':');
                }

                sb.Append(segment);
                wrote = true;

                if (separator < 0)
                {
                    break;
                }

                index += separator + 1;
            }

            return stripped ? sb.ToString() : definition;
        }
        finally
        {
            sb.Dispose();
        }
    }

    /// <summary>
    /// Appends the non-default tokens to a definition being serialized. Defaults are omitted so a
    /// definition written before the tokens existed round-trips to exactly its old text.
    /// </summary>
    /// <param name="sb">The buffer holding the positional part of the definition.</param>
    /// <param name="wake">The trigger's <c>wake:</c> value.</param>
    /// <param name="mode">The trigger's <c>mode:</c> value.</param>
    /// <param name="when">The trigger's raw <c>when:</c> expression, or null.</param>
    public static void Append(scoped ref ValueStringBuilder sb, bool wake, CycleMode mode, string when)
    {
        if (wake)
        {
            sb.Append(":wake:true");
        }

        if (mode == CycleMode.Tick)
        {
            sb.Append(":mode:tick");
        }

        if (!string.IsNullOrEmpty(when))
        {
            sb.Append(":when:");
            sb.Append(when);
        }
    }
}
