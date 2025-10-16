using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

namespace Osm.Domain.Profiling;

public sealed class ProfileInsightAnalyzer
{
    public ProfileInsightReport Analyze(ProfileSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (snapshot.Columns.Length == 0
            && snapshot.UniqueCandidates.Length == 0
            && snapshot.CompositeUniqueCandidates.Length == 0
            && snapshot.ForeignKeys.Length == 0)
        {
            return ProfileInsightReport.Empty;
        }

        var builders = new Dictionary<TableKey, TableInsightsBuilder>(TableKeyComparer.Instance);

        foreach (var column in snapshot.Columns)
        {
            var key = TableKey.Create(column.Schema.Value, column.Table.Value);
            builders.TryGetValue(key, out var builder);
            builder ??= new TableInsightsBuilder();
            builder.AddColumn(column);
            builders[key] = builder;
        }

        foreach (var unique in snapshot.UniqueCandidates)
        {
            var key = TableKey.Create(unique.Schema.Value, unique.Table.Value);
            builders.TryGetValue(key, out var builder);
            builder ??= new TableInsightsBuilder();
            builder.AddUniqueCandidate(unique);
            builders[key] = builder;
        }

        foreach (var composite in snapshot.CompositeUniqueCandidates)
        {
            var key = TableKey.Create(composite.Schema.Value, composite.Table.Value);
            builders.TryGetValue(key, out var builder);
            builder ??= new TableInsightsBuilder();
            builder.AddCompositeUniqueCandidate(composite);
            builders[key] = builder;
        }

        foreach (var foreignKey in snapshot.ForeignKeys)
        {
            var key = TableKey.Create(foreignKey.Reference.FromSchema.Value, foreignKey.Reference.FromTable.Value);
            builders.TryGetValue(key, out var builder);
            builder ??= new TableInsightsBuilder();
            builder.AddForeignKey(foreignKey);
            builders[key] = builder;
        }

        var modules = builders
            .Select(pair => pair.Value.Build(pair.Key))
            .Where(module => !module.Insights.IsDefaultOrEmpty && module.Insights.Length > 0)
            .OrderBy(module => module.Schema, StringComparer.Ordinal)
            .ThenBy(module => module.Table, StringComparer.Ordinal)
            .ToImmutableArray();

        return modules.IsDefaultOrEmpty || modules.Length == 0
            ? ProfileInsightReport.Empty
            : new ProfileInsightReport(modules);
    }

