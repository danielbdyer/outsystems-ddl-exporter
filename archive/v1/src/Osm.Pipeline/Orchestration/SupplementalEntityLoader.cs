using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
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
        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (configuration.IncludeUsers)
        {
            var builtInResult = await TryLoadBuiltInUsersAsync(processed, builder, cancellationToken).ConfigureAwait(false);
            if (builtInResult.IsFailure)
            {
                return Result<ImmutableArray<EntityModel>>.Failure(builtInResult.Errors);
            }

            if (!builtInResult.Value)
            {
                AddEntityIfNew(OutSystemsInternalModel.Users, processed, builder);
            }
        }

        if (configuration.Paths.Count == 0)
        {
            return builder.ToImmutable();
        }

        foreach (var path in configuration.Paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var loadResult = await LoadEntitiesFromPathAsync(path, processed, builder, cancellationToken).ConfigureAwait(false);
            if (loadResult.IsFailure)
            {
                return Result<ImmutableArray<EntityModel>>.Failure(loadResult.Errors);
            }
        }

        return builder.ToImmutable();
    }

    private async Task<Result<bool>> TryLoadBuiltInUsersAsync(
        HashSet<string> processed,
        ImmutableArray<EntityModel>.Builder builder,
        CancellationToken cancellationToken)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var candidatePaths = new List<string>
        {
            Path.Combine(baseDirectory, "config", "supplemental", "ossys-user.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "config", "supplemental", "ossys-user.json"),
        };

        foreach (var candidate in candidatePaths)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            var result = await LoadEntitiesFromPathAsync(candidate, processed, builder, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return Result<bool>.Failure(result.Errors);
            }

            return Result<bool>.Success(true);
        }

        return Result<bool>.Success(false);
    }

    private async Task<Result<bool>> LoadEntitiesFromPathAsync(
        string path,
        HashSet<string> processed,
        ImmutableArray<EntityModel>.Builder builder,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return Result<bool>.Failure(ValidationError.Create(
                "pipeline.supplemental.path.missing",
                $"Supplemental model '{path}' was not found."));
        }

        await using var stream = File.OpenRead(path);
        var modelResult = _deserializer.Deserialize(stream);
        if (modelResult.IsFailure)
        {
            return Result<bool>.Failure(modelResult.Errors);
        }

        foreach (var module in modelResult.Value.Modules)
        {
            foreach (var entity in module.Entities)
            {
                AddEntityIfNew(entity, processed, builder);
            }
        }

        return Result<bool>.Success(true);
    }

    private static void AddEntityIfNew(
        EntityModel entity,
        HashSet<string> processed,
        ImmutableArray<EntityModel>.Builder builder)
    {
        if (entity is null)
        {
            return;
        }

        var schema = entity.Schema.Value;
        var table = entity.PhysicalName.Value;

        var key = string.Create(
            schema.Length + table.Length + 1,
            (schema, table),
            static (span, state) =>
            {
                var (innerSchema, innerTable) = state;
                innerSchema.AsSpan().CopyTo(span);
                span[innerSchema.Length] = '.';
                innerTable.AsSpan().CopyTo(span[(innerSchema.Length + 1)..]);
            });

        if (processed.Add(key))
        {
            builder.Add(entity);
        }
    }
}
