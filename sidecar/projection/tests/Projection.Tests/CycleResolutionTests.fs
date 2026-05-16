module Projection.Tests.CycleResolutionTests

open Xunit
open Projection.Core
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// classify — V1 RDBMS-flavored rule mapping (IsNullable, OnDelete) to
// EdgeStrength. Pure function of two IR fields; testable in isolation.
// ---------------------------------------------------------------------------

[<Fact>]
let ``classify: non-nullable + NoAction = Other`` () =
    // The synthetic fixture's Order.CustomerId attribute is non-nullable
    // and the reference's OnDelete is NoAction.
    let result = CycleResolution.classify order (List.head order.References)
    Assert.Equal<EdgeStrength>(EdgeStrength.Other, result)

let private mkAttr (key: string) (nullable: bool) : Attribute =
    { SsKey        = testKey key
      Name         = Name.create "Fk" |> Result.value
      Type         = Integer
      Column       = { ColumnName = "FK"; IsNullable = nullable }
      IsPrimaryKey = false; IsMandatory = false; Length = None; Precision = None; Scale = None; IsIdentity = false; Description = None; IsActive = true; DefaultValue = None; Computed = None; ExtendedProperties = [] }

let private mkRef (sourceAttrKey: string) (action: ReferenceAction) : Reference =
    { SsKey           = refKey ["x"]
      Name            = Name.create "x" |> Result.value
      SourceAttribute = testKey sourceAttrKey
      TargetKind      = kindKey ["target"]
      OnDelete        = action
      IsUserFk        = false }

let private kindWith (a: Attribute) : Kind =
    { SsKey      = kindKey ["owner"]
      Name       = Name.create "owner" |> Result.value
      Origin     = OsNative
      Modality   = []
      Physical   = { Schema = "dbo"; Table = "owner"; Catalog = None }
      Attributes = [ a ]
      References = []; Indexes = []; Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }

[<Fact>]
let ``classify: nullable + NoAction = Weak`` () =
    let attr = mkAttr "OS_ATTR_fk" true
    let r = mkRef "OS_ATTR_fk" NoAction
    let k = kindWith attr
    Assert.Equal<EdgeStrength>(EdgeStrength.Weak, CycleResolution.classify k r)

[<Fact>]
let ``classify: nullable + SetNull = Weak`` () =
    let attr = mkAttr "OS_ATTR_fk" true
    let r = mkRef "OS_ATTR_fk" SetNull
    let k = kindWith attr
    Assert.Equal<EdgeStrength>(EdgeStrength.Weak, CycleResolution.classify k r)

[<Fact>]
let ``classify: nullable + Cascade = Cascade (Cascade overrides nullability)`` () =
    let attr = mkAttr "OS_ATTR_fk" true
    let r = mkRef "OS_ATTR_fk" ReferenceAction.Cascade
    let k = kindWith attr
    Assert.Equal<EdgeStrength>(EdgeStrength.Cascade, CycleResolution.classify k r)

[<Fact>]
let ``classify: non-nullable + Cascade = Cascade`` () =
    let attr = mkAttr "OS_ATTR_fk" false
    let r = mkRef "OS_ATTR_fk" ReferenceAction.Cascade
    let k = kindWith attr
    Assert.Equal<EdgeStrength>(EdgeStrength.Cascade, CycleResolution.classify k r)

[<Fact>]
let ``classify: non-nullable + SetNull = Other`` () =
    let attr = mkAttr "OS_ATTR_fk" false
    let r = mkRef "OS_ATTR_fk" SetNull
    let k = kindWith attr
    Assert.Equal<EdgeStrength>(EdgeStrength.Other, CycleResolution.classify k r)

[<Fact>]
let ``classify: non-nullable + Restrict = Other`` () =
    let attr = mkAttr "OS_ATTR_fk" false
    let r = mkRef "OS_ATTR_fk" Restrict
    let k = kindWith attr
    Assert.Equal<EdgeStrength>(EdgeStrength.Other, CycleResolution.classify k r)

[<Fact>]
let ``classify: nullable + Restrict = Other (Restrict not breakable)`` () =
    // Restrict means "refuse to delete the parent"; even with a
    // nullable source, the FK semantics don't permit silent breakage.
    let attr = mkAttr "OS_ATTR_fk" true
    let r = mkRef "OS_ATTR_fk" Restrict
    let k = kindWith attr
    Assert.Equal<EdgeStrength>(EdgeStrength.Other, CycleResolution.classify k r)

// ---------------------------------------------------------------------------
// asymmetric2CycleStrategy — V1's resolver heuristic. Pure function of
// SCC members + classified internal edges.
// ---------------------------------------------------------------------------

let private aKey = testKey "A"
let private bKey = testKey "B"
let private cKey = testKey "C"

