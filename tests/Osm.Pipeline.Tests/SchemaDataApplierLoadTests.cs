using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Osm.Pipeline.Orchestration;
using Osm.Pipeline.Sql;
using Xunit;

namespace Osm.Pipeline.Tests;

public sealed class SchemaDataApplierLoadTests
{
    private const long MemoryBudgetBytes = 150L * 1024 * 1024;

    [Fact]
    public async Task ApplyAsync_streams_large_batches_within_memory_budget()
    {
        var fileSystem = new FileSystem();
        var tempRoot = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), $"schema-applier-{Guid.NewGuid():N}");
        fileSystem.Directory.CreateDirectory(tempRoot);

        var scriptPath = CreateLargeScript(fileSystem, tempRoot, batchCount: 25, targetBatchSizeBytes: 8 * 1024 * 1024);

        try
        {
            var connection = new TrackingDbConnection();
            var applier = new SchemaDataApplier(
                (_, _) => new StubDbConnectionFactory(connection),
                fileSystem,
                TimeProvider.System,
                NullLogger<SchemaDataApplier>.Instance);

            var baselineMemory = GC.GetTotalMemory(true);
            connection.InitializeBaseline(baselineMemory);

            var request = new SchemaDataApplyRequest(
                ConnectionString: "Server=(local);Database=Test;Integrated Security=true;",
                ConnectionOptions: SqlConnectionOptions.Default,
                CommandTimeoutSeconds: 120,
                ScriptPaths: ImmutableArray.Create(scriptPath),
                SeedScriptPaths: ImmutableArray<string>.Empty);

            var result = await applier.ApplyAsync(request, CancellationToken.None);

            Assert.True(result.IsSuccess);

            var outcome = result.Value;
            Assert.Equal(25, outcome.ExecutedBatchCount);
            Assert.True(outcome.StreamingEnabled);
            Assert.Equal(connection.ObservedBatchSizes.Max(), outcome.MaxBatchSizeBytes);
            Assert.All(connection.ObservedCommandTimeouts, timeout => Assert.Equal(120, timeout));

            var peakDelta = Math.Max(0, connection.PeakMemoryBytes - baselineMemory);
            Assert.True(
                peakDelta <= MemoryBudgetBytes,
                $"Peak managed memory {peakDelta:n0} bytes exceeded budget {MemoryBudgetBytes:n0} bytes.");
        }
        finally
        {
            try
            {
                fileSystem.Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // Swallow cleanup failures to avoid hiding the underlying test outcome.
            }
        }
    }

    private static string CreateLargeScript(IFileSystem fileSystem, string directory, int batchCount, int targetBatchSizeBytes)
    {
        var path = fileSystem.Path.Combine(directory, "large-schema.sql");
        using var stream = fileSystem.File.Create(path);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var payload = new string('A', 4096);
        var statement = $"SELECT '{payload}' AS Payload;";
        var statementBytes = Encoding.UTF8.GetByteCount(statement + Environment.NewLine);
        var linesPerBatch = Math.Max(1, targetBatchSizeBytes / statementBytes);

        for (var batch = 0; batch < batchCount; batch++)
        {
            for (var line = 0; line < linesPerBatch; line++)
            {
                writer.WriteLine(statement);
            }

            writer.WriteLine("GO");
        }

        writer.Flush();
        return path;
    }

    private sealed class StubDbConnectionFactory : IDbConnectionFactory
    {
        private readonly DbConnection _connection;

        public StubDbConnectionFactory(DbConnection connection)
        {
            _connection = connection;
        }

        public Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_connection);
    }

    private sealed class TrackingDbConnection : DbConnection
    {
        private readonly List<long> _batchSizes = new();
        private readonly List<int> _timeouts = new();

        public IReadOnlyList<long> ObservedBatchSizes => _batchSizes;
        public IReadOnlyList<int> ObservedCommandTimeouts => _timeouts;
        public long PeakMemoryBytes { get; private set; }

        public void InitializeBaseline(long baselineBytes)
        {
            PeakMemoryBytes = baselineBytes;
        }

        internal Task<int> RecordExecutionAsync(string? commandText, int timeout, CancellationToken cancellationToken)
        {
            var batchText = commandText ?? string.Empty;
            _batchSizes.Add(Encoding.UTF8.GetByteCount(batchText));
            _timeouts.Add(timeout);

            var current = GC.GetTotalMemory(true);
            if (current > PeakMemoryBytes)
            {
                PeakMemoryBytes = current;
            }

            return Task.FromResult(0);
        }

        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "Test";
        public override string DataSource => "Test";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName)
            => throw new NotSupportedException();

        public override void Close()
        {
        }

        public override void Open()
        {
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
            => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand()
            => new TrackingDbCommand(this);

        protected override void Dispose(bool disposing)
        {
            // No-op so the captured telemetry remains accessible after disposal.
        }
    }

    private sealed class TrackingDbCommand : DbCommand
    {
        private readonly TrackingDbConnection _connection;
        private readonly TrackingParameterCollection _parameters = new();
        private string _commandText = string.Empty;

        public TrackingDbCommand(TrackingDbConnection connection)
        {
            _connection = connection;
        }

        [AllowNull]
        public override string CommandText
        {
            get => _commandText;
            set => _commandText = value ?? string.Empty;
        }
        public override int CommandTimeout { get; set; } = 30;
        public override CommandType CommandType { get; set; } = CommandType.Text;
        public override bool DesignTimeVisible { get; set; }
            = false;
        public override UpdateRowSource UpdatedRowSource { get; set; }
            = UpdateRowSource.None;

        protected override DbConnection? DbConnection
        {
            get => _connection;
            set { }
        }

        protected override DbParameterCollection DbParameterCollection => _parameters;

        protected override DbTransaction? DbTransaction { get; set; }
            = null;

        public override void Cancel()
        {
        }

        protected override DbParameter CreateDbParameter()
            => new TrackingDbParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            => throw new NotSupportedException();

        public override int ExecuteNonQuery()
            => ExecuteNonQueryAsync(CancellationToken.None).GetAwaiter().GetResult();

        public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await _connection.RecordExecutionAsync(CommandText, CommandTimeout, cancellationToken).ConfigureAwait(false);
        }

        public override object? ExecuteScalar()
            => throw new NotSupportedException();

        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public override void Prepare()
        {
        }
    }

    private sealed class TrackingParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _parameters = new();

        public override int Count => _parameters.Count;
        public override object SyncRoot { get; } = new();
        public override int Add(object value)
        {
            _parameters.Add((DbParameter)value);
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
            => _parameters.Clear();

        public override bool Contains(object value)
            => _parameters.Contains((DbParameter)value);

        public override bool Contains(string value)
            => _parameters.Any(p => p.ParameterName == value);

        public override void CopyTo(Array array, int index)
            => _parameters.ToArray().CopyTo(array, index);

        public override System.Collections.IEnumerator GetEnumerator()
            => _parameters.GetEnumerator();

        public override int IndexOf(object value)
            => _parameters.IndexOf((DbParameter)value);

        public override int IndexOf(string parameterName)
            => _parameters.FindIndex(p => string.Equals(p.ParameterName, parameterName, StringComparison.Ordinal));

        public override void Insert(int index, object value)
            => _parameters.Insert(index, (DbParameter)value);

        public override bool IsFixedSize => false;
        public override bool IsReadOnly => false;
        public override bool IsSynchronized => false;

        public override void Remove(object value)
            => _parameters.Remove((DbParameter)value);

        public override void RemoveAt(int index)
            => _parameters.RemoveAt(index);

        public override void RemoveAt(string parameterName)
        {
            parameterName ??= string.Empty;
            var index = IndexOf(parameterName);
            if (index >= 0)
            {
                _parameters.RemoveAt(index);
            }
        }

        protected override DbParameter GetParameter(int index)
            => _parameters[index];

        protected override DbParameter GetParameter(string parameterName)
        {
            parameterName ??= string.Empty;
            var index = IndexOf(parameterName);
            if (index < 0)
            {
                throw new IndexOutOfRangeException($"Parameter '{parameterName}' was not found.");
            }

            return _parameters[index];
        }

        protected override void SetParameter(int index, DbParameter value)
            => _parameters[index] = value;

        protected override void SetParameter(string parameterName, DbParameter value)
        {
            parameterName ??= string.Empty;
            var index = IndexOf(parameterName);
            if (index >= 0)
            {
                _parameters[index] = value;
            }
            else
            {
                value.ParameterName = parameterName;
                _parameters.Add(value);
            }
        }
    }

    private sealed class TrackingDbParameter : DbParameter
    {
        private string _parameterName = string.Empty;
        private string _sourceColumn = string.Empty;

        public override DbType DbType { get; set; }
            = DbType.String;

        public override ParameterDirection Direction { get; set; }
            = ParameterDirection.Input;

        public override bool IsNullable { get; set; }
            = true;

        [AllowNull]
        public override string ParameterName
        {
            get => _parameterName;
            set => _parameterName = value ?? string.Empty;
        }

        [AllowNull]
        public override string SourceColumn
        {
            get => _sourceColumn;
            set => _sourceColumn = value ?? string.Empty;
        }

        public override DataRowVersion SourceVersion { get; set; }
            = DataRowVersion.Current;

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
}
