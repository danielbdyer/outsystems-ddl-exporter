using System;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Osm.Dmm;

internal static class ScriptDomDataTypeComparer
{
    private static readonly TSql150Parser Parser = new(initialQuotedIdentifiers: true);
    private static readonly Sql150ScriptGenerator Generator = new(new SqlScriptGeneratorOptions
    {
        KeywordCasing = KeywordCasing.Lowercase,
        IncludeSemicolons = false,
        SqlVersion = SqlVersion.Sql150,
    });

    public static bool AreEquivalent(string expected, string actual)
    {
        if (string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var expectedType = Parse(expected);
        var actualType = Parse(actual);

        if (expectedType is null || actualType is null)
        {
            return string.Equals(Normalize(expected), Normalize(actual), StringComparison.OrdinalIgnoreCase);
        }

        var expectedScript = Script(expectedType);
        var actualScript = Script(actualType);
        return string.Equals(expectedScript, actualScript, StringComparison.OrdinalIgnoreCase);
    }

    private static DataTypeReference? Parse(string dataType)
    {
        if (string.IsNullOrWhiteSpace(dataType))
        {
            return null;
        }

        var script = $"CREATE TABLE [t] ([c] {dataType} NULL);";
        using var reader = new StringReader(script);
        var fragment = Parser.Parse(reader, out var errors);
        if (errors is { Count: > 0 })
        {
            return null;
        }

        if (fragment is not TSqlScript sqlScript)
        {
            return null;
        }

        var batch = sqlScript.Batches.FirstOrDefault();
        var statement = batch?.Statements.OfType<CreateTableStatement>().FirstOrDefault();
        var column = statement?.Definition.ColumnDefinitions.FirstOrDefault();
        return column?.DataType;
    }

    private static string Script(DataTypeReference dataType)
    {
        Generator.GenerateScript(dataType, out var script);
        return Normalize(script);
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        var chars = trimmed.Where(ch => !char.IsWhiteSpace(ch));
        return new string(chars.ToArray());
    }
}
