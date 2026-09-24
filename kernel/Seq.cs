using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Estate.Kernel;

/// <summary>
/// The kernel's one collection: immutable, sorted when it is built, and equal to another Seq exactly when their
/// elements are equal one by one. The order is the element type's own <see cref="IComparable{T}"/>, except that
/// strings sort ordinally, never by a culture; so the same elements given in any order build the same Seq, in
/// every process. An element type's order must return zero only for equal elements, as the kernel's types do. A
/// record holding a Seq compares by content, where one holding an array or an ImmutableArray would compare
/// references. default(Seq&lt;T&gt;) is the empty Seq.
/// </summary>
[CollectionBuilder(typeof(Seq), nameof(Seq.Of))]
public readonly struct Seq<T> : IReadOnlyList<T>, IEquatable<Seq<T>>
{
    private readonly ImmutableArray<T> _sorted;

    internal Seq(ImmutableArray<T> sorted) => _sorted = sorted;

    private ImmutableArray<T> Items => _sorted.IsDefault ? [] : _sorted;

    public int Count => Items.Length;

    public T this[int index] => Items[index];

    public static bool operator ==(Seq<T> left, Seq<T> right) => left.Equals(right);

    public static bool operator !=(Seq<T> left, Seq<T> right) => !left.Equals(right);

    public ImmutableArray<T>.Enumerator GetEnumerator() => Items.GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)Items).GetEnumerator();

    public bool Equals(Seq<T> other) => Items.AsSpan().SequenceEqual(other.Items.AsSpan(), EqualityComparer<T>.Default);

    public override bool Equals(object? obj) => obj is Seq<T> other && Equals(other);

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

/// <summary>Builds a <see cref="Seq{T}"/>: <c>Seq.Of(a, b)</c>, <c>Seq.Of(items)</c>, or a collection expression.</summary>
public static class Seq
{
    public static Seq<T> Of<T>(params ReadOnlySpan<T> items) where T : IComparable<T> => Sorted(items.ToArray());

    public static Seq<T> Of<T>(IEnumerable<T> items) where T : IComparable<T> => Sorted(items.ToArray());

    // Sorts the array it owns in place, then hands it to the ImmutableArray without a copy.
    private static Seq<T> Sorted<T>(T[] items) where T : IComparable<T>
    {
        Array.Sort(items, typeof(T) == typeof(string) ? (IComparer<T>)StringComparer.Ordinal : Comparer<T>.Default);
        return new Seq<T>(ImmutableCollectionsMarshal.AsImmutableArray(items));
    }
}
