module Projection.Tests.TopologicalOrderPassTests

open Xunit
open FsCheck
open FsCheck.Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Acyclic happy path — the synthetic fixture's only FK is Order → Customer,
// so the order is some permutation that keeps Customer before Order.
// Country has no references, so it floats to either end depending on its
// SsKey-sorted position.
// ---------------------------------------------------------------------------

[<Fact>]
let ``acyclic catalog produces Mode = Topological`` () =
    let result = TopologicalOrderPass.run sampleCatalog
    Assert.Equal(Topological, result.Value.Mode)

[<Fact>]
let ``Order is non-empty and includes every kind exactly once`` () =
    let result = TopologicalOrderPass.run sampleCatalog
    let order = result.Value.Order
    let allKindKeys =
        Catalog.allKinds sampleCatalog
        |> List.map (fun k -> k.SsKey)
        |> Set.ofList
    Assert.Equal(allKindKeys.Count, order.Length)
    Assert.Equal<Set<SsKey>>(allKindKeys, Set.ofList order)

[<Fact>]
let ``Customer precedes Order in the output`` () =
    // Order references Customer ⇒ Customer is the parent ⇒ comes first.
    let result = TopologicalOrderPass.run sampleCatalog
    Assert.True(TopologicalOrder.precedes customerKey orderKey result.Value)

// ---------------------------------------------------------------------------
// Property: parent precedes child for every FK edge in the output.
// ---------------------------------------------------------------------------

[<Fact>]
let ``every FK edge has parent before child in the output`` () =
    let result = TopologicalOrderPass.run sampleCatalog
    // Walk the catalog's references; for each (source, target),
    // target should precede source in Order.
    for k in Catalog.allKinds sampleCatalog do
        for r in k.References do
            Assert.True(
                TopologicalOrder.precedes r.TargetKind k.SsKey result.Value,
                sprintf "Expected %A to precede %A" r.TargetKind k.SsKey)

// ---------------------------------------------------------------------------
// Permutation invariance — the V2 contract. The output is byte-identical
// for any permutation of the input modules / kinds / references.
// (Phrased as V2's contract; the diagnostic value of the V1 finding is
// preserved in DECISIONS.md 2026-05-08.)
// ---------------------------------------------------------------------------

let private permuteRefs (k: Kind) : Kind =
    { k with References = List.rev k.References }

let private permuteKinds (m: Module) : Module =
    { m with Kinds = m.Kinds |> List.map permuteRefs |> List.rev }

let private permuteModules (c: Catalog) : Catalog =
    { Modules = c.Modules |> List.map permuteKinds |> List.rev }

[<Fact>]
let ``contract: TopologicalOrder.run is invariant under input permutation`` () =
    let direct  = TopologicalOrderPass.run sampleCatalog
    let permuted = TopologicalOrderPass.run (permuteModules sampleCatalog)
    Assert.Equal(direct.Value, permuted.Value)

[<Property>]
let ``property: output is identical across input permutations`` (reverseModules: bool) (reverseKinds: bool) (reverseRefs: bool) =
    let perturb (c: Catalog) : Catalog =
        let withModules =
            { Modules =
                c.Modules
                |> List.map (fun m ->
                    let withKinds =
                        m.Kinds
                        |> List.map (fun k ->
                            if reverseRefs then { k with References = List.rev k.References } else k)
                    if reverseKinds then { m with Kinds = List.rev withKinds }
                    else { m with Kinds = withKinds }) }
        if reverseModules then { withModules with Modules = List.rev withModules.Modules }
        else withModules
    let direct   = (TopologicalOrderPass.run sampleCatalog).Value
    let permuted = (TopologicalOrderPass.run (perturb sampleCatalog)).Value
    direct = permuted

// ---------------------------------------------------------------------------
// Determinism (T1) — same input ⇒ byte-identical output across repeats,
// trail included.
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: TopologicalOrderPass is deterministic`` () =
    let r1 = TopologicalOrderPass.run sampleCatalog
    let r2 = TopologicalOrderPass.run sampleCatalog
    Assert.Equal(r1.Value, r2.Value)
    Assert.Equal<LineageEvent list>(r1.Trail, r2.Trail)

