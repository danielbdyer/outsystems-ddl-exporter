namespace Projection.Pipeline

open Projection.Core

/// One witnessed source's TERMINAL sink state, as the eject carries it
/// (the data-sink chapter, S15/K10): after the eject there is no upstream
/// to re-derive from, so the witnessed metadata editions and their
/// displacement journals ride the package — the post-eject team can still
/// answer "what was registered, when, and what displaced it" from the
/// store the package names.
type SinkTerminalState =
    {
        Digest         : string
        EnvLabel       : string option
        LatestSyncId   : int
        JournalEntries : int
        CapturedAtUtc  : System.DateTimeOffset
    }

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
        /// The sink's terminal witnessed states (S15/K10) — stamped by the
        /// face from the configured store (`withSinkStates`); empty when no
        /// sink store rides the run (the pre-sink eject, byte-identical).
        SinkStates          : SinkTerminalState list
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
                  ReconstructedSchema = reconstructed
                  SinkStates = [] }

    /// Stamp the sink's terminal witnessed states onto a package (the
    /// `withSinkClaims` idiom — the assembly stays pure; the face collects).
    /// Empty is the identity: a pre-sink eject is byte-identical.
    let withSinkStates (states: SinkTerminalState list) (pkg: EjectPackage) : EjectPackage =
        if List.isEmpty states then pkg else { pkg with SinkStates = states }

    /// Collect the terminal witnessed states from the configured sink store
    /// (store-gated: no store, no states — the named pre-sink shape). One
    /// row per witnessed source, digest-ordered for T1 determinism; each
    /// carries its journal length (the displacement history's size, the
    /// FTC's evidence mass).
    let sinkTerminalStates () : SinkTerminalState list =
        match EstateStoreLocation.storeDir () with
        | None -> []
        | Some root ->
            SinkStore.manifests root
            |> List.map (fun m ->
                { Digest = m.ConnDigest
                  EnvLabel = m.EnvLabel
                  LatestSyncId = m.LatestSyncId
                  JournalEntries =
                      SinkJournal.load (SinkStore.journalPath root m.ConnDigest)
                      |> Result.map List.length
                      |> Result.defaultValue 0
                  CapturedAtUtc = m.CapturedAtUtc })
            |> List.sortBy (fun s -> s.Digest)

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
