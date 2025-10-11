using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Osm.Emission;
using Osm.Smo;
using Osm.Validation.Tightening;

namespace Osm.Pipeline.Orchestration;

public sealed class EmissionFingerprintCalculator
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public SsdtEmissionMetadata Compute(SmoModel model, PolicyDecisionSet decisions, SmoBuildOptions options)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (decisions is null)
        {
            throw new ArgumentNullException(nameof(decisions));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var document = new EmissionFingerprintDocument(
            new EmissionFingerprintOptions(
                options.IncludePlatformAutoIndexes,
                options.EmitBareTableOnly,
                options.SanitizeModuleNames,
                options.ModuleParallelism,
                options.DefaultCatalogName),
            BuildTables(model),
            BuildDecisions(decisions));

        var payload = JsonSerializer.Serialize(document, SerializerOptions);
        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        return new SsdtEmissionMetadata("SHA256", hash);
    }

    private static IReadOnlyList<TableDocument> BuildTables(SmoModel model)
    {
        if (model.Tables.IsDefaultOrEmpty)
        {
            return Array.Empty<TableDocument>();
        }

        return model.Tables
            .OrderBy(table => table.Module, StringComparer.Ordinal)
            .ThenBy(table => table.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(table => table.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(table => table.LogicalName, StringComparer.Ordinal)
            .Select((table, index) => new TableDocument(
                table.Module,
                table.OriginalModule,
                table.Name,
                table.Schema,
                table.Catalog,
                table.LogicalName,
                table.Description,
                BuildColumns(table.Columns),
                BuildIndexes(table.Indexes),
                BuildForeignKeys(table.ForeignKeys),
                BuildTriggers(table.Triggers)))
            .ToList();
    }

    private static IReadOnlyList<ColumnDocument> BuildColumns(ImmutableArray<SmoColumnDefinition> columns)
    {
        if (columns.IsDefaultOrEmpty)
        {
            return Array.Empty<ColumnDocument>();
        }

        return columns
            .Select((column, ordinal) => new ColumnDocument(
                column.Name,
                column.LogicalName,
                BuildDataType(column.DataType),
                column.Nullable,
                column.IsIdentity,
                column.IdentitySeed,
                column.IdentityIncrement,
                column.IsComputed,
                column.ComputedExpression,
                column.DefaultExpression,
                column.Collation,
                column.Description,
                BuildDefaultConstraint(column.DefaultConstraint),
                BuildCheckConstraints(column.CheckConstraints),
                ordinal))
            .OrderBy(column => column.Ordinal)
            .ToList();
    }

    private static DataTypeDocument BuildDataType(Microsoft.SqlServer.Management.Smo.DataType dataType)
    {
        if (dataType is null)
        {
            return new DataTypeDocument(string.Empty, string.Empty, 0, 0, 0);
        }

        return new DataTypeDocument(
            dataType.Name,
            dataType.SqlDataType.ToString(),
            dataType.MaximumLength,
            dataType.NumericPrecision,
            dataType.NumericScale);
    }

    private static DefaultConstraintDocument? BuildDefaultConstraint(SmoDefaultConstraintDefinition? constraint)
    {
        if (constraint is null)
        {
            return null;
        }

        return new DefaultConstraintDocument(constraint.Name, constraint.Expression, constraint.IsNotTrusted);
    }

    private static IReadOnlyList<CheckConstraintDocument> BuildCheckConstraints(ImmutableArray<SmoCheckConstraintDefinition> constraints)
    {
        if (constraints.IsDefaultOrEmpty)
        {
            return Array.Empty<CheckConstraintDocument>();
        }

        return constraints
            .OrderBy(constraint => constraint.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(constraint => constraint.Expression, StringComparer.OrdinalIgnoreCase)
            .Select(constraint => new CheckConstraintDocument(constraint.Name, constraint.Expression, constraint.IsNotTrusted))
            .ToList();
    }

    private static IReadOnlyList<IndexDocument> BuildIndexes(ImmutableArray<SmoIndexDefinition> indexes)
    {
        if (indexes.IsDefaultOrEmpty)
        {
            return Array.Empty<IndexDocument>();
        }

        return indexes
            .OrderByDescending(index => index.IsPrimaryKey)
            .ThenByDescending(index => index.IsUnique)
            .ThenBy(index => index.Name, StringComparer.OrdinalIgnoreCase)
            .Select(index => new IndexDocument(
                index.Name,
                index.IsUnique,
                index.IsPrimaryKey,
                index.IsPlatformAuto,
                BuildIndexColumns(index.Columns),
                BuildIndexMetadata(index.Metadata)))
            .ToList();
    }

    private static IReadOnlyList<IndexColumnDocument> BuildIndexColumns(ImmutableArray<SmoIndexColumnDefinition> columns)
    {
        if (columns.IsDefaultOrEmpty)
        {
            return Array.Empty<IndexColumnDocument>();
        }

        return columns
            .OrderBy(column => column.Ordinal)
            .ThenBy(column => column.Name, StringComparer.OrdinalIgnoreCase)
            .Select(column => new IndexColumnDocument(column.Name, column.Ordinal, column.IsIncluded, column.IsDescending))
            .ToList();
    }

    private static IndexMetadataDocument BuildIndexMetadata(SmoIndexMetadata metadata)
    {
        return new IndexMetadataDocument(
            metadata.IsDisabled,
            metadata.IsPadded,
            metadata.FillFactor,
            metadata.IgnoreDuplicateKey,
            metadata.AllowRowLocks,
            metadata.AllowPageLocks,
            metadata.StatisticsNoRecompute,
            metadata.FilterDefinition,
            metadata.DataSpace is null ? null : new IndexDataSpaceDocument(metadata.DataSpace.Name, metadata.DataSpace.Type),
            metadata.PartitionColumns.IsDefaultOrEmpty
                ? Array.Empty<IndexPartitionColumnDocument>()
                : metadata.PartitionColumns
                    .OrderBy(column => column.Ordinal)
                    .Select(column => new IndexPartitionColumnDocument(column.Name, column.Ordinal))
                    .ToList(),
            metadata.DataCompression.IsDefaultOrEmpty
                ? Array.Empty<IndexCompressionDocument>()
                : metadata.DataCompression
                    .OrderBy(setting => setting.PartitionNumber)
                    .Select(setting => new IndexCompressionDocument(setting.PartitionNumber, setting.Compression))
                    .ToList());
    }

    private static IReadOnlyList<ForeignKeyDocument> BuildForeignKeys(ImmutableArray<SmoForeignKeyDefinition> foreignKeys)
    {
        if (foreignKeys.IsDefaultOrEmpty)
        {
            return Array.Empty<ForeignKeyDocument>();
        }

        return foreignKeys
            .OrderBy(fk => fk.Name, StringComparer.OrdinalIgnoreCase)
            .Select(fk => new ForeignKeyDocument(
                fk.Name,
                fk.Column,
                fk.ReferencedModule,
                fk.ReferencedTable,
                fk.ReferencedSchema,
                fk.ReferencedColumn,
                fk.ReferencedLogicalTable,
                fk.DeleteAction.ToString(),
                fk.IsNoCheck))
            .ToList();
    }

    private static IReadOnlyList<TriggerDocument> BuildTriggers(ImmutableArray<SmoTriggerDefinition> triggers)
    {
        if (triggers.IsDefaultOrEmpty)
        {
            return Array.Empty<TriggerDocument>();
        }

        return triggers
            .OrderBy(trigger => trigger.Name, StringComparer.OrdinalIgnoreCase)
            .Select(trigger => new TriggerDocument(
                trigger.Name,
                trigger.Schema,
                trigger.Table,
                trigger.IsDisabled,
                trigger.Definition))
            .ToList();
    }

    private static DecisionDocument BuildDecisions(PolicyDecisionSet decisions)
    {
        return new DecisionDocument(
            BuildNullabilityDecisions(decisions.Nullability),
            BuildForeignKeyDecisions(decisions.ForeignKeys),
            BuildUniqueIndexDecisions(decisions.UniqueIndexes),
            BuildDiagnostics(decisions.Diagnostics));
    }

    private static IReadOnlyList<NullabilityDecisionDocument> BuildNullabilityDecisions(ImmutableDictionary<ColumnCoordinate, NullabilityDecision> decisions)
    {
        if (decisions.IsEmpty)
        {
            return Array.Empty<NullabilityDecisionDocument>();
        }

        return decisions
            .OrderBy(pair => pair.Key.Schema.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Key.Table.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Key.Column.Value, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new NullabilityDecisionDocument(
                pair.Key.Schema.Value,
                pair.Key.Table.Value,
                pair.Key.Column.Value,
                pair.Value.MakeNotNull,
                pair.Value.RequiresRemediation,
                SortRationales(pair.Value.Rationales)))
            .ToList();
    }

    private static IReadOnlyList<ForeignKeyDecisionDocument> BuildForeignKeyDecisions(ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision> decisions)
    {
        if (decisions.IsEmpty)
        {
            return Array.Empty<ForeignKeyDecisionDocument>();
        }

        return decisions
            .OrderBy(pair => pair.Key.Schema.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Key.Table.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Key.Column.Value, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new ForeignKeyDecisionDocument(
                pair.Key.Schema.Value,
                pair.Key.Table.Value,
                pair.Key.Column.Value,
                pair.Value.CreateConstraint,
                SortRationales(pair.Value.Rationales)))
            .ToList();
    }

    private static IReadOnlyList<UniqueIndexDecisionDocument> BuildUniqueIndexDecisions(ImmutableDictionary<IndexCoordinate, UniqueIndexDecision> decisions)
    {
        if (decisions.IsEmpty)
        {
            return Array.Empty<UniqueIndexDecisionDocument>();
        }

        return decisions
            .OrderBy(pair => pair.Key.Schema.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Key.Table.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Key.Index.Value, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new UniqueIndexDecisionDocument(
                pair.Key.Schema.Value,
                pair.Key.Table.Value,
                pair.Key.Index.Value,
                pair.Value.EnforceUnique,
                pair.Value.RequiresRemediation,
                SortRationales(pair.Value.Rationales)))
            .ToList();
    }

    private static IReadOnlyList<DiagnosticDocument> BuildDiagnostics(ImmutableArray<TighteningDiagnostic> diagnostics)
    {
        if (diagnostics.IsDefaultOrEmpty)
        {
            return Array.Empty<DiagnosticDocument>();
        }

        return diagnostics
            .OrderBy(diagnostic => diagnostic.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(diagnostic => diagnostic.CanonicalModule, StringComparer.OrdinalIgnoreCase)
            .ThenBy(diagnostic => diagnostic.CanonicalSchema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(diagnostic => diagnostic.CanonicalPhysicalName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(diagnostic => diagnostic.LogicalName, StringComparer.OrdinalIgnoreCase)
            .Select(diagnostic => new DiagnosticDocument(
                diagnostic.Code,
                diagnostic.Message,
                diagnostic.Severity.ToString(),
                diagnostic.LogicalName,
                diagnostic.CanonicalModule,
                diagnostic.CanonicalSchema,
                diagnostic.CanonicalPhysicalName,
                BuildDuplicateCandidates(diagnostic.Candidates),
                diagnostic.ResolvedByOverride))
            .ToList();
    }

    private static IReadOnlyList<DuplicateCandidateDocument> BuildDuplicateCandidates(ImmutableArray<TighteningDuplicateCandidate> candidates)
    {
        if (candidates.IsDefaultOrEmpty)
        {
            return Array.Empty<DuplicateCandidateDocument>();
        }

        return candidates
            .OrderBy(candidate => candidate.Module, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Schema, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.PhysicalName, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => new DuplicateCandidateDocument(candidate.Module, candidate.Schema, candidate.PhysicalName))
            .ToList();
    }

    private static IReadOnlyList<string> SortRationales(ImmutableArray<string> rationales)
    {
        if (rationales.IsDefaultOrEmpty)
        {
            return Array.Empty<string>();
        }

        return rationales
            .Where(rationale => !string.IsNullOrWhiteSpace(rationale))
            .Select(rationale => rationale.Trim())
            .OrderBy(rationale => rationale, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed record EmissionFingerprintDocument(
        EmissionFingerprintOptions Options,
        IReadOnlyList<TableDocument> Tables,
        DecisionDocument Decisions);

    private sealed record EmissionFingerprintOptions(
        bool IncludePlatformAutoIndexes,
        bool EmitBareTableOnly,
        bool SanitizeModuleNames,
        int ModuleParallelism,
        string DefaultCatalog);

    private sealed record TableDocument(
        string Module,
        string OriginalModule,
        string Name,
        string Schema,
        string Catalog,
        string LogicalName,
        string? Description,
        IReadOnlyList<ColumnDocument> Columns,
        IReadOnlyList<IndexDocument> Indexes,
        IReadOnlyList<ForeignKeyDocument> ForeignKeys,
        IReadOnlyList<TriggerDocument> Triggers);

    private sealed record ColumnDocument(
        string Name,
        string LogicalName,
        DataTypeDocument DataType,
        bool Nullable,
        bool IsIdentity,
        int IdentitySeed,
        int IdentityIncrement,
        bool IsComputed,
        string? ComputedExpression,
        string? DefaultExpression,
        string? Collation,
        string? Description,
        DefaultConstraintDocument? DefaultConstraint,
        IReadOnlyList<CheckConstraintDocument> CheckConstraints,
        int Ordinal);

    private sealed record DataTypeDocument(
        string Name,
        string SqlDataType,
        int MaximumLength,
        int NumericPrecision,
        int NumericScale);

    private sealed record DefaultConstraintDocument(string? Name, string Expression, bool IsNotTrusted);

    private sealed record CheckConstraintDocument(string? Name, string Expression, bool IsNotTrusted);

    private sealed record IndexDocument(
        string Name,
        bool IsUnique,
        bool IsPrimaryKey,
        bool IsPlatformAuto,
        IReadOnlyList<IndexColumnDocument> Columns,
        IndexMetadataDocument Metadata);

    private sealed record IndexColumnDocument(string Name, int Ordinal, bool IsIncluded, bool IsDescending);

    private sealed record IndexMetadataDocument(
        bool IsDisabled,
        bool IsPadded,
        int? FillFactor,
        bool IgnoreDuplicateKey,
        bool AllowRowLocks,
        bool AllowPageLocks,
        bool StatisticsNoRecompute,
        string? FilterDefinition,
        IndexDataSpaceDocument? DataSpace,
        IReadOnlyList<IndexPartitionColumnDocument> PartitionColumns,
        IReadOnlyList<IndexCompressionDocument> Compression);

    private sealed record IndexDataSpaceDocument(string Name, string Type);

    private sealed record IndexPartitionColumnDocument(string Name, int Ordinal);

    private sealed record IndexCompressionDocument(int PartitionNumber, string Compression);

    private sealed record ForeignKeyDocument(
        string Name,
        string Column,
        string ReferencedModule,
        string ReferencedTable,
        string ReferencedSchema,
        string ReferencedColumn,
        string ReferencedLogicalTable,
        string DeleteAction,
        bool IsNoCheck);

    private sealed record TriggerDocument(
        string Name,
        string Schema,
        string Table,
        bool IsDisabled,
        string Definition);

    private sealed record DecisionDocument(
        IReadOnlyList<NullabilityDecisionDocument> Nullability,
        IReadOnlyList<ForeignKeyDecisionDocument> ForeignKeys,
        IReadOnlyList<UniqueIndexDecisionDocument> UniqueIndexes,
        IReadOnlyList<DiagnosticDocument> Diagnostics);

    private sealed record NullabilityDecisionDocument(
        string Schema,
        string Table,
        string Column,
        bool MakeNotNull,
        bool RequiresRemediation,
        IReadOnlyList<string> Rationales);

    private sealed record ForeignKeyDecisionDocument(
        string Schema,
        string Table,
        string Column,
        bool CreateConstraint,
        IReadOnlyList<string> Rationales);

    private sealed record UniqueIndexDecisionDocument(
        string Schema,
        string Table,
        string Index,
        bool EnforceUnique,
        bool RequiresRemediation,
        IReadOnlyList<string> Rationales);

    private sealed record DiagnosticDocument(
        string Code,
        string Message,
        string Severity,
        string LogicalName,
        string CanonicalModule,
        string CanonicalSchema,
        string CanonicalPhysicalName,
        IReadOnlyList<DuplicateCandidateDocument> Candidates,
        bool ResolvedByOverride);

    private sealed record DuplicateCandidateDocument(string Module, string Schema, string PhysicalName);
}
