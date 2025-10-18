using System.Collections.Immutable;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening;
using Xunit;

namespace Osm.Validation.Tests.Policy;

public sealed class ChangeRiskClassifierTests
{
    private static readonly ColumnCoordinate Column = new(new SchemaName("dbo"), new TableName("T"), new ColumnName("C"));
    private static readonly IndexCoordinate Index = new(new SchemaName("dbo"), new TableName("T"), new IndexName("IX"));

    [Fact]
    public void ForNotNull_WhenProfileMissing_ReturnsHighRisk()
    {
        var decision = NullabilityDecision.Create(
            Column,
            makeNotNull: false,
            requiresRemediation: false,
            ImmutableArray.Create(TighteningRationales.ProfileMissing));

        var risk = ChangeRiskClassifier.ForNotNull(decision);

        Assert.Equal(RiskLevel.High, risk.Level);
    }

    [Fact]
    public void ForForeignKey_WhenOrphansDetected_ReturnsHighRisk()
    {
        var decision = ForeignKeyDecision.Create(Column, createConstraint: false, ImmutableArray<string>.Empty);

        var risk = ChangeRiskClassifier.ForForeignKey(
            decision,
            hasOrphan: true,
            ignoreRule: false,
            crossSchemaBlocked: false,
            crossCatalogBlocked: false);

        Assert.Equal(RiskLevel.High, risk.Level);
    }

    [Fact]
    public void ForUniqueIndex_WhenDuplicatesDetected_ReturnsHighRisk()
    {
        var decision = UniqueIndexDecision.Create(Index, enforceUnique: false, requiresRemediation: false, ImmutableArray<string>.Empty);
        var analysis = new UniqueIndexDecisionStrategy.UniqueIndexAnalysis(
            Index,
            decision,
            ImmutableArray.Create(TighteningRationales.UniqueDuplicatesPresent),
            true,
            false,
            false,
            true,
            false,
            ImmutableArray.Create(Column));

        var risk = ChangeRiskClassifier.ForUniqueIndex(analysis);

        Assert.Equal(RiskLevel.High, risk.Level);
    }
}
