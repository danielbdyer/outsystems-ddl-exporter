using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Osm.Domain.ValueObjects;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;

namespace Osm.Pipeline.Tests.SqlExtraction;

#pragma warning disable CS8765
public class SqlClientAdvancedSqlExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldConcatenateAllJsonChunks()
    {
        var moduleResult = ModuleName.Create("ModuleA");
        moduleResult.IsSuccess.Should().BeTrue();
        var request = new AdvancedSqlRequest(
            ImmutableArray.Create(moduleResult.Value),
            includeSystemModules: false,
            includeInactiveModules: true,
            onlyActiveAttributes: true);

        var connection = new StubConnection(new StubCommand("chunk-1", "chunk-2"));
        var factory = new StubConnectionFactory(connection);
        var scriptProvider = new StubScriptProvider("SELECT 1");
        var executor = new SqlClientAdvancedSqlExecutor(
            factory,
            scriptProvider,
            SqlExecutionOptions.Default,
            NullLogger<SqlClientAdvancedSqlExecutor>.Instance);

        await using var destination = new MemoryStream();

        var result = await executor.ExecuteAsync(request, destination, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(destination.Length);

        destination.Position = 0;
        using var reader = new StreamReader(destination, Encoding.UTF8, leaveOpen: true);
        var payload = await reader.ReadToEndAsync();
        payload.Should().Be("chunk-1chunk-2");
        destination.Position.Should().Be(destination.Length);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnFailureWhenNoRows()
    {
        var request = new AdvancedSqlRequest(ImmutableArray<ModuleName>.Empty, includeSystemModules: false, includeInactiveModules: true, onlyActiveAttributes: true);
        var connection = new StubConnection(new StubCommand());
        var factory = new StubConnectionFactory(connection);
        var scriptProvider = new StubScriptProvider("SELECT 1");
        var executor = new SqlClientAdvancedSqlExecutor(
            factory,
            scriptProvider,
            SqlExecutionOptions.Default,
            NullLogger<SqlClientAdvancedSqlExecutor>.Instance);

        await using var destination = new MemoryStream();

        var result = await executor.ExecuteAsync(request, destination, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Code == "extraction.sql.emptyJson");
        destination.Length.Should().Be(0);
    }

    private sealed class StubScriptProvider : IAdvancedSqlScriptProvider
    {
        private readonly string _script;

        public StubScriptProvider(string script)
        {
            _script = script;
        }

        public string GetScript() => _script;
    }

    private sealed class StubConnectionFactory : IDbConnectionFactory
    {
        private readonly StubConnection _connection;

        public StubConnectionFactory(StubConnection connection)
        {
            _connection = connection;
        }

        public Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            _connection.Open();
            return Task.FromResult<DbConnection>(_connection);
        }
    }

    private sealed class StubConnection : DbConnection
    {
        private readonly StubCommand _command;
        private ConnectionState _state = ConnectionState.Closed;

        public StubConnection(StubCommand command)
        {
            _command = command;
        }

        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => "Stub";

        public override string DataSource => "Stub";

        public override string ServerVersion => "1.0";

        public override ConnectionState State => _state;

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close()
        {
            _state = ConnectionState.Closed;
        }

        public override void Open()
        {
            _state = ConnectionState.Open;
        }

        public override Task OpenAsync(CancellationToken cancellationToken)
        {
            Open();
            return Task.CompletedTask;
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
            => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => _command;
    }

    private sealed class StubCommand : DbCommand
    {
        private readonly IReadOnlyList<string?> _chunks;
        private readonly StubParameterCollection _parameters = new();

        public StubCommand(params string[] chunks)
        {
            _chunks = chunks;
        }

        public override string CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; }
            = 30;

        public override CommandType CommandType { get; set; }
            = CommandType.Text;

        protected override DbConnection? DbConnection { get; set; }
            = null;

        protected override DbParameterCollection DbParameterCollection => _parameters;

        protected override DbTransaction? DbTransaction { get; set; }
            = null;

        public override bool DesignTimeVisible { get; set; }
            = false;

        public override UpdateRowSource UpdatedRowSource { get; set; }
            = UpdateRowSource.None;

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery() => 0;

        public override object? ExecuteScalar() => _chunks.FirstOrDefault();

        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
            => Task.FromResult<object?>(_chunks.FirstOrDefault());

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter() => new StubParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            => new StubDataReader(_chunks);

        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
            => Task.FromResult<DbDataReader>(new StubDataReader(_chunks));
    }

    private sealed class StubParameter : DbParameter
    {
        public override DbType DbType { get; set; }
            = DbType.String;

        public override ParameterDirection Direction { get; set; }
            = ParameterDirection.Input;

        public override bool IsNullable { get; set; }
            = true;

        public override string ParameterName { get; set; }
            = string.Empty;

        public override string SourceColumn { get; set; }
            = string.Empty;

        public override object? Value { get; set; }
            = null;

        public override bool SourceColumnNullMapping { get; set; }
            = false;

        public override int Size { get; set; }
            = 0;

        public override void ResetDbType()
        {
        }
    }

    #pragma warning disable CS8765
    private sealed class StubParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _parameters = new();

        public override int Count => _parameters.Count;

        public override object SyncRoot => _parameters;

        public override int Add([AllowNull] object? value)
        {
            if (value is not DbParameter parameter)
            {
                throw new ArgumentException("Value must be a DbParameter.", nameof(value));
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

        public override void Clear() => _parameters.Clear();

        public override bool Contains([AllowNull] object? value) => _parameters.Contains((DbParameter)value!);

        public override bool Contains(string value)
            => _parameters.Any(p => string.Equals(p.ParameterName, value, StringComparison.OrdinalIgnoreCase));

        public override void CopyTo(Array array, int index)
            => ((ICollection)_parameters).CopyTo(array, index);

        public override IEnumerator GetEnumerator() => _parameters.GetEnumerator();

        protected override DbParameter GetParameter(int index) => _parameters[index];

        protected override DbParameter GetParameter(string parameterName)
            => _parameters.First(p => string.Equals(p.ParameterName, parameterName, StringComparison.OrdinalIgnoreCase));

        public override int IndexOf([AllowNull] object? value) => _parameters.IndexOf((DbParameter)value!);

        public override int IndexOf(string parameterName)
            => _parameters.FindIndex(p => string.Equals(p.ParameterName, parameterName, StringComparison.OrdinalIgnoreCase));

        public override void Insert(int index, [AllowNull] object? value)
            => _parameters.Insert(index, (DbParameter)value!);

        public override void Remove([AllowNull] object? value)
            => _parameters.Remove((DbParameter)value!);

        public override void RemoveAt(int index)
            => _parameters.RemoveAt(index);

        public override void RemoveAt(string parameterName)
        {
            var index = IndexOf(parameterName);
            if (index >= 0)
            {
                _parameters.RemoveAt(index);
            }
        }

        protected override void SetParameter(int index, DbParameter value)
            => _parameters[index] = value;

        protected override void SetParameter(string parameterName, DbParameter value)
        {
            var index = IndexOf(parameterName);
            if (index >= 0)
            {
                _parameters[index] = value;
                return;
            }

            throw new IndexOutOfRangeException($"Parameter '{parameterName}' not found.");
        }
    }
    #pragma warning restore CS8765

    private sealed class StubDataReader : DbDataReader
    {
        private readonly IReadOnlyList<string?> _chunks;
        private int _index = -1;
        private bool _isClosed;

        public StubDataReader(IReadOnlyList<string?> chunks)
        {
            _chunks = chunks;
        }

        public override int FieldCount => 1;

        public override bool HasRows => _chunks.Count > 0;

        public override bool IsClosed => _isClosed;

        public override int RecordsAffected => -1;

        public override int Depth => 0;

        public override object this[int ordinal]
            => GetValue(ordinal);

        public override object this[string name]
            => throw new NotSupportedException();

        public override string GetName(int ordinal) => "JSON";

        public override string GetDataTypeName(int ordinal) => typeof(string).Name;

        public override Type GetFieldType(int ordinal) => typeof(string);

        public override object GetValue(int ordinal)
            => _chunks[_index] ?? string.Empty;

        public override int GetValues(object[] values)
        {
            if (values is null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (values.Length == 0)
            {
                return 0;
            }

            values[0] = GetValue(0);
            return 1;
        }

        public override bool IsDBNull(int ordinal)
            => _chunks[_index] is null;

        public override string GetString(int ordinal)
            => _chunks[_index] ?? string.Empty;

        public override int GetOrdinal(string name) => 0;

        public override bool Read()
        {
            if (_index + 1 >= _chunks.Count)
            {
                return false;
            }

            _index++;
            return true;
        }

        public override Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Read());
        }

        public override bool NextResult() => false;

        public override Task<bool> NextResultAsync(CancellationToken cancellationToken)
            => Task.FromResult(false);

        public override void Close()
        {
            _isClosed = true;
        }

        public override bool GetBoolean(int ordinal) => throw new NotSupportedException();

        public override byte GetByte(int ordinal) => throw new NotSupportedException();

        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
            => throw new NotSupportedException();

        public override char GetChar(int ordinal) => throw new NotSupportedException();

        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
            => throw new NotSupportedException();

        public override Guid GetGuid(int ordinal) => throw new NotSupportedException();

        public override short GetInt16(int ordinal) => throw new NotSupportedException();

        public override int GetInt32(int ordinal) => throw new NotSupportedException();

        public override long GetInt64(int ordinal) => throw new NotSupportedException();

        public override float GetFloat(int ordinal) => throw new NotSupportedException();

        public override double GetDouble(int ordinal) => throw new NotSupportedException();

        public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();

        public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();

        public override IEnumerator GetEnumerator()
            => _chunks.GetEnumerator();

        public override Stream GetStream(int ordinal) => throw new NotSupportedException();

        public override TextReader GetTextReader(int ordinal)
            => new StringReader(_chunks[_index] ?? string.Empty);
    }
}
#pragma warning restore CS8765
