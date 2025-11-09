using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.CommandLine.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli.Commands;
using Osm.Cli.Commands.Binders;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Runtime;
using Osm.Pipeline.Runtime.Verbs;
using Xunit;

namespace Osm.Cli.Tests.Commands;

public class PipelineCommandFactorySharedTests
{
    public static IEnumerable<object[]> PipelineFactories()
    {
        yield return new object[]
        {
            new PipelineCommandCase(
                CommandName: "build-ssdt",
                SharedArgs: "--config config.json --model model.json --profile profile.json --out output --max-degree-of-parallelism 3",
                FactoryType: typeof(BuildSsdtCommandFactory),
                VerbName: BuildSsdtVerb.VerbName,
                OptionsType: typeof(BuildSsdtVerbOptions),
                ConfigureServices: (services, verb) =>
                {
                    services.AddSingleton<CliGlobalOptions>();
                    services.AddSingleton<ModuleFilterOptionBinder>();
                    services.AddSingleton<CacheOptionBinder>();
                    services.AddSingleton<SqlOptionBinder>();
                    services.AddSingleton<TighteningOptionBinder>();
                    services.AddSingleton<IVerbRegistry>(_ => new SingleVerbRegistry(verb));
                    services.AddSingleton<BuildSsdtCommandFactory>();
                },
                AssertOptions: options =>
                {
                    var typed = Assert.IsType<BuildSsdtVerbOptions>(options);
                    Assert.Equal("config.json", typed.ConfigurationPath);
                    Assert.Equal("model.json", typed.Overrides.ModelPath);
                    Assert.Equal("profile.json", typed.Overrides.ProfilePath);
                    Assert.Equal("output", typed.Overrides.OutputDirectory);
                    Assert.Equal(3, typed.Overrides.MaxDegreeOfParallelism);
                })
        };

        yield return new object[]
        {
            new PipelineCommandCase(
                CommandName: "dmm-compare",
                SharedArgs: "--config config.json --model model.json --profile profile.json --dmm baseline.dmm --out diff --max-degree-of-parallelism 2",
                FactoryType: typeof(DmmCompareCommandFactory),
                VerbName: DmmCompareVerb.VerbName,
                OptionsType: typeof(DmmCompareVerbOptions),
                ConfigureServices: (services, verb) =>
                {
                    services.AddSingleton<CliGlobalOptions>();
                    services.AddSingleton<ModuleFilterOptionBinder>();
                    services.AddSingleton<CacheOptionBinder>();
                    services.AddSingleton<SqlOptionBinder>();
                    services.AddSingleton<TighteningOptionBinder>();
                    services.AddSingleton<IVerbRegistry>(_ => new SingleVerbRegistry(verb));
                    services.AddSingleton<DmmCompareCommandFactory>();
                },
                AssertOptions: options =>
                {
                    var typed = Assert.IsType<DmmCompareVerbOptions>(options);
                    Assert.Equal("config.json", typed.ConfigurationPath);
                    Assert.Equal("model.json", typed.Overrides.ModelPath);
                    Assert.Equal("profile.json", typed.Overrides.ProfilePath);
                    Assert.Equal("baseline.dmm", typed.Overrides.DmmPath);
                    Assert.Equal("diff", typed.Overrides.OutputDirectory);
                    Assert.Equal(2, typed.Overrides.MaxDegreeOfParallelism);
                })
        };

        yield return new object[]
        {
            new PipelineCommandCase(
                CommandName: "extract-model",
                SharedArgs: "--config config.json --out extracted.json --only-active-attributes",
                FactoryType: typeof(ExtractModelCommandFactory),
                VerbName: ExtractModelVerb.VerbName,
                OptionsType: typeof(ExtractModelVerbOptions),
                ConfigureServices: (services, verb) =>
                {
                    services.AddSingleton<CliGlobalOptions>();
                    services.AddSingleton<ModuleFilterOptionBinder>();
                    services.AddSingleton<SqlOptionBinder>();
                    services.AddSingleton<IVerbRegistry>(_ => new SingleVerbRegistry(verb));
                    services.AddSingleton<ExtractModelCommandFactory>();
                },
                AssertOptions: options =>
                {
                    var typed = Assert.IsType<ExtractModelVerbOptions>(options);
                    Assert.Equal("config.json", typed.ConfigurationPath);
                    Assert.Equal("extracted.json", typed.Overrides.OutputPath);
                    Assert.True(typed.Overrides.OnlyActiveAttributes);
                })
        };

        yield return new object[]
        {
            new PipelineCommandCase(
                CommandName: "full-export",
                SharedArgs: string.Join(
                    ' ',
                    "--config config.json",
                    "--modules ModuleA",
                    "--include-system-modules",
                    "--include-inactive-modules",
                    "--allow-missing-primary-key Module::*",
                    "--allow-missing-schema Module::*",
                    "--cache-root ./cache",
                    "--refresh-cache",
                    "--connection-string Server=Sql01;Database=Osm;Trusted_Connection=True;",
                    "--command-timeout 120",
                    "--sampling-threshold 5000",
                    "--sampling-size 250",
                    "--sql-authentication ActiveDirectoryPassword",
                    "--sql-trust-server-certificate false",
                    "--sql-application-name OsmCli",
                    "--sql-access-token TOKEN",
                    "--profiling-connection-string Perf::Server02",
                    "--model model.json",
                    "--profile profile.json",
                    "--build-out ./build",
                    "--profiler-provider fixture",
                    "--static-data static.json",
                    "--rename-table Module=Override",
                    "--max-degree-of-parallelism 4",
                    "--build-sql-metadata-out build-metadata.json",
                    "--profile-out ./profiles",
                    "--profile-sql-metadata-out profile-metadata.json",
                    "--extract-out extracted.json",
                    "--only-active-attributes",
                    "--mock-advanced-sql manifest.json",
                    "--extract-sql-metadata-out extract-metadata.json",
                    "--remediation-generate-pre-scripts false",
                    "--remediation-max-rows-default-backfill 250",
                    "--remediation-sentinel-numeric 999",
                    "--remediation-sentinel-text [NULL]",
                    "--remediation-sentinel-date 2000-01-01",
                    "--use-profile-mock-folder",
                    "--profile-mock-folder mocks"),
                FactoryType: typeof(FullExportCommandFactory),
                VerbName: FullExportVerb.VerbName,
                OptionsType: typeof(FullExportVerbOptions),
                ConfigureServices: (services, verb) =>
                {
                    services.AddSingleton<CliGlobalOptions>();
                    services.AddSingleton<ModuleFilterOptionBinder>();
                    services.AddSingleton<CacheOptionBinder>();
                    services.AddSingleton<SqlOptionBinder>();
                    services.AddSingleton<TighteningOptionBinder>();
                    services.AddSingleton<IVerbRegistry>(_ => new SingleVerbRegistry(verb));
                    services.AddSingleton<FullExportCommandFactory>();
                },
                AssertOptions: options =>
                {
                    var typed = Assert.IsType<FullExportVerbOptions>(options);
                    Assert.Equal("config.json", typed.ConfigurationPath);

                    Assert.Equal(new[] { "ModuleA" }, typed.ModuleFilter.Modules);
                    Assert.True(typed.ModuleFilter.IncludeSystemModules);
                    Assert.True(typed.ModuleFilter.IncludeInactiveModules);
                    Assert.Equal(new[] { "Module::*" }, typed.ModuleFilter.AllowMissingPrimaryKey);
                    Assert.Equal(new[] { "Module::*" }, typed.ModuleFilter.AllowMissingSchema);

                    Assert.Equal("./cache", typed.Cache.Root);
                    Assert.True(typed.Cache.Refresh);

                    Assert.Equal("Server=Sql01;Database=Osm;Trusted_Connection=True;", typed.Sql.ConnectionString);
                    Assert.Equal(120, typed.Sql.CommandTimeoutSeconds);
                    Assert.Equal(5000, typed.Sql.SamplingThreshold);
                    Assert.Equal(250, typed.Sql.SamplingSize);
                    Assert.Equal(SqlAuthenticationMethod.ActiveDirectoryPassword, typed.Sql.AuthenticationMethod);
                    Assert.Equal(false, typed.Sql.TrustServerCertificate);
                    Assert.Equal("OsmCli", typed.Sql.ApplicationName);
                    Assert.Equal("TOKEN", typed.Sql.AccessToken);
                    Assert.Equal(new[] { "Perf::Server02" }, typed.Sql.ProfilingConnectionStrings);

                    Assert.NotNull(typed.Tightening);
                    Assert.False(typed.Tightening!.RemediationGeneratePreScripts);
                    Assert.Equal(250, typed.Tightening.RemediationMaxRowsDefaultBackfill);
                    Assert.True(typed.Tightening.MockingUseProfileMockFolder);
                    Assert.Equal("mocks", typed.Tightening.MockingProfileMockFolder);
                    Assert.Equal("999", typed.Tightening.RemediationSentinelNumeric);
                    Assert.Equal("[NULL]", typed.Tightening.RemediationSentinelText);
                    Assert.Equal("2000-01-01", typed.Tightening.RemediationSentinelDate);

                    Assert.Equal("model.json", typed.Overrides.Build.ModelPath);
                    Assert.Equal("profile.json", typed.Overrides.Build.ProfilePath);
                    Assert.Equal("./build", typed.Overrides.Build.OutputDirectory);
                    Assert.Equal("fixture", typed.Overrides.Build.ProfilerProvider);
                    Assert.Equal("static.json", typed.Overrides.Build.StaticDataPath);
                    Assert.Equal("Module=Override", typed.Overrides.Build.RenameOverrides);
                    Assert.Equal(4, typed.Overrides.Build.MaxDegreeOfParallelism);
                    Assert.Equal("build-metadata.json", typed.Overrides.Build.SqlMetadataOutputPath);

                    Assert.Equal("model.json", typed.Overrides.Profile.ModelPath);
                    Assert.Equal("./profiles", typed.Overrides.Profile.OutputDirectory);
                    Assert.Equal("fixture", typed.Overrides.Profile.ProfilerProvider);
                    Assert.Equal("profile.json", typed.Overrides.Profile.ProfilePath);
                    Assert.Equal("profile-metadata.json", typed.Overrides.Profile.SqlMetadataOutputPath);

                    Assert.Equal(new[] { "ModuleA" }, typed.Overrides.Extract.Modules);
                    Assert.True(typed.Overrides.Extract.IncludeSystemModules);
                    Assert.True(typed.Overrides.Extract.OnlyActiveAttributes);
                    Assert.Equal("extracted.json", typed.Overrides.Extract.OutputPath);
                    Assert.Equal("manifest.json", typed.Overrides.Extract.MockAdvancedSqlManifest);
                    Assert.Equal("extract-metadata.json", typed.Overrides.Extract.SqlMetadataOutputPath);
                })
        };
    }

