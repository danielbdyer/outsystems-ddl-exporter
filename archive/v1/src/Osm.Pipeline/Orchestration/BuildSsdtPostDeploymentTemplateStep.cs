using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;

namespace Osm.Pipeline.Orchestration;

/// <summary>
/// Generates PostDeployment-Bootstrap.sql template for SSDT projects.
/// This template includes guard logic to conditionally apply bootstrap snapshot on first deployment.
/// </summary>
public sealed class BuildSsdtPostDeploymentTemplateStep : IBuildSsdtStep<BootstrapSnapshotGenerated, PostDeploymentTemplateGenerated>
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    public async Task<Result<PostDeploymentTemplateGenerated>> ExecuteAsync(
        BootstrapSnapshotGenerated state,
        CancellationToken cancellationToken = default)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Generate PostDeployment template
        var templateScript = GeneratePostDeploymentTemplate(state);

        // Write to output directory root
        var templatePath = Path.Combine(state.Request.OutputDirectory, "PostDeployment-Bootstrap.sql");
        await File.WriteAllTextAsync(templatePath, templateScript, Utf8NoBom, cancellationToken)
            .ConfigureAwait(false);

        // Log template generation
        state.Log.Record(
            "postDeployment.template.generated",
            "Generated PostDeployment bootstrap template for SSDT",
            new PipelineLogMetadataBuilder()
                .WithPath("postDeployment.template.path", templatePath)
                .WithCount("baselineSeedFiles", state.StaticSeedScriptPaths.Length)
                .WithValue("hasBootstrapSnapshot", state.BootstrapSnapshotPath != null ? "true" : "false")
                .Build());

        return Result<PostDeploymentTemplateGenerated>.Success(new PostDeploymentTemplateGenerated(
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
            state.SqlProjectPath,
            state.SqlValidation,
            state.StaticSeedScriptPaths,
            state.StaticSeedData,
            state.DynamicInsertScriptPaths,
            state.DynamicInsertOutputMode,
            state.StaticSeedTopologicalOrderApplied,
            state.StaticSeedOrderingMode,
            state.DynamicInsertTopologicalOrderApplied,
            state.DynamicInsertOrderingMode,
            state.BootstrapSnapshotPath,
            state.BootstrapTopologicalOrderApplied,
            state.BootstrapOrderingMode,
            state.BootstrapEntityCount,
            PostDeploymentTemplatePath: templatePath));
    }

    private string GeneratePostDeploymentTemplate(BootstrapSnapshotGenerated state)
    {
        var builder = new StringBuilder();

        // Header
        builder.AppendLine("--------------------------------------------------------------------------------");
        builder.AppendLine("-- PostDeployment Bootstrap Script");
        builder.AppendLine($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        builder.AppendLine("-- Usage: Copy this file to your SSDT project's PostDeployment folder");
        builder.AppendLine("--------------------------------------------------------------------------------");
        builder.AppendLine();

        // Guard logic: Only apply bootstrap on first deployment
        if (!state.StaticSeedData.IsDefaultOrEmpty && state.BootstrapSnapshotPath != null)
        {
            var canonicalTable = state.StaticSeedData.FirstOrDefault();
            if (canonicalTable != null)
            {
                var schema = canonicalTable.Definition.Schema;
                var tableName = canonicalTable.Definition.PhysicalName;

                builder.AppendLine("-- Guard: Only apply bootstrap snapshot on first deployment");
                builder.AppendLine($"IF NOT EXISTS (SELECT 1 FROM [{schema}].[{tableName}])");
                builder.AppendLine("BEGIN");
                builder.AppendLine("    PRINT 'First deployment detected - applying bootstrap snapshot';");
                builder.AppendLine("    PRINT 'Loading: Bootstrap/AllEntitiesIncludingStatic.bootstrap.sql';");
                builder.AppendLine();
                builder.AppendLine("    :r Bootstrap\\AllEntitiesIncludingStatic.bootstrap.sql");
                builder.AppendLine();
                builder.AppendLine($"    PRINT 'Bootstrap snapshot applied successfully ({state.BootstrapEntityCount} entities)';");
                builder.AppendLine("END");
                builder.AppendLine("ELSE");
                builder.AppendLine("BEGIN");
                builder.AppendLine("    PRINT 'Existing deployment detected - skipping bootstrap snapshot';");
                builder.AppendLine("END");
                builder.AppendLine("GO");
                builder.AppendLine();
            }
        }

        // Baseline Seeds section
        builder.AppendLine("--------------------------------------------------------------------------------");
        builder.AppendLine("-- Baseline Seeds (Static Entities) - Applied on every deployment");
        builder.AppendLine("--------------------------------------------------------------------------------");

        var seedPaths = state.StaticSeedScriptPaths;
        if (!seedPaths.IsDefaultOrEmpty)
        {
            if (seedPaths.Length == 1)
            {
                // Single file mode
                builder.AppendLine("PRINT 'Applying baseline seeds (static entities)';");
                builder.AppendLine();
                var relativePath = GetRelativePathFromOutput(seedPaths[0], state.Request.OutputDirectory);
                builder.AppendLine($":r {relativePath.Replace("/", "\\")}");
            }
            else
            {
                // Multiple files (per-module mode or per-table mode)
                builder.AppendLine($"PRINT 'Applying baseline seeds from {seedPaths.Length} files';");
                builder.AppendLine();

                foreach (var seedPath in seedPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    var relativePath = GetRelativePathFromOutput(seedPath, state.Request.OutputDirectory);
                    var fileName = Path.GetFileName(relativePath);
                    var directoryPart = Path.GetDirectoryName(relativePath);

                    if (!string.IsNullOrEmpty(directoryPart))
                    {
                        var moduleName = Path.GetFileName(directoryPart);
                        builder.AppendLine($"-- {moduleName}/{fileName}");
                    }
                    else
                    {
                        builder.AppendLine($"-- {fileName}");
                    }

                    builder.AppendLine($":r {relativePath.Replace("/", "\\")}");
                    builder.AppendLine();
                }
            }

            builder.AppendLine("PRINT 'Baseline seeds applied successfully';");
        }
        else
        {
            builder.AppendLine("-- No baseline seeds emitted");
        }

        builder.AppendLine("GO");

        return builder.ToString();
    }

    private static string GetRelativePathFromOutput(string filePath, string outputDirectory)
    {
        var absoluteFile = Path.GetFullPath(filePath);
        var absoluteOutput = Path.GetFullPath(outputDirectory);
        var relativePath = Path.GetRelativePath(absoluteOutput, absoluteFile);

        // If the path goes outside the output directory, just use the file name
        if (relativePath.StartsWith("..", StringComparison.Ordinal))
        {
            return Path.GetFileName(absoluteFile);
        }

        return relativePath;
    }
}
