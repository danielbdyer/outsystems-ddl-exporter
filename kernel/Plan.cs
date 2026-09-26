using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace DbChange.Kernel;

/// <summary>
/// A deploy report as DacFx writes one for a plan (DacServices.Script's DeploymentReport): the operations the plan holds, each on one
/// element, and the alerts it raises about the data. A report with no operation is the empty deploy plan: the target matches the package
/// (N18). io/DacFx.Report reads DacFx's XML into it, keying each item as io/Ssdt.ReadModel keys the element.
/// </summary>
public sealed record DeployReport(SortedArray<PlanOperation> Operations, SortedArray<PlanAlert> Alerts)
{
    /// <summary>Whether the plan holds no operation.</summary>
    public bool IsEmpty => Operations.Count == 0;

    /// <summary>The operation whose item cites <paramref name="alert"/> as one of its data issues; null for an alert none cites, and for one with no id (a DataMotion).</summary>
    public PlanOperation? Operation(PlanAlert alert) => alert.Id is { } id ? Operations.FirstOrDefault(o => o.Issues.Contains(id)) : null;
}

/// <summary>One operation of a deploy plan: what DacFx does, to which element, and the ids of the report's data issues the operation raises.</summary>
public sealed record PlanOperation(PlanOperationKind Kind, ElementKey Key, SortedArray<int> Issues) : IComparable<PlanOperation>
{
    /// <summary>By key, then kind, then issues, so an element's operations sit together.</summary>
    public int CompareTo(PlanOperation? other) =>
        other is null ? 1
        : Key.CompareTo(other.Key) is var k and not 0 ? k
        : Kind.CompareTo(other.Kind) is var c and not 0 ? c
        : SortedArray.Compare(Issues, other.Issues);
}

/// <summary>
/// What an operation of a deploy plan does, by the name the deploy report gives it, measured on DacFx 170.5.96: Create, Alter, Drop,
/// TableRebuild and Rename, and the three DacFx adds for the dependents of an object it changes, Refresh, UnbindSchemabinding and
/// RebindSchemabinding. DacFx does not document the names as closed, and its resources name more (AddSystemVersioning, MoveSchema), so any
/// other name is <see cref="Unlisted"/>, DacFx's name kept as written, and no plan is refused for holding one.
/// </summary>
public abstract record PlanOperationKind : IComparable<PlanOperationKind>
{
    private PlanOperationKind(string name) => Name = name;

    /// <summary>DacFx's name for the operation, as the deploy report writes it.</summary>
    public string Name { get; }

    /// <summary>The name as dbchange writes it in a finding's code and in JSON: create, alter, table-rebuild, unbind-schemabinding; unlisted for a name outside the list.</summary>
    public string Word => this is Unlisted ? "unlisted" : Words.Hyphenated(Name);

    /// <summary>Whether DacFx adds the operation for an object that depends on one the plan changes (a refresh, an unbind or a rebind), which is no difference of its own.</summary>
    public bool IsConsequence => this is Refresh or UnbindSchemabinding or RebindSchemabinding;

    /// <summary>The kind the report's name gives, or <see cref="Unlisted"/> for a name outside the list.</summary>
    public static PlanOperationKind Of(string name) => name switch
    {
        "Create" => new Create(),
        "Alter" => new Alter(),
        "Drop" => new Drop(),
        "TableRebuild" => new TableRebuild(),
        "Rename" => new Rename(),
        "Refresh" => new Refresh(),
        "UnbindSchemabinding" => new UnbindSchemabinding(),
        "RebindSchemabinding" => new RebindSchemabinding(),
        _ => new Unlisted(name),
    };

    /// <summary>Ordinally by DacFx's name.</summary>
    public int CompareTo(PlanOperationKind? other) => other is null ? 1 : string.CompareOrdinal(Name, other.Name);

    public sealed override string ToString() => Name;

    public sealed record Create() : PlanOperationKind("Create");

    public sealed record Alter() : PlanOperationKind("Alter");

    public sealed record Drop() : PlanOperationKind("Drop");

