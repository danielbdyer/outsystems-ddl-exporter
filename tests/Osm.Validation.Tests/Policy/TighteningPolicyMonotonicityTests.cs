using System;
using System.Collections.Immutable;
using System.Linq;
using FsCheck;
using FsCheck.Xunit;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Validation.Tightening;

namespace Osm.Validation.Tests.Policy;

public class TighteningPolicyMonotonicityTests
{
    private static readonly Lazy<OsmModel> Model = new(BuildModel);
    private static readonly SchemaName DefaultSchema = SchemaName.Create("dbo").Value;
    private static readonly TableName ParentTable = TableName.Create("OSUSR_PARENT").Value;
    private static readonly TableName ChildTable = TableName.Create("OSUSR_CHILD").Value;
    private static readonly ColumnName ParentIdentifierColumn = ColumnName.Create("ID").Value;
    private static readonly ColumnName ParentIdColumn = ColumnName.Create("PARENTID").Value;
    private static readonly ColumnName RequiredNameColumn = ColumnName.Create("REQUIREDNAME").Value;
    private static readonly ColumnCoordinate RequiredNameCoordinate = new(DefaultSchema, ChildTable, RequiredNameColumn);
    private static readonly ColumnCoordinate ParentIdCoordinate = new(DefaultSchema, ChildTable, ParentIdColumn);

    [Property]
    public bool Nullability_decisions_strengthen_when_nulls_drop(
        PositiveInt rowCountSeed,
        NonNegativeInt initialNullSeed,
        NonNegativeInt improvementSeed,
        NonNegativeInt budgetSeed)
    {
        var rowCount = (long)rowCountSeed.Get;
        var initialNulls = Math.Min(rowCount, initialNullSeed.Get % (rowCount + 1));
        var improvement = improvementSeed.Get % (initialNulls + 1);
        var improvedNulls = initialNulls - improvement;
        var nullBudget = Math.Min(1.0, (budgetSeed.Get % 101) / 100.0);

        foreach (var mode in new[] { TighteningMode.EvidenceGated, TighteningMode.Aggressive })
        {
            var before = EvaluateNullDecision(rowCount, initialNulls, nullBudget, mode);
            var after = EvaluateNullDecision(rowCount, improvedNulls, nullBudget, mode);

            if (before.MakeNotNull && !after.MakeNotNull)
            {
                return false;
            }

            if (!before.RequiresRemediation && after.RequiresRemediation)
            {
                return false;
            }
        }

        return true;
    }

    [Property]
    public bool Foreign_key_decisions_strengthen_when_orphans_clear(bool hasConstraint, bool beforeHasOrphan, bool clearOrphan)
    {
        var afterHasOrphan = beforeHasOrphan && !clearOrphan;
        var options = CreateOptions(TighteningMode.EvidenceGated, TighteningOptions.Default.Policy.NullBudget);
        var before = EvaluateForeignKeyDecision(beforeHasOrphan, hasConstraint, options);
        var after = EvaluateForeignKeyDecision(afterHasOrphan, hasConstraint, options);

        return !before.CreateConstraint || after.CreateConstraint;
    }

    private static NullabilityDecision EvaluateNullDecision(long rowCount, long nullCount, double nullBudget, TighteningMode mode)
    {
        var snapshot = BuildNullSnapshot(rowCount, nullCount);
        var options = CreateOptions(mode, nullBudget);
        var policy = new TighteningPolicy();
        var decisions = policy.Decide(Model.Value, snapshot, options);
        return decisions.Nullability[RequiredNameCoordinate];
    }

    private static ForeignKeyDecision EvaluateForeignKeyDecision(bool hasOrphan, bool hasDatabaseConstraint, TighteningOptions options)
    {
        var snapshot = BuildForeignKeySnapshot(hasOrphan, hasDatabaseConstraint);
        var policy = new TighteningPolicy();
        var decisions = policy.Decide(Model.Value, snapshot, options);
        return decisions.ForeignKeys.TryGetValue(ParentIdCoordinate, out var decision)
            ? decision
            : ForeignKeyDecision.Create(ParentIdCoordinate, createConstraint: false, ImmutableArray<string>.Empty);
    }

