using System;
using CsCheck;
using Xunit;

namespace Estate.Kernel.Tests;

/// <summary>An error is a code, a message and a remedy; one without a remedy cannot be constructed.</summary>
public sealed class ErrorTests
{
    private static readonly Gen<string> Code =
        Gen.Char["az09"].Array[1, 4].Select(cs => new string(cs)).Array[2, 4].Select(words => string.Join('.', words));

    private static readonly Gen<string> Text = Gen.String.Where(s => !string.IsNullOrWhiteSpace(s));

    private static readonly Gen<string?> Blank = Gen.OneOf(
        Gen.Const((string?)null),
        Gen.Char[" \t\n\u00A0\u3000"].Array[0, 4].Select(cs => (string?)new string(cs)));

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
    public void An_error_keeps_its_code_message_and_remedy() =>
        Gen.Select(Code, Text, Text).Sample((code, message, remedy) =>
            new Error(code, message, remedy) is var error
            && error.Code == code && error.Message == message && error.Remedy == remedy);

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
    public void An_error_code_is_a_category_and_a_detail_in_lowercase_words(string code) =>
        Assert.Throws<ArgumentException>("code", () => new Error(code, "Failed.", "Do the other thing."));

    /// <summary>Every word of a code, the category included, may hold digits and single hyphens, as scratch-server and git-branch do.</summary>
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("scratch-server.missing")]
    [InlineData("git-branch.exists")]
    [InlineData("toolchain.window-order")]
    [InlineData("v2.sql-72014.detail")]
    public void Each_word_of_an_error_code_may_hold_digits_and_single_hyphens(string code) =>
        Assert.Equal(code, new Error(code, "Failed.", "Do the other thing.").Code);
}
