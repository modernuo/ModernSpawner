using Server.Engines.ModernSpawner.Migration;
using Xunit;

namespace Server.Engines.ModernSpawner.Tests.Migration;

/// <summary>
/// <see cref="XmlSpawnerPropertyExpression" /> translates the XmlSpawner <c>PlayerPropertyName</c> grammar
/// (dev-docs/xmlspawner-migration.md §5) into the ModernSpawner expression engine's syntax, for use as a
/// trigger's <c>when:</c> condition. Pure string translation - no world needed.
/// </summary>
public class XmlSpawnerPropertyExpressionTests
{
    [Theory]
    [InlineData("Karma>0", "trigMob.Karma > 0")]
    [InlineData("Karma=0", "trigMob.Karma == 0")]
    [InlineData("Karma!=0", "trigMob.Karma != 0")]
    [InlineData("Karma<0", "trigMob.Karma < 0")]
    [InlineData("Karma>=0", "trigMob.Karma >= 0")]
    [InlineData("Karma<=0", "trigMob.Karma <= 0")]
    [InlineData("TRIGMOB.Karma>0", "trigMob.Karma > 0")]
    [InlineData("Female=True", "trigMob.Female == true")]
    [InlineData("~Karma>0", "not (trigMob.Karma > 0)")]
    public void TryTranslate_SingleComparison_TranslatesOperatorAndOperands(string xml, string expected)
    {
        Assert.True(XmlSpawnerPropertyExpression.TryTranslate(xml, out var expression, out var reason));
        Assert.Equal(expected, expression);
        Assert.Null(reason);
    }

    [Fact]
    public void TryTranslate_Conjunction_NestsRightAssociatively()
    {
        // BaseXmlSpawner.CheckPropertyString nests A & B | C as A & (B | C).
        Assert.True(XmlSpawnerPropertyExpression.TryTranslate("Karma>0&Fame>0|Female=True", out var expression, out _));
        Assert.Equal("(trigMob.Karma > 0 and (trigMob.Fame > 0 or trigMob.Female == true))", expression);
    }

    [Fact]
    public void TryTranslate_StringValue_QuotesTheUnquotedXmlSpawnerLiteral()
    {
        // XmlSpawner property tests are never quoted; the value side falls back to a string literal
        // once it fails every other literal/property shape.
        Assert.True(XmlSpawnerPropertyExpression.TryTranslate("Name=SomePlayer", out var expression, out var reason));
        Assert.Equal("trigMob.Name == \"SomePlayer\"", expression);
        Assert.Null(reason);
    }

    [Theory]
    [InlineData("GETONTHIS,Karma>0")]
    [InlineData("PLAYERSINRANGE,5>0")]
    [InlineData("Karma>RND,1,100")]
    [InlineData("not an expression")]
    public void TryTranslate_UnsupportedConstruct_FailsWithReason(string xml)
    {
        Assert.False(XmlSpawnerPropertyExpression.TryTranslate(xml, out var expression, out var reason));
        Assert.Null(expression);
        Assert.NotNull(reason);
    }

    [Fact]
    public void TryTranslate_EmptyString_Fails()
    {
        Assert.False(XmlSpawnerPropertyExpression.TryTranslate("", out var expression, out var reason));
        Assert.Null(expression);
        Assert.NotNull(reason);
    }
}
