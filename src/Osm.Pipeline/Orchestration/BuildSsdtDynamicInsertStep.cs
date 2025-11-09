using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Emission;
using Osm.Emission.Seeds;
using Osm.Smo;

namespace Osm.Pipeline.Orchestration;

public sealed class BuildSsdtDynamicInsertStep : IBuildSsdtStep<StaticSeedsGenerated, DynamicInsertsGenerated>
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly DynamicEntityInsertGenerator _generator;

    public BuildSsdtDynamicInsertStep(DynamicEntityInsertGenerator generator)
    {
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
    }

    public async Task<Result<DynamicInsertsGenerated>> ExecuteAsync(
        StaticSeedsGenerated state,
        CancellationToken cancellationToken = default)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var dataset = state.Request.DynamicDataset ?? DynamicEntityDataset.Empty;
        if (dataset.IsEmpty)
        {
            state.Log.Record(
                "dynamicData.insert.skipped",
                "No dynamic dataset was provided; INSERT scripts were not generated.");

            return Result<DynamicInsertsGenerated>.Success(new DynamicInsertsGenerated(
                state.Request,
                state.Log,
                state.Bootstrap,
                state.EvidenceCache,
                state.Decisions,
                state.Report,
                state.Opportunities,
                state.Validations,
                state.Insights,
                state.Manifest,
                state.DecisionLogPath,
                state.OpportunityArtifacts,
                state.SqlValidation,
                state.StaticSeedScriptPaths,
                state.StaticSeedData,
                ImmutableArray<string>.Empty,
                state.StaticSeedTopologicalOrderApplied,
                DynamicInsertTopologicalOrderApplied: false));
        }

        var scripts = _generator.GenerateScripts(
            dataset,
            state.StaticSeedData,
            model: state.Bootstrap.FilteredModel);
        var dynamicOrderApplied = state.Bootstrap.FilteredModel is not null && !scripts.IsDefaultOrEmpty;
        if (scripts.IsDefaultOrEmpty || scripts.Length == 0)
        {
            state.Log.Record(
                "dynamicData.insert.skipped",
                "Dynamic dataset did not contain rows outside of the static seed catalog. No INSERT scripts emitted.");

            return Result<DynamicInsertsGenerated>.Success(new DynamicInsertsGenerated(
                state.Request,
                state.Log,
                state.Bootstrap,
                state.EvidenceCache,
                state.Decisions,
                state.Report,
                state.Opportunities,
                state.Validations,
                state.Insights,
                state.Manifest,
                state.DecisionLogPath,
                state.OpportunityArtifacts,
                state.SqlValidation,
                state.StaticSeedScriptPaths,
                state.StaticSeedData,
                ImmutableArray<string>.Empty,
                state.StaticSeedTopologicalOrderApplied,
                DynamicInsertTopologicalOrderApplied: dynamicOrderApplied));
        }

        var outputRoot = state.Request.DynamicDataOutputDirectoryHint;
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            outputRoot = Path.Combine(state.Request.OutputDirectory, "DynamicData");
        }

        Directory.CreateDirectory(outputRoot!);

        var sanitizeModules = state.Request.Scope.SmoOptions.SanitizeModuleNames;
        var recordedModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scriptPaths = ImmutableArray.CreateBuilder<string>(scripts.Length);

        foreach (var script in scripts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var moduleName = script.Definition.Module ?? "<unknown>";
            var directoryName = sanitizeModules ? ModuleNameSanitizer.Sanitize(moduleName) : moduleName;
            if (!recordedModules.Add(directoryName))
            {
                var suffix = 2;
                var baseName = directoryName;
                while (!recordedModules.Add(directoryName = $"{baseName}_{suffix}"))
                {
                    suffix++;
                }

                if (sanitizeModules)
                {
                    state.Log.Record(
                        "dynamicData.insert.moduleNameRemapped",
                        $"Sanitized module name '{baseName}' for '{moduleName}' collided with another module. Remapped to '{directoryName}'.",
                        new PipelineLogMetadataBuilder()
                            .WithValue("module.originalName", moduleName)
                            .WithValue("module.sanitizedName", baseName)
                            .WithValue("module.disambiguatedName", directoryName)
                            .Build());
                }
            }

            var moduleDirectory = Path.Combine(outputRoot!, directoryName);
            Directory.CreateDirectory(moduleDirectory);

            var fileName = $"{script.Definition.PhysicalName}.dynamic.sql";
            var filePath = Path.Combine(moduleDirectory, fileName);
            await File.WriteAllTextAsync(filePath, script.Script, Utf8NoBom, cancellationToken).ConfigureAwait(false);
            scriptPaths.Add(filePath);
        }

        state.Log.Record(
            "dynamicData.insert.generated",
            "Generated dynamic entity INSERT scripts.",
            new PipelineLogMetadataBuilder()
                .WithValue(
                    "outputs.dynamicInsertPaths",
                    scriptPaths.Count == 0 ? string.Empty : string.Join(";", scriptPaths))
                .WithCount("tables", scripts.Length)
                .Build());

        return Result<DynamicInsertsGenerated>.Success(new DynamicInsertsGenerated(
            state.Request,
            state.Log,
            state.Bootstrap,
            state.EvidenceCache,
            state.Decisions,
            state.Report,
            state.Opportunities,
            state.Validations,
            state.Insights,
            state.Manifest,
            state.DecisionLogPath,
            state.OpportunityArtifacts,
            state.SqlValidation,
            state.StaticSeedScriptPaths,
            state.StaticSeedData,
            scriptPaths.ToImmutable(),
            state.StaticSeedTopologicalOrderApplied,
            DynamicInsertTopologicalOrderApplied: dynamicOrderApplied));
    }
}
