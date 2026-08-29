using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Xunit;

namespace Osm.Domain.Tests;

public sealed class ResultCombinatorTests
{
    private static ValidationError Error(string code) => ValidationError.Create(code, code + " message");

    [Fact]
    public void Failure_CodeMessage_CreatesSingleError()
    {
        var result = Result<int>.Failure("code.x", "boom");

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("code.x", error.Code);
        Assert.Equal("boom", error.Message);
    }

    [Fact]
    public void Match_SelectsBranchByOutcome()
    {
        Assert.Equal("ok:5", Result<int>.Success(5).Match(v => "ok:" + v, _ => "err"));
        Assert.Equal("err", Result<int>.Failure("e", "m").Match(v => "ok:" + v, _ => "err"));
    }

    [Fact]
    public void Tap_RunsOnlyOnSuccess_AndReturnsSelf()
    {
        var observed = 0;
        var success = Result<int>.Success(7);
        Assert.Same(success, success.Tap(v => observed = v));
        Assert.Equal(7, observed);

        observed = -1;
        var failure = Result<int>.Failure("e", "m");
        Assert.Same(failure, failure.Tap(v => observed = v));
        Assert.Equal(-1, observed);
    }

    [Fact]
    public void TapError_RunsOnlyOnFailure()
    {
        var seen = 0;
        Result<int>.Success(1).TapError(errors => seen = errors.Length);
        Assert.Equal(0, seen);

        Result<int>.Failure(Error("a"), Error("b")).TapError(errors => seen = errors.Length);
        Assert.Equal(2, seen);
    }

    [Fact]
    public void MapErrors_TransformsFailuresAndPassesSuccessThrough()
    {
        var success = Result<int>.Success(3);
        Assert.Same(success, success.MapErrors(e => e.WithMetadata("k", "v")));

        var mapped = Result<int>.Failure(Error("a")).MapErrors(e => e.WithMetadata("path", "$.a"));
        Assert.Equal("$.a", Assert.Single(mapped.Errors).Metadata["path"]);
    }

    [Fact]
    public void Combine_ReturnsTupleWhenAllSucceed()
    {
        var combined = Result.Combine(Result<int>.Success(1), Result<string>.Success("a"));

        Assert.True(combined.IsSuccess);
        Assert.Equal((1, "a"), combined.Value);
    }

    [Fact]
    public void Combine_AccumulatesAllFailures()
    {
        var combined = Result.Combine(
            Result<int>.Failure(Error("first")),
            Result<string>.Success("a"),
            Result<bool>.Failure(Error("third")));

        Assert.True(combined.IsFailure);
        Assert.Equal(new[] { "first", "third" }, combined.Errors.Select(e => e.Code).ToArray());
    }

    [Fact]
    public void Traverse_ShortCircuitsOnFirstFailure()
    {
        var result = Result.Traverse(new[] { 1, 2, -3, 4 }, n =>
            n < 0 ? Result<int>.Failure("neg", "negative") : Result<int>.Success(n * 2));

        Assert.True(result.IsFailure);
        Assert.Equal("neg", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Traverse_MapsAllOnSuccess()
    {
        var result = Result.Traverse(new[] { 1, 2, 3 }, n => Result<int>.Success(n * 10));

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { 10, 20, 30 }, result.Value.ToArray());
    }

    [Fact]
    public void Traverse_IndexAware_PassesPositionalIndex()
    {
        var result = Result.Traverse(new[] { "a", "b" }, (value, index) => Result<string>.Success($"{index}:{value}"));

        Assert.Equal(new[] { "0:a", "1:b" }, result.Value.ToArray());
    }

    [Fact]
    public async Task MatchAsync_AwaitsAndSelectsBranch()
    {
        var ok = await Task.FromResult(Result<int>.Success(2)).MatchAsync(v => v + 1, _ => -1);
        Assert.Equal(3, ok);

        var err = await Task.FromResult(Result<int>.Failure("e", "m")).MatchAsync(v => v + 1, _ => -1);
        Assert.Equal(-1, err);
    }

    [Fact]
    public async Task EnsureAsync_FailsWhenPredicateFalse()
    {
        var result = await Task.FromResult(Result<int>.Success(1))
            .EnsureAsync(v => v > 5, Error("too.small"));

        Assert.True(result.IsFailure);
        Assert.Equal("too.small", Assert.Single(result.Errors).Code);
    }
}
