using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Profiling;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Orchestration;
using Osm.Validation.Tightening;
using Tests.Support;
using Xunit;

namespace Osm.Pipeline.Tests;

public sealed class AnalyzeApplicationServiceTests
{
    [Fact]
    public async Task RunAsync_WhenModelPathMissing_ReturnsError()
    {
        var dispatcher = new CapturingDispatcher();
        var service = new AnalyzeApplicationService(dispatcher);
        var context = CreateContext(modelPath: null, profilePath: "profile.json", profilerProfilePath: null);
        var overrides = new AnalyzeOverrides(null, "profile.json", "out");

        var result = await service.RunAsync(new AnalyzeApplicationInput(context, overrides));

        Assert.True(result.IsFailure);
        Assert.Single(result.Errors);
        Assert.Equal("pipeline.analyze.model.missing", result.Errors[0].Code);
        Assert.Null(dispatcher.LastRequest);
    }

    [Fact]
    public async Task RunAsync_WhenProfilePathMissing_ReturnsError()
    {
        var dispatcher = new CapturingDispatcher();
        var service = new AnalyzeApplicationService(dispatcher);
        var context = CreateContext(modelPath: "model.json", profilePath: null, profilerProfilePath: null);
        var overrides = new AnalyzeOverrides("model.json", null, "out");

        var result = await service.RunAsync(new AnalyzeApplicationInput(context, overrides));

        Assert.True(result.IsFailure);
        Assert.Single(result.Errors);
        Assert.Equal("pipeline.analyze.profile.missing", result.Errors[0].Code);
        Assert.Null(dispatcher.LastRequest);
    }

    [Fact]
    public async Task RunAsync_DispatchesRequestWithResolvedPaths()
    {
        using var temp = new TempDirectory();
        var dispatcher = new CapturingDispatcher();
        var service = new AnalyzeApplicationService(dispatcher);
        var modelPath = Path.Combine(temp.Path, "model.json");
        var profilePath = Path.Combine(temp.Path, "profile.json");
        File.WriteAllText(modelPath, "{}");
        File.WriteAllText(profilePath, "{}");

        var context = CreateContext(modelPath: null, profilePath: profilePath, profilerProfilePath: null);
        var overrides = new AnalyzeOverrides(modelPath, null, temp.Path);

        var result = await service.RunAsync(new AnalyzeApplicationInput(context, overrides));

        Assert.True(result.IsSuccess);
        Assert.NotNull(dispatcher.LastRequest);
        var request = dispatcher.LastRequest!;
        Assert.Equal(modelPath, request.ModelPath);
        Assert.Equal(profilePath, request.ProfilePath);
        Assert.Equal(temp.Path, request.OutputDirectory);
        Assert.Equal(temp.Path, result.Value.OutputDirectory);
        Assert.Equal(modelPath, result.Value.ModelPath);
        Assert.Equal(profilePath, result.Value.ProfilePath);
    }

    private static CliConfigurationContext CreateContext(string? modelPath, string? profilePath, string? profilerProfilePath)
    {
        var configuration = new CliConfiguration(
            TighteningOptions.Default,
            ModelPath: modelPath,
            ProfilePath: profilePath,
            DmmPath: null,
            CacheConfiguration.Empty,
            new ProfilerConfiguration(null, profilerProfilePath, null),
            SqlConfiguration.Empty,
            ModuleFilterConfiguration.Empty,
            TypeMappingConfiguration.Empty,
            SupplementalModelConfiguration.Empty);

        return new CliConfigurationContext(configuration, null);
    }

    private sealed class CapturingDispatcher : ICommandDispatcher
    {
        public TighteningAnalysisPipelineRequest? LastRequest { get; private set; }

        public Task<Result<TResult>> DispatchAsync<TRequest, TResult>(TRequest command, CancellationToken cancellationToken = default)
            where TRequest : ICommand<TResult>
        {
            if (command is TighteningAnalysisPipelineRequest request && typeof(TResult) == typeof(TighteningAnalysisPipelineResult))
            {
                LastRequest = request;
                var report = PolicyDecisionReporter.Create(PolicyDecisionSet.Create(
                    ImmutableDictionary<ColumnCoordinate, NullabilityDecision>.Empty,
                    ImmutableDictionary<ColumnCoordinate, ForeignKeyDecision>.Empty,
                    ImmutableDictionary<IndexCoordinate, UniqueIndexDecision>.Empty,
                    ImmutableArray<TighteningDiagnostic>.Empty,
                    ImmutableDictionary<ColumnCoordinate, string>.Empty,
                    ImmutableDictionary<IndexCoordinate, string>.Empty,
                    TighteningOptions.Default));
                var profile = ProfileFixtures.LoadSnapshot("profiling/profile.edge-case.json");
                var pipelineResult = new TighteningAnalysisPipelineResult(
                    report,
                    profile,
                    ImmutableArray.Create("summary"),
                    Path.Combine(request.OutputDirectory, "summary.txt"),
                    Path.Combine(request.OutputDirectory, "policy-decisions.json"),
                    PipelineExecutionLog.Empty,
                    ImmutableArray<string>.Empty);

                return Task.FromResult(Result<TResult>.Success((TResult)(object)pipelineResult));
            }

            throw new InvalidOperationException("Unexpected command type.");
        }
    }
}
