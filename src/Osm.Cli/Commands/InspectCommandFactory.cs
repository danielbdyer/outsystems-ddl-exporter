using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Pipeline.ModelIngestion;

namespace Osm.Cli.Commands;

internal sealed class InspectCommandFactory : ICommandFactory
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Option<string> _modelOption = new("--model", "Path to the model JSON file.")
    {
        IsRequired = true
    };

    public InspectCommandFactory(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
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
        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var ingestion = services.GetRequiredService<IModelIngestionService>();

        var modelPath = context.ParseResult.GetValueForOption(_modelOption);
        var warnings = new List<string>();
        var result = await ingestion
            .LoadFromFileAsync(modelPath!, warnings, context.GetCancellationToken())
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            CommandConsole.WriteErrors(context.Console, result.Errors);
            context.ExitCode = 1;
            return;
        }

        var model = result.Value;
        CommandConsole.EmitPipelineWarnings(context.Console, warnings.ToImmutableArray());

        var entityCount = model.Modules.Sum(static module => module.Entities.Length);
        var attributeCount = model.Modules.Sum(static module => module.Entities.Sum(static entity => entity.Attributes.Length));

        CommandConsole.WriteLine(context.Console, $"Model exported at {model.ExportedAtUtc:O}");
        CommandConsole.WriteLine(context.Console, $"Modules: {model.Modules.Length}");
        CommandConsole.WriteLine(context.Console, $"Entities: {entityCount}");
        CommandConsole.WriteLine(context.Console, $"Attributes: {attributeCount}");
        context.ExitCode = 0;
    }
}
