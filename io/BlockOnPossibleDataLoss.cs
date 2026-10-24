using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DbChange.Kernel;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DbChange.Io;

/// <summary>
/// The BlockOnPossibleDataLoss checks of a plan, read from DacFx's own script and run read-only (WP 2.2, V3_MILESTONES.md §8 and §1 fact
/// 3). The script's line ends become LF and its SQLCMD directive lines are left empty, so a line here is the script's line; each $(var)
/// is substituted from the script's own :setvar lines and the environment's SQLCMD values, in memory, the substituted text never written,
/// logged or put in an error (WP 1.5); and TSql160Parser reads the rest. A check is an IF with no ELSE whose predicate is EXISTS (…) and
/// whose body is one RAISERROR at state 127, matched on the tree and never on text: spike S2 measured, on DacFx 162.5.57 and 170.5.96,
/// one such check per table, and three lookalikes no tree match takes, a table rebuild's copy step IF EXISTS (SELECT TOP 1 1 FROM [T])
/// BEGIN … INSERT … END, the verification of a constraint added WITH NOCHECK, RAISERROR (…, 16, 127) under IF @@ROWCOUNT &gt; 0,
/// and the check's text quoted in the pre-deploy script's block comment. A check's sites are the paragraphs of the block comment DacFx
/// writes just before it, one for each statement the check protects. Its EXISTS is written back by io's Sql160ScriptGenerator and runs
/// as SELECT CASE WHEN EXISTS (&lt;predicate&gt;) THEN 1 ELSE 0 END, one of the allowlist's forms, through SqlServer.Measure.
/// </summary>
public static class BlockOnPossibleDataLoss
{
    /// <summary>The state DacFx raises a BlockOnPossibleDataLoss check at (§1 fact 3).</summary>
    public const string State = "127";

