using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using DbChange.Budgets.Tests;
using DbChange.Kernel;
using DbChange.Tests;
using Xunit;
using static DbChange.Tests.Expect;

namespace DbChange.Io.Tests;

/// <summary>
/// synthetic-copy up (WP 3.5; the Facsimile's copy build, its ARCHITECTURE.md §5, steps 1 to 8), run by dbchange in this process on the
/// golden project of a scratch repository. Each test writes a profile of its own, the pipeline's with a comment naming the test, so the
/// base's fingerprint, and with it the synthetic copy's name and its backup's key, are the test's alone; each drops the synthetic copies
/// and backups it made. An environment is a registered copy the test publishes a ref to and writes rows into; its existing data is
/// counted as the copy build counts its own (SyntheticCopy.Ask and Count, as predict will count an environment's), with the trust state
/// of each foreign key and check read through SqlServer.Measure. No value the environment holds reaches the copy: the file holds
/// booleans.
/// </summary>
[Collection(PublishedToolCollection.Name)]
public sealed class SyntheticCopyTests(ScratchRepository repository) : IClassFixture<ScratchRepository>
{
    /// <summary>
    /// R6's five sites as one change to the golden project: Customer.ContactPhone made NOT NULL and Customer.Region narrowed to
    /// nvarchar(7), which BlockOnPossibleDataLoss checks on a populated Customer; a foreign key from CustomerAddress to Customer; a unique
    /// constraint on Account.Name; and a foreign key from Order to Status, which no seeded row breaks.
    /// </summary>
    private static readonly SampleChange FiveSites = new("R6's five sites",
    [
        new GoldenEdit("Modules/Customer.sql", "ContactPhone    NVARCHAR(40)    NULL,", "ContactPhone    NVARCHAR(40)    NOT NULL,"),
        new GoldenEdit("Modules/Customer.sql", "Region          NVARCHAR(50)    NULL,", "Region          NVARCHAR(7)     NULL,"),
        new GoldenEdit("Modules/CustomerAddress.sql", "CustomerId  INT             NOT NULL,",
            "CustomerId  INT             NOT NULL CONSTRAINT FK_CustomerAddress_Customer_CustomerId REFERENCES dbo.Customer (Id),"),
        new GoldenEdit("Modules/Account.sql", "CONSTRAINT PK_Account_Id PRIMARY KEY CLUSTERED (Id)",
            "CONSTRAINT PK_Account_Id PRIMARY KEY CLUSTERED (Id),\n    CONSTRAINT UQ_Account_Name UNIQUE (Name)"),
        new GoldenEdit("Modules/Order.sql", "CONSTRAINT PK_Order_Id PRIMARY KEY CLUSTERED (Id)",
            "CONSTRAINT PK_Order_Id PRIMARY KEY CLUSTERED (Id),\n"
                + "    CONSTRAINT FK_Order_Status_StatusId FOREIGN KEY (StatusId) REFERENCES dbo.Status (Id)"),
    ], []);

    /// <summary>The rows that put each of R6's sites into an environment at the golden base: a NULL, a value over the new length, an
    /// orphan and a duplicate, on tables the seed populates.</summary>
    private const string FiveSitesRows =
        "UPDATE dbo.Customer SET ContactPhone = NULL WHERE Id = 3; UPDATE dbo.Customer SET Region = N'Northwest' WHERE Id = 1; "
        + "INSERT INTO dbo.CustomerAddress (CustomerId, Line1) VALUES (999, N'9 Nowhere Lane'); "
        + "INSERT INTO dbo.Account (Name) VALUES (N'Acme Holdings');";

