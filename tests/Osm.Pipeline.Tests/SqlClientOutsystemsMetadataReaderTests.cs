using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Osm.Domain.ValueObjects;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;
using Xunit;

namespace Osm.Pipeline.Tests.SqlExtraction;

public class SqlClientOutsystemsMetadataReaderTests
{
    [Fact]
    public async Task ReadAsync_ShouldMaterializeSnapshot()
    {
        var resultSets = CreateDefaultResultSets();

        var command = new StubCommand(resultSets);
        var connection = new StubConnection(command);
        var factory = new StubConnectionFactory(connection);
        var scriptProvider = new StubScriptProvider("SELECT 1");
        var reader = new SqlClientOutsystemsMetadataReader(
            factory,
            scriptProvider,
            SqlExecutionOptions.Default,
            NullLogger<SqlClientOutsystemsMetadataReader>.Instance);

        var request = new AdvancedSqlRequest(ImmutableArray.Create(ModuleName.Create("ModuleA").Value), includeSystemModules: false, onlyActiveAttributes: true);

        var result = await reader.ReadAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.ModuleJson);
        Assert.Equal("ModuleA", result.Value.ModuleJson[0].ModuleName);
        Assert.Equal("Fixture", result.Value.DatabaseName);
    }

    [Fact]
    public async Task ReadAsync_ShouldUseSequentialAccessCommandBehavior()
    {
        var resultSets = CreateDefaultResultSets();
        var command = new StubCommand(resultSets);
        var connection = new StubConnection(command);
        var factory = new StubConnectionFactory(connection);
        var scriptProvider = new StubScriptProvider("SELECT 1");
        var executor = new TrackingCommandExecutor(resultSets);
        var reader = new SqlClientOutsystemsMetadataReader(
            factory,
            scriptProvider,
            SqlExecutionOptions.Default,
            NullLogger<SqlClientOutsystemsMetadataReader>.Instance,
            executor);

        var request = new AdvancedSqlRequest(ImmutableArray.Create(ModuleName.Create("ModuleA").Value), includeSystemModules: false, onlyActiveAttributes: true);

        var result = await reader.ReadAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CommandBehavior.SequentialAccess, executor.LastBehavior);
        Assert.NotNull(executor.LastReader);
        Assert.Equal(resultSets.Length - 1, executor.LastReader!.NextResultCalls);
    }

    [Fact]
    public async Task ReadAsync_ShouldReturnFailureWhenResultSetMissing()
    {
        var resultSets = CreateDefaultResultSets();
        var truncated = new[] { resultSets[0] };
        var command = new StubCommand(truncated);
        var connection = new StubConnection(command);
        var factory = new StubConnectionFactory(connection);
        var scriptProvider = new StubScriptProvider("SELECT 1");
        var executor = new TrackingCommandExecutor(truncated);
        var reader = new SqlClientOutsystemsMetadataReader(
            factory,
            scriptProvider,
            SqlExecutionOptions.Default,
            NullLogger<SqlClientOutsystemsMetadataReader>.Instance,
            executor);

        var request = new AdvancedSqlRequest(ImmutableArray.Create(ModuleName.Create("ModuleA").Value), includeSystemModules: false, onlyActiveAttributes: true);

        var result = await reader.ReadAsync(request, CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("extraction.metadata.resultSets.missing", error.Code);
        Assert.NotNull(executor.LastReader);
        Assert.Equal(1, executor.LastReader!.NextResultCalls);
    }

    [Fact]
    public async Task ReadAsync_ShouldLogAndReturnFailure_WhenRequiredColumnIsNull()
    {
        var resultSets = CreateDefaultResultSets();
        var moduleColumns = resultSets[0].Columns.ToArray();
        var moduleRow = resultSets[0].Rows[0].ToArray();
        moduleRow[1] = null; // EspaceName is required.
        resultSets[0] = ResultSet.Create(moduleColumns, new[] { moduleRow });

        var command = new StubCommand(resultSets);
        var connection = new StubConnection(command);
        var factory = new StubConnectionFactory(connection);
        var scriptProvider = new StubScriptProvider("SELECT 1");
        var logger = new ListLogger<SqlClientOutsystemsMetadataReader>();
        var reader = new SqlClientOutsystemsMetadataReader(
            factory,
            scriptProvider,
            SqlExecutionOptions.Default,
            logger);

        var request = new AdvancedSqlRequest(ImmutableArray.Create(ModuleName.Create("ModuleA").Value), includeSystemModules: false, onlyActiveAttributes: true);

        var result = await reader.ReadAsync(request, CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("extraction.metadata.rowMapping", error.Code);

        var logEntry = Assert.Single(logger.Entries.Where(entry => entry.LogLevel == LogLevel.Error));
        Assert.Contains("Modules", logEntry.Message);
        Assert.Contains("Column: EspaceName", logEntry.Message);
        Assert.Contains("ColumnValuePreview: <NULL>", logEntry.Message);
        Assert.Contains("RowSnapshot:", logEntry.Message);
        var exception = Assert.IsType<MetadataRowMappingException>(logEntry.Exception);
        Assert.Equal("Modules", exception.ResultSetName);
        Assert.Equal(0, exception.RowIndex);
        Assert.Equal("EspaceName", exception.ColumnName);
        Assert.NotNull(exception.RowSnapshot);
    }

    [Fact]
    public async Task ReadAsync_ShouldLogAndReturnFailure_WhenColumnValueIsInvalid()
    {
        var resultSets = CreateDefaultResultSets();
        var entityColumns = resultSets[1].Columns.ToArray();
        var entityRow = resultSets[1].Rows[0].ToArray();
        entityRow[0] = "not-an-int"; // EntityId should be an int.
        resultSets[1] = ResultSet.Create(entityColumns, new[] { entityRow });

        var command = new StubCommand(resultSets);
        var connection = new StubConnection(command);
        var factory = new StubConnectionFactory(connection);
        var scriptProvider = new StubScriptProvider("SELECT 1");
        var logger = new ListLogger<SqlClientOutsystemsMetadataReader>();
        var reader = new SqlClientOutsystemsMetadataReader(
            factory,
            scriptProvider,
            SqlExecutionOptions.Default,
            logger);

        var request = new AdvancedSqlRequest(ImmutableArray.Create(ModuleName.Create("ModuleA").Value), includeSystemModules: false, onlyActiveAttributes: true);

        var result = await reader.ReadAsync(request, CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("extraction.metadata.rowMapping", error.Code);

        var logEntry = Assert.Single(logger.Entries.Where(entry => entry.LogLevel == LogLevel.Error));
        Assert.Contains("Entities", logEntry.Message);
        Assert.Contains("Column: EntityId", logEntry.Message);
        Assert.Contains("ColumnValuePreview", logEntry.Message);
        Assert.Contains("RowSnapshot:", logEntry.Message);
        var exception = Assert.IsType<MetadataRowMappingException>(logEntry.Exception);
        Assert.Equal("Entities", exception.ResultSetName);
        Assert.Equal(0, exception.RowIndex);
        Assert.Equal("EntityId", exception.ColumnName);
        var highlighted = exception.HighlightedColumn;
        Assert.NotNull(highlighted);
        Assert.Equal("EntityId", highlighted!.Name);
    }

    [Fact]
    public async Task ReadAsync_ShouldLogAndReturnFailure_WhenAttributeJsonNullWithoutOverride()
    {
        var resultSets = CreateDefaultResultSets();
        var attributeIndex = Array.FindIndex(resultSets, set => set.Columns.SequenceEqual(new[] { "EntityId", "AttributesJson" }));
        Assert.NotEqual(-1, attributeIndex);

        var attributeRow = resultSets[attributeIndex].Rows[0].ToArray();
        attributeRow[1] = null;
        resultSets[attributeIndex] = ResultSet.Create(resultSets[attributeIndex].Columns, new[] { attributeRow });

        var command = new StubCommand(resultSets);
        var connection = new StubConnection(command);
        var factory = new StubConnectionFactory(connection);
        var scriptProvider = new StubScriptProvider("SELECT 1");
        var logger = new ListLogger<SqlClientOutsystemsMetadataReader>();
        var reader = new SqlClientOutsystemsMetadataReader(
            factory,
            scriptProvider,
            SqlExecutionOptions.Default,
            logger);

        var request = new AdvancedSqlRequest(ImmutableArray.Create(ModuleName.Create("ModuleA").Value), includeSystemModules: false, onlyActiveAttributes: true);

        var result = await reader.ReadAsync(request, CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("extraction.metadata.rowMapping", error.Code);
        var logEntry = Assert.Single(logger.Entries.Where(entry => entry.LogLevel == LogLevel.Error));
        Assert.Contains("AttributeJson", logEntry.Message);
        Assert.Contains("AttributesJson", logEntry.Message);
        Assert.Contains("ColumnValuePreview: <NULL>", logEntry.Message);
        Assert.Contains("RowSnapshot:", logEntry.Message);
        var exception = Assert.IsType<MetadataRowMappingException>(logEntry.Exception);
        Assert.NotNull(exception.RowSnapshot);
    }

    [Fact]
    public async Task ReadAsync_ShouldAllowNullableAttributeJson_WhenOverrideConfigured()
    {
        var resultSets = CreateDefaultResultSets();
        var attributeIndex = Array.FindIndex(resultSets, set => set.Columns.SequenceEqual(new[] { "EntityId", "AttributesJson" }));
        Assert.NotEqual(-1, attributeIndex);

        var attributeRow = resultSets[attributeIndex].Rows[0].ToArray();
        attributeRow[1] = null;
        resultSets[attributeIndex] = ResultSet.Create(resultSets[attributeIndex].Columns, new[] { attributeRow });

        var command = new StubCommand(resultSets);
        var connection = new StubConnection(command);
        var factory = new StubConnectionFactory(connection);
        var scriptProvider = new StubScriptProvider("SELECT 1");
        var overrides = MetadataContractOverrides.Strict.WithOptionalColumn("AttributeJson", "AttributesJson");
        var reader = new SqlClientOutsystemsMetadataReader(
            factory,
            scriptProvider,
            SqlExecutionOptions.Default,
            NullLogger<SqlClientOutsystemsMetadataReader>.Instance,
            commandExecutor: null,
            contractOverrides: overrides);

        var request = new AdvancedSqlRequest(ImmutableArray.Create(ModuleName.Create("ModuleA").Value), includeSystemModules: false, onlyActiveAttributes: true);

        var result = await reader.ReadAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var attributeRowResult = Assert.Single(result.Value.AttributeJson);
        Assert.Null(attributeRowResult.AttributesJson);
    }

    private sealed class TrackingCommandExecutor : IDbCommandExecutor
    {
        private readonly ResultSet[] _resultSets;

        public TrackingCommandExecutor(ResultSet[] resultSets)
        {
            _resultSets = resultSets ?? throw new ArgumentNullException(nameof(resultSets));
        }

        public CommandBehavior LastBehavior { get; private set; }

        public StubDataReader? LastReader { get; private set; }

        public Task<DbDataReader> ExecuteReaderAsync(DbCommand command, CommandBehavior behavior, CancellationToken cancellationToken)
        {
            if (command is null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            LastBehavior = behavior;
            var reader = new StubDataReader(_resultSets);
            LastReader = reader;
            return Task.FromResult<DbDataReader>(reader);
        }
    }

    private static ResultSet[] CreateDefaultResultSets()
        => new[]
        {
            ResultSet.Create(
                new[] { "EspaceId", "EspaceName", "IsSystemModule", "ModuleIsActive", "EspaceKind", "EspaceSSKey" },
                new object?[][]
                {
                    new object?[] { 1, "ModuleA", false, true, null, null }
                }),
            ResultSet.Create(
                new[] { "EntityId", "EntityName", "PhysicalTableName", "EspaceId", "EntityIsActive", "IsSystemEntity", "IsExternalEntity", "DataKind", "PrimaryKeySSKey", "EntitySSKey", "EntityDescription" },
                new object?[][]
                {
                    new object?[] { 10, "EntityA", "OSUSR_MOD_ENTITYA", 1, true, false, false, "entity", Guid.Empty, Guid.Empty, "Entity description" }
                }),
            ResultSet.Create(
                new[]
                {
                    "AttrId", "EntityId", "AttrName", "AttrSSKey", "DataType", "Length", "Precision", "Scale", "DefaultValue",
                    "IsMandatory", "AttrIsActive", "IsAutoNumber", "IsIdentifier", "RefEntityId", "OriginalName", "ExternalColumnType",
                    "DeleteRule", "PhysicalColumnName", "DatabaseColumnName", "LegacyType", "Decimals", "OriginalType", "AttrDescription"
                },
                new object?[][]
                {
                    new object?[]
                    {
                        100,
                        10,
                        "Id",
                        Guid.Empty,
                        "Identifier",
                        0,
                        null,
                        null,
                        null,
                        true,
                        true,
                        true,
                        true,
                        10,
                        "Id",
                        null,
                        "Ignore",
                        "ID",
                        "ID",
                        "Legacy",
                        null,
                        "Type",
                        "Primary key",
                    }
                }),
            ResultSet.Create(
                new[] { "AttrId", "RefEntityId", "RefEntityName", "RefPhysicalName" },
                new object?[][]
                {
                    new object?[] { 100, 10, "EntityA", "OSUSR_MOD_ENTITYA" }
                }),
            ResultSet.Create(
                new[] { "EntityId", "SchemaName", "TableName", "object_id" },
                new object?[][]
                {
                    new object?[] { 10, "dbo", "OSUSR_MOD_ENTITYA", 500 }
                }),
            ResultSet.Create(
                new[] { "AttrId", "IsNullable", "SqlType", "MaxLength", "Precision", "Scale", "CollationName", "IsIdentity", "IsComputed", "ComputedDefinition", "DefaultConstraintName", "DefaultDefinition", "PhysicalColumn" },
                new object?[][]
                {
                    new object?[] { 100, false, "int", 4, 10, 0, null, true, false, null, null, null, "ID" }
                }),
            ResultSet.Empty,
            ResultSet.Empty,
            ResultSet.Create(new[] { "AttrId" }, new object?[][] { new object?[] { 100 } }),
            ResultSet.Empty,
            ResultSet.Empty,
            ResultSet.Empty,
            ResultSet.Empty,
            ResultSet.Empty,
            ResultSet.Empty,
            ResultSet.Empty,
            ResultSet.Empty,
            ResultSet.Empty,
            ResultSet.Create(new[] { "EntityId", "AttributesJson" }, new object?[][] { new object?[] { 10, "[{\"name\":\"Id\"}]" } }),
            ResultSet.Create(new[] { "EntityId", "RelationshipsJson" }, new object?[][] { new object?[] { 10, "[]" } }),
            ResultSet.Create(new[] { "EntityId", "IndexesJson" }, new object?[][] { new object?[] { 10, "[]" } }),
            ResultSet.Create(new[] { "EntityId", "TriggersJson" }, new object?[][] { new object?[] { 10, "[]" } }),
            ResultSet.Create(new[] { "module.name", "module.isSystem", "module.isActive", "module.entities" }, new object?[][] { new object?[] { "ModuleA", false, true, "[{\"name\":\"EntityA\"}]" } })
        };

    private sealed class StubScriptProvider : IAdvancedSqlScriptProvider
    {
        private readonly string _script;

        public StubScriptProvider(string script) => _script = script;

        public string GetScript() => _script;
    }

    private sealed class StubConnectionFactory : IDbConnectionFactory
    {
        private readonly StubConnection _connection;

        public StubConnectionFactory(StubConnection connection) => _connection = connection;

        public Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            _connection.Open();
            return Task.FromResult<DbConnection>(_connection);
        }
    }

    #pragma warning disable CS8765
    private sealed class StubConnection : DbConnection
    {
        private readonly StubCommand _command;
        private ConnectionState _state = ConnectionState.Closed;

        public StubConnection(StubCommand command) => _command = command;

        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => "Fixture";

        public override string DataSource => "Stub";

        public override string ServerVersion => "1.0";

        public override ConnectionState State => _state;

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close() => _state = ConnectionState.Closed;

        public override void Open() => _state = ConnectionState.Open;

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
        private readonly ResultSet[] _resultSets;
        private readonly StubParameterCollection _parameters = new();

        public StubCommand(ResultSet[] resultSets) => _resultSets = resultSets;

        public override string CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; } = 30;

        public override CommandType CommandType { get; set; } = CommandType.Text;

        protected override DbConnection? DbConnection { get; set; }
            = null;

        protected override DbParameterCollection DbParameterCollection => _parameters;

        protected override DbTransaction? DbTransaction { get; set; }
            = null;

        public override bool DesignTimeVisible { get; set; } = false;

        public override UpdateRowSource UpdatedRowSource { get; set; } = UpdateRowSource.None;

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery() => 0;

        public override object? ExecuteScalar() => null;

        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
            => Task.FromResult<object?>(null);

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter() => new StubParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            => new StubDataReader(_resultSets);

        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
            => Task.FromResult<DbDataReader>(new StubDataReader(_resultSets));
    }

    private sealed class StubParameter : DbParameter
    {
        public override DbType DbType { get; set; } = DbType.String;

        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;

        public override bool IsNullable { get; set; }
            = true;

        public override string ParameterName { get; set; } = string.Empty;

        public override string SourceColumn { get; set; } = string.Empty;

        public override object? Value { get; set; }
            = null;

        public override void ResetDbType()
        {
        }

        public override int Size { get; set; }
            = 0;

        public override bool SourceColumnNullMapping { get; set; }
            = false;
    }

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
            => _parameters.Exists(parameter => string.Equals(parameter.ParameterName, value, StringComparison.OrdinalIgnoreCase));

        public override void CopyTo(Array array, int index) => ((ICollection)_parameters).CopyTo(array, index);

        public override System.Collections.IEnumerator GetEnumerator() => _parameters.GetEnumerator();

        protected override DbParameter GetParameter(int index) => _parameters[index];

        protected override DbParameter GetParameter(string parameterName)
            => _parameters.Find(parameter => string.Equals(parameter.ParameterName, parameterName, StringComparison.OrdinalIgnoreCase))!;

        public override int IndexOf([AllowNull] object? value) => _parameters.IndexOf((DbParameter)value!);

        public override int IndexOf(string parameterName)
            => _parameters.FindIndex(parameter => string.Equals(parameter.ParameterName, parameterName, StringComparison.OrdinalIgnoreCase));

        public override void Insert(int index, [AllowNull] object? value) => _parameters.Insert(index, (DbParameter)value!);

        public override bool IsFixedSize => false;

        public override bool IsReadOnly => false;

        public override bool IsSynchronized => false;

        public override void Remove([AllowNull] object? value) => _parameters.Remove((DbParameter)value!);

        public override void RemoveAt(int index) => _parameters.RemoveAt(index);

        public override void RemoveAt(string parameterName)
        {
            var index = IndexOf(parameterName);
            if (index >= 0)
            {
                _parameters.RemoveAt(index);
            }
        }

        protected override void SetParameter(int index, DbParameter value) => _parameters[index] = value;

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

    private sealed class StubDataReader : DbDataReader
    {
        private readonly ResultSet[] _resultSets;
        private int _resultIndex;
        private int _rowIndex = -1;
        private int _nextResultCalls;

        public StubDataReader(ResultSet[] resultSets) => _resultSets = resultSets;

        private ResultSet CurrentSet => _resultSets[_resultIndex];

        public override object this[int ordinal] => GetValue(ordinal);

        public override object this[string name] => GetValue(GetOrdinal(name));

        public override int FieldCount => CurrentSet.Columns.Length;

        public override bool HasRows => CurrentSet.Rows.Length > 0;

        public override bool IsClosed => false;

        public override int RecordsAffected => -1;

        public override int Depth => 0;

        public int NextResultCalls => _nextResultCalls;

        public override string GetName(int ordinal) => FieldCount > ordinal ? CurrentSet.Columns[ordinal] : string.Empty;

        public override string GetDataTypeName(int ordinal)
            => GetFieldType(ordinal).Name;

        public override Type GetFieldType(int ordinal)
        {
            if (FieldCount == 0)
            {
                return typeof(object);
            }

            if (CurrentSet.Rows.Length == 0)
            {
                return typeof(object);
            }

            return CurrentSet.Rows[0][ordinal]?.GetType() ?? typeof(object);
        }

        public override object GetValue(int ordinal)
        {
            if (FieldCount == 0 || CurrentSet.Rows.Length == 0)
            {
                return DBNull.Value;
            }

            var value = CurrentRow()[ordinal];
            return value ?? DBNull.Value;
        }

        public override int GetValues(object[] values)
        {
            if (values is null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (FieldCount == 0 || CurrentSet.Rows.Length == 0)
            {
                return 0;
            }

            var row = CurrentRow();
            var count = Math.Min(values.Length, row.Length);
            for (var i = 0; i < count; i++)
            {
                values[i] = row[i] ?? DBNull.Value;
            }

            return count;
        }

        public override int GetOrdinal(string name)
        {
            for (var i = 0; i < CurrentSet.Columns.Length; i++)
            {
                if (string.Equals(CurrentSet.Columns[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        public override bool GetBoolean(int ordinal) => Convert.ToBoolean(CurrentRow()[ordinal]!, CultureInfo.InvariantCulture);

        public override byte GetByte(int ordinal) => Convert.ToByte(CurrentRow()[ordinal]!, CultureInfo.InvariantCulture);

        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
            => throw new NotSupportedException();

        public override char GetChar(int ordinal) => Convert.ToChar(CurrentRow()[ordinal]!, CultureInfo.InvariantCulture);

        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
            => throw new NotSupportedException();

        public override Guid GetGuid(int ordinal)
        {
            var value = CurrentRow()[ordinal];
            return value switch
            {
                null => Guid.Empty,
                Guid guid => guid,
                string s when Guid.TryParse(s, out var parsed) => parsed,
                _ => throw new InvalidCastException()
            };
        }

        public override short GetInt16(int ordinal) => Convert.ToInt16(CurrentRow()[ordinal]!, CultureInfo.InvariantCulture);

        public override int GetInt32(int ordinal) => Convert.ToInt32(CurrentRow()[ordinal]!, CultureInfo.InvariantCulture);

        public override long GetInt64(int ordinal) => Convert.ToInt64(CurrentRow()[ordinal]!, CultureInfo.InvariantCulture);

        public override float GetFloat(int ordinal) => Convert.ToSingle(CurrentRow()[ordinal]!, CultureInfo.InvariantCulture);

        public override double GetDouble(int ordinal) => Convert.ToDouble(CurrentRow()[ordinal]!, CultureInfo.InvariantCulture);

        public override string GetString(int ordinal) => (string)CurrentRow()[ordinal]!;

        public override decimal GetDecimal(int ordinal) => Convert.ToDecimal(CurrentRow()[ordinal]!, CultureInfo.InvariantCulture);

        public override DateTime GetDateTime(int ordinal) => Convert.ToDateTime(CurrentRow()[ordinal]!, CultureInfo.InvariantCulture);

        public override IEnumerator GetEnumerator() => CurrentSet.Rows.GetEnumerator();

        public override bool IsDBNull(int ordinal) => CurrentRow()[ordinal] is null;

        public override bool NextResult()
        {
            _nextResultCalls++;
            if (_resultIndex + 1 >= _resultSets.Length)
            {
                return false;
            }

            _resultIndex++;
            _rowIndex = -1;
            return true;
        }

        public override Task<bool> NextResultAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(NextResult());
        }

        public override bool Read()
        {
            if (_rowIndex + 1 >= CurrentSet.Rows.Length)
            {
                return false;
            }

            _rowIndex++;
            return true;
        }

        public override Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Read());
        }

        public override void Close()
        {
        }

        public override DataTable GetSchemaTable() => throw new NotSupportedException();

        public override Stream GetStream(int ordinal) => throw new NotSupportedException();

        public override TextReader GetTextReader(int ordinal) => throw new NotSupportedException();

        private object?[] CurrentRow()
        {
            if (CurrentSet.Rows.Length == 0)
            {
                throw new InvalidOperationException("No rows available.");
            }

            if (_rowIndex < 0 || _rowIndex >= CurrentSet.Rows.Length)
            {
                throw new InvalidOperationException("No current row available.");
            }

            return CurrentSet.Rows[_rowIndex];
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }

        private readonly List<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> Entries => _entries;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) where TState : notnull
        {
            if (formatter is null)
            {
                throw new ArgumentNullException(nameof(formatter));
            }

            var message = formatter(state, exception);
            _entries.Add(new LogEntry(logLevel, message, exception, eventId));
        }

        public sealed record LogEntry(LogLevel LogLevel, string Message, Exception? Exception, EventId EventId);
    }

    private sealed record ResultSet(string[] Columns, object?[][] Rows)
    {
        public static ResultSet Create(string[] columns, object?[][] rows) => new(columns, rows);

        public static ResultSet Empty { get; } = new(Array.Empty<string>(), Array.Empty<object?[]>());
    }
    #pragma warning restore CS8765
}
