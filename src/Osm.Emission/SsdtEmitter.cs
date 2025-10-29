using System;
using System.Collections.Generic;
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
    private readonly TableEmissionPlanner _planner;
    private readonly ITablePlanWriter _planWriter;
    private readonly ManifestBuilder _manifestBuilder;
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
        : this(
            CreatePlanner(perTableWriter, fileSystem),
            new TablePlanWriter(fileSystem),
            new ManifestBuilder(),
            fileSystem)
    {
    }

    internal SsdtEmitter(
        TableEmissionPlanner planner,
        ITablePlanWriter planWriter,
        ManifestBuilder manifestBuilder,
        IFileSystem fileSystem)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _planWriter = planWriter ?? throw new ArgumentNullException(nameof(planWriter));
        _manifestBuilder = manifestBuilder ?? throw new ArgumentNullException(nameof(manifestBuilder));
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
        var columnCount = model.Tables.Sum(static table => table.Columns.Length);
        var constraintCount = model.Tables.Sum(static table => table.Indexes.Length + table.ForeignKeys.Length);

        var plans = await _planner.PlanAsync(model, outputDirectory, options, cancellationToken).ConfigureAwait(false);
        await _planWriter.WriteAsync(plans, options.ModuleParallelism, cancellationToken).ConfigureAwait(false);

        var manifestEntries = new List<TableManifestEntry>(plans.Count);
        for (var i = 0; i < plans.Count; i++)
        {
            manifestEntries.Add(plans[i].ManifestEntry);
        }

        var manifest = _manifestBuilder.Build(
            manifestEntries,
            options,
            emission,
            decisionReport,
            preRemediation,
            coverage,
            predicateCoverage,
            unsupported,
            tableCount,
            columnCount,
            constraintCount);

        await WriteManifestAsync(outputDirectory, manifest, cancellationToken).ConfigureAwait(false);

        return manifest;
    }

    private async Task WriteManifestAsync(string outputDirectory, SsdtManifest manifest, CancellationToken cancellationToken)
    {
        var manifestPath = _fileSystem.Path.Combine(outputDirectory, "manifest.json");
        var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await _fileSystem.File.WriteAllTextAsync(manifestPath, manifestJson, Utf8NoBom, cancellationToken).ConfigureAwait(false);
    }

    private static TableEmissionPlanner CreatePlanner(PerTableWriter perTableWriter, IFileSystem fileSystem)
    {
        if (perTableWriter is null)
        {
            throw new ArgumentNullException(nameof(perTableWriter));
        }

        if (fileSystem is null)
        {
            throw new ArgumentNullException(nameof(fileSystem));
        }

        return new TableEmissionPlanner(perTableWriter, new TableHeaderFactory(), fileSystem);
    }
}
