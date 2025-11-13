using System;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli.Commands.Options;
using Osm.Pipeline.Application;
using Osm.Pipeline.DynamicData;
using Osm.Pipeline.Runtime;
using Osm.Pipeline.Runtime.Verbs;

namespace Osm.Cli.Commands;

internal sealed class BuildSsdtCommandFactory : PipelineCommandFactory<BuildSsdtVerbOptions, BuildSsdtVerbResult>
{
    private readonly VerbOptionDeclaration<BuildSsdtOverrides> _verbOptions;

    public BuildSsdtCommandFactory(
        IServiceScopeFactory scopeFactory,
        VerbOptionRegistry optionRegistry)
        : base(scopeFactory)
    {
        if (optionRegistry is null)
        {
            throw new ArgumentNullException(nameof(optionRegistry));
        }

        _verbOptions = optionRegistry.BuildSsdt;
    }

    protected override string VerbName => BuildSsdtVerb.VerbName;

    protected override Command CreateCommandCore()
    {
        var command = new Command("build-ssdt", "Emit SSDT artifacts from an OutSystems model.");
        _verbOptions.Configure(command);
        return command;
    }

    protected override BuildSsdtVerbOptions BindOptions(InvocationContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var bound = _verbOptions.Bind(context.ParseResult);
        context.BindingContext.AddService(_ => bound);

        return new BuildSsdtVerbOptions
        {
            ConfigurationPath = bound.ConfigurationPath,
            Overrides = bound.Overrides,
            ModuleFilter = bound.ModuleFilter ?? throw new InvalidOperationException("Module filter overrides missing."),
            Sql = bound.Sql ?? throw new InvalidOperationException("SQL overrides missing."),
            Cache = bound.Cache,
            Tightening = bound.Tightening
        };
    }

    protected override async Task<int> OnRunSucceededAsync(InvocationContext context, BuildSsdtVerbResult payload)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        var bound = context.BindingContext.GetService<VerbBoundOptions<BuildSsdtOverrides>>();
        var openReport = bound?.GetExtension<OpenReportSettings>()?.OpenReport ?? false;
        await CommandConsole.EmitBuildSsdtRunAsync(
                context.Console,
                payload.ApplicationResult,
                payload.ApplicationResult.PipelineResult,
                openReport,
                context.GetCancellationToken())
            .ConfigureAwait(false);
        return 0;
    }
}
