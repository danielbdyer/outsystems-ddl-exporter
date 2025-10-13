using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace Osm.Pipeline.RemapUsers;

/// <summary>
/// Holds mutable state shared across remap-users pipeline steps.
/// </summary>
public sealed class RemapUsersState
{
    private readonly List<UserForeignKeyCatalogEntry> _fkCatalog = new();
    private readonly Dictionary<UserForeignKeyCatalogEntry, ColumnRewriteSummary> _rewriteSummaries = new();

    public IReadOnlyList<UserForeignKeyCatalogEntry> ForeignKeyCatalog => new ReadOnlyCollection<UserForeignKeyCatalogEntry>(_fkCatalog);

    public IReadOnlyDictionary<UserForeignKeyCatalogEntry, ColumnRewriteSummary> RewriteSummaries => new ReadOnlyDictionary<UserForeignKeyCatalogEntry, ColumnRewriteSummary>(_rewriteSummaries);

    public IReadOnlyList<SchemaTable> LoadOrder { get; private set; } = Array.Empty<SchemaTable>();

    public UserMapReport? UserMapReport { get; private set; }

    public DryRunSummary? DryRunSummary { get; private set; }

    public PostLoadValidationReport? PostLoadValidation { get; private set; }

    public void ReplaceForeignKeyCatalog(IEnumerable<UserForeignKeyCatalogEntry> entries)
    {
        if (entries is null)
        {
            throw new ArgumentNullException(nameof(entries));
        }

        _fkCatalog.Clear();
        _fkCatalog.AddRange(entries);
    }

    public void RecordRewrite(UserForeignKeyCatalogEntry entry, ColumnRewriteSummary summary)
    {
        if (entry is null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        _rewriteSummaries[entry] = summary ?? throw new ArgumentNullException(nameof(summary));
    }

    public void SetLoadOrder(IReadOnlyList<SchemaTable> loadOrder)
    {
        LoadOrder = loadOrder ?? throw new ArgumentNullException(nameof(loadOrder));
    }

    public void SetUserMapReport(UserMapReport report)
    {
        UserMapReport = report ?? throw new ArgumentNullException(nameof(report));
    }

    public void SetDryRunSummary(DryRunSummary summary)
    {
        DryRunSummary = summary ?? throw new ArgumentNullException(nameof(summary));
    }

    public void SetPostLoadValidation(PostLoadValidationReport report)
    {
        PostLoadValidation = report ?? throw new ArgumentNullException(nameof(report));
    }
}

public sealed record UserForeignKeyCatalogEntry(
    string TableSchema,
    string TableName,
    string ColumnName,
    IReadOnlyList<string> PathSegments)
{
    public string QualifiedTable => $"[{TableSchema}].[{TableName}]";

    public override string ToString()
    {
        var path = PathSegments.Count == 0
            ? "(direct)"
            : string.Join(" -> ", PathSegments);
        return string.Format(CultureInfo.InvariantCulture, "{0}.{1} via {2}", QualifiedTable, ColumnName, path);
    }
}

public sealed record ColumnRewriteSummary(
    long RemappedRowCount,
    long ReassignedRowCount,
    long PrunedRowCount,
    long UnmappedRowCount,
    RemapUsersPolicy Policy);

public sealed record UserMapCoverageRow(
    string MatchReason,
    long MatchedCount);

public sealed record UserMapReport(
    IReadOnlyList<UserMapCoverageRow> Coverage,
    long UnresolvedCount,
    IReadOnlyList<string> SampleUnresolvedIdentifiers);

public sealed record DryRunSummary(
    IReadOnlyList<ColumnDelta> ColumnChanges,
    long TotalRemapped,
    long TotalReassigned,
    long TotalPruned,
    long TotalUnmapped);

public sealed record ColumnDelta(
    string TableSchema,
    string TableName,
    string ColumnName,
    long RemappedRows,
    long ReassignedRows,
    long PrunedRows,
    long UnmappedRows,
    RemapUsersPolicy Policy);

public sealed record PostLoadValidationReport(
    int DisabledForeignKeys,
    int UntrustedForeignKeys,
    bool ReferentialIntegrityVerified,
    IReadOnlyList<string> ValidationErrors);
