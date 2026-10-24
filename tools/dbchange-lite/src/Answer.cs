using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DbChangeLite;

public sealed record Finding(string Code, string Severity, string Subject, string Message, string? Remedy)
{
    public static Finding Error(string code, string subject, string message, string remedy) => new(code, "error", subject, message, remedy);

    public static Finding Warning(string code, string subject, string message, string? remedy) => new(code, "warning", subject, message, remedy);

    public static Finding Note(string code, string subject, string message) => new(code, "note", subject, message, null);
}

public sealed class Stop(Outcome outcome, Finding finding) : Exception(finding.Message)
{
    public Outcome Outcome { get; } = outcome;

    public Finding Finding { get; } = finding;

    public static Stop Of(string word, string code, string subject, string message, string remedy) =>
        new(Contract.Of(word), Finding.Error(code, subject, message, remedy));
}

public sealed record Provenance(string? Change, string? Onto, string? Merged, string? PublishProfile, bool ProfileVerified, string At);

public sealed record Next(string? Command, string? By, string? Person, string? Question)
{
    public static Next Run(string command, string by) => new(command, by, null, null);

    public static Next Ask(string person, string question) => new(null, null, person, question);
}

public sealed record OpenItem(string Env, int? Level, string Why, Next Next);

public sealed record CheckAnswer(string Table, bool? Fires, string ReadBy, string Query, long? Count = null);

public sealed record PreconditionAnswer(string Kind, string Table, IReadOnlyList<string> Columns, bool? Violated, long? Count, string Query);

public sealed record PlanOperationAnswer(string Kind, string Type, string Name, string? Cause);

public sealed record PlanAnswer(IReadOnlyList<PlanOperationAnswer> Operations, IReadOnlyList<string> Rebuilds, IReadOnlyList<string> Drops,
    IReadOnlyList<PlanOperationAnswer> Unexplained);

public sealed record NotPredictableAnswer(string Why, string Part, int Line, string Statement);

public sealed record EnvironmentAnswer(string Env, int? Level, string? Assumed, string Outcome, string? BlockedBy, string? Baseline, string? Collation,
    string? NotRead, IReadOnlyList<CheckAnswer>? Checks, IReadOnlyList<PreconditionAnswer>? Preconditions, PlanAnswer? Plan,
    IReadOnlyList<NotPredictableAnswer>? NotPredictable, IReadOnlyList<string>? BeforePromoting)
{
    public static EnvironmentAnswer Unread(string env, string why) => new(env, 0, null, "unknown", null, null, null, why, null, null, null, null, null);

    public static EnvironmentAnswer Worst(string env, string outcome, string at, IReadOnlyList<string> beforePromoting) => new(env, null, "worst", outcome, null, at, null, null, null, null, null, null, beforePromoting);

    public EnvironmentAnswer Summary() => this with { Plan = Plan is null ? null : Plan with { Operations = [], Rebuilds = [], Drops = [] } };
}

public sealed record Answer(string Schema, string Outcome, int Exit, string Message, string? BlockedBy, IReadOnlyList<Finding> Findings, string Version,
    string Dacfx, string Pin, Provenance? Provenance, string? Confidence, IReadOnlyList<EnvironmentAnswer>? Environments, IReadOnlyList<OpenItem>? Open,
    bool Truncated, string? Full, string? Log, long? ElapsedMs)
{
    public const int SummaryLines = 400;

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public static Answer Of(string verb, Outcome outcome, string message, IReadOnlyList<Finding> findings, Run run) =>
        new("dbchange-lite." + verb + "/1", outcome.Word, outcome.Exit, message, null, findings, Contract.Version, DbChangeLite.DacFxRelease.Running, run.Pin, null, null, null,
            null, false, null, run.Log.Relative, null);

    public static Answer Stopped(string verb, Stop stop, Run run) => Of(verb, stop.Outcome, stop.Finding.Message, [stop.Finding], run);

    public static Answer Before(string schema, Stop stop) => new(schema, stop.Outcome.Word, stop.Outcome.Exit, stop.Finding.Message, null, [stop.Finding], Contract.Version,
        DacFxRelease.Running, "UNPINNED", null, null, null, null, false, null, null, null);

    public string Text() => JsonSerializer.Serialize(this, Json) + "\n";

    public Answer Written(Run run)
    {
        var elapsed = this with { ElapsedMs = run.ElapsedMs() };
        var text = elapsed.Text();
        File.WriteAllText(Path.Combine(run.Folder, "answer.json"), text);
        if (text.Count(c => c == '\n') <= SummaryLines)
        {
            return elapsed;
        }

        return elapsed with
        {
            Environments = elapsed.Environments?.Select(e => e.Summary()).ToList(),
            Truncated = true,
            Full = run.Relative(Path.Combine(run.Folder, "answer.json")),
        };
    }
}
