using System;
using System.Collections.Generic;
using System.Linq;

namespace DbChange.Kernel;

public static partial class Vocabulary
{
    /// <summary>
    /// What the rows' conditions and kinds read beside a statement: the property read's preconditions; both models by key; the change; the
    /// tables the plan rebuilds, each one an INSERT … SELECT copies into its tmp_ms_xx_ table; and the tables whose constraints a statement
    /// validates, each with the place of the first that does.
    /// </summary>
    private sealed class Context(SortedArray<PlanStatement> plan, SortedArray<Precondition> preconditions,
        Dictionary<ElementKey, Element> before, Dictionary<ElementKey, Element> after, Change change)
    {
        public HashSet<Name> Rebuilt { get; } = [.. plan.Where(s => s.Part == ScriptPart.Plan && s.Type == "InsertStatement"
                && s.Target is { } t && Copied(t.Name) is { } from && s.In(PlanRole.Read).Any(o => o.Name == from))
            .Select(s => Copied(s.Target!.Name)!.Value)];

        public Dictionary<Name, int> Validated { get; } = [];

        /// <summary>The tables a BlockOnPossibleDataLoss check among DacFx's own statements reads.</summary>
        public HashSet<Name> Checked { get; } = [];

        /// <summary>The precondition of the foreign key or check of that one-part name on the table, keyed in the table's schema in the
        /// model the change brings, as the property read makes it; null where the model holds none.</summary>
        public Precondition? Rule(Name table, Name element) =>
            (table.Schema is { } schema ? Name.Of(schema, element.Base) : Name.Of(element.Base)) is Result<Name>.Ok { Value: var named }
            && ((string[])["ForeignKeyConstraint", "CheckConstraint"])
                .Select(type => ElementKey.Of(type, named) is Result<ElementKey>.Ok { Value: var key }
                    ? after.GetValueOrDefault(key) : null)
                .OfType<Element>().FirstOrDefault() is { } rule
            && Preconditions.Rule(rule) is Result<Precondition>.Ok { Value: var precondition } ? precondition : null;

        /// <summary>
        /// A table or view a deploy script names, as the models key it: a one-part name in dbo, the default schema a deployment runs
        /// under, and a name that matches one table or view of the model the change brings, or else of the model before it, ignoring case
        /// as SQL Server's usual collation reads names, under that table's own spelling; any other name as the script writes it.
        /// </summary>
        public Name Canonical(Name name)
        {
            var two = name.Schema is null && Name.Of("dbo", name.Base) is Result<Name>.Ok { Value: var inDbo } ? inDbo : name;
            foreach (var model in (Dictionary<ElementKey, Element>[])[after, before])
            {
                var matched = model.Keys.Where(k => k is { Parent: null, Type: "Table" or "View" }
                    && string.Equals(k.Name.Schema, two.Schema, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(k.Name.Base, two.Base, StringComparison.OrdinalIgnoreCase)).Select(k => k.Name).Distinct().ToArray();
                if (matched is [var one])
                {
                    return one;
                }
            }

            return two;
        }

        /// <summary>The preconditions <paramref name="kind"/> takes on each table named, or null where one of them holds none, or none is
        /// named.</summary>
        public Precondition[]? Held(IEnumerable<Name> tables, Func<Precondition, bool> kind)
        {
            var named = tables.Distinct().ToArray();
            var held = named.Select(t => preconditions.Where(p => p.Table == t && kind(p)).ToArray()).ToArray();
            return named.Length == 0 || held.Any(h => h.Length == 0) ? null : [.. held.SelectMany(h => h)];
        }

        /// <summary>The most bytes the columns' values of a row hold together, by the model the change brings: long.MaxValue, which stands
        /// for no bound, where one is of a max type, in whatever place it sorts; null where the model gives a column no type it knows the
        /// size of.</summary>
        public long? KeyBytes(Name table, SortedArray<Name> columns)
        {
            long total = 0;
            foreach (var column in columns)
            {
                if (Column(after, table, column) is not { } element
                    || ColumnType.Of(element).Bind(t => t.Stored(after)) is not Result<ColumnType>.Ok { Value: var type }
                    || type.KeyBytes is not { } bytes)
                {
                    return null;
                }

                total = bytes == long.MaxValue || total == long.MaxValue ? long.MaxValue : total + bytes;
            }

            return total;
        }

        /// <summary>A computed column's expression as the model the change brings writes it, and whether it is persisted; null for a column
        /// that is not computed or that the model does not hold.</summary>
        public (string Expression, bool Persisted)? Computed(Name table, Name column) =>
            Column(after, table, column) is { } element && element["Expression"] is Value.Script expression
                ? (expression.Content, element["Persisted"] is Value.Boolean { IsTrue: true }) : null;

        /// <summary>Whether the constraint or index of that one-part name on the table is clustered in the model before the change: a
        /// primary key or unique constraint, keyed in the table's schema, or an index, keyed under the table.</summary>
        public bool Clustered(Name table, Name element)
        {
            var named = table.Schema is { } schema ? Name.Of(schema, element.Base) : Name.Of(element.Base);
            var keys = ((string[])["PrimaryKeyConstraint", "UniqueConstraint"]).Select(type => named.Bind(n => ElementKey.Of(type, n)))
                .Concat(TableKey(before, table) is { } key ? [ElementKey.Of(key, "Index", element)] : []);
            return keys.Any(k => k is Result<ElementKey>.Ok { Value: var held }
                && before.GetValueOrDefault(held)?["Clustered"] is Value.Boolean { IsTrue: true });
        }

        /// <summary>The table's identity column in the model the change brings, or null where it has none.</summary>
        public Name? Identity(Name table) => Columns(after, table)
            .FirstOrDefault(c => c["IsIdentity"] is Value.Boolean { IsTrue: true })?.Key.Name;

        /// <summary>The table's period in the model the change brings, its GENERATED ALWAYS AS ROW START and ROW END columns; null where it
        /// has none.</summary>
        public (Name Start, Name End)? Period(Name table)
        {
            var columns = Columns(after, table).ToArray();
            Name? Generated(string member) => columns.FirstOrDefault(c => c["GeneratedAlwaysType"] is Value.Enumeration { Member: var m }
                && m == member)?.Key.Name;
            return (Generated("GeneratedAlwaysAsRowStart"), Generated("GeneratedAlwaysAsRowEnd")) is ({ } start, { } end)
                ? (start, end) : null;
        }

        /// <summary>Whether the change creates the table's column, or the table.</summary>
        public bool Created(Name table, Name column) => Created(table)
            || change.Created.Any(e => e.Key.Type == "Column" && e.Key.Name == column && e.Key.Parent?.Name == table);

        /// <summary>Whether the model the change brings, or the model before it, holds a table or view of that name: an object whose rows
        /// a deploy script can read, where a procedure it executes or a table it makes for itself holds none of the database's.</summary>
        public bool Holds(Name table) => TableKey(after, table) is not null || TableKey(before, table) is not null;

        /// <summary>Whether the change creates the table or view of that name.</summary>
        public bool Created(Name table) =>
            change.Created.Any(e => e.Key is { Parent: null, Type: "Table" or "View" } && e.Key.Name == table);

        /// <summary>Whether the change creates or alters the table's column.</summary>
        public bool Changed(Name table, Name column) => Created(table, column)
            || change.Altered.Any(a => a.Key.Type == "Column" && a.Key.Name == column && a.Key.Parent?.Name == table);

        /// <summary>The column of the table that a deploy script names, as the model the change brings spells it, ignoring case; null where
        /// the model holds no such column.</summary>
        public Name? ColumnOf(Name table, Name column) => Columns(after, table).Select(c => c.Key.Name)
            .Where(n => string.Equals(n.Base, column.Base, StringComparison.OrdinalIgnoreCase)).Select(n => (Name?)n).FirstOrDefault();

        /// <summary>
        /// A seed's code: the columns of each unique key the table holds in the model the change brings, a primary key, a unique constraint
        /// or a unique index, that the seed writes whole and that is not the key its MERGE matches on, so a row of the table holding a
        /// seed's code under another key makes the seed's INSERT a duplicate (the Facsimile's QST-111).
        /// </summary>
        public SortedArray<Name> Code(Name table, SortedArray<Name> key, SortedArray<Name> written)
        {
            if (TableKey(after, table) is not { } host)
            {
                return [];
            }

            var keys = after.Values.Where(e => e.Key.Type is "PrimaryKeyConstraint" or "UniqueConstraint"
                    ? On(e, "Host") == host
                    : e.Key.Type == "Index" && e["Unique"] is Value.Boolean { IsTrue: true } && On(e, "IndexedObject") == host)
                .Select(e => SortedArray.Of((e.Relationships.FirstOrDefault(r => r.Name == "Columns")?.Targets ?? [])
                    .Select(t => t.Key.Name)))
                .Where(columns => columns.Count > 0 && columns != key && columns.All(written.Contains));
            return SortedArray.Of(keys.SelectMany(columns => columns).Distinct());
        }

        private static ElementKey? On(Element e, string relationship) =>
            e.Relationships.FirstOrDefault(r => r.Name == relationship)?.Targets is [var only] ? only.Key : null;

        private static Element? Column(Dictionary<ElementKey, Element> model, Name table, Name column) =>
            TableKey(model, table) is { } key && ElementKey.Of(key, "Column", column) is Result<ElementKey>.Ok { Value: var held }
                ? model.GetValueOrDefault(held) : null;

        private static IEnumerable<Element> Columns(Dictionary<ElementKey, Element> model, Name table) =>
            TableKey(model, table) is { } key && model.GetValueOrDefault(key) is { } held
                ? (held.Relationships.FirstOrDefault(r => r.Name == "Columns")?.Targets ?? [])
                    .Select(t => model.GetValueOrDefault(t.Key)).OfType<Element>()
                : [];

        /// <summary>The key of the table or view of that name the model holds, or null.</summary>
        private static ElementKey? TableKey(Dictionary<ElementKey, Element> model, Name table) =>
            ((string[])["Table", "View"]).Select(type => ElementKey.Of(type, table))
                .Select(k => k is Result<ElementKey>.Ok { Value: var key } && model.ContainsKey(key) ? key : null)
                .FirstOrDefault(k => k is not null);
    }
}
