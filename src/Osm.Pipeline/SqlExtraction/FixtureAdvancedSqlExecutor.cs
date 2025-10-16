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

public sealed class FixtureAdvancedSqlExecutor : IAdvancedSqlExecutor
{
    private readonly IReadOnlyDictionary<string, FixtureEntry> _entries;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<FixtureAdvancedSqlExecutor> _logger;

    public FixtureAdvancedSqlExecutor(string manifestPath, IFileSystem? fileSystem = null, ILogger<FixtureAdvancedSqlExecutor>? logger = null)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            throw new ArgumentException("Manifest path must be provided.", nameof(manifestPath));
        }

        _fileSystem = fileSystem ?? new FileSystem();
        _logger = logger ?? NullLogger<FixtureAdvancedSqlExecutor>.Instance;

        var path = manifestPath.Trim();
        if (!_fileSystem.File.Exists(path))
        {
            throw new FileNotFoundException($"Advanced SQL fixture manifest '{path}' was not found.", path);
        }

        using var stream = _fileSystem.File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        _entries = LoadEntries(document.RootElement, Path.GetDirectoryName(path) ?? _fileSystem.Directory.GetCurrentDirectory());

        _logger.LogInformation(
            "Loaded advanced SQL fixture manifest from {ManifestPath} with {CaseCount} case(s).",
            path,
            _entries.Count);
    }

    public async Task<Result<long>> ExecuteAsync(
        AdvancedSqlRequest request,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (destination is null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        if (!destination.CanWrite)
        {
            throw new ArgumentException("Destination stream must be writable.", nameof(destination));
        }

        if (!destination.CanSeek)
        {
            throw new ArgumentException("Destination stream must support seeking.", nameof(destination));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var key = BuildKey(request.ModuleNames, request.IncludeSystemModules, request.OnlyActiveAttributes);
        _logger.LogInformation(
            "Resolving advanced SQL fixture for key {FixtureKey} (modules: {Modules}, includeSystem: {IncludeSystem}, onlyActive: {OnlyActive}).",
            key,
            string.Join(",", request.ModuleNames.Select(static module => module.Value)),
            request.IncludeSystemModules,
            request.OnlyActiveAttributes);

        if (!_entries.TryGetValue(key, out var entry))
        {
            _logger.LogWarning("Advanced SQL fixture entry not found for key {FixtureKey}.", key);
            var moduleList = string.Join(",", request.ModuleNames.Select(static module => module.Value));
            return Result<long>.Failure(ValidationError.Create(
                "extraction.fixture.missing",
                $"No fixture JSON registered for modules '{moduleList}', includeSystem={request.IncludeSystemModules}, onlyActive={request.OnlyActiveAttributes}."));
        }

        if (!_fileSystem.File.Exists(entry.JsonPath))
        {
            _logger.LogError("Advanced SQL fixture JSON '{JsonPath}' was not found on disk.", entry.JsonPath);
            return Result<long>.Failure(ValidationError.Create(
                "extraction.fixture.jsonMissing",
                $"Fixture JSON '{entry.JsonPath}' was not found."));
        }

        await using var sourceStream = _fileSystem.File.OpenRead(entry.JsonPath);
        if (sourceStream.Length == 0)
        {
            _logger.LogError("Advanced SQL fixture JSON '{JsonPath}' was empty.", entry.JsonPath);
            return Result<long>.Failure(ValidationError.Create(
                "extraction.fixture.jsonEmpty",
                $"Fixture JSON '{entry.JsonPath}' is empty."));
        }

        destination.SetLength(0);
        destination.Position = 0;
        await sourceStream.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);

        var bytesWritten = destination.Position;
        destination.Position = 0;

        if (bytesWritten == 0)
        {
            _logger.LogError("Advanced SQL fixture JSON '{JsonPath}' was empty after copy.", entry.JsonPath);
            return Result<long>.Failure(ValidationError.Create(
                "extraction.fixture.jsonEmpty",
                $"Fixture JSON '{entry.JsonPath}' is empty."));
        }

        _logger.LogInformation(
            "Resolved advanced SQL fixture JSON '{JsonPath}' with payload length {PayloadLength} bytes.",
            entry.JsonPath,
            bytesWritten);

        return Result<long>.Success(bytesWritten);
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