// ---------------------------------------------------------------------------
// Edges and missing-edges accounting.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Edges record every FK reference (source, target)`` () =
    let result = TopologicalOrderPass.run sampleCatalog
    let expected =
        [ for k in Catalog.allKinds sampleCatalog do
            for r in k.References do
                yield (k.SsKey, r.TargetKind) ]
        |> List.sort
    Assert.Equal<(SsKey * SsKey) list>(expected, result.Value.Edges)

[<Fact>]
let ``no missing edges when every FK target is present`` () =
    let result = TopologicalOrderPass.run sampleCatalog
    Assert.Empty(result.Value.MissingEdges)
    Assert.True(TopologicalOrder.isComplete result.Value)

[<Fact>]
let ``MissingEdges are recorded when an FK target is absent`` () =
    // Drop Customer from the catalog; Order's FK to Customer becomes a
    // missing edge.
    let withoutCustomer =
        { sampleCatalog with
            Modules =
                sampleCatalog.Modules
                |> List.map (fun m ->
                    { m with
                        Kinds =
                            m.Kinds
                            |> List.filter (fun k -> k.SsKey <> customerKey) }) }
    let result = TopologicalOrderPass.run withoutCustomer
    let missing = result.Value.MissingEdges
    Assert.Equal(1, missing.Length)
    Assert.Equal((orderKey, customerKey), missing.[0])
    Assert.False(TopologicalOrder.isComplete result.Value)

// ---------------------------------------------------------------------------
// Cycle behavior (commit-4 placeholder) — when a cycle is present, fall
// back to alphabetical with a generic diagnostic. SCC enumeration is
// the next commit's job.
// ---------------------------------------------------------------------------

let private addReference (sourceKey: SsKey) (targetKey: SsKey) (refKey: SsKey) (refName: string) (sourceAttrKey: SsKey) (c: Catalog) : Catalog =
    { Modules =
        c.Modules
        |> List.map (fun m ->
            { m with
                Kinds =
                    m.Kinds
                    |> List.map (fun k ->
                        if k.SsKey = sourceKey then
                            let newRef : Reference =
                                { SsKey           = refKey
                                  Name            = Name.create refName |> Result.value
                                  SourceAttribute = sourceAttrKey
                                  TargetKind      = targetKey
                                  OnDelete        = NoAction
                                  IsUserFk        = false }
                            { k with References = newRef :: k.References }
                        else k) }) }

[<Fact>]
let ``cycle: Mode is Alphabetical when input contains a cycle`` () =
    // Add a reverse reference Customer → Order so we have a 2-cycle.
    let backRefKey = refKey ["Customer"; "Order"; "back"]
    let cyclic =
        sampleCatalog
        |> addReference customerKey orderKey backRefKey "Order_back" customerNameKey
    let result = TopologicalOrderPass.run cyclic
    Assert.Equal(Alphabetical, result.Value.Mode)

[<Fact>]
let ``cycle: every kind still appears in Order under alphabetical fallback`` () =
    let backRefKey = refKey ["Customer"; "Order"; "back"]
    let cyclic =
        sampleCatalog
        |> addReference customerKey orderKey backRefKey "Order_back" customerNameKey
    let result = TopologicalOrderPass.run cyclic
    let allKindKeys =
        Catalog.allKinds cyclic
        |> List.map (fun k -> k.SsKey)
        |> Set.ofList
    Assert.Equal<Set<SsKey>>(allKindKeys, Set.ofList result.Value.Order)

[<Fact>]
let ``cycle: at least one CycleDiagnostic is emitted`` () =
    let backRefKey = refKey ["Customer"; "Order"; "back"]
    let cyclic =
        sampleCatalog
        |> addReference customerKey orderKey backRefKey "Order_back" customerNameKey
    let result = TopologicalOrderPass.run cyclic
    Assert.NotEmpty(result.Value.Cycles)

// ---------------------------------------------------------------------------
// Tarjan's SCC enumeration (commit 5).
//
// A 2-cycle (Customer ↔ Order) produces exactly one SCC of two members.
// A 3-cycle (A → B → C → A) produces exactly one SCC of three members.
// Disjoint cycles produce multiple SCCs, sorted by smallest member.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Tarjan: 2-cycle produces one SCC with both members`` () =
    let backRefKey = refKey ["Customer"; "Order"; "back"]
    let cyclic =
        sampleCatalog
        |> addReference customerKey orderKey backRefKey "Order_back" customerNameKey
    let result = TopologicalOrderPass.run cyclic
    Assert.Equal(1, result.Value.Cycles.Length)
    let scc = result.Value.Cycles |> List.head
    Assert.Equal<Set<SsKey>>(
        Set.ofList [ customerKey; orderKey ],
        Set.ofList scc.Members)

