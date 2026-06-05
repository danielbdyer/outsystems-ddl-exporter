namespace Projection.Pipeline

open System.Threading.Tasks
open Projection.Core
open Projection.Adapters.Osm

/// Frontier base — the capability-typed input boundary. A `Source` resolves a
/// `Catalog` from somewhere (a snapshot file, an inline JSON model, a live
/// connection), and **declares what it can do**. The discriminating predicate
/// lives in the type, exactly as `Comparison.Apply` does: a capability IS the
/// presence of its function — `AcquireProfile = Some f` iff the source can
/// profile; `None` for a snapshot. A consumer that needs profiling pattern-
/// matches `profile src` and finds `None` for a snapshot, so asking a static
/// model to profile is structurally impossible, not a runtime failure.
///
/// This SUPPORTS live OSSYS (a `Source` with the live capabilities) without
/// completing it — the live *adapter* is the verb; this is the port it plugs
/// into. `CatalogReader.SnapshotSource` is the seed it generalizes.
module Source =

    type Capability =
        | ReadCatalog
        | Profile
        | Live
        | Cdc

    type Source = {
        /// What this source is (`file:…`, `json:inline`, `live://…`).
        Identity       : string
        Capabilities   : Set<Capability>
        /// Always present — every source reads a catalog.
        ReadCatalog    : unit -> Task<Result<Catalog>>
        /// Present iff the source can profile (the capability, reified).
        AcquireProfile : (Catalog -> Task<Result<Profile>>) option
    }

    let private snapshot (identity: string) (src: CatalogReader.SnapshotSource) : Source =
        { Identity       = identity
          Capabilities   = Set.ofList [ ReadCatalog ]
          ReadCatalog    = (fun () -> CatalogReader.parse src)
          AcquireProfile = None }

    /// A static-model source from a snapshot file — reads only, no profiling.
    let ofFile (path: string) : Source =
        snapshot ("file:" + path) (CatalogReader.SnapshotFile path)

    /// A static-model source from an inline JSON model — reads only.
    let ofJson (json: string) : Source =
        snapshot "json:inline" (CatalogReader.SnapshotJson json)

    /// Enrich a source with the `Profile` capability (the live / profilable
    /// form). The capability is the presence of the function — cf.
    /// `Comparison.Apply`. The live-connection adapter calls this to declare
    /// it can profile.
    let withProfile (acquire: Catalog -> Task<Result<Profile>>) (s: Source) : Source =
        { s with
            Capabilities   = Set.add Profile s.Capabilities
            AcquireProfile = Some acquire }

    /// Declare an additional capability (e.g. `Live` / `Cdc`) on a source.
    let withCapability (c: Capability) (s: Source) : Source =
        { s with Capabilities = Set.add c s.Capabilities }

    let has (c: Capability) (s: Source) : bool = Set.contains c s.Capabilities
    let canProfile (s: Source) : bool = Option.isSome s.AcquireProfile

    let read (s: Source) : Task<Result<Catalog>> = s.ReadCatalog ()

    /// The profile action — `Some` exactly when the source can profile.
    let profile (s: Source) : (Catalog -> Task<Result<Profile>>) option = s.AcquireProfile
