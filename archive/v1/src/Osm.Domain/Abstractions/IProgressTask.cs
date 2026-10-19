using System;

namespace Osm.Domain.Abstractions;

public interface IProgressTask : IDisposable
{
    /// <summary>
    /// Updates the task description.
    /// </summary>
    void Description(string description);

    /// <summary>
    /// Increments the task progress by the specified amount.
    /// </summary>
    void Increment(double amount);

    /// <summary>
    /// Sets the task progress to the specified value.
    /// </summary>
    void Value(double value);

    /// <summary>
    /// Sets the maximum value of the task progress.
    /// </summary>
    void MaxValue(double max);
}