    /// <summary>
    /// WP 3.5's first done-when (R12, the synthetic copy's half): synthetic-copy up at the golden base, with no existing data, builds a
    /// copy named dbchange_synthetic_&lt;16 hex&gt;_existing-data, publishes the base with its seed and plants no presence row, every table
    /// the seed populates holding three rows or more; check drift --target synthetic-copy at the base exits 0; up again with the same
    /// inputs answers current, with nothing applied. VALUES.md O6: a second checkout of the same commits names the same copy and is
    /// refused it (copy.foreign, exit 9), the copy left standing. VALUES.md R3: git status lists nothing the build wrote.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Value", "O6")]
    [Trait("Value", "R3")]
    public async Task Check_drift_of_the_synthetic_copy_built_at_the_golden_base_exits_0_and_up_again_answers_it_current()
    {
        var profile = Profile("in sync");
        try
        {
            var (exit, answer) = Up("--at", repository.Base, "--profile", profile);

            Assert.True(exit == 0, answer.ToJsonString());
            Assert.Equal("built", (string?)answer["outcome"]);
            var copy = (string)answer["syntheticCopy"]!["copy"]!;
            Assert.Matches("^dbchange_synthetic_[0-9a-f]{16}_existing-data$", copy);
            Assert.True(await SqlServerFixture.ExistsAsync(copy), copy + " is not on the local server");
            Assert.Equal(5, await SqlServerFixture.ScalarAsync(await Connection(copy), "SELECT COUNT(*) FROM dbo.Customer;"));
            Assert.All(answer["syntheticCopy"]!["planted"]!.AsArray(), p => Assert.Equal(0, (int)p!["planted"]!));
            var (drift, output) = Drift(repository.Base, profile);
            Assert.True(drift == 0, output);
            Assert.StartsWith("synthetic-copy is in sync with ref:" + repository.Base, output, StringComparison.Ordinal);

            (exit, answer) = Up("--at", repository.Base, "--profile", profile);

            Assert.True(exit == 0, answer.ToJsonString());
            Assert.Equal(("current", copy), ((string?)answer["outcome"], (string?)answer["syntheticCopy"]!["copy"]));
            var clone = repository.Clone();
            try
            {
                var (foreign, refused) = repository.AnswerAt(clone, "synthetic-copy", "up", "--at", repository.Base, "--profile", profile);

                Assert.True(foreign == 9, refused.ToJsonString());
                Assert.Equal("copy.foreign", (string?)Assert.Single(refused["findings"]!.AsArray())!["code"]);
                Assert.True(await SqlServerFixture.ExistsAsync(copy), "the second checkout's up dropped " + copy);
            }
            finally
            {
                Io.Git.Release(clone);
            }

            Assert.Equal("", repository.Status());
        }
        finally
        {
            await Dropped();
        }
    }

    /// <summary>
    /// R6 and M3's done-when: an environment at the golden base holding each of R6's sites (rows, a NULL, an orphan, a duplicate and a
    /// value over the new length), and a foreign key no row breaks, counted for the change that makes them preconditions. The copy planted
    /// at that existing data, its duplicate declared in dbchange/witnesses.sql with a name the seed defines, answers every precondition as
    /// the environment does, every no included; the base, published once, is restored from the cache for it; and its plan from the base is
    /// empty, check drift --target synthetic-copy exiting 0.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    [Trait("Exit", "M3.3")]
    public async Task A_copy_planted_at_an_environment_holding_each_of_R6_s_sites_answers_every_precondition_as_the_environment_does()
    {
        var head = repository.Committed(FiveSites,
            "-- witness: Unique on [dbo].[Account] ([Name])\nINSERT INTO dbo.Account (Name) VALUES (N'Acme Holdings');\n");
        var profile = Profile("R6's sites");
        try
        {
            var existing = await Environment(repository.Base, FiveSitesRows, head, profile);
            Assert.Equal(
            [
                "Fits on [dbo].[Customer] ([Region]) populated violating OverLength",
                "ForeignKey on [dbo].[CustomerAddress] ([CustomerId]) populated violating None",
                "ForeignKey on [dbo].[Order] ([StatusId]) populated clean None",
                "NotNull on [dbo].[Customer] ([ContactPhone]) populated violating None",
                "TableEmpty on [dbo].[Customer] populated violating None",
                "Unique on [dbo].[Account] ([Name]) populated violating Exact",
            ], existing.States.Select(Said).Order(StringComparer.Ordinal));
            Assert.Equal(0, Up("--at", repository.Base, "--profile", profile).Exit);

            var (exit, answer) = Up("--at", repository.Base, "--existing-data", Written(existing), "--profile", profile);

            Assert.True(exit == 0, answer.ToJsonString());
            Assert.Equal(("built", true, head), ((string?)answer["outcome"], (bool?)answer["syntheticCopy"]!["restored"],
                (string?)answer["syntheticCopy"]!["head"]));
            Assert.Equal(JsonNode.Parse(ExistingDataFile.Text(existing))!["states"]!.ToJsonString(),
                answer["syntheticCopy"]!["answers"]!.ToJsonString());
            var (drift, output) = Drift(repository.Base, profile);
            Assert.True(drift == 0, output);
        }
        finally
        {
            await Dropped();
        }
    }

