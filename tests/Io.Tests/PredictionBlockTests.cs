using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json.Nodes;
using CsCheck;
using DbChange.Kernel;
using DbChange.Tests;
using Xunit;
using static DbChange.Tests.Expect;

namespace DbChange.Io.Tests;

/// <summary>
/// What predict writes for others to read (WP 2.5): the existing data's file, dbchange.existing-data/1, which synthetic-copy up reads with
/// --existing-data, now holding every kind of precondition; and the machine block that ends predict's Markdown, one line of JSON per
/// environment, which the gate (M5) reads back from a pull request's description with the same parser.
/// </summary>
public sealed class PredictionBlockTests
{
    /// <summary>
    /// WP 2.1b's seven kinds once wrote nothing but their name into the file, and a file holding one was refused as none of the six:
    /// now an existing data of every kind of precondition, each with what its kind holds, its state and its classes, and a trust list,
    /// reads back from the file dbchange writes as the existing data it was written from; and the generator meets all thirteen kinds.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void An_existing_data_of_every_kind_of_precondition_reads_back_from_its_file_as_it_was_written()
    {
        var seen = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        PredictionProperties.Preconditions.Sample(p => seen[p.Kind.Word] = true, iter: 2000);

        PredictionProperties.ExistingData.Sample(existing =>
            Value(ExistingDataFile.Parse(ExistingDataFile.Text(existing), "existing-data.json")) == existing);
        Assert.Equal(["Authored", "Check", "Computed", "Fits", "ForeignKey", "KeyLength", "NotNull", "Rebuild", "Scan", "SeedDrift",
            "TableEmpty", "Temporal", "Unique"], seen.Keys.Order(StringComparer.Ordinal));
    }

    /// <summary>A Scan or a Rebuild reads the rows and no value fails its statement, so a file that answers one violating holds a state no
    /// count gives, and a gapped class belongs to a Rebuild alone; each is existing-data.malformed, naming the place.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("{ \"kind\": \"Scan\", \"table\": \"[dbo].[T]\" }, \"populated\": true, \"violating\": true",
        "$.states[0] answers a Scan violating, which no row's value fails.")]
    [InlineData("{ \"kind\": \"NotNull\", \"table\": \"[dbo].[T]\", \"columns\": [\"[C]\"] }, \"populated\": true, \"violating\": true, "
        + "\"classes\": [\"gapped\"]", "$.states[0].classes[0] is no class a state of the precondition's kind holds.")]
    [InlineData("{ \"kind\": \"KeyLength\", \"table\": \"[dbo].[T]\", \"columns\": [\"[C]\"], \"index\": \"[IX]\" }, \"populated\": true, "
        + "\"violating\": true", "$.states[0].precondition.clustered is missing.")]
    public void A_file_whose_state_no_count_gives_is_malformed_naming_the_place(string state, string said)
    {
        var error = Failed(ExistingDataFile.Parse("{ \"schema\": \"dbchange.existing-data/1\", \"states\": [ { \"precondition\": " + state
            + " } ] }", "existing-data.json"), "existing-data.malformed");

        Assert.Contains(said, error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The block round-trips through its parser: generated predictions of one to three environments, each with its existing data of every
    /// kind, its counts and its provenance, are read back from the Markdown they are written into as the predictions written, in order;
    /// and two blocks in one description, a developer's for Dev and a lead's for QA and UAT, read as both, in the order they stand.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Each_prediction_reads_back_from_the_machine_block_it_was_written_into()
    {
        PredictionProperties.Predictions.Sample(predictions =>
            Value(PredictionBlock.Read("predicted · read-only\n\n" + Fenced(predictions) + "\nNot checked.\n")).SequenceEqual(predictions));
        Gen.Select(PredictionProperties.Of("dev"), PredictionProperties.Of("qa")).Sample((dev, qa) =>
            Value(PredictionBlock.Read(Fenced([dev]) + "\n\nThe lead's:\n\n" + Fenced([qa]))).SequenceEqual([dev, qa]));
    }

    /// <summary>
    /// What the gate must not take as a prediction is refused as prediction.malformed, naming the block's line and quoting nothing: a line
    /// that is no JSON object, a key the form does not name, an existing data edited by hand, which its provenance's fingerprint no longer
    /// names, a provenance about another environment, one environment given twice, and a fence left open.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("no JSON", "line 2 of the machine block is no JSON object")]
    [InlineData("an unknown key", "line 2 of the machine block holds a key the form does not name")]
    [InlineData("an edited existing data", "line 2 of the machine block holds an existing data whose fingerprint its provenance does not name")]
    [InlineData("another environment", "line 2 of the machine block holds a provenance about env:qa")]
    [InlineData("an environment twice", "the machine block predicts env:dev twice")]
    [InlineData("an open fence", "a machine block opened at line 1 is never closed")]
    public void A_block_the_gate_must_not_take_as_a_prediction_is_malformed_naming_its_line(string flaw, string said)
    {
        var dev = PredictionProperties.Of("dev").Single();
        var line = PredictionBlock.Machine([dev])[1];
        var json = JsonNode.Parse(line)!.AsObject();
        string[] lines = flaw switch
        {
            "no JSON" => ["```" + PredictionBlock.Schema, "{ \"environment\": ", "```"],
            "an unknown key" => ["```" + PredictionBlock.Schema, Edited(json, j => j["Pa55!planted#7f3a"] = 1), "```"],
            "an edited existing data" => ["```" + PredictionBlock.Schema, Edited(json, j => j["existingData"]!["states"] = JsonNode.Parse(
                "[{\"precondition\": {\"kind\": \"TableEmpty\", \"table\": \"[dbo].[Edited]\", \"columns\": []}, \"populated\": false, "
                + "\"violating\": false, \"classes\": []}]")), "```"],
            "another environment" => ["```" + PredictionBlock.Schema, Edited(json, j => j["provenance"]!["target"] = "env:qa"), "```"],
            "an environment twice" => ["```" + PredictionBlock.Schema, line, line, "```"],
            _ => ["```" + PredictionBlock.Schema, line],
        };

        var error = Failed(PredictionBlock.Read(string.Join('\n', lines) + "\n"), "prediction.malformed");

        Assert.Contains(said, error.Message, StringComparison.Ordinal);
        PlantedValue.Password.AbsentFrom(error);
    }

    /// <summary>A text that holds no machine block holds no prediction: a description no lead has pasted a block into yet.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_text_with_no_machine_block_holds_no_prediction() =>
        Assert.Empty(Value(PredictionBlock.Read("## The data\n\n```sql\nSELECT 1;\n```\n")));

    private static string Fenced(Kernel.Prediction[] predictions) => string.Join('\n', PredictionBlock.Machine(predictions));

    private static string Edited(JsonObject json, Action<JsonObject> edit)
    {
        var copy = json.DeepClone().AsObject();
        edit(copy);
        return copy.ToJsonString();
    }
}
