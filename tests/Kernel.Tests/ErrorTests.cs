using System;
using System.Linq;
using CsCheck;
using Xunit;

namespace Estate.Kernel.Tests;

/// <summary>An error is a code of a closed category, a message and a remedy; one without a remedy, or of a category the set lacks, cannot be constructed.</summary>
public sealed class ErrorTests
{
    private static readonly Gen<string> Category = Gen.OneOfConst(Enum.GetValues<ErrorCategory>().Select(ErrorCode.Text).ToArray());

    private static readonly Gen<string> Code = Gen.Select(Category, Gen.Char["az09"].Array[1, 4].Select(cs => new string(cs)).Array[1, 3])
        .Select((category, details) => category + "." + string.Join('.', details));

    private static readonly Gen<string> Text = Gen.String.Where(s => !string.IsNullOrWhiteSpace(s));

    private static readonly Gen<string?> Blank = Gen.OneOf(
        Gen.Const((string?)null),
        Gen.Char[" \t\n 　"].Array[0, 4].Select(cs => (string?)new string(cs)));

    [Fact]
    [Trait("Category", "fast")]
    public void An_error_without_a_remedy_cannot_be_constructed() =>
        Gen.Select(Code, Text, Blank).Sample((code, message, remedy) =>
            Assert.Throws<ArgumentException>("remedy", () => new Error(code, message, remedy!)));

    [Fact]
    [Trait("Category", "fast")]
    public void An_error_without_a_message_cannot_be_constructed() =>
        Gen.Select(Code, Blank, Text).Sample((code, message, remedy) =>
            Assert.Throws<ArgumentException>("message", () => new Error(code, message!, remedy)));

    [Fact]
    [Trait("Category", "fast")]
    public void An_error_keeps_its_code_message_and_remedy_and_reads_its_category_from_the_code() =>
        Gen.Select(Code, Text, Text).Sample((code, message, remedy) =>
            new Error(code, message, remedy) is var error
            && error.Code == code && error.Message == message && error.Remedy == remedy
            && ErrorCode.Text(error.Category) == code.Split('.')[0]);

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
    [InlineData("Scratch-Server.missing")]
    [InlineData("scratch--server.missing")]
    [InlineData("-scratch.missing")]
    [InlineData("scratch-server.")]
    [InlineData("scratch-server")]
    public void An_error_code_is_a_category_and_a_detail_in_lowercase_words(string code) =>
        Assert.Throws<ArgumentException>("code", () => new Error(code, "Failed.", "Do the other thing."));

    /// <summary>Every word of a code, the category included, may hold digits and single hyphens, as scratch-server and git-branch do.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("scratch-server.missing")]
    [InlineData("git-branch.exists")]
    [InlineData("toolchain.window-order")]
    [InlineData("name.too-long")]
    [InlineData("dacfx.sql-72014.detail")]
    public void Each_word_of_an_error_code_may_hold_digits_and_single_hyphens(string code) =>
        Assert.Equal(code, new Error(code, "Failed.", "Do the other thing.").Code);

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
        Assert.Equal(("scratch-server", "dacfx", "git-branch"), (ErrorCode.Text(ErrorCategory.ScratchServer), ErrorCode.Text(ErrorCategory.DacFx), ErrorCode.Text(ErrorCategory.GitBranch)));
    }
}
