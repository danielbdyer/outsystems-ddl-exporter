using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DbChange.Kernel;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Xunit;

namespace DbChange.Io.Tests;

/// <summary>
/// A deployment script DacFx wrote, read as spike S2 reads it (V3_MILESTONES.md §6): its line endings turned to LF, each SQLCMD
/// directive line (<c>:setvar</c>, <c>:on error exit</c>) dropped, and the rest parsed by io's TSql160Parser, which must report no
/// error. WP 2.2's reader in io/SqlServer.Plan replaces <see cref="In"/> and is held to the same committed files.
/// </summary>
internal static class BlockOnPossibleDataLossChecks
{
    /// <summary>
    /// The BlockOnPossibleDataLoss checks in the script, in script order, each as DacFx wrote it (LF line endings): an IF whose predicate
    /// is EXISTS (…) and whose body is a single RAISERROR at state 127 (§1 fact 3). Neither lookalike matches: the copy step of a table
    /// rebuild, IF EXISTS (SELECT TOP 1 1 FROM [T]) BEGIN … INSERT … END, and the verification of a constraint DacFx added WITH NOCHECK,
    /// RAISERROR (…, 16, 127) under IF @@ROWCOUNT &gt; 0; nor does the check's text quoted in the pre-deploy script's block comment.
    /// </summary>
    public static IReadOnlyList<string> In(string script) => [.. Checks(script).Select(c => c.Text)];

    /// <summary>Each check as <see cref="In"/> finds it, with the query its EXISTS runs.</summary>
    public static IReadOnlyList<(string Text, QueryExpression Predicate)> Checks(string script)
    {
        var body = Body(script);
        var found = new List<(string, QueryExpression)>();
        Parsed(body).Accept(new Visitor(body, found));
        return found;
    }

    /// <summary>
    /// Every statement of the script, in script order, each written back by io's Sql160ScriptGenerator with its white space collapsed to
    /// one space; an IF, a BEGIN … END and a TRY … CATCH are listed and then each statement they hold, so a statement nested in a table
    /// rebuild's copy step is listed on its own.
    /// </summary>
    public static IReadOnlyList<(string Kind, string Text)> Statements(string script)
    {
        var found = new List<(string, string)>();
        foreach (var statement in ((TSqlScript)Parsed(Body(script))).Batches.SelectMany(b => b.Statements))
        {
            Collect(statement, found);
        }

        return found;
    }

    /// <summary>The script with LF line endings and each SQLCMD directive line left empty, so that line numbers hold.</summary>
    public static string Body(string script) =>
        string.Join('\n', LineEndings.Lf(script).Split('\n').Select(l => l.TrimStart().StartsWith(':') ? "" : l));

    private static TSqlFragment Parsed(string body)
    {
        var parsed = TSql.Parse(body, out var errors);
        Assert.True(errors.Count == 0,
            "TSql160Parser reports errors in the script: " + string.Join("; ", errors.Select(e => e.Line + ": " + e.Message)));
        return parsed;
    }

    private static void Collect(TSqlStatement statement, List<(string, string)> found)
    {
        found.Add((statement.GetType().Name, Regex.Replace(TSql.Text(statement), @"\s+", " ")));
        IEnumerable<TSqlStatement> held = statement switch
        {
            IfStatement { ElseStatement: { } otherwise } branch => [branch.ThenStatement, otherwise],
            IfStatement branch => [branch.ThenStatement],
            BeginEndBlockStatement block => block.StatementList.Statements,
            TryCatchStatement both => [.. both.TryStatements.Statements, .. both.CatchStatements.Statements],
            _ => [],
        };
        foreach (var inner in held)
        {
            Collect(inner, found);
        }
    }

    private sealed class Visitor(string body, List<(string, QueryExpression)> found) : TSqlFragmentVisitor
    {
        public override void ExplicitVisit(IfStatement node)
        {
            if (node.Predicate is ExistsPredicate exists
                && node.ThenStatement is RaiseErrorStatement { ThirdParameter: IntegerLiteral { Value: "127" } }
                && node.ElseStatement is null)
            {
                found.Add((body.Substring(node.StartOffset, node.FragmentLength), exists.Subquery.QueryExpression));
            }

            base.ExplicitVisit(node);
        }
    }
}
