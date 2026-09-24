using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Emission;
using Osm.Emission.Formatting;
using Osm.Emission.Seeds;
using Osm.Pipeline.Evidence;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.DynamicData;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;
using Osm.Json;
using Osm.Smo;
using Osm.Validation.Tightening;
using Osm.Validation.Tightening.Opportunities;
using OpportunitiesReport = Osm.Validation.Tightening.Opportunities.OpportunitiesReport;
using ValidationReport = Osm.Validation.Tightening.Validations.ValidationReport;
using Osm.Validation.Profiling;
using Tests.Support;
using Xunit;
using Osm.Pipeline.Configuration;

namespace Osm.Pipeline.Tests;

public class BuildSsdtPipelineStepTests
{
    [Fact]
    public async Task BootstrapStep_populates_state_and_logs_request()
    {
        using var output = new TempDirectory();
        var request = CreateRequest(output.Path);
        var initial = new PipelineInitialized(request, new PipelineExecutionLogBuilder(TimeProvider.System));
        var step = new BuildSsdtBootstrapStep(CreatePipelineBootstrapper(), CreateProfilerFactory());

        var result = await step.ExecuteAsync(initial);

        Assert.True(result.IsSuccess);
        var state = result.Value;
        Assert.NotNull(state.Bootstrap);
        Assert.NotNull(state.Bootstrap.Profile);
        Assert.NotNull(state.Bootstrap.FilteredModel);
        Assert.False(state.Bootstrap.Insights.IsDefault);
        var log = state.Log.Build();
        Assert.Contains(log.Entries, entry => entry.Step == "request.received");
        Assert.Contains(log.Entries, entry => entry.Step == "profiling.capture.completed");
    }

    [Fact]
    public async Task EvidenceCacheStep_persists_result_and_records_metadata()
    {
        using var output = new TempDirectory();
        using var cacheDirectory = new TempDirectory();
        var manifest = new EvidenceCacheManifest(
            Version: "1.0",
            Key: "key",
            Command: "build-ssdt",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            LastValidatedAtUtc: DateTimeOffset.UtcNow,
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddDays(1),
            ModuleSelection: EvidenceCacheModuleSelection.Empty,
            Metadata: new Dictionary<string, string?>(StringComparer.Ordinal),
            Artifacts: new List<EvidenceCacheArtifact>());
        var evaluation = new EvidenceCacheEvaluation(
            EvidenceCacheOutcome.Created,
            EvidenceCacheInvalidationReason.ManifestMissing,
            DateTimeOffset.UtcNow,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["cacheOutcome"] = EvidenceCacheOutcome.Created.ToString(),
            });
        var cacheResult = new EvidenceCacheResult(cacheDirectory.Path, manifest, evaluation);
        var cacheService = new FakeEvidenceCacheService(Result<EvidenceCacheResult>.Success(cacheResult));

        var cacheOptions = new EvidenceCachePipelineOptions(
            cacheDirectory.Path,
            Refresh: false,
            Command: "build-ssdt",
            ModelPath: FixtureFile.GetPath("model.edge-case.json"),
            ProfilePath: FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json")),
            DmmPath: null,
            ConfigPath: null,
            Metadata: new Dictionary<string, string?>());

        var request = CreateRequest(output.Path, cacheOptions: cacheOptions);
        var initial = new PipelineInitialized(request, new PipelineExecutionLogBuilder(TimeProvider.System));
        var bootstrapStep = new BuildSsdtBootstrapStep(CreatePipelineBootstrapper(), CreateProfilerFactory());
        var bootstrapState = (await bootstrapStep.ExecuteAsync(initial)).Value;
        var coordinator = new EvidenceCacheCoordinator(cacheService);
        var step = new BuildSsdtEvidenceCacheStep(coordinator);

        var result = await step.ExecuteAsync(bootstrapState);

