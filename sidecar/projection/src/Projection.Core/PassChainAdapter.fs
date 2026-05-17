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
