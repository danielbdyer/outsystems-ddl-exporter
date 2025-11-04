using System;

namespace Osm.Smo;

internal static class SmoNormalization
{
    public static string? NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    public static string? NormalizeSqlExpression(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return TrimRedundantParentheses(trimmed);
    }

    private static string TrimRedundantParentheses(string value)
    {
        var candidate = value;

        while (HasRedundantOuterParentheses(candidate))
        {
            var inner = candidate[1..^1].Trim();
            if (inner.Length == 0)
            {
                break;
            }

            if (inner.IndexOf('\'') >= 0)
            {
                break;
            }

            candidate = inner;
        }

        return candidate;
    }

    private static bool HasRedundantOuterParentheses(string value)
    {
        if (value.Length < 2)
        {
            return false;
        }

        if (value[0] != '(' || value[^1] != ')')
        {
            return false;
        }

        var depth = 0;

        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];

            if (ch == '\'')
            {
                i = SkipStringLiteral(value, i);
                continue;
            }

            if (ch == '(')
            {
                depth++;
                continue;
            }

            if (ch != ')')
            {
                continue;
            }

            depth--;
            if (depth < 0)
            {
                return false;
            }

            if (depth == 0 && i < value.Length - 1)
            {
                return false;
            }
        }

        return depth == 0;
    }

    private static int SkipStringLiteral(string value, int startIndex)
    {
        var index = startIndex + 1;

        while (index < value.Length)
        {
            if (value[index] != '\'')
            {
                index++;
                continue;
            }

            if (index + 1 < value.Length && value[index + 1] == '\'')
            {
                index += 2;
                continue;
            }

            return index;
        }

        return value.Length - 1;
    }
}
