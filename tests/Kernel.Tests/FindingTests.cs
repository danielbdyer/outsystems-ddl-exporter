using System;
using CsCheck;
using Xunit;

namespace Estate.Kernel.Tests;

/// <summary>A finding is a code, a severity, a subject and a message; one of severity error carries a remedy, which its factory cannot be given blank.</summary>
public sealed class FindingTests
{
    private static readonly Gen<string> Text = Gen.String.Where(s => !string.IsNullOrWhiteSpace(s));

    private static readonly Gen<string?> Blank = Gen.OneOf(
        Gen.Const((string?)null),
        Gen.Char[" \t\n 　"].Array[0, 4].Select(cs => (string?)new string(cs)));

    [Fact]
    [Trait("Category", "fast")]
    public void A_finding_of_severity_error_without_a_remedy_cannot_be_constructed() =>
        Gen.Select(Text, Text, Blank).Sample((subject, message, remedy) =>
            Assert.Throws<ArgumentException>("remedy", () => Finding.Error("doctor.sdk", subject, message, remedy!)));

    [Fact]
    [Trait("Category", "fast")]
    public void A_finding_made_of_an_error_carries_the_error_s_code_message_and_remedy_at_severity_error()
    {
        var error = new Error("sdk.missing", "This machine has no .NET 10 SDK.", "Install the .NET SDK 10.0.401.");

        var finding = Finding.Of(error, "the machine");

        Assert.Equal((Severity.Error, "sdk.missing", "the machine", error.Message, error.Remedy), (finding.Severity, finding.Code, finding.Subject, finding.Message, finding.Remedy));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_warning_may_carry_a_remedy_or_none_and_a_note_carries_none()
    {
        var warned = Finding.Warning("drift.alter", "Table [dbo].[Customer]", "The deploy plan would alter it.", "Run estate diff to see each property.");
        var bare = Finding.Warning("drift.column", "Column [dbo].[Customer].[Email]", "Length 300 → 256.");
        var noted = Finding.Note("toolchain.unpinned", "estate/ledgers/toolchain.md", "The ledger pins no DacFx release.");

        Assert.Equal((Severity.Warning, "Run estate diff to see each property."), (warned.Severity, warned.Remedy));
        Assert.Equal((Severity.Warning, null), (bare.Severity, bare.Remedy));
        Assert.Equal((Severity.Note, null), (noted.Severity, noted.Remedy));
    }

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("Drift.alter")]
    [InlineData("drift")]
    [InlineData("drift..alter")]
    [InlineData("drift.-alter")]
    [InlineData("")]
    public void A_finding_s_code_follows_the_one_code_pattern(string code)
    {
        Assert.Throws<ArgumentException>("code", () => Finding.Warning(code, "Table [dbo].[Customer]", "The deploy plan would alter it."));
        Assert.Throws<ArgumentException>("code", () => Finding.Note(code, "Table [dbo].[Customer]", "The deploy plan would alter it."));
        Assert.Throws<ArgumentException>("code", () => Finding.Error(code, "Table [dbo].[Customer]", "The deploy plan would alter it.", "Run estate diff."));
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_finding_needs_a_subject_and_a_message() =>
        Blank.Sample(blank =>
        {
            Assert.Throws<ArgumentException>("subject", () => Finding.Note("diff.unlike-sources", blank!, "One side is a database."));
            Assert.Throws<ArgumentException>("message", () => Finding.Note("diff.unlike-sources", "estate diff", blank!));
        });
}
