using System.Collections.Immutable;
using System.IO;
using System.IO.Abstractions;
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

    public async Task<Result<ProfilingCaptureResult>> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_fileSystem.File.Exists(_fixturePath))
        {
            return Result<ProfilingCaptureResult>.Failure(ValidationError.Create(
                "profiler.fixture.missing",
                $"Profiling fixture '{_fixturePath}' was not found."));
        }

        await using var stream = _fileSystem.File.Open(_fixturePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var snapshotResult = _deserializer.Deserialize(stream);
        if (snapshotResult.IsFailure)
        {
            return Result<ProfilingCaptureResult>.Failure(snapshotResult.Errors);
        }

        var capture = new ProfilingCaptureResult(snapshotResult.Value, ImmutableArray<string>.Empty);
        return Result<ProfilingCaptureResult>.Success(capture);
    }
}
