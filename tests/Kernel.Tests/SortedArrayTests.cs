using System;
using System.Globalization;
using System.Linq;
using CsCheck;
using Xunit;

namespace Estate.Kernel.Tests;

/// <summary>A SortedArray is sorted when it is built and compared element by element, whatever the order or the culture.</summary>
public sealed class SortedArrayTests
{
    // Up to eight short words over letters whose ordinal and linguistic orders disagree, so repeats are common.
    private static readonly Gen<string[]> Words =
        Gen.Char["aAbB-\u00E4\u00E9"].Array[0, 3].Select(cs => new string(cs)).Array[0, 8];

    private sealed record Holder(SortedArray<string> Items);

    [Fact]
    [Trait("Category", "fast")]
    public void The_same_elements_in_any_order_build_one_sorted_array() =>
        Words.SelectMany(xs => Gen.Shuffle(xs.ToArray()).Select(ys => (xs, ys))).Sample((xs, ys) =>
            SortedArray.Of(xs) == SortedArray.Of(ys)
            && SortedArray.Of(xs).GetHashCode() == SortedArray.Of(ys).GetHashCode()
            && SortedArray.Of(xs).SequenceEqual(SortedArray.Of(ys)));

    [Fact]
    [Trait("Category", "fast")]
    public void Sorted_arrays_are_equal_exactly_when_their_elements_are_equal_one_by_one() =>
        Gen.Select(Words, Words).Sample((xs, ys) =>
            (SortedArray.Of(xs) == SortedArray.Of(ys)) == xs.Order(StringComparer.Ordinal).SequenceEqual(ys.Order(StringComparer.Ordinal))
            && SortedArray.Of(xs).Equals((object)SortedArray.Of(ys)) == (SortedArray.Of(xs) == SortedArray.Of(ys))
            && (SortedArray.Of(xs) != SortedArray.Of(ys)) == !(SortedArray.Of(xs) == SortedArray.Of(ys)));

    // A near miss changes one element: one character's case flipped, a character appended, or the word swapped for
    // another. Every sample is unequal, so an equality looser than element by element fails on the first sample.
    [Fact]
    [Trait("Category", "fast")]
    public void A_sorted_array_differs_from_its_near_miss() =>
        Gen.Select(Words.Where(xs => xs.Length > 0), Gen.Int[0, 7], Gen.Int[0, 2], Words.Where(ws => ws.Length > 0)).Sample((xs, at, change, others) =>
        {
            var ys = xs.ToArray();
            var i = at % ys.Length;
            var cased = Array.FindIndex(ys[i].ToCharArray(), c => char.ToUpperInvariant(c) != char.ToLowerInvariant(c));
            ys[i] = (change, cased) switch
            {
                (0, >= 0) => string.Concat(ys[i][..cased], Flip(ys[i][cased]).ToString(), ys[i][(cased + 1)..]),
                (2, _) when others[0] != ys[i] => others[0],
                _ => ys[i] + "b",
            };
            return SortedArray.Of(xs) != SortedArray.Of(ys) && !SortedArray.Of(xs).Equals((object)SortedArray.Of(ys));
        });

    [Theory]
    [Trait("Category", "fast")]
    [InlineData(new[] { "a" }, new[] { "A" })]
    [InlineData(new[] { "a" }, new[] { "a", "a" })]
    [InlineData(new[] { "a" }, new[] { "ab" })]
    [InlineData(new[] { "ab" }, new[] { "a", "b" })]
    [InlineData(new[] { "a", "b" }, new[] { "a", "c" })]
    [InlineData(new[] { "ä" }, new[] { "Ä" })]
    public void Pinned_near_misses_are_different_sorted_arrays(string[] xs, string[] ys)
    {
        Assert.True(SortedArray.Of(xs) != SortedArray.Of(ys));
        Assert.False(SortedArray.Of(xs).Equals(SortedArray.Of(ys)));
    }

    private static char Flip(char c) => char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c);

    [Fact]
    [Trait("Category", "fast")]
    public void A_record_holding_a_sorted_array_compares_the_elements_not_the_array() =>
        Words.Sample(xs =>
            new Holder(SortedArray.Of(xs)) == new Holder(SortedArray.Of(Enumerable.Reverse(xs).Select(x => new string(x.AsSpan())))));

    // Each culture's own order puts "a" before "B"; ordinal order puts "B" first.
    [Theory]
    [Trait("Category", "fast")]
    [InlineData("")]
    [InlineData("en-US")]
    [InlineData("sv-SE")]
    [InlineData("tr-TR")]
    public void A_sorted_array_is_in_ordinal_order_whatever_the_culture(string culture)
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            Assert.True(CultureInfo.CurrentCulture.CompareInfo.Compare("a", "B") < 0, "the culture is not in force");
            Gen.String.Array[0, 8].Sample(xs => SortedArray.Of(xs).SequenceEqual(xs.Order(StringComparer.Ordinal)), threads: 1);
            SortedArray<string> built = ["b", "\u00E9", "B", "a", "\u00E4", "A"];
            Assert.Equal(new[] { "A", "B", "a", "b", "\u00E4", "\u00E9" }, built.ToArray());
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    [Trait("Category", "fast")]
    public void The_default_sorted_array_is_the_empty_sorted_array()
    {
        SortedArray<string> empty = [];
        Assert.True(empty == default(SortedArray<string>));
        Assert.Equal(empty.GetHashCode(), default(SortedArray<string>).GetHashCode());
        Assert.Empty(default(SortedArray<string>));
    }
}
