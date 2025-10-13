using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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
    private readonly IAdvancedSqlExecutor _executor;
    private readonly IModelJsonDeserializer _deserializer;
    private readonly ILogger<SqlModelExtractionService> _logger;

    public SqlModelExtractionService(
        IAdvancedSqlExecutor executor,
        IModelJsonDeserializer deserializer,
        ILogger<SqlModelExtractionService>? logger = null)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
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

        var sqlTimer = Stopwatch.StartNew();
        var jsonResult = await _executor.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        sqlTimer.Stop();

        if (jsonResult.IsFailure)
        {
            _logger.LogError(
                "Advanced SQL execution failed after {DurationMs} ms with errors: {Errors}.",
                sqlTimer.Elapsed.TotalMilliseconds,
                string.Join(", ", jsonResult.Errors.Select(static error => error.Code)));
            return Result<ModelExtractionResult>.Failure(jsonResult.Errors.ToArray());
        }

        var json = jsonResult.Value;
        _logger.LogInformation(
            "Advanced SQL execution succeeded in {DurationMs} ms with payload length {PayloadLength} characters.",
            sqlTimer.Elapsed.TotalMilliseconds,
            json?.Length ?? 0);

        if (string.IsNullOrWhiteSpace(json))
        {
            _logger.LogError("Advanced SQL execution returned no JSON payload.");
            return Result<ModelExtractionResult>.Failure(ValidationError.Create("extraction.sql.emptyJson", "Advanced SQL returned no JSON payload."));
        }

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var warnings = new List<string>();

        var deserializeTimer = Stopwatch.StartNew();
        var modelResult = _deserializer.Deserialize(stream, warnings);
        deserializeTimer.Stop();

        if (modelResult.IsFailure)
        {
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

        var result = new ModelExtractionResult(modelResult.Value, json, DateTimeOffset.UtcNow, warnings);
        _logger.LogInformation(
            "Model extraction finished in {TotalDurationMs} ms.",
            sqlTimer.Elapsed.TotalMilliseconds + deserializeTimer.Elapsed.TotalMilliseconds);

        return Result<ModelExtractionResult>.Success(result);
    }
}

public sealed class ModelExtractionResult
{
    public ModelExtractionResult(OsmModel model, string json, DateTimeOffset extractedAtUtc, IReadOnlyList<string> warnings)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        Json = json ?? throw new ArgumentNullException(nameof(json));
        ExtractedAtUtc = extractedAtUtc;
        Warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));
    }

    public OsmModel Model { get; }

    public string Json { get; }

    public DateTimeOffset ExtractedAtUtc { get; }

    public IReadOnlyList<string> Warnings { get; }
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
