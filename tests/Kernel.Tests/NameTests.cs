using System;
using System.Linq;
using CsCheck;
using Xunit;

namespace Estate.Kernel.Tests;

/// <summary>
/// A Name is one or two parts, each 1 to 128 UTF-16 code units, whatever the units are: SQL Server admits white space and control
/// characters inside brackets, so a part of spaces alone, or one holding a tab, is a name a schema can hold (decision 2.25).
/// </summary>
public sealed class NameTests
{
    /// <summary>A part of 1 to 128 code units drawn from the whole BMP: control characters (U+0000 to U+001F, U+007F to U+009F), white space and the rest.</summary>
    private static readonly Gen<string> Part = Gen.OneOf(
        Gen.Char['\u0000', '￿'].Array[1, 128].Select(cs => new string(cs)),
        Gen.Char[" \t\r\n\u0000\u001B\u007F\u0085  　"].Array[1, 8].Select(cs => new string(cs)));

    private static readonly Gen<Name> Small =
        Gen.Select(Gen.Bool, Gen.Char["aA."].Array[1, 2], Gen.Char["aA."].Array[1, 2])
            .Select((qualified, s, b) => qualified ? N(new string(s), new string(b)) : N(new string(b)));

    [Fact]
    [Trait("Category", "fast")]
    public void A_name_is_one_or_two_parts_of_1_to_128_code_units_kept_as_given_white_space_and_control_characters_included()
    {
        Gen.Select(Part, Part).Sample((schema, part) =>
            Name.Of(part) is Result<Name>.Ok(var one) && one.Schema is null && one.Base == part
            && Name.Of(schema, part) is Result<Name>.Ok(var two) && two.Schema == schema && two.Base == part);
        Assert.Equal(" ", N(" ").Base);
        Assert.Equal("a\tb", N("dbo", "a\tb").Base);
        Assert.Equal(128, N(new string('x', 128)).Base.Length);
    }

    [Fact]
    [Trait("Category", "fast")]
    public void A_name_rejects_an_empty_part_and_a_part_past_128_code_units_in_either_place()
    {
        Gen.Char['a', 'z'].Array[129, 300].Select(cs => new string(cs)).Sample(overlong =>
            Code(Name.Of(overlong)) == "name.too-long" && Code(Name.Of(overlong, "Customer")) == "name.too-long" && Code(Name.Of("dbo", overlong)) == "name.too-long");
        Assert.Equal("name.too-long", Code(Name.Of(new string('x', 129))));
        Assert.Equal("name.blank", Code(Name.Of("")));
        Assert.Equal("name.blank", Code(Name.Of("", "Customer")));
        Assert.Equal("name.blank", Code(Name.Of("dbo", "")));
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
            SortedArray.Of(N("dbo", "A"), N("customer"), N("Customer")).Select(n => n.ToString()));
        Assert.Equal("[a]]b].[c.d]", N("a]b", "c.d").ToString());
    }

    private static string? Code(Result<Name> result) => (result as Result<Name>.Failed)?.Error.Code;

    private static Name N(string part) => Assert.IsType<Result<Name>.Ok>(Name.Of(part)).Value;

    private static Name N(string schema, string part) => Assert.IsType<Result<Name>.Ok>(Name.Of(schema, part)).Value;
}
