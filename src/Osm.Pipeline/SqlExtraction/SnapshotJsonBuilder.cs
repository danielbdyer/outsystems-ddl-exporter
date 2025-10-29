using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.SqlExtraction;

public sealed class SnapshotJsonBuilder
{
    private const string DefaultSchemaFallback = "dbo";

    public SnapshotJsonBuildArtifact Build(
        OutsystemsMetadataSnapshot snapshot,
        DateTime exportedAtUtc,
        ModelExtractionOptions options)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var artifact = CreateDestination(options);

        try
        {
            BuildJsonFromSnapshot(snapshot, exportedAtUtc, artifact.Stream);
            return artifact;
        }
        catch
        {
            artifact.DisposeAsync().GetAwaiter().GetResult();
            throw;
        }
    }

    private static SnapshotJsonBuildArtifact CreateDestination(ModelExtractionOptions options)
    {
        if (options.DestinationStream is { } stream)
        {
            if (!stream.CanWrite)
            {
                throw new ArgumentException("Destination stream must be writable.", nameof(options));
            }

            if (!stream.CanSeek)
            {
                throw new ArgumentException("Destination stream must support seeking.", nameof(options));
            }

            stream.SetLength(0);
            stream.Position = 0;
            return new SnapshotJsonBuildArtifact(stream, filePath: null, dispose: false);
        }

        if (!string.IsNullOrWhiteSpace(options.DestinationPath))
        {
            var absolutePath = Path.GetFullPath(options.DestinationPath!.Trim());
            var directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var fileStream = new FileStream(absolutePath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            fileStream.SetLength(0);
            fileStream.Position = 0;
            return new SnapshotJsonBuildArtifact(fileStream, absolutePath, dispose: true);
        }

        var memoryStream = new MemoryStream();
        return new SnapshotJsonBuildArtifact(memoryStream, filePath: null, dispose: false);
    }

    private static void BuildJsonFromSnapshot(
        OutsystemsMetadataSnapshot snapshot,
        DateTime exportedAtUtc,
        Stream destination)
    {
        if (destination is null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        destination.SetLength(0);

        using var writer = new Utf8JsonWriter(destination);

        writer.WriteStartObject();
        writer.WriteString("exportedAtUtc", exportedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        writer.WritePropertyName("modules");
        writer.WriteStartArray();

        WriteModules(writer, snapshot);

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteModules(Utf8JsonWriter writer, OutsystemsMetadataSnapshot snapshot)
    {
        var modules = snapshot.Modules.ToArray();
        if (modules.Length == 0)
        {
            return;
        }

        var entitiesByModule = snapshot.Entities
            .GroupBy(static entity => entity.EspaceId)
            .ToDictionary(static group => group.Key, static group => group.ToArray());

        var attributeJsonByEntity = snapshot.AttributeJson
            .GroupBy(static row => row.EntityId)
            .ToDictionary(static group => group.Key, static group => (string?)group.Last().AttributesJson);

        var relationshipJsonByEntity = snapshot.RelationshipJson
            .GroupBy(static row => row.EntityId)
            .ToDictionary(static group => group.Key, static group => (string?)group.Last().RelationshipsJson);

        var indexJsonByEntity = snapshot.IndexJson
            .GroupBy(static row => row.EntityId)
            .ToDictionary(static group => group.Key, static group => (string?)group.Last().IndexesJson);

        var triggerJsonByEntity = snapshot.TriggerJson
            .GroupBy(static row => row.EntityId)
            .ToDictionary(static group => group.Key, static group => (string?)group.Last().TriggersJson);

        var schemaByEntity = snapshot.PhysicalTables
            .GroupBy(static row => row.EntityId)
            .ToDictionary(static group => group.Key, static group => group.Last().SchemaName);

        var databaseName = string.IsNullOrWhiteSpace(snapshot.DatabaseName)
            ? null
            : snapshot.DatabaseName;

        foreach (var module in modules)
        {
            writer.WriteStartObject();
            writer.WriteString("name", module.EspaceName);
            writer.WriteBoolean("isSystem", module.IsSystemModule);
            writer.WriteBoolean("isActive", module.ModuleIsActive);
            writer.WritePropertyName("entities");
            writer.WriteStartArray();

            if (entitiesByModule.TryGetValue(module.EspaceId, out var entities) && entities.Length > 0)
            {
                foreach (var entity in entities)
                {
                    WriteEntity(
                        writer,
                        entity,
                        databaseName,
                        schemaByEntity,
                        attributeJsonByEntity,
                        relationshipJsonByEntity,
                        indexJsonByEntity,
                        triggerJsonByEntity);
                }
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }

    private static void WriteEntity(
        Utf8JsonWriter writer,
        OutsystemsEntityRow entity,
        string? databaseName,
        IReadOnlyDictionary<int, string> schemaByEntity,
        IReadOnlyDictionary<int, string?> attributeJsonByEntity,
        IReadOnlyDictionary<int, string?> relationshipJsonByEntity,
        IReadOnlyDictionary<int, string?> indexJsonByEntity,
        IReadOnlyDictionary<int, string?> triggerJsonByEntity)
    {
        writer.WriteStartObject();
        writer.WriteString("name", entity.EntityName);
        writer.WriteString("physicalName", entity.PhysicalTableName);
        writer.WriteBoolean("isStatic", string.Equals(entity.DataKind, "staticEntity", StringComparison.OrdinalIgnoreCase));
        writer.WriteBoolean("isExternal", entity.IsExternalEntity);
        writer.WriteBoolean("isActive", entity.EntityIsActive);

        if (!string.IsNullOrWhiteSpace(databaseName))
        {
            writer.WriteString("db_catalog", databaseName);
        }
        else
        {
            writer.WriteNull("db_catalog");
        }

        var schema = schemaByEntity.TryGetValue(entity.EntityId, out var schemaName)
            ? schemaName
            : null;

        var normalizedSchema = string.IsNullOrWhiteSpace(schema) ? DefaultSchemaFallback : schema!.Trim();
        writer.WriteString("db_schema", normalizedSchema);

        if (!string.IsNullOrWhiteSpace(entity.EntityDescription))
        {
            writer.WriteString("meta", entity.EntityDescription.Trim());
        }

        writer.WritePropertyName("attributes");
        writer.WriteRawValue(ResolveJsonArray(attributeJsonByEntity, entity.EntityId), skipInputValidation: true);

        writer.WritePropertyName("relationships");
        writer.WriteRawValue(ResolveJsonArray(relationshipJsonByEntity, entity.EntityId), skipInputValidation: true);

        writer.WritePropertyName("indexes");
        writer.WriteRawValue(ResolveJsonArray(indexJsonByEntity, entity.EntityId), skipInputValidation: true);

        writer.WritePropertyName("triggers");
        writer.WriteRawValue(ResolveJsonArray(triggerJsonByEntity, entity.EntityId), skipInputValidation: true);

        writer.WriteEndObject();
    }

    private static string ResolveJsonArray(IReadOnlyDictionary<int, string?> source, int entityId)
    {
        if (source.TryGetValue(entityId, out var payload))
        {
            if (payload is null)
            {
                return "null";
            }

            if (payload.Length > 0)
            {
                return payload;
            }
        }

        return "[]";
    }
}

public sealed class SnapshotJsonBuildArtifact : IAsyncDisposable
{
    private readonly bool _dispose;

    public SnapshotJsonBuildArtifact(Stream stream, string? filePath, bool dispose)
    {
        Stream = stream ?? throw new ArgumentNullException(nameof(stream));
        FilePath = filePath;
        _dispose = dispose;
    }

    public Stream Stream { get; }

    public string? FilePath { get; }

    public async ValueTask DisposeAsync()
    {
        if (_dispose)
        {
            await Stream.DisposeAsync().ConfigureAwait(false);
        }
    }
}