    /// <summary>
    /// PAR-033 and M3's done-when: the change's declared witnesses run on the copy as DML, by their lines in dbchange/witnesses.sql. A
    /// row SQL Server refuses (a NULL in Account.Name, Msg 515) is witness.failed at exit 8, naming the file's line; corrected to a row
    /// that breaks no rule, the copy misses the duplicate the environment holds, and acceptance refuses it at exit 8, naming the
    /// precondition and the remedy, to plant a row that breaks it under its header.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_declared_witness_SQL_Server_refuses_is_named_by_its_line_and_one_breaking_no_row_fails_acceptance()
    {
        var failing = repository.Committed(FiveSites,
            "-- witness: Unique on [dbo].[Account] ([Name])\nINSERT INTO dbo.Account (Name) VALUES (NULL);\n");
        var profile = Profile("declared witnesses");
        try
        {
            var file = Written(await Environment(repository.Base, FiveSitesRows, failing, profile));

            var (exit, answer) = Up("--at", repository.Base, "--existing-data", file, "--profile", profile);

            Assert.True(exit == 8, answer.ToJsonString());
            Assert.Equal("needs-input", (string?)answer["outcome"]);
            var failed = Assert.Single(answer["findings"]!.AsArray(), f => (string?)f!["code"] == "witness.failed")!;
            Assert.StartsWith("dbchange/witnesses.sql line 2 failed with Msg 515", (string)failed["message"]!, StringComparison.Ordinal);

            repository.Committed(FiveSites,
                "-- witness: Unique on [dbo].[Account] ([Name])\nINSERT INTO dbo.Account (Name) VALUES (N'wDistinct');\n");
            (exit, answer) = Up("--at", repository.Base, "--existing-data", file, "--profile", profile);

            Assert.True(exit == 8, answer.ToJsonString());
            Assert.Equal("needs-input", (string?)answer["outcome"]);
            var refused = Assert.Single(answer["findings"]!.AsArray(), f => (string?)f!["code"] == "witness.acceptance")!;
            Assert.Equal(("error", "Unique on [dbo].[Account] ([Name])"), ((string)refused["severity"]!, (string)refused["subject"]!));
            Assert.Equal("The copy answers Unique on [dbo].[Account] ([Name]) populated and clean, where the environment answered it "
                + "populated and violating (exact).", (string)refused["message"]!);
            Assert.Equal("Plant a row that breaks it, under -- witness: Unique on [dbo].[Account] ([Name]) in dbchange/witnesses.sql.",
                (string)refused["remedy"]!);
        }
        finally
        {
            await Dropped();
        }
    }

