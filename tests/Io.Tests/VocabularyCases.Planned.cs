using System;
using System.Collections.Generic;
using DbChange.Kernel;
using DbChange.Tests;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>DacFx's own plans of the changes v3's cases of a rebuild's key, an INCLUDE and a new table write by hand.</summary>
public sealed partial class VocabularyCases
{
    /// <summary>Each such case's change as T-SQL, the model before it and the model it brings.</summary>
    private static readonly Dictionary<string, (string[] Before, string[] After)> Planned = new(StringComparer.Ordinal)
    {
        [RebuiltPrimaryKey] = (["CREATE TABLE dbo.D (Id INT NOT NULL, Path NVARCHAR(460) NOT NULL, CONSTRAINT PK_D PRIMARY KEY CLUSTERED (Id));"],
            ["CREATE TABLE dbo.D (Id INT NOT NULL, Path NVARCHAR(460) NOT NULL, CONSTRAINT PK_D PRIMARY KEY CLUSTERED (Path));"]),
        [RebuiltUnique] = (["CREATE TABLE dbo.E (Id INT NOT NULL, Path NVARCHAR(900) NOT NULL, CONSTRAINT PK_E PRIMARY KEY CLUSTERED (Id));"],
            ["CREATE TABLE dbo.E (Id INT IDENTITY (1, 1) NOT NULL, Path NVARCHAR(900) NOT NULL, CONSTRAINT PK_E PRIMARY KEY CLUSTERED (Id),"
                + " CONSTRAINT UQ_E_Path UNIQUE NONCLUSTERED (Path));"]),
        [IncludesAComputedColumn] = (["CREATE TABLE dbo.T (Id INT NOT NULL, Ref VARCHAR(20) NULL, RefNum AS (CONVERT (INT, [Ref])));"],
            ["CREATE TABLE dbo.T (Id INT NOT NULL, Ref VARCHAR(20) NULL, RefNum AS (CONVERT (INT, [Ref])));",
                "CREATE NONCLUSTERED INDEX IX_T_Id ON dbo.T (Id) INCLUDE (RefNum);"]),
        [ComputedOnANewTable] = (["CREATE TABLE dbo.Customer (Id INT NOT NULL CONSTRAINT PK_Customer_Id PRIMARY KEY);"],
            ["CREATE TABLE dbo.Customer (Id INT NOT NULL CONSTRAINT PK_Customer_Id PRIMARY KEY);",
                "CREATE TABLE dbo.T (Id INT NOT NULL, Ref VARCHAR(20) NULL, RefNum AS (CONVERT (INT, [Ref])));",
                "CREATE NONCLUSTERED INDEX IX_T_RefNum ON dbo.T (RefNum);"]),
    };

    /// <summary>
    /// The plans of those cases are written in the shapes DacFx writes, and DacFx 170.5.96 plans each change package to package, under
    /// the pipeline profile and connected to nothing, into the same statements: a clustered primary key moved onto a column of 920 bytes
    /// and a new unique constraint of 1,800 bytes are each a rebuild whose tmp_ms_xx_ table declares the key inline, and the indexes are
    /// a plain CREATE INDEX. Each plan, read whole with the #tmpErrors blocks around DacFx's statements, holds no unclassified statement
    /// and asks what the case asks.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData(RebuiltPrimaryKey)]
    [InlineData(RebuiltUnique)]
    [InlineData(IncludesAComputedColumn)]
    [InlineData(ComputedOnANewTable)]
    public void DacFx_s_own_plan_of_a_case_s_change_asks_what_the_case_asks(string id)
    {
        using var scratch = ScratchFolder.Temporary("vocabulary-plans");
        using var was = Expect.Value(Ssdt.Open(Packaged(scratch, "before", Planned[id].Before)));
        using var now = Expect.Value(Ssdt.Open(Packaged(scratch, "after", Planned[id].After)));
        var plan = Expect.Value(DacFx.Plan(now, was, "Target", Expect.Value(PublishProfiles.Load(GoldenProject.Profile)), []));
        var (from, to) = (Expect.Value(was.Elements).Elements, Expect.Value(now.Elements).Elements);
        var change = Expect.Value(Change.Between(from, to, [], Expect.Value(Ssdt.CollationOf(to))));
        var listing = Expect.Value(PlanStatements.Read(plan.Script, [], new(null, null), new(null, null)));
        var sites = BlockOnPossibleDataLoss.Sites(Expect.Value(BlockOnPossibleDataLoss.Read(plan.Script, [])));

        var reading = Vocabulary.Read(listing.Plan, listing.ScriptsBefore, Expect.Value(Preconditions.Of(from, to, change, sites)), from,
            to, change);

        Assert.Empty(reading.Unclassified);
        Assert.Equal(SortedArray.Of(Cases[id].Expected), reading.Preconditions);
    }

    /// <summary>A package of the model the T-SQL declares, built under the scratch folder.</summary>
    private static string Packaged(ScratchFolder scratch, string name, string[] scripts)
    {
        var dacpac = scratch.Under(name + ".dacpac");
        using var model = new TSqlModel(SqlServerVersion.Sql160, new TSqlModelOptions());
        foreach (var script in scripts)
        {
            model.AddObjects(script);
        }

        DacPackageExtensions.BuildPackage(dacpac, model, new PackageMetadata());
        return dacpac;
    }
}
