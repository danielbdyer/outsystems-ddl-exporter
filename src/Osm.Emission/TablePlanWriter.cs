using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Emission;

public sealed class TablePlanWriter : ITablePlanWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly IFileSystem _fileSystem;

    public TablePlanWriter(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public async Task WriteAsync(
        IReadOnlyList<TableEmissionPlan> plans,
        int moduleParallelism,
        CancellationToken cancellationToken)
    {
        if (plans is null)
        {
            throw new ArgumentNullException(nameof(plans));
        }

        if (plans.Count == 0)
        {
            return;
        }

        if (moduleParallelism <= 1)
        {
            for (var i = 0; i < plans.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WriteSingleAsync(plans[i], cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        var boundedConcurrency = Math.Max(1, moduleParallelism);
        using var semaphore = new SemaphoreSlim(boundedConcurrency, boundedConcurrency);
        var writeTasks = new Task[plans.Count];

        for (var i = 0; i < plans.Count; i++)
        {
            var plan = plans[i];
            writeTasks[i] = WritePlanAsync(plan, semaphore, cancellationToken);
        }

        await Task.WhenAll(writeTasks).ConfigureAwait(false);
    }

    private async Task WritePlanAsync(
        TableEmissionPlan plan,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WriteSingleAsync(plan, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task WriteSingleAsync(TableEmissionPlan plan, CancellationToken cancellationToken)
    {
        var directory = _fileSystem.Path.GetDirectoryName(plan.Path);
        if (!string.IsNullOrEmpty(directory))
        {
            _fileSystem.Directory.CreateDirectory(directory);
        }

        var content = plan.Script.EndsWith(Environment.NewLine, StringComparison.Ordinal)
            ? plan.Script
            : plan.Script + Environment.NewLine;

        if (_fileSystem.File.Exists(plan.Path))
        {
            var existing = await _fileSystem.File.ReadAllTextAsync(plan.Path, cancellationToken).ConfigureAwait(false);
            if (string.Equals(existing, content, StringComparison.Ordinal))
            {
                return;
            }
        }

        await _fileSystem.File.WriteAllTextAsync(plan.Path, content, Utf8NoBom, cancellationToken).ConfigureAwait(false);
    }
}
