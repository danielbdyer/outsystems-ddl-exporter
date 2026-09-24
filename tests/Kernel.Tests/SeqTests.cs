using System;
using System.Globalization;
using System.Linq;
using CsCheck;
using Xunit;

namespace Estate.Kernel.Tests;

/// <summary>A Seq is sorted when it is built and compared element by element, whatever the order or the culture.</summary>
public sealed class SeqTests
{
    // Up to eight short words over letters whose ordinal and linguistic orders disagree, so repeats are common.
    private static readonly Gen<string[]> Words =
        Gen.Char["aAbB-\u00E4\u00E9"].Array[0, 3].Select(cs => new string(cs)).Array[0, 8];

    private sealed record Holder(Seq<string> Items);

    [Fact]
    [Trait("Category", "fast")]
    public void The_same_elements_in_any_order_build_one_seq() =>
        Words.SelectMany(xs => Gen.Shuffle(xs.ToArray()).Select(ys => (xs, ys))).Sample((xs, ys) =>
            Seq.Of(xs) == Seq.Of(ys)
            && Seq.Of(xs).GetHashCode() == Seq.Of(ys).GetHashCode()
            && Seq.Of(xs).SequenceEqual(Seq.Of(ys)));

    [Fact]
    [Trait("Category", "fast")]
    public void Seqs_are_equal_exactly_when_their_elements_are_equal_one_by_one() =>
        Gen.Select(Words, Words).Sample((xs, ys) =>
            (Seq.Of(xs) == Seq.Of(ys)) == xs.Order(StringComparer.Ordinal).SequenceEqual(ys.Order(StringComparer.Ordinal))
            && Seq.Of(xs).Equals((object)Seq.Of(ys)) == (Seq.Of(xs) == Seq.Of(ys))
            && (Seq.Of(xs) != Seq.Of(ys)) == !(Seq.Of(xs) == Seq.Of(ys)));

    [Fact]
    [Trait("Category", "fast")]
    public void A_record_holding_a_seq_compares_the_elements_not_the_array() =>
        Words.Sample(xs =>
            new Holder(Seq.Of(xs)) == new Holder(Seq.Of(Enumerable.Reverse(xs).Select(x => new string(x.AsSpan())))));

    // Each culture's own order puts "a" before "B"; ordinal order puts "B" first.
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("")]
    [InlineData("en-US")]
    [InlineData("sv-SE")]
    [InlineData("tr-TR")]
    public void A_seq_is_in_ordinal_order_whatever_the_culture(string culture)
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            Assert.True(CultureInfo.CurrentCulture.CompareInfo.Compare("a", "B") < 0, "the culture is not in force");
            Gen.String.Array[0, 8].Sample(xs => Seq.Of(xs).SequenceEqual(xs.Order(StringComparer.Ordinal)), threads: 1);
            Seq<string> built = ["b", "\u00E9", "B", "a", "\u00E4", "A"];
            Assert.Equal(new[] { "A", "B", "a", "b", "\u00E4", "\u00E9" }, built.ToArray());
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_default_seq_is_the_empty_seq()
    {
        Seq<string> empty = [];
        Assert.True(empty == default(Seq<string>));
        Assert.Equal(empty.GetHashCode(), default(Seq<string>).GetHashCode());
        Assert.Empty(default(Seq<string>));
    }
}
