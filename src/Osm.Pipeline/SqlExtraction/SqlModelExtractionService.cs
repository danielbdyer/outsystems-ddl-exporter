using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Emission;
using Osm.Json;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.SqlExtraction;

public interface ISqlModelExtractionService
{
    Task<Result<ModelExtractionResult>> ExtractAsync(
        ModelExtractionCommand command,
        ModelExtractionOptions? options = null,
        CancellationToken cancellationToken = default);
}

public sealed class SqlModelExtractionService : ISqlModelExtractionService
{
    private readonly AdvancedSqlMetadataOrchestrator _metadataOrchestrator;
    private readonly SnapshotJsonBuilder _snapshotJsonBuilder;
    private readonly SnapshotValidator _snapshotValidator;
    private readonly ModelDeserializerFacade _modelDeserializerFacade;
    private readonly ILogger<SqlModelExtractionService> _logger;

    public SqlModelExtractionService(
        AdvancedSqlMetadataOrchestrator metadataOrchestrator,
        SnapshotJsonBuilder snapshotJsonBuilder,
        SnapshotValidator snapshotValidator,
        ModelDeserializerFacade modelDeserializerFacade,
        ILogger<SqlModelExtractionService>? logger = null)
    {
        _metadataOrchestrator = metadataOrchestrator ?? throw new ArgumentNullException(nameof(metadataOrchestrator));
        _snapshotJsonBuilder = snapshotJsonBuilder ?? throw new ArgumentNullException(nameof(snapshotJsonBuilder));
        _snapshotValidator = snapshotValidator ?? throw new ArgumentNullException(nameof(snapshotValidator));
        _modelDeserializerFacade = modelDeserializerFacade ?? throw new ArgumentNullException(nameof(modelDeserializerFacade));
        _logger = logger ?? NullLogger<SqlModelExtractionService>.Instance;
    }

    public async Task<Result<ModelExtractionResult>> ExtractAsync(
        ModelExtractionCommand command,
        ModelExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        options ??= ModelExtractionOptions.InMemory();

        var metadataResult = await _metadataOrchestrator
            .ExecuteAsync(command, options, cancellationToken)
            .ConfigureAwait(false);

        if (metadataResult.IsFailure)
        {
            return Result<ModelExtractionResult>.Failure(metadataResult.Errors);
        }

        var metadata = metadataResult.Value;

        await using var artifact = _snapshotJsonBuilder.Build(
            metadata.Snapshot,
            metadata.ExportedAtUtc.UtcDateTime,
            options);

        var jsonStream = artifact.Stream;

        if (jsonStream.Position == 0)
        {
            _logger.LogError("Metadata reader produced an empty JSON payload.");
            return Result<ModelExtractionResult>.Failure(
                ValidationError.Create("extraction.sql.emptyJson", "Metadata reader produced no JSON payload."));
        }

        ValidationError? contractError;
        try
        {
            contractError = _snapshotValidator.Validate(jsonStream);
        }
        catch (InvalidDataException ex)
        {
            _logger.LogError(
                "Advanced SQL payload violated array contract: {Message}.",
                ex.Message);
            return Result<ModelExtractionResult>.Failure(
                ValidationError.Create("extraction.sql.contract.entityArray", ex.Message));
        }

        if (contractError is ValidationError error)
        {
            _logger.LogError(
                "Advanced SQL payload violated array contract: {Message}.",
                error.Message);
            return Result<ModelExtractionResult>.Failure(error);
        }

        jsonStream.Position = 0;

        var deserializationResult = _modelDeserializerFacade.Deserialize(
            jsonStream,
            metadata.Snapshot,
            command,
            metadata.ExportedAtUtc,
            metadata.ModulesWithoutEntities);

        if (deserializationResult.IsFailure)
        {
            return Result<ModelExtractionResult>.Failure(deserializationResult.Errors);
        }

        var outcome = deserializationResult.Value;

        jsonStream.Position = 0;
        var payload = artifact.FilePath is not null
            ? ModelJsonPayload.FromFile(artifact.FilePath)
            : ModelJsonPayload.FromStream(jsonStream);

        var result = new ModelExtractionResult(
            outcome.Model,
            payload,
            metadata.ExportedAtUtc,
            outcome.Warnings,
            metadata.Snapshot,
            DynamicEntityDataset.Empty);

        _logger.LogInformation(
            "Model extraction finished in {TotalDurationMs} ms (metadata: {MetadataMs} ms, deserialize: {DeserializeMs} ms).",
            metadata.MetadataDuration.TotalMilliseconds + outcome.Duration.TotalMilliseconds,
            metadata.MetadataDuration.TotalMilliseconds,
            outcome.Duration.TotalMilliseconds);

        return Result<ModelExtractionResult>.Success(result);
    }
}

