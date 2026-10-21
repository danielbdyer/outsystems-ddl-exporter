using System;
using System.Collections.Generic;
using System.Linq;
using DbChange.Kernel;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using ColumnKind = Microsoft.SqlServer.TransactSql.ScriptDom.ColumnType;

namespace DbChange.Io;

/// <summary>
/// What io reads off one statement's tree, in the words kernel/PlanStatement lists: the words, the objects in their roles, the columns,
/// a key and an index's INCLUDE columns. An IF and a WHILE are read by their condition alone, since the statements an IF holds are listed
/// after it; a CREATE or an ALTER of a view, a procedure, a function or a trigger by its name alone, since its body runs later, if ever.
/// Elsewhere each table a FROM or a JOIN of the statement names, its target's own reference left out, is read, and each OBJECT_ID of a
/// string names an object without reading it. An ALTER TABLE that adds or drops several elements is read once for each, and a CREATE
/// TABLE once for the table and once for each key constraint it declares.
/// </summary>
public static partial class PlanStatements
{
    /// <summary>What one statement, or one element of an ALTER TABLE or a CREATE TABLE, gives the vocabulary.</summary>
    private sealed record Parts(SortedArray<string> Words, SortedArray<PlanObject> Objects, SortedArray<Name> Columns,
        SortedArray<Name> Key)
    {
        public SortedArray<Name> Included { get; init; } = [];
    }

    private static IEnumerable<Parts> Of(TSqlStatement statement) => statement switch
    {
        IfStatement i => [Condition(i.Predicate, Checks(i) ? ["RAISERROR 127"] : [])],
        WhileStatement w => [Condition(w.Predicate, [])],
        CreateIndexStatement c => [Made([.. When(c.Unique, "UNIQUE"), .. When(c.Clustered == true, "CLUSTERED"),
            .. When(c.FilterPredicate is not null, "WHERE")], [Object(PlanRole.Target, c.OnName), Element(c.Name)],
            c.Columns.Select(k => Last(k.Column))) with { Included = Names(Lasts(c.IncludeColumns)) }],
        CreateTableStatement t => Created(t),
        AlterTableAddTableElementStatement a => Added(a),
        AlterTableDropTableElementStatement d => d.AlterTableDropTableElements.Select(e => Dropped(d, e)),
        AlterTableAlterColumnStatement a => [Made([.. Nullability(a.AlterTableAlterColumnOption),
            .. When(a.Collation is not null, "COLLATE")], [Object(PlanRole.Target, a.SchemaObjectName)], [a.ColumnIdentifier])],
        AlterTableConstraintModificationStatement m => [Made([.. Enforced("WITH ", m.ExistingRowsCheckEnforcement, ""),
            .. Enforced("", m.ConstraintEnforcement, " CONSTRAINT"), .. When(m.All, "ALL")],
            [Object(PlanRole.Target, m.SchemaObjectName), .. m.ConstraintNames.Select(Element)])],
        AlterTableSetStatement s => [Made(s.Options.Select(o => o is SystemVersioningTableOption v
                ? "SYSTEM_VERSIONING = " + v.OptionState.ToString().ToUpperInvariant()
                : "SET " + o.OptionKind.ToString().ToUpperInvariant()),
            [Object(PlanRole.Target, s.SchemaObjectName), .. s.Options.OfType<SystemVersioningTableOption>()
                .Select(v => Object(PlanRole.History, v.HistoryTable))])],
        InsertStatement i => [Leaf(i, Source(i.InsertSpecification.InsertSource), i.InsertSpecification.Target,
            Lasts(i.InsertSpecification.Columns))],
        SelectStatement s => [Made(When(s.Into is not null, "INTO"),
            [Object(PlanRole.Target, s.Into), .. Reads(s, null), .. ObjectIds(s)])],
        MergeStatement m => [Merged(m)],
        UpdateStatement u => [Leaf(u, [], u.UpdateSpecification.Target, [.. Lasts(u.UpdateSpecification.SetClauses
            .OfType<AssignmentSetClause>().Select(c => c.Column).OfType<ColumnReferenceExpression>()),
            .. Lasts(ColumnsIn(u.UpdateSpecification.WhereClause))])],
        DeleteStatement d => [Leaf(d, [], d.DeleteSpecification.Target, Lasts(ColumnsIn(d.DeleteSpecification.WhereClause)))],
        ExecuteStatement e => [Executed(e)],
        WaitForStatement w => [Made([w.WaitForOption.ToString().ToUpperInvariant()], [])],
        CreateStatisticsStatement c => [Made([], [Object(PlanRole.Target, c.OnName), Element(c.Name)], Lasts(c.Columns))],
        DropIndexStatement d => d.DropIndexClauses.OfType<DropIndexClause>()
            .Select(c => Made([], [Object(PlanRole.Target, c.Object), Element(c.Index)])),
        DropTableStatement d => [Made([], d.Objects.Select(o => Object(PlanRole.Target, o)))],
        TruncateTableStatement t => [Made([], [Object(PlanRole.Target, t.TableName)])],
        UpdateStatisticsStatement u => [Made([], [Object(PlanRole.Target, u.SchemaObjectName)])],
        AlterIndexStatement x => [Made([], [Object(PlanRole.Target, x.OnName), Element(x.Name)])],
        CreateFullTextIndexStatement f => [Made([], [Object(PlanRole.Target, f.OnName)])],
        AlterTableStatement a => [Made([], [Object(PlanRole.Target, a.SchemaObjectName), .. Reads(a, null), .. ObjectIds(a)])],
        ViewStatementBody or ProcedureStatementBodyBase or TriggerStatementBody => [Made([], [Defined(statement)])],
        BeginEndBlockStatement or TryCatchStatement => [Made([], [])],
        _ => [Made([], [.. Reads(statement, null), .. ObjectIds(statement)])],
    };

