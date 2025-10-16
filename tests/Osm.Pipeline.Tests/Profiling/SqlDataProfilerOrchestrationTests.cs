using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Pipeline.Profiling;
using Osm.Pipeline.Sql;
using Tests.Support;
using Xunit;
using Xunit.Sdk;

namespace Osm.Pipeline.Tests.Profiling;

public sealed class SqlDataProfilerOrchestrationTests
{
    [Fact]
    public async Task CaptureAsync_ComposesMetadataPlansAndQueryResults()
    {
        var model = ModelFixtures.LoadModel("model.micro-unique.json");
        var expected = ProfileFixtures.LoadSnapshot("profiling/profile.micro-unique.json");

        var metadata = new Dictionary<(string Schema, string Table, string Column), ColumnMetadata>(ColumnKeyComparer.Instance)
        {
            [("dbo", "OSUSR_U_USER", "ID")] = new ColumnMetadata(false, false, true, null),
            [("dbo", "OSUSR_U_USER", "EMAIL")] = new ColumnMetadata(true, false, false, null)
        };
        var rowCounts = new Dictionary<(string Schema, string Table), long>(TableKeyComparer.Instance)
        {
            [("dbo", "OSUSR_U_USER")] = 100
        };

        var plan = new TableProfilingPlan(
            "dbo",
            "OSUSR_U_USER",
            100,
            ImmutableArray.Create("EMAIL", "ID"),
            ImmutableArray.Create(new UniqueCandidatePlan("email", ImmutableArray.Create("EMAIL"))),
            ImmutableArray<ForeignKeyPlan>.Empty);

        var results = new TableProfilingResults(
            new Dictionary<string, long>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["ID"] = 0,
                ["EMAIL"] = 0
            },
            new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["email"] = false
            },
            new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase));

        var metadataLoader = new StubMetadataLoader(metadata, rowCounts);
        var planBuilder = new StubPlanBuilder(plan);
        var queryExecutor = new StubQueryExecutor(results);
        var profiler = new SqlDataProfiler(new StubConnectionFactory(), model, SqlProfilerOptions.Default, metadataLoader, planBuilder, queryExecutor);

        var snapshot = await profiler.CaptureAsync(CancellationToken.None);

        Assert.True(snapshot.IsSuccess, string.Join(", ", snapshot.Errors.Select(e => e.Code)));

        var actual = snapshot.Value;

        if (expected.Columns.Length != actual.Columns.Length)
        {
            throw new XunitException($"Column count mismatch. Expected {expected.Columns.Length}, actual {actual.Columns.Length}.");
        }
        for (var i = 0; i < expected.Columns.Length; i++)
        {
            if (!Equals(expected.Columns[i], actual.Columns[i]))
            {
                throw new Xunit.Sdk.XunitException($"Column mismatch at index {i}: expected {DescribeColumn(expected.Columns[i])}, actual {DescribeColumn(actual.Columns[i])}");
            }
        }

        if (expected.UniqueCandidates.Length != actual.UniqueCandidates.Length)
        {
            throw new XunitException($"Unique candidate count mismatch. Expected {expected.UniqueCandidates.Length}, actual {actual.UniqueCandidates.Length}.");
        }
        for (var i = 0; i < expected.UniqueCandidates.Length; i++)
        {
            if (!Equals(expected.UniqueCandidates[i], actual.UniqueCandidates[i]))
            {
                throw new Xunit.Sdk.XunitException($"Unique candidate mismatch at index {i}: expected {DescribeUnique(expected.UniqueCandidates[i])}, actual {DescribeUnique(actual.UniqueCandidates[i])}");
            }
        }

        if (expected.CompositeUniqueCandidates.Length != actual.CompositeUniqueCandidates.Length)
        {
            throw new XunitException($"Composite unique candidate count mismatch. Expected {expected.CompositeUniqueCandidates.Length}, actual {actual.CompositeUniqueCandidates.Length}.");
        }
        for (var i = 0; i < expected.CompositeUniqueCandidates.Length; i++)
        {
            if (!Equals(expected.CompositeUniqueCandidates[i], actual.CompositeUniqueCandidates[i]))
            {
                throw new Xunit.Sdk.XunitException($"Composite unique candidate mismatch at index {i}: expected {DescribeComposite(expected.CompositeUniqueCandidates[i])}, actual {DescribeComposite(actual.CompositeUniqueCandidates[i])}");
            }
        }

        if (expected.ForeignKeys.Length != actual.ForeignKeys.Length)
        {
            throw new XunitException($"Foreign key count mismatch. Expected {expected.ForeignKeys.Length}, actual {actual.ForeignKeys.Length}.");
        }
        for (var i = 0; i < expected.ForeignKeys.Length; i++)
        {
            if (!Equals(expected.ForeignKeys[i], actual.ForeignKeys[i]))
            {
                throw new Xunit.Sdk.XunitException($"Foreign key mismatch at index {i}: expected {DescribeForeignKey(expected.ForeignKeys[i])}, actual {DescribeForeignKey(actual.ForeignKeys[i])}");
            }
        }
    }

    private sealed class StubMetadataLoader : ITableMetadataLoader
    {
        private readonly Dictionary<(string Schema, string Table, string Column), ColumnMetadata> _metadata;
        private readonly Dictionary<(string Schema, string Table), long> _rowCounts;

        public StubMetadataLoader(
            Dictionary<(string Schema, string Table, string Column), ColumnMetadata> metadata,
            Dictionary<(string Schema, string Table), long> rowCounts)
        {
            _metadata = metadata;
            _rowCounts = rowCounts;
        }

        public Task<Dictionary<(string Schema, string Table, string Column), ColumnMetadata>> LoadColumnMetadataAsync(DbConnection connection, IReadOnlyCollection<(string Schema, string Table)> tables, CancellationToken cancellationToken)
        {
            return Task.FromResult(new Dictionary<(string Schema, string Table, string Column), ColumnMetadata>(_metadata, ColumnKeyComparer.Instance));
        }

        public Task<Dictionary<(string Schema, string Table), long>> LoadRowCountsAsync(DbConnection connection, IReadOnlyCollection<(string Schema, string Table)> tables, CancellationToken cancellationToken)
        {
            return Task.FromResult(new Dictionary<(string Schema, string Table), long>(_rowCounts, TableKeyComparer.Instance));
        }
    }

    private sealed class StubPlanBuilder : IProfilingPlanBuilder
    {
        private readonly TableProfilingPlan _plan;

        public StubPlanBuilder(TableProfilingPlan plan)
        {
            _plan = plan;
        }

        public Dictionary<(string Schema, string Table), TableProfilingPlan> BuildPlans(
            IReadOnlyDictionary<(string Schema, string Table, string Column), ColumnMetadata> metadata,
            IReadOnlyDictionary<(string Schema, string Table), long> rowCounts)
        {
            return new Dictionary<(string Schema, string Table), TableProfilingPlan>(TableKeyComparer.Instance)
            {
                [("dbo", "OSUSR_U_USER")] = _plan
            };
        }
    }

    private sealed class StubQueryExecutor : IProfilingQueryExecutor
    {
        private readonly TableProfilingResults _results;

        public StubQueryExecutor(TableProfilingResults results)
        {
            _results = results;
        }

        public Task<TableProfilingResults> ExecuteAsync(TableProfilingPlan plan, CancellationToken cancellationToken)
        {
            return Task.FromResult(_results);
        }
    }

    private sealed class StubConnectionFactory : IDbConnectionFactory
    {
        public Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<DbConnection>(RecordingDbConnection.WithResultSets());
        }
    }

    private static string DescribeColumn(ColumnProfile profile)
    {
        return $"{profile.Schema.Value}.{profile.Table.Value}.{profile.Column.Value} (nullable:{profile.IsNullablePhysical}, computed:{profile.IsComputed}, pk:{profile.IsPrimaryKey}, unique:{profile.IsUniqueKey}, default:{profile.DefaultDefinition ?? "<null>"}, rows:{profile.RowCount}, nulls:{profile.NullCount})";
    }

    private static string DescribeUnique(UniqueCandidateProfile profile)
    {
        return $"{profile.Schema.Value}.{profile.Table.Value}.{profile.Column.Value} (hasDuplicate:{profile.HasDuplicate})";
    }

    private static string DescribeComposite(CompositeUniqueCandidateProfile profile)
    {
        var columns = string.Join(",", profile.Columns.Select(column => column.Value));
        return $"{profile.Schema.Value}.{profile.Table.Value}([{columns}]) (hasDuplicate:{profile.HasDuplicate})";
    }

    private static string DescribeForeignKey(ForeignKeyReality reality)
    {
        var reference = reality.Reference;
        return $"{reference.FromSchema.Value}.{reference.FromTable.Value}.{reference.FromColumn.Value}->{reference.ToSchema.Value}.{reference.ToTable.Value}.{reference.ToColumn.Value} (hasOrphan:{reality.HasOrphan}, noCheck:{reality.IsNoCheck}, hasConstraint:{reference.HasDatabaseConstraint})";
    }
}
