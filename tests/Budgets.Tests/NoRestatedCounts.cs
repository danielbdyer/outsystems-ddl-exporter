using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Estate.Budgets.Tests;

/// <summary>
/// No hand-written document restates a count the build can compute: the number of verbs, laws, operations, samples,
/// packages, hooks or files, or a file's line count (instruction architecture §10, test 7). A logical line that
/// carries a date is a dated historical measurement and is allowed. The five root design documents are excluded until
/// M8, because they must count what v1 and v2 carried.
/// </summary>
public sealed class NoRestatedCounts
{
    // A number, in digits or in words from two up and not after "no" (as in "no two files"), then at most two
    // words that are not articles or prepositions, then a thing the build counts.
    private static readonly Regex Count = new(
        @"(?<!\bno\s+)\b(?:\d[\d,]*|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty|thirty|forty|fifty|hundred|dozen)"
        + @"\s+(?:(?!(?:the|a|an|its|their|this|each|every|of|in|on|per|for|to)\b)[\w'-]+\s+){0,2}?"
        + @"(?:verbs?|laws?|operations?|samples?|packages?|hooks?|files?|lines?)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex Dated = new(@"\b\d{4}-\d{2}-\d{2}\b", RegexOptions.CultureInvariant);

    public static TheoryData<string> HandWritten => new(Documents.HandWritten);

    [Theory]
    [Trait("Category", "fast")]
    [Trait("Value", "L2")]
    [MemberData(nameof(HandWritten))]
    public void A_hand_written_document_restates_no_count_the_build_computes(string document) =>
        Assert.Empty(Documents.Blocks(document)
            .Where(b => !Dated.IsMatch(b.Text))
            .SelectMany(b => Count.Matches(b.Text).Select(m => document + ":" + b.Line.ToString(CultureInfo.InvariantCulture) + ": '" + m.Value + "' restates a count; name the generated file that carries it, or drop the number")));

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("The CLI has 13 verbs.", true)]
    [InlineData("the kernel's five source files", true)]
    [InlineData("README.md is 68 lines long.", true)]
    [InlineData("the two hooks", true)]
    [InlineData("a literal credential is exit 6 naming the file", false)]
    [InlineData("no two emitted files differ only by case", false)]
    [InlineData("The fast lane finishes in five minutes.", false)]
    [InlineData("one file per verb", false)]
    public void The_rule_finds_a_restated_count(string line, bool restates) => Assert.Equal(restates, Count.IsMatch(line));
}
