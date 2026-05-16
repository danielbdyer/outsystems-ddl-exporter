namespace Projection.Core

/// Where a transformation fires in the pipeline. Five stage seams per
/// `DECISIONS 2026-05-15 (late)`'s canonical strongly-typed registry
/// shape; each enumerates a class of transformation site:
///
///   - `Adapter` — raw rowset / JSON → Catalog-fragment translations
///     (e.g., OSSYS adapter rules).
///   - `Pass` — `Catalog -> Lineage<Diagnostics<Catalog>>` (the existing
///     12 passes under `Projection.Core/Passes/`).
///   - `OrderingPolicy` — ordering-policy parameter sites (e.g.,
///     `SelfLoopPolicy` on `TopologicalOrderPass`).
///   - `Emitter` — `Catalog -> ArtifactByKind<'element>` (sibling Π
///     emitters).
///   - `Pipeline` — `Compose`-level transformations (e.g.,
///     `TableRename.applyRenames`).
///
/// **Naming note:** `StageBinding.OrderingPolicy` (the stage-seam
/// vocabulary) is distinct from `OverlayAxis.Ordering` (the
/// operator-intent vocabulary, in `Classification.fs`). The two are
/// related — a transformation registered at `StageBinding =
/// OrderingPolicy` is typically classified `OperatorIntent Ordering` —
/// but the StageBinding names where the transformation fires and the
/// OverlayAxis names whose intent it expresses.
type StageBinding =
    | Adapter
    | Pass
    | OrderingPolicy
    | Emitter
    | Pipeline

/// The codified concerns of V2's structural-evidence layer per
/// `PRODUCT_AXIOMS.md`. Each transformation site advertises which
/// concern it operates on; the registry's totality property test
/// (slice θ) asserts every registered transformation names a Domain.
///
/// **Cross-cutting** is the catch-all for transformations that
/// operate orthogonally to schema / data / identity / diagnostics /
/// cutover-safety (e.g., lineage emission itself, bench probes,
/// the registry traversal). Use a concrete Domain when one fits;
/// `CrossCutting` is reserved for genuinely orthogonal seams.
type Domain =
    | Schema
    | Data
    | Identity
    | Diagnostics
    | CutoverSafety
    | CrossCutting

/// One classified sub-site within a registered transformation.
/// Captures intra-pass classification fidelity per `DECISIONS
/// 2026-05-15 (late)` Q11: a single pass may have multiple sites,
/// each classified independently. `TopologicalOrderPass`'s
/// `[SortKahn; SelfLoopHandling]` pair is the canonical worked
/// example (the SortKahn site is `DataIntent`; the SelfLoopHandling
/// site is `OperatorIntent Ordering` per the chapter A.4.7 open's
/// Q9-trigger-fires worked example).
///
/// The `Rationale` field is the harvest-discipline analysis prose
/// (pillar 9's 4-step workflow output). `TransformRegistry.create`
/// rejects empty rationales — the discipline requires the analysis
/// to be done, not assumed. The harvest-classification coverage
/// property test (slice θ) cross-references this prose against
/// `Tolerance.fs` entries for v1↔v2 divergence audit.
type TransformSite = {
    SiteName : string
    Classification : Classification
    Rationale : string
}

/// Whether a registered transformation has a v2 implementation.
/// Per the harvest workflow's triple-deliverable: v1 transformations
/// without a v2 equivalent ship as `NotImplementedInV2 of rationale`
/// registry entries (paired with a `Tolerance.fs` entry naming the
/// divergence and a `[<Fact(Skip = "...")>]` test stub citing the
/// classification rationale). The three surfaces redundantly catch
/// silent harvest inclusion (pillar 9 named failure mode).
///
/// `TransformRegistry.create` rejects `NotImplementedInV2 ""` —
/// the rationale must name the harvest decision substantively
/// (matches the LINT-ALLOW substantive-rationale discipline pattern
/// per `DECISIONS 2026-05-10`).
type TransformStatus =
    | Active
    | NotImplementedInV2 of rationale: string