    private static ProfileSnapshot BuildNullSnapshot(long rowCount, long nullCount)
    {
        var status = ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UtcNow, rowCount);
        var profile = ColumnProfile.Create(
            DefaultSchema,
            ChildTable,
            RequiredNameColumn,
            isNullablePhysical: true,
            isComputed: false,
            isPrimaryKey: false,
            isUniqueKey: false,
            defaultDefinition: null,
            rowCount,
            nullCount,
            status).Value;

        return ProfileSnapshot.Create(
            new[] { profile },
            Array.Empty<UniqueCandidateProfile>(),
            Array.Empty<CompositeUniqueCandidateProfile>(),
            Array.Empty<ForeignKeyReality>()).Value;
    }

    private static ProfileSnapshot BuildForeignKeySnapshot(bool hasOrphan, bool hasDatabaseConstraint)
    {
        var status = ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UtcNow, sampleSize: 0);
        var reference = ForeignKeyReference.Create(
            DefaultSchema,
            ChildTable,
            ParentIdColumn,
            DefaultSchema,
            ParentTable,
            ParentIdentifierColumn,
            hasDatabaseConstraint).Value;
        var reality = ForeignKeyReality.Create(reference, hasOrphan, isNoCheck: false, status).Value;

        var columnStatus = ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UtcNow, sampleSize: 0);
        var columnProfile = ColumnProfile.Create(
            DefaultSchema,
            ChildTable,
            ParentIdColumn,
            isNullablePhysical: true,
            isComputed: false,
            isPrimaryKey: false,
            isUniqueKey: false,
            defaultDefinition: null,
            rowCount: 10,
            nullCount: 0,
            columnStatus).Value;

        return ProfileSnapshot.Create(
            new[] { columnProfile },
            Array.Empty<UniqueCandidateProfile>(),
            Array.Empty<CompositeUniqueCandidateProfile>(),
            new[] { reality }).Value;
    }

    private static TighteningOptions CreateOptions(TighteningMode mode, double nullBudget)
    {
        var defaults = TighteningOptions.Default;
        var policy = PolicyOptions.Create(mode, nullBudget).Value;
        return TighteningOptions.Create(policy, defaults.ForeignKeys, defaults.Uniqueness, defaults.Remediation, defaults.Emission, defaults.Mocking).Value;
    }

    private static OsmModel BuildModel()
    {
        var moduleName = ModuleName.Create("PolicyModule").Value;
        var parentEntityName = EntityName.Create("Parent").Value;
        var childEntityName = EntityName.Create("Child").Value;

        var parentId = AttributeModel.Create(
            AttributeName.Create("Id").Value,
            ParentIdentifierColumn,
            dataType: "Identifier",
            isMandatory: true,
            isIdentifier: true,
            isAutoNumber: true,
            isActive: true).Value;

        var parentEntity = EntityModel.Create(
            moduleName,
            parentEntityName,
            ParentTable,
            DefaultSchema,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            attributes: new[] { parentId }).Value;

        var requiredName = AttributeModel.Create(
            AttributeName.Create("RequiredName").Value,
            RequiredNameColumn,
            dataType: "Text",
            isMandatory: true,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true).Value;

        var parentReference = AttributeReference.Create(
            isReference: true,
            targetEntityId: 1,
            targetEntity: parentEntityName,
            targetPhysicalName: ParentTable,
            deleteRuleCode: "Protect",
            hasDatabaseConstraint: false).Value;

        var parentIdAttribute = AttributeModel.Create(
            AttributeName.Create("ParentId").Value,
            ParentIdColumn,
            dataType: "Identifier",
            isMandatory: false,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            reference: parentReference).Value;

        var childId = AttributeModel.Create(
            AttributeName.Create("Id").Value,
            ColumnName.Create("ID").Value,
            dataType: "Identifier",
            isMandatory: true,
            isIdentifier: true,
            isAutoNumber: true,
            isActive: true).Value;

        var childEntity = EntityModel.Create(
            moduleName,
            childEntityName,
            ChildTable,
            DefaultSchema,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            attributes: new[] { childId, parentIdAttribute, requiredName }).Value;

        var module = ModuleModel.Create(moduleName, isSystemModule: false, isActive: true, new[] { parentEntity, childEntity }).Value;
        return OsmModel.Create(DateTime.UtcNow, new[] { module }).Value;
    }

}
