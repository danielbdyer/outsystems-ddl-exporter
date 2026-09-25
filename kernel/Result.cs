using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Estate.Kernel;

/// <summary>
/// A value, or the <see cref="Error"/> that stands in its place: what a smart constructor or any other kernel
/// function that can fail returns, instead of throwing or returning null. The two cases are closed (the
/// constructor is private), so <see cref="Match{TOut}"/> covers every result. A value or an error converts to a
/// result implicitly, so a function returns either one as it is.
/// </summary>
public abstract record Result<T>
{
    private Result()
    {
    }

    public static implicit operator Result<T>(T value) => new Ok(value);

    public static implicit operator Result<T>(Error error) => new Failed(error);

    public TOut Match<TOut>(Func<T, TOut> ok, Func<Error, TOut> failed) => this switch
    {
        Ok o => ok(o.Value),
        Failed f => failed(f.Error),
        _ => throw new UnreachableException(),
    };

    public Result<TOut> Map<TOut>(Func<T, TOut> map) => Match(value => Result.Ok(map(value)), Result.Fail<TOut>);

    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> bind) => Match(bind, Result.Fail<TOut>);

    /// <summary>The value.</summary>
    public sealed record Ok(T Value) : Result<T>;

    /// <summary>The error, in the value's place.</summary>
    public sealed record Failed(Error Error) : Result<T>;
}

/// <summary>Builds a <see cref="Result{T}"/> where an implicit conversion would not read clearly.</summary>
public static class Result
{
    public static Result<T> Ok<T>(T value) => new Result<T>.Ok(value);

    public static Result<T> Fail<T>(Error error) => new Result<T>.Failed(error);

    /// <summary>
    /// Every value of <paramref name="results"/>, in their order, when each holds one; else the first error in that order. The results
    /// are read one at a time and none after the first error, so a result whose making reads a file or a reference is not made past it.
    /// </summary>
    public static Result<IReadOnlyList<T>> All<T>(IEnumerable<Result<T>> results)
    {
        var values = new List<T>();
        foreach (var result in results)
        {
            if (result is Result<T>.Failed failed)
            {
                return failed.Error;
            }

            values.Add(((Result<T>.Ok)result).Value);
        }

        return Ok<IReadOnlyList<T>>(values);
    }
}
