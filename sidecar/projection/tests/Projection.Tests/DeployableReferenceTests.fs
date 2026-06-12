module Projection.Tests.DeployableReferenceTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------
// DECISIONS 2026-06-12 — reconciliation slice 1 (WP1): symmetric-closure
// inverse references are logical-only edges. They stay in the catalog
// for navigation/ordering, but they never reach a constraint-modeling
// surface: not the FK tightening pass, not `createTableStatement`'s FK
// list, not `untrustedFkAlters`, not the drop diagnostics. The corporate
// shape that exposed the defect — two forward references to the same
// target (CreatedBy/UpdatedBy → User) — would otherwise script two
// identically-named FKs ON THE TARGET'S PK COLUMN.
//
// These are post-CHAIN witnesses (the prior canaries emitted from
// hand-built pre-chain catalogs, so the pass/emitter interaction was
// never deployed under test — the CI gap this file closes).
// ---------------------------------------------------------------------

let private nm (s: string) : Name =
    match Name.create s with | Ok n -> n | Error _ -> failwithf "name %s" s

let private kkey (s: string) : SsKey =
    match SsKey.synthesized "TEST_KIND" s with | Ok k -> k | Error _ -> failwithf "k %s" s

let private akey (s: string) : SsKey =
    match SsKey.synthesized "TEST_ATTR" s with | Ok k -> k | Error _ -> failwithf "a %s" s

let private refk (s: string) : SsKey =
    match SsKey.synthesized "TEST_REF" s with | Ok k -> k | Error _ -> failwithf "r %s" s

let private mkAttr (k: SsKey) (col: string) (isPk: bool) : Attribute =
    { Attribute.create k (nm col) Integer with
        Column = ColumnRealization.create (col.ToUpperInvariant()) (not isPk) |> Result.value
        IsPrimaryKey = isPk
        IsMandatory = isPk }

let private userKey = kkey "User"
let private taskKey = kkey "Task"

/// The corporate shape: Task carries CreatedBy + UpdatedBy, both
/// referencing User. `constraintState` sets the forward references'
/// `(HasDbConstraint, IsConstraintTrusted)` pair — inverses inherit it,
/// which is exactly the hazard (an inverse indistinguishable from a
/// storage-backed edge by flags alone; the derivation class is the
/// discriminator).
let private corporateShape (hasDb: bool) (trusted: bool) : Catalog =
    let user =
        Kind.create userKey (nm "User") (mkTableId "dbo" "OSUSR_DR_USER")
            [ mkAttr (akey "User.Id") "Id" true ]
    let mkRef (key: SsKey) (sourceAttr: SsKey) : Reference =
        Reference.create key (nm "User") sourceAttr userKey
        |> Reference.withConstraintState hasDb trusted
    let task =
        { Kind.create taskKey (nm "Task") (mkTableId "dbo" "OSUSR_DR_TASK")
            [ mkAttr (akey "Task.Id") "Id" true
              mkAttr (akey "Task.CreatedBy") "CreatedBy" false
              mkAttr (akey "Task.UpdatedBy") "UpdatedBy" false ]
          with References =
                [ mkRef (refk "Task.CreatedBy") (akey "Task.CreatedBy")
                  mkRef (refk "Task.UpdatedBy") (akey "Task.UpdatedBy") ] }
    match Catalog.create [ { SsKey = kkey "Mod"; Name = nm "DrMod"; Kinds = [ user; task ]; IsActive = true; ExtendedProperties = [] } ] [] with
    | Ok c -> c
    | Error e -> failwithf "catalog %A" e

/// Apply the symmetric-closure pass (the inverse mint).
let private closed (catalog: Catalog) : Catalog =
    SymmetricClosure.registered.Run catalog
    |> LineageDiagnostics.payload

/// Run the FULL canonical chain (every registered pass, empty pins).
let private fullChain (policy: Policy) (profile: Profile) (catalog: Catalog) : Catalog =
    let chain = RegisteredTransforms.allChainStepsFor policy profile
    let composed = PassChainAdapter.compose chain (ComposeState.initial catalog)
    (LineageDiagnostics.payload composed).Catalog

