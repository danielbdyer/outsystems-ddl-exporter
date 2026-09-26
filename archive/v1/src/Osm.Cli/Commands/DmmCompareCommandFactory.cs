using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli.Commands.Options;
using Osm.Pipeline.Application;
using Osm.Pipeline.Runtime;
using Osm.Pipeline.Runtime.Verbs;

namespace Osm.Cli.Commands;

internal sealed class DmmCompareCommandFactory : PipelineCommandFactory<DmmCompareVerbOptions, DmmCompareVerbResult>
{
    private readonly VerbOptionDeclaration<CompareWithDmmOverrides> _verbOptions;

    public DmmCompareCommandFactory(
        IServiceScopeFactory scopeFactory,
        VerbOptionRegistry optionRegistry)
        : base(scopeFactory)
    {
        if (optionRegistry is null)
        {
            throw new ArgumentNullException(nameof(optionRegistry));
        }

        _verbOptions = optionRegistry.DmmCompare;
    }

    protected override string VerbName => DmmCompareVerb.VerbName;

    protected override Command CreateCommandCore()
    {
        var command = new Command("dmm-compare", "Compare the emitted SSDT artifacts with a DMM baseline.");
        _verbOptions.Configure(command);
        return command;
    }

    protected override DmmCompareVerbOptions BindOptions(InvocationContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var bound = _verbOptions.Bind(context.ParseResult);

        if (bound.ModuleFilter is null)
        {
            throw new InvalidOperationException("Module filter overrides missing.");
        }

        if (bound.Sql is null)
        {
            throw new InvalidOperationException("SQL overrides missing.");
        }

        return new DmmCompareVerbOptions
        {
            ConfigurationPath = bound.ConfigurationPath,
            Overrides = bound.Overrides,
            ModuleFilter = bound.ModuleFilter,
            Sql = bound.Sql,
            Cache = bound.Cache,
            Tightening = bound.Tightening
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
