using Osm.Domain.Configuration;

namespace Osm.Pipeline.ModelIngestion;

public sealed record ModelIngestionOptions(
    ModuleValidationOverrides ValidationOverrides,
    string? MissingSchemaFallback)
{
    public static ModelIngestionOptions Empty { get; }
        = new(ModuleValidationOverrides.Empty, null);
}
