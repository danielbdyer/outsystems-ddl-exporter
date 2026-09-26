using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Cli;
using Osm.Cli.Commands.Options;
using Osm.Domain.Abstractions;
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
        services.AddSingleton<ITaskProgressAccessor, TaskProgressAccessor>();
        services.AddSingleton<IProgressRunner>(new TestProgressRunner());
        return services;
    }

    private sealed class TestProgressRunner : IProgressRunner
    {
        public Task RunAsync(IServiceProvider services, Func<Task> action) => action();
    }
}
