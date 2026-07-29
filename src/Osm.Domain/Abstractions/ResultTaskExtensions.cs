using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Domain.Abstractions;

public static class ResultTaskExtensions
{
    public static async Task<Result<TIn>> EnsureAsync<TIn>(
        this Task<Result<TIn>> task,
        Func<TIn, bool> predicate,
        ValidationError error)
    {
        if (task is null)
        {
            throw new ArgumentNullException(nameof(task));
        }

        if (predicate is null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        var result = await task.ConfigureAwait(false);
        return result.Ensure(predicate, error);
    }

    public static async Task<TOut> MatchAsync<TIn, TOut>(
        this Task<Result<TIn>> task,
        Func<TIn, TOut> onSuccess,
        Func<ImmutableArray<ValidationError>, TOut> onFailure)
    {
        if (task is null)
        {
            throw new ArgumentNullException(nameof(task));
        }

        var result = await task.ConfigureAwait(false);
        return result.Match(onSuccess, onFailure);
    }

    public static async Task<Result<TIn>> TapAsync<TIn>(
        this Task<Result<TIn>> task,
        Action<TIn> action)
    {
        if (task is null)
        {
            throw new ArgumentNullException(nameof(task));
        }

        var result = await task.ConfigureAwait(false);
        return result.Tap(action);
    }

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
