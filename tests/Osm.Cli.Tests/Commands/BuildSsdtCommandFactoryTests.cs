using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli.Commands;
using Osm.Cli.Commands.Binders;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
using Osm.Emission;
using Osm.Pipeline.Application;
using Osm.Pipeline.DynamicData;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Evidence;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.Runtime;
using Osm.Pipeline.Runtime.Verbs;
using Osm.Validation.Tightening;
using OpportunitiesReport = Osm.Validation.Tightening.Opportunities.OpportunitiesReport;
using ValidationReport = Osm.Validation.Tightening.Validations.ValidationReport;
using Opportunity = Osm.Validation.Tightening.Opportunities.Opportunity;
using OpportunityType = Osm.Validation.Tightening.Opportunities.OpportunityType;
using OpportunityDisposition = Osm.Validation.Tightening.Opportunities.OpportunityDisposition;
using OpportunityCategory = Osm.Validation.Tightening.Opportunities.OpportunityCategory;
using Xunit;

namespace Osm.Cli.Tests.Commands;

public class BuildSsdtCommandFactoryTests
{
    [Fact]
    public async Task Invoke_ParsesOptionsAndRunsPipeline()
    {
        var configurationService = new FakeConfigurationService();
        var application = new FakeBuildApplicationService();

        var services = new ServiceCollection();
        services.AddSingleton<ICliConfigurationService>(configurationService);
        services.AddSingleton<IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>>(application);
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<ModuleFilterOptionBinder>();
        services.AddSingleton<CacheOptionBinder>();
        services.AddSingleton<SqlOptionBinder>();
        services.AddSingleton<TighteningOptionBinder>();
        services.AddSingleton<IVerbRegistry>(sp => new FakeVerbRegistry(configurationService, application));
        services.AddSingleton<BuildSsdtCommandFactory>();

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<BuildSsdtCommandFactory>();
        var command = factory.Create();
        Assert.NotNull(command.Handler);

        var moduleBinder = provider.GetRequiredService<ModuleFilterOptionBinder>();
        Assert.Contains(moduleBinder.ModulesOption, command.Options);

        var cacheBinder = provider.GetRequiredService<CacheOptionBinder>();
        Assert.Contains(cacheBinder.CacheRootOption, command.Options);

        var sqlBinder = provider.GetRequiredService<SqlOptionBinder>();
        Assert.Contains(sqlBinder.ConnectionStringOption, command.Options);
        Assert.Contains(sqlBinder.ProfilingConnectionStringsOption, command.Options);

        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var console = new TestConsole();
        var args = "build-ssdt --config config.json --modules ModuleA --include-system-modules --include-inactive-modules --allow-missing-primary-key Module::* --cache-root ./cache --refresh-cache --connection-string DataSource --model model.json --profile profile.json --profiler-provider fixture --static-data data.json --out output --rename-table Module=Override --max-degree-of-parallelism 4 --sql-metadata-out metadata.json --extract-model --remediation-generate-pre-scripts false --remediation-max-rows-default-backfill 500 --remediation-sentinel-numeric 999 --remediation-sentinel-text [NULL] --remediation-sentinel-date 2000-01-01 --use-profile-mock-folder --profile-mock-folder mocks";
        var exitCode = await parser.InvokeAsync(args, console);

        Assert.Equal(0, exitCode);
        Assert.Equal("config.json", configurationService.LastPath);
        var input = application.LastInput!;
        Assert.Equal("model.json", input.Overrides.ModelPath);
        Assert.Equal("profile.json", input.Overrides.ProfilePath);
        Assert.Equal("output", input.Overrides.OutputDirectory);
        Assert.Equal("fixture", input.Overrides.ProfilerProvider);
        Assert.Equal("data.json", input.Overrides.StaticDataPath);
        Assert.Equal("Module=Override", input.Overrides.RenameOverrides);
        Assert.Equal(4, input.Overrides.MaxDegreeOfParallelism);
        Assert.Equal("metadata.json", input.Overrides.SqlMetadataOutputPath);
        Assert.True(input.Overrides.ExtractModelInline);
        Assert.Equal(new[] { "ModuleA" }, input.ModuleFilter.Modules);
        Assert.True(input.ModuleFilter.IncludeSystemModules);
        Assert.True(input.ModuleFilter.IncludeInactiveModules);
        Assert.Equal(new[] { "Module::*" }, input.ModuleFilter.AllowMissingPrimaryKey);
        Assert.Equal("./cache", input.Cache.Root);
        Assert.True(input.Cache.Refresh);
        Assert.Equal("DataSource", input.Sql.ConnectionString);
        Assert.Null(input.Sql.ProfilingConnectionStrings);
        Assert.NotNull(input.TighteningOverrides);
        Assert.False(input.TighteningOverrides!.RemediationGeneratePreScripts);
        Assert.Equal(500, input.TighteningOverrides.RemediationMaxRowsDefaultBackfill);
        Assert.Equal("999", input.TighteningOverrides.RemediationSentinelNumeric);
        Assert.Equal("[NULL]", input.TighteningOverrides.RemediationSentinelText);
        Assert.Equal("2000-01-01", input.TighteningOverrides.RemediationSentinelDate);
        Assert.True(input.TighteningOverrides.MockingUseProfileMockFolder);
        Assert.Equal("mocks", input.TighteningOverrides.MockingProfileMockFolder);

        var result = application.LastResult!;
        Assert.Equal("output", result.OutputDirectory);
        Assert.Equal("model.json", result.ModelPath);
        Assert.Equal("fixture", result.ProfilerProvider);
        Assert.Single(result.PipelineResult.Manifest.Tables);
        var table = result.PipelineResult.Manifest.Tables.Single();
        Assert.Equal("Orders", table.Table);
        Assert.Equal("dbo", table.Schema);
        Assert.Equal("dbo.Orders.sql", table.TableFile);
        Assert.NotEmpty(result.PipelineResult.DecisionReport.TogglePrecedence);

        var output = console.Out.ToString() ?? string.Empty;
        Assert.Contains("SSDT build summary:", output);
        Assert.Contains("Output: output", output);
        Assert.Contains("Manifest: output/manifest.json", output);
        Assert.Contains("Decision log: decision.log", output);
        Assert.Contains("Opportunities: opportunities.json", output);
        Assert.Contains("Safe script: suggestions/safe-to-apply.sql", output);
        Assert.Contains("Remediation script: suggestions/needs-remediation.sql", output);
        Assert.Contains("Tightening: Columns 1/2, Unique 1/1, Foreign Keys 1/1", output);
        var summaryIndex = output.IndexOf("SSDT build summary:", StringComparison.Ordinal);
        var detailedIndex = output.IndexOf("SSDT Emission Summary:", StringComparison.Ordinal);
        Assert.InRange(summaryIndex, 0, int.MaxValue);
        Assert.InRange(detailedIndex, 0, int.MaxValue);
        Assert.True(summaryIndex < detailedIndex, output);
        Assert.Contains("SSDT Emission Summary:", output);
        Assert.Contains("Tables: 1 emitted to output", output);
        Assert.Contains("Manifest: ", output);
        Assert.Contains("manifest.json", output);
        Assert.Contains($"SQL project: {Path.Combine("output", "OutSystemsModel.sqlproj")}", output);
        Assert.Contains("Tightening Statistics:", output);
        Assert.Contains("Columns: 1/2 confirmed NOT NULL", output);
        Assert.Contains("Unique indexes: 1/1 confirmed UNIQUE", output);
        Assert.Contains("Foreign keys: 1/1 safe to create", output);
        Assert.Contains("SQL Validation:", output);
        Assert.Contains("Files: 1 validated, 0 with errors", output);
        Assert.Contains("Module summary:", output);
        Assert.Contains("Sales:", output);
        Assert.Contains("Tables: 1, Indexes: 0, Foreign Keys: 1", output);
        Assert.Contains("Columns: 2 total, 1 confirmed NOT NULL, 1 need remediation", output);
        Assert.Contains("Tightening toggles:", output);
        Assert.Contains("policy.mode = EvidenceGated (Configuration)", output);
        Assert.Contains("Tightening Artifacts:", output);
        Assert.Contains("Decision log: decision.log", output);

        Assert.Equal(string.Empty, console.Error.ToString());
    }

