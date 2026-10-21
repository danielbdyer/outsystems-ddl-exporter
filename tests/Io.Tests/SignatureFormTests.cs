using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using DbChange.Kernel;
using DbChange.Tests;
using Xunit;
using static DbChange.Tests.Expect;

namespace DbChange.Io.Tests;

/// <summary>
/// The one form of the allowlist that answers text (V3_MILESTONES.md WP 3.7, §17 item 24; VALUES.md P2): a column's signatures with
/// their counts, the rare ones folded, admitted only as io/SqlServer.Signatures.cs commits it for the table and the column it reads. The
/// negative cases are the committed form with one part changed: the fold gone or loosened, a value or a part of one returned, a class map
/// other than the committed one, a column read without TRANSLATE, a second column, table or statement; and a property changes any one
/// token of the form and finds every change refused. The integer forms refuse the signature form, which answers text.
/// </summary>
public sealed class SignatureFormTests
{
    private static readonly Name Customer = Value(Name.Of("dbo", "Customer"));

    private static string Form => SqlServer.SignatureQuery.Form(Customer, "Email");

    [Theory]
    [Trait("Category", "fast")]
    [Trait("Value", "P2")]
    [InlineData("dbo", "Customer", "Email")]
    [InlineData("Sales Ledger", "Order]Line", "Note, as typed")]
    [InlineData("dbo", "Customer", "t")]
    public void The_committed_form_is_admitted_for_any_table_and_column_and_its_text_read_again_is_the_same_query(string schema,
        string table, string column)
    {
        var name = Value(Name.Of(schema, table));

        var built = Value(SqlServer.SignatureQuery.Of(name, column));
        var read = Value(SqlServer.SignatureQuery.Of(built.Statement, "the form read again"));

        Assert.Equal((built.Statement, name, column), (read.Statement, read.Table, read.Column));
        Assert.Equal(name + "." + SqlServer.Quoted(column) + " signatures", built.Site);
    }

