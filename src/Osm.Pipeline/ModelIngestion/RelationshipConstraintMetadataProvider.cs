using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.ModelIngestion;

internal interface IRelationshipConstraintMetadataProvider
{
    Task<IReadOnlyList<ForeignKeyColumnMetadata>> LoadAsync(
        IReadOnlyCollection<RelationshipConstraintKey> requests,
        ModelIngestionSqlMetadataOptions sqlOptions,
        CancellationToken cancellationToken);
}

internal sealed class SqlRelationshipConstraintMetadataProvider : IRelationshipConstraintMetadataProvider
{
    private readonly Func<string, SqlConnectionOptions, IDbConnectionFactory> _connectionFactoryFactory;

    public SqlRelationshipConstraintMetadataProvider(
        Func<string, SqlConnectionOptions, IDbConnectionFactory> connectionFactoryFactory)
    {
        _connectionFactoryFactory = connectionFactoryFactory ?? throw new ArgumentNullException(nameof(connectionFactoryFactory));
    }

    public async Task<IReadOnlyList<ForeignKeyColumnMetadata>> LoadAsync(
        IReadOnlyCollection<RelationshipConstraintKey> requests,
        ModelIngestionSqlMetadataOptions sqlOptions,
        CancellationToken cancellationToken)
    {
        if (requests is null)
        {
            throw new ArgumentNullException(nameof(requests));
        }

        if (sqlOptions is null)
        {
            throw new ArgumentNullException(nameof(sqlOptions));
        }

        if (requests.Count == 0)
        {
            return Array.Empty<ForeignKeyColumnMetadata>();
        }

        var uniqueRequests = new HashSet<RelationshipConstraintKey>(RelationshipConstraintKeyComparer.Instance);
        foreach (var request in requests)
        {
            if (request.IsValid)
            {
                uniqueRequests.Add(request);
            }
        }

        if (uniqueRequests.Count == 0)
        {
            return Array.Empty<ForeignKeyColumnMetadata>();
        }

        var factory = _connectionFactoryFactory(sqlOptions.ConnectionString, sqlOptions.ConnectionOptions);
        await using var connection = await factory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        if (sqlOptions.CommandTimeoutSeconds.HasValue)
        {
            command.CommandTimeout = sqlOptions.CommandTimeoutSeconds.Value;
        }

        var valuesClause = BuildValuesClause(uniqueRequests, command);
        command.CommandText = $@"
WITH MissingConstraints (SchemaName, TableName, ConstraintName) AS (
    SELECT * FROM (VALUES {valuesClause}) AS mc (SchemaName, TableName, ConstraintName)
)
SELECT
    mc.SchemaName,
    mc.TableName,
    mc.ConstraintName,
    fkc.constraint_column_id AS Ordinal,
    parent_col.name AS ParentColumn,
    ref_col.name AS ReferencedColumn,
    ref_schema.name AS ReferencedSchema,
    ref_table.name AS ReferencedTable
FROM MissingConstraints mc
JOIN sys.schemas parent_schema ON parent_schema.name = mc.SchemaName
JOIN sys.tables parent_table ON parent_table.schema_id = parent_schema.schema_id AND parent_table.name = mc.TableName
JOIN sys.foreign_keys fk ON fk.parent_object_id = parent_table.object_id AND fk.name = mc.ConstraintName
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.columns parent_col ON parent_col.object_id = fk.parent_object_id AND parent_col.column_id = fkc.parent_column_id
JOIN sys.columns ref_col ON ref_col.object_id = fk.referenced_object_id AND ref_col.column_id = fkc.referenced_column_id
JOIN sys.tables ref_table ON ref_table.object_id = fk.referenced_object_id
JOIN sys.schemas ref_schema ON ref_schema.schema_id = ref_table.schema_id
ORDER BY mc.SchemaName, mc.TableName, mc.ConstraintName, fkc.constraint_column_id;";

        var results = new List<ForeignKeyColumnMetadata>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var constraintKey = new RelationshipConstraintKey(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2));

            results.Add(new ForeignKeyColumnMetadata(
                constraintKey,
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7)));
        }

        return results;
    }

    private static string BuildValuesClause(
        IEnumerable<RelationshipConstraintKey> requests,
        DbCommand command)
    {
        var builder = new StringBuilder();
        var index = 0;
        foreach (var request in requests)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            var schemaParameter = command.CreateParameter();
            schemaParameter.ParameterName = $"@schema{index}";
            schemaParameter.Value = request.Schema;
            command.Parameters.Add(schemaParameter);

            var tableParameter = command.CreateParameter();
            tableParameter.ParameterName = $"@table{index}";
            tableParameter.Value = request.Table;
            command.Parameters.Add(tableParameter);

            var constraintParameter = command.CreateParameter();
            constraintParameter.ParameterName = $"@constraint{index}";
            constraintParameter.Value = request.ConstraintName;
            command.Parameters.Add(constraintParameter);

            builder.Append($"(@schema{index}, @table{index}, @constraint{index})");
            index++;
        }

        return builder.ToString();
    }
}

internal sealed record ForeignKeyColumnMetadata(
    RelationshipConstraintKey Constraint,
    int Ordinal,
    string OwnerColumn,
    string ReferencedColumn,
    string ReferencedSchema,
    string ReferencedTable);
