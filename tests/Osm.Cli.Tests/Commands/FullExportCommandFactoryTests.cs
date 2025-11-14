using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.IO;
using System.IO.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli.Commands;
using Osm.Cli.Commands.Binders;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Profiling;
using Osm.Emission;
using Osm.LoadHarness;
using Osm.Pipeline.Application;
using Osm.Pipeline.DynamicData;
using Osm.Pipeline.UatUsers;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.SqlExtraction;
using Osm.Pipeline.Runtime;
using Osm.Pipeline.Runtime.Verbs;
using Osm.Validation.Tightening;
using OpportunitiesReport = Osm.Validation.Tightening.Opportunities.OpportunitiesReport;
using OpportunityCategory = Osm.Validation.Tightening.Opportunities.OpportunityCategory;
using OpportunityDisposition = Osm.Validation.Tightening.Opportunities.OpportunityDisposition;
using OpportunityType = Osm.Validation.Tightening.Opportunities.OpportunityType;
using Opportunity = Osm.Validation.Tightening.Opportunities.Opportunity;
using RiskLevel = Osm.Validation.Tightening.RiskLevel;
using ValidationReport = Osm.Validation.Tightening.Validations.ValidationReport;
using Tests.Support;
using Xunit;

namespace Osm.Cli.Tests.Commands;