    /// <summary>The integer forms of the allowlist refuse the signature form: its select list answers a signature, which is text.</summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "P2")]
    public void The_integer_forms_refuse_the_signature_form_since_it_answers_text()
    {
        var refused = Failed(SqlServer.AggregateQuery.Of(Form, "dbo.Customer.Email signatures"), "aggregate-query.refused");

        Assert.Contains("answers other than an integer", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The negative cases: the committed form with one fragment replaced by another, every occurrence, or a query given whole where the
    /// fragment is empty; the label, the fragment, its replacement and what the refusal names. The class map's fragments are written from
    /// the committed map, so no case retypes it.
    /// </summary>
    public static TheoryData<string, string, string, string> Refusals()
    {
        var (printable, classes) = (Quoted(SqlServer.Printable), Quoted(SqlServer.Classes));
        var classMap = "TRANSLATE(TRANSLATE(z.v, " + printable + ", " + classes + "), z.o, REPLICATE(N'x', DATALENGTH(z.o) / 2))";
        var digitsKept = Quoted(new string([.. SqlServer.Classes.Select((c, i) => c == '9' ? SqlServer.Printable[i] : c)]));
        var lettersKept = Quoted(new string([.. SqlServer.Classes.Select((c, i) => c == 'a' ? SqlServer.Printable[i] : c)]));
        return new()
        {
            { "a signature query without the fold", "GROUP BY CASE WHEN g.n >= 5 AND g.s LIKE N'%[aA9x]%' THEN g.s END;", "GROUP BY g.s;",
                "without the committed fold" },
            { "a fold that keeps a signature seen once", "g.n >= 5", "g.n >= 1", "without the committed fold" },
            { "a fold that keeps a signature of punctuation alone", " AND g.s LIKE N'%[aA9x]%'", "", "without the committed fold" },
            { "a query that returns a value", "",
                "SELECT t.Email AS signature, COUNT_BIG(*) AS [rows], 1 AS signatures FROM dbo.Customer AS t GROUP BY t.Email",
                "a value read without TRANSLATE" },
            { "a query that returns a substring of a value", "CAST(t.[Email] AS nvarchar(max))", "SUBSTRING(t.[Email], 1, 3)",
                "a part of a value, kept by SUBSTRING" },
            { "a query that returns the first letters of a value", "CAST(t.[Email] AS nvarchar(max))", "LEFT(t.[Email], 2)",
                "a part of a value, kept by LEFT" },
            { "a TRANSLATE that keeps digits", classes, digitsKept, "a TRANSLATE whose class map is not the committed one" },
            { "a TRANSLATE that keeps lower-case letters", classes, lettersKept, "a TRANSLATE whose class map is not the committed one" },
            { "a column read without TRANSLATE", classMap, "z.v", "a value read without TRANSLATE" },
            { "a select list that adds the value", "SUM(g.n) AS [rows]", "MAX(g.s) AS [rows]", "a select list other than the signature" },
            { "a second column read", "WHERE DATALENGTH(t.[Email]) > 0", "WHERE DATALENGTH(t.[Name]) > 0",
                "a query other than the committed signature form" },
            { "a table named in three parts", "FROM [dbo].[Customer] AS t", "FROM [Sales].[dbo].[Customer] AS t",
                "a query other than the committed signature form" },
            { "a second table joined", "FROM [dbo].[Customer] AS t", "FROM [dbo].[Customer] AS t CROSS JOIN [dbo].[Account] AS a",
                "a query other than the committed signature form" },
            { "rows that are empty kept", "WHERE DATALENGTH(t.[Email]) > 0", "WHERE DATALENGTH(t.[Email]) >= 0",
                "a query other than the committed signature form" },
            { "a second statement", "END;", "END; DELETE FROM dbo.Customer;", "other than one SELECT" },
        };
    }

    /// <summary>
    /// Each negative case of the brief, and the other ways a query can differ from the form, refused as aggregate-query.refused, naming what
    /// it is and quoting none of the query.
    /// </summary>
    [Theory]
    [Trait("Category", "fast")]
    [Trait("Value", "P2")]
    [MemberData(nameof(Refusals))]
    public void Each_shape_of_a_signature_query_other_than_the_committed_form_is_refused_naming_what_it_is(string label, string find,
        string replace, string what)
    {
        var text = find.Length == 0 ? replace : Form.Replace(find, replace, StringComparison.Ordinal);
        Assert.True(find.Length == 0 || text != Form, label + ": the committed form holds no " + find);

        var refused = Failed(SqlServer.SignatureQuery.Of(text, "dbo.Customer.Email"), "aggregate-query.refused");

        Assert.Contains(what, refused.Message, StringComparison.Ordinal);
        Assert.StartsWith("The signature query of dbo.Customer.Email is refused: ", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Latin1_General_BIN2", refused.Message + refused.Remedy, StringComparison.Ordinal);
    }

    /// <summary>A change of one token of the form: the token's place and what stands there instead.</summary>
    public sealed record Mutation(int At, string Other);

    /// <summary>
    /// Any one token of the committed form changed to another, or dropped, is refused, unless ScriptDom writes the changed query as the
    /// same text: the form is admitted in one shape alone.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "P2")]
    public void Any_one_token_of_the_committed_form_changed_or_dropped_is_refused()
    {
        var tokens = Tokens(Form);
        var committed = Value(SqlServer.SignatureQuery.Of(Form, "the form")).Statement;
        string[] others = ["", " ", "1", "4", "6", "0", "t", "g", "s", "v", "z", "o", "x", "N'a'", "N'x'", "N''", "N'%'", ",", "(", ")",
            "*", "MAX", "MIN", "LEFT", "TRANSLATE", "REPLACE", "SUM", "COUNT", "AND", "OR", "NOT", "<", ">", "=", "<=", "Email", "[Name]",
            "Latin1_General_CI_AS", "nvarchar(10)", "DISTINCT", "TOP 1"];

        Assert.Equal(Form, string.Concat(tokens));
        Gen.Select(Gen.Int[0, tokens.Count - 1], Gen.OneOfConst(others), (at, other) => new Mutation(at, other)).Sample(mutation =>
        {
            var changed = string.Concat(tokens.Select((token, i) => i == mutation.At ? mutation.Other : token));
            return changed == Form || SqlServer.SignatureQuery.Of(changed, "the form").Match(
                admitted => admitted.Statement == committed, refused => refused.Code == "aggregate-query.refused");
        }, iter: 600, print: mutation => "token " + mutation.At + " (" + tokens[mutation.At] + ") as '" + mutation.Other + "'");
    }

    /// <summary>
    /// The committed class map: each printable ASCII character's class, a lower-case letter a, an upper-case one A, a digit 9 and a space
    /// _, and each other printable character, the ASCII punctuation, kept as itself.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void The_class_map_puts_each_letter_digit_and_space_in_its_class_and_keeps_punctuation()
    {
        var kept = SqlServer.Printable.Zip(SqlServer.Classes).Where(p => p.First == p.Second && !char.IsAsciiLetterOrDigit(p.First))
            .Select(p => p.First);
        var classed = SqlServer.Printable.Zip(SqlServer.Classes).Where(p => char.IsAsciiLetterOrDigit(p.First) || p.First == ' ')
            .GroupBy(p => p.Second).ToDictionary(g => g.Key, g => string.Concat(g.Select(p => p.First)));

        Assert.Equal(95, SqlServer.Printable.Length);
        Assert.Equal("!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~", string.Concat(kept));
        Assert.Equal(("abcdefghijklmnopqrstuvwxyz", "ABCDEFGHIJKLMNOPQRSTUVWXYZ", "0123456789", " "),
            (classed['a'], classed['A'], classed['9'], classed['_']));
        Assert.Equal("Aax.Aa-Aa_99/99", Expected("Zoë.Ferreira-Lopes 09/12"));
        Assert.Equal("x_x", Expected("王小明 Иван"));
    }

    /// <summary>
    /// What the form answers is read as signatures and the fold, and an answer the committed form cannot give is internal.answer-type with
    /// its text withheld: a signature seen fewer than five times, one of punctuation alone, a run left uncollapsed, a second fold, or a row
    /// of another shape.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "P2")]
    public void An_answer_the_committed_form_cannot_give_is_refused_and_its_text_withheld()
    {
        var planted = PlantedValue.Unique();
        var read = Value(SqlServer.Answered([["a.a@a.a", 11L, 1L], ["9999-99-99", 5L, 1L], [null, 4L, 3L]]));
        object?[][] impossible =
        [
            [planted.Text, 1L, 1L], ["a.a@a.a", 4L, 1L], ["--", 6L, 1L], ["aa.a", 5L, 1L], [planted.Text + "x", 9L, 1L],
            ["a", 5L, 2L], ["a", 5L], ["a", "5", 1L],
        ];

        Assert.Equal(new SqlServer.Signature[] { new("a.a@a.a", 11), new("9999-99-99", 5) }, read.Shown.ToArray());
        Assert.Equal((4L, 3L), (read.FoldedRows, read.FoldedSignatures));
        Assert.All(impossible, row => planted.AbsentFrom(Failed(SqlServer.Answered([row]), "internal.answer-type")));
        Failed(SqlServer.Answered([[null, 4L, 3L], [null, 1L, 1L]]), "internal.answer-type");
    }

    /// <summary>The signature the committed form computes for a value, written in C# from the class map: the oracle the fixture tests
    /// hold the server to.</summary>
    internal static string Expected(string value)
    {
        var classed = string.Concat(value.Select(c =>
            c is >= ' ' and <= '~' ? SqlServer.Classes[c - ' '] : 'x'));
        return string.Concat(classed.Where((c, i) => i == 0 || c != classed[i - 1] || c is not ('a' or 'A' or 'x')));
    }

    /// <summary>A Unicode string literal as the form writes one, a quote doubled.</summary>
    private static string Quoted(string text) => "N'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";

    /// <summary>The form's tokens and the spaces between them, in order, a string literal whole.</summary>
    private static List<string> Tokens(string text)
    {
        var tokens = new List<string>();
        for (var i = 0; i < text.Length;)
        {
            var end = text[i] == '\'' || (text[i] == 'N' && i + 1 < text.Length && text[i + 1] == '\'')
                ? Literal(text, text[i] == 'N' ? i + 1 : i)
                : char.IsLetterOrDigit(text[i]) || text[i] is '_' or '.' or '[' or ']'
                    ? Enumerable.Range(i, text.Length - i).FirstOrDefault(j => !(char.IsLetterOrDigit(text[j]) || text[j] is '_' or '.' or '['
                        or ']'), text.Length)
                : i + 1;
            tokens.Add(text[i..end]);
            i = end;
        }

        return tokens;
    }

    /// <summary>The end of the string literal opened at <paramref name="quote"/>, a doubled quote inside it.</summary>
    private static int Literal(string text, int quote)
    {
        for (var j = quote + 1; j < text.Length; j++)
        {
            if (text[j] == '\'' && (j + 1 == text.Length || text[j + 1] != '\''))
            {
                return j + 1;
            }

            if (text[j] == '\'')
            {
                j++;
            }
        }

        return text.Length;
    }
}