    /// <summary>Whether an IF is a BlockOnPossibleDataLoss check, as io/BlockOnPossibleDataLoss matches one: EXISTS (…), one RAISERROR
    /// at state 127, and no ELSE.</summary>
    private static bool Checks(IfStatement i) => i is { Predicate: ExistsPredicate, ElseStatement: null }
        && i.ThenStatement is RaiseErrorStatement { ThirdParameter: IntegerLiteral { Value: BlockOnPossibleDataLoss.State } };

    private static IEnumerable<string> Nullability(AlterTableAlterColumnOption option) => option switch
    {
        AlterTableAlterColumnOption.Null => ["NULL"],
        AlterTableAlterColumnOption.NotNull => ["NOT NULL"],
        _ => [],
    };

    private static IEnumerable<string> Source(InsertSource? source) => source switch
    {
        SelectInsertSource => ["SELECT"],
        ValuesInsertSource => ["VALUES"],
        ExecuteInsertSource => ["EXECUTE"],
        _ => [],
    };

    /// <summary>
    /// One element added, each its own statement: a column, COLUMN with NULL or NOT NULL, AS and PERSISTED for a computed one and DEFAULT
    /// for a default it declares, and the words of a constraint it declares inline; a primary key or unique constraint, PRIMARY KEY or
    /// UNIQUE and CLUSTERED; a foreign key, FOREIGN KEY, the table it references Referenced; a check, CHECK, over the columns its condition
    /// reads; a default, DEFAULT; a period, PERIOD FOR SYSTEM_TIME over its two columns, its start the key. WITH CHECK or WITH NOCHECK
    /// stands beside each.
    /// </summary>
    private static IEnumerable<Parts> Added(AlterTableAddTableElementStatement a)
    {
        string[] with = [.. Enforced("WITH ", a.ExistingRowsCheckEnforcement, "")];
        var target = Object(PlanRole.Target, a.SchemaObjectName);
        foreach (var column in a.Definition?.ColumnDefinitions ?? [])
        {
            string[] words = ["COLUMN",
                .. column.Constraints.OfType<NullableConstraintDefinition>().Select(n => n.Nullable ? "NULL" : "NOT NULL"),
                .. When(column.ComputedColumnExpression is not null, "AS"), .. When(column.IsPersisted, "PERSISTED"),
                .. When(column.DefaultConstraint is not null, "DEFAULT"), .. column.Constraints.SelectMany(Constrains)];
            yield return Made([.. with, .. words], [target], [column.ColumnIdentifier]);
        }

        foreach (var constraint in a.Definition?.TableConstraints ?? [])
        {
            var referenced = constraint is ForeignKeyConstraintDefinition f ? Object(PlanRole.Referenced, f.ReferenceTableName) : null;
            yield return Made([.. with, .. Constrains(constraint)], [target, Element(constraint.ConstraintIdentifier), referenced],
                constraint switch
                {
                    UniqueConstraintDefinition u => u.Columns.Select(c => Last(c.Column)),
                    ForeignKeyConstraintDefinition k => k.Columns,
                    CheckConstraintDefinition c => Lasts(ColumnsIn(c.CheckCondition)),
                    DefaultConstraintDefinition { Column: { } on } => [on],
                    _ => [],
                });
        }

        if (a.Definition?.SystemTimePeriod is { } period)
        {
            yield return Made([.. with, "PERIOD FOR SYSTEM_TIME"], [target], [period.StartTimeColumn, period.EndTimeColumn],
                [period.StartTimeColumn]);
        }
    }

