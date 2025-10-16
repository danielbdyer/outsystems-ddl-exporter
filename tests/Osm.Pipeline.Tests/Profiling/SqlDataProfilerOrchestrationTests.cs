using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.Profiling;
using Osm.Domain.ValueObjects;
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
        var profiler = new SqlDataProfiler(new NullConnectionFactory(), model, SqlProfilerOptions.Default, metadataLoader, planBuilder, queryExecutor);

        var snapshot = await profiler.CaptureAsync(CancellationToken.None);

        Assert.True(snapshot.IsSuccess, string.Join(", ", snapshot.Errors.Select(e => e.Code)));
        AssertProfileSnapshotEqual(expected, snapshot.Value);
    }

    [Fact]
    public async Task CaptureAsync_ShouldHonorNamingOverridesForForeignKeyTargets()
    {
        var (model, namingOverrides) = CreateDuplicateEntityModel();
        var metadata = new Dictionary<(string Schema, string Table, string Column), ColumnMetadata>(ColumnKeyComparer.Instance)
        {
            [("dbo", "OSUSR_SALES_ORDER", "ID")] = new ColumnMetadata(false, false, true, null),
            [("dbo", "OSUSR_SALES_ORDER", "CUSTOMER_ID")] = new ColumnMetadata(true, false, false, null)
        };
        var rowCounts = new Dictionary<(string Schema, string Table), long>(TableKeyComparer.Instance)
        {
            [("dbo", "OSUSR_SALES_ORDER")] = 50,
            [("dbo", "OSUSR_SALES_CUSTOMER")] = 10,
            [("dbo", "OSUSR_SUPPORT_CUSTOMER")] = 12
        };

        var metadataLoader = new StubMetadataLoader(metadata, rowCounts);
        var foreignKeyKey = string.Join(
            "|",
            new[] { "CUSTOMER_ID", "dbo", "OSUSR_SUPPORT_CUSTOMER", "ID" }.Select(static value => value.ToLowerInvariant()));
        var queryExecutor = new OverrideAwareQueryExecutor(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            [foreignKeyKey] = false
        });

        var options = SqlProfilerOptions.Default with { NamingOverrides = namingOverrides };
        var profiler = new SqlDataProfiler(
            new NullConnectionFactory(),
            model,
            options,
            metadataLoader,
            planBuilder: null,
            queryExecutor);

        var snapshot = await profiler.CaptureAsync(CancellationToken.None);

        Assert.True(snapshot.IsSuccess, string.Join(", ", snapshot.Errors.Select(e => e.Code)));
        var foreignKey = Assert.Single(snapshot.Value.ForeignKeys, fk => fk.Reference.FromTable.Value == "OSUSR_SALES_ORDER");
        Assert.Equal("OSUSR_SUPPORT_CUSTOMER", foreignKey.Reference.ToTable.Value);
        Assert.Equal("ID", foreignKey.Reference.ToColumn.Value);
    }

    private static void AssertProfileSnapshotEqual(ProfileSnapshot expected, ProfileSnapshot actual)
    {
        var expectedJson = JsonSerializer.Serialize(expected, SnapshotSerializerOptions);
        var actualJson = JsonSerializer.Serialize(actual, SnapshotSerializerOptions);
        Assert.Equal(expectedJson, actualJson);
    }

    private static readonly JsonSerializerOptions SnapshotSerializerOptions = new()
    {
        WriteIndented = false
    };

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

    private sealed class OverrideAwareQueryExecutor : IProfilingQueryExecutor
    {
        private readonly IReadOnlyDictionary<string, bool> _foreignKeyResults;

        public OverrideAwareQueryExecutor(IReadOnlyDictionary<string, bool> foreignKeyResults)
        {
            _foreignKeyResults = foreignKeyResults;
        }

        public Task<TableProfilingResults> ExecuteAsync(TableProfilingPlan plan, CancellationToken cancellationToken)
        {
            var foreignKeys = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in plan.ForeignKeys)
            {
                foreignKeys[candidate.Key] = _foreignKeyResults.TryGetValue(candidate.Key, out var value) && value;
            }

            return Task.FromResult(new TableProfilingResults(
                new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
                foreignKeys));
        }
    }

    private static (OsmModel Model, NamingOverrideOptions NamingOverrides) CreateDuplicateEntityModel()
    {
        var salesIdentifier = CreateIdentifier();
        var supportIdentifier = CreateIdentifier();
        var orderIdentifier = CreateIdentifier();

        var salesCustomer = EntityModel.Create(
            new ModuleName("Sales"),
            new EntityName("Customer"),
            new TableName("OSUSR_SALES_CUSTOMER"),
            SchemaName.Create("dbo").Value,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[]
            {
                salesIdentifier,
                CreateNameAttribute()
            },
            allowMissingPrimaryKey: true).Value;

        var supportCustomer = EntityModel.Create(
            new ModuleName("Support"),
            new EntityName("Customer"),
            new TableName("OSUSR_SUPPORT_CUSTOMER"),
            SchemaName.Create("dbo").Value,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[]
            {
                supportIdentifier,
                CreateNameAttribute()
            },
            allowMissingPrimaryKey: true).Value;

        var referenceResult = AttributeReference.Create(
            true,
            targetEntityId: null,
            new EntityName("Customer"),
            new TableName("OSUSR_SALES_CUSTOMER"),
            deleteRuleCode: null,
            hasDatabaseConstraint: false).Value;

        var order = EntityModel.Create(
            new ModuleName("Sales"),
            new EntityName("Order"),
            new TableName("OSUSR_SALES_ORDER"),
            SchemaName.Create("dbo").Value,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            new[]
            {
                orderIdentifier,
                AttributeModel.Create(
                    new AttributeName("CustomerId"),
                    new ColumnName("CUSTOMER_ID"),
                    dataType: "INT",
                    isMandatory: false,
                    isIdentifier: false,
                    isAutoNumber: false,
                    isActive: true,
                    reference: referenceResult).Value
            },
            allowMissingPrimaryKey: true).Value;

        var salesModule = ModuleModel.Create(
            new ModuleName("Sales"),
            isSystemModule: false,
            isActive: true,
            new[] { salesCustomer, order }).Value;

        var supportModule = ModuleModel.Create(
            new ModuleName("Support"),
            isSystemModule: false,
            isActive: true,
            new[] { supportCustomer }).Value;

        var model = OsmModel.Create(DateTime.UtcNow, new[] { salesModule, supportModule }).Value;

        var overrideRule = NamingOverrideRule.Create(null, null, "Support", "Customer", "CUSTOMER_OVERRIDE").Value;
        var namingOverrides = NamingOverrideOptions.Create(new[] { overrideRule }).Value;

        return (model, namingOverrides);
    }

    private static AttributeModel CreateIdentifier()
    {
        return AttributeModel.Create(
            new AttributeName("Id"),
            new ColumnName("ID"),
            dataType: "INT",
            isMandatory: true,
            isIdentifier: true,
            isAutoNumber: false,
            isActive: true).Value;
    }

    private static AttributeModel CreateNameAttribute()
    {
        return AttributeModel.Create(
            new AttributeName("Name"),
            new ColumnName("NAME"),
            dataType: "TEXT",
            isMandatory: false,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true).Value;
    }

    private sealed class NullConnectionFactory : IDbConnectionFactory
    {
        public Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken)
        {
            DbConnection connection = RecordingDbConnection.WithResultSets();
            return Task.FromResult(connection);
        }
    }
}
