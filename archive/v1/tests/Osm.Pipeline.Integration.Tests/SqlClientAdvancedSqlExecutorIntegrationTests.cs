using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Osm.Domain.ValueObjects;
using Osm.Pipeline.Sql;
using Osm.Pipeline.SqlExtraction;
using Osm.TestSupport;
using Xunit;

namespace Osm.Pipeline.Integration.Tests;

[CollectionDefinition("SqlServerCollection")]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
}

[Collection("SqlServerCollection")]
public sealed class SqlClientAdvancedSqlExecutorIntegrationTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly SqlServerFixture _fixture;

    public SqlClientAdvancedSqlExecutorIntegrationTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    public static IEnumerable<object[]> GetAdvancedSqlCases()
    {
        var manifestPath = Path.GetFullPath("tests/Fixtures/extraction/advanced-sql.manifest.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var baseDirectory = Path.GetDirectoryName(manifestPath) ?? Directory.GetCurrentDirectory();

        foreach (var element in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            var modules = element.GetProperty("modules").EnumerateArray()
                .Select(property => property.GetString() ?? string.Empty)
                .ToArray();
            var includeSystem = element.GetProperty("includeSystemModules").GetBoolean();
            var onlyActive = element.GetProperty("onlyActiveAttributes").GetBoolean();
            var jsonPath = element.GetProperty("jsonPath").GetString() ?? string.Empty;
            var resolvedJsonPath = Path.GetFullPath(Path.Combine(baseDirectory, jsonPath));
            yield return new object[]
            {
                new AdvancedSqlCase(modules, includeSystem, onlyActive, resolvedJsonPath)
            };
        }
    }

    [DockerTheory]
    [MemberData(nameof(GetAdvancedSqlCases))]
    public async Task ExecuteAsync_ShouldMatchAdvancedSqlFixture(AdvancedSqlCase testCase)
    {
        var moduleNames = ImmutableArray.CreateBuilder<ModuleName>(testCase.Modules.Count);
        foreach (var module in testCase.Modules)
        {
            var moduleResult = ModuleName.Create(module);
            moduleResult.IsSuccess.Should().BeTrue();
            moduleNames.Add(moduleResult.Value);
        }

        var connectionFactory = new SqlConnectionFactory(_fixture.DatabaseConnectionString);
        var executor = new SqlClientAdvancedSqlExecutor(
            connectionFactory,
            new EmbeddedAdvancedSqlScriptProvider());

        await using var destination = new MemoryStream();
        var request = new AdvancedSqlRequest(
            moduleNames.ToImmutable(),
            includeSystemModules: testCase.IncludeSystemModules,
            includeInactiveModules: true,
            onlyActiveAttributes: testCase.OnlyActiveAttributes);

        var result = await executor.ExecuteAsync(request, destination, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        destination.Position.Should().Be(result.Value);
        destination.Position = 0;

        using var reader = new StreamReader(destination, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var payload = await reader.ReadToEndAsync();
        payload.Should().NotBeNullOrWhiteSpace();

        var expected = await File.ReadAllTextAsync(testCase.ExpectedJsonPath);
        Canonicalize(payload).Should().Be(Canonicalize(expected));
    }

    [DockerFact]
    public async Task ExecuteAsync_ShouldHonorCommandTimeout()
    {
        var connectionFactory = new SqlConnectionFactory(_fixture.DatabaseConnectionString);
        var executor = new SqlClientAdvancedSqlExecutor(
            connectionFactory,
            new InlineScriptProvider("WAITFOR DELAY '00:00:03'; SELECT 1;"),
            new SqlExecutionOptions(1, SqlSamplingOptions.Default));

        await using var destination = new MemoryStream();
        var request = new AdvancedSqlRequest(ImmutableArray<ModuleName>.Empty, includeSystemModules: false, includeInactiveModules: true, onlyActiveAttributes: false);

        var result = await executor.ExecuteAsync(request, destination, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(error =>
            error.Code == "extraction.sql.executionFailed"
            && error.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase));
    }

    private static string Canonicalize(string json)
    {
        var node = JsonNode.Parse(json);
        if (node is null)
        {
            return string.Empty;
        }

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
            case JsonArray array:
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

    private static void SortArray(JsonArray array, string key)
    {
        Comparison<JsonNode?>? comparison = key switch
        {
            "modules[]" => CompareByString("name"),
            "modules[].entities[]" => CompareByString("name"),
            "modules[].entities[].attributes[]" => CompareByString("name"),
            "modules[].entities[].indexes[]" => CompareByString("name"),
            "modules[].entities[].indexes[].columns[]" => CompareByIntThenString("ordinal", "attribute"),
            "modules[].entities[].indexes[].partitionColumns[]" => CompareByInt("ordinal"),
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
            return value.GetValue<int>();
        }

        return 0;
    }

    private static void NormalizeValues(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var pair in obj.ToList())
                {
                    if (pair.Value is null)
                    {
                        continue;
                    }

                    NormalizeValues(pair.Value);
                }
                break;
            case JsonArray array:
                foreach (var element in array)
                {
                    if (element is not null)
                    {
                        NormalizeValues(element);
                    }
                }
                break;
        }
    }

    public sealed record AdvancedSqlCase(
        IReadOnlyList<string> Modules,
        bool IncludeSystemModules,
        bool OnlyActiveAttributes,
        string ExpectedJsonPath);

    private sealed class InlineScriptProvider : IAdvancedSqlScriptProvider
    {
        private readonly string _script;

        public InlineScriptProvider(string script)
        {
            _script = script;
        }

        public string GetScript() => _script;
    }
}