[<Fact>]
let ``Tarjan: SCC members are sorted by SsKey within the diagnostic`` () =
    let backRefKey = refKey ["Customer"; "Order"; "back"]
    let cyclic =
        sampleCatalog
        |> addReference customerKey orderKey backRefKey "Order_back" customerNameKey
    let result = TopologicalOrderPass.run cyclic
    let scc = result.Value.Cycles |> List.head
    Assert.Equal<SsKey list>(scc.Members, List.sort scc.Members)

[<Fact>]
let ``Tarjan: Country (no cycle) is not in any SCC`` () =
    // The 2-cycle is Customer↔Order; Country has no FK at all.
    let backRefKey = refKey ["Customer"; "Order"; "back"]
    let cyclic =
        sampleCatalog
        |> addReference customerKey orderKey backRefKey "Order_back" customerNameKey
    let result = TopologicalOrderPass.run cyclic
    let allSccMembers =
        result.Value.Cycles
        |> List.collect (fun c -> c.Members)
        |> Set.ofList
    Assert.DoesNotContain(countryKey, allSccMembers)

[<Fact>]
let ``Tarjan: deterministic — same cyclic input produces same SCC list`` () =
    let backRefKey = refKey ["Customer"; "Order"; "back"]
    let cyclic =
        sampleCatalog
        |> addReference customerKey orderKey backRefKey "Order_back" customerNameKey
    let r1 = TopologicalOrderPass.run cyclic
    let r2 = TopologicalOrderPass.run cyclic
    Assert.Equal<CycleDiagnostic list>(r1.Value.Cycles, r2.Value.Cycles)

[<Fact>]
let ``Tarjan: BreakableEdges is empty in v2 (resolver pending v3)`` () =
    let backRefKey = refKey ["Customer"; "Order"; "back"]
    let cyclic =
        sampleCatalog
        |> addReference customerKey orderKey backRefKey "Order_back" customerNameKey
    let result = TopologicalOrderPass.run cyclic
    let scc = result.Value.Cycles |> List.head
    Assert.Empty(scc.BreakableEdges)

[<Fact>]
let ``Tarjan: SCC reason explains why the cycle stayed unresolved`` () =
    // The synthetic fixture uses non-nullable FK columns on both sides
    // of the back-reference, so the resolver classifies both edges as
    // Other and refuses to break either one. The Reason field records
    // the class of failure.
    let backRefKey = refKey ["Customer"; "Order"; "back"]
    let cyclic =
        sampleCatalog
        |> addReference customerKey orderKey backRefKey "Order_back" customerNameKey
    let result = TopologicalOrderPass.run cyclic
    let scc = result.Value.Cycles |> List.head
    Assert.Contains("Weak edge", scc.Reason)

// ---------------------------------------------------------------------------
// Self-loop detection (v4) — surfaced by chapter 4.1.B slice δ.
//
// Pre-v4: `tarjanScc`'s post-filter dropped 1-node SCCs unconditionally
// (per the comment "Self-loops would require explicit detection — adds
// when a real fixture surfaces them"). The data-emission path (slice δ
// `StaticSeedsEmitter`) needs cycle membership for self-referencing
// kinds (`employee.manager_id → employee` and similar) to populate
// `DeferredFkSet` and emit the two-phase MERGE/UPDATE pattern. v4
// retains 1-node SCCs whose sole member has a self-edge.
// ---------------------------------------------------------------------------

[<Fact>]
let ``v4 self-loop: kind referencing itself produces a 1-member SCC`` () =
    let selfRefKey = refKey ["Customer"; "self"]
    let selfCyclic =
        sampleCatalog
        |> addReference customerKey customerKey selfRefKey "self" customerNameKey
    let result = TopologicalOrderPass.run selfCyclic
    Assert.NotEmpty(result.Value.Cycles)
    let scc =
        result.Value.Cycles
        |> List.find (fun c -> c.Members = [ customerKey ])
    Assert.Equal<SsKey list>([ customerKey ], scc.Members)

