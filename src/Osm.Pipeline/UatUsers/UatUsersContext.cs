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
    private readonly HashSet<UserIdentifier> _allowedUserIdSet = new();
    private IReadOnlyList<UserIdentifier> _allowedUserIds = Array.Empty<UserIdentifier>();
    private readonly HashSet<UserIdentifier> _orphanUserIdSet = new();
    private IReadOnlyList<UserIdentifier> _orphanUserIds = Array.Empty<UserIdentifier>();
    private IReadOnlyDictionary<UserIdentifier, UserInventoryRecord> _qaUserInventory
        = ImmutableSortedDictionary<UserIdentifier, UserInventoryRecord>.Empty;
    private IReadOnlyDictionary<UserIdentifier, UserInventoryRecord> _uatUserInventory
        = ImmutableSortedDictionary<UserIdentifier, UserInventoryRecord>.Empty;
    private IReadOnlyDictionary<UserFkColumn, IReadOnlyDictionary<UserIdentifier, long>> _foreignKeyValueCounts
        = ImmutableDictionary<UserFkColumn, IReadOnlyDictionary<UserIdentifier, long>>.Empty;
    private IReadOnlyList<UserMappingEntry> _automaticMappings = Array.Empty<UserMappingEntry>();
    private IReadOnlyList<UserMatchingResult> _matchingResults = Array.Empty<UserMatchingResult>();

    public UatUsersContext(
        IUserSchemaGraph schemaGraph,
        UatUsersArtifacts artifacts,
        IDbConnectionFactory connectionFactory,
        string userSchema,
        string userTable,
        string userIdColumn,
        IReadOnlyCollection<string>? includeColumns,
        string userMapPath,
        string uatUserInventoryPath,
        string qaUserInventoryPath,
        string? snapshotPath,
        string? userEntityIdentifier,
        bool fromLiveMetadata,
        string sourceFingerprint,
        UserMatchingStrategy matchingStrategy = UserMatchingStrategy.CaseInsensitiveEmail,
        string? matchingAttribute = null,
        string? matchingRegexPattern = null,
        UserFallbackAssignmentMode fallbackAssignment = UserFallbackAssignmentMode.Ignore,
        IEnumerable<UserIdentifier>? fallbackTargets = null,
        bool idempotentEmission = false,
        int? concurrency = null,
        IProgress<(int Completed, int Total)>? progress = null)
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
        if (string.IsNullOrWhiteSpace(uatUserInventoryPath))
        {
            throw new ArgumentException("UAT user inventory path must be provided.", nameof(uatUserInventoryPath));
        }

        if (string.IsNullOrWhiteSpace(qaUserInventoryPath))
        {
            throw new ArgumentException("QA user inventory path must be provided.", nameof(qaUserInventoryPath));
        }

        UserMapPath = Path.GetFullPath(userMapPath);
        UatUserInventoryPath = Path.GetFullPath(uatUserInventoryPath);
        QaUserInventoryPath = Path.GetFullPath(qaUserInventoryPath);
        SnapshotPath = string.IsNullOrWhiteSpace(snapshotPath) ? null : Path.GetFullPath(snapshotPath);
        UserEntityIdentifier = string.IsNullOrWhiteSpace(userEntityIdentifier) ? null : userEntityIdentifier.Trim();
        FromLiveMetadata = fromLiveMetadata;
        SourceFingerprint = sourceFingerprint.Trim();
        MatchingStrategy = matchingStrategy;
        MatchingAttribute = UserMatchingConfigurationHelper.ResolveAttribute(matchingStrategy, matchingAttribute);
        MatchingRegexPattern = string.IsNullOrWhiteSpace(matchingRegexPattern) ? null : matchingRegexPattern.Trim();
        FallbackAssignment = fallbackAssignment;
        FallbackTargets = fallbackTargets is null
            ? ImmutableArray<UserIdentifier>.Empty
            : fallbackTargets
                .Distinct()
                .ToImmutableArray();
        IdempotentEmission = idempotentEmission;
        Concurrency = concurrency ?? 4;
        Progress = progress;
    }

    public IUserSchemaGraph SchemaGraph { get; }

    public UatUsersArtifacts Artifacts { get; }

    public IDbConnectionFactory ConnectionFactory { get; }

    public string UserSchema { get; }

    public string UserTable { get; }

    public string UserIdColumn { get; }

    public ISet<string>? IncludeColumns { get; }

    public string UserMapPath { get; }

    public string UatUserInventoryPath { get; }

    public string QaUserInventoryPath { get; }

    public string? SnapshotPath { get; }

    public string? UserEntityIdentifier { get; }

    public bool FromLiveMetadata { get; }

    public string SourceFingerprint { get; }

    public IReadOnlyList<UserFkColumn> UserFkCatalog => _userFkCatalog;

    public IReadOnlyList<UserMappingEntry> UserMap => _userMappings;

    public IReadOnlyCollection<UserIdentifier> AllowedUserIds => _allowedUserIds;

    public IReadOnlyCollection<UserIdentifier> OrphanUserIds => _orphanUserIds;

    public IReadOnlyDictionary<UserIdentifier, UserInventoryRecord> QaUserInventory => _qaUserInventory;

    public IReadOnlyDictionary<UserIdentifier, UserInventoryRecord> UatUserInventory => _uatUserInventory;

    public bool TryGetQaUser(UserIdentifier userId, out UserInventoryRecord record)
    {
        return _qaUserInventory.TryGetValue(userId, out record!);
    }

    public IReadOnlyDictionary<UserFkColumn, IReadOnlyDictionary<UserIdentifier, long>> ForeignKeyValueCounts => _foreignKeyValueCounts;

    public bool IsAllowedUser(UserIdentifier userId)
    {
        return _allowedUserIdSet.Contains(userId);
    }

    public bool IsOrphan(UserIdentifier userId)
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

    public void SetAllowedUserIds(IReadOnlyCollection<UserIdentifier> allowedUserIds)
    {
        if (allowedUserIds is null)
        {
            throw new ArgumentNullException(nameof(allowedUserIds));
        }

        _allowedUserIdSet.Clear();
        var ordered = new SortedSet<UserIdentifier>(allowedUserIds);
        foreach (var id in ordered)
        {
            _allowedUserIdSet.Add(id);
        }

        _allowedUserIds = ordered.ToList();
    }

    public void SetOrphanUserIds(IReadOnlyCollection<UserIdentifier> orphanUserIds)
    {
        if (orphanUserIds is null)
        {
            throw new ArgumentNullException(nameof(orphanUserIds));
        }

        _orphanUserIdSet.Clear();
        var ordered = new SortedSet<UserIdentifier>(orphanUserIds);
        foreach (var id in ordered)
        {
            _orphanUserIdSet.Add(id);
        }

        _orphanUserIds = ordered.ToList();
    }

    public void SetQaUserInventory(IReadOnlyDictionary<UserIdentifier, UserInventoryRecord> inventory)
    {
        if (inventory is null)
        {
            throw new ArgumentNullException(nameof(inventory));
        }

        _qaUserInventory = inventory switch
        {
            ImmutableSortedDictionary<UserIdentifier, UserInventoryRecord> sorted => sorted,
            ImmutableDictionary<UserIdentifier, UserInventoryRecord> immutable => immutable,
            _ => ImmutableSortedDictionary.CreateRange(inventory)
        };
    }

    public void SetUatUserInventory(IReadOnlyDictionary<UserIdentifier, UserInventoryRecord> inventory)
    {
        if (inventory is null)
        {
            throw new ArgumentNullException(nameof(inventory));
        }

        _uatUserInventory = inventory switch
        {
            ImmutableSortedDictionary<UserIdentifier, UserInventoryRecord> sorted => sorted,
            ImmutableDictionary<UserIdentifier, UserInventoryRecord> immutable => immutable,
            _ => ImmutableSortedDictionary.CreateRange(inventory)
        };
    }

    public void SetForeignKeyValueCounts(IReadOnlyDictionary<UserFkColumn, IReadOnlyDictionary<UserIdentifier, long>> counts)
    {
        if (counts is null)
        {
            throw new ArgumentNullException(nameof(counts));
        }

        var builder = ImmutableDictionary.CreateBuilder<UserFkColumn, IReadOnlyDictionary<UserIdentifier, long>>();
        foreach (var pair in counts)
        {
            IReadOnlyDictionary<UserIdentifier, long> values = pair.Value switch
            {
                ImmutableDictionary<UserIdentifier, long> immutable => immutable,
                ImmutableSortedDictionary<UserIdentifier, long> sorted => sorted,
                _ => ImmutableSortedDictionary.CreateRange(pair.Value)
            };

            builder[pair.Key] = values;
        }

        _foreignKeyValueCounts = builder.ToImmutable();
    }

    public void SetAutomaticMappings(IReadOnlyList<UserMappingEntry> entries)
    {
        _automaticMappings = entries ?? throw new ArgumentNullException(nameof(entries));
    }

    public void SetMatchingResults(IReadOnlyList<UserMatchingResult> results)
    {
        _matchingResults = results ?? throw new ArgumentNullException(nameof(results));
    }

    public IReadOnlyList<UserMappingEntry> AutomaticMappings => _automaticMappings;

    public IReadOnlyList<UserMatchingResult> MatchingResults => _matchingResults;

    public UserMatchingStrategy MatchingStrategy { get; }

    public string? MatchingAttribute { get; }

    public string? MatchingRegexPattern { get; }

    public UserFallbackAssignmentMode FallbackAssignment { get; }

    public ImmutableArray<UserIdentifier> FallbackTargets { get; }

    public bool IdempotentEmission { get; }

    public int Concurrency { get; }

    public IProgress<(int Completed, int Total)>? Progress { get; }

}
