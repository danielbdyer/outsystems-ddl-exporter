using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Osm.Domain.Abstractions;
using Osm.Pipeline.SqlExtraction;

namespace Osm.Pipeline.Sql;

public sealed class SqlMetadataLog
{
    private readonly ConcurrentQueue<SqlRequestLogEntry> _requests = new();
    private readonly object _statusLock = new();

    private OutsystemsMetadataSnapshot? _snapshot;
    private DateTimeOffset? _exportedAtUtc;
    private string? _databaseName;
    private IReadOnlyList<ValidationError> _errors = Array.Empty<ValidationError>();
    private MetadataRowSnapshot? _failureRowSnapshot;

    internal void RecordSnapshot(OutsystemsMetadataSnapshot snapshot, DateTimeOffset exportedAtUtc)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        lock (_statusLock)
        {
            _snapshot = snapshot;
            _exportedAtUtc = exportedAtUtc;
            _databaseName = snapshot.DatabaseName;
            _errors = Array.Empty<ValidationError>();
            _failureRowSnapshot = null;
        }
    }

    internal void RecordFailure(IReadOnlyList<ValidationError> errors, MetadataRowSnapshot? rowSnapshot)
    {
        lock (_statusLock)
        {
            _errors = errors ?? Array.Empty<ValidationError>();
            _failureRowSnapshot = rowSnapshot;
        }
    }

    internal void RecordRequest(string name, object? payload)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Request name must be provided.", nameof(name));
        }

        _requests.Enqueue(new SqlRequestLogEntry(name, payload));
    }

    internal SqlMetadataLogState BuildState()
    {
        lock (_statusLock)
        {
            return new SqlMetadataLogState(
                _snapshot,
                _exportedAtUtc,
                _databaseName,
                _errors,
                _failureRowSnapshot,
                _requests.ToArray());
        }
    }
}

internal sealed record SqlRequestLogEntry(string Name, object? Payload);

internal sealed record SqlMetadataLogState(
    OutsystemsMetadataSnapshot? Snapshot,
    DateTimeOffset? ExportedAtUtc,
    string? DatabaseName,
    IReadOnlyList<ValidationError> Errors,
    MetadataRowSnapshot? FailureRowSnapshot,
    IReadOnlyList<SqlRequestLogEntry> Requests)
{
    public bool HasSnapshot => Snapshot is not null;

    public bool HasErrors => Errors.Count > 0;

    public bool HasRequests => Requests.Count > 0;
}
