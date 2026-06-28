module Projection.Cli.Faces.Common

// Cross-face helpers shared by the run faces — the spine the per-verb Faces/*.fs
// files sit on (recon #3). Compiled before RunFaces so both the coupled core
// (transfer / reverse-leg narration) and the extracted families (verify-data)
// share ONE definition instead of a private copy stranded in the wall. This is
// the seed of the `FacesCommon` the remaining coupled-core extraction grows.

open Projection.Core

/// Resolve an `SsKey` to its `Name` via the catalog name-index, falling back to
/// the honest `rootOriginal` (a bare GUID for an `OssysOriginal` key) when the
/// key is absent. The reconciliation / integrity / load-plan narration surfaces
/// share this so a real OSSYS estate doesn't render as a wall of hex.
let nameOf (names: Map<SsKey, string>) (key: SsKey) : string =
    Map.tryFind key names |> Option.defaultValue (SsKey.rootOriginal key)
