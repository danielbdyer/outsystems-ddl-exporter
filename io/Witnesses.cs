using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DbChange.Kernel;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using ScriptLiteral = Microsoft.SqlServer.TransactSql.ScriptDom.Literal;

namespace DbChange.Io;

/// <summary>
/// The change's declared witnesses (V3_MILESTONES.md WP 3.5, step 5; the Facsimile's witness.sql): <see cref="Path"/>, committed on the
/// change's branch, read at the checkout's HEAD. The file is DML in blocks, each led by a header line: <c>-- plants: [dbo].[T]</c>, a
/// table whose rows the block plants whole, for which the planter plants none; or <c>-- witness: NotNull on [dbo].[T] ([C])</c>, a
/// precondition of the change as dbchange writes it, whose violating row the block plants in the planter's place. Parsed here, before
/// anything runs: a statement before the first header is witness.outside-block; a block TSql160Parser cannot read is witness.unparsed, by
/// line and column; a block of more than one batch is witness.batch, since a block runs as one batch, so SET IDENTITY_INSERT holds for
/// its INSERTs; a statement other than INSERT, UPDATE, DELETE, MERGE and SET IDENTITY_INSERT, an INSERT of EXEC's rows, a read of another
/// server through OPENROWSET, OPENQUERY or OPENDATASOURCE, a name in three or four parts, of a table, a sequence or a function in
/// another database or on another server, or a synonym of the repository, which can stand for either, is witness.not-dml, since a
/// declared witness changes no schema, reads and writes the copy alone and carries nothing in; and a string or binary literal that is
/// neither synthetic nor defined by the repository's model (<see cref="Synthetic"/>), or a number SQL Server makes text or bytes of
/// whose digits the model does not define (Witnesses.Numbers.cs), is witness.not-synthetic. Each names its line and quotes no literal.
/// </summary>
public static partial class Witnesses
{
    /// <summary>Where the change's declared witnesses are committed, from the repository's root.</summary>
    public const string Path = "dbchange/witnesses.sql";

    private static readonly Regex HeaderLine = new(@"^\s*--\s*(?<kind>plants|witness):\s*(?<what>.*?)\s*$", RegexOptions.CultureInvariant);

    private static readonly Regex Quoted = new(@"N?'((?:[^']|'')*)'", RegexOptions.CultureInvariant);

    private static readonly Regex Binary = new(@"\b0[xX][0-9A-Fa-f]*", RegexOptions.CultureInvariant);

