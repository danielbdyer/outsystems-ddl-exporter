module Projection.Tests.QueryHintPassTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Tests.IRBuilders

// ---------------------------------------------------------------------------
// H-076 — Query plan hint annotation (fill factor suggestions)
// ---------------------------------------------------------------------------

let private synthKey (ns: string) (key: string) : SsKey =
    SsKey.synthesized ns key |> Result.value

let private physical (table: string) : PhysicalRealization =
    TableId.create "dbo" table |> Result.value

// All keys derived from a single Kind use the Kind's `rootOriginal`
// projection as their namespace to stay consistent with how
// `Reference.create` derives the SourceAttribute via `rootOriginal`.
let private kindAttrKey (ownerKey: SsKey) (name: string) : SsKey =
    synthKey (SsKey.rootOriginal ownerKey) name

let private mkAttr (ownerKey: SsKey) (name: string) : Attribute =
    let key = kindAttrKey ownerKey name
    Attribute.create key (Name.create name |> Result.value) PrimitiveType.Integer

let private mkRef (ownerKey: SsKey) (targetKey: SsKey) (attrName: string) : Reference =
    let attrKey = kindAttrKey ownerKey attrName
    let refKey  = kindAttrKey ownerKey (attrName + "_ref")
    { Reference.create refKey (Name.create attrName |> Result.value) attrKey targetKey with
        HasDbConstraint = true }

let private mkIndex (ownerKey: SsKey) (name: string) (attrKey: SsKey) (fillFactor: int option) : Index =
    let col = IndexColumn.create attrKey Ascending
    let idx = Index.create (kindAttrKey ownerKey name) (Name.create name |> Result.value) [col]
    { idx with FillFactor = fillFactor }

let private mkSelectivity (refKey: SsKey) (distinctCount: int64) : ForeignKeySelectivity =
    { ReferenceKey  = refKey
      Frequencies   = []
      DistinctCount = distinctCount
      IsTruncated   = true
      ProbeStatus   = ProbeStatus.noProbeRun }

let private buildPairCatalog (fkIdx: Index) (kB: SsKey) (kA: SsKey) (fkRef: Reference) (fkAttr: Attribute) : Catalog =
    let kindA =
        Kind.create kA (Name.create "A" |> Result.value) (physical "A")
            [ mkAttr kA "Id" |> fun a -> { a with IsPrimaryKey = true } ]
    let kindB =
        { Kind.create kB (Name.create "B" |> Result.value) (physical "B")
              [ (mkAttr kB "Id" |> fun a -> { a with IsPrimaryKey = true }); fkAttr ]
          with References = [ fkRef ]
               Indexes    = [ fkIdx ] }
    mkCatalog [ mkModule (synthKey "M" "M") (Name.create "M" |> Result.value) [kindA; kindB] ]

[<Fact>]
let ``empty catalog with empty profile produces empty hint report`` () =
    let result =
        QueryHintPass.run (mkCatalog []) Profile.empty
        |> LineageDiagnostics.payload
    Assert.Empty(result.FillFactorSuggestions)

[<Fact>]
let ``catalog with no profile FK selectivities produces no hints`` () =
    let kA     = synthKey "M" "A"
    let kB     = synthKey "M" "B"
    let fkRef  = mkRef kB kA "FkCol"
    let fkAttr = mkAttr kB "FkCol"
    let fkIdx  = mkIndex kB "idx_fk" (kindAttrKey kB "FkCol") None
    let catalog = buildPairCatalog fkIdx kB kA fkRef fkAttr
    let result =
        QueryHintPass.run catalog Profile.empty
        |> LineageDiagnostics.payload
    Assert.Empty(result.FillFactorSuggestions)

[<Fact>]
let ``FK reference with high selectivity and no fill factor produces a hint`` () =
    let kA     = synthKey "M" "A"
    let kB     = synthKey "M" "B"
    let fkRef  = mkRef kB kA "FkCol"
    let fkAttr = mkAttr kB "FkCol"
    let idxKey = kindAttrKey kB "idx_fk"
    let fkIdx  = mkIndex kB "idx_fk" (kindAttrKey kB "FkCol") None
    let catalog = buildPairCatalog fkIdx kB kA fkRef fkAttr
    let profile =
        { Profile.empty with
            ForeignKeySelectivities = [ mkSelectivity fkRef.SsKey 200L ] }
    let result =
        QueryHintPass.run catalog profile
        |> LineageDiagnostics.payload
    Assert.NotEmpty(result.FillFactorSuggestions)
    let (suggestedIdx, ff) = result.FillFactorSuggestions.[0]
    Assert.Equal(idxKey, suggestedIdx)
    Assert.Equal(70, ff)

[<Fact>]
let ``FK with explicitly set fill factor does not produce a hint`` () =
    let kA     = synthKey "M" "A"
    let kB     = synthKey "M" "B"
    let fkRef  = mkRef kB kA "FkCol"
    let fkAttr = mkAttr kB "FkCol"
    let fkIdxWithFf = mkIndex kB "idx_fk" (kindAttrKey kB "FkCol") (Some 90)
    let catalog = buildPairCatalog fkIdxWithFf kB kA fkRef fkAttr
    let profile =
        { Profile.empty with
            ForeignKeySelectivities = [ mkSelectivity fkRef.SsKey 500L ] }
    let result =
        QueryHintPass.run catalog profile
        |> LineageDiagnostics.payload
    Assert.Empty(result.FillFactorSuggestions)

[<Fact>]
let ``fill factor hint diagnostic has Info severity and correct code`` () =
    let kA     = synthKey "M" "A"
    let kB     = synthKey "M" "B"
    let fkRef  = mkRef kB kA "FkCol"
    let fkAttr = mkAttr kB "FkCol"
    let fkIdx  = mkIndex kB "idx_fk" (kindAttrKey kB "FkCol") None
    let catalog = buildPairCatalog fkIdx kB kA fkRef fkAttr
    let profile =
        { Profile.empty with
            ForeignKeySelectivities = [ mkSelectivity fkRef.SsKey 300L ] }
    let diagnostics =
        QueryHintPass.run catalog profile
        |> LineageDiagnostics.entries
    Assert.NotEmpty(diagnostics)
    for d in diagnostics do
        Assert.Equal(DiagnosticSeverity.Info, d.Severity)
        Assert.Equal("topology.queryHint.fillFactor", d.Code)

[<Fact>]
let ``low selectivity FK below threshold produces no hint`` () =
    let kA     = synthKey "M" "A"
    let kB     = synthKey "M" "B"
    let fkRef  = mkRef kB kA "FkCol"
    let fkAttr = mkAttr kB "FkCol"
    let fkIdx  = mkIndex kB "idx_fk" (kindAttrKey kB "FkCol") None
    let catalog = buildPairCatalog fkIdx kB kA fkRef fkAttr
    // DistinctCount = 50, below the 100 threshold
    let profile =
        { Profile.empty with
            ForeignKeySelectivities = [ mkSelectivity fkRef.SsKey 50L ] }
    let result =
        QueryHintPass.run catalog profile
        |> LineageDiagnostics.payload
    Assert.Empty(result.FillFactorSuggestions)
