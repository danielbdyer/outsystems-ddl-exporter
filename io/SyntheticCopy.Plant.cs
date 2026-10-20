using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DbChange.Kernel;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Planted = DbChange.Kernel.Witness.Planted;

namespace DbChange.Io;

/// <summary>
/// Steps 2 to 5 of the copy build: WP 3.8's rows planted as ScriptDom INSERTs, presence rows then violating rows, table by table in
/// Order's order, each skipped where the copy as restored already answers its every purpose (PLT-97), the presence of row r where the
/// table holds r rows; a parent's key resolved from the copy (PLT-49 to PLT-53): row r of a Linked key takes the parent's r-th row,
/// cycling, its rows as restored first in the order of the columns linked, then the rows planted into it by ordinal, and a key past a
/// column's largest takes the largest the copy held as restored; a reference deferred inside a cycle set by an UPDATE once every table
/// is planted; each table's enabled DML triggers disabled while its rows are planted and enabled again (PLT-45); the change's declared
/// blocks run in the same order, a table's -- plants: block in its rows' place (PLT-83 to PLT-85), with triggers firing; and every
/// constraint the environment holds disabled or untrusted, every foreign key a planted row breaks and every foreign key of a table a
/// block plants suspended from the first row to the last block, then restored to the environment's state, a constraint it lists no state
/// for to the copy's own, and read back through SqlServer.Measure (the Facsimile's F9; spike S9). A planted row SQL Server refuses on a
/// check is the need NeedsRow.Reason.Rejects (PLT-44), and any other refusal names its row and its number; neither stops the rest.
/// Every row runs under SET LANGUAGE us_english and SET DATEFORMAT ymd (PLT-123).
/// </summary>
public static partial class SyntheticCopy
{
    /// <summary>The session a planted row and a declared block run under, on the batch's first line.</summary>
    private const string Session = "SET LANGUAGE us_english; SET DATEFORMAT ymd; ";

    /// <summary>
    /// A constraint the envelope suspends: its key, its table, its one-part name, whether the copy holds it disabled as the repository
    /// declares it, and the state it is restored to: the environment's where its existing data lists one, else the copy's own.
    /// </summary>
    private sealed record Suspension(ElementKey Key, Name Table, Name Constraint, bool Disabled, TrustState Restored);