    /// <summary>A script that names a LIKE or a synonym, which <see cref="Repository.Of"/> reads with ScriptDom.</summary>
    private static readonly Regex Named = new(@"\b(?:LIKE|SYNONYM)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>A string written as a date or a time in an ISO form, which T-SQL writes only as a string and which carries no mark
    /// (PLT-124), or as one of the planter's two uniqueidentifier forms.</summary>
    private static readonly Regex Unmarkable = new(
        @"\A(?:[0-9]{4}-[0-9]{2}-[0-9]{2}(?:[T ][0-9]{2}:[0-9]{2}(?::[0-9]{2}(?:\.[0-9]{1,7})?)?)?"
        + @"(?:Z|[+-][0-9]{2}:[0-9]{2})?|[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?"
        + @"|(?:77777777-0000-4000-8000|FFFFFFFF-FFFF-4FFF-BFFF)-[0-9A-Fa-f]{12})\z", RegexOptions.CultureInvariant);

    /// <summary>
    /// A block of the file: the line of its header; the table it plants whole, or the precondition it witnesses as the header writes it;
    /// its statements, as the file holds them from the line after the header; and that line.
    /// </summary>
    public sealed record Block(int Line, Name? Plants, string? Witness, string Text, int FirstLine);

    /// <summary>
    /// What the repository holds the declared witnesses to: each string literal its models define, its quotes undone, and each binary
    /// literal, its digits in upper case; the text each LIKE pattern of the models holds before its first wildcard; each synonym the
    /// models define or a script of theirs creates; and the copy's tables, the base's, whose columns' types say where SQL Server makes
    /// text or bytes of a number.
    /// </summary>
    public sealed record Repository(IReadOnlySet<string> Literals, IReadOnlySet<string> Prefixes, IReadOnlyList<Name> Synonyms,
        Schema Copy)
    {
        /// <summary>
        /// What <paramref name="models"/>, the elements of the base and of the change, define, with the copy's tables: the literals of each
        /// script and text of the elements (a seed's MERGE, a default's, a check's); the LIKE patterns and the CREATE SYNONYM statements of
        /// each script that names either and that TSql160Parser reads as a predicate, an expression or a batch (a check's, a module's, a
        /// deploy script's), a script it reads as none giving neither; and each Synonym element.
        /// </summary>
        public static Repository Of(IEnumerable<Element> models, Schema copy)
        {
            var (literals, found) = (new HashSet<string>(StringComparer.Ordinal), new Defined());
            foreach (var element in models)
            {
                found.Synonyms.AddRange(element.Key.Type == "Synonym" ? [element.Key.Name] : []);
                foreach (var value in element.Properties.Select(p => p.Value).Where(v => v is Value.Script or Value.Text))
                {
                    var text = value is Value.Script script ? script.Content : ((Value.Text)value).Content;
                    literals.UnionWith(Quoted.Matches(text).Select(m => m.Groups[1].Value.Replace("''", "'", StringComparison.Ordinal)));
                    literals.UnionWith(Binary.Matches(text).Select(m => Hex(m.Value)));
                    if (value is Value.Script && Named.IsMatch(text))
                    {
                        Tree(text)?.Accept(found);
                    }
                }
            }

            return new Repository(literals, found.Prefixes, found.Synonyms, copy);
        }

        /// <summary>A script as TSql160Parser reads it with no error: a check's predicate, a default's expression, or a batch; null where
        /// it reads as none of the three.</summary>
        private static TSqlFragment? Tree(string text) => (TSqlFragment?)TSql.Predicate(text) ?? TSql.Scalar(text)
            ?? (TSql.Parse(text, out var errors) is var batch && errors.Count == 0 ? batch : null);
    }

    /// <summary>The file's blocks, each checked as the class says against what the repository defines.</summary>
    public static Result<IReadOnlyList<Block>> Parse(string text, Repository repository)
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
    /// Whether a declared string or binary literal may enter a copy (VALUES.md P1; law 12′; PLT-124, PLT-84): a string that is empty,
    /// that begins with the planter's mark w, that is a literal the repository's model defines, or that begins with the text a LIKE
    /// pattern of the model holds before its first wildcard and then the mark, as N'ABCw1' under a check of LIKE 'ABC%'; a string written
    /// as an ISO date or time, or as one of the planter's two uniqueidentifier forms, which carry no mark; a binary value that is empty,
    /// begins with 0x77, the mark's byte, or is one the model defines. Any other value can have come from an environment, and a committed
    /// file would carry it. A number carries no mark (PLT-124) and passes here; where SQL Server makes text or bytes of it,
    /// Witnesses.Numbers.cs holds its digits to the model's, so a string of digits such as a phone or a card number, written in quotes
    /// or written without them as a character column's value, enters only where the model defines it.
    /// </summary>
    internal static bool Synthetic(ScriptLiteral literal, Repository repository) =>
        literal switch
        {
            StringLiteral s => s.Value.Length == 0 || s.Value.StartsWith('w') || repository.Literals.Contains(s.Value)
                || Unmarkable.IsMatch(s.Value) || repository.Prefixes.Any(p => s.Value.StartsWith(p + "w", StringComparison.Ordinal)),
            BinaryLiteral b => b.Value.Length <= 2 || b.Value.StartsWith("0x77", StringComparison.OrdinalIgnoreCase)
                || repository.Literals.Contains(Hex(b.Value)),
            _ => true,
        };

    /// <summary>A block parsed and checked: one batch, every statement DML, every literal synthetic or the repository's.</summary>
    private static Result<Block> Checked(Block block, Repository repository)
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
            if (Unwritable(statement, repository.Synonyms) is { } what)
            {
                return new Error("witness.not-dml", At(line, "holds " + what + ", and a declared witness is DML only: INSERT, UPDATE, "
                        + "DELETE, MERGE and SET IDENTITY_INSERT, reading and writing the copy alone"),
                    "Remove the statement at that line, or name the copy's own table or function, in one or two parts and not through a "
                        + "synonym; a declared witness changes no schema and reads no other database or server.");
            }

            var literals = new LiteralsFound();
            statement.Accept(literals);
            if (literals.Found.FirstOrDefault(l => !Synthetic(l, repository)) is { } carried)
            {
                return new Error("witness.not-synthetic", At(block.FirstLine + carried.StartLine - 1,
                        "carries a literal neither marked synthetic nor defined by the repository's model; the literal is not quoted"),
                    "Write a value that begins with w, the planter's mark, or one the repository's model defines, at that line.");
            }

            if (NumberAsText(statement, repository) is { } number)
            {
                return new Error("witness.not-synthetic", At(block.FirstLine + number.StartLine - 1,
                        "carries a number SQL Server makes text or bytes of, as a character or binary column's value or a conversion's "
                            + "operand, whose digits the repository's model does not define; the literal is not quoted"),
                    "Write the value as text that begins with w, the planter's mark, or as one the repository's model defines, at that "
                        + "line.");
            }
        }

