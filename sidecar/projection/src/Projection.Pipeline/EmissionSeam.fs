namespace Projection.Pipeline

open Projection.Core

/// F3 (audit 2026-06-17) — the post-chain EMISSION SEAM as a single bound,
/// registered source. Every `Catalog → Catalog` rewrite applied AFTER the pass
/// chain and BEFORE emission lives here as a named, classified entry, so the
/// `registered ⇔ executed` totality proof
/// (`RegisteredAllTransformsBidirectionalTests`) covers the seam exactly the way
/// E1/E2 cover the emit/read stages.
///
/// This closes the blind spot the sweep named (audit F2/F3): a post-chain
/// mutator (`EmissionPolicy.filterPlatformAutoIndexes`) ran at the emit boundary
/// OUTSIDE every bound source the totality test iterates, so it was registered
/// (F2) but its *execution* was not bound to its registration. Routing it
/// through this one seam fixes that structurally: `apply` folds exactly the
/// registered `rewrites`, and `metadata` / `executedNames` project from the SAME
/// `rewrites` list — so a rewrite added here is BOTH executed (by `apply`) and
/// registered (in `metadata`, wired into `RegisteredAllTransforms.all`) by
/// construction, never one without the other.
///
/// The seam is `EmissionPolicy`-driven (the rewrites' evidence is operator
/// emission policy, not catalog-derived — A18 amended), which is why it is a
/// post-chain seam rather than a pass-chain step: it must not pay the pass-chain
/// cost and it consumes Policy the chain never sees.
[<RequireQualifiedAccess>]
module EmissionSeam =

    /// One post-chain emission rewrite: its registry metadata paired with the
    /// pure `EmissionPolicy`-driven `Catalog → Catalog` transform it executes.
    /// The pairing is the load-bearing invariant — the metadata and the
    /// transform travel together, so neither can drift from the other.
    type private Rewrite =
        { Metadata  : RegisteredTransformMetadata
          Transform : EmissionPolicy -> Catalog -> Catalog }

    /// F2 — the platform-auto index prune. `OperatorIntent Emission` (the
    /// `IncludePlatformAutoIndexes` toggle is operator policy); identity when the
    /// toggle is `true` (V1-parity default). Formerly registered inline in
    /// `RegisteredAllTransforms` and executed by a bare call at the Pipeline emit
    /// seam — now both flow through this one bound entry.
    let private filterPlatformAutoIndexes : Rewrite =
        { Metadata =
            RegisteredTransformMetadata.emitter "filterPlatformAutoIndexes" Schema
                [ TransformSite.operatorIntent "platformAutoIndexPruning" Emission
                    "Prune indexes marked IsPlatformAuto=true from the emitted catalog when Policy.Emission.IncludePlatformAutoIndexes=false (chapter 4.8 slice γ; V1-parity default keeps them). A post-chain emission-seam rewrite (executed at its own site, not the pass chain). OperatorIntent Emission: the IncludePlatformAutoIndexes toggle is operator-supplied emission policy, not source evidence." ]
          Transform = EmissionPolicy.filterPlatformAutoIndexes }

    /// The registered post-chain rewrites, in application order. The SINGLE
    /// source `apply` / `metadata` / `executedNames` all project from.
    let private rewrites : Rewrite list = [ filterPlatformAutoIndexes ]

    /// Apply every registered post-chain rewrite, in order — the ONE seam the
    /// Pipeline routes its post-chain `Catalog → Catalog` rewrites through. A
    /// future rewrite is added to `rewrites` (covered by the totality test), not
    /// as a bare call somewhere in the Pipeline.
    let apply (policy: EmissionPolicy) (catalog: Catalog) : Catalog =
        rewrites |> List.fold (fun c r -> r.Transform policy c) catalog

    /// The seam's registry metadata — projected from the SAME `rewrites` that
    /// `apply` executes, so `registered ⇔ executed` holds for the seam by
    /// construction (the E1 discipline). Spliced into `RegisteredAllTransforms.all`.
    let metadata : RegisteredTransformMetadata list =
        rewrites |> List.map (fun r -> r.Metadata)

    /// The executed rewrite names — the bidirectional test pairs these against
    /// `metadata` (the seam's own closure) and against `RegisteredAllTransforms.all`
    /// (the wiring), the two halves of the seam's totality guarantee.
    let executedNames : string list =
        rewrites |> List.map (fun r -> r.Metadata.Name)
