using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;

namespace Osm.Emission.Seeds;

public static class EntitySeedDeterminizer
{
    public static ImmutableArray<StaticEntityTableData> Normalize(IReadOnlyList<StaticEntityTableData> tables)
    {
        if (tables is null)
        {
            throw new ArgumentNullException(nameof(tables));
        }

        if (tables.Count == 0)
        {
            return ImmutableArray<StaticEntityTableData>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<StaticEntityTableData>(tables.Count);
        foreach (var table in tables)
        {
            if (table is null)
            {
                continue;
            }

            var sortedRows = table.Rows.IsDefaultOrEmpty
                ? ImmutableArray<StaticEntityRow>.Empty
                : table.Rows.Sort(StaticEntityRowComparer.Create(table.Definition));

            builder.Add(new StaticEntityTableData(table.Definition, sortedRows));
        }

        return builder.ToImmutable();
    }

    private sealed class StaticEntityRowComparer : IComparer<StaticEntityRow>
    {
        private readonly StaticEntitySeedTableDefinition _definition;
        private readonly int[] _sortIndices;

        private StaticEntityRowComparer(StaticEntitySeedTableDefinition definition, int[] sortIndices)
        {
            _definition = definition;
            _sortIndices = sortIndices;
        }

        public static StaticEntityRowComparer Create(StaticEntitySeedTableDefinition definition)
        {
            if (definition is null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            var indices = new List<int>();
            for (var i = 0; i < definition.Columns.Length; i++)
            {
                if (definition.Columns[i].IsPrimaryKey)
                {
                    indices.Add(i);
                }
            }

            if (indices.Count == 0)
            {
                for (var i = 0; i < definition.Columns.Length; i++)
                {
                    indices.Add(i);
                }
            }

            return new StaticEntityRowComparer(definition, indices.ToArray());
        }

        public int Compare(StaticEntityRow? x, StaticEntityRow? y)
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

            foreach (var index in _sortIndices)
            {
                var left = index < x.Values.Length ? x.Values[index] : null;
                var right = index < y.Values.Length ? y.Values[index] : null;
                var comparison = CompareValues(left, right);
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            for (var i = 0; i < Math.Min(x.Values.Length, y.Values.Length); i++)
            {
                var comparison = CompareValues(x.Values[i], y.Values[i]);
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return x.Values.Length.CompareTo(y.Values.Length);
        }

        private static int CompareValues(object? left, object? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            if (IsNumeric(left) && IsNumeric(right))
            {
                try
                {
                    var leftDecimal = Convert.ToDecimal(left, CultureInfo.InvariantCulture);
                    var rightDecimal = Convert.ToDecimal(right, CultureInfo.InvariantCulture);
                    return leftDecimal.CompareTo(rightDecimal);
                }
                catch (FormatException)
                {
                }
                catch (InvalidCastException)
                {
                }
            }

            if (left is DateTime leftDateTime && right is DateTime rightDateTime)
            {
                return leftDateTime.CompareTo(rightDateTime);
            }

            if (left is DateTimeOffset leftOffset && right is DateTimeOffset rightOffset)
            {
                return leftOffset.CompareTo(rightOffset);
            }

            if (left is DateOnly leftDate && right is DateOnly rightDate)
            {
                return leftDate.CompareTo(rightDate);
            }

            if (left is TimeOnly leftTime && right is TimeOnly rightTime)
            {
                return leftTime.CompareTo(rightTime);
            }

            if (left is TimeSpan leftSpan && right is TimeSpan rightSpan)
            {
                return leftSpan.CompareTo(rightSpan);
            }

            if (left is Guid leftGuid && right is Guid rightGuid)
            {
                return leftGuid.CompareTo(rightGuid);
            }

            if (left is bool leftBool && right is bool rightBool)
            {
                return leftBool.CompareTo(rightBool);
            }

            if (left is byte[] leftBytes && right is byte[] rightBytes)
            {
                return CompareByteArrays(leftBytes, rightBytes);
            }

            if (left is IComparable comparable && left.GetType() == right.GetType())
            {
                return comparable.CompareTo(right);
            }

            var leftText = Convert.ToString(left, CultureInfo.InvariantCulture) ?? string.Empty;
            var rightText = Convert.ToString(right, CultureInfo.InvariantCulture) ?? string.Empty;
            return string.CompareOrdinal(leftText, rightText);
        }

        private static bool IsNumeric(object value)
            => value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

        private static int CompareByteArrays(IReadOnlyList<byte> left, IReadOnlyList<byte> right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            var lengthComparison = left.Count.CompareTo(right.Count);
            if (lengthComparison != 0)
            {
                return lengthComparison;
            }

            for (var i = 0; i < left.Count; i++)
            {
                var comparison = left[i].CompareTo(right[i]);
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return 0;
        }
    }
}
