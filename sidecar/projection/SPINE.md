# V2 — The Spine at Multiple Levels

> **Indexed by `THE_USE_CASE_ONTOLOGY.md` (the masterwork, 2026-06-03).** The masterwork is the single
> index of the target; it carries the structural shape forward through its matrix and laws. This file
> remains canonical **provenance** for the categorical rendering — the seven primitives, the seven
> tessellating patterns, and the six structural inferences (sheaf / adjunction / Hom-set / quotient /
> continuation / tessellation). Read the masterwork first; read this for the categorical depth.

**Date:** 2026-05-08; **chapter-3.1-close update:** 2026-05-30
**Purpose:** Render the deeper structural shape of the V2 system — the primitives that recur, the patterns that tessellate, the inferences that only become visible once all the chapter pre-scopes, the backlog, and the algebraic foundation are in view simultaneously. The PLAYBOOK names *what* and *how*; this document names the *why-it-fits-together*.

> **Chapter-3.1-close note (2026-05-30).** Pattern Π's output realized as `seq<Statement>` for SSDT (sessions 34, 36 — A35 cash-out). Π's canonical form is no longer presumed `string`; it is a typed deterministic stream. Realizations (`Render.toText`, `Deploy.executeStream`) are sibling consumers of the same stream. Pattern Pass produced its first cross-emitter consumer instance: `TopologicalOrderPass.runWith` consumed by `RawTextEmitter.emissionOrder` (A32 partial advance). Inference I6 (contract precedes instances) was vindicated by the chapter-3.1 audit's "declared ports unrealized" finding (`AUDIT_2026_05_DDD_HEXAGONAL_FP.md`); chapter 3.5's Π port realization closes the structural gap.

This is the document to consult when a chapter's slice plan feels arbitrary. It is to surface the shape such that the next slice writes itself.

---

## Contents

