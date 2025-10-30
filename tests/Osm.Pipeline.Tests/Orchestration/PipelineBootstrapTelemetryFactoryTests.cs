using System.Collections.Generic;
using System.Globalization;
using Osm.Domain.Configuration;
using Osm.Pipeline.Orchestration;
using Osm.Smo;
using Osm.Validation.Tightening;
using Xunit;

namespace Osm.Pipeline.Tests.Orchestration;

public class PipelineBootstrapTelemetryFactoryTests
{
    private static readonly ModuleFilterOptions FilterOptions = ModuleFilterOptions.Create(
        new[] { "Customer", "Account" },
        includeSystemModules: false,
        includeInactiveModules: true,
        entityFilters: new Dictionary<string, IReadOnlyList<string>>
        {
            ["Customer"] = new[] { "ActiveCustomers" }
        },
        validationOverrides: new Dictionary<string, ModuleValidationOverrideConfiguration>
        {
            ["Account"] = new(
                AllowMissingPrimaryKey: new[] { "Ledger" },
                AllowMissingPrimaryKeyForAll: false,
                AllowMissingSchema: new[] { "Draft" },
                AllowMissingSchemaForAll: false)
        }).Value;

    private static readonly SupplementalModelOptions SupplementalOptions = new(false, new[] { "users.json" });

    private static readonly TighteningOptions Tightening = TighteningOptions.Default;

    private static readonly SmoBuildOptions Smo = SmoBuildOptions.FromEmission(Tightening.Emission);

    [Fact]
    public void Create_ForCaptureProfile_IncludesFixtureMetadata()
    {
        var scope = ModelExecutionScope.Create(
            "model.json",
            FilterOptions,
            SupplementalOptions,
            Tightening,
            Smo);
        var descriptor = new PipelineCommandDescriptor(
            "Received profile capture request.",
            "Capturing profiling snapshot.",
            "Captured profiling snapshot.");
        var extras = new PipelineBootstrapTelemetryExtras(
            ProfilerProvider: "fixture",
            FixtureProfilePath: "fixtures/profile.json");

        var telemetry = new PipelineBootstrapTelemetryFactory()
            .Create(scope, descriptor, extras);

        Assert.Equal("model.json", telemetry.RequestMetadata["paths.model"]);
        Assert.Equal("true", telemetry.RequestMetadata["flags.moduleFilter.hasFilter"]);
        Assert.Equal("2", telemetry.RequestMetadata["counts.moduleFilter.modules"]);
        Assert.Equal("false", telemetry.RequestMetadata["flags.moduleFilter.includeSystemModules"]);
        Assert.Equal("true", telemetry.RequestMetadata["flags.moduleFilter.includeInactiveModules"]);
        Assert.Equal("1", telemetry.RequestMetadata["counts.moduleFilter.entityFilters"]);
        Assert.Equal("true", telemetry.RequestMetadata["flags.moduleFilter.hasValidationOverrides"]);
        Assert.Equal("false", telemetry.RequestMetadata["flags.supplemental.includeUsers"]);
        Assert.Equal("1", telemetry.RequestMetadata["counts.supplemental.paths"]);
        Assert.Equal("fixture", telemetry.RequestMetadata["profiling.provider"]);
        Assert.Equal("fixture", telemetry.ProfilingStartMetadata["profiling.provider"]);
        Assert.Equal("fixtures/profile.json", telemetry.ProfilingStartMetadata["paths.profiling.fixture"]);
    }

