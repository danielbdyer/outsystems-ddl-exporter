using System;
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
        var parser = new DmmParser();
        using var reader = new StringReader(EdgeCaseScript);
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
        var parser = new DmmParser();
        using var reader = new StringReader(EdgeCaseScript.Replace("[EMAIL] NVARCHAR(255) NOT NULL", "[EMAIL] NVARCHAR(255) NULL"));
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
        Assert.Contains(comparison.Differences, diff => diff.Contains("nullability mismatch", StringComparison.OrdinalIgnoreCase));
    }

    private const string EdgeCaseScript = @"CREATE TABLE [dbo].[OSUSR_ABC_CUSTOMER](
    [ID] INT NOT NULL,
    [EMAIL] NVARCHAR(255) NOT NULL,
    [FIRSTNAME] NVARCHAR(100) NULL,
    [LASTNAME] NVARCHAR(100) NULL,
    [CITYID] INT NOT NULL,
    [LEGACYCODE] NVARCHAR(50) NULL,
    CONSTRAINT [PK_OSUSR_ABC_CUSTOMER] PRIMARY KEY ([ID])
);
CREATE TABLE [dbo].[OSUSR_DEF_CITY](
    [ID] INT NOT NULL,
    [NAME] NVARCHAR(200) NOT NULL,
    [ISACTIVE] BIT NOT NULL,
    CONSTRAINT [PK_OSUSR_DEF_CITY] PRIMARY KEY ([ID])
);
CREATE TABLE [billing].[BILLING_ACCOUNT](
    [ID] INT NOT NULL,
    [ACCOUNTNUMBER] VARCHAR(50) NOT NULL,
    [EXTREF] VARCHAR(50) NULL,
    CONSTRAINT [PK_BILLING_ACCOUNT] PRIMARY KEY ([ID])
);
CREATE TABLE [dbo].[OSUSR_XYZ_JOBRUN](
    [ID] INT NOT NULL,
    [TRIGGEREDBYUSERID] INT NULL,
    [CREATEDON] DATETIME NOT NULL,
    CONSTRAINT [PK_OSUSR_XYZ_JOBRUN] PRIMARY KEY ([ID])
);";
}
