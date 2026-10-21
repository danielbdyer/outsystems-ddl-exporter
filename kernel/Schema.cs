using System;
using System.Diagnostics;

namespace DbChange.Kernel;

/// <summary>
/// The typed schema the planter reads (V3_MILESTONES.md WP 3.1): every table of a model, with what a row of it must satisfy. Each table
/// holds its columns in ordinal order, each with its stored type, nullability, identity and default, or flagged computed or generated;
/// its primary key; its unique constraints and unique indexes; the references it makes, which <see cref="Order"/> orders the tables by;
/// its checks; its DML triggers; and its system versioning. <see cref="Of"/> reads it from a model's elements (Schema.Read.cs). It
/// holds no constraint's trust: DacFx's model holds none (a WITH NOCHECK foreign key or check extracts as a trusted one; measured on
/// DacFx 170.5.96), so WP 3.5's trust step reads sys.foreign_keys and sys.check_constraints' is_not_trusted from the environment. A
/// column set (XML COLUMN_SET FOR ALL_SPARSE_COLUMNS) reads as a nullable xml column, since the model marks it no other way. A copy's
/// expressions are SQL Server's normalized text and a package's are the text as written, so an expression of one is never compared with
/// one of the other. Tables sort by name, and the lists of a table each by its own key, which is unique within the table.
/// </summary>
public sealed partial record Schema(SortedArray<Table> Tables)
{
    /// <summary>The table of that name, or null when the schema holds none.</summary>
    public Table? Find(Name table)
    {
        var (low, high) = (0, Tables.Count - 1);
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            switch (Tables[middle].Name.CompareTo(table))
            {
                case 0: return Tables[middle];
                case < 0: low = middle + 1; break;
                default: high = middle - 1; break;
            }
        }

        return null;
    }
}

/// <summary>A table and what a row of it must satisfy; ordered by name.</summary>
public sealed record Table(Name Name, SortedArray<Column> Columns, PrimaryKey? Key, SortedArray<Unique> Uniques,
    SortedArray<Reference> References, SortedArray<Check> Checks, SortedArray<Trigger> Triggers, Versioning? Versioning)
    : IComparable<Table>
{
    /// <summary>The column of that one-part name, or null when the table holds none.</summary>
    public Column? Find(Name column)
    {
        foreach (var c in Columns)
        {
            if (c.Name == column)
            {
                return c;
            }
        }

        return null;
    }

    public int CompareTo(Table? other) => other is null ? 1 : Name.CompareTo(other.Name);
}

/// <summary>
/// A column of a table, at its ordinal (1 for the first, as sys.columns.column_id counts a new table's), with its one-part name and
/// whether it admits NULL. The cases are closed: a column a row gives a value, one SQL Server computes from an expression, and one SQL
/// Server generates (GENERATED ALWAYS). Columns sort by ordinal.
/// </summary>
public abstract record Column : IComparable<Column>
{
    private Column(int ordinal, Name name, bool nullable) => (Ordinal, Name, Nullable) = (ordinal, name, nullable);

    public int Ordinal { get; init; }

    public Name Name { get; init; }

    public bool Nullable { get; init; }

    public T Match<T>(Func<Stored, T> stored, Func<Computed, T> computed, Func<Generated, T> generated) => this switch
    {
        Stored s => stored(s),
        Computed c => computed(c),
        Generated g => generated(g),
        _ => throw new UnreachableException(),
    };

    public int CompareTo(Column? other) =>
        other is null ? 1 : Ordinal.CompareTo(other.Ordinal) is var c and not 0 ? c : Name.CompareTo(other.Name);

    /// <summary>
    /// A column a row gives a value, or its default or identity does: the type SQL Server stores (an alias resolved to its base type with
    /// the alias's facets, <see cref="ColumnType.Stored"/>), the alias it declares, its identity, its default, and whether it is SPARSE or
    /// the ROWGUIDCOL. A rowversion column is one of these, by its type; SQL Server fills it.
    /// </summary>
    public sealed record Stored(int Ordinal, Name Name, bool Nullable, ColumnType Type, Name? Alias, Identity? Identity, Default? Default,
        bool Sparse, bool RowGuid) : Column(Ordinal, Name, Nullable);

    /// <summary>A column declared AS (expression), which SQL Server computes and no INSERT names; a persisted one is stored. DacFx gives it
    /// no type. Its nullability is DacFx's PersistedNullable when it is persisted (a PERSISTED NOT NULL column reads Nullable true and
    /// PersistedNullable false, measured), and its Nullable otherwise.</summary>
    public sealed record Computed(int Ordinal, Name Name, bool Nullable, Value.Script Expression, bool Persisted)
        : Column(Ordinal, Name, Nullable);

    /// <summary>A column declared GENERATED ALWAYS, which SQL Server fills and no INSERT names: a period column of a system-versioned
    /// table, or another generated kind (a transaction id or a sequence number).</summary>
    public sealed record Generated(int Ordinal, Name Name, bool Nullable, ColumnType Type, GeneratedAs As)
        : Column(Ordinal, Name, Nullable);
}