    [Fact]
    public void Create_ForBuildSsdt_IncludesTighteningAndEmissionMetadata()
    {
        var scope = ModelExecutionScope.Create(
            "model.json",
            FilterOptions,
            SupplementalOptions,
            Tightening,
            Smo);
        var descriptor = new PipelineCommandDescriptor(
            "Received build-ssdt pipeline request.",
            "Capturing profiling snapshot.",
            "Captured profiling snapshot.",
            IncludeSupplementalDetails: true,
            IncludeTighteningDetails: true,
            IncludeEmissionDetails: true);
        var extras = new PipelineBootstrapTelemetryExtras(
            ProfilerProvider: "fixture",
            ProfilePath: "profiling/profile.json",
            OutputPath: "output",
            DiffTarget: null);

        var telemetry = new PipelineBootstrapTelemetryFactory()
            .Create(scope, descriptor, extras);

        Assert.Equal("output", telemetry.RequestMetadata["paths.output"]);
        Assert.Equal(Tightening.Policy.Mode.ToString(), telemetry.RequestMetadata["tightening.mode"]);
        Assert.Equal(
            Tightening.Policy.NullBudget.ToString("0.###", CultureInfo.InvariantCulture),
            telemetry.RequestMetadata["metrics.tightening.nullBudget"]);
        Assert.Equal(Smo.IncludePlatformAutoIndexes ? "true" : "false", telemetry.RequestMetadata["flags.emission.includePlatformAutoIndexes"]);
        Assert.Equal(Smo.EmitBareTableOnly ? "true" : "false", telemetry.RequestMetadata["flags.emission.emitBareTableOnly"]);
        Assert.Equal(Smo.SanitizeModuleNames ? "true" : "false", telemetry.RequestMetadata["flags.emission.sanitizeModuleNames"]);
        Assert.Equal(Smo.ModuleParallelism.ToString(CultureInfo.InvariantCulture), telemetry.RequestMetadata["counts.emission.moduleParallelism"]);
        Assert.Equal("profiling/profile.json", telemetry.ProfilingStartMetadata["paths.profile"]);
    }

    [Fact]
    public void Create_ForDmmCompare_IncludesDiffTargetMetadata()
    {
        var scope = ModelExecutionScope.Create(
            "model.json",
            FilterOptions,
            SupplementalOptions,
            Tightening,
            Smo);
        var descriptor = new PipelineCommandDescriptor(
            "Received dmm-compare pipeline request.",
            "Loading profiling snapshot from fixtures.",
            "Loaded profiling snapshot.",
            IncludeSupplementalDetails: false,
            IncludeTighteningDetails: true,
            IncludeEmissionDetails: true);
        var extras = new PipelineBootstrapTelemetryExtras(
            ProfilePath: "profiling/profile.json",
            DiffTarget: PipelineDiffTarget.Create("baseline", "ssdt"));

        var telemetry = new PipelineBootstrapTelemetryFactory()
            .Create(scope, descriptor, extras);

        Assert.Equal("profiling/profile.json", telemetry.RequestMetadata["paths.profile"]);
        Assert.Equal("baseline", telemetry.RequestMetadata["paths.baseline"]);
        Assert.Equal("ssdt", telemetry.RequestMetadata["baseline.type"]);
        Assert.Equal("profiling/profile.json", telemetry.ProfilingStartMetadata["paths.profile"]);
    }

    [Fact]
    public void Create_ForTighteningAnalysis_IncludesProfileMetadata()
    {
        var scope = ModelExecutionScope.Create(
            "model.json",
            FilterOptions,
            SupplementalOptions,
            Tightening,
            smoOptions: null);
        var descriptor = new PipelineCommandDescriptor(
            "Received tightening analysis request.",
            "Loading profiling snapshot from disk.",
            "Loaded profiling snapshot from disk.",
            IncludeSupplementalDetails: true,
            IncludeTighteningDetails: true);
        var extras = new PipelineBootstrapTelemetryExtras(
            ProfilePath: "profiling/profile.json");

        var telemetry = new PipelineBootstrapTelemetryFactory()
            .Create(scope, descriptor, extras);

        Assert.Equal("profiling/profile.json", telemetry.RequestMetadata["paths.profile"]);
        Assert.Equal("profiling/profile.json", telemetry.ProfilingStartMetadata["paths.profile"]);
        Assert.Equal(Tightening.Policy.Mode.ToString(), telemetry.RequestMetadata["tightening.mode"]);
    }
}
