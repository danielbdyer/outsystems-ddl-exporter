using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Osm.Smo;

namespace Osm.Smo.PerTableEmission;

internal sealed class SqlScriptFormatter
{
    private readonly IdentifierFormatter _identifierFormatter;
    private readonly ConstraintFormatter _constraintFormatter;
    private readonly CreateTableFormatter _createTableFormatter;

    public SqlScriptFormatter()
    {
        _constraintFormatter = new ConstraintFormatter();
        _identifierFormatter = new IdentifierFormatter();
        _createTableFormatter = new CreateTableFormatter(_constraintFormatter);
    }

    internal SqlScriptFormatter(
        IdentifierFormatter identifierFormatter,
        ConstraintFormatter constraintFormatter,
        CreateTableFormatter createTableFormatter)
    {
        _identifierFormatter = identifierFormatter ?? throw new ArgumentNullException(nameof(identifierFormatter));
        _constraintFormatter = constraintFormatter ?? throw new ArgumentNullException(nameof(constraintFormatter));
        _createTableFormatter = createTableFormatter ?? throw new ArgumentNullException(nameof(createTableFormatter));
    }

    public Identifier CreateIdentifier(string value, SmoFormatOptions format)
        => _identifierFormatter.CreateIdentifier(value, format);

    public SchemaObjectName BuildSchemaObjectName(string schema, string name, SmoFormatOptions format)
        => _identifierFormatter.BuildSchemaObjectName(schema, name, format);

    public ColumnReferenceExpression BuildColumnReference(string columnName, SmoFormatOptions format)
        => _identifierFormatter.BuildColumnReference(columnName, format);

    public string ResolveConstraintName(
        string originalName,
        string originalTableName,
        string logicalTableName,
        string effectiveTableName)
        => _identifierFormatter.ResolveConstraintName(originalName, originalTableName, logicalTableName, effectiveTableName);

    public string QuoteIdentifier(string identifier, SmoFormatOptions format)
        => _identifierFormatter.QuoteIdentifier(identifier, format);

    public string NormalizeWhitespace(string script)
    {
        if (script is null)
        {
            throw new ArgumentNullException(nameof(script));
        }

        var lines = script.Split(Environment.NewLine);
        var builder = new StringBuilder(script.Length);

        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
            }

            builder.Append(lines[i].TrimEnd());
        }

        return builder.ToString();
    }

    public string JoinStatements(IReadOnlyList<string> statements, SmoFormatOptions format)
    {
        if (statements is null)
        {
            throw new ArgumentNullException(nameof(statements));
        }

        if (format is null)
        {
            throw new ArgumentNullException(nameof(format));
        }

        var builder = new StringBuilder();
        for (var i = 0; i < statements.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
                builder.AppendLine("GO");
                builder.AppendLine();
            }

            builder.AppendLine(statements[i]);
        }

        var script = builder.ToString().TrimEnd();
        return format.NormalizeWhitespace ? NormalizeWhitespace(script) : script;
    }

    public string FormatCreateTableScript(
        string script,
        CreateTableStatement statement,
        IReadOnlyDictionary<string, bool>? foreignKeyTrustLookup,
        SmoFormatOptions format)
        => _createTableFormatter.FormatCreateTableScript(script, statement, foreignKeyTrustLookup, format);

    public string FormatForeignKeyConstraints(
        string script,
        IReadOnlyDictionary<string, bool>? foreignKeyTrustLookup)
        => _constraintFormatter.FormatForeignKeyConstraints(script, foreignKeyTrustLookup);

    public string FormatPrimaryKeyConstraints(string script)
        => _constraintFormatter.FormatPrimaryKeyConstraints(script);
}
