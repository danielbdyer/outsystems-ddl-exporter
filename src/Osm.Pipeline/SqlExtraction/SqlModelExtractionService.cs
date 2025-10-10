using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
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

    public SqlModelExtractionService(IAdvancedSqlExecutor executor, IModelJsonDeserializer deserializer)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _deserializer = deserializer ?? throw new ArgumentNullException(nameof(deserializer));
    }

    public async Task<Result<ModelExtractionResult>> ExtractAsync(ModelExtractionCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        var request = new AdvancedSqlRequest(command.ModuleNames, command.IncludeSystemModules, command.OnlyActiveAttributes);
        var jsonResult = await _executor.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        if (jsonResult.IsFailure)
        {
            return Result<ModelExtractionResult>.Failure(jsonResult.Errors.ToArray());
        }

        var json = jsonResult.Value;
        if (string.IsNullOrWhiteSpace(json))
        {
            return Result<ModelExtractionResult>.Failure(ValidationError.Create("extraction.sql.emptyJson", "Advanced SQL returned no JSON payload."));
        }

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var warnings = new List<string>();
        var modelResult = _deserializer.Deserialize(stream, warnings);
        if (modelResult.IsFailure)
        {
            return Result<ModelExtractionResult>.Failure(modelResult.Errors.ToArray());
        }

        var result = new ModelExtractionResult(modelResult.Value, json, DateTimeOffset.UtcNow, warnings);
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
    private ModelExtractionCommand(IReadOnlyList<string> moduleNames, bool includeSystemModules, bool onlyActiveAttributes)
    {
        ModuleNames = moduleNames;
        IncludeSystemModules = includeSystemModules;
        OnlyActiveAttributes = onlyActiveAttributes;
    }

    public IReadOnlyList<string> ModuleNames { get; }

    public bool IncludeSystemModules { get; }

    public bool OnlyActiveAttributes { get; }

    public static Result<ModelExtractionCommand> Create(IEnumerable<string>? moduleNames, bool includeSystemModules, bool onlyActiveAttributes)
    {
        var normalized = new List<string>();
        if (moduleNames is not null)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in moduleNames)
            {
                if (candidate is null)
                {
                    return Result<ModelExtractionCommand>.Failure(ValidationError.Create("extraction.modules.null", "Module names must not be null."));
                }

                var trimmed = candidate.Trim();
                if (trimmed.Length == 0)
                {
                    return Result<ModelExtractionCommand>.Failure(ValidationError.Create("extraction.modules.empty", "Module names must not be empty or whitespace."));
                }

                if (seen.Add(trimmed))
                {
                    normalized.Add(trimmed);
                }
            }
        }

        normalized.Sort(StringComparer.OrdinalIgnoreCase);
        return Result<ModelExtractionCommand>.Success(new ModelExtractionCommand(normalized, includeSystemModules, onlyActiveAttributes));
    }
}
