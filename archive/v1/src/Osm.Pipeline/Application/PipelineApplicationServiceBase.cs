using System;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Orchestration;

namespace Osm.Pipeline.Application;

public abstract class PipelineApplicationServiceBase
{
    protected static T EnsureNotNull<T>(T? value, string parameterName)
        where T : class
    {
        if (value is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        return value;
    }

    private protected static Result<PipelineRequestContext> BuildContext(PipelineRequestContextBuilderRequest request)
        => PipelineRequestContextBuilder.Build(request);

    protected static Result<string> RequirePath(string? overridePath, string? fallbackPath, string errorCode, string errorMessage)
    {
        var resolved = overridePath ?? fallbackPath;
        if (string.IsNullOrWhiteSpace(resolved))
        {
            return ValidationError.Create(errorCode, errorMessage);
        }

        return Result<string>.Success(resolved);
    }

    protected static string ResolveOutputDirectory(string? overridePath, string defaultDirectory = "out")
        => string.IsNullOrWhiteSpace(overridePath) ? defaultDirectory : overridePath!;

    private protected static EvidenceCachePipelineOptions? CreateCacheOptions(
        PipelineRequestContext context,
        string command,
        string modelPath,
        string? profilePath,
        string? dmmPath)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        return context.CreateCacheOptions(command, modelPath, profilePath, dmmPath);
    }

    private protected static async Task<Result<T>> EnsureSuccessOrFlushAsync<T>(
        Result<T> result,
        PipelineRequestContext context,
        CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        if (result.IsFailure)
        {
            await context.FlushMetadataAsync(cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    private protected static Task FlushMetadataAsync(PipelineRequestContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        return context.FlushMetadataAsync(cancellationToken);
    }
}
