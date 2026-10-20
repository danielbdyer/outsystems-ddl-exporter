using System.Globalization;

namespace DbChange.Kernel;

/// <summary>
/// The kinds only a plan's statements show (WP 2.1's second slice, V3_MILESTONES.md §4 row 20), each found by a row of
/// <see cref="Vocabulary"/> on io's statements of DacFx's plan: KeyLength, Rebuild, Computed, Temporal, Scan, Authored and SeedDrift, after
/// the six kinds the property read finds, in the Facsimile's row order (archive/v2/ssdt-agent/facsimile/ARCHITECTURE.md §4). Scan and
/// Rebuild read the rows but no value can make their statement fail, so neither is ever violating; a Computed or an Authored state is
/// declared by a person, never counted (<see cref="PreconditionKind.Declared"/>).
/// </summary>
public abstract partial record PreconditionKind
{
    /// <summary>Whether a person declares the state, which no count gives: Computed and Authored.</summary>
    public bool Declared => this is Computed or Authored;

    /// <summary>Whether a row can make the statement fail: every kind but Scan and Rebuild, which read the rows and fail on none.</summary>
    public bool Breakable => this is not (Scan or Rebuild);

    /// <summary>
    /// No row's key under the index exceeds SQL Server's limit, 900 bytes for a clustered index and 1,700 for any other (Msg 1946 at the
    /// build of the index): the index or the key constraint, none for one DacFx adds unnamed, and whether it is clustered. The columns are
    /// the key's, which DacFx's plan writes as a plain CREATE INDEX or ADD CONSTRAINT, so the width comes from the model.
    /// </summary>
    public sealed record KeyLength(Name? Index, bool Clustered) : PreconditionKind
    {
        /// <summary>The most bytes a key may hold: 900 for a clustered index, 1,700 for another.</summary>
        public int Limit => Clustered ? 900 : 1700;
    }

    /// <summary>
    /// DacFx rebuilds the table: it creates tmp_ms_xx_ beside it, copies every row in with INSERT … SELECT, drops it and renames the copy.
    /// The column is the identity column of the table the change makes, none where it has none, whose gaps the copy keeps
    /// (<see cref="RowClasses.Gapped"/>). The moved columns' types and the rebuilt keys are the property read's Fits and Unique.
    /// </summary>
    public sealed record Rebuild : PreconditionKind;

    /// <summary>A computed column DacFx adds PERSISTED, or indexes while it is not, computes its expression over every existing row: the
    /// expression as the model writes it.</summary>
    public sealed record Computed(string Expression) : PreconditionKind;

    /// <summary>
    /// SQL Server checks each row's period, its end after its start, when DacFx adds PERIOD FOR SYSTEM_TIME over the table's columns, or
    /// the history table's rows when DacFx turns SYSTEM_VERSIONING on over it: the columns are the period's, its start and its end, and
    /// <see cref="Current"/> is the table whose history the rows are, none for a table's own period.
    /// </summary>
    public sealed record Temporal(Name Start, Name End, Name? Current) : PreconditionKind;

    /// <summary>DacFx reads every row and no value can make the statement fail: a non-unique index built, a NOT NULL column with a default
    /// added and filled, a clustered key dropped. Its rows are counted, and it never blocks.</summary>
    public sealed record Scan : PreconditionKind;

    /// <summary>A statement of the change's pre- or post-deploy script that the script before the change does not hold reads the table:
    /// which script, and the statement's first line in it. What it reads is declared by a person who looks.</summary>
    public sealed record Authored(ScriptPart Script, int Line) : PreconditionKind;

    /// <summary>
    /// A MERGE seed of the change's deploy script that the script before it does not hold: the columns are the seed's key, which its ON
    /// matches; <see cref="Code"/> holds the columns of the table's other unique keys that the seed writes, whose values in rows under
    /// another key would make its INSERT a duplicate; and where the seed stands. The rows counted are those whose key or code disagrees
    /// with the seed's own rows.
    /// </summary>
    public sealed record SeedDrift(SortedArray<Name> Code, ScriptPart Script, int Line) : PreconditionKind;

    /// <summary>What a precondition's text adds after its table and columns: the index and limit of a KeyLength, the expression of a
    /// Computed, the current table of a Temporal, the place of an Authored and a SeedDrift; nothing for the other kinds.</summary>
    internal string Detail => Match(() => "", () => "", _ => "", _ => "", _ => "", _ => "",
        k => (k.Index is { } index ? " of " + index : "") + " at most " + k.Limit.ToString(CultureInfo.InvariantCulture) + " bytes",
        () => "", c => " as " + c.Expression,
        t => " from " + t.Start + " to " + t.End + (t.Current is { } current ? ", the history of " + current : ""), () => "",
        a => At(a.Script, a.Line), s => (s.Code.Count == 0 ? "" : " with code (" + string.Join(", ", s.Code) + ")") + At(s.Script, s.Line));

    private static string At(ScriptPart script, int line) => " at line " + line.ToString(CultureInfo.InvariantCulture) + " of the "
        + (script == ScriptPart.PreDeploy ? "pre-deploy" : "post-deploy") + " script";
}
