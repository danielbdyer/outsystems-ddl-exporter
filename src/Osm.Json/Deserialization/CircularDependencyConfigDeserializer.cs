using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;

namespace Osm.Json.Deserialization;

/// <summary>
/// Deserializes circular dependency configuration from JSON.
/// </summary>
public sealed class CircularDependencyConfigDeserializer
{
    /// <summary>
    /// Loads CircularDependencyOptions from a JSON file.
    /// </summary>
    public static async Task<Result<CircularDependencyOptions>> LoadFromFileAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Result<CircularDependencyOptions>.Failure(
                ValidationError.Create("CircularDepsConfig.MissingPath", "Configuration file path is required"));
        }

        if (!File.Exists(filePath))
        {
            return Result<CircularDependencyOptions>.Failure(
                ValidationError.Create("CircularDepsConfig.FileNotFound", $"Configuration file not found: {filePath}"));
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            return Deserialize(json);
        }
        catch (Exception ex)
        {
            return Result<CircularDependencyOptions>.Failure(
                ValidationError.Create("CircularDepsConfig.ReadError", $"Failed to read configuration file: {ex.Message}"));
        }
    }

    /// <summary>
    /// Deserializes CircularDependencyOptions from JSON string.
    /// </summary>
    public static Result<CircularDependencyOptions> Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Result<CircularDependencyOptions>.Failure(
                ValidationError.Create("CircularDepsConfig.EmptyJson", "Configuration JSON is empty"));
        }

        try
        {
            var doc = JsonDocument.Parse(json);
            return DeserializeFromDocument(doc.RootElement);
        }
        catch (JsonException ex)
        {
            return Result<CircularDependencyOptions>.Failure(
                ValidationError.Create("CircularDepsConfig.InvalidJson", $"Invalid JSON: {ex.Message}"));
        }
    }

    private static Result<CircularDependencyOptions> DeserializeFromDocument(JsonElement root)
    {
        // Parse strictMode (optional, defaults to false)
        var strictMode = false;
        if (root.TryGetProperty("strictMode", out var strictModeElement))
        {
            strictMode = strictModeElement.GetBoolean();
        }

        // Parse allowedCycles (optional, defaults to empty)
        var allowedCyclesBuilder = ImmutableArray.CreateBuilder<AllowedCycle>();

        if (root.TryGetProperty("allowedCycles", out var allowedCyclesElement) &&
            allowedCyclesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var cycleElement in allowedCyclesElement.EnumerateArray())
            {
                var cycleResult = ParseAllowedCycle(cycleElement);
                if (cycleResult.IsFailure)
                {
                    return Result<CircularDependencyOptions>.Failure(cycleResult.Errors);
                }

                allowedCyclesBuilder.Add(cycleResult.Value);
            }
        }

        return CircularDependencyOptions.Create(allowedCyclesBuilder.ToImmutable(), strictMode);
    }

    private static Result<AllowedCycle> ParseAllowedCycle(JsonElement cycleElement)
    {
        if (!cycleElement.TryGetProperty("tableOrdering", out var tableOrderingElement) ||
            tableOrderingElement.ValueKind != JsonValueKind.Array)
        {
            return Result<AllowedCycle>.Failure(
                ValidationError.Create("AllowedCycle.MissingTableOrdering", "AllowedCycle must have 'tableOrdering' array"));
        }

        var tableOrderingBuilder = ImmutableArray.CreateBuilder<TableOrdering>();

        foreach (var tableElement in tableOrderingElement.EnumerateArray())
        {
            var tableResult = ParseTableOrdering(tableElement);
            if (tableResult.IsFailure)
            {
                return Result<AllowedCycle>.Failure(tableResult.Errors);
            }

            tableOrderingBuilder.Add(tableResult.Value);
        }

        return AllowedCycle.Create(tableOrderingBuilder.ToImmutable());
    }

    private static Result<TableOrdering> ParseTableOrdering(JsonElement tableElement)
    {
        if (!tableElement.TryGetProperty("tableName", out var tableNameElement) ||
            tableNameElement.ValueKind != JsonValueKind.String)
        {
            return Result<TableOrdering>.Failure(
                ValidationError.Create("TableOrdering.MissingTableName", "TableOrdering must have 'tableName' string"));
        }

        if (!tableElement.TryGetProperty("position", out var positionElement) ||
            positionElement.ValueKind != JsonValueKind.Number)
        {
            return Result<TableOrdering>.Failure(
                ValidationError.Create("TableOrdering.MissingPosition", "TableOrdering must have 'position' number"));
        }

        var tableName = tableNameElement.GetString();
        var position = positionElement.GetInt32();

        return TableOrdering.Create(tableName!, position);
    }
}
