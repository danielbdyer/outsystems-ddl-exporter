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
}

public readonly record struct ValidationError(string Code, string Message)
{
    public static ValidationError Create(string code, string message)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Validation code must be provided.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Validation message must be provided.", nameof(message));
        }

        return new ValidationError(code, message);
    }
}
