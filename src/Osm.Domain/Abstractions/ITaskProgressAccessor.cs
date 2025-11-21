namespace Osm.Domain.Abstractions;

public interface ITaskProgressAccessor
{
    ITaskProgress? Progress { get; set; }
}
