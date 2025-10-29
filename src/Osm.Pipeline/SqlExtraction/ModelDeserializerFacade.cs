using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Json;
using Osm.Domain.Configuration;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.SqlExtraction;

public sealed class ModelDeserializerFacade
{
    private readonly IModelJsonDeserializer _deserializer;
    private readonly ILogger<ModelDeserializerFacade> _logger;

    public ModelDeserializerFacade(
        IModelJsonDeserializer deserializer,
        ILogger<ModelDeserializerFacade>? logger = null)
    {
        _deserializer = deserializer ?? throw new ArgumentNullException(nameof(deserializer));
        _logger = logger ?? NullLogger<ModelDeserializerFacade>.Instance;
    }

    public Result<ModelDeserializerOutcome> Deserialize(
        Stream jsonStream,
        OutsystemsMetadataSnapshot snapshot,
        ModelExtractionCommand command,
        DateTimeOffset exportedAtUtc,
        IReadOnlyList<string> modulesWithoutEntities)
    {
        if (jsonStream is null)
        {
            throw new ArgumentNullException(nameof(jsonStream));
        }

        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        if (modulesWithoutEntities is null)
        {
            throw new ArgumentNullException(nameof(modulesWithoutEntities));
        }

        var warnings = new List<string>();
        var timer = Stopwatch.StartNew();
        var options = EnsureDuplicateLogicalNameTolerance(
            BuildDeserializerOptions(snapshot, command.OnlyActiveAttributes));
        var modelResult = _deserializer.Deserialize(jsonStream, warnings, options);
        timer.Stop();

        if (modelResult.IsFailure)
        {
            if (modelResult.Errors.Length == 1 && modelResult.Errors[0].Code == "model.modules.empty")
            {
                var emptyModel = new OsmModel(
                    exportedAtUtc.UtcDateTime,
                    ImmutableArray<ModuleModel>.Empty,
                    ImmutableArray<SequenceModel>.Empty,
                    ExtendedProperty.EmptyArray);

                warnings.Add("Advanced SQL returned no modules for the requested filter.");

                return Result<ModelDeserializerOutcome>.Success(
                    new ModelDeserializerOutcome(emptyModel, warnings, timer.Elapsed));
            }

            _logger.LogError(
                "Model JSON deserialization failed after {DurationMs} ms with errors: {Errors}.",
                timer.Elapsed.TotalMilliseconds,
                string.Join(", ", modelResult.Errors.Select(static error => error.Code)));

            return Result<ModelDeserializerOutcome>.Failure(modelResult.Errors);
        }

        AppendModuleWarnings(modulesWithoutEntities, warnings);

        if (warnings.Count > 0)
        {
            _logger.LogWarning(
                "Model JSON deserialized with {WarningCount} warning(s): {Warnings}.",
                warnings.Count,
                string.Join(" | ", warnings));
        }
        else
        {
            _logger.LogInformation(
                "Model JSON deserialized successfully in {DurationMs} ms with no warnings.",
                timer.Elapsed.TotalMilliseconds);
        }

        return Result<ModelDeserializerOutcome>.Success(
            new ModelDeserializerOutcome(modelResult.Value, warnings, timer.Elapsed));
    }

    private static void AppendModuleWarnings(
        IReadOnlyList<string> modulesWithoutEntities,
        ICollection<string> warnings)
    {
        foreach (var moduleName in modulesWithoutEntities)
        {
            var message = $"Module '{moduleName}' contains no entities and will be skipped.";
            if (!warnings.Contains(message, StringComparer.Ordinal))
            {
                warnings.Add(message);
            }
        }
    }

    private static ModelJsonDeserializerOptions? BuildDeserializerOptions(
        OutsystemsMetadataSnapshot snapshot,
        bool onlyActiveAttributes)
    {
        if (!onlyActiveAttributes || snapshot.Modules.Count == 0)
        {
            return null;
        }

        var overrides = new Dictionary<string, ModuleValidationOverrideConfiguration>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var module in snapshot.Modules)
        {
            overrides[module.EspaceName] = new ModuleValidationOverrideConfiguration(
                Array.Empty<string>(),
                AllowMissingPrimaryKeyForAll: true,
                Array.Empty<string>(),
                AllowMissingSchemaForAll: false);
        }

        if (overrides.Count == 0)
        {
            return null;
        }

        var overrideResult = ModuleValidationOverrides.Create(overrides);
        if (overrideResult.IsFailure)
        {
            return null;
        }

        return new ModelJsonDeserializerOptions(overrideResult.Value);
    }

    private static ModelJsonDeserializerOptions EnsureDuplicateLogicalNameTolerance(
        ModelJsonDeserializerOptions? options)
    {
        return (options ?? ModelJsonDeserializerOptions.Default)
            .WithAllowDuplicateAttributeLogicalNames(true)
            .WithAllowDuplicateAttributeColumnNames(true);
    }
}

public sealed record ModelDeserializerOutcome(
    OsmModel Model,
    IReadOnlyList<string> Warnings,
    TimeSpan Duration);
