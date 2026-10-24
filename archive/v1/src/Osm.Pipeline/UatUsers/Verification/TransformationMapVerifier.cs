using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace Osm.Pipeline.UatUsers.Verification;

/// <summary>
/// Verifies transformation map completeness and consistency.
/// Validates that all orphan users have valid mappings and all targets exist in UAT inventory.
/// </summary>
public sealed class TransformationMapVerifier
{
    /// <summary>
    /// Verifies the transformation map against QA and UAT inventories.
    /// </summary>
    /// <param name="userMapPath">Path to the user mapping CSV file (00_user_map.csv)</param>
    /// <param name="qaInventoryPath">Path to the QA user inventory CSV</param>
    /// <param name="uatInventoryPath">Path to the UAT user inventory CSV</param>
    /// <param name="orphanUserIdsPath">Optional path to orphan user IDs file</param>
    /// <returns>Verification result with discrepancies if any</returns>
    public UserMapVerificationResult Verify(
        string userMapPath,
        string qaInventoryPath,
        string uatInventoryPath,
        string? orphanUserIdsPath = null)
    {
        if (string.IsNullOrWhiteSpace(userMapPath))
        {
            throw new ArgumentException("User map path must be provided.", nameof(userMapPath));
        }

        if (string.IsNullOrWhiteSpace(qaInventoryPath))
        {
            throw new ArgumentException("QA inventory path must be provided.", nameof(qaInventoryPath));
        }

        if (string.IsNullOrWhiteSpace(uatInventoryPath))
        {
            throw new ArgumentException("UAT inventory path must be provided.", nameof(uatInventoryPath));
        }

        // Load artifacts
        var userMap = LoadUserMap(userMapPath);
        var qaInventory = LoadInventory(qaInventoryPath);
        var uatInventory = LoadInventory(uatInventoryPath);
        var orphanIds = LoadOrphanIds(orphanUserIdsPath);

        // Perform validations
        var duplicateSources = FindDuplicateSources(userMap);
        var missingSources = FindMissingSources(userMap, qaInventory);
        var invalidTargets = FindInvalidTargets(userMap, uatInventory);
        var unmappedOrphans = FindUnmappedOrphans(userMap, orphanIds);

        var mappedCount = userMap.Count(entry => entry.TargetUserId.HasValue);
        var unmappedCount = unmappedOrphans.Length;
        var orphanCount = orphanIds?.Count ?? userMap.Count;

        var isValid = duplicateSources.Length == 0 &&
                      missingSources.Length == 0 &&
                      invalidTargets.Length == 0 &&
                      unmappedCount == 0;

        if (isValid)
        {
            return UserMapVerificationResult.Success(orphanCount, mappedCount);
        }

        return UserMapVerificationResult.Failure(
            orphanCount,
            mappedCount,
            unmappedCount,
            duplicateSources,
            missingSources,
            invalidTargets);
    }

    private static IReadOnlyList<UserMappingEntry> LoadUserMap(string path)
    {
        if (!File.Exists(path))
        {
            return Array.Empty<UserMappingEntry>();
        }

        return UserMapLoader.Load(path);
    }

    private static IReadOnlyDictionary<UserIdentifier, UserInventoryRecord> LoadInventory(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<UserIdentifier, UserInventoryRecord>();
        }

        var result = UserInventoryLoader.Load(path);
        return result.Records;
    }

    private static HashSet<UserIdentifier>? LoadOrphanIds(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var lines = File.ReadAllLines(path);
        var orphans = new HashSet<UserIdentifier>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            if (UserIdentifier.TryParse(line.Trim(), out var id))
            {
                orphans.Add(id);
            }
        }

        return orphans;
    }

    private static ImmutableArray<UserIdentifier> FindDuplicateSources(
        IReadOnlyList<UserMappingEntry> userMap)
    {
        var seen = new HashSet<UserIdentifier>();
        var duplicates = new HashSet<UserIdentifier>();

        foreach (var entry in userMap)
        {
            if (!seen.Add(entry.SourceUserId))
            {
                duplicates.Add(entry.SourceUserId);
            }
        }

        return duplicates.ToImmutableArray();
    }

    private static ImmutableArray<UserIdentifier> FindMissingSources(
        IReadOnlyList<UserMappingEntry> userMap,
        IReadOnlyDictionary<UserIdentifier, UserInventoryRecord> qaInventory)
    {
        var missing = new List<UserIdentifier>();

        foreach (var entry in userMap)
        {
            if (!qaInventory.ContainsKey(entry.SourceUserId))
            {
                missing.Add(entry.SourceUserId);
            }
        }

        return missing.ToImmutableArray();
    }

    private static ImmutableArray<UserIdentifier> FindInvalidTargets(
        IReadOnlyList<UserMappingEntry> userMap,
        IReadOnlyDictionary<UserIdentifier, UserInventoryRecord> uatInventory)
    {
        var invalid = new List<UserIdentifier>();

        foreach (var entry in userMap)
        {
            if (entry.TargetUserId.HasValue && !uatInventory.ContainsKey(entry.TargetUserId.Value))
            {
                invalid.Add(entry.TargetUserId.Value);
            }
        }

        return invalid.ToImmutableArray();
    }

    private static ImmutableArray<UserIdentifier> FindUnmappedOrphans(
        IReadOnlyList<UserMappingEntry> userMap,
        HashSet<UserIdentifier>? orphanIds)
    {
        if (orphanIds is null)
        {
            // If orphan IDs not provided, consider unmapped entries in the map
            return userMap
                .Where(entry => !entry.TargetUserId.HasValue)
                .Select(entry => entry.SourceUserId)
                .ToImmutableArray();
        }

        var mapped = userMap
            .Where(entry => entry.TargetUserId.HasValue)
            .Select(entry => entry.SourceUserId)
            .ToHashSet();

        return orphanIds
            .Where(id => !mapped.Contains(id))
            .ToImmutableArray();
    }
}
