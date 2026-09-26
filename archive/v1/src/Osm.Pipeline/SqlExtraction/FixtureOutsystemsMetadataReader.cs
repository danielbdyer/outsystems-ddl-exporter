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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Osm.Domain.Abstractions;
using Osm.Domain.ValueObjects;

namespace Osm.Pipeline.SqlExtraction;

public sealed class FixtureOutsystemsMetadataReader : IOutsystemsMetadataReader
{
    private readonly IReadOnlyDictionary<string, FixtureEntry> _entries;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<FixtureOutsystemsMetadataReader> _logger;

    public FixtureOutsystemsMetadataReader(string manifestPath, IFileSystem? fileSystem = null, ILogger<FixtureOutsystemsMetadataReader>? logger = null)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            throw new ArgumentException("Manifest path must be provided.", nameof(manifestPath));
        }

        _fileSystem = fileSystem ?? new FileSystem();
        _logger = logger ?? NullLogger<FixtureOutsystemsMetadataReader>.Instance;

        var path = manifestPath.Trim();
        if (!_fileSystem.File.Exists(path))
        {
            throw new FileNotFoundException($"Metadata fixture manifest '{path}' was not found.", path);
        }

        using var stream = _fileSystem.File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        _entries = LoadEntries(document.RootElement, Path.GetDirectoryName(path) ?? _fileSystem.Directory.GetCurrentDirectory());

        _logger.LogInformation(
            "Loaded metadata fixture manifest from {ManifestPath} with {CaseCount} case(s).",
            path,
            _entries.Count);
    }

    public Task<Result<OutsystemsMetadataSnapshot>> ReadAsync(AdvancedSqlRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var key = BuildKey(request.ModuleNames, request.IncludeSystemModules, request.IncludeInactiveModules, request.OnlyActiveAttributes);
        _logger.LogInformation(
            "Resolving metadata fixture for key {FixtureKey} (modules: {Modules}, includeSystem: {IncludeSystem}, includeInactive: {IncludeInactive}, onlyActive: {OnlyActive}).",
            key,
            string.Join(",", request.ModuleNames.Select(static module => module.Value)),
            request.IncludeSystemModules,
            request.IncludeInactiveModules,
            request.OnlyActiveAttributes);

        if (!_entries.TryGetValue(key, out var entry))
        {
            _logger.LogWarning("Metadata fixture entry not found for key {FixtureKey}.", key);
            return Task.FromResult(Result<OutsystemsMetadataSnapshot>.Failure(ValidationError.Create(
                "extraction.fixture.missing",
                $"No metadata fixture registered for modules '{string.Join(",", request.ModuleNames.Select(static module => module.Value))}', includeSystem={request.IncludeSystemModules}, includeInactive={request.IncludeInactiveModules}, onlyActive={request.OnlyActiveAttributes}.")));
        }

        if (!_fileSystem.File.Exists(entry.JsonPath))
        {
            _logger.LogError("Metadata fixture JSON '{JsonPath}' was not found on disk.", entry.JsonPath);
            return Task.FromResult(Result<OutsystemsMetadataSnapshot>.Failure(ValidationError.Create(
                "extraction.fixture.jsonMissing",
                $"Fixture JSON '{entry.JsonPath}' was not found.")));
        }

        using var jsonStream = _fileSystem.File.OpenRead(entry.JsonPath);
        using var document = JsonDocument.Parse(jsonStream);

        if (!document.RootElement.TryGetProperty("modules", out var modulesElement) || modulesElement.ValueKind != JsonValueKind.Array)
        {
            _logger.LogError("Fixture JSON '{JsonPath}' is missing a modules array.", entry.JsonPath);
            return Task.FromResult(Result<OutsystemsMetadataSnapshot>.Failure(ValidationError.Create(
                "extraction.fixture.invalid",
                $"Fixture JSON '{entry.JsonPath}' is missing a modules array.")));
        }

        var moduleJsonRows = new List<OutsystemsModuleJsonRow>();
        foreach (var moduleElement in modulesElement.EnumerateArray())
        {
            var name = moduleElement.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
            var isSystem = moduleElement.TryGetProperty("isSystem", out var systemElement) && systemElement.GetBoolean();
            var isActive = moduleElement.TryGetProperty("isActive", out var activeElement) ? activeElement.GetBoolean() : true;
            var entitiesJson = moduleElement.TryGetProperty("entities", out var entitiesElement)
                ? entitiesElement.GetRawText()
                : "[]";

            moduleJsonRows.Add(new OutsystemsModuleJsonRow(name, isSystem, isActive, entitiesJson));
        }

        var snapshot = new OutsystemsMetadataSnapshot(
            Array.Empty<OutsystemsModuleRow>(),
            Array.Empty<OutsystemsEntityRow>(),
            Array.Empty<OutsystemsAttributeRow>(),
            Array.Empty<OutsystemsReferenceRow>(),
            Array.Empty<OutsystemsPhysicalTableRow>(),
            Array.Empty<OutsystemsColumnRealityRow>(),
            Array.Empty<OutsystemsColumnCheckRow>(),
            Array.Empty<OutsystemsColumnCheckJsonRow>(),
            Array.Empty<OutsystemsPhysicalColumnPresenceRow>(),
            Array.Empty<OutsystemsIndexRow>(),
            Array.Empty<OutsystemsIndexColumnRow>(),
            Array.Empty<OutsystemsForeignKeyRow>(),
            Array.Empty<OutsystemsForeignKeyColumnRow>(),
            Array.Empty<OutsystemsForeignKeyAttrMapRow>(),
            Array.Empty<OutsystemsAttributeHasFkRow>(),
            Array.Empty<OutsystemsForeignKeyColumnsJsonRow>(),
            Array.Empty<OutsystemsForeignKeyAttributeJsonRow>(),
            Array.Empty<OutsystemsTriggerRow>(),
            Array.Empty<OutsystemsAttributeJsonRow>(),
            Array.Empty<OutsystemsRelationshipJsonRow>(),
            Array.Empty<OutsystemsIndexJsonRow>(),
            Array.Empty<OutsystemsTriggerJsonRow>(),
            moduleJsonRows,
            "Fixture");

        return Task.FromResult(Result<OutsystemsMetadataSnapshot>.Success(snapshot));
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
            var includeInactive = element.TryGetProperty("includeInactiveModules", out var includeInactiveElement)
                ? includeInactiveElement.GetBoolean()
                : true;

            var key = BuildKey(normalizedModules, includeSystem, includeInactive, onlyActive);
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

    private static string BuildKey(IEnumerable<ModuleName> modules, bool includeSystem, bool includeInactive, bool onlyActive)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"modules:{string.Join("|", modules.Select(static module => module.Value))}|system:{includeSystem}|inactive:{includeInactive}|active:{onlyActive}");
    }

    private sealed record FixtureEntry(string JsonPath, ImmutableArray<ModuleName> Modules);
}
