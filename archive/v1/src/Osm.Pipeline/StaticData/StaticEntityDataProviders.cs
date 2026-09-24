using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Osm.Domain.Abstractions;
using Osm.Emission.Seeds;
using Osm.Pipeline.Sql;

namespace Osm.Pipeline.StaticData;

public sealed class FixtureStaticEntityDataProvider : IStaticEntityDataProvider
{
    private readonly string _dataPath;

    public FixtureStaticEntityDataProvider(string dataPath)
    {
        if (string.IsNullOrWhiteSpace(dataPath))
        {
            throw new ArgumentException("Static entity data path must be provided.", nameof(dataPath));
        }

        _dataPath = Path.GetFullPath(dataPath.Trim());
    }

    public Task<Result<IReadOnlyList<StaticEntityTableData>>> GetDataAsync(
        IReadOnlyList<StaticEntitySeedTableDefinition> definitions,
        CancellationToken cancellationToken = default)
    {
        if (definitions is null)
        {
            throw new ArgumentNullException(nameof(definitions));
        }

        if (!File.Exists(_dataPath))
        {
            return Task.FromResult(Result<IReadOnlyList<StaticEntityTableData>>.Failure(ValidationError.Create(
                "cli.staticData.fixture.missing",
                $"Static entity data fixture '{_dataPath}' was not found.")));
        }

        using var stream = File.OpenRead(_dataPath);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        if (!root.TryGetProperty("tables", out var tablesElement) || tablesElement.ValueKind != JsonValueKind.Array)
        {
            return Task.FromResult(Result<IReadOnlyList<StaticEntityTableData>>.Failure(ValidationError.Create(
                "cli.staticData.fixture.invalid",
                "Static entity data fixture must contain a 'tables' array.")));
        }

        var lookup = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var tableElement in tablesElement.EnumerateArray())
        {
            if (!tableElement.TryGetProperty("schema", out var schemaElement) || schemaElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            if (!tableElement.TryGetProperty("table", out var tableNameElement) || tableNameElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var schema = schemaElement.GetString() ?? string.Empty;
            var table = tableNameElement.GetString() ?? string.Empty;
            if (schema.Length == 0 || table.Length == 0)
            {
                continue;
            }

            var key = BuildKey(schema, table);
            lookup[key] = tableElement;
        }

        var results = new List<StaticEntityTableData>(definitions.Count);
        foreach (var definition in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = BuildKey(definition.Schema, definition.PhysicalName);
            if (!lookup.TryGetValue(key, out var element))
            {
                return Task.FromResult(Result<IReadOnlyList<StaticEntityTableData>>.Failure(ValidationError.Create(
                    "cli.staticData.fixture.tableMissing",
                    $"Fixture data does not contain rows for '{definition.Schema}.{definition.PhysicalName}'.")));
            }

            if (!element.TryGetProperty("rows", out var rowsElement) || rowsElement.ValueKind != JsonValueKind.Array)
            {
                return Task.FromResult(Result<IReadOnlyList<StaticEntityTableData>>.Failure(ValidationError.Create(
                    "cli.staticData.fixture.rowsMissing",
                    $"Fixture data for '{definition.Schema}.{definition.PhysicalName}' must include a 'rows' array.")));
            }

            var rows = new List<StaticEntityRow>();
            foreach (var rowElement in rowsElement.EnumerateArray())
            {
                var values = new object?[definition.Columns.Length];
                for (var i = 0; i < definition.Columns.Length; i++)
                {
                    var column = definition.Columns[i];
                    if (!rowElement.TryGetProperty(column.ColumnName, out var valueElement))
                    {
                        return Task.FromResult(Result<IReadOnlyList<StaticEntityTableData>>.Failure(ValidationError.Create(
                            "cli.staticData.fixture.columnMissing",
                            $"Row for '{definition.Schema}.{definition.PhysicalName}' is missing column '{column.ColumnName}'.")));
                    }

                    values[i] = column.NormalizeValue(ConvertJsonValue(valueElement, column));
                }

                rows.Add(StaticEntityRow.Create(values));
            }

            results.Add(StaticEntityTableData.Create(definition, rows));
        }

        return Task.FromResult(Result<IReadOnlyList<StaticEntityTableData>>.Success(results));
    }

    private static object? ConvertJsonValue(JsonElement element, StaticEntitySeedColumn column)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var type = column.DataType ?? string.Empty;
        if (IsBoolean(type))
        {
            if (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)
            {
                return element.GetBoolean();
            }

            if (element.ValueKind == JsonValueKind.String && bool.TryParse(element.GetString(), out var boolResult))
            {
                return boolResult;
            }
        }

        if (IsInteger(type))
        {
            if (element.ValueKind == JsonValueKind.Number)
            {
                return element.TryGetInt64(out var longValue) ? longValue : element.GetDecimal();
            }

            if (element.ValueKind == JsonValueKind.String && long.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLong))
            {
                return parsedLong;
            }
        }

        if (IsDecimal(type))
        {
            if (element.ValueKind == JsonValueKind.Number)
            {
                return element.GetDecimal();
            }

            if (element.ValueKind == JsonValueKind.String && decimal.TryParse(element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedDecimal))
            {
                return parsedDecimal;
            }
        }

        if (IsDateOnly(type))
        {
            if (element.ValueKind == JsonValueKind.String && DateOnly.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return date;
            }
        }

