namespace Projection.Core

/// Pass-chain adapter wrapping a typed `RegisteredTransform` into a
/// uniform `ComposeState -> Lineage<Diagnostics<ComposeState>>` step.
/// Per chapter A.4.7' axis 2 (`CHAPTER_A_4_7_PRIME_OPEN.md`): this is
/// the type-erasure boundary that makes the registry-driven fold
/// well-typed despite heterogeneous `'Out` across passes.
///
/// `Name` mirrors the wrapped `RegisteredTransform.Name` so the
/// chain-step trail can be cross-referenced against
/// `TransformRegistry.all`.
type PassChainAdapter = {
    Name : string
    Apply : ComposeState -> Lineage<Diagnostics<ComposeState>>
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

    /// Chapter A.4.7' slice γ — fold a `PassChainAdapter list` into
    /// one composed Apply step. Each adapter's Apply is threaded
    /// through `LineageDiagnostics.bind`; both writers compose
    /// chronologically (A24: earliest-first; the Diagnostics-trail
    /// sibling follows the same convention).
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
