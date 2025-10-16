using System.Collections.Generic;
using System.Collections.Immutable;
using Osm.Domain.Abstractions;

namespace Osm.Domain.Profiling.Insights;

public sealed record ProfileInsight(
    string Code,
    string Title,
    string Detail,
    ProfileInsightSeverity Severity,
    ProfileInsightAnchor Anchor,
    ImmutableDictionary<string, string> Metadata)
{
    public static Result<ProfileInsight> Create(
        string? code,
        string? title,
        string? detail,
        ProfileInsightSeverity severity,
        ProfileInsightAnchor? anchor = null,
        IEnumerable<KeyValuePair<string, string?>>? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return ValidationError.Create("profiling.insight.code.missing", "Insight code must be provided.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return ValidationError.Create("profiling.insight.title.missing", "Insight title must be provided.");
        }

        if (string.IsNullOrWhiteSpace(detail))
        {
            return ValidationError.Create("profiling.insight.detail.missing", "Insight detail must be provided.");
        }

        var metadataBuilder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        if (metadata is not null)
        {
            foreach (var pair in metadata)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    return ValidationError.Create("profiling.insight.metadata.key.invalid", "Metadata keys must be provided when metadata is supplied.");
                }

                if (!string.IsNullOrWhiteSpace(pair.Value))
                {
                    metadataBuilder[pair.Key] = pair.Value!;
                }
            }
        }

        return Result<ProfileInsight>.Success(new ProfileInsight(
            code.Trim(),
            title.Trim(),
            detail.Trim(),
            severity,
            anchor ?? ProfileInsightAnchor.None,
            metadataBuilder.ToImmutable()));
    }
}
