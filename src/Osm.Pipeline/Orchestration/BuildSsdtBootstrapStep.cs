using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Json;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.Orchestration;

public sealed class BuildSsdtBootstrapStep : IBuildSsdtStep
{
    private readonly IPipelineBootstrapper _bootstrapper;
    private readonly ProfileSnapshotDeserializer _profileSnapshotDeserializer;

    public BuildSsdtBootstrapStep(
        IPipelineBootstrapper bootstrapper,
        ProfileSnapshotDeserializer profileSnapshotDeserializer)
    {
        _bootstrapper = bootstrapper ?? throw new ArgumentNullException(nameof(bootstrapper));
        _profileSnapshotDeserializer = profileSnapshotDeserializer ?? throw new ArgumentNullException(nameof(profileSnapshotDeserializer));
    }

    public async Task<Result<BuildSsdtPipelineContext>> ExecuteAsync(
        BuildSsdtPipelineContext context,
        CancellationToken cancellationToken = default)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var request = context.Request;
        var telemetry = CreateTelemetry(request);
        var bootstrapRequest = new PipelineBootstrapRequest(
            request.ModelPath,
            request.ModuleFilter,
            request.SupplementalModels,
            telemetry,
            (model, token) => CaptureProfileAsync(request, model, token));

        var bootstrapResult = await _bootstrapper
            .BootstrapAsync(context.Log, bootstrapRequest, cancellationToken)
            .ConfigureAwait(false);
        if (bootstrapResult.IsFailure)
        {
            return Result<BuildSsdtPipelineContext>.Failure(bootstrapResult.Errors);
        }

        context.SetBootstrapContext(bootstrapResult.Value);
        return Result<BuildSsdtPipelineContext>.Success(context);
    }

    private static PipelineBootstrapTelemetry CreateTelemetry(BuildSsdtPipelineRequest request)
    {
        return new PipelineBootstrapTelemetry(
            "Received build-ssdt pipeline request.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["modelPath"] = request.ModelPath,
                ["outputDirectory"] = request.OutputDirectory,
                ["profilerProvider"] = request.ProfilerProvider,
                ["moduleFilter.hasFilter"] = request.ModuleFilter.HasFilter ? "true" : "false",
                ["moduleFilter.moduleCount"] = request.ModuleFilter.Modules.Length.ToString(CultureInfo.InvariantCulture),
                ["supplemental.includeUsers"] = request.SupplementalModels.IncludeUsers ? "true" : "false",
                ["supplemental.pathCount"] = request.SupplementalModels.Paths.Count.ToString(CultureInfo.InvariantCulture),
                ["tightening.mode"] = request.TighteningOptions.Policy.Mode.ToString(),
                ["tightening.nullBudget"] = request.TighteningOptions.Policy.NullBudget.ToString(CultureInfo.InvariantCulture),
                ["emission.includePlatformAutoIndexes"] = request.SmoOptions.IncludePlatformAutoIndexes ? "true" : "false",
                ["emission.emitBareTableOnly"] = request.SmoOptions.EmitBareTableOnly ? "true" : "false",
                ["emission.sanitizeModuleNames"] = request.SmoOptions.SanitizeModuleNames ? "true" : "false",
                ["emission.moduleParallelism"] = request.SmoOptions.ModuleParallelism.ToString(CultureInfo.InvariantCulture)
            },
            "Capturing profiling snapshot.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["provider"] = request.ProfilerProvider,
                ["profilePath"] = request.ProfilePath
            },
            "Captured profiling snapshot.");
    }

    private async Task<Result<ProfileSnapshot>> CaptureProfileAsync(
        BuildSsdtPipelineRequest request,
        OsmModel model,
        CancellationToken cancellationToken)
    {
        if (string.Equals(request.ProfilerProvider, "sql", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.SqlOptions.ConnectionString))
            {
                return ValidationError.Create(
                    "pipeline.buildSsdt.sql.connectionString.missing",
                    "Connection string is required when using the SQL profiler.");
            }

            var sampling = request.SqlOptions.ToSamplingOptions();
            var connectionOptions = request.SqlOptions.ToConnectionOptions();
            var profilerOptions = SqlProfilerOptions.Default with
            {
                CommandTimeoutSeconds = request.SqlOptions.CommandTimeoutSeconds,
                Sampling = sampling
            };

            var sqlProfiler = new SqlDataProfiler(
                new SqlConnectionFactory(request.SqlOptions.ConnectionString!, connectionOptions),
                model,
                profilerOptions);

            return await sqlProfiler.CaptureAsync(cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(request.ProfilePath))
        {
            return ValidationError.Create(
                "pipeline.buildSsdt.profile.path.missing",
                "Profile path is required when using the fixture profiler.");
        }

        var fixtureProfiler = new FixtureDataProfiler(request.ProfilePath!, _profileSnapshotDeserializer);
        return await fixtureProfiler.CaptureAsync(cancellationToken).ConfigureAwait(false);
    }
}
