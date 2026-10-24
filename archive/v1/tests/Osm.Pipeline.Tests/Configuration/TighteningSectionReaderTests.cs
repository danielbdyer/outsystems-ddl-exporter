using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Json.Configuration;
using Osm.Pipeline.Configuration;

namespace Osm.Pipeline.Tests.Configuration;

public sealed class TighteningSectionReaderTests
{
    [Fact]
    public async Task ReadAsync_WhenDocumentContainsPolicy_TreatsConfigurationAsLegacy()
    {
        var expectedOptions = CreateOptions(TighteningMode.Cautious);
        var deserializer = new StubDeserializer(Result<TighteningOptions>.Success(expectedOptions));
        var reader = new TighteningSectionReader(deserializer);
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
        await File.WriteAllTextAsync(tempPath, "{\"policy\":{}}", Encoding.UTF8);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(tempPath, Encoding.UTF8));
        var result = await reader.ReadAsync(document.RootElement, Path.GetDirectoryName(tempPath)!, tempPath);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsLegacyDocument.Should().BeTrue();
        result.Value.Options.Should().Be(expectedOptions);
        deserializer.CapturedPayloads.Should().ContainSingle(payload => payload.Contains("\"policy\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadAsync_WhenTighteningPathResolves_DeserializesOptionsFromFile()
    {
        var expectedOptions = CreateOptions(TighteningMode.EvidenceGated);
        var deserializer = new StubDeserializer(Result<TighteningOptions>.Success(expectedOptions));
        var reader = new TighteningSectionReader(deserializer);
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var tighteningPath = Path.Combine(directory, "tightening.json");
        await File.WriteAllTextAsync(tighteningPath, "{}", Encoding.UTF8);

        var json = $"{{ \"tighteningPath\": \"{Path.GetFileName(tighteningPath)}\" }}";
        using var document = JsonDocument.Parse(json);

        var result = await reader.ReadAsync(document.RootElement, directory, Path.Combine(directory, "config.json"));

        result.IsSuccess.Should().BeTrue();
        result.Value.IsLegacyDocument.Should().BeFalse();
        result.Value.Options.Should().Be(expectedOptions);
        deserializer.CapturedPayloads.Should().ContainSingle(payload => payload == "{}");
    }

    [Fact]
    public async Task ReadAsync_WhenInlineSectionPresent_OverridesFileOptions()
    {
        var fileOptions = CreateOptions(TighteningMode.Cautious);
        var inlineOptions = CreateOptions(TighteningMode.Aggressive);
        var deserializer = new StubDeserializer(
            Result<TighteningOptions>.Success(fileOptions),
            Result<TighteningOptions>.Success(inlineOptions));
        var reader = new TighteningSectionReader(deserializer);
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var tighteningPath = Path.Combine(directory, "tightening.json");
        await File.WriteAllTextAsync(tighteningPath, "{}", Encoding.UTF8);

        const string InlineSection = "\"tightening\": { }";
        var json = $"{{ \"tighteningPath\": \"{Path.GetFileName(tighteningPath)}\", {InlineSection} }}";
        using var document = JsonDocument.Parse(json);

        var result = await reader.ReadAsync(document.RootElement, directory, Path.Combine(directory, "config.json"));

        result.IsSuccess.Should().BeTrue();
        result.Value.IsLegacyDocument.Should().BeFalse();
        result.Value.Options.Should().Be(inlineOptions);
        deserializer.CapturedPayloads.Should().HaveCount(2);
        deserializer.CapturedPayloads[1].Replace(" ", string.Empty).Should().Be("{}");
    }

    [Fact]
    public async Task ReadAsync_WhenTighteningPathMissing_ReturnsValidationError()
    {
        var deserializer = new StubDeserializer();
        var reader = new TighteningSectionReader(deserializer);
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var json = "{ \"tighteningPath\": \"missing.json\" }";
        using var document = JsonDocument.Parse(json);

        var result = await reader.ReadAsync(document.RootElement, directory, Path.Combine(directory, "config.json"));

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(error => error.Code == "cli.config.tighteningPath.missing");
        deserializer.CapturedPayloads.Should().BeEmpty();
    }

    private static TighteningOptions CreateOptions(TighteningMode mode)
    {
        return TighteningOptions.Create(
            PolicyOptions.Create(mode, 0.5).Value,
            ForeignKeyOptions.Create(enableCreation: true, allowCrossSchema: false, allowCrossCatalog: false).Value,
            UniquenessOptions.Create(true, true).Value,
            RemediationOptions.Create(true, RemediationSentinelOptions.Create("1", "sentinel", "2020-01-01").Value, 10).Value,
            EmissionOptions.Create(true, false, true, TableEmissionMode.FullOnly, false, 1, namingOverrides: NamingOverrideOptions.Empty, staticSeeds: StaticSeedOptions.Default).Value,
            MockingOptions.Create(false, null).Value).Value;
    }

    private sealed class StubDeserializer : ITighteningOptionsDeserializer
    {
        private readonly Queue<Result<TighteningOptions>> _results;

        public StubDeserializer(params Result<TighteningOptions>[] results)
        {
            _results = new Queue<Result<TighteningOptions>>(results);
        }

        public List<string> CapturedPayloads { get; } = new();

        public Result<TighteningOptions> Deserialize(Stream jsonStream)
        {
            using var reader = new StreamReader(jsonStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            CapturedPayloads.Add(reader.ReadToEnd());

            if (_results.Count == 0)
            {
                throw new InvalidOperationException("No result configured for deserializer invocation.");
            }

            return _results.Dequeue();
        }
    }
}
