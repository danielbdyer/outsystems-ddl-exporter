using System.Threading;

namespace Osm.Domain.Abstractions;

public class TaskProgressAccessor : ITaskProgressAccessor
{
    private static readonly AsyncLocal<ITaskProgress?> _current = new();

    public ITaskProgress? Progress
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}
