using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Osm.Emission;

public static class SsdtPredicateNames
{
    public const string HasTemporalHistory = "HasTemporalHistory";
    public const string HasTrigger = "HasTrigger";
    public const string IsStaticEntity = "IsStaticEntity";
    public const string IsExternalEntity = "IsExternalEntity";
    public const string IsInactiveEntity = "IsInactiveEntity";
    public const string HasInactiveColumns = "HasInactiveColumns";
    public const string HasDefaultConstraint = "HasDefaultConstraint";
    public const string HasCheckConstraint = "HasCheckConstraint";
    public const string HasExtendedProperties = "HasExtendedProperties";
    public const string HasUniqueIndex = "HasUniqueIndex";
    public const string HasCompositeUniqueIndex = "HasCompositeUniqueIndex";
    public const string HasFilteredIndex = "HasFilteredIndex";
    public const string HasIncludedIndexColumns = "HasIncludedIndexColumns";
    public const string HasLogicalForeignKey = "HasLogicalForeignKey";
    public const string HasLogicalForeignKeyWithoutDbConstraint = "HasLogicalForeignKeyWithoutDbConstraint";
    public const string HasLogicalForeignKeyWithDbConstraint = "HasLogicalForeignKeyWithDbConstraint";
}

public sealed record PredicateCoverageEntry(
    string Module,
    string Schema,
    string Table,
    IReadOnlyList<string> Predicates)
{
    public static PredicateCoverageEntry Create(string module, string schema, string table, ImmutableArray<string> predicates)
    {
        IReadOnlyList<string> values = predicates.IsDefaultOrEmpty
            ? Array.Empty<string>()
            : predicates.ToArray();
        return new PredicateCoverageEntry(module, schema, table, values);
    }
}

public sealed record SsdtPredicateCoverage(
    IReadOnlyList<PredicateCoverageEntry> Tables,
    IReadOnlyDictionary<string, int> PredicateCounts)
{
    public static readonly SsdtPredicateCoverage Empty = new(
        Array.Empty<PredicateCoverageEntry>(),
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
}
