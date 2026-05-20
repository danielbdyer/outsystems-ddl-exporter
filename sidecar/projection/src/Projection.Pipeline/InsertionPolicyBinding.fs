namespace Projection.Pipeline

open Projection.Core

/// Chapter C slice C.5 — operator-supplied insertion semantics.
/// `Policy.InsertionPolicy` is the four-variant closed DU at
/// `Projection.Core.Policy.fs` (`SchemaOnly | InsertNew | Merge |
/// TruncateAndInsert`); the operator picks one via the
/// `policy.insertion` config string. The binder maps the string to
/// the typed DU at config-bind time and surfaces structural errors on
/// unknown values.
///
/// **Wiring scope (this slice):** the binder lands + threads the
/// resulting `InsertionPolicy` into `Policy.Insertion`. Downstream
/// pass/emitter consumers (data emission, DataEmissionComposer) do
/// not yet read `Policy.Insertion` — the operator-facing config
/// surface lands now so hand-editing produces no surprises; consumer
/// wiring follows under concrete operator-pull pressure per
/// IR-grows-under-evidence. Today's effect is observable through the
/// bound `Policy` record (manifest emission; tests; downstream
/// surfaces that already consume `Policy`).
///
/// **Pillar 9 classification** — `InsertionPolicy` is the canonical
/// `OperatorIntent of Insertion` overlay (the V2-axis vocabulary maps
/// 1:1 to V1's Stage 6 InsertionStrategy per `DECISIONS 2026-05-19
/// (chapter B.4 hygiene strike + axis-survey supplement)` axis 9
/// revised).

[<RequireQualifiedAccess>]
module InsertionPolicyBinding =

    let private bindError (code: string) (message: string) : ValidationError =
        ValidationError.create (sprintf "pipeline.insertionPolicy.%s" code) message

    /// Map a textual config value to the typed DU. The recognized
    /// vocabulary is the closed-DU's case names verbatim; structural
    /// totality means unknown names surface as
    /// `pipeline.insertionPolicy.unknownVariant` before any consumer
    /// fires.
    let fromString (value: string) : Result<InsertionPolicy> =
        match value with
        | "SchemaOnly"        -> Result.success SchemaOnly
        | "InsertNew"         -> Result.success InsertNew
        | "Merge"             -> Result.success Merge
        | "TruncateAndInsert" -> Result.success TruncateAndInsert
        | other ->
            Result.failureOf (
                bindError
                    "unknownVariant"
                    (sprintf
                        "policy.insertion value '%s' is not a recognized InsertionPolicy. Known: SchemaOnly | InsertNew | Merge | TruncateAndInsert."
                        other))

    /// Bind from a parsed `Config`. Empty string at `policy.insertion`
    /// is treated as the default `SchemaOnly` (V2-driver Stage 6
    /// neutral default; operator must opt-in to data-emitting forms).
    let fromConfig (cfg: Config.Config) : Result<InsertionPolicy> =
        if System.String.IsNullOrEmpty cfg.Policy.Insertion then
            Result.success InsertionPolicy.empty
        else
            fromString cfg.Policy.Insertion
