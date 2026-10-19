using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Osm.Pipeline.UatUsers.Verification;

/// <summary>
/// Orchestrates UAT-users artifact verification.
/// Coordinates all verifiers and builds the verification context.
/// </summary>
public sealed class UatUsersVerifier
{
    private readonly TransformationMapVerifier _mapVerifier;
    private readonly FkCatalogCompletenessVerifier _catalogVerifier;
    private readonly SqlSafetyAnalyzer _sqlAnalyzer;

    public UatUsersVerifier()
    {
        _mapVerifier = new TransformationMapVerifier();
        _catalogVerifier = new FkCatalogCompletenessVerifier();
        _sqlAnalyzer = new SqlSafetyAnalyzer();
    }

    /// <summary>
    /// Verifies all UAT-users artifacts in the specified directory.
    /// </summary>
    /// <param name="artifactRoot">Root directory containing uat-users artifacts</param>
    /// <param name="qaInventoryPath">Path to QA user inventory CSV</param>
    /// <param name="uatInventoryPath">Path to UAT user inventory CSV</param>
    /// <returns>Verification context with all results</returns>
    public async Task<UatUsersVerificationContext> VerifyAsync(
        string artifactRoot,
        string qaInventoryPath,
        string uatInventoryPath)
    {
        if (string.IsNullOrWhiteSpace(artifactRoot))
        {
            throw new ArgumentException("Artifact root must be provided.", nameof(artifactRoot));
        }

        if (!Directory.Exists(artifactRoot))
        {
            throw new DirectoryNotFoundException($"Artifact root directory not found: {artifactRoot}");
        }

        // Standard UAT-users artifact paths
        var uatUsersDir = Path.Combine(artifactRoot, "uat-users");
        var userMapPath = Path.Combine(uatUsersDir, "00_user_map.csv");
        var sqlScriptPath = Path.Combine(uatUsersDir, "02_apply_user_remap.sql");
        var fkCatalogPath = Path.Combine(uatUsersDir, "03_user_fk_catalog.json");

        // Run all verifications
        var mapVerification = _mapVerifier.Verify(
            userMapPath,
            qaInventoryPath,
            uatInventoryPath);

        var catalogVerification = await _catalogVerifier.VerifyAsync(fkCatalogPath)
            .ConfigureAwait(false);

        var sqlSafety = _sqlAnalyzer.Analyze(sqlScriptPath);

        var inventoryCompliance = VerifyInventoryCompliance(
            userMapPath,
            uatInventoryPath);

        return new UatUsersVerificationContext(
            artifactRoot,
            mapVerification,
            catalogVerification,
            sqlSafety,
            inventoryCompliance);
    }

    private static UatInventoryComplianceResult VerifyInventoryCompliance(
        string userMapPath,
        string uatInventoryPath)
    {
        if (!File.Exists(userMapPath) || !File.Exists(uatInventoryPath))
        {
            return UatInventoryComplianceResult.Failure(
                uatInventoryCount: 0,
                referencedTargetCount: 0,
                missingUatUsers: ImmutableArray<UserIdentifier>.Empty);
        }

        try
        {
            var userMap = UserMapLoader.Load(userMapPath);
            var uatInventoryResult = UserInventoryLoader.Load(uatInventoryPath);
            var uatInventory = uatInventoryResult.Records;

            var referencedTargets = userMap
                .Where(entry => entry.TargetUserId.HasValue)
                .Select(entry => entry.TargetUserId!.Value)
                .Distinct()
                .ToList();

            var missingUatUsers = referencedTargets
                .Where(targetId => !uatInventory.ContainsKey(targetId))
                .ToImmutableArray();

            var isValid = missingUatUsers.Length == 0;

            if (isValid)
            {
                return UatInventoryComplianceResult.Success(
                    uatInventory.Count,
                    referencedTargets.Count);
            }

            return UatInventoryComplianceResult.Failure(
                uatInventory.Count,
                referencedTargets.Count,
                missingUatUsers);
        }
        catch (Exception)
        {
            // If we can't load the files, return a failure result
            return UatInventoryComplianceResult.Failure(
                uatInventoryCount: 0,
                referencedTargetCount: 0,
                missingUatUsers: ImmutableArray<UserIdentifier>.Empty);
        }
    }
}