    [Fact]
    public async Task Invoke_SuppressesInformationalProfilingInsightsWhenNoActionRequired()
    {
        var configurationService = new FakeConfigurationService();
        var application = new FakeBuildApplicationService
        {
            ProfilingInsights = ImmutableArray.Create(
                new ProfilingInsight(
                    ProfilingInsightSeverity.Info,
                    ProfilingInsightCategory.Nullability,
                    "Column contains 12.5% null values.",
                    new ProfilingInsightCoordinate(
                        new SchemaName("dbo"),
                        new TableName("Orders"),
                        new ColumnName("CustomerId"),
                        null,
                        null,
                        null)))
        };

        var services = new ServiceCollection();
        services.AddSingleton<ICliConfigurationService>(configurationService);
        services.AddSingleton<IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>>(application);
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<ModuleFilterOptionBinder>();
        services.AddSingleton<CacheOptionBinder>();
        services.AddSingleton<SqlOptionBinder>();
        services.AddSingleton<TighteningOptionBinder>();
        services.AddSingleton<IVerbRegistry>(sp => new FakeVerbRegistry(configurationService, application));
        services.AddSingleton<BuildSsdtCommandFactory>();

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<BuildSsdtCommandFactory>();
        var command = factory.Create();
        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var console = new TestConsole();

        var args = "build-ssdt --model model.json --profile profile.json --out output";
        var exitCode = await parser.InvokeAsync(args, console);

        Assert.Equal(0, exitCode);

        var output = console.Out.ToString() ?? string.Empty;
        Assert.DoesNotContain("Profiling insights:", output);
        Assert.DoesNotContain("Informational insights:", output);
        Assert.DoesNotContain("Column contains 12.5% null values.", output);
    }

