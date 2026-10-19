using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using static DbChange.Kernel.Tests.KernelProperties;

namespace DbChange.Kernel.Tests;

/// <summary>
/// A model as the Facsimile's cases write one, built into the elements io/Ssdt.ReadModel reads from DacFx 170.5.96 (measured on models
/// DacFx built from T-SQL): each column written as "Customer.Email nvarchar(256) NULL", a table named in two parts being in dbo, and each
/// table made from the columns written for it, in order; and the keys, indexes, constraints and defaults over them, each keyed as io keys
/// it (a named constraint at the top level in its table's schema, an index under its table) with the relationships DacFx gives it. A
/// column's facets are DacFx's: a length for the string and binary types (IsMax for max), a precision and scale for decimal and numeric
/// (18 and 0 when none is written), a scale for datetime2, time and datetimeoffset (7 when none is written), a precision for float (53),
/// and 0 elsewhere; its collation is written after its nullability, and none is the database's.
/// </summary>
internal sealed record Model(ImmutableList<Model.Column> Columns, ImmutableList<Model.Rule> Rules)
{
    private static readonly Regex Spec = new(
        @"^(?<path>[\w.]+) (?<type>[\w.]+)(?:\((?<first>max|\d+)(?:,\s*(?<second>\d+))?\))?"
            + @"(?<nullable> NOT NULL| NULL)?(?: (?<collation>\w+))?$",
        RegexOptions.CultureInvariant);

    public static readonly Model Empty = new([], []);

    public static Model Of(params string[] columns) => Empty.With(columns);

    /// <summary>The elements io would read from this model, sorted as a model is.</summary>
    public SortedArray<Element> Elements => SortedArray.Of(Build());

    /// <summary>The model with each column written added after its table's last, or put in place of the column of its name.</summary>
    public Model With(params string[] columns) => columns.Select(Parse).Aggregate(this, (model, column) =>
        model.Columns.FindIndex(c => (c.Schema, c.Table, c.Name) == (column.Schema, column.Table, column.Name)) is var at and >= 0
            ? model with { Columns = model.Columns.SetItem(at, column) }
            : model with { Columns = model.Columns.Add(column) });

    /// <summary>The model without the column written as Customer.Email, and without every rule over it.</summary>
    public Model WithoutColumn(string column)
    {
        var (schema, table, name) = ColumnPath(column);
        return new Model(
            Columns.RemoveAll(c => (c.Schema, c.Table, c.Name) == (schema, table, name)),
            Rules.RemoveAll(r => ((r.Schema, r.Table) == (schema, table) && r.Columns.Concat(r.Reads).Contains(name))
                || (r.Parent == (schema, table) && r.ParentColumns.Contains(name))));
    }

    /// <summary>The model without the table written as OrderLine or ref.Status, and without every rule on it or referencing it.</summary>
    public Model WithoutTable(string table)
    {
        var at = TablePath(table);
        return new Model(Columns.RemoveAll(c => (c.Schema, c.Table) == at),
            Rules.RemoveAll(r => (r.Schema, r.Table) == at || r.Parent == at));
    }

    /// <summary>The model with a column renamed in place, and every rule over it naming the new name.</summary>
    public Model RenameColumn(string column, string to)
    {
        var (schema, table, name) = ColumnPath(column);
        string[] Renamed(string[] columns, bool on) => on ? [.. columns.Select(c => c == name ? to : c)] : columns;
        return new Model(
            Columns.Select(c => (c.Schema, c.Table, c.Name) == (schema, table, name) ? c with { Name = to } : c).ToImmutableList(),
            Rules.Select(r => r with
            {
                Columns = Renamed(r.Columns, (r.Schema, r.Table) == (schema, table)),
                Reads = Renamed(r.Reads, (r.Schema, r.Table) == (schema, table)),
                ParentColumns = Renamed(r.ParentColumns, r.Parent == (schema, table)),
            }).ToImmutableList());
    }

    /// <summary>
    /// The model with a table renamed, or moved to another schema: its columns and the rules on it go with it, each named constraint on it
    /// into its new schema as ALTER SCHEMA … TRANSFER takes it, and each foreign key referencing it names it where it is now.
    /// </summary>
    public Model MoveTable(string table, string toSchema, string? toName = null)
    {
        var from = TablePath(table);
        (string, string) to = (toSchema, toName ?? from.Table);
        return new Model(
            Columns.Select(c => (c.Schema, c.Table) == from ? c with { Schema = to.Item1, Table = to.Item2 } : c).ToImmutableList(),
            Rules.Select(r => (r.Schema, r.Table) == from ? r with { Schema = to.Item1, Table = to.Item2 } : r)
                .Select(r => r.Parent == from ? r with { Parent = to } : r).ToImmutableList());
    }

    public Model PrimaryKey(string name, string table, params string[] columns) => Add("PrimaryKeyConstraint", name, table, columns);

