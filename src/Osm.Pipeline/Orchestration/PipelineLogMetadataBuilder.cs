using System;
using System.Collections.Generic;
using System.Globalization;

namespace Osm.Pipeline.Orchestration;

public sealed class PipelineLogMetadataBuilder
{
    private readonly Dictionary<string, string?> _metadata = new(StringComparer.Ordinal);

    public PipelineLogMetadataBuilder WithCount(string name, int value)
    {
        return WithCount(name, (long)value);
    }

    public PipelineLogMetadataBuilder WithCount(string name, long value)
    {
        var key = ComposeKey("counts", name);
        _metadata[key] = value.ToString(CultureInfo.InvariantCulture);
        return this;
    }

    public PipelineLogMetadataBuilder WithCount(string name, double value)
    {
        var key = ComposeKey("counts", name);
        _metadata[key] = value.ToString("0.###", CultureInfo.InvariantCulture);
        return this;
    }

    public PipelineLogMetadataBuilder WithFlag(string name, bool value)
    {
        var key = ComposeKey("flags", name);
        _metadata[key] = value ? "true" : "false";
        return this;
    }

    public PipelineLogMetadataBuilder WithPath(string name, string? value)
    {
        var key = ComposeKey("paths", name);
        _metadata[key] = value;
        return this;
    }

    public PipelineLogMetadataBuilder WithTimestamp(string name, DateTimeOffset value)
    {
        var key = ComposeKey("timestamps", name);
        _metadata[key] = value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        return this;
    }

    public PipelineLogMetadataBuilder WithMetric(string name, double value)
    {
        var key = ComposeKey("metrics", name);
        _metadata[key] = value.ToString("0.###", CultureInfo.InvariantCulture);
        return this;
    }

    public PipelineLogMetadataBuilder WithMetric(string name, decimal value)
    {
        var key = ComposeKey("metrics", name);
        _metadata[key] = value.ToString("0.###", CultureInfo.InvariantCulture);
        return this;
    }

    public PipelineLogMetadataBuilder WithValue(string key, string? value)
    {
        var normalized = NormalizeKey(key);
        _metadata[normalized] = value;
        return this;
    }

    public PipelineLogMetadataBuilder WithOptionalValue(string key, string? value)
    {
        var normalized = NormalizeKey(key);
        if (!string.IsNullOrEmpty(value))
        {
            _metadata[normalized] = value;
        }
        else
        {
            _metadata[normalized] = value;
        }

        return this;
    }

    public IReadOnlyDictionary<string, string?> Build()
    {
        return new Dictionary<string, string?>(_metadata, StringComparer.Ordinal);
    }

    private static string ComposeKey(string category, string name)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            throw new ArgumentException("Category must be provided.", nameof(category));
        }

        var normalizedName = NormalizeKey(name);
        return string.Create(category.Length + 1 + normalizedName.Length, (category, normalizedName), static (span, state) =>
        {
            state.category.AsSpan().CopyTo(span);
            span[state.category.Length] = '.';
            state.normalizedName.AsSpan().CopyTo(span[(state.category.Length + 1)..]);
        });
    }

    private static string NormalizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key must be provided.", nameof(key));
        }

        var trimmed = key.Trim();
        foreach (var ch in trimmed)
        {
            if (!char.IsLetterOrDigit(ch) && ch != '.' && ch != '_' && ch != '-')
            {
                throw new ArgumentException($"Metadata key '{key}' contains invalid character '{ch}'.", nameof(key));
            }
        }

        if (trimmed.StartsWith(".", StringComparison.Ordinal) || trimmed.EndsWith(".", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Metadata key '{key}' cannot start or end with a '.'.", nameof(key));
        }

        return trimmed;
    }
}
