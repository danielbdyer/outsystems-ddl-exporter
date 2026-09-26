using System;
using System.Linq;
using CsCheck;
using Xunit;

namespace DbChange.Kernel.Tests;

/// <summary>kernel/LineEndings: CRLF and a lone CR become LF, and nothing else changes, so a fingerprint sees one text whatever wrote its line breaks.</summary>
public sealed class LineEndingsTests
{
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("a\rb", "a\nb")]          // a lone CR, which io/Write kept until this rule (R7)
    [InlineData("a\r\nb", "a\nb")]
    [InlineData("\r\r\n", "\n\n")]        // a CR before a CRLF is one break, then the CRLF is another
    [InlineData("\n\r", "\n\n")]
    [InlineData("", "")]
    public void CRLF_and_a_lone_CR_become_LF_and_a_CR_before_CRLF_is_its_own_break(string text, string lf) =>
        Assert.Equal(lf, LineEndings.Lf(text));

    [Fact]
    [Trait("Category", "fast")]
    public void Lf_leaves_no_CR_keeps_every_other_character_in_order_and_is_idempotent() =>
        Gen.Char["a\r\n é"].Array[0, 16].Select(cs => new string(cs)).Sample(text =>
        {
            var lf = LineEndings.Lf(text);
            var breaks = text.Count(c => c == '\n') + text.Count(c => c == '\r') - text.Split("\r\n").Length + 1;
            return !lf.Contains('\r', StringComparison.Ordinal)
                && lf.Replace("\n", "", StringComparison.Ordinal) == text.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal)
                && lf.Count(c => c == '\n') == breaks
                && LineEndings.Lf(lf) == lf;
        });

    /// <summary>Fingerprint applies the same rule, so a fingerprint of a text and of the text already in LF are one fingerprint.</summary>
    [Fact]
    [Trait("Category", "fast")]
    public void A_fingerprint_of_a_text_equals_the_fingerprint_of_its_LF_form() =>
        Gen.String.Sample(text => Fingerprint.Of(text) == Fingerprint.Of(LineEndings.Lf(text)));
}
