using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli.Commands.Binders;
using Osm.Dmm;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;

namespace Osm.Cli.Commands;

internal sealed class DmmCompareCommandFactory : ICommandFactory
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CliGlobalOptions _globalOptions;
    private readonly ModuleFilterOptionBinder _moduleFilterBinder;
    private readonly CacheOptionBinder _cacheOptionBinder;
    private readonly SqlOptionBinder _sqlOptionBinder;

    private readonly Option<string?> _modelOption = new("--model", "Path to the model JSON file.");
    private readonly Option<string?> _profileOption = new("--profile", "Path to the profiling snapshot.");
    private readonly Option<string?> _dmmOption = new("--dmm", "Path to the baseline DMM script.");
    private readonly Option<string?> _outputOption = new("--out", () => "out", "Output directory for comparison artifacts.");

    public DmmCompareCommandFactory(
        IServiceScopeFactory scopeFactory,
        CliGlobalOptions globalOptions,
        ModuleFilterOptionBinder moduleFilterBinder,
        CacheOptionBinder cacheOptionBinder,
        SqlOptionBinder sqlOptionBinder)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _globalOptions = globalOptions ?? throw new ArgumentNullException(nameof(globalOptions));
        _moduleFilterBinder = moduleFilterBinder ?? throw new ArgumentNullException(nameof(moduleFilterBinder));
        _cacheOptionBinder = cacheOptionBinder ?? throw new ArgumentNullException(nameof(cacheOptionBinder));
        _sqlOptionBinder = sqlOptionBinder ?? throw new ArgumentNullException(nameof(sqlOptionBinder));
    }

    public Command Create()
    {
        var command = new Command("dmm-compare", "Compare the emitted SSDT artifacts with a DMM baseline.")
        {
            _modelOption,
            _profileOption,
            _dmmOption,
            _outputOption,
            _globalOptions.MaxDegreeOfParallelism
        };

        command.AddGlobalOption(_globalOptions.ConfigPath);
        CommandOptionBuilder.AddModuleFilterOptions(command, _moduleFilterBinder);
        CommandOptionBuilder.AddCacheOptions(command, _cacheOptionBinder);
        CommandOptionBuilder.AddSqlOptions(command, _sqlOptionBinder);

        command.SetHandler(async context => await ExecuteAsync(context).ConfigureAwait(false));
        return command;
    }

    private async Task ExecuteAsync(InvocationContext context)
    {
        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var configurationService = services.GetRequiredService<ICliConfigurationService>();
        var application = services.GetRequiredService<IApplicationService<CompareWithDmmApplicationInput, CompareWithDmmApplicationResult>>();

        var cancellationToken = context.GetCancellationToken();
        var configPath = context.ParseResult.GetValueForOption(_globalOptions.ConfigPath);
        var configurationResult = await configurationService.LoadAsync(configPath, cancellationToken).ConfigureAwait(false);
        if (configurationResult.IsFailure)
        {
            CommandConsole.WriteErrors(context.Console, configurationResult.Errors);
            context.ExitCode = 1;
            return;
        }

        var moduleFilter = _moduleFilterBinder.Bind(context.ParseResult);
        var cache = _cacheOptionBinder.Bind(context.ParseResult);
        var sqlOverrides = _sqlOptionBinder.Bind(context.ParseResult);

        var overrides = new CompareWithDmmOverrides(
            context.ParseResult.GetValueForOption(_modelOption),
            context.ParseResult.GetValueForOption(_profileOption),
            context.ParseResult.GetValueForOption(_dmmOption),
            context.ParseResult.GetValueForOption(_outputOption),
            context.ParseResult.GetValueForOption(_globalOptions.MaxDegreeOfParallelism));

        var input = new CompareWithDmmApplicationInput(
            configurationResult.Value,
            overrides,
            moduleFilter,
            sqlOverrides,
            cache);

        var result = await application.RunAsync(input, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            CommandConsole.WriteErrors(context.Console, result.Errors);
            context.ExitCode = 1;
            return;
        }

        EmitResults(context, result.Value);
    }

    private void EmitResults(InvocationContext context, CompareWithDmmApplicationResult applicationResult)
    {
        var pipelineResult = applicationResult.PipelineResult;
        CommandConsole.EmitPipelineLog(context.Console, pipelineResult.ExecutionLog);
        CommandConsole.EmitPipelineWarnings(context.Console, pipelineResult.Warnings);

        if (pipelineResult.EvidenceCache is { } cacheResult)
        {
            CommandConsole.WriteLine(context.Console, $"Cached inputs to {cacheResult.CacheDirectory} (key {cacheResult.Manifest.Key}).");
        }

        if (pipelineResult.Comparison.IsMatch)
        {
            CommandConsole.WriteLine(context.Console, $"DMM parity confirmed. Diff artifact written to {applicationResult.DiffOutputPath}.");
            context.ExitCode = 0;
            return;
        }

        if (pipelineResult.Comparison.ModelDifferences.Count > 0)
        {
            CommandConsole.WriteErrorLine(context.Console, "Model requires additional SSDT coverage:");
            foreach (var difference in pipelineResult.Comparison.ModelDifferences)
            {
                CommandConsole.WriteErrorLine(context.Console, $" - {FormatDifference(difference)}");
            }
        }

        if (pipelineResult.Comparison.SsdtDifferences.Count > 0)
        {
            CommandConsole.WriteErrorLine(context.Console, "SSDT scripts drift from OutSystems model:");
            foreach (var difference in pipelineResult.Comparison.SsdtDifferences)
            {
                CommandConsole.WriteErrorLine(context.Console, $" - {FormatDifference(difference)}");
            }
        }

        CommandConsole.WriteErrorLine(context.Console, $"Diff artifact written to {applicationResult.DiffOutputPath}.");
        context.ExitCode = 2;
    }

    private static string FormatDifference(DmmDifference difference)
    {
        if (difference is null)
        {
            return string.Empty;
        }

        var scopeParts = new System.Collections.Generic.List<string>(capacity: 3);
        if (!string.IsNullOrWhiteSpace(difference.Schema))
        {
            scopeParts.Add(difference.Schema);
        }

        if (!string.IsNullOrWhiteSpace(difference.Table))
        {
            scopeParts.Add(difference.Table);
        }

        var scope = scopeParts.Count > 0 ? string.Join('.', scopeParts) : "artifact";

        if (!string.IsNullOrWhiteSpace(difference.Column))
        {
            scope += $".{difference.Column}";
        }
        else if (!string.IsNullOrWhiteSpace(difference.Index))
        {
            scope += $" [Index: {difference.Index}]";
        }
        else if (!string.IsNullOrWhiteSpace(difference.ForeignKey))
        {
            scope += $" [FK: {difference.ForeignKey}]";
        }

        var property = string.IsNullOrWhiteSpace(difference.Property) ? "Difference" : difference.Property;
        var expected = difference.Expected ?? "<none>";
        var actual = difference.Actual ?? "<none>";

        var message = $"{scope} – {property} expected {expected} actual {actual}";
        if (!string.IsNullOrWhiteSpace(difference.ArtifactPath))
        {
            message += $" ({difference.ArtifactPath})";
        }

        return message;
    }
}
