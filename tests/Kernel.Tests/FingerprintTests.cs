using System;
using System.Linq;
using System.Text;
using CsCheck;
using Xunit;

namespace DbChange.Kernel.Tests;

/// <summary>A fingerprint is SHA-256 over canonical bytes: equal inputs give equal fingerprints in any process.</summary>
public sealed class FingerprintTests
{
    private const string LfVector = "7e18f737311b2dc3b2f269dd78396b0351f14fb66efa879f768cb23181883c78"; // "a\nb"

    // The vectors are the cross-process evidence: FIPS 180-2's own, and the rest computed by coreutils'
    // sha256sum, an implementation other than the one under test.
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq", "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1")]
    [InlineData("\u00E9", "4a99557e4033c3539de2eb65472017cad5f9557f7a0625a09f1c3f6e2ba69c4c")]
    [InlineData("\U0001F600", "f0443a342c5ef54783a111b51ba56c938e474c32324d90c3a60c9c8e3a37e2d9")]
    [InlineData("a\nb", LfVector)]
    [InlineData("a\r\nb", LfVector)]
    [InlineData("a\rb", LfVector)]
    [InlineData("\uFEFFa\nb", LfVector)]
    public void Known_vectors_pin_the_fingerprint_in_every_process(string text, string sha256) =>
        Assert.Equal(sha256, Fingerprint.Of(text).ToString());

    [Fact]
    [Trait("Category", "fast")]
    public void Bytes_are_fingerprinted_as_given()
    {
        Assert.Equal(
            "cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0",
            Fingerprint.Of(Enumerable.Repeat((byte)'a', 1_000_000).ToArray()).ToString());
        Assert.Equal(Fingerprint.Of("abc"), Fingerprint.Of("abc"u8));
        Assert.NotEqual(Fingerprint.Of("a\nb"), Fingerprint.Of("a\r\nb"u8));
    }

    [Fact]
    [Trait("Category", "fast")]
    [Trait("Value", "D3")]
    public void Text_is_fingerprinted_in_its_canonical_form() =>
        Gen.Char["ab \n\u00E9"].Array[0, 12].Select(cs => new string(cs)).Sample(lf =>
            Fingerprint.Of(lf) == Fingerprint.Of(Encoding.UTF8.GetBytes(lf))
            && Fingerprint.Of(lf) == Fingerprint.Of(lf.Replace("\n", "\r\n", StringComparison.Ordinal))
            && Fingerprint.Of(lf) == Fingerprint.Of(lf.Replace('\n', '\r'))
            && Fingerprint.Of(lf) == Fingerprint.Of("\uFEFF" + lf));

    [Fact]
    [Trait("Category", "fast")]
    public void A_fingerprint_renders_as_64_lowercase_hex_digits_and_parses_back() =>
        Gen.Byte.Array.Sample(b =>
            Fingerprint.Of(b).ToString() is var hex
            && hex.Length == 64
            && hex.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f')
            && Fingerprint.Parse(hex) == Result.Ok(Fingerprint.Of(b)));

    [Theory]
    [Trait("Category", "fast")]
    [InlineData("")]
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b85")]
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b8550")]
    [InlineData("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855")]
    [InlineData("g3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    public void Parse_rejects_anything_but_64_lowercase_hex_digits(string text) =>
        Assert.Equal(
            "fingerprint.malformed",
            Assert.IsType<Result<Fingerprint>.Failed>(Fingerprint.Parse(text)).Error.Code);
}