    public Model UniqueConstraint(string name, string table, params string[] columns) => Add("UniqueConstraint", name, table, columns);

    public Model UniqueIndex(string name, string table, params string[] columns) => Add("Index", name, table, columns, unique: true);

    /// <summary>A unique index with a filter, as the model writes its FilterPredicate, over the columns the filter reads.</summary>
    public Model FilteredIndex(string name, string table, string[] columns, string filter, params string[] reads) =>
        Add("Index", name, table, columns, unique: true, text: filter, reads: reads);

    /// <summary>An index that is not unique, with the columns it includes.</summary>
    public Model Index(string name, string table, string[] columns, params string[] included) =>
        Add("Index", name, table, columns, included: included);

    public Model ForeignKey(string name, string table, string[] columns, string parent, string[] parentColumns,
        string onDelete = "NoAction", bool disabled = false) =>
        Add("ForeignKeyConstraint", name, table, columns, parent: parent, parentColumns: parentColumns, onDelete: onDelete,
            disabled: disabled);

    /// <summary>A check, its expression as the model writes it, over the columns it reads.</summary>
    public Model Check(string name, string table, string expression, string[] reads, bool disabled = false) =>
        Add("CheckConstraint", name, table, [], text: expression, reads: reads, disabled: disabled);

    /// <summary>A named default on a column, its expression as the model writes it.</summary>
    public Model Default(string name, string table, string column, string expression) =>
        Add("DefaultConstraint", name, table, [column], text: expression);

    /// <summary>The model without the rule of that name.</summary>
    public Model Dropping(string name) => this with { Rules = Rules.RemoveAll(r => r.Name == name) };

    /// <summary>The model with the rule of that name changed.</summary>
    public Model Changing(string name, Func<Rule, Rule> change) =>
        this with { Rules = Rules.Select(r => r.Name == name ? change(r) : r).ToImmutableList() };

    /// <summary>The name of a table written as Customer or ref.Status.</summary>
    public static Name TableName(string table)
    {
        var (schema, name) = TablePath(table);
        return Ok(Kernel.Name.Of(schema, name));
    }

    /// <summary>The key of a table written as Customer or ref.Status.</summary>
    public static ElementKey TableKey(string table) => Ok(ElementKey.Of("Table", TableName(table)));

    /// <summary>The key of a column written as Customer.Email or ref.Status.Code.</summary>
    public static ElementKey ColumnKey(string column)
    {
        var (schema, table, name) = ColumnPath(column);
        return Key(Key("Table", schema, table), "Column", name);
    }

    /// <summary>A column's type as the model holds it, written as nvarchar(50), dbo.ShortCode or nvarchar(50)
    /// Latin1_General_CS_AS.</summary>
    public static ColumnType Type(string written) => Parse("T.c " + written).Type;

    /// <summary>A column as the model holds it.</summary>
    public sealed record Column(string Schema, string Table, string Name, ColumnType Type, bool Nullable);

    /// <summary>
    /// A key, an index, a constraint or a default: its element type, its table's schema and the table, its name, its columns in order (a
    /// default's column); a foreign key's parent, the parent's columns and the delete action; a check's expression or a filtered index's
    /// filter, as <see cref="Text"/>, and the columns it reads; an index's included columns; whether an index is unique; and whether it is
    /// disabled.
    /// </summary>
    public sealed record Rule(string Type, string Schema, string Table, string Name, string[] Columns,
        (string Schema, string Table)? Parent, string[] ParentColumns, string? Text, string[] Reads, string[] Included, bool Unique,
        bool Disabled, string OnDelete);

    private Model Add(string type, string name, string table, string[] columns, bool unique = false, string? text = null,
        string[]? reads = null, string[]? included = null, string? parent = null, string[]? parentColumns = null,
        string onDelete = "NoAction", bool disabled = false)
    {
        var (schema, on) = TablePath(table);
        return this with
        {
            Rules = Rules.Add(new Rule(type, schema, on, name, columns, parent is null ? null : TablePath(parent), parentColumns ?? [],
                text, reads ?? [], included ?? [], unique, disabled, onDelete)),
        };
    }

