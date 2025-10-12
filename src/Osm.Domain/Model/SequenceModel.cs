using System.Collections.Immutable;

using Osm.Domain.Abstractions;
using Osm.Domain.ValueObjects;

namespace Osm.Domain.Model;

public enum SequenceCacheMode
{
    Unspecified,
    Cache,
    NoCache,
    UnsupportedYet
}

public sealed record SequenceModel(
    SchemaName Schema,
    SequenceName Name,
    string DataType,
    decimal? StartValue,
    decimal? Increment,
    decimal? Minimum,
    decimal? Maximum,
    bool IsCycleEnabled,
    SequenceCacheMode CacheMode,
    int? CacheSize,
    ImmutableArray<ExtendedProperty> ExtendedProperties)
{
    public static Result<SequenceModel> Create(
        SchemaName schema,
        SequenceName name,
        string? dataType,
        decimal? startValue,
        decimal? increment,
        decimal? minimum,
        decimal? maximum,
        bool isCycleEnabled,
        SequenceCacheMode cacheMode,
        int? cacheSize,
        ImmutableArray<ExtendedProperty> extendedProperties)
    {
        if (string.IsNullOrWhiteSpace(dataType))
        {
            return Result<SequenceModel>.Failure(ValidationError.Create("sequence.dataType.invalid", "Sequence data type must be provided."));
        }

        if (cacheMode == SequenceCacheMode.Cache && (cacheSize is null or < 0))
        {
            cacheMode = SequenceCacheMode.UnsupportedYet;
        }

        var normalized = dataType.Trim();

        return Result<SequenceModel>.Success(new SequenceModel(
            schema,
            name,
            normalized,
            startValue,
            increment,
            minimum,
            maximum,
            isCycleEnabled,
            cacheMode,
            cacheSize,
            extendedProperties.IsDefault ? ExtendedProperty.EmptyArray : extendedProperties));
    }
}