        if (IsTime(type))
        {
            if (element.ValueKind == JsonValueKind.String && TimeOnly.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                return time;
            }
        }

        if (IsDateTime(type))
        {
            if (element.ValueKind == JsonValueKind.String && DateTime.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTime))
            {
                return dateTime;
            }

            if (element.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTimeOffset))
            {
                return dateTimeOffset;
            }
        }

        if (IsGuid(type))
        {
            if (element.ValueKind == JsonValueKind.String && Guid.TryParse(element.GetString(), out var guid))
            {
                return guid;
            }
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var number) ? number : element.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => element.GetRawText()
        };
    }

    private static string BuildKey(string schema, string table)
        => FormattableString.Invariant($"{schema}.{table}");

    private static bool IsBoolean(string dataType)
        => string.Equals(dataType, "Boolean", StringComparison.OrdinalIgnoreCase);

    private static bool IsInteger(string dataType)
        => dataType.Equals("Identifier", StringComparison.OrdinalIgnoreCase)
            || dataType.Equals("Integer", StringComparison.OrdinalIgnoreCase)
            || dataType.Equals("LongInteger", StringComparison.OrdinalIgnoreCase)
            || dataType.Equals("AutoNumber", StringComparison.OrdinalIgnoreCase);

    private static bool IsDecimal(string dataType)
        => dataType.Equals("Decimal", StringComparison.OrdinalIgnoreCase)
            || dataType.Equals("Currency", StringComparison.OrdinalIgnoreCase);

    private static bool IsDateTime(string dataType)
        => dataType.Equals("DateTime", StringComparison.OrdinalIgnoreCase)
            || dataType.Equals("DateTime2", StringComparison.OrdinalIgnoreCase);

    private static bool IsDateOnly(string dataType)
        => dataType.Equals("Date", StringComparison.OrdinalIgnoreCase);

    private static bool IsTime(string dataType)
        => dataType.Equals("Time", StringComparison.OrdinalIgnoreCase);

    private static bool IsGuid(string dataType)
        => dataType.Equals("Guid", StringComparison.OrdinalIgnoreCase)
            || dataType.Equals("UniqueIdentifier", StringComparison.OrdinalIgnoreCase);
}

public sealed class SqlStaticEntityDataProvider : IStaticEntityDataProvider
{
    private readonly SqlConnectionFactory _connectionFactory;
    private readonly int? _commandTimeoutSeconds;

    public SqlStaticEntityDataProvider(SqlConnectionFactory connectionFactory, int? commandTimeoutSeconds)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _commandTimeoutSeconds = commandTimeoutSeconds;
    }

    public async Task<Result<IReadOnlyList<StaticEntityTableData>>> GetDataAsync(
        IReadOnlyList<StaticEntitySeedTableDefinition> definitions,
        CancellationToken cancellationToken = default)
    {
        if (definitions is null)
        {
            throw new ArgumentNullException(nameof(definitions));
        }

        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var results = new List<StaticEntityTableData>(definitions.Count);
            foreach (var definition in definitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var command = connection.CreateCommand();
                command.CommandText = BuildSelectStatement(definition);
                command.CommandType = CommandType.Text;
                if (_commandTimeoutSeconds.HasValue)
                {
                    command.CommandTimeout = _commandTimeoutSeconds.Value;
                }

                var rows = new List<StaticEntityRow>();
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var values = new object?[definition.Columns.Length];
                    for (var i = 0; i < definition.Columns.Length; i++)
                    {
                        var column = definition.Columns[i];
                        var rawValue = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        values[i] = column.NormalizeValue(rawValue);
                    }

                    rows.Add(StaticEntityRow.Create(values));
                }

                results.Add(StaticEntityTableData.Create(definition, rows));
            }

            return Result<IReadOnlyList<StaticEntityTableData>>.Success(results);
        }
        catch (DbException ex)
        {
            return Result<IReadOnlyList<StaticEntityTableData>>.Failure(ValidationError.Create(
                "cli.staticData.sql.failed",
                $"Failed to retrieve static entity data: {ex.Message}"));
        }
    }

    private static string BuildSelectStatement(StaticEntitySeedTableDefinition definition)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append("SELECT ");
        builder.Append(string.Join(", ", definition.Columns.Select(static c => FormatColumnName(c.ColumnName))));
        builder.Append(" FROM ");
        builder.Append(FormatTwoPartName(definition.Schema, definition.PhysicalName));

        var orderColumns = definition.Columns.Where(static c => c.IsPrimaryKey).Select(static c => c.ColumnName).ToArray();
        if (orderColumns.Length == 0 && definition.Columns.Length > 0)
        {
            orderColumns = new[] { definition.Columns[0].ColumnName };
        }

        if (orderColumns.Length > 0)
        {
            builder.Append(" ORDER BY ");
            builder.Append(string.Join(", ", orderColumns.Select(static name => FormatColumnName(name))));
        }

        return builder.ToString();
    }

    private static string FormatTwoPartName(string schema, string name)
        => $"[{schema.Replace("]", "]]", StringComparison.Ordinal)}].[{name.Replace("]", "]]", StringComparison.Ordinal)}]";

    private static string FormatColumnName(string name)
        => $"[{name.Replace("]", "]]", StringComparison.Ordinal)}]";
}
