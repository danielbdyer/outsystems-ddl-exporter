using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Osm.Domain.Abstractions;
using Spectre.Console;

namespace Osm.Cli;

public interface IProgressRunner
{
    Task RunAsync(IServiceProvider services, Func<Task> action);
}

public sealed class SpectreProgressRunner : IProgressRunner
{
    public async Task RunAsync(IServiceProvider services, Func<Task> action)
    {
        await AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn()
            })
            .StartAsync(async ctx =>
            {
                var accessor = services.GetRequiredService<ITaskProgressAccessor>();
                accessor.Progress = new SpectreConsoleProgressService(ctx);
                await action();
            });
    }
}
