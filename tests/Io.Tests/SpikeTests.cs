using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Estate.Budgets.Tests;
using Estate.Io;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Xunit;

namespace Estate.Io.Tests;

/// <summary>
/// V3_MILESTONES.md Appendix E as tests: section 1's measured facts 1 to 7, 10 and 11, one assertion each, against the
/// committed engine (DacFx 170.5.96, used directly). Every read runs as the read-only principal; only the fixture's copies
/// are written.
/// </summary>
public sealed class SpikeTests(GoldenProject ground) : IClassFixture<GoldenProject>
{
    private static readonly XNamespace Report = "http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02";

    [Fact]
    [Trait("Category", "fixture")]
    public void Fact_1_the_classic_build_of_the_golden_project_carries_the_refactorlog_and_both_deploy_scripts()
    {
        using var dacpac = ZipFile.OpenRead(ground.Base);

        Assert.Superset(new HashSet<string>(["model.xml", "refactor.xml", "predeploy.sql", "postdeploy.sql"]), dacpac.Entries.Select(e => e.FullName).ToHashSet());
        Assert.Contains("MERGE dbo.Customer AS target", ToolFolderTests.Text(dacpac, "postdeploy.sql"), StringComparison.Ordinal);   // Data/Seed.sql, inlined from its :r
        Assert.Contains("Pre-deploy: no backfill active.", ToolFolderTests.Text(dacpac, "predeploy.sql"), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "fixture")]
    public void Fact_2_Script_of_a_head_against_a_published_copy_runs_as_the_read_only_login()
    {
        var (script, report) = ground.Plan(ground.Mandatory);

        Assert.Equal(ground.Reader.Login, new SqlConnectionStringBuilder(ground.Reader.ConnectionString).UserID);
        Assert.Contains("ALTER COLUMN [Email] NVARCHAR (256) NOT NULL", script, StringComparison.Ordinal);
        Assert.NotEmpty(report.Descendants(Report + "Operation"));
    }

    [Fact]
    [Trait("Category", "fixture")]
    public async Task Fact_3_the_data_loss_check_is_found_at_state_127_and_its_predicate_returns_1_as_the_read_only_login()
    {
        var (script, _) = ground.Plan(ground.Mandatory);
        var body = string.Join('\n', script.Split('\n').Where(l => !l.TrimStart().StartsWith(':')));
        var parsed = new TSql160Parser(initialQuotedIdentifiers: true).Parse(new StringReader(body), out var errors);
        var checks = new List<QueryExpression>();
        parsed.Accept(new DataLossChecks(checks));

        Assert.Empty(errors);
        new Sql160ScriptGenerator().GenerateScript(Assert.Single(checks), out var predicate);
        Assert.Contains("[dbo].[Customer]", predicate, StringComparison.Ordinal);
        Assert.Equal(1, await SqlServerFixture.ScalarAsync(ground.Reader.ConnectionString, "SELECT CASE WHEN EXISTS (" + predicate + ") THEN 1 ELSE 0 END;"));
    }

    [Fact]
    [Trait("Category", "fixture")]
    public void Fact_4_the_deploy_report_of_a_copy_that_matches_its_package_has_no_operations()
    {
        var (_, report) = ground.Plan(ground.Base);

        Assert.Empty(report.Descendants(Report + "Operation"));
    }

    [Fact]
    [Trait("Category", "fixture")]
    public void Fact_5_LoadFromDatabase_runs_as_the_read_only_login_into_the_model_the_build_produced()
    {
        using var database = TSqlModel.LoadFromDatabase(ground.Reader.ConnectionString, new ModelExtractOptions());
        using var package = TSqlModel.LoadFromDacpac(ground.Base, new ModelLoadOptions());

        Assert.Contains("[dbo].[Customer]", Tables(database));
        Assert.Equal(Tables(package), Tables(database));
    }

    [Fact]
    [Trait("Category", "fixture")]
    public void Fact_6_the_property_walk_finds_Nullable_true_to_false_and_nothing_else()
    {
        using var before = TSqlModel.LoadFromDacpac(ground.Base, new ModelLoadOptions());
        using var after = TSqlModel.LoadFromDacpac(ground.Mandatory, new ModelLoadOptions());

        Assert.Equal(("[dbo].[Customer].[Email]", "Nullable", (object?)true, (object?)false), Assert.Single(Changes(before, after)));
    }

