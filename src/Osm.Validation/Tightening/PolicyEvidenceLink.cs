using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using Osm.Domain.Profiling;

namespace Osm.Validation.Tightening;

public sealed record PolicyEvidenceLink(string Source, string Reference, bool IsPresent, ImmutableDictionary<string, string> Metrics)
{
    public static PolicyEvidenceLink Create(string source, string reference, IReadOnlyDictionary<string, string>? metrics = null)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Evidence source must be provided.", nameof(source));
        }

        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("Evidence reference must be provided.", nameof(reference));
        }

        var metricDictionary = metrics is null
            ? ImmutableDictionary<string, string>.Empty
            : metrics.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);

        return new PolicyEvidenceLink(source, reference, true, metricDictionary);
    }

    public static PolicyEvidenceLink Missing(string source, string reference)
        => Create(source, reference, ImmutableDictionary<string, string>.Empty).WithPresence(false);

    public static PolicyEvidenceLink FromColumnProfile(ColumnProfile profile)
    {
        var metrics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["RowCount"] = profile.RowCount.ToString(CultureInfo.InvariantCulture),
            ["NullCount"] = profile.NullCount.ToString(CultureInfo.InvariantCulture),
            ["PhysicalNullable"] = profile.IsNullablePhysical ? "true" : "false",
            ["Default"] = string.IsNullOrWhiteSpace(profile.DefaultDefinition) ? string.Empty : profile.DefaultDefinition,
            ["IsComputed"] = profile.IsComputed ? "true" : "false",
            ["IsPrimaryKey"] = profile.IsPrimaryKey ? "true" : "false",
            ["IsUniqueKey"] = profile.IsUniqueKey ? "true" : "false"
        };

        return Create("profiling.column", new ColumnCoordinate(profile.Schema, profile.Table, profile.Column).ToString(), metrics);
    }

    public static PolicyEvidenceLink FromUniqueCandidate(UniqueCandidateProfile profile)
    {
        var metrics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["HasDuplicate"] = profile.HasDuplicate ? "true" : "false"
        };

        return Create("profiling.unique", new ColumnCoordinate(profile.Schema, profile.Table, profile.Column).ToString(), metrics);
    }

    public static PolicyEvidenceLink FromForeignKeyReality(ForeignKeyReality reality)
    {
        var metrics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["HasOrphan"] = reality.HasOrphan ? "true" : "false",
            ["IsNoCheck"] = reality.IsNoCheck ? "true" : "false"
        };

        return Create("profiling.foreignKey", ColumnCoordinate.From(reality.Reference).ToString(), metrics);
    }

    private PolicyEvidenceLink WithPresence(bool isPresent)
        => new(Source, Reference, isPresent, Metrics);
}
