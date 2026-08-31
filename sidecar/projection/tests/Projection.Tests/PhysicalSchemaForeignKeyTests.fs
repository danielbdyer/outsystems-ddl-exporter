module Projection.Tests.PhysicalSchemaForeignKeyTests

open Xunit
open Projection.Core
open Projection.Tests.IRBuilders
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// NM-28 (CLOSED at schema-L3.2) — `PhysicalSchema.toPhysicalForeignKeys`
// reflects a foreign key per LEG. A single-column reference reflects its one
// leg (paired against the target's first PK column, exactly the pre-lift
// shape). A LEG-BEARING composite reference (`Reference.Legs`) reflects one
// `PhysicalForeignKey` entry per leg, both sides resolved explicitly — the
// `CompositePkFkUnreflected` tolerance is RETIRED; the legless-against-
// composite residual is refused upstream by the arity gate
// (`Reference.compositeArityMismatch`, shared with the estate board).
// These tests pin the faithful single-PK case, the per-leg reflection, the
// legless residual's arity refusal, and the NM-28b named drop.
// ---------------------------------------------------------------------------

let private name (s: string) : Name = Name.create s |> Result.value

let private mkOk r =
    match r with
    | Ok v -> v
    | Error es ->
        let codes = es |> List.map (fun (e: ValidationError) -> e.Code) |> String.concat ", "
        invalidOp (sprintf "fixture construction failed: %s" codes)

/// Build an attribute with explicit pk flag (all Integer, NOT NULL).
let private attr (key: SsKey) (logical: string) (isPk: bool) : Attribute =
    { Attribute.create key (name logical) Integer with
        Column       = ColumnRealization.create (logical.ToUpperInvariant()) false |> Result.value
        IsPrimaryKey = isPk }

// -- Keys -------------------------------------------------------------------

let private parentKey = kindKey ["P"]
let private parentA   = attrKey ["P"; "A"]
let private parentB   = attrKey ["P"; "B"]

let private childKey  = kindKey ["Ch"]
let private childId   = attrKey ["Ch"; "Id"]
let private childFk   = attrKey ["Ch"; "ParentRef"]
let private childFk2  = attrKey ["Ch"; "ParentRefB"]
let private childRef  = refKey  ["Ch"; "Parent"]

// -- Catalogs ---------------------------------------------------------------

/// Parent with a SINGLE-column PK (`A`); child FK references it.
let private singlePkParent : Kind =
    { Kind.create parentKey (name "Parent") (mkTableId "dbo" "PARENT")
        [ attr parentA "A" true ] with
        Modality = [] }

/// Parent with a COMPOSITE PK (`A`, `B` in declaration order); child FK
/// references it through the single-column `Reference` IR.
let private compositePkParent : Kind =
    { Kind.create parentKey (name "Parent") (mkTableId "dbo" "PARENT")
        [ attr parentA "A" true
          attr parentB "B" true ] with
        Modality = [] }

let private child : Kind =
    { Kind.create childKey (name "Child") (mkTableId "dbo" "CHILD")
        [ attr childId "Id"        true
          attr childFk "ParentRef" false ] with
        References = [ Reference.create childRef (name "Parent") childFk parentKey ] }

let private catalogWith (parent: Kind) : Catalog =
    Catalog.create [ mkModule (modKey "M") (name "M") [ parent; child ] ] [] |> mkOk

// ---------------------------------------------------------------------------

[<Fact>]
let ``NM-28: a single-PK target FK reflects its one leg faithfully`` () =
    let phys = PhysicalSchema.ofCatalog (catalogWith singlePkParent)
    let fks = phys.ForeignKeys |> Set.toList
    let fk = Assert.Single fks
    Assert.Equal ("CHILD", fk.SourceTable)
    Assert.Equal ("PARENTREF", fk.SourceColumn)
    Assert.Equal ("PARENT", fk.TargetTable)
    Assert.Equal ("A", fk.TargetColumn)

[<Fact>]
let ``NM-28 closure: a leg-bearing composite-FK reflects one PhysicalForeignKey per leg`` () =
    // schema-L3.2 — the child carries a second FK column and the reference
    // carries BOTH legs explicitly; the projection reflects both, in the
    // target columns the legs name (nothing derived from the PK).
    let child2 : Kind =
        { Kind.create childKey (name "Child") (mkTableId "dbo" "CHILD")
            [ attr childId "Id" true
              attr childFk "ParentRef" false
              attr childFk2 "ParentRefB" false ] with
            References =
                [ { Reference.create childRef (name "Parent") childFk parentKey with
                      Legs =
                        [ { SourceAttribute = childFk; TargetAttribute = parentA }
                          { SourceAttribute = childFk2; TargetAttribute = parentB } ] } ] }
    let catalog = Catalog.create [ mkModule (modKey "M") (name "M") [ compositePkParent; child2 ] ] [] |> mkOk
    let fks = PhysicalSchema.ofCatalog catalog |> fun p -> p.ForeignKeys |> Set.toList
    Assert.Equal (2, List.length fks)
    Assert.Contains (fks, fun f -> f.SourceColumn = "PARENTREF" && f.TargetColumn = "A")
    Assert.Contains (fks, fun f -> f.SourceColumn = "PARENTREFB" && f.TargetColumn = "B")

[<Fact>]
let ``NM-28 residual: a LEGLESS composite-PK target FK reflects its single first-leg pairing (the arity gate refuses its deploy)`` () =
    let phys = PhysicalSchema.ofCatalog (catalogWith compositePkParent)
    let fks = phys.ForeignKeys |> Set.toList
    // One entry, first PK leg — faithful to the arity-gated shape: this
    // reference cannot DEPLOY (the emitter refuses; the board reds via the
    // SAME predicate), so reflecting one leg reflects what would exist.
    let fk = Assert.Single fks
    Assert.Equal ("A", fk.TargetColumn)
    Assert.DoesNotContain (fks, fun f -> f.TargetColumn = "B")

[<Fact>]
let ``schema-L3.2: compositeArityMismatch — legless-vs-composite refuses; leg-complete passes; single-PK passes`` () =
    let legless = Reference.create childRef (name "Parent") childFk parentKey
    let legComplete =
        { legless with
            Legs =
                [ { SourceAttribute = childFk; TargetAttribute = parentA }
                  { SourceAttribute = childFk2; TargetAttribute = parentB } ] }
    Assert.True (Reference.compositeArityMismatch compositePkParent legless)
    Assert.False (Reference.compositeArityMismatch compositePkParent legComplete)
    Assert.False (Reference.compositeArityMismatch singlePkParent legless)
    // A leg list of the WRONG width against a composite PK also refuses.
    let oneLeg = { legless with Legs = [ { SourceAttribute = childFk; TargetAttribute = parentA } ] }
    Assert.True (Reference.compositeArityMismatch compositePkParent oneLeg)

[<Fact>]
let ``NM-28b: an FK whose target has NO primary key is dropped (no PhysicalForeignKey)`` () =
    // Parent with no PK column at all — there is no key for the FK to reference,
    // so toPhysicalForeignKeys drops it (the empty-PK-list branch). Documented
    // as NM-28b; surfacing it as a Core diagnostic needs a PhysicalSchema
    // diagnostics channel (flagged, not landed).
    let pklessParent : Kind =
        { Kind.create parentKey (name "Parent") (mkTableId "dbo" "PARENT")
            [ attr parentA "A" false ] with
            Modality = [] }
    let phys = PhysicalSchema.ofCatalog (catalogWith pklessParent)
    Assert.Empty phys.ForeignKeys
