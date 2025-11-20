using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Emission;
using Osm.Emission.Seeds;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.DynamicData;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;
using Osm.Smo;
using Osm.Pipeline.UatUsers;
using Osm.Validation.Profiling;
using Osm.Validation.Tightening;
using Osm.Validation.Tightening.Opportunities;
using Osm.Validation.Tightening.Validations;
using Tests.Support;
using Xunit;
using OpportunitiesReport = Osm.Validation.Tightening.Opportunities.OpportunitiesReport;

namespace Osm.Pipeline.Tests;

public sealed class FullExportPipelineTests
{
    private static readonly string SafeScriptPath = Path.Combine(Path.GetTempPath(), "full-export-safe.sql");
    private static readonly string RemediationScriptPath = Path.Combine(Path.GetTempPath(), "full-export-remediation.sql");
    private static readonly string SeedScriptPath = Path.Combine(Path.GetTempPath(), "full-export-seed.sql");

    [Fact]
    public async Task HandleAsync_skips_apply_when_connection_missing()
    {
        var dispatcher = new StubCommandDispatcher();
        var schemaApplier = new FakeSchemaDataApplier();
        var orchestrator = new SchemaApplyOrchestrator(schemaApplier);
        var uatRunner = new RecordingUatUsersRunner();
        var pipeline = CreatePipeline(dispatcher, orchestrator, uatRunner);

        var (extractRequest, extractResult) = CreateExtractionArtifacts();
        var (captureRequest, captureResult) = CreateCaptureArtifacts();
        var (buildRequest, buildResult) = CreateBuildArtifacts();

        dispatcher.Register<ExtractModelPipelineRequest, ModelExtractionResult>((request, _) =>
        {
            Assert.Equal(extractRequest, request);
            return Task.FromResult(Result<ModelExtractionResult>.Success(extractResult));
        });
        dispatcher.Register<CaptureProfilePipelineRequest, CaptureProfilePipelineResult>((request, _) =>
        {
            Assert.Equal(captureRequest, request);
            return Task.FromResult(Result<CaptureProfilePipelineResult>.Success(captureResult));
        });
        dispatcher.Register<BuildSsdtPipelineRequest, BuildSsdtPipelineResult>((request, _) =>
        {
            Assert.Equal(buildRequest, request);
            return Task.FromResult(Result<BuildSsdtPipelineResult>.Success(buildResult));
        });

        var request = new FullExportPipelineRequest(extractRequest, captureRequest, buildRequest, SchemaApplyOptions.Disabled);
        var result = await pipeline.HandleAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Null(schemaApplier.LastRequest);

        var apply = result.Value.Apply;
        Assert.False(apply.Attempted);
        Assert.Equal(buildResult.Opportunities.ContradictionCount, apply.PendingRemediationCount);
        Assert.Equal(buildResult.RemediationScriptPath, apply.RemediationScriptPath);
        Assert.Contains(result.Value.ExecutionLog.Entries, entry => entry.Step == "fullExport.apply.skipped");
        Assert.Contains(result.Value.ExecutionLog.Entries, entry => entry.Step == "fullExport.apply.remediationPending");
    }

