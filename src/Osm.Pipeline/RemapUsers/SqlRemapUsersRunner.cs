using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.RemapUsers;

public sealed class SqlRemapUsersRunner : ISqlRunner
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SqlRemapUsersRunner(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<int> ExecuteAsync(string commandText, IReadOnlyDictionary<string, object?> parameters, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = CreateCommand(connection, commandText, parameters, timeout);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TResult?> ExecuteScalarAsync<TResult>(string commandText, IReadOnlyDictionary<string, object?> parameters, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = CreateCommand(connection, commandText, parameters, timeout);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is null || result is DBNull)
        {
            return default;
        }

        return (TResult)Convert.ChangeType(result, typeof(TResult), CultureInfo.InvariantCulture);
    }

    public async Task<IReadOnlyList<TResult>> QueryAsync<TResult>(string commandText, IReadOnlyDictionary<string, object?> parameters, Func<IDataRecord, TResult> projector, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (projector is null)
        {
            throw new ArgumentNullException(nameof(projector));
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = CreateCommand(connection, commandText, parameters, timeout);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var results = new List<TResult>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(projector(reader));
        }

        return results;
    }

    public async Task ExecuteInTransactionAsync(string transactionName, TimeSpan timeout, Func<ISqlTransactionalRunner, CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        if (work is null)
        {
            throw new ArgumentNullException(nameof(work));
        }

        await using var connection = (SqlConnection)await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        SqlTransaction? transaction = null;
        try
        {
            try
            {
                transaction = connection.BeginTransaction(IsolationLevel.Snapshot, transactionName);
            }
            catch (Exception)
            {
                transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted, transactionName);
            }

            var runner = new SqlTransactionalRunner(connection, transaction, timeout);
            await work(runner, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }

            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static SqlCommand CreateCommand(DbConnection connection, string commandText, IReadOnlyDictionary<string, object?> parameters, TimeSpan timeout)
    {
        if (connection is null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        if (string.IsNullOrWhiteSpace(commandText))
        {
            throw new ArgumentException("Command text must be provided.", nameof(commandText));
        }

        var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.CommandTimeout = Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds));

        if (parameters is not null)
        {
            foreach (var pair in parameters)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = pair.Key.StartsWith("@", StringComparison.Ordinal) ? pair.Key : "@" + pair.Key;
                parameter.Value = pair.Value ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }
        }

        return (SqlCommand)command;
    }

    private sealed class SqlTransactionalRunner : ISqlTransactionalRunner
    {
        private readonly SqlConnection _connection;
        private readonly SqlTransaction _transaction;
        private readonly TimeSpan _timeout;

        public SqlTransactionalRunner(SqlConnection connection, SqlTransaction transaction, TimeSpan timeout)
        {
            _connection = connection;
            _transaction = transaction;
            _timeout = timeout;
        }

        public async Task<int> ExecuteAsync(string commandText, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
        {
            await using var command = CreateCommand(_connection, commandText, parameters, _timeout);
            command.Transaction = _transaction;
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<TResult?> ExecuteScalarAsync<TResult>(string commandText, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
        {
            await using var command = CreateCommand(_connection, commandText, parameters, _timeout);
            command.Transaction = _transaction;
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (result is null || result is DBNull)
            {
                return default;
            }

            return (TResult)Convert.ChangeType(result, typeof(TResult), CultureInfo.InvariantCulture);
        }

        public async Task<IReadOnlyList<TResult>> QueryAsync<TResult>(string commandText, IReadOnlyDictionary<string, object?> parameters, Func<IDataRecord, TResult> projector, CancellationToken cancellationToken)
        {
            if (projector is null)
            {
                throw new ArgumentNullException(nameof(projector));
            }

            await using var command = CreateCommand(_connection, commandText, parameters, _timeout);
            command.Transaction = _transaction;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var results = new List<TResult>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(projector(reader));
            }

            return results;
        }
    }
}
