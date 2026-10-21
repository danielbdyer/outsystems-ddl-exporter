using System;
using System.Collections.Generic;
using System.Linq;

namespace DbChange.Kernel;

/// <summary>What a vocabulary row says a statement asks of the existing rows: nothing; a kind the property read finds, where it holds
/// one for the statement's object; or a kind only a plan's statements show, which the row raises.</summary>
public enum Asks
{
    Nothing,
    TableEmpty,
    NotNullOrFits,
    Unique,
    ForeignKeyOrCheck,
    KeyLength,
    Rebuild,
    Computed,
    Temporal,
    Scan,
}

/// <summary>
/// A condition a row sets beside its words, which the plan or a model decides: every object the statement reads is outside the database's
/// rows (<see cref="PlanObject.OutsideTheRows"/>); its target is; its target is DacFx's tmp_ms_xx_&lt;T&gt; and it reads &lt;T&gt;; its
/// target is a tmp_ms_xx_ table; it reads a table another statement copies into a tmp_ms_xx_ table; its key's widest value, by the model
/// the change brings, passes the index's limit; a column of its key or its INCLUDE is computed and not persisted there; what it drops is
/// a clustered key or index in the model before the change.
/// </summary>
public enum When
{
    ReadsNoRows,
    TargetOutsideTheRows,
    IntoRebuild,
    OnRebuild,
    ReadsRebuilt,
    KeyOverLimit,
    ComputedKey,
    DropsClustered,
}

/// <summary>
/// One row of the vocabulary: the ScriptDom type of the statement it matches, the words the statement holds and those it does not, the
/// conditions on the plan and the models, what a statement it matches asks of the existing rows, and the case that fails when the row is
/// removed (the Facsimile's E1 and M1: no row without its case).
/// </summary>
public sealed record VocabularyRow(string Type, Asks Asks, string Case, IReadOnlyList<string> With, IReadOnlyList<string> Without,
    IReadOnlyList<When> When);

/// <summary>How a statement of the plan reads the existing rows (the Facsimile's F4): it reads none; it reads them and no value can fail it
/// (Scan); a value can fail it; it is the change's own pre- or post-deploy script, which is authored; or no row of the vocabulary matches
/// it, an unclassified change.</summary>
public enum StatementClass
{
    ReadsNothing,
    DataInsensitive,
    DataSensitive,
    Authored,
    Unknown,
}

/// <summary>
/// What the vocabulary read of a plan: the preconditions the change asserts, the property read's that the plan shows and the kinds only
/// its statements show; and each statement, with its class and the preconditions it asks. A plan that holds an unclassified statement is
/// an unclassified change, which WP 2.5 routes to proof.
/// </summary>
public sealed record Reading(SortedArray<Precondition> Preconditions, SortedArray<Reading.Read> Statements)
{
    /// <summary>The statements no row matches.</summary>
    public SortedArray<PlanStatement> Unclassified =>
        SortedArray.Of(Statements.Where(s => s.Class == StatementClass.Unknown).Select(s => s.Statement));

    /// <summary>Each unclassified statement as a finding, by its line.</summary>
    public IEnumerable<Finding> Findings => Unclassified.Select(s => Finding.Warning(Vocabulary.UnclassifiedCode, Vocabulary.Where(s),
        Vocabulary.Where(s) + ", " + s.Type + (s.Target is { } target ? " on " + target.Name : "") + ", is matched by no row of the"
            + " vocabulary, so what it reads of the existing rows is not known: an unclassified change.",
        "Prove the change on a copy with dbchange prove, or add a row for the statement's shape with its case."));

    /// <summary>A statement, its class, and the preconditions it asks.</summary>
    public sealed record Read(PlanStatement Statement, StatementClass Class, SortedArray<Precondition> Asks) : IComparable<Read>
    {
        public int CompareTo(Read? other) => other is null ? 1 : Statement.CompareTo(other.Statement);
    }
}

/// <summary>
/// The Facsimile's vocabulary (tests/Cases/ARCHITECTURE.md §4) as v3 reads it (WP 2.1's second slice): a table of rows,
/// which is data, matched on io's statements of DacFx's plan. Every row whose type, words and conditions a statement meets adds what it
/// asks; a kind the property read finds counts only where Preconditions.Of holds one for the statement's object, and a statement no row
/// counts for is unclassified, so the world is closed: a new DacFx shape enters as a row with its case. A statement that asks a kind a
/// value can fail is data-sensitive and no Scan. The change's own deploy scripts are read apart (Vocabulary.Scripts.cs).
/// </summary>
public static partial class Vocabulary
{
    internal const string UnclassifiedCode = "plan.unclassified-statement";

