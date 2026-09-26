namespace Projection.Pipeline

open Projection.Core

/// **X6 — the eject (protein P-7): the append-forever provenance package.** The
/// fork is RESOLVED to *append-forever* (`DECISIONS`/handoff): freezing a
/// timeline preserves EVERY episode + the full accumulated refactorlog — there
/// is NO collapse to the latest snapshot. Any prior state stays reconstructable
/// (a downstream consumer may DacFx-publish an intermediate pre-freeze schema).
/// The package is self-verifying: the FTC reconstruction from genesis
/// (`reconstructLatestSchema`) reproduces the frozen latest schema.
type EjectPackage =
    {
        Timeline            : Timeline
        /// Every episode along the timeline — append-forever, never collapsed.
        Episodes            : Episode list
        /// The full refactorlog reference chain in timeline order — the
        /// accumulated Decision-plane provenance.
        RefactorLogRefs     : string list
        GenesisSchema       : Catalog
        /// The latest recorded schema — the state the timeline freezes at.
        FrozenSchema        : Catalog
        /// The FTC reconstruction from genesis (`fold applyDiff` over the
        /// per-edge displacements). Must reproduce `FrozenSchema`.
        ReconstructedSchema : Catalog
    }

[<RequireQualifiedAccess>]
module EjectRun =

    /// Assemble the append-forever package from a loaded chain (pure). Fails
    /// only if the schema-evolution fold fails (a non-composable edge).
    let fromChain (chain: EpisodicLifecycle) : Result<EjectPackage, EmitError> =
        let episodes = EpisodicLifecycle.episodes chain
        match EpisodicLifecycle.reconstructLatestSchema chain with
        | Error e -> Error e
        | Ok reconstructed ->
            Ok
                { Timeline = EpisodicLifecycle.timeline chain
                  Episodes = episodes
                  RefactorLogRefs = episodes |> List.choose (fun e -> e.RefactorLogRef)
                  GenesisSchema = (EpisodicLifecycle.head chain).Schema
                  FrozenSchema = (EpisodicLifecycle.latest chain).Schema
                  ReconstructedSchema = reconstructed }

    /// The package's self-verification: the genesis→latest FTC reconstruction
    /// reproduces the frozen state at the `PhysicalSchema` level (the chain-level
    /// round-trip law). True ⇒ the path is faithfully preserved and any prior
    /// state is reconstructable from the package.
    let isFaithful (pkg: EjectPackage) : bool =
        PhysicalSchema.isSchemaEqual
            (PhysicalSchema.diff
                (PhysicalSchema.ofCatalog pkg.FrozenSchema)
                (PhysicalSchema.ofCatalog pkg.ReconstructedSchema))

    /// Load the durable timeline at `path` and eject it (the operator-facing
    /// entry). Fail-closed: a malformed store or a non-composable edge surfaces
    /// as a string error.
    let fromStore (path: string) : Result<EjectPackage, string> =
        LifecycleStore.withLoaded fromChain "the timeline could not be reconstructed from the store." path
