using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using DbChange.Kernel;

namespace DbChange.Io;

/// <summary>
/// predict's machine block (WP 2.5, V3_MILESTONES.md §3): the fenced block that ends predict's Markdown, its info string
/// dbchange.prediction/1, one line of JSON per environment holding the environment, its existing data as dbchange.existing-data/1 writes
/// it, the counts its states came from, and the claim's provenance. The gate (M5) reads every such block of a pull request's description
/// with <see cref="Read"/>, in the order they stand, so a developer's block for Dev and a lead's for QA and UAT read together. A block is
/// held to what predict writes: each line one JSON object of those four keys, its provenance about env:&lt;the environment&gt; and naming
/// the existing data's own fingerprint, so an existing data edited by hand is refused; each environment once in a block; and every fence
/// closed. Anything else is prediction.malformed, naming the block's line and quoting nothing of it.
/// </summary>
public static class PredictionBlock
{
    /// <summary>The machine block's info string, after its opening fence.</summary>
    public const string Schema = "dbchange.prediction/1";

    private const string Fence = "```";

    private const string Time = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    private static readonly string[] Keys = ["environment", "existingData", "counts", "provenance"];

    private static readonly string[] ProvenanceKeys =
        ["change", "schema", "existingData", "dacfx", "server", "publishProfile", "target", "at"];

    /// <summary>
    /// predict's Markdown, the block a developer or a lead pastes into the pull request: its label, predicted · read-only, the time every
    /// claim records and the DacFx release; the change, by its two commits; a line per environment, saying whether it blocks, applies, is
    /// not predicted whole or was not read, and why, with what was not answered and the baseline drift beneath it; and, last, the machine
    /// block of the environments predicted.
    /// </summary>
    public static IReadOnlyList<string> Markdown(Predict.Answer answer, DacFxVersion dacfx) =>
    [
        "#### predicted · read-only · " + answer.Time.UtcDateTime.ToString(Time, CultureInfo.InvariantCulture) + " · DacFx " + dacfx, "",
        "The change from commit " + answer.Base[..8] + " to commit " + answer.Commit[..8] + " (ref:" + answer.At
            + "), planned against each environment as it stands:", "",
        .. answer.Environments.SelectMany(e => e.Predicted.Match(p => Said(e.Name, p), error => ["- env:" + e.Name + " is not predicted: "
            + error.Message])),
        "",
        .. Machine(answer.Environments.Select(e => e.Predicted).OfType<Result<Predict.Predicted>.Ok>().Select(p => p.Value.Prediction)),
    ];

    /// <summary>What an environment predicts, in a line and the lines beneath it.</summary>
    private static IEnumerable<string> Said(EnvironmentName name, Predict.Predicted p) =>
    [
        "- env:" + name + " " + p.Outcome.Match(
            _ => p.Whole ? "applies." : "is not predicted whole: what its counts answer applies, and a proof decides the rest.",
            blocks => "blocks: " + string.Join("; ", blocks.Because.Select(s => Because(p, s))) + "; it ships " + blocks.Shipping.Match(
                () => "in two releases.", operation => "as " + operation + ".", () => "as a proof on a copy decides.")),
        .. p.Unanswered.Select(u => "  - unanswered: " + u.Precondition + ": " + u.Why),
        .. p.Unclassified.Select(s => "  - unclassified: " + Vocabulary.Where(s) + ", " + s.Type),
        .. p.Outcome.BaselineDrift.Count == 0 ? [] : new[]
        {
            "  - baseline drift, what env:" + name + "'s plan does that the change does not: "
                + string.Join(", ", p.Outcome.BaselineDrift.Select(o => o.Kind.Word + " " + o.Key)),
        },
    ];