    [Fact]
    public async Task HandleAsync_invokes_schema_applier_when_enabled()
    {
        var dispatcher = new StubCommandDispatcher();
        var schemaApplier = new FakeSchemaDataApplier
        {
            Result = Result<SchemaDataApplyOutcome>.Success(new SchemaDataApplyOutcome(
                ImmutableArray.Create(SafeScriptPath),
                ImmutableArray.Create(SeedScriptPath),
                ExecutedBatchCount: 3,
                Duration: TimeSpan.FromMilliseconds(125),
                MaxBatchSizeBytes: 4096,
                StreamingEnabled: true,
                StaticSeedValidation: StaticSeedValidationSummary.NotAttempted))
        };
        var orchestrator = new SchemaApplyOrchestrator(schemaApplier);
        var uatRunner = new RecordingUatUsersRunner();
        var pipeline = CreatePipeline(dispatcher, orchestrator, uatRunner);

        var (extractRequest, extractResult) = CreateExtractionArtifacts();
        var (captureRequest, captureResult) = CreateCaptureArtifacts();
        var (buildRequest, buildResult) = CreateBuildArtifacts();

        dispatcher.Register<ExtractModelPipelineRequest, ModelExtractionResult>((_, _) => Task.FromResult(Result<ModelExtractionResult>.Success(extractResult)));
        dispatcher.Register<CaptureProfilePipelineRequest, CaptureProfilePipelineResult>((_, _) => Task.FromResult(Result<CaptureProfilePipelineResult>.Success(captureResult)));
        dispatcher.Register<BuildSsdtPipelineRequest, BuildSsdtPipelineResult>((_, _) => Task.FromResult(Result<BuildSsdtPipelineResult>.Success(buildResult)));

        var applyOptions = new SchemaApplyOptions(
            Enabled: true,
            ConnectionString: "Server=(localdb)\\MSSQLLocalDB;Database=Test;Integrated Security=true;",
            Authentication: new SqlAuthenticationSettings(null, null, null, null),
            CommandTimeoutSeconds: 30,
            ApplySafeScript: true,
            ApplyStaticSeeds: true,
            StaticSeedSynchronizationMode.NonDestructive);

        var request = new FullExportPipelineRequest(extractRequest, captureRequest, buildRequest, applyOptions);
        var result = await pipeline.HandleAsync(request);

        Assert.True(result.IsSuccess);
        Assert.NotNull(schemaApplier.LastRequest);
        Assert.Equal(applyOptions.ConnectionString, schemaApplier.LastRequest!.ConnectionString);
        Assert.Equal(ImmutableArray.Create(SafeScriptPath), schemaApplier.LastRequest.ScriptPaths);
        Assert.Equal(ImmutableArray.Create(SeedScriptPath), schemaApplier.LastRequest.SeedScriptPaths);

        var apply = result.Value.Apply;
        Assert.True(apply.Attempted);
        Assert.True(apply.SafeScriptApplied);
        Assert.True(apply.StaticSeedsApplied);
        Assert.Contains(SafeScriptPath, apply.AppliedScripts);
        Assert.Contains(SeedScriptPath, apply.AppliedSeedScripts);
        Assert.Contains(result.Value.ExecutionLog.Entries, entry => entry.Step == "fullExport.apply.completed");
    }

    [Fact]
    public async Task HandleAsync_records_stage_telemetry_and_artifacts()
    {
        var dispatcher = new StubCommandDispatcher();
        var schemaApplier = new FakeSchemaDataApplier();
        var orchestrator = new SchemaApplyOrchestrator(schemaApplier);
        var uatRunner = new RecordingUatUsersRunner();
        var pipeline = CreatePipeline(dispatcher, orchestrator, uatRunner);

        var (extractRequest, extractBase) = CreateExtractionArtifacts();
        var extractionResult = new ModelExtractionResult(
            extractBase.Model,
            extractBase.JsonPayload,
            extractBase.ExtractedAtUtc,
            new[] { "Stale metadata snapshot." },
            extractBase.Metadata);

        var (captureRequest, captureBase) = CreateCaptureArtifacts();
        var captureResult = new CaptureProfilePipelineResult(
            captureBase.Profile,
            captureBase.Manifest,
            captureBase.ProfilePath,
            captureBase.ManifestPath,
            captureBase.Insights,
            captureBase.ExecutionLog,
            ImmutableArray.Create("Profiler fell back to cached evidence."),
            captureBase.MultiEnvironmentReport);

        var (buildRequest, buildResult) = CreateBuildArtifacts();

        dispatcher.Register<ExtractModelPipelineRequest, ModelExtractionResult>((request, _) =>
        {
            Assert.Equal(extractRequest, request);
            return Task.FromResult(Result<ModelExtractionResult>.Success(extractionResult));
        });
        dispatcher.Register<CaptureProfilePipelineRequest, CaptureProfilePipelineResult>((request, _) =>
        {
            Assert.Equal(captureRequest, request);
            return Task.FromResult(Result<CaptureProfilePipelineResult>.Success(captureResult));
        });
        dispatcher.Register<BuildSsdtPipelineRequest, BuildSsdtPipelineResult>((request, _) =>
        {
            Assert.Equal(buildRequest, request);
            return Task.FromResult(Result<BuildSsdtPipelineResult>.Success(buildResult));
        });

        var request = new FullExportPipelineRequest(extractRequest, captureRequest, buildRequest, SchemaApplyOptions.Disabled);
        var outcome = await pipeline.HandleAsync(request);

        Assert.True(outcome.IsSuccess);
        var result = outcome.Value;
        Assert.False(result.Apply.Attempted);
        Assert.Equal(buildResult.SafeScriptPath, result.Apply.SafeScriptPath);
        Assert.Equal(buildResult.RemediationScriptPath, result.Apply.RemediationScriptPath);
        Assert.Equal(2, result.Apply.PendingRemediationCount);
        Assert.Equal(new[] { SafeScriptPath, SeedScriptPath }, result.Apply.SkippedScripts);

        var entries = result.ExecutionLog.Entries;
        Assert.Contains(entries, entry => entry.Step == "fullExport.started");
        Assert.Contains(entries, entry => entry.Step == "fullExport.completed");

        var extractEntry = Assert.Single(entries.Where(entry => entry.Step == "fullExport.extract.completed"));
        Assert.Equal("1", extractEntry.Metadata["counts.warnings"]);
        Assert.True(extractEntry.Metadata.ContainsKey("timestamps.extractedAtUtc"));

        var profileEntry = Assert.Single(entries.Where(entry => entry.Step == "fullExport.profile.completed"));
        Assert.Equal("1", profileEntry.Metadata["counts.warnings"]);
        Assert.Equal(captureResult.ProfilePath, profileEntry.Metadata["paths.profile.path"]);

        var buildEntry = Assert.Single(entries.Where(entry => entry.Step == "fullExport.build.completed"));
        Assert.Equal(request.Build.OutputDirectory, buildEntry.Metadata["paths.paths.output"]);
        Assert.Equal(buildResult.SafeScriptPath, buildEntry.Metadata["paths.paths.safeScript"]);
        Assert.Equal(buildResult.RemediationScriptPath, buildEntry.Metadata["paths.paths.remediationScript"]);
        Assert.Equal(buildResult.Opportunities.TotalCount.ToString(), buildEntry.Metadata["counts.opportunities.total"]);

        var remediationEntry = Assert.Single(entries.Where(entry => entry.Step == "fullExport.apply.remediationPending"));
        Assert.Equal("2", remediationEntry.Metadata["counts.contradictions.pending"]);
        Assert.Equal(buildResult.RemediationScriptPath, remediationEntry.Metadata["paths.paths.remediationScript"]);

        var skippedEntry = Assert.Single(entries.Where(entry => entry.Step == "fullExport.apply.skipped"));
        Assert.Equal("false", skippedEntry.Metadata["flags.apply.enabled"]);
        Assert.Equal(buildResult.SafeScriptPath, skippedEntry.Metadata["paths.paths.safeScript"]);
        Assert.Equal(buildResult.RemediationScriptPath, skippedEntry.Metadata["paths.paths.remediationScript"]);
    }