    [Fact]
    public async Task Invoke_EmitsSqlValidationErrorsWhenPresent()
    {
        var configurationService = new FakeConfigurationService();
        var summary = SsdtSqlValidationSummary.Create(
            2,
            new[]
            {
                SsdtSqlValidationIssue.Create(
                    "Modules/Sales/dbo.Orders.sql",
                    new[]
                    {
                        SsdtSqlValidationError.Create(102, 0, 16, 5, 12, "Incorrect syntax near ')'.")
                    })
            });
        var application = new FakeBuildApplicationService
        {
            SqlValidationSummaryOverride = summary
        };

        var services = new ServiceCollection();
        services.AddSingleton<ICliConfigurationService>(configurationService);
        services.AddSingleton<IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>>(application);
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<ModuleFilterOptionBinder>();
        services.AddSingleton<CacheOptionBinder>();
        services.AddSingleton<SqlOptionBinder>();
        services.AddSingleton<TighteningOptionBinder>();
        services.AddSingleton<IVerbRegistry>(sp => new FakeVerbRegistry(configurationService, application));
        services.AddSingleton<BuildSsdtCommandFactory>();

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<BuildSsdtCommandFactory>();
        var command = factory.Create();
        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var console = new TestConsole();

        var exitCode = await parser.InvokeAsync("build-ssdt --model model.json --profile profile.json --out output", console);

        Assert.Equal(0, exitCode);

        var output = console.Out.ToString() ?? string.Empty;
        Assert.Contains("SQL Validation:", output);
        Assert.Contains("Files: 2 validated, 1 with errors", output);
        Assert.Contains("Errors: 1 total", output);

        var errorOutput = console.Error.ToString() ?? string.Empty;
        Assert.Contains("Error samples:", errorOutput);
        Assert.Contains("Modules/Sales/dbo.Orders.sql:5:12 [#102] Incorrect syntax near ')'.", errorOutput);
    }

