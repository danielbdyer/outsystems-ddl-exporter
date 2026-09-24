using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;

namespace Osm.Pipeline.Mediation;

/// <summary>
/// Marker interface for commands dispatched inside the pipeline layer.
/// </summary>
/// <typeparam name="TResponse">Response type returned by the command handler.</typeparam>
public interface ICommand<TResponse>
{
}

/// <summary>
/// Handles a specific command type and produces a result value.
/// </summary>
/// <typeparam name="TCommand">Command type handled by the implementation.</typeparam>
/// <typeparam name="TResponse">Response payload type.</typeparam>
public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}

/// <summary>
/// Coordinates the dispatch of commands to their registered handlers.
/// </summary>
public interface ICommandDispatcher
{
    Task<Result<TResponse>> DispatchAsync<TCommand, TResponse>(
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResponse>;
}
