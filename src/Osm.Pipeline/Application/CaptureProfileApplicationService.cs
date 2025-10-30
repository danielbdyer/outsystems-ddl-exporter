using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Orchestration;
using Osm.Smo;
using Osm.Pipeline.Profiling;

namespace Osm.Pipeline.Application;

public sealed record CaptureProfileApplicationInput(
    CliConfigurationContext ConfigurationContext,
    CaptureProfileOverrides Overrides,
    ModuleFilterOverrides ModuleFilter,
    SqlOptionsOverrides Sql);

public sealed record CaptureProfileApplicationResult(
    CaptureProfilePipelineResult PipelineResult,
    string OutputDirectory,
    string ModelPath,
    string ProfilerProvider,
    string? FixtureProfilePath,
    ImmutableArray<SqlProfilerPreflightDiagnostic> ProfilerDiagnostics);

public sealed class CaptureProfileApplicationService : PipelineApplicationServiceBase, IApplicationService<CaptureProfileApplicationInput, CaptureProfileApplicationResult>
{
    private readonly ICommandDispatcher _dispatcher;
    private readonly ISqlProfilerPreflight _preflight;

    public CaptureProfileApplicationService(ICommandDispatcher dispatcher, ISqlProfilerPreflight preflight)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _preflight = preflight ?? throw new ArgumentNullException(nameof(preflight));
    }

    public async Task<Result<CaptureProfileApplicationResult>> RunAsync(
        CaptureProfileApplicationInput input,
        CancellationToken cancellationToken = default)
    {
        input = EnsureNotNull(input, nameof(input));
        var configurationContext = EnsureNotNull(input.ConfigurationContext, nameof(input.ConfigurationContext));

        var contextResult = BuildContext(new PipelineRequestContextBuilderRequest(
            configurationContext,
            input.ModuleFilter,
            input.Sql,
            CacheOptionsOverrides: null,
            SqlMetadataOutputPath: input.Overrides.SqlMetadataOutputPath,
            NamingOverrides: null));
        if (contextResult.IsFailure)
        {
            return Result<CaptureProfileApplicationResult>.Failure(contextResult.Errors);
        }

        var context = contextResult.Value;
        var configuration = context.Configuration;
        var overrides = input.Overrides ?? new CaptureProfileOverrides(null, null, null, null, null);

        var modelPathResult = RequirePath(
            overrides.ModelPath,
            configuration.ModelPath,
            "pipeline.captureProfile.model.missing",
            "Model path must be provided for profile capture.");
        if (modelPathResult.IsFailure)
        {
            return Result<CaptureProfileApplicationResult>.Failure(modelPathResult.Errors);
        }

        var profilerProvider = ResolveProfilerProvider(configuration, overrides);
        var fixtureProfileResult = ResolveFixtureProfilePath(profilerProvider, configuration, overrides);
        if (fixtureProfileResult.IsFailure)
        {
            return Result<CaptureProfileApplicationResult>.Failure(fixtureProfileResult.Errors);
        }

        var modelPath = modelPathResult.Value;
        var fixtureProfilePath = fixtureProfileResult.Value;

        var outputDirectory = ResolveOutputDirectory(overrides.OutputDirectory, defaultDirectory: "profiles");
        Directory.CreateDirectory(outputDirectory);

        var baseSmoOptions = SmoBuildOptions.FromEmission(context.Tightening.Emission);
        var namingOverrides = context.NamingOverrides ?? baseSmoOptions.NamingOverrides;
        var smoOptions = baseSmoOptions.WithNamingOverrides(namingOverrides);

        var skipPreflight = overrides.SkipProfilerPreflight;
        var profilerDiagnostics = ImmutableArray<SqlProfilerPreflightDiagnostic>.Empty;
        SqlProfilerPreflightResult? preflightResult = null;

        if (string.Equals(profilerProvider, "sql", StringComparison.OrdinalIgnoreCase))
        {
            if (skipPreflight)
            {
                profilerDiagnostics = ImmutableArray.Create(
                    new SqlProfilerPreflightDiagnostic(
                        SqlProfilerPreflightSeverity.Information,
                        "Profiler preflight skipped via --skip-profiler-preflight."));
            }
            else if (string.IsNullOrWhiteSpace(context.SqlOptions.ConnectionString))
            {
                await FlushMetadataAsync(context, cancellationToken).ConfigureAwait(false);
                return ValidationError.Create(
                    "pipeline.buildSsdt.sql.connectionString.missing",
                    "Connection string is required when using the SQL profiler.");
            }
            else
            {
                var connectionOptions = SqlProfilerOptionFactory.CreateConnectionOptions(context.SqlOptions.Authentication);
                var profilerOptions = SqlProfilerOptionFactory.CreateProfilerOptions(context.SqlOptions, smoOptions);
                var preflight = _preflight.Run(new SqlProfilerPreflightRequest(
                    context.SqlOptions.ConnectionString!,
                    connectionOptions,
                    profilerOptions));
                preflight = await EnsureSuccessOrFlushAsync(preflight, context, cancellationToken).ConfigureAwait(false);
                if (preflight.IsFailure)
                {
                    return Result<CaptureProfileApplicationResult>.Failure(preflight.Errors);
                }

                preflightResult = preflight.Value;
                profilerDiagnostics = preflightResult.Diagnostics;
            }
        }
        else if (skipPreflight)
        {
            profilerDiagnostics = ImmutableArray.Create(
                new SqlProfilerPreflightDiagnostic(
                    SqlProfilerPreflightSeverity.Information,
                    "Profiler preflight skipped via --skip-profiler-preflight."));
        }

        var request = new CaptureProfilePipelineRequest(
            modelPath,
            context.ModuleFilter,
            context.SupplementalModels,
            profilerProvider,
            fixtureProfilePath,
            context.SqlOptions,
            context.Tightening,
            context.TypeMappingPolicy,
            smoOptions,
            outputDirectory,
            context.SqlMetadataLog,
            skipPreflight,
            preflightResult);

        var pipelineResult = await _dispatcher
            .DispatchAsync<CaptureProfilePipelineRequest, CaptureProfilePipelineResult>(request, cancellationToken)
            .ConfigureAwait(false);

        await FlushMetadataAsync(context, cancellationToken).ConfigureAwait(false);

        if (pipelineResult.IsFailure)
        {
            return Result<CaptureProfileApplicationResult>.Failure(pipelineResult.Errors);
        }

        return new CaptureProfileApplicationResult(
            pipelineResult.Value,
            outputDirectory,
            modelPath,
            profilerProvider,
            fixtureProfilePath,
            profilerDiagnostics);
    }

    private static string ResolveProfilerProvider(CliConfiguration configuration, CaptureProfileOverrides overrides)
    {
        if (!string.IsNullOrWhiteSpace(overrides.ProfilerProvider))
        {
            return overrides.ProfilerProvider!;
        }

        if (!string.IsNullOrWhiteSpace(configuration.Profiler.Provider))
        {
            return configuration.Profiler.Provider!;
        }

        return "sql";
    }

    private static Result<string?> ResolveFixtureProfilePath(
        string provider,
        CliConfiguration configuration,
        CaptureProfileOverrides overrides)
    {
        if (!string.Equals(provider, "fixture", StringComparison.OrdinalIgnoreCase))
        {
            return Result<string?>.Success(overrides.ProfilePath
                ?? configuration.ProfilePath
                ?? configuration.Profiler.ProfilePath);
        }

        var resolved = overrides.ProfilePath
            ?? configuration.ProfilePath
            ?? configuration.Profiler.ProfilePath;

        if (string.IsNullOrWhiteSpace(resolved))
        {
            return ValidationError.Create(
                "pipeline.captureProfile.fixture.missing",
                "Profile path must be provided when using the fixture profiler.");
        }

        return Result<string?>.Success(resolved);
    }
}
