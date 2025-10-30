using System;
using System.Linq;
using Microsoft.Data.SqlClient;
using Osm.Domain.ValueObjects;
using Osm.Pipeline.Profiling;
using Xunit;

namespace Osm.Pipeline.Tests;

public sealed class SqlDataProfilerCommandBuilderTests
{
    [Fact]
    public void BuildTableFilterClause_AddsParametersForEachTable()
    {
        using var command = new SqlCommand();
        var tables = new[]
        {
            TableCoordinate.Create("dbo", "OSUSR_A_ENTITY").Value,
            TableCoordinate.Create("sales", "ORDERS").Value
        };

        var clause = SqlDataProfiler.BuildTableFilterClause(command, tables, "s.name", "t.name");

        Assert.Equal(
            "EXISTS (SELECT 1 FROM (VALUES (@schema0, @table0), (@schema1, @table1)) AS targets(SchemaName, TableName) WHERE targets.SchemaName = s.name AND targets.TableName = t.name)",
            clause);
        Assert.Equal(4, command.Parameters.Count);
        Assert.Collection(command.Parameters.Cast<SqlParameter>(),
            parameter =>
            {
                Assert.Equal("@schema0", parameter.ParameterName);
                Assert.Equal("dbo", parameter.Value);
            },
            parameter =>
            {
                Assert.Equal("@table0", parameter.ParameterName);
                Assert.Equal("OSUSR_A_ENTITY", parameter.Value);
            },
            parameter =>
            {
                Assert.Equal("@schema1", parameter.ParameterName);
                Assert.Equal("sales", parameter.Value);
            },
            parameter =>
            {
                Assert.Equal("@table1", parameter.ParameterName);
                Assert.Equal("ORDERS", parameter.Value);
            });
    }

    [Fact]
    public void BuildTableFilterClause_ReturnsFalseClause_WhenTableListEmpty()
    {
        using var command = new SqlCommand();

        var clause = SqlDataProfiler.BuildTableFilterClause(command, Array.Empty<TableCoordinate>(), "s.name", "t.name");

        Assert.Equal("1 = 0", clause);
        Assert.Empty(command.Parameters);
    }
}
