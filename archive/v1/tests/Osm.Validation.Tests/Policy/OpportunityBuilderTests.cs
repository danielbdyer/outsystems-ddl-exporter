using System.Collections.Immutable;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening;
using Osm.Validation.Tightening.Opportunities;

namespace Osm.Validation.Tests.Policy;

public sealed class OpportunityBuilderTests
{
    private static readonly SchemaName Schema = SchemaName.Create("dbo").Value;
    private static readonly TableName Table = TableName.Create("OSUSR_TEST").Value;
    private static readonly IndexName Index = IndexName.Create("IX_TEST").Value;
    private static readonly ColumnName Column = ColumnName.Create("TEST_COLUMN").Value;

    [Fact]
    public void TryCreate_ReturnsNullWhenEnforcementSafe()
    {
        var analysis = CreateAnalysis(
            enforceUnique: true,
            requiresRemediation: false,
            hasDuplicates: false,
            policyDisabled: false,
            hasEvidence: true,
            dataClean: true);

        var builder = new OpportunityBuilder();
        var result = builder.TryCreate(analysis, CreateColumn());

        Assert.Null(result);
    }

    [Theory]
    [InlineData(true, true, false, false, true, false, "Unique index was not enforced. Remediate data before enforcement can proceed.", RiskLevel.Moderate)]
    [InlineData(false, false, true, false, true, false, "Unique index was not enforced. Resolve duplicate values before enforcement can proceed.", RiskLevel.High)]
    [InlineData(false, false, false, true, true, false, "Unique index was not enforced. Enable policy support before enforcement can proceed.", RiskLevel.Moderate)]
    [InlineData(false, false, false, false, false, false, "Unique index was not enforced. Collect profiling evidence before enforcement can proceed.", RiskLevel.Moderate)]
    [InlineData(false, false, false, false, true, false, "Unique index was not enforced. Review policy requirements before enforcement can proceed.", RiskLevel.Moderate)]
    public void TryCreate_BuildsOpportunityWithExpectedSummary(
        bool enforceUnique,
        bool requiresRemediation,
        bool hasDuplicates,
        bool policyDisabled,
        bool hasEvidence,
        bool physicalReality,
        string expectedSummary,
        RiskLevel expectedRisk)
    {
        var analysis = CreateAnalysis(
            enforceUnique,
            requiresRemediation,
            hasDuplicates,
            policyDisabled,
            hasEvidence,
            dataClean: hasEvidence && !hasDuplicates,
            physicalReality: physicalReality);

        var builder = new OpportunityBuilder();
        var opportunity = builder.TryCreate(analysis, CreateColumn());

        Assert.NotNull(opportunity);
        Assert.Equal(expectedSummary, opportunity!.Summary);
        Assert.Equal(expectedRisk, opportunity.Risk.Level);
        Assert.Equal(OpportunityDisposition.NeedsRemediation, opportunity.Disposition);
        Assert.Equal(analysis.Index, opportunity.Index);
    }

    private static ColumnCoordinate CreateColumn()
        => new(Schema, Table, Column);

    private static UniqueIndexDecisionStrategy.UniqueIndexAnalysis CreateAnalysis(
        bool enforceUnique,
        bool requiresRemediation,
        bool hasDuplicates,
        bool policyDisabled,
        bool hasEvidence,
        bool dataClean,
        bool physicalReality = false)
    {
        var indexCoordinate = new IndexCoordinate(Schema, Table, Index);
        var decision = UniqueIndexDecision.Create(indexCoordinate, enforceUnique, requiresRemediation, ImmutableArray<string>.Empty);
        return new UniqueIndexDecisionStrategy.UniqueIndexAnalysis(
            indexCoordinate,
            decision,
            ImmutableArray<string>.Empty,
            hasDuplicates,
            physicalReality,
            policyDisabled,
            hasEvidence,
            dataClean,
            ImmutableArray.Create(CreateColumn()));
    }
}

