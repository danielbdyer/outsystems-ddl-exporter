module Projection.Tests.ChangeAlgebraSweepTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core

// ===========================================================================
// THE VECTOR · Wave 2 · M3 — the swept change-algebra property (§5.2,
// "the highest-value unfired proof in the system").
//
// `CatalogDiffTests.fs` witnesses the change-algebra round-trip law on a
// fixed set of HAND-BUILT fixtures (sampleCatalog perturbed by named edits).
// M3 raises that from fixture-witnessed to PROPERTY-witnessed: `genCatalogPair`
// draws a random VALID base Catalog A and a random VALID perturbed Catalog B,
// and the `[<Property>]` tests below sweep the master equation, the no-cheat
// dependence, and the norm-consistency law over ~100 such pairs each.
//
// **In-process backend only (Wave-2 scope).** The reverse-leg movement engine
// is done, so the in-process change algebra (`CatalogDiff.between` / `applyDiff`
// over IR values) is the backend under test here. The live-deploy / Docker
// backend (round-tripping a delta through a real SQL deployment) stays
// separately gated and is NOT exercised by this sweep — that is the
// INTEGRATOR's matrix territory, not Wave-2's.
//
// **Validity by construction.** Every generated Catalog (A and every
// perturbation step toward B) is built through `Catalog.create` /
// `Module.create` / the attribute smart constructors, so the five
// referential-integrity invariants (module/kind/sequence key disjointness,
// reference dangling-source/target, reference constraint-state quadrant, index
// dangling-column, NM-14 storage/type agreement) hold by construction. The
// generator deliberately carries NO references and NO indexes so the
// add/remove-kind and add/remove-attribute edits cannot strand an FK source,
// FK target, or index column — keeping each B valid no matter the edit list.
// The generator is SELF-CONTAINED: it depends only on `Projection.Core`'s
// public smart constructors, never on another test file's private members.
// ===========================================================================

/// Observe the displacement A → B. Wave-2's trio (M13) made
/// `CatalogDiff.between` total, so this is a thin name for the observational
/// differential; every property below calls `diff a b`.
let private diff (a: Catalog) (b: Catalog) : CatalogDiff =
    CatalogDiff.between a b

// ---------------------------------------------------------------------------
// Self-contained builders. Force-unwrap the single-arity `Result<'a>` from
// the production smart constructors (these inputs are constants known to
// satisfy the validators — the fixture posture).
// ---------------------------------------------------------------------------

let private nm (s: string) : Name = Name.create s |> Result.value

/// Distinct, stable `OssysOriginal` key from an int — so identity is
/// rename-stable (a kind/attribute rename CHANGES the Name, never the SsKey)
/// and the diff threads renames natively as `Renamed`, not as drop+add.
let private key (n: int) : SsKey =
    SsKey.ossysOriginal (System.Guid(n, 0s, 0s, 0uy, 0uy, 0uy, 0uy, 0uy, 0uy, 0uy, 0uy))

let private tableId (n: int) : TableId =
    TableId.create "dbo" (sprintf "T%d" n) |> Result.value

/// A minimum-evidence attribute. No `SqlStorage` (so NM-14's storage/type
/// agreement is vacuous), an explicit lowercase-derived column name, and the
/// caller-chosen nullability.
let private mkAttr (k: SsKey) (logical: string) (ptype: PrimitiveType) (isPk: bool) (nullable: bool) : Attribute =
    { Attribute.create k (nm logical) ptype with
        Column = ColumnRealization.create (logical.ToUpperInvariant()) nullable |> Result.value
        IsPrimaryKey = isPk
        IsMandatory = isPk }

/// A kind over the given attributes, carrying NO references and NO indexes
/// (the generator's discipline — see the header). The first attribute is the
/// PK by construction.
let private mkKind (k: SsKey) (name: string) (tid: int) (attrs: Attribute list) : Kind =
    Kind.create k (nm name) (tableId tid) attrs

/// A single-module catalog over the given kinds.
let private catalogOf (kinds: Kind list) : Catalog =
    let m = Module.create (key 90000) (nm "M") kinds true [] |> Result.value
    Catalog.create [ m ] [] |> Result.value

// ---------------------------------------------------------------------------
// Generator alphabets.
// ---------------------------------------------------------------------------

let private allPrimitiveTypes : PrimitiveType list =
    [ Integer; Decimal; Text; Boolean; DateTime ]

let private genBool : Gen<bool> = Gen.elements [ true; false ]

