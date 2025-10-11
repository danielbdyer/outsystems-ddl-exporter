using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
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

    public SsdtEmitter()
        : this(new PerTableWriter())
    {
    }

    public SsdtEmitter(PerTableWriter perTableWriter)
    {
        _perTableWriter = perTableWriter ?? throw new ArgumentNullException(nameof(perTableWriter));
    }

    public async Task<SsdtManifest> EmitAsync(
        SmoModel model,
        string outputDirectory,
        SmoBuildOptions options,
        SsdtEmissionMetadata emission,
        PolicyDecisionReport? decisionReport = null,
        IReadOnlyList<PreRemediationManifestEntry>? preRemediation = null,
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

        Directory.CreateDirectory(outputDirectory);
        var tableCount = model.Tables.Length;
        var moduleDirectories = new ConcurrentDictionary<string, ModuleDirectoryPaths>(StringComparer.Ordinal);
        var manifestEntries = new List<TableManifestEntry>(tableCount);

        if (!model.Tables.IsDefaultOrEmpty)
        {
            var results = new TableManifestEntry[tableCount];

            if (options.ModuleParallelism <= 1)
            {
                for (var i = 0; i < tableCount; i++)
                {
                    results[i] = await EmitTableAsync(
                        model.Tables[i],
                        outputDirectory,
                        options,
                        moduleDirectories,
                        cancellationToken).ConfigureAwait(false);
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
                    async (index, ct) =>
                    {
                        results[index] = await EmitTableAsync(
                            model.Tables[index],
                            outputDirectory,
                            options,
                            moduleDirectories,
                            ct).ConfigureAwait(false);
                    }).ConfigureAwait(false);
            }

            manifestEntries.AddRange(results);
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
                decisionReport.ForeignKeyRationaleCounts);
        }

        var preRemediationEntries = preRemediation ?? Array.Empty<PreRemediationManifestEntry>();

        var manifest = new SsdtManifest(
            manifestEntries,
            new SsdtManifestOptions(
                options.IncludePlatformAutoIndexes,
                options.EmitBareTableOnly,
                options.SanitizeModuleNames,
                options.ModuleParallelism),
            summary,
            emission,
            preRemediationEntries);

        var manifestPath = Path.Combine(outputDirectory, "manifest.json");
        var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(manifestPath, manifestJson, Utf8NoBom, cancellationToken).ConfigureAwait(false);

        return manifest;
    }

    private async Task<TableManifestEntry> EmitTableAsync(
        SmoTableDefinition table,
        string outputDirectory,
        SmoBuildOptions options,
        ConcurrentDictionary<string, ModuleDirectoryPaths> moduleDirectories,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var modulePaths = EnsureModuleDirectories(moduleDirectories, outputDirectory, table.Module);
        var result = _perTableWriter.Generate(table, options, cancellationToken);

        var tablesRoot = modulePaths.TablesRoot;
        var tableFilePath = Path.Combine(tablesRoot, $"{table.Schema}.{result.EffectiveTableName}.sql");
        await WriteIfChangedAsync(tableFilePath, result.Script, cancellationToken).ConfigureAwait(false);

        var indexNames = result.IndexNames.IsDefaultOrEmpty
            ? ImmutableArray<string>.Empty
            : result.IndexNames;

        var foreignKeyNames = result.ForeignKeyNames.IsDefaultOrEmpty
            ? ImmutableArray<string>.Empty
            : result.ForeignKeyNames;

        return new TableManifestEntry(
            table.Module,
            table.Schema,
            result.EffectiveTableName,
            Relativize(tableFilePath, outputDirectory),
            indexNames,
            foreignKeyNames,
            result.IncludesExtendedProperties);
    }

    private static ModuleDirectoryPaths EnsureModuleDirectories(
        ConcurrentDictionary<string, ModuleDirectoryPaths> cache,
        string outputDirectory,
        string module)
    {
        return cache.GetOrAdd(module, key =>
        {
            var moduleRoot = Path.Combine(outputDirectory, "Modules", key);
            var tablesRoot = Path.Combine(moduleRoot, "Tables");
            Directory.CreateDirectory(tablesRoot);

            return new ModuleDirectoryPaths(tablesRoot);
        });
    }

    private static async Task WriteIfChangedAsync(string path, string script, CancellationToken cancellationToken)
    {
        var content = script.EndsWith(Environment.NewLine, StringComparison.Ordinal)
            ? script
            : script + Environment.NewLine;

        if (File.Exists(path))
        {
            var existing = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            if (string.Equals(existing, content, StringComparison.Ordinal))
            {
                return;
            }
        }

        await File.WriteAllTextAsync(path, content, Utf8NoBom, cancellationToken).ConfigureAwait(false);
    }

    private static string Relativize(string path, string root)
    {
        return Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
    }

    private sealed record ModuleDirectoryPaths(string TablesRoot);
}
