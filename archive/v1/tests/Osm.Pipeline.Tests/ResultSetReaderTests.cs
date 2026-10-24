using System;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Osm.Pipeline.SqlExtraction;
using Xunit;

namespace Osm.Pipeline.Tests.SqlExtraction;

public class ResultSetReaderTests
{
    [Fact]
    public async Task ReadAllAsync_ShouldProjectTypedRows()
    {
        var rows = new[]
        {
            new object?[] { 1, "Module", true, Guid.Empty }
        };

        using var reader = new SingleResultSetDataReader(rows, new[] { "Id", "Name", "Flag", "Token" });
        var resultSetReader = ResultSetReader<TestRow>.Create(static row => new TestRow(
            row.GetInt32(0),
            row.GetString(1),
            row.GetBoolean(2),
            row.GetGuidOrNull(3)));

        var result = await resultSetReader.ReadAllAsync(reader, "TestResultSet", CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(1, result[0].Id);
        Assert.Equal("Module", result[0].Name);
        Assert.True(result[0].Flag);
        Assert.Equal(Guid.Empty, result[0].Token);
    }

    [Fact]
    public async Task ReadAllAsync_ShouldReturnNullsForDbNull()
    {
        var rows = new[]
        {
            new object?[] { 2, null, null }
        };

        using var reader = new SingleResultSetDataReader(rows, new[] { "Id", "Name", "Flag" });
        var resultSetReader = ResultSetReader<NullableRow>.Create(static row => new NullableRow(
            row.GetInt32(0),
            row.GetStringOrNull(1),
            row.GetBooleanOrNull(2)));

        var result = await resultSetReader.ReadAllAsync(reader, "TestResultSet", CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(2, result[0].Id);
        Assert.Null(result[0].Name);
        Assert.Null(result[0].Flag);
    }

    [Fact]
    public void GetRequiredString_ShouldThrowInvalidOperationException_WhenColumnIsNull()
    {
        var rows = new[]
        {
            new object?[] { null }
        };

        using var reader = new SingleResultSetDataReader(rows, new[] { "TestColumn" });
        Assert.True(reader.Read());
        var row = new DbRow(reader, "TestResultSet", 0);

        var exception = Assert.Throws<InvalidOperationException>(() => row.GetRequiredString(0, "TestColumn"));

        Assert.Contains("TestColumn", exception.Message);
        Assert.Contains("ordinal 0", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAllAsync_ShouldWrapFactoryExceptions()
    {
        var rows = new[]
        {
            new object?[] { "bad-int" }
        };

        using var reader = new SingleResultSetDataReader(rows, new[] { "AttrId" });
        var resultSetReader = ResultSetReader<int>.Create(static row => row.GetInt32(0));

        var exception = await Assert.ThrowsAsync<MetadataRowMappingException>(() =>
            resultSetReader.ReadAllAsync(reader, "BrokenSet", CancellationToken.None));

        Assert.Equal("BrokenSet", exception.ResultSetName);
        Assert.Equal(0, exception.RowIndex);
        Assert.Null(exception.ColumnName);
        Assert.IsType<FormatException>(exception.InnerException);

        var snapshot = exception.RowSnapshot;
        Assert.NotNull(snapshot);
        Assert.Equal("BrokenSet", snapshot!.ResultSetName);
        Assert.Equal(0, snapshot.RowIndex);
        var column = Assert.Single(snapshot.Columns);
        Assert.Equal("AttrId", column.Name);
        Assert.False(column.IsNull);
        Assert.Equal("bad-int", column.ValuePreview);
    }

    [Fact]
    public async Task ReadAllAsync_ShouldPropagateColumnMetadata()
    {
        var rows = new[]
        {
            new object?[] { "value" }
        };

        using var reader = new SingleResultSetDataReader(rows, new[] { "EntityId" });
        var resultSetReader = ResultSetReader<int>.Create(static _ => throw new ColumnReadException(
            "EntityId",
            0,
            typeof(int),
            typeof(string),
            null,
            null,
            new InvalidOperationException("boom")));

        var exception = await Assert.ThrowsAsync<MetadataRowMappingException>(() =>
            resultSetReader.ReadAllAsync(reader, "ColumnSet", CancellationToken.None));

        Assert.Equal("ColumnSet", exception.ResultSetName);
        Assert.Equal(0, exception.RowIndex);
        Assert.Equal("EntityId", exception.ColumnName);
        Assert.Equal(0, exception.Ordinal);
        Assert.Equal(typeof(int), exception.ExpectedClrType);
        Assert.Equal(typeof(string), exception.ProviderFieldType);
        var columnException = Assert.IsType<ColumnReadException>(exception.InnerException);
        Assert.Equal("ColumnSet", columnException.ResultSetName);
        Assert.Equal(0, columnException.RowIndex);

        var snapshot = exception.RowSnapshot;
        Assert.NotNull(snapshot);
        var highlighted = exception.HighlightedColumn;
        Assert.NotNull(highlighted);
        Assert.Equal("EntityId", highlighted!.Name);
    }

    [Fact]
    public async Task ReadAllAsync_ShouldReportRootCauseMessagesForColumnFailures()
    {
        var rows = new[]
        {
            new object?[] { "value" }
        };

        using var reader = new SingleResultSetDataReader(rows, new[] { "AttributesJson" });
        var resultSetReader = ResultSetReader<int>.Create(static _ => throw new ColumnReadException(
            "AttributesJson",
            0,
            typeof(string),
            typeof(string),
            null,
            null,
            new InvalidOperationException("Column 'AttributesJson' (ordinal 0) contained NULL but a non-null value was required.")));

        var exception = await Assert.ThrowsAsync<MetadataRowMappingException>(() =>
            resultSetReader.ReadAllAsync(reader, "NullableSet", CancellationToken.None));

        Assert.Contains("NullableSet", exception.Message);
        Assert.Contains("AttributesJson", exception.Message);
        Assert.Contains("Root cause", exception.Message);
        Assert.Contains("contained NULL", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Column snapshot preview", exception.Message);
    }

    private sealed record TestRow(int Id, string Name, bool Flag, Guid? Token);

    private sealed record NullableRow(int Id, string? Name, bool? Flag);
}