    private const string Rebuilding = "tmp_ms_xx_";

    /// <summary>
    /// The rows, each with its case. First what reads no row of the database: DacFx's frame around its statements (the header, SQLCMD's
    /// check, the transaction and its #tmpErrors, the Query Store block, the constraint verification's #__checkStatus and its cursor's
    /// loop), statements that write no existing value (a nullable or computed column added, a default constraint, a constraint added
    /// WITH NOCHECK, a drop, an index dropped, a schema transfer) and the objects that hold no rows. Then the kinds the property read
    /// finds, then the kinds only the plan shows. Spike S2 measured each statement on DacFx 162.5.57 and 170.5.96 (DECISIONS.md,
    /// 2026-10-19).
    /// </summary>
    public static IReadOnlyList<VocabularyRow> Rows { get; } =
    [
        R("PrintStatement", Asks.Nothing, "QST-02"),
        R("PredicateSetStatement", Asks.Nothing, "QST-04"),
        R("SetTransactionIsolationLevelStatement", Asks.Nothing, "S2 make-mandatory"),
        R("SetIdentityInsertStatement", Asks.Nothing, "QST-64"),
        R("UseStatement", Asks.Nothing, "QST-02"),
        R("BeginTransactionStatement", Asks.Nothing, "QST-63"),
        R("CommitTransactionStatement", Asks.Nothing, "QST-63"),
        R("RollbackTransactionStatement", Asks.Nothing, "S2 make-mandatory"),
        R("BeginEndBlockStatement", Asks.Nothing, "QST-04"),
        R("TryCatchStatement", Asks.Nothing, "S2 add-unnamed-constraints"),
        R("RaiseErrorStatement", Asks.Nothing, "QST-01"),
        R("WaitForStatement", Asks.Nothing, "S2 add-a-foreign-key", ["DELAY"]),
        R("DeclareVariableStatement", Asks.Nothing, "S2 add-unnamed-constraints", when: [When.ReadsNoRows]),
        R("IfStatement", Asks.Nothing, "QST-04", without: ["RAISERROR 127"], when: [When.ReadsNoRows]),
        R("WhileStatement", Asks.Nothing, "S2 add-unnamed-constraints", ["@@FETCH_STATUS"], when: [When.ReadsNoRows]),
        R("OpenCursorStatement", Asks.Nothing, "S2 add-unnamed-constraints"),
        R("FetchCursorStatement", Asks.Nothing, "S2 add-unnamed-constraints"),
        R("CloseCursorStatement", Asks.Nothing, "S2 add-unnamed-constraints"),
        R("DeallocateCursorStatement", Asks.Nothing, "S2 add-unnamed-constraints"),
        R("SelectStatement", Asks.Nothing, "S2 add-unnamed-constraints", without: ["INTO"], when: [When.ReadsNoRows]),
        R("CreateTableStatement", Asks.Nothing, "QST-45"),
        R("DropTableStatement", Asks.Nothing, "QST-74"),
        R("InsertStatement", Asks.Nothing, "QST-71", ["VALUES"], when: [When.TargetOutsideTheRows]),
        R("ExecuteStatement", Asks.Nothing, "QST-71", ["PROCEDURE sp_rename"]),
        R("ExecuteStatement", Asks.Nothing, "QST-69", ["PROCEDURE sp_addextendedproperty"]),
        R("ExecuteStatement", Asks.Nothing, "QST-02", ["PROCEDURE sp_refreshsqlmodule"]),
        R("AlterDatabaseSetStatement", Asks.Nothing, "QST-05"),
        R("AlterTableAddTableElementStatement", Asks.Nothing, "QST-69", ["COLUMN", "NULL"], ["AS"]),
        R("AlterTableAddTableElementStatement", Asks.Nothing, "QST-67", ["COLUMN", "AS"], ["PERSISTED"]),
        R("AlterTableAddTableElementStatement", Asks.Nothing, "QST-10", ["COLUMN", "NOT NULL"], ["AS", "DEFAULT"]),
        R("AlterTableAddTableElementStatement", Asks.Nothing, "QST-69", ["DEFAULT"], ["COLUMN"]),
        R("AlterTableAddTableElementStatement", Asks.Nothing, "QST-50", ["WITH NOCHECK", "FOREIGN KEY"]),
        R("AlterTableAddTableElementStatement", Asks.Nothing, "QST-60", ["WITH NOCHECK", "CHECK"]),
        R("AlterTableDropTableElementStatement", Asks.Nothing, "QST-73"),
        R("AlterTableConstraintModificationStatement", Asks.Nothing, "QST-57", without: ["WITH CHECK"]),
        R("AlterTableSetStatement", Asks.Nothing, "S2 convert-to-temporal", ["SYSTEM_VERSIONING = OFF"]),
        R("AlterSchemaStatement", Asks.Nothing, "QST-72"),
        R("DropIndexStatement", Asks.Nothing, "QST-76"),
        R("CreateSchemaStatement", Asks.Nothing, "QST-69"),
        R("CreateViewStatement", Asks.Nothing, "QST-69"),
        R("CreateProcedureStatement", Asks.Nothing, "QST-69"),
        R("GrantStatement", Asks.Nothing, "QST-69"),
        R("CreateRoleStatement", Asks.Nothing, "S2 grant-two-roles"),
        R("CreateFunctionStatement", Asks.Nothing, "S2 add-one-object-of-each-serialized-type"),
        R("CreateTypeUddtStatement", Asks.Nothing, "S2 add-one-object-of-each-serialized-type"),
        R("CreateTypeTableStatement", Asks.Nothing, "S2 add-one-object-of-each-serialized-type"),
        R("CreateSequenceStatement", Asks.Nothing, "S2 declare-what-the-planter-reads"),
        R("CreateTriggerStatement", Asks.Nothing, "S2 declare-what-the-planter-reads"),
        R("AlterTableAlterColumnStatement", Asks.Nothing, "S2 make-optional", ["NULL"]),

        R("IfStatement", Asks.TableEmpty, "QST-01", ["RAISERROR 127"]),
        R("AlterTableAlterColumnStatement", Asks.NotNullOrFits, "QST-16"),
        R("CreateIndexStatement", Asks.Unique, "QST-35", ["UNIQUE"]),
        R("AlterTableAddTableElementStatement", Asks.Unique, "QST-41", ["UNIQUE"]),
        R("AlterTableAddTableElementStatement", Asks.Unique, "QST-42", ["PRIMARY KEY"]),
        R("AlterTableConstraintModificationStatement", Asks.ForeignKeyOrCheck, "QST-50", ["WITH CHECK", "CHECK CONSTRAINT"]),
        R("DeclareCursorStatement", Asks.ForeignKeyOrCheck, "S2 add-unnamed-constraints", when: [When.ReadsNoRows]),

        R("CreateIndexStatement", Asks.KeyLength, "QST-47", when: [When.KeyOverLimit]),
        R("AlterTableAddTableElementStatement", Asks.KeyLength, "QST-49", ["PRIMARY KEY"], when: [When.KeyOverLimit]),
        R("AlterTableAddTableElementStatement", Asks.KeyLength, "a unique constraint over the key limit", ["UNIQUE"],
            when: [When.KeyOverLimit]),
        R("CreateTableStatement", Asks.KeyLength, "a rebuilt table's primary key over the key limit", ["PRIMARY KEY"],
            when: [When.OnRebuild, When.KeyOverLimit]),
        R("CreateTableStatement", Asks.KeyLength, "a rebuilt table's unique constraint over the key limit", ["UNIQUE"],
            when: [When.OnRebuild, When.KeyOverLimit]),
        R("InsertStatement", Asks.Rebuild, "QST-63", ["SELECT"], when: [When.IntoRebuild]),
        R("IfStatement", Asks.Rebuild, "QST-64", without: ["RAISERROR 127"], when: [When.ReadsRebuilt]),
        R("CreateTableStatement", Asks.Computed, "QST-63", ["PERSISTED"], when: [When.OnRebuild]),
        R("AlterTableAddTableElementStatement", Asks.Computed, "QST-66", ["COLUMN", "AS", "PERSISTED"]),
        R("CreateIndexStatement", Asks.Computed, "QST-68", when: [When.ComputedKey]),
        R("AlterTableSetStatement", Asks.Temporal, "QST-89", ["SYSTEM_VERSIONING = ON"]),
        R("AlterTableAddTableElementStatement", Asks.Temporal, "QST-105", ["PERIOD FOR SYSTEM_TIME"]),
        R("CreateIndexStatement", Asks.Scan, "QST-44", without: ["UNIQUE"]),
        R("AlterTableAddTableElementStatement", Asks.Scan, "QST-70", ["COLUMN", "NOT NULL", "DEFAULT"], ["AS"]),
        R("AlterTableDropTableElementStatement", Asks.Scan, "QST-107", ["DROP CONSTRAINT"], when: [When.DropsClustered]),
        R("CreateStatisticsStatement", Asks.Scan, "S2 add-one-object-of-each-serialized-type"),
    ];

