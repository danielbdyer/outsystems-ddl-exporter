using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using static DbChange.Kernel.Tests.KernelProperties;

namespace DbChange.Kernel.Tests;

/// <summary>
/// Models of one to three tables, in dbo or sales, each with a primary key on Id, a few columns of the types DacFx models and, by chance, a
/// unique index, a check and a foreign key to another table's Id, some disabled; and edits of them whose preconditions are known by
/// construction: a column made mandatory or optional, retyped, given a unique index or a check, dropped, or joined by a new column (one
/// with a default and a unique index over it among them); a foreign key's delete action changed or the key enabled or disabled; a check's
/// expression changed; a table created; columns and tables renamed through the refactorlog; and the tables the plan's sites check. Each
/// edit records what the precondition table must find, so a property compares Preconditions.Of with what the edits made, under the
/// names the change leaves.
/// </summary>
internal static class PreconditionProperties
{
    private static readonly (string Type, string? Collation)[] Types =
    [
        ("int", null), ("bigint", null), ("smallint", null), ("bit", null), ("nvarchar(10)", null), ("nvarchar(50)", null),
        ("nvarchar(max)", null), ("varchar(20)", null), ("decimal(18,2)", null), ("decimal(9,2)", null), ("datetime2(7)", null),
        ("date", null), ("nvarchar(50)", "Latin1_General_CS_AS"),
    ];

    /// <summary>
    /// Generated models before and after edits, the refactorlog's renames, the plan's sites, the preconditions the edits make, what each
    /// edit was (for a failing sample's print), and for each table and column after the change the key it had before, the key itself for
    /// one never renamed, and none for a column the change creates, with the default the edit gave it.
    /// </summary>
    public sealed record Edited(Model Before, Model After, SortedArray<Rename> Renames, SortedArray<BlockOnPossibleDataLossSite> Sites,
        SortedArray<Precondition> Expected, string Edits, IReadOnlyDictionary<ElementKey, ElementKey> Was,
        IReadOnlyDictionary<ElementKey, string> Defaults)
    {
        public SortedArray<Element> BeforeElements => Before.Elements;

        public SortedArray<Element> AfterElements => After.Elements;

        public Change Change => Ok(Kernel.Change.Between(BeforeElements, AfterElements, Renames));
    }

    public static readonly Gen<Edited> Edits = Gen.Int[0, 999].Array[96].Select(Build);

    /// <summary>Draws in order from a fixed array of generated numbers, so a failing sample shrinks to smaller models.</summary>
    private sealed class Draw(int[] numbers)
    {
        private int next;

        public int Of(int choices) => numbers[next++ % numbers.Length] % choices;

        public bool OneIn(int n) => Of(n) == 0;
    }

    /// <summary>What an edit makes, kept until the renames are known: the kind and the table, column and rule it reads.</summary>
    private sealed record Intent(string Kind, int Table, string Column, string Rule = "");

