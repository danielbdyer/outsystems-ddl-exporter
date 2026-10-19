using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable CS8765

namespace Osm.Pipeline.Tests.Profiling;

internal sealed class RecordingDbConnection : DbConnection
{
    private readonly Queue<FakeCommandDefinition> _definitions;
    private ConnectionState _state = ConnectionState.Open;

    private RecordingDbConnection(IEnumerable<FakeCommandDefinition> definitions)
    {
        _definitions = new Queue<FakeCommandDefinition>(definitions);
    }

    public static RecordingDbConnection WithResultSets(params FakeCommandDefinition[] definitions)
    {
        return new RecordingDbConnection(definitions);
    }

    public RecordingDbCommand? LastCommand { get; private set; }

    public override string ConnectionString { get; set; } = string.Empty;

    public override string Database => string.Empty;

    public override string DataSource => string.Empty;

    public override string ServerVersion => "0";

    public override ConnectionState State => _state;

    public override void ChangeDatabase(string databaseName)
    {
        throw new NotSupportedException();
    }

    public override void Close()
    {
        _state = ConnectionState.Closed;
    }

    public override void Open()
    {
        _state = ConnectionState.Open;
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        throw new NotSupportedException();
    }

    protected override DbCommand CreateDbCommand()
    {
        var definition = _definitions.Count > 0 ? _definitions.Dequeue() : FakeCommandDefinition.Empty;
        var command = new RecordingDbCommand(this, definition.Rows);
        LastCommand = command;
        return command;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _state = ConnectionState.Closed;
    }
}

internal sealed class FakeCommandDefinition
{
    public FakeCommandDefinition(IReadOnlyList<object?[]> rows)
    {
        Rows = rows ?? throw new ArgumentNullException(nameof(rows));
    }

    public IReadOnlyList<object?[]> Rows { get; }

    public static FakeCommandDefinition Empty { get; } = new(Array.Empty<object?[]>());
}

internal sealed class RecordingDbCommand : DbCommand
{
    private readonly RecordingDbConnection _connection;
    private readonly IReadOnlyList<object?[]> _rows;
    private readonly RecordingDbParameterCollection _parameters = new();

    public RecordingDbCommand(RecordingDbConnection connection, IReadOnlyList<object?[]> rows)
    {
        _connection = connection;
        _rows = rows;
    }

    public override string CommandText { get; set; } = string.Empty;

    public override int CommandTimeout { get; set; }

    public override CommandType CommandType { get; set; } = CommandType.Text;

    public override bool DesignTimeVisible { get; set; }

    public override UpdateRowSource UpdatedRowSource { get; set; } = UpdateRowSource.Both;

    protected override DbConnection DbConnection
    {
        get => _connection;
        set => throw new NotSupportedException();
    }

    protected override DbParameterCollection DbParameterCollection => _parameters;

    protected override DbTransaction? DbTransaction { get; set; }

    public RecordingDbParameterCollection ParametersCollection => _parameters;

    public override void Cancel()
    {
    }

    public override int ExecuteNonQuery()
    {
        throw new NotSupportedException();
    }

    public override object? ExecuteScalar()
    {
        throw new NotSupportedException();
    }

    public override void Prepare()
    {
    }

    protected override DbParameter CreateDbParameter()
    {
        return new RecordingDbParameter();
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        return new FakeDbDataReader(_rows);
    }

    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
    {
        return Task.FromResult<DbDataReader>(new FakeDbDataReader(_rows));
    }
}

internal sealed class RecordingDbParameterCollection : DbParameterCollection
{
    private readonly List<DbParameter> _parameters = new();

    public IReadOnlyList<DbParameter> Items => _parameters;

    public override int Count => _parameters.Count;

    public override object SyncRoot => ((ICollection)_parameters).SyncRoot ?? this;

    public override int Add(object value)
    {
        if (value is not DbParameter parameter)
        {
            throw new ArgumentException("Only DbParameter instances are supported.", nameof(value));
        }

        _parameters.Add(parameter);
        return _parameters.Count - 1;
    }

