using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DbChange.Budgets.Tests;
using DbChange.Kernel;
using DbChange.Tests;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>A package published to a registered database and read back as io reads a copy: the same keys, the same plan and the same
/// elements; and a database holding a user whose login was dropped, read whole.</summary>
public sealed partial class ModelElementsTests
{
    /// <summary>
    /// The golden project with a table of unnamed inline constraints, two of them checks on one column, and a procedure over it,
    /// published to a registered copy and read back through SqlServer.Model: the database's model keys every object as the package's
    /// does, though SQL Server named each constraint, and each unnamed key names the same constraint in both (the same
    /// targets; the tied checks the same text once SQL Server's brackets and parentheses are set aside). Their values differ (SQL
    /// Server stores a check's text as it normalized it), which is why model fingerprints are compared only between like sources.
    /// The procedure's Definition reads alike from both: SQL Server keeps a module's text as the publish sent it.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_package_and_the_database_it_was_published_to_key_every_object_alike_unnamed_constraints_included()
    {
        var package = heads.Models["unnamed constraints"].Elements
            .Where(e => e.Key.Type is not (Element.PreDeploymentScript or Element.PostDeploymentScript or Element.RefactorLogOperation))
            .ToList();
        var database = await PublishedAndRead(heads.Dacpacs["unnamed constraints"]);

        Assert.Contains(package, e => e.Key.ToString() == "DefaultConstraint [dbo].[Note].[Pinned].[TargetColumn]");
        Assert.Equal(package.Select(e => e.Key), database.Select(e => e.Key));
        var unnamed = package.Where(e => e.Key.Name.Base.Split(' ')[0] is "Host" or "TargetColumn" or "ExpressionDependencies" or "Columns")
            .ToList();
        Assert.Equal(8, unnamed.Count(e => e.Key.Path.StartsWith("[dbo].[Note].", StringComparison.Ordinal)));
        Assert.All(unnamed, e => Assert.Equal(e.Relationships, database.Single(d => d.Key == e.Key).Relationships));
        Assert.All(
            ["CheckConstraint [dbo].[Note].[Score].[ExpressionDependencies 1]",
                "CheckConstraint [dbo].[Note].[Score].[ExpressionDependencies 2]"],
            (string key) =>
            Assert.Equal(Bare(Expression(package.Single(e => e.Key.ToString() == key))),
                Bare(Expression(database.Single(e => e.Key.ToString() == key)))));
        var check = package.Single(e => e.Key.ToString() == "CheckConstraint [dbo].[Note].[Pinned].[ExpressionDependencies]");
        Assert.NotEqual(check["Expression"], database.Single(e => e.Key == check.Key)["Expression"]);
        var procedure = package.Single(e => e.Key.ToString() == "Procedure [dbo].[NoteCount]");
        Assert.Contains("WHERE CustomerId = @CustomerId", Assert.IsType<Value.Script>(procedure["Definition"]).Content,
            StringComparison.Ordinal);
        Assert.Equal(procedure["Definition"], database.Single(e => e.Key == procedure.Key)["Definition"]);
    }

    /// <summary>
    /// The grants head (two roles granted SELECT on dbo.Account, one also INSERT, and both VIEW DEFINITION on the database)
    /// published to a registered copy and read back through SqlServer.Model holds the Permission keys the package's model holds.
    /// DacFx 170.5.96's LoadFromDatabase leaves every permission out unless ModelExtractOptions.IgnorePermissions is set false.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_package_s_grants_published_to_a_copy_read_back_as_the_same_Permission_keys()
    {
        var database = await PublishedAndRead(heads.Dacpacs["grants"]);

        var (packaged, published) = (Permissions(heads.Models["grants"].Elements), Permissions(database));
        output.WriteLine(string.Join('\n', published));

        Assert.Contains("Permission [dbo].[Account].[Grant.Select.Object].[AppReader].[dbo]", packaged);
        Assert.Equal(packaged, published);
    }

    /// <summary>
    /// The case from the review of H1's first round (2026-09-25): dbo.T (Id, B INT NULL CHECK (B &gt; 0), A INT NULL CHECK (A &gt; 5))
    /// packaged with the refactorlog's entry renaming C to A, published to a registered copy and read back through SqlServer.Model. A
    /// database has no refactorlog, so each unnamed check's key has to follow from what both models hold: each CheckConstraint key names
    /// the check on the same column in the package's model and in the database's.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task Unnamed_checks_beside_a_renamed_column_are_keyed_alike_in_a_package_and_the_database_it_was_published_to()
    {
        var dacpac = Built(RenameCToA,
            "CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, B INT NULL CHECK (B > 0), A INT NULL CHECK (A > 5));");
        try
        {
            SortedArray<Element> package;
            using (var loaded = Ok(Ssdt.Open(dacpac)))
            {
                package = Ok(Ssdt.ReadModel(loaded)).Elements;
            }

            var database = await PublishedAndRead(new DacDeployOptions(), dacpac);

            output.WriteLine(Checked(database));
            Assert.Contains("CheckConstraint [dbo].[T].[A].[ExpressionDependencies] → Column [dbo].[T].[A]", Checked(package),
                StringComparison.Ordinal);
            Assert.Equal(Checked(package), Checked(database));
        }
        finally
        {
            Delete(dacpac);
        }
    }