/// The canonical strongly-typed surface for a transformation site.
/// Each pass module's primary public surface (slice γ onward) becomes
/// `<PassName>.registered : RegisteredTransform<'In, 'Out>`; the
/// `let run` function becomes private; consumers invoke
/// `registered.Run`. Single definition site; no parallel enumeration
/// (per `DECISIONS 2026-05-15 (late)` Q3 answer).
///
/// The `Run` field carries the typed transformation function itself.
/// Per `DECISIONS 2026-05-13 — Pass return-type codification`, the
/// canonical pass shape is `Lineage<Diagnostics<'Out>>` — the dual
/// writer captures both decisions (Lineage) and observer-relevant
/// findings (Diagnostics). Passes that emit only decisions (no
/// diagnostics) wrap their output via `Diagnostics.ofValue` at
/// `.registered` registration time (slice γ adapts).
///
/// Type erasure happens at the `RegisteredTransformMetadata` boundary
/// (the type-stripped view used for the flat `TransformRegistry.all`
/// enumeration list, the manifest emission, and the totality
/// coverage property test). Per-Run invocation uses the pass
/// module's typed export directly — no `obj`-boxing at consumer
/// call sites.
type RegisteredTransform<'In, 'Out when 'Out : equality> = {
    Name : string
    Domain : Domain
    StageBinding : StageBinding
    Sites : TransformSite list
    Run : 'In -> Lineage<Diagnostics<'Out>>
    Status : TransformStatus
}

/// Type-erased projection of `RegisteredTransform<'In, 'Out>` —
/// drops the `Run` field so the registry's flat collection can hold
/// per-stage heterogeneous transformations without unsafe `obj`-
/// boxing. The metadata view is what `TransformRegistry.create`
/// validates, what the manifest emitter serializes (slice η), and
/// what the totality coverage property test (slice θ) enumerates.
/// Pass-module consumers invoke `<PassName>.registered.Run`
/// directly on the typed export.
type RegisteredTransformMetadata = {
    Name : string
    Domain : Domain
    StageBinding : StageBinding
    Sites : TransformSite list
    Status : TransformStatus
}

[<RequireQualifiedAccess>]
module RegisteredTransform =

    /// Project a typed `RegisteredTransform<'In, 'Out>` to its
    /// type-erased metadata for registry enumeration. The `Run`
    /// field is dropped; consumers that need to invoke `Run` use
    /// the pass module's `.registered` export directly.
    let toMetadata (rt: RegisteredTransform<'In, 'Out>) : RegisteredTransformMetadata =
        { Name = rt.Name
          Domain = rt.Domain
          StageBinding = rt.StageBinding
          Sites = rt.Sites
          Status = rt.Status }

