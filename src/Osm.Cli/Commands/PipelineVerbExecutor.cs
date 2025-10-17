using System;
using System.CommandLine.Invocation;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Pipeline.Runtime;

namespace Osm.Cli.Commands;

internal sealed class PipelineVerbExecutor
{
    private readonly IServiceScopeFactory _scopeFactory;

    public PipelineVerbExecutor(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    public async Task<PipelineVerbExecutionResult<TPayload>> ExecuteAsync<TPayload>(
        InvocationContext context,
        string verbName,
        object options,
        CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (string.IsNullOrWhiteSpace(verbName))
        {
            throw new ArgumentException("Verb name must be provided.", nameof(verbName));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var registry = services.GetRequiredService<IVerbRegistry>();
        var verb = registry.Get(verbName);

        var run = await verb.RunAsync(options, cancellationToken).ConfigureAwait(false);
        if (!run.IsSuccess)
        {
            CommandConsole.WriteErrors(context.Console, run.Errors);
            context.ExitCode = 1;
            return PipelineVerbExecutionResult<TPayload>.Failure();
        }

        if (run.Payload is not TPayload payload)
        {
            CommandConsole.WriteErrorLine(context.Console, $"[error] Unexpected result type for {verbName} verb.");
            context.ExitCode = 1;
            return PipelineVerbExecutionResult<TPayload>.Failure();
        }

        return PipelineVerbExecutionResult<TPayload>.Success(payload);
    }
}

internal readonly struct PipelineVerbExecutionResult<TPayload>
{
    private PipelineVerbExecutionResult(bool isSuccess, TPayload? payload)
    {
        IsSuccess = isSuccess;
        Payload = payload;
    }

    public bool IsSuccess { get; }

    public TPayload? Payload { get; }

    public static PipelineVerbExecutionResult<TPayload> Success(TPayload payload)
        => new(true, payload);

    public static PipelineVerbExecutionResult<TPayload> Failure()
        => new(false, default);
}
