module Projection.Tests.MigrationTests

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

let private nm (s: string) : Name = Name.create s |> mustResultOk
let private key (n: int) : SsKey =
    SsKey.ossysOriginal (System.Guid(n, 0s, 0s, 0uy, 0uy, 0uy, 0uy, 0uy, 0uy, 0uy, 0uy))

// -- targets, each isolating one move channel ------------------------------

/// Rename `customer` → `Patron` (SsKey preserved, A1) — the RefactorLog channel.
let private renamedTarget : Catalog =
    IRBuilders.mkCatalog [ { salesModule with Kinds = [ { customer with Name = nm "Patron" }; order; country ] } ]

/// Flip the first attribute's nullability — a shape reshape (the ALTER channel).
let private reshapeCustomer (f: Attribute -> Attribute) : Catalog =
    let c' = { customer with Attributes = customer.Attributes |> List.mapi (fun i a -> if i = 0 then f a else a) }
    IRBuilders.mkCatalog [ { salesModule with Kinds = [ c'; order; country ] } ]

let private reshapedTarget : Catalog =
    reshapeCustomer (fun a -> { a with Column = { a.Column with IsNullable = not a.Column.IsNullable } })

/// Flip IsIdentity — a NON-shape facet the schema emitter refuses (it cannot be
/// expressed as a single ALTER COLUMN).
let private nonShapeReshapeTarget : Catalog =
    reshapeCustomer (fun a -> { a with IsIdentity = not a.IsIdentity })

/// A standalone leaf kind (no references in or out) so it can be added/removed
/// without disturbing referential integrity.
let private extraKind : Kind =
    Kind.create (key 9001) (nm "Extra") (TableId.create "dbo" "Extra" |> mustResultOk)
        [ Attribute.create (key 9002) (nm "Id") PrimitiveType.Integer ]

let private withExtraKind (c: Catalog) : Catalog =
    let m0 = List.head c.Modules
    let m0' = { m0 with Kinds = m0.Kinds @ [ extraKind ] }
    Catalog.create (m0' :: List.tail c.Modules) c.Sequences |> mustResultOk

// ===========================================================================
// T16 — the master equation: migrate A B reproduces B (the Project square)
// ===========================================================================

[<Fact>]
let ``T16: applyTo (plan A B) A = B — migrate A B reproduces the target (master equation)`` () =
    for target in [ renamedTarget; reshapedTarget; withExtraKind sampleCatalog ] do
        let plan = Migration.plan DeclareAll sampleCatalog target |> mustOk
        let reproduced = Migration.applyTo plan sampleCatalog
        // B reproduced modulo the diff's captured surface.
        Assert.True(CatalogDiff.isEmpty (CatalogDiff.between target reproduced))

[<Fact>]
let ``T16: migrate A A is idempotent — zero minimum-viable touches`` () =
    let plan = Migration.plan DeclareNone sampleCatalog sampleCatalog |> mustOk
    Assert.True(Migration.isIdempotent plan)
    Assert.Equal(0, plan.Preview.Norm)
    Assert.True(Migration.isSafe plan)

// ===========================================================================
// Minimum-viable touches — the preview names exactly what changed, by channel
// ===========================================================================

[<Fact>]
let ``migrate: a rename is the RefactorLog channel (one renamed kind, norm 1), not a drop+add`` () =
    let plan = Migration.plan DeclareNone sampleCatalog renamedTarget |> mustOk
    Assert.Equal(1, plan.Preview.Channels.RenamedKinds)
    Assert.Equal(0, plan.Preview.Channels.AddedKinds)
    Assert.Equal(0, plan.Preview.Channels.RemovedKinds)
    Assert.Equal(1, plan.Preview.Norm)
    let (_, fromN, toN) = List.exactlyOne plan.Preview.RenamedKinds
    Assert.Equal("Customer", Name.value fromN)
    Assert.Equal("Patron", Name.value toN)
    // A rename carries no data violation (‖rename‖_data = 0, A43).
    Assert.True(Migration.isSafe plan)

[<Fact>]
let ``migrate: a reshape is the ALTER channel (one changed attribute), touching only the changed facet`` () =
    let plan = Migration.plan DeclareNone sampleCatalog reshapedTarget |> mustOk
    Assert.Equal(1, plan.Preview.Channels.ChangedAttributes)
    Assert.Equal(0, plan.Preview.Channels.RenamedKinds)
    let (_, _, facets) = List.exactlyOne plan.Preview.ReshapedAttributes
    Assert.Equal<Set<AttributeFacet>>(Set.singleton AttributeFacet.Nullability, facets)

[<Fact>]
let ``migrate: an added kind is the Added channel`` () =
    let plan = Migration.plan DeclareNone sampleCatalog (withExtraKind sampleCatalog) |> mustOk
    Assert.Equal(1, plan.Preview.Channels.AddedKinds)
    Assert.Equal<SsKey list>([ key 9001 ], plan.Preview.AddedKinds)
    Assert.True(Migration.isSafe plan)

// ===========================================================================
// Fail-loud on violation — destructive drops refuse unless opted in
// ===========================================================================

[<Fact>]
let ``migrate: a dropped kind is a fail-loud violation unless allowDrops`` () =
    let sourceWithExtra = withExtraKind sampleCatalog
    // source has Extra, target does not → Extra is dropped.
    let plan = Migration.plan DeclareNone sourceWithExtra sampleCatalog |> mustOk
    Assert.False(Migration.isSafe plan)
    match plan.Violations with
    | [ SchemaLoss.DropKind k ] ->
        Assert.Equal(key 9001, k)
        // The declarable handle the operator copies into --declare-drop.
        Assert.Equal(sprintf "kind:%s" (SsKey.rootOriginal k), Migration.lossToken (SchemaLoss.DropKind k))
    | other -> Assert.Fail(sprintf "expected one SchemaLoss.DropKind, got %A" other)

[<Fact>]
let ``migrate: allowDrops clears the violation (operator accepts the data loss)`` () =
    let sourceWithExtra = withExtraKind sampleCatalog
    let plan = Migration.plan DeclareAll sourceWithExtra sampleCatalog |> mustOk
    Assert.True(Migration.isSafe plan)
    Assert.Empty(plan.Violations)
    // The displacement still records the removal (the preview is honest).
    Assert.Equal(1, plan.Preview.Channels.RemovedKinds)

[<Fact>]
let ``migrate: a dropped attribute is a fail-loud violation unless allowDrops`` () =
    // Source = sample; target drops the first attribute of customer.
    let dropAttrTarget =
        let c' = { customer with Attributes = List.tail customer.Attributes }
        IRBuilders.mkCatalog [ { salesModule with Kinds = [ c'; order; country ] } ]
    let plan = Migration.plan DeclareNone sampleCatalog dropAttrTarget |> mustOk
    Assert.False(Migration.isSafe plan)
    Assert.Contains(plan.Violations, fun v -> match v with SchemaLoss.DropAttribute _ -> true | _ -> false)

[<Fact>]
let ``migrate: destructiveLosses spans the FK channel (P-GATE completeness) — the prior kind+attr gate missed references/indexes/sequences`` () =
    // Target = sample but `order` loses its FK to customer. The OLD violationsOf
    // (kinds + attributes only) returned [] here — a silent gap; the complete
    // enumeration surfaces it as a DropReference.
    let droppedFkTarget =
        let order' = { order with References = [] }
        IRBuilders.mkCatalog [ { salesModule with Kinds = [ customer; order'; country ] } ]
    let plan = Migration.plan DeclareNone sampleCatalog droppedFkTarget |> mustOk
    Assert.False(Migration.isSafe plan)
    Assert.Contains(plan.Violations, fun v -> match v with SchemaLoss.DropReference _ -> true | _ -> false)

[<Fact>]
let ``migrate: DeclareThese clears the named loss but still refuses an undeclared sibling (granular gate, engine-side)`` () =
    // Source carries two extra leaf kinds; the target drops both.
    let extra2 =
        Kind.create (key 9101) (nm "Extra2") (TableId.create "dbo" "Extra2" |> mustResultOk)
            [ Attribute.create (key 9102) (nm "Id") PrimitiveType.Integer ]
    let withTwoExtras (c: Catalog) =
        let m0 = List.head c.Modules
        Catalog.create ({ m0 with Kinds = m0.Kinds @ [ extraKind; extra2 ] } :: List.tail c.Modules) c.Sequences
        |> mustResultOk
    let source = withTwoExtras sampleCatalog
    // Declare ONLY the first extra's token; the second stays undeclared.
    let declared = Set.singleton (Migration.lossToken (SchemaLoss.DropKind (key 9001)))
    let plan = Migration.plan (DeclareThese declared) source sampleCatalog |> mustOk
    Assert.False(Migration.isSafe plan)
    match plan.Violations with
    | [ SchemaLoss.DropKind k ] -> Assert.Equal(key 9101, k)   // only the undeclared Extra2 remains
    | other -> Assert.Fail(sprintf "expected only the undeclared Extra2 drop, got %A" other)
    // Declaring BOTH tokens makes the plan safe.
    let bothDeclared =
        [ key 9001; key 9101 ] |> List.map (fun k -> Migration.lossToken (SchemaLoss.DropKind k)) |> Set.ofList
    let plan2 = Migration.plan (DeclareThese bothDeclared) source sampleCatalog |> mustOk
    Assert.True(Migration.isSafe plan2)

// ===========================================================================
// Recording — a migration becomes a durable episode (6.H provenance)
// ===========================================================================

[<Fact>]
let ``migrate: toEpisode records the target as the new schema plane`` () =
    let plan = Migration.plan DeclareAll sampleCatalog renamedTarget |> mustOk
    let coord = EpisodeCoordinate.create (Version.create 1 "1.1.0" |> mustResultOk) Environment.Dev (DateTimeOffset.Parse "2026-06-08T09:00:00+00:00")
    let episode = Migration.toEpisode coord (Some "reflog#1") (DataObservation.create 30 (Some "lsn:0x05")) plan
    Assert.Equal<Catalog>(renamedTarget, episode.Schema)
    Assert.Equal(Some "reflog#1", episode.RefactorLogRef)
    Assert.Equal(30, episode.Data.CdcCaptureCount)
