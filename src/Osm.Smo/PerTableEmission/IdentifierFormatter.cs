using System;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Osm.Smo.PerTableEmission;

internal sealed class IdentifierFormatter
{
    public Identifier CreateIdentifier(string value, SmoFormatOptions format)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (format is null)
        {
            throw new ArgumentNullException(nameof(format));
        }

        return new Identifier
        {
            Value = value,
            QuoteType = MapQuoteType(format.IdentifierQuoteStrategy),
        };
    }

    public SchemaObjectName BuildSchemaObjectName(string schema, string name, SmoFormatOptions format)
    {
        if (schema is null)
        {
            throw new ArgumentNullException(nameof(schema));
        }

        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        if (format is null)
        {
            throw new ArgumentNullException(nameof(format));
        }

        return new SchemaObjectName
        {
            Identifiers =
            {
                CreateIdentifier(schema, format),
                CreateIdentifier(name, format),
            }
        };
    }

    public ColumnReferenceExpression BuildColumnReference(string columnName, SmoFormatOptions format)
    {
        if (columnName is null)
        {
            throw new ArgumentNullException(nameof(columnName));
        }

        if (format is null)
        {
            throw new ArgumentNullException(nameof(format));
        }

        return new ColumnReferenceExpression
        {
            MultiPartIdentifier = new MultiPartIdentifier
            {
                Identifiers = { CreateIdentifier(columnName, format) }
            }
        };
    }

    public string ResolveConstraintName(
        string originalName,
        string originalTableName,
        string logicalTableName,
        string effectiveTableName)
    {
        if (string.IsNullOrWhiteSpace(originalName) ||
            string.Equals(originalTableName, effectiveTableName, StringComparison.OrdinalIgnoreCase))
        {
            return originalName;
        }

        var renamed = ReplaceIgnoreCase(originalName, originalTableName, effectiveTableName);
        renamed = ReplaceIgnoreCase(renamed, logicalTableName, effectiveTableName);
        return renamed;
    }

    public string QuoteIdentifier(string identifier, SmoFormatOptions format)
    {
        if (identifier is null)
        {
            throw new ArgumentNullException(nameof(identifier));
        }

        if (format is null)
        {
            throw new ArgumentNullException(nameof(format));
        }

        return format.IdentifierQuoteStrategy switch
        {
            IdentifierQuoteStrategy.DoubleQuote => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"",
            IdentifierQuoteStrategy.None => identifier,
            _ => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]",
        };
    }

    private static QuoteType MapQuoteType(IdentifierQuoteStrategy strategy) => strategy switch
    {
        IdentifierQuoteStrategy.DoubleQuote => QuoteType.DoubleQuote,
        IdentifierQuoteStrategy.None => QuoteType.NotQuoted,
        _ => QuoteType.SquareBracket,
    };

    private static string ReplaceIgnoreCase(string source, string search, string replacement)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(search))
        {
            return source;
        }

        var currentIndex = 0;
        const StringComparison comparison = StringComparison.OrdinalIgnoreCase;
        var result = new System.Text.StringBuilder(source.Length + replacement.Length);

        while (true)
        {
            var matchIndex = source.IndexOf(search, currentIndex, comparison);
            if (matchIndex < 0)
            {
                result.Append(source, currentIndex, source.Length - currentIndex);
                break;
            }

            result.Append(source, currentIndex, matchIndex - currentIndex);
            result.Append(replacement);
            currentIndex = matchIndex + search.Length;
        }

        return result.ToString();
    }
}
