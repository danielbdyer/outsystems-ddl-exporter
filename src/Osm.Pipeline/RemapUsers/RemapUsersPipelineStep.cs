using System;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.RemapUsers;

internal abstract class RemapUsersPipelineStep : IPipelineStep<RemapUsersContext>
{
    protected RemapUsersPipelineStep(string name)
    {
        Name = !string.IsNullOrWhiteSpace(name)
            ? name
            : throw new ArgumentException("Step name must be provided.", nameof(name));
    }

    public string Name { get; }

    public async Task ExecuteAsync(RemapUsersContext context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.Telemetry.StepStarted(Name);
        try
        {
            await ExecuteCoreAsync(context, cancellationToken).ConfigureAwait(false);
            context.Telemetry.StepCompleted(Name);
        }
        catch (Exception ex)
        {
            context.Telemetry.Error(Name, $"Step '{Name}' failed.", ex, null);
            throw;
        }
    }

    protected abstract Task ExecuteCoreAsync(RemapUsersContext context, CancellationToken cancellationToken);
}
