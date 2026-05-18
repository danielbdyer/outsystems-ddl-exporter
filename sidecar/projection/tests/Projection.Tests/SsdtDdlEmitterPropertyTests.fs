module Projection.Tests.SsdtDdlEmitterPropertyTests

// Slice 5.13.schema-axis-property-sweep — per-axis FsCheck property
// sweep on SCHEMA emission. Closes the SCHEMA-axis V2-driver gate
// for the seven emit features that shipped during the 2026-05-18
// emit-features arc: DEFAULT, CHECK, OnUpdate, NOCHECK,
// IGNORE_DUP_KEY, DATA_COMPRESSION, IsDisabled. (Matrix rows 12 +
// 53 + 55 + 58 + 59 + 182.)
//
// Why this exists. V2_DRIVER's per-axis stakes table sets the
// SCHEMA-axis bar at "structural-type-level enforcement plus per-axis
// property tests" (verification depth: Highest). The 2026-05-18 arc
// shipped the structural-type enforcement (closed-DU + record-field
// extensions); the per-axis canary tests in `SsdtDdlEmitterTests.fs`
// cover example-based assertions for one variant value each. This
// file lifts verification depth from "example-based" to
// "property-based" — exhaustively sweeping each variant value space
// via FsCheck. After this slice, SCHEMA-axis V2-driver mode is
// gated only by the named residuals (single-column-PK inline;
// computed columns; partition-scheme axis row 56) per HANDOFF
// 2026-05-18.
//
// The three pinned properties per axis. Each axis carries three
// property tests:
//
//   (P1) **T1 byte-determinism.** For any axis-value v, two
//        consecutive `SsdtDdlEmitter.emitSlices` runs against the
//        fixture-built catalog produce byte-identical body text per
//        kind. Pins A35 (Π's canonical output is a deterministic
//        stream) on the new axis values.
//
//   (P2) **Permutation invariance.** For any axis-value v, shuffling
//        the Modules list in the input Catalog produces a per-kind
//        file body byte-identical to the original-order emission.
//        Pins T11 (sibling commutativity) at the Modules-shuffle
//        level — the emitter is keyed by Kind.SsKey, not by list
//        position. Stronger shuffle axes (Kinds-within-Module;
//        Indexes-within-Kind for the sorted-by-SsKey index list)
//        also fall under P2 where the per-axis fixture exercises
//        them.
//
//   (P3) **V1-emission-shape coverage.** For any axis-value v, the
//        rendered body contains the V1-convention clause for that
//        value (e.g., DEFAULT 0 / DEFAULT N'<text>'; ON UPDATE
//        CASCADE; ALTER TABLE [...] WITH NOCHECK CHECK CONSTRAINT;
//        IGNORE_DUP_KEY = ON; DATA_COMPRESSION = ROW/PAGE;
//        ALTER INDEX [...] DISABLE). Pins the V1-equivalence claim
//        across the variant space, not just one variant per axis.
//
// (P3) is the FsCheck amplification of the per-feature canary
// tests — it exercises the rendered-output shape across every valid
// variant, not just one. (P1 + P2) pin the structural-property
// claims that operator-reality canary cannot exercise (it asserts
// PhysicalSchema diff, not byte-determinism of SQL text).

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Shared infrastructure. CanonicalizeIdentity.registered.Run is the canonical
// pre-emit pass per chapter A.4.7' slice η; bodyOf folds the standard
// `enrich → emitSlices → ArtifactByKind.toMap` walk into a single helper
// keyed on the kind SsKey under test.
// ---------------------------------------------------------------------------

type private FsResult<'a, 'b> = Microsoft.FSharp.Core.Result<'a, 'b>

