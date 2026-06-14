module Projection.Tests.PhysicalSchemaForeignKeyTests

open Xunit
open Projection.Core
open Projection.Tests.IRBuilders
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// NM-28 — `PhysicalSchema.toPhysicalForeignKeys` reflects a foreign key on the
// FIRST leg of its target's primary key. For a single-column target PK the
// reflection is complete and round-trips faithfully. For a COMPOSITE target PK
// only the first leg is reflected — V2's `Reference` IR is single-column per
// chapter 5.0, so there is no source column to pair the second target PK leg
// against. That residual is the named, closed tolerance
// `ToleratedDivergence.CompositePkFkUnreflected` (not a silent first-element
// pick). These tests pin both the faithful single-PK case and the named gap.
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
let ``NM-28: a composite-PK target FK reflects ONLY the first leg (CompositePkFkUnreflected)`` () =
    let phys = PhysicalSchema.ofCatalog (catalogWith compositePkParent)
    let fks = phys.ForeignKeys |> Set.toList
    // Exactly one PhysicalForeignKey — the first PK leg (`A`). The second leg
    // (`B`) is UNREFLECTED: the single-column Reference IR has no source column
    // to pair it against. No entry mentions `B`.
    let fk = Assert.Single fks
    Assert.Equal ("A", fk.TargetColumn)
    Assert.DoesNotContain (fks, fun f -> f.TargetColumn = "B")
    // The residual is a named, known tolerance — not a silent collapse.
    Assert.Contains (ToleratedDivergence.CompositePkFkUnreflected, ToleratedDivergence.allKnown)
    Assert.Equal ("CompositePkFkUnreflected", ToleratedDivergence.name ToleratedDivergence.CompositePkFkUnreflected)

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
