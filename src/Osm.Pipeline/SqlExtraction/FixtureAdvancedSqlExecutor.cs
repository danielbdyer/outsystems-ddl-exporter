using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.ValueObjects;

namespace Osm.Pipeline.SqlExtraction;

public sealed class FixtureAdvancedSqlExecutor : IAdvancedSqlExecutor
{
    private readonly IReadOnlyDictionary<string, FixtureEntry> _entries;
    private readonly IFileSystem _fileSystem;

    public FixtureAdvancedSqlExecutor(string manifestPath, IFileSystem? fileSystem = null)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            throw new ArgumentException("Manifest path must be provided.", nameof(manifestPath));
        }

        _fileSystem = fileSystem ?? new FileSystem();
        var path = manifestPath.Trim();
        if (!_fileSystem.File.Exists(path))
        {
            throw new FileNotFoundException($"Advanced SQL fixture manifest '{path}' was not found.", path);
        }

        using var stream = _fileSystem.File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        _entries = LoadEntries(document.RootElement, Path.GetDirectoryName(path) ?? _fileSystem.Directory.GetCurrentDirectory());
    }

    public Task<Result<string>> ExecuteAsync(AdvancedSqlRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var key = BuildKey(request.ModuleNames, request.IncludeSystemModules, request.OnlyActiveAttributes);
        if (!_entries.TryGetValue(key, out var entry))
        {
            return Task.FromResult(Result<string>.Failure(ValidationError.Create(
                "extraction.fixture.missing",
                $"No fixture JSON registered for modules '{string.Join(",", request.ModuleNames.Select(static module => module.Value))}', includeSystem={request.IncludeSystemModules}, onlyActive={request.OnlyActiveAttributes}.")));
        }

        if (!_fileSystem.File.Exists(entry.JsonPath))
        {
            return Task.FromResult(Result<string>.Failure(ValidationError.Create(
                "extraction.fixture.jsonMissing",
                $"Fixture JSON '{entry.JsonPath}' was not found.")));
        }

        using var reader = new StreamReader(_fileSystem.File.OpenRead(entry.JsonPath));
        var json = reader.ReadToEnd();
        if (string.IsNullOrWhiteSpace(json))
        {
            return Task.FromResult(Result<string>.Failure(ValidationError.Create(
                "extraction.fixture.jsonEmpty",
                $"Fixture JSON '{entry.JsonPath}' is empty.")));
        }

        return Task.FromResult(Result<string>.Success(json));
    }

    private IReadOnlyDictionary<string, FixtureEntry> LoadEntries(JsonElement root, string baseDirectory)
    {
        if (!root.TryGetProperty("cases", out var casesElement) || casesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Fixture manifest must contain an array property 'cases'.");
        }

        var entries = new Dictionary<string, FixtureEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in casesElement.EnumerateArray())
        {
            var modules = element.TryGetProperty("modules", out var modulesElement) && modulesElement.ValueKind == JsonValueKind.Array
                ? modulesElement.EnumerateArray().Select(m => m.GetString() ?? string.Empty).ToArray()
                : Array.Empty<string>();
            var includeSystem = element.TryGetProperty("includeSystemModules", out var includeElement) && includeElement.GetBoolean();
            var onlyActive = element.TryGetProperty("onlyActiveAttributes", out var onlyActiveElement) && onlyActiveElement.GetBoolean();
            if (!element.TryGetProperty("jsonPath", out var jsonPathElement) || jsonPathElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("Each fixture case must include a 'jsonPath'.");
            }

            var normalizedModules = NormalizeModules(modules);

            var key = BuildKey(normalizedModules, includeSystem, onlyActive);
            var jsonPath = jsonPathElement.GetString() ?? string.Empty;
            if (jsonPath.Length == 0)
            {
                throw new InvalidDataException("Fixture jsonPath must be a non-empty string.");
            }

            var absolute = Path.IsPathRooted(jsonPath)
                ? jsonPath
                : Path.GetFullPath(Path.Combine(baseDirectory, jsonPath));

            entries[key] = new FixtureEntry(absolute, normalizedModules);
        }

        return entries;
    }

    private static ImmutableArray<ModuleName> NormalizeModules(IEnumerable<string> modules)
    {
        var builder = ImmutableArray.CreateBuilder<ModuleName>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in modules)
        {
            if (string.IsNullOrWhiteSpace(module))
            {
                continue;
            }

            var trimmed = module.Trim();
            var result = ModuleName.Create(trimmed);
            if (result.IsFailure)
            {
                var details = string.Join(", ", result.Errors.Select(static error => error.Message));
                throw new InvalidDataException($"Fixture module '{trimmed}' is invalid: {details}.");
            }

            var moduleName = result.Value;
            if (seen.Add(moduleName.Value))
            {
                builder.Add(moduleName);
            }
        }

        var materialized = builder.ToImmutable();
        if (!materialized.IsDefaultOrEmpty)
        {
            materialized = materialized.Sort(Comparer<ModuleName>.Create(static (left, right)
                => string.Compare(left.Value, right.Value, StringComparison.OrdinalIgnoreCase)));
        }

        return materialized;
    }

    private static string BuildKey(IEnumerable<ModuleName> modules, bool includeSystem, bool onlyActive)
    {
        return string.Create(CultureInfo.InvariantCulture, $"modules:{string.Join("|", modules.Select(static module => module.Value))}|system:{includeSystem}|active:{onlyActive}");
    }

    private sealed record FixtureEntry(string JsonPath, ImmutableArray<ModuleName> Modules);
}
