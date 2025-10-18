using System;
using System.Collections.Immutable;
using System.Linq;
using FsCheck;
using FsCheck.Fluent;
using FluentArb = FsCheck.Fluent.Arb;
using FluentGen = FsCheck.Fluent.Gen;
using FsCheck.Xunit;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening;
using Tests.Support;
using Xunit;

namespace Osm.Validation.Tests.Policy;

public sealed class TighteningPolicyMonotonicityTests
{
    private static readonly OsmModel Model = ValidationModelFixture.CreateValidModel();
    private static readonly ProfileSnapshot BaseSnapshot = ProfileFixtures.LoadSnapshot(FixtureProfileSource.EdgeCase);
    private static readonly ImmutableArray<int> EvidenceBackedColumns = BuildEvidenceColumns();

    [Property(MaxTest = 100)]
    public Property NullabilityMonotonicWhenNullsDecrease()
    {
        if (EvidenceBackedColumns.IsDefaultOrEmpty)
        {
            return Prop.ToProperty(true);
        }

        var arbitrary = FluentArb.From(CreateNullCountScenarioGenerator(), _ => Enumerable.Empty<NullCountScenario>());

        return Prop.ForAll(arbitrary, scenario =>
        {
            var options = BuildOptions(scenario.Mode, scenario.NullBudget);
            var coordinate = ColumnCoordinate.From(BaseSnapshot.Columns[scenario.ColumnIndex]);

            var baselineDecisions = EvaluatePolicy(BaseSnapshot, options);
            var mutatedSnapshot = ApplyNullCount(BaseSnapshot, scenario.ColumnIndex, scenario.TargetNullCount);
            var mutatedDecisions = EvaluatePolicy(mutatedSnapshot, options);

            if (!baselineDecisions.Nullability.TryGetValue(coordinate, out var before) ||
                !mutatedDecisions.Nullability.TryGetValue(coordinate, out var after))
            {
                return true;
            }

            var preventsWeakerTighten = !(before.MakeNotNull && !after.MakeNotNull);
            var avoidsNewRemediation = !(before.MakeNotNull == after.MakeNotNull && !before.RequiresRemediation && after.RequiresRemediation);

            return preventsWeakerTighten && avoidsNewRemediation;
        });
    }

    [Property(MaxTest = 60)]
    public Property ForeignKeyCreationMonotonicWhenOrphansClear()
    {
        if (BaseSnapshot.ForeignKeys.IsDefaultOrEmpty)
        {
            return Prop.ToProperty(true);
        }

        var arbitrary = FluentArb.From(CreateForeignKeyScenarioGenerator(), _ => Enumerable.Empty<ForeignKeyScenario>());

        return Prop.ForAll(arbitrary, scenario =>
        {
            var options = BuildOptions(scenario.Mode, nullBudget: 0.0);
            var baseSnapshot = ApplyForeignKeyOrphanState(BaseSnapshot, scenario.ForeignKeyIndex, hasOrphan: true);
            var cleanSnapshot = ApplyForeignKeyOrphanState(BaseSnapshot, scenario.ForeignKeyIndex, hasOrphan: false);

            var baselineDecisions = EvaluatePolicy(baseSnapshot, options);
            var mutatedDecisions = EvaluatePolicy(cleanSnapshot, options);

            var reference = BaseSnapshot.ForeignKeys[scenario.ForeignKeyIndex].Reference;
            var coordinate = new ColumnCoordinate(reference.FromSchema, reference.FromTable, reference.FromColumn);

            if (!baselineDecisions.ForeignKeys.TryGetValue(coordinate, out var before) ||
                !mutatedDecisions.ForeignKeys.TryGetValue(coordinate, out var after))
            {
                return true;
            }

            var preventsRegression = !(before.CreateConstraint && !after.CreateConstraint);
            return preventsRegression;
        });
    }

    private static PolicyDecisionSet EvaluatePolicy(ProfileSnapshot snapshot, TighteningOptions options)
    {
        var policy = new TighteningPolicy();
        return policy.Decide(Model, snapshot, options);
    }

