using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using DbChange.Tests;
using Xunit;

namespace DbChange.Kernel.Tests;

/// <summary>
/// The SQLCMD grammar a deploy script is written in, held once: a variable's name, its placeholder $(name), sqlcmd's substitution of it,
/// the :setvar lines a kept script leaves out for the values a reference gave, and a script without its sqlcmd directives. Each reads
/// the one name grammar, so a plan and a parse of its script never read a variable two ways.
/// </summary>
public sealed class SqlCmdTests
{
    private static readonly PlantedValue Planted = PlantedValue.Password;

    /// <summary>A name the grammar admits: a letter or '_', then up to 127 letters, digits, '_' and '-'.</summary>
    private static readonly Gen<string> Name = Gen.Select(Gen.Char["abcXYZ_"], Gen.Char["abcXYZ019_-"].Array[0, 127])
        .Select((first, rest) => first + new string(rest));

    [Fact]
    [Trait("Category", "fast")]
    public void A_placeholder_substituted_with_its_variable_s_value_is_the_value() =>
        Gen.Select(Name, Gen.String).Sample((name, value) =>
            SqlCmdVariable.Substitute(SqlCmdVariable.Placeholder(name),
                new Dictionary<string, string>(StringComparer.Ordinal) { [name] = value }) is Result<string>.Ok(var text) && text == value);

    [Fact]
    [Trait("Category", "fast")]
    public void Substitution_replaces_each_variable_as_sqlcmd_does_ignoring_case_and_never_twice()
    {
        const string script = "PRINT N'$(EnvironmentTag)'; -- $(environmenttag)\nALTER USER [$(ServiceUser)] WITH DEFAULT_SCHEMA = dbo;";
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["EnvironmentTag"] = "dev",
            ["ServiceUser"] = "svc$(EnvironmentTag)" };

        Assert.Equal("PRINT N'dev'; -- dev\nALTER USER [svc$(EnvironmentTag)] WITH DEFAULT_SCHEMA = dbo;",
            Expect.Value(SqlCmdVariable.Substitute(script, values)));
        Assert.Equal("no variables here", Expect.Value(SqlCmdVariable.Substitute("no variables here", new Dictionary<string, string>())));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Substitution_fails_on_a_variable_with_no_value_naming_it_and_quotes_no_value()
    {
        var error = Expect.Failed(SqlCmdVariable.Substitute("PRINT '$(Tag)'; PRINT '$(Missing)';",
            new Dictionary<string, string> { ["Tag"] = Planted.Text }), "sqlcmd.undefined");

        Assert.Contains("$(Missing)", error.Message, StringComparison.Ordinal);
        Planted.AbsentFrom(error);
    }

    /// <summary>
    /// The kept script leaves out the :setvar line of each variable a reference gave, so the value never reaches disk: the named lines go
    /// in either case, and every other line stays byte for byte, a :setvar of another name, one whose name the named one begins, a
    /// commented one and CRLF line ends included. A :setvar with no value goes alone, not with the line after it.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Unset_leaves_out_the_setvar_lines_of_the_names_given_and_keeps_every_other_line_as_it_was()
    {
        var script = ":setvar Tag \"dev\"\r\n:SETVAR ServiceToken \"" + Planted
            + "\"\r\n:setvar TagSuffix \"keep\"\r\n:setvar Other \"keep\"\r\n"
            + "-- :setvar Tag \"commented\"\r\nPRINT N'$(Tag)';\r\nGO\r\n:setvar Empty\nPRINT 1;\n";

        var kept = SqlCmdVariable.Unset(script, ["tag", "ServiceToken", "empty"]);

        Assert.Equal(
            ":setvar TagSuffix \"keep\"\r\n:setvar Other \"keep\"\r\n-- :setvar Tag \"commented\"\r\nPRINT N'$(Tag)';\r\nGO\r\nPRINT 1;\n",
            kept);
        Assert.Equal(script, SqlCmdVariable.Unset(script, []));
    }

    /// <summary>What is left of a script once sqlcmd's own lines go: every line whose first non-blank character is ':' (:setvar, :r, :on
    /// error, :connect) goes, and every other line stays, GO included.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_script_with_its_sqlcmd_commands_removed_keeps_its_statements_and_GO_and_drops_each_sqlcmd_line()
    {
        const string Script = ":setvar DatabaseName \"Estate\"\n:on error exit\n  :r .\\Seed.sql\n:CONNECT dev-sql\n"
            + "USE [$(DatabaseName)];\nGO\nPRINT N'a: b';\n";

        Assert.Equal("USE [$(DatabaseName)];\nGO\nPRINT N'a: b';\n", SqlCmdVariable.WithoutDirectives(Script));
    }

    /// <summary>
    /// The same script with each sqlcmd line left empty and its line end kept (CRLF or LF), so a parser's line number in what is left is
    /// the line of the script DacFx wrote.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_script_with_its_sqlcmd_commands_left_empty_keeps_the_line_of_every_statement()
    {
        const string Script = ":setvar DatabaseName \"Estate\"\r\n:on error exit\n  :r .\\Seed.sql\nUSE [$(DatabaseName)];\nGO\n:setvar Last";

        Assert.Equal("\r\n\n\nUSE [$(DatabaseName)];\nGO\n", SqlCmdVariable.WithoutDirectives(Script, keepLines: true));
    }

    /// <summary>
    /// The values a script's own :setvar lines give, which DacFx writes for every variable a plan sets but those a reference gave: a value
    /// in double quotes, a doubled quote inside read as one, or a bare word; the command and the name in any case, the later line
    /// winning as sqlcmd reads them; and a :setvar with no value removing the variable. A line that only mentions :setvar sets nothing.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_script_s_setvar_lines_give_each_variable_its_last_value_quoted_or_bare()
    {
        const string Script = ":setvar DatabaseName \"Estate\"\r\n:SETVAR Tag \"a \"\"quoted\"\" tag\"\n:setvar Bare word\n"
            + ":setvar databasename \"Later\"\n:setvar Gone \"x\"\n:setvar Gone\n-- :setvar Comment \"no\"\nPRINT N'$(Tag)';\n";

        Assert.Equal(new Dictionary<string, string> { ["databasename"] = "Later", ["Tag"] = "a \"quoted\" tag", ["Bare"] = "word" },
            SqlCmdVariable.SetIn(Script));
    }

    /// <summary>
    /// A substitution records where it put each value in the text it returns, by the variable's name as the script writes it, so a caller
    /// can tell which part of the substituted text holds a value marked sensitive without reading the value back.
    /// </summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_substitution_records_where_in_its_text_it_put_each_variable_s_value()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["Tag"] = "dev", ["ServiceToken"] = Planted.Text };

        var substituted = Expect.Value(SqlCmdVariable.Substituted("PRINT N'$(tag)'; ALTER USER [$(ServiceToken)]; -- $(Tag)", values));

        Assert.Equal("PRINT N'dev'; ALTER USER [" + Planted.Text + "]; -- dev", substituted.Text);
        Assert.Equal([("tag", "dev"), ("ServiceToken", Planted.Text), ("Tag", "dev")],
            substituted.Placed.Select(p => (p.Name.ToString(), substituted.Text.Substring(p.Start, p.Length))));
    }
}
