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

namespace Osm.Pipeline.Tests.Profiling;

public sealed class SqlDataProfilerOrchestrationTests
{
    [Fact]
    public async Task CaptureAsync_ComposesMetadataPlansAndQueryResults()
    {
        var model = ModelFixtures.LoadModel("model.micro-unique.json");

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

        var probeStatus = ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UnixEpoch, 100);
        var results = new TableProfilingResults(
            new Dictionary<string, long>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["ID"] = 0,
                ["EMAIL"] = 0
            },
            new Dictionary<string, ProfilingProbeStatus>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["ID"] = probeStatus,
                ["EMAIL"] = probeStatus
            },
            new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["email"] = false
            },
            new Dictionary<string, ProfilingProbeStatus>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["email"] = probeStatus
            },
            new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, ProfilingProbeStatus>(System.StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, ProfilingProbeStatus>(System.StringComparer.OrdinalIgnoreCase));

        var metadataLoader = new StubMetadataLoader(metadata, rowCounts);
        var planBuilder = new StubPlanBuilder(plan);
        var queryExecutor = new StubQueryExecutor(results);
        var profiler = new SqlDataProfiler(new NullConnectionFactory(), model, SqlProfilerOptions.Default, metadataLoader, planBuilder, queryExecutor);

        var snapshot = await profiler.CaptureAsync(CancellationToken.None);

        Assert.True(snapshot.IsSuccess, string.Join(", ", snapshot.Errors.Select(e => e.Code)));
        Assert.Collection(
            snapshot.Value.Columns,
            column =>
            {
                Assert.Equal("dbo", column.Schema.Value);
                Assert.Equal("OSUSR_U_USER", column.Table.Value);
                Assert.Equal("ID", column.Column.Value);
                Assert.Equal(ProfilingProbeOutcome.Succeeded, column.NullCountStatus.Outcome);
            },
            column =>
            {
                Assert.Equal("EMAIL", column.Column.Value);
                Assert.Equal(ProfilingProbeOutcome.Succeeded, column.NullCountStatus.Outcome);
            });

        var unique = Assert.Single(snapshot.Value.UniqueCandidates);
        Assert.Equal(ProfilingProbeOutcome.Succeeded, unique.ProbeStatus.Outcome);

        Assert.Empty(snapshot.Value.CompositeUniqueCandidates);
        Assert.Empty(snapshot.Value.ForeignKeys);
    }

    [Fact]
    public async Task CaptureAsync_ShouldPopulateForeignKeyNoCheckFromResults()
    {
        var model = ModelFixtures.LoadModel("model.micro-fk-protect.json");

        var metadata = new Dictionary<(string Schema, string Table, string Column), ColumnMetadata>(ColumnKeyComparer.Instance)
        {
            [("dbo", "OSUSR_P_PARENT", "ID")] = new ColumnMetadata(false, false, true, null),
            [("dbo", "OSUSR_C_CHILD", "ID")] = new ColumnMetadata(false, false, true, null),
            [("dbo", "OSUSR_C_CHILD", "PARENTID")] = new ColumnMetadata(true, false, false, null)
        };

        var rowCounts = new Dictionary<(string Schema, string Table), long>(TableKeyComparer.Instance)
        {
            [("dbo", "OSUSR_P_PARENT")] = 5,
            [("dbo", "OSUSR_C_CHILD")] = 10
        };

        var foreignKeyKey = ProfilingPlanBuilder.BuildForeignKeyKey("PARENTID", "dbo", "OSUSR_P_PARENT", "ID");
        var plan = new TableProfilingPlan(
            "dbo",
            "OSUSR_C_CHILD",
            10,
            ImmutableArray.Create("ID", "PARENTID"),
            ImmutableArray<UniqueCandidatePlan>.Empty,
            ImmutableArray.Create(new ForeignKeyPlan(foreignKeyKey, "PARENTID", "dbo", "OSUSR_P_PARENT", "ID")));

        var probeStatus = ProfilingProbeStatus.CreateSucceeded(DateTimeOffset.UnixEpoch, 10);
        var results = new TableProfilingResults(
            new Dictionary<string, long>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["ID"] = 0,
                ["PARENTID"] = 0
            },
            new Dictionary<string, ProfilingProbeStatus>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["ID"] = probeStatus,
                ["PARENTID"] = probeStatus
            },
            new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, ProfilingProbeStatus>(System.StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase)
            {
                [foreignKeyKey] = false
            },
            new Dictionary<string, ProfilingProbeStatus>(System.StringComparer.OrdinalIgnoreCase)
            {
                [foreignKeyKey] = probeStatus
            },
            new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase)
            {
                [foreignKeyKey] = true
            },
            new Dictionary<string, ProfilingProbeStatus>(System.StringComparer.OrdinalIgnoreCase)
            {
                [foreignKeyKey] = probeStatus
            });

        var metadataLoader = new StubMetadataLoader(metadata, rowCounts);
        var planBuilder = new StubPlanBuilder(new Dictionary<(string Schema, string Table), TableProfilingPlan>(TableKeyComparer.Instance)
        {
            [("dbo", "OSUSR_C_CHILD")] = plan
        });
        var queryExecutor = new StubQueryExecutor(results);
        var profiler = new SqlDataProfiler(new NullConnectionFactory(), model, SqlProfilerOptions.Default, metadataLoader, planBuilder, queryExecutor);

        var snapshot = await profiler.CaptureAsync(CancellationToken.None);

        Assert.True(snapshot.IsSuccess, string.Join(", ", snapshot.Errors.Select(e => e.Code)));
        var foreignKey = Assert.Single(snapshot.Value.ForeignKeys, fk => fk.Reference.FromTable.Value == "OSUSR_C_CHILD");
        Assert.True(foreignKey.IsNoCheck);
        Assert.False(foreignKey.HasOrphan);
        Assert.Equal(ProfilingProbeOutcome.Succeeded, foreignKey.ProbeStatus.Outcome);
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
        private readonly Dictionary<(string Schema, string Table), TableProfilingPlan> _plans;

        public StubPlanBuilder(TableProfilingPlan plan)
        {
            _plans = new Dictionary<(string Schema, string Table), TableProfilingPlan>(TableKeyComparer.Instance)
            {
                [(plan.Schema, plan.Table)] = plan
            };
        }

        public StubPlanBuilder(Dictionary<(string Schema, string Table), TableProfilingPlan> plans)
        {
            _plans = new Dictionary<(string Schema, string Table), TableProfilingPlan>(plans, TableKeyComparer.Instance);
        }

        public Dictionary<(string Schema, string Table), TableProfilingPlan> BuildPlans(
            IReadOnlyDictionary<(string Schema, string Table, string Column), ColumnMetadata> metadata,
            IReadOnlyDictionary<(string Schema, string Table), long> rowCounts)
        {
            return new Dictionary<(string Schema, string Table), TableProfilingPlan>(_plans, TableKeyComparer.Instance);
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

    private sealed class NullConnectionFactory : IDbConnectionFactory
    {
        public Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<DbConnection>(RecordingDbConnection.WithResultSets());
        }
    }
}
