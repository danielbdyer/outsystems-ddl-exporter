using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using DbChange.Kernel;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DbChange.Io;

/// <summary>
/// The statements of a deploy plan as kernel/Vocabulary reads them (WP 2.1's second slice): DacFx's script, its line ends LF, its SQLCMD
/// directive lines left empty and each $(var) substituted as io/BlockOnPossibleDataLoss substitutes it, read by TSql160Parser. Each
/// statement of each batch is listed, then each it holds: an IF's, a BEGIN … END's and a TRY … CATCH's, one deeper; a WHILE's body is not,
/// since the WHILE of DacFx's constraint verification runs its cursor's statements, sp_executesql among them, and is read whole. DacFx's
/// script holds the pre-deploy and the post-deploy script of the model the change brings verbatim (spike S2, on 162.5.57 and 170.5.96),
/// so each is found by its text, the pre-deploy's first place and the post-deploy's last, and a statement within either is that script's,
/// at its line there. The statements of the model before the change's deploy scripts are read alone, with the plan's SQLCMD values, so a
/// statement the change leaves as it was has the same text in both. A statement's text, the variables' values substituted, stays in
/// memory: no finding prints it.
/// </summary>
public static partial class PlanStatements
{
    /// <summary>A model's deploy scripts, as its package holds them: each null where the model has none.</summary>
    public sealed record Scripts(string? PreDeploy, string? PostDeploy);

    /// <summary>The plan's statements, and the statements of the deploy scripts of the model before the change.</summary>
    public sealed record Listing(SortedArray<PlanStatement> Plan, SortedArray<PlanStatement> ScriptsBefore);

    private static readonly Regex Space = new(@"\s+", RegexOptions.CultureInvariant);

    /// <summary>The statements of a plan against <paramref name="target"/>, its script substituted with the target's own SQLCMD
    /// values.</summary>
    public static Result<Listing> Read(Plan plan, SqlServer.Database target, Scripts after, Scripts before) =>
        SqlServer.SqlCmdValues(target).Bind(values => Read(plan.Script, values, after, before));

    /// <summary>
    /// The statements of <paramref name="script"/>, DacFx's script of a plan, its variables given by its :setvar lines and, over them,
    /// <paramref name="values"/>; and those of <paramref name="before"/>'s deploy scripts. A script TSql160Parser does not read is
    /// plan.unparsed, by line and column; a deploy script of <paramref name="after"/> that holds a statement and that the plan's script
    /// does not hold verbatim is plan.deploy-script; a variable with no value is sqlcmd.undefined.
    /// </summary>
    internal static Result<Listing> Read(string script, IReadOnlyList<SqlCmdValue> values, Scripts after, Scripts before)
    {
        var kept = LineEndings.Lf(script);
        var given = new Dictionary<string, string>(SqlCmdVariable.SetIn(kept), StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            given.Remove(value.Name.ToString());
            given[value.Name.ToString()] = value.Text;
        }

        return Regions(kept, after, given).Bind(regions => Parsed(kept, given, "the plan's script").Map(parsed =>
                Listed(parsed, (line, _) => regions.FirstOrDefault(r => r.First <= line && line <= r.Last) is { } region
                    ? (region.Part, line - region.First + 1 + region.Lead) : (ScriptPart.Plan, line), 0)))
            .Bind(plan => Alone(before.PreDeploy, ScriptPart.PreDeploy, given, 0).Bind(pre =>
                Alone(before.PostDeploy, ScriptPart.PostDeploy, given, pre.Count)
                    .Map(post => new Listing(SortedArray.Of(plan), SortedArray.Of(pre.Concat(post))))));
    }

    /// <summary>Where a deploy script stands in the plan's script: its part, its first and last line there, and the lines of white space
    /// its text begins with, which the plan's lines leave out.</summary>
    private sealed record Region(ScriptPart Part, int First, int Last, int Lead);

