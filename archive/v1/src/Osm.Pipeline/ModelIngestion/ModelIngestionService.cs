using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Json;

namespace Osm.Pipeline.ModelIngestion;

public interface IModelIngestionService
{
    Task<Result<OsmModel>> LoadFromFileAsync(
        string path,
        ICollection<string>? warnings = null,
        CancellationToken cancellationToken = default,
        ModelIngestionOptions? options = null);
}

public sealed class ModelIngestionService : IModelIngestionService
{
    private readonly IModelJsonDeserializer _deserializer;
    private readonly IRelationshipConstraintHydrator _constraintHydrator;
    private readonly IFileSystem _fileSystem;

    public ModelIngestionService(
        IModelJsonDeserializer deserializer,
        IRelationshipConstraintHydrator? constraintHydrator = null,
        IFileSystem? fileSystem = null)
    {
        _deserializer = deserializer ?? throw new ArgumentNullException(nameof(deserializer));
        _constraintHydrator = constraintHydrator ?? NullRelationshipConstraintHydrator.Instance;
        _fileSystem = fileSystem ?? new FileSystem();
    }

    public async Task<Result<OsmModel>> LoadFromFileAsync(
        string path,
        ICollection<string>? warnings = null,
        CancellationToken cancellationToken = default,
        ModelIngestionOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Result<OsmModel>.Failure(ValidationError.Create("ingestion.path.missing", "Input path must be provided."));
        }

        var trimmed = path.Trim();
        if (!_fileSystem.File.Exists(trimmed))
        {
            return Result<OsmModel>.Failure(ValidationError.Create("ingestion.path.notFound", $"Input file '{trimmed}' was not found."));
        }

        await using var stream = _fileSystem.File.Open(trimmed, FileMode.Open, FileAccess.Read, FileShare.Read);
        var ingestionOptions = options ?? ModelIngestionOptions.Empty;
        var deserializerOptions = new ModelJsonDeserializerOptions(
            ingestionOptions.ValidationOverrides,
            ingestionOptions.MissingSchemaFallback);

        var deserializeResult = _deserializer.Deserialize(stream, warnings, deserializerOptions);
        if (deserializeResult.IsFailure)
        {
            return deserializeResult;
        }

        var model = deserializeResult.Value;
        if (ingestionOptions.SqlMetadata is null)
        {
            return Result<OsmModel>.Success(model);
        }

        var hydratedResult = await _constraintHydrator
            .HydrateAsync(model, ingestionOptions.SqlMetadata, warnings, cancellationToken)
            .ConfigureAwait(false);

        return hydratedResult;
    }
}