[<Fact>]
let ``v4 self-loop: non-self-loop 1-node SCCs are still NOT cycles (filter retained)`` () =
    // Country is an isolated kind (no FKs); after Tarjan it would form a
    // singleton {Country} component but with no self-edge — the v4
    // filter keeps the pre-v4 behavior of dropping it.
    let result = TopologicalOrderPass.run sampleCatalog
    let allSccMembers =
        result.Value.Cycles
        |> List.collect (fun c -> c.Members)
        |> Set.ofList
    Assert.DoesNotContain(countryKey, allSccMembers)
    Assert.DoesNotContain(customerKey, allSccMembers)
    Assert.DoesNotContain(orderKey, allSccMembers)

[<Fact>]
let ``v4 self-loop: SkipSelfEdges policy still drops the self-edge before SCC`` () =
    // The v4 filter preserves SelfLoopPolicy.SkipSelfEdges semantics:
    // self-edges are dropped during graph construction, so Tarjan sees
    // no edge at all, the kind has indegree 0, Kahn processes it, and
    // no SCC is produced.
    let selfRefKey = refKey ["Customer"; "self"]
    let selfCyclic =
        sampleCatalog
        |> addReference customerKey customerKey selfRefKey "self" customerNameKey
    let result = TopologicalOrderPass.runWith SkipSelfEdges selfCyclic
    Assert.Equal(Topological, result.Value.Mode)
    Assert.Empty(result.Value.Cycles)

// ---------------------------------------------------------------------------
// Property: post-symmetric-closure SCC enumeration is permutation-invariant
// (the V2 contract under cyclic input as well as acyclic).
// ---------------------------------------------------------------------------

[<Fact>]
let ``contract: SCC enumeration is invariant under input permutation`` () =
    let withInverses =
        sampleCatalog
        |> SymmetricClosure.run
        |> fun lineage -> lineage.Value
    let permuted =
        { Modules =
            withInverses.Modules
            |> List.map (fun m -> { m with Kinds = List.rev m.Kinds })
            |> List.rev }
    let r1 = (TopologicalOrderPass.run withInverses).Value
    let r2 = (TopologicalOrderPass.run permuted).Value
    Assert.Equal<CycleDiagnostic list>(r1.Cycles, r2.Cycles)

// ---------------------------------------------------------------------------
// Three-disjoint cycles fixture exercises multi-SCC enumeration. (Built
// from scratch — synthetic fixture has only one cycle once symmetric
// closure runs.)
// ---------------------------------------------------------------------------

let private mkKey s = testKey s
let private mkName s = Name.create s |> Result.value

let private kindWithFk (kindKey: string) (fkKey: string) (targetKey: SsKey) : Kind =
    let attrId = mkKey (kindKey + "_Id")
    let attrFk = mkKey (kindKey + "_Fk")
    { SsKey = mkKey kindKey
      Name = mkName kindKey
      Origin = OsNative
      Modality = []
      Physical = { Schema = "dbo"; Table = kindKey }
      Attributes = [
          { SsKey = attrId; Name = mkName "Id"; Type = Integer
            Column = { ColumnName = "ID"; IsNullable = false }
            IsPrimaryKey = true; IsMandatory = false; Length = None; Precision = None; Scale = None; IsIdentity = false }
          { SsKey = attrFk; Name = mkName "Fk"; Type = Integer
            Column = { ColumnName = "FK"; IsNullable = false }
            IsPrimaryKey = false; IsMandatory = false; Length = None; Precision = None; Scale = None; IsIdentity = false } ]
      References = [
          { SsKey = mkKey fkKey
            Name = mkName "ToOther"
            SourceAttribute = attrFk
            TargetKind = targetKey
            OnDelete = NoAction
            IsUserFk = false } ]
      Indexes = [] }

