using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Json;

namespace Osm.Pipeline.SqlExtraction;

public interface ISqlModelExtractionService
{
    Task<Result<ModelExtractionResult>> ExtractAsync(ModelExtractionCommand command, CancellationToken cancellationToken = default);
}

public sealed class SqlModelExtractionService : ISqlModelExtractionService
{
    private readonly IOutsystemsMetadataReader _metadataReader;
    private readonly IModelJsonDeserializer _deserializer;
    private readonly ILogger<SqlModelExtractionService> _logger;

    public SqlModelExtractionService(
        IOutsystemsMetadataReader metadataReader,
        IModelJsonDeserializer deserializer,
        ILogger<SqlModelExtractionService>? logger = null)
    {
        _metadataReader = metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));
        _deserializer = deserializer ?? throw new ArgumentNullException(nameof(deserializer));
        _logger = logger ?? NullLogger<SqlModelExtractionService>.Instance;
    }

    public async Task<Result<ModelExtractionResult>> ExtractAsync(ModelExtractionCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        _logger.LogInformation(
            "Executing advanced SQL for {ModuleCount} module(s) (includeSystem: {IncludeSystem}, onlyActive: {OnlyActive}).",
            command.ModuleNames.Length,
            command.IncludeSystemModules,
            command.OnlyActiveAttributes);

        if (command.ModuleNames.Length > 0)
        {
            _logger.LogDebug(
                "Advanced SQL module list: {Modules}.",
                string.Join(",", command.ModuleNames.Select(static module => module.Value)));
        }

        var request = new AdvancedSqlRequest(command.ModuleNames, command.IncludeSystemModules, command.OnlyActiveAttributes);

        var metadataTimer = Stopwatch.StartNew();
        var metadataResult = await _metadataReader.ReadAsync(request, cancellationToken).ConfigureAwait(false);
        metadataTimer.Stop();

        if (metadataResult.IsFailure)
        {
            _logger.LogError(
                "Metadata reader failed after {DurationMs} ms with errors: {Errors}.",
                metadataTimer.Elapsed.TotalMilliseconds,
                string.Join(", ", metadataResult.Errors.Select(static error => error.Code)));
            return Result<ModelExtractionResult>.Failure(metadataResult.Errors.ToArray());
        }

        var snapshot = metadataResult.Value;
        var exportedAtUtc = DateTimeOffset.UtcNow;

        await using var jsonBuffer = new MemoryStream();
        BuildJsonFromSnapshot(snapshot, exportedAtUtc.UtcDateTime, jsonBuffer);

        if (jsonBuffer.Length == 0)
        {
            _logger.LogError("Metadata reader produced an empty JSON payload.");
            return Result<ModelExtractionResult>.Failure(ValidationError.Create("extraction.sql.emptyJson", "Metadata reader produced no JSON payload."));
        }

        var warnings = new List<string>();

        var deserializeTimer = Stopwatch.StartNew();
        jsonBuffer.Position = 0;
        var modelResult = _deserializer.Deserialize(jsonBuffer, warnings);
        deserializeTimer.Stop();

        if (modelResult.IsFailure)
        {
            if (modelResult.Errors.Length == 1 && modelResult.Errors[0].Code == "model.modules.empty")
            {
                _logger.LogWarning(
                    "Model JSON deserialization returned no modules after {DurationMs} ms. Treating as empty snapshot.",
                    deserializeTimer.Elapsed.TotalMilliseconds);

                var emptyModel = new OsmModel(
                    exportedAtUtc.UtcDateTime,
                    ImmutableArray<ModuleModel>.Empty,
                    ImmutableArray<SequenceModel>.Empty,
                    ExtendedProperty.EmptyArray);

                warnings.Add("Advanced SQL returned no modules for the requested filter.");
                var emptyJson = MaterializeJson(jsonBuffer);
                var emptyResult = new ModelExtractionResult(emptyModel, emptyJson, exportedAtUtc, warnings, snapshot);
                return Result<ModelExtractionResult>.Success(emptyResult);
            }

            _logger.LogError(
                "Model JSON deserialization failed after {DurationMs} ms with errors: {Errors}.",
                deserializeTimer.Elapsed.TotalMilliseconds,
                string.Join(", ", modelResult.Errors.Select(static error => error.Code)));
            return Result<ModelExtractionResult>.Failure(modelResult.Errors.ToArray());
        }

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
                deserializeTimer.Elapsed.TotalMilliseconds);
        }

        var materializedJson = MaterializeJson(jsonBuffer);
        var result = new ModelExtractionResult(modelResult.Value, materializedJson, exportedAtUtc, warnings, snapshot);
        _logger.LogInformation(
            "Model extraction finished in {TotalDurationMs} ms (metadata: {MetadataMs} ms, deserialize: {DeserializeMs} ms).",
            metadataTimer.Elapsed.TotalMilliseconds + deserializeTimer.Elapsed.TotalMilliseconds,
            metadataTimer.Elapsed.TotalMilliseconds,
            deserializeTimer.Elapsed.TotalMilliseconds);

        return Result<ModelExtractionResult>.Success(result);
    }

    private static void BuildJsonFromSnapshot(OutsystemsMetadataSnapshot snapshot, DateTime exportedAtUtc, Stream destination)
    {
        if (destination is null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        destination.SetLength(0);

        using var writer = new Utf8JsonWriter(destination);

        writer.WriteStartObject();
        writer.WriteString("exportedAtUtc", exportedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        writer.WritePropertyName("modules");
        writer.WriteStartArray();

        foreach (var module in snapshot.ModuleJson)
        {
            writer.WriteStartObject();
            writer.WriteString("name", module.ModuleName);
            writer.WriteBoolean("isSystem", module.IsSystem);
            writer.WriteBoolean("isActive", module.IsActive);
            writer.WritePropertyName("entities");

            var entitiesPayload = string.IsNullOrWhiteSpace(module.ModuleEntitiesJson)
                ? "[]"
                : module.ModuleEntitiesJson;

            writer.WriteRawValue(entitiesPayload, skipInputValidation: true);

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static string MaterializeJson(MemoryStream buffer)
    {
        if (buffer is null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        if (!buffer.TryGetBuffer(out var segment))
        {
            return Encoding.UTF8.GetString(buffer.ToArray());
        }

        return Encoding.UTF8.GetString(segment.Array!, segment.Offset, segment.Count);
    }
}

public sealed class ModelExtractionResult
{
    public ModelExtractionResult(
        OsmModel model,
        string json,
        DateTimeOffset extractedAtUtc,
        IReadOnlyList<string> warnings,
        OutsystemsMetadataSnapshot metadata)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        Json = json ?? throw new ArgumentNullException(nameof(json));
        ExtractedAtUtc = extractedAtUtc;
        Warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    public OsmModel Model { get; }

    public string Json { get; }

    public DateTimeOffset ExtractedAtUtc { get; }

    public IReadOnlyList<string> Warnings { get; }

    public OutsystemsMetadataSnapshot Metadata { get; }
}

public sealed class ModelExtractionCommand
{
    private ModelExtractionCommand(ImmutableArray<ModuleName> moduleNames, bool includeSystemModules, bool onlyActiveAttributes)
    {
        ModuleNames = moduleNames;
        IncludeSystemModules = includeSystemModules;
        OnlyActiveAttributes = onlyActiveAttributes;
    }

    public ImmutableArray<ModuleName> ModuleNames { get; }

    public bool IncludeSystemModules { get; }

    public bool OnlyActiveAttributes { get; }

    public static Result<ModelExtractionCommand> Create(IEnumerable<string>? moduleNames, bool includeSystemModules, bool onlyActiveAttributes)
    {
        if (moduleNames is null)
        {
            return new ModelExtractionCommand(ImmutableArray<ModuleName>.Empty, includeSystemModules, onlyActiveAttributes);
        }

        var modules = ImmutableArray.CreateBuilder<ModuleName>();
        var errors = ImmutableArray.CreateBuilder<ValidationError>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var candidate in moduleNames)
        {
            if (candidate is null)
            {
                errors.Add(ValidationError.Create(
                    "extraction.modules.null",
                    $"Module name at position {index} must not be null."));
                index++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(candidate))
            {
                errors.Add(ValidationError.Create(
                    "extraction.modules.empty",
                    $"Module name at position {index} must not be empty or whitespace."));
                index++;
                continue;
            }

            var trimmed = candidate.Trim();
            var moduleResult = ModuleName.Create(trimmed);
            if (moduleResult.IsFailure)
            {
                foreach (var error in moduleResult.Errors)
                {
                    errors.Add(ValidationError.Create(
                        error.Code,
                        $"Module name '{trimmed}' is invalid: {error.Message}"));
                }

                index++;
                continue;
            }

            var moduleName = moduleResult.Value;
            if (seen.Add(moduleName.Value))
            {
                modules.Add(moduleName);
            }

            index++;
        }

        if (errors.Count > 0)
        {
            return Result<ModelExtractionCommand>.Failure(errors.ToImmutable());
        }

        var normalized = modules.ToImmutable();
        if (!normalized.IsDefaultOrEmpty)
        {
            normalized = normalized.Sort(Comparer<ModuleName>.Create(static (left, right)
                => string.Compare(left.Value, right.Value, StringComparison.OrdinalIgnoreCase)));
        }

        return new ModelExtractionCommand(normalized, includeSystemModules, onlyActiveAttributes);
    }
}
