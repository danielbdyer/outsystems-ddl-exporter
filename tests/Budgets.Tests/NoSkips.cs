using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace DbChange.Budgets.Tests;

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
    [Trait("Value", "L6")]
    [Trait("Value", "L9")]
    public void No_test_skips_itself()
    {
        var skips = Repository.Files
            .Where(f => f.StartsWith("tests/", StringComparison.Ordinal) && f.EndsWith(".cs", StringComparison.Ordinal))
            .SelectMany(f => Repository.Lines(f).Select((line, i) => (Where: f + ":" + (i + 1).ToString(CultureInfo.InvariantCulture), Line: line)))
            .Where(x => Skipping.IsMatch(x.Line))
            .Select(x => x.Where + ": " + x.Line.Trim());

        Assert.Empty(skips);
    }

    /// <summary>
    /// The scan that reads each test's category finds a method whatever its return type is written as: a method returning
    /// System.Threading.Tasks.Task spelled out keeps its own category, and the test after it keeps its own.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_trait_scan_reads_a_test_whose_return_type_is_written_with_dots()
    {
        string[] planted =
        [
            "namespace Planted;",
            "public sealed class Tests",
            "{",
            "    [Fact]",
            "    [Trait(\"Category\", \"fixture\")]",
            "    public async System.Threading.Tasks.Task Spelled_out() => await System.Threading.Tasks.Task.Yield();",
            "",
            "    [Fact]",
            "    [Trait(\"Category\", \"fast\")]",
            "    public void Next() { }",
            "}",
        ];

        Assert.Equal([("Planted.Tests.Spelled_out", "fixture"), ("Planted.Tests.Next", "fast")],
            TestTraits.In("Planted", planted).Select(t => (t.FullName, t.Traits.Single(x => x.Name == "Category").Value)));
    }

    /// <summary>A test with no category runs in no lane, a skip that reads as a pass; one with two runs twice.</summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "L9")]
    public void Every_test_carries_exactly_one_category_fast_fixture_or_build()
    {
        string[] categories = ["fast", "fixture", "build"];

        Assert.NotEmpty(TestTraits.All);
        Assert.Empty(TestTraits.All.Where(t => t.Values("Category").Count() != 1 || !t.Values("Category").All(categories.Contains))
            .Select(t => t.FullName + " carries " + (t.Values("Category").Any() ? string.Join(" and ", t.Values("Category")) : "no category") + "; a test carries one of fast, fixture and build"));
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
