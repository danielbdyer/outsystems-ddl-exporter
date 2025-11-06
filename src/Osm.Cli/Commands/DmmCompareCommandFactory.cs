using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli.Commands.Binders;
using Osm.Pipeline.Application;
using Osm.Pipeline.Runtime;
using Osm.Pipeline.Runtime.Verbs;

namespace Osm.Cli.Commands;

internal sealed class DmmCompareCommandFactory : PipelineCommandFactory<DmmCompareVerbOptions, DmmCompareVerbResult>
{
    private readonly CliGlobalOptions _globalOptions;
    private readonly ModuleFilterOptionBinder _moduleFilterBinder;
    private readonly CacheOptionBinder _cacheOptionBinder;
    private readonly SqlOptionBinder _sqlOptionBinder;
    private readonly TighteningOptionBinder _tighteningBinder;

    private readonly Option<string?> _modelOption = new("--model", "Path to the model JSON file.");
    private readonly Option<string?> _profileOption = new("--profile", "Path to the profiling snapshot.");
    private readonly Option<string?> _dmmOption = new("--dmm", "Path to the baseline DMM script.");
    private readonly Option<string?> _outputOption = new("--out", () => "out", "Output directory for comparison artifacts.");

    public DmmCompareCommandFactory(
        IServiceScopeFactory scopeFactory,
        CliGlobalOptions globalOptions,
        ModuleFilterOptionBinder moduleFilterBinder,
        CacheOptionBinder cacheOptionBinder,
        SqlOptionBinder sqlOptionBinder,
        TighteningOptionBinder tighteningOptionBinder)
        : base(scopeFactory)
    {
        _globalOptions = globalOptions ?? throw new ArgumentNullException(nameof(globalOptions));
        _moduleFilterBinder = moduleFilterBinder ?? throw new ArgumentNullException(nameof(moduleFilterBinder));
        _cacheOptionBinder = cacheOptionBinder ?? throw new ArgumentNullException(nameof(cacheOptionBinder));
        _sqlOptionBinder = sqlOptionBinder ?? throw new ArgumentNullException(nameof(sqlOptionBinder));
        _tighteningBinder = tighteningOptionBinder ?? throw new ArgumentNullException(nameof(tighteningOptionBinder));
    }

    protected override string VerbName => DmmCompareVerb.VerbName;

    protected override Command CreateCommandCore()
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
        CommandOptionBuilder.AddTighteningOptions(command, _tighteningBinder);
        return command;
    }

    protected override DmmCompareVerbOptions BindOptions(InvocationContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var parseResult = context.ParseResult;
        var moduleFilter = _moduleFilterBinder.Bind(parseResult);
        var cache = _cacheOptionBinder.Bind(parseResult);
        var sqlOverrides = _sqlOptionBinder.Bind(parseResult);
        var tightening = _tighteningBinder.Bind(parseResult);

        var overrides = new CompareWithDmmOverrides(
            parseResult.GetValueForOption(_modelOption),
            parseResult.GetValueForOption(_profileOption),
            parseResult.GetValueForOption(_dmmOption),
            parseResult.GetValueForOption(_outputOption),
            parseResult.GetValueForOption(_globalOptions.MaxDegreeOfParallelism));

        return new DmmCompareVerbOptions
        {
            ConfigurationPath = parseResult.GetValueForOption(_globalOptions.ConfigPath),
            Overrides = overrides,
            ModuleFilter = moduleFilter,
            Sql = sqlOverrides,
            Cache = cache,
            Tightening = tightening
        };
    }

    protected override Task<int> OnRunSucceededAsync(InvocationContext context, DmmCompareVerbResult payload)
        => Task.FromResult(EmitResults(context, payload));

    private int EmitResults(InvocationContext context, DmmCompareVerbResult payload)
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
            return 0;
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
        return 2;
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