    public override void AddRange(Array values)
    {
        foreach (var value in values)
        {
            Add(value!);
        }
    }

    public override void Clear()
    {
        _parameters.Clear();
    }

    public override bool Contains(object value)
    {
        return value is DbParameter parameter && _parameters.Contains(parameter);
    }

    public override bool Contains(string value)
    {
        return _parameters.Exists(parameter => string.Equals(parameter.ParameterName, value, StringComparison.Ordinal));
    }

    public override void CopyTo(Array array, int index)
    {
        ((ICollection)_parameters).CopyTo(array, index);
    }

    public override IEnumerator GetEnumerator()
    {
        return _parameters.GetEnumerator();
    }

    protected override DbParameter GetParameter(int index)
    {
        return _parameters[index];
    }

    protected override DbParameter GetParameter(string parameterName)
    {
        var index = IndexOf(parameterName);
        return _parameters[index];
    }

    public override int IndexOf(object value)
    {
        return value is DbParameter parameter ? _parameters.IndexOf(parameter) : -1;
    }

    public override int IndexOf(string parameterName)
    {
        for (var i = 0; i < _parameters.Count; i++)
        {
            if (string.Equals(_parameters[i].ParameterName, parameterName, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    public override void Insert(int index, object value)
    {
        if (value is not DbParameter parameter)
        {
            throw new ArgumentException("Only DbParameter instances are supported.", nameof(value));
        }

        _parameters.Insert(index, parameter);
    }

    public override void Remove(object value)
    {
        if (value is DbParameter parameter)
        {
            _parameters.Remove(parameter);
        }
    }

    public override void RemoveAt(int index)
    {
        _parameters.RemoveAt(index);
    }

    public override void RemoveAt(string parameterName)
    {
        var index = IndexOf(parameterName);
        if (index >= 0)
        {
            _parameters.RemoveAt(index);
        }
    }

    protected override void SetParameter(int index, DbParameter value)
    {
        _parameters[index] = value;
    }

    protected override void SetParameter(string parameterName, DbParameter value)
    {
        var index = IndexOf(parameterName);
        if (index >= 0)
        {
            _parameters[index] = value;
        }
        else
        {
            Add(value);
        }
    }
}

internal sealed class RecordingDbParameter : DbParameter
{
    public override DbType DbType { get; set; }

    public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;

    public override bool IsNullable { get; set; }

    public override string ParameterName { get; set; } = string.Empty;

    public override string SourceColumn { get; set; } = string.Empty;

    public override object Value { get; set; } = default!;

    public override bool SourceColumnNullMapping { get; set; }

    public override int Size { get; set; }

    public override void ResetDbType()
    {
    }
}

internal sealed class FakeDbDataReader : DbDataReader
{
    private readonly IReadOnlyList<object?[]> _rows;
    private int _position = -1;

    public FakeDbDataReader(IReadOnlyList<object?[]> rows)
    {
        _rows = rows ?? throw new ArgumentNullException(nameof(rows));
    }

    public override int FieldCount => _rows.Count > 0 ? _rows[0].Length : 0;

    public override bool HasRows => _rows.Count > 0;

    public override bool IsClosed => false;

    public override int RecordsAffected => -1;

    public override int Depth => 0;

    public override object this[int ordinal] => GetValue(ordinal);

    public override object this[string name] => throw new NotSupportedException();

    public override bool GetBoolean(int ordinal)
    {
        return (bool)_rows[_position][ordinal]!;
    }

    public override byte GetByte(int ordinal)
    {
        return Convert.ToByte(_rows[_position][ordinal], System.Globalization.CultureInfo.InvariantCulture);
    }

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        if (_rows[_position][ordinal] is not byte[] data)
        {
            throw new InvalidOperationException("Column does not contain binary data.");
        }

        var available = Math.Max(0, data.Length - (int)dataOffset);
        if (buffer is null)
        {
            return available;
        }

        var copyLength = Math.Min(length, available);
        Array.Copy(data, (int)dataOffset, buffer, bufferOffset, copyLength);
        return copyLength;
    }

    public override char GetChar(int ordinal)
    {
        var value = _rows[_position][ordinal];
        if (value is char c)
        {
            return c;
        }

        if (value is string s && s.Length > 0)
        {
            return s[0];
        }

        throw new InvalidOperationException("Column does not contain character data.");
    }

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        string text = _rows[_position][ordinal] switch
        {
            char c => c.ToString(),
            string s => s,
            null => throw new InvalidOperationException("Column does not contain character data."),
            _ => _rows[_position][ordinal]!.ToString() ?? string.Empty
        };

        var available = Math.Max(0, text.Length - (int)dataOffset);
        if (buffer is null)
        {
            return available;
        }

        var copyLength = Math.Min(length, available);
        text.CopyTo((int)dataOffset, buffer, bufferOffset, copyLength);
        return copyLength;
    }

    public override string GetDataTypeName(int ordinal)
    {
        return GetFieldType(ordinal).Name;
    }

    public override DateTime GetDateTime(int ordinal)
    {
        return Convert.ToDateTime(_rows[_position][ordinal], System.Globalization.CultureInfo.InvariantCulture);
    }

    public override decimal GetDecimal(int ordinal)
    {
        return Convert.ToDecimal(_rows[_position][ordinal], System.Globalization.CultureInfo.InvariantCulture);
    }

    public override double GetDouble(int ordinal)
    {
        return Convert.ToDouble(_rows[_position][ordinal], System.Globalization.CultureInfo.InvariantCulture);
    }

    public override int GetInt32(int ordinal)
    {
        return Convert.ToInt32(_rows[_position][ordinal], System.Globalization.CultureInfo.InvariantCulture);
    }

    public override float GetFloat(int ordinal)
    {
        return Convert.ToSingle(_rows[_position][ordinal], System.Globalization.CultureInfo.InvariantCulture);
    }

    public override Guid GetGuid(int ordinal)
    {
        return _rows[_position][ordinal] switch
        {
            Guid guid => guid,
            string text => Guid.Parse(text),
            _ => throw new InvalidOperationException("Column does not contain GUID data."),
        };
    }

    public override short GetInt16(int ordinal)
    {
        return Convert.ToInt16(_rows[_position][ordinal], System.Globalization.CultureInfo.InvariantCulture);
    }

    public override long GetInt64(int ordinal)
    {
        return Convert.ToInt64(_rows[_position][ordinal], System.Globalization.CultureInfo.InvariantCulture);
    }

    public override string GetString(int ordinal)
    {
        return (string)_rows[_position][ordinal]!;
    }

    public override object GetValue(int ordinal)
    {
        return _rows[_position][ordinal] ?? DBNull.Value;
    }

    public override bool IsDBNull(int ordinal)
    {
        return _rows[_position][ordinal] is null;
    }

    public override Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Read());
    }

    public override bool Read()
    {
        if (_position + 1 >= _rows.Count)
        {
            return false;
        }

        _position++;
        return true;
    }

    public override bool NextResult()
    {
        return false;
    }

    public override IEnumerator GetEnumerator()
    {
        return ((IEnumerable)_rows).GetEnumerator();
    }

    public override string GetName(int ordinal)
    {
        throw new NotSupportedException();
    }

    public override int GetOrdinal(string name)
    {
        throw new NotSupportedException();
    }

    public override Type GetFieldType(int ordinal)
    {
        return _rows.Count > 0 && _rows[0][ordinal] is not null
            ? _rows[0][ordinal]!.GetType()
            : typeof(object);
    }

    public override int GetValues(object[] values)
    {
        if (_rows.Count == 0)
        {
            return 0;
        }

        var current = _rows[_position];
        var length = Math.Min(values.Length, current.Length);
        for (var i = 0; i < length; i++)
        {
            values[i] = current[i] ?? DBNull.Value;
        }

        return length;
    }

    public override void Close()
    {
    }
}

#pragma warning restore CS8765
