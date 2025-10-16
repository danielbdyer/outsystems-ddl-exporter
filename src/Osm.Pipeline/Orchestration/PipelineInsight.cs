using System;
using System.Collections.Immutable;

namespace Osm.Pipeline.Orchestration;

public enum PipelineInsightSeverity
{
    Info,
    Advisory,
    Warning,
    Critical
}

public sealed record PipelineInsight
{
    public PipelineInsight(
        string code,
        string title,
        string summary,
        PipelineInsightSeverity severity,
        ImmutableArray<string> affectedObjects,
        string suggestedAction,
        string? documentationUri = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Insight code must be provided.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Insight title must be provided.", nameof(title));
        }

        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new ArgumentException("Insight summary must be provided.", nameof(summary));
        }

        Code = code;
        Title = title;
        Summary = summary;
        Severity = severity;
        AffectedObjects = affectedObjects.IsDefault ? ImmutableArray<string>.Empty : affectedObjects;
        SuggestedAction = string.IsNullOrWhiteSpace(suggestedAction)
            ? "No action required."
            : suggestedAction;
        DocumentationUri = string.IsNullOrWhiteSpace(documentationUri) ? null : documentationUri;
    }

    public string Code { get; }

    public string Title { get; }

    public string Summary { get; }

    public PipelineInsightSeverity Severity { get; }

    public ImmutableArray<string> AffectedObjects { get; }

    public string SuggestedAction { get; }

    public string? DocumentationUri { get; }
}
