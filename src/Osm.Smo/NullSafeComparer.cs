using System.Collections.Generic;

namespace Osm.Smo;

/// <summary>
/// Base comparer that factors out the identical null-handling preamble
/// (reference-equal → 0, left-null → -1, right-null → 1) shared by the SMO
/// ordering comparers, leaving each subclass to express only its key chain.
/// </summary>
internal abstract class NullSafeComparer<T> : IComparer<T>
    where T : class
{
    public int Compare(T? x, T? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        return CompareNonNull(x, y);
    }

    protected abstract int CompareNonNull(T x, T y);
}
