using System;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli.Commands.Options;
using Osm.Pipeline.Runtime.Verbs;

namespace Osm.Cli.Tests.Commands;

internal static class TestServiceCollectionExtensions
{
    public static IServiceCollection AddVerbOptionRegistryForTesting(this IServiceCollection services)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.AddSingleton<IVerbOptionExtension>(new OpenReportVerbExtension(BuildSsdtVerb.VerbName));
        services.AddSingleton<IVerbOptionExtension>(new OpenReportVerbExtension(FullExportVerb.VerbName));
        services.AddSingleton<IVerbOptionExtension, FullExportLoadHarnessExtension>();
        services.AddSingleton<VerbOptionRegistry>();
        return services;
    }
}
