using System;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Domain.Abstractions;

public static class ResultTaskExtensions
{
    public static Task<Result<TOut>> BindAsync<TIn, TOut>(
        this Task<Result<TIn>> task,
        Func<TIn, Result<TOut>> next)
    {
        if (next is null)
        {
            throw new ArgumentNullException(nameof(next));
        }

        return task.BindAsync(value => Task.FromResult(next(value)));
    }

    public static async Task<Result<TOut>> BindAsync<TIn, TOut>(
        this Task<Result<TIn>> task,
        Func<TIn, Task<Result<TOut>>> next)
    {
        if (task is null)
        {
            throw new ArgumentNullException(nameof(task));
        }

        if (next is null)
        {
            throw new ArgumentNullException(nameof(next));
        }

        var result = await task.ConfigureAwait(false);
        if (result.IsFailure)
        {
            return Result<TOut>.Failure(result.Errors);
        }

        return await next(result.Value).ConfigureAwait(false);
    }

    public static Task<Result<TOut>> BindAsync<TIn, TOut>(
        this Task<Result<TIn>> task,
        Func<TIn, CancellationToken, Task<Result<TOut>>> next,
        CancellationToken cancellationToken)
    {
        if (next is null)
        {
            throw new ArgumentNullException(nameof(next));
        }

        return task.BindAsync(value => next(value, cancellationToken));
    }

    public static Task<Result<TOut>> MapAsync<TIn, TOut>(
        this Task<Result<TIn>> task,
        Func<TIn, TOut> map)
    {
        if (map is null)
        {
            throw new ArgumentNullException(nameof(map));
        }

        return task.MapAsync(value => Task.FromResult(map(value)));
    }

    public static async Task<Result<TOut>> MapAsync<TIn, TOut>(
        this Task<Result<TIn>> task,
        Func<TIn, Task<TOut>> map)
    {
        if (task is null)
        {
            throw new ArgumentNullException(nameof(task));
        }

        if (map is null)
        {
            throw new ArgumentNullException(nameof(map));
        }

        var result = await task.ConfigureAwait(false);
        if (result.IsFailure)
        {
            return Result<TOut>.Failure(result.Errors);
        }

        var mapped = await map(result.Value).ConfigureAwait(false);
        return Result<TOut>.Success(mapped);
    }

    public static Task<Result<TOut>> MapAsync<TIn, TOut>(
        this Task<Result<TIn>> task,
        Func<TIn, CancellationToken, Task<TOut>> map,
        CancellationToken cancellationToken)
    {
        if (map is null)
        {
            throw new ArgumentNullException(nameof(map));
        }

        return task.MapAsync(value => map(value, cancellationToken));
    }
}
