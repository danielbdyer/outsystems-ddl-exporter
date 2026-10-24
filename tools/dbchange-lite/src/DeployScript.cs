using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DbChangeLite;

public sealed record Statement(TSqlStatement Node, string Part, int Line, string Kind, string Text, TSqlBatch Batch);

public sealed record DeployScript(IReadOnlyList<Statement> Statements, IReadOnlyList<string> Unresolved);

public static class DeployScripts
{
    private static readonly Regex Variable = new(@"\$\(([A-Za-z_][A-Za-z0-9_]*)\)", RegexOptions.CultureInvariant);
    private static readonly Regex SetVar = new(@"^\s*:setvar\s+(\S+)\s+(?:""([^""]*)""|(\S+))", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex Go = new(@"^\s*GO(\s+\d+)?\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static DeployScript Read(string script, string preDeploy, string postDeploy, IReadOnlyDictionary<string, string> sqlcmd)
    {
        var unresolved = new List<string>();
        var pre = Rendered(preDeploy, sqlcmd, unresolved);
        var post = Rendered(postDeploy, sqlcmd, unresolved);
        var statements = new List<Statement>();
        foreach (var (line, batch) in Batches(script, sqlcmd, unresolved))
        {
            foreach (var node in Flatten(batch.Statements))
            {
                var text = Render(node);
                var part = pre.Contains(text) ? "pre" : post.Contains(text) ? "post" : "own";
                statements.Add(new Statement(node, part, line + node.StartLine - 1, node.GetType().Name, text, batch));
            }
        }

        return new DeployScript(statements, unresolved.Distinct(StringComparer.Ordinal).ToList());
    }

    public static IReadOnlyList<(int Line, TSqlBatch Batch)> Batches(string script, IReadOnlyDictionary<string, string> sqlcmd, List<string> unresolved)
    {
        var values = new Dictionary<string, string>(sqlcmd, StringComparer.OrdinalIgnoreCase);
        var lines = script.ReplaceLineEndings("\n").Split('\n');
        var batches = new List<(int Line, TSqlBatch Batch)>();
        var current = new List<string>();
        var start = 1;
        void Close(int next)
        {
            var text = string.Join('\n', current);
            if (text.Trim().Length > 0)
            {
                var substituted = Variable.Replace(text, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : Unresolved(m.Groups[1].Value, unresolved));
                var parsed = Parse(substituted);
                batches.AddRange(parsed.Batches.Select(b => (start, b)));
            }

            current.Clear();
            start = next;
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (SetVar.Match(line) is { Success: true } set)
            {
                values[set.Groups[1].Value] = set.Groups[2].Success ? set.Groups[2].Value : set.Groups[3].Value;
                current.Add("");
            }
            else if (line.TrimStart().StartsWith(':'))
            {
                current.Add("");
            }
            else if (Go.IsMatch(line))
            {
                Close(i + 2);
            }
            else
            {
                current.Add(line);
            }
        }

        Close(lines.Length + 1);
        return batches;
    }

    private static string Unresolved(string name, List<string> unresolved)
    {
        unresolved.Add(name);
        return name;
    }

    public static TSqlScript Parse(string text)
    {
        var parser = new TSql160Parser(true);
        var fragment = parser.Parse(new StringReader(text), out var errors);
        if (errors.Count > 0)
        {
            throw Stop.Of("unparsed-input", "script.unparsed", "deploy script", "ScriptDom does not parse the deploy script at line " + errors[0].Line + ": " + errors[0].Message,
                "Correct the pre- or post-deploy script at the line named, or report the DacFx statement it names.");
        }

        return (TSqlScript)fragment;
    }

    public static IEnumerable<TSqlStatement> Flatten(IEnumerable<TSqlStatement> statements)
    {
        foreach (var statement in statements)
        {
            yield return statement;
            var inner = statement switch
            {
                IfStatement i => new[] { i.ThenStatement, i.ElseStatement }.Where(s => s is not null).Select(s => s!),
                BeginEndBlockStatement b => b.StatementList.Statements,
                TryCatchStatement t => t.TryStatements.Statements.Concat(t.CatchStatements.Statements),
                WhileStatement w => [w.Statement],
                _ => [],
            };
            foreach (var child in Flatten(inner))
            {
                yield return child;
            }
        }
    }

    public static string Render(TSqlFragment fragment)
    {
        new Sql160ScriptGenerator(new SqlScriptGeneratorOptions { KeywordCasing = KeywordCasing.Uppercase, IncludeSemicolons = false, NewLineBeforeFromClause = false, NewLineBeforeWhereClause = false })
            .GenerateScript(fragment, out var text);
        return Regex.Replace(text, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
    }

    private static HashSet<string> Rendered(string script, IReadOnlyDictionary<string, string> sqlcmd, List<string> unresolved)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (script.Trim().Length == 0)
        {
            return set;
        }

        foreach (var (_, batch) in Batches(script, sqlcmd, unresolved))
        {
            foreach (var node in Flatten(batch.Statements))
            {
                set.Add(Render(node));
            }
        }

        return set;
    }

    public static string TableOf(SchemaObjectName name) => Names.Quote(name.SchemaIdentifier?.Value ?? "dbo", name.BaseIdentifier.Value);

    public static string? TargetOf(TableReference? target) => target is NamedTableReference named ? TableOf(named.SchemaObject) : null;
}