    /// <summary>
    /// The plan read by the rows. <paramref name="plan"/> is io's listing of DacFx's script; <paramref name="scriptsBefore"/> the listing
    /// of the deploy scripts of the model before the change; <paramref name="preconditions"/> what Preconditions.Of finds between
    /// <paramref name="before"/> and <paramref name="after"/> for <paramref name="change"/>. The property read's ForeignKey or Check stands
    /// only where a statement validates it, on a table the plan validates, since a constraint DacFx adds WITH NOCHECK and never checks
    /// reads no row (the Facsimile's QST-56 and QST-57); its TableEmpty only where a BlockOnPossibleDataLoss check among DacFx's own
    /// statements reads the table, since the same IF EXISTS … RAISERROR written in a deploy script is the script's own statement, Authored
    /// (QST-81); the rest of the property read stands as found, and each kind a row raises joins it.
    /// </summary>
    public static Reading Read(SortedArray<PlanStatement> plan, SortedArray<PlanStatement> scriptsBefore,
        SortedArray<Precondition> preconditions, SortedArray<Element> before, SortedArray<Element> after, Change change,
        IReadOnlyList<VocabularyRow>? rows = null)
    {
        var context = new Context(plan, preconditions, Keyed(before), Keyed(after), change);
        var read = new List<Reading.Read>();
        var raised = new List<Precondition>();
        foreach (var statement in plan.Where(s => s.Part == ScriptPart.Plan))
        {
            var asked = new List<Asked>();
            foreach (var row in (rows ?? Rows).Where(r => Matches(r, statement, context)))
            {
                if (Ask(row.Asks, statement, context) is { } answer)
                {
                    asked.Add(answer);
                }
            }

            var sensitive = asked.Any(a => a.Asks is not (Asks.Nothing or Asks.Scan));
            var asks = asked.Where(a => !sensitive || a.Asks != Asks.Scan).SelectMany(a => a.Preconditions).Distinct().ToArray();
            raised.AddRange(asks.Where(p => !preconditions.Contains(p)));
            read.Add(new Reading.Read(statement, asked.Count == 0 ? StatementClass.Unknown
                : sensitive ? StatementClass.DataSensitive
                : asked.Any(a => a.Asks == Asks.Scan) ? StatementClass.DataInsensitive : StatementClass.ReadsNothing,
                SortedArray.Of(asks)));
        }

        var changed = Authored(plan, scriptsBefore, context, s => (rows ?? Rows).Any(r => Matches(r, s, context)));
        read.AddRange(plan.Where(s => s.Part != ScriptPart.Plan).Select(s => new Reading.Read(s,
            changed.Any(c => c.Unread && c.Statement == s) ? StatementClass.Unknown : StatementClass.Authored,
            SortedArray.Of(changed.Where(c => c.Statement == s).Select(c => c.Asks).OfType<Precondition>()))));
        var kept = preconditions.Where(p => p.Kind switch
            {
                PreconditionKind.ForeignKey or PreconditionKind.Check => context.Validated.ContainsKey(p.Table),
                PreconditionKind.TableEmpty => context.Checked.Contains(p.Table),
                _ => true,
            }).Select(p => StandsFor(p, changed, context));
        return new Reading(SortedArray.Of(kept.Concat(raised).Concat(changed.Select(c => c.Asks).OfType<Precondition>()).Distinct()),
            SortedArray.Of(read));
    }

