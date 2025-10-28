using System.Collections.Generic;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening;
using Xunit;

namespace Osm.Validation.Tests.Policy;

public sealed class NullabilityEvaluatorTests
{
    [Fact]
    public void EvidenceGated_Should_Tighten_MandatoryColumn_When_NullBudgetNotExceeded()
    {
        var options = TighteningPolicyTestHelper.CreateOptions(TighteningMode.EvidenceGated, nullBudget: 0.05);

        var coordinate = new ColumnCoordinate(new SchemaName("dbo"), new TableName("OSUSR_TEST_SAMPLE"), new ColumnName("MANDATORY"));
        var mandatoryAttribute = TighteningEvaluatorTestHelper.CreateAttribute("Mandatory", "MANDATORY", isMandatory: true);
        var identifier = TighteningEvaluatorTestHelper.CreateAttribute("Id", "ID", isIdentifier: true);
        var entity = TighteningEvaluatorTestHelper.CreateEntity("Sample", "SampleEntity", "OSUSR_TEST_SAMPLE", new[] { identifier, mandatoryAttribute });
        var module = TighteningEvaluatorTestHelper.CreateModule("Sample", entity);
        var model = TighteningEvaluatorTestHelper.CreateModel(module);

        var foreignKeyTargets = TighteningEvaluatorTestHelper.CreateForeignKeyTargetIndex(
            model,
            new Dictionary<EntityName, EntityModel> { [entity.LogicalName] = entity });

        var columnProfile = TighteningEvaluatorTestHelper.CreateColumnProfile(coordinate, isNullablePhysical: true, rowCount: 100, nullCount: 4);

        var evaluator = new NullabilityEvaluator(
            options,
            new Dictionary<ColumnCoordinate, ColumnProfile> { [coordinate] = columnProfile },
            new Dictionary<ColumnCoordinate, UniqueCandidateProfile>(),
            new Dictionary<ColumnCoordinate, ForeignKeyReality>(),
            foreignKeyTargets,
            new HashSet<ColumnCoordinate>(),
            new HashSet<ColumnCoordinate>(),
            new HashSet<ColumnCoordinate>(),
            new HashSet<ColumnCoordinate>());

        var decision = evaluator.Evaluate(entity, mandatoryAttribute, coordinate);

        Assert.True(decision.MakeNotNull);
        Assert.False(decision.RequiresRemediation);
        Assert.Contains(TighteningRationales.Mandatory, decision.Rationales);
        Assert.Contains(TighteningRationales.DataNoNulls, decision.Rationales);
        Assert.Contains(TighteningRationales.NullBudgetEpsilon, decision.Rationales);
    }

    [Fact]
    public void EvidenceGated_Should_StayNullable_When_MandatoryColumn_Has_Nulls()
    {
        var options = TighteningPolicyTestHelper.CreateOptions(TighteningMode.EvidenceGated, nullBudget: 0.05);

        var coordinate = new ColumnCoordinate(new SchemaName("dbo"), new TableName("OSUSR_TEST_SAMPLE"), new ColumnName("MANDATORY"));
        var mandatoryAttribute = TighteningEvaluatorTestHelper.CreateAttribute("Mandatory", "MANDATORY", isMandatory: true);
        var identifier = TighteningEvaluatorTestHelper.CreateAttribute("Id", "ID", isIdentifier: true);
        var entity = TighteningEvaluatorTestHelper.CreateEntity("Sample", "SampleEntity", "OSUSR_TEST_SAMPLE", new[] { identifier, mandatoryAttribute });
        var module = TighteningEvaluatorTestHelper.CreateModule("Sample", entity);
        var model = TighteningEvaluatorTestHelper.CreateModel(module);

        var foreignKeyTargets = TighteningEvaluatorTestHelper.CreateForeignKeyTargetIndex(
            model,
            new Dictionary<EntityName, EntityModel> { [entity.LogicalName] = entity });

        var columnProfile = TighteningEvaluatorTestHelper.CreateColumnProfile(coordinate, isNullablePhysical: true, rowCount: 100, nullCount: 12);

        var evaluator = new NullabilityEvaluator(
            options,
            new Dictionary<ColumnCoordinate, ColumnProfile> { [coordinate] = columnProfile },
            new Dictionary<ColumnCoordinate, UniqueCandidateProfile>(),
            new Dictionary<ColumnCoordinate, ForeignKeyReality>(),
            foreignKeyTargets,
            new HashSet<ColumnCoordinate>(),
            new HashSet<ColumnCoordinate>(),
            new HashSet<ColumnCoordinate>(),
            new HashSet<ColumnCoordinate>());

        var decision = evaluator.Evaluate(entity, mandatoryAttribute, coordinate);

        Assert.False(decision.MakeNotNull);
        Assert.False(decision.RequiresRemediation);
        Assert.Contains(TighteningRationales.Mandatory, decision.Rationales);
        Assert.Contains(TighteningRationales.DataHasNulls, decision.Rationales);
        Assert.DoesNotContain(TighteningRationales.DataNoNulls, decision.Rationales);
    }

    [Fact]
    public void Aggressive_Should_Flag_Remediation_When_UniqueSignal_Exceeds_NullBudget()
    {
        var options = TighteningPolicyTestHelper.CreateOptions(TighteningMode.Aggressive, nullBudget: 0.05);

        var coordinate = new ColumnCoordinate(new SchemaName("dbo"), new TableName("OSUSR_TEST_SAMPLE"), new ColumnName("UNIQUE_COL"));
        var uniqueAttribute = TighteningEvaluatorTestHelper.CreateAttribute("Unique", "UNIQUE_COL");
        var identifier = TighteningEvaluatorTestHelper.CreateAttribute("Id", "ID", isIdentifier: true);
        var entity = TighteningEvaluatorTestHelper.CreateEntity("Sample", "SampleEntity", "OSUSR_TEST_SAMPLE", new[] { identifier, uniqueAttribute });
        var module = TighteningEvaluatorTestHelper.CreateModule("Sample", entity);
        var model = TighteningEvaluatorTestHelper.CreateModel(module);

        var foreignKeyTargets = TighteningEvaluatorTestHelper.CreateForeignKeyTargetIndex(
            model,
            new Dictionary<EntityName, EntityModel> { [entity.LogicalName] = entity });

        var columnProfile = TighteningEvaluatorTestHelper.CreateColumnProfile(coordinate, isNullablePhysical: true, rowCount: 100, nullCount: 20);

        var evaluator = new NullabilityEvaluator(
            options,
            new Dictionary<ColumnCoordinate, ColumnProfile> { [coordinate] = columnProfile },
            new Dictionary<ColumnCoordinate, UniqueCandidateProfile>(),
            new Dictionary<ColumnCoordinate, ForeignKeyReality>(),
            foreignKeyTargets,
            new HashSet<ColumnCoordinate> { coordinate },
            new HashSet<ColumnCoordinate>(),
            new HashSet<ColumnCoordinate>(),
            new HashSet<ColumnCoordinate>());

        var decision = evaluator.Evaluate(entity, uniqueAttribute, coordinate);

        Assert.True(decision.MakeNotNull);
        Assert.True(decision.RequiresRemediation);
        Assert.Contains(TighteningRationales.UniqueNoNulls, decision.Rationales);
        Assert.Contains(TighteningRationales.RemediateBeforeTighten, decision.Rationales);
    }
}