    private readonly record struct TableKey(string Schema, string Table)
    {
        public static TableKey Create(string schema, string table)
        {
            if (schema is null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            if (table is null)
            {
                throw new ArgumentNullException(nameof(table));
            }

            return new TableKey(schema, table);
        }
    }

    private sealed class TableKeyComparer : IEqualityComparer<TableKey>
    {
        public static TableKeyComparer Instance { get; } = new();

        public bool Equals(TableKey x, TableKey y)
        {
            return string.Equals(x.Schema, y.Schema, StringComparison.Ordinal)
                && string.Equals(x.Table, y.Table, StringComparison.Ordinal);
        }

        public int GetHashCode(TableKey obj)
        {
            return HashCode.Combine(
                obj.Schema is null ? 0 : StringComparer.Ordinal.GetHashCode(obj.Schema),
                obj.Table is null ? 0 : StringComparer.Ordinal.GetHashCode(obj.Table));
        }
    }

    private sealed class TableInsightsBuilder
    {
        private readonly List<ProfileInsight> _insights = new();
        private long _maxRowCount;
        private int _columnCount;

        public ProfileInsightModule Build(TableKey key)
        {
            var builder = ImmutableArray.CreateBuilder<ProfileInsight>();

            if (_columnCount > 0)
            {
                var message = string.Format(
                    CultureInfo.InvariantCulture,
                    "Profile scanned {0:N0} row(s) across {1} column(s).",
                    _maxRowCount,
                    _columnCount);
                builder.Add(new ProfileInsight(ProfileInsightSeverity.Info, message));
            }
            else if (_insights.Count > 0)
            {
                builder.Add(new ProfileInsight(
                    ProfileInsightSeverity.Info,
                    "Profile captured relationship evidence without column distribution."));
            }

            if (_insights.Count == 0 && builder.Count == 0)
            {
                builder.Add(new ProfileInsight(
                    ProfileInsightSeverity.Info,
                    "No profiling evidence was captured for this table."));
            }

            foreach (var insight in _insights
                .OrderByDescending(insight => insight.Severity)
                .ThenBy(insight => insight.Message, StringComparer.Ordinal))
            {
                builder.Add(insight);
            }

            return new ProfileInsightModule(
                key.Schema,
                key.Table,
                builder.ToImmutable());
        }

        public void AddColumn(ColumnProfile column)
        {
            _columnCount++;
            _maxRowCount = Math.Max(_maxRowCount, column.RowCount);

            if (column.RowCount > 0 && column.NullCount > 0)
            {
                var severity = column.IsNullablePhysical
                    ? ProfileInsightSeverity.Warning
                    : ProfileInsightSeverity.Critical;

                var ratio = (double)column.NullCount / column.RowCount;
                var message = string.Format(
                    CultureInfo.InvariantCulture,
                    "Column {0} contains {1:N0} null(s) out of {2:N0} row(s) ({3:P1}).",
                    column.Column.Value,
                    column.NullCount,
                    column.RowCount,
                    ratio);

                if (!column.IsNullablePhysical)
                {
                    message += " Physical schema reports NOT NULL.";
                }

                AddInsight(severity, message);
            }

            if (column.IsComputed)
            {
                var message = string.Format(
                    CultureInfo.InvariantCulture,
                    "Column {0} is computed; values were derived during profiling.",
                    column.Column.Value);
                AddInsight(ProfileInsightSeverity.Info, message);
            }
        }

        public void AddUniqueCandidate(UniqueCandidateProfile profile)
        {
            if (!profile.HasDuplicate)
            {
                return;
            }

            var message = string.Format(
                CultureInfo.InvariantCulture,
                "Unique candidate on column {0} observed duplicates during profiling.",
                profile.Column.Value);
            AddInsight(ProfileInsightSeverity.Critical, message);
        }

        public void AddCompositeUniqueCandidate(CompositeUniqueCandidateProfile profile)
        {
            if (!profile.HasDuplicate)
            {
                return;
            }

            var columns = string.Join(
                ", ",
                profile.Columns.Select(column => column.Value));

            var message = string.Format(
                CultureInfo.InvariantCulture,
                "Composite unique candidate on columns ({0}) observed duplicates during profiling.",
                columns);
            AddInsight(ProfileInsightSeverity.Critical, message);
        }

        public void AddForeignKey(ForeignKeyReality foreignKey)
        {
            var reference = foreignKey.Reference;
            var fromColumn = reference.FromColumn.Value;
            var target = string.Format(
                CultureInfo.InvariantCulture,
                "{0}.{1}.{2}",
                reference.ToSchema.Value,
                reference.ToTable.Value,
                reference.ToColumn.Value);

            if (foreignKey.HasOrphan)
            {
                var message = string.Format(
                    CultureInfo.InvariantCulture,
                    "Foreign key {0} -> {1} has orphaned rows in the captured sample.",
                    fromColumn,
                    target);
                AddInsight(ProfileInsightSeverity.Critical, message);
            }

            if (!reference.HasDatabaseConstraint)
            {
                var message = string.Format(
                    CultureInfo.InvariantCulture,
                    "Foreign key {0} -> {1} is not constrained in the database; tightening will create it.",
                    fromColumn,
                    target);
                AddInsight(ProfileInsightSeverity.Warning, message);
            }

            if (foreignKey.IsNoCheck)
            {
                var message = string.Format(
                    CultureInfo.InvariantCulture,
                    "Foreign key {0} -> {1} is marked WITH NOCHECK in the source database.",
                    fromColumn,
                    target);
                AddInsight(ProfileInsightSeverity.Warning, message);
            }
        }

        private void AddInsight(ProfileInsightSeverity severity, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            _insights.Add(new ProfileInsight(severity, message));
        }
    }
}
