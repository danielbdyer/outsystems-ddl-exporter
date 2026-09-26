using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DbChange.Kernel;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DbChange.Io;

/// <summary>
/// T-SQL as ScriptDom reads and writes it for io: SQL Server 2022's grammar with quoted identifiers on (TSql160Parser), and
/// Sql160ScriptGenerator's default formatting with LF line ends and no surrounding white space, the one form in which dbchange runs and
/// logs a statement it built or checked. Each call makes its own parser and generator, since ScriptDom does not document either as
/// safe to share between threads.
/// </summary>
internal static class TSql
{
    /// <summary>The tree ScriptDom reads from <paramref name="text"/>, and the errors it found, each with its line and column.</summary>
    internal static TSqlFragment Parse(string text, out IList<ParseError> errors) => Parser().Parse(new StringReader(text), out errors);

    /// <summary>A tree written back as T-SQL: the text dbchange sends and its query log holds, byte for byte on every operating system.</summary>
    internal static string Text(TSqlFragment fragment)
    {
        new Sql160ScriptGenerator().GenerateScript(fragment, out var script);
        return LineEndings.Lf(script).Trim();
    }

    /// <summary>The parts of an object's name of one to four parts, unquoted as ScriptDom reads them ([a]]b] is a]b); null when the text is no such name.</summary>
    internal static string[]? NameParts(string name) =>
        Parser().ParseSchemaObjectName(new StringReader(name), out var errors) is { } parsed && errors.Count == 0 ? [.. parsed.Identifiers.Select(i => i.Value)] : null;

    /// <summary>Each object a tree reads by name in a FROM or a JOIN, once, its parts quoted as QUOTENAME quotes them, in the order the tree first names them.</summary>
    internal static IReadOnlyList<string> TablesNamed(TSqlFragment fragment)
    {
        var named = new Named();
        fragment.Accept(named);
        return [.. named.Found.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The one way io makes a parser: SQL Server 2022's grammar, quoted identifiers on, or off for text a script may have written under
    /// SET QUOTED_IDENTIFIER OFF (io/SchemaText tries both).
    /// </summary>
    internal static TSql160Parser Parser(bool quotedIdentifiers = true) => new(initialQuotedIdentifiers: quotedIdentifiers);

    private sealed class Named : TSqlFragmentVisitor
    {
        public List<string> Found { get; } = [];

        public override void Visit(NamedTableReference node) =>
            Found.Add(string.Join('.', node.SchemaObject.Identifiers.Select(i => "[" + i.Value.Replace("]", "]]", StringComparison.Ordinal) + "]")));
    }
}
