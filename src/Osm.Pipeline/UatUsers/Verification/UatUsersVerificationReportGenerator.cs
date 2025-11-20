using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Osm.Pipeline.UatUsers.Verification;

/// <summary>
/// Generates JSON verification reports for UAT-users artifacts.
/// Follows M1.1 export verification pattern for consistency.
/// </summary>
public sealed class UatUsersVerificationReportGenerator
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    static UatUsersVerificationReportGenerator()
    {
        SerializerOptions.Converters.Add(new UserIdentifierJsonConverter());
    }

    /// <summary>
    /// Generates a verification report from the verification context.
    /// </summary>
    /// <param name="context">Verification context with all results</param>
    /// <param name="artifactRoot">Root directory of artifacts</param>
    /// <returns>Verification report ready for serialization</returns>
    public UatUsersVerificationReport GenerateReport(
        UatUsersVerificationContext context,
        string artifactRoot)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var discrepancies = BuildDiscrepanciesList(context);

        if (context.IsValid)
        {
            return UatUsersVerificationReport.Success(context, artifactRoot);
        }

        return UatUsersVerificationReport.Failure(
            context,
            discrepancies.ToImmutableArray(),
            artifactRoot);
    }

    /// <summary>
    /// Writes the verification report to a JSON file.
    /// </summary>
    /// <param name="report">Verification report to write</param>
    /// <param name="outputPath">Path where report JSON will be written</param>
    public async Task WriteReportAsync(UatUsersVerificationReport report, string outputPath)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path must be provided.", nameof(outputPath));
        }

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = new FileStream(
            outputPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous);

        await JsonSerializer.SerializeAsync(stream, report, SerializerOptions)
            .ConfigureAwait(false);
    }

    private static List<string> BuildDiscrepanciesList(UatUsersVerificationContext context)
    {
        var discrepancies = new List<string>();

        // Map verification discrepancies
        if (!context.MapVerification.IsValid)
        {
            if (context.MapVerification.DuplicateSources.Length > 0)
            {
                discrepancies.Add($"Found {context.MapVerification.DuplicateSources.Length} duplicate source user ID(s) in transformation map");
            }

            if (context.MapVerification.MissingSources.Length > 0)
            {
                discrepancies.Add($"Found {context.MapVerification.MissingSources.Length} source user ID(s) not present in QA inventory");
            }

            if (context.MapVerification.InvalidTargets.Length > 0)
            {
                discrepancies.Add($"Found {context.MapVerification.InvalidTargets.Length} target user ID(s) not present in UAT inventory");
            }

            if (context.MapVerification.UnmappedCount > 0)
            {
                discrepancies.Add($"Found {context.MapVerification.UnmappedCount} orphan user(s) without target mappings");
            }
        }

        // Catalog verification discrepancies
        if (!context.CatalogVerification.IsValid)
        {
            if (context.CatalogVerification.MissingColumns.Length > 0)
            {
                discrepancies.Add($"FK catalog missing {context.CatalogVerification.MissingColumns.Length} expected column(s)");
            }

            if (context.CatalogVerification.UnexpectedColumns.Length > 0)
            {
                discrepancies.Add($"FK catalog contains {context.CatalogVerification.UnexpectedColumns.Length} unexpected column(s) (warning only)");
            }
        }

        // SQL safety discrepancies
        if (!context.SqlSafety.IsValid)
        {
            if (context.SqlSafety.MissingGuards.Length > 0)
            {
                discrepancies.Add($"SQL script missing {context.SqlSafety.MissingGuards.Length} required safety guard(s)");
                foreach (var guard in context.SqlSafety.MissingGuards)
                {
                    discrepancies.Add($"  - {guard}");
                }
            }
        }

        // Inventory compliance discrepancies
        if (!context.InventoryCompliance.IsValid)
        {
            if (context.InventoryCompliance.MissingUatUsers.Length > 0)
            {
                discrepancies.Add($"Found {context.InventoryCompliance.MissingUatUsers.Length} target user ID(s) not in UAT inventory");
            }
        }

        return discrepancies;
    }
}
