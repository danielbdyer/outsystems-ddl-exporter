using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.UatUsers;

public interface IUserForeignKeyValueProvider
{
    Task<IReadOnlyDictionary<UserFkColumn, IReadOnlyDictionary<UserIdentifier, long>>> CollectAsync(
        IReadOnlyList<UserFkColumn> catalog,
        IDbConnectionFactory connectionFactory,
        int maxConcurrency,
        IProgress<int>? progress,
        CancellationToken cancellationToken);
}

public sealed class SqlUserForeignKeyValueProvider : IUserForeignKeyValueProvider
{
    private readonly ILogger<SqlUserForeignKeyValueProvider> _logger;

    public SqlUserForeignKeyValueProvider(ILogger<SqlUserForeignKeyValueProvider>? logger = null)
    {
        _logger = logger ?? NullLogger<SqlUserForeignKeyValueProvider>.Instance;
    }

    public async Task<IReadOnlyDictionary<UserFkColumn, IReadOnlyDictionary<UserIdentifier, long>>> CollectAsync(
        IReadOnlyList<UserFkColumn> catalog,
        IDbConnectionFactory connectionFactory,
        int maxConcurrency,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        if (connectionFactory is null)
        {
            throw new ArgumentNullException(nameof(connectionFactory));
        }

        if (catalog.Count == 0)
        {
            _logger.LogInformation("No catalog entries provided for foreign key analysis.");
            return ImmutableDictionary<UserFkColumn, IReadOnlyDictionary<UserIdentifier, long>>.Empty;
        }

        if (maxConcurrency <= 0)
        {
            maxConcurrency = 1;
        }

        _logger.LogInformation(
            "Collecting foreign key statistics for {ColumnCount} columns using concurrency limit {Concurrency}.",
            catalog.Count,
            maxConcurrency);

        var results = new ConcurrentDictionary<UserFkColumn, IReadOnlyDictionary<UserIdentifier, long>>();
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxConcurrency,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(catalog, parallelOptions, async (column, token) =>
        {
            _logger.LogDebug(
                "Scanning column {Schema}.{Table}.{Column} for user references.",
                column.SchemaName,
                column.TableName,
                column.ColumnName);

            try
            {
                await using var connection = await connectionFactory.CreateOpenConnectionAsync(token).ConfigureAwait(false);
                using var command = connection.CreateCommand();
                command.CommandText = BuildCommandText(column);
                command.CommandType = CommandType.Text;

                _logger.LogDebug("Executing SQL for {Schema}.{Table}.{Column}: {Sql}", column.SchemaName, column.TableName, column.ColumnName, command.CommandText);

                using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                var values = new SortedDictionary<UserIdentifier, long>();
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    if (reader.IsDBNull(0))
                    {
                        continue;
                    }

                    var rawValue = reader.GetValue(0);
                    var userId = UserIdentifier.FromDatabaseValue(rawValue);
                    var rowCount = Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture);
                    values[userId] = rowCount;
                }

                var distinctCount = values.Count;
                var totalRowCount = values.Sum(static pair => pair.Value);

                var columnResult = distinctCount == 0
                    ? ImmutableDictionary<UserIdentifier, long>.Empty
                    : values.ToImmutableDictionary(static pair => pair.Key, static pair => pair.Value);

                results[column] = columnResult;
                progress?.Report(1);

                _logger.LogDebug(
                    "Column {Schema}.{Table}.{Column} produced {DistinctCount} distinct user IDs across {RowCount} rows.",
                    column.SchemaName,
                    column.TableName,
                    column.ColumnName,
                    distinctCount,
                    totalRowCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze column {Schema}.{Table}.{Column}.", column.SchemaName, column.TableName, column.ColumnName);
                throw;
            }
        }).ConfigureAwait(false);

        return results.ToImmutableDictionary();
    }

    private static string BuildCommandText(UserFkColumn column)
    {
        var schema = SqlFormatting.QuoteIdentifier(column.SchemaName);
        var table = SqlFormatting.QuoteIdentifier(column.TableName);
        var col = SqlFormatting.QuoteIdentifier(column.ColumnName);

        return $@"
SELECT t.{col} AS UserId, COUNT_BIG(*) AS RowCount
FROM {schema}.{table} AS t
WHERE t.{col} IS NOT NULL
GROUP BY t.{col}
ORDER BY t.{col};";
    }
}
