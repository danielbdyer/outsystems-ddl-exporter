using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;


namespace Osm.Domain.Abstractions;

public sealed class Result<T>
{
    private readonly T? _value;

    private Result(T? value, ImmutableArray<ValidationError> errors, bool isSuccess)
    {
        _value = value;
        Errors = errors;
        IsSuccess = isSuccess;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public ImmutableArray<ValidationError> Errors { get; }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access Value on a failed Result.");

    public static Result<T> Success(T value) => new(value, ImmutableArray<ValidationError>.Empty, true);

    public static Result<T> Failure(params ValidationError[] errors) => Failure(errors.AsEnumerable());

    /// <summary>
    /// Convenience overload for the ubiquitous single-error failure, replacing
    /// <c>Failure(ValidationError.Create(code, message))</c> at call sites.
    /// </summary>
    public static Result<T> Failure(string code, string message)
        => Failure(ValidationError.Create(code, message));

    public static Result<T> Failure(IEnumerable<ValidationError> errors)
    {
        var materialized = errors.ToImmutableArray();
        if (materialized.IsDefaultOrEmpty)
        {
            throw new ArgumentException("At least one validation error must be provided.", nameof(errors));
        }

        return new Result<T>(default, materialized, false);
    }

    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> next)
    {
        if (next is null)
        {
            throw new ArgumentNullException(nameof(next));
        }

        return IsFailure
            ? Result<TOut>.Failure(Errors)
            : next(Value);
    }

    public Result<TOut> Map<TOut>(Func<T, TOut> map)
    {
        if (map is null)
        {
            throw new ArgumentNullException(nameof(map));
        }

        return IsFailure
            ? Result<TOut>.Failure(Errors)
            : Result<TOut>.Success(map(Value));
    }

    public Result<T> Ensure(Func<T, bool> predicate, ValidationError error)
    {
        if (predicate is null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        return IsFailure || predicate(Value)
            ? this
            : Failure(error);
    }

    /// <summary>
    /// Collapses the success/failure branch into a single value, replacing the
    /// <c>if (result.IsFailure) { … } else { … }</c> idiom at terminal boundaries.
    /// </summary>
    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<ImmutableArray<ValidationError>, TOut> onFailure)
    {
        if (onSuccess is null)
        {
            throw new ArgumentNullException(nameof(onSuccess));
        }

        if (onFailure is null)
        {
            throw new ArgumentNullException(nameof(onFailure));
        }

        return IsSuccess ? onSuccess(Value) : onFailure(Errors);
    }

    /// <summary>
    /// Performs a side effect on the success value without altering the result,
    /// keeping a fluent chain unbroken (e.g. logging, warning emission).
    /// </summary>
    public Result<T> Tap(Action<T> action)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        if (IsSuccess)
        {
            action(Value);
        }

        return this;
    }

    /// <summary>
    /// Performs a side effect on the errors of a failed result without altering it.
    /// </summary>
    public Result<T> TapError(Action<ImmutableArray<ValidationError>> action)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        if (IsFailure)
        {
            action(Errors);
        }

        return this;
    }

    /// <summary>
    /// Transforms each error of a failed result (e.g. decorating with JSON-path
    /// metadata); a successful result passes through unchanged.
    /// </summary>
    public Result<T> MapErrors(Func<ValidationError, ValidationError> transform)
    {
        if (transform is null)
        {
            throw new ArgumentNullException(nameof(transform));
        }

        if (IsSuccess)
        {
            return this;
        }

        var builder = ImmutableArray.CreateBuilder<ValidationError>(Errors.Length);
        foreach (var error in Errors)
        {
            builder.Add(transform(error));
        }

        return Failure(builder.ToImmutable());
    }

    public static implicit operator Result<T>(ValidationError error) => Failure(error);

    public static implicit operator Result<T>(T value) => Success(value);
}

public static class Result
{
    public static Result<ImmutableArray<T>> Collect<T>(IEnumerable<Result<T>> results)
    {
        if (results is null)
        {
            throw new ArgumentNullException(nameof(results));
        }

        var successes = ImmutableArray.CreateBuilder<T>();
        foreach (var result in results)
        {
            if (result is null)
            {
                return Result<ImmutableArray<T>>.Failure(ValidationError.Create("result.null", "Encountered a null result."));
            }

            if (result.IsFailure)
            {
                return Result<ImmutableArray<T>>.Failure(result.Errors);
            }

            successes.Add(result.Value);
        }

        return Result<ImmutableArray<T>>.Success(successes.ToImmutable());
    }

    /// <summary>
    /// Applicative combination: returns the tuple of values when every result
    /// succeeds, otherwise a failure accumulating the errors of <em>all</em>
    /// failed results (not just the first). Replaces the hand-rolled
    /// builder + <c>AddRange(...Errors)</c> dance in multi-field smart constructors.
    /// </summary>
    public static Result<(T1, T2)> Combine<T1, T2>(Result<T1> first, Result<T2> second)
    {
        if (first is null)
        {
            throw new ArgumentNullException(nameof(first));
        }

        if (second is null)
        {
            throw new ArgumentNullException(nameof(second));
        }

        if (first.IsSuccess && second.IsSuccess)
        {
            return Result<(T1, T2)>.Success((first.Value, second.Value));
        }

        var errors = ImmutableArray.CreateBuilder<ValidationError>();
        if (first.IsFailure)
        {
            errors.AddRange(first.Errors);
        }

        if (second.IsFailure)
        {
            errors.AddRange(second.Errors);
        }

        return Result<(T1, T2)>.Failure(errors.ToImmutable());
    }