    [Theory]
    [MemberData(nameof(PipelineFactories))]
    public async Task Invoke_WhenPipelineFails_WritesErrorsAndExitCodeOne(PipelineCommandCase testCase)
    {
        var verb = new TestPipelineVerb(
            testCase.VerbName,
            testCase.OptionsType,
            _ => TestPipelineRun.Failure(testCase.VerbName));

        var services = new ServiceCollection();
        testCase.ConfigureServices(services, verb);

        await using var provider = services.BuildServiceProvider();
        var factory = (ICommandFactory)provider.GetRequiredService(testCase.FactoryType);
        var command = factory.Create();
        var parser = new CommandLineBuilder(new RootCommand { command }).UseDefaults().Build();
        var console = new TestConsole();
        var args = string.IsNullOrWhiteSpace(testCase.SharedArgs)
            ? testCase.CommandName
            : $"{testCase.CommandName} {testCase.SharedArgs}";

        var exitCode = await parser.InvokeAsync(args, console);

        Assert.Equal(1, exitCode);
        Assert.NotNull(verb.LastOptions);
        testCase.AssertOptions(verb.LastOptions);
        var errorOutput = console.Error.ToString() ?? string.Empty;
        Assert.Contains("cli.shared: run failed.", errorOutput);
    }

    [Theory]
    [MemberData(nameof(PipelineFactories))]
    public async Task Invoke_WhenPayloadIsUnexpected_WritesUniformError(PipelineCommandCase testCase)
    {
        var verb = new TestPipelineVerb(
            testCase.VerbName,
            testCase.OptionsType,
            _ => TestPipelineRun.Success(testCase.VerbName, new object()));

        var services = new ServiceCollection();
        testCase.ConfigureServices(services, verb);

        await using var provider = services.BuildServiceProvider();
        var factory = (ICommandFactory)provider.GetRequiredService(testCase.FactoryType);
        var command = factory.Create();
        var parser = new CommandLineBuilder(new RootCommand { command }).UseDefaults().Build();
        var console = new TestConsole();
        var args = string.IsNullOrWhiteSpace(testCase.SharedArgs)
            ? testCase.CommandName
            : $"{testCase.CommandName} {testCase.SharedArgs}";

        var exitCode = await parser.InvokeAsync(args, console);

        Assert.Equal(1, exitCode);
        var errorOutput = console.Error.ToString() ?? string.Empty;
        Assert.Contains($"[error] Unexpected result type for {testCase.VerbName} verb.", errorOutput);
    }