- [The shape of the system](#the-shape-of-the-system)
- [Seven primitives that recur](#seven-primitives-that-recur)
- [Seven patterns that tessellate](#seven-patterns-that-tessellate)
- [Six structural inferences](#six-structural-inferences)
- [F# expression — where point-free pays](#f-expression--where-point-free-pays)
- [Computation expressions — where they earn their place](#computation-expressions--where-they-earn-their-place)
- [Active patterns — when nested matches recur](#active-patterns--when-nested-matches-recur)
- [The chapter as a tessellation instance](#the-chapter-as-a-tessellation-instance)
- [Concrete leverage — what the deeper view enables](#concrete-leverage--what-the-deeper-view-enables)
- [Closing — the spine is the system](#closing--the-spine-is-the-system)

---

## The shape of the system

Stand back from the eight chapter pre-scopes, the 375 backlog items, the four canonical surfaces. Look at what's left when you erase the names.

What remains is **a category**:

- **Objects:** typed values — `Catalog`, `Profile`, `Policy`, `Lifecycle`, `ArtifactByKind<'element>`, `CatalogDiff`, `Lineage<'a>`, `Diagnostics<'a>`, `Result<'a, 'e>`, `Map<SsKey, _>`, `Set<SsKey>`.
- **Morphisms:** pure functions between objects — `Emitter<'element>`, `Pass`, `Adapter`, `Render`, `Compare`, `Diff`, `Predicate`.
- **Composition:** function composition (`>>`) — and morphisms compose iff their types align.
- **Identity:** `id`.

Plus three structural augmentations:

- **Functorial structure** in writer monads (`Lineage`, `Diagnostics`, `LineageDiagnostics`) — they `map` over their payload.
- **Monoidal structure** in coproducts (T2 — `Catalog ⊕ Catalog`; `Lineage` event lists; `Diagnostics` entry lists; `Set<SsKey>` union).
- **Limits/colimits** at the keyset level (`Set.intersect`, `Set.union`, `Set.difference` on SsKey collections).

The system isn't *like* a category. The system *is* a category. F# closed DUs + smart constructors + Result/Lineage/Diagnostics writers are how F# encodes categories. The chapter pre-scopes are *implementations of category-theoretic operations*, with names borrowed from the cutover domain.

Why this matters: once you internalize that V2 is a category, you stop *deciding* shape per chapter and start *recognizing* shape. The next emitter is a morphism `Catalog -> ArtifactByKind<X>`. The next pass is a morphism `Catalog × Policy × Profile -> Lineage<Y>`. The next adapter is a morphism `External -> Task<Result<Catalog, _>>`. The shape is fixed; the only question is the type variable.

---

## Seven primitives that recur

Each primitive is a structural commitment — a value-type or pattern that appears across chapters. Internalize these and most code writes itself.

### P1. SsKey-keyed Map

**Shape:** `Map<SsKey, _>`.

**Where it appears:**
- `ArtifactByKind<'element>` — every emitter output.
- `Catalog.allKinds` keyed view of kinds.
- `CatalogDiff.Renamed : Map<SsKey, RenameRecord>`.
- `UserRemapContext.Mapping : Map<SourceUserId, TargetUserId>` (different key type but identical role — a partial function indexed by identity).
- `Diagnostics` entries when grouped by SsKey for routing.

**Why it tessellates:** SsKey is the spine of identity (A4); every per-kind value is keyed by it; every per-kind operation (drift detection, remediation, comparator) reduces to `Map<SsKey, _>` operations.

**F# expression:** F# `Map<,>` is a balanced tree; structural equality is honest; `Map.tryFind`, `Map.fold`, `Map.map` cover most use. Use `IReadOnlyDictionary<,>` only if profiling shows hot-path allocation pressure.

### P2. Writer-monad accumulation

**Shape:** `Lineage<'a> = { Value : 'a; Trail : LineageEvent list }`, `Diagnostics<'a> = { Value : 'a; Entries : DiagnosticEntry list }`, `LineageDiagnostics<'a> = Lineage<Diagnostics<'a>>`.

**Where it appears:**
- Every pass produces `Lineage<'output>` (decisions only) or `Lineage<Diagnostics<'output>>` (decisions + observer-relevant findings).
- Lineage trails are earliest-first under bind (A24).
- Diagnostics entries accumulate via `Diagnostics.tellMany`.

**Why it tessellates:** decisions and diagnostics are *first-class*, not side effects. Auditability is structural. Composition is monadic bind.

**F# expression:** the writer-monad bind operator `>>=` is `let inline (>>=) m f = Lineage.bind f m`. Computation expressions (`lineage { let! x = ... }`) earn their place when chains exceed three binds.

### P3. Ordered linearization (TopologicalOrder)

**Shape:** `TopologicalOrder = { Mode; Order : SsKey list; Edges; MissingEdges; Cycles; Diagnostics }`.

**Where it appears:**
- Schema emission walks the order (A33).
- Data emission interleaves rows from three emitters under one order.
- `Render.concatSql topoOrder artifact` composes per-kind slices into a single string.
- Property tests assert `(parent, child) FK ⇒ position parent < position child`.

**Why it tessellates:** the kinds aren't a set; they're a partial order under FK dependency. Every operation that walks kinds in *some* order — emitting, deploying, validating, diffing — needs the linearization.

**F# expression:** `TopologicalOrderPass.run : Catalog -> Lineage<TopologicalOrder>` is once-per-Catalog; downstream consumers thread the value. `Render.concatSql : SsKey list -> ArtifactByKind<string> -> string` accepts the linearization as a parameter.

### P4. Smart-constructor invariants

**Shape:** `let create (raw: 'input) : Result<'value, ValidationError>`.

**Where it appears:**
- `SsKey.original`, `SsKey.synthesized`, `SsKey.fromV1`, `Name.create`, `Email.create`.
- `CategoricalDistribution.create`, `NumericDistribution.create`.
- `ArtifactByKind.create` (per-kind keyset enforcement).
- `CatalogDiff.between` (exhaustiveness invariant).

**Why it tessellates:** every value-typed invariant rides on every value, *forever*. Downstream consumers don't re-validate; they pattern-match on the value with confidence the invariant holds.

**F# expression:** the smart constructor returns `Result<'value, ValidationError>` (or a typed error DU at boundaries). Consumers get the unwrapped value via `Result.value` (in tests/fixtures) or `Result.bind` (in production code chains).

### P5. Origin tagging (provenance carriage)

**Shape:** every value carries a discriminated origin.

**Where it appears:**
- `SsKey` four-variant DU: `OssysOriginal | Synthesized | DerivedFrom | V1Mapped`.
- `Origin` DU on `Kind` (V2-native vs admire-source).
- `ProbeOutcome` carrying execution provenance (Succeeded / Timeout / Cancelled / TrustedConstraint).
- `LineageEvent.Source` (PassName + version).
- `DiagnosticEntry.Source` (`"<passName>"`, `"adapter:<adapter-name>"`, `"emitter:<emitter-name>"`).
- `Tolerance` flags' citations to V1 file:line.

**Why it tessellates:** when a value can come from multiple origins, the variant tag carries the difference forward without runtime cost. Property tests stratify naturally; consumers route on origin.

**F# expression:** closed DUs with `[<RequireQualifiedAccess>]` when case names recur. Pattern-match exhaustively. Active patterns when origin classification is itself a recurring computation.

### P6. Erasure declaration

**Shape:** `equalModulo : ErasureSet -> 'a -> 'a -> bool`.

**Where it appears:**
- `Catalog.equalModuloDacpacErasure : Catalog -> Catalog -> bool` — DACPAC round-trip cannot preserve `Origin`, `Modality`, `Static.populations`, `Lineage`.
- `CatalogEquivalence.equalModulo : Tolerance -> Catalog -> Catalog -> Diff` — comparator with named tolerance flags.
- Skip-stubbed tests citing deliberate divergence (`[<Fact(Skip = "...")>]`).
- ADMIRE entries documenting won't-carry-forward components.

**Why it tessellates:** every comparison axis V2 *deliberately* doesn't preserve must be named. Erasure is honest; silent erasure is a bug. The function definition IS the contract.

**F# expression:** `equalModulo` takes the erasure set as an explicit parameter, not as global state. Default tolerance profiles are `let defaultTolerance = { ... }` values composed by callers.

### P7. Closed DUs with structured rationale

**Shape:** `type Outcome = | EnforceX of evidence: Evidence | DoNotEnforce of reason: Reason | RequireOperatorApproval of conflict: Conflict`.

**Where it appears:**
- `NullabilityOutcome`, `UniqueIndexOutcome`, `ForeignKeyOutcome`.
- `RemapDiagnostic` (NoEmail / EmailDidNotMatch / SsKeyDidNotMatch / OverrideMissing / NoFallbackConfigured).
- `RefactorOperationKind` (RenameRefactor; future MoveSchemaRefactor / etc.).
- `EmitError` (KindNotProduced / UnexpectedKind / RenderFailed).

**Why it tessellates:** decisions, errors, and diagnostics are all *categorically* shaped: a small set of mutually-exclusive cases, each carrying its own evidence. The DU IS the decision; pattern-matching IS the consumer.

**F# expression:** `[<RequireQualifiedAccess>]` when case names recur. Use `match` with explicit cases (avoid wildcards `_ ->` in production code; force exhaustiveness). Active patterns when downstream consumers need a coarser view.

---

## Seven patterns that tessellate

Each pattern is a *type signature shape* that recurs across chapters. The eight chapter pre-scopes are largely concrete instances of these seven patterns.

### Π. Emitter

```fsharp
type Emitter<'element> = Catalog -> Result<ArtifactByKind<'element>, EmitError>
//  with Profile-consuming variant: Catalog -> Profile -> Result<ArtifactByKind<'element>, EmitError>
//  with diff-consuming variant:    CatalogDiff -> Result<ArtifactByKind<'element>, EmitError>
```

**Instances:**
- `RawTextEmitter : Emitter<string>` (shipped)
- `JsonEmitter : Emitter<JsonElement>` (shipped)
- `DistributionsEmitter : Catalog -> Profile -> Result<ArtifactByKind<DistributionSlice>, EmitError>` (shipped, post-refactor)
- `DacpacEmitter : Emitter<TSqlObjectScript>` (chapter 3.3)
- `SsdtDdlEmitter : Emitter<SsdtFile>` (chapter 4.1.A)
- `RefactorLogEmitter : CatalogDiff -> Result<ArtifactByKind<RefactorLogEntry list>, EmitError>` (chapter 3.5)
- `StaticSeedsEmitter : Catalog -> Profile -> Result<ArtifactByKind<DataInsertScript>, EmitError>` (chapter 4.1.B)
- `MigrationDependenciesEmitter`, `BootstrapEmitter` (chapter 4.1.B)
- `DecisionLogEmitter`, `OpportunitiesEmitter`, `ValidationsEmitter` (chapter 4.3)
- `RemediationEmitter : Catalog -> Catalog -> Result<RemediationDacpac, RemediationError>` (chapter 4.4) — composes over CatalogDiff

**Universal property:** T11 holds by smart-constructor enforcement. Any two emitters of the same source produce equal SsKey-keysets *by construction*.

### Adapter

```fsharp
type Adapter<'source, 'internal, 'error> = 'source -> Task<Result<'internal, 'error>>
```

**Instances:**
- `Projection.Adapters.Osm.CatalogReader.parse : SnapshotSource -> Task<Result<Catalog, ParseError>>` (shipped)
- `Projection.Adapters.Sql.ReadSide.CatalogReader.readCatalog : connStr -> schemas -> Task<Result<Catalog, ReadSideError>>` (chapter 3.1)
- `Projection.Adapters.Sql.ReadSide.CdcDiscovery.discoverCdc : connStr -> Task<Result<CdcAwareness, ReadSideError>>` (chapter 4.1.B)
- `Projection.Adapters.Migration.DependencyReader.read : pickupDir -> Task<Result<MigrationDependencyContext, ReadError>>` (chapter 4.1.B)
- `Projection.Adapters.UserMap.UserMapLoader.load : path -> Task<Result<Map<SourceUserId, TargetUserId>, ParseError>>` (chapter 4.2)

**Universal property:** Adapters are *co-emitters* — they go the other direction in the same category. Together with emitters, they form a duality: `read-side ∘ deploy ∘ emit` is the canary's claimed identity (modulo Π-erasure).

### Pass

```fsharp
type Pass = Catalog -> Policy -> Profile -> Lineage<'output>
//  or:    Catalog -> Policy -> Profile -> Lineage<Diagnostics<'output>>
//  with the convention: 'output is DecisionSet for analysis passes, Catalog for E-passes
```

**Instances:**
- `CanonicalizeIdentityPass.run : Catalog -> Lineage<Catalog>` (shipped — E-pass; produces canonical Catalog)
- `NullabilityPass.run : Catalog -> Policy -> Profile -> Lineage<Diagnostics<NullabilityDecisionSet>>` (shipped)
- `UniqueIndexPass.run`, `ForeignKeyPass.run` (shipped — analysis passes)
- `TopologicalOrderPass.run : Catalog -> Lineage<TopologicalOrder>` (shipped)
- `SymmetricClosure.run : Catalog -> Lineage<Catalog>` (shipped)
- `EmissionPolicyPass.run : Policy -> Catalog -> Lineage<Catalog>` (chapter 4.1.A)
- `UserFkReflowPass.discover : UserPopulation -> UserPopulation -> UserMatchingStrategy -> Lineage<Diagnostics<UserRemapContext>>` (chapter 4.2)

**Universal property:** the projection `Project = Π ∘ E` is the composition `emit ∘ run pass1 ∘ run pass2 ∘ ...` with all `E` passes producing enriched Catalogs. Π consumes the final enriched Catalog plus Profile, never Policy (A18 amended).

### Render

```fsharp
type Render<'element, 'output> = SsKey list -> ArtifactByKind<'element> -> 'output
```

**Instances:**
- `Render.concatSql : SsKey list -> ArtifactByKind<string> -> string` (debug oracle; raw concatenation)
- `Render.toJsonDocument : ArtifactByKind<JsonElement> -> JsonDocument` (no order needed; JSON is recursive)
- `Render.toDacpac : SsKey list -> ArtifactByKind<TSqlObjectScript> -> DacPackage` (chapter 3.3)
- `Render.toSsdtDirectory : ArtifactByKind<SsdtFile> -> Manifest -> Map<RelativePath, string>` (chapter 4.1.A; key-keyed output)
- `Render.toRefactorLogXml : ArtifactByKind<RefactorLogEntry list> -> string` (chapter 3.5)

**Universal property:** Render is the *concrete syntax* layer. Per-kind slices (`ArtifactByKind`) are the abstract syntax; the renderer composes them into the consumer-shaped output. The split lets the same emitter feed multiple renderers (per-target-shape).

### Compare

```fsharp
type Compare<'tolerance> = 'tolerance -> Catalog -> Catalog -> Diff
```

**Instances:**
- `CatalogEquivalence.equalModulo : Tolerance -> Catalog -> Catalog -> Diff` (chapter 3.1)
- `CatalogEquivalence.equalModuloDacpacErasure : Catalog -> Catalog -> bool` (specialized; tolerance is fixed)

**Universal property:** equivalence-up-to-tolerance. The `Tolerance` carries the *deliberately-erased axes*; the function name carries the contract.

### Property

```fsharp
type Property = Catalog -> bool
//  or:        Catalog -> Catalog -> bool  (relational)
//  or with parameters: Catalog -> ParamA -> ParamB -> bool
```

**Instances (predicate library, chapter 3.4):**
- `roundTripBySsKey : Catalog -> bool`
- `idempotentRedeploy : Catalog -> bool`
- `siblingChorusAgrees : Catalog -> bool`
- `renameSurvives : Catalog -> SsKey -> Name -> bool`
- `t1ByteEqual : Catalog -> bool`
- `t1ModelEqual : Catalog -> bool`
- `coproductPreservation : Catalog -> bool`
- `policyOrthogonal : Policy -> Policy -> Catalog -> bool`
- `siblingDeployRoundTrip : Catalog -> bool`
- `wellFormedDeploy : Catalog -> bool`
- `populationRoundTrip : Catalog -> bool`

**Universal property:** every axiom that has a structural form (T1, T2, T11, A1, A18, A33, A34) gets a property test that exercises it on `forall c : Catalog`. The property *is* the axiom's enforcement.

### Diff

```fsharp
type Diff = Catalog -> Catalog -> Result<CatalogDiff, EmitError>
```

**Instances:**
- `CatalogDiff.between : Catalog -> Catalog -> Result<CatalogDiff, EmitError>` (chapter 3.5)
- `ArtifactByKind.compareWith : ('a -> 'a -> bool) -> ArtifactByKind<'a> -> ArtifactByKind<'a> -> Map<SsKey, DriftKind>` (drift detection — chapter 3.1 byproduct)

**Universal property:** evolution is a value, not a verb. `CatalogDiff` carries the *change* between two states; consumers (RefactorLogEmitter, RemediationEmitter, drift detection) operate on the diff value, not on the implicit transition.

---

## Six structural inferences

These are the inferences only fully visible once the full set of pre-scopes, the backlog, and the algebra are in view. Each pays back as a concrete capability.

### I1. The system is a sheaf over (time × environment)

The four-environment cutover (dev, qa, UAT, prod) × the temporal index (Lifecycle) defines a 2-dimensional space. At each `(env, time)` point, the algebra produces a triple `(Catalog, Policy, Profile)`. These are *local sections* of the sheaf.

The sheaf's gluing condition is **R4 from VISION_REVIEW** — the multi-environment property: for any two environments E1 and E2, `Project(catalog, policy_E1, profile_E1).Catalog ≡ Project(catalog, policy_E2, profile_E2).Catalog` modulo policy/profile-shaped variance.

If the gluing condition holds, the four local sections compose into a single global object: the cutover-consistent schema.

**Concrete leverage:** the four-environment cutover doesn't need separate code paths. It's the same algebra applied four times to four `(Policy, Profile)` pairs. The gluing condition is a single property test (chapter 3.4, `policyOrthogonal`).

### I2. Emit and read-side form an adjunction

`emit : Catalog -> ArtifactByKind` is left adjoint to `read-side : DeployedSchema -> Catalog`, modulo Π-erasure.

The unit of the adjunction (`η : id ⇒ read-side ∘ emit`) is the canary's fixpoint claim: emitting and reading-back is the identity on Catalog up to erasure.

The counit of the adjunction (`ε : emit ∘ read-side ⇒ id`) is the converse: reading a deployed schema, projecting it back through V2's passes, and re-emitting should be the identity on the deployed shape.

**Concrete leverage:**
- The triangulation comparator (`C_ossys ≡ C_round`?) is testing the adjunction's unit.
- The promoted-lane integration test (`deploy → re-read → equal-by-SsKey`) is testing the counit.
- Drift detection is `read-side(deployed) - C_ossys` — the failure of the unit, surfaced as a diff.

If the adjunction holds, V2 IS the V1-deployed schema's algebraic shadow. The cutover-trustworthy property follows from the adjunction.

### I3. CatalogDiff is the morphism set

In the category of catalog-evolutions (catalogs over time, evolving via renames/adds/removes), the morphisms are diffs. `CatalogDiff.between a b` is `Hom(a, b)` — the set of morphisms from `a` to `b`.

Composition is `compose : CatalogDiff -> CatalogDiff -> CatalogDiff` — given `between a b` and `between b c`, you get `between a c` (modulo loss-free paths).

Identity is `CatalogDiff.empty` — between a Catalog and itself.

**Concrete leverage:**
- `RefactorLogEmitter` is a functor from the morphism category to the SSDT-XML category.
- `RemediationEmitter` is a functor to the DACPAC category.
- Cross-version drift threading (chapter 3.5 + post-cutover) is *composition of diffs*: today's drift composes with yesterday's via `CatalogDiff.compose`.

This is why `RefactorLogEmitter` *isn't* "the special emitter that takes a separate input." It's just another emitter, where the input happens to be a morphism. The asymmetry was an illusion of names.

### I4. The fallback ladder is a quotient

V1-only / V2-augmented / V2-driver are three points on a chain of quotients applied to the projection's range:

- **V1-only:** the quotient that says "V2's output is irrelevant; only V1's deployed schema matters."
- **V2-augmented:** the quotient that says "V2's output is informational; V1's deployed schema is the ground truth, but V2 must agree with V1 modulo tolerance."
- **V2-driver:** the quotient that says "V2's output IS the ground truth; V1's existence is a fallback."

Same algebra, three quotients. The decision criterion at T-30 is the *choice of quotient* per environment.

**Concrete leverage:**
- The fallback ladder doesn't need three implementations. It needs one implementation and three CI configurations selecting which projection is authoritative.
- Per-environment-per-artifact-type granularity falls out for free: `(env, artifact-type) -> quotient`.
- The transition from V2-augmented to V2-driver per environment is a *change in the quotient's equality relation*, not a code change.

### I5. Property tests are continuations

A failing FsCheck property invokes the shrinker. The shrinker walks back along the morphism chain (Catalog → emit → ArtifactByKind → render → string → deploy → SQL Server → read-back → Catalog) to the smallest counterexample.

The shrinker IS the *continuation* — the inverse of the property's morphism composition. FsCheck's shrinker shape (per Appendix E §E.2: drop module → drop kind → drop reference → drop index → drop attribute → shrink type) walks the morphism chain in reverse, removing structure at each step.

**Concrete leverage:**
- A well-shaped shrinker reduces a 1000-line counterexample to 5 lines.
- The shrinker order matters: outer-first (drop a module) reduces faster than inner-first (drop a single attribute).
- Custom shrinkers preserve cross-field coherence (FK targets exist; PKs hold) — the *category structure* is preserved across shrinks.

### I6. The chapter is a tessellation instance

Each chapter delivers exactly one new instance of one tessellation pattern. The patterns are the seven listed above; the chapters are concrete inhabitants.

| Chapter | Pattern delivered | Type variable inhabited |
|---|---|---|
| 3.1 | Adapter (read-side) + Compare | `Sql.ReadSide` source; `Tolerance` |
| 3.2 | Adapter (rowsets variant) | `SnapshotSource.Rowsets` |
| 3.3 | Π (DACPAC) + Render | `TSqlObjectScript` + `DacPackage` |
| 3.4 | Property × 12 | tier-1/2/3 predicates |
| 3.5 | Π (RefactorLog) + Diff | `RefactorLogEntry list` over `CatalogDiff` |
| 3-cross-cutting | Primitive (P1, P5, P7 type-encoded) | `ArtifactByKind`, `SsKey` four-variant, `CatalogDiff` |
| 4.1.A | Π (SSDT DDL) + Render + Π (Manifest) | `SsdtFile` + `Manifest` |
| 4.1.B | Π × 3 (data triumvirate) + Adapter (CDC discovery) + Adapter (Migration) | `DataInsertScript` + `CdcAwareness` |
| 4.2 | Pass (UserFkReflow) + Primitive (P5 — `UserMatchingStrategy`) | `UserRemapContext` |
| 4.3 | Π × 3 (operational diagnostics) | `JsonElement` per artifact |
| 4.4 | Π (Remediation) over Diff | `RemediationDacpac` |

Every chapter is "instantiate pattern X with type Y." The slice list is the *implementation* of the instantiation; the pattern is universal.

**Concrete leverage:** when a chapter feels arbitrary, identify the pattern. The pattern's universal properties (T11 for Π, A18 amended for emitters, A33 for ordering, A34 for Profile independence) tell you what tests must pass. The chapter writes itself.

---

## F# expression — where point-free pays

F# has `>>` (forward composition) and `<<` (backward composition). Adopt judiciously: point-free pays where the parameter name carries no documentation value.

### Where it pays

**The canary predicate library.** Predicates are `Catalog -> bool`; combinators compose them.

```fsharp
// Verbose
let canaryPredicate c =
    let emitted = RawTextEmitter.emit c |> Result.value
    let deployed = deploy emitted
    let reread = ReadSide.toCatalog deployed
    Catalog.equalModuloDacpacErasure c reread

// Pipe form (operator-style)
let canaryPredicate c =
    c
    |> RawTextEmitter.emit
    |> Result.value
    |> deploy
    |> ReadSide.toCatalog
    |> Catalog.equalModuloDacpacErasure c

// Point-free, with the round-trip extracted
let private roundTrip = RawTextEmitter.emit >> Result.value >> deploy >> ReadSide.toCatalog
let canaryPredicate c = Catalog.equalModuloDacpacErasure c (roundTrip c)
```

Point-free wins because `roundTrip` *names a thing*: the round-trip pipeline. The pipeline is the load-bearing concept; expressing it as `>>` composition makes the structure visible.

**The renderer chain.** Render compositions are pure pipelines.

```fsharp
let toSsdtDirectory =
    Render.toSsdtDirectory
    >> Map.add "manifest.json" (ManifestEmitter.emit catalog |> Render.toJsonString)
```

Where the renderer chain is long enough that the intermediate names don't add value, point-free is cleaner.

**Property combinators.** Compositional predicates.

```fsharp
let (.&&.) p1 p2 c = p1 c && p2 c
let (.||.) p1 p2 c = p1 c || p2 c
let not p c = not (p c)

// Compose:
let canaryFastLane = t1ByteEqual .&&. siblingChorusAgrees .&&. coproductPreservation
let canaryFullStack = canaryFastLane .&&. roundTripBySsKey .&&. idempotentRedeploy
```

The combinator pattern makes property libraries composable without explicit binding.

### Where to keep parameter names

When a parameter carries domain meaning that aids the reader, keep it.

```fsharp
// Don't:
let processCatalog = CanonicalizeIdentityPass.run >> Lineage.bind NullabilityPass.run >> ...

// Do:
let processCatalog (catalog: Catalog) (policy: Policy) (profile: Profile) =
    catalog
    |> CanonicalizeIdentityPass.run
    |> Lineage.bind (NullabilityPass.run policy profile)
    |> Lineage.bind (UniqueIndexPass.run policy profile)
    |> Lineage.bind (ForeignKeyPass.run policy profile)
```

The named parameters make `policy` and `profile` threading explicit. Going point-free here loses the threading visibility.

### The two-consumer threshold for combinators

Don't extract `>>=`, `<!>`, or other operator combinators on the first consumer. The shape needs to recur — at least three call sites of the same shape — before the operator pays its keep. Per `DECISIONS 2026-05-13` (anticipation vs speculation).

---

## Computation expressions — where they earn their place

CLAUDE.md's F# feature surface table marks computation expressions as **underused**, with trigger: "consumer chains grow long enough that the operator-style noise outweighs the explicit operations."

The trigger fires in three places.

### CE1. Pass composition with branching error handling

```fsharp
// Operator form
let projectCatalog catalog policy profile =
    CanonicalizeIdentityPass.run catalog
    |> Lineage.bind (fun c -> NullabilityPass.run c policy profile)
    |> Lineage.bind (fun nullableResult ->
        match nullableResult with
        | Diagnostics.HasErrors errs -> Lineage.error errs
        | Diagnostics.Clean c -> UniqueIndexPass.run c policy profile)
    |> Lineage.bind (fun c -> ForeignKeyPass.run c policy profile)
    |> Lineage.bind (fun c -> SymmetricClosure.run c)

// Computation expression form
let projectCatalog catalog policy profile = lineage {
    let! canonical = CanonicalizeIdentityPass.run catalog
    let! withNullability = NullabilityPass.run canonical policy profile
    let! withUnique = UniqueIndexPass.run withNullability policy profile
    let! withForeignKey = ForeignKeyPass.run withUnique policy profile
    return! SymmetricClosure.run withForeignKey
}
```

The CE form makes the pass *order* visible without the noise of `Lineage.bind` repeating. The trigger fires when chains exceed 3 binds.

### CE2. Diagnostics accumulation across passes

```fsharp
let analyzeCatalog catalog policy profile = diagnostics {
    do! NullabilityPass.diagnose catalog policy profile
    do! UniqueIndexPass.diagnose catalog policy profile
    do! ForeignKeyPass.diagnose catalog policy profile
    return ()
}
```

Where each pass produces a `Diagnostics<unit>` (just findings, no value), the CE form is dramatically cleaner than nested `Diagnostics.tellMany`.

### CE3. Result-bind chains in adapters

```fsharp
let parseSnapshot bytes = result {
    let! json = parseJson bytes
    let! schema = validateSchema json
    let! catalog = projectToCatalog schema
    return catalog
}
```

When adapter code chains 4+ binds, CE form earns its place. Per CLAUDE.md, ship CE Builder types when the consumer feedback shows chains noisy.

### What CE doesn't replace

CE does not replace the underlying writer monad's primitive operations. `Lineage.bind`, `Diagnostics.tellMany`, `LineageDiagnostics.bind` remain the canonical surface. CE is *syntax sugar* over those primitives.

---

## Active patterns — when nested matches recur

Active patterns earn their place when the same nested-match shape appears in three or more consumers. Per the two-consumer threshold + the third-distinct-shape refinement (`DECISIONS 2026-05-14`).

### Worked candidate: outcome destructuring

```fsharp
// Today: nested match across NullabilityPass, UniqueIndexPass, ForeignKeyPass
match outcome with
| EnforceNotNull evidence ->
    match evidence with
    | PrimaryKey -> ...
    | PhysicallyNotNull -> ...
    | LogicalMandatoryNoNulls _ -> ...
| KeepNullable reason -> ...
| RequireOperatorApproval conflict -> ...

// Active pattern (when shape recurs across 3+ pass consumers)
let (|Enforce|Keep|Approval|) outcome =
    match outcome with
    | EnforceNotNull e -> Enforce e
    | KeepNullable r -> Keep r
    | RequireOperatorApproval c -> Approval c

// Now consumer can use:
match outcome with
| Enforce e -> ...
| Keep r -> ...
| Approval c -> ...
```

The active pattern is a *coarser view* over the closed DU; consumers that don't care about evidence sub-cases use the coarse form. The fine-grained form remains available for consumers that do.

### Worked candidate: SsKey root extraction

```fsharp
let (|RootIs|) (k: SsKey) =
    let rec walk = function
        | OssysOriginal g -> Some g
        | V1Mapped (g, _) -> Some g
        | DerivedFrom (parent, _) -> walk parent
        | Synthesized _ -> None
    walk k

// Consumer:
match k with
| RootIs (Some guid) -> // unconditional A1; render guid form
| RootIs None -> // synthesized; render basis
```

The active pattern factors the recursion across consumers.

---

## The chapter as a tessellation instance

This deserves its own framing because it changes how to read the eight pre-scopes.

**Old frame:** "Each chapter is a milestone with a slice list and acceptance criteria."

**New frame:** "Each chapter is one new instance of one tessellation pattern. The slice list is *the implementation* of the instantiation; the pattern is *the contract*."

What this means concretely:

- **When a slice plan looks unfamiliar**, identify the pattern. Once identified, the slice plan's load-bearing tests follow from the pattern's universal properties.
- **When a chapter pre-scope omits something**, ask: "is this required by the pattern?" If yes, it's a gap; if no, it's a deferred extension.
- **When two chapters' pre-scopes overlap**, ask: "are they instantiating the same pattern with different type variables, or different patterns?" Same-pattern overlaps can be unified; different-pattern overlaps must be split.
- **When a new chapter is being scoped**, name the pattern first; then write the slice list as the pattern's implementation.

**Example:** Chapter 4.1.B is named "CDC-aware data triumvirate." Its pattern: three Π's (`StaticSeedsEmitter`, `MigrationDependenciesEmitter`, `BootstrapEmitter`) plus an Adapter (`CdcDiscovery`). The triumvirate is *not* "three new things" — it's three instantiations of Pattern Π with type variable `DataInsertScript` plus per-emitter Profile-shape variation.

**Example:** Chapter 3.5 is named "RefactorLogEmitter + CatalogDiff." Pattern: one Π over Diff plus one Primitive (CatalogDiff). The "RefactorLog is special" framing was an illusion — it's just Π with Diff input.

---

## Concrete leverage — what the deeper view enables

The seven primitives + seven patterns + six inferences aren't decorative. Each unlocks a specific capability the chapter pre-scopes hint at but don't always articulate.

### L1. Chapters can ship in parallel where patterns are independent

Two chapters instantiating *different* patterns can ship simultaneously. The cross-cutting refactor (`ArtifactByKind` + `SsKey` DU split) instantiates Primitives P1, P5, P7 and is a precondition for chapters that instantiate Pattern Π. But chapter 3.1 (Adapter + Compare) doesn't depend on the refactor's primitives — its patterns are different. **Chapters 3.1 and the cross-cutting refactor can run in parallel.**

### L2. The canary's predicate library is portable across emitters

`siblingChorusAgrees`, `t1ByteEqual`, `coproductPreservation` work on `Emitter<'element>`-typed inputs. The same property test runs against any emitter that implements Pattern Π. **Chapter 4.1.A's SSDT DDL emitter inherits chapter 3.4's predicate library at zero cost.**

### L3. Drift detection is a free corollary of read-side adapter

Drift detection = `compare (read-side deployed) (project-from-OSSYS source)`. Both halves are already chapter-3.1 deliverables. **Drift detection ships as a CI cron job, not a chapter.**

### L4. RemediationEmitter is a free corollary of CatalogDiff + DacpacEmitter

RemediationEmitter = `dacpac ∘ CatalogDiff.toRemediationCatalog`. Both halves are chapter-3 deliverables. **Chapter 4.4 is ~360 LOC, not a "new emitter" chapter.**

### L5. Cross-version evolution is just compose-of-diffs

Multi-version refactor logs (V0 → V1 → V2 of the schema) compose by `CatalogDiff.compose`. **No new code is required to handle cross-version refactor histories beyond the basic `CatalogDiff` primitive.**

### L6. Multi-environment cutover is the same algebra applied N times

Per inference I1 (sheaf structure), the four-environment cutover is one algebra evaluated four times against four `(Policy, Profile)` pairs. **No new code is required for N environments; the property `policyOrthogonal` proves the algebra commutes with environment.**

### L7. Future emitters drop in trivially

GraphQL schema emitter (deferred): `Emitter<GraphqlTypeDef>`. Same Π pattern. T11 free. Approximately 100 LOC. **Future emitters are bounded by their Π body, not by integration cost.**

### L8. Adapter-language portability is type-encoded

The `Adapter<'source, 'internal, 'error>` shape doesn't care about the source language. A future DACPAC reader, a future OData reader, a future REST API reader — each instantiates the same pattern. **The OutSystems-specific bits live in the type variable, not in the pattern.**

### L9. The fallback ladder is configuration, not branching code

Per inference I4 (quotient structure), the three tiers of the fallback ladder are three CI configurations selecting which projection is authoritative. **The cutover decision criterion at T-30 is a YAML edit, not a code change.**

### L10. AXIOMS amendments compose

T1 amended (binary) doesn't replace T1; it *specializes* T1 to the binary case. T11 amended (diff inputs) doesn't replace T11; it *extends* T11 to diff-typed sources. A1 amended (four-variant) refines A1 with the bound type-visible. **AXIOMS evolves monotonically; old proofs remain valid; new proofs strengthen the system without invalidating prior ones.**

---

## Closing — the spine is the system

The PLAYBOOK names the *patterns*. This document names *what makes the patterns inevitable*: the system *is* a category, and the chapter pre-scopes are concrete morphism constructions.

When the next chapter feels heavy, return to this document and ask:

1. **Which pattern instantiates here?** (Π / Adapter / Pass / Render / Compare / Property / Diff)
2. **What's the type variable?** (the new element type, source type, output type)
3. **Which primitives compose?** (P1–P7 — what does this chapter add to which primitive?)
4. **Which inferences pay back?** (I1–I6 — what does this chapter unlock for downstream chapters?)
5. **What's free?** (drift detection from read-side; RemediationEmitter from CatalogDiff; cross-version evolution from diff composition)

The answers tell you what the chapter is, what tests must pass, and what it unlocks.

V1 ships the cutover. V2 makes it verifiable through a sibling chorus. The chorus is a category. The category has seven primitives, seven patterns, and six structural inferences. Every chapter is a tessellation instance. Every test is a proof obligation on a universal property. Every divergence is a documented erasure.

Hold the spine. The spine is the system.

— Recorded for the receiving agent.
