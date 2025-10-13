using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.UatUsers;

public interface IUserForeignKeyValueProvider
{
    Task<IReadOnlyDictionary<UserFkColumn, IReadOnlyDictionary<long, long>>> CollectAsync(
        IReadOnlyList<UserFkColumn> catalog,
        IDbConnectionFactory connectionFactory,
        CancellationToken cancellationToken);
}

public sealed class SqlUserForeignKeyValueProvider : IUserForeignKeyValueProvider
{
    public async Task<IReadOnlyDictionary<UserFkColumn, IReadOnlyDictionary<long, long>>> CollectAsync(
        IReadOnlyList<UserFkColumn> catalog,
        IDbConnectionFactory connectionFactory,
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
            return ImmutableDictionary<UserFkColumn, IReadOnlyDictionary<long, long>>.Empty;
        }

        await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var results = ImmutableDictionary.CreateBuilder<UserFkColumn, IReadOnlyDictionary<long, long>>();

        foreach (var column in catalog)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var command = connection.CreateCommand();
            command.CommandText = BuildCommandText(column);
            command.CommandType = CommandType.Text;

            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var values = new SortedDictionary<long, long>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.IsDBNull(0))
                {
                    continue;
                }

                var userId = Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture);
                var rowCount = Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture);
                values[userId] = rowCount;
            }

            results[column] = values.Count == 0
                ? ImmutableDictionary<long, long>.Empty
                : values.ToImmutableDictionary(static pair => pair.Key, static pair => pair.Value);
        }

        return results.ToImmutable();
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
