using System;
using System.Collections.Generic;
using System.Linq;
using DbChange.Kernel;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using ColumnType = DbChange.Kernel.ColumnType;
using ScriptLiteral = Microsoft.SqlServer.TransactSql.ScriptDom.Literal;

namespace DbChange.Io;

/// <summary>
/// ForeignKey and Unique, each joining the table aliased t to a derived table of its own, grouped so that each row of t matches one row of
/// it at most. ForeignKey: the child rows whose key columns are all non-null and match no parent row, the parent's keys grouped under the
/// collation they are compared in; a parent the change creates holds no row, so every child row with a non-null key is an orphan. Unique:
/// the rows of duplicate groups over the key, under the constraint's own null semantics (NULLs equal, as a unique index reads them) and
/// within its filter, and the rows of the groups of each collation class (the Facsimile's): exact, the same bytes twice; trailing space,
/// one value with and without trailing spaces; case and accent, values the collation the index is built with reads as one and a case- or
/// accent-sensitive one as two; and two NULLs. The groups are read in three steps, each a GROUP BY the allowlist admits: by the bytes, by
/// a binary collation, which ignores trailing spaces, then by the index's collation. A key over a column the change creates reads the value
/// every existing row holds, so the table's rows are one group. A key or a foreign key over a column the change converts to another kind
/// of type, or rounds, reads not predictable read-only, since the conversion can make two values one.
/// </summary>
public static partial class AggregateQueries
{
    private static Asked ForeignKey(Coordinates.Counted counted, PreconditionKind.ForeignKey key, Context context)
    {
        var precondition = counted.Precondition;
        var children = key.Pairs.Select(p => context.Column(counted, precondition.Table, p.Column)).ToList();
        if (children.IndexOf(null) is var missing and >= 0)
        {
            return Asked.Not(Unplaced(key.Pairs[missing].Column));
        }

        if (children.FirstOrDefault(c => Converted(c!) is not null) is { } converted)
        {
            return Asked.Not(Converted(converted)!);
        }

        var values = children.Select(c => Operand(c!, "t")).ToList();
        if (values.Contains(null))
        {
            return Asked.Not("a column the change creates has a default that does not parse");
        }

        var named = values.Select(v => (BooleanExpression)Is(v!, isNull: false));
        if (counted.Places.First(p => p.After.Parent is null && p.After.Name == key.Parent).Environment is not { } parent)
        {
            return Part.Violating(precondition, All(named));
        }

        var referenced = key.Pairs.Select(p => context.Column(counted, key.Parent, p.Referenced)).ToList();
        if (referenced.FirstOrDefault(r => r?.Name is null) is { } created)
        {
            return Asked.Not("the change creates " + created.Place.After + " on the parent, which every existing row holds as one value");
        }

        if (referenced.FirstOrDefault(r => Converted(r!) is not null) is { } retyped)
        {
            return Asked.Not(Converted(retyped)!);
        }

        // The parent's keys grouped, each compared under the parent column's collation after the change where both columns hold strings.
        var alias = context.Alias("p");
        var collations = referenced.Select((r, i) => Character(r!) && Character(children[i]!) ? context.Collation(r!.After) : null)
            .ToList();
        var keys = referenced.Select((r, i) => (ScalarExpression)ColumnRef(null, r!.Name!, collations[i])).ToList();
        var grouped = Derived(Query(keys.Select((k, i) => (k, (string?)("k" + (i + 1)))), Named(parent.Name, null), null, keys), alias);
        var on = All(values.Select((v, i) => (BooleanExpression)Compare(ColumnRef(alias, "k" + (i + 1)), BooleanComparisonType.Equals,
            collations[i] is { } collation && children[i]!.Name is { } child ? ColumnRef("t", child, collation) : v!)));
        return Part.Violating(precondition, All([.. named, Is(ColumnRef(alias, "k1"), isNull: true)]), (grouped, on));
    }

