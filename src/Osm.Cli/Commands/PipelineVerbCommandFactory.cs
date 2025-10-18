using System;
using System.CommandLine.Invocation;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Pipeline.Runtime;

namespace Osm.Cli.Commands;

internal abstract class PipelineVerbCommandFactory : ICommandFactory
{
    private readonly IServiceScopeFactory _scopeFactory;

    protected PipelineVerbCommandFactory(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    public abstract System.CommandLine.Command Create();

    protected async Task<bool> ExecuteVerbAsync<TOptions, TResult>(
        InvocationContext context,
        string verbName,
        Func<IServiceProvider, InvocationContext, TOptions> createOptions,
        Func<IServiceProvider, InvocationContext, TResult, ValueTask> onSuccess,
        string? unexpectedPayloadMessage = null)
        where TResult : class
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (string.IsNullOrWhiteSpace(verbName))
        {
            throw new ArgumentException("Verb name must be provided.", nameof(verbName));
        }

        if (createOptions is null)
        {
            throw new ArgumentNullException(nameof(createOptions));
        }

        if (onSuccess is null)
        {
            throw new ArgumentNullException(nameof(onSuccess));
        }

        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var registry = services.GetRequiredService<IVerbRegistry>();
        var verb = registry.Get(verbName);

        var options = createOptions(services, context);
        if (options is null)
        {
            throw new InvalidOperationException($"Options factory for verb '{verbName}' returned null.");
        }

        var run = await verb.RunAsync(options!, context.GetCancellationToken()).ConfigureAwait(false);
        if (!run.IsSuccess)
        {
            CommandConsole.WriteErrors(context.Console, run.Errors);
            context.ExitCode = 1;
            return false;
        }

        if (run.Payload is not TResult payload)
        {
            var message = unexpectedPayloadMessage ?? $"[error] Unexpected result type for {verbName} verb.";
            CommandConsole.WriteErrorLine(context.Console, message);
            context.ExitCode = 1;
            return false;
        }

        await onSuccess(services, context, payload).ConfigureAwait(false);
        return true;
    }
}
