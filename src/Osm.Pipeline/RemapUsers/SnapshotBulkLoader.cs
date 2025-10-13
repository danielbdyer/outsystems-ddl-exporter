using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.RemapUsers;

public sealed class SnapshotBulkLoader : IBulkLoader
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SnapshotBulkLoader(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task LoadAsync(BulkLoadRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var snapshotFile = ResolveSnapshotFile(request.SourceDirectory, request.TableSchema, request.TableName);
        await using var connection = (SqlConnection)await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var table = await CreateTableTemplateAsync(connection, request, cancellationToken).ConfigureAwait(false);
        await PopulateTableFromSnapshotAsync(table, snapshotFile, cancellationToken).ConfigureAwait(false);

        using var bulkCopy = new SqlBulkCopy(connection)
        {
            DestinationTableName = $"[{request.StagingSchema}].[{request.TableName}]",
            BatchSize = request.BatchSize,
            BulkCopyTimeout = Math.Max(1, (int)Math.Ceiling(request.CommandTimeout.TotalSeconds))
        };

        await bulkCopy.WriteToServerAsync(table, cancellationToken).ConfigureAwait(false);
    }

    private static string ResolveSnapshotFile(string sourceDirectory, string schema, string table)
    {
        var root = Path.GetFullPath(sourceDirectory);
        var candidates = new[]
        {
            Path.Combine(root, $"{schema}.{table}.json"),
            Path.Combine(root, schema, $"{table}.json"),
            Path.Combine(root, $"{table}.json")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Snapshot file for {schema}.{table} was not found in {root}.");
    }

    private static async Task<DataTable> CreateTableTemplateAsync(SqlConnection connection, BulkLoadRequest request, CancellationToken cancellationToken)
    {
        var commandText = $"SELECT TOP 0 * FROM [{request.StagingSchema}].[{request.TableName}]";
        await using var command = new SqlCommand(commandText, connection)
        {
            CommandTimeout = Math.Max(1, (int)Math.Ceiling(request.CommandTimeout.TotalSeconds))
        };

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken).ConfigureAwait(false);
        var schema = reader.GetSchemaTable();
        if (schema is null)
        {
            throw new InvalidOperationException($"Unable to resolve staging schema for {request.TableSchema}.{request.TableName}.");
        }

        var table = new DataTable();
        foreach (DataRow row in schema.Rows)
        {
            var columnName = (string)row["ColumnName"];
            var dataType = (Type)row["DataType"];
            var column = table.Columns.Add(columnName, dataType);
            column.AllowDBNull = row.Field<bool>("AllowDBNull");
        }

        return table;
    }

    private static async Task PopulateTableFromSnapshotAsync(DataTable table, string snapshotFile, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(snapshotFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Snapshot files must contain a JSON array of rows.");
        }

        foreach (var element in document.RootElement.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var row = table.NewRow();
            foreach (DataColumn column in table.Columns)
            {
                if (!element.TryGetProperty(column.ColumnName, out var property) || property.ValueKind == JsonValueKind.Null)
                {
                    row[column.ColumnName] = DBNull.Value;
                    continue;
                }

                row[column.ColumnName] = ConvertJsonValue(property, column.DataType) ?? DBNull.Value;
            }

            table.Rows.Add(row);
        }
    }

    private static object? ConvertJsonValue(JsonElement element, Type targetType)
    {
        var nonNullable = Nullable.GetUnderlyingType(targetType) ?? targetType;

        try
        {
            if (nonNullable == typeof(string))
            {
                return element.GetString();
            }

            if (nonNullable == typeof(int))
            {
                return element.GetInt32();
            }

            if (nonNullable == typeof(long))
            {
                return element.GetInt64();
            }

            if (nonNullable == typeof(short))
            {
                return (short)element.GetInt16();
            }

            if (nonNullable == typeof(decimal))
            {
                return element.GetDecimal();
            }

            if (nonNullable == typeof(double))
            {
                return element.GetDouble();
            }

            if (nonNullable == typeof(bool))
            {
                return element.GetBoolean();
            }

            if (nonNullable == typeof(DateTime))
            {
                return element.GetDateTime();
            }

            if (nonNullable == typeof(DateTimeOffset))
            {
                return element.GetDateTimeOffset();
            }

            if (nonNullable == typeof(Guid))
            {
                return element.GetGuid();
            }

            if (nonNullable == typeof(byte[]))
            {
                return element.GetBytesFromBase64();
            }

            if (nonNullable == typeof(byte))
            {
                return element.GetByte();
            }

            if (nonNullable.IsEnum)
            {
                var value = element.ValueKind == JsonValueKind.Number ? element.GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture) : element.GetString();
                return Enum.Parse(nonNullable, value!, ignoreCase: true);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Unable to convert snapshot value '{element}' to {nonNullable}.", ex);
        }

        return element.GetString();
    }
}
