using System.Collections.Generic;
using System.Runtime.CompilerServices;
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

    public async IAsyncEnumerable<Result<ProfileObservation>> StreamAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var snapshot = ProfileFixtures.LoadSnapshot(_fixtureName);

        foreach (var column in snapshot.Columns)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return Result<ProfileObservation>.Success(ProfileObservation.ForColumn(column));
            await Task.Yield();
        }

        foreach (var candidate in snapshot.UniqueCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return Result<ProfileObservation>.Success(ProfileObservation.ForUniqueCandidate(candidate));
            await Task.Yield();
        }

        foreach (var composite in snapshot.CompositeUniqueCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return Result<ProfileObservation>.Success(ProfileObservation.ForCompositeUniqueCandidate(composite));
            await Task.Yield();
        }

        foreach (var foreignKey in snapshot.ForeignKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return Result<ProfileObservation>.Success(ProfileObservation.ForForeignKey(foreignKey));
            await Task.Yield();
        }
    }
}