    /// <summary>A table dropped and created again with its rows copied, as when a column is inserted between others under IgnoreColumnOrder False.</summary>
    public sealed record TableRebuild() : PlanOperationKind("TableRebuild");

    /// <summary>An object renamed through a refactorlog entry (sp_rename), the new name as the item's name.</summary>
    public sealed record Rename() : PlanOperationKind("Rename");

    /// <summary>A view or a module over a changed object, refreshed (sp_refreshsqlmodule).</summary>
    public sealed record Refresh() : PlanOperationKind("Refresh");

    public sealed record UnbindSchemabinding() : PlanOperationKind("UnbindSchemabinding");

    public sealed record RebindSchemabinding() : PlanOperationKind("RebindSchemabinding");

    /// <summary>An operation whose name the list does not hold, kept as DacFx wrote it.</summary>
    public sealed record Unlisted(string Operation) : PlanOperationKind(Operation);
}

/// <summary>An alert of a deploy report about the data: its kind, the id an operation's item cites it by (a DataIssue has one, a DataMotion none), and DacFx's text, which names types and objects and never a row.</summary>
public sealed record PlanAlert(PlanAlertKind Kind, int? Id, string Text) : IComparable<PlanAlert>
{
    public int CompareTo(PlanAlert? other) =>
        other is null ? 1
        : Kind.CompareTo(other.Kind) is var k and not 0 ? k
        : Nullable.Compare(Id, other.Id) is var i and not 0 ? i
        : string.CompareOrdinal(Text, other.Text);
}

/// <summary>
/// The kind of a deploy report's alert, by the name DacFx gives it (its PlanReportBuilder.HighlightAction enumeration): DataIssue, data a
/// change can lose; DataMotion, rows a table rebuild copies; DropClusteredIndex and CreateClusteredIndex. Any other name is <see cref="Unlisted"/>.
/// </summary>
public abstract record PlanAlertKind : IComparable<PlanAlertKind>
{
    private PlanAlertKind(string name) => Name = name;

    /// <summary>DacFx's name for the alert, as the deploy report writes it.</summary>
    public string Name { get; }

    /// <summary>The name as dbchange writes it in a finding's code: data-issue, data-motion, drop-clustered-index; unlisted for a name outside the list.</summary>
    public string Word => this is Unlisted ? "unlisted" : Words.Hyphenated(Name);

    public static PlanAlertKind Of(string name) => name switch
    {
        "DataIssue" => new DataIssue(),
        "DataMotion" => new DataMotion(),
        "DropClusteredIndex" => new DropClusteredIndex(),
        "CreateClusteredIndex" => new CreateClusteredIndex(),
        _ => new Unlisted(name),
    };

    /// <summary>Ordinally by DacFx's name.</summary>
    public int CompareTo(PlanAlertKind? other) => other is null ? 1 : string.CompareOrdinal(Name, other.Name);

    public sealed override string ToString() => Name;

    public sealed record DataIssue() : PlanAlertKind("DataIssue");

    public sealed record DataMotion() : PlanAlertKind("DataMotion");

    public sealed record DropClusteredIndex() : PlanAlertKind("DropClusteredIndex");

    public sealed record CreateClusteredIndex() : PlanAlertKind("CreateClusteredIndex");

    /// <summary>An alert whose name the list does not hold, kept as DacFx wrote it.</summary>
    public sealed record Unlisted(string Alert) : PlanAlertKind(Alert);
}

/// <summary>
/// Whether a database has drifted from the repository at a ref (§1 fact 4, law 2′), decided from the deploy plan of the ref's package
/// against the target: <see cref="InSync"/> when the plan is empty; else <see cref="Differs"/>, with the plan and the columns that differ
/// under each table the plan alters or rebuilds, which DacFx's report names as the table alone. The cases are closed.
/// </summary>
public abstract record Drift
{
    /// <summary>The property of a computed column that SQL Server keeps as it normalized the text, so a database's differs from its package's where the schemas agree (DF-8).</summary>
    private const string Normalized = "Expression";

