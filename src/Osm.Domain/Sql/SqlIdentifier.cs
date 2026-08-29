using System;

namespace Osm.Domain.Sql;

/// <summary>
/// Bracket-quoting for SQL Server identifiers. Centralizes the
/// <c>[name]</c> / <c>[name]]with]]brackets]</c> escaping rule that was
/// previously hand-reimplemented across the emission, validation, and
/// pipeline layers. This is pure SQL Server dialect formatting; it carries
/// no model or persistence state.
/// </summary>
public static class SqlIdentifier
{
    /// <summary>
    /// Wraps an identifier in square brackets, escaping any embedded closing
    /// bracket by doubling it. Null or empty input yields <c>[]</c>.
    /// </summary>
    public static string Quote(string? identifier)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            return "[]";
        }

        return "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";
    }

    /// <summary>
    /// Produces a two-part <c>[schema].[name]</c> quoted identifier.
    /// </summary>
    public static string Qualify(string schema, string name)
        => Quote(schema) + "." + Quote(name);
}
