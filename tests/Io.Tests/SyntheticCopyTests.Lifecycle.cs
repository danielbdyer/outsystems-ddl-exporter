using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using DbChange.Kernel;
using DbChange.Tests;
using Xunit;
using static DbChange.Tests.Expect;

namespace DbChange.Io.Tests;

/// <summary>
/// synthetic-copy check, status and down (WP 3.6) on copies synthetic-copy up builds from the golden project: check holds a copy to law
/// 11′ in its planted form, reading it alone; status lists this checkout's registered copies and verifies each name against its row; down
/// drops them, and with --if-idle only while no dbchange process holds .dbchange/synthetic-copy.lock. A second checkout of the same
/// commits, whose up meets the first's copy as copy.foreign, sees that copy in a note from status and leaves it standing through down.
/// </summary>
public sealed partial class SyntheticCopyTests
{
    /// <summary>
    /// Law 11′ in its planted form and M3 exit 2 (R8, as the planter's determinism): an environment at the golden base holding each of
    /// R6's sites, counted for the change that makes them preconditions. The copy up plants at that existing data passes check: each table
    /// holds the rows up recorded, the one key of the base, FK_Order_Customer_CustomerId, is trusted and every row of Order finds its
    /// Customer, the copy answers every precondition as the environment does, and its plan from the base is empty. Dropped by down and
    /// planted again from the same inputs, restored from the cache, every table holds byte-identical rows, its content hash the same,
    /// and check passes again. A schema edit, a nullable column added to Status at a second ref with the same rows in the environment,
    /// replants only the table it touches: every other table hashes as at the base.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Law", "11′ a planted set repeats itself, replants only the touched tables and holds no orphan its existing data lacks")]
    [Trait("Exit", "M3.2")]
    public async Task Two_plantings_from_the_same_inputs_are_byte_identical_and_synthetic_copy_check_passes_on_each()
    {
        var head = repository.Committed(FiveSites, null);
        var profile = Profile("law 11′, planted");
        try
        {
            string[] inputs = ["--at", repository.Base, "--existing-data", Written(await Environment(repository.Base, FiveSitesRows, head,
                profile)), "--profile", profile];
            var (exit, first) = Up(inputs);
            Assert.True(exit == 0, first.ToJsonString());
            Assert.Equal("built", (string?)first["outcome"]);
            var copy = (string)first["syntheticCopy"]!["copy"]!;

            var (check, passed) = Synthetic(["check", .. inputs]);

            Assert.True(check == 0, passed.ToJsonString());
            Assert.Equal(("passed", copy), ((string?)passed["outcome"], (string?)passed["checked"]!["copy"]));
            var key = Assert.Single(passed["checked"]!["references"]!.AsArray())!;
            Assert.Equal(("ForeignKeyConstraint [dbo].[FK_Order_Customer_CustomerId]", "trusted", 0),
                ((string)key["reference"]!, (string)key["state"]!, (int)key["orphans"]!));
            Assert.All(passed["checked"]!["tables"]!.AsArray(), t => Assert.Equal((string?)t!["recorded"], (string?)t["hash"]));
            Assert.Equal(first["syntheticCopy"]!["answers"]!.ToJsonString(), passed["checked"]!["answers"]!.ToJsonString());

            var (down, dropped) = Synthetic("down");

            Assert.True(down == 0, dropped.ToJsonString());
            Assert.Equal("dropped", (string?)dropped["outcome"]);
            Assert.False(await SqlServerFixture.ExistsAsync(copy), copy + " is still on the local server");

            (exit, var second) = Up(inputs);

            Assert.True(exit == 0, second.ToJsonString());
            Assert.Equal(("built", true, copy), ((string?)second["outcome"], (bool?)second["syntheticCopy"]!["restored"],
                (string?)second["syntheticCopy"]!["copy"]));
            Assert.Equal(first["syntheticCopy"]!["planted"]!.ToJsonString(), second["syntheticCopy"]!["planted"]!.ToJsonString());
            Assert.Equal(first["syntheticCopy"]!["tables"]!.ToJsonString(), second["syntheticCopy"]!["tables"]!.ToJsonString());
            Assert.Equal(0, Synthetic(["check", .. inputs]).Exit);

            GoldenEdit noted = new("Modules/Status.sql", "IsActive    BIT             NOT NULL CONSTRAINT DF_Status_IsActive DEFAULT (1),",
                "IsActive    BIT             NOT NULL CONSTRAINT DF_Status_IsActive DEFAULT (1),\n    Note        NVARCHAR(20)    NULL,");
            var at = repository.Committed(new SampleChange("a nullable column on Status", [noted], []), null);
            var edited = repository.Committed(FiveSites with { Edits = [.. FiveSites.Edits, noted] }, null);
            (exit, var third) = Up("--at", at, "--existing-data", Written(await Environment(at, FiveSitesRows, edited, profile)),
                "--profile", profile);

            Assert.True(exit == 0, third.ToJsonString());
            Assert.Equal(first["syntheticCopy"]!["answers"]!.ToJsonString(), third["syntheticCopy"]!["answers"]!.ToJsonString());
            var before = Hashes(first);
            var after = Hashes(third);
            Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
            Assert.Equal(["[dbo].[Status]"], before.Keys.Where(t => before[t] != after[t]));
        }
        finally
        {
            await Dropped();
        }
    }