    /// <summary>A column written as [schema.]Table.Column type[(n[,m])|(max)] [NULL|NOT NULL] [collation].</summary>
    private static Column Parse(string written)
    {
        var m = Spec.Match(written);
        if (!m.Success)
        {
            throw new ArgumentException("'" + written + "' is no column the cases write.", nameof(written));
        }

        var (schema, table, name) = ColumnPath(m.Groups["path"].Value);
        var type = m.Groups["type"].Value;
        var (first, second) = (m.Groups["first"].Value, m.Groups["second"].Value);
        long Number(string digits, long none) => digits.Length == 0 ? none : long.Parse(digits, CultureInfo.InvariantCulture);
        var (length, isMax, precision, scale) = type.Contains('.', StringComparison.Ordinal) ? (0L, false, 0L, 0L)
            : type switch
            {
                "char" or "varchar" or "nchar" or "nvarchar" or "binary" or "varbinary" =>
                    (first == "max" ? 0 : Number(first, 1), first == "max", 0, 0),
                "decimal" or "numeric" => (0, false, Number(first, 18), Number(second, 0)),
                "datetime2" or "time" or "datetimeoffset" => (0, false, 0, Number(first, 7)),
                "float" => (0, false, Number(first, 53), 0),
                _ => (0L, false, 0L, 0L),
            };
        var typeName = type.Split('.') is [var s, var n] ? Ok(Kernel.Name.Of(s, n)) : Ok(Kernel.Name.Of(type));
        var collation = m.Groups["collation"].Success ? m.Groups["collation"].Value : null;
        return new Column(schema, table, name, new ColumnType(typeName, length, isMax, precision, scale, collation),
            m.Groups["nullable"].Value != " NOT NULL");
    }

    /// <summary>A column written as [schema.]Table.Column, in dbo when no schema is written.</summary>
    private static (string Schema, string Table, string Column) ColumnPath(string written) => written.Split('.') switch
    {
        [var t, var c] => ("dbo", t, c),
        [var s, var t, var c] => (s, t, c),
        _ => throw new ArgumentException("'" + written + "' is no column the cases write.", nameof(written)),
    };

    /// <summary>A table written as [schema.]Table, in dbo when no schema is written.</summary>
    private static (string Schema, string Table) TablePath(string written) => written.Split('.') switch
    {
        [var t] => ("dbo", t),
        [var s, var t] => (s, t),
        _ => throw new ArgumentException("'" + written + "' is no table the cases write.", nameof(written)),
    };

    private IEnumerable<Element> Build()
    {
        ElementKey Table(string s, string t) => Key("Table", s, t);
        ElementKey[] Of(string s, string t, IEnumerable<string> cs) => [.. cs.Select(c => Key(Table(s, t), "Column", c))];

        foreach (var table in Columns.GroupBy(c => (c.Schema, c.Table)))
        {
            var (s, t) = table.Key;
            yield return New(Table(s, t), [],
                [("Columns", Of(s, t, table.Select(c => c.Name))), ("Schema", [Ok(ElementKey.Of("Schema", Ok(Kernel.Name.Of(s))))])]);
        }

        foreach (var c in Columns)
        {
            yield return New(Key(Table(c.Schema, c.Table), "Column", c.Name),
                [("Collation", c.Type.Collation is { } collation ? Text(collation) : new Value.Null()), ("Expression", new Value.Null()),
                    ("IsMax", Bool(c.Type.IsMax)), ("Length", Int(c.Type.Length)), ("Nullable", Bool(c.Nullable)),
                    ("Precision", Int(c.Type.Precision)), ("Scale", Int(c.Type.Scale))],
                [("DataType", [Ok(ElementKey.Of("DataType", c.Type.Type))])]);
        }

        foreach (var r in Rules)
        {
            var host = Table(r.Schema, r.Table);
            var columns = Of(r.Schema, r.Table, r.Columns);
            var reads = Of(r.Schema, r.Table, r.Reads);
            var disabled = ("Disabled", Bool(r.Disabled));
            yield return r.Type switch
            {
                "Index" => New(Key(host, "Index", r.Name),
                    [disabled, ("FilterPredicate", r.Text is { } f ? new Value.Script(f) : new Value.Null()), ("Unique", Bool(r.Unique))],
                    [("Columns", columns), ("IndexedObject", [host]), ("IncludedColumns", Of(r.Schema, r.Table, r.Included)),
                        ("BodyDependencies", reads)]),
                "ForeignKeyConstraint" => New(Key(r.Type, r.Schema, r.Name),
                    [("DeleteAction", new Value.Enumeration("ForeignKeyAction", r.OnDelete)), disabled,
                        ("UpdateAction", new Value.Enumeration("ForeignKeyAction", "NoAction"))],
                    [("Columns", columns), ("ForeignColumns", Of(r.Parent!.Value.Schema, r.Parent.Value.Table, r.ParentColumns)),
                        ("ForeignTable", [Table(r.Parent.Value.Schema, r.Parent.Value.Table)]), ("Host", [host])]),
                "CheckConstraint" => New(Key(r.Type, r.Schema, r.Name), [disabled, ("Expression", new Value.Script(r.Text!))],
                    [("ExpressionDependencies", reads), ("Host", [host])]),
                "DefaultConstraint" => New(Key(r.Type, r.Schema, r.Name), [disabled, ("Expression", new Value.Script(r.Text!))],
                    [("Host", [host]), ("TargetColumn", columns)]),
                _ => New(Key(r.Type, r.Schema, r.Name), [("Clustered", Bool(r.Type == "PrimaryKeyConstraint")), disabled],
                    [("Columns", columns), ("Host", [host])]),
            };
        }
    }
}
