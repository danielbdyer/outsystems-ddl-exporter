using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using DbChange.Kernel;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace DbChange.Io;

/// <summary>
/// The change's declared witnesses (V3_MILESTONES.md WP 3.5, step 5; the Facsimile's witness.sql): <see cref="Path"/>, committed on the
/// change's branch, read at the checkout's HEAD. The file is DML in blocks, each led by a header line: <c>-- plants: [dbo].[T]</c>, a
/// table whose rows the block plants whole, for which the planter plants none; or <c>-- witness: NotNull on [dbo].[T] ([C])</c>, a
/// precondition of the change as dbchange writes it, whose violating row the block plants in the planter's place. Parsed here, before
/// anything runs: a statement before the first header is witness.outside-block; a block TSql160Parser cannot read is witness.unparsed, by
/// line and column; a block of more than one batch is witness.batch, since a block runs as one batch, so SET IDENTITY_INSERT holds for
/// its INSERTs; a statement other than INSERT, UPDATE, DELETE, MERGE and SET IDENTITY_INSERT, or an INSERT of EXEC's rows, or a read of
/// another server through OPENROWSET, OPENQUERY or OPENDATASOURCE, is witness.not-dml, since a declared witness changes no schema and
/// carries nothing in; and a string or binary literal that is neither synthetic nor defined by the repository's model is
/// witness.not-synthetic (<see cref="Synthetic"/>). Each names its line and quotes no literal.
/// </summary>
public static class Witnesses
{
    /// <summary>Where the change's declared witnesses are committed, from the repository's root.</summary>
    public const string Path = "dbchange/witnesses.sql";

    private static readonly Regex HeaderLine = new(@"^\s*--\s*(?<kind>plants|witness):\s*(?<what>.*?)\s*$", RegexOptions.CultureInvariant);

    private static readonly Regex Quoted = new(@"N?'((?:[^']|'')*)'", RegexOptions.CultureInvariant);

    private static readonly Regex Binary = new(@"\b0[xX][0-9A-Fa-f]*", RegexOptions.CultureInvariant);

    /// <summary>A string written as a number, or as a date or a time in an ISO form: it carries no mark, as a number does not (PLT-124).
    /// </summary>
    private static readonly Regex Unmarkable = new(
        @"\A(?:[+-]?[0-9]+(?:\.[0-9]+)?|[0-9]{4}-[0-9]{2}-[0-9]{2}(?:[T ][0-9]{2}:[0-9]{2}(?::[0-9]{2}(?:\.[0-9]{1,7})?)?)?"
        + @"(?:Z|[+-][0-9]{2}:[0-9]{2})?|[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?"
        + @"|(?:77777777-0000-4000-8000|FFFFFFFF-FFFF-4FFF-BFFF)-[0-9A-Fa-f]{12})\z", RegexOptions.CultureInvariant);

    /// <summary>
    /// A block of the file: the line of its header; the table it plants whole, or the precondition it witnesses as the header writes it;
    /// its statements, as the file holds them from the line after the header; and that line.
    /// </summary>
    public sealed record Block(int Line, Name? Plants, string? Witness, string Text, int FirstLine);