    [Fact]
    [Trait("Category", "fixture")]
    public void Fact_7_the_deploy_report_stays_coarse_one_Alter_on_the_table_and_no_alert()
    {
        var (_, report) = ground.Plan(ground.Mandatory);

        var operation = Assert.Single(report.Descendants(Report + "Operation"));
        var item = Assert.Single(operation.Elements(Report + "Item"));
        Assert.Equal(("Alter", "[dbo].[Customer]", "SqlTable"), ((string?)operation.Attribute("Name"), (string?)item.Attribute("Value"), (string?)item.Attribute("Type")));
        Assert.Empty(report.Descendants(Report + "Alert"));
    }

    [Fact]
    [Trait("Category", "fixture")]
    public void Fact_10_the_pipeline_profile_loads_as_deploy_options_names_no_target_and_is_the_only_profile()
    {
        var profile = DacProfile.Load(ground.Profile);
        var options = profile.DeployOptions;

        Assert.True(string.IsNullOrEmpty(profile.TargetConnectionString) && string.IsNullOrEmpty(profile.TargetDatabaseName));
        Assert.Equal((true, false, true, false), (options.BlockOnPossibleDataLoss, options.GenerateSmartDefaults, options.IgnoreColumnOrder, options.DropObjectsNotInSource));
        Assert.Equal(["pipeline.publish.xml"], Directory.GetFiles(Path.GetDirectoryName(ground.Profile)!).Select(Path.GetFileName));
    }

    [Fact]
    [Trait("Category", "fixture")]
    public async Task Fact_11_a_clean_foreign_key_on_a_populated_child_lands_trusted_by_default_and_untrusted_with_validation_off()
    {
        var notTrusted = await Task.WhenAll(
            Task.Run(() => NotTrustedAfterPublishing(new DacDeployOptions { BlockOnPossibleDataLoss = true })),
            Task.Run(() => NotTrustedAfterPublishing(new DacDeployOptions { BlockOnPossibleDataLoss = true, ScriptNewConstraintValidation = false })));

        Assert.Equal([0, 1], notTrusted);
    }

    /// <summary>A fresh copy: the base, published and seeded; then the head declaring Customer.AccountId's key to Account, under the options given.</summary>
    private async Task<int> NotTrustedAfterPublishing(DacDeployOptions options)
    {
        await using var copy = await SqlServerFixture.RegisterAsync();
        GoldenProject.Publish(ground.Base, copy, ground.Pipeline);
        Assert.True(await SqlServerFixture.ScalarAsync(copy.ConnectionString, "SELECT COUNT(*) FROM dbo.Customer WHERE AccountId IS NOT NULL;") > 0, "the child table is not populated");

        GoldenProject.Publish(ground.ForeignKey, copy, options);
        return await SqlServerFixture.ScalarAsync(copy.ConnectionString, "SELECT CAST(is_not_trusted AS int) FROM sys.foreign_keys WHERE name = N'FK_Customer_Account_AccountId';");
    }

    private static List<string> Tables(TSqlModel model) =>
        [.. model.GetObjects(DacQueryScopes.UserDefined, Table.TypeClass).Select(t => t.Name.ToString()).Order(StringComparer.Ordinal)];

    /// <summary>One walk over <c>ModelTypeClass.Properties</c>, no code per property: every column of every table, base against head.</summary>
    private static IEnumerable<(string Column, string Property, object? Before, object? After)> Changes(TSqlModel before, TSqlModel after)
    {
        var heads = after.GetObjects(DacQueryScopes.UserDefined, Table.TypeClass).SelectMany(t => t.GetReferenced(Table.Columns))
            .ToDictionary(c => c.Name.ToString(), StringComparer.Ordinal);
        return from column in before.GetObjects(DacQueryScopes.UserDefined, Table.TypeClass).SelectMany(t => t.GetReferenced(Table.Columns))
               from property in column.ObjectType.Properties
               let pair = (Before: column.GetProperty(property), After: heads[column.Name.ToString()].GetProperty(property))
               where !Equals(pair.Before, pair.After)
               select (column.Name.ToString(), property.Name, pair.Before, pair.After);
    }

    /// <summary>DacFx's data-loss check, IF EXISTS (…) RAISERROR (…, 16, 127); the script's other IF EXISTS blocks check database options.</summary>
    private sealed class DataLossChecks(List<QueryExpression> found) : TSqlFragmentVisitor
    {
        public override void ExplicitVisit(IfStatement node)
        {
            if (node.Predicate is ExistsPredicate exists && node.ThenStatement is RaiseErrorStatement { ThirdParameter: IntegerLiteral { Value: "127" } })
            {
                found.Add(exists.Subquery.QueryExpression);
            }

            base.ExplicitVisit(node);
        }
    }
}