    private static Asked Unique(Coordinates.Counted counted, PreconditionKind.Unique unique, Context context)
    {
        var precondition = counted.Precondition;
        var keys = precondition.Columns.Select(c => context.Column(counted, precondition.Table, c)).ToList();
        if (keys.IndexOf(null) is var missing and >= 0)
        {
            return Asked.Not(Unplaced(precondition.Columns[missing]));
        }

        var (inner, outer) = ((BooleanExpression?)null, (BooleanExpression?)null);
        if (unique.Filter is { } filter)
        {
            var columns = unique.FilterColumns.Select(c => context.Column(counted, precondition.Table, c)).OfType<Column>().ToList();
            var (within, whyWithin) = Filtered(filter, columns, null, context);
            var (over, whyOver) = Filtered(filter, columns, "t", context);
            if ((whyWithin ?? whyOver) is { } why)
            {
                return Asked.Not(why);
            }

            (inner, outer) = (within, over);
        }

        var existing = keys.Where(k => k!.Name is not null).Select(k => k!).ToList();
        var created = keys.Where(k => k!.Name is null).ToList();
        if (created.FirstOrDefault(c => c!.Place.Default is { } text && !(TSql.Scalar(text) is { } value && IsLiteral(value)))
            is { } varying)
        {
            return Asked.Not(varying.Place.After + " is created with the default " + varying.Place.Default
                + ", which is no literal, so the value each existing row holds is not known read-only");
        }

        if (existing.FirstOrDefault(k => Converted(k) is not null) is { } retyped)
        {
            return Asked.Not(Converted(retyped)!);
        }

        var nulled = created.Any(c => c!.Place.Default is null);
        if (existing.Count == 0)
        {
            // Every existing row holds the one key the change gives it, so the rows within the filter are one group.
            (long, IReadOnlyList<long>) Group(long rows) => rows >= 2 ? (rows, [rows]) : (0, [0]);
            Class[] classes = [nulled ? Class.Nulls : Class.Exact];
            return outer is null ? new Part(precondition, [], [], classes, (rows, _) => Group(rows))
                : new Part(precondition, [(Sum(outer), null)], [], classes, (_, values) => Group(values[0]));
        }

        var nulls = NullsKept(unique.Filter, existing, context);
        return Grouped(precondition, TableOf(counted), existing, nulled, inner, outer, nulls, context);
    }

    /// <summary>
    /// The duplicate groups of the key's columns the environment holds, and the rows of each collation class: by the bytes, by a binary
    /// collation and by the index's for a key with a string column; by the values alone for one without.
    /// </summary>
    private static Asked Grouped(Precondition precondition, Name table, IReadOnlyList<Column> keys, bool nulled, BooleanExpression? inner,
        BooleanExpression? outer, bool nulls, Context context)
    {
        var alias = context.Alias("u");
        ColumnReferenceExpression At(string column) => ColumnRef(alias, column);
        string Key(int i) => "k" + (i + 1);
        var strings = keys.Select(Character).ToList();
        var nullable = keys.Select((k, i) => (k.Nullable, Key: Key(i))).Where(k => k.Nullable).Select(k => k.Key).ToList();
        var on = All(keys.Select((k, i) =>
        {
            BooleanExpression equal = Compare(At(Key(i)), BooleanComparisonType.Equals,
                ColumnRef("t", k.Name!, strings[i] ? context.Collation(k.After) : null));
            return k.Nullable ? Any([equal, All([Is(At(Key(i)), true), Is(ColumnRef("t", k.Name!), true)])]) : equal;
        }));
        var held = nullable.Count == 0 ? null : All(nullable.Select(k => (BooleanExpression)Is(At(k), isNull: false)));
        var classes = new List<(Class Class, BooleanExpression When)>();
        BooleanExpression duplicate;
        QueryDerivedTable grouped;
        if (!strings.Contains(true))
        {
            var columns = keys.Select(k => (ScalarExpression)ColumnRef(null, k.Name!)).ToList();
            grouped = Derived(Query([.. columns.Select((c, i) => (c, (string?)Key(i))), (CountRows(), "v")], Named(table, null), inner,
                columns), alias);
            duplicate = Exceeds(At("v"), 1);
            classes.Add((Class.Exact, duplicate));
        }
        else
        {
            if (keys.Select((k, i) => strings[i] && Bin2(context.Collation(k.Before), Unicode(k.Before)) is null ? k : null)
                .FirstOrDefault(k => k is not null) is { } unnamed)
            {
                return Asked.Not("dbchange names no binary collation for the code page of " + context.Collation(unnamed.Before));
            }

            grouped = Derived(Collated(table, keys, strings, inner, context), alias);
            duplicate = Any([Exceeds(At("v"), 1), Exceeds(At("e"), 0), Exceeds(At("b"), 0)]);
            (bool Case, bool Accent, string Key) Insensitive(Column k, int i) =>
                strings[i] && Kernel.Collation.Of(context.Collation(k.After)) is Result<Kernel.Collation>.Ok { Value: var c }
                    ? (!c.IsCaseSensitive, !c.IsAccentSensitive, Key(i)) : (false, false, Key(i));
            var sensitivity = keys.Select(Insensitive).ToList();
            classes.Add((Class.Exact, Exceeds(At("e"), 0)));
            if (sensitivity.Any(s => s.Case))
            {
                classes.Add((Class.Case, Any(sensitivity.Where(s => s.Case).Select(s => (BooleanExpression)(s.Accent
                    ? Compare(At("c" + s.Key[1..]), BooleanComparisonType.NotEqualToBrackets, At("a" + s.Key[1..]))
                    : Exceeds(At("c" + s.Key[1..]), 1))))));
            }

            if (sensitivity.Any(s => s.Accent))
            {
                classes.Add((Class.Accent,
                    Any(sensitivity.Where(s => s.Accent).Select(s => (BooleanExpression)Exceeds(At("a" + s.Key[1..]), 1)))));
            }

            classes.Add((Class.TrailingSpace, Exceeds(At("b"), 0)));
        }

        // A group whose key holds a NULL, as a unique index holds two NULLs equal, is of the class two NULLs and of no other.
        var counted = new List<(Class Class, BooleanExpression When)>();
        if (nulled)
        {
            counted.Add((Class.Nulls, duplicate));
        }
        else
        {
            counted.AddRange(classes.Select(c => (c.Class, held is null ? c.When : All([c.When, held]))));
            if (nulls && held is not null)
            {
                counted.Add((Class.Nulls, All([duplicate, Any(nullable.Select(k => (BooleanExpression)Is(At(k), isNull: true)))])));
            }
        }

        BooleanExpression Within(BooleanExpression when) => outer is null ? when : All([when, outer]);
        return new Part(precondition,
            [(Sum(Within(duplicate)), null), .. counted.Select(c => ((ScalarExpression)Sum(Within(c.When)), (Class?)c.Class))],
            [(grouped, on)], [.. counted.Select(c => c.Class)], (_, values) => (values[0], [.. values.Skip(1)]));
    }

