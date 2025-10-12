using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.ValueObjects;
using Osm.Dmm;
using Osm.Smo;
using Osm.Validation.Tightening;
using Tests.Support;
using Xunit;

namespace Osm.Dmm.Tests;

public class DmmComparatorTests
{
    private readonly SmoModel _smoModel;
    private readonly IReadOnlyList<DmmTable> _baselineTables;

    public DmmComparatorTests()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var options = TighteningOptions.Default;
        var policy = new TighteningPolicy();
        var decisions = policy.Decide(model, snapshot, options);
        var factory = new SmoModelFactory();
        var smoOptions = SmoBuildOptions.FromEmission(options.Emission);
        _smoModel = factory.Create(
            model,
            decisions,
            profile: snapshot,
            options: smoOptions);

        var projection = new SmoDmmLens().Project(new SmoDmmLensRequest(_smoModel, smoOptions));
        if (!projection.IsSuccess)
        {
            throw new InvalidOperationException("Unable to project SMO model into DMM comparison tables for test setup.");
        }

        _baselineTables = projection.Value;
    }

    [Fact]
    public void Compare_returns_match_for_equivalent_dmm_script()
    {
        var comparator = new DmmComparator();
        var comparison = comparator.Compare(_baselineTables, ParseScript(EdgeCaseScript));

        Assert.True(comparison.IsMatch);
        Assert.Empty(comparison.ModelDifferences);
        Assert.Empty(comparison.SsdtDifferences);
    }

    [Fact]
    public void Compare_detects_nullability_difference()
    {
        var comparator = new DmmComparator();
        var comparison = comparator.Compare(
            _baselineTables,
            ParseScript(EdgeCaseScript.Replace(
                "[EMAIL] NVARCHAR(255) COLLATE Latin1_General_CI_AI NOT NULL",
                "[EMAIL] NVARCHAR(255) COLLATE Latin1_General_CI_AI NULL")));

        Assert.False(comparison.IsMatch);
        Assert.Contains(
            comparison.SsdtDifferences,
            diff => string.Equals(diff.Property, "Nullability", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Column, "EMAIL", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(comparison.ModelDifferences);
    }

    [Fact]
    public void Compare_detects_column_order_difference()
    {
        var reorderedScript = EdgeCaseScript.Replace(
            "[ID] BIGINT NOT NULL,\n    [EMAIL] NVARCHAR(255) COLLATE Latin1_General_CI_AI NOT NULL,",
            "[EMAIL] NVARCHAR(255) COLLATE Latin1_General_CI_AI NOT NULL,\n    [ID] BIGINT NOT NULL,");

        var comparator = new DmmComparator();
        var comparison = comparator.Compare(_baselineTables, ParseScript(reorderedScript));

        Assert.False(comparison.IsMatch);
        Assert.Contains(
            comparison.SsdtDifferences,
            diff => string.Equals(diff.Property, "ColumnOrder", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Table, "OSUSR_ABC_CUSTOMER", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(comparison.ModelDifferences);
    }

    [Fact]
    public void Compare_detects_missing_table()
    {
        var script = EdgeCaseScript.Replace(
            "CREATE TABLE [dbo].[OSUSR_DEF_CITY](\n    [ID] BIGINT NOT NULL,\n    [NAME] NVARCHAR(200) NOT NULL,\n    [ISACTIVE] BIT NOT NULL,\n    CONSTRAINT [PK_City] PRIMARY KEY ([ID])\n);\n",
            string.Empty);

        var comparator = new DmmComparator();
        var comparison = comparator.Compare(_baselineTables, ParseScript(script));

        Assert.False(comparison.IsMatch);
        Assert.Contains(
            comparison.ModelDifferences,
            diff => string.Equals(diff.Property, "TablePresence", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Schema, "dbo", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Table, "OSUSR_DEF_CITY", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Expected, "Present", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Actual, "Missing", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(comparison.SsdtDifferences);
    }

    [Fact]
    public void Compare_detects_unexpected_table()
    {
        var script = EdgeCaseScript + "CREATE TABLE [dbo].[EXTRA](\n    [ID] BIGINT NOT NULL,\n    CONSTRAINT [PK_EXTRA] PRIMARY KEY ([ID])\n);";
        var comparator = new DmmComparator();
        var comparison = comparator.Compare(_baselineTables, ParseScript(script));

        Assert.False(comparison.IsMatch);
        Assert.Contains(
            comparison.SsdtDifferences,
            diff => string.Equals(diff.Property, "TablePresence", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Schema, "dbo", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Table, "EXTRA", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Expected, "Missing", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Actual, "Present", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(comparison.ModelDifferences);
    }

    [Fact]
    public void Compare_detects_missing_column()
    {
        var script = EdgeCaseScript.Replace("    [EMAIL] NVARCHAR(255) COLLATE Latin1_General_CI_AI NOT NULL,\n", string.Empty);
        var comparator = new DmmComparator();
        var comparison = comparator.Compare(_baselineTables, ParseScript(script));

        Assert.False(comparison.IsMatch);
        Assert.Contains(
            comparison.ModelDifferences,
            diff => string.Equals(diff.Property, "Presence", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Column, "EMAIL", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Expected, "Present", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Actual, "Missing", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            comparison.ModelDifferences,
            diff => string.Equals(diff.Property, "ColumnCount", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Table, "OSUSR_ABC_CUSTOMER", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compare_detects_unexpected_column()
    {
        var script = EdgeCaseScript.Replace(
            "    [EMAIL] NVARCHAR(255) COLLATE Latin1_General_CI_AI NOT NULL,\n",
            "    [EMAIL] NVARCHAR(255) COLLATE Latin1_General_CI_AI NOT NULL,\n    [EXTRA] INT NULL,\n");

        var comparator = new DmmComparator();
        var comparison = comparator.Compare(_baselineTables, ParseScript(script));

        Assert.False(comparison.IsMatch);
        Assert.Contains(
            comparison.SsdtDifferences,
            diff => string.Equals(diff.Property, "Presence", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Column, "EXTRA", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Expected, "Missing", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Actual, "Present", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            comparison.SsdtDifferences,
            diff => string.Equals(diff.Property, "Collation", StringComparison.OrdinalIgnoreCase)
                || string.Equals(diff.Property, "Default", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compare_detects_primary_key_difference()
    {
        var script = EdgeCaseScript.Replace(
            "CONSTRAINT [PK_Customer] PRIMARY KEY ([ID])",
            "CONSTRAINT [PK_Customer] PRIMARY KEY ([EMAIL])");

        var comparator = new DmmComparator();
        var comparison = comparator.Compare(_baselineTables, ParseScript(script));

        Assert.False(comparison.IsMatch);
        Assert.Contains(
            comparison.SsdtDifferences,
            diff => string.Equals(diff.Property, "PrimaryKeyOrdinal", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Table, "OSUSR_ABC_CUSTOMER", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compare_detects_data_type_difference()
    {
        var script = EdgeCaseScript.Replace(
            "[EMAIL] NVARCHAR(255) COLLATE Latin1_General_CI_AI NOT NULL",
            "[EMAIL] NVARCHAR(200) COLLATE Latin1_General_CI_AI NOT NULL");
        var comparator = new DmmComparator();
        var comparison = comparator.Compare(_baselineTables, ParseScript(script));

        Assert.False(comparison.IsMatch);
        Assert.Contains(
            comparison.SsdtDifferences,
            diff => string.Equals(diff.Property, "DataType", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Column, "EMAIL", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compare_detects_missing_index_when_feature_enabled()
    {
        var comparator = new DmmComparator(DmmComparisonFeatures.Columns | DmmComparisonFeatures.PrimaryKeys | DmmComparisonFeatures.Indexes);
        var expectedTable = new DmmTable(
            "dbo",
            "Example",
            new[] { new DmmColumn("Id", "int", false, null, null, null) },
            new[] { "Id" },
            new[]
            {
                new DmmIndex(
                    "IX_Example",
                    true,
                    new[] { new DmmIndexColumn("Id", false) },
                    Array.Empty<DmmIndexColumn>(),
                    null,
                    false,
                    new DmmIndexOptions(null, null, null, null, null, null))
            },
            Array.Empty<DmmForeignKey>(),
            null);
        var actualTable = expectedTable with { Indexes = Array.Empty<DmmIndex>() };

        var comparison = comparator.Compare(new[] { expectedTable }, new[] { actualTable });

        Assert.False(comparison.IsMatch);
        Assert.Contains(
            comparison.ModelDifferences,
            diff => string.Equals(diff.Property, "Presence", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Index, "IX_Example", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Expected, "Present", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Actual, "Missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compare_detects_foreign_key_drift_when_feature_enabled()
    {
        var comparator = new DmmComparator(DmmComparisonFeatures.Columns | DmmComparisonFeatures.PrimaryKeys | DmmComparisonFeatures.ForeignKeys);
        var expectedTable = new DmmTable(
            "dbo",
            "Orders",
            new[] { new DmmColumn("CustomerId", "int", false, null, null, null) },
            new[] { "CustomerId" },
            Array.Empty<DmmIndex>(),
            new[]
            {
                new DmmForeignKey("FK_Orders_Customers", "CustomerId", "dbo", "Customers", "Id", "NoAction", false)
            },
            null);
        var actualTable = expectedTable with { ForeignKeys = new[] { expectedTable.ForeignKeys[0] with { ReferencedTable = "Customers_Archive" } } };

        var comparison = comparator.Compare(new[] { expectedTable }, new[] { actualTable });

        Assert.False(comparison.IsMatch);
        Assert.Contains(
            comparison.SsdtDifferences,
            diff => string.Equals(diff.Property, "ReferencedTable", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.ForeignKey, "FK_Orders_Customers", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compare_detects_extended_property_drift_when_feature_enabled()
    {
        var comparator = new DmmComparator(DmmComparisonFeatures.Columns | DmmComparisonFeatures.PrimaryKeys | DmmComparisonFeatures.ExtendedProperties);
        var expectedTable = new DmmTable(
            "dbo",
            "Customers",
            new[] { new DmmColumn("Email", "nvarchar(255)", false, null, null, "Customer email") },
            new[] { "Email" },
            Array.Empty<DmmIndex>(),
            Array.Empty<DmmForeignKey>(),
            "Customer table");
        var actualTable = expectedTable with
        {
            Columns = new[] { expectedTable.Columns[0] with { Description = "Updated email" } },
            Description = "Changed description"
        };

        var comparison = comparator.Compare(new[] { expectedTable }, new[] { actualTable });

        Assert.False(comparison.IsMatch);
        Assert.Contains(
            comparison.SsdtDifferences,
            diff => string.Equals(diff.Property, "Description", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Column, "Email", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            comparison.SsdtDifferences,
            diff => string.Equals(diff.Property, "Description", StringComparison.OrdinalIgnoreCase)
                && string.Equals(diff.Table, "Customers", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compare_accepts_equivalent_type_spacing_and_casing()
    {
        var script = EdgeCaseScript
            .Replace(
                "[EMAIL] NVARCHAR(255) COLLATE Latin1_General_CI_AI NOT NULL",
                "[EMAIL] NvArChAr ( 255 ) COLLATE Latin1_General_CI_AI NOT NULL")
            .Replace("[ACCOUNTNUMBER] VARCHAR(50) NOT NULL", "[ACCOUNTNUMBER] varchar ( 50 ) NOT NULL");

        var comparator = new DmmComparator();
        var comparison = comparator.Compare(_baselineTables, ParseScript(script));

        Assert.True(comparison.IsMatch);
        Assert.Empty(comparison.ModelDifferences);
        Assert.Empty(comparison.SsdtDifferences);
    }

    [Fact]
    public void Compare_honors_entity_name_overrides()
    {
        var overrideResult = NamingOverrideRule.Create(null, null, null, "Customer", "CUSTOMER_EXTERNAL");
        Assert.True(overrideResult.IsSuccess);
        var namingOverrides = NamingOverrideOptions.Create(new[] { overrideResult.Value });
        Assert.True(namingOverrides.IsSuccess);

        var renamedScript = EdgeCaseScript.Replace("OSUSR_ABC_CUSTOMER", "CUSTOMER_EXTERNAL");

        var comparator = new DmmComparator();
        var comparison = comparator.Compare(
            ProjectSmo(_smoModel, namingOverrides.Value),
            ParseScript(renamedScript));

        Assert.True(
            comparison.IsMatch,
            $"Model: {string.Join(" | ", comparison.ModelDifferences.Select(DescribeDifference))}; " +
            $"DMM: {string.Join(" | ", comparison.SsdtDifferences.Select(DescribeDifference))}");
    }

    [Fact]
    public void Compare_honors_module_scoped_entity_name_overrides_with_sanitized_module_name()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var module = model.Modules.First(m => string.Equals(m.Name.Value, "AppCore", StringComparison.OrdinalIgnoreCase));

        var renamedModuleName = ModuleName.Create("App Core");
        Assert.True(renamedModuleName.IsSuccess);

        var renamedEntities = module.Entities
            .Select(e => e with { Module = renamedModuleName.Value })
            .ToImmutableArray();
        var mutatedModule = module with { Name = renamedModuleName.Value, Entities = renamedEntities };
        var mutatedModel = model with { Modules = model.Modules.Replace(module, mutatedModule) };

        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var options = TighteningOptions.Default;
        var policy = new TighteningPolicy();
        var decisions = policy.Decide(mutatedModel, snapshot, options);
        var smoOptions = SmoBuildOptions.FromEmission(options.Emission);
        var smoModel = new SmoModelFactory().Create(
            mutatedModel,
            decisions,
            profile: snapshot,
            options: smoOptions);

        var overrideResult = NamingOverrideRule.Create(null, null, "App Core", "Customer", "CUSTOMER_EXTERNAL");
        Assert.True(overrideResult.IsSuccess);
        var namingOverrides = NamingOverrideOptions.Create(new[] { overrideResult.Value });
        Assert.True(namingOverrides.IsSuccess);

        var renamedScript = EdgeCaseScript.Replace("OSUSR_ABC_CUSTOMER", "CUSTOMER_EXTERNAL");

        var comparator = new DmmComparator();
        var comparison = comparator.Compare(
            ProjectSmo(smoModel, namingOverrides.Value),
            ParseScript(renamedScript));

        Assert.True(
            comparison.IsMatch,
            $"Model: {string.Join(" | ", comparison.ModelDifferences.Select(DescribeDifference))}; " +
            $"DMM: {string.Join(" | ", comparison.SsdtDifferences.Select(DescribeDifference))}");
    }

    [Fact]
    public void Parser_captures_primary_keys_added_via_alter_table()
    {
        const string script = @"CREATE TABLE [dbo].[OSUSR_ABC_CUSTOMER](
    [ID] BIGINT NOT NULL,
    [EMAIL] NVARCHAR(255) NOT NULL
);
ALTER TABLE [dbo].[OSUSR_ABC_CUSTOMER]
    ADD CONSTRAINT [PK_Customer] PRIMARY KEY ([ID]);";

        var tables = ParseScript(script);
        var table = Assert.Single(tables, t => string.Equals(t.Name, "OSUSR_ABC_CUSTOMER", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(new[] { "ID" }, table.PrimaryKeyColumns);
    }

    private static IReadOnlyList<DmmTable> ParseScript(string script)
    {
        var lens = new ScriptDomDmmLens();
        using var reader = new StringReader(script);
        var result = lens.Project(reader);
        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Errors.Select(e => $"{e.Code}:{e.Message}")));
        return result.Value;
    }

    private static IReadOnlyList<DmmTable> ProjectSmo(SmoModel model, NamingOverrideOptions overrides)
    {
        var options = SmoBuildOptions.Default.WithNamingOverrides(overrides);
        var result = new SmoDmmLens().Project(new SmoDmmLensRequest(model, options));
        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Errors.Select(e => $"{e.Code}:{e.Message}")));
        return result.Value;
    }

    private static string DescribeDifference(DmmDifference difference)
    {
        if (difference is null)
        {
            return string.Empty;
        }

        var target = difference.Column ?? difference.Index ?? difference.ForeignKey ?? difference.Table;
        return $"{difference.Property}:{target}";
    }

    private const string EdgeCaseScript = @"CREATE TABLE [dbo].[OSUSR_ABC_CUSTOMER](
    [ID] BIGINT NOT NULL,
    [EMAIL] NVARCHAR(255) COLLATE Latin1_General_CI_AI NOT NULL,
    [FIRSTNAME] NVARCHAR(100) NULL DEFAULT (''),
    [LASTNAME] NVARCHAR(100) NULL DEFAULT (''),
    [CITYID] BIGINT NOT NULL,
    CONSTRAINT [PK_Customer] PRIMARY KEY ([ID])
);
CREATE TABLE [dbo].[OSUSR_DEF_CITY](
    [ID] BIGINT NOT NULL,
    [NAME] NVARCHAR(200) NOT NULL,
    [ISACTIVE] BIT NOT NULL DEFAULT ((1)),
    CONSTRAINT [PK_City] PRIMARY KEY ([ID])
);
CREATE TABLE [billing].[BILLING_ACCOUNT](
    [ID] BIGINT NOT NULL,
    [ACCOUNTNUMBER] VARCHAR(50) NOT NULL,
    [EXTREF] VARCHAR(50) NULL,
    CONSTRAINT [PK_BillingAccount] PRIMARY KEY ([ID])
);
CREATE TABLE [dbo].[OSUSR_XYZ_JOBRUN](
    [ID] BIGINT NOT NULL,
    [TRIGGEREDBYUSERID] BIGINT NULL,
    [CREATEDON] DATETIME NOT NULL DEFAULT (getutcdate()),
    CONSTRAINT [PK_JobRun] PRIMARY KEY ([ID])
);";
}
