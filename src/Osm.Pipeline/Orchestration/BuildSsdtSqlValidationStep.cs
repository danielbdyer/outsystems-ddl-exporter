using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Osm.Domain.Abstractions;

namespace Osm.Pipeline.Orchestration;

public sealed class BuildSsdtSqlValidationStep : IBuildSsdtStep<SqlProjectSynthesized, SqlValidated>
{
    private readonly ISsdtSqlValidator _validator;

    public BuildSsdtSqlValidationStep()
        : this(new SsdtSqlValidator())
    {
    }

    public BuildSsdtSqlValidationStep(ISsdtSqlValidator validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<Result<SqlValidated>> ExecuteAsync(
        SqlProjectSynthesized state,
        CancellationToken cancellationToken = default)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var manifest = state.Manifest ?? throw new InvalidOperationException("Manifest must be available before SQL validation.");
        var summary = await _validator
            .ValidateAsync(state.Request.OutputDirectory, manifest.Tables, cancellationToken)
            .ConfigureAwait(false);

        RecordSummary(state, summary);

        if (summary.ErrorCount > 0)
        {
            RecordGroupedErrors(state, summary);
            return Result<SqlValidated>.Failure(ValidationError.Create(
                "pipeline.buildSsdt.sql.validationFailed",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Encountered {0} T-SQL parse error(s) across {1} emitted file(s).",
                    summary.ErrorCount,
                    summary.FilesWithErrors)));
        }

        return Result<SqlValidated>.Success(new SqlValidated(
            state.Request,
            state.Log,
            state.Bootstrap,
            state.EvidenceCache,
            state.Decisions,
            state.Report,
            state.Opportunities,
            state.Validations,
            state.Insights,
            manifest,
            state.DecisionLogPath,
            state.OpportunityArtifacts,
            state.SqlProjectPath,
            summary));
    }

    private static void RecordSummary(SqlProjectSynthesized state, SsdtSqlValidationSummary summary)
    {
        state.Log.Record(
            "ssdt.sql.validation.completed",
            "Validated emitted SQL scripts with ScriptDom.",
            new PipelineLogMetadataBuilder()
                .WithCount("validatedFiles", summary.TotalFiles)
                .WithCount("filesWithErrors", summary.FilesWithErrors)
                .WithCount("errorCount", summary.ErrorCount)
                .Build());
    }

    private static void RecordGroupedErrors(SqlProjectSynthesized state, SsdtSqlValidationSummary summary)
    {
        var groups = summary.Issues
            .SelectMany(issue => issue.Errors.Select(error => new { issue.Path, Error = error }))
            .GroupBy(
                item => (item.Error.Number, item.Error.Message),
                (key, items) => new
                {
                    key.Number,
                    key.Message,
                    Items = items.ToArray(),
                });

        foreach (var group in groups)
        {
            var sampleCount = Math.Min(3, group.Items.Length);
            var examples = group.Items
                .Take(sampleCount)
                .Select(item => string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}:{1}:{2}",
                    item.Path,
                    item.Error.Line,
                    item.Error.Column))
                .ToArray();

            var totalOccurrences = group.Items.Length;
            var builder = new PipelineLogMetadataBuilder()
                .WithValue("error.code", group.Number.ToString(CultureInfo.InvariantCulture))
                .WithValue("error.message", group.Message)
                .WithCount("error.occurrences", totalOccurrences)
                .WithValue("error.examples", string.Join(" | ", examples));

            var suppressed = totalOccurrences - sampleCount;
            if (suppressed > 0)
            {
                builder.WithCount("error.suppressed", suppressed);
            }

            state.Log.Record(
                "ssdt.sql.validation.error",
                "ScriptDom parse failure detected.",
                builder.Build());
        }
    }
}