        Assert.True(result.IsSuccess);
        var state = result.Value;
        Assert.Equal(cacheResult, state.EvidenceCache);
        var log = state.Log.Build();
        Assert.Contains(log.Entries, entry => entry.Step == "evidence.cache.requested");
        Assert.Contains(log.Entries, entry => entry.Step == "evidence.cache.persisted" || entry.Step == "evidence.cache.reused");
    }

    [Fact]
    public async Task PolicyStep_synthesizes_decision_report()
    {
        using var output = new TempDirectory();
        var request = CreateRequest(output.Path);
        var initial = new PipelineInitialized(request, new PipelineExecutionLogBuilder(TimeProvider.System));
        var bootstrapStep = new BuildSsdtBootstrapStep(CreatePipelineBootstrapper(), CreateProfilerFactory());
        var bootstrapState = (await bootstrapStep.ExecuteAsync(initial)).Value;
        var evidenceState = new EvidencePrepared(
            bootstrapState.Request,
            bootstrapState.Log,
            bootstrapState.Bootstrap,
            EvidenceCache: null);
        var step = new BuildSsdtPolicyDecisionStep(new TighteningPolicy(), new TighteningOpportunitiesAnalyzer());

        var result = await step.ExecuteAsync(evidenceState);

        Assert.True(result.IsSuccess);
        var state = result.Value;
        Assert.NotNull(state.Report);
        Assert.True(state.Report.ColumnCount > 0);
        var log = state.Log.Build();
        Assert.Contains(log.Entries, entry => entry.Step == "policy.decisions.synthesized");
    }

    [Fact]
    public async Task EmissionStep_writes_manifest_and_decision_log()
    {
        using var output = new TempDirectory();
        var request = CreateRequest(output.Path);
        var initial = new PipelineInitialized(request, new PipelineExecutionLogBuilder(TimeProvider.System));
        var bootstrapStep = new BuildSsdtBootstrapStep(CreatePipelineBootstrapper(), CreateProfilerFactory());
        var bootstrapState = (await bootstrapStep.ExecuteAsync(initial)).Value;
        var evidenceState = new EvidencePrepared(
            bootstrapState.Request,
            bootstrapState.Log,
            bootstrapState.Bootstrap,
            EvidenceCache: null);
        var policyStep = new BuildSsdtPolicyDecisionStep(new TighteningPolicy(), new TighteningOpportunitiesAnalyzer());
        var decisionState = (await policyStep.ExecuteAsync(evidenceState)).Value;
        var step = new BuildSsdtEmissionStep(
            new SmoModelFactory(),
            new SsdtEmitter(),
            new PolicyDecisionLogWriter(),
            new EmissionFingerprintCalculator(),
            new OpportunityLogWriter());

        var result = await step.ExecuteAsync(decisionState);

        Assert.True(result.IsSuccess);
        var state = result.Value;
        Assert.NotNull(state.Manifest);
        Assert.False(state.Manifest.Tables.Count == 0);
        Assert.NotNull(state.DecisionLogPath);
        Assert.True(File.Exists(Path.Combine(output.Path, "manifest.json")));
        Assert.True(File.Exists(state.DecisionLogPath));
        var log = state.Log.Build();
        Assert.Contains(log.Entries, entry => entry.Step == "ssdt.emission.completed");
        Assert.Contains(log.Entries, entry => entry.Step == "policy.log.persisted");
    }

    [Fact]
    public async Task StaticSeedStep_generates_seed_scripts()
    {
        using var output = new TempDirectory();
        var request = CreateRequest(output.Path, staticDataProvider: new EchoStaticEntityDataProvider());
        var initial = new PipelineInitialized(request, new PipelineExecutionLogBuilder(TimeProvider.System));
        var bootstrapStep = new BuildSsdtBootstrapStep(CreatePipelineBootstrapper(), CreateProfilerFactory());
        var bootstrapState = (await bootstrapStep.ExecuteAsync(initial)).Value;
        var evidenceState = new EvidencePrepared(
            bootstrapState.Request,
            bootstrapState.Log,
            bootstrapState.Bootstrap,
            EvidenceCache: null);
        var policyStep = new BuildSsdtPolicyDecisionStep(new TighteningPolicy(), new TighteningOpportunitiesAnalyzer());
        var decisionState = (await policyStep.ExecuteAsync(evidenceState)).Value;
        var emissionStep = new BuildSsdtEmissionStep(
            new SmoModelFactory(),
            new SsdtEmitter(),
            new PolicyDecisionLogWriter(),
            new EmissionFingerprintCalculator(),
            new OpportunityLogWriter());
        var emissionState = (await emissionStep.ExecuteAsync(decisionState)).Value;
        var sqlProjectStep = new BuildSsdtSqlProjectStep();
        var projectState = (await sqlProjectStep.ExecuteAsync(emissionState)).Value;
        var validationStep = new BuildSsdtSqlValidationStep();
        var validatedState = (await validationStep.ExecuteAsync(projectState)).Value;
        var step = new BuildSsdtStaticSeedStep(CreateSeedGenerator());

        var result = await step.ExecuteAsync(validatedState);

        Assert.True(result.IsSuccess);
        var state = result.Value;
        Assert.False(state.StaticSeedScriptPaths.IsDefaultOrEmpty);
        Assert.All(state.StaticSeedScriptPaths, path => Assert.True(File.Exists(path)));
        var log = state.Log.Build();
        Assert.Contains(log.Entries, entry => entry.Step == "staticData.seed.generated");
        Assert.Contains(log.Entries, entry => entry.Step == "staticData.seed.preflight");
    }

    [Fact]
    public async Task StaticSeedStep_OrdersTablesByForeignKeyDependencies()
    {
        using var output = new TempDirectory();
        var request = CreateForeignKeyRequest(output.Path);
        var logBuilder = new PipelineExecutionLogBuilder(TimeProvider.System);
        var bootstrap = CreateForeignKeyBootstrapContext(request, logBuilder);

        var policyDecisions = PolicyDecisionSet.Create(
            ImmutableDictionary<ColumnCoordinate, NullabilityDecision>.Empty,
            ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision>.Empty,
            ImmutableDictionary<IndexCoordinate, UniqueIndexDecision>.Empty,
            ImmutableArray<TighteningDiagnostic>.Empty,
            ImmutableDictionary<ColumnCoordinate, ColumnIdentity>.Empty,
            ImmutableDictionary<IndexCoordinate, string>.Empty,
            TighteningOptions.Default);

        var toggles = TighteningToggleSnapshot.Create(TighteningOptions.Default, _ => null);
        var decisionReport = new PolicyDecisionReport(
            ImmutableArray<ColumnDecisionReport>.Empty,
            ImmutableArray<UniqueIndexDecisionReport>.Empty,
            ImmutableArray<ForeignKeyDecisionReport>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableArray<TighteningDiagnostic>.Empty,
            ImmutableDictionary<string, ModuleDecisionRollup>.Empty,
            ImmutableDictionary<string, ToggleExportValue>.Empty,
            ImmutableDictionary<string, string>.Empty,
            ImmutableDictionary<string, string>.Empty,
            toggles);

        var opportunities = new OpportunitiesReport(
            ImmutableArray<Opportunity>.Empty,
            ImmutableDictionary<OpportunityDisposition, int>.Empty,
            ImmutableDictionary<OpportunityCategory, int>.Empty,
            ImmutableDictionary<OpportunityType, int>.Empty,
            ImmutableDictionary<RiskLevel, int>.Empty,
            DateTimeOffset.UtcNow);

        var validations = ValidationReport.Empty(DateTimeOffset.UtcNow);

        var manifest = new SsdtManifest(
            Array.Empty<TableManifestEntry>(),
            new SsdtManifestOptions(false, false, true, 1),
            null,
            new SsdtEmissionMetadata("sha256", "hash"),
            Array.Empty<PreRemediationManifestEntry>(),
            SsdtCoverageSummary.CreateComplete(0, 0, 0),
            SsdtPredicateCoverage.Empty,
            Array.Empty<string>());

        var decisionLogPath = Path.Combine(output.Path, "decision-log.json");
        await File.WriteAllTextAsync(decisionLogPath, "{}");
        var opportunitiesPath = Path.Combine(output.Path, "opportunities.json");
        await File.WriteAllTextAsync(opportunitiesPath, "{}");
        var validationsPath = Path.Combine(output.Path, "validations.json");
        await File.WriteAllTextAsync(validationsPath, "{}");
        var safePath = Path.Combine(output.Path, "safe.sql");
        await File.WriteAllTextAsync(safePath, "PRINT 1;");
        var remediationPath = Path.Combine(output.Path, "remediation.sql");
        await File.WriteAllTextAsync(remediationPath, "PRINT 2;");

        var opportunityArtifacts = new OpportunityArtifacts(opportunitiesPath, validationsPath, safePath, "PRINT 1;", remediationPath, "PRINT 2;");

        var state = new SqlValidated(
            request,
            logBuilder,
            bootstrap,
            EvidenceCache: null,
            policyDecisions,
            decisionReport,
            opportunities,
            validations,
            ImmutableArray<PipelineInsight>.Empty,
            manifest,
            decisionLogPath,
            opportunityArtifacts,
            Path.Combine(output.Path, "OutSystemsModel.sqlproj"),
            SsdtSqlValidationSummary.Empty);

        var step = new BuildSsdtStaticSeedStep(CreateSeedGenerator());

        var result = await step.ExecuteAsync(state);

        Assert.True(result.IsSuccess);
        var seeds = result.Value;
        Assert.True(seeds.StaticSeedTopologicalOrderApplied);
        Assert.Equal(2, seeds.StaticSeedData.Length);

        var parentIndex = seeds.StaticSeedData
            .Select((table, index) => (table, index))
            .First(pair => string.Equals(pair.table.Definition.LogicalName, "Parent", StringComparison.OrdinalIgnoreCase)).index;
        var childIndex = seeds.StaticSeedData
            .Select((table, index) => (table, index))
            .First(pair => string.Equals(pair.table.Definition.LogicalName, "Child", StringComparison.OrdinalIgnoreCase)).index;
        Assert.True(parentIndex < childIndex);

        var moduleSeedPath = Assert.Single(seeds.StaticSeedScriptPaths);
        var script = await File.ReadAllTextAsync(moduleSeedPath);
        Assert.True(
            script.IndexOf("-- Entity: Parent", StringComparison.Ordinal) <
            script.IndexOf("-- Entity: Child", StringComparison.Ordinal));
        var log = seeds.Log.Build();
        Assert.Contains(log.Entries, entry => entry.Step == "staticData.seed.preflight");
    }

    [Fact]
    public async Task StaticSeedStep_emits_master_seed_when_enabled()
    {
        using var output = new TempDirectory();
        var request = CreateRequest(output.Path, staticDataProvider: new EchoStaticEntityDataProvider());

        var defaults = request.Scope.TighteningOptions;
        var staticSeedOptions = StaticSeedOptions.Create(
            groupByModule: true,
            emitMasterFile: true,
            defaults.Emission.StaticSeeds.SynchronizationMode).Value;
        var emission = EmissionOptions.Create(
            defaults.Emission.PerTableFiles,
            defaults.Emission.IncludePlatformAutoIndexes,
            defaults.Emission.SanitizeModuleNames,
            defaults.Emission.EmitTableMode,
            defaults.Emission.EmitTableHeaders,
            defaults.Emission.ModuleParallelism,
            defaults.Emission.NamingOverrides,
            staticSeedOptions).Value;
        var tightening = TighteningOptions.Create(
            defaults.Policy,
            defaults.ForeignKeys,
            defaults.Uniqueness,
            defaults.Remediation,
            emission,
            defaults.Mocking).Value;

        request = request with
        {
            Scope = request.Scope with
            {
                TighteningOptions = tightening,
                SmoOptions = SmoBuildOptions.FromEmission(emission)
            }
        };

        var initial = new PipelineInitialized(request, new PipelineExecutionLogBuilder(TimeProvider.System));
        var bootstrapStep = new BuildSsdtBootstrapStep(CreatePipelineBootstrapper(), CreateProfilerFactory());
        var bootstrapState = (await bootstrapStep.ExecuteAsync(initial)).Value;
        var evidenceState = new EvidencePrepared(
            bootstrapState.Request,
            bootstrapState.Log,
            bootstrapState.Bootstrap,
            EvidenceCache: null);
        var policyStep = new BuildSsdtPolicyDecisionStep(new TighteningPolicy(), new TighteningOpportunitiesAnalyzer());
        var decisionState = (await policyStep.ExecuteAsync(evidenceState)).Value;
        var emissionStep = new BuildSsdtEmissionStep(
            new SmoModelFactory(),
            new SsdtEmitter(),
            new PolicyDecisionLogWriter(),
            new EmissionFingerprintCalculator(),
            new OpportunityLogWriter());
        var emissionState = (await emissionStep.ExecuteAsync(decisionState)).Value;
        var sqlProjectStep = new BuildSsdtSqlProjectStep();
        var projectState = (await sqlProjectStep.ExecuteAsync(emissionState)).Value;
        var validationStep = new BuildSsdtSqlValidationStep();
        var validatedState = (await validationStep.ExecuteAsync(projectState)).Value;
        var step = new BuildSsdtStaticSeedStep(CreateSeedGenerator());

        var result = await step.ExecuteAsync(validatedState);

        Assert.True(result.IsSuccess);
        var state = result.Value;
        Assert.Contains(Path.Combine(output.Path, "BaselineSeeds", "StaticEntities.seed.sql"), state.StaticSeedScriptPaths);
        Assert.True(state.StaticSeedScriptPaths.Length >= 2);
    }

    [Fact]
    public async Task StaticSeedStep_disambiguates_colliding_sanitized_module_names()
    {
        using var output = new TempDirectory();
        var request = CreateRequest(output.Path, staticDataProvider: new CollidingStaticEntityDataProvider());
        var initial = new PipelineInitialized(request, new PipelineExecutionLogBuilder(TimeProvider.System));
        var bootstrapStep = new BuildSsdtBootstrapStep(CreatePipelineBootstrapper(), CreateProfilerFactory());
        var bootstrapState = (await bootstrapStep.ExecuteAsync(initial)).Value;
        var evidenceState = new EvidencePrepared(
            bootstrapState.Request,
            bootstrapState.Log,
            bootstrapState.Bootstrap,
            EvidenceCache: null);
        var policyStep = new BuildSsdtPolicyDecisionStep(new TighteningPolicy(), new TighteningOpportunitiesAnalyzer());
        var decisionState = (await policyStep.ExecuteAsync(evidenceState)).Value;
        var emissionStep = new BuildSsdtEmissionStep(
            new SmoModelFactory(),
            new SsdtEmitter(),
            new PolicyDecisionLogWriter(),
            new EmissionFingerprintCalculator(),
            new OpportunityLogWriter());
        var emissionState = (await emissionStep.ExecuteAsync(decisionState)).Value;
        var sqlProjectStep = new BuildSsdtSqlProjectStep();
        var projectState = (await sqlProjectStep.ExecuteAsync(emissionState)).Value;
        var validationStep = new BuildSsdtSqlValidationStep();
        var validatedState = (await validationStep.ExecuteAsync(projectState)).Value;
        var step = new BuildSsdtStaticSeedStep(CreateSeedGenerator());

        var result = await step.ExecuteAsync(validatedState);

        Assert.True(result.IsSuccess);
        var state = result.Value;
        Assert.Equal(2, state.StaticSeedScriptPaths.Length);
        var moduleFolders = state.StaticSeedScriptPaths
            .Select(path => Path.GetFileName(Path.GetDirectoryName(path)!))
            .ToArray();
        Assert.False(string.Equals(moduleFolders[0], moduleFolders[1], StringComparison.OrdinalIgnoreCase));
        Assert.Contains(moduleFolders, folder => string.Equals(folder, "Module_Alpha", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(moduleFolders, folder => string.Equals(folder, "Module_Alpha_2", StringComparison.OrdinalIgnoreCase));

        var log = state.Log.Build();
        var remapEntry = Assert.Single(log.Entries.Where(entry => entry.Step == "staticData.seed.moduleNameRemapped"));
        Assert.Equal("Module#Alpha", remapEntry.Metadata["module.originalName"]);
        Assert.Equal("Module_Alpha", remapEntry.Metadata["module.sanitizedName"]);
        Assert.Equal("Module_Alpha_2", remapEntry.Metadata["module.disambiguatedName"]);
    }

    [Fact]
    public async Task TelemetryPackagingStep_creates_archive_with_expected_artifacts()
    {
        using var output = new TempDirectory();
        var request = CreateRequest(output.Path, staticDataProvider: new EchoStaticEntityDataProvider());
        var initial = new PipelineInitialized(request, new PipelineExecutionLogBuilder(TimeProvider.System));
        var bootstrapStep = new BuildSsdtBootstrapStep(CreatePipelineBootstrapper(), CreateProfilerFactory());
        var bootstrapState = (await bootstrapStep.ExecuteAsync(initial)).Value;
        var evidenceState = new EvidencePrepared(
            bootstrapState.Request,
            bootstrapState.Log,
            bootstrapState.Bootstrap,
            EvidenceCache: null);
        var policyStep = new BuildSsdtPolicyDecisionStep(new TighteningPolicy(), new TighteningOpportunitiesAnalyzer());
        var decisionState = (await policyStep.ExecuteAsync(evidenceState)).Value;
        var emissionStep = new BuildSsdtEmissionStep(
            new SmoModelFactory(),
            new SsdtEmitter(),
            new PolicyDecisionLogWriter(),
            new EmissionFingerprintCalculator(),
            new OpportunityLogWriter());
        var emissionState = (await emissionStep.ExecuteAsync(decisionState)).Value;
        var sqlProjectStep = new BuildSsdtSqlProjectStep();
        var projectState = (await sqlProjectStep.ExecuteAsync(emissionState)).Value;
        var validationStep = new BuildSsdtSqlValidationStep();
        var validatedState = (await validationStep.ExecuteAsync(projectState)).Value;
        var staticSeedStep = new BuildSsdtStaticSeedStep(CreateSeedGenerator());
        var seedState = (await staticSeedStep.ExecuteAsync(validatedState)).Value;

        var literalFormatter = new SqlLiteralFormatter();
        var dynamicInsertStep = new BuildSsdtDynamicInsertStep(
            new DynamicEntityInsertGenerator(literalFormatter),
            new PhasedDynamicEntityInsertGenerator(literalFormatter));
        var dynamicState = (await dynamicInsertStep.ExecuteAsync(seedState)).Value;

        var bootstrapSnapshotStep = new BuildSsdtBootstrapSnapshotStep(
            new StaticSeedSqlBuilder(literalFormatter),
            new PhasedDynamicEntityInsertGenerator(literalFormatter));
        var bootstrapSnapshotState = (await bootstrapSnapshotStep.ExecuteAsync(dynamicState)).Value;

        var postDeploymentTemplateStep = new BuildSsdtPostDeploymentTemplateStep();
        var postDeploymentState = (await postDeploymentTemplateStep.ExecuteAsync(bootstrapSnapshotState)).Value;

        var step = new BuildSsdtTelemetryPackagingStep();
        var result = await step.ExecuteAsync(postDeploymentState);

        Assert.True(result.IsSuccess);
        var packaged = result.Value;
        var packagePath = Assert.Single(packaged.TelemetryPackagePaths);
        Assert.True(File.Exists(packagePath));

        using (var archive = ZipFile.OpenRead(packagePath))
        {
            var entries = archive.Entries.Select(entry => entry.FullName).ToArray();
            var expectedEntries = new[]
            {
                NormalizeRelative(output.Path, Path.Combine(output.Path, "manifest.json")),
                NormalizeRelative(output.Path, packaged.DecisionLogPath),
                NormalizeRelative(output.Path, packaged.OpportunityArtifacts.SafeScriptPath),
                NormalizeRelative(output.Path, packaged.OpportunityArtifacts.RemediationScriptPath),
            };

            foreach (var expected in expectedEntries)
            {
                Assert.Contains(expected, entries);
            }
        }

        var log = packaged.Log.Build();
        Assert.Contains(log.Entries, entry => entry.Step == "pipeline.execution");

        static string NormalizeRelative(string root, string path)
        {
            var absoluteRoot = Path.GetFullPath(root);
            var absolutePath = Path.GetFullPath(path);
            var relative = Path.GetRelativePath(absoluteRoot, absolutePath);
            if (relative.StartsWith("..", StringComparison.Ordinal))
            {
                return Path.GetFileName(absolutePath);
            }

            return relative.Replace('\\', '/');
        }
    }

    [Fact]
    public async Task DynamicInsertStep_is_deprecated_and_skips_emission()
    {
        using var output = new TempDirectory();
        var request = CreateRequest(output.Path, staticDataProvider: new EchoStaticEntityDataProvider());
        var initial = new PipelineInitialized(request, new PipelineExecutionLogBuilder(TimeProvider.System));
        var bootstrapStep = new BuildSsdtBootstrapStep(CreatePipelineBootstrapper(), CreateProfilerFactory());
        var bootstrapState = (await bootstrapStep.ExecuteAsync(initial)).Value;
        var evidenceState = new EvidencePrepared(
            bootstrapState.Request,
            bootstrapState.Log,
            bootstrapState.Bootstrap,
            EvidenceCache: null);
        var policyStep = new BuildSsdtPolicyDecisionStep(new TighteningPolicy(), new TighteningOpportunitiesAnalyzer());
        var decisionState = (await policyStep.ExecuteAsync(evidenceState)).Value;
        var emissionStep = new BuildSsdtEmissionStep(
            new SmoModelFactory(),
            new SsdtEmitter(),
            new PolicyDecisionLogWriter(),
            new EmissionFingerprintCalculator(),
            new OpportunityLogWriter());
        var emissionState = (await emissionStep.ExecuteAsync(decisionState)).Value;
        var sqlProjectStep = new BuildSsdtSqlProjectStep();
        var projectState = (await sqlProjectStep.ExecuteAsync(emissionState)).Value;
        var validationStep = new BuildSsdtSqlValidationStep();
        var validatedState = (await validationStep.ExecuteAsync(projectState)).Value;
        var staticSeedStep = new BuildSsdtStaticSeedStep(CreateSeedGenerator());
        var seedState = (await staticSeedStep.ExecuteAsync(validatedState)).Value;

        var dataset = CreateDynamicDataset();
        var singleFileSeedState = seedState with
        {
            Request = seedState.Request with
            {
                DynamicDataset = dataset,
                DynamicDatasetSource = DynamicDatasetSource.UserProvided,
                DynamicInsertOutputMode = DynamicInsertOutputMode.SingleFile
            }
        };

        var literalFormatter = new SqlLiteralFormatter();
        var dynamicInsertStep = new BuildSsdtDynamicInsertStep(
            new DynamicEntityInsertGenerator(literalFormatter),
            new PhasedDynamicEntityInsertGenerator(literalFormatter));
        var dynamicStateResult = await dynamicInsertStep.ExecuteAsync(singleFileSeedState);

        Assert.True(dynamicStateResult.IsSuccess);
        var dynamicState = dynamicStateResult.Value;
        Assert.Equal(DynamicInsertOutputMode.SingleFile, dynamicState.DynamicInsertOutputMode);
        Assert.Empty(dynamicState.DynamicInsertScriptPaths);
        Assert.False(dynamicState.DynamicInsertTopologicalOrderApplied);
    }

    [Fact]
    public async Task DynamicInsertStep_reports_skip_when_dataset_present()
    {
        using var output = new TempDirectory();
        var fixture = CreateSanitizedDynamicFixture();
        var seedState = CreateStaticSeedsGeneratedState(output.Path, fixture.Model, fixture.Dataset, fixture.NamingOverrides);
        var literalFormatter = new SqlLiteralFormatter();
        var step = new BuildSsdtDynamicInsertStep(
            new DynamicEntityInsertGenerator(literalFormatter),
            new PhasedDynamicEntityInsertGenerator(literalFormatter));

        var result = await step.ExecuteAsync(seedState);

        Assert.True(result.IsSuccess);
        var state = result.Value;
        Assert.False(state.DynamicInsertTopologicalOrderApplied);
        Assert.Empty(state.DynamicInsertScriptPaths);
    }

    [Fact]
    public async Task SqlValidationStep_records_summary_for_valid_scripts()
    {
        using var output = new TempDirectory();
        var request = CreateRequest(output.Path);
        var initial = new PipelineInitialized(request, new PipelineExecutionLogBuilder(TimeProvider.System));
        var bootstrapStep = new BuildSsdtBootstrapStep(CreatePipelineBootstrapper(), CreateProfilerFactory());
        var bootstrapState = (await bootstrapStep.ExecuteAsync(initial)).Value;
        var evidenceState = new EvidencePrepared(
            bootstrapState.Request,
            bootstrapState.Log,
            bootstrapState.Bootstrap,
            EvidenceCache: null);
        var policyStep = new BuildSsdtPolicyDecisionStep(new TighteningPolicy(), new TighteningOpportunitiesAnalyzer());
        var decisionState = (await policyStep.ExecuteAsync(evidenceState)).Value;
        var emissionStep = new BuildSsdtEmissionStep(
            new SmoModelFactory(),
            new SsdtEmitter(),
            new PolicyDecisionLogWriter(),
            new EmissionFingerprintCalculator(),
            new OpportunityLogWriter());
        var emissionState = (await emissionStep.ExecuteAsync(decisionState)).Value;
        var sqlProjectStep = new BuildSsdtSqlProjectStep();
        var projectStateResult = await sqlProjectStep.ExecuteAsync(emissionState);
        Assert.True(projectStateResult.IsSuccess);
        var projectState = projectStateResult.Value;
        var step = new BuildSsdtSqlValidationStep();

        var result = await step.ExecuteAsync(projectState);

        Assert.True(result.IsSuccess);
        var validated = result.Value;
        Assert.Equal(emissionState.Manifest.Tables.Count, validated.SqlValidation.TotalFiles);
        Assert.Equal(0, validated.SqlValidation.ErrorCount);
        var log = validated.Log.Build();
        Assert.Contains(log.Entries, entry => entry.Step == "ssdt.sql.validation.completed");
        Assert.DoesNotContain(log.Entries, entry => entry.Step == "ssdt.sql.validation.error");
    }

    [Fact]
    public async Task SqlValidationStep_returns_failure_when_scripts_invalid()
    {
        using var output = new TempDirectory();
        var request = CreateRequest(output.Path);
        var initial = new PipelineInitialized(request, new PipelineExecutionLogBuilder(TimeProvider.System));
        var bootstrapStep = new BuildSsdtBootstrapStep(CreatePipelineBootstrapper(), CreateProfilerFactory());
        var bootstrapState = (await bootstrapStep.ExecuteAsync(initial)).Value;
        var evidenceState = new EvidencePrepared(
            bootstrapState.Request,
            bootstrapState.Log,
            bootstrapState.Bootstrap,
            EvidenceCache: null);
        var policyStep = new BuildSsdtPolicyDecisionStep(new TighteningPolicy(), new TighteningOpportunitiesAnalyzer());
        var decisionState = (await policyStep.ExecuteAsync(evidenceState)).Value;
        var emissionStep = new BuildSsdtEmissionStep(
            new SmoModelFactory(),
            new SsdtEmitter(),
            new PolicyDecisionLogWriter(),
            new EmissionFingerprintCalculator(),
            new OpportunityLogWriter());
        var emissionState = (await emissionStep.ExecuteAsync(decisionState)).Value;
        Assert.NotEmpty(emissionState.Manifest.Tables);
        var firstTable = emissionState.Manifest.Tables[0];
        var artifactPath = Path.Combine(request.OutputDirectory, firstTable.TableFile.Replace('/', Path.DirectorySeparatorChar));
        await File.WriteAllTextAsync(artifactPath, "CREATE TABLE ???");
        var sqlProjectStep = new BuildSsdtSqlProjectStep();
        var projectStateResult = await sqlProjectStep.ExecuteAsync(emissionState);
        Assert.True(projectStateResult.IsSuccess);
        var projectState = projectStateResult.Value;
        var step = new BuildSsdtSqlValidationStep();

        var result = await step.ExecuteAsync(projectState);

        Assert.True(result.IsFailure);
        var log = emissionState.Log.Build();
        Assert.Contains(log.Entries, entry => entry.Step == "ssdt.sql.validation.completed");
        Assert.Contains(log.Entries, entry => entry.Step == "ssdt.sql.validation.error");
        var error = Assert.Single(result.Errors);
        Assert.Equal("pipeline.buildSsdt.sql.validationFailed", error.Code);
    }

    private static BuildSsdtPipelineRequest CreateRequest(
        string outputDirectory,
        EvidenceCachePipelineOptions? cacheOptions = null,
        IStaticEntityDataProvider? staticDataProvider = null)
    {
        var modelPath = FixtureFile.GetPath("model.edge-case.json");
        var profilePath = FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json"));
        var scope = new ModelExecutionScope(
            modelPath,
            ModuleFilterOptions.IncludeAll,
            SupplementalModelOptions.Default,
            TighteningOptions.Default,
            new ResolvedSqlOptions(
                ConnectionString: null,
                CommandTimeoutSeconds: null,
                Sampling: new SqlSamplingSettings(null, null),
                Authentication: new SqlAuthenticationSettings(null, null, null, null),
                MetadataContract: MetadataContractOverrides.Strict,
                ProfilingConnectionStrings: ImmutableArray<string>.Empty,
                TableNameMappings: ImmutableArray<TableNameMappingConfiguration>.Empty),
            SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission),
            TypeMappingPolicyLoader.LoadDefault(),
            profilePath);

        return new BuildSsdtPipelineRequest(
            scope,
            outputDirectory,
            "fixture",
            cacheOptions,
            DynamicDataset: DynamicEntityDataset.Empty,
            DynamicDatasetSource: DynamicDatasetSource.None,
            staticDataProvider,
            SeedOutputDirectoryHint: null,
            DynamicDataOutputDirectoryHint: null,
            SqlProjectPathHint: null,
            SqlMetadataLog: null);
    }

    private static DynamicEntityDataset CreateDynamicDataset()
    {
        var columns = ImmutableArray.Create(new StaticEntitySeedColumn(
            LogicalName: "Identifier",
            ColumnName: "ID",
            EmissionName: "ID",
            DataType: "int",
            Length: null,
            Precision: null,
            Scale: null,
            IsPrimaryKey: true,
            IsIdentity: true,
            IsNullable: false));

        var definition = new StaticEntitySeedTableDefinition(
            Module: "Core",
            LogicalName: "User",
            Schema: "dbo",
            PhysicalName: "OSUSR_CORE_USER",
            EffectiveName: "OSUSR_CORE_USER",
            Columns: columns);

        var rows = ImmutableArray.Create(
            StaticEntityRow.Create(new object?[] { 1 }),
            StaticEntityRow.Create(new object?[] { 2 }));
        var table = StaticEntityTableData.Create(definition, rows);
        return DynamicEntityDataset.Create(new[] { table });
    }

    private static StaticSeedsGenerated CreateStaticSeedsGeneratedState(
        string outputDirectory,
        OsmModel model,
        DynamicEntityDataset dataset,
        NamingOverrideOptions namingOverrides)
    {
        Directory.CreateDirectory(outputDirectory);
        var sqlProjectPath = Path.Combine(outputDirectory, "sqlproj");
        Directory.CreateDirectory(sqlProjectPath);

        var scope = new ModelExecutionScope(
            ModelPath: Path.Combine(outputDirectory, "model.json"),
            ModuleFilter: ModuleFilterOptions.IncludeAll,
            SupplementalModels: SupplementalModelOptions.Default,
            TighteningOptions: TighteningOptions.Default,
            SqlOptions: new ResolvedSqlOptions(
                ConnectionString: null,
                CommandTimeoutSeconds: null,
                Sampling: new SqlSamplingSettings(null, null),
                Authentication: new SqlAuthenticationSettings(null, null, null, null),
                MetadataContract: MetadataContractOverrides.Strict,
                ProfilingConnectionStrings: ImmutableArray<string>.Empty,
                TableNameMappings: ImmutableArray<TableNameMappingConfiguration>.Empty),
            SmoOptions: SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission).WithNamingOverrides(namingOverrides),
            TypeMappingPolicy: TypeMappingPolicyLoader.LoadDefault(),
            ProfilePath: null,
            BaselineProfilePath: null,
            InlineModel: model);

        var request = new BuildSsdtPipelineRequest(
            scope,
            outputDirectory,
            "fixture",
            EvidenceCache: null,
            DynamicDataset: dataset,
            DynamicDatasetSource: DynamicDatasetSource.UserProvided,
            StaticDataProvider: null,
            SeedOutputDirectoryHint: null,
            DynamicDataOutputDirectoryHint: null,
            SqlProjectPathHint: null);

        var log = new PipelineExecutionLogBuilder(TimeProvider.System);
        var profile = ProfileSnapshot.Create(
            Array.Empty<ColumnProfile>(),
            Array.Empty<UniqueCandidateProfile>(),
            Array.Empty<CompositeUniqueCandidateProfile>(),
            Array.Empty<ForeignKeyReality>()).Value;
        var bootstrap = new PipelineBootstrapContext(
            model,
            ImmutableArray<EntityModel>.Empty,
            profile,
            ImmutableArray<ProfilingInsight>.Empty,
            ImmutableArray<string>.Empty,
            MultiEnvironmentReport: null);

        var decisions = PolicyDecisionSet.Create(
            ImmutableDictionary<ColumnCoordinate, NullabilityDecision>.Empty,
            ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision>.Empty,
            ImmutableDictionary<IndexCoordinate, UniqueIndexDecision>.Empty,
            ImmutableArray<TighteningDiagnostic>.Empty,
            ImmutableDictionary<ColumnCoordinate, ColumnIdentity>.Empty,
            ImmutableDictionary<IndexCoordinate, string>.Empty,
            TighteningOptions.Default);
        var report = PolicyDecisionReporter.Create(decisions);

        var opportunities = new OpportunitiesReport(
            ImmutableArray<Opportunity>.Empty,
            ImmutableDictionary<OpportunityDisposition, int>.Empty,
            ImmutableDictionary<OpportunityCategory, int>.Empty,
            ImmutableDictionary<OpportunityType, int>.Empty,
            ImmutableDictionary<RiskLevel, int>.Empty,
            DateTimeOffset.UtcNow);
        var validations = ValidationReport.Empty(DateTimeOffset.UtcNow);
        var manifest = new SsdtManifest(
            Array.Empty<TableManifestEntry>(),
            new SsdtManifestOptions(false, false, false, 1),
            PolicySummary: null,
            Emission: new SsdtEmissionMetadata("test", "hash"),
            PreRemediation: Array.Empty<PreRemediationManifestEntry>(),
            Coverage: SsdtCoverageSummary.CreateComplete(0, 0, 0),
            PredicateCoverage: SsdtPredicateCoverage.Empty,
            Unsupported: Array.Empty<string>());

        var decisionLogPath = Path.Combine(outputDirectory, "decisions.json");
        var opportunityArtifacts = new OpportunityArtifacts(
            Path.Combine(outputDirectory, "opportunities.json"),
            Path.Combine(outputDirectory, "validations.json"),
            Path.Combine(outputDirectory, "safe.sql"),
            string.Empty,
            Path.Combine(outputDirectory, "remediation.sql"),
            string.Empty);

        return new StaticSeedsGenerated(
            request,
            log,
            bootstrap,
            EvidenceCache: null,
            decisions,
            report,
            opportunities,
            validations,
            ImmutableArray<PipelineInsight>.Empty,
            manifest,
            decisionLogPath,
            opportunityArtifacts,
            sqlProjectPath,
            SsdtSqlValidationSummary.Empty,
            ImmutableArray<string>.Empty,
            ImmutableArray<StaticEntityTableData>.Empty,
            StaticSeedTopologicalOrderApplied: true,
            StaticSeedOrderingMode: EntityDependencyOrderingMode.Alphabetical);
    }

    private static SanitizedDynamicFixture CreateSanitizedDynamicFixture()
    {
        var parentColumns = ImmutableArray.Create(new StaticEntitySeedColumn(
            LogicalName: "Id",
            ColumnName: "ID",
            EmissionName: "Id",
            DataType: "INT",
            Length: null,
            Precision: null,
            Scale: null,
            IsPrimaryKey: true,
            IsIdentity: false,
            IsNullable: false));

        var childColumns = ImmutableArray.Create(
            new StaticEntitySeedColumn(
                LogicalName: "Id",
                ColumnName: "ID",
                EmissionName: "Id",
                DataType: "INT",
                Length: null,
                Precision: null,
                Scale: null,
                IsPrimaryKey: true,
                IsIdentity: false,
                IsNullable: false),
            new StaticEntitySeedColumn(
                LogicalName: "ParentId",
                ColumnName: "PARENTID",
                EmissionName: "ParentId",
                DataType: "INT",
                Length: null,
                Precision: null,
                Scale: null,
                IsPrimaryKey: false,
                IsIdentity: false,
                IsNullable: false));

        var parentDefinition = new StaticEntitySeedTableDefinition(
            Module: "Sample",
            LogicalName: "Parent",
            Schema: "dbo",
            PhysicalName: "USR_SAMPLE_PARENT_SAN",
            EffectiveName: "USR_SAMPLE_PARENT_SAN",
            Columns: parentColumns);

        var childDefinition = new StaticEntitySeedTableDefinition(
            Module: "Sample",
            LogicalName: "Child",
            Schema: "dbo",
            PhysicalName: "USR_SAMPLE_CHILD_SAN",
            EffectiveName: "USR_SAMPLE_CHILD_SAN",
            Columns: childColumns);

        var dataset = DynamicEntityDataset.Create(new[]
        {
            StaticEntityTableData.Create(childDefinition, new[] { StaticEntityRow.Create(new object?[] { 10, 1 }) }),
            StaticEntityTableData.Create(parentDefinition, new[] { StaticEntityRow.Create(new object?[] { 1 }) })
        });

        var relationship = RelationshipModel.Create(
            new AttributeName("ParentId"),
            new EntityName("Parent"),
            new TableName("OSUSR_SAMPLE_PARENT"),
            deleteRuleCode: "Cascade",
            hasDatabaseConstraint: true,
            actualConstraints: new[]
            {
                RelationshipActualConstraint.Create(
                    "FK_CHILD_PARENT",
                    referencedSchema: "dbo",
                    referencedTable: "OSUSR_SAMPLE_PARENT",
                    onDeleteAction: "NO_ACTION",
                    onUpdateAction: "NO_ACTION",
                    new[] { RelationshipActualConstraintColumn.Create("PARENTID", "ParentId", "ID", "Id", 0) })
            }).Value;

        var parentEntity = EntityModel.Create(
            new ModuleName("Sample"),
            new EntityName("Parent"),
            new TableName("OSUSR_SAMPLE_PARENT"),
            new SchemaName("dbo"),
            catalog: null,
            isStatic: true,
            isExternal: false,
            isActive: true,
            attributes: new[] { CreateAttribute("Id", "ID", isIdentifier: true) }).Value;

        var childEntity = EntityModel.Create(
            new ModuleName("Sample"),
            new EntityName("Child"),
            new TableName("OSUSR_SAMPLE_CHILD"),
            new SchemaName("dbo"),
            catalog: null,
            isStatic: true,
            isExternal: false,
            isActive: true,
            attributes: new[]
            {
                CreateAttribute("Id", "ID", isIdentifier: true),
                CreateAttribute("ParentId", "PARENTID")
            },
            relationships: new[] { relationship }).Value;

        var module = ModuleModel.Create(new ModuleName("Sample"), isSystemModule: false, isActive: true, entities: new[]
        {
            parentEntity,
            childEntity
        }).Value;
        var model = OsmModel.Create(DateTime.UtcNow, new[] { module }).Value;

        var parentOverride = NamingOverrideRule.Create("dbo", "OSUSR_SAMPLE_PARENT", null, null, "USR_SAMPLE_PARENT_SAN").Value;
        var childOverride = NamingOverrideRule.Create("dbo", "OSUSR_SAMPLE_CHILD", null, null, "USR_SAMPLE_CHILD_SAN").Value;
        var namingOverrides = NamingOverrideOptions.Create(new[] { parentOverride, childOverride }).Value;

        return new SanitizedDynamicFixture(dataset, model, namingOverrides);
    }

    private sealed record SanitizedDynamicFixture(
        DynamicEntityDataset Dataset,
        OsmModel Model,
        NamingOverrideOptions NamingOverrides);

    private static IDataProfilerFactory CreateProfilerFactory()
    {
        return new DataProfilerFactory(
            new ProfileSnapshotDeserializer(),
            static (connectionString, options) => new SqlConnectionFactory(connectionString, options));
    }

    private static StaticEntitySeedScriptGenerator CreateSeedGenerator()
    {
        var literalFormatter = new SqlLiteralFormatter();
        var sqlBuilder = new StaticSeedSqlBuilder(literalFormatter);
        var templateService = new StaticEntitySeedTemplateService();
        return new StaticEntitySeedScriptGenerator(templateService, sqlBuilder);
    }

    private static PipelineBootstrapper CreatePipelineBootstrapper()
    {
        return new PipelineBootstrapper(
            new ModelIngestionService(new ModelJsonDeserializer()),
            new ModuleFilter(),
            new SupplementalEntityLoader(new ModelJsonDeserializer()),
            new ProfilingInsightGenerator());
    }

    private static BuildSsdtPipelineRequest CreateForeignKeyRequest(string outputDirectory)
    {
        var scope = new ModelExecutionScope(
            Path.Combine(outputDirectory, "model.json"),
            ModuleFilterOptions.IncludeAll,
            SupplementalModelOptions.Default,
            TighteningOptions.Default,
            new ResolvedSqlOptions(
                ConnectionString: null,
                CommandTimeoutSeconds: null,
                Sampling: new SqlSamplingSettings(null, null),
                Authentication: new SqlAuthenticationSettings(null, null, null, null),
                MetadataContract: MetadataContractOverrides.Strict,
                ProfilingConnectionStrings: ImmutableArray<string>.Empty,
                TableNameMappings: ImmutableArray<TableNameMappingConfiguration>.Empty),
            SmoBuildOptions.FromEmission(TighteningOptions.Default.Emission),
            TypeMappingPolicyLoader.LoadDefault());

        return new BuildSsdtPipelineRequest(
            scope,
            outputDirectory,
            "fixture",
            EvidenceCache: null,
            DynamicDataset: DynamicEntityDataset.Empty,
            DynamicDatasetSource: DynamicDatasetSource.None,
            StaticDataProvider: new ForeignKeyStaticEntityDataProvider(),
            SeedOutputDirectoryHint: null,
            DynamicDataOutputDirectoryHint: null,
            SqlProjectPathHint: null,
            SqlMetadataLog: null);
    }

    private static PipelineBootstrapContext CreateForeignKeyBootstrapContext(
        BuildSsdtPipelineRequest request,
        PipelineExecutionLogBuilder logBuilder)
    {
        var model = CreateForeignKeyModel();
        var profile = ProfileSnapshot.Create(
            Array.Empty<ColumnProfile>(),
            Array.Empty<UniqueCandidateProfile>(),
            Array.Empty<CompositeUniqueCandidateProfile>(),
            Array.Empty<ForeignKeyReality>()).Value;

        return new PipelineBootstrapContext(
            model,
            ImmutableArray<EntityModel>.Empty,
            profile,
            ImmutableArray<ProfilingInsight>.Empty,
            ImmutableArray<string>.Empty,
            MultiEnvironmentProfileReport.Empty);
    }

    private static OsmModel CreateForeignKeyModel()
    {
        var parentEntity = EntityModel.Create(
            new ModuleName("Sample"),
            new EntityName("Parent"),
            new TableName("OSUSR_SAMPLE_PARENT"),
            new SchemaName("dbo"),
            catalog: null,
            isStatic: true,
            isExternal: false,
            isActive: true,
            attributes: new[]
            {
                CreateAttribute("Id", "ID", isIdentifier: true),
                CreateAttribute("Name", "NAME")
            }).Value;

        var relationship = RelationshipModel.Create(
            new AttributeName("ParentId"),
            new EntityName("Parent"),
            new TableName("OSUSR_SAMPLE_PARENT"),
            deleteRuleCode: "Cascade",
            hasDatabaseConstraint: true,
            actualConstraints: new[]
            {
                RelationshipActualConstraint.Create(
                    "FK_CHILD_PARENT",
                    referencedSchema: "dbo",
                    referencedTable: "OSUSR_SAMPLE_PARENT",
                    onDeleteAction: "NO_ACTION",
                    onUpdateAction: "NO_ACTION",
                    new[]
                    {
                        RelationshipActualConstraintColumn.Create("PARENTID", "ParentId", "ID", "Id", 0)
                    })
            }).Value;

        var childEntity = EntityModel.Create(
            new ModuleName("Sample"),
            new EntityName("Child"),
            new TableName("OSUSR_SAMPLE_CHILD"),
            new SchemaName("dbo"),
            catalog: null,
            isStatic: true,
            isExternal: false,
            isActive: true,
            attributes: new[]
            {
                CreateAttribute("Id", "ID", isIdentifier: true),
                CreateAttribute("ParentId", "PARENTID"),
                CreateAttribute("Name", "NAME")
            },
            relationships: new[] { relationship }).Value;

        var module = ModuleModel.Create(new ModuleName("Sample"), isSystemModule: false, isActive: true, entities: new[] { parentEntity, childEntity }).Value;
        return OsmModel.Create(DateTime.UtcNow, new[] { module }).Value;
    }

    private sealed class FakeEvidenceCacheService : IEvidenceCacheService
    {
        private readonly Result<EvidenceCacheResult> _result;

        public FakeEvidenceCacheService(Result<EvidenceCacheResult> result)
        {
            _result = result;
        }

        public Task<Result<EvidenceCacheResult>> CacheAsync(EvidenceCacheRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }

    private sealed class EchoStaticEntityDataProvider : IStaticEntityDataProvider
    {
        public Task<Result<IReadOnlyList<StaticEntityTableData>>> GetDataAsync(
            IReadOnlyList<StaticEntitySeedTableDefinition> definitions,
            CancellationToken cancellationToken = default)
        {
            var tables = definitions
                .Select(definition => StaticEntityTableData.Create(
                    definition,
                    new[]
                    {
                        StaticEntityRow.Create(GenerateValues(definition))
                    }))
                .Cast<StaticEntityTableData>()
                .ToList();

            return Task.FromResult(Result<IReadOnlyList<StaticEntityTableData>>.Success(tables));
        }

        private static object?[] GenerateValues(StaticEntitySeedTableDefinition definition)
        {
            var values = new object?[definition.Columns.Length];
            for (var i = 0; i < definition.Columns.Length; i++)
            {
                values[i] = i == 0 ? 1 : $"Sample{i}";
            }

            return values;
        }
    }

    private sealed class CollidingStaticEntityDataProvider : IStaticEntityDataProvider
    {
        public Task<Result<IReadOnlyList<StaticEntityTableData>>> GetDataAsync(
            IReadOnlyList<StaticEntitySeedTableDefinition> definitions,
            CancellationToken cancellationToken = default)
        {
            if (definitions.Count == 0)
            {
                throw new InvalidOperationException("Fixture model must contain at least one static entity definition.");
            }

            var template = definitions[0];
            var tables = new[]
            {
                CreateTable(template, "Module Alpha", "Alpha"),
                CreateTable(template, "Module#Alpha", "Hash"),
            };

            return Task.FromResult(Result<IReadOnlyList<StaticEntityTableData>>.Success(tables));
        }

        private static StaticEntityTableData CreateTable(
            StaticEntitySeedTableDefinition template,
            string moduleName,
            string suffix)
        {
            var definition = template with
            {
                Module = moduleName,
                LogicalName = $"{template.LogicalName}_{suffix}",
                PhysicalName = $"{template.PhysicalName}_{suffix}",
                EffectiveName = $"{template.EffectiveName}_{suffix}"
            };

            return StaticEntityTableData.Create(
                definition,
                new[]
                {
                    StaticEntityRow.Create(GenerateValues(definition))
                });
        }

        private static object?[] GenerateValues(StaticEntitySeedTableDefinition definition)
        {
            var values = new object?[definition.Columns.Length];
            for (var i = 0; i < definition.Columns.Length; i++)
            {
                values[i] = i == 0 ? 1 : $"Sample{i}";
            }

            return values;
        }
    }

    private sealed class ForeignKeyStaticEntityDataProvider : IStaticEntityDataProvider
    {
        public Task<Result<IReadOnlyList<StaticEntityTableData>>> GetDataAsync(
            IReadOnlyList<StaticEntitySeedTableDefinition> definitions,
            CancellationToken cancellationToken = default)
        {
            var tables = definitions
                .OrderBy(definition => string.Equals(definition.LogicalName, "Parent", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenBy(definition => definition.LogicalName, StringComparer.OrdinalIgnoreCase)
                .Select(definition => StaticEntityTableData.Create(definition, CreateRows(definition)))
                .Cast<StaticEntityTableData>()
                .ToArray();

            Array.Reverse(tables);
            return Task.FromResult(Result<IReadOnlyList<StaticEntityTableData>>.Success(tables));
        }

        private static IEnumerable<StaticEntityRow> CreateRows(StaticEntitySeedTableDefinition definition)
        {
            var values = new object?[definition.Columns.Length];
            for (var i = 0; i < definition.Columns.Length; i++)
            {
                var column = definition.Columns[i];
                if (column.IsPrimaryKey || string.Equals(column.LogicalName, "ParentId", StringComparison.OrdinalIgnoreCase))
                {
                    values[i] = 1;
                }
                else
                {
                    values[i] = $"{definition.LogicalName}_{column.LogicalName}";
                }
            }

            yield return StaticEntityRow.Create(values);
        }
    }

    private static AttributeModel CreateAttribute(string logicalName, string columnName, bool isIdentifier = false)
    {
        return AttributeModel.Create(
            new AttributeName(logicalName),
            new ColumnName(columnName),
            dataType: "INT",
            isMandatory: isIdentifier,
            isIdentifier: isIdentifier,
            isAutoNumber: false,
            isActive: true,
            reality: new AttributeReality(null, null, null, null, IsPresentButInactive: false),
            metadata: AttributeMetadata.Empty,
            onDisk: AttributeOnDiskMetadata.Empty).Value;
    }
}