let private mustOk (r: FsResult<'a, EmitError>) : 'a =
    match r with
    | FsResult.Ok v    -> v
    | FsResult.Error e -> invalidOp (sprintf "expected Ok; got %A" e)

let private enrich (c: Catalog) : Catalog =
    (CanonicalizeIdentity.registered.Run c |> Lineage.map (fun d -> d.Value)).Value

let private mkName (s: string) : Name = Name.create s |> Result.value

let private bodyOf (k: SsKey) (cat: Catalog) : string =
    let artifact = SsdtDdlEmitter.emitSlices (enrich cat) |> mustOk
    (ArtifactByKind.toMap artifact |> Map.find k).Body

/// Deterministic list permutation seeded by an int. Used to derive
/// shuffled module/kind/index orderings from FsCheck's generator
/// seed without introducing nondeterminism.
let private permute (seed: int) (xs: 'a list) : 'a list =
    let rng = System.Random(seed)
    xs |> List.map (fun x -> (rng.Next(), x)) |> List.sortBy fst |> List.map snd

let private shuffleModules (seed: int) (cat: Catalog) : Catalog =
    { cat with Modules = permute seed cat.Modules }

/// Wrap a kind under test in a catalog with a sibling sentinel module
/// so module-list shuffle is non-trivial (a single-module list shuffles
/// to itself). The sentinel is structurally minimal but valid.
let private wrap (kind: Kind) : Catalog =
    let sentinelKey = kindKey ["PropSentinel"]
    let sentinelIdAttr =
        { Attribute.create (attrKey ["PropSentinel"; "Id"]) (mkName "Id") Integer with
            Column       = { ColumnName = "ID"; IsNullable = false }
            IsPrimaryKey = true
            IsMandatory  = true }
    let sentinel =
        { Kind.create sentinelKey (mkName "PropSentinel")
            { Schema = "dbo"; Table = "OSUSR_PS_SENTINEL"; Catalog = None }
            [ sentinelIdAttr ]
          with References = []; Indexes = []; ColumnChecks = [] }
    {
        Modules =
            [ { SsKey = modKey "PropAxis"
                Name = mkName "PropAxis"
                Kinds = [ kind ]
                IsActive = true
                ExtendedProperties = [] }
              { SsKey = modKey "PropSentinelMod"
                Name = mkName "PropSentinelMod"
                Kinds = [ sentinel ]
                IsActive = true
                ExtendedProperties = [] } ]
        Sequences = []
    }

// ---------------------------------------------------------------------------
// Axis 1 — DEFAULT (matrix rows 53 + 182).
// ---------------------------------------------------------------------------

[<RequireQualifiedAccess>]
type DefaultAxis =
    | IntLiteral  of int
    | TextLiteral of string

// Text variants exclude empty string: `SqlLiteral.ofRaw Text ""` returns
// `NullLit` (V2 IR's NULL sentinel per `SqlLiteral.fs:75`). The NULL-as-
// default axis is structurally distinct from "DEFAULT <text>" and is
// deferred — covered by V2's "no DefaultValue → no DEFAULT clause" axis,
// which doesn't appear in the 2026-05-18 emit-features arc's scope.
let private defaultAxisGen : Gen<DefaultAxis> =
    Gen.oneof [
        Gen.choose (0, 999)
        |> Gen.map DefaultAxis.IntLiteral
        Gen.elements [ "active"; "pending"; "n_a"; "zero" ]
        |> Gen.map DefaultAxis.TextLiteral
    ]

let private defaultAxisKey = kindKey ["PropDefault"]

let private defaultAxisKind (axis: DefaultAxis) : Kind =
    let idAttr =
        { Attribute.create (attrKey ["PropDefault"; "Id"]) (mkName "Id") Integer with
            Column       = { ColumnName = "ID"; IsNullable = false }
            IsPrimaryKey = true
            IsMandatory  = true }
    let valAttr =
        let baseAttr = Attribute.create (attrKey ["PropDefault"; "Val"]) (mkName "Val") Integer
        match axis with
        | DefaultAxis.IntLiteral n ->
            { baseAttr with
                Column       = { ColumnName = "VAL"; IsNullable = false }
                DefaultValue = Some (SqlLiteral.ofRaw Integer (string n))
                IsMandatory  = true }
        | DefaultAxis.TextLiteral s ->
            { baseAttr with
                Type         = Text
                Length       = Some 100
                Column       = { ColumnName = "VAL"; IsNullable = false }
                DefaultValue = Some (SqlLiteral.ofRaw Text s)
                IsMandatory  = true }
    { Kind.create defaultAxisKey (mkName "PropDefault")
        { Schema = "dbo"; Table = "OSUSR_PD_DEFAULT"; Catalog = None }
        [ idAttr; valAttr ]
      with References = []; Indexes = [] }

let private defaultExpectedClause (axis: DefaultAxis) : string =
    match axis with
    | DefaultAxis.IntLiteral n  -> sprintf "DEFAULT %d" n
    | DefaultAxis.TextLiteral s -> sprintf "DEFAULT N'%s'" s

// ---------------------------------------------------------------------------
// Axis 2 — CHECK (matrix rows 12 + 182).
// ---------------------------------------------------------------------------

[<RequireQualifiedAccess>]
type CheckAxis =
    | NonNegative
    | UpperBound of int

let private checkAxisGen : Gen<CheckAxis> =
    Gen.oneof [
        Gen.constant CheckAxis.NonNegative
        Gen.choose (1, 1000) |> Gen.map CheckAxis.UpperBound
    ]

let private checkAxisKey = kindKey ["PropCheck"]

let private checkAxisKind (axis: CheckAxis) : Kind =
    let idAttr =
        { Attribute.create (attrKey ["PropCheck"; "Id"]) (mkName "Id") Integer with
            Column       = { ColumnName = "ID"; IsNullable = false }
            IsPrimaryKey = true
            IsMandatory  = true }
    let valAttr =
        { Attribute.create (attrKey ["PropCheck"; "Val"]) (mkName "Val") Integer with
            Column      = { ColumnName = "VAL"; IsNullable = false }
            IsMandatory = true }
    let constraintName, predicate =
        match axis with
        | CheckAxis.NonNegative ->
            "CK_PropCheck_NonNeg", "([VAL] >= 0)"
        | CheckAxis.UpperBound n ->
            sprintf "CK_PropCheck_LtN_%d" n,
            sprintf "([VAL] < %d)" n
    let check =
        ColumnCheck.create
            (attrKey ["PropCheck"; constraintName])
            (Name.create constraintName |> Result.toOption)
            predicate
            false
        |> Result.value
    { Kind.create checkAxisKey (mkName "PropCheck")
        { Schema = "dbo"; Table = "OSUSR_PC_CHECK"; Catalog = None }
        [ idAttr; valAttr ]
      with ColumnChecks = [ check ]; References = []; Indexes = [] }

let private checkExpectedConstraintName (axis: CheckAxis) : string =
    match axis with
    | CheckAxis.NonNegative  -> "CK_PropCheck_NonNeg"
    | CheckAxis.UpperBound n -> sprintf "CK_PropCheck_LtN_%d" n

// ---------------------------------------------------------------------------
// Axis 3 + 4 — OnUpdate (rows 58) + NOCHECK trust state (row 59). Single
// fixture pair: A is the parent (PK); B carries the FK whose axis values
// are (OnUpdate variant × IsConstraintTrusted bool).
// ---------------------------------------------------------------------------

let private fkParentKey   = kindKey ["PropFkParent"]
let private fkChildKey    = kindKey ["PropFkChild"]
let private fkParentPkKey = attrKey ["PropFkParent"; "Id"]
let private fkChildPkKey  = attrKey ["PropFkChild"; "Id"]
let private fkChildFkKey  = attrKey ["PropFkChild"; "ParentId"]
let private fkRefKey      = refKey  ["PropFkChild"; "ParentId"]

let private fkParentKind : Kind =
    { Kind.create fkParentKey (mkName "PropFkParent")
        { Schema = "dbo"; Table = "OSUSR_PFP_PARENT"; Catalog = None }
        [ { Attribute.create fkParentPkKey (mkName "Id") Integer with
                Column       = { ColumnName = "ID"; IsNullable = false }
                IsPrimaryKey = true
                IsMandatory  = true } ]
      with References = []; Indexes = [] }

let private fkChildKind (onUpdate: ReferenceAction option) (trusted: bool) : Kind =
    let reference =
        { Reference.create fkRefKey (mkName "FkToParent") fkChildFkKey fkParentKey with
            OnDelete            = Cascade
            HasDbConstraint     = true
            OnUpdate            = onUpdate
            IsConstraintTrusted = trusted }
    { Kind.create fkChildKey (mkName "PropFkChild")
        { Schema = "dbo"; Table = "OSUSR_PFC_CHILD"; Catalog = None }
        [ { Attribute.create fkChildPkKey (mkName "Id") Integer with
                Column       = { ColumnName = "ID"; IsNullable = false }
                IsPrimaryKey = true
                IsMandatory  = true }
          { Attribute.create fkChildFkKey (mkName "ParentId") Integer with
                Column      = { ColumnName = "PARENT_ID"; IsNullable = false }
                IsMandatory = true } ]
      with References = [ reference ]; Indexes = [] }

let private fkCatalog (onUpdate: ReferenceAction option) (trusted: bool) : Catalog =
    {
        Modules =
            [ { SsKey = modKey "PropFkParentMod"
                Name = mkName "PropFkParentMod"
                Kinds = [ fkParentKind ]
                IsActive = true
                ExtendedProperties = [] }
              { SsKey = modKey "PropFkChildMod"
                Name = mkName "PropFkChildMod"
                Kinds = [ fkChildKind onUpdate trusted ]
                IsActive = true
                ExtendedProperties = [] } ]
        Sequences = []
    }

let private onUpdateAxisGen : Gen<ReferenceAction option> =
    Gen.elements [ None; Some NoAction; Some Cascade; Some SetNull; Some Restrict ]

let private onUpdateExpectedClause (axis: ReferenceAction option) : string option =
    match axis with
    | None             -> None
    | Some NoAction    -> Some "ON UPDATE NO ACTION"
    | Some Cascade     -> Some "ON UPDATE CASCADE"
    | Some SetNull     -> Some "ON UPDATE SET NULL"
    | Some Restrict    -> Some "ON UPDATE NO ACTION"  // T-SQL: RESTRICT renders as NO ACTION

// ---------------------------------------------------------------------------
// Axis 5 + 6 + 7 — index features: IGNORE_DUP_KEY (row 55), DATA_COMPRESSION
// (row 56's single-value portion; partition-scheme axis is the named
// residual), IsDisabled (row 55). One fixture, three orthogonal axis values.
// ---------------------------------------------------------------------------

let private idxAxisKey  = kindKey ["PropIdx"]
let private idxIdAttr   = attrKey ["PropIdx"; "Id"]
let private idxNameAttr = attrKey ["PropIdx"; "Name"]

/// Two indexes per kind so the SsKey-sorted permutation invariance
/// (sibling-Π emitter sorts Indexes) is non-trivial. The second index
/// is on a different attribute pair; both carry the axis values under
/// test so any per-index oversight surfaces.
let private idxAxisKind
    (ignoreDup: bool)
    (disabled: bool)
    (compression: DataCompressionLevel option)
    : Kind =
    let idAttr =
        { Attribute.create idxIdAttr (mkName "Id") Integer with
            Column       = { ColumnName = "ID"; IsNullable = false }
            IsPrimaryKey = true
            IsMandatory  = true }
    let nameAttr =
        { Attribute.create idxNameAttr (mkName "Name") Text with
            Column      = { ColumnName = "NAME"; IsNullable = false }
            Length      = Some 100
            IsMandatory = true }
    let idxA =
        { Index.create
            (idxKey ["PropIdx"; "IX_Name_A"]) (mkName "IX_PropIdx_NameA")
            (IndexColumn.ascendingList [ idxNameAttr ]) with
            IsUnique           = true
            IgnoreDuplicateKey = ignoreDup
            IsDisabled         = disabled
            DataCompression    = compression }
    let idxB =
        { Index.create
            (idxKey ["PropIdx"; "IX_Name_B"]) (mkName "IX_PropIdx_NameB")
            (IndexColumn.ascendingList [ idxNameAttr; idxIdAttr ]) with
            IsUnique           = false
            IgnoreDuplicateKey = ignoreDup
            IsDisabled         = disabled
            DataCompression    = compression }
    { Kind.create idxAxisKey (mkName "PropIdx")
        { Schema = "dbo"; Table = "OSUSR_PI_IDX"; Catalog = None }
        [ idAttr; nameAttr ]
      with Indexes = [ idxA; idxB ]; References = []; ColumnChecks = [] }

let private idxIgnoreDupGen : Gen<bool> = Gen.elements [ false; true ]
let private idxIsDisabledGen : Gen<bool> = Gen.elements [ false; true ]
let private idxCompressionGen : Gen<DataCompressionLevel option> =
    Gen.elements
        [ None
          Some DataCompressionLevel.None
          Some DataCompressionLevel.Row
          Some DataCompressionLevel.Page ]

let private idxCompressionExpectedClause (c: DataCompressionLevel option) : string option =
    match c with
    | None                              -> None
    | Some DataCompressionLevel.None    -> Some "DATA_COMPRESSION = NONE"
    | Some DataCompressionLevel.Row     -> Some "DATA_COMPRESSION = ROW"
    | Some DataCompressionLevel.Page    -> Some "DATA_COMPRESSION = PAGE"

// ---------------------------------------------------------------------------
// Generator-registration shim (FsCheck.Xunit 2.x convention; mirrors
// `UserFkReflowPropertyTests.Generators`).
// ---------------------------------------------------------------------------

type Generators =
    static member DefaultAxis ()    = Arb.fromGen defaultAxisGen
    static member CheckAxis ()      = Arb.fromGen checkAxisGen
    static member OnUpdateAxis ()   = Arb.fromGen onUpdateAxisGen
    static member IdxIgnoreDup ()   = Arb.fromGen idxIgnoreDupGen
    static member IdxIsDisabled ()  = Arb.fromGen idxIsDisabledGen
    static member IdxCompression () = Arb.fromGen idxCompressionGen

// ---------------------------------------------------------------------------
// Axis 1 — DEFAULT properties (P1 / P2 / P3).
// ---------------------------------------------------------------------------

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``5.13.schema-axis-property-sweep: DEFAULT (P1) T1 byte-determinism`` (axis: DefaultAxis) : bool =
    let cat = wrap (defaultAxisKind axis)
    bodyOf defaultAxisKey cat = bodyOf defaultAxisKey cat

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``5.13.schema-axis-property-sweep: DEFAULT (P2) permutation invariance on Modules`` (axis: DefaultAxis) (seed: int) : bool =
    let cat = wrap (defaultAxisKind axis)
    bodyOf defaultAxisKey cat = bodyOf defaultAxisKey (shuffleModules seed cat)

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``5.13.schema-axis-property-sweep: DEFAULT (P3) V1 emission clause surfaces`` (axis: DefaultAxis) : bool =
    let body = bodyOf defaultAxisKey (wrap (defaultAxisKind axis))
    body.Contains (defaultExpectedClause axis)

// ---------------------------------------------------------------------------
// Axis 2 — CHECK properties.
// ---------------------------------------------------------------------------

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``5.13.schema-axis-property-sweep: CHECK (P1) T1 byte-determinism`` (axis: CheckAxis) : bool =
    let cat = wrap (checkAxisKind axis)
    bodyOf checkAxisKey cat = bodyOf checkAxisKey cat

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``5.13.schema-axis-property-sweep: CHECK (P2) permutation invariance on Modules`` (axis: CheckAxis) (seed: int) : bool =
    let cat = wrap (checkAxisKind axis)
    bodyOf checkAxisKey cat = bodyOf checkAxisKey (shuffleModules seed cat)

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``5.13.schema-axis-property-sweep: CHECK (P3) named constraint surfaces`` (axis: CheckAxis) : bool =
    let body = bodyOf checkAxisKey (wrap (checkAxisKind axis))
    body.Contains (sprintf "CONSTRAINT [%s] CHECK" (checkExpectedConstraintName axis))

// ---------------------------------------------------------------------------
// Axis 3 — OnUpdate properties.
// ---------------------------------------------------------------------------

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``5.13.schema-axis-property-sweep: OnUpdate (P1) T1 byte-determinism`` (axis: ReferenceAction option) : bool =
    let cat = fkCatalog axis true
    bodyOf fkChildKey cat = bodyOf fkChildKey cat

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``5.13.schema-axis-property-sweep: OnUpdate (P2) permutation invariance on Modules`` (axis: ReferenceAction option) (seed: int) : bool =
    let cat = fkCatalog axis true
    bodyOf fkChildKey cat = bodyOf fkChildKey (shuffleModules seed cat)

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``5.13.schema-axis-property-sweep: OnUpdate (P3) ON UPDATE clause surfaces (or absent for None)`` (axis: ReferenceAction option) : bool =
    let body = bodyOf fkChildKey (fkCatalog axis true)
    match onUpdateExpectedClause axis with
    | None        -> not (body.Contains "ON UPDATE")
    | Some clause -> body.Contains clause

// ---------------------------------------------------------------------------
// Axis 4 — NOCHECK trust-state properties.
// ---------------------------------------------------------------------------

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``5.13.schema-axis-property-sweep: NOCHECK (P1) T1 byte-determinism`` (trusted: bool) : bool =
    let cat = fkCatalog None trusted
    bodyOf fkChildKey cat = bodyOf fkChildKey cat

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``5.13.schema-axis-property-sweep: NOCHECK (P2) permutation invariance on Modules`` (trusted: bool) (seed: int) : bool =
    let cat = fkCatalog None trusted
    bodyOf fkChildKey cat = bodyOf fkChildKey (shuffleModules seed cat)

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``5.13.schema-axis-property-sweep: NOCHECK (P3) ALTER WITH NOCHECK present iff not trusted`` (trusted: bool) : bool =
    let body = bodyOf fkChildKey (fkCatalog None trusted)
    let hasAlter = body.Contains "WITH NOCHECK CHECK CONSTRAINT"
    hasAlter = not trusted

// ---------------------------------------------------------------------------
// Axis 5 — IGNORE_DUP_KEY properties.
// ---------------------------------------------------------------------------

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``5.13.schema-axis-property-sweep: IGNORE_DUP_KEY (P1) T1 byte-determinism`` (ignoreDup: bool) : bool =
    let cat = wrap (idxAxisKind ignoreDup false None)
    bodyOf idxAxisKey cat = bodyOf idxAxisKey cat

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``5.13.schema-axis-property-sweep: IGNORE_DUP_KEY (P2) permutation invariance on Modules`` (ignoreDup: bool) (seed: int) : bool =
    let cat = wrap (idxAxisKind ignoreDup false None)
    bodyOf idxAxisKey cat = bodyOf idxAxisKey (shuffleModules seed cat)

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``5.13.schema-axis-property-sweep: IGNORE_DUP_KEY (P3) clause present iff true`` (ignoreDup: bool) : bool =
    let body = bodyOf idxAxisKey (wrap (idxAxisKind ignoreDup false None))
    body.Contains "IGNORE_DUP_KEY = ON" = ignoreDup

// ---------------------------------------------------------------------------
// Axis 6 — DATA_COMPRESSION properties (single-value; partition-scheme axis
// row 56 deferred per HANDOFF 2026-05-18 named residual).
// ---------------------------------------------------------------------------

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``5.13.schema-axis-property-sweep: DATA_COMPRESSION (P1) T1 byte-determinism`` (compression: DataCompressionLevel option) : bool =
    let cat = wrap (idxAxisKind false false compression)
    bodyOf idxAxisKey cat = bodyOf idxAxisKey cat

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``5.13.schema-axis-property-sweep: DATA_COMPRESSION (P2) permutation invariance on Modules`` (compression: DataCompressionLevel option) (seed: int) : bool =
    let cat = wrap (idxAxisKind false false compression)
    bodyOf idxAxisKey cat = bodyOf idxAxisKey (shuffleModules seed cat)

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``5.13.schema-axis-property-sweep: DATA_COMPRESSION (P3) clause surfaces (or absent for None)`` (compression: DataCompressionLevel option) : bool =
    let body = bodyOf idxAxisKey (wrap (idxAxisKind false false compression))
    match idxCompressionExpectedClause compression with
    | None        -> not (body.Contains "DATA_COMPRESSION")
    | Some clause -> body.Contains clause

// ---------------------------------------------------------------------------
// Axis 7 — IsDisabled properties.
// ---------------------------------------------------------------------------

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``5.13.schema-axis-property-sweep: IsDisabled (P1) T1 byte-determinism`` (disabled: bool) : bool =
    let cat = wrap (idxAxisKind false disabled None)
    bodyOf idxAxisKey cat = bodyOf idxAxisKey cat

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``5.13.schema-axis-property-sweep: IsDisabled (P2) permutation invariance on Modules`` (disabled: bool) (seed: int) : bool =
    let cat = wrap (idxAxisKind false disabled None)
    bodyOf idxAxisKey cat = bodyOf idxAxisKey (shuffleModules seed cat)

[<Property(Arbitrary = [| typeof<Generators> |])>]
let ``5.13.schema-axis-property-sweep: IsDisabled (P3) ALTER INDEX DISABLE present iff true`` (disabled: bool) : bool =
    let body = bodyOf idxAxisKey (wrap (idxAxisKind false disabled None))
    body.Contains "ALTER INDEX" = disabled