    /// <summary>Why a state stops the change: the table BlockOnPossibleDataLoss finds rows in, or the rows that break a rule.</summary>
    private static string Because(Predict.Predicted p, PreconditionState state) => p.Count(state.Precondition) is not { } count
        ? state.Precondition + " stops it"
        : state.Precondition.Kind is PreconditionKind.TableEmpty
            ? "BlockOnPossibleDataLoss fires on " + state.Precondition.Table + ", which holds " + Rows(count.Rows)
            : count.Violating.ToString("N0", CultureInfo.InvariantCulture) + " of its " + Rows(count.Rows) + " would violate "
                + state.Precondition;

    private static string Rows(long rows) => rows.ToString("N0", CultureInfo.InvariantCulture) + (rows == 1 ? " row" : " rows");

    /// <summary>The machine block's lines: its opening fence, one line of JSON per prediction, and its closing fence.</summary>
    public static IReadOnlyList<string> Machine(IEnumerable<Prediction> predictions) =>
        [Fence + Schema, .. predictions.Select(p => Json.Line(new JsonObject
        {
            ["environment"] = p.Environment.ToString(),
            ["existingData"] = JsonNode.Parse(ExistingDataFile.Text(p.ExistingData)),
            ["counts"] = new JsonArray([.. p.Counts.Select(ExistingDataFile.CountJson)]),
            ["provenance"] = ProvenanceJson(p.Provenance),
        })), Fence];

    /// <summary>A claim's provenance as the machine block writes it: each fingerprint as sha256: and its digits, the DacFx release, the
    /// SQL Server or null, the target and the time in whole seconds, UTC.</summary>
    public static JsonObject ProvenanceJson(Provenance p) => new()
    {
        ["change"] = Digest(p.Change), ["schema"] = Digest(p.Schema), ["existingData"] = p.ExistingData is { } data ? Digest(data) : null,
        ["dacfx"] = p.DacFx.ToString(),
        ["server"] = p.Server is { } s ? new JsonObject
        {
            ["version"] = s.Version, ["edition"] = s.Edition, ["compatibilityLevel"] = s.CompatibilityLevel,
        } : null,
        ["publishProfile"] = Digest(p.PublishProfile), ["target"] = p.Target.ToString(),
        ["at"] = p.At.UtcDateTime.ToString(Time, CultureInfo.InvariantCulture),
    };

    /// <summary>The predictions of every machine block <paramref name="text"/> holds, block by block in the order they stand; none for a
    /// text that holds no block; or prediction.malformed.</summary>
    public static Result<IReadOnlyList<Prediction>> Read(string text)
    {
        var lines = LineEndings.Lf(text).Split('\n');
        var read = new List<Prediction>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim() != Fence + Schema)
            {
                continue;
            }

            var (opened, block) = (i, new List<Prediction>());
            for (i++; i < lines.Length && lines[i].Trim() != Fence; i++)
            {
                if (lines[i].Trim().Length == 0)
                {
                    continue;   // a description's editor may leave a blank line inside the block
                }

                if (Line(lines[i], i - opened + 1).Failed(out var prediction, out var error))
                {
                    return error;
                }

                if (block.Any(p => p.Environment == prediction.Environment))
                {
                    return Malformed("the machine block predicts env:" + prediction.Environment + " twice");
                }

                block.Add(prediction);
            }

            if (i == lines.Length)
            {
                return Malformed("a machine block opened at line " + (opened + 1).ToString(CultureInfo.InvariantCulture)
                    + " is never closed");
            }

