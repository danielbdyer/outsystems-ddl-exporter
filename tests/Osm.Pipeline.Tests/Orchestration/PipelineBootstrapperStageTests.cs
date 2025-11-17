using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.SqlExtraction;
using Osm.Validation.Profiling;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests.Orchestration;

public sealed class PipelineBootstrapperStageTests
{

    [Fact]
    public async Task ModelLoader_LoadsModelAndLogsWarnings()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var ingestion = new FakeModelIngestionService(Result<OsmModel>.Success(model))
        {
            Warnings = { "First warning", "Second warning" }
        };

        var context = CreateContext();
        context.RecordRequestTelemetry();

        var loader = new ModelLoader(ingestion);
        var result = await loader.LoadAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(model, context.Model);
        Assert.Equal(ingestion.Warnings, context.Warnings);

        var entries = context.Log.Build().Entries;
        Assert.Contains(entries, entry => entry.Step == "model.schema.warnings" && entry.Metadata["warnings.summary"] == "First warning");
        Assert.Contains(entries, entry => entry.Step == "model.ingested" && entry.Metadata["counts.modules"] == model.Modules.Length.ToString());
    }

    [Fact]
    public async Task ModelLoader_UsesInlineModelWhenProvided()
    {
        var inlineModel = ModelFixtures.LoadModel("model.edge-case.json");
        var warnings = ImmutableArray.Create("Inline warning");
        var ingestion = new FakeModelIngestionService(Result<OsmModel>.Success(inlineModel));
        var context = CreateContext(inlineModel: inlineModel, inlineWarnings: warnings);
        context.RecordRequestTelemetry();

        var loader = new ModelLoader(ingestion);
        var result = await loader.LoadAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(inlineModel, context.Model);
        Assert.Equal(warnings, context.Warnings);
        Assert.False(ingestion.LoadCalled);
    }

    [Fact]
    public async Task ModelLoader_PropagatesFailures()
    {
        var failure = ValidationError.Create("model.load.failed", "Model load failed.");
        var ingestion = new FakeModelIngestionService(Result<OsmModel>.Failure(failure));
        var context = CreateContext();
        context.RecordRequestTelemetry();

        var loader = new ModelLoader(ingestion);
        var result = await loader.LoadAsync(context, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "model.load.failed");
    }

    [Fact]
    public void ModuleFilterRunner_AppliesFilter()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var moduleName = model.Modules[0].Name.Value;
        var filterOptions = ModuleFilterOptions.Create(new[] { moduleName }, includeSystemModules: true, includeInactiveModules: true).Value;
        var context = CreateContext(filter: filterOptions);
        context.RecordRequestTelemetry();
        context.SetModel(model, Array.Empty<string>());

        var runner = new ModuleFilterRunner(new ModuleFilter());
        var result = runner.Run(context);

        Assert.True(result.IsSuccess);
        Assert.NotNull(context.FilteredModel);
        var filteredModule = Assert.Single(context.FilteredModel.Modules);
        Assert.Equal(moduleName, filteredModule.Name.Value);

        var filteredEntry = Assert.Single(context.Log.Build().Entries.Where(e => e.Step == "model.filtered"));
        Assert.Equal("1", filteredEntry.Metadata["counts.modules.filtered"]);
    }

    [Fact]
    public void ModuleFilterRunner_PropagatesFailures()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var filterOptions = ModuleFilterOptions.Create(new[] { "missing" }, includeSystemModules: true, includeInactiveModules: true).Value;
        var context = CreateContext(filter: filterOptions);
        context.RecordRequestTelemetry();
        context.SetModel(model, Array.Empty<string>());

        var runner = new ModuleFilterRunner(new ModuleFilter());
        var result = runner.Run(context);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "modelFilter.modules.missing");
    }

    [Fact]
    public async Task SupplementalLoader_LoadsEntitiesAndLogs()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var context = CreateContext();
        context.RecordRequestTelemetry();
        context.SetModel(model, Array.Empty<string>());
        context.SetFilteredModel(model);

        var loader = new SupplementalLoader(new SupplementalEntityLoader());
        var result = await loader.LoadAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var supplementalEntry = Assert.Single(context.Log.Build().Entries.Where(e => e.Step == "supplemental.loaded"));
        Assert.Equal(
            context.SupplementalEntities.Length.ToString(CultureInfo.InvariantCulture),
            supplementalEntry.Metadata["counts.entities.supplemental"]);
        Assert.Equal(
            context.Request.SupplementalModels.Paths.Count.ToString(CultureInfo.InvariantCulture),
            supplementalEntry.Metadata["counts.supplemental.paths"]);
    }

    [Fact]
    public async Task SupplementalLoader_PropagatesFailures()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var supplemental = new SupplementalModelOptions(false, new[] { "missing.json" });
        var context = CreateContext(supplemental: supplemental);
        context.RecordRequestTelemetry();
        context.SetModel(model, Array.Empty<string>());
        context.SetFilteredModel(model);

        var loader = new SupplementalLoader(new SupplementalEntityLoader());
        var result = await loader.LoadAsync(context, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "pipeline.supplemental.path.missing");
    }

    [Fact]
    public async Task ProfilerRunner_CapturesProfileAndGeneratesInsights()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var profile = ProfileFixtures.LoadSnapshot("profiling/profile.edge-case.json");
        var insightGenerator = new FakeInsightGenerator();
        var context = CreateContext(profileCapture: (_, _) => Task.FromResult(Result<ProfileCaptureResult>.Success(new ProfileCaptureResult(profile, MultiEnvironmentProfileReport.Empty))));
        context.RecordRequestTelemetry();
        context.SetModel(model, Array.Empty<string>());
        context.SetFilteredModel(model);

        var runner = new ProfilerRunner(insightGenerator);
        var result = await runner.RunAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(profile, context.Profile);
        Assert.Equal(insightGenerator.GeneratedInsights, context.Insights);

        var entries = context.Log.Build().Entries;
        Assert.Contains(entries, entry => entry.Step == "profiling.capture.start");
        Assert.Contains(entries, entry => entry.Step == "profiling.capture.completed" && entry.Metadata["counts.profiles.columns"] == profile.Columns.Length.ToString());
    }

    [Fact]
    public async Task ProfilerRunner_PropagatesFailures()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var context = CreateContext(profileCapture: (_, _) => Task.FromResult(Result<ProfileCaptureResult>.Failure(ValidationError.Create("profiling.failed", "failed"))));
        context.RecordRequestTelemetry();
        context.SetModel(model, Array.Empty<string>());
        context.SetFilteredModel(model);

        var runner = new ProfilerRunner(new FakeInsightGenerator());
        var result = await runner.RunAsync(context, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, error => error.Code == "profiling.failed");
    }

    private static BootstrapPipelineContext CreateContext(
        ModuleFilterOptions? filter = null,
        SupplementalModelOptions? supplemental = null,
        Func<OsmModel, CancellationToken, Task<Result<ProfileCaptureResult>>>? profileCapture = null,
        OsmModel? inlineModel = null,
        ImmutableArray<string> inlineWarnings = default)
    {
        var telemetry = new PipelineBootstrapTelemetry(
            "Request received",
            new PipelineLogMetadataBuilder().Build(),
            "Profiling started",
            new PipelineLogMetadataBuilder().Build(),
            "Profiling completed");

        var request = new PipelineBootstrapRequest(
            "model.json",
            filter ?? ModuleFilterOptions.IncludeAll,
            supplemental ?? SupplementalModelOptions.Default,
            telemetry,
            profileCapture ?? ((_, _) => Task.FromResult(Result<ProfileCaptureResult>.Failure(ValidationError.Create("profiling.unused", "Profiling not configured for test.")))),
            PipelineBootstrapperTestDefaults.SqlOptions,
            inlineModel,
            inlineWarnings);

        return new BootstrapPipelineContext(new PipelineExecutionLogBuilder(), request);
    }
}

