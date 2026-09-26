using System;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.Runtime;

/// <summary>
/// Contract implemented by executable pipeline verbs.
/// </summary>
public interface IPipelineVerb
{
    /// <summary>
    /// Gets the canonical verb name (e.g. "build-ssdt").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the options record type the verb expects.
    /// </summary>
    Type OptionsType { get; }

    /// <summary>
    /// Executes the verb using the supplied options.
    /// </summary>
    /// <param name="options">Options object matching <see cref="OptionsType"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IPipelineRun> RunAsync(object options, CancellationToken cancellationToken = default);
}
