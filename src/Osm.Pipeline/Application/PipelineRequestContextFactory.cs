using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Pipeline.Configuration;

namespace Osm.Pipeline.Application;

public sealed class PipelineRequestContextFactory
{
    public Task<PipelineRequestContextScope> CreateAsync(
        PipelineRequestContextFactoryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.ConfigurationContext is null)
        {
            throw new ArgumentNullException(nameof(request.ConfigurationContext));
        }

        var moduleFilterOverrides = request.ModuleFilterOverrides
            ?? new ModuleFilterOverrides(
                Array.Empty<string>(),
                null,
                null,
                Array.Empty<string>(),
                Array.Empty<string>());

        var sqlOptionsOverrides = request.SqlOptionsOverrides
            ?? new SqlOptionsOverrides(null, null, null, null, null, null, null, null);

        var cacheOverrides = request.CacheOptionsOverrides ?? new CacheOptionsOverrides(null, null);

        var builderRequest = new PipelineRequestContextBuilderRequest(
            request.ConfigurationContext,
            moduleFilterOverrides,
            sqlOptionsOverrides,
            cacheOverrides,
            request.SqlMetadataOutputPath,
            request.NamingOverrides);

        var contextResult = PipelineRequestContextBuilder.Build(builderRequest);
        if (contextResult.IsFailure)
        {
            return Task.FromResult(PipelineRequestContextScope.Failure(contextResult.Errors));
        }

        var scope = PipelineRequestContextScope.Success(contextResult.Value, cancellationToken);
        return Task.FromResult(scope);
    }
}

public sealed record PipelineRequestContextFactoryRequest(
    CliConfigurationContext ConfigurationContext,
    ModuleFilterOverrides? ModuleFilterOverrides,
    SqlOptionsOverrides? SqlOptionsOverrides,
    CacheOptionsOverrides? CacheOptionsOverrides,
    string? SqlMetadataOutputPath,
    NamingOverridesRequest? NamingOverrides);

public sealed class PipelineRequestContextScope : IAsyncDisposable, IDisposable
{
    private readonly PipelineRequestContext? _context;
    private readonly CancellationToken _cancellationToken;
    private bool _disposed;

    private PipelineRequestContextScope(
        PipelineRequestContext? context,
        IReadOnlyCollection<ValidationError> errors,
        CancellationToken cancellationToken)
    {
        _context = context;
        _cancellationToken = cancellationToken;
        Errors = errors;
    }

    public bool IsSuccess => _context is not null;

    public bool IsFailure => !IsSuccess;

    public IReadOnlyCollection<ValidationError> Errors { get; }

    public PipelineRequestContext Context
        => _context ?? throw new InvalidOperationException("Pipeline request context is not available when creation fails.");

    public static PipelineRequestContextScope Success(
        PipelineRequestContext context,
        CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        return new PipelineRequestContextScope(
            context,
            Array.Empty<ValidationError>(),
            cancellationToken);
    }

    public static PipelineRequestContextScope Failure(IReadOnlyCollection<ValidationError> errors)
    {
        if (errors is null)
        {
            throw new ArgumentNullException(nameof(errors));
        }

        return new PipelineRequestContextScope(null, errors, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_context is null)
        {
            return;
        }

        await _context.FlushMetadataAsync(_cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_context is null)
        {
            return;
        }

        _context.FlushMetadataAsync(_cancellationToken).GetAwaiter().GetResult();
    }
}
