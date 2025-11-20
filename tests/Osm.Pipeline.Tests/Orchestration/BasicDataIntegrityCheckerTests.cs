using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Pipeline.Orchestration;
using Xunit;

namespace Osm.Pipeline.Tests.Orchestration;

public sealed class BasicDataIntegrityCheckerTests
{
    private const string SourceConnection = "source";
    private const string TargetConnection = "target";

    [Fact]
    public async Task CheckAsync_WhenCountsMatch_ReturnsPassed()
    {
        var model = BuildModel();
        var overrides = CreateOverrides();
        var executor = new FakeDataIntegrityQueryExecutor();
        executor.SetRowCount(SourceConnection, "dbo", "OSUSR_TEST_CUSTOMER", 5);
        executor.SetRowCount(TargetConnection, "dbo", "Customer_Export", 5);
        executor.SetNullCount(SourceConnection, "dbo", "OSUSR_TEST_CUSTOMER", "NAME", 1);
        executor.SetNullCount(TargetConnection, "dbo", "Customer_Export", "Name", 1);

        var checker = new BasicDataIntegrityChecker(executor);
        var result = await checker.CheckAsync(new BasicIntegrityCheckRequest(
            SourceConnection,
            TargetConnection,
            model,
            overrides,
            CommandTimeoutSeconds: 30)).ConfigureAwait(false);

        Assert.True(result.Passed);
        Assert.Empty(result.Warnings);
        Assert.Equal(1, result.TablesChecked);
        Assert.Equal(1, result.RowCountMatches);
        Assert.Equal(1, result.NullCountMatches);

        Assert.Contains(executor.Requests, request =>
            request.Connection == TargetConnection
            && request.Table == "Customer_Export"
            && request.Column == "Name");
    }

    [Fact]
    public async Task CheckAsync_WhenRowCountDiffers_ReturnsWarning()
    {
        var model = BuildModel();
        var executor = new FakeDataIntegrityQueryExecutor();
        executor.SetRowCount(SourceConnection, "dbo", "OSUSR_TEST_CUSTOMER", 5);
        executor.SetRowCount(TargetConnection, "dbo", "Customer", 3);
        executor.SetNullCount(SourceConnection, "dbo", "OSUSR_TEST_CUSTOMER", "NAME", 0);
        executor.SetNullCount(TargetConnection, "dbo", "Customer", "Name", 0);

        var checker = new BasicDataIntegrityChecker(executor);
        var result = await checker.CheckAsync(new BasicIntegrityCheckRequest(
            SourceConnection,
            TargetConnection,
            model)).ConfigureAwait(false);

        Assert.False(result.Passed);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal("RowCountMismatch", warning.WarningType);
        Assert.Equal(5, warning.ExpectedValue);
        Assert.Equal(3, warning.ActualValue);
    }

    [Fact]
    public async Task CheckAsync_WhenNullCountDiffers_ReturnsWarning()
    {
        var model = BuildModel();
        var executor = new FakeDataIntegrityQueryExecutor();
        executor.SetRowCount(SourceConnection, "dbo", "OSUSR_TEST_CUSTOMER", 2);
        executor.SetRowCount(TargetConnection, "dbo", "Customer", 2);
        executor.SetNullCount(SourceConnection, "dbo", "OSUSR_TEST_CUSTOMER", "NAME", 1);
        executor.SetNullCount(TargetConnection, "dbo", "Customer", "Name", 0);

        var checker = new BasicDataIntegrityChecker(executor);
        var result = await checker.CheckAsync(new BasicIntegrityCheckRequest(
            SourceConnection,
            TargetConnection,
            model)).ConfigureAwait(false);

        Assert.False(result.Passed);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal("NullCountMismatch", warning.WarningType);
        Assert.Equal("NAME", warning.ColumnName);
        Assert.Equal(1, warning.ExpectedValue);
        Assert.Equal(0, warning.ActualValue);
    }

