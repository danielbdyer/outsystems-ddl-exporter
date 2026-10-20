using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DbChange.Kernel;

/// <summary>Schema.Of: the reading rules, each over the shapes io/Ssdt.ReadModel gives DacFx 170.5.96's objects.</summary>
public sealed partial record Schema
{
    /// <summary>The element types a table owns that the schema reads, each by the relationship that names its table.</summary>
    private static readonly Dictionary<string, string> Owners = new(StringComparer.Ordinal)
    {
        ["PrimaryKeyConstraint"] = "Host", ["UniqueConstraint"] = "Host", ["ForeignKeyConstraint"] = "Host",
        ["CheckConstraint"] = "Host", ["DefaultConstraint"] = "Host", ["Index"] = "IndexedObject", ["DmlTrigger"] = "TriggerObject",
    };

    /// <summary>
    /// Every table of a model (an element of type Table at the top level) and what the planter reads of it: the targets of its Columns, in
    /// order, as its columns; the primary key, unique constraints, foreign keys, checks and defaults whose Host it is; the unique indexes
    /// whose IndexedObject it is; the DML triggers whose TriggerObject it is; and its TemporalSystemVersioningHistoryTable. Nothing else is
    /// read: no view or view column, no module, permission, statistics, role, non-unique index or database option, no deploy script and no
    /// refactorlog entry, and no index or trigger on a view. A column with an Expression is computed; one whose GeneratedAlwaysType is
    /// other than None is generated; any other is stored, with its type resolved through <see cref="ColumnType.Stored"/>. A constraint
    /// keyed under its table or a column is unnamed, as io keys an unnamed one. Every element the reading follows (a column, an alias
    /// type, a foreign key's or a default's table and column, a history table) must be one of the model's, and a target io keyed
    /// Unresolved is none: either is the error model.unresolved. An identity seed or increment outside bigint is the error model.identity.
    /// </summary>
    public static Result<Schema> Of(SortedArray<Element> model)
    {
        var byKey = new Dictionary<ElementKey, Element>();
        foreach (var e in model)
        {
            byKey[e.Key] = e;
        }

        var owned = new Dictionary<ElementKey, List<Element>>();
        foreach (var e in model)
        {
            if (Owners.TryGetValue(e.Key.Type, out var relationship) && Targets(e, relationship) is [var owner]
                && (e.Key.Type != "Index" || e["Unique"] is Value.Boolean { IsTrue: true }))
            {
                if (owner.Type == Unresolved)
                {
                    return Missing(e.Key, owner);
                }

                (owned.TryGetValue(owner, out var list) ? list : owned[owner] = []).Add(e);
            }
        }

        var tables = new List<Table>();
        var histories = new Dictionary<Name, Name>();
        foreach (var e in model.Where(e => e.Key is { Type: "Table", Parent: null }))
        {
            if (Read(e, owned.GetValueOrDefault(e.Key) ?? [], byKey).Failed(out var table, out var error))
            {
                return error;
            }

            tables.Add(table);
            if (table.Versioning is Versioning.Current current)
            {
                histories[current.HistoryTable] = table.Name;
            }
        }

        return new Schema(SortedArray.Of(tables.Select(t =>
            histories.TryGetValue(t.Name, out var current) ? t with { Versioning = new Versioning.History(current) } : t)));
    }

    private const string Unresolved = "Unresolved";