    private static Edited Build(int[] numbers)
    {
        var draw = new Draw(numbers);
        var count = 1 + draw.Of(3);
        var schemas = Enumerable.Range(0, count).Select(_ => draw.OneIn(2) ? "dbo" : "sales").ToArray();
        string TableOf(int i) => schemas[i] + ".T" + i;
        var columns = new List<(int Table, string Name, (string Type, string? Collation) Type, bool Nullable)>();
        var model = Model.Empty;
        for (var i = 0; i < count; i++)
        {
            model = model.With(TableOf(i) + ".Id int NOT NULL").PrimaryKey("PK_T" + i, TableOf(i), "Id");
            var width = 1 + draw.Of(3);
            for (var c = 1; c <= width; c++)
            {
                var type = Types[draw.Of(Types.Length)];
                var nullable = draw.OneIn(2);
                columns.Add((i, "c" + c, type, nullable));
                model = model.With(Spec(TableOf(i) + ".c" + c, type, nullable));
            }

            var mine = columns.Where(c => c.Table == i).ToList();
            if (draw.OneIn(2))
            {
                model = model.UniqueIndex("UX_T" + i, TableOf(i), mine[draw.Of(mine.Count)].Name);
            }

            if (draw.OneIn(2))
            {
                model = model.Check("CK_T" + i, TableOf(i), "check " + i, [mine[draw.Of(mine.Count)].Name], disabled: draw.OneIn(4));
            }

            if (count > 1 && draw.OneIn(2))
            {
                var parent = TableOf((i + 1 + draw.Of(count - 1)) % count);
                model = model.ForeignKey("FK_T" + i, TableOf(i), [mine[draw.Of(mine.Count)].Name], parent, ["Id"], disabled: draw.OneIn(4));
            }
        }

        var (before, after, intents, edits) = (model, model, new List<Intent>(), new List<string>());
        var defaults = new Dictionary<(int, string), string>();
        foreach (var (table, name, type, nullable) in columns)
        {
            var path = TableOf(table) + "." + name;
            switch (draw.Of(9))
            {
                case 1 when nullable:
                    after = after.With(Spec(path, type, false));
                    intents.Add(new Intent("NotNull", table, name));
                    edits.Add(path + " made mandatory");
                    break;
                case 2 when !nullable:
                    after = after.With(Spec(path, type, true));
                    edits.Add(path + " made optional");
                    break;
                case 3:
                    var retyped = Types[(Array.IndexOf(Types, type) + 1 + draw.Of(Types.Length - 1)) % Types.Length];
                    after = after.With(Spec(path, retyped, nullable));
                    intents.Add(new Intent("Fits", table, name, Text(type) + "|" + Text(retyped)));
                    edits.Add(path + " retyped " + Text(type) + " to " + Text(retyped));
                    break;
                case 4:
                    after = after.UniqueIndex("UX2_T" + table + "_" + name, TableOf(table), name);
                    intents.Add(new Intent("Unique", table, name));
                    edits.Add(path + " given a unique index");
                    break;
                case 5:
                    after = after.Check("CK2_T" + table + "_" + name, TableOf(table), "check of " + name, [name]);
                    intents.Add(new Intent("Check", table, name, "check of " + name));
                    edits.Add(path + " given a check");
                    break;
                case 6:
                    after = after.WithoutColumn(path);
                    edits.Add(path + " dropped");
                    break;
                case 7:
                    after = draw.OneIn(2) ? after.With(TableOf(table) + ".n" + name + " int NULL")
                        : after.With(TableOf(table) + ".n" + name + " int NOT NULL").Default("DF_T" + table + "_n" + name, TableOf(table),
                            "n" + name, "((0))");
                    edits.Add(path + " joined by a new column");
                    break;
                case 8:
                    after = after.With(TableOf(table) + ".n" + name + " int NOT NULL")
                        .Default("DF_T" + table + "_n" + name, TableOf(table), "n" + name, "((7))")
                        .UniqueIndex("UX3_T" + table + "_n" + name, TableOf(table), "n" + name);
                    defaults[(table, "n" + name)] = "((7))";
                    intents.Add(new Intent("Unique", table, "n" + name));
                    edits.Add(path + " joined by a new column with a default and a unique index");
                    break;
            }
        }

        for (var i = 0; i < count; i++)
        {
            if (after.Rules.Any(r => r.Name == "FK_T" + i) && draw.OneIn(3))
            {
                after = draw.OneIn(2) ? after.Changing("FK_T" + i, r => r with { OnDelete = "Cascade" })
                    : after.Changing("FK_T" + i, r => r with { Disabled = !r.Disabled });
                intents.Add(new Intent("ForeignKey", i, "", "FK_T" + i));
                edits.Add("FK_T" + i + " changed");
            }

            if (after.Rules.Any(r => r.Name == "CK_T" + i) && draw.OneIn(3))
            {
                after = after.Changing("CK_T" + i, r => r with { Text = r.Text + " again" });
                intents.Add(new Intent("Check", i, "", "CK_T" + i));
                edits.Add("CK_T" + i + " changed");
            }
        }

        if (draw.OneIn(3))
        {
            after = after.With("dbo.N.Id int NOT NULL", "dbo.N.Name nvarchar(50) NULL").PrimaryKey("PK_N", "dbo.N", "Id");
            intents.Add(new Intent("Created", -1, "Id"));
            edits.Add("dbo.N created");
        }

        var sites = Enumerable.Range(0, count).Where(_ => draw.OneIn(3)).ToList();
        intents.AddRange(sites.Select(i => new Intent("TableEmpty", i, "")));

        // Renames through the refactorlog: a column, then a table, each entry under the keys of its moment, as SSDT records it.
        var (renames, columnName, tableName) = (new List<Rename>(), new Dictionary<(int, string), string>(), new Dictionary<int, string>());
        var standing = columns
            .Where(c => after.Columns.Any(a => (a.Schema, a.Table, a.Name) == (schemas[c.Table], "T" + c.Table, c.Name))).ToList();
        if (standing.Count > 0 && draw.OneIn(3))
        {
            var (table, name, _, _) = standing[draw.Of(standing.Count)];
            after = after.RenameColumn(TableOf(table) + "." + name, name + "x");
            renames.Add(Ok(Rename.Of(Model.ColumnKey(TableOf(table) + "." + name), name + "x")));
            columnName[(table, name)] = name + "x";
            edits.Add(TableOf(table) + "." + name + " renamed");
        }

        if (draw.OneIn(3))
        {
            var table = draw.Of(count);
            after = after.MoveTable(TableOf(table), schemas[table], "T" + table + "x");
            renames.Add(Ok(Rename.Of(Model.TableKey(TableOf(table)), "T" + table + "x")));
            tableName[table] = "T" + table + "x";
            edits.Add(TableOf(table) + " renamed");
        }

        string FinalTable(int i) => i < 0 ? "dbo.N" : schemas[i] + "." + tableName.GetValueOrDefault(i, "T" + i);
        string FinalColumn(int i, string c) => columnName.GetValueOrDefault((i, c), c);
        var expected = intents.SelectMany(intent => Expect(intent, after, FinalTable, FinalColumn)).Distinct();
        var was = new Dictionary<ElementKey, ElementKey>();
        for (var i = 0; i < count; i++)
        {
            was[Model.TableKey(FinalTable(i))] = Model.TableKey(TableOf(i));
            foreach (var c in columns.Where(c => c.Table == i).Select(c => c.Name).Prepend("Id"))
            {
                was[Model.ColumnKey(FinalTable(i) + "." + FinalColumn(i, c))] = Model.ColumnKey(TableOf(i) + "." + c);
            }
        }

        return new Edited(before, after, SortedArray.Of(renames), SortedArray.Of(sites.Select(i =>
                new BlockOnPossibleDataLossSite(Model.TableName(FinalTable(i)), "the statement the check on T" + i + " protects"))),
            SortedArray.Of(expected), string.Join("; ", edits), was,
            defaults.ToDictionary(d => Model.ColumnKey(FinalTable(d.Key.Item1) + "." + d.Key.Item2), d => d.Value));
    }