    [Fact]
    public async Task CheckAsync_WhenQueryFails_ReturnsQueryWarning()
    {
        var model = BuildModel();
        var executor = new FakeDataIntegrityQueryExecutor();
        executor.SetFailure(SourceConnection, "dbo", "OSUSR_TEST_CUSTOMER", null, "boom");

        var checker = new BasicDataIntegrityChecker(executor);
        var result = await checker.CheckAsync(new BasicIntegrityCheckRequest(
            SourceConnection,
            TargetConnection,
            model)).ConfigureAwait(false);

        Assert.False(result.Passed);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal("RowCountQueryFailed", warning.WarningType);
        Assert.Contains("boom", warning.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static NamingOverrideOptions CreateOverrides()
    {
        var rule = NamingOverrideRule.Create(schema: null, table: null, module: "Test", logicalName: "Customer", target: "Customer_Export").Value;
        return NamingOverrideOptions.Create(new[] { rule }).Value;
    }

    private static OsmModel BuildModel()
    {
        var idAttribute = AttributeModel.Create(
            AttributeName.Create("ID").Value,
            ColumnName.Create("ID").Value,
            dataType: "INT",
            isMandatory: true,
            isIdentifier: true,
            isAutoNumber: true,
            isActive: true,
            reference: AttributeReference.None).Value;

        var nameAttribute = AttributeModel.Create(
            AttributeName.Create("Name").Value,
            ColumnName.Create("NAME").Value,
            dataType: "NVARCHAR",
            isMandatory: false,
            isIdentifier: false,
            isAutoNumber: false,
            isActive: true,
            reference: AttributeReference.None).Value;

        var entity = EntityModel.Create(
            ModuleName.Create("Test").Value,
            EntityName.Create("Customer").Value,
            TableName.Create("OSUSR_TEST_CUSTOMER").Value,
            SchemaName.Create("dbo").Value,
            catalog: null,
            isStatic: false,
            isExternal: false,
            isActive: true,
            attributes: new[] { idAttribute, nameAttribute },
            indexes: Array.Empty<IndexModel>(),
            relationships: Array.Empty<RelationshipModel>(),
            triggers: Array.Empty<TriggerModel>()).Value;

        var module = ModuleModel.Create(
            ModuleName.Create("Test").Value,
            isSystemModule: false,
            isActive: true,
            entities: new[] { entity }).Value;

        return OsmModel.Create(
            DateTime.UtcNow,
            new[] { module }).Value;
    }

    private sealed record QueryRequest(string Connection, string Schema, string Table, string? Column);

    private sealed class FakeDataIntegrityQueryExecutor : IDataIntegrityQueryExecutor
    {
        private readonly Dictionary<QueryRequest, Result<long>> _responses = new(new QueryRequestComparer());

        public List<QueryRequest> Requests { get; } = new();

        public Task<Result<long>> GetRowCountAsync(
            string connectionString,
            string schema,
            string table,
            int? commandTimeoutSeconds,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Resolve(connectionString, schema, table, null));
        }

        public Task<Result<long>> GetNullCountAsync(
            string connectionString,
            string schema,
            string table,
            string column,
            int? commandTimeoutSeconds,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Resolve(connectionString, schema, table, column));
        }

        public void SetRowCount(string connection, string schema, string table, long value)
        {
            _responses[new QueryRequest(connection, schema, table, null)] = Result<long>.Success(value);
        }

        public void SetNullCount(string connection, string schema, string table, string column, long value)
        {
            _responses[new QueryRequest(connection, schema, table, column)] = Result<long>.Success(value);
        }

        public void SetFailure(string connection, string schema, string table, string? column, string message)
        {
            _responses[new QueryRequest(connection, schema, table, column)] = ValidationError.Create("query.failed", message);
        }

        private Result<long> Resolve(string connection, string schema, string table, string? column)
        {
            var request = new QueryRequest(connection, schema, table, column);
            Requests.Add(request);

            if (_responses.TryGetValue(request, out var result))
            {
                return result;
            }

            return ValidationError.Create("query.missing", $"No response configured for {schema}.{table}.{column ?? "<row>"}");
        }

        private sealed class QueryRequestComparer : IEqualityComparer<QueryRequest>
        {
            public bool Equals(QueryRequest? x, QueryRequest? y)
            {
                return string.Equals(x?.Connection, y?.Connection, StringComparison.Ordinal)
                    && string.Equals(x?.Schema, y?.Schema, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x?.Table, y?.Table, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x?.Column, y?.Column, StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode(QueryRequest obj)
            {
                var hash = new HashCode();
                hash.Add(obj.Connection, StringComparer.Ordinal);
                hash.Add(obj.Schema, StringComparer.OrdinalIgnoreCase);
                hash.Add(obj.Table, StringComparer.OrdinalIgnoreCase);
                hash.Add(obj.Column, StringComparer.OrdinalIgnoreCase);
                return hash.ToHashCode();
            }
        }
    }
}
