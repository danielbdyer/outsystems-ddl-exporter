using System;
using System.Text;
using Osm.Validation.Tightening;

namespace Osm.Cli;

internal static class PolicyDecisionLinkBuilder
{
    public static string CreateModuleAnchor(string module)
        => BuildAnchor("module", module);

    public static string CreateColumnAnchor(ColumnCoordinate coordinate)
        => BuildAnchor("column", $"{coordinate.Schema.Value}.{coordinate.Table.Value}.{coordinate.Column.Value}");

    public static string CreateForeignKeyAnchor(ColumnCoordinate coordinate)
        => BuildAnchor("foreign", $"{coordinate.Schema.Value}.{coordinate.Table.Value}.{coordinate.Column.Value}");

    public static string CreateUniqueIndexAnchor(IndexCoordinate coordinate)
        => BuildAnchor("unique", $"{coordinate.Schema.Value}.{coordinate.Table.Value}.{coordinate.Index.Value}");

    public static string CreateDiagnosticAnchor(TighteningDiagnostic diagnostic)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(diagnostic.CanonicalModule))
        {
            builder.Append(diagnostic.CanonicalModule);
            builder.Append('.');
        }

        if (!string.IsNullOrWhiteSpace(diagnostic.CanonicalSchema))
        {
            builder.Append(diagnostic.CanonicalSchema);
            builder.Append('.');
        }

        if (!string.IsNullOrWhiteSpace(diagnostic.CanonicalPhysicalName))
        {
            builder.Append(diagnostic.CanonicalPhysicalName);
            builder.Append('.');
        }

        builder.Append(diagnostic.Code);
        return BuildAnchor("diagnostic", builder.ToString());
    }

    public static string BuildReportLink(string reportPath, string anchor)
    {
        if (string.IsNullOrWhiteSpace(reportPath))
        {
            throw new ArgumentException("Report path must be provided.", nameof(reportPath));
        }

        if (string.IsNullOrWhiteSpace(anchor))
        {
            return reportPath;
        }

        return reportPath + "#" + anchor;
    }

    private static string BuildAnchor(string prefix, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return prefix;
        }

        var sanitized = Sanitize(value);
        return string.IsNullOrEmpty(sanitized) ? prefix : $"{prefix}-{sanitized}";
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasSeparator = false;

        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator)
            {
                builder.Append('-');
                lastWasSeparator = true;
            }
        }

        if (builder.Length > 0 && builder[^1] == '-')
        {
            builder.Length--;
        }

        return builder.ToString();
    }
}
