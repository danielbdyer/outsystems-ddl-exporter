using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;

namespace Osm.Pipeline.SqlExtraction;

internal static class SqlMetadataDiagnosticsWriter
{
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = true };

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static async Task WriteSnapshotAsync(
        string? path,
        OutsystemsMetadataSnapshot snapshot,
        DateTimeOffset exportedAtUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var absolutePath = EnsureDirectory(path);

        await using var stream = new FileStream(absolutePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var writer = new Utf8JsonWriter(stream, WriterOptions);

        writer.WriteStartObject();
        writer.WriteString("status", "success");
        writer.WriteString("exportedAtUtc", exportedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        writer.WriteString("databaseName", snapshot.DatabaseName);

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

        writer.WriteEndObject();

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task WriteFailureAsync(
        string? path,
        IReadOnlyList<ValidationError> errors,
        MetadataRowSnapshot? rowSnapshot,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var absolutePath = EnsureDirectory(path);

        await using var stream = new FileStream(absolutePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var writer = new Utf8JsonWriter(stream, WriterOptions);

        writer.WriteStartObject();
        writer.WriteString("status", "failure");
        writer.WritePropertyName("errors");
        writer.WriteStartArray();
        if (errors is not null)
        {
            foreach (var error in errors)
            {
                writer.WriteStartObject();
                writer.WriteString("code", error.Code);
                writer.WriteString("message", error.Message);
                writer.WriteEndObject();
            }
        }
        writer.WriteEndArray();

        if (rowSnapshot is not null)
        {
            writer.WritePropertyName("rowSnapshot");
            rowSnapshot.WriteTo(writer);
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