public class FullExportCommandFactoryTests
{
    [Fact]
    public async Task Invoke_RunLoadHarnessSkipsWhenApplyConnectionMissing()
    {
        const string connectionString = "Server=Test;";
        using var tempDir = new TempDirectory();

        var loadHarnessRunner = new FakeLoadHarnessRunner();
        var configuration = CliConfiguration.Empty with
        {
            Sql = SqlConfiguration.Empty with
            {
                ConnectionString = connectionString
            }
        };

        var applicationResult = CreateFullExportApplicationResult(tempDir.Path, connectionString);
        var verbResult = new FullExportVerbResult(
            new CliConfigurationContext(configuration, "config.json"),
            applicationResult);

        var services = new ServiceCollection();
        services.AddSingleton<ICliConfigurationService>(new StubConfigurationService());
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<ModuleFilterOptionBinder>();
        services.AddSingleton<CacheOptionBinder>();
        services.AddSingleton<SqlOptionBinder>();
        services.AddSingleton<TighteningOptionBinder>();
        services.AddSingleton<SchemaApplyOptionBinder>();
        services.AddSingleton<UatUsersOptionBinder>();
        services.AddVerbOptionRegistryForTesting();
        services.AddSingleton<ILoadHarnessRunner>(loadHarnessRunner);
        services.AddSingleton<LoadHarnessReportWriter>(_ => new LoadHarnessReportWriter(new FileSystem()));
        var fakeVerb = new FakeFullExportVerb(verbResult);
        services.AddSingleton<IVerbRegistry>(_ => new FakeVerbRegistry(fakeVerb));
        services.AddSingleton<FullExportCommandFactory>();

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<FullExportCommandFactory>();
        var command = factory.Create();
        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var console = new TestConsole();

        var reportPath = Path.Combine(tempDir.Path, "load-harness.report.json");
        var args = $"full-export --run-load-harness --load-harness-report-out {reportPath}";
        var exitCode = await parser.InvokeAsync(args, console);

        Assert.Equal(0, exitCode);
        Assert.Null(loadHarnessRunner.LastOptions);
        Assert.Contains(
            "Load harness skipped (no connection string provided via CLI or configuration).",
            console.Error.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invoke_BindsUatUsersOverridesWhenEnabled()
    {
        using var tempDir = new TempDirectory();

        var loadHarnessRunner = new FakeLoadHarnessRunner();
        var configuration = CliConfiguration.Empty;
        var applicationResult = CreateFullExportApplicationResult(tempDir.Path, "Server=Test;");
        var verbResult = new FullExportVerbResult(
            new CliConfigurationContext(configuration, "config.json"),
            applicationResult);

        var services = new ServiceCollection();
        services.AddSingleton<ICliConfigurationService>(new StubConfigurationService());
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<ModuleFilterOptionBinder>();
        services.AddSingleton<CacheOptionBinder>();
        services.AddSingleton<SqlOptionBinder>();
        services.AddSingleton<TighteningOptionBinder>();
        services.AddSingleton<SchemaApplyOptionBinder>();
        services.AddSingleton<UatUsersOptionBinder>();
        services.AddVerbOptionRegistryForTesting();
        services.AddSingleton<ILoadHarnessRunner>(loadHarnessRunner);
        services.AddSingleton<LoadHarnessReportWriter>(_ => new LoadHarnessReportWriter(new FileSystem()));
        var fakeVerb = new FakeFullExportVerb(verbResult);
        services.AddSingleton<IVerbRegistry>(_ => new FakeVerbRegistry(fakeVerb));
        services.AddSingleton<FullExportCommandFactory>();

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<FullExportCommandFactory>();
        var command = factory.Create();
        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();

        var args = string.Join(
            ' ',
            "full-export",
            "--connection-string",
            "Server=QA;",
            "--enable-uat-users",
            "--user-table",
            "Security.Users",
            "--user-id-column",
            "PersonId",
            "--include-columns",
            "SourceId,TargetId",
            "--user-map",
            "map.csv",
            "--uat-user-inventory",
            "uat.csv",
            "--qa-user-inventory",
            "qa.csv",
            "--snapshot",
            "snapshot.json",
            "--user-entity-id",
            "Users::Entity");

        var exitCode = await parser.InvokeAsync(args);

        Assert.Equal(0, exitCode);
        Assert.NotNull(fakeVerb.LastOptions);
        var overrides = fakeVerb.LastOptions!.Overrides.UatUsers;
        Assert.NotNull(overrides);
        Assert.True(overrides!.Enabled);
        Assert.Equal("Security", overrides.UserSchema);
        Assert.Equal("Users", overrides.UserTable);
        Assert.Equal("PersonId", overrides.UserIdColumn);
        Assert.Equal(new[] { "SourceId", "TargetId" }, overrides.IncludeColumns);
        Assert.Equal("map.csv", overrides.UserMapPath);
        Assert.Equal("uat.csv", overrides.UatUserInventoryPath);
        Assert.Equal("qa.csv", overrides.QaUserInventoryPath);
        Assert.Equal("snapshot.json", overrides.SnapshotPath);
        Assert.Equal("Users::Entity", overrides.UserEntityIdentifier);
        Assert.False(overrides.IdempotentEmission);
    }

    [Fact]
    public async Task Invoke_BindsUatUsersIdempotentFlag()
    {
        using var tempDir = new TempDirectory();

        var loadHarnessRunner = new FakeLoadHarnessRunner();
        var configuration = CliConfiguration.Empty;
        var applicationResult = CreateFullExportApplicationResult(tempDir.Path, "Server=Test;");
        var verbResult = new FullExportVerbResult(
            new CliConfigurationContext(configuration, "config.json"),
            applicationResult);

        var services = new ServiceCollection();
        services.AddSingleton<ICliConfigurationService>(new StubConfigurationService());
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<ModuleFilterOptionBinder>();
        services.AddSingleton<CacheOptionBinder>();
        services.AddSingleton<SqlOptionBinder>();
        services.AddSingleton<TighteningOptionBinder>();
        services.AddSingleton<SchemaApplyOptionBinder>();
        services.AddSingleton<UatUsersOptionBinder>();
        services.AddVerbOptionRegistryForTesting();
        services.AddSingleton<ILoadHarnessRunner>(loadHarnessRunner);
        services.AddSingleton<LoadHarnessReportWriter>(_ => new LoadHarnessReportWriter(new FileSystem()));
        var fakeVerb = new FakeFullExportVerb(verbResult);
        services.AddSingleton<IVerbRegistry>(_ => new FakeVerbRegistry(fakeVerb));
        services.AddSingleton<FullExportCommandFactory>();

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<FullExportCommandFactory>();
        var command = factory.Create();
        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();

        var args = "full-export --connection-string Server=QA; --enable-uat-users --uat-user-inventory uat.csv --qa-user-inventory qa.csv --uat-users-idempotent-emission";

        var exitCode = await parser.InvokeAsync(args);

        Assert.Equal(0, exitCode);
        var overrides = fakeVerb.LastOptions!.Overrides.UatUsers;
        Assert.True(overrides!.IdempotentEmission);
    }

    [Fact]
    public async Task Invoke_WhenUatUsersEnabledRequiresUatInventory()
    {
        using var tempDir = new TempDirectory();

        var configuration = CliConfiguration.Empty;
        var applicationResult = CreateFullExportApplicationResult(tempDir.Path, "Server=Test;");
        var verbResult = new FullExportVerbResult(
            new CliConfigurationContext(configuration, "config.json"),
            applicationResult);

        var services = new ServiceCollection();
        services.AddSingleton<ICliConfigurationService>(new StubConfigurationService());
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<ModuleFilterOptionBinder>();
        services.AddSingleton<CacheOptionBinder>();
        services.AddSingleton<SqlOptionBinder>();
        services.AddSingleton<TighteningOptionBinder>();
        services.AddSingleton<SchemaApplyOptionBinder>();
        services.AddSingleton<UatUsersOptionBinder>();
        services.AddVerbOptionRegistryForTesting();
        services.AddSingleton<ILoadHarnessRunner, FakeLoadHarnessRunner>();
        services.AddSingleton<LoadHarnessReportWriter>(_ => new LoadHarnessReportWriter(new FileSystem()));
        var fakeVerb = new FakeFullExportVerb(verbResult);
        services.AddSingleton<IVerbRegistry>(_ => new FakeVerbRegistry(fakeVerb));
        services.AddSingleton<FullExportCommandFactory>();

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<FullExportCommandFactory>();
        var command = factory.Create();
        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var console = new TestConsole();

        var args = string.Join(
            ' ',
            "full-export",
            "--connection-string",
            "Server=QA;",
            "--enable-uat-users",
            "--qa-user-inventory",
            "qa.csv");

        var exitCode = await parser.InvokeAsync(args, console);

        Assert.Equal(1, exitCode);
        Assert.Contains("--uat-user-inventory must be supplied", console.Error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Null(fakeVerb.LastOptions);
    }

    [Fact]
    public async Task Invoke_WhenUatUsersEnabledRequiresQaInventory()
    {
        using var tempDir = new TempDirectory();

        var configuration = CliConfiguration.Empty;
        var applicationResult = CreateFullExportApplicationResult(tempDir.Path, "Server=Test;");
        var verbResult = new FullExportVerbResult(
            new CliConfigurationContext(configuration, "config.json"),
            applicationResult);

        var services = new ServiceCollection();
        services.AddSingleton<ICliConfigurationService>(new StubConfigurationService());
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<ModuleFilterOptionBinder>();
        services.AddSingleton<CacheOptionBinder>();
        services.AddSingleton<SqlOptionBinder>();
        services.AddSingleton<TighteningOptionBinder>();
        services.AddSingleton<SchemaApplyOptionBinder>();
        services.AddSingleton<UatUsersOptionBinder>();
        services.AddVerbOptionRegistryForTesting();
        services.AddSingleton<ILoadHarnessRunner, FakeLoadHarnessRunner>();
        services.AddSingleton<LoadHarnessReportWriter>(_ => new LoadHarnessReportWriter(new FileSystem()));
        var fakeVerb = new FakeFullExportVerb(verbResult);
        services.AddSingleton<IVerbRegistry>(_ => new FakeVerbRegistry(fakeVerb));
        services.AddSingleton<FullExportCommandFactory>();

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<FullExportCommandFactory>();
        var command = factory.Create();
        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var console = new TestConsole();

        var args = string.Join(
            ' ',
            "full-export",
            "--connection-string",
            "Server=QA;",
            "--enable-uat-users",
            "--uat-user-inventory",
            "allowed.csv");

        var exitCode = await parser.InvokeAsync(args, console);

        Assert.Equal(1, exitCode);
        Assert.Contains("--qa-user-inventory must be supplied", console.Error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Null(fakeVerb.LastOptions);
    }

    private static FullExportApplicationResult CreateFullExportApplicationResult(string root, string connectionString)
    {
        var modelPath = FixtureFile.GetPath("model.edge-case.json");
        var profilePath = FixtureFile.GetPath(Path.Combine("profiling", "profile.edge-case.json"));
        var extraction = CreateExtractionApplicationResult(modelPath);
        var profilesDirectory = Path.Combine(root, "Profiles");
        var capture = CreateCaptureApplicationResult(profilePath, modelPath, profilesDirectory);
        var buildOutput = Path.Combine(root, "Build");
        Directory.CreateDirectory(buildOutput);
        var safeScriptPath = Path.Combine(buildOutput, "safe.sql");
        File.WriteAllText(safeScriptPath, "PRINT 'safe';");
        var remediationScriptPath = Path.Combine(buildOutput, "remediation.sql");
        File.WriteAllText(remediationScriptPath, "PRINT 'remediation';");
        var staticSeedPath = Path.Combine(buildOutput, "StaticEntities.seed.sql");
        File.WriteAllText(staticSeedPath, "PRINT 'seed';");
        var build = CreateBuildApplicationResult(
            buildOutput,
            modelPath,
            profilePath,
            safeScriptPath,
            remediationScriptPath,
            ImmutableArray.Create(staticSeedPath));

        var apply = new SchemaApplyResult(
            Attempted: false,
            SafeScriptApplied: false,
            StaticSeedsApplied: false,
            AppliedScripts: ImmutableArray<string>.Empty,
            AppliedSeedScripts: ImmutableArray<string>.Empty,
            SkippedScripts: ImmutableArray<string>.Empty,
            Warnings: ImmutableArray<string>.Empty,
            PendingRemediationCount: 0,
            SafeScriptPath: safeScriptPath,
            RemediationScriptPath: remediationScriptPath,
            StaticSeedScriptPaths: ImmutableArray.Create(staticSeedPath),
            Duration: TimeSpan.Zero,
            StaticSeedSynchronizationMode: StaticSeedSynchronizationMode.NonDestructive,
            StaticSeedValidation: StaticSeedValidationSummary.NotAttempted);

        var authentication = new SqlAuthenticationSettings(null, null, null, null);
        var applyOptions = new SchemaApplyOptions(
            Enabled: true,
            ConnectionString: connectionString,
            Authentication: authentication,
            CommandTimeoutSeconds: null);

        return new FullExportApplicationResult(
            build,
            capture,
            extraction,
            apply,
            applyOptions,
            UatUsersApplicationResult.Disabled);
    }

    private static ExtractModelApplicationResult CreateExtractionApplicationResult(string modelPath)
    {
        var extractionResult = new ModelExtractionResult(
            ModelFixtures.LoadModel("model.edge-case.json"),
            ModelJsonPayload.FromFile(modelPath),
            DateTimeOffset.UtcNow,
            ImmutableArray<string>.Empty,
            CreateMetadataSnapshot("TestDatabase"),
            DynamicEntityDataset.Empty);

        return new ExtractModelApplicationResult(extractionResult, modelPath);
    }

    private static CaptureProfileApplicationResult CreateCaptureApplicationResult(
        string profilePath,
        string modelPath,
        string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var manifestPath = Path.Combine(outputDirectory, "manifest.json");
        File.WriteAllText(manifestPath, "{}");

        var profile = ProfileFixtures.LoadSnapshot(Path.Combine("profiling", "profile.edge-case.json"));
        var manifest = new CaptureProfileManifest(
            modelPath,
            profilePath,
            "fixture",
            new CaptureProfileModuleSummary(false, Array.Empty<string>(), true, true),
            new CaptureProfileSupplementalSummary(false, Array.Empty<string>()),
            new CaptureProfileSnapshotSummary(0, 0, 0, 0, 0),
            Array.Empty<CaptureProfileInsight>(),
            Array.Empty<string>(),
            DateTimeOffset.UtcNow);

        var pipelineResult = new CaptureProfilePipelineResult(
            profile,
            manifest,
            profilePath,
            manifestPath,
            ImmutableArray<ProfilingInsight>.Empty,
            PipelineExecutionLog.Empty,
            ImmutableArray<string>.Empty,
            null);

        return new CaptureProfileApplicationResult(
            pipelineResult,
            outputDirectory,
            modelPath,
            "fixture",
            profilePath);
    }

    private static BuildSsdtApplicationResult CreateBuildApplicationResult(
        string outputDirectory,
        string modelPath,
        string profilePath,
        string safeScriptPath,
        string remediationScriptPath,
        ImmutableArray<string> staticSeedPaths)
    {
        Directory.CreateDirectory(outputDirectory);
        var manifestFilePath = Path.Combine(outputDirectory, "manifest.json");
        File.WriteAllText(manifestFilePath, "{}");

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
        var togglePrecedence = toggleSnapshot
            .ToExportDictionary()
            .ToImmutableDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

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
            ImmutableDictionary<OpportunityCategory, int>.Empty,
            ImmutableDictionary<OpportunityType, int>.Empty,
            ImmutableDictionary<RiskLevel, int>.Empty,
            DateTimeOffset.UtcNow);

        var validations = ValidationReport.Empty(DateTimeOffset.UtcNow);

        var decisionLogPath = Path.Combine(outputDirectory, "decision-log.json");
        File.WriteAllText(decisionLogPath, "{}");
        var opportunitiesPath = Path.Combine(outputDirectory, "opportunities.json");
        File.WriteAllText(opportunitiesPath, "{}");
        var validationsPath = Path.Combine(outputDirectory, "validations.json");
        File.WriteAllText(validationsPath, "{}");

        var pipelineResult = new BuildSsdtPipelineResult(
            ProfileFixtures.LoadSnapshot(Path.Combine("profiling", "profile.edge-case.json")),
            ImmutableArray<ProfilingInsight>.Empty,
            decisionReport,
            opportunities,
            validations,
            manifest,
            ImmutableDictionary<string, ModuleManifestRollup>.Empty,
            ImmutableArray<PipelineInsight>.Empty,
            decisionLogPath,
            opportunitiesPath,
            validationsPath,
            safeScriptPath,
            "PRINT 'safe';",
            remediationScriptPath,
            "PRINT 'remediation';",
            Path.Combine(outputDirectory, "OutSystemsModel.sqlproj"),
            staticSeedPaths,
            ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty,
            SsdtSqlValidationSummary.Empty,
            null,
            PipelineExecutionLog.Empty,
            StaticSeedTopologicalOrderApplied: false,
            DynamicInsertTopologicalOrderApplied: false,
            DynamicInsertOutputMode: DynamicInsertOutputMode.PerEntity,
            ImmutableArray<string>.Empty,
            null);

        return new BuildSsdtApplicationResult(
            pipelineResult,
            "fixture",
            profilePath,
            outputDirectory,
            modelPath,
            true,
            ImmutableArray<string>.Empty);
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

    private sealed class FakeFullExportVerb : IPipelineVerb
    {
        private readonly FullExportVerbResult _result;

        public FakeFullExportVerb(FullExportVerbResult result)
        {
            _result = result;
        }

        public FullExportVerbOptions? LastOptions { get; private set; }

        public string Name => FullExportVerb.VerbName;

        public Type OptionsType => typeof(FullExportVerbOptions);

        public Task<IPipelineRun> RunAsync(object options, CancellationToken cancellationToken = default)
        {
            LastOptions = Assert.IsType<FullExportVerbOptions>(options);
            var outcome = Result<FullExportVerbResult>.Success(_result);
            var run = new PipelineRun<FullExportVerbResult>(
                Name,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                outcome,
                Array.Empty<PipelineArtifact>(),
                new Dictionary<string, string?>());

            return Task.FromResult<IPipelineRun>(run);
        }
    }

    private sealed class FakeVerbRegistry : IVerbRegistry
    {
        private readonly IPipelineVerb _verb;

        public FakeVerbRegistry(IPipelineVerb verb)
        {
            _verb = verb;
        }

        public IPipelineVerb Get(string verbName)
        {
            if (!string.Equals(verbName, _verb.Name, StringComparison.OrdinalIgnoreCase))
            {
                throw new KeyNotFoundException();
            }

            return _verb;
        }

        public bool TryGet(string verbName, out IPipelineVerb verb)
        {
            if (string.Equals(verbName, _verb.Name, StringComparison.OrdinalIgnoreCase))
            {
                verb = _verb;
                return true;
            }

            verb = default!;
            return false;
        }
    }

    private sealed class FakeLoadHarnessRunner : ILoadHarnessRunner
    {
        public LoadHarnessOptions? LastOptions { get; private set; }

        public Task<LoadHarnessReport> RunAsync(LoadHarnessOptions options, CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            return Task.FromResult(LoadHarnessReport.Empty());
        }
    }

    private sealed class StubConfigurationService : ICliConfigurationService
    {
        public Task<Result<CliConfigurationContext>> LoadAsync(string? overrideConfigPath, CancellationToken cancellationToken = default)
        {
            var context = new CliConfigurationContext(CliConfiguration.Empty, overrideConfigPath);
            return Task.FromResult(Result<CliConfigurationContext>.Success(context));
        }
    }
}
