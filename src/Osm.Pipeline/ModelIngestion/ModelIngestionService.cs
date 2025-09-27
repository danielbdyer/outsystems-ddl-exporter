using System.IO;
using System.IO.Abstractions;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Json;

namespace Osm.Pipeline.ModelIngestion;

public interface IModelIngestionService
{
    Task<Result<OsmModel>> LoadFromFileAsync(string path, CancellationToken cancellationToken = default);
}

public sealed class ModelIngestionService : IModelIngestionService
{
    private readonly IModelJsonDeserializer _deserializer;
    private readonly IFileSystem _fileSystem;

    public ModelIngestionService(IModelJsonDeserializer deserializer, IFileSystem? fileSystem = null)
    {
        _deserializer = deserializer ?? throw new ArgumentNullException(nameof(deserializer));
        _fileSystem = fileSystem ?? new FileSystem();
    }

    public async Task<Result<OsmModel>> LoadFromFileAsync(string path, CancellationToken cancellationToken = default)
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
        return _deserializer.Deserialize(stream);
    }
}
