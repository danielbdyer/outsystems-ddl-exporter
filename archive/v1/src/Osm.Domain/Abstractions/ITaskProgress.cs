using System;

namespace Osm.Domain.Abstractions;

public interface ITaskProgress
{
    /// <summary>
    /// Starts a new progress task.
    /// </summary>
    /// <param name="description">The description of the task.</param>
    /// <param name="total">The total value of the task (defaults to 100).</param>
    /// <returns>An <see cref="IProgressTask"/> representing the started task.</returns>
    IProgressTask Start(string description, double total = 100);
}