/// All (owner TableId, ForeignKeyDef) pairs across the emitted stream.
let private emittedFks (catalog: Catalog) : (TableId * ForeignKeyDef) list =
    SsdtDdlEmitter.statements catalog
    |> Seq.collect (function
        | Statement.CreateTable (tid, _, _, fks, _, _) ->
            fks |> List.map (fun fk -> tid, fk) |> Seq.ofList
        | _ -> Seq.empty)
    |> List.ofSeq

let private isDerived (key: SsKey) : bool =
    match key with
    | DerivedFrom _ -> true
    | _ -> false

let private fkPolicy (enableCreation: bool) : Policy =
    let fkCfg = ForeignKeyTighteningConfig.create enableCreation true true false true
    { Policy.empty with
        Tightening = { Interventions = [ TighteningIntervention.ForeignKey ("dr-fk", fkCfg) ] } }

// ---------------------------------------------------------------------
// The fixture exercises the hazard: closure mints inverses that inherit
// the storage flags.
// ---------------------------------------------------------------------

[<Fact>]
let ``fixture guard: closure mints two inverses on User, inheriting HasDbConstraint`` () =
    let catalog = closed (corporateShape true true)
    let user =
        Catalog.allKinds catalog
        |> List.find (fun k -> k.SsKey = userKey)
    let inverses = user.References |> List.filter Reference.isInverse
    Assert.Equal(2, List.length inverses)
    Assert.All(inverses, fun r -> Assert.True r.HasDbConstraint)
    Assert.All(inverses, fun r -> Assert.False (Reference.isDeployable r))

// ---------------------------------------------------------------------
// WP1 — emission excludes the inverse class.
// ---------------------------------------------------------------------

[<Fact>]
let ``post-closure emission carries exactly the forward FKs — no FK on the target's PK, no duplicate names`` () =
    let catalog = closed (corporateShape true true)
    let fks = emittedFks catalog
    // Exactly the two forward references, both owned by Task.
    Assert.Equal(2, List.length fks)
    Assert.All(fks, fun (tid, _) ->
        Assert.Equal("OSUSR_DR_TASK", TableId.tableText tid))
    // No second FK on User (the pre-fix symptom: inverses scripted FKs
    // on the target's PK column).
    let userFks = fks |> List.filter (fun (tid, _) -> TableId.tableText tid = "OSUSR_DR_USER")
    Assert.Empty userFks
    // Distinct constraint names (the pre-fix symptom: both inverses
    // named by User's PK column — identical FK_* names).
    let names = fks |> List.map (fun (_, fk) -> fk.Name)
    Assert.Equal(List.length names, names |> List.distinct |> List.length)

[<Fact>]
let ``full canonical chain preserves the contract: post-chain emission carries no inverse-sourced FKs`` () =
    // The corporate default (no FK intervention registered) — the exact
    // configuration that emitted inverse FKs before slice 1.
    let catalog = fullChain Policy.empty Profile.empty (corporateShape true true)
    let fks = emittedFks catalog
    Assert.Equal(2, List.length fks)
    let names = fks |> List.map (fun (_, fk) -> fk.Name)
    Assert.Equal(List.length names, names |> List.distinct |> List.length)
    // Identify User's post-chain table (LogicalTableEmission may have
    // substituted the logical name); assert it owns no FKs.
    let user =
        Catalog.allKinds catalog
        |> List.find (fun k -> k.SsKey = userKey)
    let userTable = TableId.tableText user.Physical
    Assert.Empty (fks |> List.filter (fun (tid, _) -> TableId.tableText tid = userTable))

[<Fact>]
let ``untrusted forward FKs emit one ALTER pair each; inherited-untrusted inverses emit none`` () =
    // Both forward refs source-backed + untrusted; inverses inherit the
    // pair. Pre-fix: four phantom ALTERs against never-created inverse
    // constraint names.
    let catalog = closed (corporateShape true false)
    let alters =
        SsdtDdlEmitter.statements catalog
        |> Seq.choose (function
            | Statement.AlterTableDisableConstraint (tid, name) -> Some (tid, name)
            | Statement.AlterTableNoCheckConstraint (tid, name) -> Some (tid, name)
            | _ -> None)
        |> List.ofSeq
    // 2 forward refs × (disable + nocheck) = 4 statements, all on Task.
    Assert.Equal(4, List.length alters)
    Assert.All(alters, fun (tid, _) ->
        Assert.Equal("OSUSR_DR_TASK", TableId.tableText tid))

// ---------------------------------------------------------------------
// WP1 — the FK pass (v3) decision domain excludes the inverse class.
// ---------------------------------------------------------------------

