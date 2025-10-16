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

        using var reader = new SingleResultSetDataReader(rows);
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

        using var reader = new SingleResultSetDataReader(rows);
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

        using var reader = new SingleResultSetDataReader(rows);
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

        using var reader = new SingleResultSetDataReader(rows);
        var resultSetReader = ResultSetReader<int>.Create(static row => row.GetInt32(0));

        var exception = await Assert.ThrowsAsync<MetadataRowMappingException>(() =>
            resultSetReader.ReadAllAsync(reader, "BrokenSet", CancellationToken.None));

        Assert.Equal("BrokenSet", exception.ResultSetName);
        Assert.Equal(0, exception.RowIndex);
        Assert.Null(exception.ColumnName);
        Assert.IsType<FormatException>(exception.InnerException);
    }

    [Fact]
    public async Task ReadAllAsync_ShouldPropagateColumnMetadata()
    {
        var rows = new[]
        {
            new object?[] { "value" }
        };

        using var reader = new SingleResultSetDataReader(rows);
        var resultSetReader = ResultSetReader<int>.Create(static _ => throw new ColumnReadException(
            "TestColumn",
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
        Assert.Equal("TestColumn", exception.ColumnName);
        Assert.Equal(0, exception.Ordinal);
        Assert.Equal(typeof(int), exception.ExpectedClrType);
        Assert.Equal(typeof(string), exception.ProviderFieldType);
        var columnException = Assert.IsType<ColumnReadException>(exception.InnerException);
        Assert.Equal("ColumnSet", columnException.ResultSetName);
        Assert.Equal(0, columnException.RowIndex);
    }

    private sealed record TestRow(int Id, string Name, bool Flag, Guid? Token);

    private sealed record NullableRow(int Id, string? Name, bool? Flag);

    private sealed class SingleResultSetDataReader : DbDataReader
    {
        private readonly object?[][] _rows;
        private int _rowIndex = -1;

        public SingleResultSetDataReader(object?[][] rows) => _rows = rows;

        public override object this[int ordinal] => GetValue(ordinal);

        public override object this[string name] => throw new NotSupportedException();

        public override int Depth => 0;

        public override int FieldCount => _rows.Length == 0 ? 0 : _rows[0].Length;

        public override bool HasRows => _rows.Length > 0;

        public override bool IsClosed => false;

        public override int RecordsAffected => 0;

        public override bool GetBoolean(int ordinal)
            => Convert.ToBoolean(_rows[_rowIndex][ordinal], CultureInfo.InvariantCulture);

        public override int GetInt32(int ordinal)
            => Convert.ToInt32(_rows[_rowIndex][ordinal], CultureInfo.InvariantCulture);

        public override string GetString(int ordinal)
            => (string)_rows[_rowIndex][ordinal]!;

        public override Guid GetGuid(int ordinal)
        {
            var value = _rows[_rowIndex][ordinal];
            return value switch
            {
                null => throw new InvalidCastException("Cannot read null Guid."),
                Guid guid => guid,
                string text => Guid.Parse(text, CultureInfo.InvariantCulture),
                _ => throw new InvalidCastException()
            };
        }

        public override object GetValue(int ordinal)
        {
            var value = _rows[_rowIndex][ordinal];
            return value ?? DBNull.Value;
        }

        public override int GetValues(object[] values)
        {
            if (values is null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (_rowIndex < 0 || _rowIndex >= _rows.Length)
            {
                return 0;
            }

            var source = _rows[_rowIndex];
            var length = Math.Min(values.Length, source.Length);
            for (var i = 0; i < length; i++)
            {
                values[i] = source[i] ?? DBNull.Value;
            }

            return length;
        }

        public override int GetOrdinal(string name)
            => throw new NotSupportedException();

        public override string GetName(int ordinal) => $"Column{ordinal}";

        public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;

        public override Type GetFieldType(int ordinal)
        {
            if (_rows.Length == 0)
            {
                return typeof(object);
            }

            var value = _rows[0][ordinal];
            return value?.GetType() ?? typeof(object);
        }

        public override bool IsDBNull(int ordinal) => _rows[_rowIndex][ordinal] is null;

        public override bool NextResult() => false;

        public override Task<bool> NextResultAsync(CancellationToken cancellationToken)
            => Task.FromResult(false);

        public override bool Read()
        {
            if (_rowIndex + 1 >= _rows.Length)
            {
                return false;
            }

            _rowIndex++;
            return true;
        }

        public override Task<bool> ReadAsync(CancellationToken cancellationToken)
            => Task.FromResult(Read());

        public override IEnumerator GetEnumerator() => _rows.GetEnumerator();

        public override void Close()
        {
        }

        public override DataTable GetSchemaTable() => throw new NotSupportedException();

        public override byte GetByte(int ordinal) => throw new NotSupportedException();

        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
            => throw new NotSupportedException();

        public override char GetChar(int ordinal) => throw new NotSupportedException();

        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
            => throw new NotSupportedException();

        public override short GetInt16(int ordinal) => throw new NotSupportedException();

        public override long GetInt64(int ordinal) => throw new NotSupportedException();

        public override float GetFloat(int ordinal) => throw new NotSupportedException();

        public override double GetDouble(int ordinal) => throw new NotSupportedException();

        public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();

        public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();
    }
}
