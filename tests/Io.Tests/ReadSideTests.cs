using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using DbChange.Budgets.Tests;
using DbChange.Kernel;
using DbChange.Tests;
using Xunit;
using Xunit.Abstractions;

namespace DbChange.Io.Tests;

/// <summary>
/// WP 3.1's done-when (V3_MILESTONES.md §9): Schema.Of over a published copy's model equals, as ReadSideFacts maps both, the catalog v2's
/// read side writes for the same copy. The copy is the golden project with the sample change declare-what-the-planter-reads, which
/// declares what the planter reads (foreign keys, a composite one and cycles among them, checks, uniques, a filtered unique index,
/// computed and period columns, an alias type, a sequence default, an identity, a trigger), published under the pipeline's profile
/// through io/LocalServer and read back through DacFx.Extract and io/Ssdt.ReadModel. v2's read side runs where DBCHANGE_READSIDE names
/// the program ci/readside.sh builds, which the Ubuntu SQL lane does on every push; everywhere the test compares v3 with the committed
/// facts, which that lane holds equal to v2's, so the committed facts cannot go stale while the lane is green.
/// </summary>
public sealed class ReadSideTests(PlannerCopy copy, ITestOutputHelper output) : IClassFixture<PlannerCopy>
{
    [Fact]
    [Trait("Category", "fixture")]
    public void Schema_of_a_published_copy_equals_the_catalog_v2_s_read_side_writes_for_it()
    {
        var v3 = ReadSideFacts.Of(copy.Schema);
        var v2 = V2Facts();
        if (v2 is { } oracle && Environment.GetEnvironmentVariable("DBCHANGE_BLESS") == "1")
        {
            ReadSideFacts.Bless(PlannerCopy.Change, oracle);
        }

        output.WriteLine(v2 is null ? "v2's read side did not run: DBCHANGE_READSIDE is unset; v3 was compared with the committed facts."
            : "v2's read side ran; v3 and v2 were each compared with the committed facts, and the committed file with the bytes Bless"
                + " writes for v2's.");
        Assert.Equal(ReadSideFacts.Committed(PlannerCopy.Change), v3);
        if (v2 is not null)
        {
            Assert.Equal(ReadSideFacts.Committed(PlannerCopy.Change), v2);
            Assert.Equal(ReadSideFacts.Written(v2.Value), ReadSideFacts.Text(PlannerCopy.Change));
        }
    }

    /// <summary>
    /// The golden copy's load order: Account before Customer before CustomerAddress; Status before Order before OrderLine before
    /// OrderLineNote, whose composite key references OrderLine's unique key; Category before Product; Department before Employee, with
    /// FK_Department_Head and Employee's unnamed ManagerId self-reference deferred; Egg and Hen, whose NOT NULL keys reference each other,
    /// the one cycle.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public void Order_of_the_golden_copy_puts_parents_first_defers_the_optional_cycle_and_names_the_mandatory_one()
    {
        var order = Order.Of(copy.Schema);
        var at = order.Tables.ToDictionary(s => s.Table.Base, s => s.Position);

        Assert.True(at["Account"] < at["Customer"] && at["Customer"] < at["CustomerAddress"], string.Join(", ", order.Tables));
        Assert.True(at["Status"] < at["Order"] && at["Order"] < at["OrderLine"] && at["OrderLine"] < at["OrderLineNote"],
            string.Join(", ", order.Tables));
        Assert.True(at["Category"] < at["Product"] && at["Department"] < at["Employee"], string.Join(", ", order.Tables));
        Assert.Equal(["ForeignKeyConstraint [dbo].[FK_Department_Head]", "ForeignKeyConstraint [dbo].[Employee].[ManagerId].[Columns]"],
            order.Deferred.Select(d => d.Reference.ToString()));
        var cycle = Assert.Single(order.Cycles);
        Assert.Equal(["[dbo].[Egg]", "[dbo].[Hen]"], cycle.Members.Select(m => m.ToString()));
        Assert.Equal("synthetic-copy.cycle", cycle.Error.Code);
    }

    /// <summary>v2's facts for the copy, or null where DBCHANGE_READSIDE names no program.</summary>
    private SortedArray<string>? V2Facts()
    {
        var program = Environment.GetEnvironmentVariable("DBCHANGE_READSIDE");
        if (string.IsNullOrEmpty(program))
        {
            return null;
        }

        var catalog = Path.Combine(copy.Root, "v2-catalog.json");
        var ran = Programs.InRepository("dotnet", program, catalog) with
        {
            Environment = new Dictionary<string, string?>(Programs.NoTelemetry, StringComparer.Ordinal)
            {
                ["DBCHANGE_READSIDE_CONNECTION"] = copy.Copy.Copy.Connection,
            },
        };
        var (exit, log) = ran.Finish().Joined();
        Assert.True(exit == 0, "v2's read side exited " + exit + ": " + log);
        return ReadSideFacts.Of(JsonNode.Parse(File.ReadAllText(catalog))!);
    }
}

/// <summary>
/// The golden project with the sample change declare-what-the-planter-reads, built, published to a copy io/LocalServer makes under the
/// pipeline's profile, extracted and read into a Schema once for the class; the copy is dropped after it.
/// </summary>
public sealed class PlannerCopy : IAsyncLifetime
{
    public const string Change = "declare-what-the-planter-reads";

    private DisposableCopy? made;

    public string Root { get; } = SqlServerFixture.RepositoryRoot(Path.Combine(Repository.Root, ".dbchange", "read-side-under-test",
        Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8]));

    internal DisposableCopy Copy => made!;

    public Schema Schema { get; private set; } = new([]);

    public async Task InitializeAsync()
    {
        var dacpac = await GoldenProject.Built(GoldenProject.Change(Change));
        made = DisposableCopy.Create(Root, await SqlServerFixture.ServerAsync(), dacpac, Expect.Value(PublishProfiles.Load(GoldenProject.Profile)));
        using var package = Expect.Value(DacFx.Extract(made.Copy));
        Schema = Expect.Value(Schema.Of(Expect.Value(package.Elements).Elements));
    }

    public Task DisposeAsync()
    {
        try
        {
            made?.Dispose();
        }
        finally
        {
            Directory.Delete(Root, recursive: true);
        }

        return Task.CompletedTask;
    }
}
