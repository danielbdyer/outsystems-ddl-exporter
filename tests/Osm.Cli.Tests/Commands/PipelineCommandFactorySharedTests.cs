using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.CommandLine.IO;
using System.Threading;
using System.Threading.Tasks;
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
