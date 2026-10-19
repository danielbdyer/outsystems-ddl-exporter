using System;

namespace Osm.Validation.Tightening;

public sealed record ChangeRisk(RiskLevel Level, string Label, string Description)
{
    public static ChangeRisk Create(RiskLevel level, string label, string description)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("Label must be provided.", nameof(label));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Description must be provided.", nameof(description));
        }

        return new ChangeRisk(level, label, description);
    }

    public static ChangeRisk Unknown(string description)
        => Create(RiskLevel.Unknown, "Unknown", description);

    public static ChangeRisk Low(string description)
        => Create(RiskLevel.Low, "Low", description);

    public static ChangeRisk Moderate(string description)
        => Create(RiskLevel.Moderate, "Moderate", description);

    public static ChangeRisk High(string description)
        => Create(RiskLevel.High, "High", description);
}