    /// <summary>
    /// A CREATE TABLE: the table, PERSISTED where it holds a persisted computed column, over its columns; then each PRIMARY KEY or UNIQUE
    /// constraint it declares, of the table or of one column, its own statement over its key's columns with its words, PRIMARY KEY or
    /// UNIQUE and CLUSTERED, as an ALTER TABLE's added constraint is, so the key a rebuild's tmp_ms_xx_ table declares inline is read as
    /// its ADD CONSTRAINT would be (spike S2: a clustered primary key redefined arrives as a rebuild, never as ADD CONSTRAINT).
    /// </summary>
    private static IEnumerable<Parts> Created(CreateTableStatement t)
    {
        var target = Object(PlanRole.Target, t.SchemaObjectName);
        var columns = t.Definition?.ColumnDefinitions ?? [];
        yield return Made(When(columns.Any(d => d is { ComputedColumnExpression: not null, IsPersisted: true }), "PERSISTED"), [target],
            columns.Select(d => d.ColumnIdentifier));
        var keys = (t.Definition?.TableConstraints ?? []).OfType<UniqueConstraintDefinition>()
            .Select(u => (Key: u, Columns: u.Columns.Select(c => Last(c.Column))))
            .Concat(columns.SelectMany(d => d.Constraints.OfType<UniqueConstraintDefinition>()
                .Select(u => (Key: u, Columns: (IEnumerable<Identifier>)[d.ColumnIdentifier]))));
        foreach (var (key, on) in keys)
        {
            yield return Made(Constrains(key), [target, Element(key.ConstraintIdentifier)], on);
        }
    }

    /// <summary>One element dropped: DROP COLUMN over its column, DROP CONSTRAINT or DROP INDEX of its name, DROP PERIOD.</summary>
    private static Parts Dropped(AlterTableDropTableElementStatement d, AlterTableDropTableElement e) => Made(
        ["DROP " + (e.TableElementType is TableElementType.NotSpecified ? "CONSTRAINT" : e.TableElementType.ToString().ToUpperInvariant())],
        [Object(PlanRole.Target, d.SchemaObjectName), e.TableElementType is TableElementType.Column ? null : Element(e.Name)],
        e.TableElementType is TableElementType.Column && e.Name is { } column ? [column] : []);

    /// <summary>A constraint's words: PRIMARY KEY or UNIQUE, and CLUSTERED; FOREIGN KEY; CHECK; DEFAULT.</summary>
    private static IEnumerable<string> Constrains(ConstraintDefinition constraint) => constraint switch
    {
        UniqueConstraintDefinition u => [u.IsPrimaryKey ? "PRIMARY KEY" : "UNIQUE", .. When(u.Clustered == true, "CLUSTERED")],
        ForeignKeyConstraintDefinition => ["FOREIGN KEY"],
        CheckConstraintDefinition => ["CHECK"],
        DefaultConstraintDefinition => ["DEFAULT"],
        _ => [],
    };