    private sealed class Planter(SqlServer.Copy copy, Schema schema, Witness plan, SqlServer.QueryLog log,
        Func<Precondition, RowClasses, bool> answered)
    {
        private readonly Dictionary<Name, long> rows = [];
        private readonly Dictionary<Name, Name[]> linked = [];
        private readonly Dictionary<Name, List<object?[]>> parents = [];
        private readonly Dictionary<(Name Table, Name Column), decimal> largest = [];
        private readonly List<(Table Table, Witness.Row Row, Name[] Key, object?[] Values)> second = [];

        /// <summary>Each table's rows as the copy was restored, counted before the first row.</summary>
        public Dictionary<Name, long> Restored { get; } = [];

        public List<Finding> Needs { get; } = [];

        public Dictionary<Name, (int Planted, int Skipped)> Tally { get; } = [];

        /// <summary>
        /// The copy as restored, read once: the rows of every table the plan plants or names, the rows of each parent a Linked key names,
        /// sorted by the columns linked, and the largest value of each column a key past it names.
        /// </summary>
        public Result<Planter> Read()
        {
            var cells = plan.Tables.SelectMany(t => t.Rows).SelectMany(r => r.Cells).Select(c => c.Value)
                .Select(v => v is Planted.Linked { FirstPass: { } first } link ? [link, first] : new[] { v }).SelectMany(v => v).ToList();
            foreach (var parent in cells.OfType<Planted.Linked>().GroupBy(l => l.Parent))
            {
                linked[parent.Key] = [.. parent.Select(l => l.Column).Distinct()
                    .OrderBy(c => schema.Find(parent.Key)?.Find(c)?.Ordinal ?? int.MaxValue).ThenBy(c => c)];
            }

            return Result.All(schema.Tables.Select(t => t.Name).Select(t => Values(t + ": rows", "SELECT COUNT_BIG(*) FROM " + t + ";")
                    .Map(found => Restored[t] = rows[t] = Convert.ToInt64(found[0][0], CultureInfo.InvariantCulture))))
                .Bind(_ => Result.All(linked.Select(p => Values(p.Key + ": keys", "SELECT " + string.Join(", ", p.Value) + " FROM " + p.Key
                        + " ORDER BY " + string.Join(", ", p.Value) + ";")
                    .Map(found => parents[p.Key] = [.. found.Select(r => r.ToArray())]))))
                .Bind(_ => Result.All(cells.OfType<Planted.AfterMax>().Select(m => (m.Table, m.Column)).Distinct()
                    .Select(m => Values(m.Table + ": largest " + m.Column, "SELECT MAX(" + m.Column + ") FROM " + m.Table + ";")
                        .Map(found => largest[m] = found[0][0] is { } max ? Convert.ToDecimal(max, CultureInfo.InvariantCulture) : 0))))
                .Map(_ => this);
        }

        /// <summary>
        /// The constraints suspended, as the class says: each the environment lists, each a planted row breaks, and each foreign key of a
        /// table a block plants, which the copy holds; a constraint the copy lacks is left, with a note.
        /// </summary>
        public Result<IReadOnlyList<Suspension>> Suspend(SortedArray<TrustState> trust, IEnumerable<Name> blocked, List<Finding> notes)
        {
            var held = schema.Tables.SelectMany(t => t.References.Select(r => (r.Key, Table: t.Name, r.Constraint, r.Disabled))
                .Concat(t.Checks.Select(c => (c.Key, Table: t.Name, c.Constraint, c.Disabled)))).ToDictionary(c => c.Key);
            var keys = trust.Select(t => t.Constraint)
                .Concat(plan.Tables.SelectMany(t => t.Rows).SelectMany(r => r.Suspends))
                .Concat(blocked.Select(schema.Find).OfType<Table>().SelectMany(t => t.References.Select(r => r.Key))).Distinct();
            var suspended = new List<Suspension>();
            foreach (var key in keys)
            {
                if (!held.TryGetValue(key, out var c) || c.Constraint is not { } named)
                {
                    notes.Add(Finding.Note("synthetic-copy.trust-absent", key.ToString(), held.ContainsKey(key)
                        ? key + " is unnamed in the copy, so the planter cannot suspend it by name, and its rows stand as the copy holds"
                            + " it."
                        : "The existing data lists the state of " + key + ", which the copy does not hold; the repository at the ref lacks"
                            + " it."));
                    continue;
                }

                var restored = trust.FirstOrDefault(t => t.Constraint == key) ?? new TrustState(key, c.Disabled, c.Disabled);
                suspended.Add(new Suspension(key, c.Table, Named(named.Base), c.Disabled, restored));
            }

            return Result.All(suspended.Where(s => !s.Disabled).Select(s =>
                    Run(s.Key + ": suspend", "ALTER TABLE " + s.Table + " NOCHECK CONSTRAINT " + s.Constraint + ";")))
                .Map(_ => (IReadOnlyList<Suspension>)suspended);
        }

        /// <summary>
        /// Each suspended constraint restored to its state, and read back through SqlServer.Measure: a constraint the rows break that is to
        /// end trusted is a need naming it, since its trust is never forced (the Twin's gate turned a block into a wrong clean); a state
        /// read back otherwise is synthetic-copy.trust.
        /// </summary>
        public Result<Planter> Restore(IReadOnlyList<Suspension> suspended)
        {
            foreach (var s in suspended)
            {
                var statement = "ALTER TABLE " + s.Table + (s.Restored.Disabled ? " NOCHECK CONSTRAINT " : s.Restored.Untrusted
                    ? " WITH NOCHECK CHECK CONSTRAINT " : " WITH CHECK CHECK CONSTRAINT ") + s.Constraint + ";";
                if (Run(s.Key + ": restore", statement, out var refused).Failed(out _, out var error))
                {
                    return error;
                }

                if (refused is not null)
                {
                    Needs.Add(Finding.Error("witness.trust", s.Key.ToString(),
                        "The rows planted or declared break " + s.Key + ", which is to end enabled and trusted, as the existing data lists"
                            + " no other state for it; SQL Server refused it WITH CHECK (Msg "
                            + refused.Number.ToString(CultureInfo.InvariantCulture) + "), and dbchange never forces a constraint's trust.",
                        "Read the environment's trust state again, so its existing data lists " + s.Key + " as the environment holds it."));
                    continue;
                }

                if (AggregateQueries.Trust(copy, s.Key, log).Failed(out var held, out error))
                {
                    return error;
                }

                if (Restoring(s.Restored, held) is { } wrong)
                {
                    return wrong;
                }
            }

            return this;
        }

        /// <summary>A table's planted rows, its enabled triggers disabled around them, each row skipped or planted as the class
        /// says.</summary>
        public Result<Planter> Plant(Witness.Planting planting)
        {
            var table = schema.Find(planting.Table)!;
            var triggers = table.Triggers.Where(t => !t.Disabled).ToList();
            var tally = (Planted: 0, Skipped: 0);
            var made = Result.All(triggers.Select(t => Run(t.Name + ": disable", "DISABLE TRIGGER " + t.Name + " ON " + table.Name + ";")));
            foreach (var row in planting.Rows.TakeWhile(_ => made is Result<IReadOnlyList<string>>.Ok))
            {
                if (row.Serves.All(p => p.Precondition is null ? rows[table.Name] >= row.Ordinal : answered(p.Precondition, p.Class)))
                {
                    tally.Skipped++;
                    continue;
                }

                if (Insert(table, row).Failed(out var inserted, out var error))
                {
                    made = error;
                }
                else if (inserted)
                {
                    tally.Planted++;
                    rows[table.Name]++;
                }
            }

            Tally[table.Name] = tally;
            return made.Bind(_ => Result.All(triggers.Select(t => Run(t.Name + ": enable", "ENABLE TRIGGER " + t.Name + " ON " + table.Name
                + ";")))).Map(_ => this);
        }

        /// <summary>
        /// A declared block run as one batch on the copy, with triggers firing, SET IDENTITY_INSERT holding for its statements; a statement
        /// SQL Server refuses is witness.failed, by its line. The rows of a table it plants are read again, sorted, for a child to link to.
        /// </summary>
        public Result<Planter> Declare(Witnesses.Block block, Name table)
        {
            if (Values(Witnesses.Path + " line " + Number(block.Line), block.Text, out var refused).Failed(out _, out var error))
            {
                return error;
            }

            if (refused is not null)
            {
                return Witnesses.Failed(block, refused.Line, refused.Number, refused.Message);
            }

            return !linked.TryGetValue(table, out var columns) ? this
                : Values(table + ": keys", "SELECT " + string.Join(", ", columns) + " FROM " + table + " ORDER BY "
                    + string.Join(", ", columns) + ";").Map(found =>
                    {
                        parents[table] = [.. found.Select(r => r.ToArray())];
                        return this;
                    });
        }

        /// <summary>Each reference deferred inside a cycle set by an UPDATE of its child row, once every table is planted; a parent that
        /// holds no row then leaves the reference as its first pass wrote it, a need naming the row.</summary>
        public Result<Planter> Defer() => Result.All(second.Where(d => d.Row.Cells.Select(c => c.Value).OfType<Planted.Linked>()
            .All(l => parents[l.Parent].Count > 0 || Missed(d.Table, d.Row, l.Parent))).Select(d =>
        {
            var update = new UpdateSpecification { Target = Reference(d.Table.Name) };
            foreach (var cell in d.Row.Cells.Where(c => c.Value is Planted.Linked { FirstPass: not null }))
            {
                update.SetClauses.Add(new AssignmentSetClause
                {
                    Column = ColumnOf(cell.Column), NewValue = Expression(cell.Value, deferred: false),
                    AssignmentKind = AssignmentKind.Equals,
                });
            }

            update.WhereClause = new WhereClause
            {
                SearchCondition = d.Key.Select((k, i) => (BooleanExpression)new BooleanComparisonExpression
                {
                    FirstExpression = ColumnOf(k), ComparisonType = BooleanComparisonType.Equals, SecondExpression = Literal(d.Values[i]),
                }).Aggregate((a, b) => new BooleanBinaryExpression
                {
                    BinaryExpressionType = BooleanBinaryExpressionType.And, FirstExpression = a, SecondExpression = b,
                }),
            };
            return Run(d.Table.Name + ": row " + Number(d.Row.Ordinal) + ", second pass",
                TSql.Text(new UpdateStatement { UpdateSpecification = update }));
        })).Map(_ => this);

        /// <summary>
        /// One row as a ScriptDom INSERT, its values typed, a column SQL Server fills left out, the columns a child links to and, for a row
        /// with a deferred reference, its key returned through OUTPUT; whether the row was planted, a refusal by SQL Server being a need.
        /// </summary>
        private Result<bool> Insert(Table table, Witness.Row row)
        {
            if (row.Cells.Select(c => c.Value).OfType<Planted.Linked>()
                .FirstOrDefault(l => l.FirstPass is null && parents[l.Parent].Count == 0) is { } orphaned)
            {
                return Missed(table, row, orphaned.Parent);
            }

            var key = row.Cells.Any(c => c.Value is Planted.Linked { FirstPass: not null }) ? Keyed(table, row) : [];
            var output = linked.GetValueOrDefault(table.Name, []).Concat(key).Distinct().ToArray();
            var insert = new InsertSpecification { InsertOption = InsertOption.Into, Target = Reference(table.Name) };
            var values = new RowValue();
            foreach (var cell in row.Cells.Where(c => c.Value is not Planted.Omitted))
            {
                insert.Columns.Add(ColumnOf(cell.Column));
                values.ColumnValues.Add(Expression(cell.Value, deferred: true));
            }

            insert.InsertSource = values.ColumnValues.Count == 0 ? new ValuesInsertSource { IsDefaultValues = true }
                : new ValuesInsertSource { RowValues = { values } };
            if (output.Length > 0)
            {
                insert.OutputClause = new OutputClause();
                foreach (var column in output)
                {
                    insert.OutputClause.SelectColumns.Add(new SelectScalarExpression { Expression = Inserted(column) });
                }
            }

            var site = table.Name + ": row " + Number(row.Ordinal);
            return Values(site, TSql.Text(new InsertStatement { InsertSpecification = insert }), out var refused).Map(found =>
            {
                if (refused is not null)
                {
                    Needs.Add(Refused(table, row, refused));
                    return false;
                }

                if (linked.TryGetValue(table.Name, out var columns))
                {
                    parents[table.Name].Add([.. columns.Select(c => found[0][Array.IndexOf(output, c)])]);
                }

                if (key.Length > 0)
                {
                    second.Add((table, row, key, [.. key.Select(c => found[0][Array.IndexOf(output, c)])]));
                }

                return true;
            });
        }

        /// <summary>A row that links to a parent holding no row, since SQL Server refused the rows planted into it: a need naming the
        /// parent, and false.</summary>
        private bool Missed(Table table, Witness.Row row, Name parent)
        {
            Needs.Add(Finding.Error("witness.planted-row", table.Name.ToString(),
                "Planted row " + Number(row.Ordinal) + " of " + table.Name + " links to " + parent
                    + ", which holds no row, since SQL Server refused the rows planted into it.",
                "Declare the rows of " + parent + " in " + Witnesses.Path + ", which then plant the table whole."));
            return false;
        }

        /// <summary>A refused row as a need: Msg 547 on a check of the copy is NeedsRow.Reason.Rejects (PLT-44); any other refusal names
        /// the row and SQL Server's number and message, the copy's rows being synthetic.</summary>
        private Finding Refused(Table table, Witness.Row row, SqlServer.StatementFailure refused) =>
            refused.Number == 547 && table.Checks.FirstOrDefault(c => c.Constraint is { } named
                && refused.Message?.Contains("\"" + named.Base + "\"", StringComparison.Ordinal) == true) is { } check
                ? Need(new Witness.NeedsRow(table.Name, new Witness.NeedsRow.Reason.Rejects(check)))
                : Finding.Error("witness.planted-row", table.Name.ToString(),
                    "SQL Server refused planted row " + Number(row.Ordinal) + " of " + table.Name + " with Msg "
                        + refused.Number.ToString(CultureInfo.InvariantCulture) + ": " + refused.Message,
                    "Declare the rows of " + table.Name + " in " + Witnesses.Path + ", which then plant the table whole.");

        /// <summary>A cell's value as a typed expression: a literal; a parent's key, resolved from the copy; or, for a reference deferred
        /// inside a cycle, what its first pass writes.</summary>
        private ScalarExpression Expression(Planted value, bool deferred) => value switch
        {
            Planted.Linked { FirstPass: { } first } when deferred => Expression(first, deferred),
            Planted.Linked l =>
                Literal(parents[l.Parent][(l.Row - 1) % parents[l.Parent].Count][Array.IndexOf(linked[l.Parent], l.Column)]),
            Planted.AfterMax m => Literal(largest[(m.Table, m.Column)] + m.Step),
            Planted.Null => new NullLiteral(),
            Planted.Number n => n.Literal.Contains('E', StringComparison.OrdinalIgnoreCase) ? new RealLiteral { Value = n.Literal }
                : n.Literal.Contains('.', StringComparison.Ordinal) ? new NumericLiteral { Value = n.Literal }
                : new IntegerLiteral { Value = n.Literal },
            Planted.Text t => new StringLiteral { Value = t.Content, IsNational = t.National },
            Planted.Bytes b => new BinaryLiteral { Value = "0x" + b.Hex },
            _ => new DefaultLiteral(),
        };

        /// <summary>The key a deferred row is found by again: the primary key, else a unique key, else every column the row
        /// writes.</summary>
        private static Name[] Keyed(Table table, Witness.Row row) =>
            table.Key is { } pk ? [.. pk.Columns.Select(c => c.Column)]
            : table.Uniques.FirstOrDefault(u => !u.Disabled && u.Filter is null) is { } unique ? [.. unique.Columns.Select(c => c.Column)]
            : [.. row.Cells.Where(c => c.Value is Planted.Number or Planted.Text or Planted.Bytes).Select(c => c.Column)];

        private Result<string> Run(string site, string statement) => Values(site, statement).Map(_ => site);

        /// <summary>A statement to the copy, its refusal by SQL Server below severity 20 kept as <paramref name="refused"/>.</summary>
        private Result<string> Run(string site, string statement, out SqlServer.StatementFailure? refused) =>
            Values(site, statement, out refused).Map(_ => site);

        private Result<IReadOnlyList<IReadOnlyList<object?>>> Values(string site, string statement) =>
            Values(site, statement, out var refused).Bind(found => refused is null ? Result.Ok(found)
                : Result.Fail<IReadOnlyList<IReadOnlyList<object?>>>(copy.ErrorOf(refused.Number, refused.Message ?? "")));

        private Result<IReadOnlyList<IReadOnlyList<object?>>> Values(string site, string statement, out SqlServer.StatementFailure? refused)
        {
            SqlServer.StatementFailure? failure = null;
            var answer = SqlServer.Query(copy, new SqlServer.Statement(site, Session + statement), log,
                rows => rows, failed =>
                {
                    failure = failed;
                    return (IReadOnlyList<IReadOnlyList<object?>>)[];
                });
            refused = failure;
            return answer;
        }
    }