    /// <summary>The literals a model defines: each string literal in a script or a text of its elements (a seed's MERGE, a default's,
    /// a check's), its quotes undone, and each binary literal, its digits in upper case.</summary>
    public static IReadOnlySet<string> Literals(IEnumerable<Element> model)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in model.SelectMany(e => e.Properties).Select(p => p.Value)
            .Select(v => v switch { Value.Script s => s.Content, Value.Text t => t.Content, _ => null }).OfType<string>())
        {
            found.UnionWith(Quoted.Matches(text).Select(m => m.Groups[1].Value.Replace("''", "'", StringComparison.Ordinal)));
            found.UnionWith(Binary.Matches(text).Select(m => Hex(m.Value)));
        }

        return found;
    }

    /// <summary>The file's blocks, each checked as the class says against the literals the repository's model defines.</summary>
    public static Result<IReadOnlyList<Block>> Parse(string text, IReadOnlySet<string> repository)
    {
        var lines = LineEndings.Lf(text).Split('\n');
        var blocks = new List<Block>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (HeaderLine.Match(lines[i]) is not { Success: true } header)
            {
                continue;
            }

            var end = Array.FindIndex(lines, i + 1, l => HeaderLine.IsMatch(l)) is var next and >= 0 ? next : lines.Length;
            var what = header.Groups["what"].Value;
            Name? plants = null;
            if (header.Groups["kind"].Value == "plants")
            {
                if (TSql.NameParts(what) is not { Length: 1 or 2 } parts
                    || (parts.Length == 1 ? Name.Of(parts[0]) : Name.Of(parts[0], parts[1])) is not Result<Name>.Ok(var table))
                {
                    return new Error("witness.header", At(i + 1, "names no table as QUOTENAME writes one, such as [dbo].[Customer]"),
                        "Write the table the block plants after -- plants:, in two parts, such as [dbo].[Customer].");
                }

                plants = table;
            }
            else if (what.Length == 0)
            {
                return new Error("witness.header", At(i + 1, "names no precondition after -- witness:"),
                    "Write the precondition the block witnesses as dbchange names it, such as NotNull on [dbo].[Customer] ([Email]).");
            }

            blocks.Add(new Block(i + 1, plants, plants is null ? what : null, string.Join('\n', lines[(i + 1)..end]), i + 2));
        }

        var first = blocks.Count == 0 ? lines.Length : blocks[0].Line - 1;
        var outside = TSql.Parse(string.Join('\n', lines[..first]), out _) is TSqlScript script
            ? script.Batches.SelectMany(b => b.Statements).FirstOrDefault() : null;
        return outside is not null
            ? new Error("witness.outside-block", At(outside.StartLine, "holds a statement before any -- plants: or -- witness: header"),
                "Write each statement under a header line, -- plants: <table> or -- witness: <precondition>.")
            : Result.All(blocks.Select(b => Checked(b, repository))).Map(checkedBlocks => (IReadOnlyList<Block>)checkedBlocks);
    }

    /// <summary>
    /// Whether a declared literal may enter a copy (VALUES.md P1; law 12′; PLT-124, PLT-84): a string that is empty, that begins with
    /// the planter's mark w, that is a literal the repository's model defines, or one of those, its LIKE wildcards dropped, followed by
    /// the mark, as N'ABCw1' under a check of LIKE 'ABC%'; a string written as a number, an ISO date or time, or one of the planter's
    /// two uniqueidentifier forms, which carry no mark as a number carries none; a binary value that is empty, begins with 0x77, the
    /// mark's byte, or is one the model defines. Any other value can have come from an environment, and a committed file would carry it.
    /// </summary>
    internal static bool Synthetic(Microsoft.SqlServer.TransactSql.ScriptDom.Literal literal, IReadOnlySet<string> repository) =>
        literal switch
        {
            StringLiteral s => s.Value.Length == 0 || s.Value.StartsWith('w') || repository.Contains(s.Value) || Unmarkable.IsMatch(s.Value)
                || repository.Select(r => r.Replace("%", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal))
                    .Any(r => r.Length > 0 && s.Value.StartsWith(r + "w", StringComparison.Ordinal)),
            BinaryLiteral b => b.Value.Length <= 2 || b.Value.StartsWith("0x77", StringComparison.OrdinalIgnoreCase)
                || repository.Contains(Hex(b.Value)),
            _ => true,
        };

    /// <summary>A block parsed and checked: one batch, every statement DML, every literal synthetic or the repository's.</summary>
    private static Result<Block> Checked(Block block, IReadOnlySet<string> repository)
    {
        var parsed = TSql.Parse(block.Text, out var errors);
        if (errors.Count > 0)
        {
            return new Error("witness.unparsed", At(block.FirstLine + errors[0].Line - 1,
                    string.Create(CultureInfo.InvariantCulture, $"does not parse with TSql160Parser at column {errors[0].Column}")),
                "Correct the statement at that line of " + Path + ".");
        }

        var batches = ((TSqlScript)parsed).Batches;
        if (batches.Count > 1)
        {
            return new Error("witness.batch", At(block.FirstLine + batches[1].StartLine - 1,
                    "begins a second batch in the block of line " + Number(block.Line) + "; a block runs as one batch"),
                "Remove the GO at that line, so the block's statements run as one batch.");
        }

        foreach (var statement in batches.SelectMany(b => b.Statements))
        {
            var line = block.FirstLine + statement.StartLine - 1;
            if (Unwritable(statement) is { } what)
            {
                return new Error("witness.not-dml", At(line, "holds " + what + ", and a declared witness is DML only: INSERT, UPDATE, "
                        + "DELETE, MERGE and SET IDENTITY_INSERT, reading the copy alone"),
                    "Remove the statement at that line; a declared witness changes no schema and reads no other server.");
            }

            var literals = new LiteralsFound();
            statement.Accept(literals);
            if (literals.Found.FirstOrDefault(l => !Synthetic(l, repository)) is { } carried)
            {
                return new Error("witness.not-synthetic", At(block.FirstLine + carried.StartLine - 1,
                        "carries a literal neither marked synthetic nor defined by the repository's model; the literal is not quoted"),
                    "Write a value that begins with w, the planter's mark, or one the repository's model defines, at that line.");
            }
        }

        return block;
    }

    /// <summary>What a statement holds that no declared witness may: a statement other than DML, an INSERT of EXEC's rows, or a read
    /// through OPENROWSET, OPENQUERY or OPENDATASOURCE; null for a DML statement that reads the copy alone.</summary>
    private static string? Unwritable(TSqlStatement statement)
    {
        if (statement is not (InsertStatement or UpdateStatement or DeleteStatement or MergeStatement or SetIdentityInsertStatement))
        {
            return "a statement of the kind " + statement.GetType().Name.Replace("Statement", "", StringComparison.Ordinal);
        }

        var outside = new Outside();
        statement.Accept(outside);
        return outside.Found;
    }

    /// <summary>witness.header: a block's header naming what the copy or the change does not hold.</summary>
    internal static Error Header(int line, string what, string remedy) => new("witness.header", At(line, what), remedy);

    /// <summary>
    /// witness.failed: a statement of a block SQL Server refused, by the line of the file it stands at (<paramref name="line"/>, the line
    /// SQL Server gives, counted from the block's first) and SQL Server's number, with the constraint or index it names; SQL Server's text
    /// is not quoted, though the copy's rows are synthetic, since the remedy needs the number and the object alone.
    /// </summary>
    internal static Error Failed(Block block, int line, int number, string? message) =>
        new("witness.failed", At(block.FirstLine + Math.Max(line, 1) - 1, "failed with Msg " + Number(number)
                + (message is not null && Named.Match(message) is { Success: true } named ? " on " + named.Groups["name"].Value : "")),
            "Correct the row at that line of " + Path + " so SQL Server takes it, then run the verb again.");

    /// <summary>The constraint or index SQL Server names in a refusal: CHECK constraint "CK_T", PRIMARY KEY constraint 'PK_T', unique
    /// index 'UX_T'.</summary>
    private static readonly Regex Named = new(@"(?:constraint|index) [""'](?<name>[^""']{1,128})[""']", RegexOptions.CultureInvariant);

    /// <summary>A binary literal as the model's literals are kept: 0x, then its digits in upper case.</summary>
    private static string Hex(string literal) => "0x" + literal[2..].ToUpperInvariant();

    /// <summary>A message naming the file's line, then what is wrong there.</summary>
    private static string At(int line, string what) => Path + " line " + Number(line) + " " + what + ".";

    private static string Number(int n) => n.ToString(CultureInfo.InvariantCulture);

    private sealed class LiteralsFound : TSqlFragmentVisitor
    {
        public List<Microsoft.SqlServer.TransactSql.ScriptDom.Literal> Found { get; } = [];

        public override void Visit(Microsoft.SqlServer.TransactSql.ScriptDom.Literal node) => Found.Add(node);
    }

    private sealed class Outside : TSqlFragmentVisitor
    {
        public string? Found { get; private set; }

        public override void Visit(ExecuteInsertSource node) => Found ??= "an INSERT of the rows an EXEC returns";

        public override void Visit(OpenRowsetTableReference node) => Found ??= "a read through OPENROWSET";

        public override void Visit(BulkOpenRowset node) => Found ??= "a read through OPENROWSET";

        public override void Visit(OpenQueryTableReference node) => Found ??= "a read through OPENQUERY";

        public override void Visit(AdHocTableReference node) => Found ??= "a read through OPENDATASOURCE";
    }
}
