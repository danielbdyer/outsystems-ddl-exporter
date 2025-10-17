using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;
using Osm.Cli.Commands.Binders;
using Osm.Pipeline.Application;
using Osm.Pipeline.Runtime.Verbs;

namespace Osm.Cli.Commands;

internal sealed class DmmCompareCommandFactory : ICommandFactory
{
    private readonly PipelineVerbExecutor _verbExecutor;
    private readonly CliGlobalOptions _globalOptions;
    private readonly ModuleFilterOptionBinder _moduleFilterBinder;
    private readonly CacheOptionBinder _cacheOptionBinder;
    private readonly SqlOptionBinder _sqlOptionBinder;

    private readonly Option<string?> _modelOption = new("--model", "Path to the model JSON file.");
    private readonly Option<string?> _profileOption = new("--profile", "Path to the profiling snapshot.");
    private readonly Option<string?> _dmmOption = new("--dmm", "Path to the baseline DMM script.");
    private readonly Option<string?> _outputOption = new("--out", () => "out", "Output directory for comparison artifacts.");

    public DmmCompareCommandFactory(
        PipelineVerbExecutor verbExecutor,
        CliGlobalOptions globalOptions,
        ModuleFilterOptionBinder moduleFilterBinder,
        CacheOptionBinder cacheOptionBinder,
        SqlOptionBinder sqlOptionBinder)
    {
        _verbExecutor = verbExecutor ?? throw new ArgumentNullException(nameof(verbExecutor));
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
        var cancellationToken = context.GetCancellationToken();
        var moduleFilter = _moduleFilterBinder.Bind(context.ParseResult);
        var cache = _cacheOptionBinder.Bind(context.ParseResult);
        var sqlOverrides = _sqlOptionBinder.Bind(context.ParseResult);

        var overrides = new CompareWithDmmOverrides(
            context.ParseResult.GetValueForOption(_modelOption),
            context.ParseResult.GetValueForOption(_profileOption),
            context.ParseResult.GetValueForOption(_dmmOption),
            context.ParseResult.GetValueForOption(_outputOption),
            context.ParseResult.GetValueForOption(_globalOptions.MaxDegreeOfParallelism));

        var options = new DmmCompareVerbOptions
        {
            ConfigurationPath = context.ParseResult.GetValueForOption(_globalOptions.ConfigPath),
            Overrides = overrides,
            ModuleFilter = moduleFilter,
            Sql = sqlOverrides,
            Cache = cache
        };

        var execution = await _verbExecutor
            .ExecuteAsync<DmmCompareVerbResult>(context, DmmCompareVerb.VerbName, options, cancellationToken)
            .ConfigureAwait(false);

        if (!execution.IsSuccess || execution.Payload is not { } payload)
        {
            return;
        }

        EmitResults(context, payload);
    }

    private void EmitResults(InvocationContext context, DmmCompareVerbResult payload)
    {
        var applicationResult = payload.ApplicationResult;
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

    private static string FormatDifference(Osm.Dmm.DmmDifference difference)
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
