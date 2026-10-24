using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using DbChangeLite;
using Xunit;

namespace DbChangeLite.Tests;

public sealed class ContractTests
{
    private const string Frozen = "# The frozen exit table: a code is added here in the change that adds it to cli/Contract.cs; no line is ever removed, renamed or renumbered.\n"
        + "0 done\n1 bad-arguments\n2 unparsed-input\n3 blocked\n4 unreachable\n5 differs\n6 configuration-refused\n7 build-failed\n8 needs-input\n9 refused-by-name\n130 interrupted\n";

    [Fact]
    [Trait("Category", "fast")]
    public void The_exit_table_is_v3s_frozen_table_byte_for_byte()
    {
        var own = File.ReadAllText(Path.Combine(Repo.Src, "exits.frozen"));
        Assert.Equal(Frozen, own);
        var v3 = Path.Combine(Repo.Root, "cli", "exits.frozen");
        if (File.Exists(v3))
        {
            Assert.Equal(File.ReadAllText(v3), own);
        }

        Assert.Equal(Contract.Frozen(Repo.Src).Select(f => (f.Code, f.Name)), Contract.Exits.Select(e => (e.Code, e.Name)));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Every_outcome_of_every_verb_pairs_with_its_exit_as_the_frozen_table_does()
    {
        foreach (var outcome in Contract.Verbs.SelectMany(v => v.Outcomes))
        {
            Assert.Equal(Contract.Of(outcome.Word).Exit, outcome.Exit);
            Assert.Contains(Contract.Exits, e => e.Code == outcome.Exit);
        }

        Assert.All(Contract.Exits, e => Assert.EndsWith(".", e.Remedy, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Help_as_data_lists_every_verb_with_its_flags_and_the_stopping_rule()
    {
        using var scratch = new Scratch();
        var (exit, answer) = Cli.Run(scratch.Path, "--help", "--json");
        Assert.Equal(0, exit);
        Assert.Equal("dbchange-lite.help/1", answer.GetProperty("schema").GetString());
        Assert.Equal(["doctor", "drift", "diff", "predict", "prove"], answer.GetProperty("verbs").EnumerateArray().Select(v => v.GetProperty("name").GetString()!).ToArray());
        Assert.Contains("open", answer.GetProperty("stoppingRule").GetString(), StringComparison.Ordinal);
        Assert.Equal(11, answer.GetProperty("exits").GetArrayLength());
    }

    [Fact]
    [Trait("Category", "fast")]
    public void An_unknown_verb_a_missing_flag_and_an_unknown_flag_are_bad_arguments()
    {
        using var scratch = new Scratch();
        var (exit, answer) = Cli.Run(scratch.Path, "deploy");
        Assert.Equal(1, exit);
        Assert.Equal("bad-arguments", answer.GetProperty("outcome").GetString());
        Assert.Equal(1, Cli.Run(scratch.Path, "drift", "--env", "dev").Exit);
        Assert.Equal(1, Cli.Run(scratch.Path, "predict", "--env", "dev", "--rows", "x").Exit);
        Assert.Throws<Stop>(() => Contract.Parse(["predict", "--env"]));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_verb_run_without_an_environments_file_is_refused_with_the_file_named()
    {
        using var scratch = new Scratch();
        var (exit, answer) = Cli.Run(scratch.Path, "drift", "--env", "dev", "--at", "ref:HEAD");
        Assert.Equal(6, exit);
        Assert.Equal("configuration-refused", answer.GetProperty("outcome").GetString());
        var finding = answer.GetProperty("findings").EnumerateArray().Single();
        Assert.Equal("environments.missing", finding.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(finding.GetProperty("remedy").GetString()));
        Assert.True(File.Exists(Path.Combine(scratch.Path, ".dbchange", "runs", Directory.GetDirectories(Path.Combine(scratch.Path, ".dbchange", "runs")).Select(Path.GetFileName).Single()!, "answer.json")));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_answer_pairs_blockedBy_with_exit_3_alone_and_names_the_queries_log()
    {
        using var scratch = new Scratch();
        var run = Runs.At(scratch.Path, "predict");
        var answer = Answer.Of("predict", Contract.Of("done"), "Done.", [], run);
        Assert.Null(answer.BlockedBy);
        Assert.StartsWith(".dbchange/runs/", answer.Log, StringComparison.Ordinal);
        Assert.EndsWith("-predict/queries.log", answer.Log, StringComparison.Ordinal);
        var text = answer.Text();
        using var parsed = JsonDocument.Parse(text);
        Assert.Equal("dbchange-lite.predict/1", parsed.RootElement.GetProperty("schema").GetString());
        Assert.False(parsed.RootElement.TryGetProperty("blockedBy", out _));
    }
}