    [Fact]
    public async Task HandleAsync_propagates_stage_errors()
    {
        var dispatcher = new StubCommandDispatcher();
        var schemaApplier = new FakeSchemaDataApplier();
        var orchestrator = new SchemaApplyOrchestrator(schemaApplier);
        var uatRunner = new RecordingUatUsersRunner();
        var pipeline = CreatePipeline(dispatcher, orchestrator, uatRunner);

        var (extractRequest, extractResult) = CreateExtractionArtifacts();
        var (captureRequest, _) = CreateCaptureArtifacts();
        var (buildRequest, _) = CreateBuildArtifacts();

        dispatcher.Register<ExtractModelPipelineRequest, ModelExtractionResult>((_, _) =>
            Task.FromResult(Result<ModelExtractionResult>.Success(extractResult)));

        var failureErrors = ImmutableArray.Create(ValidationError.Create("pipeline.profile.failure", "Profiling failed."));
        dispatcher.Register<CaptureProfilePipelineRequest, CaptureProfilePipelineResult>((_, _) =>
            Task.FromResult(Result<CaptureProfilePipelineResult>.Failure(failureErrors)));

        var request = new FullExportPipelineRequest(extractRequest, captureRequest, buildRequest, SchemaApplyOptions.Disabled);
        var result = await pipeline.HandleAsync(request);

        Assert.True(result.IsFailure);
        Assert.Equal(failureErrors, result.Errors);
        Assert.Null(schemaApplier.LastRequest);
    }

