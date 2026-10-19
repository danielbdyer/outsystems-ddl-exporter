using System.Collections.Immutable;

namespace Osm.Validation.Tightening;

public enum TighteningDiagnosticSeverity
{
    Info = 0,
    Warning = 1
}

public sealed record TighteningDuplicateCandidate(string Module, string Schema, string PhysicalName);

public sealed record TighteningDiagnostic(
    string Code,
    string Message,
    TighteningDiagnosticSeverity Severity,
    string LogicalName,
    string CanonicalModule,
    string CanonicalSchema,
    string CanonicalPhysicalName,
    ImmutableArray<TighteningDuplicateCandidate> Candidates,
    bool ResolvedByOverride)
{
    public static TighteningDiagnostic CreateMandatoryNullConflict(
        string schema,
        string table,
        string column,
        string logicalName,
        string moduleName,
        ImmutableArray<string> primaryKeyColumns,
        ImmutableArray<string> sampleNullRows,
        long totalNullRows,
        string remediationQuery)
    {
        var message = BuildMandatoryNullMessage(schema, table, column, primaryKeyColumns, sampleNullRows, totalNullRows, remediationQuery);

        return new TighteningDiagnostic(
            "tightening.nullability.mandatory.nulls",
            message,
            TighteningDiagnosticSeverity.Warning,
            logicalName,
            moduleName,
            schema,
            $"{table}.{column}",
            ImmutableArray<TighteningDuplicateCandidate>.Empty,
            false);
    }

    private static string BuildMandatoryNullMessage(
        string schema,
        string table,
        string column,
        ImmutableArray<string> primaryKeyColumns,
        ImmutableArray<string> sampleNullRows,
        long totalNullRows,
        string remediationQuery)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine($"Column '{schema}.{table}.{column}' is marked as mandatory (isMandatory=true) but contains {totalNullRows} NULL value(s).");
        builder.AppendLine();

        if (!primaryKeyColumns.IsDefaultOrEmpty && !sampleNullRows.IsDefaultOrEmpty)
        {
            builder.AppendLine($"Sample rows with NULL values (identified by {string.Join(", ", primaryKeyColumns)}):");
            foreach (var row in sampleNullRows)
            {
                builder.AppendLine($"  - {row}");
            }

            if (sampleNullRows.Length < totalNullRows)
            {
                builder.AppendLine($"  ... and {totalNullRows - sampleNullRows.Length} more row(s)");
            }

            builder.AppendLine();
        }

        builder.AppendLine("Suggested remediation query:");
        builder.AppendLine(remediationQuery);

        return builder.ToString();
    }
}