    /// <summary>
    /// One check: the table its EXISTS reads, the check as DacFx wrote it (LF line ends), its first line in the plan's script, the sites
    /// its block comment names, and its predicate as the aggregate query that runs it. None of these holds a value marked sensitive.
    /// </summary>
    public sealed record Check(Name Table, string Text, int Line, SortedArray<BlockOnPossibleDataLossSite> Sites,
        SqlServer.AggregateQuery Query)
    {
        public override string ToString() =>
            "BlockOnPossibleDataLoss on " + Table + " at line " + Line.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The checks of a plan against <paramref name="target"/>, its script substituted with the target's own SQLCMD
    /// values.</summary>
    public static Result<IReadOnlyList<Check>> Read(Plan plan, SqlServer.Database target) =>
        SqlServer.SqlCmdValues(target).Bind(values => Read(plan.Script, values));

    /// <summary>
    /// The checks of <paramref name="script"/>, in script order. Its variables take the values of its :setvar lines, and over them
    /// <paramref name="values"/>; a variable with none is sqlcmd.undefined. A script TSql160Parser does not read is plan.unparsed, by line
    /// and column; a check whose predicate reads no one table is plan.check-table; a check that reads a value a reference gave, which its
    /// query would write to the run's queries.log, is sqlcmd.sensitive, naming the variable; and a predicate the allowlist refuses is
    /// aggregate-query.refused, by the check's line. None of these errors quotes the script.
    /// </summary>
    internal static Result<IReadOnlyList<Check>> Read(string script, IReadOnlyList<SqlCmdValue> values)
    {
        var kept = LineEndings.Lf(script);
        var given = new Dictionary<string, string>(SqlCmdVariable.SetIn(kept), StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            given.Remove(value.Name.ToString());
            given[value.Name.ToString()] = value.Text;
        }

        var sensitive = values.Where(v => v.Referenced).Select(v => v.Name).ToHashSet();
        return SqlCmdVariable.Substituted(SqlCmdVariable.WithoutDirectives(kept, keepLines: true), given).Bind(substituted =>
        {
            var parsed = TSql.Parse(substituted.Text, out var errors);
            if (errors.Count > 0)
            {
                return new Error("plan.unparsed", string.Create(CultureInfo.InvariantCulture,
                        $"The plan's script does not parse with TSql160Parser at line {errors[0].Line}, column {errors[0].Column}, ")
                        + "so its BlockOnPossibleDataLoss checks cannot be read; the script is not quoted.",
                    "Correct the statement at that line of the project's pre-deployment or post-deployment script.");
            }

            var found = new Matches();
            parsed.Accept(found);
            return Result.All(found.Checks.Select(c => Checked(c.Node, c.Exists, parsed.ScriptTokenStream, substituted, sensitive)))
                .Map(checks => (IReadOnlyList<Check>)checks);
        });
    }

    /// <summary>
    /// The sites of the checks, for Preconditions.Of (WP 2.1a), which reads a TableEmpty on each table; a check DacFx wrote with no block
    /// comment before it gives one site with an empty statement, so its table still counts.
    /// </summary>
    public static SortedArray<BlockOnPossibleDataLossSite> Sites(IEnumerable<Check> checks) =>
        SortedArray.Of(checks.SelectMany(c => c.Sites));

    /// <summary>
    /// The check's predicate run against <paramref name="target"/> through SqlServer.Measure: 1 when its table holds a row and 0 when it
    /// holds none. A failed query against a named environment reports its number and the check's site alone.
    /// </summary>
    public static Result<SqlServer.Measurement> Measure(SqlServer.Database target, Check check, SqlServer.QueryLog log) =>
        SqlServer.Measure(target, check.Query, log);

    private static Result<Check> Checked(IfStatement node, ExistsPredicate exists, IList<TSqlParserToken> tokens,
        SqlCmdSubstitution substituted, HashSet<SqlCmdName> sensitive)
    {
        var comment = Comment(node, tokens);
        var (start, line) = (comment?.Offset ?? node.StartOffset, node.StartLine.ToString(CultureInfo.InvariantCulture));
        var at = "The BlockOnPossibleDataLoss check at line " + line + " of the plan's script";
        if (substituted.Placed.FirstOrDefault(p => sensitive.Contains(p.Name)
            && p.Overlaps(start, node.StartOffset + node.FragmentLength - start)) is { } placed)
        {
            var variable = SqlCmdVariable.Placeholder(placed.Name.ToString());
            return new Error("sqlcmd.sensitive",
                at + " reads " + variable + ", a SQLCMD value a reference gives, which is marked sensitive; its query would write the "
                + "value to the run's queries.log.",
                "Give " + variable + " as a literal marked non-sensitive in the environment's sqlcmd in " + EnvironmentsFile.Json
                    + ", or predict the change on a copy.");
        }

        var table = TSql.TableReferences(exists.Subquery) is [{ SchemaObject: var one }] ? one : null;
        // written from the whole EXISTS: ScriptDom 180.102.0 writes the subquery's QuerySpecification alone without its WHERE clause
        var predicate = "SELECT CASE WHEN " + TSql.Text(exists) + " THEN 1 ELSE 0 END";
        return SqlServer.AggregateQuery.Of(predicate, "BlockOnPossibleDataLoss "
                + (table is null ? "at line " + line : "on " + string.Join('.', table.Identifiers.Select(Quoted))))
            .Match(Result.Ok, refused => new Error(refused.Code,
                at + " reads a predicate the allowlist refuses, so the site cannot be predicted read-only: " + refused.Message,
                refused.Remedy))
            .Bind(query => table is null
                ? new Error("plan.check-table",
                    at + " reads other than one table, and a check reads the table whose rows stop the publish.",
                    "Report the DacFx release and the check's line to dbchange's maintainers.")
                : (table.SchemaIdentifier is { } schema ? Name.Of(schema.Value, table.BaseIdentifier.Value)
                    : Name.Of(table.BaseIdentifier.Value))
                .Map(name => new Check(name, substituted.Text.Substring(node.StartOffset, node.FragmentLength), node.StartLine,
                    SortedArray.Of((comment?.Paragraphs ?? [""]).Select(p => new BlockOnPossibleDataLossSite(name, p))), query)));
    }

    /// <summary>A name part as QUOTENAME writes it, as kernel/Name prints one.</summary>
    private static string Quoted(Identifier part) => "[" + part.Value.Replace("]", "]]", StringComparison.Ordinal) + "]";

    /// <summary>
    /// The block comment just before a check, past white space alone: where it starts, and its paragraphs, each trimmed and its lines
    /// joined by LF; null when the token before the check is anything else.
    /// </summary>
    private static (int Offset, string[] Paragraphs)? Comment(IfStatement node, IList<TSqlParserToken> tokens)
    {
        var i = node.FirstTokenIndex - 1;
        while (i >= 0 && tokens[i].TokenType == TSqlTokenType.WhiteSpace)
        {
            i--;
        }

        if (i < 0 || tokens[i].TokenType != TSqlTokenType.MultilineComment)
        {
            return null;
        }

        var text = tokens[i].Text;
        var body = text.StartsWith("/*", StringComparison.Ordinal) && text.EndsWith("*/", StringComparison.Ordinal) ? text[2..^2] : text;
        return (tokens[i].Offset, [.. body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.Length > 0)]);
    }

    /// <summary>Each IF that is a BlockOnPossibleDataLoss check, in script order, wherever it stands in the tree.</summary>
    private sealed class Matches : TSqlFragmentVisitor
    {
        public List<(IfStatement Node, ExistsPredicate Exists)> Checks { get; } = [];

        public override void ExplicitVisit(IfStatement node)
        {
            if (node is { Predicate: ExistsPredicate exists, ElseStatement: null }
                && node.ThenStatement is RaiseErrorStatement { ThirdParameter: IntegerLiteral { Value: State } })
            {
                Checks.Add((node, exists));
            }

            base.ExplicitVisit(node);
        }
    }
}
