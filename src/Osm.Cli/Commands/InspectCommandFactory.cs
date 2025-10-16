using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Linq;
using System.Threading.Tasks;
using Osm.Pipeline.Hosting;
using Osm.Pipeline.Hosting.Verbs;

namespace Osm.Cli.Commands;

internal sealed class InspectCommandFactory : ICommandFactory
{
    private readonly IPipelineVerb<InspectModelVerbOptions> _verb;
    private readonly Option<string> _modelOption = new("--model", "Path to the model JSON file.")
    {
        IsRequired = true
    };

    public InspectCommandFactory(IPipelineVerb<InspectModelVerbOptions> verb)
    {
        _verb = verb ?? throw new ArgumentNullException(nameof(verb));
        _modelOption.AddAlias("--in");
    }

    public Command Create()
    {
        var command = new Command("inspect", "Inspect an OutSystems model JSON file.")
        {
            _modelOption
        };

        command.SetHandler(async context => await ExecuteAsync(context).ConfigureAwait(false));
        return command;
    }

    private async Task ExecuteAsync(InvocationContext context)
    {
        var modelPath = context.ParseResult.GetValueForOption(_modelOption);
        var options = new InspectModelVerbOptions(modelPath!);
        var result = await _verb.RunAsync(options, context.GetCancellationToken()).ConfigureAwait(false);
        context.ExitCode = result.ExitCode;
    }
}
