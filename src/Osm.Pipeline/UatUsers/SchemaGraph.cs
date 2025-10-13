using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Osm.Domain.Model;
using Osm.Domain.ValueObjects;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.UatUsers;

public interface IUserSchemaGraph
{
    Task<IReadOnlyList<ForeignKeyDefinition>> GetForeignKeysAsync(CancellationToken cancellationToken);
}

public sealed record ForeignKeyDefinition(
    string Name,
    ForeignKeyTable Parent,
    ForeignKeyTable Referenced,
    ImmutableArray<ForeignKeyColumn> Columns);

public sealed record ForeignKeyTable(string Schema, string Table);

public sealed record ForeignKeyColumn(string ParentColumn, string ReferencedColumn);

public sealed class ModelSchemaGraph : IUserSchemaGraph
{
    private readonly OsmModel _model;

    public ModelSchemaGraph(OsmModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public IReadOnlyList<ForeignKeyDefinition> GetSyntheticUserForeignKeys(
        string userSchema,
        string userTable,
        string userIdColumn,
        string? userEntityIdentifier)
    {
        if (string.IsNullOrWhiteSpace(userSchema) ||
            string.IsNullOrWhiteSpace(userTable) ||
            string.IsNullOrWhiteSpace(userIdColumn))
        {
            return Array.Empty<ForeignKeyDefinition>();
        }

        int? parsedEntityId = null;
        if (!string.IsNullOrWhiteSpace(userEntityIdentifier) &&
            int.TryParse(userEntityIdentifier, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericId))
        {
            parsedEntityId = numericId;
        }

        var results = new List<ForeignKeyDefinition>();
        foreach (var module in _model.Modules)
        {
            foreach (var entity in module.Entities)
            {
                foreach (var attribute in entity.Attributes)
                {
                    var reference = attribute.Reference;
                    if (!reference.IsReference)
                    {
                        continue;
                    }

                    if (!MatchesUserReference(reference, attribute, userTable, userEntityIdentifier, parsedEntityId))
                    {
                        continue;
                    }

                    var foreignKeyName = $"synthetic_{entity.PhysicalName.Value}_{attribute.ColumnName.Value}";
                    var parent = new ForeignKeyTable(entity.Schema.Value, entity.PhysicalName.Value);
                    var referenced = new ForeignKeyTable(userSchema, userTable);
                    var columns = ImmutableArray.Create(new ForeignKeyColumn(attribute.ColumnName.Value, userIdColumn));
                    results.Add(new ForeignKeyDefinition(foreignKeyName, parent, referenced, columns));
                }
            }
        }

        return results;
    }

    public Task<IReadOnlyList<ForeignKeyDefinition>> GetForeignKeysAsync(CancellationToken cancellationToken)
    {
        var result = new List<ForeignKeyDefinition>();

        foreach (var module in _model.Modules)
        {
            foreach (var entity in module.Entities)
            {
                if (entity.Relationships.IsDefaultOrEmpty)
                {
                    continue;
                }

                foreach (var relationship in entity.Relationships)
                {
                    if (relationship.ActualConstraints.IsDefaultOrEmpty)
                    {
                        continue;
                    }

                    foreach (var constraint in relationship.ActualConstraints)
                    {
                        if (string.IsNullOrWhiteSpace(constraint.Name) ||
                            string.IsNullOrWhiteSpace(constraint.ReferencedTable))
                        {
                            continue;
                        }

                        var columns = constraint.Columns
                            .Where(column =>
                                !string.IsNullOrWhiteSpace(column.OwnerColumn) &&
                                !string.IsNullOrWhiteSpace(column.ReferencedColumn))
                            .Select(column => new ForeignKeyColumn(
                                column.OwnerColumn.Trim(),
                                column.ReferencedColumn.Trim()))
                            .ToImmutableArray();

                        if (columns.IsDefaultOrEmpty)
                        {
                            continue;
                        }

                        var referencedSchema = string.IsNullOrWhiteSpace(constraint.ReferencedSchema)
                            ? entity.Schema.Value
                            : constraint.ReferencedSchema.Trim();

                        var definition = new ForeignKeyDefinition(
                            constraint.Name.Trim(),
                            new ForeignKeyTable(entity.Schema.Value, entity.PhysicalName.Value),
                            new ForeignKeyTable(referencedSchema, constraint.ReferencedTable.Trim()),
                            columns);

                        result.Add(definition);
                    }
                }
            }
        }

        return Task.FromResult<IReadOnlyList<ForeignKeyDefinition>>(result);
    }

    private static bool MatchesUserReference(
        AttributeReference reference,
        AttributeModel attribute,
        string userTable,
        string? userEntityIdentifier,
        int? parsedEntityId)
    {
        if (reference.TargetPhysicalName is TableName physicalName &&
            string.Equals(physicalName.Value, userTable, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (parsedEntityId.HasValue && reference.TargetEntityId.HasValue && reference.TargetEntityId.Value == parsedEntityId.Value)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(userEntityIdentifier))
        {
            return false;
        }

        if (reference.TargetPhysicalName is TableName hintedPhysical &&
            string.Equals(hintedPhysical.Value, userEntityIdentifier, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (reference.TargetEntity is EntityName entityName &&
            string.Equals(entityName.Value, userEntityIdentifier, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (reference.TargetEntityId.HasValue &&
            string.Equals(
                reference.TargetEntityId.Value.ToString(CultureInfo.InvariantCulture),
                userEntityIdentifier,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(attribute.DataType) &&
            string.Equals(attribute.DataType, userEntityIdentifier, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}

public sealed class LiveSchemaGraph : IUserSchemaGraph
{
    private readonly IDbConnectionFactory _connectionFactory;

    public LiveSchemaGraph(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<IReadOnlyList<ForeignKeyDefinition>> GetForeignKeysAsync(CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT
    fk.name,
    sp.name AS ParentSchema,
    tp.name AS ParentTable,
    cpa.name AS ParentColumn,
    sr.name AS ReferencedSchema,
    tr.name AS ReferencedTable,
    cref.name AS ReferencedColumn,
    fkc.constraint_column_id
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.tables tp ON tp.object_id = fk.parent_object_id
JOIN sys.schemas sp ON sp.schema_id = tp.schema_id
JOIN sys.tables tr ON tr.object_id = fk.referenced_object_id
JOIN sys.schemas sr ON sr.schema_id = tr.schema_id
JOIN sys.columns cpa ON cpa.object_id = fkc.parent_object_id AND cpa.column_id = fkc.parent_column_id
JOIN sys.columns cref ON cref.object_id = fkc.referenced_object_id AND cref.column_id = fkc.referenced_column_id
WHERE fk.is_ms_shipped = 0
ORDER BY fk.name, fkc.constraint_column_id;";

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, (SqlConnection)connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var lookup = new Dictionary<string, List<(ForeignKeyColumn Column, string ParentSchema, string ParentTable, string ReferencedSchema, string ReferencedTable)>>(
            StringComparer.OrdinalIgnoreCase);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var name = reader.GetString(0);
            var parentSchema = reader.GetString(1);
            var parentTable = reader.GetString(2);
            var parentColumn = reader.GetString(3);
            var referencedSchema = reader.GetString(4);
            var referencedTable = reader.GetString(5);
            var referencedColumn = reader.GetString(6);

            var column = new ForeignKeyColumn(parentColumn, referencedColumn);
            if (!lookup.TryGetValue(name, out var values))
            {
                values = new List<(ForeignKeyColumn, string, string, string, string)>();
                lookup.Add(name, values);
            }

            values.Add((column, parentSchema, parentTable, referencedSchema, referencedTable));
        }

        var results = new List<ForeignKeyDefinition>(lookup.Count);
        foreach (var pair in lookup)
        {
            if (pair.Value.Count == 0)
            {
                continue;
            }

            var first = pair.Value[0];
            var columns = pair.Value.Select(v => v.Column).ToImmutableArray();
            if (columns.IsDefaultOrEmpty)
            {
                continue;
            }

            results.Add(new ForeignKeyDefinition(
                pair.Key,
                new ForeignKeyTable(first.ParentSchema, first.ParentTable),
                new ForeignKeyTable(first.ReferencedSchema, first.ReferencedTable),
                columns));
        }

        return results;
    }
}
