using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Osm.Pipeline.UatUsers.Verification;

/// <summary>
/// Verifies FK catalog completeness by validating the catalog artifact.
/// For MVP, validates that the catalog file exists and is well-formed.
/// Future: Compare against expected columns from model metadata.
/// </summary>
public sealed class FkCatalogCompletenessVerifier
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    static FkCatalogCompletenessVerifier()
    {
        SerializerOptions.Converters.Add(new UserIdentifierJsonConverter());
    }

    /// <summary>
    /// Verifies the FK catalog snapshot file.
    /// </summary>
    /// <param name="catalogPath">Path to the FK catalog JSON snapshot file</param>
    /// <param name="expectedColumns">Optional list of expected column identifiers for comparison</param>
    /// <returns>Verification result</returns>
    public async Task<FkCatalogVerificationResult> VerifyAsync(
        string catalogPath,
        IReadOnlyList<string>? expectedColumns = null)
    {
        if (string.IsNullOrWhiteSpace(catalogPath))
        {
            throw new ArgumentException("Catalog path must be provided.", nameof(catalogPath));
        }

        if (!File.Exists(catalogPath))
        {
            return FkCatalogVerificationResult.Failure(
                discoveredColumnCount: 0,
                expectedColumnCount: expectedColumns?.Count ?? 0,
                missingColumns: expectedColumns?.ToImmutableArray() ?? ImmutableArray<string>.Empty,
                unexpectedColumns: ImmutableArray<string>.Empty);
        }

        // Load the FK snapshot
        UserForeignKeySnapshot? snapshot;
        try
        {
            await using var stream = File.OpenRead(catalogPath);
            snapshot = await JsonSerializer.DeserializeAsync<UserForeignKeySnapshot>(
                stream,
                SerializerOptions,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return FkCatalogVerificationResult.Failure(
                discoveredColumnCount: 0,
                expectedColumnCount: expectedColumns?.Count ?? 0,
                missingColumns: expectedColumns?.ToImmutableArray() ?? ImmutableArray<string>.Empty,
                unexpectedColumns: ImmutableArray<string>.Empty);
        }

        if (snapshot?.Columns is null)
        {
            return FkCatalogVerificationResult.Failure(
                discoveredColumnCount: 0,
                expectedColumnCount: expectedColumns?.Count ?? 0,
                missingColumns: expectedColumns?.ToImmutableArray() ?? ImmutableArray<string>.Empty,
                unexpectedColumns: ImmutableArray<string>.Empty);
        }

        var discoveredColumnIds = snapshot.Columns
            .Select(col => FormatColumnId(col.Schema, col.Table, col.Column))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var discoveredCount = discoveredColumnIds.Count;

        // If no expected columns provided, just validate that we have some columns
        if (expectedColumns is null || expectedColumns.Count == 0)
        {
            return FkCatalogVerificationResult.Success(discoveredCount, discoveredCount);
        }

        // Compare discovered vs expected
        var expectedSet = expectedColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingColumns = expectedSet.Except(discoveredColumnIds).ToImmutableArray();
        var unexpectedColumns = discoveredColumnIds.Except(expectedSet).ToImmutableArray();

        var isValid = missingColumns.Length == 0;

        if (isValid)
        {
            // Note: Unexpected columns are warnings only, don't fail verification
            return FkCatalogVerificationResult.Success(discoveredCount, expectedColumns.Count);
        }

        return FkCatalogVerificationResult.Failure(
            discoveredCount,
            expectedColumns.Count,
            missingColumns,
            unexpectedColumns);
    }

    private static string FormatColumnId(string schema, string table, string column)
    {
        return $"{schema}.{table}.{column}";
    }
}