    public sealed record PipelineCommandCase(
        string CommandName,
        string SharedArgs,
        Type FactoryType,
        string VerbName,
        Type OptionsType,
        Action<ServiceCollection, IPipelineVerb> ConfigureServices,
        Action<object?> AssertOptions);

    private sealed class TestPipelineVerb : IPipelineVerb
    {
        private readonly Func<object, IPipelineRun> _run;

        public TestPipelineVerb(string name, Type optionsType, Func<object, IPipelineRun> run)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            OptionsType = optionsType ?? throw new ArgumentNullException(nameof(optionsType));
            _run = run ?? throw new ArgumentNullException(nameof(run));
        }

        public string Name { get; }

        public Type OptionsType { get; }

        public object? LastOptions { get; private set; }

        public Task<IPipelineRun> RunAsync(object options, CancellationToken cancellationToken = default)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            LastOptions = options;
            return Task.FromResult(_run(options));
        }
    }

    private sealed class SingleVerbRegistry : IVerbRegistry
    {
        private readonly IPipelineVerb _verb;

        public SingleVerbRegistry(IPipelineVerb verb)
        {
            _verb = verb ?? throw new ArgumentNullException(nameof(verb));
        }

        public IPipelineVerb Get(string verbName) => _verb;

        public bool TryGet(string verbName, out IPipelineVerb verb)
        {
            verb = _verb;
            return true;
        }
    }

    private sealed class TestPipelineRun : IPipelineRun
    {
        private TestPipelineRun(string verb, bool isSuccess, ImmutableArray<ValidationError> errors, object? payload)
        {
            Verb = verb;
            IsSuccess = isSuccess;
            Errors = errors;
            Payload = payload;
            StartedAt = DateTimeOffset.UtcNow;
            CompletedAt = StartedAt;
            Artifacts = Array.Empty<PipelineArtifact>();
            Metadata = new Dictionary<string, string?>();
        }

        public string Verb { get; }

        public DateTimeOffset StartedAt { get; }

        public DateTimeOffset CompletedAt { get; }

        public IReadOnlyList<PipelineArtifact> Artifacts { get; }

        public IReadOnlyDictionary<string, string?> Metadata { get; }

        public bool IsSuccess { get; }

        public ImmutableArray<ValidationError> Errors { get; }

        public object? Payload { get; }

        public static IPipelineRun Failure(string verb)
        {
            var errors = ImmutableArray.Create(ValidationError.Create("cli.shared", "run failed."));
            return new TestPipelineRun(verb, false, errors, null);
        }

        public static IPipelineRun Success(string verb, object? payload)
        {
            return new TestPipelineRun(verb, true, ImmutableArray<ValidationError>.Empty, payload);
        }
    }
}
