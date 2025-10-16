using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Domain.Abstractions;

namespace Osm.Pipeline.Mediation;

/// <summary>
/// Default in-process implementation of the command dispatcher that resolves
/// handlers from an <see cref="IServiceProvider"/>.
/// </summary>
public sealed class CommandDispatcher : ICommandDispatcher
{
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public CommandDispatcher(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
    }

    public async Task<Result<TResponse>> DispatchAsync<TCommand, TResponse>(
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResponse>
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        using var scope = _serviceScopeFactory.CreateScope();
        var handler = scope.ServiceProvider.GetService<ICommandHandler<TCommand, TResponse>>();
        if (handler is null)
        {
            throw new InvalidOperationException($"No handler registered for command type {typeof(TCommand).FullName}.");
        }

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
        return result;
    }
}
