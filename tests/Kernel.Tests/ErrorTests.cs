using System;
using System.Linq;
using CsCheck;
using Xunit;

namespace Estate.Kernel.Tests;

/// <summary>An error is a code of a closed category, a message and a remedy; one without a remedy, or of a category the set lacks, cannot be constructed.</summary>
public sealed class ErrorTests
{
    private static readonly Gen<ErrorCategory> Member = Gen.OneOfConst(Enum.GetValues<ErrorCategory>());

    /// <summary>A word of the code grammar: runs of lowercase letters and digits joined by single hyphens, as local-server and too-long are.</summary>
    private static readonly Gen<string> Word = Gen.Char["az09"].Array[1, 3].Select(cs => new string(cs)).Array[1, 3].Select(runs => string.Join('-', runs));

    /// <summary>A member's word, then one to three words of detail: every code the grammar admits under a category the set holds.</summary>
    private static readonly Gen<(ErrorCategory Category, string Code)> Code = Gen.Select(Member, Word.Array[1, 3])
        .Select((category, details) => (category, ErrorCode.Text(category) + "." + string.Join('.', details)));

    private static readonly Gen<string> Text = Gen.String.Where(s => !string.IsNullOrWhiteSpace(s));

    private static readonly Gen<string?> Blank = Gen.OneOf(
        Gen.Const((string?)null),
        Gen.Char[" \t\n 　"].Array[0, 4].Select(cs => (string?)new string(cs)));

    [Fact]
    [Trait("Category", "fast")]
    public void An_error_without_a_remedy_cannot_be_constructed() =>
        Gen.Select(Code, Text, Blank).Sample((code, message, remedy) =>
            Assert.Throws<ArgumentException>("remedy", () => new Error(code.Code, message, remedy!)));

    [Fact]
    [Trait("Category", "fast")]
    public void An_error_without_a_message_cannot_be_constructed() =>
        Gen.Select(Code, Blank, Text).Sample((code, message, remedy) =>
            Assert.Throws<ArgumentException>("message", () => new Error(code.Code, message!, remedy)));

    /// <summary>Over the whole grammar, hyphenated words and digits included, a code under a member's word constructs, and the error reads that member as its category.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Every_code_the_grammar_admits_under_a_member_s_word_constructs_an_error_of_that_category() =>
        Code.Sample(code => new Error(code.Code, "Failed.", "Do the other thing.").Category == code.Category, print: code => code.Code);

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("")]
    [InlineData("name")]
    [InlineData("Name.blank")]
    [InlineData("name..blank")]
    [InlineData(".name.blank")]
    [InlineData("name.blank.")]
    [InlineData("name.too long")]
    [InlineData("name.-long")]
    [InlineData("name.long-")]
    [InlineData("name.too--long")]
    [InlineData("name.blank\n")]
    [InlineData("\nname.blank")]
    [InlineData("Local-Server.missing")]
    [InlineData("scratch--server.missing")]
    [InlineData("-scratch.missing")]
    [InlineData("local-server.")]
    [InlineData("local-server")]
    public void An_error_code_is_a_category_and_a_detail_in_lowercase_words(string code) =>
        Assert.Throws<ArgumentException>("code", () => new Error(code, "Failed.", "Do the other thing."));

    /// <summary>The category set is closed: a well-formed code whose first word no member writes as cannot be made into an error, and the error names the word.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void An_error_of_a_category_the_set_lacks_cannot_be_constructed_and_the_exception_names_the_category()
    {
        var thrown = Assert.Throws<ArgumentException>("code", () => new Error("nowhere.x", "Failed.", "Do the other thing."));

        Assert.Contains("'nowhere'", thrown.Message, StringComparison.Ordinal);
        Assert.Null(ErrorCode.Parse("nowhere"));
        Assert.Null(ErrorCode.Parse("Name"));
    }

    /// <summary>Each member writes as one word its code carries, distinct from every other member's, and reads back from it.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void Each_category_writes_as_a_word_the_code_pattern_admits_and_parses_back_from_it()
    {
        var members = Enum.GetValues<ErrorCategory>();

        Assert.All(members, m => Assert.Equal(m, ErrorCode.Parse(ErrorCode.Text(m))));
        Assert.Equal(members.Length, members.Select(ErrorCode.Text).Distinct(StringComparer.Ordinal).Count());
        Assert.All(members, m => Assert.Matches(ErrorCode.Pattern, ErrorCode.Text(m) + ".x"));
        Assert.Equal(("local-server", "dacfx", "git-branch"), (ErrorCode.Text(ErrorCategory.LocalServer), ErrorCode.Text(ErrorCategory.DacFx), ErrorCode.Text(ErrorCategory.GitBranch)));
    }
}