public sealed class PipelineBootstrapperIntegrationTests
{
    [Fact]
    public async Task BootstrapAsync_ComposesStagesAndPreservesTelemetry()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var profile = ProfileFixtures.LoadSnapshot("profiling/profile.edge-case.json");
        var warnings = new[] { "Schema warning" };
        var ingestion = new FakeModelIngestionService(Result<OsmModel>.Success(model))
        {
            Warnings = { warnings[0] }
        };

        var insightGenerator = new FakeInsightGenerator();
        var bootstrapper = new PipelineBootstrapper(
            ingestion,
            new ModuleFilter(),
            new SupplementalEntityLoader(),
            insightGenerator);

        var telemetry = new PipelineBootstrapTelemetry(
            "Bootstrap request received",
            new PipelineLogMetadataBuilder()
                .WithFlag("test.enabled", true)
                .Build(),
            "Profiling start",
            new PipelineLogMetadataBuilder()
                .WithValue("phase", "profiling")
                .Build(),
            "Profiling done");

        var filterOptions = ModuleFilterOptions.IncludeAll;
        var request = new PipelineBootstrapRequest(
            "model.json",
            filterOptions,
            SupplementalModelOptions.Default,
            telemetry,
            (_, _) => Task.FromResult(Result<ProfileCaptureResult>.Success(new ProfileCaptureResult(profile, MultiEnvironmentProfileReport.Empty))),
            PipelineBootstrapperTestDefaults.SqlOptions,
            InlineModel: null,
            ModelWarnings: default);

