using System;
using System.IO;
using System.Linq;
using DbChange.Budgets.Tests;
using DbChange.Kernel;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;
using Xunit;
using Contract = DbChange.Cli.Contract;

namespace DbChange.Io.Tests;

/// <summary>
/// io/DacFx with no SQL Server: the release made once; a deploy report's items keyed as io/Ssdt.ReadModel keys the elements; a plan of one
/// package against another built in memory, which connects to nothing; and DacFx's failures mapped once, its messages quoted and its
/// informational ones left out.
/// </summary>
public sealed class DacFxTests : IDisposable
{
    private const string Pipeline = "tests/Golden/project/profiles/pipeline.publish.xml";

    private readonly string scratch = Directory.CreateTempSubdirectory("dbchange-dacfx-").FullName;

    public void Dispose() => Directory.Delete(scratch, recursive: true);

    /// <summary>A host that bundles DacFx into one file gives its assembly no path; the informational version, before its '+', names the release then.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_version_falls_back_to_the_informational_version_when_the_assembly_has_no_location()
    {
        Assert.Equal("170.5.96.0", Ok(DacFx.VersionOf("", "170.5.96.0+734bf28f08f22c5b8c346dc5eb7137b4593b6a07")).ToString());
        Assert.Equal(("toolchain.dacfx-version", 6), (Failed(DacFx.VersionOf("", null)).Code, Contract.Exit(Failed(DacFx.VersionOf("", null)))));
        Assert.Equal("toolchain.dacfx-version", Failed(DacFx.VersionOf("", "")).Code);
        Assert.Equal("170.5.96", Ok(DacFx.Version).ToString());
    }

    /// <summary>
    /// The measured rename report (a column renamed through a refactorlog entry, then the view over it altered): an item carries its own
    /// type and name, and its key is the element either set holds, under the parent that set gives it; a name no set explains is keyed from
    /// its parts, as a composed object is.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_report_item_is_keyed_under_the_parent_the_element_sets_hold()
    {
        const string Renamed = "<DeploymentReport xmlns=\"http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02\"><Alerts />"
            + "<Operations><Operation Name=\"Rename\"><Item Value=\"[dbo].[T].[Mail]\" Type=\"SqlSimpleColumn\" /></Operation></Operations></DeploymentReport>";
        var table = Key("Table", "dbo", "T");
        var view = Key("View", "dbo", "T");
        SortedArray<Element> underTable = [Element(table), Element(Ok(ElementKey.Of(table, "Column", Ok(Name.Of("Mail")))))];
        SortedArray<Element> underView = [Element(view)];

        var (inTable, inView, unexplained) = (Keyed(Renamed, underTable, []), Keyed(Renamed, [], underView), Keyed(Renamed, [], []));

        Assert.Equal(("Column [dbo].[T].[Mail]", "Table"), (inTable.ToString(), inTable.Parent?.Type));
        Assert.Equal(("Column [dbo].[T].[Mail]", "View"), (inView.ToString(), inView.Parent?.Type));
        Assert.Equal(("Column [dbo].[T].[Mail]", "Column"), (unexplained.ToString(), unexplained.Parent?.Type));
    }

    /// <summary>A report item of a serialized type the source package no longer holds, such as a dropped sequence, keys by the fixed map, which a map read from the package's own model.xml could not.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_dropped_type_the_source_package_lacks_still_keys()
    {
        const string Dropped = "<DeploymentReport xmlns=\"http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02\"><Alerts />"
            + "<Operations><Operation Name=\"Drop\"><Item Value=\"[dbo].[Seq]\" Type=\"SqlSequence\" /><Item Value=\"[dbo].[F]\" Type=\"SqlInlineTableValuedFunction\" /></Operation></Operations></DeploymentReport>";

        var report = Ok(DacFx.Report(Dropped, [], []));

        Assert.Equal(["Drop Sequence [dbo].[Seq]", "Drop TableValuedFunction [dbo].[F]"], report.Report.Operations.Select(o => o.Kind + " " + o.Key).Order(StringComparer.Ordinal));
        Assert.Empty(report.Notes);
    }

