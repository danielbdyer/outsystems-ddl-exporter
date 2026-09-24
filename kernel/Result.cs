using System;
using System.Diagnostics;

namespace Estate.Kernel;

/// <summary>
/// A value, or the <see cref="Refusal"/> that stands in its place: what a smart constructor or any other kernel
/// function that can refuse returns, instead of throwing or returning null. The two cases are closed (the
/// constructor is private), so <see cref="Match{TOut}"/> covers every result. A value or a refusal converts to a
/// result implicitly, so a function returns either one as it is.
/// </summary>
public abstract record Result<T>
{
    private Result()
    {
    }

    public static implicit operator Result<T>(T value) => new Ok(value);

    public static implicit operator Result<T>(Refusal refusal) => new Refused(refusal);

    public TOut Match<TOut>(Func<T, TOut> ok, Func<Refusal, TOut> refused) => this switch
    {
        Ok o => ok(o.Value),
        Refused r => refused(r.Refusal),
        _ => throw new UnreachableException(),
    };

    public Result<TOut> Map<TOut>(Func<T, TOut> map) => Match(value => Result.Ok(map(value)), Result.Refuse<TOut>);

    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> bind) => Match(bind, Result.Refuse<TOut>);

    /// <summary>The value.</summary>
    public sealed record Ok(T Value) : Result<T>;

    /// <summary>The refusal, in the value's place.</summary>
    public sealed record Refused(Refusal Refusal) : Result<T>;
}

/// <summary>Builds a <see cref="Result{T}"/> where an implicit conversion would not read clearly.</summary>
public static class Result
{
    public static Result<T> Ok<T>(T value) => new Result<T>.Ok(value);

    public static Result<T> Refuse<T>(Refusal refusal) => new Result<T>.Refused(refusal);
}
