using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Emission;
using Osm.Emission.Seeds;
using Osm.Json;
using Osm.Pipeline.Application;
using Osm.Pipeline.DynamicData;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;
using Osm.Pipeline.UatUsers;
using Osm.Validation.Tightening;
using Opportunities = Osm.Validation.Tightening.Opportunities;
using ValidationReport = Osm.Validation.Tightening.Validations.ValidationReport;
using Xunit;

namespace Osm.Pipeline.Tests;

public sealed class FullExportApplicationServiceTests
{
    [Fact]
    public async Task RunAsync_ReusesExistingModel_WhenReuseSignalProvided()
    {
        var modelPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(modelPath, "{}");

        try
        {
            var model = CreateModel();
            var modelDeserializer = new StubModelJsonDeserializer(model);

            var profileResult = new CaptureProfileApplicationResult(
                CreateCaptureProfilePipelineResult(),
                OutputDirectory: "profiles",
                ModelPath: modelPath,
                ProfilerProvider: "fixture",
                FixtureProfilePath: null);
            var profileService = new StubProfileService(Result<CaptureProfileApplicationResult>.Success(profileResult));

            var extractService = new RecordingExtractService();

            var buildResult = CreateBuildResult(modelPath);
            var buildService = new RecordingBuildService(Result<BuildSsdtApplicationResult>.Success(buildResult));

        var schemaApplyOrchestrator = new SchemaApplyOrchestrator(new StubSchemaDataApplier());
        var uatRunner = new RecordingUatUsersRunner();
        var schemaGraphFactory = new RecordingSchemaGraphFactory();
        var coordinator = new FullExportCoordinator(schemaGraphFactory);

        var service = new FullExportApplicationService(
            profileService,
            extractService,
            buildService,
            schemaApplyOrchestrator,
            modelDeserializer,
            uatRunner,
            coordinator);

            var configurationContext = new CliConfigurationContext(CliConfiguration.Empty, ConfigPath: null);
            var overrides = new FullExportOverrides(
                new BuildSsdtOverrides(modelPath, null, null, null, null, null, null, null),
                new CaptureProfileOverrides(modelPath, null, null, null, null),
                new ExtractModelOverrides(null, null, null, null, null, null),
                Apply: null,
                ReuseModelPath: true);
            var input = new FullExportApplicationInput(
                configurationContext,
                overrides,
                new ModuleFilterOverrides(Array.Empty<string>(), null, null, Array.Empty<string>(), Array.Empty<string>()),
                new SqlOptionsOverrides(null, null, null, null, null, null, null, null, null),
                new CacheOptionsOverrides(null, null));

            var result = await service.RunAsync(input, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(0, extractService.CallCount);
            Assert.NotNull(buildService.LastInput);
            Assert.Equal(modelPath, buildService.LastInput!.Overrides.ModelPath);
            Assert.NotNull(buildService.LastInput.DynamicDataset);
            Assert.True(buildService.LastInput.DynamicDataset!.IsEmpty);
            Assert.True(result.Value.Extraction.ModelWasReused);
            Assert.Equal(Path.GetFullPath(modelPath), Path.GetFullPath(result.Value.Extraction.OutputPath));
            Assert.False(result.Value.UatUsers.Executed);
            Assert.Null(schemaGraphFactory.LastExtraction);
        }
        finally
        {
            if (File.Exists(modelPath))
            {
                File.Delete(modelPath);
            }
        }
    }

    [Fact]
    public async Task RunAsync_ExecutesUatUsersPipelineWhenEnabled()
    {
        var model = CreateModel();
        var extractionResult = CreateExtractionApplicationResult(model);
        var profileResult = new CaptureProfileApplicationResult(
            CreateCaptureProfilePipelineResult(),
            OutputDirectory: "profiles",
            ModelPath: "model.json",
            ProfilerProvider: "fixture",
            FixtureProfilePath: null);
        var buildResult = CreateBuildResult("model.json");

        var profileService = new StubProfileService(Result<CaptureProfileApplicationResult>.Success(profileResult));
        var extractService = new StubExtractService(Result<ExtractModelApplicationResult>.Success(extractionResult));
        var buildService = new RecordingBuildService(Result<BuildSsdtApplicationResult>.Success(buildResult));
        var schemaApplyOrchestrator = new SchemaApplyOrchestrator(new StubSchemaDataApplier());
        var modelDeserializer = new StubModelJsonDeserializer(model);
        var uatRunner = new RecordingUatUsersRunner
        {
            ResultToReturn = Result<UatUsersApplicationResult>.Success(new UatUsersApplicationResult(
                Executed: true,
                Context: null,
                Warnings: ImmutableArray<string>.Empty))
        };
        var schemaGraphFactory = new RecordingSchemaGraphFactory
        {
            GraphToReturn = new ModelSchemaGraph(model)
        };
        var coordinator = new FullExportCoordinator(schemaGraphFactory);

        var service = new FullExportApplicationService(
            profileService,
            extractService,
            buildService,
            schemaApplyOrchestrator,
            modelDeserializer,
            uatRunner,
            coordinator);

        var configurationContext = new CliConfigurationContext(CliConfiguration.Empty, ConfigPath: null);
        var overrides = new FullExportOverrides(
            Build: new BuildSsdtOverrides(null, null, null, null, null, null, null, null),
            Profile: new CaptureProfileOverrides(null, null, null, null, null),
            Extract: new ExtractModelOverrides(null, null, null, null, null, null),
            Apply: null,
            ReuseModelPath: false,
            UatUsers: new UatUsersOverrides(
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
        var input = new FullExportApplicationInput(
            configurationContext,
            overrides,
            new ModuleFilterOverrides(Array.Empty<string>(), null, null, Array.Empty<string>(), Array.Empty<string>()),
            new SqlOptionsOverrides("Server=Test;Database=Uat;Integrated Security=true;", null, null, null, null, null, null, null, null),
            new CacheOptionsOverrides(null, null));

        var result = await service.RunAsync(input, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.UatUsers.Executed);
        var request = Assert.IsType<UatUsersPipelineRequest>(uatRunner.LastRequest);
        Assert.Equal("Server=Test;Database=Uat;Integrated Security=true;", request.SourceConnectionString);
        Assert.Equal(extractionResult.ExtractionResult, request.Extraction);
        Assert.Equal(buildResult.OutputDirectory, request.OutputDirectory);
        Assert.Same(schemaGraphFactory.GraphToReturn, request.SchemaGraph);
        Assert.Same(extractionResult.ExtractionResult, schemaGraphFactory.LastExtraction);
        Assert.False(request.Overrides.IdempotentEmission);
    }

    [Fact]
    public async Task RunAsync_ExecutesUatUsersPipelineUsingConfigurationDefaults()
    {
        var model = CreateModel();
        var extractionResult = CreateExtractionApplicationResult(model);
        var profileResult = new CaptureProfileApplicationResult(
            CreateCaptureProfilePipelineResult(),
            OutputDirectory: "profiles",
            ModelPath: "model.json",
            ProfilerProvider: "fixture",
            FixtureProfilePath: null);
        var buildResult = CreateBuildResult("model.json");

        var profileService = new StubProfileService(Result<CaptureProfileApplicationResult>.Success(profileResult));
        var extractService = new StubExtractService(Result<ExtractModelApplicationResult>.Success(extractionResult));
        var buildService = new RecordingBuildService(Result<BuildSsdtApplicationResult>.Success(buildResult));
        var schemaApplyOrchestrator = new SchemaApplyOrchestrator(new StubSchemaDataApplier());
        var modelDeserializer = new StubModelJsonDeserializer(model);
        var uatRunner = new RecordingUatUsersRunner
        {
            ResultToReturn = Result<UatUsersApplicationResult>.Success(new UatUsersApplicationResult(
                Executed: true,
                Context: null,
                Warnings: ImmutableArray<string>.Empty))
        };
        var schemaGraphFactory = new RecordingSchemaGraphFactory
        {
            GraphToReturn = new ModelSchemaGraph(model)
        };
        var coordinator = new FullExportCoordinator(schemaGraphFactory);

        var service = new FullExportApplicationService(
            profileService,
            extractService,
            buildService,
            schemaApplyOrchestrator,
            modelDeserializer,
            uatRunner,
            coordinator);

        var sqlConfig = new SqlConfiguration(
            ConnectionString: "Server=Config;Database=Uat;",
            CommandTimeoutSeconds: null,
            Sampling: SqlSamplingConfiguration.Empty,
            Authentication: SqlAuthenticationConfiguration.Empty,
            MetadataContract: MetadataContractConfiguration.Empty,
            ProfilingConnectionStrings: Array.Empty<string>(),
            TableNameMappings: Array.Empty<TableNameMappingConfiguration>());

        var configuration = CliConfiguration.Empty with
        {
            Sql = sqlConfig,
            UatUsers = new UatUsersConfiguration(
                ModelPath: null,
                FromLiveMetadata: null,
                UserSchema: "app",
                UserTable: "Users",
                UserIdColumn: "UserId",
                IncludeColumns: new[] { "CreatedBy" },
                OutputRoot: "./out",
                UserMapPath: "configured-map.csv",
                UatUserInventoryPath: "configured-uat.csv",
                QaUserInventoryPath: "configured-qa.csv",
                SnapshotPath: "configured-snapshot.json",
                UserEntityIdentifier: "UserEntity",
                MatchingStrategy: UserMatchingStrategy.CaseInsensitiveEmail,
                MatchingAttribute: "Email",
                MatchingRegexPattern: null,
                FallbackAssignment: UserFallbackAssignmentMode.Ignore,
                FallbackTargets: Array.Empty<string>(),
                IdempotentEmission: true)
        };

        var configurationContext = new CliConfigurationContext(configuration, ConfigPath: null);
        var overrides = new FullExportOverrides(
            Build: new BuildSsdtOverrides(null, null, null, null, null, null, null, null),
            Profile: new CaptureProfileOverrides(null, null, null, null, null),
            Extract: new ExtractModelOverrides(null, null, null, null, null, null),
            Apply: null,
            ReuseModelPath: false,
            UatUsers: null);
        var input = new FullExportApplicationInput(
            configurationContext,
            overrides,
            new ModuleFilterOverrides(Array.Empty<string>(), null, null, Array.Empty<string>(), Array.Empty<string>()),
            new SqlOptionsOverrides(null, null, null, null, null, null, null, null, null),
            new CacheOptionsOverrides(null, null),
            UatUsersConfiguration: configuration.UatUsers);

        var result = await service.RunAsync(input, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.UatUsers.Executed);
        var request = Assert.IsType<UatUsersPipelineRequest>(uatRunner.LastRequest);
        Assert.Equal("Server=Config;Database=Uat;", request.SourceConnectionString);
        Assert.Equal("app", request.Overrides.UserSchema);
        Assert.Equal("Users", request.Overrides.UserTable);
        Assert.Equal("UserId", request.Overrides.UserIdColumn);
        Assert.Equal(new[] { "CreatedBy" }, request.Overrides.IncludeColumns);
        Assert.Equal("configured-map.csv", request.Overrides.UserMapPath);
        Assert.Equal("configured-uat.csv", request.Overrides.UatUserInventoryPath);
        Assert.Equal("configured-snapshot.json", request.Overrides.SnapshotPath);
        Assert.Equal("UserEntity", request.Overrides.UserEntityIdentifier);
        Assert.True(request.Overrides.IdempotentEmission);
    }

    [Fact]
    public async Task RunAsync_ExecutesUatUsersPipeline_MergesCliOverridesWithConfigurationDefaults()
    {
        var model = CreateModel();
        var extractionResult = CreateExtractionApplicationResult(model);
        var profileResult = new CaptureProfileApplicationResult(
            CreateCaptureProfilePipelineResult(),
            OutputDirectory: "profiles",
            ModelPath: "model.json",
            ProfilerProvider: "fixture",
            FixtureProfilePath: null);
        var buildResult = CreateBuildResult("model.json");

        var profileService = new StubProfileService(Result<CaptureProfileApplicationResult>.Success(profileResult));
        var extractService = new StubExtractService(Result<ExtractModelApplicationResult>.Success(extractionResult));
        var buildService = new RecordingBuildService(Result<BuildSsdtApplicationResult>.Success(buildResult));
        var schemaApplyOrchestrator = new SchemaApplyOrchestrator(new StubSchemaDataApplier());
        var modelDeserializer = new StubModelJsonDeserializer(model);
        var uatRunner = new RecordingUatUsersRunner
        {
            ResultToReturn = Result<UatUsersApplicationResult>.Success(new UatUsersApplicationResult(
                Executed: true,
                Context: null,
                Warnings: ImmutableArray<string>.Empty))
        };
        var schemaGraphFactory = new RecordingSchemaGraphFactory
        {
            GraphToReturn = new ModelSchemaGraph(model)
        };
        var coordinator = new FullExportCoordinator(schemaGraphFactory);

        var service = new FullExportApplicationService(
            profileService,
            extractService,
            buildService,
            schemaApplyOrchestrator,
            modelDeserializer,
            uatRunner,
            coordinator);

        var configuration = CliConfiguration.Empty with
        {
            UatUsers = new UatUsersConfiguration(
                ModelPath: null,
                FromLiveMetadata: null,
                UserSchema: "cfg",
                UserTable: "CfgUsers",
                UserIdColumn: "CfgId",
                IncludeColumns: new[] { "CfgCreated" },
                OutputRoot: "./out",
                UserMapPath: "cfg-map.csv",
                UatUserInventoryPath: "cfg-uat.csv",
                QaUserInventoryPath: "cfg-qa.csv",
                SnapshotPath: "cfg-snapshot.json",
                UserEntityIdentifier: "cfg-entity",
                MatchingStrategy: UserMatchingStrategy.CaseInsensitiveEmail,
                MatchingAttribute: "Email",
                MatchingRegexPattern: null,
                FallbackAssignment: UserFallbackAssignmentMode.Ignore,
                FallbackTargets: Array.Empty<string>(),
                IdempotentEmission: null)
        };

        var configurationContext = new CliConfigurationContext(configuration, ConfigPath: null);
        var overrides = new FullExportOverrides(
            Build: new BuildSsdtOverrides(null, null, null, null, null, null, null, null),
            Profile: new CaptureProfileOverrides(null, null, null, null, null),
            Extract: new ExtractModelOverrides(null, null, null, null, null, null),
            Apply: null,
            ReuseModelPath: false,
            UatUsers: new UatUsersOverrides(
                Enabled: true,
                UserSchema: "cliSchema",
                UserTable: null,
                UserIdColumn: null,
                IncludeColumns: new[] { "CliColumn" },
                UserMapPath: "cli-map.csv",
                UatUserInventoryPath: "cli-uat.csv",
                QaUserInventoryPath: null,
                SnapshotPath: "cli-snapshot.json",
                UserEntityIdentifier: null));

        var input = new FullExportApplicationInput(
            configurationContext,
            overrides,
            new ModuleFilterOverrides(Array.Empty<string>(), null, null, Array.Empty<string>(), Array.Empty<string>()),
            new SqlOptionsOverrides("Server=Cli;Database=Uat;", null, null, null, null, null, null, null, null),
            new CacheOptionsOverrides(null, null),
            UatUsersConfiguration: configuration.UatUsers);

        var result = await service.RunAsync(input, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.UatUsers.Executed);
        var request = Assert.IsType<UatUsersPipelineRequest>(uatRunner.LastRequest);
        Assert.Equal("Server=Cli;Database=Uat;", request.SourceConnectionString);
        Assert.Equal("cliSchema", request.Overrides.UserSchema);
        Assert.Equal("CfgUsers", request.Overrides.UserTable);
        Assert.Equal("CfgId", request.Overrides.UserIdColumn);
        Assert.Equal(new[] { "CliColumn" }, request.Overrides.IncludeColumns);
        Assert.Equal("cli-map.csv", request.Overrides.UserMapPath);
        Assert.Equal("cli-uat.csv", request.Overrides.UatUserInventoryPath);
        Assert.Equal("cfg-qa.csv", request.Overrides.QaUserInventoryPath);
        Assert.Equal("cli-snapshot.json", request.Overrides.SnapshotPath);
        Assert.Equal("cfg-entity", request.Overrides.UserEntityIdentifier);
        Assert.False(request.Overrides.IdempotentEmission);
    }

    [Fact]
    public async Task RunAsync_SkipsSchemaApplyWithoutExplicitConnection()
    {
        var model = CreateModel();
        var extractionResult = CreateExtractionApplicationResult(model);
        var profileResult = new CaptureProfileApplicationResult(
            CreateCaptureProfilePipelineResult(),
            OutputDirectory: "profiles",
            ModelPath: "model.json",
            ProfilerProvider: "fixture",
            FixtureProfilePath: null);
        var buildResult = CreateBuildResult("model.json");

        var profileService = new StubProfileService(Result<CaptureProfileApplicationResult>.Success(profileResult));
        var extractService = new StubExtractService(Result<ExtractModelApplicationResult>.Success(extractionResult));
        var buildService = new RecordingBuildService(Result<BuildSsdtApplicationResult>.Success(buildResult));
        var schemaDataApplier = new RecordingSchemaDataApplier();
        var schemaApplyOrchestrator = new SchemaApplyOrchestrator(schemaDataApplier);
        var modelDeserializer = new StubModelJsonDeserializer(model);
        var uatRunner = new RecordingUatUsersRunner();
        var schemaGraphFactory = new RecordingSchemaGraphFactory();
        var coordinator = new FullExportCoordinator(schemaGraphFactory);

        var configuration = CliConfiguration.Empty with
        {
            Sql = CliConfiguration.Empty.Sql with
            {
                ConnectionString = "Server=Source;Database=Outsystems;"
            }
        };

        var service = new FullExportApplicationService(
            profileService,
            extractService,
            buildService,
            schemaApplyOrchestrator,
            modelDeserializer,
            uatRunner,
            coordinator);

        var configurationContext = new CliConfigurationContext(configuration, ConfigPath: null);
        var overrides = new FullExportOverrides(
            Build: new BuildSsdtOverrides(null, null, null, null, null, null, null, null),
            Profile: new CaptureProfileOverrides(null, null, null, null, null),
            Extract: new ExtractModelOverrides(null, null, null, null, null, null),
            Apply: null,
            ReuseModelPath: false,
            UatUsers: UatUsersOverrides.Disabled);
        var input = new FullExportApplicationInput(
            configurationContext,
            overrides,
            new ModuleFilterOverrides(Array.Empty<string>(), null, null, Array.Empty<string>(), Array.Empty<string>()),
            new SqlOptionsOverrides(
                ConnectionString: "Server=Source;Database=Outsystems;",
                CommandTimeoutSeconds: null,
                SamplingThreshold: null,
                SamplingSize: null,
                AuthenticationMethod: null,
                TrustServerCertificate: null,
                ApplicationName: null,
                AccessToken: null,
                ProfilingConnectionStrings: null),
            new CacheOptionsOverrides(null, null));

        var result = await service.RunAsync(input, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(schemaDataApplier.LastRequest);
        Assert.False(result.Value.Apply.Attempted);
        Assert.False(result.Value.ApplyOptions.Enabled);
        Assert.Null(result.Value.ApplyOptions.ConnectionString);
    }

    [Fact]
    public async Task RunAsync_UsesSchemaApplyOverrideSynchronizationMode()
    {
        var model = CreateModel();
        var extractionResult = CreateExtractionApplicationResult(model);
        var profileResult = new CaptureProfileApplicationResult(
            CreateCaptureProfilePipelineResult(),
            OutputDirectory: "profiles",
            ModelPath: "model.json",
            ProfilerProvider: "fixture",
            FixtureProfilePath: null);
        var buildResult = CreateBuildResult("model.json");

        var profileService = new StubProfileService(Result<CaptureProfileApplicationResult>.Success(profileResult));
        var extractService = new StubExtractService(Result<ExtractModelApplicationResult>.Success(extractionResult));
        var buildService = new RecordingBuildService(Result<BuildSsdtApplicationResult>.Success(buildResult));
        var schemaDataApplier = new RecordingSchemaDataApplier();
        var schemaApplyOrchestrator = new SchemaApplyOrchestrator(schemaDataApplier);
        var modelDeserializer = new StubModelJsonDeserializer(model);
        var uatRunner = new RecordingUatUsersRunner();
        var schemaGraphFactory = new RecordingSchemaGraphFactory();
        var coordinator = new FullExportCoordinator(schemaGraphFactory);

        var service = new FullExportApplicationService(
            profileService,
            extractService,
            buildService,
            schemaApplyOrchestrator,
            modelDeserializer,
            uatRunner,
            coordinator);

        var configurationContext = new CliConfigurationContext(CliConfiguration.Empty, ConfigPath: null);
        var overrides = new FullExportOverrides(
            Build: new BuildSsdtOverrides(null, null, null, null, null, null, null, null),
            Profile: new CaptureProfileOverrides(null, null, null, null, null),
            Extract: new ExtractModelOverrides(null, null, null, null, null, null),
            Apply: null,
            ReuseModelPath: false,
            UatUsers: UatUsersOverrides.Disabled);
        var applyOverrides = new SchemaApplyOverrides(
            Enabled: true,
            ConnectionString: "Server=Target;Database=Apply;Trusted_Connection=True;",
            CommandTimeoutSeconds: 45,
            AuthenticationMethod: SqlAuthenticationMethod.SqlPassword,
            TrustServerCertificate: true,
            ApplicationName: "OsmApply",
            AccessToken: null,
            ApplySafeScript: true,
            ApplyStaticSeeds: true,
            StaticSeedSynchronizationMode: StaticSeedSynchronizationMode.Authoritative);
        var input = new FullExportApplicationInput(
            configurationContext,
            overrides,
            new ModuleFilterOverrides(Array.Empty<string>(), null, null, Array.Empty<string>(), Array.Empty<string>()),
            new SqlOptionsOverrides(null, null, null, null, null, null, null, null, null),
            new CacheOptionsOverrides(null, null),
            TighteningOverrides: null,
            ApplyOverrides: applyOverrides);

        var result = await service.RunAsync(input, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(schemaDataApplier.LastRequest);
        Assert.Equal(
            StaticSeedSynchronizationMode.Authoritative,
            schemaDataApplier.LastRequest!.StaticSeedSynchronizationMode);
        Assert.Equal(
            StaticSeedSynchronizationMode.Authoritative,
            result.Value.ApplyOptions.StaticSeedSynchronizationMode);
    }

    [Fact]
    public async Task RunAsync_UsesConfigurationSynchronizationModeWhenOverrideMissing()
    {
        var model = CreateModel();
        var extractionResult = CreateExtractionApplicationResult(model);
        var profileResult = new CaptureProfileApplicationResult(
            CreateCaptureProfilePipelineResult(),
            OutputDirectory: "profiles",
            ModelPath: "model.json",
            ProfilerProvider: "fixture",
            FixtureProfilePath: null);
        var buildResult = CreateBuildResult("model.json");

        var profileService = new StubProfileService(Result<CaptureProfileApplicationResult>.Success(profileResult));
        var extractService = new StubExtractService(Result<ExtractModelApplicationResult>.Success(extractionResult));
        var buildService = new RecordingBuildService(Result<BuildSsdtApplicationResult>.Success(buildResult));
        var schemaDataApplier = new RecordingSchemaDataApplier();
        var schemaApplyOrchestrator = new SchemaApplyOrchestrator(schemaDataApplier);
        var modelDeserializer = new StubModelJsonDeserializer(model);
        var uatRunner = new RecordingUatUsersRunner();
        var schemaGraphFactory = new RecordingSchemaGraphFactory();
        var coordinator = new FullExportCoordinator(schemaGraphFactory);

        var staticSeeds = StaticSeedOptions.Create(
            groupByModule: true,
            emitMasterFile: false,
            StaticSeedSynchronizationMode.ValidateThenApply).Value;
        var emission = EmissionOptions.Create(
            perTableFiles: true,
            includePlatformAutoIndexes: false,
            sanitizeModuleNames: true,
            emitBareTableOnly: false,
            emitTableHeaders: false,
            moduleParallelism: 1,
            namingOverrides: TighteningOptions.Default.Emission.NamingOverrides,
            staticSeeds: staticSeeds).Value;
        var tightening = TighteningOptions.Create(
            TighteningOptions.Default.Policy,
            TighteningOptions.Default.ForeignKeys,
            TighteningOptions.Default.Uniqueness,
            TighteningOptions.Default.Remediation,
            emission,
            TighteningOptions.Default.Mocking).Value;
        var empty = CliConfiguration.Empty;
        var configuration = new CliConfiguration(
            tightening,
            empty.ModelPath,
            empty.ProfilePath,
            empty.DmmPath,
            empty.Cache,
            empty.Profiler,
            empty.Sql,
            empty.ModuleFilter,
            empty.TypeMapping,
            empty.SupplementalModels,
            empty.DynamicData,
            empty.UatUsers);

        var service = new FullExportApplicationService(
            profileService,
            extractService,
            buildService,
            schemaApplyOrchestrator,
            modelDeserializer,
            uatRunner,
            coordinator);

        var configurationContext = new CliConfigurationContext(configuration, ConfigPath: null);
        var overrides = new FullExportOverrides(
            Build: new BuildSsdtOverrides(null, null, null, null, null, null, null, null),
            Profile: new CaptureProfileOverrides(null, null, null, null, null),
            Extract: new ExtractModelOverrides(null, null, null, null, null, null),
            Apply: new SchemaApplyOverrides(
                Enabled: true,
                ConnectionString: "Server=Target;Database=Apply;Trusted_Connection=True;",
                CommandTimeoutSeconds: null,
                AuthenticationMethod: SqlAuthenticationMethod.SqlPassword,
                TrustServerCertificate: true,
                ApplicationName: null,
                AccessToken: null,
                ApplySafeScript: true,
                ApplyStaticSeeds: true,
                StaticSeedSynchronizationMode: null),
            ReuseModelPath: false,
            UatUsers: UatUsersOverrides.Disabled);
        var input = new FullExportApplicationInput(
            configurationContext,
            overrides,
            new ModuleFilterOverrides(Array.Empty<string>(), null, null, Array.Empty<string>(), Array.Empty<string>()),
            new SqlOptionsOverrides("Server=Test;", null, null, null, null, null, null, null, null),
            new CacheOptionsOverrides(null, null));

        var result = await service.RunAsync(input, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(schemaDataApplier.LastRequest);
        Assert.Equal(
            StaticSeedSynchronizationMode.ValidateThenApply,
            schemaDataApplier.LastRequest!.StaticSeedSynchronizationMode);
        Assert.Equal(
            StaticSeedSynchronizationMode.ValidateThenApply,
            result.Value.ApplyOptions.StaticSeedSynchronizationMode);
    }

    [Fact]
    public async Task RunAsync_ReturnsFailureWhenUatUsersPipelineFails()
    {
        var model = CreateModel();
        var extractionResult = CreateExtractionApplicationResult(model);
        var profileResult = new CaptureProfileApplicationResult(
            CreateCaptureProfilePipelineResult(),
            OutputDirectory: "profiles",
            ModelPath: "model.json",
            ProfilerProvider: "fixture",
            FixtureProfilePath: null);
        var buildResult = CreateBuildResult("model.json");

        var profileService = new StubProfileService(Result<CaptureProfileApplicationResult>.Success(profileResult));
        var extractService = new StubExtractService(Result<ExtractModelApplicationResult>.Success(extractionResult));
        var buildService = new RecordingBuildService(Result<BuildSsdtApplicationResult>.Success(buildResult));
        var schemaApplyOrchestrator = new SchemaApplyOrchestrator(new StubSchemaDataApplier());
        var modelDeserializer = new StubModelJsonDeserializer(model);
        var failureError = ValidationError.Create("pipeline.uatUsers.failure", "uat-users failed");
        var uatRunner = new RecordingUatUsersRunner
        {
            ResultToReturn = Result<UatUsersApplicationResult>.Failure(failureError)
        };
        var schemaGraphFactory = new RecordingSchemaGraphFactory
        {
            GraphToReturn = new ModelSchemaGraph(model)
        };
        var coordinator = new FullExportCoordinator(schemaGraphFactory);

        var service = new FullExportApplicationService(
            profileService,
            extractService,
            buildService,
            schemaApplyOrchestrator,
            modelDeserializer,
            uatRunner,
            coordinator);

        var configurationContext = new CliConfigurationContext(CliConfiguration.Empty, ConfigPath: null);
        var overrides = new FullExportOverrides(
            Build: new BuildSsdtOverrides(null, null, null, null, null, null, null, null),
            Profile: new CaptureProfileOverrides(null, null, null, null, null),
            Extract: new ExtractModelOverrides(null, null, null, null, null, null),
            Apply: null,
            ReuseModelPath: false,
            UatUsers: new UatUsersOverrides(
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
        var input = new FullExportApplicationInput(
            configurationContext,
            overrides,
            new ModuleFilterOverrides(Array.Empty<string>(), null, null, Array.Empty<string>(), Array.Empty<string>()),
            new SqlOptionsOverrides("Server=Test;", null, null, null, null, null, null, null, null),
            new CacheOptionsOverrides(null, null));

        var result = await service.RunAsync(input, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(failureError, Assert.Single(result.Errors));
        Assert.NotNull(uatRunner.LastRequest);
        Assert.Same(extractionResult.ExtractionResult, schemaGraphFactory.LastExtraction);
    }

    [Fact]
    public async Task RunAsync_ReturnsFailureWhenSchemaGraphFactoryFails()
    {
        var model = CreateModel();
        var extractionResult = CreateExtractionApplicationResult(model);
        var profileResult = new CaptureProfileApplicationResult(
            CreateCaptureProfilePipelineResult(),
            OutputDirectory: "profiles",
            ModelPath: "model.json",
            ProfilerProvider: "fixture",
            FixtureProfilePath: null);
        var buildResult = CreateBuildResult("model.json");

        var profileService = new StubProfileService(Result<CaptureProfileApplicationResult>.Success(profileResult));
        var extractService = new StubExtractService(Result<ExtractModelApplicationResult>.Success(extractionResult));
        var buildService = new RecordingBuildService(Result<BuildSsdtApplicationResult>.Success(buildResult));
        var schemaApplyOrchestrator = new SchemaApplyOrchestrator(new StubSchemaDataApplier());
        var modelDeserializer = new StubModelJsonDeserializer(model);
        var uatRunner = new RecordingUatUsersRunner();
        var schemaGraphFactory = new RecordingSchemaGraphFactory
        {
            ShouldFail = true,
            FailureErrors = ImmutableArray.Create(ValidationError.Create("uatUsers.schemaGraph.error", "graph failed"))
        };
        var coordinator = new FullExportCoordinator(schemaGraphFactory);

        var service = new FullExportApplicationService(
            profileService,
            extractService,
            buildService,
            schemaApplyOrchestrator,
            modelDeserializer,
            uatRunner,
            coordinator);

        var configurationContext = new CliConfigurationContext(CliConfiguration.Empty, ConfigPath: null);
        var overrides = new FullExportOverrides(
            Build: new BuildSsdtOverrides(null, null, null, null, null, null, null, null),
            Profile: new CaptureProfileOverrides(null, null, null, null, null),
            Extract: new ExtractModelOverrides(null, null, null, null, null, null),
            Apply: null,
            ReuseModelPath: false,
            UatUsers: new UatUsersOverrides(
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
        var input = new FullExportApplicationInput(
            configurationContext,
            overrides,
            new ModuleFilterOverrides(Array.Empty<string>(), null, null, Array.Empty<string>(), Array.Empty<string>()),
            new SqlOptionsOverrides(null, null, null, null, null, null, null, null, null),
            new CacheOptionsOverrides(null, null));

        var result = await service.RunAsync(input, CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("uatUsers.schemaGraph.error", error.Code);
        Assert.Null(uatRunner.LastRequest);
        Assert.Same(extractionResult.ExtractionResult, schemaGraphFactory.LastExtraction);
    }

    [Fact]
    public async Task RunAsync_SkipsUatUsersWhenDisabled()
    {
        var model = CreateModel();
        var extractionResult = CreateExtractionApplicationResult(model);
        var profileResult = new CaptureProfileApplicationResult(
            CreateCaptureProfilePipelineResult(),
            OutputDirectory: "profiles",
            ModelPath: "model.json",
            ProfilerProvider: "fixture",
            FixtureProfilePath: null);
        var buildResult = CreateBuildResult("model.json");

        var profileService = new StubProfileService(Result<CaptureProfileApplicationResult>.Success(profileResult));
        var extractService = new StubExtractService(Result<ExtractModelApplicationResult>.Success(extractionResult));
        var buildService = new RecordingBuildService(Result<BuildSsdtApplicationResult>.Success(buildResult));
        var schemaApplyOrchestrator = new SchemaApplyOrchestrator(new StubSchemaDataApplier());
        var modelDeserializer = new StubModelJsonDeserializer(model);
        var uatRunner = new RecordingUatUsersRunner();
        var schemaGraphFactory = new RecordingSchemaGraphFactory();
        var coordinator = new FullExportCoordinator(schemaGraphFactory);

        var service = new FullExportApplicationService(
            profileService,
            extractService,
            buildService,
            schemaApplyOrchestrator,
            modelDeserializer,
            uatRunner,
            coordinator);

        var configurationContext = new CliConfigurationContext(CliConfiguration.Empty, ConfigPath: null);
        var overrides = new FullExportOverrides(
            Build: new BuildSsdtOverrides(null, null, null, null, null, null, null, null),
            Profile: new CaptureProfileOverrides(null, null, null, null, null),
            Extract: new ExtractModelOverrides(null, null, null, null, null, null),
            Apply: null,
            ReuseModelPath: false,
            UatUsers: UatUsersOverrides.Disabled);
        var input = new FullExportApplicationInput(
            configurationContext,
            overrides,
            new ModuleFilterOverrides(Array.Empty<string>(), null, null, Array.Empty<string>(), Array.Empty<string>()),
            new SqlOptionsOverrides(null, null, null, null, null, null, null, null, null),
            new CacheOptionsOverrides(null, null));

        var result = await service.RunAsync(input, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.UatUsers.Executed);
        Assert.Null(uatRunner.LastRequest);
        Assert.Null(schemaGraphFactory.LastExtraction);
    }

    private static OsmModel CreateModel()
    {
        var moduleName = ModuleName.Create("AppCore").Value;
        var entityName = EntityName.Create("Customer").Value;
        var tableName = TableName.Create("OSUSR_CUSTOMER").Value;
        var schemaName = SchemaName.Create("dbo").Value;
        var attribute = AttributeModel.Create(
            AttributeName.Create("Id").Value,
            ColumnName.Create("Id").Value,
            dataType: "int",
            isMandatory: true,
            isIdentifier: true,
            isAutoNumber: false,
            isActive: true).Value;
        var entity = EntityModel.Create(
            moduleName,
            entityName,
            tableName,
            schemaName,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[] { attribute },
            metadata: EntityMetadata.Empty,
            allowMissingPrimaryKey: false).Value;
        var module = ModuleModel.Create(moduleName, isSystemModule: false, isActive: true, new[] { entity }).Value;
        return OsmModel.Create(DateTime.UtcNow, new[] { module }).Value;
    }

    private static ExtractModelApplicationResult CreateExtractionApplicationResult(OsmModel model)
    {
        var payload = ModelJsonPayload.FromStream(new MemoryStream(Array.Empty<byte>()));
        var metadata = new OutsystemsMetadataSnapshot(
            Array.Empty<OutsystemsModuleRow>(),
            Array.Empty<OutsystemsEntityRow>(),
            Array.Empty<OutsystemsAttributeRow>(),
            Array.Empty<OutsystemsReferenceRow>(),
            Array.Empty<OutsystemsPhysicalTableRow>(),
            Array.Empty<OutsystemsColumnRealityRow>(),
            Array.Empty<OutsystemsColumnCheckRow>(),
            Array.Empty<OutsystemsColumnCheckJsonRow>(),
            Array.Empty<OutsystemsPhysicalColumnPresenceRow>(),
            Array.Empty<OutsystemsIndexRow>(),
            Array.Empty<OutsystemsIndexColumnRow>(),
            Array.Empty<OutsystemsForeignKeyRow>(),
            Array.Empty<OutsystemsForeignKeyColumnRow>(),
            Array.Empty<OutsystemsForeignKeyAttrMapRow>(),
            Array.Empty<OutsystemsAttributeHasFkRow>(),
            Array.Empty<OutsystemsForeignKeyColumnsJsonRow>(),
            Array.Empty<OutsystemsForeignKeyAttributeJsonRow>(),
            Array.Empty<OutsystemsTriggerRow>(),
            Array.Empty<OutsystemsAttributeJsonRow>(),
            Array.Empty<OutsystemsRelationshipJsonRow>(),
            Array.Empty<OutsystemsIndexJsonRow>(),
            Array.Empty<OutsystemsTriggerJsonRow>(),
            Array.Empty<OutsystemsModuleJsonRow>(),
            "TestDatabase");

        var extraction = new ModelExtractionResult(
            model,
            payload,
            DateTimeOffset.UtcNow,
            Array.Empty<string>(),
            metadata,
            DynamicEntityDataset.Empty);
        return new ExtractModelApplicationResult(extraction, "model.json");
    }

    private static CaptureProfilePipelineResult CreateCaptureProfilePipelineResult()
    {
        var snapshot = ProfileSnapshot.Create(
            Array.Empty<ColumnProfile>(),
            Array.Empty<UniqueCandidateProfile>(),
            Array.Empty<CompositeUniqueCandidateProfile>(),
            Array.Empty<ForeignKeyReality>()).Value;
        var manifest = new CaptureProfileManifest(
            ModelPath: "model.json",
            ProfilePath: "profile.json",
            ProfilerProvider: "fixture",
            ModuleFilter: new CaptureProfileModuleSummary(false, Array.Empty<string>(), IncludeSystemModules: true, IncludeInactiveModules: true),
            SupplementalModels: new CaptureProfileSupplementalSummary(false, Array.Empty<string>()),
            Snapshot: new CaptureProfileSnapshotSummary(0, 0, 0, 0, 0),
            Insights: Array.Empty<CaptureProfileInsight>(),
            Warnings: Array.Empty<string>(),
            CapturedAtUtc: DateTimeOffset.UtcNow);
        return new CaptureProfilePipelineResult(
            snapshot,
            manifest,
            ProfilePath: "profile.json",
            ManifestPath: "manifest.json",
            Insights: ImmutableArray<ProfilingInsight>.Empty,
            ExecutionLog: PipelineExecutionLog.Empty,
            Warnings: ImmutableArray<string>.Empty,
            MultiEnvironmentReport: null);
    }

    private static BuildSsdtApplicationResult CreateBuildResult(string modelPath)
    {
        var profileSnapshot = ProfileSnapshot.Create(
            Array.Empty<ColumnProfile>(),
            Array.Empty<UniqueCandidateProfile>(),
            Array.Empty<CompositeUniqueCandidateProfile>(),
            Array.Empty<ForeignKeyReality>()).Value;
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
            TighteningToggleSnapshot.Create(TighteningOptions.Default));
        var manifest = new SsdtManifest(
            Array.Empty<TableManifestEntry>(),
            new SsdtManifestOptions(false, false, true, 1),
            null,
            new SsdtEmissionMetadata("sha256", "hash"),
            Array.Empty<PreRemediationManifestEntry>(),
            SsdtCoverageSummary.CreateComplete(0, 0, 0),
            SsdtPredicateCoverage.Empty,
            Array.Empty<string>());
        var opportunities = new Opportunities.OpportunitiesReport(
            ImmutableArray<Opportunities.Opportunity>.Empty,
            ImmutableDictionary<Opportunities.OpportunityDisposition, int>.Empty,
            ImmutableDictionary<Opportunities.OpportunityCategory, int>.Empty,
            ImmutableDictionary<Opportunities.OpportunityType, int>.Empty,
            ImmutableDictionary<RiskLevel, int>.Empty,
            DateTimeOffset.UtcNow);
        var validations = ValidationReport.Empty(DateTimeOffset.UtcNow);

        var pipelineResult = new BuildSsdtPipelineResult(
            profileSnapshot,
            ImmutableArray<ProfilingInsight>.Empty,
            decisionReport,
            opportunities,
            validations,
            manifest,
            ImmutableDictionary<string, ModuleManifestRollup>.Empty,
            ImmutableArray<PipelineInsight>.Empty,
            DecisionLogPath: "decision.json",
            OpportunitiesPath: "opportunities.json",
            ValidationsPath: "validations.json",
            SafeScriptPath: "safe.sql",
            SafeScript: string.Empty,
            RemediationScriptPath: "remediation.sql",
            RemediationScript: string.Empty,
            SqlProjectPath: "project.sqlproj",
            StaticSeedScriptPaths: ImmutableArray<string>.Empty,
            DynamicInsertScriptPaths: ImmutableArray<string>.Empty,
            TelemetryPackagePaths: ImmutableArray<string>.Empty,
            SsdtSqlValidationSummary.Empty,
            EvidenceCache: null,
            ExecutionLog: PipelineExecutionLog.Empty,
            StaticSeedTopologicalOrderApplied: false,
            DynamicInsertTopologicalOrderApplied: false,
            DynamicInsertOutputMode: DynamicInsertOutputMode.PerEntity,
            DynamicTableReconciliations: ImmutableArray<DynamicEntityTableReconciliation>.Empty,
            Warnings: ImmutableArray<string>.Empty,
            MultiEnvironmentReport: null);

        return new BuildSsdtApplicationResult(
            pipelineResult,
            ProfilerProvider: "fixture",
            ProfilePath: "profile.json",
            OutputDirectory: "out",
            ModelPath: modelPath,
            ModelWasExtracted: false,
            ModelExtractionWarnings: ImmutableArray<string>.Empty);
    }

    private sealed class StubProfileService : IApplicationService<CaptureProfileApplicationInput, CaptureProfileApplicationResult>
    {
        private readonly Result<CaptureProfileApplicationResult> _result;

        public StubProfileService(Result<CaptureProfileApplicationResult> result)
        {
            _result = result;
        }

        public Task<Result<CaptureProfileApplicationResult>> RunAsync(CaptureProfileApplicationInput input, CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }

    private sealed class RecordingExtractService : IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult>
    {
        public int CallCount { get; private set; }

        public Task<Result<ExtractModelApplicationResult>> RunAsync(ExtractModelApplicationInput input, CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException("Extract service should not be invoked when model reuse is enabled.");
        }
    }

    private sealed class StubExtractService : IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult>
    {
        private readonly Result<ExtractModelApplicationResult> _result;

        public StubExtractService(Result<ExtractModelApplicationResult> result)
        {
            _result = result;
        }

        public Task<Result<ExtractModelApplicationResult>> RunAsync(ExtractModelApplicationInput input, CancellationToken cancellationToken = default)
            => Task.FromResult(_result);
    }

    private sealed class RecordingBuildService : IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>
    {
        private readonly Result<BuildSsdtApplicationResult> _result;

        public RecordingBuildService(Result<BuildSsdtApplicationResult> result)
        {
            _result = result;
        }

        public BuildSsdtApplicationInput? LastInput { get; private set; }

        public Task<Result<BuildSsdtApplicationResult>> RunAsync(BuildSsdtApplicationInput input, CancellationToken cancellationToken = default)
        {
            LastInput = input;
            return Task.FromResult(_result);
        }
    }

    private sealed class RecordingUatUsersRunner : IUatUsersPipelineRunner
    {
        public Result<UatUsersApplicationResult> ResultToReturn { get; set; }
            = Result<UatUsersApplicationResult>.Success(UatUsersApplicationResult.Disabled);

        public UatUsersPipelineRequest? LastRequest { get; private set; }

        public Task<Result<UatUsersApplicationResult>> RunAsync(
            UatUsersPipelineRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(ResultToReturn);
        }
    }

    private sealed class RecordingSchemaGraphFactory : IModelUserSchemaGraphFactory
    {
        public ModelExtractionResult? LastExtraction { get; private set; }

        public ModelSchemaGraph? GraphToReturn { get; set; }

        public bool ShouldFail { get; set; }

        public ImmutableArray<ValidationError> FailureErrors { get; set; }
            = ImmutableArray<ValidationError>.Empty;

        public Result<ModelSchemaGraph> Create(ModelExtractionResult extraction)
        {
            LastExtraction = extraction;

            if (ShouldFail)
            {
                var errors = FailureErrors.IsDefaultOrEmpty
                    ? ImmutableArray.Create(ValidationError.Create("uatUsers.schemaGraph.failure", "Factory failure."))
                    : FailureErrors;
                return Result<ModelSchemaGraph>.Failure(errors);
            }

            if (GraphToReturn is not null)
            {
                return Result<ModelSchemaGraph>.Success(GraphToReturn);
            }

            return Result<ModelSchemaGraph>.Success(new ModelSchemaGraph(extraction.Model));
        }
    }

    private sealed class RecordingSchemaDataApplier : ISchemaDataApplier
    {
        public SchemaDataApplyRequest? LastRequest { get; private set; }

        public Task<Result<SchemaDataApplyOutcome>> ApplyAsync(
            SchemaDataApplyRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;

            return Task.FromResult(Result<SchemaDataApplyOutcome>.Success(new SchemaDataApplyOutcome(
                ImmutableArray<string>.Empty,
                ImmutableArray<string>.Empty,
                ExecutedBatchCount: 0,
                TimeSpan.Zero,
                MaxBatchSizeBytes: 0,
                StreamingEnabled: true,
                StaticSeedValidation: StaticSeedValidationSummary.NotAttempted)));
        }
    }

    private sealed class StubSchemaDataApplier : ISchemaDataApplier
    {
        public Task<Result<SchemaDataApplyOutcome>> ApplyAsync(
            SchemaDataApplyRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Result<SchemaDataApplyOutcome>.Success(new SchemaDataApplyOutcome(
                ImmutableArray<string>.Empty,
                ImmutableArray<string>.Empty,
                ExecutedBatchCount: 0,
                TimeSpan.Zero,
                MaxBatchSizeBytes: 0,
                StreamingEnabled: true,
                StaticSeedValidation: StaticSeedValidationSummary.NotAttempted)));
    }

    private sealed class StubModelJsonDeserializer : IModelJsonDeserializer
    {
        private readonly OsmModel _model;

        public StubModelJsonDeserializer(OsmModel model)
        {
            _model = model;
        }

        public Result<OsmModel> Deserialize(Stream jsonStream, ICollection<string>? warnings = null, ModelJsonDeserializerOptions? options = null)
        {
            warnings?.Clear();
            return Result<OsmModel>.Success(_model);
        }
    }
}