        var logBuilder = new PipelineExecutionLogBuilder();
        var result = await bootstrapper.BootstrapAsync(logBuilder, request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var context = result.Value;
        Assert.Same(profile, context.Profile);
        Assert.Equal(insightGenerator.GeneratedInsights, context.Insights);
        Assert.Equal(warnings, context.Warnings);

        var entries = logBuilder.Build().Entries;
        Assert.Equal(new[]
        {
            "request.received",
            "model.schema.warnings",
            "model.ingested",
            "model.filtered",
            "supplemental.loaded",
            "profiling.capture.start",
            "profiling.capture.completed"
        }, entries.Select(entry => entry.Step));

        var warningEntry = entries.First(entry => entry.Step == "model.schema.warnings");
        Assert.Equal("Schema warning", warningEntry.Metadata["warnings.summary"]);
    }
}

internal static class PipelineBootstrapperTestDefaults
{
    public static readonly ResolvedSqlOptions SqlOptions = new(
        ConnectionString: "Server=(local);Database=OSM",
        CommandTimeoutSeconds: null,
        Sampling: new SqlSamplingSettings(null, null),
        Authentication: new SqlAuthenticationSettings(null, null, "osm-tests", null),
        MetadataContract: MetadataContractOverrides.Strict,
        ProfilingConnectionStrings: ImmutableArray<string>.Empty,
        TableNameMappings: ImmutableArray<TableNameMappingConfiguration>.Empty);
}

internal sealed class FakeModelIngestionService : IModelIngestionService
{
    private readonly Result<OsmModel> _result;

    public FakeModelIngestionService(Result<OsmModel> result)
    {
        _result = result;
    }

    public List<string> Warnings { get; } = new();

    public bool LoadCalled { get; private set; }

    public Task<Result<OsmModel>> LoadFromFileAsync(
        string path,
        ICollection<string>? warnings = null,
        CancellationToken cancellationToken = default,
        ModelIngestionOptions? options = null)
    {
        LoadCalled = true;
        if (warnings is not null)
        {
            foreach (var warning in Warnings)
            {
                warnings.Add(warning);
            }
        }

        return Task.FromResult(_result);
    }
}

internal sealed class FakeInsightGenerator : IProfilingInsightGenerator
{
    public ImmutableArray<ProfilingInsight> GeneratedInsights { get; private set; } = ImmutableArray<ProfilingInsight>.Empty;

    public ImmutableArray<ProfilingInsight> Generate(ProfileSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        var firstColumn = snapshot.Columns.IsDefaultOrEmpty
            ? null
            : snapshot.Columns[0];

        ProfilingInsightCoordinate? coordinate = null;
        if (firstColumn is not null)
        {
            var coordinateResult = ProfilingInsightCoordinate.Create(firstColumn.Schema, firstColumn.Table, firstColumn.Column);
            coordinate = coordinateResult.IsSuccess ? coordinateResult.Value : null;
        }
        var insight = ProfilingInsight.Create(
            ProfilingInsightSeverity.Info,
            ProfilingInsightCategory.Evidence,
            "Generated test insight.",
            coordinate).Value;

        GeneratedInsights = ImmutableArray.Create(insight);
        return GeneratedInsights;
    }
}
