using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DbChange.Tests;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// synthetic-copy up where it stops short of a built copy: a row only a person can write, at exit 8 needs-input (the operator's ruling of
/// 2026-10-19), known to the plan before any row or met at a planted row SQL Server refuses; a copy whose plan from the base drops and
/// creates a check written with IN, at exit 5; and a local server on the host a named environment runs on, refused by R15 before up
/// sends SQL Server any statement.
/// </summary>
public sealed partial class SyntheticCopyTests
{
    /// <summary>
    /// PLT-120: an environment at the golden base holding an Order.Total of -5, counted for the change that adds CK_Order_Total CHECK
    /// (Total &gt;= 0). The plan knows the need before any row: the planter writes no literal that makes a check false, so only a person
    /// declares that row. up answers needs-input at exit 8, its one finding witness.needs-row naming [dbo].[Order] and the declared row
    /// to write; no row is planted, and the copy holds the seed's four orders.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_need_the_plan_knows_stops_up_at_needs_input_before_any_row_is_planted()
    {
        var head = repository.Committed(new SampleChange("CK_Order_Total added", [TotalChecked("Total >= 0")], []), null);
        var profile = Profile("a need the plan knows");
        try
        {
            var existing = await Environment(repository.Base, "UPDATE dbo.[Order] SET Total = -5 WHERE Id = 1;", head, profile);
            Assert.Contains(existing.States, s => s.Precondition.Kind is Kernel.PreconditionKind.Check && s.Violating);

            var (exit, answer) = Up("--at", repository.Base, "--existing-data", Written(existing), "--profile", profile);

            Assert.True(exit == 8, answer.ToJsonString());
            Assert.Equal("needs-input", (string?)answer["outcome"]);
            var need = Assert.Single(answer["findings"]!.AsArray())!;
            Assert.Equal(("witness.needs-row", "error", "[dbo].[Order]"),
                ((string)need["code"]!, (string)need["severity"]!, (string)need["subject"]!));
            Assert.StartsWith("Declare one row of [dbo].[Order] that makes the check false", (string)need["remedy"]!, StringComparison.Ordinal);
            Assert.Empty(answer["syntheticCopy"]!["planted"]!.AsArray());
            Assert.Equal(4, await SqlServerFixture.ScalarAsync(await Connection((string)answer["syntheticCopy"]!["copy"]!),
                "SELECT COUNT(*) FROM dbo.[Order];"));
        }
        finally
        {
            await Dropped();
        }
    }

    /// <summary>
    /// PLT-44: at a ref whose Order holds CK_Order_Total CHECK (Total &gt;= 10), which the seed's orders meet, an environment holding an
    /// Order.StatusText over the nine characters the change narrows it to. The planter reads no check's expression: the Order row it
    /// plants carries its ordinal Total, SQL Server refuses the INSERT by the check (Msg 547), and up answers needs-input at exit 8 with
    /// one witness.needs-row naming [dbo].[Order] and the check that rejects the planted literals.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_planted_row_a_check_of_the_copy_refuses_is_a_need_naming_the_check_at_needs_input()
    {
        var at = repository.Committed(new SampleChange("CK_Order_Total over ten", [TotalChecked("Total >= 10")], []), null);
        var head = repository.Committed(new SampleChange("Order.StatusText narrowed to nvarchar(9) under CK_Order_Total",
            [TotalChecked("Total >= 10"), new("Modules/Order.sql", "StatusText      NVARCHAR(20)", "StatusText      NVARCHAR(9) ")], []),
            null);
        var profile = Profile("a check that rejects a planted row");
        try
        {
            var existing = await Environment(at, "UPDATE dbo.[Order] SET StatusText = N'Backordered' WHERE Id = 2;", head, profile);

            var (exit, answer) = Up("--at", at, "--existing-data", Written(existing), "--profile", profile);

            Assert.True(exit == 8, answer.ToJsonString());
            Assert.Equal("needs-input", (string?)answer["outcome"]);
            var need = Assert.Single(answer["findings"]!.AsArray())!;
            Assert.Equal(("witness.needs-row", "[dbo].[Order]"), ((string)need["code"]!, (string)need["subject"]!));
            Assert.Equal("[dbo].[Order] rejects the planted literals: SQL Server refused the planted row by the check "
                + "[dbo].[CK_Order_Total].", (string)need["message"]!);
            Assert.Equal(0, Planted(answer, "[dbo].[Order]"));
        }
        finally
        {
            await Dropped();
        }
    }

