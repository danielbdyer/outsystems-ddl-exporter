using System;
using System.Text;

namespace Osm.Cli;

internal static class PolicyDecisionLinkBuilder
{
    public static string BuildModuleAnchor(string module)
        => $"module-{Sanitize(module)}";

    public static string BuildColumnAnchor(string schema, string table, string column)
        => $"column-{Sanitize(schema)}-{Sanitize(table)}-{Sanitize(column)}";

    public static string BuildUniqueIndexAnchor(string schema, string table, string index)
        => $"index-{Sanitize(schema)}-{Sanitize(table)}-{Sanitize(index)}";

    public static string BuildForeignKeyAnchor(string schema, string table, string column)
        => $"foreign-key-{Sanitize(schema)}-{Sanitize(table)}-{Sanitize(column)}";

    public static string BuildDiagnosticAnchor(string code, string module, string schema, string physicalName)
        => $"diagnostic-{Sanitize(code)}-{Sanitize(module)}-{Sanitize(schema)}-{Sanitize(physicalName)}";

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var builder = new StringBuilder(value.Length);
        var lastWasHyphen = false;

        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                lastWasHyphen = false;
                continue;
            }

            if (ch is '-' or '_')
            {
                if (!lastWasHyphen)
                {
                    builder.Append(ch);
                    lastWasHyphen = true;
                }

                continue;
            }

            if (!lastWasHyphen)
            {
                builder.Append('-');
                lastWasHyphen = true;
            }
        }

        if (builder.Length == 0)
        {
            return "unknown";
        }

        var result = builder.ToString().Trim('-');
        return string.IsNullOrEmpty(result) ? "unknown" : result;
    }
}
