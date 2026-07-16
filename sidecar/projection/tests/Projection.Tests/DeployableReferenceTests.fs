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
    Assert.All(inverses, fun r -> Assert.True (Reference.hasDbConstraint r))
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
// F8 (audit 2026-06-17) — the CONSERVATION guard against a FUTURE
// reference-consuming emission site that forgets the `isDeployable`
// filter. SymmetricClosure mints inverses (classified DataIntent);
// faithfulness rests on EVERY constraint-emission site filtering them
// out (5 sites today in SsdtDdlEmitter.fs). Rather than pin the site
// list (which drifts), this pins the INVARIANT: across the WHOLE emitted
// statement stream, the constraints CREATED number exactly the
// DEPLOYABLE references, and every constraint an ALTER touches was
// actually created. A 6th site that leaked an inverse would either
// overshoot the created count or name a never-created constraint — so it
// breaks conservation even on a shape that carries inverses.
// ---------------------------------------------------------------------

[<Fact>]
let ``F8 conservation: emitted constraints number exactly the deployable references; no ALTER names a never-created constraint`` () =
    // Untrusted forward refs so the stream carries BOTH the CreateTable FK
    // list AND the disable/no-check ALTER pair — two of the five sites at once.
    let catalog = closed (corporateShape true false)
    let stmts = SsdtDdlEmitter.statements catalog |> List.ofSeq
    // Constraints CREATED (the CreateTable inline FK list).
    let createdFkNames =
        stmts
        |> List.collect (function
            | Statement.CreateTable (_, _, _, fks, _, _) -> fks |> List.map (fun fk -> fk.Name)
            | _ -> [])
        |> Set.ofList
    // Constraints REFERENCED by every constraint-touching ALTER.
    let referencedConstraintNames =
        stmts
        |> List.choose (function
            | Statement.AlterTableDisableConstraint (_, name) -> Some name
            | Statement.AlterTableNoCheckConstraint (_, name) -> Some name
            | _ -> None)
    // Deployable references in the (post-closure) catalog — the inverses are
    // present but non-deployable, so they must NOT be among the created FKs.
    let deployableRefCount =
        Catalog.allKinds catalog
        |> List.collect (fun k -> k.References)
        |> List.filter Reference.isDeployable
        |> List.length
    Assert.Equal(deployableRefCount, Set.count createdFkNames)
    Assert.All(referencedConstraintNames, fun name ->
        Assert.True(
            Set.contains name createdFkNames,
            sprintf "ALTER references constraint %s which was never created — an inverse leaked past isDeployable" name))

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

// ---------------------------------------------------------------------
// WP-16 (DECISIONS 2026-07-16) — the TABLE-name collision tripwire. Every
// kind emits one CREATE TABLE at its schema-qualified (schema, table); two
// kinds resolving to the same one would emit duplicate CREATE TABLEs (a
// DacFx build failure across modules, a silent last-wins within one). One
// Error per participating kind, never a silent last-win. Mirror of the
// FK-name tripwire above (packet H7).
// ---------------------------------------------------------------------

/// Two kinds (distinct SsKeys) resolving to the same physical (dbo, Customer),
/// each in its own module — the cross-module duplicate-entity shape.
let private twoCustomerModules () : Catalog =
    let a = Kind.create (kkey "A.Customer") (nm "Customer") (mkTableId "dbo" "Customer")
                [ mkAttr (akey "A.Customer.Id") "Id" true ]
    let b = Kind.create (kkey "B.Customer") (nm "Customer") (mkTableId "dbo" "Customer")
                [ mkAttr (akey "B.Customer.Id") "Id" true ]
    match Catalog.create
              [ { SsKey = kkey "ModA"; Name = nm "ModA"; Kinds = [ a ]; IsActive = true; ExtendedProperties = [] }
                { SsKey = kkey "ModB"; Name = nm "ModB"; Kinds = [ b ]; IsActive = true; ExtendedProperties = [] } ] [] with
    | Ok cat -> cat
    | Error e -> failwithf "catalog %A" e

[<Fact>]
let ``Table-name collision tripwire: two kinds at the same (schema, table) surface one Error per participating kind`` () =
    let diags = SsdtDdlEmitter.tableNameCollisionDiagnostics (twoCustomerModules ())
    Assert.Equal(2, List.length diags)
    Assert.All(diags, fun d ->
        Assert.Equal<string>("emit.ssdt.table.nameCollision", d.Code)
        Assert.Equal(DiagnosticSeverity.Error, d.Severity)
        Assert.Equal<string option>(Some "dbo", Map.tryFind "schema" d.Metadata)
        Assert.Equal<string option>(Some "Customer", Map.tryFind "table" d.Metadata))

[<Fact>]
let ``Table-name collision tripwire: a same-module duplicate (silent last-wins today) is named, not swallowed`` () =
    // Two distinct kinds at (dbo, Customer) inside ONE module — the shared
    // Modules/<Module>/dbo.Customer.sql file would silently last-win.
    let a = Kind.create (kkey "Customer") (nm "Customer") (mkTableId "dbo" "Customer")
                [ mkAttr (akey "Customer.Id") "Id" true ]
    let b = Kind.create (kkey "CustomerDup") (nm "Customer") (mkTableId "dbo" "Customer")
                [ mkAttr (akey "CustomerDup.Id") "Id" true ]
    let catalog =
        match Catalog.create
                  [ { SsKey = kkey "Mod"; Name = nm "Mod"; Kinds = [ a; b ]; IsActive = true; ExtendedProperties = [] } ] [] with
        | Ok cat -> cat
        | Error e -> failwithf "catalog %A" e
    Assert.Equal(2, List.length (SsdtDdlEmitter.tableNameCollisionDiagnostics catalog))

[<Fact>]
let ``Table-name collision tripwire is silent when every kind has a distinct (schema, table)`` () =
    Assert.Empty (SsdtDdlEmitter.tableNameCollisionDiagnostics (corporateShape true true))

// ---------------------------------------------------------------------
// Slice 3b (DECISIONS 2026-06-13) — the identifier-length budget for
// generated constraint names.
// ---------------------------------------------------------------------

[<Fact>]
let ``IdentifierBudget: names within 128 chars pass through byte-identical`` () =
    let name = "FK_Task_User_CreatedBy"
    Assert.Equal<string>(name, IdentifierBudget.fit name)
    let exactly128 = String.replicate 128 "x"
    Assert.Equal<string>(exactly128, IdentifierBudget.fit exactly128)

[<Fact>]
let ``IdentifierBudget: over-budget names land at exactly 128 with the readable head and a deterministic hash suffix`` () =
    let long = "FK_" + String.replicate 90 "A" + "_" + String.replicate 90 "B"
    let fitted = IdentifierBudget.fit long
    Assert.Equal(128, fitted.Length)
    Assert.StartsWith(long.Substring(0, 115), fitted)
    Assert.Equal('_', fitted.[115])
    // Deterministic (T1): same input, same bytes.
    Assert.Equal<string>(fitted, IdentifierBudget.fit long)

[<Fact>]
let ``IdentifierBudget: two over-budget names sharing a 115-char head still get distinct fitted names`` () =
    let head = "FK_" + String.replicate 120 "A"
    let a = IdentifierBudget.fit (head + "_TARGET_ONE")
    let b = IdentifierBudget.fit (head + "_TARGET_TWO")
    Assert.NotEqual<string>(a, b)
    Assert.Equal(128, a.Length)
    Assert.Equal(128, b.Length)