    /// <summary>
    /// A key with a string column grouped in three steps, so that each step's GROUP BY is a column or its length: l0 by the bytes, the
    /// string columns under their binary collation beside their DATALENGTH, n counting a value's rows; l1 by the binary collation, which
    /// ignores trailing spaces, b counting a value's variants in bytes and e those that occur twice; and the key by the index's collation,
    /// v counting its binary values, with each string column's values counted under the case- and accent-sensitive forms of that collation.
    /// </summary>
    private static QuerySpecification Collated(Name table, IReadOnlyList<Column> keys, IReadOnlyList<bool> strings,
        BooleanExpression? inner, Context context)
    {
        string Key(int i) => "k" + (i + 1);
        var bytes = keys.Select((k, i) => (ScalarExpression)ColumnRef(null, k.Name!,
            strings[i] ? Bin2(context.Collation(k.Before), Unicode(k.Before)) : null)).ToList();
        var lengths = keys.Where((_, i) => strings[i]).Select(k => (ScalarExpression)Call("DATALENGTH", ColumnRef(null, k.Name!)));
        var l0 = Query([.. bytes.Select((b, i) => (b, (string?)Key(i))), (CountRows(), "n")], Named(table, null), inner,
            [.. bytes, .. lengths]);
        var binaries = keys.Select((_, i) => (ScalarExpression)ColumnRef("l0", Key(i))).ToList();
        var l1 = Query([.. binaries.Select((b, i) => (b, (string?)Key(i))), (CountRows(), "b"),
            (Sum(Exceeds(ColumnRef("l0", "n"), 1)), "e")], Derived(l0, "l0"), null, binaries);
        var collated = keys.Select((k, i) => (ScalarExpression)ColumnRef("l1", Key(i), strings[i] ? context.Collation(k.After) : null))
            .ToList();
        var counts = new List<(ScalarExpression, string?)>();
        foreach (var (k, i) in keys.Select((k, i) => (k, i)).Where(k => strings[k.i]))
        {
            var collation = context.Collation(k.After);
            if (Kernel.Collation.Of(collation) is Result<Kernel.Collation>.Ok { Value: var c }
                && !(c.IsCaseSensitive && c.IsAccentSensitive))
            {
                var name = (c.IsCaseSensitive ? "a" : "c") + (i + 1);
                counts.Add((Distinct("l1", Key(i), Sensitive(collation, toCase: true, toAccent: true)), name));
                counts.AddRange(!c.IsCaseSensitive && !c.IsAccentSensitive
                    ? [(Distinct("l1", Key(i), Sensitive(collation, toCase: false, toAccent: true)), "a" + (i + 1))] : []);
            }
        }

        return Query([.. collated.Select((c, i) => (c, (string?)Key(i))), (CountRows(), "v"),
            (Sum(Exceeds(ColumnRef("l1", "e"), 0)), "e"), (Sum(Exceeds(ColumnRef("l1", "b"), 1)), "b"), .. counts],
            Derived(l1, "l1"), null, collated);
    }