let rec private genAll (gs: Gen<'a> list) : Gen<'a list> =
    match gs with
    | [] -> Gen.constant []
    | g :: rest -> gen { let! x = g in let! xs = genAll rest in return x :: xs }

// ---------------------------------------------------------------------------
// Key-space partitions. Kinds, attributes, and the perturbation's freshly
// minted entities each draw from a disjoint integer band so no `add` edit can
// ever collide an SsKey with an existing one (preserving A4 disjointness).
//   kinds (base)      : 1_000 ..
//   attributes (base) : 10_000 .. (10_000 + kindIx*100 + attrIx)
//   fresh kinds       : 50_000 ..
//   fresh attributes  : 60_000 ..
// ---------------------------------------------------------------------------

let private baseKindKey (i: int) : SsKey = key (1_000 + i)
let private baseAttrKey (kindIx: int) (attrIx: int) : SsKey = key (10_000 + kindIx * 100 + attrIx)

/// One generated attribute (its facets freely chosen). The PK attribute is
/// forced non-nullable and integer-typed so it reads like a real surrogate
/// key; non-PK attributes vary across the full facet surface.
let private genAttr (k: SsKey) (logical: string) (isPk: bool) : Gen<Attribute> =
    if isPk then
        Gen.constant (mkAttr k logical Integer true false)
    else
        gen {
            let! ptype = Gen.elements allPrimitiveTypes
            let! nullable = genBool
            let! len = Gen.frequency [ 1, Gen.constant None; 2, Gen.map Some (Gen.choose (1, 4000)) ]
            return { mkAttr k logical ptype false nullable with Length = len }
        }

/// One generated base kind: a PK attribute plus 0..3 non-PK attributes.
let private genKind (kindIx: int) : Gen<Kind> =
    gen {
        let! nExtra = Gen.choose (0, 3)
        let pkGen = genAttr (baseAttrKey kindIx 0) (sprintf "K%d_Id" kindIx) true
        let extraGens =
            [ for j in 1 .. nExtra ->
                genAttr (baseAttrKey kindIx j) (sprintf "K%d_A%d" kindIx j) false ]
        let! attrs = genAll (pkGen :: extraGens)
        return mkKind (baseKindKey kindIx) (sprintf "K%d" kindIx) kindIx attrs
    }

/// A valid base Catalog A: a single module of 1..4 kinds.
let private genBaseCatalog : Gen<Catalog> =
    gen {
        let! nKinds = Gen.choose (1, 4)
        let! kinds = genAll [ for i in 0 .. nKinds - 1 -> genKind i ]
        return catalogOf kinds
    }

// ---------------------------------------------------------------------------
// The perturbation algebra. Each `Edit` transforms a kinds list into another
// VALID kinds list (every step is constructed, never validated). The list of
// edits is folded over A's kinds to produce B's kinds. A fresh-key counter
// threaded through the fold guarantees every minted kind / attribute SsKey is
// globally unique.
// ---------------------------------------------------------------------------

/// A perturbation drawn against the kinds present at the moment it is applied.
/// (We sample concrete indices/types now and resolve them positionally during
/// the fold, clamping to the live list so an earlier `RemoveKind` can never
/// strand a later index.)
type private Edit =
    /// Append a brand-new kind (PK + one extra attribute), keyed from the
    /// fresh band so it collides with nothing in A or B.
    | AddKind of extraType: PrimitiveType
    /// Drop the kind at this position — but only if more than one kind
    /// remains (the module must keep ≥ 1 kind, LR1).
    | RemoveKind of pos: int
    /// Rename the kind at this position (Name changes; SsKey is stable, so the
    /// diff threads it as `Renamed`, not drop+add).
    | RenameKind of pos: int
    /// Reshape a facet of a non-PK attribute on the kind at this position:
    /// flip nullability, change the declared type, and move the length.
    | ReshapeAttribute of pos: int * newType: PrimitiveType * newNullable: bool * newLen: int option
    /// Append a brand-new attribute to the kind at this position (fresh-band
    /// key).
    | AddAttribute of pos: int * ptype: PrimitiveType * nullable: bool
    /// Drop a non-PK attribute from the kind at this position — only if the
    /// kind would keep ≥ 1 attribute (and the PK is never dropped).
    | RemoveAttribute of pos: int

let private genEdit : Gen<Edit> =
    Gen.oneof
        [ gen { let! t = Gen.elements allPrimitiveTypes in return AddKind t }
          gen { let! p = Gen.choose (0, 5) in return RemoveKind p }
          gen { let! p = Gen.choose (0, 5) in return RenameKind p }
          gen {
              let! p = Gen.choose (0, 5)
              let! t = Gen.elements allPrimitiveTypes
              let! n = genBool
              let! len = Gen.frequency [ 1, Gen.constant None; 2, Gen.map Some (Gen.choose (1, 4000)) ]
              return ReshapeAttribute (p, t, n, len)
          }
          gen {
              let! p = Gen.choose (0, 5)
              let! t = Gen.elements allPrimitiveTypes
              let! n = genBool
              return AddAttribute (p, t, n)
          }
          gen { let! p = Gen.choose (0, 5) in return RemoveAttribute p } ]

/// Apply one edit to the live kinds list, threading the fresh-key counter.
/// Every branch returns a list that still satisfies the catalog invariants:
/// kinds stay non-empty, kinds keep ≥ 1 attribute including their PK, and all
/// freshly minted SsKeys come from the disjoint fresh band.
let private applyEdit (kinds: Kind list, fresh: int) (edit: Edit) : Kind list * int =
    let n = List.length kinds
    // Resolve a sampled position against the live list (empty list is
    // impossible — the module always holds ≥ 1 kind).
    let at (pos: int) : int = if n = 0 then 0 else pos % n
    match edit with
    | AddKind extraType ->
        let kKey = key (50_000 + fresh)
        let pk = mkAttr (key (60_000 + fresh * 10)) (sprintf "F%d_Id" fresh) Integer true false
        let extra = mkAttr (key (60_000 + fresh * 10 + 1)) (sprintf "F%d_A" fresh) extraType false true
        let newKind = mkKind kKey (sprintf "F%d" fresh) (50_000 + fresh) [ pk; extra ]
        kinds @ [ newKind ], fresh + 1
    | RemoveKind pos ->
        if n <= 1 then kinds, fresh
        else
            let i = at pos
            (kinds |> List.mapi (fun j k -> j, k) |> List.filter (fun (j, _) -> j <> i) |> List.map snd), fresh
    | RenameKind pos ->
        if n = 0 then kinds, fresh
        else
            let i = at pos
            kinds
            |> List.mapi (fun j k -> if j = i then { k with Name = nm (sprintf "R%d_%d" i fresh) } else k),
            fresh + 1
    | ReshapeAttribute (pos, newType, newNullable, newLen) ->
        if n = 0 then kinds, fresh
        else
            let i = at pos
            kinds
            |> List.mapi (fun j k ->
                if j <> i then k
                else
                    // Reshape the FIRST non-PK attribute, if any (the PK is
                    // never reshaped — it stays a stable Integer surrogate).
                    // `List.mapFold` threads the "already reshaped one" flag
                    // purely, avoiding a closure-captured mutable.
                    let reshaped, _ =
                        k.Attributes
                        |> List.mapFold
                            (fun touched a ->
                                if (not a.IsPrimaryKey) && not touched then
                                    { a with
                                        Type = newType
                                        Length = newLen
                                        Column = { a.Column with IsNullable = newNullable } },
                                    true
                                else a, touched)
                            false
                    { k with Attributes = reshaped }),
            fresh
    | AddAttribute (pos, ptype, nullable) ->
        if n = 0 then kinds, fresh
        else
            let i = at pos
            kinds
            |> List.mapi (fun j k ->
                if j <> i then k
                else
                    let a =
                        mkAttr (key (60_000 + fresh * 10 + 2)) (sprintf "N%d_%d" i fresh) ptype false nullable
                    { k with Attributes = k.Attributes @ [ a ] }),
            fresh + 1
    | RemoveAttribute pos ->
        if n = 0 then kinds, fresh
        else
            let i = at pos
            kinds
            |> List.mapi (fun j k ->
                if j <> i then k
                else
                    // Drop the FIRST non-PK attribute, if any — never the PK,
                    // and only when the kind keeps ≥ 1 attribute afterward.
                    let nonPk = k.Attributes |> List.filter (fun a -> not a.IsPrimaryKey)
                    match nonPk with
                    | [] -> k
                    | victim :: _ ->
                        { k with Attributes = k.Attributes |> List.filter (fun a -> a.SsKey <> victim.SsKey) }),
            fresh

/// Fold the edit list over A's kinds to produce B (validity preserved at every
/// step; the fresh-key counter starts past A's bands).
let private perturb (a: Catalog) (edits: Edit list) : Catalog =
    let kindsA = Catalog.allKinds a
    let kindsB, _ = List.fold applyEdit (kindsA, 0) edits
    catalogOf kindsB

/// THE generator: a base Catalog A and a perturbed Catalog B, both valid by
/// construction. `genCatalogPair : Gen<Catalog * Catalog>`.
let private genCatalogPair : Gen<Catalog * Catalog> =
    gen {
        let! a = genBaseCatalog
        let! nEdits = Gen.choose (0, 6)
        let! edits = genAll (List.replicate nEdits genEdit)
        let b = perturb a edits
        return a, b
    }

let private arbCatalogPair : Arbitrary<Catalog * Catalog> = Arb.fromGen genCatalogPair

// ---------------------------------------------------------------------------
// Property 1 — the T16 master equation (in-process backend; applyTo=applyDiff,
// plan=between). `applyDiff (between A B) A` reproduces B modulo the captured
// surface, witnessed order-insensitively by `isEmpty (between B (…))`.
// ---------------------------------------------------------------------------

[<Property>]
let ``M3 (swept T16): applyDiff (between A B) A reproduces B over genCatalogPair`` () =
    Prop.forAll arbCatalogPair (fun (a, b) ->
        let d = diff a b
        let reconstructed = CatalogDiff.applyDiff a d
        // The round-trip residual against B is empty over the captured surface.
        CatalogDiff.isEmpty (diff b reconstructed))

// ---------------------------------------------------------------------------
// Property 2 — no-cheat: `applyDiff` genuinely THREADS its base argument, it is
// not `fun _ d -> target d`. We apply the A→B delta to a DIFFERENT base
// C = A + one extra kind the delta never mentions; that extra kind must
// survive the apply (a target-copying impl would have dropped it).
// ---------------------------------------------------------------------------

[<Property>]
let ``M3 (swept no-cheat): applyDiff threads the passed-in base, not the recorded target`` () =
    Prop.forAll arbCatalogPair (fun (a, b) ->
        let d = diff a b
        // C = A plus a fresh kind whose SsKey is in neither A nor B (the fresh
        // band sits above every base key; the perturbation never reaches it
        // because C is built independently of B's edit list).
        let extraKey = key 70_000
        let extraKind =
            mkKind extraKey "ExtraProbe" 70_000
                [ mkAttr (key 71_000) "Extra_Id" Integer true false ]
        let c = catalogOf (Catalog.allKinds a @ [ extraKind ])
        let result = CatalogDiff.applyDiff c d
        // The extra kind (present only in C, named by neither side of the diff)
        // must ride through — proving the result is threaded from the base, not
        // copied from `target d`.
        (Catalog.tryFindKind extraKey result).IsSome
        // …and it is genuinely absent from the recorded target, so this is a
        // real discriminator (not vacuously true).
        && (Catalog.tryFindKind extraKey (CatalogDiff.target d)).IsNone)

// ---------------------------------------------------------------------------
// Property 3 — norm consistency. `norm d = 0 ⟺ isEmpty d`; `norm (between A A)
// = 0`; and `norm` equals the sum of ALL its `ChannelCounts` fields (a guard
// against a missed channel — if `norm` or `channelCounts` ever dropped a
// channel, the two would diverge on a B that touches that channel).
// ---------------------------------------------------------------------------

/// Sum of every field of a `ChannelCounts` — the independent recomputation of
/// the norm. Touches all 20 channels so a dropped channel in EITHER `norm` or
/// `channelCounts` is caught.
let private sumChannelCounts (c: CatalogDiff.ChannelCounts) : int =
    c.RenamedKinds + c.AddedKinds + c.RemovedKinds + c.ChangedKinds
    + c.AddedAttributes + c.RemovedAttributes + c.RenamedAttributes + c.ChangedAttributes
    + c.AddedReferences + c.RemovedReferences + c.RenamedReferences + c.ChangedReferences
    + c.AddedIndexes + c.RemovedIndexes + c.RenamedIndexes + c.ChangedIndexes
    + c.AddedSequences + c.RemovedSequences + c.RenamedSequences + c.ChangedSequences

[<Property>]
let ``M3 (swept norm): norm d = 0 iff isEmpty d, and norm = sum of channel counts`` () =
    Prop.forAll arbCatalogPair (fun (a, b) ->
        let d = diff a b
        let n = CatalogDiff.norm d
        let emptyIffZero = (n = 0) = CatalogDiff.isEmpty d
        let normIsChannelSum = n = sumChannelCounts (CatalogDiff.channelCounts d)
        // Identity diff: A against itself is empty and norm 0.
        let identity = CatalogDiff.norm (diff a a)
        emptyIffZero && normIsChannelSum && identity = 0)