    /// <summary>
    /// Step 8 and the operator's ruling of 2026-10-19: at the sample change add-a-check-written-with-in, whose CK_Order_StatusText SQL
    /// Server stores as a chain of ORs, up builds the copy and answers differs at exit 5, its one difference
    /// synthetic-copy.check-written-with-in, which names the IN the repository writes and the OR form the copy stores. Every plan against
    /// a database holding the check drops and creates it, so the copy holds the repository's schema as SQL Server stores it and is ready:
    /// check drift --target synthetic-copy at the ref resolves it and answers exit 5 with drift.check-written-with-in, and up again with
    /// the same inputs answers current, with nothing applied.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_copy_differing_by_a_check_written_with_IN_alone_answers_differs_naming_it_and_stays_the_target_check_drift_reads()
    {
        var at = repository.Committed(GoldenProject.Change("add-a-check-written-with-in"), null);
        var profile = Profile("a check written with IN");
        try
        {
            var (exit, answer) = Up("--at", at, "--profile", profile);

            Assert.True(exit == 5, answer.ToJsonString());
            Assert.Equal("differs", (string?)answer["outcome"]);
            var copy = (string)answer["syntheticCopy"]!["copy"]!;
            var stored = await SqlServerFixture.TextAsync(await Connection(copy),
                "SELECT definition FROM sys.check_constraints WHERE name = @name;", "CK_Order_StatusText");
            var cause = Assert.Single(answer["findings"]!.AsArray(), f => (string)f!["severity"]! != "note")!;
            Assert.Equal(("synthetic-copy.check-written-with-in", "warning", "CheckConstraint [dbo].[CK_Order_StatusText]"),
                ((string)cause["code"]!, (string)cause["severity"]!, (string)cause["subject"]!));
            Assert.Contains("StatusText IN (N'Pending', N'Shipped', N'Cancelled')", (string)cause["message"]!, StringComparison.Ordinal);
            Assert.EndsWith(": CHECK " + stored + ".", (string)cause["remedy"]!, StringComparison.Ordinal);

            var (drift, checkedDrift) = repository.Answer(["check", "drift", "--target", "synthetic-copy", "--at", at, "--profile", profile]);

            Assert.True(drift == 5, checkedDrift.ToJsonString());
            Assert.Contains(checkedDrift["findings"]!.AsArray(), f => (string?)f!["code"] == "drift.check-written-with-in");

            (exit, answer) = Up("--at", at, "--profile", profile);

            Assert.True(exit == 0, answer.ToJsonString());
            Assert.Equal(("current", copy), ((string?)answer["outcome"], (string?)answer["syntheticCopy"]!["copy"]));
        }
        finally
        {
            await Dropped();
        }
    }

