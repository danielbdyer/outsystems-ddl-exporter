using System.IO;
using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Profiling;
using Osm.Json;

namespace Osm.Pipeline.Profiling;

public sealed class FixtureDataProfiler : IDataProfiler
{
    private readonly string _fixturePath;
    private readonly IProfileSnapshotDeserializer _deserializer;
    private readonly IFileSystem _fileSystem;

    public FixtureDataProfiler(
        string fixturePath,
        IProfileSnapshotDeserializer deserializer,
        IFileSystem? fileSystem = null)
    {
        if (string.IsNullOrWhiteSpace(fixturePath))
        {
            throw new ArgumentException("Fixture path must be provided.", nameof(fixturePath));
        }

        _fixturePath = fixturePath.Trim();
        _deserializer = deserializer ?? throw new ArgumentNullException(nameof(deserializer));
        _fileSystem = fileSystem ?? new FileSystem();
    }

    public async Task<Result<ProfileSnapshot>> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_fileSystem.File.Exists(_fixturePath))
        {
            return Result<ProfileSnapshot>.Failure(ValidationError.Create(
                "profiler.fixture.missing",
                $"Profiling fixture '{_fixturePath}' was not found."));
        }

        await using var stream = _fileSystem.File.Open(_fixturePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return _deserializer.Deserialize(stream);
    }

    public async IAsyncEnumerable<Result<ProfileObservation>> StreamAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_fileSystem.File.Exists(_fixturePath))
        {
            yield return Result<ProfileObservation>.Failure(ValidationError.Create(
                "profiler.fixture.missing",
                $"Profiling fixture '{_fixturePath}' was not found."));
            yield break;
        }

        await using var stream = _fileSystem.File.Open(_fixturePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var snapshotResult = _deserializer.Deserialize(stream);
        if (snapshotResult.IsFailure)
        {
            yield return Result<ProfileObservation>.Failure(snapshotResult.Errors);
            yield break;
        }

        var snapshot = snapshotResult.Value;
        foreach (var column in snapshot.Columns)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return Result<ProfileObservation>.Success(ProfileObservation.ForColumn(column));
        }

        foreach (var candidate in snapshot.UniqueCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return Result<ProfileObservation>.Success(ProfileObservation.ForUniqueCandidate(candidate));
        }

        foreach (var composite in snapshot.CompositeUniqueCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return Result<ProfileObservation>.Success(ProfileObservation.ForCompositeUniqueCandidate(composite));
        }

        foreach (var foreignKey in snapshot.ForeignKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return Result<ProfileObservation>.Success(ProfileObservation.ForForeignKey(foreignKey));
        }
    }
}
