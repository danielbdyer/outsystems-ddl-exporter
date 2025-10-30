using System;

namespace Osm.Pipeline.Orchestration;

public interface IPipelineBootstrapTelemetryFactory
{
    PipelineBootstrapTelemetry Create(
        ModelExecutionScope scope,
        PipelineCommandDescriptor descriptor,
        PipelineBootstrapTelemetryExtras? extras = null);
}

public sealed record PipelineCommandDescriptor(
    string RequestMessage,
    string ProfilingStartMessage,
    string ProfilingCompletedMessage,
    bool IncludeSupplementalDetails = true,
    bool IncludeTighteningDetails = false,
    bool IncludeEmissionDetails = false);

public sealed record PipelineDiffTarget(string Path, string Kind)
{
    public static PipelineDiffTarget Create(string path, string kind)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Diff target path must be provided.", nameof(path));
        }

        if (string.IsNullOrWhiteSpace(kind))
        {
            throw new ArgumentException("Diff target kind must be provided.", nameof(kind));
        }

        return new PipelineDiffTarget(path, kind);
    }
}

public sealed record PipelineBootstrapTelemetryExtras(
    string? ProfilerProvider = null,
    string? ProfilePath = null,
    string? FixtureProfilePath = null,
    string? OutputPath = null,
    PipelineDiffTarget? DiffTarget = null);

public sealed class PipelineBootstrapTelemetryFactory : IPipelineBootstrapTelemetryFactory
{
    public PipelineBootstrapTelemetry Create(
        ModelExecutionScope scope,
        PipelineCommandDescriptor descriptor,
        PipelineBootstrapTelemetryExtras? extras = null)
    {
        if (scope is null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        extras ??= new PipelineBootstrapTelemetryExtras();

        var requestMetadata = new PipelineLogMetadataBuilder()
            .WithPath("model", scope.ModelPath);

        scope.ModuleFilter.Apply(requestMetadata);

        if (descriptor.IncludeSupplementalDetails)
        {
            requestMetadata
                .WithFlag("supplemental.includeUsers", scope.SupplementalModels.IncludeUsers)
                .WithCount("supplemental.paths", scope.SupplementalModels.Paths.Count);
        }

        if (descriptor.IncludeTighteningDetails && scope.TighteningOptions is not null)
        {
            requestMetadata
                .WithValue("tightening.mode", scope.TighteningOptions.Policy.Mode.ToString())
                .WithMetric("tightening.nullBudget", scope.TighteningOptions.Policy.NullBudget);
        }

        if (descriptor.IncludeEmissionDetails && scope.SmoOptions is not null)
        {
            requestMetadata
                .WithFlag("emission.includePlatformAutoIndexes", scope.SmoOptions.IncludePlatformAutoIndexes)
                .WithFlag("emission.emitBareTableOnly", scope.SmoOptions.EmitBareTableOnly)
                .WithFlag("emission.sanitizeModuleNames", scope.SmoOptions.SanitizeModuleNames)
                .WithCount("emission.moduleParallelism", scope.SmoOptions.ModuleParallelism);
        }

        if (!string.IsNullOrWhiteSpace(extras.OutputPath))
        {
            requestMetadata.WithPath("output", extras.OutputPath);
        }

        if (!string.IsNullOrWhiteSpace(extras.ProfilerProvider))
        {
            requestMetadata.WithValue("profiling.provider", extras.ProfilerProvider);
        }

        if (!string.IsNullOrWhiteSpace(extras.ProfilePath))
        {
            requestMetadata.WithPath("profile", extras.ProfilePath);
        }

        if (extras.DiffTarget is not null)
        {
            requestMetadata
                .WithPath("baseline", extras.DiffTarget.Path)
                .WithValue("baseline.type", extras.DiffTarget.Kind);
        }

        var profilingMetadata = new PipelineLogMetadataBuilder();

        if (!string.IsNullOrWhiteSpace(extras.ProfilerProvider))
        {
            profilingMetadata.WithValue("profiling.provider", extras.ProfilerProvider);
        }

        if (!string.IsNullOrWhiteSpace(extras.ProfilePath))
        {
            profilingMetadata.WithPath("profile", extras.ProfilePath);
        }

        if (!string.IsNullOrWhiteSpace(extras.FixtureProfilePath))
        {
            profilingMetadata.WithPath("profiling.fixture", extras.FixtureProfilePath);
        }

        return new PipelineBootstrapTelemetry(
            descriptor.RequestMessage,
            requestMetadata.Build(),
            descriptor.ProfilingStartMessage,
            profilingMetadata.Build(),
            descriptor.ProfilingCompletedMessage);
    }
}