    private static Result<Table> Read(Element table, List<Element> owned, Dictionary<ElementKey, Element> model)
    {
        var defaults = new Dictionary<ElementKey, Default>();
        foreach (var d in owned.Where(o => o.Key.Type == "DefaultConstraint"))
        {
            if (Follow(d, "TargetColumn", model).Failed(out var column, out var error))
            {
                return error;
            }

            defaults[column.Key] = new Default(Constraint(d.Key), Script(d["Expression"]),
                Targets(d, "ExpressionDependencies").FirstOrDefault(t => t.Type == "Sequence")?.Name);
        }

        var columns = new List<Column>();
        foreach (var target in Relationship(table, "Columns"))
        {
            if (Follow(table.Key, target.Key, model).Bind(c => ColumnOf(c, target.Position + 1, defaults, model))
                .Failed(out var column, out var error))
            {
                return error;
            }

            columns.Add(column);
        }

        var nullable = columns.ToDictionary(c => c.Name, c => c.Nullable);
        var references = new List<Reference>();
        foreach (var fk in owned.Where(o => o.Key.Type == "ForeignKeyConstraint"))
        {
            if (ReferenceOf(fk, nullable, model).Failed(out var reference, out var error))
            {
                return error;
            }

            references.Add(reference);
        }

        Versioning? versioning = null;
        if (Targets(table, "TemporalSystemVersioningHistoryTable") is [var history])
        {
            var period = columns.OfType<Column.Generated>().ToLookup(g => g.As, g => g.Name);
            if (Follow(table.Key, history, model).Failed(out var held, out var error))
            {
                return error;
            }

            if (period[GeneratedAs.RowStart].ToArray() is not [var start] || period[GeneratedAs.RowEnd].ToArray() is not [var end])
            {
                return new Error("model.unresolved",
                    $"{table.Key} names {held.Key} as its history table but does not hold one GENERATED ALWAYS AS ROW START"
                        + " column and one AS ROW END column.", ColumnType.ReadModelDefect);
            }

            versioning = new Versioning.Current(held.Key.Name, start, end);
        }

        var key = owned.FirstOrDefault(o => o.Key.Type == "PrimaryKeyConstraint");
        return new Table(table.Key.Name, SortedArray.Of(columns),
            key is null ? null : new PrimaryKey(Constraint(key.Key), KeyColumns(key), Boolean(key, "Clustered")),
            SortedArray.Of(owned.Where(o => o.Key.Type is "UniqueConstraint" or "Index").Select(u => new Unique(u.Key, Constraint(u.Key),
                KeyColumns(u), u.Key.Type == "Index", u["FilterPredicate"] as Value.Script, Boolean(u, "Disabled")))),
            SortedArray.Of(references),
            SortedArray.Of(owned.Where(o => o.Key.Type == "CheckConstraint")
                .Select(c => new Check(c.Key, Constraint(c.Key), Script(c["Expression"]), Boolean(c, "Disabled")))),
            SortedArray.Of(owned.Where(o => o.Key.Type == "DmlTrigger").Select(t => new Trigger(t.Key.Name, Boolean(t, "Disabled"),
                t["TriggerType"] is Value.Enumeration { Member: "InsteadOf" }))),
            versioning);
    }

    private static Result<Column> ColumnOf(Element column, int ordinal, Dictionary<ElementKey, Default> defaults,
        Dictionary<ElementKey, Element> model)
    {
        var (name, nullable) = (column.Key.Name, Boolean(column, "Nullable"));
        if (column["Expression"] is Value.Script expression)
        {
            var persisted = Boolean(column, "Persisted");
            return new Column.Computed(ordinal, name,
                persisted && column["PersistedNullable"] is Value.Boolean b ? b.IsTrue : nullable, expression, persisted);
        }

        if (ColumnType.Of(column).Failed(out var declared, out var unresolved))
        {
            return unresolved;
        }

        if (declared.IsAlias && Follow(column.Key, ElementKey.Of("DataType", declared.Type), model).Failed(out _, out var missing))
        {
            return missing;
        }

        if (declared.Stored(model).Failed(out var stored, out var error))
        {
            return error;
        }

        if (column["GeneratedAlwaysType"] is Value.Enumeration { Member: var member and not "None" })
        {
            return new Column.Generated(ordinal, name, nullable, stored, member switch
            {
                "GeneratedAlwaysAsRowStart" => GeneratedAs.RowStart,
                "GeneratedAlwaysAsRowEnd" => GeneratedAs.RowEnd,
                _ => GeneratedAs.Other,
            });
        }

        Identity? identity = null;
        if (Boolean(column, "IsIdentity"))
        {
            if ((Long(column, "IdentitySeed"), Long(column, "IdentityIncrement")) is not ({ } seed, { } increment))
            {
                return new Error("model.identity",
                    $"{column.Key} is an IDENTITY with seed {column["IdentitySeed"]} and increment {column["IdentityIncrement"]}, and the"
                        + " planter reads each as a bigint.",
                    "Declare the column's IDENTITY seed and increment within bigint's range, or plant its table's rows by hand.");
            }

            identity = new Identity(seed, increment);
        }

        return new Column.Stored(ordinal, name, nullable, stored, declared.IsAlias ? declared.Type : null, identity,
            defaults.GetValueOrDefault(column.Key), Boolean(column, "Sparse"), Boolean(column, "IsRowGuidCol"));
    }