    /// <summary>A statement's place, as a finding names it: its line in the plan's script, or in the change's pre- or post-deploy
    /// script.</summary>
    public static string Where(PlanStatement s) => "Line " + s.Line.ToString(System.Globalization.CultureInfo.InvariantCulture) + " of "
        + s.Part switch
        {
            ScriptPart.PreDeploy => "the pre-deploy script",
            ScriptPart.PostDeploy => "the post-deploy script",
            _ => "the plan's script",
        };

    /// <summary>What one matched row asks: the row's kind, and the preconditions it gives, none for a row that asks nothing or a
    /// precondition the change answers.</summary>
    private sealed record Asked(Asks Asks, Precondition[] Preconditions);

    private static VocabularyRow R(string type, Asks asks, string @case, string[]? with = null, string[]? without = null,
        When[]? when = null) => new(type, asks, @case, with ?? [], without ?? [], when ?? []);

    private static bool Matches(VocabularyRow row, PlanStatement s, Context context) =>
        row.Type == s.Type && row.With.All(s.Has) && !row.Without.Any(s.Has) && row.When.All(w => Holds(w, s, context));

    private static bool Holds(When when, PlanStatement s, Context context) => when switch
    {
        When.ReadsNoRows => s.In(PlanRole.Read).All(o => o.OutsideTheRows),
        When.TargetOutsideTheRows => s.Target is { OutsideTheRows: true },
        When.IntoRebuild => s.Target is { } t && Copied(t.Name) is { } from && s.In(PlanRole.Read).Any(o => o.Name == from),
        When.OnRebuild => s.Target is { } t && Copied(t.Name) is not null,
        When.ReadsRebuilt => s.In(PlanRole.Read).Any(o => context.Rebuilt.Contains(o.Name)),
        When.KeyOverLimit => s.Target is { } t && context.KeyBytes(Table(t.Name), s.Columns) > (s.Has("CLUSTERED") ? 900 : 1700),
        When.ComputedKey => s.Target is { } t
            && s.Columns.Concat(s.Included).Any(c => context.Computed(Table(t.Name), c) is { Persisted: false }),
        When.DropsClustered => s.Target is { } t && s.In(PlanRole.Element).Any(e => context.Clustered(Table(t.Name), e.Name)),
        _ => throw new ArgumentOutOfRangeException(nameof(when)),
    };

