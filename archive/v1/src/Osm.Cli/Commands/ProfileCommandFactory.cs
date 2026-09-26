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

internal sealed class ProfileCommandFactory : PipelineCommandFactory<ProfileVerbOptions, ProfileVerbResult>
{
    private readonly VerbOptionDeclaration<CaptureProfileOverrides> _verbOptions;

    public ProfileCommandFactory(
        IServiceScopeFactory scopeFactory,
        VerbOptionRegistry optionRegistry)
        : base(scopeFactory)
    {
        if (optionRegistry is null)
        {
            throw new ArgumentNullException(nameof(optionRegistry));
        }

        _verbOptions = optionRegistry.Profile;
    }

    protected override string VerbName => ProfileVerb.VerbName;

    protected override Command CreateCommandCore()
    {
        var command = new Command("profile", "Capture and persist a profiling snapshot.");
        _verbOptions.Configure(command);
        return command;
    }

    protected override ProfileVerbOptions BindOptions(InvocationContext context)
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

        return new ProfileVerbOptions
        {
            ConfigurationPath = bound.ConfigurationPath,
            Overrides = bound.Overrides,
            ModuleFilter = bound.ModuleFilter,
            Sql = bound.Sql,
            Tightening = bound.Tightening
        };
    }

    protected override Task<int> OnRunSucceededAsync(InvocationContext context, ProfileVerbResult payload)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        CommandConsole.EmitProfileSummary(context.Console, payload.ApplicationResult);
        return Task.FromResult(0);
    }
}