    /// <summary>
    /// PAR-034 and spike S9: an environment at a ref declaring a foreign key from CustomerAddress to Customer and a check on Order.Total
    /// holds the key disabled, over an orphan, and the check enabled and untrusted; the change makes CustomerAddress.City NOT NULL and the
    /// key ON DELETE CASCADE. The copy reproduces both states exactly, read back from its catalog: the key is disabled before the copy is
    /// read, so the copy's own plan of the change is the environment's; the orphan and the NULL are planted under the suspended key; the
    /// copy answers as the environment does; and step 8 names the disabled key, which the plan from the base drops and adds, in a note.
    /// It never forces trust: an existing data holding the orphan that lists the key as trusted is no state rows can reach, and SQL Server
    /// refusing the key WITH CHECK over the planted orphan is witness.trust at exit 8, naming the key.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_copy_reproduces_a_key_the_environment_holds_disabled_and_a_check_it_holds_untrusted_and_never_forces_their_trust()
    {
        const string Key = "CustomerId  INT             NOT NULL CONSTRAINT FK_CustomerAddress_Customer_CustomerId REFERENCES dbo.Customer (Id)";
        GoldenEdit[] keys =
        [
            new("Modules/CustomerAddress.sql", "CustomerId  INT             NOT NULL,", Key + ","),
            new("Modules/Order.sql", "CONSTRAINT PK_Order_Id PRIMARY KEY CLUSTERED (Id)",
                "CONSTRAINT PK_Order_Id PRIMARY KEY CLUSTERED (Id),\n    CONSTRAINT CK_Order_Total CHECK (Total >= 0)"),
        ];
        var at = repository.Committed(new SampleChange("a foreign key and a check", keys, []), null);
        var head = repository.Committed(new SampleChange("CustomerAddress.City made NOT NULL and its key ON DELETE CASCADE",
        [
            new("Modules/CustomerAddress.sql", "CustomerId  INT             NOT NULL,", Key + " ON DELETE CASCADE,"), keys[1],
            new("Modules/CustomerAddress.sql", "City        NVARCHAR(80)    NULL,", "City        NVARCHAR(80)    NOT NULL,"),
        ], []), null);
        var profile = Profile("trust");
        try
        {
            var existing = await Environment(at,
                "ALTER TABLE dbo.CustomerAddress NOCHECK CONSTRAINT FK_CustomerAddress_Customer_CustomerId; "
                + "INSERT INTO dbo.CustomerAddress (CustomerId, Line1, City) VALUES (999, N'9 Nowhere Lane', NULL); "
                + "ALTER TABLE dbo.[Order] NOCHECK CONSTRAINT CK_Order_Total; "
                + "ALTER TABLE dbo.[Order] WITH NOCHECK CHECK CONSTRAINT CK_Order_Total;",
                head, profile);
            Assert.Equal(["CheckConstraint [dbo].[CK_Order_Total] enabled and untrusted",
                    "ForeignKeyConstraint [dbo].[FK_CustomerAddress_Customer_CustomerId] disabled"],
                existing.Trust.Select(t => t.ToString()).Order(StringComparer.Ordinal));
            Assert.Contains("ForeignKey on [dbo].[CustomerAddress] ([CustomerId]) populated violating None", existing.States.Select(Said));

            var (exit, answer) = Up("--at", at, "--existing-data", Written(existing), "--profile", profile);

            Assert.True(exit == 0, answer.ToJsonString());
            Assert.Equal(JsonNode.Parse(ExistingDataFile.Text(existing))!["states"]!.ToJsonString(),
                answer["syntheticCopy"]!["answers"]!.ToJsonString());
            var copy = await Connection((string)answer["syntheticCopy"]!["copy"]!);
            Assert.Equal((1, 1), (await SqlServerFixture.ScalarAsync(copy,
                    "SELECT CAST(is_disabled AS int) FROM sys.foreign_keys WHERE name = N'FK_CustomerAddress_Customer_CustomerId';"),
                await SqlServerFixture.ScalarAsync(copy,
                    "SELECT CAST(is_not_trusted AS int) FROM sys.foreign_keys WHERE name = N'FK_CustomerAddress_Customer_CustomerId';")));
            Assert.Equal((0, 1), (await SqlServerFixture.ScalarAsync(copy,
                    "SELECT CAST(is_disabled AS int) FROM sys.check_constraints WHERE name = N'CK_Order_Total';"),
                await SqlServerFixture.ScalarAsync(copy,
                    "SELECT CAST(is_not_trusted AS int) FROM sys.check_constraints WHERE name = N'CK_Order_Total';")));
            Assert.Contains(answer["findings"]!.AsArray(), f => (string?)f!["code"] == "synthetic-copy.disabled"
                && (string?)f["severity"] == "note"
                && (string?)f["subject"] == "ForeignKeyConstraint [dbo].[FK_CustomerAddress_Customer_CustomerId]");

            (exit, answer) = Up("--at", at, "--existing-data", Written(existing with { Trust = [] }), "--profile", profile);

            Assert.True(exit == 8, answer.ToJsonString());
            var forced = Assert.Single(answer["findings"]!.AsArray(), f => (string?)f!["code"] == "witness.trust")!;
            Assert.Equal("ForeignKeyConstraint [dbo].[FK_CustomerAddress_Customer_CustomerId]", (string)forced["subject"]!);
            Assert.Contains("never forces a constraint's trust", (string)forced["message"]!, StringComparison.Ordinal);
        }
        finally
        {
            await Dropped();
        }
    }

