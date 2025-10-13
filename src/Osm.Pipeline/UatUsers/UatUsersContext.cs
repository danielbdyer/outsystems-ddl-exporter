using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.UatUsers;

public sealed class UatUsersContext
{
    private IReadOnlyList<UserFkColumn> _userFkCatalog = Array.Empty<UserFkColumn>();
    private IReadOnlyList<UserMappingEntry> _userMappings = Array.Empty<UserMappingEntry>();
    private readonly HashSet<long> _allowedUserIdSet = new();
    private IReadOnlyList<long> _allowedUserIds = Array.Empty<long>();
    private readonly HashSet<long> _orphanUserIdSet = new();
    private IReadOnlyList<long> _orphanUserIds = Array.Empty<long>();
    private IReadOnlyDictionary<UserFkColumn, IReadOnlyDictionary<long, long>> _foreignKeyValueCounts
        = ImmutableDictionary<UserFkColumn, IReadOnlyDictionary<long, long>>.Empty;

    public UatUsersContext(
        IUserSchemaGraph schemaGraph,
        UatUsersArtifacts artifacts,
        IDbConnectionFactory connectionFactory,
        string userSchema,
        string userTable,
        string userIdColumn,
        IReadOnlyCollection<string>? includeColumns,
        string userMapPath,
        string? allowedUsersSqlPath,
        string? allowedUserIdsPath,
        string? snapshotPath,
        string? userEntityIdentifier,
        bool fromLiveMetadata,
        string sourceFingerprint)
    {
        SchemaGraph = schemaGraph ?? throw new ArgumentNullException(nameof(schemaGraph));
        Artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        ConnectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

        if (string.IsNullOrWhiteSpace(userSchema))
        {
            throw new ArgumentException("User schema must be provided.", nameof(userSchema));
        }

        if (string.IsNullOrWhiteSpace(userTable))
        {
            throw new ArgumentException("User table must be provided.", nameof(userTable));
        }

        if (string.IsNullOrWhiteSpace(userIdColumn))
        {
            throw new ArgumentException("User identifier column must be provided.", nameof(userIdColumn));
        }

        if (string.IsNullOrWhiteSpace(userMapPath))
        {
            throw new ArgumentException("User map path must be provided.", nameof(userMapPath));
        }

        if (string.IsNullOrWhiteSpace(sourceFingerprint))
        {
            throw new ArgumentException("Source fingerprint must be provided.", nameof(sourceFingerprint));
        }

        UserSchema = userSchema.Trim();
        UserTable = userTable.Trim();
        UserIdColumn = userIdColumn.Trim();
        IncludeColumns = includeColumns is null || includeColumns.Count == 0
            ? null
            : includeColumns.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        UserMapPath = Path.GetFullPath(userMapPath);
        if (string.IsNullOrWhiteSpace(allowedUsersSqlPath) && string.IsNullOrWhiteSpace(allowedUserIdsPath))
        {
            throw new ArgumentException("At least one allowed user source must be provided.");
        }

        AllowedUsersSqlPath = string.IsNullOrWhiteSpace(allowedUsersSqlPath)
            ? null
            : Path.GetFullPath(allowedUsersSqlPath);
        AllowedUserIdsPath = string.IsNullOrWhiteSpace(allowedUserIdsPath)
            ? null
            : Path.GetFullPath(allowedUserIdsPath);
        SnapshotPath = string.IsNullOrWhiteSpace(snapshotPath) ? null : Path.GetFullPath(snapshotPath);
        UserEntityIdentifier = string.IsNullOrWhiteSpace(userEntityIdentifier) ? null : userEntityIdentifier.Trim();
        FromLiveMetadata = fromLiveMetadata;
        SourceFingerprint = sourceFingerprint.Trim();
    }

    public IUserSchemaGraph SchemaGraph { get; }

    public UatUsersArtifacts Artifacts { get; }

    public IDbConnectionFactory ConnectionFactory { get; }

    public string UserSchema { get; }

    public string UserTable { get; }

    public string UserIdColumn { get; }

    public ISet<string>? IncludeColumns { get; }

    public string UserMapPath { get; }

    public string? AllowedUsersSqlPath { get; }

    public string? AllowedUserIdsPath { get; }

    public string? SnapshotPath { get; }

    public string? UserEntityIdentifier { get; }

    public bool FromLiveMetadata { get; }

    public string SourceFingerprint { get; }

    public IReadOnlyList<UserFkColumn> UserFkCatalog => _userFkCatalog;

    public IReadOnlyList<UserMappingEntry> UserMap => _userMappings;

    public IReadOnlyCollection<long> AllowedUserIds => _allowedUserIds;

    public IReadOnlyCollection<long> OrphanUserIds => _orphanUserIds;

    public IReadOnlyDictionary<UserFkColumn, IReadOnlyDictionary<long, long>> ForeignKeyValueCounts => _foreignKeyValueCounts;

    public bool IsAllowedUser(long userId)
    {
        return _allowedUserIdSet.Contains(userId);
    }

    public bool IsOrphan(long userId)
    {
        return _orphanUserIdSet.Contains(userId);
    }

    public void SetUserFkCatalog(IReadOnlyList<UserFkColumn> catalog)
    {
        _userFkCatalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public void SetUserMap(IReadOnlyList<UserMappingEntry> mappings)
    {
        _userMappings = mappings ?? throw new ArgumentNullException(nameof(mappings));
    }

    public void SetAllowedUserIds(IReadOnlyCollection<long> allowedUserIds)
    {
        if (allowedUserIds is null)
        {
            throw new ArgumentNullException(nameof(allowedUserIds));
        }

        _allowedUserIdSet.Clear();
        var ordered = new SortedSet<long>(allowedUserIds);
        foreach (var id in ordered)
        {
            _allowedUserIdSet.Add(id);
        }

        _allowedUserIds = ordered.ToList();
    }

    public void SetOrphanUserIds(IReadOnlyCollection<long> orphanUserIds)
    {
        if (orphanUserIds is null)
        {
            throw new ArgumentNullException(nameof(orphanUserIds));
        }

        _orphanUserIdSet.Clear();
        var ordered = new SortedSet<long>(orphanUserIds);
        foreach (var id in ordered)
        {
            _orphanUserIdSet.Add(id);
        }

        _orphanUserIds = ordered.ToList();
    }

    public void SetForeignKeyValueCounts(IReadOnlyDictionary<UserFkColumn, IReadOnlyDictionary<long, long>> counts)
    {
        if (counts is null)
        {
            throw new ArgumentNullException(nameof(counts));
        }

        var builder = ImmutableDictionary.CreateBuilder<UserFkColumn, IReadOnlyDictionary<long, long>>();
        foreach (var pair in counts)
        {
            IReadOnlyDictionary<long, long> values = pair.Value switch
            {
                ImmutableDictionary<long, long> immutable => immutable,
                ImmutableSortedDictionary<long, long> sorted => sorted,
                _ => ImmutableSortedDictionary.CreateRange(pair.Value)
            };

            builder[pair.Key] = values;
        }

        _foreignKeyValueCounts = builder.ToImmutable();
    }
}
