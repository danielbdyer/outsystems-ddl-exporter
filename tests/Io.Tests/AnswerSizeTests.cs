using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using DbChange.Cli;
using DbChange.Kernel;
using DbChange.Tests;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// VALUES.md O11 (decision 2.23): a payload that can be large is cut to its first entries by default, carries truncated and full,
/// and has a --summary form; the whole answer is in the run's answer.json.
/// </summary>
public sealed class AnswerSizeTests : IDisposable
{
    private const int Columns = 3000;

    private readonly ScratchFolder root = ScratchFolder.Temporary("answer-size");

    public void Dispose() => root.Dispose();

    /// <summary>
    /// A change of 3,000 altered columns, built in memory and rendered as diff's answer: the Markdown holds the first entries and ends
    /// with the line naming the whole answer's file; the JSON's altered list holds the first entries, its counts the whole change, and
    /// truncated true; the file full names parses, holds every entry and says truncated false; --summary holds no entry and the same counts.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "O11")]
    public void A_large_change_renders_short()
    {
        var (markdownExit, markdown) = Run(["diff"]);
        var (jsonExit, json) = Run(["diff", "--json"]);
        var (summaryExit, summary) = Run(["diff", "--json", "--summary"]);

        Assert.Equal((0, 0, 0), (markdownExit, jsonExit, summaryExit));
        var lines = markdown.TrimEnd('\n').Split('\n');
        Assert.True(lines.Length <= Render.Shown + 10, "the Markdown has " + lines.Length + " lines");
        Assert.StartsWith("Column [dbo].[T0000].[C]: Length 300 → 256", lines[0], StringComparison.Ordinal);
        Assert.Matches("^… 2,950 more; the whole answer is in \\.dbchange/runs/[^/]+/answer\\.json\\.$", lines[^1]);

        var answer = JsonNode.Parse(json)!;
        ScratchRepository.Valid("dbchange.diff.1.schema.json", answer);
        Assert.Equal(Render.Shown, answer["diff"]!["change"]!["altered"]!.AsArray().Count);
        Assert.Equal((Columns, true), ((int)answer["diff"]!["counts"]!["altered"]!, (bool)answer["truncated"]!));
        var full = (string)answer["full"]!;
        Assert.Matches("^\\.dbchange/runs/[^/]+/answer\\.json$", full);
        var whole = JsonNode.Parse(File.ReadAllText(root.Under(full)))!;
        ScratchRepository.Valid("dbchange.diff.1.schema.json", whole);
        Assert.Equal((Columns, false, (string?)null), (whole["diff"]!["change"]!["altered"]!.AsArray().Count, (bool)whole["truncated"]!, (string?)whole["full"]));

        var summarised = JsonNode.Parse(summary)!;
        ScratchRepository.Valid("dbchange.diff.1.schema.json", summarised);
        Assert.Empty(summarised["diff"]!["change"]!["altered"]!.AsArray());
        Assert.Equal((Columns, true), ((int)summarised["diff"]!["counts"]!["altered"]!, (bool)summarised["truncated"]!));
    }

    /// <summary>dbchange diff run in this process with a body that answers the large change, from a checkout under the scratch folder.</summary>
    private (int Exit, string Output) Run(string[] arguments)
    {
        var diff = Contract.Verbs.Single(v => v.Name == "diff") with { Body = (_, _) => Verbs.Diff(Side("dacpac:before.dacpac"), Side("dacpac:after.dacpac"), Large(), Collation.CaseSensitive, false, new Stamp(Ok(DacFx.Version))) };
        using var output = new MemoryStream();
        var exit = Cli.Program.Run(arguments, output, () => new Checkout(root.Path, root.Path, null), [diff]);
        return (exit, Encoding.UTF8.GetString(output.ToArray()));
    }

    private static Verbs.Source Side(string target) => new(Expect.Value(SqlServer.Target(target, "--from")), new Ssdt.ModelElements([], []), null, false);

    /// <summary>One column of each of 3,000 tables, its Length altered from 300 to 256.</summary>
    private static Change Large() => new([], [], [], SortedArray.Of(Enumerable.Range(0, Columns).Select(i =>
        new Change.Alteration(
            Expect.Value(ElementKey.Of(Expect.Value(ElementKey.Of("Table", Expect.Value(Name.Of("dbo", "T" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture))))), "Column", Expect.Value(Name.Of("C")))),
            [new Change.Property("Length", new Value.Integer(300), new Value.Integer(256))],
            []))));

    private static T Ok<T>(Result<T> result) => result.Match(value => value, error => throw new Xunit.Sdk.XunitException(error.Code + ": " + error.Message));
}