    private static Result<Reference> ReferenceOf(Element fk, Dictionary<Name, bool> nullable, Dictionary<ElementKey, Element> model)
    {
        if (Follow(fk, "ForeignTable", model).Failed(out var parent, out var error))
        {
            return error;
        }

        var (columns, parents) = (Targets(fk, "Columns"), Targets(fk, "ForeignColumns"));
        if (columns.Length != parents.Length || columns.Length == 0)
        {
            return new Error("model.unresolved",
                $"{fk.Key} pairs {columns.Length} columns with {parents.Length} columns of {parent.Key}.", ColumnType.ReadModelDefect);
        }

        var disabled = Boolean(fk, "Disabled");
        var legs = SortedArray.Of(columns.Select((c, i) => new Reference.Leg(i, c.Name, parents[i].Name)));
        return new Reference(fk.Key, Constraint(fk.Key), parent.Key.Name, legs, Action(fk["DeleteAction"]), Action(fk["UpdateAction"]),
            disabled, disabled || legs.Any(l => nullable.GetValueOrDefault(l.Column)));
    }

    private static SortedArray<KeyColumn> KeyColumns(Element key) => SortedArray.Of(Relationship(key, "Columns").Select(t =>
        new KeyColumn(t.Position, t.Key.Name,
            key[string.Create(CultureInfo.InvariantCulture, $"Columns[{t.Position}].Ascending")] is Value.Boolean { IsTrue: false })));

    /// <summary>A constraint's name: a named one is keyed at the top level, an index is always named, and io keys an unnamed constraint
    /// under its table or its column.</summary>
    private static Name? Constraint(ElementKey key) => key.Parent is null || key.Type == "Index" ? key.Name : null;

    private static ReferentialAction Action(Value? value) => value is Value.Enumeration { Member: var member }
        && Enum.TryParse<ReferentialAction>(member, out var action) ? action : ReferentialAction.NoAction;

    private static SortedArray<Element.Relationship.Target> Relationship(Element e, string name) =>
        e.Relationships.FirstOrDefault(r => r.Name == name)?.Targets ?? [];

    private static ElementKey[] Targets(Element e, string name) => [.. Relationship(e, name).Select(t => t.Key)];

    private static bool Boolean(Element e, string property) => e[property] is Value.Boolean { IsTrue: true };

    private static long? Long(Element e, string property) => e[property] switch
    {
        Value.Integer i => i.Number,
        Value.Text t when long.TryParse(t.Content, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) => n,
        _ => null,
    };

    private static Value.Script Script(Value? value) => value as Value.Script ?? new Value.Script(value is Value.Text t ? t.Content : "");

    /// <summary>The one target of the element's relationship, which the model must hold.</summary>
    private static Result<Element> Follow(Element e, string relationship, Dictionary<ElementKey, Element> model) =>
        Targets(e, relationship) is [var target] ? Follow(e.Key, target, model)
            : new Error("model.unresolved", $"{e.Key} names no one {relationship} target.", ColumnType.ReadModelDefect);

    private static Result<Element> Follow(ElementKey owner, Result<ElementKey> target, Dictionary<ElementKey, Element> model) =>
        target.Bind(key => key.Type != Unresolved && model.TryGetValue(key, out var held) ? Result.Ok(held) : Missing(owner, key));

    private static Error Missing(ElementKey owner, ElementKey target) => target.Type == Unresolved
        ? new Error("model.unresolved", $"{owner} names {target.Path}, which DacFx resolved to no object of the model.",
            "Read the model from a built package or a published copy, where every reference resolves.")
        : new Error("model.unresolved", $"{owner} names {target}, which the model does not hold.", ColumnType.ReadModelDefect);
}
