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
/// `Pass.composeAll` over `Meter.pass`-decorated arrows (§9.8.5 — the
/// former "modulo per-step Bench scoping", factored into a value).
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
    /// ONLY for direct unit-test callers that don't run the
    /// topological-order pass first — an ASSEMBLED chain can no longer
    /// reach this default (align-I.6: the step declares `Requires =
    /// [ChainProduct.Topology]` and assembly excludes it, by name, when
    /// the producer is absent; A52).
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

    /// Lift a pass that consumes BOTH `state.Catalog` and the
    /// pre-computed `state.TopologicalOrder` from ComposeState (e.g.
    /// SchemaComplexityPass, whose metrics span the FK graph *and* IR
    /// attribute statistics). Falls back to `TopologicalOrder.empty`
    /// only for direct unit-test callers — assembled chains cannot
    /// reach the default (align-I.6; A52). Unlike `liftDecisionPass`,
    /// the topology is read from ComposeState at apply-time rather than
    /// baked in at registration — so the pass sees the chain's real
    /// topology (the prior `registered None` wiring computed every metric
    /// over zero edges).
    let liftCatalogTopologyPass
        (name: string)
        (run: Catalog -> TopologicalOrder -> Lineage<Diagnostics<'Decision>>)
        (writeBack: 'Decision -> ComposeState -> ComposeState)
        : PassChainAdapter =
        { Name = name
          Apply =
            fun state ->
                let topo =
                    state.TopologicalOrder
                    |> Option.defaultValue TopologicalOrder.empty
                run state.Catalog topo
                |> LineageDiagnostics.map (fun decision ->
                    writeBack decision state) }

    /// Chapter A.4.7' slice γ — fold a `PassChainAdapter list` into
    /// one composed Apply step.
    ///
    /// **H-003 — Kleisli structure.** This IS `Pass.composeAll` over the
    /// pass-chain endo-arrows (`Pass<ComposeState, ComposeState> list`),
    /// each decorated by `Meter.pass` (§9.8.5 — the per-step Bench
    /// scoping, factored from "modulo" prose into a value; card S1).
    /// `composeAll` folds `compose` from the identity arrow, computing
    /// `(((id >=> a₁) >=> a₂) >=> … >=> aₙ) state`. Both writers'
    /// trails compose chronologically (A24 for Lineage; same convention
    /// for Diagnostics); `Meter.pass` is identity on the value plane, so
    /// the decoration is invisible to the algebra. The Kleisli laws are
    /// tested in `DiagnosticsTests.fs` (H-003) against the algebraic
    /// `Pass.composeAll`; the per-step labels keep their exact bytes
    /// (`compose.passChain.<Name>` — the perf surfaces read them).
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
        let metered =
            adapters
            |> List.map (fun adapter ->
                Meter.pass
                    (System.String.Concat("compose.passChain.", adapter.Name))
                    adapter.Apply)
        Pass.composeAll metered state


/// A named intermediate product a chain step writes into `ComposeState`
/// for LATER steps to consume (align-I.6; audit a3-F2). The vocabulary
/// is the chain's dependency structure, not a ComposeState field
/// inventory — a product is named only when at least one downstream
/// step requires it, so assembly can validate satisfiability and name
/// what an exclusion cascades to.
[<RequireQualifiedAccess>]
type ChainProduct =
    /// The FK-graph topological order (`ComposeState.TopologicalOrder`)
    /// — produced by `topologicalOrder`; required by the four
    /// graph-analytics dependents (centrality, boundedContext,
    /// schemaComplexity, cascadeShockZones).
    | Topology
    /// Profiling evidence (`Profile`) — supplied by ACQUISITION, not
    /// produced by any chain step; required by the profile-consuming
    /// decision/tightening steps. Declared so an assembly states what
    /// it supplies and the profile-invariant prefix is derivable.
    | ProfileEvidence

/// One named exclusion from a chain assembly (align-I.6): the step and
/// the product requirement no kept step (nor the assembly's supplied
/// set) satisfies. Exclusions are the honest voice of a narrowed
/// assembly — absent-with-a-name, never present-and-degenerate.
type ChainExclusion = {
    StepName : string
    Missing  : ChainProduct
}

/// A single registered pass-chain step — the **single definition site**
/// the "registry drives the run" refactor (`DECISIONS 2026-06-04`)
/// establishes. Each step pairs the pillar-9 classification surface
/// (`Metadata` — what `transform.registered`, the manifest, and the
/// totality property tests read) with how the step plugs into the run
/// (`Build` captures the lift strategy + `ComposeState` writeback + the
/// per-call `Policy` / `Profile` threading). The metadata and the
/// execution can no longer drift, because both project from the same
/// value: the `RegisteredTransform.Run` carried inside the `.registered`
/// the `Build` closure lifts IS what runs, and `Metadata` is the
/// `RegisteredTransform.toMetadata` of the same pass factory. One
/// `ChainStep` per pass; adding a pass is one entry, not three.
///
/// **Product preconditions (align-I.6).** `Requires` names the
/// `ChainProduct`s the step reads; `Produces` names the one it writes
/// (if any). Assembly validates satisfiability in execution order —
/// the full chain asserts zero exclusions; narrowed assemblies (the
/// skeleton) return the named exclusion cascade (A52).
type ChainStep = {
    Metadata : RegisteredTransformMetadata
    Requires : ChainProduct list
    Produces : ChainProduct option
    Build    : Policy -> Profile -> PassChainAdapter
}

[<RequireQualifiedAccess>]
module ChainStep =

    let metadata (step: ChainStep) : RegisteredTransformMetadata = step.Metadata

    let build (policy: Policy) (profile: Profile) (step: ChainStep) : PassChainAdapter =
        step.Build policy profile

    /// Assemble a chain honoring product preconditions (align-I.6;
    /// A52). Walks `steps` in execution order, keeping a step iff every
    /// `Requires` entry is satisfied by `supplied` or by a KEPT earlier
    /// step's `Produces`. A dropped producer cascades to its dependents
    /// — the forward walk realizes the cascade because products only
    /// flow forward. Each exclusion is returned BY NAME with the
    /// missing product; nothing is silently defaulted.
    let assemble
        (supplied: Set<ChainProduct>)
        (steps: ChainStep list)
        : ChainStep list * ChainExclusion list =
        let kept, excluded, _ =
            steps
            |> List.fold
                (fun (kept, excluded, available) step ->
                    match step.Requires |> List.tryFind (fun p -> not (Set.contains p available)) with
                    | Some missing ->
                        (kept, { StepName = step.Metadata.Name; Missing = missing } :: excluded, available)
                    | None ->
                        let available' =
                            match step.Produces with
                            | Some p -> Set.add p available
                            | None -> available
                        (step :: kept, excluded, available'))
                ([], [], supplied)
        (List.rev kept, List.rev excluded)

    /// Assert-only satisfiability for the FULL chain (align-I.6; A52):
    /// a canonical chain with an unmet product precondition is a
    /// structural mis-wiring — fail loud at assembly, naming every
    /// exclusion, rather than letting a step compute over a defaulted
    /// product at run time.
    let assertSatisfiable
        (supplied: Set<ChainProduct>)
        (steps: ChainStep list)
        : ChainStep list =
        match assemble supplied steps with
        | kept, [] -> kept
        | _, exclusions ->
            let detail =
                exclusions
                |> List.map (fun e -> sprintf "%s (missing %A)" e.StepName e.Missing)
                |> String.concat ", "
            invalidOp (sprintf "chain assembly mis-wired — unmet product preconditions: %s" detail)
