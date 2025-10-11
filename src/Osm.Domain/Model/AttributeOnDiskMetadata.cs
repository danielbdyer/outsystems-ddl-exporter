using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Osm.Domain.Model;

public sealed record AttributeOnDiskMetadata(
    bool? IsNullable,
    string? SqlType,
    int? MaxLength,
    int? Precision,
    int? Scale,
    string? Collation,
    bool? IsIdentity,
    bool? IsComputed,
    string? ComputedDefinition,
    string? DefaultDefinition,
    AttributeOnDiskDefaultConstraint? DefaultConstraint,
    ImmutableArray<AttributeOnDiskCheckConstraint> CheckConstraints)
{
    public static readonly AttributeOnDiskMetadata Empty = new(null, null, null, null, null, null, null, null, null, null, null, ImmutableArray<AttributeOnDiskCheckConstraint>.Empty);

    public static AttributeOnDiskMetadata Create(
        bool? isNullable,
        string? sqlType,
        int? maxLength,
        int? precision,
        int? scale,
        string? collation,
        bool? isIdentity,
        bool? isComputed,
        string? computedDefinition,
        string? defaultDefinition,
        AttributeOnDiskDefaultConstraint? defaultConstraint = null,
        IEnumerable<AttributeOnDiskCheckConstraint>? checkConstraints = null)
    {
        var normalizedType = string.IsNullOrWhiteSpace(sqlType) ? null : sqlType!.Trim();
        var normalizedCollation = string.IsNullOrWhiteSpace(collation) ? null : collation!.Trim();
        var normalizedComputed = string.IsNullOrWhiteSpace(computedDefinition) ? null : computedDefinition;
        var normalizedDefault = string.IsNullOrWhiteSpace(defaultDefinition) ? null : defaultDefinition;
        var resolvedDefaultConstraint = defaultConstraint ?? AttributeOnDiskDefaultConstraint.Create(null, normalizedDefault);
        var normalizedChecks = (checkConstraints ?? Enumerable.Empty<AttributeOnDiskCheckConstraint>())
            .Where(static constraint => constraint is not null)
            .Select(static constraint => constraint!)
            .ToImmutableArray();
        if (normalizedChecks.IsDefault)
        {
            normalizedChecks = ImmutableArray<AttributeOnDiskCheckConstraint>.Empty;
        }

        return new AttributeOnDiskMetadata(
            isNullable,
            normalizedType,
            maxLength,
            precision,
            scale,
            normalizedCollation,
            isIdentity,
            isComputed,
            normalizedComputed,
            resolvedDefaultConstraint?.Definition ?? normalizedDefault,
            resolvedDefaultConstraint,
            normalizedChecks);
    }
}
