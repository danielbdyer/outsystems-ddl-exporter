module Projection.Tests.DecisionEmissionTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

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
            Column = ColumnRealization.create (col.ToUpperInvariant()) (nullable) |> Result.value
            IsPrimaryKey = isPk
            IsMandatory = isPk }
    let idA = akey "Widget.Id"
    let alphaA = akey "Widget.Alpha"
    let betaA = akey "Widget.Beta"
    let idx = Index.ofKeyColumns (ikey "Widget.IX_Alpha") (nm "IX_Widget_Alpha") [ alphaA ]
    { Kind.create (kkey "Widget") (nm "Widget")
        (mkTableId "dbo" "OSUSR_DEC_WIDGET")
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

// ---------------------------------------------------------------------
// Wave-2 slice 2.4 — FK gating + NOCHECK at emission. `sampleCatalog` has
// exactly one FK (Order → Customer, keyed `orderRefToCustomer`). A
// `DoNotEnforce` decision (DropFk) suppresses the inline FK; a
// `ScriptWithNoCheck` decision (NoCheckFk) keeps the FK but adds the
// `AlterTableNoCheckConstraint` (untrusted).
// ---------------------------------------------------------------------

let private emittedFkCount (overlay: DecisionOverlay) (catalog: Catalog) : int =
    SsdtDdlEmitter.statementsWith overlay catalog
    |> Seq.sumBy (function
        | Statement.CreateTable (_, _, _, fks, _, _) -> List.length fks
        | _ -> 0)

let private emittedNoCheckCount (overlay: DecisionOverlay) (catalog: Catalog) : int =
    SsdtDdlEmitter.statementsWith overlay catalog
    |> Seq.sumBy (function
        | Statement.AlterTableNoCheckConstraint _ -> 1
        | _ -> 0)

[<Fact>]
let ``A42 (2.4): baseline empty overlay emits the inline FK and no NOCHECK alter`` () =
    Assert.Equal(1, emittedFkCount DecisionOverlay.empty sampleCatalog)
    Assert.Equal(0, emittedNoCheckCount DecisionOverlay.empty sampleCatalog)

[<Fact>]
let ``A42 (2.4): a DoNotEnforce FK decision suppresses the inline constraint`` () =
    let overlay = { DecisionOverlay.empty with DropFk = Set.singleton orderRefToCustomer }
    Assert.Equal(0, emittedFkCount overlay sampleCatalog)
    // The dropped FK has no constraint to NOCHECK.
    Assert.Equal(0, emittedNoCheckCount overlay sampleCatalog)

[<Fact>]
let ``A42 (2.4): a ScriptWithNoCheck FK decision keeps the FK and emits WITH NOCHECK`` () =
    let overlay = { DecisionOverlay.empty with NoCheckFk = Set.singleton orderRefToCustomer }
    // Inline FK still emitted (the constraint must exist for the ALTER).
    Assert.Equal(1, emittedFkCount overlay sampleCatalog)
    // Plus the untrusting NOCHECK alter.
    Assert.Equal(1, emittedNoCheckCount overlay sampleCatalog)

// ---------------------------------------------------------------------
// Wave-2 slice 2.5(b) — the FK silent-drop WITNESS (L3-X7; slice-μ retired).
// `SsdtDdlEmitter.foreignKeyDropDiagnostics` is a PURE SIBLING of the
// emitter port (statements/emitSlices stay byte-identical). Two drop reasons:
//   - target-missing-PK: REACHABLE through Catalog.create (a PK-less kind is
//     valid) — the genuinely-reachable silent drop.
//   - unresolved-target: Catalog.create already REJECTS this
//     (catalog.reference.danglingTarget) — stronger than a witness; the
//     witness is defense-in-depth for a smart-constructor bypass.
// ---------------------------------------------------------------------

[<Fact>]
let ``L3-X7: an FK whose target kind has no PK emits the targetMissingPrimaryKey witness (reachable via Catalog.create)`` () =
    // Target A declares NO primary key; B references A.
    let aKey = kkey "A"
    let bKey = kkey "B"
    let refKeyV = akey "B.refToA"
    let mkAttr (k: SsKey) (col: string) (isPk: bool) : Attribute =
        { Attribute.create k (nm col) Integer with
            Column = ColumnRealization.create (col.ToUpperInvariant()) (not isPk) |> Result.value
            IsPrimaryKey = isPk; IsMandatory = isPk }
    let a = Kind.create aKey (nm "A") (mkTableId "dbo" "OSUSR_X7_A") [ mkAttr (akey "A.Val") "Val" false ]
    let b =
        { Kind.create bKey (nm "B") (mkTableId "dbo" "OSUSR_X7_B")
            [ mkAttr (akey "B.Id") "Id" true; mkAttr (akey "B.AId") "AId" false ]
          with References = [ Reference.create refKeyV (nm "A") (akey "B.AId") aKey ] }
    let catalog =
        match Catalog.create [ { SsKey = kkey "Mod"; Name = nm "X7Mod"; Kinds = [ a; b ]; IsActive = true; ExtendedProperties = [] } ] [] with
        | Ok c -> c | Error e -> failwithf "catalog %A" e
    let diags = SsdtDdlEmitter.foreignKeyDropDiagnostics catalog
    Assert.Equal(1, List.length diags)
    let d = List.head diags
    Assert.Equal("emit.ssdt.foreignKey.targetMissingPrimaryKeyDropped", d.Code)
    Assert.Equal(DiagnosticSeverity.Warning, d.Severity)
    Assert.Equal("emitter:ssdtDdlEmitter", d.Source)
    Assert.Equal(Some refKeyV, d.SsKey)

[<Fact>]
let ``L3-X7: an FK to a target kind absent from the catalog emits the unresolvedTarget witness (defense-in-depth)`` () =
    // Bypass Catalog.create (which would REJECT a dangling target) to exercise
    // the defensive witness: B references a kind "Ghost" not in the catalog.
    let bKey = kkey "B"
    let ghostKey = kkey "Ghost"
    let refKeyV = akey "B.refToGhost"
    let mkAttr (k: SsKey) (col: string) (isPk: bool) : Attribute =
        { Attribute.create k (nm col) Integer with
            Column = ColumnRealization.create (col.ToUpperInvariant()) (not isPk) |> Result.value
            IsPrimaryKey = isPk; IsMandatory = isPk }
    let b =
        { Kind.create bKey (nm "B") (mkTableId "dbo" "OSUSR_X7_B")
            [ mkAttr (akey "B.Id") "Id" true; mkAttr (akey "B.GId") "GId" false ]
          with References = [ Reference.create refKeyV (nm "Ghost") (akey "B.GId") ghostKey ] }
    // Direct record construction — NOT through Catalog.create.
    let catalog : Catalog =
        { Modules = [ { SsKey = kkey "Mod"; Name = nm "X7Mod"; Kinds = [ b ]; IsActive = true; ExtendedProperties = [] } ]
          Sequences = [] }
    let diags = SsdtDdlEmitter.foreignKeyDropDiagnostics catalog
    Assert.Equal(1, List.length diags)
    Assert.Equal("emit.ssdt.foreignKey.unresolvedTargetDropped", (List.head diags).Code)

[<Fact>]
let ``L3-X7: a fully-resolvable FK emits no drop witness`` () =
    // sampleCatalog's Order → Customer FK resolves cleanly.
    Assert.Empty(SsdtDdlEmitter.foreignKeyDropDiagnostics sampleCatalog)

// ---------------------------------------------------------------------
// 6.A.9 — the DECISION-driven FK-drop audit trail. A reference whose key is
// in `overlay.DropFk` is filtered out of the emitted DDL BEFORE `fkDef` is
// consulted, so `foreignKeyDropDiagnostics` (structural drops only) never
// sees it — the removal was silent at emission. `foreignKeyDecisionDropDiagnostics`
// surfaces one Warning (`decision.fkDropped`) per DropFk key so every
// constraint the engine removed by decision is named. Witness for the
// matrix: "every DropFk decision surfaces a Warning diagnostic".
// ---------------------------------------------------------------------

/// sampleCatalog's Order → Customer FK reference key (the only reference).
let private orderToCustomerRefKey () : SsKey =
    sampleCatalog
    |> Catalog.allKinds
    |> List.collect (fun k -> k.References)
    |> List.head
    |> (fun r -> r.SsKey)

[<Fact>]
let ``every DropFk decision surfaces a Warning diagnostic`` () =
    let refKey = orderToCustomerRefKey ()
    let overlay = { DecisionOverlay.empty with DropFk = Set.singleton refKey }

    // The structurally-resolvable FK emits NO drop witness (it was dropped by
    // decision, not by structure) — proving the decision drop would be silent
    // without this audit.
    Assert.Empty(SsdtDdlEmitter.foreignKeyDropDiagnostics sampleCatalog)

    // The decision-drop audit surfaces exactly one Warning naming the removal.
    let diags = SsdtDdlEmitter.foreignKeyDecisionDropDiagnostics overlay sampleCatalog
    Assert.Equal(1, List.length diags)
    let d = List.head diags
    Assert.Equal("decision.fkDropped", d.Code)
    Assert.Equal(DiagnosticSeverity.Warning, d.Severity)
    Assert.Equal("emitter:ssdtDdlEmitter", d.Source)
    Assert.Equal(Some refKey, d.SsKey)

[<Fact>]
let ``decision.fkDropped surfaces one Warning for every key in DropFk (no silent removal)`` () =
    // Two references, both dropped by decision — each must surface.
    let aKey = kkey "A"
    let bKey = kkey "B"
    let refToA = akey "B.refToA"
    let refToA2 = akey "B.refToA2"
    let mkAttr (k: SsKey) (col: string) (isPk: bool) : Attribute =
        { Attribute.create k (nm col) Integer with
            Column = ColumnRealization.create (col.ToUpperInvariant()) (not isPk) |> Result.value
            IsPrimaryKey = isPk; IsMandatory = isPk }
    let a =
        Kind.create aKey (nm "A") (mkTableId "dbo" "OSUSR_FD_A")
            [ mkAttr (akey "A.Id") "Id" true ]
    let b =
        { Kind.create bKey (nm "B") (mkTableId "dbo" "OSUSR_FD_B")
            [ mkAttr (akey "B.Id") "Id" true
              mkAttr (akey "B.AId") "AId" false
              mkAttr (akey "B.AId2") "AId2" false ]
          with References =
                [ Reference.create refToA (nm "A") (akey "B.AId") aKey
                  Reference.create refToA2 (nm "A") (akey "B.AId2") aKey ] }
    let catalog =
        match Catalog.create [ { SsKey = kkey "Mod"; Name = nm "FdMod"; Kinds = [ a; b ]; IsActive = true; ExtendedProperties = [] } ] [] with
        | Ok c -> c | Error e -> failwithf "catalog %A" e
    let overlay = { DecisionOverlay.empty with DropFk = Set.ofList [ refToA; refToA2 ] }
    let diags = SsdtDdlEmitter.foreignKeyDecisionDropDiagnostics overlay catalog
    Assert.Equal(2, List.length diags)
    Assert.All(diags, fun d ->
        Assert.Equal("decision.fkDropped", d.Code)
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity))
    let keysSurfaced = diags |> List.choose (fun d -> d.SsKey) |> Set.ofList
    Assert.Equal<Set<SsKey>>(Set.ofList [ refToA; refToA2 ], keysSurfaced)

[<Fact>]
let ``no DropFk decision emits no decision-drop diagnostic (empty overlay is silent-clean)`` () =
    Assert.Empty(SsdtDdlEmitter.foreignKeyDecisionDropDiagnostics DecisionOverlay.empty sampleCatalog)