    public static Result<(T1, T2, T3)> Combine<T1, T2, T3>(Result<T1> first, Result<T2> second, Result<T3> third)
    {
        if (first is null)
        {
            throw new ArgumentNullException(nameof(first));
        }

        if (second is null)
        {
            throw new ArgumentNullException(nameof(second));
        }

        if (third is null)
        {
            throw new ArgumentNullException(nameof(third));
        }

        if (first.IsSuccess && second.IsSuccess && third.IsSuccess)
        {
            return Result<(T1, T2, T3)>.Success((first.Value, second.Value, third.Value));
        }

        var errors = ImmutableArray.CreateBuilder<ValidationError>();
        if (first.IsFailure)
        {
            errors.AddRange(first.Errors);
        }

        if (second.IsFailure)
        {
            errors.AddRange(second.Errors);
        }

        if (third.IsFailure)
        {
            errors.AddRange(third.Errors);
        }

        return Result<(T1, T2, T3)>.Failure(errors.ToImmutable());
    }

    public static Result<(T1, T2, T3, T4)> Combine<T1, T2, T3, T4>(
        Result<T1> first,
        Result<T2> second,
        Result<T3> third,
        Result<T4> fourth)
    {
        if (first is null)
        {
            throw new ArgumentNullException(nameof(first));
        }

        if (second is null)
        {
            throw new ArgumentNullException(nameof(second));
        }

        if (third is null)
        {
            throw new ArgumentNullException(nameof(third));
        }

        if (fourth is null)
        {
            throw new ArgumentNullException(nameof(fourth));
        }

        if (first.IsSuccess && second.IsSuccess && third.IsSuccess && fourth.IsSuccess)
        {
            return Result<(T1, T2, T3, T4)>.Success((first.Value, second.Value, third.Value, fourth.Value));
        }

        var errors = ImmutableArray.CreateBuilder<ValidationError>();
        if (first.IsFailure)
        {
            errors.AddRange(first.Errors);
        }

        if (second.IsFailure)
        {
            errors.AddRange(second.Errors);
        }

        if (third.IsFailure)
        {
            errors.AddRange(third.Errors);
        }

        if (fourth.IsFailure)
        {
            errors.AddRange(fourth.Errors);
        }

        return Result<(T1, T2, T3, T4)>.Failure(errors.ToImmutable());
    }

    /// <summary>
    /// Maps each element through a result-returning selector, short-circuiting on
    /// the first failure. Replaces the sized-builder + indexed loop + per-element
    /// <c>if (IsFailure) return Failure(...)</c> scaffold in the document mappers.
    /// </summary>
    public static Result<ImmutableArray<TOut>> Traverse<TIn, TOut>(
        IEnumerable<TIn> source,
        Func<TIn, Result<TOut>> selector)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (selector is null)
        {
            throw new ArgumentNullException(nameof(selector));
        }

        return Traverse(source, (item, _) => selector(item));
    }

    /// <summary>
    /// Index-aware <see cref="Traverse{TIn,TOut}(IEnumerable{TIn},Func{TIn,Result{TOut}})"/>;
    /// the index lets callers attach positional path context to per-element errors.
    /// </summary>
    public static Result<ImmutableArray<TOut>> Traverse<TIn, TOut>(
        IEnumerable<TIn> source,
        Func<TIn, int, Result<TOut>> selector)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (selector is null)
        {
            throw new ArgumentNullException(nameof(selector));
        }

        var builder = ImmutableArray.CreateBuilder<TOut>();
        var index = 0;
        foreach (var item in source)
        {
            var result = selector(item, index);
            if (result is null)
            {
                return Result<ImmutableArray<TOut>>.Failure(
                    ValidationError.Create("result.null", "Encountered a null result."));
            }

            if (result.IsFailure)
            {
                return Result<ImmutableArray<TOut>>.Failure(result.Errors);
            }

            builder.Add(result.Value);
            index++;
        }

        return Result<ImmutableArray<TOut>>.Success(builder.ToImmutable());
    }
}

public readonly record struct ValidationError
{
    public ValidationError(string code, string message)
        : this(code, message, ImmutableDictionary<string, string?>.Empty)
    {
    }

    public ValidationError(string code, string message, ImmutableDictionary<string, string?> metadata)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Validation code must be provided.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Validation message must be provided.", nameof(message));
        }

        Code = code;
        Message = message;
        Metadata = metadata ?? ImmutableDictionary<string, string?>.Empty;
    }

    public string Code { get; }

    public string Message { get; }

    public ImmutableDictionary<string, string?> Metadata { get; }

    public bool HasMetadata => !Metadata.IsEmpty;

    public ValidationError WithMessage(string message)
        => new(Code, message, Metadata);

    public ValidationError WithMetadata(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return this;
        }

        var builder = Metadata.IsEmpty
            ? ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal)
            : Metadata.ToBuilder();
        builder[key] = value;
        return new ValidationError(Code, Message, builder.ToImmutable());
    }

    public ValidationError WithMetadata(IEnumerable<KeyValuePair<string, string?>> metadata)
    {
        if (metadata is null)
        {
            return this;
        }

        var builder = Metadata.IsEmpty
            ? ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal)
            : Metadata.ToBuilder();

        foreach (var (key, value) in metadata)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            builder[key] = value;
        }

        return new ValidationError(Code, Message, builder.ToImmutable());
    }

    public static ValidationError Create(string code, string message)
        => new(code, message);

    public static ValidationError Create(
        string code,
        string message,
        IEnumerable<KeyValuePair<string, string?>> metadata)
    {
        var dictionary = metadata is null
            ? ImmutableDictionary<string, string?>.Empty
            : ImmutableDictionary.CreateRange(StringComparer.Ordinal, metadata);
        return new ValidationError(code, message, dictionary);
    }
}