[<Fact>]
let ``Tarjan: two disjoint 2-cycles produce two SCCs`` () =
    // Build A↔B and C↔D as two separate 2-cycles.
    let a = kindWithFk "A" "RefA" (mkKey "B")
    let b = kindWithFk "B" "RefB" (mkKey "A")
    let c = kindWithFk "C" "RefC" (mkKey "D")
    let d = kindWithFk "D" "RefD" (mkKey "C")
    let twoCycles : Catalog =
        { Modules = [
            { SsKey = mkKey "M"; Name = mkName "M"; Kinds = [ a; b; c; d ] } ] }
    let result = TopologicalOrderPass.run twoCycles
    Assert.Equal(2, result.Value.Cycles.Length)
    // SCCs are sorted by smallest member; A-B comes before C-D.
    let firstScc  = result.Value.Cycles.[0]
    let secondScc = result.Value.Cycles.[1]
    Assert.Equal<Set<SsKey>>(Set.ofList [ mkKey "A"; mkKey "B" ], Set.ofList firstScc.Members)
    Assert.Equal<Set<SsKey>>(Set.ofList [ mkKey "C"; mkKey "D" ], Set.ofList secondScc.Members)

// ---------------------------------------------------------------------------
// Edge classification (commit 6).
//
// Build a tiny catalog where the source attribute's IsNullable and the
// reference's OnDelete combine to produce each EdgeStrength variant.
// ---------------------------------------------------------------------------

let private kindWithRef
    (kindKey: string)
    (refKey: string)
    (targetKey: SsKey)
    (sourceAttrNullable: bool)
    (onDelete: ReferenceAction)
    : Kind =
    let attrId = mkKey (kindKey + "_Id")
    let attrFk = mkKey (kindKey + "_Fk")
    { SsKey = mkKey kindKey
      Name = mkName kindKey
      Origin = OsNative
      Modality = []
      Physical = { Schema = "dbo"; Table = kindKey }
      Attributes = [
          { SsKey = attrId; Name = mkName "Id"; Type = Integer
            Column = { ColumnName = "ID"; IsNullable = false }
            IsPrimaryKey = true; IsMandatory = false; Length = None; Precision = None; Scale = None; IsIdentity = false }
          { SsKey = attrFk; Name = mkName "Fk"; Type = Integer
            Column = { ColumnName = "FK"; IsNullable = sourceAttrNullable }
            IsPrimaryKey = false; IsMandatory = false; Length = None; Precision = None; Scale = None; IsIdentity = false } ]
      References = [
          { SsKey = mkKey refKey
            Name = mkName "ToOther"
            SourceAttribute = attrFk
            TargetKind = targetKey
            OnDelete = onDelete
            IsUserFk = false } ]
      Indexes = [] }

let private noRefKind (kindKey: string) : Kind =
    let attrId = mkKey (kindKey + "_Id")
    { SsKey = mkKey kindKey
      Name = mkName kindKey
      Origin = OsNative
      Modality = []
      Physical = { Schema = "dbo"; Table = kindKey }
      Attributes = [
          { SsKey = attrId; Name = mkName "Id"; Type = Integer
            Column = { ColumnName = "ID"; IsNullable = false }
            IsPrimaryKey = true; IsMandatory = false; Length = None; Precision = None; Scale = None; IsIdentity = false } ]
      References = []; Indexes = [] }

// ---------------------------------------------------------------------------
// V1 contract: SortByForeignKeys_AutoDetectsAsymmetricAuditCycle.
//
// V1 fixture: Parent and Audit, where:
//   Parent → Audit (non-nullable, NoAction)  [Other / Strong]
//   Audit  → Parent (nullable, NoAction)     [Weak]
//
// V1 expectation: the resolver auto-detects the asymmetry, removes the
// Weak edge (Audit → Parent), and topologically sorts the result. After
// resolution Parent precedes Audit.
//
// V2 contract: same observable behavior; lifted from the V1 admire
// scout report's test inventory.
// ---------------------------------------------------------------------------