    /// <summary>
    /// PLT-53 and WP 3.5's done-when: at a ref whose Order holds a NOT NULL foreign key to the seeded lookup Status, an environment
    /// holding an Order.StatusText over the nine characters the change narrows it to. The one Order row the copy plants links to Status's
    /// lowest seeded row, Id 1, and Status keeps its three rows and its content hash, no row planted into it.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_planted_child_of_a_seeded_lookup_links_to_its_lowest_seeded_row_and_the_lookup_keeps_its_rows_and_hash()
    {
        GoldenEdit keyed = new("Modules/Order.sql", "CONSTRAINT PK_Order_Id PRIMARY KEY CLUSTERED (Id)",
            "CONSTRAINT PK_Order_Id PRIMARY KEY CLUSTERED (Id),\n"
                + "    CONSTRAINT FK_Order_Status_StatusId FOREIGN KEY (StatusId) REFERENCES dbo.Status (Id)");
        var at = repository.Committed(new SampleChange("Order's key to Status", [keyed], []), null);
        var head = repository.Committed(new SampleChange("Order.StatusText narrowed to nvarchar(9)",
            [keyed, new("Modules/Order.sql", "StatusText      NVARCHAR(20)", "StatusText      NVARCHAR(9) ")], []), null);
        var profile = Profile("a seeded lookup");
        try
        {
            var existing = await Environment(at, "UPDATE dbo.[Order] SET StatusText = N'Backordered' WHERE Id = 2;", head, profile);

            var (exit, answer) = Up("--at", at, "--existing-data", Written(existing), "--profile", profile);

            Assert.True(exit == 0, answer.ToJsonString());
            var copy = answer["syntheticCopy"]!;
            Assert.Equal([("[dbo].[Order]", 1)], copy["planted"]!.AsArray().Where(p => (int)p!["planted"]! > 0)
                .Select(p => ((string)p!["table"]!, (int)p["planted"]!)));
            Assert.Equal(1, await SqlServerFixture.ScalarAsync(await Connection((string)copy["copy"]!),
                "SELECT StatusId FROM dbo.[Order] WHERE Id NOT IN (1, 2, 3, 4);"));
            var status = Assert.Single(copy["tables"]!.AsArray(), t => (string)t!["table"]! == "[dbo].[Status]")!;
            Assert.Equal((3L, 3L, (string?)status["before"]), ((long)status["rowsBefore"]!, (long)status["rows"]!, (string?)status["hash"]));
        }
        finally
        {
            await Dropped();
        }
    }

    /// <summary>
    /// PAR-148 (v2's TwinViewLoopTests): the golden project with a view over Customer and Order, which refuses an INSERT since it reads
    /// two base tables, builds its copy: the planter plants into tables alone, the publish keeps the view, it reads through to the seeded
    /// rows, and check drift --target synthetic-copy at that commit exits 0.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task The_golden_project_with_a_view_over_two_tables_builds_its_synthetic_copy_and_the_view_reads_the_seeded_rows()
    {
        var at = repository.Committed(new SampleChange("a view over Customer and Order",
        [
            new GoldenEdit("Modules/OrderStatusText.sql", "-- Intentionally no schema object. The column lives in Modules/Order.sql.",
                "CREATE VIEW dbo.vCustomerOrders AS SELECT c.Id AS CustomerId, c.Name, o.Id AS OrderId, o.Total "
                    + "FROM dbo.Customer AS c JOIN dbo.[Order] AS o ON o.CustomerId = c.Id;"),
        ], []), null);
        var profile = Profile("a view");
        try
        {
            var (exit, answer) = Up("--at", at, "--profile", profile);

            Assert.True(exit == 0, answer.ToJsonString());
            var copy = await Connection((string)answer["syntheticCopy"]!["copy"]!);
            Assert.Equal(1, await SqlServerFixture.ScalarAsync(copy, "SELECT COUNT(*) FROM sys.views WHERE name = N'vCustomerOrders';"));
            Assert.Equal(3, await SqlServerFixture.ScalarAsync(copy, "SELECT COUNT(*) FROM dbo.vCustomerOrders;"));
            Assert.DoesNotContain(answer["syntheticCopy"]!["planted"]!.AsArray(), p => ((string)p!["table"]!).Contains("vCustomerOrders",
                StringComparison.Ordinal));
            var (drift, output) = Drift(at, profile);
            Assert.True(drift == 0, output);
        }
        finally
        {
            await Dropped();
        }
    }

    /// <summary>check drift of the synthetic copy at a ref, under the profile.</summary>
    private (int Exit, string Output) Drift(string at, string profile) =>
        repository.Run("check", "drift", "--target", "synthetic-copy", "--at", at, "--profile", profile);

    /// <summary>synthetic-copy up with its arguments, run at the repository's root with --json.</summary>
    private (int Exit, JsonNode Answer) Up(params string[] arguments) => repository.Answer(["synthetic-copy", "up", .. arguments]);

