using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Osm.Smo;
using Osm.Validation.Tightening;

namespace Osm.Emission;

public sealed class SsdtEmitter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly PerTableWriter _perTableWriter;
    private readonly IFileSystem _fileSystem;

    public SsdtEmitter()
        : this(new PerTableWriter(), new FileSystem())
    {
    }

    public SsdtEmitter(PerTableWriter perTableWriter)
        : this(perTableWriter, new FileSystem())
    {
    }

    public SsdtEmitter(PerTableWriter perTableWriter, IFileSystem fileSystem)
    {
        _perTableWriter = perTableWriter ?? throw new ArgumentNullException(nameof(perTableWriter));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public async Task<SsdtManifest> EmitAsync(
        SmoModel model,
        string outputDirectory,
        SmoBuildOptions options,
        SsdtEmissionMetadata emission,
        PolicyDecisionReport? decisionReport = null,
        IReadOnlyList<PreRemediationManifestEntry>? preRemediation = null,
        SsdtCoverageSummary? coverage = null,
        SsdtPredicateCoverage? predicateCoverage = null,
        IReadOnlyList<string>? unsupported = null,
        CancellationToken cancellationToken = default)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory must be provided.", nameof(outputDirectory));
        }

        cancellationToken.ThrowIfCancellationRequested();

        _fileSystem.Directory.CreateDirectory(outputDirectory);
        _fileSystem.Directory.CreateDirectory(_fileSystem.Path.Combine(outputDirectory, "Modules"));
        var tableCount = model.Tables.Length;
        var moduleDirectories = new ConcurrentDictionary<string, ModuleDirectoryPaths>(StringComparer.Ordinal);
        var manifestEntries = new List<TableManifestEntry>(tableCount);

        var renameLookup = ImmutableDictionary<string, SmoRenameMapping>.Empty;
        if (!model.Tables.IsDefaultOrEmpty)
        {
            if (options.Header.Enabled)
            {
                var renameLens = new SmoRenameLens();
                var renameEntries = renameLens.Project(new SmoRenameLensRequest(model, options.NamingOverrides));
                if (!renameEntries.IsDefaultOrEmpty)
                {
                    var lookupBuilder = ImmutableDictionary.CreateBuilder<string, SmoRenameMapping>(StringComparer.OrdinalIgnoreCase);
                    foreach (var entry in renameEntries)
                    {
                        lookupBuilder[SchemaTableKey(entry.Schema, entry.PhysicalName)] = entry;
                    }

                    renameLookup = lookupBuilder.ToImmutable();
                }
            }

            var tablePlans = new TableEmissionPlan[tableCount];

            if (options.ModuleParallelism <= 1)
            {
                for (var i = 0; i < tableCount; i++)
                {
                    tablePlans[i] = PlanTableEmission(
                        model.Tables[i],
                        outputDirectory,
                        options,
                        moduleDirectories,
                        renameLookup,
                        cancellationToken);
                }
            }
            else
            {
                var parallelOptions = new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = Math.Max(1, options.ModuleParallelism),
                };

                await Parallel.ForEachAsync(
                    Enumerable.Range(0, tableCount),
                    parallelOptions,
                    (index, ct) =>
                    {
                        tablePlans[index] = PlanTableEmission(
                            model.Tables[index],
                            outputDirectory,
                            options,
                            moduleDirectories,
                            renameLookup,
                            ct);

                        return ValueTask.CompletedTask;
                    }).ConfigureAwait(false);
            }

            await WriteTablesAsync(tablePlans, options.ModuleParallelism, cancellationToken).ConfigureAwait(false);
            for (var i = 0; i < tablePlans.Length; i++)
            {
                manifestEntries.Add(tablePlans[i].ManifestEntry);
            }
        }

        SsdtPolicySummary? summary = null;
        if (decisionReport is not null)
        {
            summary = new SsdtPolicySummary(
                decisionReport.ColumnCount,
                decisionReport.TightenedColumnCount,
                decisionReport.RemediationColumnCount,
                decisionReport.UniqueIndexCount,
                decisionReport.UniqueIndexesEnforcedCount,
                decisionReport.UniqueIndexesRequireRemediationCount,
                decisionReport.ForeignKeyCount,
                decisionReport.ForeignKeysCreatedCount,
                decisionReport.ColumnRationaleCounts,
                decisionReport.UniqueIndexRationaleCounts,
                decisionReport.ForeignKeyRationaleCounts,
                decisionReport.ModuleRollups,
                decisionReport.Toggles.ToExportDictionary());
        }

        var preRemediationEntries = preRemediation ?? Array.Empty<PreRemediationManifestEntry>();
        var coverageSummary = coverage ?? SsdtCoverageSummary.CreateComplete(
            model.Tables.Length,
            model.Tables.Sum(static table => table.Columns.Length),
            model.Tables.Sum(static table => table.Indexes.Length + table.ForeignKeys.Length));
        var unsupportedEntries = unsupported ?? Array.Empty<string>();

        var manifest = new SsdtManifest(
            manifestEntries,
            new SsdtManifestOptions(
                options.IncludePlatformAutoIndexes,
                options.EmitBareTableOnly,
                options.SanitizeModuleNames,
                options.ModuleParallelism),
            summary,
            emission,
            preRemediationEntries,
            coverageSummary,
            predicateCoverage ?? SsdtPredicateCoverage.Empty,
            unsupportedEntries);

        var manifestPath = _fileSystem.Path.Combine(outputDirectory, "manifest.json");
        var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await _fileSystem.File.WriteAllTextAsync(manifestPath, manifestJson, Utf8NoBom, cancellationToken).ConfigureAwait(false);

        return manifest;
    }

    private TableEmissionPlan PlanTableEmission(
        SmoTableDefinition table,
        string outputDirectory,
        SmoBuildOptions options,
        ConcurrentDictionary<string, ModuleDirectoryPaths> moduleDirectories,
        ImmutableDictionary<string, SmoRenameMapping> renameLookup,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var modulePaths = EnsureModuleDirectories(moduleDirectories, outputDirectory, table.Module);
        IReadOnlyList<PerTableHeaderItem>? headerItems = null;
        if (options.Header.Enabled)
        {
            var builder = ImmutableArray.CreateBuilder<PerTableHeaderItem>();
            builder.Add(PerTableHeaderItem.Create("LogicalName", table.LogicalName));
            builder.Add(PerTableHeaderItem.Create("Module", table.Module));

            if (!renameLookup.IsEmpty &&
                renameLookup.TryGetValue(SchemaTableKey(table.Schema, table.Name), out var mapping))
            {
                if (!string.Equals(mapping.EffectiveName, table.Name, StringComparison.OrdinalIgnoreCase))
                {
                    builder.Add(PerTableHeaderItem.Create("RenamedFrom", $"{table.Schema}.{table.Name}"));
                    builder.Add(PerTableHeaderItem.Create("EffectiveName", mapping.EffectiveName));
                }

                if (!string.Equals(mapping.OriginalModule, mapping.Module, StringComparison.Ordinal))
                {
                    builder.Add(PerTableHeaderItem.Create("OriginalModule", mapping.OriginalModule));
                }
            }

            headerItems = builder.ToImmutable();
        }

        var result = _perTableWriter.Generate(table, options, headerItems, cancellationToken);

        var tablesRoot = modulePaths.TablesRoot;
        var tableFilePath = _fileSystem.Path.Combine(tablesRoot, $"{table.Schema}.{result.EffectiveTableName}.sql");
        var indexNames = result.IndexNames.IsDefaultOrEmpty
            ? ImmutableArray<string>.Empty
            : result.IndexNames;

        var foreignKeyNames = result.ForeignKeyNames.IsDefaultOrEmpty
            ? ImmutableArray<string>.Empty
            : result.ForeignKeyNames;

        var manifestEntry = new TableManifestEntry(
            table.Module,
            table.Schema,
            result.EffectiveTableName,
            Relativize(tableFilePath, outputDirectory),
            indexNames,
            foreignKeyNames,
            result.IncludesExtendedProperties);

        return new TableEmissionPlan(manifestEntry, tableFilePath, result.Script);
    }

    private async Task WriteTablesAsync(
        IReadOnlyList<TableEmissionPlan> plans,
        int moduleParallelism,
        CancellationToken cancellationToken)
    {
        if (plans.Count == 0)
        {
            return;
        }

        if (moduleParallelism <= 1)
        {
            for (var i = 0; i < plans.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var plan = plans[i];
                _fileSystem.Directory.CreateDirectory(_fileSystem.Path.GetDirectoryName(plan.Path)!);
                await WriteIfChangedAsync(plan.Path, plan.Script, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        var boundedConcurrency = Math.Max(1, moduleParallelism);
        using var semaphore = new SemaphoreSlim(boundedConcurrency, boundedConcurrency);
        var writeTasks = new Task[plans.Count];

        for (var i = 0; i < plans.Count; i++)
        {
            var plan = plans[i];
            writeTasks[i] = WritePlanAsync(plan, semaphore, cancellationToken);
        }

        await Task.WhenAll(writeTasks).ConfigureAwait(false);
    }

    private async Task WritePlanAsync(
        TableEmissionPlan plan,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _fileSystem.Directory.CreateDirectory(_fileSystem.Path.GetDirectoryName(plan.Path)!);
            await WriteIfChangedAsync(plan.Path, plan.Script, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private ModuleDirectoryPaths EnsureModuleDirectories(
        ConcurrentDictionary<string, ModuleDirectoryPaths> cache,
        string outputDirectory,
        string module)
    {
        return cache.GetOrAdd(module, key =>
        {
            var moduleRoot = _fileSystem.Path.Combine(outputDirectory, "Modules", key);
            var tablesRoot = _fileSystem.Path.Combine(moduleRoot, "Tables");
            return new ModuleDirectoryPaths(tablesRoot);
        });
    }

    private async Task WriteIfChangedAsync(string path, string script, CancellationToken cancellationToken)
    {
        var content = script.EndsWith(Environment.NewLine, StringComparison.Ordinal)
            ? script
            : script + Environment.NewLine;

        if (_fileSystem.File.Exists(path))
        {
            var existing = await _fileSystem.File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            if (string.Equals(existing, content, StringComparison.Ordinal))
            {
                return;
            }
        }

        await _fileSystem.File.WriteAllTextAsync(path, content, Utf8NoBom, cancellationToken).ConfigureAwait(false);
    }

    private static string SchemaTableKey(string schema, string table)
        => $"{schema}.{table}";

    private string Relativize(string path, string root)
        => _fileSystem.Path.GetRelativePath(root, path)
            .Replace(_fileSystem.Path.DirectorySeparatorChar, '/');

    private sealed record ModuleDirectoryPaths(string TablesRoot);

    private sealed record TableEmissionPlan(
        TableManifestEntry ManifestEntry,
        string Path,
        string Script);
}
