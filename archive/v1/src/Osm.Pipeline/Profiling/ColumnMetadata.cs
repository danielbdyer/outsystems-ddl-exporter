namespace Osm.Pipeline.Profiling;

internal sealed record ColumnMetadata(bool IsNullable, bool IsComputed, bool IsPrimaryKey, string? DefaultDefinition);