public sealed class ModelExtractionResult
{
    public ModelExtractionResult(
        OsmModel model,
        ModelJsonPayload jsonPayload,
        DateTimeOffset extractedAtUtc,
        IReadOnlyList<string> warnings,
        OutsystemsMetadataSnapshot metadata,
        DynamicEntityDataset? dataset = null)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        JsonPayload = jsonPayload ?? throw new ArgumentNullException(nameof(jsonPayload));
        ExtractedAtUtc = extractedAtUtc;
        Warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        Dataset = dataset ?? DynamicEntityDataset.Empty;
    }

    public OsmModel Model { get; }

    public ModelJsonPayload JsonPayload { get; }

    public DateTimeOffset ExtractedAtUtc { get; }

    public IReadOnlyList<string> Warnings { get; }

    public OutsystemsMetadataSnapshot Metadata { get; }

    public DynamicEntityDataset Dataset { get; }
}

public sealed class ModelJsonPayload
{
    private readonly Stream? _buffer;
    private readonly string? _filePath;

    private ModelJsonPayload(Stream? buffer, string? filePath)
    {
        _buffer = buffer;
        _filePath = filePath;
    }

    public static ModelJsonPayload FromStream(Stream stream)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (!stream.CanSeek)
        {
            throw new ArgumentException("JSON buffer stream must support seeking.", nameof(stream));
        }

        return new ModelJsonPayload(stream, null);
    }

    public static ModelJsonPayload FromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path must be provided.", nameof(filePath));
        }

        return new ModelJsonPayload(null, Path.GetFullPath(filePath));
    }

    public bool IsPersisted => _filePath is not null;

    public string? FilePath => _filePath;

    public async ValueTask<string> ReadAsStringAsync(CancellationToken cancellationToken = default)
    {
        if (_buffer is not null)
        {
            if (!_buffer.CanSeek)
            {
                throw new InvalidOperationException("JSON buffer stream must support seeking.");
            }

            _buffer.Position = 0;
            using var reader = new StreamReader(_buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
#if NET8_0_OR_GREATER
            var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
#else
            var text = await reader.ReadToEndAsync().ConfigureAwait(false);
#endif
            _buffer.Position = 0;
            return text;
        }

        if (_filePath is not null)
        {
            await using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
#if NET8_0_OR_GREATER
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
            return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
#else
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
            return await reader.ReadToEndAsync().ConfigureAwait(false);
#endif
        }

        throw new InvalidOperationException("JSON payload is not available.");
    }

    public async ValueTask CopyToAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        if (destination is null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        if (_buffer is not null)
        {
            if (!_buffer.CanSeek)
            {
                throw new InvalidOperationException("JSON buffer stream must support seeking.");
            }

            _buffer.Position = 0;
            await _buffer.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            _buffer.Position = 0;
            return;
        }

        if (_filePath is not null)
        {
            await using var source = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            return;
        }

        throw new InvalidOperationException("JSON payload is not available.");
    }
}

public sealed class ModelExtractionOptions
{
    public ModelExtractionOptions(
        Stream? destinationStream = null,
        string? destinationPath = null,
        string? metadataOutputPath = null,
        SqlMetadataLog? metadataLog = null)
    {
        DestinationStream = destinationStream;
        DestinationPath = destinationPath;
        MetadataOutputPath = metadataOutputPath;
        MetadataLog = metadataLog;
    }

    public Stream? DestinationStream { get; }

    public string? DestinationPath { get; }

    public string? MetadataOutputPath { get; }

    public SqlMetadataLog? MetadataLog { get; }

    public static ModelExtractionOptions InMemory(string? metadataOutputPath = null, SqlMetadataLog? metadataLog = null)
        => new(null, null, metadataOutputPath, metadataLog);

    public static ModelExtractionOptions ToStream(Stream stream, string? metadataOutputPath = null, SqlMetadataLog? metadataLog = null)
        => new(stream ?? throw new ArgumentNullException(nameof(stream)), null, metadataOutputPath, metadataLog);

    public static ModelExtractionOptions ToFile(string path, string? metadataOutputPath = null, SqlMetadataLog? metadataLog = null)
        => string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("File path must be provided.", nameof(path))
            : new ModelExtractionOptions(null, path, metadataOutputPath, metadataLog);
}

public sealed class ModelExtractionCommand
{
    private ModelExtractionCommand(
        ImmutableArray<ModuleName> moduleNames,
        bool includeSystemModules,
        bool includeInactiveModules,
        bool onlyActiveAttributes)
    {
        ModuleNames = moduleNames;
        IncludeSystemModules = includeSystemModules;
        IncludeInactiveModules = includeInactiveModules;
        OnlyActiveAttributes = onlyActiveAttributes;
    }

    public ImmutableArray<ModuleName> ModuleNames { get; }

    public bool IncludeSystemModules { get; }

    public bool IncludeInactiveModules { get; }

    public bool OnlyActiveAttributes { get; }

    public static Result<ModelExtractionCommand> Create(
        IEnumerable<string>? moduleNames,
        bool includeSystemModules,
        bool includeInactiveModules,
        bool onlyActiveAttributes)
    {
        if (moduleNames is null)
        {
            return new ModelExtractionCommand(
                ImmutableArray<ModuleName>.Empty,
                includeSystemModules,
                includeInactiveModules,
                onlyActiveAttributes);
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

        return new ModelExtractionCommand(normalized, includeSystemModules, includeInactiveModules, onlyActiveAttributes);
    }
}