[<Fact>]
let ``V1 contract: asymmetric-2-cycle auto-resolves via Weak edge`` () =
    // V1 fixture (translated to V2 owner->target reference convention):
    //   "Parent → Audit (non-nullable)" — V1 calls Parent the parent here,
    //     meaning Audit has a non-nullable FK to Parent. In V2: Audit has
    //     the reference; source attribute non-nullable; strength = Other.
    //   "Audit → Parent (nullable)" — V1 calls Audit the parent here,
    //     meaning Parent has a nullable FK to Audit. In V2: Parent has
    //     the reference; source attribute nullable; strength = Weak.
    //
    // Resolver breaks the Weak edge (Parent's FK to Audit). What remains:
    // Audit's FK to Parent — so Parent precedes Audit in the order.
    let parent = kindWithRef "Parent" "ParentFkToAudit" (mkKey "Audit") true NoAction   // Weak
    let audit  = kindWithRef "Audit"  "AuditFkToParent" (mkKey "Parent") false NoAction // Other
    let cyclic : Catalog =
        { Modules = [
            { SsKey = mkKey "M"; Name = mkName "M"; Kinds = [ parent; audit ] } ] }
    let result = TopologicalOrderPass.run cyclic
    // Resolver succeeds — Mode is Topological, not Alphabetical.
    Assert.Equal(Topological, result.Value.Mode)
    // Parent precedes Audit (matching V1's expected order).
    Assert.True(TopologicalOrder.precedes (mkKey "Parent") (mkKey "Audit") result.Value)
    // The CycleDiagnostic records the SCC and the broken edge.
    Assert.Equal(1, result.Value.Cycles.Length)
    let diag = result.Value.Cycles |> List.head
    Assert.Equal<Set<SsKey>>(
        Set.ofList [ mkKey "Parent"; mkKey "Audit" ],
        Set.ofList diag.Members)
    Assert.Equal(1, diag.BreakableEdges.Length)
    // The broken edge is the Weak one — Parent's FK to Audit, in V2's
    // (source, target) orientation.
    Assert.Equal((mkKey "Parent", mkKey "Audit"), diag.BreakableEdges.[0])
    Assert.Contains("auto-resolved", diag.Reason)

[<Fact>]
let ``resolver: 2-cycle with no Weak edges remains unresolved`` () =
    // Both edges non-nullable + NoAction = Other / Other. No weak edge.
    let parent = kindWithRef "Parent" "ParentRef" (mkKey "Audit") false NoAction
    let audit  = kindWithRef "Audit"  "AuditRef"  (mkKey "Parent") false NoAction
    let cyclic : Catalog =
        { Modules = [
            { SsKey = mkKey "M"; Name = mkName "M"; Kinds = [ parent; audit ] } ] }
    let result = TopologicalOrderPass.run cyclic
    Assert.Equal(Alphabetical, result.Value.Mode)
    let diag = result.Value.Cycles |> List.head
    Assert.Empty(diag.BreakableEdges)
    Assert.Contains("no Weak edge", diag.Reason)

[<Fact>]
let ``resolver: 2-cycle with two Weak edges remains unresolved`` () =
    // Both edges nullable + NoAction = Weak / Weak. Resolver refuses
    // to choose; cycle is unresolved.
    let parent = kindWithRef "Parent" "ParentRef" (mkKey "Audit") true NoAction
    let audit  = kindWithRef "Audit"  "AuditRef"  (mkKey "Parent") true NoAction
    let cyclic : Catalog =
        { Modules = [
            { SsKey = mkKey "M"; Name = mkName "M"; Kinds = [ parent; audit ] } ] }
    let result = TopologicalOrderPass.run cyclic
    Assert.Equal(Alphabetical, result.Value.Mode)
    let diag = result.Value.Cycles |> List.head
    Assert.Empty(diag.BreakableEdges)
    Assert.Contains("multiple Weak edges", diag.Reason)

[<Fact>]
let ``resolver: 3-cycle remains unresolved (current resolver handles 2-cycles only)`` () =
    let a = kindWithRef "A" "AtoB" (mkKey "B") true NoAction
    let b = kindWithRef "B" "BtoC" (mkKey "C") true NoAction
    let c = kindWithRef "C" "CtoA" (mkKey "A") true NoAction
    let cyclic : Catalog =
        { Modules = [
            { SsKey = mkKey "M"; Name = mkName "M"; Kinds = [ a; b; c ] } ] }
    let result = TopologicalOrderPass.run cyclic
    Assert.Equal(Alphabetical, result.Value.Mode)
    let diag = result.Value.Cycles |> List.head
    Assert.Equal(3, diag.Members.Length)
    Assert.Contains("size 3", diag.Reason)

