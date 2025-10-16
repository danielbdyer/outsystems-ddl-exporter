using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Osm.Cli;
using Osm.Cli.Commands;
using Osm.Cli.Commands.Binders;
using Osm.Domain.Abstractions;
using Osm.Json;
using Osm.Dmm;
using Osm.Pipeline.Application;
using Osm.Pipeline.Configuration;
using Osm.Pipeline.ModelIngestion;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;
using Osm.Pipeline.Mediation;

var hostBuilder = Host.CreateApplicationBuilder(args);
hostBuilder.Services.AddLogging(static builder => builder.AddSimpleConsole());
hostBuilder.Services.AddSingleton<ICliConfigurationService, CliConfigurationService>();
hostBuilder.Services.AddSingleton<IModelJsonDeserializer, ModelJsonDeserializer>();
hostBuilder.Services.AddSingleton<IProfileSnapshotDeserializer, ProfileSnapshotDeserializer>();
hostBuilder.Services.AddSingleton<Func<string, SqlConnectionOptions, IDbConnectionFactory>>(
    _ => (connectionString, options) => new SqlConnectionFactory(connectionString, options));
hostBuilder.Services.AddSingleton<IDataProfilerFactory, DataProfilerFactory>();
hostBuilder.Services.AddSingleton<IModelIngestionService, ModelIngestionService>();
hostBuilder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
hostBuilder.Services.AddSingleton<ICommandDispatcher>(
    sp => new CommandDispatcher(sp.GetRequiredService<IServiceScopeFactory>()));
hostBuilder.Services.AddSingleton<ICommandHandler<BuildSsdtPipelineRequest, BuildSsdtPipelineResult>, BuildSsdtPipeline>();
hostBuilder.Services.AddSingleton<ICommandHandler<DmmComparePipelineRequest, DmmComparePipelineResult>, DmmComparePipeline>();
hostBuilder.Services.AddSingleton<ICommandHandler<ExtractModelPipelineRequest, ModelExtractionResult>, ExtractModelPipeline>();
hostBuilder.Services.AddSingleton<BuildSsdtRequestAssembler>();
hostBuilder.Services.AddSingleton<IModelResolutionService, ModelResolutionService>();
hostBuilder.Services.AddSingleton<IOutputDirectoryResolver, OutputDirectoryResolver>();
hostBuilder.Services.AddSingleton<INamingOverridesBinder, NamingOverridesBinder>();
hostBuilder.Services.AddSingleton<IStaticDataProviderFactory, StaticDataProviderFactory>();
hostBuilder.Services.AddSingleton<IApplicationService<BuildSsdtApplicationInput, BuildSsdtApplicationResult>, BuildSsdtApplicationService>();
hostBuilder.Services.AddSingleton<IApplicationService<CompareWithDmmApplicationInput, CompareWithDmmApplicationResult>, CompareWithDmmApplicationService>();
hostBuilder.Services.AddSingleton<IApplicationService<ExtractModelApplicationInput, ExtractModelApplicationResult>, ExtractModelApplicationService>();
hostBuilder.Services.AddSingleton<CliGlobalOptions>();
hostBuilder.Services.AddSingleton<ModuleFilterOptionBinder>();
hostBuilder.Services.AddSingleton<CacheOptionBinder>();
hostBuilder.Services.AddSingleton<SqlOptionBinder>();
hostBuilder.Services.AddSingleton<ICommandFactory, BuildSsdtCommandFactory>();
hostBuilder.Services.AddSingleton<ICommandFactory, ExtractModelCommandFactory>();
hostBuilder.Services.AddSingleton<ICommandFactory, DmmCompareCommandFactory>();
hostBuilder.Services.AddSingleton<ICommandFactory, InspectCommandFactory>();

var remapUsersToggle = Environment.GetEnvironmentVariable("OSM_ENABLE_REMAP_USERS");
var enableUatUsers = remapUsersToggle is null || string.Equals(remapUsersToggle, "true", StringComparison.OrdinalIgnoreCase);

if (enableUatUsers)
{
    hostBuilder.Services.AddSingleton<IUatUsersCommand, UatUsersCommand>();
    hostBuilder.Services.AddSingleton<ICommandFactory, UatUsersCommandFactory>();
}

using var host = hostBuilder.Build();

var rootCommand = new RootCommand("OutSystems DDL Exporter CLI");
foreach (var factory in host.Services.GetRequiredService<IEnumerable<ICommandFactory>>())
{
    rootCommand.AddCommand(factory.Create());
}

var parser = new CommandLineBuilder(rootCommand)
    .UseDefaults()
    .AddMiddleware((context, next) =>
    {
        context.BindingContext.AddService(_ => host.Services);
        return next(context);
    })
    .Build();

return await parser.InvokeAsync(args);
