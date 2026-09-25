using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;

namespace Estate.Budgets.Tests.Register;

/// <summary>
/// The engine's documents are in the register: no retired word, no numbered axis, no antithesis tic, as
/// ci/register.json lists them from the instruction architecture's Appendix A. VALUES.md first, because a register of
/// one-sentence values is the document most likely to break the register's own rules. knowledge/description.md replaces
/// the list at M7; the five root design documents are excluded until M8.
/// </summary>
public sealed class Prose
{
    private static readonly IReadOnlyList<(Regex Pattern, string Name, string Instead)> Banned = Read();

    public static TheoryData<string> EngineDocuments => new(Documents.Engine);

    [Theory]
    [Trait("Category", "fast")]
    [MemberData(nameof(EngineDocuments))]
    public void An_engine_document_uses_no_retired_word_and_no_banned_form(string document)
    {
        var text = Repository.Read(document);

        Assert.Empty(Findings(text).Select(f => document + ":" + f));
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("The pillars hold.", "pillar")]
    [InlineData("the writer's tests on both CI operating systems", "writer")]
    [InlineData("It ships as Mechanism 3.", "a numbered axis")]
    [InlineData("It is a table, not a view.", "the antithesis tic")]
    [InlineData("the perf\ngate ran", "perf gate")]
    [InlineData("The `Profile` type", "`Profile`")]
    [InlineData("estate profile writes the evidence; normal; the Utf8JsonWriter; a review", null)]
    public void The_register_finds_a_retired_word_or_a_banned_form(string text, string? found) =>
        Assert.Equal(found, Findings(text).Select(f => f.Split('\'')[1]).FirstOrDefault());

    /// <summary>Each finding as line: 'what' is retired; say instead: what to write. Register.Refusals reads refusals with it too.</summary>
    internal static IEnumerable<string> Findings(string text) => Banned
        .SelectMany(b => b.Pattern.Matches(text).Select(m => (m.Index, b.Name, b.Instead)))
        .OrderBy(f => f.Index)
        .Select(f => (text.AsSpan(0, f.Index).Count('\n') + 1).ToString(CultureInfo.InvariantCulture) + ": '" + f.Name + "' is out of the register; write instead: " + f.Instead);

    private static List<(Regex, string, string)> Read()
    {
        var register = JsonNode.Parse(Repository.Read("ci/register.json"))!;
        var words = register["retired"]!.AsArray().Select(t => (
            Term: (string)t!["term"]!,
            Exact: (bool?)t["exact"] ?? false,
            Instead: (string)t["instead"]!));
        var forms = register["forms"]!.AsArray().Select(f => (
            Pattern: new Regex((string)f!["pattern"]!, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
            Name: (string)f["form"]!,
            Instead: (string)f["instead"]!));
        return words
            .Select(w => (
                new Regex(@"(?<!\w)" + Regex.Escape(w.Term).Replace(@"\ ", @"\s+", StringComparison.Ordinal) + @"(?:s|es)?(?!\w)", RegexOptions.CultureInvariant | (w.Exact ? RegexOptions.None : RegexOptions.IgnoreCase)),
                w.Term,
                w.Instead))
            .Concat(forms)
            .ToList();
    }
}