    /// <summary>A report's XML of another shape than DacFx 170.5.96 writes is refused, naming what was not expected; an alert's issue carries DacFx's text and its id.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_report_reads_its_alerts_and_refuses_an_element_DacFx_does_not_write()
    {
        const string Dropping = "<DeploymentReport xmlns=\"http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02\"><Alerts><Alert Name=\"DataIssue\">"
            + "<Issue Value=\"The column [dbo].[T].[Legacy] is being dropped, data loss could occur.\" Id=\"1\" /></Alert><Alert Name=\"DataMotion\"><Issue Value=\"[dbo].[T]\" /></Alert></Alerts>"
            + "<Operations><Operation Name=\"Alter\"><Item Value=\"[dbo].[T]\" Type=\"SqlTable\"><Issue Id=\"1\" /></Item></Operation><Operation Name=\"AddSystemVersioning\">"
            + "<Item Value=\"[dbo].[T]\" Type=\"UnlistedType\" /></Operation></Operations></DeploymentReport>";

        var read = Ok(DacFx.Report(Dropping, [], []));
        var unread = Failed(DacFx.Report(Dropping.Replace("<Alerts>", "<Warnings /><Alerts>", StringComparison.Ordinal), [], []));

        Assert.Equal([(PlanAlertKind.Of("DataIssue"), (int?)1, "The column [dbo].[T].[Legacy] is being dropped, data loss could occur."), (PlanAlertKind.Of("DataMotion"), null, "[dbo].[T]")],
            read.Report.Alerts.Select(a => (a.Kind, a.Id, a.Text)));
        Assert.Equal(["AddSystemVersioning UnlistedType [dbo].[T] []", "Alter Table [dbo].[T] [1]"],
            read.Report.Operations.Select(o => o.Kind + " " + o.Key + " [" + string.Join(",", o.Issues) + "]").Order(StringComparer.Ordinal));
        Assert.Equal(["plan.unlisted-type"], read.Notes.Select(n => n.Code));
        Assert.Equal(("plan.report-unread", 6), (unread.Code, Contract.Exit(unread)));
        Assert.Contains("Warnings", unread.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A Sql170 package planned against a Sql160 one under the pipeline profile (AllowIncompatiblePlatform False): DacFx throws with no
    /// messages and gives its reason in the exception's chain alone (measured), and the plan is refused as plan.platform naming both platforms.
    /// The same text in a constructed exception maps alike; a failure given DacFx's messages quotes them and not the chain's text.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_failure_with_no_messages_still_names_DacFx_s_reason_from_the_exception_chain()
    {
        using var newer = Ok(Ssdt.Open(Packaged("newer", SqlServerVersion.Sql170, "CREATE TABLE dbo.T (Id INT NOT NULL);")));
        using var older = Ok(Ssdt.Open(Packaged("older", SqlServerVersion.Sql160, "CREATE TABLE dbo.T (Id INT NOT NULL);")));
        var chain = new DacServicesException("An error occurred during deployment plan generation. Deployment cannot continue.",
            new InvalidOperationException("A project which specifies SQL Server 2025 as the target platform cannot be published to SQL Server 2022."));

        var refused = Failed(DacFx.Plan(newer, older, "Target", Strict(), []));
        var fromChain = DacFx.Failure(chain);
        var fromMessages = DacFx.Failure([new DacMessage(DacMessageType.Error, 71501, "[dbo].[V] has an unresolved reference to object [dbo].[Missing].", "SQL", "SqlView")], chain);

        Assert.Equal(("plan.platform", 6), (refused.Code, Contract.Exit(refused)));
        Assert.StartsWith("The package targets Sql170 and " + older.Source + " is Sql160;", refused.Message, StringComparison.Ordinal);
        Assert.Empty(fromChain.Messages);
        Assert.Equal(refused, DacFx.Unplanned(fromChain, newer, older));
        Assert.Equal(["Error SQL71501: [dbo].[V] has an unresolved reference to object [dbo].[Missing]."], fromMessages.Messages.Select(m => m.ToString()));
    }

    /// <summary>A failed publish's messages hold DacFx's errors and warnings and its informational lines, a deployment script's PRINT output among them; the error quotes the first two and not the third.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Informational_messages_never_reach_a_refusal()
    {
        var failure = DacFx.Failure(
        [
            new DacMessage(DacMessageType.Error, 72014, "Core Microsoft SqlClient Data Provider: Msg 50000, Level 16, State 127, Line 6 Rows were detected.", "SQL", null),
            new DacMessage(DacMessageType.Warning, 72015, "The deployment script was stopped.", "SQL", null),
            new DacMessage(DacMessageType.Message, 0, "Generated 12 rows for dbo.Customer.", "SQL", null),
        ], new DacServicesException("Could not deploy package."));
        var copy = new SqlServer.Copy(CopyName.Make("host", 1, 0x0a1b2c3d), "Server=localhost,11433;User ID=sa", scratch);

        var error = DacFx.Failed(copy, failure);

        Assert.Equal(("server.failed", 4), (error.Code, Contract.Exit(error)));
        Assert.Contains("Error SQL72014: ", error.Message, StringComparison.Ordinal);
        Assert.Contains("Warning SQL72015: The deployment script was stopped.", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Generated 12 rows", error.Message, StringComparison.Ordinal);
    }

    /// <summary>DF-9's other half: a value given for a variable the package does not declare reaches no script, and the plan says so in a note.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_value_for_a_variable_the_package_does_not_declare_is_a_note()
    {
        using var package = Ok(Ssdt.Open(Packaged("undeclared", SqlServerVersion.Sql160, "CREATE TABLE dbo.T (Id INT NOT NULL);")));
        var extra = new SqlCmdValue(Ok(SqlCmdName.Of("the environments file", "Extra")), "dev", Referenced: false);

        var plan = Ok(DacFx.Plan(package, package, "Target", Strict(), [extra]));

        Assert.True(plan.Report.IsEmpty);
        Assert.Equal([("sqlcmd.undeclared", "$(Extra)")], plan.Notes.Select(n => (n.Code, n.Subject)));
        Assert.Empty(package.Declared);
    }

    /// <summary>
    /// DacFx checks a live plan and refuses a model whose collation ignores case against a database whose collation does not (Error SQL72030,
    /// measured on DacFx 170.5.96 whether or not a name differs in case); package to package it checks nothing and plans dbo.Customer against
    /// dbo.CUSTOMER as one table. DacFx.Plan makes the live plan's check: plan.collation at exit 6, naming both collations and SQL72030; the
    /// reverse, a case-sensitive package against a case-insensitive target, plans.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_case_insensitive_package_against_a_case_sensitive_target_is_refused_as_DacFx_refuses_the_live_plan()
    {
        using var insensitive = Ok(Ssdt.Open(Collated("insensitive", SqlServerVersion.Sql160, "SQL_Latin1_General_CP1_CI_AS", "CREATE TABLE dbo.Customer (Id INT NOT NULL);")));
        using var sensitive = Ok(Ssdt.Open(Collated("sensitive", SqlServerVersion.Sql160, "Latin1_General_CS_AS", "CREATE TABLE dbo.CUSTOMER (Id INT NOT NULL);")));

        var refused = Failed(DacFx.Plan(insensitive, sensitive, "Target", Strict(), []));

        Assert.Equal(("plan.collation", 6), (refused.Code, Contract.Exit(refused)));
        Assert.Contains("(SQL_Latin1_General_CP1_CI_AS)", refused.Message, StringComparison.Ordinal);
        Assert.Contains("(Latin1_General_CS_AS)", refused.Message, StringComparison.Ordinal);
        Assert.Contains("SQL72030", refused.Message, StringComparison.Ordinal);
        Assert.IsType<Result<Plan>.Ok>(DacFx.Plan(sensitive, insensitive, "Target", Strict(), []));
    }

    private string Packaged(string name, SqlServerVersion platform, params string[] scripts) => Collated(name, platform, null, scripts);

    /// <summary>A package built in memory under the collation given, the model's default when null.</summary>
    private string Collated(string name, SqlServerVersion platform, string? collation, params string[] scripts)
    {
        var dacpac = Path.Combine(scratch, name + ".dacpac");
        using var model = new TSqlModel(platform, new TSqlModelOptions { Collation = collation });
        foreach (var script in scripts)
        {
            model.AddObjects(script);
        }

        DacPackageExtensions.BuildPackage(dacpac, model, new PackageMetadata());
        return dacpac;
    }

    private static PublishProfile.Strict Strict() => Ok(PublishProfiles.Load(Path.Combine(Repository.Root, Pipeline)));

    private static ElementKey Keyed(string report, SortedArray<Element> source, SortedArray<Element> target) => Ok(DacFx.Report(report, source, target)).Report.Operations.Single().Key;

    private static ElementKey Key(string type, string schema, string name) => Ok(ElementKey.Of(type, Ok(Name.Of(schema, name))));

    private static Element Element(ElementKey key) => Ok(Kernel.Element.Of(key, [], []));

    private static T Ok<T>(Result<T> result) => result.Match(value => value, error => throw new Xunit.Sdk.XunitException(error.Code + ": " + error.Message));

    private static Error Failed<T>(Result<T> result) => Assert.IsType<Result<T>.Failed>(result).Error;
}