    private static Result<Region[]> Regions(string kept, Scripts after, IReadOnlyDictionary<string, string> given)
    {
        var regions = new List<Region>();
        foreach (var (part, text) in new[] { (ScriptPart.PreDeploy, after.PreDeploy), (ScriptPart.PostDeploy, after.PostDeploy) })
        {
            var lf = LineEndings.Lf(text ?? "");
            var trimmed = lf.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var at = part == ScriptPart.PreDeploy ? kept.IndexOf(trimmed, StringComparison.Ordinal)
                : kept.LastIndexOf(trimmed, StringComparison.Ordinal);
            if (at < 0)
            {
                if (Alone(text, part, given, 0) is Result<List<PlanStatement>>.Ok { Value.Count: 0 })
                {
                    continue;
                }

                var name = part == ScriptPart.PreDeploy ? "pre-deploy" : "post-deploy";
                return new Error("plan.deploy-script",
                    "The plan's script does not hold the " + name + " script of the model the change brings verbatim, so its statements"
                        + " cannot be told from DacFx's own; the script is not quoted.",
                    "Plan the change again from the package built from the project, whose " + name + " script DacFx writes into the plan.");
            }

            var first = kept.AsSpan(0, at).Count('\n') + 1;
            regions.Add(new Region(part, first, first + trimmed.AsSpan().Count('\n'),
                lf.AsSpan(0, lf.Length - lf.TrimStart().Length).Count('\n')));
        }

        return regions.ToArray();
    }

    /// <summary>A deploy script read alone, its lines its own; none for a script the model does not have.</summary>
    private static Result<List<PlanStatement>> Alone(string? script, ScriptPart part, IReadOnlyDictionary<string, string> given,
        int ordinal) =>
        script is null ? new List<PlanStatement>()
            : Parsed(LineEndings.Lf(script), given, "the " + (part == ScriptPart.PreDeploy ? "pre-deploy" : "post-deploy")
                + " script of the model before the change").Map(parsed => Listed(parsed, (line, _) => (part, line), ordinal));

    /// <summary>A script's text with its directives left empty and its variables substituted, as TSql160Parser reads it.</summary>
    private static Result<TSqlFragment> Parsed(string kept, IReadOnlyDictionary<string, string> given, string what) =>
        SqlCmdVariable.Substituted(SqlCmdVariable.WithoutDirectives(kept, keepLines: true), given).Bind(substituted =>
        {
            var parsed = TSql.Parse(substituted.Text, out var errors);
            return errors.Count > 0
                ? new Error("plan.unparsed", string.Create(CultureInfo.InvariantCulture,
                        $"{what[..1].ToUpperInvariant()}{what[1..]} does not parse with TSql160Parser at line {errors[0].Line}, column ")
                        + string.Create(CultureInfo.InvariantCulture, $"{errors[0].Column}, so its statements cannot be read; the script")
                        + " is not quoted.",
                    "Correct the statement at that line of the project's pre-deployment or post-deployment script.")
                : Result.Ok(parsed);
        });

    /// <summary>Each statement of each batch in script order, each followed by those it holds, at its part and line as
    /// <paramref name="place"/> gives them, numbered from <paramref name="ordinal"/>.</summary>
    private static List<PlanStatement> Listed(TSqlFragment parsed, Func<int, int, (ScriptPart Part, int Line)> place, int ordinal)
    {
        var listed = new List<PlanStatement>();
        void List(TSqlStatement statement, int depth)
        {
            var (part, line) = place(statement.StartLine, depth);
            var (type, text) = (statement.GetType().Name, Space.Replace(TSql.Text(statement), " "));
            foreach (var read in Of(statement))
            {
                listed.Add(new PlanStatement(ordinal + listed.Count, part, line, depth, type, text)
                {
                    Words = read.Words, Objects = read.Objects, Columns = read.Columns, Key = read.Key, Included = read.Included,
                });
            }

            foreach (var held in Held(statement))
            {
                List(held, depth + 1);
            }
        }

        foreach (var statement in ((TSqlScript)parsed).Batches.SelectMany(b => b.Statements))
        {
            List(statement, 0);
        }

        return listed;
    }

    /// <summary>The statements a statement holds: an IF's THEN and ELSE, a BEGIN … END's, a TRY … CATCH's; no other's.</summary>
    private static IEnumerable<TSqlStatement> Held(TSqlStatement statement) => statement switch
    {
        IfStatement i => i.ElseStatement is { } otherwise ? [i.ThenStatement, otherwise] : [i.ThenStatement],
        BeginEndBlockStatement b => b.StatementList?.Statements ?? [],
        TryCatchStatement t => [.. t.TryStatements?.Statements ?? [], .. t.CatchStatements?.Statements ?? []],
        _ => [],
    };
}
