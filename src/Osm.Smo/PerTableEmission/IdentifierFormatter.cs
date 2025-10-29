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
}
