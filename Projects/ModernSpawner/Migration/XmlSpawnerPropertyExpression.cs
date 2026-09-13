using System;
using System.Globalization;

namespace Server.Engines.ModernSpawner.Migration;

/// <summary>
/// Translates the XmlSpawner property-test grammar (<c>dev-docs/xmlspawner-migration.md</c> §5:
/// <c>prop op value</c>, <c>~</c> for negation, <c>&amp;</c>/<c>|</c> for conjunction/disjunction) into an
/// expression the ModernSpawner <see cref="Scripting.Expressions.ExpressionEngine" /> accepts, for use as a
/// trigger's <c>when:</c> condition. Shared by <see cref="XmlSpawnerMigrator" /> and
/// <see cref="Serialization.XmlSpawnerImporter" /> so <c>PlayerPropertyName</c> maps identically on both
/// ingestion paths.
/// </summary>
/// <remarks>
/// Only the subset needed to carry a simple property test survives: the property side of <c>prop op
/// value</c> accepts a bare property name or <c>TRIGMOB.Prop</c>, both resolving to the triggering mobile
/// (<c>trigMob.Prop</c> in the target grammar, since <c>PlayerPropertyName</c> is always tested against the
/// triggering player); the value side additionally accepts number, hex, boolean and unquoted-string
/// literals (XmlSpawner property tests are never quoted). Constructs the target grammar has no equivalent
/// for - <c>GETONTHIS</c>, <c>GETONMOB</c>, <c>PLAYERSINRANGE</c>, <c>RND</c>, anything else with a comma -
/// are reported as unsupported rather than guessed at, per the migration requirements ("if a given property
/// test cannot be represented, emit the trigger without <c>when:</c> and add a report line").
/// </remarks>
public static class XmlSpawnerPropertyExpression
{
    /// <summary>
    /// Attempts to translate <paramref name="propertyTest" /> into a <c>when:</c> expression.
    /// </summary>
    /// <param name="propertyTest">The raw XmlSpawner property-test string (e.g. <c>PlayerPropertyName</c>).</param>
    /// <param name="expression">Receives the translated expression on success.</param>
    /// <param name="reason">Receives a human-readable reason on failure.</param>
    /// <returns>True when the whole test string was translated.</returns>
    public static bool TryTranslate(string propertyTest, out string expression, out string reason)
    {
        if (string.IsNullOrWhiteSpace(propertyTest))
        {
            expression = null;
            reason = "empty property test";
            return false;
        }

        return TryTranslateConjunction(propertyTest.Trim(), out expression, out reason);
    }

    // A & B | C nests right-recursively - A & (B | C) - matching BaseXmlSpawner.CheckPropertyString's own
    // parse, which combines the first operator it finds with the recursively-translated remainder.
    private static bool TryTranslateConjunction(string test, out string expression, out string reason)
    {
        var splitIndex = -1;
        var isAnd = false;

        for (var i = 0; i < test.Length; i++)
        {
            if (test[i] is '&' or '|')
            {
                splitIndex = i;
                isAnd = test[i] == '&';
                break;
            }
        }

        if (splitIndex < 0)
        {
            return TryTranslateSingle(test, out expression, out reason);
        }

        if (!TryTranslateSingle(test[..splitIndex], out var left, out reason))
        {
            expression = null;
            return false;
        }

        if (!TryTranslateConjunction(test[(splitIndex + 1)..], out var right, out reason))
        {
            expression = null;
            return false;
        }

        expression = $"({left} {(isAnd ? "and" : "or")} {right})";
        return true;
    }

    private static bool TryTranslateSingle(string test, out string expression, out string reason)
    {
        test = test.Trim();

        var negate = false;
        if (test.StartsWith('~'))
        {
            negate = true;
            test = test[1..].Trim();
        }

        if (!TryFindOperator(test, out var opIndex, out var opLength, out var opText))
        {
            expression = null;
            reason = $"unrecognized property test '{test}'";
            return false;
        }

        var left = test[..opIndex].Trim();
        var right = test[(opIndex + opLength)..].Trim();

        // The left side of "prop op value" is always the property under test; the right side is the
        // value being compared against, literal or another property when TRIGMOB.-qualified.
        if (!TryTranslateOperand(left, isProperty: true, out var leftExpr, out reason))
        {
            expression = null;
            return false;
        }

        if (!TryTranslateOperand(right, isProperty: false, out var rightExpr, out reason))
        {
            expression = null;
            return false;
        }

        var comparison = $"{leftExpr} {opText} {rightExpr}";
        expression = negate ? $"not ({comparison})" : comparison;
        reason = null;
        return true;
    }

    private static bool TryFindOperator(string test, out int index, out int length, out string opText)
    {
        for (var i = 0; i < test.Length; i++)
        {
            var c = test[i];
            if (c is not ('=' or '!' or '<' or '>'))
            {
                continue;
            }

            var hasEquals = i + 1 < test.Length && test[i + 1] == '=';
            index = i;
            length = hasEquals ? 2 : 1;
            opText = c switch
            {
                '=' => "==",
                '!' => "!=",
                '<' => hasEquals ? "<=" : "<",
                '>' => hasEquals ? ">=" : ">",
                _ => null
            };
            return opText != null;
        }

        index = -1;
        length = 0;
        opText = null;
        return false;
    }

    /// <summary>
    /// Translates one side of a comparison. The property side of <c>prop op value</c> only accepts a
    /// property reference (bare, or <c>TRIGMOB.</c>-qualified - both resolve to the triggering mobile,
    /// the only object <c>PlayerPropertyName</c> is ever tested against); the value side also accepts
    /// literals, including an unquoted string (XmlSpawner property tests are never quoted).
    /// </summary>
    private static bool TryTranslateOperand(string operand, bool isProperty, out string expression, out string reason)
    {
        reason = null;

        if (string.IsNullOrEmpty(operand))
        {
            expression = null;
            reason = "empty operand";
            return false;
        }

        if (operand.StartsWith("TRIGMOB.", StringComparison.OrdinalIgnoreCase))
        {
            expression = $"trigMob.{operand[8..]}";
            return true;
        }

        if (bool.TryParse(operand, out var boolValue))
        {
            expression = boolValue ? "true" : "false";
            return true;
        }

        if (operand.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && operand.Length > 2)
        {
            expression = operand;
            return true;
        }

        if (double.TryParse(operand, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            expression = operand;
            return true;
        }

        if (isProperty)
        {
            if (IsSimplePropertyPath(operand))
            {
                expression = $"trigMob.{operand}";
                return true;
            }

            expression = null;
            reason = $"unsupported property reference '{operand}'";
            return false;
        }

        // Not a recognised literal and not the property side: a bare word compares as a string, since
        // XmlSpawner property tests are never quoted - unless it looks like one of the keyword
        // constructs (GETONTHIS, GETONMOB, PLAYERSINRANGE, RND) this translator does not evaluate.
        if (operand.Contains(',', StringComparison.Ordinal))
        {
            expression = null;
            reason = $"unsupported construct '{operand}'";
            return false;
        }

        expression = $"\"{operand.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
        return true;
    }

    private static bool IsSimplePropertyPath(string value)
    {
        if (value.Length == 0 || !(char.IsLetter(value[0]) || value[0] == '_'))
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!(char.IsLetterOrDigit(c) || c is '_' or '.'))
            {
                return false;
            }
        }

        return true;
    }
}