    /// <summary>
    /// The two cases from the review of H1's second round (2026-09-25): a first package published to a registered database, then a second
    /// that inserts a column carrying an unnamed check ahead of a checked column, published over it under the golden pipeline
    /// profile's deploy options. That profile sets IgnoreColumnOrder, so DacFx appends the new column and the database holds its
    /// columns in another order than the second package. Each CheckConstraint key names the check on the same column in the
    /// second package's model and in the database's.
    /// </summary>
    [Theory]
    [Trait("Category", "fixture")]
    [InlineData("CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, B INT NULL CHECK (B > 0));",
        "CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, A INT NULL CHECK (A > 5), B INT NULL CHECK (B > 0));")]
    [InlineData("CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, B INT NULL CHECK (B > 0), A INT NULL CHECK (A > 5));",
        "CREATE TABLE dbo.T (Id INT NOT NULL PRIMARY KEY, B INT NULL CHECK (B > 0), X INT NULL CHECK (X < 9), A INT NULL CHECK (A > 5));")]
    public async Task Unnamed_checks_are_keyed_alike_in_a_package_and_a_database_that_holds_its_columns_in_another_order(string first,
        string second)
    {
        var (v1, v2) = (Built(null, first), Built(null, second));
        try
        {
            SortedArray<Element> package;
            using (var loaded = Ok(Ssdt.Open(v2)))
            {
                package = Ok(Ssdt.ReadModel(loaded)).Elements;
            }

            var profile = DacProfile.Load(GoldenProject.Profile).DeployOptions;
            Assert.True(profile.IgnoreColumnOrder);
            var database = await PublishedAndRead(profile, v1, v2);

            var columns = database.Single(e => e.Key.ToString() == "Table [dbo].[T]").Relationships.Single(r => r.Name == "Columns").Targets
                .Select(t => t.Key.Name.Base);
            Assert.NotEqual(
                package.Single(e => e.Key.ToString() == "Table [dbo].[T]").Relationships.Single(r => r.Name == "Columns").Targets
                    .Select(t => t.Key.Name.Base),
                columns);
            output.WriteLine(Checked(database));
            Assert.Equal(Checked(package), Checked(database));
        }
        finally
        {
            Delete(v1);
            Delete(v2);
        }
    }

    /// <summary>Each CheckConstraint key of a model, with the keys of the objects its ExpressionDependencies names, one line each in key
    /// order.</summary>
    private static string Checked(IEnumerable<Element> elements) =>
        string.Join('\n', elements.Where(e => e.Key.Type == "CheckConstraint").OrderBy(e => e.Key.ToString(), StringComparer.Ordinal)
        .Select(e => e.Key + " → "
            + string.Join(", ",
                e.Relationships.Where(r => r.Name == "ExpressionDependencies").SelectMany(r => r.Targets.Select(t => t.Key)))));

    private static List<string> Permissions(IEnumerable<Element> elements) =>
        [.. elements.Where(e => e.Key.Type == "Permission").Select(e => e.Key.ToString())];

    /// <summary>A head's package published to a registered database under DacFx's default deploy options, then read back as io reads a
    /// copy; the database is dropped after.</summary>
    private static Task<SortedArray<Element>> PublishedAndRead(string dacpac) => PublishedAndRead(new DacDeployOptions(), dacpac);

    /// <summary>Packages published in turn to one registered database under the deploy options, then read back as io reads a copy; the
    /// database is dropped after.</summary>
    private static async Task<SortedArray<Element>> PublishedAndRead(DacDeployOptions options, params string[] dacpacs)
    {
        await using var database = await SqlServerFixture.RegisterAsync();
        foreach (var dacpac in dacpacs)
        {
            GoldenProject.Publish(dacpac, database, options);
        }

        using var package = Ok(DacFx.Extract(new SqlServer.Copy(Ok(CopyName.Of("the registered database", database.Name)),
            await SqlServerFixture.ServerAsync(), Repository.Root)));
        return Ok(package.Elements).Elements;
    }

