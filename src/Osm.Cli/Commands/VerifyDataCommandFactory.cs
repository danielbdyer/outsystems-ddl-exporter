using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Runtime;

namespace Osm.Cli.Commands;

internal sealed class VerifyDataCommandFactory : ICommandFactory
{
    private readonly IServiceScopeFactory _scopeFactory;

    private static readonly JsonSerializerOptions ReportSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly Option<string?> _manifestOption = new("--manifest", () => "full-export.manifest.json", "Path to full-export.manifest.json.");
    private readonly Option<string?> _modelOption = new("--model", "Path to model.json (overrides manifest discovery).");
    private readonly Option<string?> _configOption = new("--config", "Pipeline configuration path (defaults to manifest or environment). ");
    private readonly Option<string?> _reportOption = new("--report-out", "Path to write data-integrity-verification.json.");
    private readonly Option<string> _sourceConnectionOption = new("--source-connection", "Source database connection string (QA).")
    {
        IsRequired = true
    };

    private readonly Option<string> _targetConnectionOption = new("--target-connection", "Target database connection string (UAT staging).")
    {
        IsRequired = true
    };

    public VerifyDataCommandFactory(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    public Command Create()
    {
        var command = new Command("verify-data", "Run standalone data integrity checks comparing source and target databases.")
        {
            _manifestOption,
            _modelOption,
            _configOption,
            _reportOption,
            _sourceConnectionOption,
            _targetConnectionOption
        };

        command.SetHandler(async context => await ExecuteAsync(context).ConfigureAwait(false));
        return command;
    }

    private async Task ExecuteAsync(InvocationContext context)
    {
        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var configurationService = services.GetRequiredService<ICliConfigurationService>();
        var ingestionService = services.GetRequiredService<IModelIngestionService>();
        var checker = services.GetRequiredService<BasicDataIntegrityChecker>();

        var manifestPath = context.ParseResult.GetValueForOption(_manifestOption);
        var modelPath = context.ParseResult.GetValueForOption(_modelOption);
        var configPath = context.ParseResult.GetValueForOption(_configOption);
        var reportPath = context.ParseResult.GetValueForOption(_reportOption);
        var sourceConnection = context.ParseResult.GetValueForOption(_sourceConnectionOption);
        var targetConnection = context.ParseResult.GetValueForOption(_targetConnectionOption);

        var manifest = await LoadManifestAsync(manifestPath, context).ConfigureAwait(false);
        modelPath ??= ResolveModelPath(manifest);
        configPath ??= manifest?.ConfigurationPath;

        if (string.IsNullOrWhiteSpace(modelPath))
        {
            CommandConsole.WriteErrorLine(context.Console, "[error] Model path could not be resolved. Use --model or provide a manifest.");
            context.ExitCode = 1;
            return;
        }

        var configResult = await configurationService
            .LoadAsync(configPath, context.GetCancellationToken())
            .ConfigureAwait(false);
        if (configResult.IsFailure)
        {
            CommandConsole.WriteErrors(context.Console, configResult.Errors);
            context.ExitCode = 1;
            return;
        }

        var warnings = new List<string>();
        var modelResult = await ingestionService
            .LoadFromFileAsync(modelPath!, warnings, context.GetCancellationToken())
            .ConfigureAwait(false);

        CommandConsole.EmitPipelineWarnings(context.Console, warnings.ToImmutableArray());

        if (modelResult.IsFailure)
        {
            CommandConsole.WriteErrors(context.Console, modelResult.Errors);
            context.ExitCode = 1;
            return;
        }

        var namingOverrides = configResult.Value.Configuration.Tightening.Emission.NamingOverrides;
        var commandTimeout = configResult.Value.Configuration.Sql.CommandTimeoutSeconds;
        var checkResult = await checker
            .CheckAsync(
                new BasicIntegrityCheckRequest(
                    sourceConnection!,
                    targetConnection!,
                    modelResult.Value,
                    namingOverrides,
                    CommandTimeoutSeconds: commandTimeout),
                context.GetCancellationToken())
            .ConfigureAwait(false);

        var overallStatus = checkResult.Passed ? "PASS" : "WARN";
        var report = new DataIntegrityVerificationReport(
            overallStatus,
            DateTimeOffset.UtcNow,
            checkResult);

        var resolvedReportPath = ResolveReportPath(reportPath, manifestPath);
        await WriteReportAsync(report, resolvedReportPath, context.GetCancellationToken()).ConfigureAwait(false);

        EmitSummary(context, checkResult, resolvedReportPath);
        context.ExitCode = checkResult.Passed ? 0 : 1;
    }

    private static async Task<FullExportRunManifest?> LoadManifestAsync(string? manifestPath, InvocationContext context)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            await using var stream = File.OpenRead(manifestPath);
            return await JsonSerializer.DeserializeAsync<FullExportRunManifest>(stream, options, context.GetCancellationToken())
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            CommandConsole.WriteErrorLine(context.Console, $"[warning] Unable to read manifest at {manifestPath}: {ex.Message}");
            return null;
        }
    }

    private static string? ResolveModelPath(FullExportRunManifest? manifest)
    {
        var extractionStage = manifest?.Stages
            .FirstOrDefault(stage => string.Equals(stage?.Name, "extract-model", StringComparison.OrdinalIgnoreCase));

        if (extractionStage is null)
        {
            return null;
        }

        if (extractionStage.Artifacts.TryGetValue("modelJson", out var modelPath)
            && !string.IsNullOrWhiteSpace(modelPath))
        {
            return modelPath;
        }

        return null;
    }

    private static string ResolveReportPath(string? reportPath, string? manifestPath)
    {
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            return Path.GetFullPath(reportPath!);
        }

        if (!string.IsNullOrWhiteSpace(manifestPath))
        {
            var manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath!));
            if (!string.IsNullOrWhiteSpace(manifestDirectory))
            {
                return Path.Combine(manifestDirectory!, "data-integrity-verification.json");
            }
        }

        return Path.GetFullPath("data-integrity-verification.json");
    }

    private static async Task WriteReportAsync(
        DataIntegrityVerificationReport report,
        string reportPath,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory!);
        }

        await using var stream = new FileStream(
            reportPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await JsonSerializer.SerializeAsync(stream, report, ReportSerializerOptions, cancellationToken).ConfigureAwait(false);
    }

    private static void EmitSummary(
        InvocationContext context,
        BasicIntegrityCheckResult result,
        string reportPath)
    {
        CommandConsole.WriteLine(context.Console, "Data integrity verification:");
        CommandConsole.WriteLine(context.Console, $"  Tables checked: {result.TablesChecked}");
        CommandConsole.WriteLine(context.Console, $"  Row count matches: {result.RowCountMatches}");
        CommandConsole.WriteLine(context.Console, $"  NULL count matches: {result.NullCountMatches}");
        CommandConsole.WriteLine(context.Console, $"  Warnings: {result.Warnings.Length}");
        CommandConsole.WriteLine(context.Console, $"  Report: {reportPath}");

        if (result.Warnings.Length == 0)
        {
            CommandConsole.WriteLine(context.Console, "  ✓ All checks passed.");
            return;
        }

        CommandConsole.WriteLine(context.Console, "  ⚠ Data integrity warnings detected:");
        foreach (var warning in result.Warnings)
        {
            CommandConsole.WriteLine(context.Console, $"    - [{warning.WarningType}] {warning.Message}");
        }
    }
}
