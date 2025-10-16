using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Profiling;

namespace Osm.Pipeline.Profiling;

public sealed record ProfilingCaptureResult
{
    public ProfilingCaptureResult(ProfileSnapshot snapshot, ImmutableArray<string> warnings)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        Warnings = warnings.IsDefault ? ImmutableArray<string>.Empty : warnings;
    }

    public ProfileSnapshot Snapshot { get; }

    public ImmutableArray<string> Warnings { get; }

    public static ProfilingCaptureResult Create(ProfileSnapshot snapshot, IEnumerable<string>? warnings = null)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        var materialized = warnings is null
            ? ImmutableArray<string>.Empty
            : warnings.ToImmutableArray();

        return new ProfilingCaptureResult(snapshot, materialized);
    }
}
