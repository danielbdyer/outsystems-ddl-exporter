using System.Linq;
using DbChange.Cli;
using DbChange.Io;
using DbChange.Kernel;
using DbChange.Tests;
using Xunit;

namespace DbChange.Budgets.Tests;

public sealed partial class ContractTests
{
    /// <summary>
    /// The help names each outcome synthetic-copy check, status and down answer with, at its exit: check passed at exit 0 and differs at
    /// exit 5, status listed at exit 0 and differs at exit 5 where a name carries fingerprints its row does not record, and down dropped
    /// and busy at exit 0; each answer validates against the verb's schema, and an answer of one subcommand carries the others' content as
    /// null.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("check", true, "passed", 0)]
    [InlineData("check", false, "differs", 5)]
    [InlineData("status", true, "listed", 0)]
    [InlineData("status", false, "differs", 5)]
    [InlineData("down", true, "dropped", 0)]
    [InlineData("down", false, "busy", 0)]
    public void Each_answer_synthetic_copy_check_status_and_down_give_takes_an_outcome_its_help_lists_at_its_exit(string subcommand,
        bool clean, string word, int exit)
    {
        var copy = CopyName.Synthetic(Fingerprint.Of("a base"), Fingerprint.Of("an existing data"));
        var table = Expect.Value(Name.Of("dbo", "Status"));
        var key = Expect.Value(ElementKey.Of("ForeignKeyConstraint", Expect.Value(Name.Of("dbo", "FK_Order_Customer_CustomerId"))));
        Finding[] differs = [Finding.Warning("synthetic-copy.hash", table.ToString(),
            "[dbo].[Status] holds 3 rows whose content hash is not the one up recorded when it planted them.",
            "Run dbchange synthetic-copy down, then dbchange synthetic-copy up, to plant the copy again.")];
        var registered = new Io.SyntheticCopy.Registered(copy, "localhost,11434", true, Fingerprint.Of("a base"),
            Fingerprint.Of("an existing data"), clean, subcommand != "down", subcommand == "down" && clean);
        var answer = subcommand switch
        {
            "check" => Verbs.Checked(new Io.SyntheticCopy.Checked(clean, copy, Expect.Value(GitRef.Of("--at", "main")), new string('a', 40),
                    new string('b', 40), [new Io.SyntheticCopy.Rehash(table, 3, Fingerprint.Of("rows"), Fingerprint.Of(clean ? "rows" : "other"))],
                    [new Io.SyntheticCopy.Resolution(new TrustState(key, false, false), 0)], [], clean ? [] : differs, []),
                new Stamp(Expect.Value(DacFx.Version))),
            "status" => Verbs.Listed(new Io.SyntheticCopy.Listing(false, [registered], clean ? [] : [Finding.Warning(
                    "synthetic-copy.fingerprint", copy.ToString(), copy + " carries fingerprints that .dbchange/copies.json does not record.",
                    "Run dbchange synthetic-copy down, then dbchange synthetic-copy up, to build the copy again.")], []), down: false),
            _ => Verbs.Listed(new Io.SyntheticCopy.Listing(!clean, clean ? [registered] : [], [], []), down: true),
        };
        var listed = Render.Help()["verbs"]!.AsArray().Single(v => (string?)v!["name"] == "synthetic-copy")!["outcomes"]!.AsArray();

        Assert.Equal((word, exit), (answer.Outcome.Word, answer.Exit));
        Assert.Contains(listed, o => (string?)o!["outcome"] == word && o["exits"]!.AsArray().Any(e => (int)e! == exit));
        var json = Render.Json(answer);
        VerbAnswer.Valid("dbchange.synthetic-copy.1.schema.json", json);
        Assert.Equal(subcommand == "check" ? ["checked"] : ["copies"],
            new[] { "syntheticCopy", "checked", "copies" }.Where(k => json[k] is not null));
    }
}
