using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Osm.Pipeline.UatUsers;

public interface IUserForeignKeySnapshotStore
{
    Task<UserForeignKeySnapshot?> LoadAsync(string path, CancellationToken cancellationToken);

    Task SaveAsync(string path, UserForeignKeySnapshot snapshot, CancellationToken cancellationToken);
}

internal sealed class FileUserForeignKeySnapshotStore : IUserForeignKeySnapshotStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger<FileUserForeignKeySnapshotStore> _logger;

    public FileUserForeignKeySnapshotStore(ILogger<FileUserForeignKeySnapshotStore>? logger = null)
    {
        _logger = logger ?? NullLogger<FileUserForeignKeySnapshotStore>.Instance;
    }

    public async Task<UserForeignKeySnapshot?> LoadAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Snapshot path must be provided.", nameof(path));
        }

        if (!File.Exists(path))
        {
            _logger.LogInformation("Snapshot not found at {Path}; live analysis will be performed.", path);
            return null;
        }

        await using var stream = File.OpenRead(path);
        var snapshot = await JsonSerializer.DeserializeAsync<UserForeignKeySnapshot>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            _logger.LogWarning("Snapshot file {Path} could not be deserialized.", path);
        }
        else
        {
            _logger.LogInformation(
                "Loaded snapshot from {Path} captured at {CapturedAt:u} with {ColumnCount} columns.",
                path,
                snapshot.CapturedAt,
                snapshot.Columns?.Count ?? 0);
        }

        return snapshot;
    }

    public async Task SaveAsync(string path, UserForeignKeySnapshot snapshot, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Snapshot path must be provided.", nameof(path));
        }

        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, snapshot, SerializerOptions, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Snapshot persisted to {Path} (AllowedUserCount={AllowedCount}, OrphanCount={OrphanCount}).",
            path,
            snapshot.AllowedUserIds?.Count ?? 0,
            snapshot.OrphanUserIds?.Count ?? 0);
    }
}
