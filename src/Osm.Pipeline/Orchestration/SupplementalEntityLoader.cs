using System.Collections.Immutable;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Json;

namespace Osm.Pipeline.Orchestration;

public sealed class SupplementalEntityLoader
{
    private readonly IModelJsonDeserializer _deserializer;

    public SupplementalEntityLoader(IModelJsonDeserializer? deserializer = null)
    {
        _deserializer = deserializer ?? new ModelJsonDeserializer();
    }

    public async Task<Result<ImmutableArray<EntityModel>>> LoadAsync(
        SupplementalModelOptions configuration,
        CancellationToken cancellationToken = default)
    {
        configuration ??= SupplementalModelOptions.Default;
        var builder = ImmutableArray.CreateBuilder<EntityModel>();

        if (configuration.IncludeUsers)
        {
            builder.Add(OutSystemsInternalModel.Users);
        }

        if (configuration.Paths.Count == 0)
        {
            return builder.ToImmutable();
        }

        foreach (var path in configuration.Paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(path))
            {
                return Result<ImmutableArray<EntityModel>>.Failure(ValidationError.Create(
                    "pipeline.supplemental.path.missing",
                    $"Supplemental model '{path}' was not found."));
            }

            await using var stream = File.OpenRead(path);
            var modelResult = _deserializer.Deserialize(stream);
            if (modelResult.IsFailure)
            {
                return Result<ImmutableArray<EntityModel>>.Failure(modelResult.Errors);
            }

            foreach (var module in modelResult.Value.Modules)
            {
                builder.AddRange(module.Entities);
            }
        }

        return builder.ToImmutable();
    }
}
