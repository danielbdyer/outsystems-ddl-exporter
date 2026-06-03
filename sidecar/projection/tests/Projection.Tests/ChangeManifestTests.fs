module Projection.Tests.ChangeManifestTests

open System
open Xunit
open Projection.Core
open Projection.Tests.Fixtures

type private FsResult<'a, 'b> = Microsoft.FSharp.Core.Result<'a, 'b>

let private mustOk (r: FsResult<'a, 'b>) : 'a =
    match r with
    | FsResult.Ok v -> v
    | FsResult.Error err -> Assert.Fail(sprintf "%A" err); Unchecked.defaultof<'a>

let private mustResultOk (r: Result<'a>) : 'a =
    match r with
    | Ok v -> v
    | Error es -> Assert.Fail(sprintf "%A" es); Unchecked.defaultof<'a>

let private nameOf (s: string) : Name = Name.create s |> mustResultOk
let private ver (o: int) (l: string) : Version = Version.create o l |> mustResultOk
let private tl (name: string) : Timeline = Timeline.create name |> mustResultOk
let private at (iso: string) : DateTimeOffset = DateTimeOffset.Parse(iso, System.Globalization.CultureInfo.InvariantCulture)

// E0 = sample; E1 renames customer→Patron; E2 renames it back (Patron→customer).
// Net displacement E0→E2 is zero (round-trip), but the path length is 2 — the
// churn the change-manifest series surfaces and netSchemaDiff hides.
let private renamedKind : Kind = { customer with Name = nameOf "Patron" }
let private renamedModule : Module = { salesModule with Kinds = [ renamedKind; order; country ] }
let private renamedCatalog : Catalog = IRBuilders.mkCatalog [ renamedModule ]

let private coord o lbl day = EpisodeCoordinate.create (ver o lbl) Environment.Dev (at (sprintf "2026-06-%02dT09:00:00+00:00" day))

let private e0 = Episode.ofSchema (coord 0 "1.0.0" 1) sampleCatalog
let private e1 =
    Episode.create (coord 1 "1.1.0" 8) renamedCatalog Profile.empty (Some "reflog#1") (DataObservation.create 120 (Some "lsn:0x10"))
let private e2 =
    Episode.create (coord 2 "1.2.0" 15) sampleCatalog Profile.empty (Some "reflog#2") (DataObservation.create 95 None)

let private chain : EpisodicLifecycle =
    EpisodicLifecycle.genesis (tl "dev") e0
    |> (fun lc -> EpisodicLifecycle.append e1 lc |> mustResultOk)
    |> (fun lc -> EpisodicLifecycle.append e2 lc |> mustResultOk)

// ===========================================================================
// 6.H.4 — the change-manifest records the displacement, not the target state
// ===========================================================================

[<Fact>]
let ``6.H.4: change-manifest records the displacement (move counts + refactorlog xref + cdc series)`` () =
    let m = ChangeManifest.between e0 e1 |> mustOk
    // The schema move: exactly one renamed kind, norm 1.
    Assert.Equal(1, m.Channels.RenamedKinds)
    Assert.Equal(1, m.SchemaNorm)
    // The Decision anchor is the To-episode's refactorlog reference.
    Assert.Equal(Some "reflog#1", m.RefactorLogRef)
    // The realized data movement is the To-episode's CDC capture series.
    Assert.Equal(120, m.CdcCaptureCount)
    // The endpoints are the two coordinates.
    Assert.Equal(0, Version.ordinal m.From.Version)
    Assert.Equal(1, Version.ordinal m.To.Version)

[<Fact>]
let ``6.H.4: an idempotent edge (no schema change) has norm 0`` () =
    // E1 → an episode with the same schema: zero schema displacement.
    let e1b = Episode.create (coord 3 "1.3.0" 22) renamedCatalog Profile.empty None (DataObservation.create 0 None)
    let m = ChangeManifest.between e1 e1b |> mustOk
    Assert.Equal(0, m.SchemaNorm)
    Assert.Equal(None, m.RefactorLogRef)

[<Fact>]
let ``6.H.4: the series has one manifest per edge`` () =
    let manifests = ChangeManifest.series chain |> mustOk
    Assert.Equal(2, List.length manifests)
    // Edge 0→1 renames; edge 1→2 renames back. Each edge norm is 1.
    Assert.All(manifests, fun m -> Assert.Equal(1, m.SchemaNorm))
    // The CDC series is read off per edge (120 then 95).
    Assert.Equal<int list>([ 120; 95 ], manifests |> List.map (fun m -> m.CdcCaptureCount))

[<Fact>]
let ``6.H.4: a genesis-only lifecycle has an empty change-manifest series`` () =
    let genesisOnly = EpisodicLifecycle.genesis (tl "dev") e0
    Assert.Empty(ChangeManifest.series genesisOnly |> mustOk)

// ===========================================================================
// 6.H.4 — the tolerance residual + applied-transforms outcome (the two
// provenance planes the change-manifest surfaces alongside the displacement).
// ===========================================================================

// E1 enriched with a per-run tolerance residual (two accepted divergences) and
// an applied-transforms outcome (one operator-intent overlay + one skeleton
// row). The manifest built TO this episode must surface both; the manifest
// built to a bare episode must surface neither.
let private tolerances : Tolerance =
    Tolerance.strict
    |> Tolerance.withDivergence ToleratedDivergence.HeaderCommentsOmitted
    |> Tolerance.withDivergence ToleratedDivergence.IndexOptionsUnreflected

let private appliedTransforms : (SsKey * OverlayAxis option) list =
    [ customerKey, Some Tightening
      orderKey,    None ]

let private e1WithProvenance : Episode =
    e1 |> Episode.withProvenance tolerances appliedTransforms

[<Fact>]
let ``6.H.4: the manifest surfaces the To-episode tolerance residual (named, sorted)`` () =
    let m = ChangeManifest.between e0 e1WithProvenance |> mustOk
    // The residual is the To-episode's accepted-divergence set as a name-sorted
    // list — the equivalence under which this edge's displacement was faithful.
    Assert.Equal<ToleratedDivergence list>(
        [ ToleratedDivergence.HeaderCommentsOmitted; ToleratedDivergence.IndexOptionsUnreflected ],
        m.ToleranceResidual)

[<Fact>]
let ``6.H.4: the manifest surfaces the To-episode applied-transforms outcome`` () =
    let m = ChangeManifest.between e0 e1WithProvenance |> mustOk
    Assert.Equal<(SsKey * OverlayAxis option) list>(appliedTransforms, m.AppliedTransforms)

[<Fact>]
let ``6.H.4: a bare episode surfaces an empty residual and empty applied-transforms`` () =
    // E1 with no provenance threaded (strict tolerance, no overlay) → both empty.
    let m = ChangeManifest.between e0 e1 |> mustOk
    Assert.Empty(m.ToleranceResidual)
    Assert.Empty(m.AppliedTransforms)

[<Fact>]
let ``6.H.4: the provenance planes are read off the To-episode, not the From-episode`` () =
    // From-episode carries provenance, To-episode does not: the manifest reads
    // the To side (the displacement's destination), so both surface empty.
    let m = ChangeManifest.between e1WithProvenance e2 |> mustOk
    Assert.Empty(m.ToleranceResidual)
    Assert.Empty(m.AppliedTransforms)

[<Fact>]
let ``6.H.4: path length (sum of edge norms) exceeds net displacement under churn`` () =
    // Path length counts every move: rename + rename-back = 2.
    let path = ChangeManifest.pathLength chain |> mustOk
    Assert.Equal(2, path)
    // Net displacement E0→E2 is zero (the schema returned to genesis).
    let net = EpisodicLifecycle.netSchemaDiff chain |> mustOk
    Assert.Equal(0, CatalogDiff.norm net)
    // The difference (2 - 0) is the timeline's churn.
    Assert.True(path > CatalogDiff.norm net)
