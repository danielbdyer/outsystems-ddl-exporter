using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.SqlExtraction;

public sealed class AdvancedSqlMetadataOrchestrator
{
    private readonly IOutsystemsMetadataReader _metadataReader;
    private readonly ILogger<AdvancedSqlMetadataOrchestrator> _logger;

    public AdvancedSqlMetadataOrchestrator(
        IOutsystemsMetadataReader metadataReader,
        ILogger<AdvancedSqlMetadataOrchestrator>? logger = null)
    {
        _metadataReader = metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));
        _logger = logger ?? NullLogger<AdvancedSqlMetadataOrchestrator>.Instance;
    }

    public async Task<Result<AdvancedSqlMetadataResult>> ExecuteAsync(
        ModelExtractionCommand command,
        ModelExtractionOptions options,
        CancellationToken cancellationToken = default)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        _logger.LogInformation(
            "Executing advanced SQL for {ModuleCount} module(s) (includeSystem: {IncludeSystem}, includeInactive: {IncludeInactive}, onlyActive: {OnlyActive}).",
            command.ModuleNames.Length,
            command.IncludeSystemModules,
            command.IncludeInactiveModules,
            command.OnlyActiveAttributes);

        if (command.ModuleNames.Length > 0)
        {
            _logger.LogDebug(
                "Advanced SQL module list: {Modules}.",
                string.Join(",", command.ModuleNames.Select(static module => module.Value)));
        }

        var request = new AdvancedSqlRequest(
            command.ModuleNames,
            command.IncludeSystemModules,
            command.IncludeInactiveModules,
            command.OnlyActiveAttributes,
            command.EntityFilters);

        options.MetadataLog?.RecordRequest(
            "advancedSql.request",
            new
            {
                modules = request.ModuleNames.Select(static module => module.Value).ToArray(),
                includeSystem = request.IncludeSystemModules,
                includeInactive = request.IncludeInactiveModules,
                onlyActive = request.OnlyActiveAttributes,
                entityFilters = request.EntityFilters,
            });

        var timer = Stopwatch.StartNew();
        var metadataResult = await _metadataReader.ReadAsync(request, cancellationToken).ConfigureAwait(false);
        timer.Stop();

        if (metadataResult.IsFailure)
        {
            _logger.LogError(
                "Metadata reader failed after {DurationMs} ms with errors: {Errors}.",
                timer.Elapsed.TotalMilliseconds,
                string.Join(", ", metadataResult.Errors.Select(static error => error.Code)));

            options.MetadataLog?.RecordFailure(metadataResult.Errors, TryGetFailureSnapshot());

            return Result<AdvancedSqlMetadataResult>.Failure(metadataResult.Errors);
        }

        var snapshot = metadataResult.Value;
        var modulesWithoutEntities = IdentifyModulesWithoutEntities(snapshot);
        var exportedAtUtc = DateTimeOffset.UtcNow;

        options.MetadataLog?.RecordSnapshot(snapshot, exportedAtUtc);
        options.MetadataLog?.RecordRequest(
            "advancedSql.duration",
            new
            {
                metadataMilliseconds = timer.Elapsed.TotalMilliseconds,
            });

        return Result<AdvancedSqlMetadataResult>.Success(
            new AdvancedSqlMetadataResult(snapshot, modulesWithoutEntities, exportedAtUtc, timer.Elapsed));
    }

    private MetadataRowSnapshot? TryGetFailureSnapshot()
    {
        if (_metadataReader is IMetadataSnapshotDiagnostics diagnostics)
        {
            return diagnostics.LastFailureRowSnapshot;
        }

        return null;
    }

    private static IReadOnlyList<string> IdentifyModulesWithoutEntities(OutsystemsMetadataSnapshot snapshot)
    {
        if (snapshot.Modules.Count == 0)
        {
            return Array.Empty<string>();
        }

        var entitiesByModule = snapshot.Entities
            .GroupBy(static entity => entity.EspaceId)
            .ToDictionary(static group => group.Key, static group => group.Count());

        var result = new List<string>();
        foreach (var module in snapshot.Modules)
        {
            if (!entitiesByModule.TryGetValue(module.EspaceId, out var entityCount) || entityCount == 0)
            {
                result.Add(module.EspaceName);
            }
        }

        return result;
    }
}

public sealed record AdvancedSqlMetadataResult(
    OutsystemsMetadataSnapshot Snapshot,
    IReadOnlyList<string> ModulesWithoutEntities,
    DateTimeOffset ExportedAtUtc,
    TimeSpan MetadataDuration);
