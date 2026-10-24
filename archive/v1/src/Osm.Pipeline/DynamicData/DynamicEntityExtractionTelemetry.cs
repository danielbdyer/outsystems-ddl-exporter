using System;
using System.Collections.Immutable;
using System.Linq;
using Osm.Emission;

namespace Osm.Pipeline.DynamicData;

public sealed record DynamicEntityExtractionResult(
    DynamicEntityDataset Dataset,
    DynamicEntityExtractionTelemetry Telemetry,
    ImmutableArray<StaticSeedParentStatus> StaticSeedParents)
{
    public static DynamicEntityExtractionResult Empty { get; } = new(
        DynamicEntityDataset.Empty,
        DynamicEntityExtractionTelemetry.Empty,
        ImmutableArray<StaticSeedParentStatus>.Empty);
}

public sealed record DynamicEntityExtractionTelemetry(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    ImmutableArray<DynamicEntityTableTelemetry> Tables)
{
    public static DynamicEntityExtractionTelemetry Empty { get; } = new(
        DateTimeOffset.MinValue,
        DateTimeOffset.MinValue,
        ImmutableArray<DynamicEntityTableTelemetry>.Empty);

    public bool HasTables => !Tables.IsDefaultOrEmpty && Tables.Length > 0;

    public int TableCount => Tables.IsDefaultOrEmpty ? 0 : Tables.Length;

    public long RowCount => Tables.IsDefaultOrEmpty
        ? 0
        : Tables.Sum(static table => table.RowCount);
}

public sealed record DynamicEntityTableTelemetry(
    string Module,
    string Entity,
    string Schema,
    string PhysicalName,
    string EffectiveName,
    long RowCount,
    int BatchCount,
    TimeSpan Duration,
    string Checksum,
    ImmutableArray<DynamicEntityChunkTelemetry> Chunks)
{
    public bool HasChunks => !Chunks.IsDefaultOrEmpty && Chunks.Length > 0;
}

public sealed record DynamicEntityChunkTelemetry(
    int Sequence,
    int RowCount,
    TimeSpan Duration);
