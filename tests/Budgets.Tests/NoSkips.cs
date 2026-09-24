using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Estate.Budgets.Tests;

/// <summary>
/// No test skips itself: under tests/ there is no skip argument on a fact or a theory, no skip-if or skip-unless call,
/// no skipping assertion and no skippable fact. A flaky test is quarantined by name with a dated finding, or deleted.
/// </summary>
public sealed class NoSkips
{
    // The word is split, and the pattern spells it with a letter class, so this file does not match itself.
    private const string Word = "Sk" + "ip";

    private static readonly Regex Skipping = new(@"\bSk[i]p\s*=|\bSk[i]p\.(?:If|Unless)\b|\bAssert\.Sk[i]p\w*\s*\(|\bSk[i]ppable(?:Fact|Theory)\b", RegexOptions.CultureInvariant);

    [Fact]
    [Trait("Category", "fast")]
    public void No_test_skips_itself()
    {
        var skips = Repository.Files
            .Where(f => f.StartsWith("tests/", StringComparison.Ordinal) && f.EndsWith(".cs", StringComparison.Ordinal))
            .SelectMany(f => Repository.Lines(f).Select((line, i) => (Where: f + ":" + (i + 1).ToString(CultureInfo.InvariantCulture), Line: line)))
            .Where(x => Skipping.IsMatch(x.Line))
            .Select(x => x.Where + ": " + x.Line.Trim());

        Assert.Empty(skips);
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("[Fact(" + Word + " = \"flaky on Windows\")]", true)]
    [InlineData("[Theory(" + Word + "=\"later\")]", true)]
    [InlineData(Word + ".If(OperatingSystem.IsWindows());", true)]
    [InlineData("Assert." + Word + "When(true, \"no Docker\");", true)]
    [InlineData("[" + Word + "pableFact]", true)]
    [InlineData("var rest = rows." + Word + "(1);", false)]
    [InlineData("[Fact]", false)]
    public void The_rule_finds_each_way_a_test_skips(string line, bool skips) => Assert.Equal(skips, Skipping.IsMatch(line));
}