    private Drift()
    {
    }

    public T Match<T>(Func<InSync, T> inSync, Func<Differs, T> differs) => this switch
    {
        InSync m => inSync(m),
        Differs d => differs(d),
        _ => throw new UnreachableException(),
    };

    /// <summary>
    /// The drift a plan shows: an empty plan is in sync; else the columns under each table an Alter or a TableRebuild names, from
    /// <see cref="Change.Between"/> the target's elements and the package's under the target's collation, each alteration without its
    /// Expression. A created or dropped table's operation says the whole, so its columns are not listed. A model two of whose elements
    /// share a key is the error model.duplicate-key.
    /// </summary>
    public static Result<Drift> Of(DeployReport plan, SortedArray<Element> target, SortedArray<Element> package, Collation collation)
    {
        if (plan.IsEmpty)
        {
            return new InSync();
        }

        var tables = plan.Operations.Where(o => o.Kind is PlanOperationKind.Alter or PlanOperationKind.TableRebuild && o.Key.Type == "Table").Select(o => o.Key)
            .ToHashSet(ElementKey.Comparer(collation));
        return Change.Between(target, package, [], collation).Map(change => (Drift)new Differs(plan, ColumnsUnder(change, tables)));
    }

    /// <summary>What of a change concerns a column of one of the tables: each such column created, dropped or matched in letter case alone, and each altered, less its Expression.</summary>
    private static Change ColumnsUnder(Change change, IReadOnlySet<ElementKey> tables)
    {
        bool Under(ElementKey key) => key.Type == "Column" && key.Parent is { } table && tables.Contains(table);
        return new Change(
            SortedArray.Of(change.Created.Where(e => Under(e.Key))),
            SortedArray.Of(change.Dropped.Where(e => Under(e.Key))),
            [],
            SortedArray.Of(change.Altered.Where(a => Under(a.Key)).Select(a => a with { Properties = SortedArray.Of(a.Properties.Where(p => p.Name != Normalized)) })
                .Where(a => a.Properties.Count + a.Relationships.Count > 0)),
            SortedArray.Of(change.CaseOnlyRenamed.Where(r => Under(r.After))));
    }

    /// <summary>The deploy plan against the target is empty.</summary>
    public sealed record InSync : Drift;

    /// <summary>The deploy plan holds operations: its deploy report, and the columns that differ under each table it alters or rebuilds, from the target to the package.</summary>
    public sealed record Differs(DeployReport Report, Change Columns) : Drift;
}

/// <summary>
/// The SQL Server platform a model targets, as DacFx names it (Sql160 for SQL Server 2022, SqlAzure for Azure SQL Database): what DacFx
/// compares a package's with its target's before it plans, and what the plan.platform error names. default(Platform) is not a platform.
/// </summary>
public readonly record struct Platform
{
    private readonly string? _name;

    private Platform(string name) => _name = name;

    /// <summary>The platform DacFx names <paramref name="name"/>, or model.platform for a name of other than letters and digits.</summary>
    public static Result<Platform> Of(string? name) => name is { Length: > 0 } && name.All(char.IsAsciiLetterOrDigit) ? new Platform(name)
        : new Error("model.platform", "'" + name + "' is no DacFx platform name, such as Sql160.", "Report the package's source with this error; DacFx names each platform it knows.");

    public override string ToString() => _name ?? throw new InvalidOperationException("default(Platform) is not a platform; make one with Platform.Of.");
}

/// <summary>How dbchange writes one of DacFx's names in a code or in JSON.</summary>
internal static class Words
{
    /// <summary>A name written in capitalised words as lowercase words joined by hyphens: TableRebuild as table-rebuild.</summary>
    public static string Hyphenated(string name)
    {
        var written = new StringBuilder(name.Length + 4);
        foreach (var c in name)
        {
            if (char.IsAsciiLetterUpper(c) && written.Length > 0)
            {
                written.Append('-');
            }

            written.Append(char.ToLowerInvariant(c));
        }

        return written.ToString();
    }
}