    /// <summary>
    /// R1's measurement, made before check drift planned package to package and kept as the pin (the ruling of 2026-09-25): the golden
    /// project published to a registered database under the pipeline profile's options; then, for its own package and each sample change,
    /// the deploy report of the package planned against the database extracted equals the report of DacServices.Script against the live
    /// database, both read by DacFx.Report against the same element sets.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task The_package_to_package_plan_reports_the_operations_the_live_plan_reports()
    {
        await using var database = await SqlServerFixture.RegisterAsync();
        var profile = Ok(PublishProfiles.Load(GoldenProject.Profile));
        GoldenProject.Publish(heads.Dacpacs["base"], database, profile.Options());
        var copy = new SqlServer.Copy(Ok(CopyName.Of("the registered database", database.Name)), await SqlServerFixture.ServerAsync(),
            Repository.Root);
        using var extracted = Ok(DacFx.Extract(copy));
        var target = Ok(extracted.Elements).Elements;

        foreach (var head in (string[])["base", "make-mandatory", "add a nullable column", "drop a column", "widen a column",
            "add a check constraint", "add a foreign key", "rename a column"])
        {
            using var package = Ok(Ssdt.Open(heads.Dacpacs[head]));
            var source = Ok(package.Elements).Elements;
            var planned = Ok(DacFx.Plan(package, extracted, database.Name, profile, [])).Report;
            var live = Ok(DacFx.Report(new DacServices(database.ConnectionString).Script(package.Dac, database.Name,
                new PublishOptions { GenerateDeploymentReport = true, DeployOptions = profile.Options() }).DeploymentReport, source,
                target)).Report;

            output.WriteLine(head + ": " + string.Join("; ", planned.Operations.Select(o => o.Kind + " " + o.Key)) + " | "
                + string.Join("; ", planned.Alerts.Select(a => a.Kind + " " + a.Text)));
            Assert.True(live == planned,
                head + ": live " + string.Join("; ", live.Operations.Select(o => o.Kind + " " + o.Key))
                + string.Join("; ", live.Alerts.Select(a => " " + a.Kind + " " + a.Text))
                + "\npackage to package " + string.Join("; ", planned.Operations.Select(o => o.Kind + " " + o.Key))
                + string.Join("; ", planned.Alerts.Select(a => " " + a.Kind + " " + a.Text)));
        }
    }

    /// <summary>
    /// The read did not change what it reads: the golden project published to a registered database, its elements as DacFx.Extract's
    /// package gives them equal its elements as TSqlModel.LoadFromDatabase gave them under the options dbchange read a database with until
    /// 2026-09-25, kept here alone.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task The_elements_of_an_extracted_copy_fingerprint_as_the_elements_LoadFromDatabase_read()
    {
        await using var database = await SqlServerFixture.RegisterAsync();
        GoldenProject.Publish(heads.Dacpacs["grants"], database, new DacDeployOptions());
        var copy = new SqlServer.Copy(Ok(CopyName.Of("the registered database", database.Name)), await SqlServerFixture.ServerAsync(),
            Repository.Root);
        using var extracted = Ok(DacFx.Extract(copy));
        using var loaded = TSqlModel.LoadFromDatabase(database.ConnectionString, new ModelExtractOptions
        {
            IgnorePermissions = false, IgnoreExtendedProperties = false, IgnoreUserLoginMappings = false,
            ExtractApplicationScopedObjectsOnly = true, ExtractReferencedServerScopedElements = true, ExtractUsageProperties = false,
            LoadAsScriptBackedModel = false, VerifyExtraction = false, Storage = DacSchemaModelStorageType.Memory,
            HashObjectNamesInLogs = false,
        });

        var (fromExtract, fromLoad) = (Ok(extracted.Elements).Elements, Ok(Ssdt.ReadModel(loaded)));

        var differing = Ok(Change.Between(fromLoad, fromExtract, []));
        Assert.True(differing.IsEmpty, string.Join('\n', Lines(differing)));
        Assert.Equal(Fingerprint.Of(fromLoad), Fingerprint.Of(fromExtract));
    }

    /// <summary>
    /// §5's unmeasured case, measured: a database holding a user whose login was dropped extracts whole under VerifyExtraction false, the
    /// user read with no login, and DacFx reports no error in the model, so such a user is no model.error note.
    /// </summary>
    [Fact]
    [Trait("Category", "fixture")]
    public async Task A_database_holding_a_user_whose_login_was_dropped_extracts_whole_and_reads_the_user_without_a_login()
    {
        await using var database = await SqlServerFixture.RegisterAsync();
        // the fixture drops a login of this name with the database, should the test stop before it does
        var login = database.Name + ReadOnlyPrincipal.Suffix;
        await SqlServerFixture.ExecuteAsync(database.ConnectionString,
            "DECLARE @sql nvarchar(max) = N'CREATE LOGIN ' + QUOTENAME(@name) + N' WITH PASSWORD = N''Orphan!"
            + Guid.NewGuid().ToString("N")[..12]
            + "''; CREATE USER [orphan] FOR LOGIN ' + QUOTENAME(@name) + N'; DROP LOGIN ' + QUOTENAME(@name) + N';'; EXEC (@sql);",
            login);
        var copy = new SqlServer.Copy(Ok(CopyName.Of("the registered database", database.Name)), await SqlServerFixture.ServerAsync(),
            Repository.Root);

        using var extracted = Ok(DacFx.Extract(copy));
        var read = Ok(extracted.Elements);

        var user = Assert.Single(read.Elements, e => e.Key.ToString() == "User [orphan]");
        Assert.DoesNotContain(user.Relationships, r => r.Name == "Login" && r.Targets.Count > 0);
        Assert.Empty(read.Errors);
    }
}
