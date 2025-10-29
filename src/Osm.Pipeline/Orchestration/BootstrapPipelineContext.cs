using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Osm.Domain.Model;
using Osm.Domain.Profiling;

namespace Osm.Pipeline.Orchestration;

internal sealed class BootstrapPipelineContext
{
    private readonly List<string> _warnings = new();

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

            var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["summary"] = ingestionWarnings[0],
                ["lineCount"] = ingestionWarnings.Count.ToString(CultureInfo.InvariantCulture)
            };

            if (ingestionWarnings.Count > 1)
            {
                metadata["example1"] = ingestionWarnings[1];
            }

            if (ingestionWarnings.Count > 2)
            {
                metadata["example2"] = ingestionWarnings[2];
            }

            if (ingestionWarnings.Count > 3)
            {
                metadata["example3"] = ingestionWarnings[3];
            }

            if (ingestionWarnings.Count > 4)
            {
                metadata["suppressed"] = ingestionWarnings[^1];
            }

            Log.Record(
                "model.schema.warnings",
                "Model JSON schema validation produced warnings.",
                metadata);
        }

        var moduleCount = model.Modules.Length;
        var entityCount = model.Modules.Sum(static module => module.Entities.Length);
        var attributeCount = model.Modules.Sum(static module => module.Entities.Sum(entity => entity.Attributes.Length));

        Log.Record(
            "model.ingested",
            "Loaded OutSystems model from disk.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["modules"] = moduleCount.ToString(CultureInfo.InvariantCulture),
                ["entities"] = entityCount.ToString(CultureInfo.InvariantCulture),
                ["attributes"] = attributeCount.ToString(CultureInfo.InvariantCulture),
                ["exportedAtUtc"] = model.ExportedAtUtc.ToString("O", CultureInfo.InvariantCulture)
            });
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
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["originalModules"] = Model.Modules.Length.ToString(CultureInfo.InvariantCulture),
                ["filteredModules"] = filteredModel.Modules.Length.ToString(CultureInfo.InvariantCulture),
                ["filter.includeSystemModules"] = Request.ModuleFilter.IncludeSystemModules ? "true" : "false",
                ["filter.includeInactiveModules"] = Request.ModuleFilter.IncludeInactiveModules ? "true" : "false"
            });
    }

    public void SetSupplementalEntities(ImmutableArray<EntityModel> supplementalEntities)
    {
        SupplementalEntities = supplementalEntities;

        Log.Record(
            "supplemental.loaded",
            "Loaded supplemental entity definitions.",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["supplementalEntityCount"] = supplementalEntities.Length.ToString(CultureInfo.InvariantCulture),
                ["requestedPaths"] = Request.SupplementalModels.Paths.Count.ToString(CultureInfo.InvariantCulture)
            });
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
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["columnProfiles"] = profile.Columns.Length.ToString(CultureInfo.InvariantCulture),
                ["uniqueCandidates"] = profile.UniqueCandidates.Length.ToString(CultureInfo.InvariantCulture),
                ["compositeUniqueCandidates"] = profile.CompositeUniqueCandidates.Length.ToString(CultureInfo.InvariantCulture),
                ["foreignKeys"] = profile.ForeignKeys.Length.ToString(CultureInfo.InvariantCulture)
            });
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
            _warnings.ToImmutableArray());
    }
}