    /// <summary>What a matched row asks of the statement, or null where a row of a kind the property read finds finds none there.</summary>
    private static Asked? Ask(Asks asks, PlanStatement s, Context context)
    {
        var table = s.Target is { } target ? Table(target.Name) : (Name?)null;
        Asked Raises(params Precondition?[] raised) => new(asks, [.. raised.OfType<Precondition>()]);
        switch (asks)
        {
            case Asks.Nothing:
                return Raises();
            case Asks.TableEmpty:
                if (context.Held(s.In(PlanRole.Read).Select(o => o.Name), p => p.Kind is PreconditionKind.TableEmpty) is not { } empty)
                {
                    return null;
                }

                context.Checked.UnionWith(empty.Select(p => p.Table));
                return new(asks, empty);
            case Asks.NotNullOrFits:
                return table is { } altered && context.Held([altered],
                    p => p.Kind is PreconditionKind.NotNull or PreconditionKind.Fits && p.Columns == s.Columns) is { } typed
                    ? new(asks, typed) : null;
            case Asks.Unique:
                return table is { } keyed && context.Held([keyed], p => p.Kind is PreconditionKind.Unique && p.Columns == s.Columns)
                    is { } unique ? new(asks, unique) : null;
            case Asks.ForeignKeyOrCheck:
                // a constraint the statement names is validated whether the change makes it or rebuilds its table (QST-63, QST-55)
                var tables = table is { } checkedTable ? [checkedTable] : s.In(PlanRole.Named).Select(o => o.Name).ToArray();
                var named = table is { } host ? s.In(PlanRole.Element).Select(e => context.Rule(host, e.Name)).OfType<Precondition>() : [];
                var validated = (context.Held(tables, p => p.Kind is PreconditionKind.ForeignKey or PreconditionKind.Check) ?? [])
                    .Concat(named).Distinct().ToArray();
                if (validated.Length == 0)
                {
                    return null;
                }

                foreach (var t in tables)
                {
                    context.Validated.TryAdd(t, s.Ordinal);
                }

                return new(asks, validated);
            case Asks.KeyLength:
                // a rebuild's key constraint is DacFx's tmp_ms_xx_constraint_… until its sp_rename, so it takes the model's name
                var index = s.Target is { } copy && Copied(copy.Name) is not null
                    ? context.KeyOf(table!.Value, s.Has("PRIMARY KEY") ? "PrimaryKeyConstraint" : "UniqueConstraint", s.Columns)
                    : s.In(PlanRole.Element).Select(e => (Name?)e.Name).FirstOrDefault();
                return Raises(new Precondition(table!.Value, s.Columns, new PreconditionKind.KeyLength(index, s.Has("CLUSTERED"))));
            case Asks.Rebuild:
                var moved = s.In(PlanRole.Read).Select(o => o.Name).First(n => s.Type == "IfStatement" ? context.Rebuilt.Contains(n)
                    : n == Copied(s.Target!.Name));
                return Raises(new Precondition(moved, SortedArray.Of(context.Identity(moved) is { } id ? [id] : Array.Empty<Name>()),
                    new PreconditionKind.Rebuild()));
            case Asks.Computed:
                // ADD … AS … PERSISTED computes its column; a rebuild's table, each persisted column the change makes or alters; an
                // index, each column of its key or its INCLUDE computed on read; and a table the change creates holds no row to compute
                // over, so there the row asks nothing and the statement is read by its other rows, a Scan or a Unique.
                var on = table!.Value;
                if (context.Created(on))
                {
                    return new(Asks.Nothing, []);
                }

                return Raises([.. s.Columns.Concat(s.Included).Select(c => (Column: c, Computed: context.Computed(on, c)))
                    .Where(c => s.Type switch
                    {
                        "CreateTableStatement" => c.Computed is { Persisted: true } && context.Changed(on, c.Column),
                        "CreateIndexStatement" => c.Computed is { Persisted: false },
                        _ => true,
                    })
                    .Select(c => new Precondition(on, [c.Column], new PreconditionKind.Computed(c.Computed?.Expression ?? "")))]);
            case Asks.Temporal:
                return Raises(Temporal(s, table!.Value, context));
            case Asks.Scan:
                return Raises(new Precondition(table!.Value, [], new PreconditionKind.Scan()));
            default:
                throw new ArgumentOutOfRangeException(nameof(asks));
        }
    }

