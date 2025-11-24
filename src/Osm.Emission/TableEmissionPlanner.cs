using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Configuration;
using Osm.Domain.Model.Artifacts;
using Osm.Smo;

namespace Osm.Emission;

public sealed class TableEmissionPlanner
{
    internal const string ModulesDirectory = "Modules";
    internal const string BareModulesDirectory = "Modules.Bare";

    private readonly PerTableWriter _perTableWriter;
    private readonly TableHeaderFactory _headerFactory;
    private readonly IFileSystem _fileSystem;

    public TableEmissionPlanner(
        PerTableWriter perTableWriter,
        TableHeaderFactory headerFactory,
        IFileSystem fileSystem)
    {
        _perTableWriter = perTableWriter ?? throw new ArgumentNullException(nameof(perTableWriter));
        _headerFactory = headerFactory ?? throw new ArgumentNullException(nameof(headerFactory));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public async Task<IReadOnlyList<TableEmissionPlan>> PlanAsync(
        SmoModel model,
        string outputDirectory,
        SmoBuildOptions options,
        CancellationToken cancellationToken)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory must be provided.", nameof(outputDirectory));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (model.Snapshots.Length != model.Tables.Length)
        {
            throw new InvalidOperationException("SMO tables and snapshots must align.");
        }

        var tableCount = model.Tables.Length;
        if (tableCount == 0)
        {
            return Array.Empty<TableEmissionPlan>();
        }

        var renameLookup = BuildRenameLookup(model, options);

        // For "Both" mode, generate plans for both bare and full variants
        if (options.EmitTableMode == TableEmissionMode.Both)
        {
            var bareOptions = options with { EmitTableMode = TableEmissionMode.BareOnly };
            var fullOptions = options with { EmitTableMode = TableEmissionMode.FullOnly };

            var allPlans = new List<TableEmissionPlan>(tableCount * 2);

            if (options.ModuleParallelism <= 1)
            {
                for (var i = 0; i < tableCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    allPlans.Add(PlanTable(
                        model.Tables[i],
                        model.Snapshots[i],
                        outputDirectory,
                        bareOptions,
                        renameLookup,
                        cancellationToken,
                        BareModulesDirectory));
                    
                    allPlans.Add(PlanTable(
                        model.Tables[i],
                        model.Snapshots[i],
                        outputDirectory,
                        fullOptions,
                        renameLookup,
                        cancellationToken,
                        ModulesDirectory));
                }
            }
            else
            {
                var parallelOptions = new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = Math.Max(1, options.ModuleParallelism),
                };

                var bareFullPairs = new (TableEmissionPlan Bare, TableEmissionPlan Full)[tableCount];

                await Parallel.ForEachAsync(
                    Enumerable.Range(0, tableCount),
                    parallelOptions,
                    (index, ct) =>
                    {
                        var barePlan = PlanTable(
                            model.Tables[index],
                            model.Snapshots[index],
                            outputDirectory,
                            bareOptions,
                            renameLookup,
                            ct,
                            BareModulesDirectory);

                        var fullPlan = PlanTable(
                            model.Tables[index],
                            model.Snapshots[index],
                            outputDirectory,
                            fullOptions,
                            renameLookup,
                            ct,
                            ModulesDirectory);

                        bareFullPairs[index] = (barePlan, fullPlan);
                        return ValueTask.CompletedTask;
                    }).ConfigureAwait(false);

                foreach (var pair in bareFullPairs)
                {
                    allPlans.Add(pair.Bare);
                    allPlans.Add(pair.Full);
                }
            }

            return allPlans;
        }

        // Single mode: BareOnly or FullOnly
        var plans = new TableEmissionPlan[tableCount];

        if (options.ModuleParallelism <= 1)
        {
            for (var i = 0; i < tableCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                plans[i] = PlanTable(
                    model.Tables[i],
                    model.Snapshots[i],
                    outputDirectory,
                    options,
                    renameLookup,
                    cancellationToken,
                    ModulesDirectory);
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
                    plans[index] = PlanTable(
                        model.Tables[index],
                        model.Snapshots[index],
                        outputDirectory,
                        options,
                        renameLookup,
                        ct,
                        ModulesDirectory);

                    return ValueTask.CompletedTask;
                }).ConfigureAwait(false);
        }

        return plans;
    }

    private ImmutableDictionary<string, SmoRenameMapping> BuildRenameLookup(SmoModel model, SmoBuildOptions options)
    {
        if (!options.Header.Enabled)
        {
            return ImmutableDictionary<string, SmoRenameMapping>.Empty;
        }

        var lens = new SmoRenameLens();
        var request = new SmoRenameLensRequest(model, options.NamingOverrides);
        var entries = lens.Project(request);
        if (entries.IsDefaultOrEmpty)
        {
            return ImmutableDictionary<string, SmoRenameMapping>.Empty;
        }

        var builder = ImmutableDictionary.CreateBuilder<string, SmoRenameMapping>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            builder[SchemaTableKey(entry.Schema, entry.PhysicalName)] = entry;
        }

        return builder.ToImmutable();
    }

    private TableEmissionPlan PlanTable(
        SmoTableDefinition table,
        TableArtifactSnapshot snapshot,
        string outputDirectory,
        SmoBuildOptions options,
        ImmutableDictionary<string, SmoRenameMapping> renameLookup,
        CancellationToken cancellationToken,
        string moduleSubdirectory = ModulesDirectory)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var headerItems = _headerFactory.Create(table, options, renameLookup);
        var result = _perTableWriter.Generate(table, options, headerItems, cancellationToken);

        var moduleRoot = _fileSystem.Path.Combine(outputDirectory, moduleSubdirectory, snapshot.Identity.Module);
        var tableFilePath = _fileSystem.Path.Combine(moduleRoot, $"{snapshot.Identity.Schema}.{result.EffectiveTableName}.sql");

        var indexNames = result.IndexNames.IsDefaultOrEmpty
            ? ImmutableArray<string>.Empty
            : result.IndexNames;

        var foreignKeyNames = result.ForeignKeyNames.IsDefaultOrEmpty
            ? ImmutableArray<string>.Empty
            : result.ForeignKeyNames;

        var emissionMetadata = TableArtifactEmissionMetadata.Create(
            result.EffectiveTableName,
            Relativize(tableFilePath, outputDirectory),
            indexNames,
            foreignKeyNames,
            result.IncludesExtendedProperties);

        var enrichedSnapshot = snapshot.WithEmission(emissionMetadata);

        return new TableEmissionPlan(enrichedSnapshot, tableFilePath, result.Script);
    }

    private string Relativize(string path, string root)
        => _fileSystem.Path.GetRelativePath(root, path)
            .Replace(_fileSystem.Path.DirectorySeparatorChar, '/');

    private static string SchemaTableKey(string schema, string table)
        => $"{schema}.{table}";
}