[<RequireQualifiedAccess>]
module TransformRegistry =

    let private registryError (code: string) (message: string) : ValidationError =
        ValidationError.create (System.String.Concat ("registry.", code)) message  // LINT-ALLOW: terminal error-code prefix at the ValidationError boundary; segments are typed (literal prefix + code identifier); BCL `String.Concat` is the use-case-specific library for two-segment terminal-text composition

    let private validateName (entry: RegisteredTransformMetadata) : Result<unit> =
        if System.String.IsNullOrWhiteSpace entry.Name then
            Result.failureOf
                (registryError "nameEmpty" "Registered transformation Name must be non-empty.")
        else
            Result.success ()

    let private validateSite (entry: RegisteredTransformMetadata) (site: TransformSite) : Result<unit> =
        if System.String.IsNullOrWhiteSpace site.Rationale then
            Result.failureOf
                (registryError
                    "siteRationaleEmpty"
                    (System.String.Concat (  // LINT-ALLOW: terminal error-message composition at the ValidationError boundary; segments are typed (pass identifier + site identifier from the registry record); BCL `String.Concat` is the use-case-specific library for the multi-segment audit-narration message
                        "Registered transformation '", entry.Name,
                        "' site '", site.SiteName,
                        "' has empty Rationale; pillar 9 harvest-discipline analysis required.")))
        else
            Result.success ()

    let private validateStatus (entry: RegisteredTransformMetadata) : Result<unit> =
        match entry.Status with
        | Active -> Result.success ()
        | NotImplementedInV2 rationale ->
            if System.String.IsNullOrWhiteSpace rationale then
                Result.failureOf
                    (registryError
                        "notImplementedRationaleEmpty"
                        (System.String.Concat (  // LINT-ALLOW: terminal error-message composition at the ValidationError boundary; segments are typed (pass identifier from the registry record + literal narration); BCL `String.Concat` is the use-case-specific library for the multi-segment audit-narration message
                            "Registered transformation '", entry.Name,
                            "' has Status = NotImplementedInV2 with empty rationale; ",
                            "harvest-discipline triple deliverable requires substantive rationale.")))
            else
                Result.success ()

    let private validateUniqueNames (entries: RegisteredTransformMetadata list) : Result<unit> =
        let duplicateNames =
            entries
            |> List.groupBy (fun e -> e.Name)
            |> List.filter (fun (_, xs) -> List.length xs > 1)
            |> List.map fst
        if List.isEmpty duplicateNames then
            Result.success ()
        else
            duplicateNames
            |> List.map (fun name ->
                registryError
                    "duplicatePassName"
                    (System.String.Concat (  // LINT-ALLOW: terminal error-message composition at the ValidationError boundary; segments are typed (literal narration + duplicated pass identifier); BCL `String.Concat` is the use-case-specific library for the multi-segment audit-narration message
                        "Registered transformation Name '", name,
                        "' appears multiple times; Names must be unique within the registry.")))
            |> Result.failure

    /// Smart constructor enforcing the registry's totality invariants.
    /// Per the chapter A.4.7 open success criteria + DECISIONS
    /// 2026-05-15 (late):
    ///   - Every `Name` unique within the registry.
    ///   - Every `Site.Rationale` non-empty (pillar 9 harvest-
    ///     discipline analysis required at every classified site).
    ///   - `Status = NotImplementedInV2 r` requires substantive `r`
    ///     (triple deliverable harvest-workflow audit).
    ///
    /// Slice β ships the constructor against an empty `all` list;
    /// slice γ / δ / ε populate the list as each pass module exposes
    /// `.registered`. The validation runs once at construction; the
    /// resulting metadata list is trusted thereafter.
    let create (entries: RegisteredTransformMetadata list) : Result<RegisteredTransformMetadata list> =
        let entryErrors =
            entries
            |> List.collect (fun entry ->
                let nameResult = validateName entry
                let statusResult = validateStatus entry
                let siteResults = entry.Sites |> List.map (validateSite entry)
                let allResults = nameResult :: statusResult :: siteResults
                allResults
                |> List.collect (fun r ->
                    match r with
                    | Ok () -> []
                    | Error es -> es))
        let uniquenessErrors =
            match validateUniqueNames entries with
            | Ok () -> []
            | Error es -> es
        let allErrors = entryErrors @ uniquenessErrors
        if List.isEmpty allErrors then
            Result.success entries
        else
            Error allErrors

    /// The empty registry. Slice β ships this as a structural
    /// placeholder; slice γ + δ + ε populate as pass modules /
    /// adapter rules / emitter strategies expose `.registered`. The
    /// top-level evaluation order is hand-maintained — each pass
    /// module's `.registered |> RegisteredTransform.toMetadata`
    /// reference is added to this list explicitly. F# top-level
    /// evaluation order resolves the cross-module dependencies.
    let all : RegisteredTransformMetadata list = []

    /// Filter to a single stage. Used by `Compose.run` traversal
    /// (slice ζ) to iterate per-stage in stage order.
    let inStage (stage: StageBinding) (entries: RegisteredTransformMetadata list) : RegisteredTransformMetadata list =
        entries |> List.filter (fun rt -> rt.StageBinding = stage)

    /// Stage-ordering function. Used by `Compose.run` (slice ζ) to
    /// traverse the registry in execution order: Adapter →
    /// (OrderingPolicy + Pass) → Emitter → Pipeline.
    let stageOrdinal (stage: StageBinding) : int =
        match stage with
        | Adapter        -> 0
        | OrderingPolicy -> 1
        | Pass           -> 2
        | Emitter        -> 3
        | Pipeline       -> 4

    /// All entries sorted in stage-ordinal order. Slice ζ's
    /// `Compose.run` traversal consumes this as its execution loop.
    let allInStageOrder (entries: RegisteredTransformMetadata list) : RegisteredTransformMetadata list =
        entries |> List.sortBy (fun rt -> stageOrdinal rt.StageBinding)
