using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Osm.Pipeline.Profiling;

namespace Osm.Json;

public interface IMultiEnvironmentProfileReportSerializer
{
    Task SerializeAsync(MultiEnvironmentProfileReport report, Stream destination, CancellationToken cancellationToken = default);
}

public sealed class MultiEnvironmentProfileReportSerializer : IMultiEnvironmentProfileReportSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task SerializeAsync(MultiEnvironmentProfileReport report, Stream destination, CancellationToken cancellationToken = default)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        if (destination is null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var document = new MultiEnvironmentProfileReportDocument
        {
            Environments = report.Environments.Select(MapEnvironmentSummary).ToArray(),
            Findings = report.Findings.Select(MapFinding).ToArray(),
            ConstraintConsensus = MapConstraintConsensus(report.ConstraintConsensus)
        };

        await JsonSerializer.SerializeAsync(destination, document, SerializerOptions, cancellationToken).ConfigureAwait(false);
    }

    private static ProfilingEnvironmentSummaryDocument MapEnvironmentSummary(ProfilingEnvironmentSummary summary)
    {
        return new ProfilingEnvironmentSummaryDocument
        {
            Name = summary.Name,
            IsPrimary = summary.IsPrimary,
            LabelOrigin = summary.LabelOrigin.ToString(),
            LabelWasAdjusted = summary.LabelWasAdjusted,
            ColumnCount = summary.ColumnCount,
            ColumnsWithNulls = summary.ColumnsWithNulls,
            ColumnsWithUnknownNullStatus = summary.ColumnsWithUnknownNullStatus,
            UniqueCandidateCount = summary.UniqueCandidateCount,
            UniqueViolations = summary.UniqueViolations,
            UniqueProbeUnknown = summary.UniqueProbeUnknown,
            CompositeUniqueCount = summary.CompositeUniqueCount,
            CompositeUniqueViolations = summary.CompositeUniqueViolations,
            ForeignKeyCount = summary.ForeignKeyCount,
            ForeignKeyOrphans = summary.ForeignKeyOrphans,
            ForeignKeyProbeUnknown = summary.ForeignKeyProbeUnknown,
            ForeignKeyNoCheck = summary.ForeignKeyNoCheck,
            Duration = summary.Duration
        };
    }

    private static MultiEnvironmentFindingDocument MapFinding(MultiEnvironmentFinding finding)
    {
        return new MultiEnvironmentFindingDocument
        {
            Code = finding.Code,
            Title = finding.Title,
            Summary = finding.Summary,
            Severity = finding.Severity,
            SuggestedAction = finding.SuggestedAction,
            AffectedObjects = finding.AffectedObjects.ToArray()
        };
    }

    private static MultiEnvironmentConstraintConsensusDocument MapConstraintConsensus(MultiEnvironmentConstraintConsensus consensus)
    {
        return new MultiEnvironmentConstraintConsensusDocument
        {
            NullabilityConsensus = consensus.NullabilityConsensus.Select(MapConsensusResult).ToArray(),
            UniqueConstraintConsensus = consensus.UniqueConstraintConsensus.Select(MapConsensusResult).ToArray(),
            ForeignKeyConsensus = consensus.ForeignKeyConsensus.Select(MapConsensusResult).ToArray(),
            Statistics = MapConsensusStatistics(consensus.Statistics)
        };
    }

    private static ConstraintConsensusResultDocument MapConsensusResult(ConstraintConsensusResult result)
    {
        return new ConstraintConsensusResultDocument
        {
            ConstraintType = result.ConstraintType,
            ConstraintDescriptor = result.ConstraintDescriptor,
            IsSafeToApply = result.IsSafeToApply,
            SafeEnvironmentCount = result.SafeEnvironmentCount,
            TotalEnvironmentCount = result.TotalEnvironmentCount,
            ConsensusRatio = result.ConsensusRatio,
            Recommendation = result.Recommendation
        };
    }

    private static ConsensusStatisticsDocument MapConsensusStatistics(ConsensusStatistics statistics)
    {
        return new ConsensusStatisticsDocument
        {
            TotalEnvironments = statistics.TotalEnvironments,
            SafeNotNullConstraints = statistics.SafeNotNullConstraints,
            UnsafeNotNullConstraints = statistics.UnsafeNotNullConstraints,
            SafeUniqueConstraints = statistics.SafeUniqueConstraints,
            UnsafeUniqueConstraints = statistics.UnsafeUniqueConstraints,
            SafeForeignKeyConstraints = statistics.SafeForeignKeyConstraints,
            UnsafeForeignKeyConstraints = statistics.UnsafeForeignKeyConstraints,
            TotalSafeConstraints = statistics.TotalSafeConstraints,
            TotalUnsafeConstraints = statistics.TotalUnsafeConstraints,
            TotalConstraints = statistics.TotalConstraints,
            SafetyRatio = statistics.SafetyRatio
        };
    }

    private sealed record MultiEnvironmentProfileReportDocument
    {
        [JsonPropertyName("environments")]
        public ProfilingEnvironmentSummaryDocument[] Environments { get; init; } = Array.Empty<ProfilingEnvironmentSummaryDocument>();

        [JsonPropertyName("findings")]
        public MultiEnvironmentFindingDocument[] Findings { get; init; } = Array.Empty<MultiEnvironmentFindingDocument>();

        [JsonPropertyName("constraintConsensus")]
        public MultiEnvironmentConstraintConsensusDocument ConstraintConsensus { get; init; } = new();
    }

    private sealed record ProfilingEnvironmentSummaryDocument
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("isPrimary")]
        public bool IsPrimary { get; init; }

        [JsonPropertyName("labelOrigin")]
        public string LabelOrigin { get; init; } = string.Empty;

        [JsonPropertyName("labelWasAdjusted")]
        public bool LabelWasAdjusted { get; init; }

        [JsonPropertyName("columnCount")]
        public int ColumnCount { get; init; }

        [JsonPropertyName("columnsWithNulls")]
        public int ColumnsWithNulls { get; init; }

        [JsonPropertyName("columnsWithUnknownNullStatus")]
        public int ColumnsWithUnknownNullStatus { get; init; }

        [JsonPropertyName("uniqueCandidateCount")]
        public int UniqueCandidateCount { get; init; }

        [JsonPropertyName("uniqueViolations")]
        public int UniqueViolations { get; init; }

        [JsonPropertyName("uniqueProbeUnknown")]
        public int UniqueProbeUnknown { get; init; }

        [JsonPropertyName("compositeUniqueCount")]
        public int CompositeUniqueCount { get; init; }

        [JsonPropertyName("compositeUniqueViolations")]
        public int CompositeUniqueViolations { get; init; }

        [JsonPropertyName("foreignKeyCount")]
        public int ForeignKeyCount { get; init; }

        [JsonPropertyName("foreignKeyOrphans")]
        public int ForeignKeyOrphans { get; init; }

        [JsonPropertyName("foreignKeyProbeUnknown")]
        public int ForeignKeyProbeUnknown { get; init; }

        [JsonPropertyName("foreignKeyNoCheck")]
        public int ForeignKeyNoCheck { get; init; }

        [JsonPropertyName("duration")]
        public TimeSpan Duration { get; init; }
    }

    private sealed record MultiEnvironmentFindingDocument
    {
        [JsonPropertyName("code")]
        public string Code { get; init; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; init; } = string.Empty;

        [JsonPropertyName("summary")]
        public string Summary { get; init; } = string.Empty;

        [JsonPropertyName("severity")]
        public MultiEnvironmentFindingSeverity Severity { get; init; }

        [JsonPropertyName("suggestedAction")]
        public string SuggestedAction { get; init; } = string.Empty;

        [JsonPropertyName("affectedObjects")]
        public string[] AffectedObjects { get; init; } = Array.Empty<string>();
    }

    private sealed record MultiEnvironmentConstraintConsensusDocument
    {
        [JsonPropertyName("nullabilityConsensus")]
        public ConstraintConsensusResultDocument[] NullabilityConsensus { get; init; } = Array.Empty<ConstraintConsensusResultDocument>();

        [JsonPropertyName("uniqueConstraintConsensus")]
        public ConstraintConsensusResultDocument[] UniqueConstraintConsensus { get; init; } = Array.Empty<ConstraintConsensusResultDocument>();

        [JsonPropertyName("foreignKeyConsensus")]
        public ConstraintConsensusResultDocument[] ForeignKeyConsensus { get; init; } = Array.Empty<ConstraintConsensusResultDocument>();

        [JsonPropertyName("statistics")]
        public ConsensusStatisticsDocument Statistics { get; init; } = new();
    }

    private sealed record ConstraintConsensusResultDocument
    {
        [JsonPropertyName("constraintType")]
        public ConstraintType ConstraintType { get; init; }

        [JsonPropertyName("constraintDescriptor")]
        public string ConstraintDescriptor { get; init; } = string.Empty;

        [JsonPropertyName("isSafeToApply")]
        public bool IsSafeToApply { get; init; }

        [JsonPropertyName("safeEnvironmentCount")]
        public int SafeEnvironmentCount { get; init; }

        [JsonPropertyName("totalEnvironmentCount")]
        public int TotalEnvironmentCount { get; init; }

        [JsonPropertyName("consensusRatio")]
        public double ConsensusRatio { get; init; }

        [JsonPropertyName("recommendation")]
        public string Recommendation { get; init; } = string.Empty;
    }

    private sealed record ConsensusStatisticsDocument
    {
        [JsonPropertyName("totalEnvironments")]
        public int TotalEnvironments { get; init; }

        [JsonPropertyName("safeNotNullConstraints")]
        public int SafeNotNullConstraints { get; init; }

        [JsonPropertyName("unsafeNotNullConstraints")]
        public int UnsafeNotNullConstraints { get; init; }

        [JsonPropertyName("safeUniqueConstraints")]
        public int SafeUniqueConstraints { get; init; }

        [JsonPropertyName("unsafeUniqueConstraints")]
        public int UnsafeUniqueConstraints { get; init; }

        [JsonPropertyName("safeForeignKeyConstraints")]
        public int SafeForeignKeyConstraints { get; init; }

        [JsonPropertyName("unsafeForeignKeyConstraints")]
        public int UnsafeForeignKeyConstraints { get; init; }

        [JsonPropertyName("totalSafeConstraints")]
        public int TotalSafeConstraints { get; init; }

        [JsonPropertyName("totalUnsafeConstraints")]
        public int TotalUnsafeConstraints { get; init; }

        [JsonPropertyName("totalConstraints")]
        public int TotalConstraints { get; init; }

        [JsonPropertyName("safetyRatio")]
        public double SafetyRatio { get; init; }
    }
}
