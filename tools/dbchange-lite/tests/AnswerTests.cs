using System;
using System.IO;
using System.Linq;
using DbChangeLite;
using Xunit;

namespace DbChangeLite.Tests;

public sealed class AnswerTests : IDisposable
{
    private const string Closed = "DBCHANGE_LITE_TEST_CLOSED";
    private const string Unset = "DBCHANGE_LITE_TEST_UNSET";

    private readonly Scratch scratch = new();

    public AnswerTests()
    {
        scratch.Write("dbchange/environments.json", "{ \"environments\": { \"dev\": { \"host\": \"dev.invalid\", \"connection\": \"env:" + Closed + "\", \"profile\": \"profiles/pipeline.publish.xml\", \"deployOrder\": 1 },"
            + " \"qa\": { \"host\": \"qa.invalid\", \"connection\": \"env:" + Unset + "\", \"profile\": \"profiles/pipeline.publish.xml\", \"deployOrder\": 2 },"
            + " \"prod\": { \"host\": \"prod.invalid\", \"connection\": \"env:" + Unset + "\", \"profile\": \"profiles/pipeline.publish.xml\", \"neverRead\": true, \"deployOrder\": 4 } } }");
        scratch.Write("profiles/pipeline.publish.xml", File.ReadAllText(Path.Combine(Repo.Golden, "project", "profiles", "pipeline.publish.xml")));
        Dacpacs.Of(scratch.Under("change.dacpac"), "CREATE TABLE [dbo].[Customer] ([Id] INT NOT NULL PRIMARY KEY, [Email] NVARCHAR(256) NULL);");
        Environment.SetEnvironmentVariable(Closed, "Server=127.0.0.1,1;Initial Catalog=Estate;User ID=reader;Password=unused;Connect Timeout=2;Encrypt=False");
        Environment.SetEnvironmentVariable(Unset, null);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Predict_answers_an_environment_it_cannot_reach_and_one_this_machine_has_no_connection_for_as_open_items_at_exit_8()
    {
        var (exit, answer) = Cli.Run(scratch.Path, "predict", "--env", "dev,qa", "--to", "dacpac:change.dacpac", "--onto", "none");
        Assert.True(exit == 8, answer.GetProperty("message").GetString());
        Assert.Equal("dev not read; qa not read.", answer.GetProperty("confidence").GetString());
        Assert.DoesNotContain(answer.GetProperty("findings").EnumerateArray(), f => f.GetProperty("code").GetString() == "internal.unexpected");
        var commands = answer.GetProperty("open").EnumerateArray().Select(o => o.GetProperty("next").GetProperty("command").GetString()!).ToList();
        Assert.Equal(["dbchange-lite predict --env dev --to dacpac:change.dacpac --onto none", "dbchange-lite predict --env qa --to dacpac:change.dacpac --onto none"], commands);
        Assert.All(commands, c => Assert.Equal("predict", Contract.Parse(c.Split(' ').Skip(1).ToArray()).Verb.Name));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Predict_of_a_never_read_environment_alone_says_that_nothing_was_read_and_states_its_worst_case()
    {
        Dacpacs.Of(scratch.Under("release.dacpac"), "CREATE TABLE [dbo].[Customer] ([Id] INT NOT NULL PRIMARY KEY, [Email] NVARCHAR(256) NULL, [Region] NVARCHAR(40) NULL);");
        var (exit, answer) = Cli.Run(scratch.Path, "predict", "--env", "prod", "--to", "dacpac:change.dacpac", "--onto", "none", "--at", "dacpac:release.dacpac");
        Assert.True(exit == 0, answer.GetProperty("message").GetString());
        Assert.Equal("No environment was read; prod blocks if [dbo].[Customer] has rows.", answer.GetProperty("message").GetString());
        Assert.Equal("prod assumed.", answer.GetProperty("confidence").GetString());
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Predict_refuses_an_env_flag_that_names_no_environment_instead_of_answering_done()
    {
        var (exit, answer) = Cli.Run(scratch.Path, "predict", "--env", ",", "--to", "dacpac:change.dacpac", "--onto", "none");
        Assert.Equal((1, "env.empty"), (exit, answer.GetProperty("findings")[0].GetProperty("code").GetString()));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_run_folder_that_cannot_be_made_is_configuration_refused_with_its_remedy_and_never_a_crash()
    {
        File.WriteAllText(scratch.Under(".dbchange"), "");
        var (exit, answer) = Cli.Run(scratch.Path, "doctor");
        Assert.Equal((6, "run.folder"), (exit, answer.GetProperty("findings")[0].GetProperty("code").GetString()));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Doctor_notes_an_environment_this_machine_holds_no_connection_for_instead_of_failing_on_it()
    {
        var (_, answer) = Cli.Run(scratch.Path, "doctor");
        var qa = answer.GetProperty("findings").EnumerateArray().Single(f => f.GetProperty("code").GetString() == "doctor.read-of-qa");
        Assert.Equal("note", qa.GetProperty("severity").GetString());
        Assert.DoesNotContain("read of qa", answer.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_summary_of_a_long_answer_keeps_every_finding_and_drops_only_the_plans_operations()
    {
        var run = Runs.At(scratch.Path, "predict");
        var findings = Enumerable.Range(0, 30).Select(i => Finding.Error("check.fires", "[dbo].[T" + i + "]", "On dev the table has rows.", "Ship it in two releases.")).ToList();
        var operations = Enumerable.Range(0, 400).Select(i => new PlanOperationAnswer("Create", "SqlTable", "[dbo].[T" + i + "]", null)).ToList();
        var answer = Answer.Of("predict", Contract.Of("blocked"), "dev blocks.", findings, run) with
        {
            Environments = [new EnvironmentAnswer("dev", 3, null, "blocks", "block-on-possible-data-loss", "extract", "", null, [], [], new PlanAnswer(operations, [], [], []), [], null)],
        };
        var summary = answer.Written(run);
        Assert.True(summary.Truncated);
        Assert.Equal(30, summary.Findings.Count);
        Assert.Empty(summary.Environments![0].Plan!.Operations);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Closed, null);
        scratch.Dispose();
    }
}
