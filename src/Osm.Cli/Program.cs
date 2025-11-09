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
using Osm.LoadHarness;
using Osm.Pipeline.Runtime;

var hostBuilder = Host.CreateApplicationBuilder(args);
hostBuilder.Services.AddLogging(static builder => builder.AddSimpleConsole());
hostBuilder.Services.AddPipeline();
hostBuilder.Services.AddSingleton<CliGlobalOptions>();
hostBuilder.Services.AddSingleton<ModuleFilterOptionBinder>();
hostBuilder.Services.AddSingleton<CacheOptionBinder>();
hostBuilder.Services.AddSingleton<SqlOptionBinder>();
hostBuilder.Services.AddSingleton<TighteningOptionBinder>();
hostBuilder.Services.AddSingleton<SchemaApplyOptionBinder>();
hostBuilder.Services.AddSingleton<LoadHarnessRunner>();
hostBuilder.Services.AddSingleton<LoadHarnessReportWriter>();
hostBuilder.Services.AddSingleton<ICommandFactory, BuildSsdtCommandFactory>();
hostBuilder.Services.AddSingleton<ICommandFactory, FullExportCommandFactory>();
hostBuilder.Services.AddSingleton<ICommandFactory, ProfileCommandFactory>();
hostBuilder.Services.AddSingleton<ICommandFactory, ExtractModelCommandFactory>();
hostBuilder.Services.AddSingleton<ICommandFactory, DmmCompareCommandFactory>();
hostBuilder.Services.AddSingleton<ICommandFactory, InspectCommandFactory>();
hostBuilder.Services.AddSingleton<ICommandFactory, AnalyzeCommandFactory>();
hostBuilder.Services.AddSingleton<ICommandFactory, PolicyCommandFactory>();

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
