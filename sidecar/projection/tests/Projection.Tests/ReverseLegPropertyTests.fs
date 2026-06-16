module Projection.Tests.ReverseLegPropertyTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Pipeline
open FsCheck

// ============================================================================
// The L2 substance for the B→A reverse leg (AUDIT_2026_05_31 §0.3): pure laws
// over a CONSTRUCTED-VALID generator (FK targets drawn from already-chosen
// kinds — validity is constructed, not generate-and-filtered), so the
// machinery the LE-3 canary witnesses on one graph is proven over the
// combinatorial space. Each test is named by the law it enforces:
//   (a) DataLoadPlan.build order soundness — every FK edge's target precedes
//       its referencer, or the column is deferred (nullable, in-cycle), or
//       the edge is the NAMED UnbreakableCycleFk;
//   (b) disposition totality — AssignedBySink ⇔ an IDENTITY PK leg exists;
//   (c) remap algebra — remapRowFks re-points exactly the captured targeted
//       columns and nothing else; a non-null miss drops the row by name;
//   (d) refusal totality — every generated unsatisfiable shape lands on its
//       named refusal code, never on success and never on an unnamed crash;
//   (e) CatalogRendition invariants — identity / IsIdentity / nullability /
//       PK shape / references are rendition-invariant for ARBITRARY models,
//       and the B→A rename map is the IDENTITY (DECISIONS 2026-06-10).
// ============================================================================

// -- constructed-valid generator ---------------------------------------------

let private nm (s: string) : Name = Name.create s |> Result.value
let private kKey (i: int) : SsKey = SsKey.synthesizedComposite "RLP_KIND" [ string i ] |> Result.value
let private aKey (ki: int) (attr: string) : SsKey = SsKey.synthesizedComposite "RLP_ATTR" [ string ki; attr ] |> Result.value
let private rKey (ki: int) (ri: int) : SsKey = SsKey.synthesizedComposite "RLP_REF" [ string ki; string ri ] |> Result.value