    /// <summary>
    /// A MERGE: VALUES where its source is a list of rows, its target, the tables its source and conditions read, the target's columns its
    /// INSERT and UPDATE write, and its key, the target's columns its ON compares, by the target's alias or by no alias.
    /// </summary>
    private static Parts Merged(MergeStatement m)
    {
        var spec = m.MergeSpecification;
        var alias = spec.TableAlias?.Value;
        var written = spec.ActionClauses.SelectMany(c => c.Action switch
        {
            InsertMergeAction i => Lasts(i.Columns),
            UpdateMergeAction u =>
                Lasts(u.SetClauses.OfType<AssignmentSetClause>().Select(s => s.Column).OfType<ColumnReferenceExpression>()),
            _ => [],
        });
        var key = References(spec.SearchCondition).Where(c => c.MultiPartIdentifier.Identifiers.Count == 1 ? alias is null
                : string.Equals(c.MultiPartIdentifier.Identifiers[^2].Value, alias, StringComparison.OrdinalIgnoreCase))
            .Select(Last);
        return Leaf(m, When(spec.TableReference is InlineDerivedTable, "VALUES"), spec.Target, written) with { Key = Names(key) };
    }

    /// <summary>An EXECUTE: PROCEDURE and the procedure's one-part name, the procedure its target; or DYNAMIC, for a string or a
    /// variable it runs.</summary>
    private static Parts Executed(ExecuteStatement e) =>
        e.ExecuteSpecification?.ExecutableEntity is ExecutableProcedureReference { ProcedureReference.ProcedureReference.Name: { } name }
            ? Made(["PROCEDURE " + name.BaseIdentifier.Value], [Object(PlanRole.Target, name), .. ObjectIds(e)])
            : Made(["DYNAMIC"], [.. ObjectIds(e)]);

    /// <summary>A condition's statement: the words given and each global variable it reads (@@ERROR, @@TRANCOUNT, …), the tables its
    /// FROMs read, the objects its OBJECT_IDs name and the columns it reads.</summary>
    private static Parts Condition(BooleanExpression condition, IEnumerable<string> words) =>
        Made([.. words, .. Visited<GlobalVariableExpression>(condition).Select(g => g.Name.ToUpperInvariant())],
            [.. Reads(condition, null), .. ObjectIds(condition)], Lasts(ColumnsIn(condition)));

    /// <summary>An INSERT, an UPDATE, a DELETE or a MERGE: its words, its target, every other table it reads, every object OBJECT_ID
    /// names in it, and the columns given.</summary>
    private static Parts Leaf(TSqlStatement statement, IEnumerable<string> words, TableReference? target,
        IEnumerable<Identifier> columns) =>
        Made(words, [Targeted(target, statement), .. Reads(statement, target), .. ObjectIds(statement)], columns);

    /// <summary>A view's, procedure's, function's or trigger's name, which its CREATE or ALTER makes; its body runs later, if
    /// ever.</summary>
    private static PlanObject? Defined(TSqlStatement statement) => statement switch
    {
        ViewStatementBody v => Object(PlanRole.Target, v.SchemaObjectName),
        ProcedureStatementBody p => Object(PlanRole.Target, p.ProcedureReference?.Name),
        FunctionStatementBody f => Object(PlanRole.Target, f.Name),
        TriggerStatementBody t => Object(PlanRole.Target, t.Name),
        _ => null,
    };

    /// <summary>The target of an INSERT, UPDATE, DELETE or MERGE: the table it names, or the table an alias of its FROM stands
    /// for.</summary>
    private static PlanObject? Targeted(TableReference? target, TSqlStatement statement) => target is not NamedTableReference named ? null
        : named.SchemaObject.Identifiers.Count == 1 && TSql.TableReferences(statement).FirstOrDefault(t => !ReferenceEquals(t, named)
            && string.Equals(t.Alias?.Value, named.SchemaObject.BaseIdentifier.Value, StringComparison.OrdinalIgnoreCase)) is { } aliased
            ? Object(PlanRole.Target, aliased.SchemaObject)
        : Object(PlanRole.Target, named.SchemaObject);

    /// <summary>Each table a fragment's FROMs and JOINs name, the target's own reference left out.</summary>
    private static IEnumerable<PlanObject?> Reads(TSqlFragment fragment, TableReference? target) => TSql.TableReferences(fragment)
        .Where(t => !ReferenceEquals(t, target)).Select(t => Object(PlanRole.Read, t.SchemaObject));

