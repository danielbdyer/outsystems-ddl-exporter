using System;
using System.Linq;
using Microsoft.SqlServer.Management.Smo;
using Osm.Domain.Configuration;
using Osm.Smo;
using Osm.Validation.Tightening;
using Tests.Support;

namespace Osm.Smo.Tests;

public class SmoModelFactoryTests
{
    private static (Osm.Domain.Model.OsmModel model, PolicyDecisionSet decisions) LoadEdgeCaseDecisions()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
        var options = TighteningOptions.Default;
        var policy = new TighteningPolicy();
        var decisions = policy.Decide(model, snapshot, options);
        return (model, decisions);
    }

    [Fact]
    public void Build_creates_tables_with_policy_driven_nullability_and_foreign_keys()
    {
        var (model, decisions) = LoadEdgeCaseDecisions();
        var factory = new SmoModelFactory();
        var smoModel = factory.Create(model, decisions, SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission));

        var customerTable = smoModel.Tables.Single(t => t.Name.Equals("OSUSR_ABC_CUSTOMER", StringComparison.OrdinalIgnoreCase));
        var emailColumn = customerTable.Columns.Single(c => c.Name.Equals("EMAIL", StringComparison.OrdinalIgnoreCase));
        var cityColumn = customerTable.Columns.Single(c => c.Name.Equals("CITYID", StringComparison.OrdinalIgnoreCase));
        Assert.False(emailColumn.Nullable);
        Assert.False(cityColumn.Nullable);
        Assert.Equal(SqlDataType.NVarChar, emailColumn.DataType.SqlDataType);
        Assert.Equal(255, emailColumn.DataType.MaximumLength);

        var pk = customerTable.Indexes.Single(i => i.IsPrimaryKey);
        Assert.Collection(pk.Columns.OrderBy(c => c.Ordinal),
            col => Assert.Equal("ID", col.Name));

        var emailIndex = customerTable.Indexes.Single(i => i.Name.Equals("IDX_CUSTOMER_EMAIL", StringComparison.OrdinalIgnoreCase));
        Assert.True(emailIndex.IsUnique);

        var cityForeignKey = customerTable.ForeignKeys.Single();
        Assert.Equal("OSUSR_DEF_CITY", cityForeignKey.ReferencedTable);
        Assert.Equal("dbo", cityForeignKey.ReferencedSchema);
        Assert.Equal(ForeignKeyAction.NoAction, cityForeignKey.DeleteAction);

        var jobRunTable = smoModel.Tables.Single(t => t.Name.Equals("OSUSR_XYZ_JOBRUN", StringComparison.OrdinalIgnoreCase));
        var jobRunTriggeredByColumn = jobRunTable.Columns.Single(c => c.Name.Equals("TRIGGEREDBYUSERID", StringComparison.OrdinalIgnoreCase));
        Assert.True(jobRunTriggeredByColumn.Nullable);
        Assert.Empty(jobRunTable.ForeignKeys);

        var billingTable = smoModel.Tables.Single(t => t.Name.Equals("BILLING_ACCOUNT", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("billing", billingTable.Schema);
        var accountNumberColumn = billingTable.Columns.Single(c => c.Name.Equals("ACCOUNTNUMBER", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(SqlDataType.VarChar, accountNumberColumn.DataType.SqlDataType);
        Assert.Equal(50, accountNumberColumn.DataType.MaximumLength);
    }

    [Fact]
    public void Build_respects_platform_auto_index_toggle()
    {
        var (model, decisions) = LoadEdgeCaseDecisions();
        var factory = new SmoModelFactory();
        var options = new SmoBuildOptions("OutSystems", IncludePlatformAutoIndexes: true, EmitConcatenatedConstraints: false, SanitizeModuleNames: true);
        var smoModel = factory.Create(model, decisions, options);

        var jobRunTable = smoModel.Tables.Single(t => t.Name.Equals("OSUSR_XYZ_JOBRUN", StringComparison.OrdinalIgnoreCase));
        var hasPlatformIndex = jobRunTable.Indexes.Any(i => i.Name.Equals("OSIDX_JOBRUN_CREATEDON", StringComparison.OrdinalIgnoreCase));
        Assert.True(hasPlatformIndex);
    }
}
