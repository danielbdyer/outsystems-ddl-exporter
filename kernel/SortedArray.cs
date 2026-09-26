using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DbChange.Kernel;

/// <summary>
/// The kernel's one collection: immutable, sorted when it is built, and equal to another SortedArray exactly when their
/// elements are equal one by one. The order is the element type's own <see cref="IComparable{T}"/>, except that
/// strings sort ordinally, never by a culture; so the same elements given in any order build the same SortedArray, in
/// every process. An element type's order must return zero only for equal elements, as the kernel's types do. A
/// record holding a SortedArray compares by content, where one holding an array or an ImmutableArray would compare
/// references. default(SortedArray&lt;T&gt;) is the empty SortedArray.
/// </summary>
[CollectionBuilder(typeof(SortedArray), nameof(SortedArray.Of))]
public readonly struct SortedArray<T> : IReadOnlyList<T>, IEquatable<SortedArray<T>>
{
    private readonly ImmutableArray<T> _sorted;

    internal SortedArray(ImmutableArray<T> sorted) => _sorted = sorted;

    private ImmutableArray<T> Items => _sorted.IsDefault ? [] : _sorted;

    public int Count => Items.Length;

    public T this[int index] => Items[index];

    public static bool operator ==(SortedArray<T> left, SortedArray<T> right) => left.Equals(right);

    public static bool operator !=(SortedArray<T> left, SortedArray<T> right) => !left.Equals(right);

    public ImmutableArray<T>.Enumerator GetEnumerator() => Items.GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)Items).GetEnumerator();

    public bool Equals(SortedArray<T> other) => Items.AsSpan().SequenceEqual(other.Items.AsSpan(), EqualityComparer<T>.Default);

    public override bool Equals(object? obj) => obj is SortedArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in Items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }
}

/// <summary>Builds a <see cref="SortedArray{T}"/>: <c>SortedArray.Of(a, b)</c>, <c>SortedArray.Of(items)</c>, or a collection expression.</summary>
public static class SortedArray
{
    public static SortedArray<T> Of<T>(params ReadOnlySpan<T> items) where T : IComparable<T> => Sorted(items.ToArray());

    public static SortedArray<T> Of<T>(IEnumerable<T> items) where T : IComparable<T> => Sorted(items.ToArray());

    /// <summary>Two sorted arrays in lexicographic order, the shorter first when one begins the other: how a record holding sorted arrays sorts.</summary>
    internal static int Compare<T>(SortedArray<T> mine, SortedArray<T> theirs) where T : IComparable<T>
    {
        for (var i = 0; i < Math.Min(mine.Count, theirs.Count); i++)
        {
            if (mine[i].CompareTo(theirs[i]) is var c and not 0)
            {
                return c;
            }
        }

        return mine.Count.CompareTo(theirs.Count);
    }

    // Sorts the array it owns in place, then hands it to the ImmutableArray without a copy.
    private static SortedArray<T> Sorted<T>(T[] items) where T : IComparable<T>
    {
        Array.Sort(items, typeof(T) == typeof(string) ? (IComparer<T>)StringComparer.Ordinal : Comparer<T>.Default);
        return new SortedArray<T>(ImmutableCollectionsMarshal.AsImmutableArray(items));
    }
}
