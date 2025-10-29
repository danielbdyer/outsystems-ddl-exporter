using System;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Validation.Tightening;
using Osm.Validation.Tightening.Opportunities;
using ITighteningAnalyzer = Osm.Validation.Tightening.ITighteningAnalyzer;
using Tests.Support;
using Xunit;

namespace Osm.Validation.Tests.Policy;

public sealed class UniqueIndexDecisionServiceTests
{
    [Fact]
    public void Analyze_adds_unique_opportunity_when_remediation_required()
    {
        var model = ModelFixtures.LoadModel("model.micro-unique.json");
        var snapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.MicroUnique);
        var options = TighteningPolicyTestHelper.CreateOptions(TighteningMode.Aggressive);
        var strippedSnapshot = RemoveEvidence(snapshot);

        var lookupContext = TighteningLookupContext.Create(model, strippedSnapshot, options);

        var analyzers = new ITighteningAnalyzer[]
        {
            new NullabilityEvaluator(
                options,
                lookupContext.ColumnProfiles,
                lookupContext.UniqueProfiles,
                lookupContext.ForeignKeyReality,
                lookupContext.ForeignKeyTargets,
                lookupContext.UniqueEvidence.SingleColumnClean,
                lookupContext.UniqueEvidence.SingleColumnDuplicates,
                lookupContext.UniqueEvidence.CompositeClean,
                lookupContext.UniqueEvidence.CompositeDuplicates),
            new ForeignKeyEvaluator(options.ForeignKeys, lookupContext.ForeignKeyReality, lookupContext.ForeignKeyTargets)
        };

        var columnService = new ColumnDecisionService(lookupContext, analyzers);
        var columnResult = columnService.Analyze();

        var strategy = new UniqueIndexDecisionStrategy(
            options,
            lookupContext.ColumnProfiles,
            lookupContext.UniqueProfiles,
            lookupContext.UniqueEvidence);

        var uniqueService = new UniqueIndexDecisionService(lookupContext, strategy);
        var uniqueResult = uniqueService.Analyze(columnResult.AnalysisBuilders);

        var emailColumn = columnResult.AnalysisBuilders.Keys.Single(c => string.Equals(c.Column.Value, "EMAIL", StringComparison.Ordinal));
        var emailBuilder = columnResult.AnalysisBuilders[emailColumn];

        var opportunity = Assert.Single(emailBuilder.Opportunities.Where(o => o.Type == OpportunityType.UniqueIndex));
        Assert.Equal("Remediate data before enforcing the unique index.", opportunity.Summary);

        var indexCoordinate = uniqueResult.UniqueDecisions.Keys.Single(k => string.Equals(k.Index.Value, "UX_USER_EMAIL", StringComparison.Ordinal));
        Assert.True(uniqueResult.UniqueDecisions[indexCoordinate].RequiresRemediation);

        var entity = model.Modules.SelectMany(static m => m.Entities).Single(e => string.Equals(e.LogicalName.Value, "User", StringComparison.Ordinal));
        Assert.Equal(entity.Module.Value, uniqueResult.IndexModules[indexCoordinate]);
    }

    private static ProfileSnapshot RemoveEvidence(ProfileSnapshot snapshot)
    {
        var updatedColumns = snapshot.Columns
            .Select(c => string.Equals(c.Column.Value, "EMAIL", StringComparison.Ordinal)
                ? c with { IsUniqueKey = false }
                : c)
            .ToImmutableArray();

        return snapshot with
        {
            Columns = updatedColumns,
            UniqueCandidates = ImmutableArray<UniqueCandidateProfile>.Empty,
            CompositeUniqueCandidates = ImmutableArray<CompositeUniqueCandidateProfile>.Empty
        };
    }
}
