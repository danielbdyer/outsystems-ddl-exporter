using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Dmm;
using Osm.Smo;
using Osm.Validation.Tightening;
using Tests.Support;

namespace Osm.Dmm.Tests;

public class DmmComparatorTests
{
    private readonly SmoModel _smoModel;

    public DmmComparatorTests()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var options = TighteningOptions.Default;
        var policy = new TighteningPolicy();
        var decisions = policy.Decide(model, snapshot, options);
        var factory = new SmoModelFactory();
        _smoModel = factory.Create(model, decisions, SmoBuildOptions.FromEmission(options.Emission));
    }

    [Fact]
    public void Compare_returns_match_for_equivalent_dmm_script()
    {
        var comparator = new DmmComparator();
        var comparison = comparator.Compare(_smoModel, ParseScript(EdgeCaseScript));
        if (!comparison.IsMatch)
        {
            foreach (var diff in comparison.Differences)
            {
                Console.WriteLine($"Comparison diff: {diff}");
            }
        }
        Assert.True(comparison.IsMatch);
        Assert.Empty(comparison.Differences);
    }

    [Fact]
    public void Compare_detects_nullability_difference()
    {
        var comparator = new DmmComparator();
        var comparison = comparator.Compare(_smoModel, ParseScript(EdgeCaseScript.Replace("[EMAIL] NVARCHAR(255) NOT NULL", "[EMAIL] NVARCHAR(255) NULL")));
        Assert.False(comparison.IsMatch);
        Assert.Contains(comparison.Differences, diff => diff.Contains("nullability mismatch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compare_detects_column_order_difference()
    {
        var reorderedScript = EdgeCaseScript.Replace(
            "[ID] INT NOT NULL,\n    [EMAIL] NVARCHAR(255) NOT NULL,",
            "[EMAIL] NVARCHAR(255) NOT NULL,\n    [ID] INT NOT NULL,");
        var comparator = new DmmComparator();
        var comparison = comparator.Compare(_smoModel, ParseScript(reorderedScript));

        Assert.False(comparison.IsMatch);
        Assert.Contains(comparison.Differences, diff => diff.Contains("column order mismatch for dbo.OSUSR_ABC_CUSTOMER", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compare_detects_missing_table()
    {
        var script = EdgeCaseScript.Replace("CREATE TABLE [dbo].[OSUSR_DEF_CITY](\n    [ID] INT NOT NULL,\n    [NAME] NVARCHAR(200) NOT NULL,\n    [ISACTIVE] BIT NOT NULL,\n    CONSTRAINT [PK_City] PRIMARY KEY ([ID])\n);\n", string.Empty);
        var comparator = new DmmComparator();
        var comparison = comparator.Compare(_smoModel, ParseScript(script));

        Assert.False(comparison.IsMatch);
        Assert.Contains(comparison.Differences, diff => diff.Equals("missing table dbo.OSUSR_DEF_CITY", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compare_detects_unexpected_table()
    {
        var script = EdgeCaseScript + "CREATE TABLE [dbo].[EXTRA](\n    [ID] INT NOT NULL,\n    CONSTRAINT [PK_EXTRA] PRIMARY KEY ([ID])\n);";
        var comparator = new DmmComparator();
        var comparison = comparator.Compare(_smoModel, ParseScript(script));

        Assert.False(comparison.IsMatch);
        Assert.Contains(comparison.Differences, diff => diff.Equals("unexpected table dbo.EXTRA", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compare_detects_missing_column()
    {
        var script = EdgeCaseScript.Replace("    [EMAIL] NVARCHAR(255) NOT NULL,\n", string.Empty);
        var comparator = new DmmComparator();
        var comparison = comparator.Compare(_smoModel, ParseScript(script));

        Assert.False(comparison.IsMatch);
        Assert.Contains(comparison.Differences, diff => diff.Contains("column count mismatch for dbo.OSUSR_ABC_CUSTOMER", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(comparison.Differences, diff => diff.Contains("missing columns for dbo.OSUSR_ABC_CUSTOMER", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compare_detects_unexpected_column()
    {
        var script = EdgeCaseScript.Replace("    [EMAIL] NVARCHAR(255) NOT NULL,\n", "    [EMAIL] NVARCHAR(255) NOT NULL,\n    [EXTRA] INT NULL,\n");
        var comparator = new DmmComparator();
        var comparison = comparator.Compare(_smoModel, ParseScript(script));

        Assert.False(comparison.IsMatch);
        Assert.Contains(comparison.Differences, diff => diff.Contains("column count mismatch for dbo.OSUSR_ABC_CUSTOMER", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(comparison.Differences, diff => diff.Contains("unexpected columns for dbo.OSUSR_ABC_CUSTOMER", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compare_detects_primary_key_difference()
    {
        var script = EdgeCaseScript.Replace("CONSTRAINT [PK_Customer] PRIMARY KEY ([ID])", "CONSTRAINT [PK_Customer] PRIMARY KEY ([EMAIL])");
        var comparator = new DmmComparator();
        var comparison = comparator.Compare(_smoModel, ParseScript(script));

        Assert.False(comparison.IsMatch);
        Assert.Contains(comparison.Differences, diff => diff.Contains("primary key mismatch for dbo.OSUSR_ABC_CUSTOMER", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<DmmTable> ParseScript(string script)
    {
        var parser = new DmmParser();
        using var reader = new StringReader(script);
        var parseResult = parser.Parse(reader);
        if (!parseResult.IsSuccess)
        {
            foreach (var error in parseResult.Errors)
            {
                Console.WriteLine($"Parse error: {error.Code} -> {error.Message}");
            }
        }

        Assert.True(parseResult.IsSuccess, string.Join(Environment.NewLine, parseResult.Errors.Select(e => $"{e.Code}:{e.Message}")));
        return parseResult.Value;
    }

    [Fact]
    public void Compare_detects_column_order_difference()
    {
        var parser = new DmmParser();
        var reorderedScript = EdgeCaseScript.Replace(
            "[ID] INT NOT NULL,\n    [EMAIL] NVARCHAR(255) NOT NULL,",
            "[EMAIL] NVARCHAR(255) NOT NULL,\n    [ID] INT NOT NULL,");
        using var reader = new StringReader(reorderedScript);
        var parseResult = parser.Parse(reader);
        if (!parseResult.IsSuccess)
        {
            foreach (var error in parseResult.Errors)
            {
                Console.WriteLine($"Parse error: {error.Code} -> {error.Message}");
            }
        }

        Assert.True(parseResult.IsSuccess, string.Join(Environment.NewLine, parseResult.Errors.Select(e => $"{e.Code}:{e.Message}")));

        var comparator = new DmmComparator();
        var comparison = comparator.Compare(_smoModel, parseResult.Value);

        Assert.False(comparison.IsMatch);
        Assert.Contains(comparison.Differences, diff => diff.Contains("column order mismatch for dbo.OSUSR_ABC_CUSTOMER", StringComparison.OrdinalIgnoreCase));
    }

    private const string EdgeCaseScript = @"CREATE TABLE [dbo].[OSUSR_ABC_CUSTOMER](
    [ID] INT NOT NULL,
    [EMAIL] NVARCHAR(255) NOT NULL,
    [FIRSTNAME] NVARCHAR(100) NULL,
    [LASTNAME] NVARCHAR(100) NULL,
    [CITYID] INT NOT NULL,
    CONSTRAINT [PK_Customer] PRIMARY KEY ([ID])
);
CREATE TABLE [dbo].[OSUSR_DEF_CITY](
    [ID] INT NOT NULL,
    [NAME] NVARCHAR(200) NOT NULL,
    [ISACTIVE] BIT NOT NULL,
    CONSTRAINT [PK_City] PRIMARY KEY ([ID])
);
CREATE TABLE [billing].[BILLING_ACCOUNT](
    [ID] INT NOT NULL,
    [ACCOUNTNUMBER] VARCHAR(50) NOT NULL,
    [EXTREF] VARCHAR(50) NULL,
    CONSTRAINT [PK_BillingAccount] PRIMARY KEY ([ID])
);
CREATE TABLE [dbo].[OSUSR_XYZ_JOBRUN](
    [ID] INT NOT NULL,
    [TRIGGEREDBYUSERID] INT NULL,
    [CREATEDON] DATETIME NOT NULL,
    CONSTRAINT [PK_JobRun] PRIMARY KEY ([ID])
);";
}