    /// <summary>
    /// synthetic-copy.trust: a constraint read back through SqlServer.Measure in another state than it was restored to; null where it
    /// holds the state.
    /// </summary>
    internal static Error? Restoring(TrustState wanted, TrustState? held) =>
        held is { } h && h.Disabled == wanted.Disabled && h.Untrusted == wanted.Untrusted ? null
        : new Error("synthetic-copy.trust", wanted.Constraint + " was restored as " + wanted + ", and the copy's catalog reads it "
            + (held is null ? "absent" : held.ToString()) + ".",
            "Report the copy's name and this answer to dbchange's maintainers: a restore SQL Server took left another state.");

    /// <summary>A need as the finding that names it, at exit 8.</summary>
    private static Finding Need(Witness.NeedsRow need) =>
        Finding.Error("witness.needs-row", need.Table.ToString(), need.Message, need.Remedy);

    /// <summary>A value read from the copy as a typed literal, written back into a row of it: a number, a string, bytes or a
    /// date.</summary>
    private static ScalarExpression Literal(object? value) => value switch
    {
        null => new NullLiteral(),
        bool b => new IntegerLiteral { Value = b ? "1" : "0" },
        byte or short or int or long => new IntegerLiteral { Value = Convert.ToString(value, CultureInfo.InvariantCulture) },
        decimal d => d == decimal.Truncate(d) ? new IntegerLiteral { Value = decimal.Truncate(d).ToString(CultureInfo.InvariantCulture) }
            : new NumericLiteral { Value = d.ToString(CultureInfo.InvariantCulture) },
        double or float => new RealLiteral { Value = Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("E17",
            CultureInfo.InvariantCulture) },
        string s => new StringLiteral { Value = s, IsNational = true },
        Guid g => new StringLiteral { Value = g.ToString("D") },
        byte[] bytes => new BinaryLiteral { Value = "0x" + Convert.ToHexString(bytes) },
        DateTime d => new StringLiteral { Value = d.ToString(d.TimeOfDay.Ticks % TimeSpan.TicksPerSecond == 0 ? "yyyy-MM-dd'T'HH:mm:ss"
            : "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture) },
        DateTimeOffset d => new StringLiteral { Value = d.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture) },
        TimeSpan t => new StringLiteral { Value = t.ToString("c", CultureInfo.InvariantCulture) },
        _ => throw new InvalidOperationException("A key of the type " + value.GetType().Name + " is no type the planter links."),
    };

    private static NamedTableReference Reference(Name table)
    {
        var named = new NamedTableReference { SchemaObject = new SchemaObjectName() };
        foreach (var part in table.Schema is null ? [table.Base] : new[] { table.Schema, table.Base })
        {
            named.SchemaObject.Identifiers.Add(new Identifier { Value = part, QuoteType = QuoteType.SquareBracket });
        }

        return named;
    }

    private static ColumnReferenceExpression ColumnOf(Name column) => new()
    {
        ColumnType = Microsoft.SqlServer.TransactSql.ScriptDom.ColumnType.Regular,
        MultiPartIdentifier = new MultiPartIdentifier
        {
            Identifiers = { new Identifier { Value = column.Base, QuoteType = QuoteType.SquareBracket } },
        },
    };

    private static ColumnReferenceExpression Inserted(Name column) => new()
    {
        ColumnType = Microsoft.SqlServer.TransactSql.ScriptDom.ColumnType.Regular,
        MultiPartIdentifier = new MultiPartIdentifier
        {
            Identifiers =
            {
                new Identifier { Value = "INSERTED" }, new Identifier { Value = column.Base, QuoteType = QuoteType.SquareBracket },
            },
        },
    };

    /// <summary>A one-part name for a constraint, as ALTER TABLE names one.</summary>
    private static Name Named(string part) => Name.Of(part) is Result<Name>.Ok(var name) ? name
        : throw new InvalidOperationException("A constraint's name part is no name.");

    private static string Number(long n) => n.ToString(CultureInfo.InvariantCulture);
}