    /// <summary>
    /// A profile of the test's own beside the repository: the pipeline's, with a comment naming the test and a random tag, so its
    /// fingerprint is new to the server; its path.
    /// </summary>
    private string Profile(string test)
    {
        var path = Path.Combine(Path.GetDirectoryName(repository.Root)!, "profiles", Guid.NewGuid().ToString("N")[..8] + ".publish.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, File.ReadAllText(Path.Combine(repository.Root, ScratchRepository.Profile))
            .Replace("<Project ", "<!-- " + test + ", " + Path.GetFileName(path) + " -->\n<Project ", StringComparison.Ordinal));
        return path;
    }

    /// <summary>
    /// An environment: a registered copy with the project at <paramref name="at"/> published to it under the profile and
    /// <paramref name="rows"/> run on it, and its existing data for the change at <paramref name="head"/>, the copy dropped after.
    /// </summary>
    private async Task<ExistingData> Environment(string at, string rows, string head, string profile)
    {
        var strict = Value(PublishProfiles.Load(profile));
        using var environment = DisposableCopy.Create(repository.Root, await SqlServerFixture.ServerAsync(), Built(at), strict);
        await SqlServerFixture.ExecuteAsync(environment.Copy.Connection, rows);
        var log = SqlServer.QueryLog.Start(repository.Root);
        using var package = Value(Ssdt.Open(Built(head)));
        using var extracted = Value(DacFx.Extract(environment.Copy));
        var model = Value(extracted.Elements).Elements;
        var asked = Value(SyntheticCopy.Ask(environment.Copy, package, strict, extracted, model, Value(Ssdt.CollationOf(model))));
        var trust = model.Select(e => e.Key).Where(k => k.Type is "ForeignKeyConstraint" or "CheckConstraint" && k.Parent is null)
            .Select(k => Value(AggregateQueries.Trust(environment.Copy, k, log))).OfType<TrustState>()
            .Where(t => t.Disabled || t.Untrusted);
        return new ExistingData(Value(SyntheticCopy.Count(environment.Copy, asked, log)), SortedArray.Of(trust));
    }

    /// <summary>The golden project at a commit of the repository, built as a ref is built; the package's path.</summary>
    private string Built(string at) => Value(Ssdt.Build(Value(Git.At(repository.Root, at)), "project/SampleCatalog.sqlproj",
        repository.Tool.Folder, Path.Combine(repository.Root, ".dbchange", "build"))).Path;

    /// <summary>The existing data as a file beside the repository, in the form predict writes; its path.</summary>
    private string Written(ExistingData existing)
    {
        var path = Path.Combine(Path.GetDirectoryName(repository.Root)!, Guid.NewGuid().ToString("N")[..8] + ".existing-data.json");
        File.WriteAllText(path, ExistingDataFile.Text(existing));
        return path;
    }

    private static string Said(PreconditionState state) =>
        state.Precondition + (state.Populated ? " populated" : " empty") + (state.Violating ? " violating " : " clean ") + state.Classes;

    /// <summary>A connection to a database of the run's server, by its name.</summary>
    private static async Task<string> Connection(string database) =>
        new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(await SqlServerFixture.ServerAsync()) { InitialCatalog = database }
            .ConnectionString;

    /// <summary>Each synthetic copy the repository's registry holds, dropped with its row, and each backup its cache records, deleted
    /// with the cache's index, so the next test's cleanup meets no backup already deleted.</summary>
    private async Task Dropped()
    {
        var registry = Path.Combine(repository.Root, LocalState.Name, "copies.json");
        foreach (var name in File.Exists(registry)
            ? JsonNode.Parse(File.ReadAllText(registry))!["copies"]!.AsArray().Select(r => (string)r!["name"]!).Distinct().ToList() : [])
        {
            if (Value(CopyName.Of("a copy the test made", name)) is { IsSynthetic: true } synthetic)
            {
                Value(LocalServer.Drop(Value(LocalServer.Registered(repository.Root, synthetic, Io.EnvironmentsFile.Read(repository.Root))),
                    SqlServer.QueryLog.Start(repository.Root)));
            }
        }

        await SqlServerFixture.DeleteBackupsAsync(repository.Root);
        File.Delete(new LocalState(repository.Root).Backups);
    }
}