[<Fact>]
let ``FK pass v3 mints decisions only for deployable references — no decision, no diagnostic keyed by an inverse`` () =
    let catalog = closed (corporateShape false true)
    let result = (ForeignKeyPass.registered (fkPolicy true) Profile.empty).Run catalog
    let decisions = (ForeignKeyPass.decisionsOf result).Decisions
    // Two forward references; two decisions; zero derived keys.
    Assert.Equal(2, List.length decisions)
    Assert.All(decisions, fun d -> Assert.False (isDerived d.ReferenceKey))
    // No spurious per-inverse diagnostics (the pre-fix noise:
    // `tightening.foreignKey.evidenceMissing` per inverse).
    let entries = LineageDiagnostics.entries result
    Assert.All(entries, fun e ->
        match e.SsKey with
        | Some key -> Assert.False (isDerived key)
        | None -> ())

// ---------------------------------------------------------------------
// WP2 — the carve-out at the pass grain (decision level is pinned in
// ForeignKeyRulesTests; this is the registered-pass face).
// ---------------------------------------------------------------------

[<Fact>]
let ``source-backed references decide EnforceConstraint(DatabaseConstraintPresent) even under EnableCreation=false`` () =
    let catalog = corporateShape true true
    let result = (ForeignKeyPass.registered (fkPolicy false) Profile.empty).Run catalog
    let decisions = (ForeignKeyPass.decisionsOf result).Decisions
    Assert.Equal(2, List.length decisions)
    Assert.All(decisions, fun d ->
        Assert.Equal(
            ForeignKeyOutcome.EnforceConstraint DatabaseConstraintPresent,
            d.Outcome))

// ---------------------------------------------------------------------
// WP1 — the FK-name collision tripwire (named, never a silent dedupe).
// ---------------------------------------------------------------------

[<Fact>]
let ``FK-name collision tripwire: schema-scoped name overlap surfaces one Error per participating reference`` () =
    // Contrived overlap: FK_<Owner>_<Target>_<Column> collides across
    // (owner "B_A" → target "C", column X) and (owner "B" → target
    // "A_C", column X) — both render FK_B_A_C_X.
    let cKey = kkey "C"
    let acKey = kkey "AC"
    let baKey = kkey "BA"
    let bKey = kkey "B"
    let c = Kind.create cKey (nm "C") (mkTableId "dbo" "C") [ mkAttr (akey "C.Id") "Id" true ]
    let ac = Kind.create acKey (nm "AC") (mkTableId "dbo" "A_C") [ mkAttr (akey "AC.Id") "Id" true ]
    let ba =
        { Kind.create baKey (nm "BA") (mkTableId "dbo" "B_A")
            [ mkAttr (akey "BA.Id") "Id" true
              mkAttr (akey "BA.X") "X" false ]
          with References = [ Reference.create (refk "BA.toC") (nm "C") (akey "BA.X") cKey ] }
    let b =
        { Kind.create bKey (nm "B") (mkTableId "dbo" "B")
            [ mkAttr (akey "B.Id") "Id" true
              mkAttr (akey "B.X") "X" false ]
          with References = [ Reference.create (refk "B.toAC") (nm "AC") (akey "B.X") acKey ] }
    let catalog =
        match Catalog.create [ { SsKey = kkey "ColMod"; Name = nm "ColMod"; Kinds = [ c; ac; ba; b ]; IsActive = true; ExtendedProperties = [] } ] [] with
        | Ok cat -> cat
        | Error e -> failwithf "catalog %A" e
    let diags = SsdtDdlEmitter.foreignKeyNameCollisionDiagnostics DecisionOverlay.empty catalog
    Assert.Equal(2, List.length diags)
    Assert.All(diags, fun d ->
        Assert.Equal("emit.ssdt.foreignKey.nameCollision", d.Code)
        Assert.Equal(DiagnosticSeverity.Error, d.Severity)
        Assert.Equal(Some "FK_B_A_C_X", Map.tryFind "constraintName" d.Metadata))

[<Fact>]
let ``FK-name collision tripwire is silent on the post-closure corporate shape (the invariant the exclusion restores)`` () =
    let catalog = closed (corporateShape true true)
    Assert.Empty (SsdtDdlEmitter.foreignKeyNameCollisionDiagnostics DecisionOverlay.empty catalog)