/// <summary>What SQL Server generates into a GENERATED ALWAYS column: a period's start or end, or another kind.</summary>
public enum GeneratedAs
{
    RowStart,
    RowEnd,
    Other,
}

/// <summary>An IDENTITY column's seed and increment, as SQL Server holds them for a bigint at most.</summary>
public readonly record struct Identity(long Seed, long Increment);

/// <summary>
/// A column's default: its constraint's two-part name, none for an unnamed one; its expression as the model holds it; and the sequence a
/// NEXT VALUE FOR in it draws from (PLT-33, tests/Cases/20-planter.md), none when it draws from no sequence.
/// </summary>
public sealed record Default(Name? Constraint, Value.Script Expression, Name? Sequence);

/// <summary>A column of a key or a unique, at its position in the key from 0, and whether the key sorts it descending.</summary>
public sealed record KeyColumn(int Position, Name Column, bool Descending) : IComparable<KeyColumn>
{
    public int CompareTo(KeyColumn? other) =>
        other is null ? 1 : Position.CompareTo(other.Position) is var c and not 0 ? c : Column.CompareTo(other.Column);
}

/// <summary>A table's primary key: its constraint's two-part name, none for an unnamed one, its columns in key order, and whether it is
/// the clustered index.</summary>
public sealed record PrimaryKey(Name? Constraint, SortedArray<KeyColumn> Columns, bool Clustered);

/// <summary>
/// A unique constraint, or a unique index: its element's key; its name, two-part for a named constraint, one-part for an index, none for
/// an unnamed constraint; its columns in key order; whether it is an index; an index's filter (WHERE), as the model holds it; and whether
/// it is disabled. Uniques sort by key.
/// </summary>
public sealed record Unique(ElementKey Key, Name? Constraint, SortedArray<KeyColumn> Columns, bool IsIndex, Value.Script? Filter,
    bool Disabled) : IComparable<Unique>
{
    public int CompareTo(Unique? other) => other is null ? 1 : Key.CompareTo(other.Key);
}

/// <summary>What a foreign key does to the referencing rows when the referenced row is deleted or its key updated.</summary>
public enum ReferentialAction
{
    NoAction,
    Cascade,
    SetNull,
    SetDefault,
}

/// <summary>
/// A foreign key the table declares: its element's key; its constraint's two-part name, none for an unnamed one; the table it references;
/// its columns paired with the referenced columns, in key order; its actions; whether it is disabled (NOCHECK); and whether a row may
/// leave it unmet, which <see cref="Order"/> defers: a disabled key checks nothing, and under MATCH SIMPLE a NULL in any of its columns
/// switches the check off for that row (PLT-52, PLT-125). References sort by key.
/// </summary>
public sealed record Reference(ElementKey Key, Name? Constraint, Name Parent, SortedArray<Reference.Leg> Legs, ReferentialAction OnDelete,
    ReferentialAction OnUpdate, bool Disabled, bool Optional) : IComparable<Reference>
{
    public int CompareTo(Reference? other) => other is null ? 1 : Key.CompareTo(other.Key);

    /// <summary>A column of the foreign key, at its position from 0, and the parent's column it references.</summary>
    public sealed record Leg(int Position, Name Column, Name ParentColumn) : IComparable<Leg>
    {
        public int CompareTo(Leg? other) =>
            other is null ? 1 : Position.CompareTo(other.Position) is var c and not 0 ? c : Column.CompareTo(other.Column);
    }
}

/// <summary>A check constraint: its element's key; its two-part name, none for an unnamed one; its expression as the model holds it; and
/// whether it is disabled. Checks sort by key.</summary>
public sealed record Check(ElementKey Key, Name? Constraint, Value.Script Expression, bool Disabled) : IComparable<Check>
{
    public int CompareTo(Check? other) => other is null ? 1 : Key.CompareTo(other.Key);
}

/// <summary>A DML trigger on the table: its two-part name, whether it is disabled, and whether it fires INSTEAD OF the statement. Triggers
/// sort by name.</summary>
public sealed record Trigger(Name Name, bool Disabled, bool InsteadOf) : IComparable<Trigger>
{
    public int CompareTo(Trigger? other) => other is null ? 1 : Name.CompareTo(other.Name);
}

/// <summary>
/// A table's part in system versioning (PLT-34, PLT-35): the current table, with its history table and its period columns, or the history
/// table, with the current table it holds the history of. The cases are closed.
/// </summary>
public abstract record Versioning
{
    private Versioning()
    {
    }

    public T Match<T>(Func<Current, T> current, Func<History, T> history) => this switch
    {
        Current c => current(c),
        History h => history(h),
        _ => throw new UnreachableException(),
    };

    /// <summary>The current table of SYSTEM_VERSIONING = ON: its history table and its PERIOD FOR SYSTEM_TIME columns.</summary>
    public sealed record Current(Name HistoryTable, Name PeriodStart, Name PeriodEnd) : Versioning;

    /// <summary>The history table: the current table whose old rows SQL Server moves into it.</summary>
    public sealed record History(Name CurrentTable) : Versioning;
}