let rec private genAll (gs: Gen<'a> list) : Gen<'a list> =
    match gs with
    | [] -> Gen.constant []
    | g :: rest -> gen { let! x = g in let! xs = genAll rest in return x :: xs }

let private genBool : Gen<bool> = Gen.elements [ true; false ]

/// One generated kind: a single Integer PK (IDENTITY per `pkIdentity`), FK
/// attributes targeting ALREADY-CHOSEN kinds (acyclic by construction; an
/// optional self-FK is the cycle injection), and Text payload. Physical
/// column names are UPPERCASE so the logical rendition substitution is a
/// real coordinate move, never a no-op.
let private buildKind
    (ix: int)
    (pkIdentity: bool)
    (fks: (int * bool) list)        // (target kind index, nullable)
    (selfFk: bool option)           // Some nullable → a self-FK (a 1-cycle)
    (payloadCount: int)
    : Kind =
    let pk =
        { Attribute.create (aKey ix "Id") (nm "Id") Integer with
            Column       = ColumnRealization.create "ID" false |> Result.value
            IsPrimaryKey = true
            IsIdentity   = pkIdentity
            IsMandatory  = true }
    let fkAttrs =
        fks
        |> List.mapi (fun j (_, nullable) ->
            { Attribute.create (aKey ix (sprintf "Fk%d" j)) (nm (sprintf "Fk%d" j)) Integer with
                Column      = ColumnRealization.create (sprintf "FK%d" j) nullable |> Result.value
                IsMandatory = not nullable })
    let selfAttr =
        match selfFk with
        | None -> []
        | Some nullable ->
            [ { Attribute.create (aKey ix "SelfFk") (nm "SelfFk") Integer with
                  Column      = ColumnRealization.create "SELF_FK" nullable |> Result.value
                  IsMandatory = not nullable } ]
    let payload =
        [ for p in 0 .. payloadCount - 1 ->
            { Attribute.create (aKey ix (sprintf "P%d" p)) (nm (sprintf "P%d" p)) Text with
                Column = ColumnRealization.create (sprintf "PAY%d" p) true |> Result.value } ]
    let refs =
        (fks |> List.mapi (fun j (target, _) ->
            Reference.create (rKey ix j) (nm (sprintf "R%d_%d" ix j)) (aKey ix (sprintf "Fk%d" j)) (kKey target)))
        @ (match selfFk with
           | None -> []
           | Some _ -> [ Reference.create (rKey ix 99) (nm (sprintf "RSelf%d" ix)) (aKey ix "SelfFk") (kKey ix) ])
    { Kind.create (kKey ix) (nm (sprintf "K%d" ix))
        (TableId.create "dbo" (sprintf "OSUSR_RLP_T%d" ix) |> Result.value)
        (pk :: fkAttrs @ selfAttr @ payload) with
        References = refs }

let private catalogOf (kinds: Kind list) : Catalog =
    Catalog.create
        [ { SsKey = SsKey.synthesizedComposite "RLP_MOD" [ "M" ] |> Result.value
            Name = nm "M"; Kinds = kinds; IsActive = true; ExtendedProperties = [] } ] []
    |> Result.value

/// A transfer-shaped catalog: 1..6 kinds, FK targets drawn from earlier
/// kinds only (acyclic), optional nullable/non-nullable self-FK cycles.
let private genTransferCatalog (allowCycles: bool) : Gen<Catalog> =
    gen {
        let! nKinds = Gen.choose (1, 6)
        let! kinds =
            [ for i in 0 .. nKinds - 1 ->
                gen {
                    let! pkIdentity = genBool
                    let! nFks = Gen.choose (0, min 2 i)
                    let! fks =
                        [ for _ in 1 .. nFks ->
                            gen {
                                let! target = Gen.choose (0, i - 1)
                                let! nullable = genBool
                                return target, nullable } ]
                        |> genAll
                    let! selfFk =
                        if allowCycles then
                            Gen.frequency
                                [ 3, Gen.constant None
                                  1, Gen.map Some genBool ]
                        else Gen.constant None
                    let! payloadCount = Gen.choose (0, 2)
                    return buildKind i pkIdentity fks selfFk payloadCount } ]
            |> genAll
        return catalogOf kinds
    }

let private planOf (catalog: Catalog) : DataLoadPlan * TopologicalOrder =
    let topo = (TopologicalOrderPass.runWith TreatAsCycle catalog).Value
    DataLoadPlan.build catalog topo Map.empty SurrogateRemapContext.empty, topo

// -- (a) order soundness -------------------------------------------------------

[<Fact>]
let ``order soundness: every FK edge's target strictly precedes its referencer, or the column is deferred (nullable, in-cycle), or the edge is the NAMED UnbreakableCycleFk`` () =
    Prop.forAll (Arb.fromGen (genTransferCatalog true)) (fun catalog ->
        let plan, _ = planOf catalog
        let position =
            plan.Loads |> List.mapi (fun i l -> l.Kind, i) |> Map.ofList
        plan.Loads
        |> List.forall (fun load ->
            match Catalog.tryFindKind load.Kind catalog with
            | None -> false
            | Some kind ->
                kind.References
                |> List.forall (fun r ->
                    let attrName =
                        Kind.tryFindAttribute r.SourceAttribute kind
                        |> Option.map (fun a -> a.Name)
                    let precedes =
                        match Map.tryFind r.TargetKind position, Map.tryFind load.Kind position with
                        | Some tp, Some kp -> tp < kp
                        | _ -> false
                    let deferred =
                        match attrName with
                        | Some n -> Set.contains n load.DeferredFkColumns
                        | None -> false
                    let unbreakable =
                        plan.UnbreakableCycleFks
                        |> List.exists (fun u ->
                            u.Kind = load.Kind && u.Target = r.TargetKind
                            && Some u.Column = attrName)
                    precedes || deferred || unbreakable)))
    |> Check.QuickThrowOnFailure

[<Fact>]
let ``order soundness: every deferred FK column is nullable — phase 1 can NULL it for phase 2 to re-point`` () =
    Prop.forAll (Arb.fromGen (genTransferCatalog true)) (fun catalog ->
        let plan, _ = planOf catalog
        plan.Loads
        |> List.forall (fun load ->
            match Catalog.tryFindKind load.Kind catalog with
            | None -> false
            | Some kind ->
                load.DeferredFkColumns
                |> Set.forall (fun n ->
                    kind.Attributes
                    |> List.exists (fun a -> a.Name = n && a.Column.IsNullable))))
    |> Check.QuickThrowOnFailure

// -- (b) disposition totality --------------------------------------------------

[<Fact>]
let ``disposition totality: AssignedBySink iff an IDENTITY primary-key leg exists; otherwise PreservedFromSource — ofKind never invents ReconciledByRule`` () =
    Prop.forAll (Arb.fromGen (genTransferCatalog true)) (fun catalog ->
        Catalog.allKinds catalog
        |> List.forall (fun k ->
            let hasIdentityPk = k.Attributes |> List.exists (fun a -> a.IsPrimaryKey && a.IsIdentity)
            match IdentityDisposition.ofKind k with
            | IdentityDisposition.AssignedBySink      -> hasIdentityPk
            | IdentityDisposition.PreservedFromSource -> not hasIdentityPk
            | IdentityDisposition.ReconciledByRule    -> false))
    |> Check.QuickThrowOnFailure

// -- (c) remap algebra -----------------------------------------------------------

/// Rows over a kind with FK columns: each FK value drawn from a small key
/// alphabet (some captured, some not, some NULL). The law: `remapRowFks`
/// re-points EXACTLY the captured targeted columns; every other value is
/// untouched; a non-null uncaptured targeted value drops the row with the
/// named diagnostic; row order is preserved for the kept rows.
[<Fact>]
let ``remap algebra: remapRowFks re-points exactly the FK columns whose targets were captured and nothing else; a miss drops the row by name`` () =
    let gen =
        gen {
            // The referencing kind: 2 FKs to kinds 0 and 1, plus payload.
            let k0 = buildKind 0 true [] None 0
            let k1 = buildKind 1 true [] None 0
            let kRef = buildKind 2 false [ (0, true); (1, true) ] None 1
            // Which targets are in the remap set (AssignedBySink kinds).
            let! target0In = genBool
            let! target1In = genBool
            let targets =
                Set.ofList
                    ([ if target0In then kKey 0
                       if target1In then kKey 1 ])
            // Captured source keys per target (the sink minted "A<k>").
            let alphabet = [ "10"; "11"; "12" ]
            let! captured0 = Gen.subListOf alphabet
            let! captured1 = Gen.subListOf alphabet
            let remap =
                List.fold
                    (fun ctx (kind, src) ->
                        match SurrogateRemapContext.capture kind (SourceKey.ofString src) (AssignedKey.ofString ("A" + src)) ctx with
                        | Ok c -> c
                        | Error _ -> ctx)
                    SurrogateRemapContext.empty
                    ((captured0 |> List.map (fun s -> kKey 0, s))
                     @ (captured1 |> List.map (fun s -> kKey 1, s)))
            // Rows: FK values from the alphabet ∪ {""} (NULL), payload free.
            let! nRows = Gen.choose (0, 6)
            let! rows =
                [ for r in 0 .. nRows - 1 ->
                    gen {
                        let! v0 = Gen.elements ("" :: alphabet)
                        let! v1 = Gen.elements ("" :: alphabet)
                        let! pay = Gen.elements [ "x"; "y"; "" ]
                        return
                            { Identifier = aKey 2 (sprintf "Row%d" r)
                              Values =
                                Map.ofList
                                    [ nm "Id", string (100 + r)
                                      nm "Fk0", v0
                                      nm "Fk1", v1
                                      nm "P0", pay ] } } ]
                |> genAll
            return kRef, targets, (Set.ofList captured0, Set.ofList captured1), remap, rows
        }
    Prop.forAll (Arb.fromGen gen) (fun (kRef, targets, (captured0, captured1), remap, rows) ->
        let fkTargets = SurrogateRemap.fkColumnsTargeting targets kRef
        // fkColumnsTargeting selects exactly the FK columns whose target is in the set.
        let expectedCols =
            Set.ofList
                ([ if Set.contains (kKey 0) targets then nm "Fk0"
                   if Set.contains (kKey 1) targets then nm "Fk1" ])
        let colsExact = (fkTargets |> Map.toList |> List.map fst |> Set.ofList) = expectedCols
        let result = SurrogateRemap.remapRowFks fkTargets remap rows
        let capturedFor (col: Name) = if col = nm "Fk0" then captured0 else captured1
        let rowSurvives (row: StaticRow) =
            fkTargets
            |> Map.forall (fun col _ ->
                match Map.tryFind col row.Values with
                | None | Some "" -> true
                | Some v -> Set.contains v (capturedFor col))
        let expectedKept = rows |> List.filter rowSurvives
        let keptMatch =
            List.length result.Rows = List.length expectedKept
            && List.forall2
                (fun (kept: StaticRow) (orig: StaticRow) ->
                    kept.Identifier = orig.Identifier
                    && orig.Values
                       |> Map.forall (fun col v ->
                            let expected =
                                if Map.containsKey col fkTargets && v <> "" && Set.contains v (capturedFor col)
                                then "A" + v
                                else v
                            Map.tryFind col kept.Values = Some expected))
                result.Rows expectedKept
        let skippedMatch =
            List.length result.Skipped = (List.length rows - List.length expectedKept)
            && result.Skipped
               |> List.forall (fun u ->
                    Map.containsKey u.Column fkTargets
                    && not (Set.contains (SourceKey.value u.UnresolvedSource) (capturedFor u.Column))
                    && SourceKey.value u.UnresolvedSource <> "")
        colsExact && keptMatch && skippedMatch)
    |> Check.QuickThrowOnFailure

// -- (d) refusal totality --------------------------------------------------------

/// The unsatisfiable shapes, injected one at a time into a generated clean
/// acyclic surround. The law: `executeGate` lands on the EXACT named code
/// for the injected pathology — and on None for the controls. The cyclic
/// AssignedBySink shape is a CONTROL since the 6.A.2 lift
/// (operator-authorized 2026-06-10): phase 2 re-points the deferred FK
/// through the completed remap keyed on the ASSIGNED PK, so the shape
/// loads instead of refusing.
type private Pathology =
    | NonNullableCycle        // transfer.unbreakableCycleFk
    | CyclicAssignedBySink    // LIFTED — gates None (loads via assigned-PK phase 2)
    | CompositeIdentityPk     // transfer.compositeSurrogateUnsupported
    | CleanControl            // executeGate = None

[<Fact>]
let ``refusal totality: every generated unsatisfiable shape lands on its named refusal code — never on success, never on an unnamed crash; the LIFTED cyclic shape gates None`` () =
    let genCase =
        gen {
            let! pathology =
                Gen.elements [ NonNullableCycle; CyclicAssignedBySink; CompositeIdentityPk; CleanControl ]
            // A clean acyclic surround (no cycles, so the surround never
            // triggers a refusal of its own).
            let! surround = genTransferCatalog false
            let surroundKinds = Catalog.allKinds surround
            let ix = List.length surroundKinds
            let injected =
                match pathology with
                | NonNullableCycle ->
                    // A NON-nullable self-FK: phase 1 cannot NULL it.
                    [ buildKind ix false [] (Some false) 0 ]
                | CyclicAssignedBySink ->
                    // IDENTITY PK + nullable self-FK (the operator's
                    // User.ManagerId shape) — supported since the lift.
                    [ buildKind ix true [] (Some true) 0 ]
                | CompositeIdentityPk ->
                    let extra =
                        { Attribute.create (aKey ix "Tenant") (nm "Tenant") Integer with
                            Column       = ColumnRealization.create "TENANT" false |> Result.value
                            IsPrimaryKey = true
                            IsMandatory  = true }
                    let k = buildKind ix true [] None 0
                    [ { k with Attributes = k.Attributes @ [ extra ] } ]
                | CleanControl -> []
            return pathology, catalogOf (surroundKinds @ injected)
        }
    Prop.forAll (Arb.fromGen genCase) (fun (pathology, catalog) ->
        let plan, _ = planOf catalog
        let refusal = Transfer.executeGate catalog plan |> Option.map (fun e -> e.Code)
        match pathology with
        | NonNullableCycle     -> refusal = Some "transfer.unbreakableCycleFk"
        | CyclicAssignedBySink -> refusal = None
        | CompositeIdentityPk  -> refusal = Some "transfer.compositeSurrogateUnsupported"
        | CleanControl         -> refusal = None)
    |> Check.QuickThrowOnFailure

// -- (e) CatalogRendition invariants ----------------------------------------------

[<Fact>]
let ``rendition invariance: SsKey, IsIdentity, nullability, PK shape, and references survive the logical rendition for ARBITRARY generated models`` () =
    Prop.forAll (Arb.fromGen (genTransferCatalog true)) (fun model ->
        let logical = CatalogRendition.logical model
        let physical = CatalogRendition.physical model
        let physicalIsIdentity = physical = model
        let logicalKinds = Catalog.allKinds logical
        let modelKinds = Catalog.allKinds model
        let aligned =
            List.length logicalKinds = List.length modelKinds
            && List.forall2
                (fun (l: Kind) (m: Kind) ->
                    l.SsKey = m.SsKey
                    && l.Name = m.Name
                    && l.References = m.References
                    && TableId.tableText l.Physical = Name.value m.Name
                    && List.length l.Attributes = List.length m.Attributes
                    && List.forall2
                        (fun (la: Attribute) (ma: Attribute) ->
                            la.SsKey = ma.SsKey
                            && la.Name = ma.Name
                            && la.IsPrimaryKey = ma.IsPrimaryKey
                            && la.IsIdentity = ma.IsIdentity
                            && la.Column.IsNullable = ma.Column.IsNullable
                            && ColumnRealization.columnNameText la.Column = Name.value ma.Name)
                        l.Attributes m.Attributes)
                logicalKinds modelKinds
        physicalIsIdentity && aligned)
    |> Check.QuickThrowOnFailure

[<Fact>]
let ``rendition invariance: the B->A rename map is the IDENTITY — logical Names are rendition-invariant, so the rendition difference rides physical coordinates only`` () =
    Prop.forAll (Arb.fromGen (genTransferCatalog true)) (fun model ->
        let logical = CatalogRendition.logical model
        let physical = CatalogRendition.physical model
        let diff = CatalogDiff.between logical physical
        RenameProjection.renames diff |> List.isEmpty)
    |> Check.QuickThrowOnFailure

[<Fact>]
let ``rendition invariance: the disposition derives identically from either rendition — one model, one identity law, two coordinate systems`` () =
    Prop.forAll (Arb.fromGen (genTransferCatalog true)) (fun model ->
        let logicalKinds = Catalog.allKinds (CatalogRendition.logical model)
        let physicalKinds = Catalog.allKinds (CatalogRendition.physical model)
        List.forall2
            (fun (l: Kind) (p: Kind) ->
                IdentityDisposition.ofKind l = IdentityDisposition.ofKind p)
            logicalKinds physicalKinds)
    |> Check.QuickThrowOnFailure

// -- packed remap equivalence -----------------------------------------------

[<Fact>]
let ``packed remap equivalence: capture keeps the FIRST binding and tryFind agrees with SurrogateRemapContext for every generated capture sequence`` () =
    let genCaptures =
        gen {
            let! n = Gen.choose (0, 40)
            let! captures =
                [ for _ in 1 .. n ->
                    gen {
                        let! kindIx = Gen.choose (0, 2)
                        let! src = Gen.elements [ "10"; "11"; "12"; "9007199254740993"; "not-a-number"; "x-77" ]
                        let! assigned = Gen.elements [ "1"; "2"; "3"; "9007199254740994"; "alpha" ]
                        return kKey kindIx, src, assigned } ]
                |> genAll
            return captures
        }
    Prop.forAll (Arb.fromGen genCaptures) (fun captures ->
        let packed = Projection.Pipeline.PackedSurrogateRemap.create ()
        let ctx =
            captures
            |> List.fold
                (fun acc (kind, src, assigned) ->
                    Projection.Pipeline.PackedSurrogateRemap.capture kind src assigned packed
                    match SurrogateRemapContext.capture kind (SourceKey.ofString src) (AssignedKey.ofString assigned) acc with
                    | Ok c -> c
                    | Error _ -> acc)
                SurrogateRemapContext.empty
        let probes =
            (captures |> List.map (fun (k, s, _) -> k, s))
            @ [ kKey 0, "999"; kKey 1, "not-a-number"; kKey 2, "" ]
        probes
        |> List.forall (fun (kind, src) ->
            let viaCtx =
                SurrogateRemapContext.tryFindAssigned kind (SourceKey.ofString src) ctx
                |> Option.map AssignedKey.value
            let viaPacked = Projection.Pipeline.PackedSurrogateRemap.tryFind packed kind src
            viaPacked = viaCtx))
    |> Check.QuickThrowOnFailure
