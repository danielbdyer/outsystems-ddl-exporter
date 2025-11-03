using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Pipeline.Profiling;

namespace Osm.Pipeline.Orchestration;

internal sealed class BootstrapPipelineContext
{
    private readonly List<string> _warnings = new();
    private MultiEnvironmentProfileReport? _multiEnvironmentReport;

    public BootstrapPipelineContext(PipelineExecutionLogBuilder log, PipelineBootstrapRequest request)
    {
        Log = log ?? throw new ArgumentNullException(nameof(log));
        Request = request ?? throw new ArgumentNullException(nameof(request));
    }

    public PipelineExecutionLogBuilder Log { get; }

    public PipelineBootstrapRequest Request { get; }

    public OsmModel? Model { get; private set; }

    public OsmModel? FilteredModel { get; private set; }

    public ImmutableArray<EntityModel> SupplementalEntities { get; private set; } = ImmutableArray<EntityModel>.Empty;

    public ProfileSnapshot? Profile { get; private set; }

    public ImmutableArray<ProfilingInsight> Insights { get; private set; } = ImmutableArray<ProfilingInsight>.Empty;

    public IReadOnlyList<string> Warnings => _warnings;

    public MultiEnvironmentProfileReport? MultiEnvironmentReport => _multiEnvironmentReport;

    public void RecordRequestTelemetry()
    {
        Log.Record("request.received", Request.Telemetry.RequestMessage, Request.Telemetry.RequestMetadata);
    }

    public void SetModel(OsmModel model, IReadOnlyList<string> ingestionWarnings)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));

        if (ingestionWarnings is null)
        {
            throw new ArgumentNullException(nameof(ingestionWarnings));
        }

        if (ingestionWarnings.Count > 0)
        {
            _warnings.AddRange(ingestionWarnings);

            var warningMetadata = new PipelineLogMetadataBuilder()
                .WithValue("warnings.summary", ingestionWarnings[0])
                .WithCount("warnings.lines", ingestionWarnings.Count);

            if (ingestionWarnings.Count > 1)
            {
                warningMetadata.WithValue("warnings.example1", ingestionWarnings[1]);
            }

            if (ingestionWarnings.Count > 2)
            {
                warningMetadata.WithValue("warnings.example2", ingestionWarnings[2]);
            }

            if (ingestionWarnings.Count > 3)
            {
                warningMetadata.WithValue("warnings.example3", ingestionWarnings[3]);
            }

            if (ingestionWarnings.Count > 4)
            {
                warningMetadata.WithValue("warnings.suppressed", ingestionWarnings[^1]);
            }

            Log.Record(
                "model.schema.warnings",
                "Model JSON schema validation produced warnings.",
                warningMetadata.Build());
        }

        var moduleCount = model.Modules.Length;
        var entityCount = model.Modules.Sum(static module => module.Entities.Length);
        var attributeCount = model.Modules.Sum(static module => module.Entities.Sum(entity => entity.Attributes.Length));

        Log.Record(
            "model.ingested",
            "Loaded OutSystems model.",
            new PipelineLogMetadataBuilder()
                .WithCount("modules", moduleCount)
                .WithCount("entities", entityCount)
                .WithCount("attributes", attributeCount)
                .WithTimestamp("model.exported", model.ExportedAtUtc)
                .Build());
    }

    public void SetFilteredModel(OsmModel filteredModel)
    {
        if (Model is null)
        {
            throw new InvalidOperationException("Model must be loaded before applying module filters.");
        }

        FilteredModel = filteredModel ?? throw new ArgumentNullException(nameof(filteredModel));

        Log.Record(
            "model.filtered",
            "Applied module filter options.",
            new PipelineLogMetadataBuilder()
                .WithCount("modules.original", Model.Modules.Length)
                .WithCount("modules.filtered", filteredModel.Modules.Length)
                .WithFlag("filters.includeSystemModules", Request.ModuleFilter.IncludeSystemModules)
                .WithFlag("filters.includeInactiveModules", Request.ModuleFilter.IncludeInactiveModules)
                .Build());
    }

    public void SetSupplementalEntities(ImmutableArray<EntityModel> supplementalEntities)
    {
        SupplementalEntities = supplementalEntities;

        Log.Record(
            "supplemental.loaded",
            "Loaded supplemental entity definitions.",
            new PipelineLogMetadataBuilder()
                .WithCount("entities.supplemental", supplementalEntities.Length)
                .WithCount("supplemental.paths", Request.SupplementalModels.Paths.Count)
                .Build());
    }

    public void LogProfilingStarted()
    {
        Log.Record(
            "profiling.capture.start",
            Request.Telemetry.ProfilingStartMessage,
            Request.Telemetry.ProfilingStartMetadata);
    }

    public void SetProfile(ProfileSnapshot profile)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));

        Log.Record(
            "profiling.capture.completed",
            Request.Telemetry.ProfilingCompletedMessage,
            new PipelineLogMetadataBuilder()
                .WithCount("profiles.columns", profile.Columns.Length)
                .WithCount("profiles.uniqueCandidates", profile.UniqueCandidates.Length)
                .WithCount("profiles.compositeUniqueCandidates", profile.CompositeUniqueCandidates.Length)
                .WithCount("profiles.foreignKeys", profile.ForeignKeys.Length)
                .Build());
    }

    public void SetMultiEnvironmentReport(MultiEnvironmentProfileReport? report)
    {
        _multiEnvironmentReport = report;
    }

    public void SetInsights(ImmutableArray<ProfilingInsight> insights)
    {
        Insights = insights;
    }

    public PipelineBootstrapContext BuildResult()
    {
        if (FilteredModel is null)
        {
            throw new InvalidOperationException("Filtered model not available.");
        }

        if (Profile is null)
        {
            throw new InvalidOperationException("Profile snapshot not available.");
        }

        return new PipelineBootstrapContext(
            FilteredModel,
            SupplementalEntities,
            Profile,
            Insights,
            _warnings.ToImmutableArray(),
            _multiEnvironmentReport);
    }
}