            read.AddRange(block);
        }

        return Result.Ok<IReadOnlyList<Prediction>>(read);
    }

    /// <summary>One line of a block, the block's line <paramref name="number"/>, counted from its opening fence.</summary>
    private static Result<Prediction> Line(string line, int number)
    {
        var at = "line " + number.ToString(CultureInfo.InvariantCulture) + " of the machine block";
        JsonObject json;
        try
        {
            json = JsonNode.Parse(line, documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false }) as JsonObject
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            return Malformed(at + " is no JSON object");
        }

        if (json.Select(p => p.Key).Order(StringComparer.Ordinal).SequenceEqual(Keys.Order(StringComparer.Ordinal)) is false)
        {
            return Malformed(at + " holds a key the form does not name, or lacks one of environment, existingData, counts and provenance");
        }

        return (json["environment"] is JsonValue name && name.TryGetValue(out string? environment)
                ? EnvironmentName.Of(at + "'s environment", environment)
                : Result.Fail<EnvironmentName>(Malformed(at + " names no environment")))
            .Bind(environment => (json["existingData"] is JsonObject data ? ExistingDataFile.Parse(data.ToJsonString(), at)
                    : Result.Fail<ExistingData>(Malformed(at + "'s existingData is no JSON object")))
                .Bind(existing => ExistingDataFile.Counts(json["counts"], at, "$.counts")
                .Bind(counts => ProvenanceOf(json["provenance"], at)
                .Bind(provenance => provenance.Target != new Target.Environment(environment)
                    ? Result.Fail<Prediction>(Malformed(at + " holds a provenance about " + provenance.Target
                        + ", and its environment is env:" + environment))
                    : provenance.ExistingData != ExistingDataFile.FingerprintOf(existing)
                        ? Result.Fail<Prediction>(Malformed(at + " holds an existing data whose fingerprint its provenance does not name"))
                    : Result.Ok(new Prediction(environment, existing, counts, provenance))))))
            .Match(Result.Ok, error => error.Code == "prediction.malformed" ? error
                : Malformed(at + " holds no prediction of the form predict writes: " + error.Message));
    }

    /// <summary>A provenance as <see cref="ProvenanceJson"/> writes it.</summary>
    private static Result<Provenance> ProvenanceOf(JsonNode? node, string at)
    {
        if (node is not JsonObject json
            || !json.Select(p => p.Key).Order(StringComparer.Ordinal).SequenceEqual(ProvenanceKeys.Order(StringComparer.Ordinal)))
        {
            return Malformed(at + "'s provenance is no JSON object of change, schema, existingData, dacfx, server, publishProfile, target "
                + "and at");
        }

        string? Text(string key) => json[key] is JsonValue value && value.TryGetValue(out string? text) ? text : null;
        Result<Fingerprint> Digested(string key) => Text(key) is { } text && text.StartsWith("sha256:", StringComparison.Ordinal)
            ? Fingerprint.Parse(text["sha256:".Length..])
            : Result.Fail<Fingerprint>(Malformed(at + "'s provenance gives " + key + " as no sha256: fingerprint"));
        var server = json["server"] is not JsonObject s ? Result.Ok<Server?>(null)
            : Kernel.Server.Of(s["version"] is JsonValue v && v.TryGetValue(out string? version) ? version : null,
                s["edition"] is JsonValue e && e.TryGetValue(out string? edition) ? edition : null,
                s["compatibilityLevel"] is JsonValue l && l.TryGetValue(out int level) ? level : 0).Map(read => (Server?)read);
        return Digested("change").Bind(change => Digested("schema").Bind(schema => Digested("existingData").Bind(existing =>
            Digested("publishProfile").Bind(profile => DacFxVersion.Of(Text("dacfx")).Bind(dacfx => server.Bind(read =>
            Target.Parse(Text("target") ?? "", at + "'s target").Bind(target =>
                DateTimeOffset.TryParseExact(Text("at"), Time, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var when)
                    ? Result.Ok(Kernel.Provenance.Prediction(change, schema, existing, dacfx, read, profile, target, when))
                    : Malformed(at + "'s provenance gives its time in no form of " + Time))))))));
    }

    private static string Digest(Fingerprint fingerprint) => "sha256:" + fingerprint;

    private static Error Malformed(string what) => new("prediction.malformed",
        what + "; the gate takes nothing from a block that predict did not write as it stands.",
        "Paste the block predict printed again, unedited, or run dbchange predict for the environment.");
}
