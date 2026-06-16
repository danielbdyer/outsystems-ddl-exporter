namespace Projection.Pipeline

open Projection.Core
open FsToolkit.ErrorHandling

/// Chapter C slice C.2 — operator-supplied acknowledgements that
/// source-side defects (missing primary keys; unresolved circular
/// FK dependencies) are known + accepted. The binder resolves the
/// textual config refs against the loaded `Catalog` into typed
/// `SsKey` sets consumed by the downstream diagnostics annotator
/// (`SpecialCircumstancesDiagnostics`).
///
/// **Annotate-don't-suppress discipline** (slice-6 reshape lesson;
/// codified DECISIONS 2026-05-20 — special-circumstances axis).
/// Allowlisted findings still surface in the diagnostic stream; the
/// annotator stamps `Metadata.acceptedVia` on matches so downstream
/// operator surfaces (LogSink envelopes) can render the acceptance
/// without occluding the underlying source defect.
///
/// **Pillar 9 classification** — `SpecialCircumstances` is an
/// operator-supplied annotation overlay (operator publishes "I've
/// already acknowledged this"). Distinct from `Policy` axes
/// (which change pipeline behavior); these change presentation
/// only. Lives parallel to `Policy`, not as a Policy axis, until a
/// future slice formalizes a `Policy.Annotation` axis if needed.
[<RequireQualifiedAccess>]
type AcceptanceState =
    | NotAccepted
    | AcceptedByConfig of source: string

/// Typed runtime form of `Overrides.AllowMissingPrimaryKey` +
/// `Overrides.CircularDependencies.AllowedCycles`. Each field is a
/// set keyed by `SsKey` (or set-of-`SsKey`-sets, for cycles —
/// SCC membership is set-shaped). `empty` is the no-acknowledgements
/// default; consuming sites must produce identical diagnostic
/// streams (modulo annotation) for `empty` vs unpopulated.
type SpecialCircumstances = {
    AllowedMissingPrimaryKeys : Set<SsKey>
    AllowedCycles             : Set<Set<SsKey>>
}

[<RequireQualifiedAccess>]
module SpecialCircumstances =

    let empty : SpecialCircumstances = {
        AllowedMissingPrimaryKeys = Set.empty
        AllowedCycles             = Set.empty
    }


[<RequireQualifiedAccess>]
module SpecialCircumstancesBinding =

    let private bindError (code: string) (message: string) : ValidationError =
        ValidationError.create (sprintf "pipeline.specialCircumstances.%s" code) message

    /// Resolve an operator-supplied `LogicalName` (Module.Entity) to
    /// the matching kind's `SsKey`. Errors structurally if no kind
    /// in `catalog` matches the (module, entity) logical pair.
    let private resolveKindByLogical
        (catalog: Catalog)
        (ref: Config.LogicalName)
        : Result<SsKey> =
        match CatalogResolution.tryKindByLogical catalog ref.Module ref.Entity with
        | Some key -> Result.success key
        | None ->
            Result.failureOf (
                bindError
                    "allowMissingPk.unresolved"
                    (sprintf
                        "overrides.allowMissingPrimaryKey entry %s.%s did not match any catalog kind."
                        ref.Module ref.Entity))

    /// Resolve a single cycle entry's physical `TableName` to the
    /// matching kind's `SsKey`. Cycle entries reference kinds by
    /// physical name (`Schema.Table`-style; the existing
    /// `CircularDependencyEntry` shape carries `TableName : string`).
    /// Currently matches against `k.Physical.Table` ignoring schema —
    /// V1's circular-dependency cycle entries don't disambiguate
    /// schemas. Promote to schema-qualified matching when a real
    /// multi-schema cycle surfaces (IR-grows-under-evidence).
    let private resolveKindByPhysicalTable
        (catalog: Catalog)
        (tableName: string)
        : Result<SsKey> =
        match CatalogResolution.tryKindByPhysicalTable catalog tableName with
        | Some key -> Result.success key
        | None ->
            Result.failureOf (
                bindError
                    "allowedCycle.unresolved"
                    (sprintf
                        "overrides.circularDependencies.allowedCycles entry tableName='%s' did not match any catalog kind."
                        tableName))

    let private bindCycle
        (catalog: Catalog)
        (cycle: Config.CircularDependencyCycle)
        : Result<Set<SsKey>> =
        cycle.TableOrdering
        |> List.map (fun e -> resolveKindByPhysicalTable catalog e.TableName)
        |> Result.aggregate
        |> Result.map Set.ofList

    /// Bind the `Overrides.AllowMissingPrimaryKey` allowlist to a
    /// typed `Set<SsKey>` against the loaded catalog. Empty list →
    /// empty set.
    let bindAllowMissingPrimaryKey
        (catalog: Catalog)
        (entries: Config.LogicalName list)
        : Result<Set<SsKey>> =
        entries
        |> List.map (resolveKindByLogical catalog)
        |> Result.aggregate
        |> Result.map Set.ofList

    /// Bind the `Overrides.CircularDependencies.AllowedCycles` list
    /// to a typed `Set<Set<SsKey>>` (each inner set is one cycle's
    /// member-kind set). `None` (no circularDependencies config
    /// section) → empty set.
    let bindAllowedCycles
        (catalog: Catalog)
        (section: Config.CircularDependenciesSection option)
        : Result<Set<Set<SsKey>>> =
        match section with
        | None -> Result.success Set.empty
        | Some s ->
            s.AllowedCycles
            |> List.map (bindCycle catalog)
            |> Result.aggregate
            |> Result.map Set.ofList

    /// Build the typed `SpecialCircumstances` runtime value from a
    /// parsed `Config`. Aggregates all binder errors so the operator
    /// sees every malformed entry in one pass.
    let fromConfig
        (catalog: Catalog)
        (cfg: Config.Config)
        : Result<SpecialCircumstances> =
        validation {
            let! pks    = bindAllowMissingPrimaryKey catalog cfg.Overrides.AllowMissingPrimaryKey
            and! cycles = bindAllowedCycles catalog cfg.Overrides.CircularDependencies
            return {
                AllowedMissingPrimaryKeys = pks
                AllowedCycles             = cycles
            }
        }