    [Fact]
    public async Task Invoke_WritesErrorsWhenPipelineFails()
    {
        var configurationService = new FakeConfigurationService();
        var application = new FakeBuildApplicationService
        {
            ShouldFail = true,
            FailureErrors = new[]
            {
                ValidationError.Create("cli.build", "Pipeline execution failed.")
            }
        };

        var services = new ServiceCollection();
        services.AddSingleton<ICliConfigurationService>(configurationService);
        services.AddSingleton<IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>>(application);
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<ModuleFilterOptionBinder>();
        services.AddSingleton<CacheOptionBinder>();
        services.AddSingleton<SqlOptionBinder>();
        services.AddSingleton<TighteningOptionBinder>();
        services.AddSingleton<IVerbRegistry>(sp => new FakeVerbRegistry(configurationService, application));
        services.AddSingleton<BuildSsdtCommandFactory>();

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<BuildSsdtCommandFactory>();
        var command = factory.Create();

        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var console = new TestConsole();
        var exitCode = await parser.InvokeAsync("build-ssdt --model model.json --profile profile.json", console);

        Assert.Equal(1, exitCode);
        Assert.Null(application.LastResult);
        Assert.Contains("cli.build: Pipeline execution failed.", console.Error.ToString());
    }

    [Fact]
    public async Task Invoke_WritesErrorMetadataWhenPresent()
    {
        var configurationService = new FakeConfigurationService();
        var application = new FakeBuildApplicationService
        {
            ShouldFail = true,
            FailureErrors = new[]
            {
                ValidationError
                    .Create("cli.build", "Pipeline execution failed.")
                    .WithMetadata("json.path", "$['modules'][0]")
            }
        };

        var services = new ServiceCollection();
        services.AddSingleton<ICliConfigurationService>(configurationService);
        services.AddSingleton<IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>>(application);
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<ModuleFilterOptionBinder>();
        services.AddSingleton<CacheOptionBinder>();
        services.AddSingleton<SqlOptionBinder>();
        services.AddSingleton<TighteningOptionBinder>();
        services.AddSingleton<IVerbRegistry>(sp => new FakeVerbRegistry(configurationService, application));
        services.AddSingleton<BuildSsdtCommandFactory>();

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<BuildSsdtCommandFactory>();
        var command = factory.Create();

        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var console = new TestConsole();
        var exitCode = await parser.InvokeAsync("build-ssdt --model model.json --profile profile.json", console);

        Assert.Equal(1, exitCode);
        var errorOutput = console.Error.ToString() ?? string.Empty;
        Assert.Contains("cli.build: Pipeline execution failed. | json.path=$['modules'][0]", errorOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invoke_WritesErrorsWhenConfigurationFails()
    {
        var configurationService = new FakeConfigurationService
        {
            FailureErrors = new[]
            {
                ValidationError.Create("cli.config", "Missing configuration.")
            }
        };
        var application = new FakeBuildApplicationService();

        var services = new ServiceCollection();
        services.AddSingleton<ICliConfigurationService>(configurationService);
        services.AddSingleton<IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>>(application);
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<ModuleFilterOptionBinder>();
        services.AddSingleton<CacheOptionBinder>();
        services.AddSingleton<SqlOptionBinder>();
        services.AddSingleton<TighteningOptionBinder>();
        services.AddSingleton<IVerbRegistry>(sp => new FakeVerbRegistry(configurationService, application));
        services.AddSingleton<BuildSsdtCommandFactory>();

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<BuildSsdtCommandFactory>();
        var command = factory.Create();

        var root = new RootCommand { command };
        var parser = new CommandLineBuilder(root).UseDefaults().Build();
        var console = new TestConsole();
        var exitCode = await parser.InvokeAsync("build-ssdt", console);

        Assert.Equal(1, exitCode);
        Assert.Null(application.LastInput);
        Assert.Contains("cli.config: Missing configuration.", console.Error.ToString());
    }

    private sealed class FakeConfigurationService : ICliConfigurationService
    {
        public string? LastPath { get; private set; }

        public IReadOnlyList<ValidationError>? FailureErrors { get; init; }

        public Task<Result<CliConfigurationContext>> LoadAsync(string? overrideConfigPath, CancellationToken cancellationToken = default)
        {
            LastPath = overrideConfigPath;
            if (FailureErrors is { Count: > 0 } errors)
            {
                return Task.FromResult(Result<CliConfigurationContext>.Failure(errors));
            }

            var context = new CliConfigurationContext(CliConfiguration.Empty, overrideConfigPath);
            return Task.FromResult(Result<CliConfigurationContext>.Success(context));
        }
    }

    private sealed class FakeBuildApplicationService : IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>
    {
        public BuildSsdtApplicationInput? LastInput { get; private set; }

        public BuildSsdtApplicationResult? LastResult { get; private set; }

        public bool ShouldFail { get; init; }

        public IReadOnlyList<ValidationError>? FailureErrors { get; init; }

        public ImmutableArray<ProfilingInsight> ProfilingInsights { get; init; } = ImmutableArray<ProfilingInsight>.Empty;

        public SsdtSqlValidationSummary? SqlValidationSummaryOverride { get; init; }

        public Task<Result<BuildSsdtApplicationResult>> RunAsync(BuildSsdtApplicationInput input, CancellationToken cancellationToken = default)
        {
            LastInput = input;
            if (ShouldFail)
            {
                return Task.FromResult(Result<BuildSsdtApplicationResult>.Failure(FailureErrors ?? Array.Empty<ValidationError>()));
            }

            LastResult = CreateResult();
            return Task.FromResult(Result<BuildSsdtApplicationResult>.Success(LastResult));
        }

        private BuildSsdtApplicationResult CreateResult()
        {
            var snapshot = ProfileSnapshot.Create(
                Array.Empty<ColumnProfile>(),
                Array.Empty<UniqueCandidateProfile>(),
                Array.Empty<CompositeUniqueCandidateProfile>(),
                Array.Empty<ForeignKeyReality>()).Value;

            var toggle = TighteningToggleSnapshot.Create(TighteningOptions.Default);
            var columns = ImmutableArray.Create(
                new ColumnDecisionReport(
                    new ColumnCoordinate(new SchemaName("dbo"), new TableName("Orders"), new ColumnName("CustomerId")),
                    true,
                    false,
                    ImmutableArray<string>.Empty),
                new ColumnDecisionReport(
                    new ColumnCoordinate(new SchemaName("dbo"), new TableName("Orders"), new ColumnName("Notes")),
                    false,
                    true,
                    ImmutableArray<string>.Empty));

            var uniqueIndexes = ImmutableArray.Create(
                new UniqueIndexDecisionReport(
                    new IndexCoordinate(new SchemaName("dbo"), new TableName("Orders"), new IndexName("IX_Orders_Customer")),
                    true,
                    false,
                    ImmutableArray<string>.Empty));

            var foreignKeys = ImmutableArray.Create(
                new ForeignKeyDecisionReport(
                    new ColumnCoordinate(new SchemaName("dbo"), new TableName("Orders"), new ColumnName("CustomerId")),
                    true,
                    false,
                    ImmutableArray<string>.Empty));

            var report = new PolicyDecisionReport(
                columns,
                uniqueIndexes,
                foreignKeys,
                ImmutableDictionary<string, int>.Empty,
                ImmutableDictionary<string, int>.Empty,
                ImmutableDictionary<string, int>.Empty,
                ImmutableArray<TighteningDiagnostic>.Empty,
                ImmutableDictionary<string, ModuleDecisionRollup>.Empty.Add(
                    "Sales",
                    new ModuleDecisionRollup(
                        ColumnCount: 2,
                        TightenedColumnCount: 1,
                        RemediationColumnCount: 1,
                        UniqueIndexCount: 1,
                        UniqueIndexesEnforcedCount: 1,
                        UniqueIndexesRequireRemediationCount: 0,
                        ForeignKeyCount: 1,
                        ForeignKeysCreatedCount: 1,
                        ImmutableDictionary<string, int>.Empty,
                        ImmutableDictionary<string, int>.Empty,
                        ImmutableDictionary<string, int>.Empty)),
                ImmutableDictionary<string, ToggleExportValue>.Empty.Add(
                    TighteningToggleKeys.PolicyMode,
                    new ToggleExportValue(TighteningMode.EvidenceGated, ToggleSource.Configuration)),
                columns.ToImmutableDictionary(static c => c.Column.ToString(), _ => "Sales"),
                uniqueIndexes.ToImmutableDictionary(static u => u.Index.ToString(), _ => "Sales"),
                toggle);

            var manifest = new SsdtManifest(
                new[]
                {
                    new TableManifestEntry(
                        Module: "Sales",
                        Schema: "dbo",
                        Table: "Orders",
                        TableFile: "dbo.Orders.sql",
                        Indexes: Array.Empty<string>(),
                        ForeignKeys: Array.Empty<string>(),
                        IncludesExtendedProperties: true)
                },
                new SsdtManifestOptions(false, false, false, 1),
                null,
                new SsdtEmissionMetadata("sha256", "hash"),
                Array.Empty<PreRemediationManifestEntry>(),
                SsdtCoverageSummary.CreateComplete(0, 0, 0),
                SsdtPredicateCoverage.Empty,
                Array.Empty<string>());

            var opportunities = new OpportunitiesReport(
                ImmutableArray<Opportunity>.Empty,
                ImmutableDictionary<OpportunityDisposition, int>.Empty,
                ImmutableDictionary<OpportunityCategory, int>.Empty,
                ImmutableDictionary<OpportunityType, int>.Empty,
                ImmutableDictionary<RiskLevel, int>.Empty,
                DateTimeOffset.UnixEpoch);

            var validations = ValidationReport.Empty(DateTimeOffset.UnixEpoch);

            var sqlValidation = SqlValidationSummaryOverride ?? SsdtSqlValidationSummary.Create(
                manifest.Tables.Count,
                Array.Empty<SsdtSqlValidationIssue>());

            var pipelineResult = new BuildSsdtPipelineResult(
                snapshot,
                ProfilingInsights,
                report,
                opportunities,
                validations,
                manifest,
                ImmutableDictionary<string, ModuleManifestRollup>.Empty.Add("Sales", new ModuleManifestRollup(1, 0, 1)),
                ImmutableArray<PipelineInsight>.Empty,
                "decision.log",
                "opportunities.json",
                "validations.json",
                "suggestions/safe-to-apply.sql",
                string.Empty,
                "suggestions/needs-remediation.sql",
                string.Empty,
                Path.Combine("output", "OutSystemsModel.sqlproj"),
                ImmutableArray<string>.Empty,
                ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty,
            sqlValidation,
            null,
            PipelineExecutionLog.Empty,
            StaticSeedTopologicalOrderApplied: false,
            DynamicInsertTopologicalOrderApplied: false,
            DynamicInsertOutputMode: DynamicInsertOutputMode.PerEntity,
            ImmutableArray<string>.Empty,
            MultiEnvironmentProfileReport.Empty);

            return new BuildSsdtApplicationResult(
                pipelineResult,
                "fixture",
                "profile.json",
                "output",
                "model.json",
                false,
                ImmutableArray<string>.Empty);
        }
    }

    private sealed class FakeVerbRegistry : IVerbRegistry
    {
        private readonly IPipelineVerb _verb;

        public FakeVerbRegistry(
            ICliConfigurationService configurationService,
            IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult> applicationService)
        {
            _verb = new BuildSsdtVerb(configurationService, applicationService);
        }

        public IPipelineVerb Get(string verbName) => _verb;

        public bool TryGet(string verbName, out IPipelineVerb verb)
        {
            verb = _verb;
            return true;
        }
    }
}