[<Fact>]
let ``resolver: 2-cycle with exactly one Weak edge returns that edge`` () =
    let edges =
        [ (aKey, bKey), EdgeStrength.Weak
          (bKey, aKey), EdgeStrength.Other ]
    let step = CycleResolution.asymmetric2CycleStrategy [ aKey; bKey ] edges
    Assert.Equal<(SsKey * SsKey) list>([ (aKey, bKey) ], step.EdgesToBreak)
    Assert.Contains("auto-resolved", step.Reason)

[<Fact>]
let ``resolver: 2-cycle with no Weak edges returns empty and explains`` () =
    let edges =
        [ (aKey, bKey), EdgeStrength.Other
          (bKey, aKey), EdgeStrength.Other ]
    let step = CycleResolution.asymmetric2CycleStrategy [ aKey; bKey ] edges
    Assert.Empty(step.EdgesToBreak)
    Assert.Contains("no Weak edge", step.Reason)

[<Fact>]
let ``resolver: 2-cycle with two Weak edges returns empty and refuses to choose`` () =
    let edges =
        [ (aKey, bKey), EdgeStrength.Weak
          (bKey, aKey), EdgeStrength.Weak ]
    let step = CycleResolution.asymmetric2CycleStrategy [ aKey; bKey ] edges
    Assert.Empty(step.EdgesToBreak)
    Assert.Contains("multiple Weak edges", step.Reason)

[<Fact>]
let ``resolver: 2-cycle with Cascade alongside Weak still uses the Weak edge`` () =
    // Cascade is structural; Weak is the only breakable choice.
    let edges =
        [ (aKey, bKey), EdgeStrength.Weak
          (bKey, aKey), EdgeStrength.Cascade ]
    let step = CycleResolution.asymmetric2CycleStrategy [ aKey; bKey ] edges
    Assert.Equal<(SsKey * SsKey) list>([ (aKey, bKey) ], step.EdgesToBreak)

[<Fact>]
let ``resolver: 3-cycle returns empty and notes the size`` () =
    let edges =
        [ (aKey, bKey), EdgeStrength.Weak
          (bKey, cKey), EdgeStrength.Weak
          (cKey, aKey), EdgeStrength.Weak ]
    let step =
        CycleResolution.asymmetric2CycleStrategy [ aKey; bKey; cKey ] edges
    Assert.Empty(step.EdgesToBreak)
    Assert.Contains("size 3", step.Reason)

[<Fact>]
let ``resolver: empty-edges 2-cycle (degenerate) returns empty`` () =
    let step = CycleResolution.asymmetric2CycleStrategy [ aKey; bKey ] []
    Assert.Empty(step.EdgesToBreak)
    Assert.Contains("no Weak edge", step.Reason)

// ---------------------------------------------------------------------------
// neverResolve — opt-out resolver for callers that prefer alphabetical
// fallback over any heuristic edge-breaking.
// ---------------------------------------------------------------------------

[<Fact>]
let ``neverResolve: returns empty for any SCC`` () =
    let edges =
        [ (aKey, bKey), EdgeStrength.Weak
          (bKey, aKey), EdgeStrength.Weak ]
    let step = CycleResolution.neverResolve [ aKey; bKey ] edges
    Assert.Empty(step.EdgesToBreak)
    Assert.Contains("disabled", step.Reason)

[<Fact>]
let ``neverResolve: notes the SCC size in the diagnostic`` () =
    let step = CycleResolution.neverResolve [ aKey; bKey; cKey ] []
    Assert.Contains("size 3", step.Reason)

// ---------------------------------------------------------------------------
// Resolver type-shape — a Resolver is `SsKey list -> ((SsKey * SsKey) *
// EdgeStrength) list -> ResolutionStep`. Calling sites can pass any
// function of that shape; the algebra in TopologicalOrderPass doesn't
// know which strategy is in use.
// ---------------------------------------------------------------------------

[<Fact>]
let ``resolver shape: a custom resolver can be passed by callers`` () =
    let alwaysFirst : CycleResolution.Resolver =
        fun _members internalEdges ->
            match internalEdges with
            | (edge, _) :: _ ->
                { EdgesToBreak = [ edge ]
                  Reason       = "always break the first edge (custom)" }
            | [] ->
                { EdgesToBreak = []
                  Reason       = "no edges" }
    let edges =
        [ (aKey, bKey), EdgeStrength.Other
          (bKey, aKey), EdgeStrength.Other ]
    let step = alwaysFirst [ aKey; bKey ] edges
    Assert.Equal<(SsKey * SsKey) list>([ (aKey, bKey) ], step.EdgesToBreak)
    Assert.Contains("custom", step.Reason)
