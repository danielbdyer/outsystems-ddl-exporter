namespace Projection.Targets.OperationalDiagnostics

// LINT-ALLOW-FILE: estate overlay emission (wave A6) — an operator
//   artifact at a terminal emission boundary: the overlay JSON composes
//   through the BCL's typed JsonObject builder (never text), and the
//   probes batch joins pre-composed probe SQL lines with comment framing
//   (the RemediationEmitter precedent).

open System.Text.Json
open System.Text.Json.Nodes
open Projection.Core

/// `estate.overlay.json` + `estate.probes.sql` — the interim posture's two
/// sibling projections (wave A6; π-coherence: both project the SAME
/// `Relaxation` list the report's RELAX-lane proposals resolved to, keyed
/// by the finding). The overlay entry's `value` is EXACTLY the
/// intervention entry `TighteningBinding` binds — every emitted key binds
/// and reaches emission (the A44 enforcement law); the id IS the finding's
/// cross-artifact key, so the board, the overlay, and the probes say one
/// name. The merge is an operator edit; the engine never applies it.
[<RequireQualifiedAccess>]
module EstateOverlayEmitter =

    let private evidenceText (evidence: (string * int64) list) : string =
        evidence
        |> List.map (fun (env, n) ->
            sprintf "%s in %s" (n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)) env)
        |> String.concat "; "

    let private entryOf (relaxation: Relaxation) : JsonObject =
        let key = FindingKey.text relaxation.Scope
        let value = JsonObject()
        value.["id"] <- JsonValue.Create key
        (match relaxation.Action with
         | RelaxationAction.KeepUntracked referenceRef ->
             value.["kind"] <- JsonValue.Create "foreignKey"
             let overrides = JsonArray()
             let o = JsonObject()
             o.["referenceRef"] <- JsonValue.Create referenceRef
             o.["action"] <- JsonValue.Create "keepUntracked"
             overrides.Add o
             value.["referenceOverrides"] <- overrides
         | RelaxationAction.KeepNullable attributeRef ->
             value.["kind"] <- JsonValue.Create "nullability"
             let overrides = JsonArray()
             let o = JsonObject()
             o.["attributeRef"] <- JsonValue.Create attributeRef
             o.["action"] <- JsonValue.Create "keepNullable"
             overrides.Add o
             value.["overrides"] <- overrides)
        let entry = JsonObject()
        entry.["findingKey"] <- JsonValue.Create key
        // The readable face the board's RELAX lever names ("Merge the
        // relaxation for <subject> (<phrase>)…"), so the operator finds
        // this entry in plain words; `findingKey` stays the machine token.
        entry.["subject"] <- JsonValue.Create (FindingKey.readableLabel relaxation.Scope)
        entry.["path"] <- JsonValue.Create "$.policy.tightening.interventions[+]"
        entry.["value"] <- value
        entry.["note"] <-
            JsonValue.Create(
                sprintf "Interim relaxation; the evidence that forced it: %s. The reopen probe retires it at zero."
                    (evidenceText relaxation.Evidence))
        entry.["reopenProbe"] <- JsonValue.Create relaxation.ReopenProbe
        entry

    /// The overlay artifact body. Each suggested edit appends one
    /// intervention entry under `policy.tightening.interventions`; the
    /// probe rides beside its entry so neither file orphans the other.
    /// Relaxed escaping: this is an OPERATOR-read artifact — the default
    /// encoder renders the plus sign and the notes' punctuation as
    /// unicode escapes, which would make the suggested path unreadable
    /// at the merge.
    let emitOverlay (generatedNote: string) (relaxations: Relaxation list) : string =
        let root = JsonObject()
        root.["note"] <- JsonValue.Create generatedNote
        let edits = JsonArray()
        for r in relaxations do edits.Add(entryOf r)
        root.["suggestedEdits"] <- edits
        root.ToJsonString(
            JsonSerializerOptions(
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping))

    /// The probes artifact body — every reopen probe as one runnable
    /// batch, each led by its finding key (the posture's retirement
    /// meter). Empty input renders the said-empty comment, never an empty
    /// file.
    let emitProbes (headerLines: string list) (relaxations: Relaxation list) : string =
        let lines =
            if List.isEmpty relaxations then
                [ "-- No proposed relaxations this run; the posture has no probes to carry." ]
            else
                relaxations
                |> List.collect (fun r ->
                    [ sprintf "-- probe %s" (FindingKey.text r.Scope)
                      r.ReopenProbe
                      "" ])
        System.String.Join("\n", headerLines @ [ "" ] @ lines)
