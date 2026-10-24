namespace Projection.Pipeline

// LINT-ALLOW-FILE: operator-facing special-circumstances diagnostic prose; segments are typed
//   and the emitted diagnostic value is immutable. `String.concat` is the BCL
//   primitive at this terminal-text boundary.

open Projection.Core

/// Chapter C slice C.2 — operator-visible diagnostic surface for
/// source-side defects that the existing structural passes detect
/// internally but don't route to the operator. Two findings:
///
/// 1. **`structural.targetMissingPrimaryKey`** — for every catalog
///    reference whose target kind has no primary-key attribute, emit
///    one `DiagnosticEntry` keyed by the target kind's `SsKey`. The
///    underlying observation matches the `SymmetricClosure` pass's
///    `Skip (ClosureSkipped TargetHasNoPrimaryKey)` event but lifts
///    it from lineage-only annotation to operator-visible diagnostic.
///    Emission is per *unique target kind* (deduplicated across the
///    references that point at it), not per reference; one
///    actionable surface per kind matches the operator allowlist's
///    one-entry-per-kind shape.
///
/// 2. **`structural.cycleUnresolved`** — for every unresolved SCC
///    in the post-chain `ComposeState.TopologicalOrder.Cycles`, emit
///    one `DiagnosticEntry` carrying the cycle's member list in
///    `Metadata.members` (semicolon-separated rendered SsKeys; same
///    format used elsewhere in the diagnostic surface).
///
/// **Acceptance annotation** — each emitted entry is cross-checked
/// against the operator's `SpecialCircumstances` allowlist; matches
/// receive `Metadata.acceptedVia` naming the config source. Per the
/// annotate-don't-suppress discipline (slice-6 reshape lesson): the
/// diagnostic still appears in the stream; the annotation lets
/// downstream operator surfaces render the acceptance without
/// occluding the underlying source defect.
///
/// **Pillar 9 classification** — this module is a `DataIntent` scan
/// (the structural findings derive from `Catalog × ComposeState`,
/// reachable from `Project(catalog, Policy.empty, profile)` without
/// operator opinion). The acceptance annotation overlay is
/// `OperatorIntent` on the annotation axis (operator publishes
/// "this is acknowledged"). The two compose: the scan is pure
/// observation; the annotation is operator overlay. Architecture
/// mirrors the C.1 binder + chain-factory separation — the
/// observation produces the typed value; the overlay adds operator
/// intent on top.
[<RequireQualifiedAccess>]
module SpecialCircumstancesDiagnostics =

    let private source = "specialCircumstancesScan"

    let private missingPrimaryKeyCode = "structural.targetMissingPrimaryKey"
    let private cycleUnresolvedCode   = "structural.cycleUnresolved"

    let private acceptedViaMissingPk = "config:overrides.allowMissingPrimaryKey"
    let private acceptedViaCycle     = "config:overrides.circularDependencies"

    /// Render a list of `SsKey`s as a single semicolon-separated
    /// string for `DiagnosticEntry.Metadata`. Per Metadata's current
    /// `Map<string, string>` shape; promote to a typed payload when
    /// a consumer demands structured access (IR-grows-under-evidence).
    let private renderKeys (keys: SsKey seq) : string =
        keys
        |> Seq.map SsKey.rootOriginal
        |> String.concat ";"

    /// True iff `k` has no primary-key attribute (`Kind.primaryKey k`
    /// returns the empty list). Mirrors the predicate that
    /// `SymmetricClosure.buildInverse` uses to skip with reason
    /// `TargetHasNoPrimaryKey`.
    let private kindHasNoPrimaryKey (k: Kind) : bool =
        List.isEmpty (Kind.primaryKey k)

    let private emitMissingPrimaryKeyDiagnostics
        (allowed: Set<SsKey>)
        (catalog: Catalog)
        : DiagnosticEntry list =
        let allKinds = Catalog.allKinds catalog
        let kindByKey = Catalog.kindIndex catalog
        // Target-kind SsKeys referenced by ANY kind, whose target has
        // no PK. Deduplicate at the target-kind level: one entry per
        // missing-PK target regardless of how many references point at
        // it (operator allowlist is keyed by target kind).
        let targetsNeedingPk =
            allKinds
            |> List.collect (fun src -> src.References |> List.map (fun r -> r.TargetKind))
            |> List.distinct
            |> List.choose (fun targetKey ->
                Map.tryFind targetKey kindByKey
                |> Option.bind (fun target ->
                    if kindHasNoPrimaryKey target then Some target else None))
        targetsNeedingPk
        |> List.map (fun target ->
            let isAccepted = Set.contains target.SsKey allowed
            let baseMeta : Map<string, string> =
                Map.empty
                |> Map.add "targetKind" (SsKey.rootOriginal target.SsKey)
            let meta =
                if isAccepted then
                    baseMeta |> Map.add "acceptedVia" acceptedViaMissingPk
                else baseMeta
            {
                Source          = source
                Severity        = DiagnosticSeverity.Warning
                Code            = missingPrimaryKeyCode
                Message         =
                    sprintf
                        "Kind %s has no primary key; references pointing at it cannot get symmetric-closure inverses (downstream FK creation may be skipped)."
                        (SsKey.rootOriginal target.SsKey)
                SsKey           = Some target.SsKey
                Metadata        = meta
                SuggestedConfig = None
            })

    let private emitCycleDiagnostics
        (allowed: Set<Set<SsKey>>)
        (topo: TopologicalOrder)
        : DiagnosticEntry list =
        topo.Cycles
        |> List.map (fun cycle ->
            let cycleMembers = CycleDiagnostic.members cycle
            let memberSet = Set.ofList cycleMembers
            let isAccepted = Set.contains memberSet allowed
            let baseMeta : Map<string, string> =
                Map.empty
                |> Map.add "members" (renderKeys cycleMembers)
                |> Map.add "reason"  (CycleDiagnostic.reasonText cycle)
            let meta =
                if isAccepted then
                    baseMeta |> Map.add "acceptedVia" acceptedViaCycle
                else baseMeta
            // Catalog-level diagnostic — cycles have multiple members
            // with no canonical single identity. SsKey left None per
            // the convention named in `Diagnostics.fs:50-53`.
            {
                Source          = source
                Severity        = DiagnosticSeverity.Warning
                Code            = cycleUnresolvedCode
                Message         =
                    sprintf
                        "Topological sort surfaced unresolved cycle [%s]: %s"
                        (renderKeys cycleMembers)
                        (CycleDiagnostic.reasonText cycle)
                SsKey           = None
                Metadata        = meta
                SuggestedConfig = None
            })

    /// Scan `state.Catalog` for missing-PK targets + read
    /// `state.TopologicalOrder.Cycles` for unresolved cycles; emit
    /// one `DiagnosticEntry` per finding, with `Metadata.acceptedVia`
    /// stamped on entries the operator has allowlisted.
    let emit
        (overrides: SpecialCircumstances)
        (state: ComposeState)
        : DiagnosticEntry list =
        let pks = emitMissingPrimaryKeyDiagnostics overrides.AllowedMissingPrimaryKeys state.Catalog
        let cycles =
            match state.TopologicalOrder with
            | None    -> []
            | Some t  -> emitCycleDiagnostics overrides.AllowedCycles t
        pks @ cycles
