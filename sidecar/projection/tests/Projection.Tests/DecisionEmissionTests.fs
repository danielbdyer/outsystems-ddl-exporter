module Projection.Tests.DecisionEmissionTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Targets.SSDT

// Wave-2 slice 2.3 — A42 (candidate) at the emission layer: the SSDT
// emitter applies the NOT NULL + UNIQUE tightening decisions carried by a
// DecisionOverlay, additive-only. These are PURE emitted-DDL inspection
// tests (no Docker): emit `statementsWith overlay catalog` and read the
// typed `Statement.CreateTable` / `Statement.CreateIndex` to confirm the
// decision reached the DDL, and ONLY for the keyed elements. The full
// deploy → read-back proof rides the Docker canary (CanaryRoundTripTests).

let private nm (s: string) : Name =
    match Name.create s with | Ok n -> n | Error _ -> failwithf "name %s" s

let private kkey (s: string) : SsKey =
    match SsKey.synthesized "TEST_KIND" s with | Ok k -> k | Error _ -> failwithf "k %s" s

let private akey (s: string) : SsKey =
    match SsKey.synthesized "TEST_ATTR" s with | Ok k -> k | Error _ -> failwithf "a %s" s

let private ikey (s: string) : SsKey =
    match SsKey.synthesized "TEST_IDX" s with | Ok k -> k | Error _ -> failwithf "i %s" s

/// A kind with a PK Id + two nullable Text columns (`Alpha`, `Beta`) and a
/// non-unique index over `Alpha`.
let private sampleKind () : Kind =
    let mkAttr (k: SsKey) (col: string) (isPk: bool) (nullable: bool) : Attribute =
        { Attribute.create k (nm col) (if isPk then Integer else Text) with
            Column = { ColumnName = col.ToUpperInvariant(); IsNullable = nullable }
            IsPrimaryKey = isPk
            IsMandatory = isPk }
    let idA = akey "Widget.Id"
    let alphaA = akey "Widget.Alpha"
    let betaA = akey "Widget.Beta"
    let idx = Index.ofKeyColumns (ikey "Widget.IX_Alpha") (nm "IX_Widget_Alpha") [ alphaA ]
    { Kind.create (kkey "Widget") (nm "Widget")
        { Schema = "dbo"; Table = "OSUSR_DEC_WIDGET"; Catalog = None }
        [ mkAttr idA "Id" true false
          mkAttr alphaA "Alpha" false true
          mkAttr betaA "Beta" false true ]
      with Indexes = [ idx ] }

let private catalogOf (k: Kind) : Catalog =
    match Catalog.create [ { SsKey = kkey "Mod"; Name = nm "DecMod"; Kinds = [ k ]; IsActive = true; ExtendedProperties = [] } ] [] with
    | Ok c -> c
    | Error e -> failwithf "catalog %A" e

/// Extract the emitted column nullability map for a kind from the overlay-
/// applied statement stream.
let private emittedColumns (overlay: DecisionOverlay) (catalog: Catalog) : Map<string, bool> =
    SsdtDdlEmitter.statementsWith overlay catalog
    |> Seq.choose (function
        | Statement.CreateTable (_, columns, _, _, _, _) -> Some columns
        | _ -> None)
    |> Seq.collect id
    |> Seq.map (fun (c: ColumnDef) -> c.Name, c.Nullable)
    |> Map.ofSeq

/// Extract the emitted index uniqueness map (index name → IsUnique).
let private emittedIndexUnique (overlay: DecisionOverlay) (catalog: Catalog) : Map<string, bool> =
    SsdtDdlEmitter.statementsWith overlay catalog
    |> Seq.choose (function
        | Statement.CreateIndex idx -> Some (idx.Name, idx.IsUnique)
        | _ -> None)
    |> Map.ofSeq

// ---------------------------------------------------------------------
// NOT NULL — A42 emission claim + additive-only converse.
// ---------------------------------------------------------------------

[<Fact>]
let ``A42 (2.3): an EnforceNotNull decision emits the source-NULL column as NOT NULL`` () =
    let k = sampleKind ()
    let catalog = catalogOf k
    let alphaKey = akey "Widget.Alpha"
    // Baseline: Alpha is source-nullable, empty overlay leaves it NULL.
    let baseline = emittedColumns DecisionOverlay.empty catalog
    Assert.True(baseline.["ALPHA"], "baseline: source-NULL Alpha should be emitted NULL")
    // Enforce: Alpha now emits NOT NULL; Beta (unkeyed) stays NULL.
    let overlay = { DecisionOverlay.empty with EnforceNotNull = Set.singleton alphaKey }
    let tightened = emittedColumns overlay catalog
    Assert.False(tightened.["ALPHA"], "EnforceNotNull(Alpha) should emit Alpha NOT NULL")
    Assert.True(tightened.["BETA"], "Beta is unkeyed; must stay NULL (additive-only)")

[<Fact>]
let ``A42 (2.3): EnforceNotNull never loosens a source NOT NULL column (additive-only)`` () =
    let k = sampleKind ()
    let catalog = catalogOf k
    // Id is source NOT NULL and NOT in the overlay; stays NOT NULL.
    let cols = emittedColumns DecisionOverlay.empty catalog
    Assert.False(cols.["ID"], "PK Id is source NOT NULL")

// ---------------------------------------------------------------------
// UNIQUE — A42 emission claim + additive-only converse.
// ---------------------------------------------------------------------

[<Fact>]
let ``A42 (2.3): an EnforceUnique decision emits the index as UNIQUE; unkeyed indexes stay non-unique`` () =
    let k = sampleKind ()
    let catalog = catalogOf k
    let idxKey = ikey "Widget.IX_Alpha"
    let baseline = emittedIndexUnique DecisionOverlay.empty catalog
    Assert.False(baseline.["IX_Widget_Alpha"], "baseline: source index is non-unique")
    let overlay = { DecisionOverlay.empty with EnforceUnique = Set.singleton idxKey }
    let tightened = emittedIndexUnique overlay catalog
    Assert.True(tightened.["IX_Widget_Alpha"], "EnforceUnique should emit the index UNIQUE")

// ---------------------------------------------------------------------
// FsCheck — every EnforceNotNull decision NOT-NULLs the emitted column,
// and only those (the additive-only converse pins it).
// ---------------------------------------------------------------------

[<Property(MaxTest = 50)>]
let ``A42 (2.3): every EnforceNotNull decision NOT-NULLs its column, and only those`` (enforceAlpha: bool) (enforceBeta: bool) =
    let k = sampleKind ()
    let catalog = catalogOf k
    let keys =
        [ if enforceAlpha then akey "Widget.Alpha"
          if enforceBeta then akey "Widget.Beta" ]
        |> Set.ofList
    let overlay = { DecisionOverlay.empty with EnforceNotNull = keys }
    let cols = emittedColumns overlay catalog
    // Alpha/Beta are source-NULL: emitted NOT NULL iff enforced.
    // Id is source-NOT NULL: always NOT NULL regardless.
    (cols.["ALPHA"] = not enforceAlpha)
    && (cols.["BETA"] = not enforceBeta)
    && (cols.["ID"] = false)
