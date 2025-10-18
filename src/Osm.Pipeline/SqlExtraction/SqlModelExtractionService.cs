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
        options.MetadataLog?.RecordRequest(
            "advancedSql.request",
            new
            {
                modules = request.ModuleNames.Select(static module => module.Value).ToArray(),
                includeSystem = request.IncludeSystemModules,
                onlyActive = request.OnlyActiveAttributes
            });

        var metadataTimer = Stopwatch.StartNew();
        var metadataResult = await _metadataReader.ReadAsync(request, cancellationToken).ConfigureAwait(false);
        metadataTimer.Stop();

        if (metadataResult.IsFailure)
        {
            _logger.LogError(
                "Metadata reader failed after {DurationMs} ms with errors: {Errors}.",
                metadataTimer.Elapsed.TotalMilliseconds,
                string.Join(", ", metadataResult.Errors.Select(static error => error.Code)));

            options.MetadataLog?.RecordFailure(metadataResult.Errors, TryGetFailureSnapshot());

            return Result<ModelExtractionResult>.Failure(metadataResult.Errors.ToArray());
        }

        var snapshot = metadataResult.Value;
        var exportedAtUtc = DateTimeOffset.UtcNow;
        options.MetadataLog?.RecordSnapshot(snapshot, exportedAtUtc);
        options.MetadataLog?.RecordRequest(
            "advancedSql.duration",
            new
            {
                metadataMilliseconds = metadataTimer.Elapsed.TotalMilliseconds
            });

        await using var destination = CreateDestination(options);
        var jsonStream = destination.Stream;

        BuildJsonFromSnapshot(snapshot, exportedAtUtc.UtcDateTime, jsonStream);

        if (jsonStream.Position == 0)
        {
            _logger.LogError("Metadata reader produced an empty JSON payload.");
            return Result<ModelExtractionResult>.Failure(ValidationError.Create("extraction.sql.emptyJson", "Metadata reader produced no JSON payload."));
        }

        jsonStream.Position = 0;
        var warnings = new List<string>();

        var deserializeTimer = Stopwatch.StartNew();
        var modelResult = _deserializer.Deserialize(jsonStream, warnings);
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
                jsonStream.Position = 0;
                var emptyPayload = destination.FilePath is not null
                    ? ModelJsonPayload.FromFile(destination.FilePath)
                    : ModelJsonPayload.FromStream(jsonStream);
                var emptyResult = new ModelExtractionResult(emptyModel, emptyPayload, exportedAtUtc, warnings, snapshot);
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

        jsonStream.Position = 0;
        var payload = destination.FilePath is not null
            ? ModelJsonPayload.FromFile(destination.FilePath)
            : ModelJsonPayload.FromStream(jsonStream);
        var result = new ModelExtractionResult(modelResult.Value, payload, exportedAtUtc, warnings, snapshot);
        _logger.LogInformation(
            "Model extraction finished in {TotalDurationMs} ms (metadata: {MetadataMs} ms, deserialize: {DeserializeMs} ms).",
            metadataTimer.Elapsed.TotalMilliseconds + deserializeTimer.Elapsed.TotalMilliseconds,
            metadataTimer.Elapsed.TotalMilliseconds,
            deserializeTimer.Elapsed.TotalMilliseconds);

        return Result<ModelExtractionResult>.Success(result);
    }

    private MetadataRowSnapshot? TryGetFailureSnapshot()
    {
        if (_metadataReader is IMetadataSnapshotDiagnostics diagnostics)
        {
            return diagnostics.LastFailureRowSnapshot;
        }

        return null;
    }

    private static DestinationScope CreateDestination(ModelExtractionOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (options.DestinationStream is { } stream)
        {
            if (!stream.CanWrite)
            {
                throw new ArgumentException("Destination stream must be writable.", nameof(options));
            }

            if (!stream.CanSeek)
            {
                throw new ArgumentException("Destination stream must support seeking.", nameof(options));
            }

            stream.SetLength(0);
            stream.Position = 0;
            return new DestinationScope(stream, filePath: null, dispose: false);
        }

        if (!string.IsNullOrWhiteSpace(options.DestinationPath))
        {
            var trimmed = options.DestinationPath!.Trim();
            var absolutePath = Path.GetFullPath(trimmed);
            var directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var fileStream = new FileStream(absolutePath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            fileStream.SetLength(0);
            fileStream.Position = 0;
            return new DestinationScope(fileStream, absolutePath, dispose: true);
        }

        var memoryStream = new MemoryStream();
        return new DestinationScope(memoryStream, filePath: null, dispose: false);
    }

    private readonly struct DestinationScope : IAsyncDisposable
    {
        private readonly bool _dispose;

        public DestinationScope(Stream stream, string? filePath, bool dispose)
        {
            Stream = stream ?? throw new ArgumentNullException(nameof(stream));
            FilePath = filePath;
            _dispose = dispose;
        }

        public Stream Stream { get; }

        public string? FilePath { get; }

        public ValueTask DisposeAsync()
        {
            return _dispose ? Stream.DisposeAsync() : ValueTask.CompletedTask;
        }
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

}

public sealed class ModelExtractionResult
{
    public ModelExtractionResult(
        OsmModel model,
        ModelJsonPayload jsonPayload,
        DateTimeOffset extractedAtUtc,
        IReadOnlyList<string> warnings,
        OutsystemsMetadataSnapshot metadata)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        JsonPayload = jsonPayload ?? throw new ArgumentNullException(nameof(jsonPayload));
        ExtractedAtUtc = extractedAtUtc;
        Warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    public OsmModel Model { get; }

    public ModelJsonPayload JsonPayload { get; }

    public DateTimeOffset ExtractedAtUtc { get; }

    public IReadOnlyList<string> Warnings { get; }

    public OutsystemsMetadataSnapshot Metadata { get; }
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
