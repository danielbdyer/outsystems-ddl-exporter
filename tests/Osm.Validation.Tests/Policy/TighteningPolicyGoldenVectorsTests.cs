using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening;
using Tests.Support;
using Xunit;

namespace Osm.Validation.Tests.Policy;

public sealed class TighteningPolicyGoldenVectorsTests
{
    private static readonly OsmModel Model = ModelFixtures.LoadModel("policy/kernel-model.json");
    private static readonly ProfileSnapshot Snapshot = ProfileFixtures.LoadSnapshot("policy/kernel-profile.json");

    private static readonly SchemaName Schema = SchemaName.Create("dbo").Value;
    private static readonly TableName CustomerTable = TableName.Create("OSUSR_POLICY_CUSTOMER").Value;
    private static readonly TableName OrderTable = TableName.Create("OSUSR_POLICY_ORDER").Value;

    [Fact]
    public void EvaluateCautiousMatchesGoldenExpectations()
    {
        var cautiousResult = TighteningPolicy.Evaluate(Model, Snapshot, TighteningMode.Cautious);
        Assert.Equal(PolicyResultKind.Decision, cautiousResult.Kind);
        var decisions = cautiousResult.Decision;

        var customerId = decisions.Nullability[CustomerColumn("ID")];
        Assert.True(customerId.MakeNotNull);
        Assert.False(customerId.RequiresRemediation);

        Assert.False(decisions.Nullability[CustomerColumn("EMAIL")].MakeNotNull);
        Assert.False(decisions.Nullability[CustomerColumn("EXTERNALID")].MakeNotNull);
        Assert.False(decisions.Nullability[OrderColumn("CUSTOMERID")].MakeNotNull);

        Assert.True(decisions.ForeignKeys[OrderColumn("CUSTOMERID")].CreateConstraint);

        Assert.False(decisions.UniqueIndexes[CustomerIndex("UX_CUSTOMER_EMAIL")].EnforceUnique);
        Assert.False(decisions.UniqueIndexes[CustomerIndex("UX_CUSTOMER_EXTERNALID")].EnforceUnique);
    }

    [Fact]
    public void EvaluateEvidenceGatedTightensOnCleanSignals()
    {
        var gatedResult = TighteningPolicy.Evaluate(Model, Snapshot, TighteningMode.EvidenceGated);
        Assert.Equal(PolicyResultKind.Decision, gatedResult.Kind);
        var decisions = gatedResult.Decision;

        var customerId = decisions.Nullability[CustomerColumn("ID")];
        Assert.True(customerId.MakeNotNull);
        Assert.False(customerId.RequiresRemediation);

        var email = decisions.Nullability[CustomerColumn("EMAIL")];
        Assert.True(email.MakeNotNull);
        Assert.False(email.RequiresRemediation);

        Assert.False(decisions.Nullability[CustomerColumn("EXTERNALID")].MakeNotNull);
        Assert.False(decisions.Nullability[OrderColumn("CUSTOMERID")].MakeNotNull);

        Assert.True(decisions.ForeignKeys[OrderColumn("CUSTOMERID")].CreateConstraint);

        var emailUnique = decisions.UniqueIndexes[CustomerIndex("UX_CUSTOMER_EMAIL")];
        Assert.True(emailUnique.EnforceUnique);
        Assert.False(emailUnique.RequiresRemediation);

        Assert.False(decisions.UniqueIndexes[CustomerIndex("UX_CUSTOMER_EXTERNALID")].EnforceUnique);
    }

    [Fact]
    public void EvaluateAggressiveRequiresRemediationWhenDataIsDirty()
    {
        var aggressiveResult = TighteningPolicy.Evaluate(Model, Snapshot, TighteningMode.Aggressive);
        Assert.Equal(PolicyResultKind.Decision, aggressiveResult.Kind);
        var decisions = aggressiveResult.Decision;

        var customerId = decisions.Nullability[CustomerColumn("ID")];
        Assert.True(customerId.MakeNotNull);
        Assert.False(customerId.RequiresRemediation);

        var email = decisions.Nullability[CustomerColumn("EMAIL")];
        Assert.True(email.MakeNotNull);
        Assert.False(email.RequiresRemediation);

        var externalId = decisions.Nullability[CustomerColumn("EXTERNALID")];
        Assert.True(externalId.MakeNotNull);
        Assert.True(externalId.RequiresRemediation);

        var customerIdFk = decisions.Nullability[OrderColumn("CUSTOMERID")];
        Assert.True(customerIdFk.MakeNotNull);
        Assert.True(customerIdFk.RequiresRemediation);

        Assert.True(decisions.ForeignKeys[OrderColumn("CUSTOMERID")].CreateConstraint);

        var emailUnique = decisions.UniqueIndexes[CustomerIndex("UX_CUSTOMER_EMAIL")];
        Assert.True(emailUnique.EnforceUnique);
        Assert.False(emailUnique.RequiresRemediation);

        var externalUnique = decisions.UniqueIndexes[CustomerIndex("UX_CUSTOMER_EXTERNALID")];
        Assert.True(externalUnique.EnforceUnique);
        Assert.True(externalUnique.RequiresRemediation);
    }

    private static ColumnCoordinate CustomerColumn(string column)
        => new(Schema, CustomerTable, ColumnName.Create(column).Value);

    private static ColumnCoordinate OrderColumn(string column)
        => new(Schema, OrderTable, ColumnName.Create(column).Value);

    private static IndexCoordinate CustomerIndex(string index)
        => new(Schema, CustomerTable, IndexName.Create(index).Value);
}
