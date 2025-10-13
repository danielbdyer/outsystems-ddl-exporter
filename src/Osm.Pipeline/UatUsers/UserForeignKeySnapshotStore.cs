using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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

    public async Task<UserForeignKeySnapshot?> LoadAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Snapshot path must be provided.", nameof(path));
        }

        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        var snapshot = await JsonSerializer.DeserializeAsync<UserForeignKeySnapshot>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
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
    }
}
