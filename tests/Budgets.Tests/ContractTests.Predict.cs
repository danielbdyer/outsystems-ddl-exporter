using System;
using System.Linq;
using System.Text.Json.Nodes;
using DbChange.Cli;
using DbChange.Io;
using DbChange.Kernel;
using DbChange.Tests;
using Xunit;
using Outcome = DbChange.Kernel.Outcome;

namespace DbChange.Budgets.Tests;

/// <summary>predict's contract (WP 2.5): each answer it gives, against its committed schema and its help.</summary>
public sealed partial class ContractTests
{
    /// <summary>
    /// Each kind of answer predict gives, for Dev predicted beside UAT whose login was refused, validates against
    /// dbchange.predict.1.schema.json and takes an outcome its help lists at its exit: applies at exit 0, blocks at exit 3 naming
    /// BlockOnPossibleDataLoss, which stops it, and unpredicted at exit 0 for a precondition no read-only query counts. UAT is a warning
    /// carrying the denied login's sentence, and the block in the JSON is the Markdown predict prints, its machine block holding Dev's
    /// prediction alone, which reads back as itself.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("applies", 0)]
    [InlineData("blocks", 3)]
    [InlineData("unpredicted", 0)]
    public void Each_answer_predict_gives_validates_against_its_schema_and_takes_an_outcome_its_help_lists_at_its_exit(string word,
        int exit)
    {
        var customer = Expect.Value(Name.Of("dbo", "Customer"));
        var (empty, email) = (new Precondition(customer, [], new PreconditionKind.TableEmpty()),
            new Precondition(customer, [Expect.Value(Name.Of("Email"))], new PreconditionKind.NotNull()));
        var rows = word == "blocks" ? 6 : 0;
        var counts = SortedArray.Of(new PreconditionCount(empty, rows, rows), new PreconditionCount(email, rows, rows / 2));
        var existing = ExistingData.Of(counts);
        var outcome = Expect.Value(Outcome.Predict(existing, [new BlockOnPossibleDataLossSite(customer, "")], [empty, email], []));
        var dev = Expect.Value(EnvironmentName.Of("test", "dev"));
        var prediction = new Prediction(dev, existing, counts, Provenance.Prediction(Fingerprint.Of("change"), Fingerprint.Of("schema"),
            ExistingDataFile.FingerprintOf(existing), Expect.Value(DacFx.Version), null, Fingerprint.Of("profile"),
            new Target.Environment(dev), DateTimeOffset.FromUnixTimeSeconds(1_800_000_000)));
        var denied = new Error("server.denied",
            "env:uat refused this identity (Msg 18456, SQL Server's message withheld); a lead's prediction will appear on the pull request.",
            "Ask a lead to predict for env:uat, or ask its DBA for VIEW DEFINITION and db_datareader there.");
        var answer = Verbs.Predicted(new Io.Predict.Answer(Expect.Value(GitRef.Of("--at", "HEAD")), new string('a', 40), new string('b', 40),
            Fingerprint.Of("change"), DateTimeOffset.FromUnixTimeSeconds(1_800_000_000),
            [
                new(dev, new Io.Predict.Predicted(prediction, outcome,
                    word == "unpredicted" ? [new AggregateQueries.Unanswered(email, "not predictable read-only: a test says so")] : [],
                    [], [])),
                new(Expect.Value(EnvironmentName.Of("test", "uat")), denied),
            ], []), new Stamp(Expect.Value(DacFx.Version), new Pin.Unpinned()));
        var listed = Render.Help()["verbs"]!.AsArray().Single(v => (string?)v!["name"] == "predict")!["outcomes"]!.AsArray();
        var json = Render.Json(answer);

        Assert.Equal((word, exit), (answer.Outcome.Word, answer.Exit));
        Assert.Contains(listed, o => (string?)o!["outcome"] == word && o["exits"]!.AsArray().Any(e => (int)e! == exit));
        VerbAnswer.Valid("dbchange.predict.1.schema.json", json);
        Assert.Equal(word == "blocks" ? "block-on-possible-data-loss" : null, (string?)json["blockedBy"]);
        var uat = Assert.Single(answer.Findings, f => f.Subject == "env:uat");
        Assert.Equal(("server.denied", Severity.Warning, denied.Message), (uat.Code, uat.Severity, uat.Message));
        Assert.Equal(string.Join('\n', answer.Lines) + "\n", (string)json["predict"]!["block"]!);
        Assert.Equal([prediction], Expect.Value(PredictionBlock.Read((string)json["predict"]!["block"]!)));
        Assert.StartsWith("#### predicted · read-only · 2027-01-15T08:00:00Z · DacFx " + Expect.Value(DacFx.Version), answer.Lines[0],
            StringComparison.Ordinal);
    }
}
