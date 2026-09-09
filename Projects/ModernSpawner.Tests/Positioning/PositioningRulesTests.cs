using Server.Engines.ModernSpawner.Positioning;
using Xunit;

namespace Server.Engines.ModernSpawner.Tests.Positioning;

public class PositioningRulesTests
{
    #region ParseRuleString Tests

    [Fact]
    public void ParseRuleString_SimpleRuleName_ReturnsRuleNameOnly()
    {
        var (ruleName, parameters) = PositioningRules.ParseRuleString("centered");

        Assert.Equal("centered", ruleName);
        Assert.Null(parameters);
    }

    [Fact]
    public void ParseRuleString_RuleWithParameters_SplitsCorrectly()
    {
        var (ruleName, parameters) = PositioningRules.ParseRuleString("relative:5,10");

        Assert.Equal("relative", ruleName);
        Assert.Equal("5,10", parameters);
    }

    [Fact]
    public void ParseRuleString_RuleWithMultipleColons_FirstColonIsSeparator()
    {
        var (ruleName, parameters) = PositioningRules.ParseRuleString("waypoint:0x12345:extra");

        Assert.Equal("waypoint", ruleName);
        Assert.Equal("0x12345:extra", parameters);
    }

    [Fact]
    public void ParseRuleString_NullInput_ReturnsNulls()
    {
        var (ruleName, parameters) = PositioningRules.ParseRuleString(null);

        Assert.Null(ruleName);
        Assert.Null(parameters);
    }

    [Fact]
    public void ParseRuleString_EmptyInput_ReturnsNulls()
    {
        var (ruleName, parameters) = PositioningRules.ParseRuleString("");

        Assert.Null(ruleName);
        Assert.Null(parameters);
    }

    [Fact]
    public void ParseRuleString_WhitespaceAroundRuleName_TrimsWhitespace()
    {
        var (ruleName, parameters) = PositioningRules.ParseRuleString("  centered  ");

        Assert.Equal("centered", ruleName);
        Assert.Null(parameters);
    }

    [Fact]
    public void ParseRuleString_WhitespaceAroundParameters_TrimsWhitespace()
    {
        var (ruleName, parameters) = PositioningRules.ParseRuleString("relative:  5,10  ");

        Assert.Equal("relative", ruleName);
        Assert.Equal("5,10", parameters);
    }

    [Fact]
    public void ParseRuleString_ColonAtEnd_EmptyParameters()
    {
        var (ruleName, parameters) = PositioningRules.ParseRuleString("test:");

        Assert.Equal("test", ruleName);
        Assert.Equal("", parameters);
    }

    #endregion

    #region GetRule Tests

    [Fact]
    public void GetRule_RegisteredRule_ReturnsRule()
    {
        var rule = PositioningRules.GetRule("centered");

        Assert.NotNull(rule);
        Assert.Equal("centered", rule.RuleName);
    }

    [Fact]
    public void GetRule_CaseInsensitive_ReturnsRule()
    {
        var rule1 = PositioningRules.GetRule("CENTERED");
        var rule2 = PositioningRules.GetRule("Centered");
        var rule3 = PositioningRules.GetRule("centered");

        Assert.NotNull(rule1);
        Assert.NotNull(rule2);
        Assert.NotNull(rule3);
    }

    [Fact]
    public void GetRule_UnregisteredRule_ReturnsNull()
    {
        var rule = PositioningRules.GetRule("nonexistent_rule");

        Assert.Null(rule);
    }

    [Fact]
    public void GetRule_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(PositioningRules.GetRule(null));
        Assert.Null(PositioningRules.GetRule(""));
    }

    #endregion

    #region Built-in Rule Registration Tests

    [Theory]
    [InlineData("near_water")]
    [InlineData("avoid_water")]
    [InlineData("centered")]
    [InlineData("perimeter")]
    [InlineData("waypoint")]
    [InlineData("relative")]
    [InlineData("player_relative")]
    [InlineData("absolute")]
    [InlineData("row_fill")]
    [InlineData("column_fill")]
    [InlineData("tiles")]
    [InlineData("notiles")]
    [InlineData("water")]
    [InlineData("at_item")]
    public void BuiltInRules_AreRegistered(string ruleName)
    {
        var rule = PositioningRules.GetRule(ruleName);

        Assert.NotNull(rule);
        Assert.Equal(ruleName, rule.RuleName);
    }

    [Fact]
    public void GetRuleNames_ReturnsAllRegisteredRules()
    {
        var ruleNames = PositioningRules.GetRuleNames();

        Assert.Contains("centered", ruleNames);
        Assert.Contains("perimeter", ruleNames);
        Assert.Contains("near_water", ruleNames);
        Assert.Contains("avoid_water", ruleNames);
    }

    [Theory]
    [InlineData("near_water")]
    [InlineData("avoid_water")]
    [InlineData("centered")]
    [InlineData("perimeter")]
    [InlineData("waypoint")]
    [InlineData("relative")]
    [InlineData("player_relative")]
    [InlineData("absolute")]
    [InlineData("row_fill")]
    [InlineData("column_fill")]
    [InlineData("tiles")]
    [InlineData("notiles")]
    [InlineData("water")]
    [InlineData("at_item")]
    public void BuiltInRules_HaveNonEmptyDescription(string ruleName)
    {
        var rule = PositioningRules.GetRule(ruleName);

        Assert.NotNull(rule);
        Assert.False(string.IsNullOrWhiteSpace(rule.Description),
            $"Rule '{ruleName}' must expose a non-empty Description so gumps and docs can show something.");
    }

    [Fact]
    public void BuiltInRules_RuleNameMatchesRegistryKey()
    {
        foreach (var ruleName in PositioningRules.GetRuleNames())
        {
            var rule = PositioningRules.GetRule(ruleName);
            Assert.NotNull(rule);
            Assert.Equal(ruleName, rule.RuleName);
        }
    }

    [Fact]
    public void Register_SameName_OverwritesPrevious()
    {
        var original = PositioningRules.GetRule("centered");
        var replacement = new FakeRule("centered", "replaced");

        PositioningRules.Register(replacement);
        try
        {
            Assert.Same(replacement, PositioningRules.GetRule("centered"));
        }
        finally
        {
            // Restore the real rule so later tests see the canonical registry.
            PositioningRules.Register(original);
        }
    }

    private sealed class FakeRule : IPositioningRule
    {
        public FakeRule(string name, string description)
        {
            RuleName = name;
            Description = description;
        }

        public string RuleName { get; }
        public string Description { get; }

        public Point3D GetPosition(PositioningContext context) => Point3D.Zero;
    }

    #endregion
}