[<Fact>]
let ``resolver: Cascade edges are never broken`` () =
    // Cascade is structural — even if the Cascade source attribute is
    // nullable, the strength is Cascade, not Weak. With the asymmetric
    // partner non-nullable, no edge is Weak; the cycle is unresolved.
    let parent = kindWithRef "Parent" "ParentRef" (mkKey "Audit") true ReferenceAction.Cascade
    let audit  = kindWithRef "Audit"  "AuditRef"  (mkKey "Parent") false NoAction
    let cyclic : Catalog =
        { Modules = [
            { SsKey = mkKey "M"; Name = mkName "M"; Kinds = [ parent; audit ] } ] }
    let result = TopologicalOrderPass.run cyclic
    Assert.Equal(Alphabetical, result.Value.Mode)
    let diag = result.Value.Cycles |> List.head
    Assert.Empty(diag.BreakableEdges)

[<Fact>]
let ``resolver: resolved cycles still appear in Cycles for audit`` () =
    // Even when the resolver succeeds, CycleDiagnostic stays in Cycles
    // so consumers can audit which cycles existed and which edges were
    // broken to fix them.
    let parent = kindWithRef "Parent" "ParentRef" (mkKey "Audit") false NoAction
    let audit  = kindWithRef "Audit"  "AuditRef"  (mkKey "Parent") true NoAction
    let cyclic : Catalog =
        { Modules = [
            { SsKey = mkKey "M"; Name = mkName "M"; Kinds = [ parent; audit ] } ] }
    let result = TopologicalOrderPass.run cyclic
    Assert.Equal(Topological, result.Value.Mode)
    Assert.NotEmpty(result.Value.Cycles)
    let diag = result.Value.Cycles |> List.head
    Assert.Equal(1, diag.BreakableEdges.Length)

// ---------------------------------------------------------------------------
// Lineage discipline — A23 + A25.
// ---------------------------------------------------------------------------

[<Fact>]
let ``A25: emits one Touched event per kind scanned`` () =
    let result = TopologicalOrderPass.run sampleCatalog
    let touchedEvents = result.Trail |> List.filter (fun e -> e.TransformKind = Touched)
    let kindCount = (Catalog.allKinds sampleCatalog).Length
    Assert.Equal(kindCount, touchedEvents.Length)

[<Fact>]
let ``A23: events carry pass version and name`` () =
    let result = TopologicalOrderPass.run sampleCatalog
    Assert.All(result.Trail, fun e ->
        Assert.Equal(TopologicalOrderPass.version, e.PassVersion)
        Assert.Equal("topologicalOrder", e.PassName))

// ---------------------------------------------------------------------------
// Empty catalog edge case.
// ---------------------------------------------------------------------------

[<Fact>]
let ``empty catalog produces empty TopologicalOrder`` () =
    let empty : Catalog = { Modules = [] }
    let result = TopologicalOrderPass.run empty
    Assert.Equal(Topological, result.Value.Mode)
    Assert.Empty(result.Value.Order)
    Assert.Empty(result.Value.Edges)
    Assert.Empty(result.Value.MissingEdges)
    Assert.Empty(result.Value.Cycles)

// ---------------------------------------------------------------------------
// Composition with symmetric closure — after symmetric closure adds an
// inverse, the topological order may itself become cyclic (because
// inverses introduce circular FKs). This is the correct V2 behavior:
// symmetric closure is for surface navigation, not for FK-safe data
// emission. Schema emitters apply alphabetical ordering per A33;
// data emitters consume the catalog WITHOUT symmetric closure.
// ---------------------------------------------------------------------------

[<Fact>]
let ``post-symmetric-closure catalog is cyclic; emitters compose correctly`` () =
    let withInverses =
        sampleCatalog
        |> SymmetricClosure.run
        |> fun lineage -> lineage.Value
    let result = TopologicalOrderPass.run withInverses
    // Symmetric closure introduced an inverse on Customer → Order
    // alongside the original Order → Customer; the FK graph is now cyclic.
    Assert.Equal(Alphabetical, result.Value.Mode)

// ---------------------------------------------------------------------------
// V1 divergences — explicit skip stubs naming intentional V2 differences
// (CHAPTER_1_CLOSE.md §2.7; session 13 skip-stub completion).
// ---------------------------------------------------------------------------

// Three Skip-annotated test stubs (sanitized-effective-names,
// manual-cycle override, junction-deferred heuristic) retired
// per the user's chapter-3.5 directive (2026-05-09: "we don't
// need them"). The contracts they reserved live in ADMIRE.md
// + DECISIONS narrative; when V2 grows OrderingPolicy or the
// junction-table heuristic, new tests land structurally with
// the implementation rather than as long-lived Skip stubs.
