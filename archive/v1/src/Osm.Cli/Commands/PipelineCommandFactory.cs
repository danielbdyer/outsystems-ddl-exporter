using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Pipeline.Runtime;
using Spectre.Console;
using Osm.Domain.Abstractions;

namespace Osm.Cli.Commands;

internal abstract class PipelineCommandFactory<TVerbOptions, TPayload> : ICommandFactory
{
    private readonly IServiceScopeFactory _scopeFactory;

    protected PipelineCommandFactory(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    protected abstract string VerbName { get; }

    protected abstract Command CreateCommandCore();

    protected abstract TVerbOptions BindOptions(InvocationContext context);

    protected virtual string UnexpectedPayloadMessage => $"[error] Unexpected result type for {VerbName} verb.";

    protected virtual int GetFailureExitCode(IPipelineRun run) => 1;

    protected virtual Task OnRunFailedAsync(InvocationContext context, IPipelineRun run)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (run is null)
        {
            throw new ArgumentNullException(nameof(run));
        }

        CommandConsole.WriteErrors(context.Console, run.Errors);
        return Task.CompletedTask;
    }

    protected abstract Task<int> OnRunSucceededAsync(InvocationContext context, TPayload payload);

    public Command Create()
    {
        var command = CreateCommandCore() ?? throw new InvalidOperationException($"Command factory for '{VerbName}' returned null.");
        command.SetHandler(async context => await ExecuteAsync(context).ConfigureAwait(false));
        return command;
    }

    private async Task ExecuteAsync(InvocationContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var runner = services.GetRequiredService<IProgressRunner>();

        await runner.RunAsync(services, async () =>
        {
            var registry = services.GetRequiredService<IVerbRegistry>();
            var verb = registry.Get(VerbName);

            var options = BindOptions(context);
            var run = await verb.RunAsync(options!, context.GetCancellationToken()).ConfigureAwait(false);

            if (!run.IsSuccess)
            {
                await OnRunFailedAsync(context, run).ConfigureAwait(false);
                context.ExitCode = GetFailureExitCode(run);
                return;
            }

            if (run.Payload is not TPayload payload)
            {
                CommandConsole.WriteErrorLine(context.Console, UnexpectedPayloadMessage);
                context.ExitCode = 1;
                return;
            }

            var exitCode = await OnRunSucceededAsync(context, payload).ConfigureAwait(false);
            context.ExitCode = exitCode;
        });
    }
}
