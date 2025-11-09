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
using Osm.Pipeline.Runtime;
using Osm.Pipeline.Runtime.Verbs;
using Xunit;

namespace Osm.Cli.Tests.Commands;

public class FullExportCommandFactoryTests
{
    [Fact]
    public async Task Invoke_WithOutRootAndRelativeStagePaths_ResolvesPathsUnderRoot()
    {
        var verb = new TestPipelineVerb(
            FullExportVerb.VerbName,
            typeof(FullExportVerbOptions),
            _ => TestPipelineRun.Failure(FullExportVerb.VerbName));

        await using var provider = BuildServiceProvider(verb);
        var factory = provider.GetRequiredService<FullExportCommandFactory>();
        var command = factory.Create();
        var parser = new CommandLineBuilder(new RootCommand { command }).UseDefaults().Build();
        var console = new TestConsole();

        var args = new[]
        {
            "full-export",
            "--config",
            "config.json",
            "--mock-advanced-sql",
            "manifest.json",
            "--out-root",
            "./artifacts"
        };

        await parser.InvokeAsync(BuildCommandLine(args), console);

        var options = Assert.IsType<FullExportVerbOptions>(verb.LastOptions);
        Assert.Equal(Path.Combine("./artifacts", "out"), options.Overrides.Build.OutputDirectory);
        Assert.Equal(Path.Combine("./artifacts", "profiles"), options.Overrides.Profile.OutputDirectory);
        Assert.Equal(Path.Combine("./artifacts", "model.extracted.json"), options.Overrides.Extract.OutputPath);
    }

    [Fact]
    public async Task Invoke_WithOutRootAndAbsoluteStagePaths_PreservesAbsoluteOverrides()
    {
        var verb = new TestPipelineVerb(
            FullExportVerb.VerbName,
            typeof(FullExportVerbOptions),
            _ => TestPipelineRun.Failure(FullExportVerb.VerbName));

        await using var provider = BuildServiceProvider(verb);
        var factory = provider.GetRequiredService<FullExportCommandFactory>();
        var command = factory.Create();
        var parser = new CommandLineBuilder(new RootCommand { command }).UseDefaults().Build();
        var console = new TestConsole();

        var extractPath = Path.Combine(Path.GetTempPath(), "extract-model.json");
        var profilePath = Path.Combine(Path.GetTempPath(), "profiles-root");
        var buildPath = Path.Combine(Path.GetTempPath(), "ssdt-root");

        var args = new[]
        {
            "full-export",
            "--config",
            "config.json",
            "--mock-advanced-sql",
            "manifest.json",
            "--out-root",
            "./artifacts",
            "--extract-out",
            extractPath,
            "--profile-out",
            profilePath,
            "--build-out",
            buildPath
        };

        await parser.InvokeAsync(BuildCommandLine(args), console);

        var options = Assert.IsType<FullExportVerbOptions>(verb.LastOptions);
        Assert.Equal(buildPath, options.Overrides.Build.OutputDirectory);
        Assert.Equal(profilePath, options.Overrides.Profile.OutputDirectory);
        Assert.Equal(extractPath, options.Overrides.Extract.OutputPath);
    }

    private static ServiceProvider BuildServiceProvider(IPipelineVerb verb)
    {
        var services = new ServiceCollection();
        services.AddSingleton<CliGlobalOptions>();
        services.AddSingleton<ModuleFilterOptionBinder>();
        services.AddSingleton<CacheOptionBinder>();
        services.AddSingleton<SqlOptionBinder>();
        services.AddSingleton<TighteningOptionBinder>();
        services.AddSingleton<IVerbRegistry>(_ => new SingleVerbRegistry(verb));
        services.AddSingleton<FullExportCommandFactory>();
        return services.BuildServiceProvider();
    }

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

    private static string BuildCommandLine(IEnumerable<string> args)
        => string.Join(' ', args.Select(QuoteIfNeeded));

    private static string QuoteIfNeeded(string value)
        => value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
}