/// <summary>
/// The golden project (tests/Golden/project/) built the classic way against dist/estate/, with two heads built beside
/// it from edited copies: make-mandatory (Customer.Email NOT NULL) and a clean foreign key (Customer.AccountId to Account).
/// The base is published to one registered database, the copy, as the fixture's admin identity, and the read-only principal
/// is created on it. Everything is dropped after the class: the build tree under .estate/golden/, the copy and its principal.
/// </summary>
public sealed class GoldenProject : IAsyncLifetime
{
    private readonly string root = Path.Combine(Repository.Root, ".estate", "golden", Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..8]);
    private RegisteredDatabase? copy;
    private ReadOnlyPrincipal? reader;

    public string Profile { get; } = Path.Combine(Repository.Root, "tests", "Golden", "project", "profiles", "pipeline.publish.xml");

    public string Base => Dacpac("base");

    public string Mandatory => Dacpac("mandatory");

    public string ForeignKey => Dacpac("foreign-key");

    public RegisteredDatabase Copy => copy!;

    public ReadOnlyPrincipal Reader => reader!;

    /// <summary>The pipeline profile's deploy options, and nothing else from it.</summary>
    public DacDeployOptions Pipeline => DacProfile.Load(Profile).DeployOptions;

    public async Task InitializeAsync()
    {
        Telemetry.OptOut();   // before DacFx loads, as estate's Main does
        var tool = new PublishedTool();
        var golden = Path.Combine(Repository.Root, "tests", "Golden");
        Directory.CreateDirectory(root);
        foreach (var stop in (string[])["Directory.Build.props", "Directory.Packages.props"])
        {
            File.Copy(Path.Combine(golden, stop), Path.Combine(root, stop));
        }

        foreach (var head in (string[])["base", "mandatory", "foreign-key"])
        {
            ToolFolderTests.Copy(Path.Combine(golden, "project"), Path.Combine(root, head));
        }

        Edit(Path.Combine(root, "mandatory", "Modules", "Customer.sql"), "Email           NVARCHAR(256)   NULL,", "Email           NVARCHAR(256)   NOT NULL,");
        Edit(Path.Combine(root, "foreign-key", "Modules", "Customer.sql"), "CONSTRAINT PK_Customer_Id PRIMARY KEY CLUSTERED (Id)",
            "CONSTRAINT PK_Customer_Id PRIMARY KEY CLUSTERED (Id),\n    CONSTRAINT FK_Customer_Account_AccountId FOREIGN KEY (AccountId) REFERENCES dbo.Account (Id)");
        var builds = await Task.WhenAll(((string[])["base", "mandatory", "foreign-key"]).Select(head => Task.Run(() => tool.Build(Path.Combine(root, head, "SampleCatalog.sqlproj")))));
        Assert.All(builds, build => Assert.True(build.Exit == 0, build.Output));

        copy = await SqlServerFixture.RegisterAsync();
        Publish(Base, copy, Pipeline);
        reader = await ReadOnlyPrincipal.CreateAsync(copy);
    }

    public async Task DisposeAsync()
    {
        if (copy is not null)
        {
            await copy.DisposeAsync();
        }

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A package published to a registered database as the fixture's admin identity: copies only. Packages load from a
    /// stream: a publish of a package loaded by path loads the assemblies beside the dacpac (the build's SampleCatalog.dll)
    /// into this process and holds them until it exits.
    /// </summary>
    public static void Publish(string dacpac, RegisteredDatabase database, DacDeployOptions options)
    {
        using var stream = File.OpenRead(dacpac);
        using var package = DacPackage.Load(stream);
        new DacServices(database.ConnectionString).Publish(package, database.Name, new PublishOptions { DeployOptions = options });
    }

    /// <summary>DacServices.Script of a package, loaded from a stream, against the copy, as the read-only principal, under the pipeline profile's options only.</summary>
    public (string Script, XDocument Report) Plan(string dacpac)
    {
        using var stream = File.OpenRead(dacpac);
        using var package = DacPackage.Load(stream);
        var plan = new DacServices(Reader.ConnectionString).Script(package, Copy.Name, new PublishOptions
        {
            GenerateDeploymentScript = true,
            GenerateDeploymentReport = true,
            DeployOptions = Pipeline,
        });
        return (plan.DatabaseScript, XDocument.Parse(plan.DeploymentReport));
    }

    private string Dacpac(string head) => Path.Combine(root, head, "bin", "Release", "SampleCatalog.dacpac");

    /// <summary>One edit to a copied file; the text must occur exactly once, so a golden that drifts fails here and not later.</summary>
    private static void Edit(string file, string from, string to)
    {
        var text = File.ReadAllText(file);
        Assert.True(text.Split(from).Length == 2, file + " does not hold exactly one '" + from + "'");
        File.WriteAllText(file, text.Replace(from, to, StringComparison.Ordinal));
    }
}
