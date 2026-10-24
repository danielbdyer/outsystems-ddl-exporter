namespace Projection.Pipeline

open Projection.Core
open Projection.Core.Passes

/// The ONE authored model at a named rendition — the reverse-leg contract
/// source (J3 / THE_DATA_PRODUCERS §6 LE-1). The B→A `legacy` leg needs two
/// SsKey-ALIGNED contracts: the logical on-prem source (B) and the physical
/// OSUSR cloud sink (A). A live read cannot produce them (`ReadSide`
/// synthesizes attribute SsKeys from physical coordinates, so two
/// independent reads never align); rendering the one model at each rendition
/// aligns them BY CONSTRUCTION — both passes preserve `SsKey` (A1) and touch
/// only the physical-realization slots, so the pair differs exactly on the
/// coordinates `Transfer.runWithRenames`'s identity-matched repoint re-points.
[<RequireQualifiedAccess>]
module CatalogRendition =

    /// The PHYSICAL rendition (A — the OSUSR cloud estate): the catalog as
    /// authored. `Kind.Physical` / `Attribute.Column` already carry the
    /// physical coordinates; the arm is the identity, named so a call site
    /// reads as the rendition pair it constructs.
    let physical (model: Catalog) : Catalog = model

    /// The LOGICAL rendition (B — the published on-prem estate): the authored
    /// model through the same two emission-axis substitutions the down-leg
    /// publish applies (`LogicalTableEmission` + `LogicalColumnEmission`,
    /// `Enabled` — the production default), so the contract names tables and
    /// columns exactly as the published bundle deployed them. The passes'
    /// lineage events are not propagated: contract derivation is a read of
    /// the model at a rendition, not a step of an emission run.
    let logical (model: Catalog) : Catalog =
        let runPass (t: RegisteredTransform<Catalog, Catalog>) (c: Catalog) : Catalog =
            t.Run c |> LineageDiagnostics.payload
        model
        |> runPass (LogicalTableEmission.registered LogicalTableEmission.Enabled)
        |> runPass (LogicalColumnEmission.registered LogicalColumnEmission.Enabled)