    /// <summary>The preconditions an edit makes, under the names the change leaves: a retyped column's own Fits, and Unique, Check or
    /// ForeignKey for each enabled key, check or foreign key over it after the change.</summary>
    private static IEnumerable<Precondition> Expect(Intent intent, Model after, Func<int, string> table, Func<int, string, string> column)
    {
        var at = table(intent.Table);
        var name = intent.Column.Length == 0 ? "" : column(intent.Table, intent.Column);
        Model.Rule? RuleNamed(string rule) => after.Rules.FirstOrDefault(r => r.Name == rule && !r.Disabled);
        bool Validated(Model.Rule r) => !r.Disabled
            && (r.Type is "PrimaryKeyConstraint" or "UniqueConstraint" or "ForeignKeyConstraint" or "CheckConstraint"
                || (r.Type == "Index" && r.Unique));
        switch (intent.Kind)
        {
            case "TableEmpty":
                yield return QuestionCases.TableEmpty(at);
                break;
            case "NotNull":
                yield return QuestionCases.NotNull(at + "." + name);
                break;
            case "Fits":
                var types = intent.Rule.Split('|');
                yield return QuestionCases.Fits(at + "." + name, types[0], types[1]);
                foreach (var rule in after.Rules.Where(r => Validated(r)
                    && ((r.Schema + "." + r.Table == at && r.Columns.Concat(r.Reads).Contains(name))
                        || (r.Parent is { } p && p.Schema + "." + p.Table == at && r.ParentColumns.Contains(name)))))
                {
                    yield return Of(rule);
                }

                break;
            case "Unique":
                yield return QuestionCases.Unique(at, name);
                break;
            case "Check" when intent.Column.Length > 0:
                yield return QuestionCases.Check(at, intent.Rule, name);
                break;
            case "Check" or "ForeignKey":
                if (RuleNamed(intent.Rule) is { } changed)
                {
                    yield return Of(changed);
                }

                break;
            case "Created":
                yield return QuestionCases.Unique("dbo.N", "Id");
                break;
        }
    }

    private static Precondition Of(Model.Rule rule) => rule.Type switch
    {
        "CheckConstraint" => QuestionCases.Check(rule.Schema + "." + rule.Table, rule.Text!, rule.Reads),
        "ForeignKeyConstraint" => QuestionCases.ForeignKey(rule.Schema + "." + rule.Table,
            rule.Parent!.Value.Schema + "." + rule.Parent.Value.Table, [.. rule.Columns.Zip(rule.ParentColumns)]),
        _ => QuestionCases.Unique(rule.Schema + "." + rule.Table, rule.Columns),
    };

    private static string Spec(string path, (string Type, string? Collation) type, bool nullable) =>
        path + " " + type.Type + (nullable ? " NULL" : " NOT NULL") + (type.Collation is { } c ? " " + c : "");

    private static string Text((string Type, string? Collation) type) => type.Type + (type.Collation is { } c ? " " + c : "");
}