    /// <summary>
    /// R15 before anything connects (V3_MILESTONES.md M1 exit 5; io/LocalServer.cs): with dbchange/environments.json naming an environment
    /// on localhost, the host the local server runs on, synthetic-copy up is copy.named-host at exit 9 and its answer names no query log,
    /// since the run sent SQL Server no statement. So it is with no row of the copy in the registry, and with a row of the same inputs
    /// whose build ended, which up answered current before the host was named; the copy built then stands.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task Up_on_the_host_a_named_environment_runs_on_is_refused_before_it_sends_a_statement_with_or_without_a_built_copy()
    {
        var profile = Profile("the host a named environment runs on");
        var environments = Path.Combine(repository.Root, "dbchange", "environments.json");
        var unnamed = File.ReadAllText(environments);
        void Named() => EnvironmentsJson.Dev("env:DBCHANGE_UNSET_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "localhost")
            .WriteTo(repository.Root);
        try
        {
            Named();
            var (exit, refused) = Up("--at", repository.Base, "--profile", profile);

            Assert.True(exit == 9, refused.ToJsonString());
            Assert.Equal("copy.named-host", (string?)Assert.Single(refused["findings"]!.AsArray())!["code"]);
            Assert.Null(refused["log"]);

            File.WriteAllText(environments, unnamed);
            var (built, answer) = Up("--at", repository.Base, "--profile", profile);
            Assert.True(built == 0, answer.ToJsonString());
            var copy = (string)answer["syntheticCopy"]!["copy"]!;
            Named();

            (exit, refused) = Up("--at", repository.Base, "--profile", profile);

            Assert.True(exit == 9, refused.ToJsonString());
            Assert.Equal("copy.named-host", (string?)Assert.Single(refused["findings"]!.AsArray())!["code"]);
            Assert.Null(refused["log"]);
            Assert.True(await SqlServerFixture.ExistsAsync(copy), copy + " is not on the local server");
        }
        finally
        {
            File.WriteAllText(environments, unnamed);
            await Dropped();
        }
    }

    /// <summary>
    /// R15 over the local server's linked servers (DECISIONS.md, 2026-10-21): dbchange/environments.json names an environment on a host
    /// of its own, and the local server holds a linked server whose data source is that host, as sp_addlinkedserver makes one for a SQL
    /// Server by its network name. A seed, a trigger or a declared witness on a copy there could reach the environment, so
    /// synthetic-copy up is copy.named-host at exit 9, naming the linked server, and its log holds one statement, the read of
    /// sys.servers: it made and planted nothing. So it is before any copy is built and after one is, which stands and is not answered
    /// current; with the linked server dropped, up builds the copy.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Value", "P1")]
    public async Task Up_on_a_local_server_linked_to_a_named_environment_s_host_is_refused_before_it_makes_or_plants_anything()
    {
        var profile = Profile("a linked server to a named environment's host");
        var environments = Path.Combine(repository.Root, "dbchange", "environments.json");
        var unnamed = File.ReadAllText(environments);
        var host = "dbchange-linked-" + Guid.NewGuid().ToString("N")[..8] + ".invalid";
        async Task Linked(bool linked) => await SqlServerFixture.ExecuteAsync(await SqlServerFixture.ServerAsync(), linked
            ? "EXEC sp_addlinkedserver @server = @name, @srvproduct = N'SQL Server';"
            : "IF EXISTS (SELECT 1 FROM sys.servers WHERE name = @name) EXEC sp_dropserver @server = @name, @droplogins = 'droplogins';",
            host);
        void Refused(int exit, System.Text.Json.Nodes.JsonNode answer)
        {
            Assert.True(exit == 9, answer.ToJsonString());
            var finding = Assert.Single(answer["findings"]!.AsArray())!;
            Assert.Equal("copy.named-host", (string?)finding["code"]);
            Assert.StartsWith("The local server holds the linked server [" + host + "], which names " + host, (string)finding["message"]!,
                StringComparison.Ordinal);
            var log = File.ReadAllText(Path.Combine(repository.Root, (string)answer["log"]!));
            Assert.Equal(1, log.Split("\nGO\n").Length - 1);
            Assert.Contains(" linked servers: ", log, StringComparison.Ordinal);
        }

        try
        {
            EnvironmentsJson.Dev("env:DBCHANGE_UNSET_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), host)
                .WriteTo(repository.Root);
            await Linked(true);

            var (exit, refused) = Up("--at", repository.Base, "--profile", profile);

            Refused(exit, refused);
            await Linked(false);
            var (built, answer) = Up("--at", repository.Base, "--profile", profile);
            Assert.True(built == 0, answer.ToJsonString());
            var copy = (string)answer["syntheticCopy"]!["copy"]!;
            await Linked(true);

            (exit, refused) = Up("--at", repository.Base, "--profile", profile);

            Refused(exit, refused);
            Assert.True(await SqlServerFixture.ExistsAsync(copy), copy + " is not on the local server");
        }
        finally
        {
            await Linked(false);
            File.WriteAllText(environments, unnamed);
            await Dropped();
        }
    }

    /// <summary>Order's check on Total, CK_Order_Total, with the predicate given.</summary>
    private static GoldenEdit TotalChecked(string predicate) => new("Modules/Order.sql", "CONSTRAINT PK_Order_Id PRIMARY KEY CLUSTERED (Id)",
        "CONSTRAINT PK_Order_Id PRIMARY KEY CLUSTERED (Id),\n    CONSTRAINT CK_Order_Total CHECK (" + predicate + ")");
}
