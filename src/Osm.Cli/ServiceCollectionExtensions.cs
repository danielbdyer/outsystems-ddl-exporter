using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Osm.Domain.Abstractions;
using Osm.Json;
using Osm.Dmm;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Mediation;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.SqlExtraction;
using Osm.Validation.Tightening;

namespace Osm.Cli;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOsmCli(this IServiceCollection services)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.AddLogging(static builder => builder.AddSimpleConsole());
        services.AddSingleton<ICliConfigurationService, CliConfigurationService>();
        services.AddSingleton<IModelJsonDeserializer, ModelJsonDeserializer>();
        services.AddSingleton<IModelIngestionService, ModelIngestionService>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
        services.AddSingleton<ICommandHandler<BuildSsdtPipelineRequest, BuildSsdtPipelineResult>, BuildSsdtPipeline>();
        services.AddSingleton<ICommandHandler<DmmComparePipelineRequest, DmmComparePipelineResult>, DmmComparePipeline>();
        services.AddSingleton<ICommandHandler<ExtractModelPipelineRequest, ModelExtractionResult>, ExtractModelPipeline>();
        services.AddSingleton<IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>, BuildSsdtApplicationService>();
        services.AddSingleton<IApplicationService<CompareWithDmmApplicationInput, CompareWithDmmApplicationResult>, CompareWithDmmApplicationService>();
        services.AddSingleton<IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult>, ExtractModelApplicationService>();

        var remapUsersToggle = Environment.GetEnvironmentVariable("OSM_ENABLE_REMAP_USERS");
        var enableUatUsers = remapUsersToggle is null || string.Equals(
            remapUsersToggle,
            "true",
            StringComparison.OrdinalIgnoreCase);

        if (enableUatUsers)
        {
            services.AddSingleton<UatUsersCommand>();
        }

        return services;
    }
}