    /// <summary>Each object a fragment's OBJECT_ID names by a string, as the string names it, three parts with the database's.</summary>
    private static IEnumerable<PlanObject?> ObjectIds(TSqlFragment fragment) => Visited<FunctionCall>(fragment)
        .Where(f => string.Equals(f.FunctionName.Value, "OBJECT_ID", StringComparison.OrdinalIgnoreCase)
            && f.Parameters.Count > 0 && f.Parameters[0] is StringLiteral)
        .Select(f => TSql.NameParts(((StringLiteral)f.Parameters[0]).Value) is { } parts ? Object(PlanRole.Named, parts) : null);

    private static PlanObject? Object(PlanRole role, SchemaObjectName? name) =>
        name is null ? null : Object(role, [.. name.Identifiers.Select(i => i?.Value ?? "")]);

    /// <summary>An object of one to four parts: its database where it names one, its schema where it names one, and its name.</summary>
    private static PlanObject? Object(PlanRole role, string[] parts)
    {
        if (parts.Length is 0 or > 4 || parts[^1].Length == 0)
        {
            return null;
        }

        var schema = parts.Length >= 2 && parts[^2].Length > 0 ? parts[^2] : null;
        var database = parts.Length >= 3 && parts[^3].Length > 0 ? parts[^3] : null;
        return (schema is null ? Name.Of(parts[^1]) : Name.Of(schema, parts[^1])) is Result<Name>.Ok { Value: var name }
            ? new PlanObject(role, name, database) : null;
    }

    private static PlanObject? Element(Identifier? name) =>
        name is not null && Name.Of(name.Value) is Result<Name>.Ok { Value: var one } ? new PlanObject(PlanRole.Element, one) : null;

    /// <summary>A CHECK or NOCHECK as the words write it, after <paramref name="before"/> and before <paramref name="after"/>; none
    /// where the statement writes neither.</summary>
    private static IEnumerable<string> Enforced(string before, ConstraintEnforcement enforcement, string after) => enforcement switch
    {
        ConstraintEnforcement.Check => [before + "CHECK" + after],
        ConstraintEnforcement.NoCheck => [before + "NOCHECK" + after],
        _ => [],
    };

    private static IEnumerable<string> When(bool holds, string word) => holds ? [word] : [];

    private static IEnumerable<ColumnReferenceExpression> ColumnsIn(TSqlFragment? fragment) => fragment is null ? [] : References(fragment);

    private static IEnumerable<ColumnReferenceExpression> References(TSqlFragment fragment) =>
        Visited<ColumnReferenceExpression>(fragment).Where(c => c is { ColumnType: ColumnKind.Regular, MultiPartIdentifier: not null });

    private static Identifier Last(ColumnReferenceExpression column) => column.MultiPartIdentifier.Identifiers[^1];

    private static IEnumerable<Identifier> Lasts(IEnumerable<ColumnReferenceExpression> columns) =>
        columns.Where(c => c.MultiPartIdentifier is not null).Select(Last);

    private static Parts Made(IEnumerable<string> words, IEnumerable<PlanObject?> objects, IEnumerable<Identifier>? columns = null,
        IEnumerable<Identifier>? key = null) =>
        new(SortedArray.Of(words.Distinct(StringComparer.Ordinal)), SortedArray.Of(objects.OfType<PlanObject>().Distinct()),
            Names(columns ?? []), Names(key ?? []));

    private static SortedArray<Name> Names(IEnumerable<Identifier> identifiers) =>
        SortedArray.Of(identifiers.Select(i => Name.Of(i.Value)).OfType<Result<Name>.Ok>().Select(n => n.Value).Distinct());

    /// <summary>Each node of a type a fragment holds, in the order the tree holds them.</summary>
    private static List<T> Visited<T>(TSqlFragment fragment)
        where T : TSqlFragment
    {
        var visitor = new Visitor<T>();
        fragment.Accept(visitor);
        return visitor.Found;
    }

    private sealed class Visitor<T> : TSqlFragmentVisitor
        where T : TSqlFragment
    {
        public List<T> Found { get; } = [];

        public override void Visit(TSqlFragment node)
        {
            if (node is T found)
            {
                Found.Add(found);
            }
        }
    }
}
