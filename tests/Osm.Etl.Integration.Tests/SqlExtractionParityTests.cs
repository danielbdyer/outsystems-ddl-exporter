using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Osm.Domain.ValueObjects;
using Osm.Json;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;
using Xunit;

namespace Osm.Etl.Integration.Tests;

[CollectionDefinition("SqlServerCollection")]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
}

[Collection("SqlServerCollection")]
public sealed class SqlExtractionParityTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly SqlServerFixture _fixture;
    private readonly ModelJsonSerializerBridge _serializer = new();

    public SqlExtractionParityTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ExtractModel_ShouldMatchEdgeCaseFixture()
    {
        var connectionFactory = new SqlConnectionFactory(_fixture.DatabaseConnectionString);
        var metadataReader = new SqlClientOutsystemsMetadataReader(connectionFactory, new EmbeddedOutsystemsMetadataScriptProvider());
        var extractionService = new SqlModelExtractionService(
            new AdvancedSqlMetadataOrchestrator(metadataReader),
            new SnapshotJsonBuilder(),
            new SnapshotValidator(),
            new ModelDeserializerFacade(new ModelJsonDeserializer()));
        var command = ModelExtractionCommand.Create(Array.Empty<string>(), includeSystemModules: false, includeInactiveModules: true, onlyActiveAttributes: false).Value;

        var extraction = await extractionService.ExtractAsync(command).ConfigureAwait(false);
        Assert.True(extraction.IsSuccess, string.Join(Environment.NewLine, extraction.Errors.Select(error => $"{error.Code}: {error.Message}")));

        var actualJson = await _serializer.SerializeAsync(extraction.Value, CancellationToken.None).ConfigureAwait(false);
        var expectedJson = await File.ReadAllTextAsync(Path.GetFullPath("tests/Fixtures/model.edge-case.json")).ConfigureAwait(false);

        var canonicalActual = Canonicalize(actualJson);
        var canonicalExpected = Canonicalize(expectedJson);

        var actualHash = ComputeHash(canonicalActual);
        var expectedHash = ComputeHash(canonicalExpected);

        Assert.Equal(expectedHash, actualHash);
    }

    [Fact]
    public async Task ExtractModel_WithInvalidPassword_ShouldReturnMetadataFailure()
    {
        var builder = new SqlConnectionStringBuilder(_fixture.DatabaseConnectionString)
        {
            Password = "WrongPassword!123"
        };

        var connectionFactory = new SqlConnectionFactory(builder.ConnectionString);
        var metadataReader = new SqlClientOutsystemsMetadataReader(connectionFactory, new EmbeddedOutsystemsMetadataScriptProvider());
        var extractionService = new SqlModelExtractionService(
            new AdvancedSqlMetadataOrchestrator(metadataReader),
            new SnapshotJsonBuilder(),
            new SnapshotValidator(),
            new ModelDeserializerFacade(new ModelJsonDeserializer()));
        var command = ModelExtractionCommand.Create(Array.Empty<string>(), includeSystemModules: false, includeInactiveModules: true, onlyActiveAttributes: false).Value;

        var result = await extractionService.ExtractAsync(command).ConfigureAwait(false);

        Assert.True(result.IsFailure, "Expected extraction to fail when using invalid credentials.");
        var error = Assert.Single(result.Errors);
        Assert.Equal("extraction.metadata.executionFailed", error.Code);
        Assert.Contains("login failed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdvancedSqlExecutor_ShouldSurfaceTimeoutMessage()
    {
        var connectionFactory = new SqlConnectionFactory(_fixture.DatabaseConnectionString);
        var executor = new SqlClientAdvancedSqlExecutor(
            connectionFactory,
            new InlineScriptProvider("WAITFOR DELAY '00:00:05'; SELECT 1;"),
            new SqlExecutionOptions(1, SqlSamplingOptions.Default));

        await using var destination = new MemoryStream();
        var request = new AdvancedSqlRequest(ImmutableArray<ModuleName>.Empty, includeSystemModules: false, includeInactiveModules: true, onlyActiveAttributes: false);
        var result = await executor.ExecuteAsync(request, destination, CancellationToken.None).ConfigureAwait(false);

        Assert.True(result.IsFailure, "Expected advanced SQL execution to time out.");
        var error = Assert.Single(result.Errors);
        Assert.Equal("extraction.sql.executionFailed", error.Code);
        Assert.Contains("timeout", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdvancedSqlExecutor_ShouldPopulateSamplingParameters()
    {
        var connectionFactory = new SqlConnectionFactory(_fixture.DatabaseConnectionString);
        const long expectedThreshold = 1_234_567;
        const int expectedSampleSize = 98765;
        var sampling = new SqlSamplingOptions(expectedThreshold, expectedSampleSize);
        var executor = new SqlClientAdvancedSqlExecutor(
            connectionFactory,
            new InlineScriptProvider(
                """
                DECLARE @payload NVARCHAR(MAX) =
                    N'{"threshold":' + CONVERT(NVARCHAR(30), @RowSamplingThreshold) +
                    N',"sampleSize":' + CONVERT(NVARCHAR(30), @SampleSize) + N'}';
                SELECT @payload;
                """),
            new SqlExecutionOptions(null, sampling));

        await using var destination = new MemoryStream();
        var request = new AdvancedSqlRequest(ImmutableArray<ModuleName>.Empty, includeSystemModules: false, includeInactiveModules: true, onlyActiveAttributes: false);
        var result = await executor.ExecuteAsync(request, destination, CancellationToken.None).ConfigureAwait(false);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Errors.Select(error => $"{error.Code}: {error.Message}")));

        destination.Position = 0;
        using var reader = new StreamReader(destination, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var payload = await reader.ReadToEndAsync().ConfigureAwait(false);

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.Equal(expectedThreshold, root.GetProperty("threshold").GetInt64());
        Assert.Equal(expectedSampleSize, root.GetProperty("sampleSize").GetInt32());
    }

    private static string Canonicalize(string json)
    {
        var node = JsonNode.Parse(json) ?? throw new InvalidOperationException("JSON payload could not be parsed.");
        var path = new List<string>();
        CanonicalizeNode(node, path);
        NormalizeValues(node);
        return node.ToJsonString(SerializerOptions);
    }

    private static void CanonicalizeNode(JsonNode? node, List<string> path)
    {
        if (node is null)
        {
            return;
        }

        switch (node)
        {
            case JsonObject obj:
            {
                var ordered = obj.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToList();

                obj.Clear();
                foreach (var (key, value) in ordered)
                {
                    path.Add(key);
                    CanonicalizeNode(value, path);
                    obj[key] = value;
                    path.RemoveAt(path.Count - 1);
                }
                break;
            }
            case JsonArray array:
            {
                path.Add("[]");
                foreach (var element in array)
                {
                    CanonicalizeNode(element, path);
                }

                SortArray(array, string.Join('.', path));
                path.RemoveAt(path.Count - 1);
                break;
            }
        }
    }

    private static void SortArray(JsonArray array, string key)
    {
        Comparison<JsonNode?>? comparison = key switch
        {
            "modules[]" => CompareByString("name"),
            "modules[].entities[]" => CompareByString("name"),
            "modules[].entities[].attributes[]" => CompareByAttribute(),
            "modules[].entities[].indexes[]" => CompareByString("name"),
            "modules[].entities[].indexes[].columns[]" => CompareByIntThenString("ordinal", "attribute"),
            "modules[].entities[].indexes[].partitionColumns[]" => CompareByInt("ordinal"),
            "modules[].entities[].indexes[].dataCompression[]" => CompareByInt("partition"),
            "modules[].entities[].relationships[]" => CompareByString("viaAttributeName"),
            "modules[].entities[].relationships[].actualConstraints[]" => CompareByString("name"),
            "modules[].entities[].relationships[].actualConstraints[].columns[]" => CompareByIntThenString("ordinal", "owner.attribute"),
            "modules[].entities[].triggers[]" => CompareByString("name"),
            _ => null
        };

        if (comparison is null)
        {
            return;
        }

        var items = array.ToList();
        items.Sort(comparison);
        array.Clear();
        foreach (var item in items)
        {
            array.Add(item);
        }
    }

    private static Comparison<JsonNode?> CompareByString(string property)
    {
        return (left, right) => string.Compare(
            GetPropertyString(left, property),
            GetPropertyString(right, property),
            StringComparison.Ordinal);
    }

    private static Comparison<JsonNode?> CompareByAttribute()
    {
        return (left, right) =>
        {
            var leftName = GetPropertyString(left, "name");
            var rightName = GetPropertyString(right, "name");
            return string.Compare(leftName, rightName, StringComparison.Ordinal);
        };
    }

    private static Comparison<JsonNode?> CompareByInt(string property)
    {
        return (left, right) => GetPropertyInt(left, property).CompareTo(GetPropertyInt(right, property));
    }

    private static Comparison<JsonNode?> CompareByIntThenString(string intProperty, string stringProperty)
    {
        return (left, right) =>
        {
            var comparison = GetPropertyInt(left, intProperty).CompareTo(GetPropertyInt(right, intProperty));
            if (comparison != 0)
            {
                return comparison;
            }

            return string.Compare(
                GetPropertyString(left, stringProperty),
                GetPropertyString(right, stringProperty),
                StringComparison.Ordinal);
        };
    }

    private static string? GetPropertyString(JsonNode? node, string property)
    {
        return node is JsonObject obj && obj.TryGetPropertyValue(property, out var value)
            ? value?.GetValue<string?>()
            : null;
    }

    private static int GetPropertyInt(JsonNode? node, string property)
    {
        if (node is JsonObject obj && obj.TryGetPropertyValue(property, out var value) && value is not null)
        {
            if (value is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var number))
            {
                return number;
            }

            if (int.TryParse(value.ToString(), out number))
            {
                return number;
            }
        }

        return int.MaxValue;
    }

    private static void NormalizeValues(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("fill_factor", out var fillFactor) && fillFactor is JsonValue value && value.TryGetValue<int>(out var number) && number == 0)
            {
                obj["fill_factor"] = null;
            }

            foreach (var property in obj.ToList())
            {
                if (property.Value is not null)
                {
                    NormalizeValues(property.Value);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var element in array)
            {
                if (element is not null)
                {
                    NormalizeValues(element);
                }
            }
        }
    }

    private static string ComputeHash(string canonicalJson)
    {
        var bytes = Encoding.UTF8.GetBytes(canonicalJson);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private sealed class ModelJsonSerializerBridge
    {
        public async Task<string> SerializeAsync(ModelExtractionResult extraction, CancellationToken cancellationToken)
        {
            if (extraction is null)
            {
                throw new ArgumentNullException(nameof(extraction));
            }

            return await extraction.JsonPayload.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class InlineScriptProvider : IAdvancedSqlScriptProvider
    {
        private readonly string _script;

        public InlineScriptProvider(string script)
        {
            _script = script ?? throw new ArgumentNullException(nameof(script));
        }

        public string GetScript() => _script;
    }
}
