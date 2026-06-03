namespace Projection.Core

// LINT-ALLOW-FILE: the documented type-erasure boundary (chapter A.4.7' axis 2) that makes the
//   registry-driven fold well-typed; the `String.Concat` here is terminal
//   composition of typed registry identifiers at that boundary. The structural
//   registry surface is fully typed.

/// Pass-chain adapter wrapping a typed `RegisteredTransform` into a
/// uniform `Pass<ComposeState, ComposeState>` step. Per chapter A.4.7'
/// axis 2 (`CHAPTER_A_4_7_PRIME_OPEN.md`): this is the type-erasure
/// boundary that makes the registry-driven fold well-typed despite
/// heterogeneous `'Out` across passes.
///
/// `Name` mirrors the wrapped `RegisteredTransform.Name` so the
/// chain-step trail can be cross-referenced against
/// `TransformRegistry.all`.
///
/// `Apply` is a Kleisli endo-arrow `Pass<ComposeState, ComposeState>`
/// (H-003 type alias in `Diagnostics.fs`). The fold in `compose` is
/// `Pass.composeAll` modulo per-step Bench scoping.
type PassChainAdapter = {
    Name : string
    Apply : Pass<ComposeState, ComposeState>
}

[<RequireQualifiedAccess>]
module PassChainAdapter =

    /// Lift a Catalog-rewriting pass. The Run output replaces
    /// `state.Catalog`; the pass's Lineage + Diagnostics threads
    /// through unchanged via `LineageDiagnostics.map`.
    let liftCatalogPass
        (rt: RegisteredTransform<Catalog, Catalog>)
        : PassChainAdapter =
        { Name = rt.Name
          Apply =
            fun state ->
                rt.Run state.Catalog
                |> LineageDiagnostics.map (fun catalog ->
                    ComposeState.withCatalog catalog state) }

    /// Lift a decision-set-producing pass. `state.Catalog` is
    /// preserved; the Run output is written back into ComposeState
    /// via the caller-supplied `writeBack` — typically one of the
    /// `ComposeState.with*` setters. The pass's Lineage + Diagnostics
    /// threads through unchanged.
    let liftDecisionPass
        (rt: RegisteredTransform<Catalog, 'Decision>)
        (writeBack: 'Decision -> ComposeState -> ComposeState)
        : PassChainAdapter =
        { Name = rt.Name
          Apply =
            fun state ->
                rt.Run state.Catalog
                |> LineageDiagnostics.map (fun decision ->
                    writeBack decision state) }

    /// Lift a pass that consumes `TopologicalOrder` (rather than
    /// `Catalog`) from ComposeState. Used by Cluster D graph-analytics
    /// passes (CentralityPass, BoundedContextPass) whose input is the
    /// pre-computed topology. Falls back to `TopologicalOrder.empty`
    /// when topology has not yet been computed (e.g., in unit tests
    /// that don't run the topological-order pass first).
    let liftTopologyPass
        (rt: RegisteredTransform<TopologicalOrder, 'Decision>)
        (writeBack: 'Decision -> ComposeState -> ComposeState)
        : PassChainAdapter =
        { Name = rt.Name
          Apply =
            fun state ->
                let topo =
                    state.TopologicalOrder
                    |> Option.defaultValue TopologicalOrder.empty
                rt.Run topo
                |> LineageDiagnostics.map (fun decision ->
                    writeBack decision state) }

    /// Chapter A.4.7' slice γ — fold a `PassChainAdapter list` into
    /// one composed Apply step.
    ///
    /// **H-003 — Kleisli structure.** This is `Pass.composeAll` over the
    /// pass-chain endo-arrows (`Pass<ComposeState, ComposeState> list`),
    /// modulo per-step Bench scoping. Each adapter's `Apply` is a
    /// Kleisli arrow; folding `LineageDiagnostics.bind` from the
    /// identity arrow (`LineageDiagnostics.ofValue state`) computes
    /// `(((id >=> a₁) >=> a₂) >=> … >=> aₙ) state`. Both writers'
    /// trails compose chronologically (A24 for Lineage; same convention
    /// for Diagnostics). The Kleisli identity and associativity laws
    /// underwrite the fold's correctness — tested in
    /// `KleisliLawTests.fs` against the algebraic `Pass.composeAll`.
    ///
    /// Slice δ consumes `compose RegisteredTransforms.allChainSteps`
    /// inside `Compose.project`; slice ε consumes
    /// `compose skeletonChainSteps` inside `Compose.runWithSkeleton`.
    /// The same primitive serves both consumers — heterogeneous
    /// `'Out` is already erased at lift time, so the fold is well-
    /// typed over the homogeneous adapter shape.
    let compose
        (adapters: PassChainAdapter list)
        (state: ComposeState)
        : Lineage<Diagnostics<ComposeState>> =
        use _ = Bench.scope "compose.passChain.compose"
        adapters
        |> List.fold
            (fun acc adapter ->
                let timedApply (s: ComposeState) =
                    use _ = Bench.scope (System.String.Concat("compose.passChain.", adapter.Name))
                    adapter.Apply s
                LineageDiagnostics.bind timedApply acc)
            (LineageDiagnostics.ofValue state)
