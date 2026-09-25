using System;
using System.Linq;
using CsCheck;
using Xunit;

namespace Estate.Kernel.Tests;

/// <summary>A Name is one or two parts, each 1 to 128 characters, not blank, with no control character.</summary>
public sealed class NameTests
{
    private static readonly Gen<string> Part =
        Gen.Char[' ', '\uFFFF'].Where(c => !char.IsControl(c)).Array[1, 128]
            .Select(cs => new string(cs)).Where(s => !string.IsNullOrWhiteSpace(s));

    // Each invalid form of a part, with the code of the error that names it.
    private static readonly Gen<(string Part, string Code)> Invalid = Gen.OneOf(
        Gen.Char[" \u00A0\u2003\u3000"].Array[0, 4].Select(cs => (new string(cs), "name.blank")),
        Gen.Char['a', 'z'].Array[129, 300].Select(cs => (new string(cs), "name.too-long")),
        // At least one letter beside the control character: a part that is only \t or U+0085 is white space, so blank.
        Gen.Select(Gen.Char['a', 'z'].Array[1, 100], Gen.OneOf(Gen.Char['\u0000', '\u001F'], Gen.Char['\u007F', '\u009F']))
            .Select((cs, c) => (new string(cs).Insert(cs.Length / 2, new string(c, 1)), "name.control-character")));

    private static readonly Gen<Name> Small =
        Gen.Select(Gen.Bool, Gen.Char["aA."].Array[1, 2], Gen.Char["aA."].Array[1, 2])
            .Select((qualified, s, b) => qualified ? N(new string(s), new string(b)) : N(new string(b)));

    [Fact]
    [Trait("Category", "fast")]
    public void A_name_is_one_or_two_valid_parts_kept_as_given()
    {
        Gen.Select(Part, Part).Sample((schema, part) =>
            Name.Of(part) is Result<Name>.Ok(var one) && one.Schema is null && one.Base == part
            && Name.Of(schema, part) is Result<Name>.Ok(var two) && two.Schema == schema && two.Base == part);
        Assert.Equal(128, N(new string('x', 128)).Base.Length);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_name_rejects_a_blank_an_overlong_or_a_control_character_part_in_either_place()
    {
        Invalid.Sample(bad =>
            Code(Name.Of(bad.Part)) == bad.Code
            && Code(Name.Of(bad.Part, "Customer")) == bad.Code
            && Code(Name.Of("dbo", bad.Part)) == bad.Code);
        Assert.Equal("name.too-long", Code(Name.Of(new string('x', 129))));
        Assert.Equal("name.blank", Code(Name.Of(null!)));
        Assert.Throws<InvalidOperationException>(() => default(Name).Base);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void Names_compare_ordinally_with_case_and_render_as_quotename_does()
    {
        Gen.Select(Small, Small).Sample((a, b) =>
            (a.CompareTo(b) == 0) == (a == b) && Math.Sign(a.CompareTo(b)) == -Math.Sign(b.CompareTo(a)));
        Assert.NotEqual(N("Customer"), N("customer"));
        Assert.Equal(
            new[] { "[Customer]", "[customer]", "[dbo].[A]" },
            Seq.Of(N("dbo", "A"), N("customer"), N("Customer")).Select(n => n.ToString()));
        Assert.Equal("[a]]b].[c.d]", N("a]b", "c.d").ToString());
    }

    private static string? Code(Result<Name> result) => (result as Result<Name>.Failed)?.Error.Code;

    private static Name N(string part) => Assert.IsType<Result<Name>.Ok>(Name.Of(part)).Value;

    private static Name N(string schema, string part) => Assert.IsType<Result<Name>.Ok>(Name.Of(schema, part)).Value;
}