    /// <summary>
    /// The Temporal a statement raises: over the history table SYSTEM_VERSIONING = ON names, the period columns of the table it versions,
    /// the history of that table; or over the table's own period, which ADD PERIOD FOR SYSTEM_TIME names, none where the change creates
    /// both columns, which then hold their defaults, start before end, in every existing row (the Facsimile's QST-105). The period is the
    /// model's, its GENERATED ALWAYS AS ROW START and ROW END columns, or else the statement's own, its start the statement's key.
    /// </summary>
    private static Precondition? Temporal(PlanStatement s, Name table, Context context)
    {
        if ((context.Period(table) ?? (s.Key is [var first] && s.Columns.Where(c => c != first).ToArray() is [var other]
            ? (first, other) : null)) is not { } period)
        {
            return null;
        }

        var (start, end) = period;
        if (s.Has("SYSTEM_VERSIONING = ON"))
        {
            return s.In(PlanRole.History).FirstOrDefault() is { } history
                ? new Precondition(history.Name, [start, end], new PreconditionKind.Temporal(start, end, table)) : null;
        }

        return context.Created(table, start) && context.Created(table, end) ? null
            : new Precondition(table, [start, end], new PreconditionKind.Temporal(start, end, null));
    }

    /// <summary>The table a statement on DacFx's tmp_ms_xx_ table stands for, the one it rebuilds; any other name as it is.</summary>
    private static Name Table(Name name) => Copied(name) ?? name;

    /// <summary>The table whose rows a tmp_ms_xx_ table receives, or null for another name.</summary>
    private static Name? Copied(Name name) =>
        name.Base.StartsWith(Rebuilding, StringComparison.Ordinal) && name.Base.Length > Rebuilding.Length
            ? (name.Schema is { } schema ? Name.Of(schema, name.Base[Rebuilding.Length..]) : Name.Of(name.Base[Rebuilding.Length..]))
                .Match(n => (Name?)n, _ => null)
            : null;

    private static Dictionary<ElementKey, Element> Keyed(SortedArray<Element> model)
    {
        var keyed = new Dictionary<ElementKey, Element>();
        foreach (var e in model)
        {
            keyed.TryAdd(e.Key, e);
        }

        return keyed;
    }
}