    [Fact]
    public async Task HandleAsync_runs_uat_users_when_enabled()
    {
        var dispatcher = new StubCommandDispatcher();
        var schemaApplier = new FakeSchemaDataApplier();
        var orchestrator = new SchemaApplyOrchestrator(schemaApplier);
        var uatRunner = new RecordingUatUsersRunner
        {
            Result = Result<UatUsersApplicationResult>.Success(new UatUsersApplicationResult(
                Executed: true,
                Context: null,
                Warnings: ImmutableArray<string>.Empty))
        };
        var (extractRequest, extractResult) = CreateExtractionArtifacts();
        var (captureRequest, captureResult) = CreateCaptureArtifacts();
        var (buildRequest, buildResult) = CreateBuildArtifacts();

        var schemaGraphFactory = new RecordingSchemaGraphFactory
        {
            GraphToReturn = new ModelSchemaGraph(extractResult.Model)
        };
        var pipeline = CreatePipeline(dispatcher, orchestrator, uatRunner, schemaGraphFactory);

        dispatcher.Register<ExtractModelPipelineRequest, ModelExtractionResult>((_, _) => Task.FromResult(Result<ModelExtractionResult>.Success(extractResult)));
        dispatcher.Register<CaptureProfilePipelineRequest, CaptureProfilePipelineResult>((_, _) => Task.FromResult(Result<CaptureProfilePipelineResult>.Success(captureResult)));
        dispatcher.Register<BuildSsdtPipelineRequest, BuildSsdtPipelineResult>((_, _) => Task.FromResult(Result<BuildSsdtPipelineResult>.Success(buildResult)));

        var applyOptions = SchemaApplyOptions.Disabled;
        var uatOptions = new UatUsersPipelineOptions(
            Enabled: true,
            UserSchema: "dbo",
            UserTable: "User",
            UserIdColumn: "Id",
            IncludeColumns: Array.Empty<string>(),
            UserMapPath: null,
            UatUserInventoryPath: "uat.csv",
            QaUserInventoryPath: "qa.csv",
            SnapshotPath: null,
            UserEntityIdentifier: null);
        var request = new FullExportPipelineRequest(extractRequest, captureRequest, buildRequest, applyOptions, uatOptions);
        var result = await pipeline.HandleAsync(request);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.UatUsers.Executed);
        Assert.NotNull(uatRunner.LastRequest);
        Assert.Equal(buildRequest.OutputDirectory, uatRunner.LastRequest!.OutputDirectory);
        Assert.Same(schemaGraphFactory.GraphToReturn, uatRunner.LastRequest!.SchemaGraph);
        Assert.Same(result.Value.Extraction, schemaGraphFactory.LastExtraction);
        var entries = result.Value.ExecutionLog.Entries;
        Assert.Contains(entries, entry => entry.Step == "fullExport.uatUsers.completed");
    }

    [Fact]
    public async Task HandleAsync_returns_failure_when_uat_users_fails()
    {
        var dispatcher = new StubCommandDispatcher();
        var schemaApplier = new FakeSchemaDataApplier();
        var orchestrator = new SchemaApplyOrchestrator(schemaApplier);
        var failure = ValidationError.Create("pipeline.uatUsers.failure", "uat-users failed");
        var uatRunner = new RecordingUatUsersRunner
        {
            Result = Result<UatUsersApplicationResult>.Failure(failure)
        };
        var pipeline = CreatePipeline(dispatcher, orchestrator, uatRunner);

        var (extractRequest, extractResult) = CreateExtractionArtifacts();
        var (captureRequest, captureResult) = CreateCaptureArtifacts();
        var (buildRequest, buildResult) = CreateBuildArtifacts();

        dispatcher.Register<ExtractModelPipelineRequest, ModelExtractionResult>((_, _) => Task.FromResult(Result<ModelExtractionResult>.Success(extractResult)));
        dispatcher.Register<CaptureProfilePipelineRequest, CaptureProfilePipelineResult>((_, _) => Task.FromResult(Result<CaptureProfilePipelineResult>.Success(captureResult)));
        dispatcher.Register<BuildSsdtPipelineRequest, BuildSsdtPipelineResult>((_, _) => Task.FromResult(Result<BuildSsdtPipelineResult>.Success(buildResult)));

        var request = new FullExportPipelineRequest(
            extractRequest,
            captureRequest,
            buildRequest,
            SchemaApplyOptions.Disabled,
            new UatUsersPipelineOptions(
                Enabled: true,
                UserSchema: "dbo",
                UserTable: "User",
                UserIdColumn: "Id",
                IncludeColumns: Array.Empty<string>(),
                UserMapPath: null,
                UatUserInventoryPath: "uat.csv",
                QaUserInventoryPath: "qa.csv",
                SnapshotPath: null,
                UserEntityIdentifier: null));

        var result = await pipeline.HandleAsync(request);

        Assert.True(result.IsFailure);
        Assert.Equal(failure, Assert.Single(result.Errors));
        Assert.Contains(result.Errors, error => error == failure);
        Assert.NotNull(uatRunner.LastRequest);
    }

    [Fact]
    public async Task HandleAsync_skips_uat_users_when_disabled()
    {
        var dispatcher = new StubCommandDispatcher();
        var schemaApplier = new FakeSchemaDataApplier();
        var orchestrator = new SchemaApplyOrchestrator(schemaApplier);
        var uatRunner = new RecordingUatUsersRunner();
        var pipeline = CreatePipeline(dispatcher, orchestrator, uatRunner);

        var (extractRequest, extractResult) = CreateExtractionArtifacts();
        var (captureRequest, captureResult) = CreateCaptureArtifacts();
        var (buildRequest, buildResult) = CreateBuildArtifacts();

        dispatcher.Register<ExtractModelPipelineRequest, ModelExtractionResult>((_, _) => Task.FromResult(Result<ModelExtractionResult>.Success(extractResult)));
        dispatcher.Register<CaptureProfilePipelineRequest, CaptureProfilePipelineResult>((_, _) => Task.FromResult(Result<CaptureProfilePipelineResult>.Success(captureResult)));
        dispatcher.Register<BuildSsdtPipelineRequest, BuildSsdtPipelineResult>((_, _) => Task.FromResult(Result<BuildSsdtPipelineResult>.Success(buildResult)));

        var request = new FullExportPipelineRequest(
            extractRequest,
            captureRequest,
            buildRequest,
            SchemaApplyOptions.Disabled,
            new UatUsersPipelineOptions(
                Enabled: false,
                UserSchema: null,
                UserTable: null,
                UserIdColumn: null,
                IncludeColumns: null,
                UserMapPath: null,
                UatUserInventoryPath: null,
                QaUserInventoryPath: null,
                SnapshotPath: null,
                UserEntityIdentifier: null));

        var result = await pipeline.HandleAsync(request);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.UatUsers.Executed);
        Assert.Null(uatRunner.LastRequest);
        var entries = result.Value.ExecutionLog.Entries;
        Assert.Contains(entries, entry => entry.Step == "fullExport.uatUsers.skipped");
    }

    [Fact]
    public async Task HandleAsync_hydrates_extraction_before_downstream_stages()
    {
        var dispatcher = new StubCommandDispatcher();
        var schemaApplier = new FakeSchemaDataApplier();
        var orchestrator = new SchemaApplyOrchestrator(schemaApplier);
        var uatRunner = new RecordingUatUsersRunner();
        var hydrator = new RecordingModelIngestionService();
        var schemaGraphFactory = new RecordingSchemaGraphFactory();
        var pipeline = CreatePipeline(dispatcher, orchestrator, uatRunner, schemaGraphFactory, hydrator);

        var (extractRequest, extractResult) = CreateExtractionArtifacts();
        var (captureRequest, captureResult) = CreateCaptureArtifacts();
        var (buildRequest, buildResult) = CreateBuildArtifacts();

        var hydratedModel = ModelFixtures.LoadModel("model.edge-case.json");
        hydrator.ModelToReturn = hydratedModel;
        uatRunner.Result = Result<UatUsersApplicationResult>.Success(new UatUsersApplicationResult(true, null, ImmutableArray<string>.Empty));

        dispatcher.Register<ExtractModelPipelineRequest, ModelExtractionResult>((_, _) => Task.FromResult(Result<ModelExtractionResult>.Success(extractResult)));
        dispatcher.Register<CaptureProfilePipelineRequest, CaptureProfilePipelineResult>((_, _) => Task.FromResult(Result<CaptureProfilePipelineResult>.Success(captureResult)));
        dispatcher.Register<BuildSsdtPipelineRequest, BuildSsdtPipelineResult>((_, _) => Task.FromResult(Result<BuildSsdtPipelineResult>.Success(buildResult)));

        var uatOptions = new UatUsersPipelineOptions(
            Enabled: true,
            UserSchema: "dbo",
            UserTable: "User",
            UserIdColumn: "Id",
            IncludeColumns: Array.Empty<string>(),
            UserMapPath: null,
            UatUserInventoryPath: "uat.csv",
            QaUserInventoryPath: "qa.csv",
            SnapshotPath: null,
            UserEntityIdentifier: null);

        var request = new FullExportPipelineRequest(extractRequest, captureRequest, buildRequest, SchemaApplyOptions.Disabled, uatOptions);
        var outcome = await pipeline.HandleAsync(request);

        Assert.True(outcome.IsSuccess);
        var result = outcome.Value;
        Assert.Equal(Path.GetFullPath(extractResult.JsonPayload.FilePath!), hydrator.LastPath);
        Assert.NotNull(hydrator.LastOptions?.SqlMetadata);
        Assert.Equal(extractRequest.SqlOptions.ConnectionString, hydrator.LastOptions!.SqlMetadata!.ConnectionString);
        Assert.Same(hydratedModel, result.Extraction.Model);
        Assert.Same(hydratedModel, schemaGraphFactory.LastExtraction!.Model);
        Assert.NotNull(uatRunner.LastRequest);
    }

    private static (ExtractModelPipelineRequest Request, ModelExtractionResult Result) CreateExtractionArtifacts()
    {
        var commandResult = ModelExtractionCommand.Create(Array.Empty<string>(), includeSystemModules: true, includeInactiveModules: false, onlyActiveAttributes: true);
        var command = commandResult.IsSuccess ? commandResult.Value : throw new InvalidOperationException("Failed to create extraction command.");
        var sqlOptions = CreateSqlOptions();
        var outputPath = Path.Combine(Path.GetTempPath(), "model.json");
        var request = new ExtractModelPipelineRequest(command, sqlOptions, AdvancedSqlFixtureManifestPath: null, OutputPath: outputPath, SqlMetadataOutputPath: null);
        var result = CreateExtractionResult();
        return (request, result);
    }

    private static (CaptureProfilePipelineRequest Request, CaptureProfilePipelineResult Result) CreateCaptureArtifacts()
    {
        var scope = CreateScope();
        var outputDirectory = Path.Combine(Path.GetTempPath(), "profile-output");
        var fixtureProfilePath = FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json"));
        var request = new CaptureProfilePipelineRequest(scope, "fixture", outputDirectory, fixtureProfilePath, SqlMetadataLog: null);
        var profile = ProfileFixtures.LoadSnapshot(Path.Combine("profiling", "profile.edge-case.json"));
        var manifest = new CaptureProfileManifest(
            scope.ModelPath,
            fixtureProfilePath,
            "fixture",
            new CaptureProfileModuleSummary(false, Array.Empty<string>(), true, true),
            new CaptureProfileSupplementalSummary(false, Array.Empty<string>()),
            new CaptureProfileSnapshotSummary(0, 0, 0, 0, 0),
            Array.Empty<CaptureProfileInsight>(),
            Array.Empty<string>(),
            DateTimeOffset.UtcNow);
        var result = new CaptureProfilePipelineResult(
            profile,
            manifest,
            fixtureProfilePath,
            Path.Combine(outputDirectory, "manifest.json"),
            ImmutableArray<ProfilingInsight>.Empty,
            PipelineExecutionLog.Empty,
            ImmutableArray<string>.Empty,
            null);
        return (request, result);
    }

    private static (BuildSsdtPipelineRequest Request, BuildSsdtPipelineResult Result) CreateBuildArtifacts()
    {
        var scope = CreateScope(profilePath: FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json")));
        var outputDirectory = Path.Combine(Path.GetTempPath(), "ssdt-output");
        var request = new BuildSsdtPipelineRequest(
            scope,
            outputDirectory,
            "fixture",
            EvidenceCache: null,
            DynamicDataset: DynamicEntityDataset.Empty,
            DynamicDatasetSource: DynamicDatasetSource.None,
            StaticDataProvider: null,
            SeedOutputDirectoryHint: Path.Combine(outputDirectory, "Seeds"),
            DynamicDataOutputDirectoryHint: Path.Combine(outputDirectory, "DynamicData"),
            SqlProjectPathHint: Path.Combine(outputDirectory, "OutSystemsModel.sqlproj"),
            SqlMetadataLog: null);

        var manifest = new SsdtManifest(
            new[]
            {
                new TableManifestEntry("Core", "dbo", "Sample", "Modules/Core.Sample.sql", Array.Empty<string>(), Array.Empty<string>(), false)
            },
            new SsdtManifestOptions(false, false, true, 1),
            null,
            new SsdtEmissionMetadata("sha256", "abc123"),
            Array.Empty<PreRemediationManifestEntry>(),
            SsdtCoverageSummary.CreateComplete(1, 1, 1),
            SsdtPredicateCoverage.Empty,
            Array.Empty<string>());

        var toggleSnapshot = TighteningToggleSnapshot.Create(TighteningOptions.Default);
        var togglePrecedence = toggleSnapshot.ToExportDictionary().ToImmutableDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var decisionReport = new PolicyDecisionReport(
            ImmutableArray<ColumnDecisionReport>.Empty,
            ImmutableArray<UniqueIndexDecisionReport>.Empty,
            ImmutableArray<ForeignKeyDecisionReport>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableDictionary<string, int>.Empty,
            ImmutableArray<TighteningDiagnostic>.Empty,
            ImmutableDictionary<string, ModuleDecisionRollup>.Empty,
            togglePrecedence,
            ImmutableDictionary<string, string>.Empty,
            ImmutableDictionary<string, string>.Empty,
            toggleSnapshot);

        var opportunities = new OpportunitiesReport(
            ImmutableArray<Opportunity>.Empty,
            ImmutableDictionary<OpportunityDisposition, int>.Empty,
            ImmutableDictionary<OpportunityCategory, int>.Empty.Add(OpportunityCategory.Contradiction, 2),
            ImmutableDictionary<OpportunityType, int>.Empty,
            ImmutableDictionary<RiskLevel, int>.Empty,
            DateTimeOffset.UtcNow);

        var validations = ValidationReport.Empty(DateTimeOffset.UtcNow);

        var result = new BuildSsdtPipelineResult(
            ProfileFixtures.LoadSnapshot(Path.Combine("profiling", "profile.edge-case.json")),
            ImmutableArray<ProfilingInsight>.Empty,
            decisionReport,
            opportunities,
            validations,
            manifest,
            ImmutableDictionary<string, ModuleManifestRollup>.Empty,
            ImmutableArray<PipelineInsight>.Empty,
            Path.Combine(outputDirectory, "decision-log.json"),
            Path.Combine(outputDirectory, "opportunities.json"),
            Path.Combine(outputDirectory, "validations.json"),
            SafeScriptPath,
            "PRINT 'safe';",
            RemediationScriptPath,
            "PRINT 'remediation';",
            Path.Combine(outputDirectory, "OutSystemsModel.sqlproj"),
            ImmutableArray.Create(SeedScriptPath),
            ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty,
            SsdtSqlValidationSummary.Empty,
            null,
            PipelineExecutionLog.Empty,
            StaticSeedTopologicalOrderApplied: false,
            DynamicInsertTopologicalOrderApplied: false,
            StaticSeedOrderingMode: EntityDependencyOrderingMode.Alphabetical,
            DynamicInsertOrderingMode: EntityDependencyOrderingMode.Alphabetical,
            DynamicInsertOutputMode: DynamicInsertOutputMode.PerEntity,
            ImmutableArray<string>.Empty,
            null);

        return (request, result);
    }

    private static ModelExecutionScope CreateScope(string? modelPath = null, string? profilePath = null)
    {
        var resolvedModelPath = modelPath ?? FixtureFile.GetPath("model.edge-case.json");
        var tightening = TighteningOptions.Default;
        return new ModelExecutionScope(
            resolvedModelPath,
            ModuleFilterOptions.IncludeAll,
            SupplementalModelOptions.Default,
            tightening,
            CreateSqlOptions(),
            SmoBuildOptions.FromEmission(tightening.Emission),
            TypeMappingPolicyLoader.LoadDefault(),
            profilePath);
    }

    private static ResolvedSqlOptions CreateSqlOptions()
    {
        return new ResolvedSqlOptions(
            ConnectionString: "Server=Test;",
            CommandTimeoutSeconds: null,
            Sampling: new SqlSamplingSettings(null, null),
            Authentication: new SqlAuthenticationSettings(null, null, null, null),
            MetadataContract: MetadataContractOverrides.Strict,
            ProfilingConnectionStrings: ImmutableArray<string>.Empty,
            TableNameMappings: ImmutableArray<TableNameMappingConfiguration>.Empty);
    }

    private static ModelExtractionResult CreateExtractionResult()
    {
        var model = ModelFixtures.LoadModel("model.edge-case.json");
        var payload = ModelJsonPayload.FromFile(FixtureFile.GetPath("model.edge-case.json"));
        var metadata = CreateMetadataSnapshot("TestDatabase");
        return new ModelExtractionResult(model, payload, DateTimeOffset.UtcNow, Array.Empty<string>(), metadata);
    }

    private static OutsystemsMetadataSnapshot CreateMetadataSnapshot(string databaseName)
    {
        return new OutsystemsMetadataSnapshot(
            Modules: Array.Empty<OutsystemsModuleRow>(),
            Entities: Array.Empty<OutsystemsEntityRow>(),
            Attributes: Array.Empty<OutsystemsAttributeRow>(),
            References: Array.Empty<OutsystemsReferenceRow>(),
            PhysicalTables: Array.Empty<OutsystemsPhysicalTableRow>(),
            ColumnReality: Array.Empty<OutsystemsColumnRealityRow>(),
            ColumnChecks: Array.Empty<OutsystemsColumnCheckRow>(),
            ColumnCheckJson: Array.Empty<OutsystemsColumnCheckJsonRow>(),
            PhysicalColumnsPresent: Array.Empty<OutsystemsPhysicalColumnPresenceRow>(),
            Indexes: Array.Empty<OutsystemsIndexRow>(),
            IndexColumns: Array.Empty<OutsystemsIndexColumnRow>(),
            ForeignKeys: Array.Empty<OutsystemsForeignKeyRow>(),
            ForeignKeyColumns: Array.Empty<OutsystemsForeignKeyColumnRow>(),
            ForeignKeyAttributeMap: Array.Empty<OutsystemsForeignKeyAttrMapRow>(),
            AttributeForeignKeys: Array.Empty<OutsystemsAttributeHasFkRow>(),
            ForeignKeyColumnsJson: Array.Empty<OutsystemsForeignKeyColumnsJsonRow>(),
            ForeignKeyAttributeJson: Array.Empty<OutsystemsForeignKeyAttributeJsonRow>(),
            Triggers: Array.Empty<OutsystemsTriggerRow>(),
            AttributeJson: Array.Empty<OutsystemsAttributeJsonRow>(),
            RelationshipJson: Array.Empty<OutsystemsRelationshipJsonRow>(),
            IndexJson: Array.Empty<OutsystemsIndexJsonRow>(),
            TriggerJson: Array.Empty<OutsystemsTriggerJsonRow>(),
            ModuleJson: Array.Empty<OutsystemsModuleJsonRow>(),
            DatabaseName: databaseName);
    }

    private static FullExportPipeline CreatePipeline(
        StubCommandDispatcher dispatcher,
        SchemaApplyOrchestrator orchestrator,
        IUatUsersPipelineRunner uatRunner,
        IModelUserSchemaGraphFactory? schemaGraphFactory = null,
        IModelIngestionService? modelIngestionService = null)
    {
        var coordinator = new FullExportCoordinator(schemaGraphFactory ?? new ModelUserSchemaGraphFactory());
        return new FullExportPipeline(
            dispatcher,
            orchestrator,
            uatRunner,
            coordinator,
            modelIngestionService ?? new RecordingModelIngestionService(),
            TimeProvider.System,
            NullLogger<FullExportPipeline>.Instance);
    }

    private sealed class RecordingSchemaGraphFactory : IModelUserSchemaGraphFactory
    {
        public ModelExtractionResult? LastExtraction { get; private set; }

        public ModelSchemaGraph? GraphToReturn { get; set; }

        public Result<ModelSchemaGraph> Create(ModelExtractionResult extraction)
        {
            LastExtraction = extraction;
            return Result<ModelSchemaGraph>.Success(GraphToReturn ?? new ModelSchemaGraph(extraction.Model));
        }
    }

    private sealed class RecordingModelIngestionService : IModelIngestionService
    {
        public string? LastPath { get; private set; }

        public ModelIngestionOptions? LastOptions { get; private set; }

        public OsmModel ModelToReturn { get; set; } = ModelFixtures.LoadModel("model.edge-case.json");

        public Task<Result<OsmModel>> LoadFromFileAsync(
            string modelPath,
            ICollection<string>? warnings = null,
            CancellationToken cancellationToken = default,
            ModelIngestionOptions? options = null)
        {
            LastPath = Path.GetFullPath(modelPath);
            LastOptions = options;
            return Task.FromResult(Result<OsmModel>.Success(ModelToReturn));
        }
    }

    private sealed class StubCommandDispatcher : ICommandDispatcher
    {
        private readonly Dictionary<Type, Func<object, CancellationToken, Task<Result<object>>>> _handlers = new();

        public Task<Result<TResponse>> DispatchAsync<TCommand, TResponse>(TCommand command, CancellationToken cancellationToken = default)
            where TCommand : ICommand<TResponse>
        {
            if (!_handlers.TryGetValue(typeof(TCommand), out var handler))
            {
                throw new InvalidOperationException($"No handler registered for {typeof(TCommand).Name}.");
            }

            return InvokeAsync<TResponse>(handler, command!, cancellationToken);
        }

        public void Register<TCommand, TResponse>(Func<TCommand, CancellationToken, Task<Result<TResponse>>> handler)
            where TCommand : ICommand<TResponse>
        {
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _handlers[typeof(TCommand)] = async (command, token) =>
            {
                var result = await handler((TCommand)command, token).ConfigureAwait(false);
                return result.Map<object>(value => value!);
            };
        }

        private static async Task<Result<TResponse>> InvokeAsync<TResponse>(
            Func<object, CancellationToken, Task<Result<object>>> handler,
            object command,
            CancellationToken cancellationToken)
        {
            var result = await handler(command, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return Result<TResponse>.Failure(result.Errors);
            }

            return Result<TResponse>.Success((TResponse)result.Value);
        }
    }

    private sealed class RecordingUatUsersRunner : IUatUsersPipelineRunner
    {
        public Result<UatUsersApplicationResult> Result { get; set; }
            = Result<UatUsersApplicationResult>.Success(UatUsersApplicationResult.Disabled);

        public UatUsersPipelineRequest? LastRequest { get; private set; }

        public Task<Result<UatUsersApplicationResult>> RunAsync(
            UatUsersPipelineRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeSchemaDataApplier : ISchemaDataApplier
    {
        public SchemaDataApplyRequest? LastRequest { get; private set; }

        public Result<SchemaDataApplyOutcome> Result { get; set; } = Result<SchemaDataApplyOutcome>.Success(
            new SchemaDataApplyOutcome(
                ImmutableArray<string>.Empty,
                ImmutableArray<string>.Empty,
                ExecutedBatchCount: 0,
                Duration: TimeSpan.Zero,
                MaxBatchSizeBytes: 0,
                StreamingEnabled: true,
                StaticSeedValidation: StaticSeedValidationSummary.NotAttempted));

        public Task<Result<SchemaDataApplyOutcome>> ApplyAsync(
            SchemaDataApplyRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(Result);
        }
    }
}
