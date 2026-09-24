using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Osm.Pipeline.Configuration;

internal sealed class SqlSectionReader
{
    public bool TryRead(JsonElement root, out SqlConfiguration configuration)
    {
        configuration = SqlConfiguration.Empty;
        if (!root.TryGetProperty("sql", out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        string? connectionString = null;
        if (element.TryGetProperty("connectionString", out var connectionElement)
            && connectionElement.ValueKind == JsonValueKind.String)
        {
            connectionString = connectionElement.GetString();
        }

        int? commandTimeout = null;
        if (element.TryGetProperty("commandTimeoutSeconds", out var timeoutElement)
            && timeoutElement.ValueKind == JsonValueKind.Number
            && timeoutElement.TryGetInt32(out var parsedTimeout))
        {
            commandTimeout = parsedTimeout;
        }

        var sampling = ReadSampling(element);
        var authentication = ReadAuthentication(element);
        var metadataContract = ReadMetadataContract(element);
        var profilingConnections = ReadProfilingConnections(element);
        var tableNameMappings = ReadTableNameMappings(element);

        configuration = new SqlConfiguration(connectionString, commandTimeout, sampling, authentication, metadataContract, profilingConnections, tableNameMappings);
        return true;
    }

    private static SqlSamplingConfiguration ReadSampling(JsonElement element)
    {
        if (!element.TryGetProperty("sampling", out var samplingElement) || samplingElement.ValueKind != JsonValueKind.Object)
        {
            return SqlSamplingConfiguration.Empty;
        }

        long? threshold = null;
        if (samplingElement.TryGetProperty("rowSamplingThreshold", out var thresholdElement)
            && thresholdElement.ValueKind == JsonValueKind.Number
            && thresholdElement.TryGetInt64(out var parsedThreshold))
        {
            threshold = parsedThreshold;
        }

        int? size = null;
        if (samplingElement.TryGetProperty("sampleSize", out var sizeElement)
            && sizeElement.ValueKind == JsonValueKind.Number
            && sizeElement.TryGetInt32(out var parsedSize))
        {
            size = parsedSize;
        }

        return new SqlSamplingConfiguration(threshold, size);
    }

    private static SqlAuthenticationConfiguration ReadAuthentication(JsonElement element)
    {
        if (!element.TryGetProperty("authentication", out var authElement) || authElement.ValueKind != JsonValueKind.Object)
        {
            return SqlAuthenticationConfiguration.Empty;
        }

        SqlAuthenticationMethod? method = null;
        if (authElement.TryGetProperty("method", out var methodElement)
            && methodElement.ValueKind == JsonValueKind.String)
        {
            var raw = methodElement.GetString();
            if (!string.IsNullOrWhiteSpace(raw)
                && Enum.TryParse(raw, ignoreCase: true, out SqlAuthenticationMethod parsedMethod))
            {
                method = parsedMethod;
            }
        }

        bool? trustServerCertificate = null;
        if (authElement.TryGetProperty("trustServerCertificate", out var trustElement)
            && ConfigurationJsonHelpers.TryParseBoolean(trustElement, out var parsedTrust))
        {
            trustServerCertificate = parsedTrust;
        }

        string? applicationName = null;
        if (authElement.TryGetProperty("applicationName", out var appElement)
            && appElement.ValueKind == JsonValueKind.String)
        {
            applicationName = appElement.GetString();
        }

        string? accessToken = null;
        if (authElement.TryGetProperty("accessToken", out var tokenElement)
            && tokenElement.ValueKind == JsonValueKind.String)
        {
            accessToken = tokenElement.GetString();
        }

        return new SqlAuthenticationConfiguration(method, trustServerCertificate, applicationName, accessToken);
    }

    private static MetadataContractConfiguration ReadMetadataContract(JsonElement element)
    {
        if (!element.TryGetProperty("metadataContract", out var contractElement)
            || contractElement.ValueKind != JsonValueKind.Object)
        {
            return MetadataContractConfiguration.Empty;
        }

        var optionalColumns = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (contractElement.TryGetProperty("optionalColumns", out var optionalElement)
            && optionalElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in optionalElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var resultSetName = property.Name?.Trim();
                if (string.IsNullOrWhiteSpace(resultSetName))
                {
                    continue;
                }

                var columns = property.Value
                    .EnumerateArray()
                    .Where(static item => item.ValueKind == JsonValueKind.String)
                    .Select(static item => item.GetString())
                    .Where(static name => !string.IsNullOrWhiteSpace(name))
                    .Select(static name => name!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (columns.Length > 0)
                {
                    optionalColumns[resultSetName] = columns;
                }
            }
        }

        return optionalColumns.Count > 0
            ? new MetadataContractConfiguration(optionalColumns)
            : MetadataContractConfiguration.Empty;
    }

    private static IReadOnlyList<string> ReadProfilingConnections(JsonElement element)
    {
        if (!element.TryGetProperty("profilingConnectionStrings", out var connectionsElement)
            || connectionsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var connections = new List<string>();
        foreach (var item in connectionsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var raw = item.GetString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var trimmed = raw.Trim();
            if (trimmed.Length > 0)
            {
                connections.Add(trimmed);
            }
        }

        return connections.Count == 0
            ? Array.Empty<string>()
            : connections;
    }

    private static IReadOnlyList<TableNameMappingConfiguration> ReadTableNameMappings(JsonElement element)
    {
        if (!element.TryGetProperty("tableNameMappings", out var mappingsElement)
            || mappingsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<TableNameMappingConfiguration>();
        }

        var mappings = new List<TableNameMappingConfiguration>();
        foreach (var item in mappingsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string? sourceSchema = null;
            if (item.TryGetProperty("sourceSchema", out var sourceSchemaElement)
                && sourceSchemaElement.ValueKind == JsonValueKind.String)
            {
                sourceSchema = sourceSchemaElement.GetString();
            }

            string? sourceTable = null;
            if (item.TryGetProperty("sourceTable", out var sourceTableElement)
                && sourceTableElement.ValueKind == JsonValueKind.String)
            {
                sourceTable = sourceTableElement.GetString();
            }

            string? targetSchema = null;
            if (item.TryGetProperty("targetSchema", out var targetSchemaElement)
                && targetSchemaElement.ValueKind == JsonValueKind.String)
            {
                targetSchema = targetSchemaElement.GetString();
            }

            string? targetTable = null;
            if (item.TryGetProperty("targetTable", out var targetTableElement)
                && targetTableElement.ValueKind == JsonValueKind.String)
            {
                targetTable = targetTableElement.GetString();
            }

            if (string.IsNullOrWhiteSpace(sourceSchema) ||
                string.IsNullOrWhiteSpace(sourceTable) ||
                string.IsNullOrWhiteSpace(targetSchema) ||
                string.IsNullOrWhiteSpace(targetTable))
            {
                continue;
            }

            mappings.Add(new TableNameMappingConfiguration(
                sourceSchema!.Trim(),
                sourceTable!.Trim(),
                targetSchema!.Trim(),
                targetTable!.Trim()));
        }

        return mappings.Count == 0
            ? Array.Empty<TableNameMappingConfiguration>()
            : mappings;
    }
}
