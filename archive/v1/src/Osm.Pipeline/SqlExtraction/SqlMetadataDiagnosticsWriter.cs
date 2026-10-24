using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.SqlExtraction;

internal static class SqlMetadataDiagnosticsWriter
{
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = true };

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static async Task WriteAsync(
        string? path,
        SqlMetadataLog log,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (log is null)
        {
            throw new ArgumentNullException(nameof(log));
        }

        var absolutePath = EnsureDirectory(path);
        var state = log.BuildState();

        await using var stream = new FileStream(absolutePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var writer = new Utf8JsonWriter(stream, WriterOptions);

        writer.WriteStartObject();
        if (state.HasSnapshot)
        {
            writer.WriteString("status", "success");
            if (state.ExportedAtUtc.HasValue)
            {
                writer.WriteString("exportedAtUtc", state.ExportedAtUtc.Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrWhiteSpace(state.DatabaseName))
            {
                writer.WriteString("databaseName", state.DatabaseName);
            }

            var snapshot = state.Snapshot!;
            WriteArray(writer, "modules", snapshot.Modules);
            WriteArray(writer, "entities", snapshot.Entities);
            WriteArray(writer, "attributes", snapshot.Attributes);
            WriteArray(writer, "references", snapshot.References);
            WriteArray(writer, "physicalTables", snapshot.PhysicalTables);
            WriteArray(writer, "columnReality", snapshot.ColumnReality);
            WriteArray(writer, "columnChecks", snapshot.ColumnChecks);
            WriteArray(writer, "columnCheckJson", snapshot.ColumnCheckJson);
            WriteArray(writer, "physicalColumnsPresent", snapshot.PhysicalColumnsPresent);
            WriteArray(writer, "indexes", snapshot.Indexes);
            WriteArray(writer, "indexColumns", snapshot.IndexColumns);
            WriteArray(writer, "foreignKeys", snapshot.ForeignKeys);
            WriteArray(writer, "foreignKeyColumns", snapshot.ForeignKeyColumns);
            WriteArray(writer, "foreignKeyAttributeMap", snapshot.ForeignKeyAttributeMap);
            WriteArray(writer, "attributeForeignKeys", snapshot.AttributeForeignKeys);
            WriteArray(writer, "foreignKeyColumnsJson", snapshot.ForeignKeyColumnsJson);
            WriteArray(writer, "foreignKeyAttributeJson", snapshot.ForeignKeyAttributeJson);
            WriteArray(writer, "triggers", snapshot.Triggers);
            WriteArray(writer, "attributeJson", snapshot.AttributeJson);
            WriteArray(writer, "relationshipJson", snapshot.RelationshipJson);
            WriteArray(writer, "indexJson", snapshot.IndexJson);
            WriteArray(writer, "triggerJson", snapshot.TriggerJson);
            WriteArray(writer, "moduleJson", snapshot.ModuleJson);
        }
        else if (state.HasErrors)
        {
            writer.WriteString("status", "failure");
            writer.WritePropertyName("errors");
            writer.WriteStartArray();
            foreach (var error in state.Errors)
            {
                writer.WriteStartObject();
                writer.WriteString("code", error.Code);
                writer.WriteString("message", error.Message);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            if (state.FailureRowSnapshot is not null)
            {
                writer.WritePropertyName("rowSnapshot");
                state.FailureRowSnapshot.WriteTo(writer);
            }
        }
        else
        {
            writer.WriteString("status", "success");
        }

        if (state.HasRequests)
        {
            writer.WritePropertyName("requests");
            writer.WriteStartArray();
            foreach (var entry in state.Requests)
            {
                writer.WriteStartObject();
                writer.WriteString("name", entry.Name);
                writer.WritePropertyName("payload");
                JsonSerializer.Serialize(writer, entry.Payload, SerializerOptions);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void WriteArray<T>(Utf8JsonWriter writer, string propertyName, IReadOnlyList<T> values)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartArray();

        if (values is not null)
        {
            foreach (var value in values)
            {
                JsonSerializer.Serialize(writer, value, SerializerOptions);
            }
        }

        writer.WriteEndArray();
    }

    private static string EnsureDirectory(string path)
    {
        var absolutePath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return absolutePath;
    }
}