    /// <summary>
    /// status, check and down on the synthetic copy at the golden base. status lists the one copy, ready, its name verified against its
    /// row and its database present; check passes, and once a seeded row of Status is deleted from the copy it answers differs at exit
    /// 5, naming the table whose rows are not the ones up planted. A second checkout of the same commits holds no copy: its status notes
    /// the database as copy.foreign, another checkout's, and its down drops nothing and leaves the database standing. A row whose base
    /// fingerprint is not the one the name begins makes status answer differs. down --if-idle while another holder has
    /// .dbchange/synthetic-copy.lock answers busy and drops nothing; once the lock is free it drops the copy and its row, and check
    /// then refuses as copy.synthetic-absent.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Value", "O6")]
    public async Task Status_verifies_each_copy_against_its_row_and_down_drops_only_this_checkout_s_copies_and_only_while_idle()
    {
        var profile = Profile("status and down");
        var clone = repository.Clone();
        try
        {
            string[] inputs = ["--at", repository.Base, "--profile", profile];
            var (exit, built) = Up(inputs);
            Assert.True(exit == 0, built.ToJsonString());
            var copy = (string)built["syntheticCopy"]!["copy"]!;

            var (status, listed) = Synthetic("status");

            Assert.True(status == 0, listed.ToJsonString());
            Assert.Equal("listed", (string?)listed["outcome"]);
            var row = Assert.Single(listed["copies"]!.AsArray())!;
            Assert.Equal((copy, true, true, true, false), ((string)row["copy"]!, (bool)row["ready"]!, (bool)row["verified"]!,
                (bool?)row["present"], (bool)row["dropped"]!));
            Assert.Null(row["existingData"]);
            Assert.Equal(0, Synthetic(["check", .. inputs]).Exit);

            await SqlServerFixture.ExecuteAsync(await Connection(copy), "DELETE FROM dbo.Status WHERE Id = 3;");
            var (check, differs) = Synthetic(["check", .. inputs]);

            Assert.True(check == 5, differs.ToJsonString());
            Assert.Equal("differs", (string?)differs["outcome"]);
            var hash = Assert.Single(differs["findings"]!.AsArray())!;
            Assert.Equal(("synthetic-copy.hash", "[dbo].[Status]"), ((string)hash["code"]!, (string)hash["subject"]!));

            var (foreign, noted) = repository.AnswerAt(clone, "synthetic-copy", "status");
            Assert.True(foreign == 0, noted.ToJsonString());
            Assert.Empty(noted["copies"]!.AsArray());
            var note = Assert.Single(noted["findings"]!.AsArray(), f => (string?)f!["subject"] == copy)!;
            Assert.Equal(("copy.foreign", "note"), ((string)note["code"]!, (string)note["severity"]!));
            Assert.Contains("another checkout made it", (string)note["message"]!, StringComparison.Ordinal);
            var (left, none) = repository.AnswerAt(clone, "synthetic-copy", "down");
            Assert.True(left == 0, none.ToJsonString());
            Assert.Equal("dropped", (string?)none["outcome"]);
            Assert.True(await SqlServerFixture.ExistsAsync(copy), "the second checkout's down dropped " + copy);

            var registry = new LocalState(repository.Root).Copies;
            var kept = File.ReadAllText(registry);
            var tampered = JsonNode.Parse(kept)!;
            tampered["copies"]!.AsArray().Single(r => (string?)r!["name"] == copy)!["base"] = Fingerprint.Of("another base").ToString();
            File.WriteAllText(registry, tampered.ToJsonString());
            (status, var unverified) = Synthetic("status");
            File.WriteAllText(registry, kept);

            Assert.True(status == 5, unverified.ToJsonString());
            Assert.Equal(("synthetic-copy.fingerprint", copy), ((string?)unverified["findings"]![0]!["code"],
                (string?)unverified["findings"]![0]!["subject"]));

            using (Value(FileLock.Take(new LocalState(repository.Root).SyntheticLock, TimeSpan.FromSeconds(10))))
            {
                var (busy, idle) = Synthetic("down", "--if-idle");

                Assert.True(busy == 0, idle.ToJsonString());
                Assert.Equal("busy", (string?)idle["outcome"]);
                Assert.True(await SqlServerFixture.ExistsAsync(copy), "down --if-idle dropped " + copy + " under another holder's lock");
            }

            var (down, dropped) = Synthetic("down", "--if-idle");

            Assert.True(down == 0, dropped.ToJsonString());
            Assert.Equal("dropped", (string?)dropped["outcome"]);
            Assert.True((bool)Assert.Single(dropped["copies"]!.AsArray())!["dropped"]!);
            Assert.False(await SqlServerFixture.ExistsAsync(copy), copy + " is still on the local server");
            Assert.DoesNotContain(copy, File.ReadAllText(registry), StringComparison.Ordinal);
            Assert.Empty(Synthetic("status").Answer["copies"]!.AsArray());
            (check, var absent) = Synthetic(["check", .. inputs]);
            Assert.True(check == 9, absent.ToJsonString());
            Assert.Equal("copy.synthetic-absent", (string?)Assert.Single(absent["findings"]!.AsArray())!["code"]);
        }
        finally
        {
            Io.Git.Release(clone);
            await Dropped();
        }
    }

    /// <summary>synthetic-copy with its subcommand and arguments, run at the repository's root with --json.</summary>
    private (int Exit, JsonNode Answer) Synthetic(params string[] arguments) => repository.Answer(["synthetic-copy", .. arguments]);

    /// <summary>Each table up hashed, by name, with its content hash after the last step.</summary>
    private static System.Collections.Generic.Dictionary<string, string> Hashes(JsonNode answer) =>
        answer["syntheticCopy"]!["tables"]!.AsArray().ToDictionary(t => (string)t!["table"]!, t => (string)t!["hash"]!);
}
