using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;
using Osm.Domain.Configuration;
using Osm.Json.Configuration;

namespace Osm.Pipeline.Configuration;

internal sealed class TighteningSectionReader
{
    private readonly ITighteningOptionsDeserializer _deserializer;

    public TighteningSectionReader(ITighteningOptionsDeserializer deserializer)
    {
        _deserializer = deserializer ?? throw new ArgumentNullException(nameof(deserializer));
    }

    public async Task<Result<TighteningSectionReadResult>> ReadAsync(JsonElement root, string baseDirectory, string configPath)
    {
        if (IsLegacyDocument(root))
        {
            await using var stream = File.OpenRead(configPath);
            var legacyResult = _deserializer.Deserialize(stream);
            if (legacyResult.IsFailure)
            {
                return Result<TighteningSectionReadResult>.Failure(legacyResult.Errors);
            }

            return new TighteningSectionReadResult(true, legacyResult.Value);
        }

        TighteningOptions? options = null;

        if (TryResolveTighteningPath(root, baseDirectory, out var path, out var pathError))
        {
            await using var stream = File.OpenRead(path);
            var fromFile = _deserializer.Deserialize(stream);
            if (fromFile.IsFailure)
            {
                return Result<TighteningSectionReadResult>.Failure(fromFile.Errors);
            }

            options = fromFile.Value;
        }
        else if (pathError is not null)
        {
            return Result<TighteningSectionReadResult>.Failure(pathError.Value);
        }

        if (TryReadTighteningInline(root, out var inlineElement))
        {
            using var buffer = new MemoryStream(Encoding.UTF8.GetBytes(inlineElement.GetRawText()));
            var inlineResult = _deserializer.Deserialize(buffer);
            if (inlineResult.IsFailure)
            {
                return Result<TighteningSectionReadResult>.Failure(inlineResult.Errors);
            }

            options = inlineResult.Value;
        }

        return new TighteningSectionReadResult(false, options);
    }

    private static bool IsLegacyDocument(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("policy", out _);
    }

    private static bool TryResolveTighteningPath(JsonElement root, string baseDirectory, out string resolvedPath, out ValidationError? error)
    {
        resolvedPath = string.Empty;
        error = null;

        if (!root.TryGetProperty("tighteningPath", out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var raw = element.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        resolvedPath = ConfigurationJsonHelpers.ResolveRelativePath(baseDirectory, raw);
        if (!File.Exists(resolvedPath))
        {
            error = ValidationError.Create(
                "cli.config.tighteningPath.missing",
                $"Tightening configuration '{resolvedPath}' was not found.");
            return false;
        }

        return true;
    }

    private static bool TryReadTighteningInline(JsonElement root, out JsonElement inlineElement)
    {
        inlineElement = default;
        if (!root.TryGetProperty("tightening", out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        inlineElement = element;
        return true;
    }
}

internal readonly record struct TighteningSectionReadResult(bool IsLegacyDocument, TighteningOptions? Options);
