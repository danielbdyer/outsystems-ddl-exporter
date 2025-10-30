using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Profiling;
using Osm.Pipeline.Profiling;

namespace Tests.Support;

public sealed class FakeProfiler : IDataProfiler
{
    private readonly string _fixtureName;

    public FakeProfiler(string fixtureName)
    {
        _fixtureName = fixtureName;
    }

    public Task<Result<ProfileSnapshot>> CaptureAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = ProfileFixtures.LoadSnapshot(_fixtureName);
        return Task.FromResult(Result<ProfileSnapshot>.Success(snapshot));
    }

    public ImmutableArray<TableProfilingTelemetry> GetTelemetrySnapshot()
    {
        return ImmutableArray<TableProfilingTelemetry>.Empty;
    }
}
