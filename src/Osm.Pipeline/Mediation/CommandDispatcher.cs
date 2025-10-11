using System;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;

namespace Osm.Pipeline.Mediation;

/// <summary>
/// Default in-process implementation of the command dispatcher that resolves
/// handlers from an <see cref="IServiceProvider"/>.
/// </summary>
public sealed class CommandDispatcher : ICommandDispatcher
{
    private readonly IServiceProvider _serviceProvider;

    public CommandDispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public Task<Result<TResponse>> DispatchAsync<TCommand, TResponse>(
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResponse>
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        var handler = (ICommandHandler<TCommand, TResponse>?)_serviceProvider.GetService(typeof(ICommandHandler<TCommand, TResponse>));
        if (handler is null)
        {
            throw new InvalidOperationException($"No handler registered for command type {typeof(TCommand).FullName}.");
        }

        return handler.HandleAsync(command, cancellationToken);
    }
}
