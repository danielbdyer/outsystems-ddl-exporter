namespace Projection.Targets.OperationalDiagnostics

open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open Projection.Core

/// H-032 — `SuggestConfigEmitter`: materializes the `SuggestedConfig`
/// payloads from a diagnostic stream into an operator-facing JSON
/// patch document.
///
/// **Use case.** After running `osm diagnose`, the operator runs
/// `osm suggest-config` to receive a JSON document containing every
/// config edit that would address the current findings. The operator
/// reviews and applies the patches to their config file, then
/// re-runs the pipeline.
///
/// **Deduplication contract.** Multiple findings may suggest the same
/// `Path`. For example, ten columns in the same intervention all
/// failing `MandatoryButHasNullsBeyondBudget` will each suggest a
/// `nullBudget` raise on the same intervention path. The emitter
/// keeps the **maximum `Value`** per `Path` (for numeric budgets:
/// the strictest suggestion that satisfies all callers) rather than
/// emitting duplicates. This produces the tightest config patch that
/// silences all suggestions for a given path.
///
/// **Pillar 9 classification.** Pure `DataIntent` derivation — the
/// suggestions derive entirely from the finding's statistical evidence
/// (carried by `SuggestedConfig.Value`). No operator opinion is
/// introduced; the operator decides which suggestions to apply.
///
/// **Output shape (JSON).**
/// ```json
/// {
///   "suggestedEdits": [
///     {
///       "path":   "$.tightening.interventions[?(@.id==\"my-id\")].nullBudget",
///       "value":  "0.05",
///       "note":   "Raises nullBudget to the observed null fraction; ...",
///       "sources": ["tightening.nullability.requireOperatorApproval"]
///     },
///     ...
///   ]
/// }
/// ```
/// Entries are sorted by `path` for byte-determinism.
[<RequireQualifiedAccess>]
module SuggestConfigEmitter =

    /// One deduplicated suggestion: the `Path` + winning `Value` + the
    /// `Note` from the suggestion that contributed the winning value +
    /// the set of diagnostic codes that contributed to this path.
    type private Suggestion = {
        Path    : string
        Value   : string
        Note    : string option
        Sources : string list
    }

    /// Collect and deduplicate suggestions from a diagnostic stream.
    /// For each unique `Path`, keep the entry whose `Value` is
    /// lexicographically greatest (numeric decimal strings sort
    /// correctly for budget values). Collect all `Code` values that
    /// suggested the same path into `Sources`.
    let private collect (diagnostics: DiagnosticEntry list) : Suggestion list =
        diagnostics
        |> List.choose (fun e ->
            match e.SuggestedConfig with
            | None     -> None
            | Some cfg -> Some (cfg.Path, cfg.Value, cfg.Note, e.Code))
        |> List.groupBy (fun (path, _, _, _) -> path)
        |> List.sortBy fst
        |> List.map (fun (path, entries) ->
            let winning = entries |> List.maxBy (fun (_, v, _, _) -> v)
            let (_, winningValue, winningNote, _) = winning
            let sources =
                entries
                |> List.map (fun (_, _, _, code) -> code)
                |> List.distinct
                |> List.sort
            { Path    = path
              Value   = winningValue
              Note    = winningNote
              Sources = sources })

    let private writeSuggestion (w: Utf8JsonWriter) (s: Suggestion) : unit =
        w.WriteStartObject()
        w.WriteString("path",  s.Path)
        w.WriteString("value", s.Value)
        match s.Note with
        | Some n -> w.WriteString("note", n)
        | None   -> ()
        w.WritePropertyName("sources")
        w.WriteStartArray()
        for src in s.Sources do
            w.WriteStringValue(src)
        w.WriteEndArray()
        w.WriteEndObject()

    /// Emit a JSON patch document containing all deduplicated config-
    /// edit suggestions from the diagnostic stream. Returns an empty
    /// `suggestedEdits` array when no entry carries `SuggestedConfig`.
    ///
    /// The output is suitable for operator review and cherry-pick
    /// merging into the V2 config file.
    let emit (diagnostics: DiagnosticEntry list) : JsonNode =
        let suggestions = collect diagnostics
        use stream = new MemoryStream()
        do
            use writer = new Utf8JsonWriter(stream, JsonOptions.compact ())
            writer.WriteStartObject()
            writer.WritePropertyName("suggestedEdits")
            writer.WriteStartArray()
            for s in suggestions do
                writeSuggestion writer s
            writer.WriteEndArray()
            writer.WriteEndObject()
            writer.Flush()
        let bytes = stream.ToArray()
        match JsonNode.Parse(System.ReadOnlySpan<byte>(bytes)) with
        | null -> invalidOp "SuggestConfigEmitter.emit: produced unparseable JSON (unreachable)"
        | n    -> n
