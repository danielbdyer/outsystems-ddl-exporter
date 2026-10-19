using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Osm.Smo;

public sealed record PerTableHeaderOptions(
    bool Enabled,
    string? Source,
    string? Profile,
    string? Decisions,
    string? FingerprintAlgorithm,
    string? FingerprintHash,
    ImmutableArray<PerTableHeaderItem> AdditionalItems)
{
    public static PerTableHeaderOptions Disabled { get; } = new(
        Enabled: false,
        Source: null,
        Profile: null,
        Decisions: null,
        FingerprintAlgorithm: null,
        FingerprintHash: null,
        AdditionalItems: ImmutableArray<PerTableHeaderItem>.Empty);

    public static PerTableHeaderOptions EnabledTemplate { get; } = new(
        Enabled: true,
        Source: null,
        Profile: null,
        Decisions: null,
        FingerprintAlgorithm: null,
        FingerprintHash: null,
        AdditionalItems: ImmutableArray<PerTableHeaderItem>.Empty);

    public PerTableHeaderOptions Normalize()
    {
        var additional = AdditionalItems.IsDefault
            ? ImmutableArray<PerTableHeaderItem>.Empty
            : AdditionalItems
                .Where(static item => item is not null)
                .Select(static item => item.Normalize())
                .OrderBy(static item => item.Label, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.Value, StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray();

        return this with { AdditionalItems = additional };
    }
}

public sealed record PerTableHeaderItem(string Label, string Value)
{
    public PerTableHeaderItem Normalize()
    {
        var trimmedLabel = string.IsNullOrWhiteSpace(Label) ? string.Empty : Label.Trim();
        var trimmedValue = string.IsNullOrWhiteSpace(Value) ? string.Empty : Value.Trim();
        return new PerTableHeaderItem(trimmedLabel, trimmedValue);
    }

    public static PerTableHeaderItem Create(string label, string value)
    {
        if (label is null)
        {
            throw new ArgumentNullException(nameof(label));
        }

        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return new PerTableHeaderItem(label, value).Normalize();
    }
}
