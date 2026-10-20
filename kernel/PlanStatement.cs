using System;
using System.Collections.Generic;
using System.Linq;

namespace DbChange.Kernel;

/// <summary>Where a statement of a deploy plan stands: in the pre-deploy script, among DacFx's own statements, or in the post-deploy
/// script, which DacFx's script holds verbatim before and after its own (spike S2, on DacFx 162.5.57 and 170.5.96 alike).</summary>
public enum ScriptPart
{
    PreDeploy,
    Plan,
    PostDeploy,
}

/// <summary>
/// What an object is to the statement that names it: its <see cref="Target"/>, the object it creates, alters, drops, inserts into, merges
/// into or executes; one a FROM of its own expressions reads (<see cref="Read"/>); the table a foreign key it adds references; the history
/// table SYSTEM_VERSIONING = ON names; an object OBJECT_ID's string names, which the statement does not read; and an index or a constraint
/// of the target, by its one-part name, that the statement creates, checks or drops (<see cref="Element"/>).
/// </summary>
public enum PlanRole
{
    Target,
    Read,
    Referenced,
    History,
    Named,
    Element,
}

/// <summary>An object a statement names, in its role: its name, in one or two parts, and the database a three-part name gives it, such as
/// master or tempdb; none for an object of the plan's own database.</summary>
public sealed record PlanObject(PlanRole Role, Name Name, string? Database = null) : IComparable<PlanObject>
{
    /// <summary>Whether the object's rows are none of the database's: a temporary table, an object of the catalog (the sys or
    /// INFORMATION_SCHEMA schema, or another database such as master or tempdb), or DacFx's refactorlog table,
    /// [dbo].[__RefactorLog].</summary>
    public bool OutsideTheRows => Name.Base.StartsWith('#') || Database is not null
        || string.Equals(Name.Schema, "sys", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Name.Schema, "INFORMATION_SCHEMA", StringComparison.OrdinalIgnoreCase)
        || (string.Equals(Name.Schema, "dbo", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Name.Base, "__RefactorLog", StringComparison.OrdinalIgnoreCase));

    public int CompareTo(PlanObject? other) =>
        other is null ? 1
        : Role.CompareTo(other.Role) is var r and not 0 ? r
        : Name.CompareTo(other.Name) is var n and not 0 ? n
        : string.CompareOrdinal(Database, other.Database);
}

/// <summary>
/// A statement of a deploy plan as io reads it from DacFx's script with ScriptDom (WP 2.1's second slice): its place in the listing from 0;
/// where it stands and its first line there, the plan's line for DacFx's own and the script's for a deploy script's; how deep it is nested,
/// 0 for a batch's own and one more inside each IF, BEGIN … END or TRY … CATCH, each listed before the statements it holds; ScriptDom's
/// name for its type, such as CreateIndexStatement; and its text as io's Sql160ScriptGenerator writes it, white space collapsed. Beside
/// these: the words io reads off its tree (<see cref="Words"/>); the objects it names, each in its role; the columns it names on its
/// target, or in its own expressions where it has none; and a MERGE's key, the target columns its ON matches. An ALTER TABLE that adds or
/// drops several elements is one statement for each element, at one line, so each element is read on its own. Statements sort by their
/// place.
/// </summary>
public sealed record PlanStatement(int Ordinal, ScriptPart Part, int Line, int Depth, string Type, string Text)
    : IComparable<PlanStatement>
{
    /// <summary>
    /// What io reads off the statement's tree, in T-SQL's own words, a closed list: of an IF, RAISERROR 127 when it raises at state 127
    /// and has no ELSE; of an IF or a WHILE, each global variable its condition reads (@@ERROR, @@TRANCOUNT, @@ROWCOUNT, @@FETCH_STATUS);
    /// of a CREATE INDEX, UNIQUE, CLUSTERED and WHERE; of a CREATE TABLE, PERSISTED when it holds a persisted computed column; of an
    /// ALTER TABLE, WITH CHECK or WITH NOCHECK, and of one element added, COLUMN with NULL or NOT NULL, AS for a computed column, PERSISTED
    /// and DEFAULT, or a constraint's PRIMARY KEY, UNIQUE, CLUSTERED, FOREIGN KEY, CHECK or DEFAULT, or PERIOD FOR SYSTEM_TIME; of one
    /// element dropped, DROP COLUMN, DROP CONSTRAINT, DROP INDEX or DROP PERIOD; of an ALTER COLUMN, NULL or NOT NULL and COLLATE; of a
    /// constraint's check, CHECK CONSTRAINT or NOCHECK CONSTRAINT, and ALL; of an ALTER TABLE SET, SYSTEM_VERSIONING = ON or OFF, or SET
    /// and another option's name; of an INSERT, its source, SELECT, VALUES or EXECUTE; of a SELECT, INTO; of a MERGE, VALUES when its
    /// source is a list of rows; of an EXECUTE, PROCEDURE and the procedure's one-part name, or DYNAMIC; of a WAITFOR, DELAY, TIME or a
    /// statement.
    /// </summary>
    public SortedArray<string> Words { get; init; } = [];

    public SortedArray<PlanObject> Objects { get; init; } = [];

    public SortedArray<Name> Columns { get; init; } = [];

    public SortedArray<Name> Key { get; init; } = [];

    /// <summary>The statement's target, null where it has none.</summary>
    public PlanObject? Target => Objects.FirstOrDefault(o => o.Role == PlanRole.Target);

    /// <summary>The objects of a role, in name order.</summary>
    public IEnumerable<PlanObject> In(PlanRole role) => Objects.Where(o => o.Role == role);

    public bool Has(string word) => Words.Contains(word);

    public int CompareTo(PlanStatement? other) =>
        other is null ? 1
        : Ordinal.CompareTo(other.Ordinal) is var o and not 0 ? o
        : Part.CompareTo(other.Part) is var p and not 0 ? p
        : Line.CompareTo(other.Line) is var l and not 0 ? l
        : Depth.CompareTo(other.Depth) is var d and not 0 ? d
        : string.CompareOrdinal(Type, other.Type) is var t and not 0 ? t
        : string.CompareOrdinal(Text, other.Text) is var x and not 0 ? x
        : string.CompareOrdinal(string.Join('\n', Words), string.Join('\n', other.Words)) is var w and not 0 ? w
        : SortedArray.Compare(Objects, other.Objects) is var b and not 0 ? b
        : SortedArray.Compare(Columns, other.Columns) is var c and not 0 ? c
        : SortedArray.Compare(Key, other.Key);
}