        return block;
    }

    /// <summary>What a statement holds that no declared witness may: a statement other than DML, an INSERT of EXEC's rows, a read through
    /// OPENROWSET, OPENQUERY or OPENDATASOURCE, a name in three or four parts, or a synonym the repository defines; null for a DML
    /// statement that reads and writes the copy alone.</summary>
    private static string? Unwritable(TSqlStatement statement, IReadOnlyList<Name> synonyms)
    {
        if (statement is not (InsertStatement or UpdateStatement or DeleteStatement or MergeStatement or SetIdentityInsertStatement))
        {
            return "a statement of the kind " + statement.GetType().Name.Replace("Statement", "", StringComparison.Ordinal);
        }

        var outside = new Outside(synonyms);
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
                + (message is not null && Constraint.Match(message) is { Success: true } named ? " on " + named.Groups["name"].Value : "")),
            "Correct the row at that line of " + Path + " so SQL Server takes it, then run the verb again.");

    /// <summary>The constraint or index SQL Server names in a refusal: CHECK constraint "CK_T", PRIMARY KEY constraint 'PK_T', unique
    /// index 'UX_T'.</summary>
    private static readonly Regex Constraint = new(@"(?:constraint|index) [""'](?<name>[^""']{1,128})[""']", RegexOptions.CultureInvariant);

    /// <summary>A binary literal as the model's literals are kept: 0x, then its digits in upper case.</summary>
    private static string Hex(string literal) => "0x" + literal[2..].ToUpperInvariant();

    /// <summary>
    /// A LIKE pattern's text before its first wildcard (%, _ or [), a character its ESCAPE character escapes taken as written; null for a
    /// pattern with no wildcard, whose text the model's literals hold whole.
    /// </summary>
    private static string? Prefix(string pattern, char? escape)
    {
        var prefix = new StringBuilder();
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == escape && i + 1 < pattern.Length)
            {
                prefix.Append(pattern[++i]);
            }
            else if (pattern[i] is '%' or '_' or '[')
            {
                return prefix.ToString();
            }
            else
            {
                prefix.Append(pattern[i]);
            }
        }

        return null;
    }

    /// <summary>A message naming the file's line, then what is wrong there.</summary>
    private static string At(int line, string what) => Path + " line " + Number(line) + " " + what + ".";

    private static string Number(int n) => n.ToString(CultureInfo.InvariantCulture);

    private sealed class LiteralsFound : TSqlFragmentVisitor
    {
        public List<ScriptLiteral> Found { get; } = [];

        public override void Visit(ScriptLiteral node) => Found.Add(node);
    }

    /// <summary>
    /// What a model's script defines beside its literals: the text before the first wildcard of each LIKE pattern written as a string,
    /// and each synonym a CREATE SYNONYM names, in dbo where it names no schema.
    /// </summary>
    private sealed class Defined : TSqlFragmentVisitor
    {
        public HashSet<string> Prefixes { get; } = new(StringComparer.Ordinal);

        public List<Name> Synonyms { get; } = [];

        public override void Visit(LikePredicate node)
        {
            var escape = node.EscapeExpression is StringLiteral { Value: [var single] } ? single : (char?)null;
            if (node.SecondExpression is StringLiteral { Value: var pattern } && Prefix(pattern, escape) is { Length: > 0 } prefix)
            {
                Prefixes.Add(prefix);
            }
        }

        public override void Visit(CreateSynonymStatement node)
        {
            if (Name.Of(node.Name.SchemaIdentifier?.Value ?? "dbo", node.Name.BaseIdentifier.Value) is Result<Name>.Ok(var synonym))
            {
                Synonyms.Add(synonym);
            }
        }
    }

    /// <summary>
    /// The first object a statement reaches outside the copy. A table, a sequence or a type is a SchemaObjectName, its server and its
    /// database parts named where it has four or three (WP 1.4's rule for Measure: names have one or two parts); a function is called
    /// through a call target, whose two parts before the function's name are a database and a schema. The block runs as one batch on the
    /// copy's connection, so a four-part name reaches whatever linked server the local server holds, a named environment's included, and a
    /// three-part name another database on the local server. A synonym of the repository, a table's or a function's in one or two parts,
    /// can stand for either, which the block's text cannot show (DECISIONS.md, 2026-09-25, refuses one to Measure alike); its name is
    /// compared ignoring case, as a case-insensitive database resolves it, and one named in one part matches a synonym of any schema.
    /// </summary>
    private sealed class Outside(IReadOnlyList<Name> synonyms) : TSqlFragmentVisitor
    {
        public string? Found { get; private set; }

        public override void Visit(SchemaObjectName node) => Found ??= node.ServerIdentifier is not null ? "a name on another server"
            : node.DatabaseIdentifier is not null ? "a name in another database"
            : Synonym(node.SchemaIdentifier?.Value, node.BaseIdentifier?.Value);

        public override void Visit(FunctionCall node) => Found ??= node.CallTarget switch
        {
            MultiPartIdentifierCallTarget { MultiPartIdentifier.Count: >= 2 } => "a function in another database",
            MultiPartIdentifierCallTarget { MultiPartIdentifier.Identifiers: [var schema] } =>
                Synonym(schema.Value, node.FunctionName.Value),
            _ => null,
        };

        public override void Visit(ExecuteInsertSource node) => Found ??= "an INSERT of the rows an EXEC returns";

        public override void Visit(OpenRowsetTableReference node) => Found ??= "a read through OPENROWSET";

        public override void Visit(BulkOpenRowset node) => Found ??= "a read through OPENROWSET";

        public override void Visit(OpenQueryTableReference node) => Found ??= "a read through OPENQUERY";

        public override void Visit(AdHocTableReference node) => Found ??= "a read through OPENDATASOURCE";

        private string? Synonym(string? schema, string? name) => synonyms.Where(s => Same(s.Base, name) && (schema is null
                || Same(s.Schema, schema))).Select(s => (Name?)s).FirstOrDefault() is { } synonym
            ? "the synonym " + synonym + ", which can stand for a table or a function in another database or on a linked server" : null;
    }
}