    private static ProfileSnapshot ApplyNullCount(ProfileSnapshot snapshot, int columnIndex, long newNullCount)
    {
        var original = snapshot.Columns[columnIndex];
        var updatedProfile = ColumnProfile.Create(
            original.Schema,
            original.Table,
            original.Column,
            original.IsNullablePhysical,
            original.IsComputed,
            original.IsPrimaryKey,
            original.IsUniqueKey,
            original.DefaultDefinition,
            original.RowCount,
            newNullCount,
            original.NullCountStatus).Value;

        var updatedColumns = snapshot.Columns.SetItem(columnIndex, updatedProfile);
        return snapshot with { Columns = updatedColumns };
    }

    private static ProfileSnapshot ApplyForeignKeyOrphanState(ProfileSnapshot snapshot, int foreignKeyIndex, bool hasOrphan)
    {
        var original = snapshot.ForeignKeys[foreignKeyIndex];
        var status = original.ProbeStatus.Outcome == ProfilingProbeOutcome.Succeeded
            ? original.ProbeStatus
            : ProfilingProbeStatus.CreateSucceeded(original.ProbeStatus.CapturedAtUtc, original.ProbeStatus.SampleSize);

        var updated = ForeignKeyReality.Create(original.Reference, hasOrphan, original.IsNoCheck, status).Value;
        var updatedForeignKeys = snapshot.ForeignKeys.SetItem(foreignKeyIndex, updated);
        return snapshot with { ForeignKeys = updatedForeignKeys };
    }

    private static TighteningOptions BuildOptions(TighteningMode mode, double nullBudget)
    {
        var policyOptions = PolicyOptions.Create(mode, nullBudget).Value;
        return TighteningOptions.Create(
            policyOptions,
            TighteningOptions.Default.ForeignKeys,
            TighteningOptions.Default.Uniqueness,
            TighteningOptions.Default.Remediation,
            TighteningOptions.Default.Emission,
            TighteningOptions.Default.Mocking).Value;
    }

    private static ImmutableArray<int> BuildEvidenceColumns()
    {
        if (BaseSnapshot.Columns.IsDefaultOrEmpty)
        {
            return ImmutableArray<int>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<int>(BaseSnapshot.Columns.Length);
        for (var i = 0; i < BaseSnapshot.Columns.Length; i++)
        {
            if (BaseSnapshot.Columns[i].NullCountStatus.Outcome == ProfilingProbeOutcome.Succeeded)
            {
                builder.Add(i);
            }
        }

        return builder.MoveToImmutable();
    }

    public sealed record NullCountScenario(int ColumnIndex, long TargetNullCount, TighteningMode Mode, double NullBudget);

    public sealed record ForeignKeyScenario(int ForeignKeyIndex, TighteningMode Mode);

    private static Gen<NullCountScenario> CreateNullCountScenarioGenerator()
    {
        return
            from index in FluentGen.Elements(EvidenceBackedColumns.ToArray())
            let column = BaseSnapshot.Columns[index]
            let maxReduction = (int)Math.Min(column.NullCount, 1_000L)
            from reduction in FluentGen.Choose(0, maxReduction)
            from forceZero in FluentGen.Elements(true, false)
            let targetNullCount = forceZero ? 0L : Math.Max(0L, column.NullCount - reduction)
            from budgetStep in FluentGen.Choose(0, 10)
            let nullBudget = budgetStep / 100.0
            from mode in FluentGen.Elements(Enum.GetValues<TighteningMode>())
            select new NullCountScenario(index, targetNullCount, mode, nullBudget);
    }

    private static Gen<ForeignKeyScenario> CreateForeignKeyScenarioGenerator()
    {
        return
            from index in FluentGen.Choose(0, BaseSnapshot.ForeignKeys.Length - 1)
            from mode in FluentGen.Elements(Enum.GetValues<TighteningMode>())
            select new ForeignKeyScenario(index, mode);
    }
}
