using System;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Pipeline.SqlExtraction;
using Osm.Pipeline.UatUsers;

namespace Osm.Pipeline.Orchestration;

public sealed record FullExportCoordinatorRequest<TExtraction, TProfile, TBuild>(
    Func<CancellationToken, Task<Result<TExtraction>>> ExtractAsync,
    Func<TExtraction, CancellationToken, Task<Result<TProfile>>> ProfileAsync,
    Func<TExtraction, TProfile, CancellationToken, Task<Result<TBuild>>> BuildAsync,
    Func<TBuild, CancellationToken, Task<Result<SchemaApplyResult>>> ApplyAsync,
    SchemaApplyOptions ApplyOptions,
    Func<TExtraction, ModelExtractionResult> ExtractionResultSelector,
    Func<TExtraction, TBuild, ModelUserSchemaGraph, CancellationToken, Task<Result<UatUsersApplicationResult>>>? RunUatUsersAsync);

public sealed record FullExportCoordinatorResult<TExtraction, TProfile, TBuild>(
    TExtraction Extraction,
    TProfile Profile,
    TBuild Build,
    SchemaApplyResult Apply,
    SchemaApplyOptions ApplyOptions,
    UatUsersApplicationResult UatUsers,
    ModelUserSchemaGraph? SchemaGraph);

public sealed class FullExportCoordinator
{
    private readonly IModelUserSchemaGraphFactory _schemaGraphFactory;
    private readonly ITaskProgressAccessor _progressAccessor;

    public FullExportCoordinator(
        IModelUserSchemaGraphFactory schemaGraphFactory,
        ITaskProgressAccessor? progressAccessor = null)
    {
        _schemaGraphFactory = schemaGraphFactory ?? throw new ArgumentNullException(nameof(schemaGraphFactory));
        _progressAccessor = progressAccessor ?? new TaskProgressAccessor();
    }

    public async Task<Result<FullExportCoordinatorResult<TExtraction, TProfile, TBuild>>> ExecuteAsync<TExtraction, TProfile, TBuild>(
        FullExportCoordinatorRequest<TExtraction, TProfile, TBuild> request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.ExtractAsync is null)
        {
            throw new ArgumentException("Extract delegate must be provided.", nameof(request));
        }

        if (request.ProfileAsync is null)
        {
            throw new ArgumentException("Profile delegate must be provided.", nameof(request));
        }

        if (request.BuildAsync is null)
        {
            throw new ArgumentException("Build delegate must be provided.", nameof(request));
        }

        if (request.ApplyAsync is null)
        {
            throw new ArgumentException("Apply delegate must be provided.", nameof(request));
        }

        if (request.ExtractionResultSelector is null)
        {
            throw new ArgumentException("Extraction result selector must be provided.", nameof(request));
        }

        var progress = _progressAccessor.Progress;
        using var task = progress?.Start("Full Export: Initializing", 5);

        task?.Description("Full Export: Extracting Model");
        var extractionResult = await request.ExtractAsync(cancellationToken).ConfigureAwait(false);
        if (extractionResult.IsFailure)
        {
            return Result<FullExportCoordinatorResult<TExtraction, TProfile, TBuild>>.Failure(extractionResult.Errors);
        }
        task?.Increment(1);

        var extraction = extractionResult.Value;

        task?.Description("Full Export: Profiling Data");
        var profileResult = await request.ProfileAsync(extraction, cancellationToken).ConfigureAwait(false);
        if (profileResult.IsFailure)
        {
            return Result<FullExportCoordinatorResult<TExtraction, TProfile, TBuild>>.Failure(profileResult.Errors);
        }
        task?.Increment(1);

        var profile = profileResult.Value;

        task?.Description("Full Export: Building SSDT Project");
        var buildResult = await request.BuildAsync(extraction, profile, cancellationToken).ConfigureAwait(false);
        if (buildResult.IsFailure)
        {
            return Result<FullExportCoordinatorResult<TExtraction, TProfile, TBuild>>.Failure(buildResult.Errors);
        }
        task?.Increment(1);

        var build = buildResult.Value;

        task?.Description("Full Export: Applying Schema");
        var applyResult = await request.ApplyAsync(build, cancellationToken).ConfigureAwait(false);
        if (applyResult.IsFailure)
        {
            return Result<FullExportCoordinatorResult<TExtraction, TProfile, TBuild>>.Failure(applyResult.Errors);
        }
        task?.Increment(1);

        var apply = applyResult.Value;
        var uatUsersOutcome = UatUsersApplicationResult.Disabled;
        ModelUserSchemaGraph? schemaGraph = null;

        if (request.RunUatUsersAsync is { } uatUsersAsync)
        {
            task?.Description("Full Export: Processing UAT Users");
            var extractionModel = request.ExtractionResultSelector(extraction)
                ?? throw new InvalidOperationException("Extraction result selector returned null.");

            var schemaGraphResult = _schemaGraphFactory.Create(extractionModel);
            if (schemaGraphResult.IsFailure)
            {
                return Result<FullExportCoordinatorResult<TExtraction, TProfile, TBuild>>.Failure(schemaGraphResult.Errors);
            }

            schemaGraph = schemaGraphResult.Value;

            var uatUsersResult = await uatUsersAsync(extraction, build, schemaGraph, cancellationToken)
                .ConfigureAwait(false);
            if (uatUsersResult.IsFailure)
            {
                return Result<FullExportCoordinatorResult<TExtraction, TProfile, TBuild>>.Failure(uatUsersResult.Errors);
            }

            uatUsersOutcome = uatUsersResult.Value;
        }
        task?.Increment(1);

        return new FullExportCoordinatorResult<TExtraction, TProfile, TBuild>(
            extraction,
            profile,
            build,
            apply,
            request.ApplyOptions,
            uatUsersOutcome,
            schemaGraph);
    }
}