    /// <summary>COUNT_BIG(DISTINCT the column under the collation given): its values the collation reads as distinct.</summary>
    private static FunctionCall Distinct(string qualifier, string column, string collation)
    {
        var count = Call("COUNT_BIG", ColumnRef(qualifier, column, collation));
        count.UniqueRowFilter = UniqueRowFilter.Distinct;
        return count;
    }

    /// <summary>A collation's form sensitive to case, to accents, or to both, its other tokens kept: Latin1_General_CI_AI to
    /// Latin1_General_CS_AS or Latin1_General_CI_AS.</summary>
    private static string Sensitive(string collation, bool toCase, bool toAccent) => string.Join('_', collation.Split('_')
        .Select(t => t == "CI" && toCase ? "CS" : t == "AI" && toAccent ? "AS" : t));

    /// <summary>
    /// The binary collation of a string column's code page, under which the bytes compare: Latin1_General_BIN2 for a Unicode column or one
    /// of SQL_Latin1_General_CP1; a SQL collation's own binary form for code pages 437 and 850; a Windows collation's binary form, its
    /// version and UTF8 kept; and none for another SQL collation's code page.
    /// </summary>
    private static string? Bin2(string collation, bool unicode)
    {
        if (unicode || collation.StartsWith("SQL_Latin1_General_CP1_", StringComparison.Ordinal))
        {
            return "Latin1_General_BIN2";
        }

        foreach (var page in (string[])["CP437", "CP850"])
        {
            if (collation.StartsWith("SQL_Latin1_General_" + page + "_", StringComparison.Ordinal))
            {
                return "SQL_Latin1_General_" + page + "_BIN2";
            }
        }

        var tokens = collation.Split('_');
        return collation.StartsWith("SQL_", StringComparison.Ordinal) ? null
            : string.Join('_', tokens.TakeWhile(t => t is not ("CI" or "CS" or "BIN" or "BIN2"))) + "_BIN2"
                + (tokens.Contains("UTF8") ? "_UTF8" : "");
    }

    /// <summary>Whether a key's two NULLs can be a duplicate: a key column allows NULL and the filter, where one stands, holds no
    /// &lt;column&gt; IS NOT NULL of its own for each column that does.</summary>
    private static bool NullsKept(string? filter, IReadOnlyList<Column> keys, Context context)
    {
        var excluded = new List<string>();
        void Conjuncts(BooleanExpression b)
        {
            switch (b)
            {
                case BooleanBinaryExpression { BinaryExpressionType: BooleanBinaryExpressionType.And } and:
                    Conjuncts(and.FirstExpression);
                    Conjuncts(and.SecondExpression);
                    break;
                case BooleanParenthesisExpression p:
                    Conjuncts(p.Expression);
                    break;
                case BooleanIsNullExpression { IsNot: true, Expression: ColumnReferenceExpression c }:
                    excluded.Add(c.MultiPartIdentifier.Identifiers[^1].Value);
                    break;
            }
        }

        if (filter is not null && TSql.Predicate(filter) is { } parsed)
        {
            Conjuncts(parsed);
        }

        return keys.Where(k => k.Nullable)
            .Any(k => !excluded.Any(e => string.Equals(e, k.Place.After.Name.Base, context.Database.Comparison)));
    }

    /// <summary>Why a column a key reads cannot be grouped read-only: the change converts it to another kind of type, or rounds it, before
    /// the key is validated; null when it keeps its values.</summary>
    private static string? Converted(Column column) =>
        column is { Before: { } before, After: { } after } && (FamilyOf(before) != FamilyOf(after)
            || (Fraction(after) is { } lesser && Fraction(before) is { } greater && lesser < greater))
            ? "the change converts " + column.Place.After + " from " + Written(before) + " to " + Written(after)
                + " before the key is validated, which can make two of its values one"
            : null;

    private static bool Character(Column column) => column.Before is { } type && FamilyOf(type) == Family.Character;

    private static bool Unicode(ColumnType? type) => type?.Type.Base.ToLowerInvariant() is "nchar" or "nvarchar" or "sysname";

    private static bool IsLiteral(ScalarExpression value) => value switch
    {
        ParenthesisExpression p => IsLiteral(p.Expression),
        UnaryExpression u => IsLiteral(u.Expression),
        _ => value is ScriptLiteral,
    };
}
