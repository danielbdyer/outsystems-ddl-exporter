# AXIOMS — The Catalog as Protagonist

The formal system the projection sidecar implements. Code refers to this
document by axiom number in comments and test names. A failing test pointing
at axiom A12 should send a reader directly to the section below.

**Companion documents.** `PRODUCT_AXIOMS.md` carries the L3 product axioms —
the operator-meaningful claims V2 must verifiably guarantee end-to-end. This
file proves V2 is internally consistent (L2 axioms); `PRODUCT_AXIOMS.md`
proves V2 satisfies the operator's contract (L3 axioms). The two are
intentionally separate: L2 axioms are the algebra; L3 axioms are the
operator's promise; they cite each other but evolve on different rhythms.
`AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md` carries the coverage analysis —
which axioms in *both* documents are structurally enforced at L1, which are
convention-enforced, which are weakly covered, and which are unnamed gaps —
along with campaigns to address the gaps. The audit doc is refreshed on each
annual re-audit; this file remains the canonical L2 surface, updated through
chapter-close amendments.

The original V1 algebraic spec stated thirty-one axioms (A1–A31)
generating ten theorems (T1–T10). V2 has extended the system: A6, A12,
A17, A18, A24, and T1 carry amendments (recorded under "V2 Amendments"
below); A32, A33, A34, A35, A36, A39, A40, A41, A42, and T11 are new.
The current count is **A1–A42 generating T1–T11** with six amended
originals. The axioms are grouped into eight thematic clusters; the
theorems cluster by what falls out of the construction. Code and tests
cite the **amended** form when both exist; the original form is the
historical lineage of the amendment.

---

## Primitive notions

- **Kind.** A schema-defined entity (table-like). Carries identity, attributes,
  references, modality marks, origin, physical realization.
- **Attribute.** A scalar field on a kind. Carries identity, name, type, and
  per-attribute physical realization (column-level metadata).
- **Reference.** A directed edge from a source kind's attribute to a target
  kind. Carries identity and delete-action policy.
- **Module.** A coproduct cell of the catalog. The catalog decomposes as
  ⊕(modules); the projection respects this decomposition.
- **Catalog.** The whole; an aggregate of modules.
- **Policy.** Static configuration data; determines how the catalog is lensed.
- **Lifecycle.** The temporal order over catalog states.
- **Surface IR.** The output of the projection; consumed by a target binder.
- **Snapshot.** An immutable Surface IR identified by a content hash.
- **Lineage.** The writer-monadic trail attached to every value in the
  pipeline; constitutive, not observed.

---

## Group A — Identity (A1–A5)

**A1. Stable identity.** Every kind, attribute, and reference carries a
stable identifier (the SS_KEY in OutSystems' case) that survives renames,
redeploys, refactors, and migrations. Identity is the spine.

  *Enforcement.* `SsKey` is a value type on every IR node.
  *Property test.* Identity is preserved across name mutations.

  *Bounded under input-path lossiness (2026-05-19).* The
  identity-survives-rename guarantee is **bounded by what the input
  source preserves**. Through V2's current `SnapshotJson` path
  (consuming V1's `osm_model.json`), `SsKey` is **synthesized
  deterministically from name fields** because V1's JSON projection
  layer strips the SSKey columns the rowsets carry — a documented
  member of the JSON-projection-lossiness class. Through that path,
  renames in the source OutSystems platform produce different
  `SsKey` values in V2's IR; A1's identity-survives-rename
  guarantee is not honored for renames that traverse the
  JSON-snapshot path.

  **Bound structurally resolved at chapter 3.2 close (2026-05-10).**
  The `SnapshotRowsets` variant of `SnapshotSource` shipped end-to-end
  across five slices (commits `6dab9cd` → `a74b904`). V2's adapter
  now reads V1's `SS_Key` columns directly via the `RowsetBundle`
  carrier, materializing them as `SsKey.OssysOriginal guid` at every
  level (module / kind / attribute). The four-variant DU amendment
  (line 833) is no longer "type-stratified placeholder" — the
  `OssysOriginal` variant is operationally reachable at the
  OSSYS-adapter boundary. Through the rowset path, A1's
  identity-survives-rename guarantee is honored unconditionally.
  Through the JSON path, the bound persists by design (the JSON
  shape continues to strip SSKey columns); both paths coexist as
  source variants. See `DECISIONS 2026-05-10 — Chapter 3.2 close`
  for the slice-by-slice account; `LiveOssysConnection` remains
  reserved for the future direct-DB-touching variant.

  Documented divergence; not a bug. Future agents reading A1
  should follow the bound's full disposition in DECISIONS rather
  than assume A1 is unconditional through every path.

**A2. Identity is not name.** Identity is what the name refers to. Names are
presentation; identity is what persists.

  *Enforcement.* `Name` and `SsKey` are distinct types. No core code uses a
  name as a structural key.
  *Property test.* Lookups by identity succeed under renames; lookups by
  name do not, by construction (no name index in the core).

**A3. Identity is invariant under rename.** Renaming a node changes its
name field but not its identity field; downstream references continue to
resolve.

  *Enforcement.* Pass invariant: name-touching passes must not touch
  `SsKey`.
  *Property test.* `rename(n) >> resolve(refTo(n)) = resolve(refTo(n))`.

**A4. Identity is type-distinguished from string.** `SsKey` is a single-case
discriminated union: `type SsKey = Original of string | Derived of original:
SsKey * reason: string`. The compiler rejects accidental string-as-key use
and machine-checks the original/derived distinction. Structural equality of
catalog nodes is by `SsKey` only; two kinds with identical `SsKey` but
differing names or attribute orderings are equal at the catalog level.

  *Enforcement.* The DU; equality overrides on IR records that compare by
  `SsKey`.
  *Property test.* `kindsWithSameSsKeyAreEqual`,
  `kindsWithDifferentSsKeyAreNotEqual`.

**A5. Derived identities are deterministic and traceable.** When a pass
introduces a new node (e.g., symmetric closure adds an inverse reference),
the new node's identity is `Derived(parent, reason)` — never freshly
generated, never randomized. The `reason` string is documented and reserved
(the sidecar uses the reasons listed in `DECISIONS.md`).

  *Enforcement.* `SsKey.Derived` is the only constructor for synthesized
  identity; no code path constructs `Original` from a non-source string.
  *Property test.* Re-running a pass yields the same derived keys for the
  same parents.

---

## Group B — Catalog structure (A6–A11)

**A6. Three aggregates, only three.** The system has three substantive
degrees of freedom: Catalog, Policy, Lifecycle. Everything else is forced.

  *Enforcement.* Three top-level F# types; nothing else floats around as
  global configuration.

**A7. Static modality is part of catalog structure.** Kinds with the
`Static` modality carry their populations as part of the catalog. The IR
holds the rows; the unfold pass lifts them into type-level metadata for Π.
The Catalog Reader populates them when reading from real meta. There is no
parallel data layer.

  *Enforcement.* `ModalityMark.Static` payload includes a populations
  collection on the kind; `Kind.staticPopulation : StaticRow list option`.
  *Note.* Real populations may be large; the algebra is indifferent. Do not
  pre-solve bounded loading until evidence requires it.

**A8. Kinds carry a fixed shape.** Every kind has: identity, name,
attributes, references, modality marks, origin, physical realization.

  *Enforcement.* `Kind` record with required fields; no optional fields
  except those documented.

**A9. Origin is a closed three-way discriminant.** Every kind originates
from one of: `OsNative`, `ExternalViaIntegrationStudio`, `ExternalDirect`.
Widen only when evidence forces it.

  *Enforcement.* `Origin` discriminated union with exactly three cases.

**A10. References are directional in the catalog.** A reference has a
source-side and target-side; symmetric navigation in the surface (if any) is
introduced by the symmetric-closure pass, not present in the source IR.

  *Enforcement.* `Reference` is directional; the symmetric pass produces
  derived inverse references whose identities are `Derived(..., "inverse")`.

**A11. Modules form a coproduct.** The catalog decomposes as ⊕(modules);
the projection respects the decomposition.

  *Enforcement.* `Catalog = { modules : Module list }`; modules are disjoint
  by identity.
  *Property test.* See T2.

---

## Group C — Policy as data (A12–A16)

**A12. Policy is data.** The Policy aggregate is a static configuration
value: auditable, diffable, version-controllable. It contains no procedural
behavior.

  *Enforcement.* `Policy` is an F# record; passes consume it as a parameter.

**A13. Type correspondence is policy.** Which primitive becomes which target
scalar is in the policy, not the structural projection.

**A14. Visibility is policy.** What is exposed and what is withheld is in
the policy.

**A15. Naming morphism is policy and never touches identity.** How
identifiers transform on the surface is in the policy. The morphism applies
to `Name`; `SsKey` flows through unchanged.

  *Enforcement.* Naming pass operates on `Name` fields only; identity field
  is untouched.

**A16. Static treatment is policy.** Whether a static kind appears as an
enum, a queryable object, or both is policy-driven.

---

## Group D — Functor factoring (A17–A20)

**A17. Project = Π ∘ E.** The exposure functor factors into enrichment (E)
composed with structural projection (Π).

**A18. E carries all configuration; Π carries none.** Π is mechanical: kinds
become types, references become field paths, attributes become scalar
fields. If you find yourself wanting to configure Π, the configuration
belongs in a pass.

  *Enforcement.* The projector module takes only an enriched IR; no policy
  parameter enters Π.

  *Amended (2026-05-12).* See "A18 amended — Π consumes evidence subsets,
  never Policy" near the end of this file. The amendment makes explicit
  which inputs **are** available to Π (whichever subset of `Catalog ×
  Profile` the emitter needs) and codifies the closed denial on Policy.
  The amended form is load-bearing for the three-sibling-Π architecture;
  the original above is the historical statement of the rule.

**A19. Each pass is a structure-preserving endofunctor on the catalog.** A
pass has signature `Catalog -> Lineage<Catalog>` (or fails inside the
lineage, returning `Lineage<Result<Catalog>>`).

**A20. Pass order is meaningful and explicit.** The pipeline declares its
passes in a list; order is part of the policy. Reordering structure-
preserving passes that commute is allowed (and tested where it matters), but
the canonical order is the contract.

---

## Group E — Lifecycle and snapshots (A21–A22)

**A21. Refresh is idempotent on stable input.** Two refreshes of the same
catalog under the same policy produce byte-identical snapshots.

  *Property test.* `` ``A21: refresh is idempotent`` ``.

**A22. Snapshots are content-addressed.** A snapshot's identity is the hash
of its bytes. Identical content ⇒ identical hash. Two refreshes that produce
identical content produce the same hash; the second is a no-op.

### A-Lifecycle-1..4 — the temporal axis (operationalized 2026-05-31, §5.3)

A `Lifecycle` is a monotone chain of `CatalogSnapshot`s along one `Timeline`
(`src/Projection.Core/Lifecycle.fs`). It is the **outer envelope** over
`Project` — the inner kernel `Catalog × Policy × Profile` is untouched
(A6-amended / A17). These underwrite `PRODUCT_AXIOMS.md` Group Lifecycle
(L3-L1 / L3-L2 / L3-L3).

**A-Lifecycle-1 (↔ L3-L1). Schema evolution is replayable.** `replayTo`
recovers the Catalog stored at any `Version` (materialized form; the
diff-replay reconstruction `fold applyDiff C₀` awaits the `CatalogDiff`
compose operator — H-007).

  *Property test.* `` ``A-Lifecycle-1 (L3-L1): replayTo recovers the snapshotted catalog`` `` (`LifecycleTests.fs`).

**A-Lifecycle-2 (↔ L3-L2). Refactor-log history is monotonic.** `append`
enforces a strictly-increasing `Version` ordinal; a non-monotone append
fails rather than reordering. Prior history is never altered.

  *Property test.* `` ``A-Lifecycle-2 (L3-L2): append advances latest and never alters prior history`` ``.

**A-Lifecycle-3 (↔ L3-L3). Per-timeline history is independent.** Each
`Lifecycle` carries exactly one `Timeline`; histories on distinct timelines
are independent values.

  *Property test.* `` ``A-Lifecycle-3 (L3-L3): timelines are independent histories`` ``.

**A-Lifecycle-4. evolutionChain composition is associative.** *(Bucket C —
not yet operational.)* `evolutionChain` folds `CatalogDiff.between` into a
per-edge diff list, but `CatalogDiff` has no compose operator (diff∘diff),
so composition-associativity is not yet expressible. Promotes when H-007
(SchemaDelta category) gives `CatalogDiff` an `apply`/`compose` peer to
`between`.

  *Skip stub.* `` ``A-Lifecycle-4: evolutionChain composition is associative`` `` (`AxiomTests.fs`).

---

## Group F — Lineage (A23–A26)

**A23. Lineage events carry transformation_version.** Each lineage event
records `{ PassName: string; PassVersion: int; SsKey: SsKey; TransformKind:
TransformKind }`. The version is what makes provenance hashes stable across
pipeline evolution; without it, two functionally different versions of the
same pass produce indistinguishable lineage and replay determinism is lost.

  *Enforcement.* `LineageEvent` record; every pass declares its version
  literal; bumping a pass's behavior bumps its version in the same commit.

**A24. Lineage composition is chronological.** When `f >>= g`, the resulting
trail is `f.Trail ++ g.Trail` — earliest-first. This is the convention; all
passes and all readers rely on it.

  *Enforcement.* The `bind` implementation in `Lineage.fs` documents and
  encodes this. Reversed-trail bugs are subtle and expensive; the test
  `` ``A24: lineage trail is chronological under bind`` `` guards it.

  *Amended (2026-05-22).* See "A24 amended — chronological-bind extends
  to the WriterT-stacked dual writer" near the end of this file. The
  amendment generalizes the law to `Diagnostics<'a>` and to
  `LineageDiagnostics<'a>` (the dual writer), names the WriterT-stacking
  algebra explicitly, and notes that the Kleisli laws over
  `Pass<'a, 'b>` (H-003) are inherited from the stacked monad's laws.
  Code and tests cite the amended form when both exist.

**A25. Lineage is constitutive, not observed.** Every IR transformation runs
inside the lineage monad. Lineage exists for every transformation, not as
an opt-in tracker.

  *Enforcement.* Pass signature `IR -> Lineage<IR>`; no pass exists outside
  it.

**A26. Lineage layers separately from structural identity.** Two nodes with
identical `SsKey` are structurally equal at the catalog level (A4) even if
their lineage trails differ. Lineage is metadata travelling alongside the
structure, not part of structural identity.

  *Property test.* `` ``A26: lineage difference does not affect structural
  equality`` ``.

---

## Group G — Snapshots (A27–A28)

**A27. Pointer swap is atomic.** The active-snapshot pointer flips from old
hash to new hash atomically; queries in flight under the old snapshot finish
under the old.

**A28. Snapshots are immutable and persistent.** Old snapshots remain in the
store forever, identifiable by hash. The store is append-only.

---

## Group H — Trust boundary and federation (A29–A31)

**A29. Authorization is not in the algebra.** Authorization gates access
without altering schema. It is composed externally (typically as middleware
over the executor); the algebra makes no claims about
`Authorization ∘ Project`.

**A30. Business logic is not in the catalog.** Computed attributes,
validation rules, action references — all outside the catalog's structural
vocabulary.

**A31. The catalog is a federation point.** The catalog originates
definitions for `OsNative` kinds and receives definitions for external
kinds via the IS-functor (Catalog Reader for external sources). The
migration arc moves origins from `OsNative` toward external, monotonically;
the catalog can become a unidirectional consumer without changing the
algebra.

---

## Theorems (T1–T10)

**T1. Determinism.** `Project` is a pure function: `(catalog, policy) ↦
surface`. Same inputs, same outputs, bit-identical.

**T2. Coproduct preservation (modular composition).** For disjoint modules
`M1`, `M2`: `Project(M1 ⊕ M2) = Project(M1) ⊕ Project(M2)`.

**T3. Free construction (universal property).** The surface is the free
exposure ontology generated by the catalog under the policy: any
structure-preserving exposure factors uniquely through `Project`.

**T4. Sibling functor commutativity.** When two projectors share an
enrichment, their outputs are mutually consistent under identity:
`Π_A ∘ E` and `Π_B ∘ E` from the same catalog produce surfaces that agree
on identity correspondences. SSDT, when it compiles, becomes a structural
typechecker for the GraphQL projection.

**T5. Lineage compositionality.** The lineage of `g ∘ f` applied to a value
equals `f.lineage ++ g.lineage`. The writer monad's monoid law is the
lineage axiom (A24).

**T6. Refresh idempotence.** Refreshing a stable catalog under a stable
policy yields a byte-identical snapshot. (Consequence of T1 and the
deterministic hash.)

**T7. Snapshot deduplication.** Identical content ⇒ identical hash; the
store rejects re-puts as no-ops.

**T8. Structural diffability.** Any two snapshots can be diffed structurally
to produce a deterministic, canonical change list keyed by identity.

**T9. Refactor freedom under rename.** Renames preserve all downstream
references, since references resolve by identity (A3, A4).

**T10. Boundary honesty.** Authorization, business logic, and authority
assignment do not alter schema. (Trivially follows from A29, A30, A31, but
worth naming as a theorem because it's the axis along which the algebra
deliberately stays small.)

---

## Operational pattern: code ↔ axiom citation

Tests that enforce an axiom or theorem name it explicitly:

    [<Property>]
    let ``A4: kinds with same SsKey are structurally equal regardless of names`` () = ...

    [<Property>]
    let ``A21: refresh is idempotent`` () = ...

    [<Property>]
    let ``T2: projection respects module coproduct`` () = ...

Comments at non-obvious code sites cite the law:

    // A24: trail is f ++ g, earliest-first
    let bind f m = ...

A test failure points at a specific axiom; a code reviewer can verify a
pass against the law it claims to satisfy. The discipline costs nothing and
pays compound interest.

---

# V2 Amendments

V2 reading of the masterwork (`docs/architecture/domain-model-constitution.md`)
and the decomposition (`docs/architecture/entity-pipeline-unification-v2.md`)
surfaced refinements to four original axioms and added three new ones.

The discipline: original numbering is preserved as a historical artifact of
the V1 algebraic spec. Amendments are recorded here with their rationale.
New axioms are appended (A32–A34, T11). Code comments and test names
should cite the **amended** form when both exist; the original form is the
formal lineage of the amendment.

## A6 amended (2026-05-06) — three substantive inputs, one temporal dimension

**Original (V1):** "Three aggregates, only three. The system has three
substantive degrees of freedom: Catalog, Policy, Lifecycle."

**Amended (V2):** The system has **three substantive inputs** — Catalog,
Policy, and Profile — and **one temporal dimension** — Lifecycle. Together
they fully determine the projection.

  - **Catalog** is structural truth — what kinds exist.
  - **Policy** is operator intent — three orthogonal axes (see A12 amended).
  - **Profile** is empirical evidence — what the data actually shows.
  - **Lifecycle** is time — the partial order under which all three evolve.

Profile cannot be folded into Catalog (structure vs. evidence) or Policy
(intent vs. fact). It earns its place by changing on a different timescale
and originating from a different source.

  *Enforcement.* `ProjectionInput = { Catalog; Policy; Profile }` in F#;
  `Profile.empty` is a valid value for use cases that consume no evidence.
  *Property test.* See A34 (Profile is independent).

## A12 amended (2026-05-06) — Policy has three orthogonal axes

**Original (V1):** "Policy is data. The Policy aggregate is a static
configuration value: auditable, diffable, version-controllable."

**Amended (V2 2026-05-06):** Policy is data **with three orthogonal axes** —
Selection (which kinds participate), Emission (what artifacts are produced),
Insertion (how artifacts are applied). Each axis is its own structured
value; the three are composed in a single record. Changing one axis does
not constrain the others.

  *Enforcement.* `Policy = { Selection; Emission; Insertion }` in F#; each
  axis a value type with its own validation.
  *Property test.* `policyAxesAreOrthogonal` — perturbing one axis does not
  alter the output of passes that read the other two.

## A12 amended again (2026-05-09) — Policy has four orthogonal axes

**Prior amendment (2026-05-06):** three axes — Selection, Emission, Insertion.

**Amended (V2 2026-05-09):** Policy is data **with four orthogonal axes** —
Selection, Emission, Insertion, **Tightening**. The Tightening axis was
surfaced under "IR grows under evidence" (DECISIONS 2026-05-09) when the
`NullabilityEvaluator` admire pass identified the need — tightening is
genuinely orthogonal to the other three, controlling *what shape of
constraint decisions* gets produced, independent of which kinds
participate, what artifacts are emitted, or how data is applied.

  *Enforcement.* `Policy = { Selection; Emission; Insertion; Tightening }`
  in F#; `TighteningPolicy = { Mode; NullBudget; AllowCautiousRelaxation;
  Overrides }`. `TighteningPolicy.create` validates `NullBudget ∈ [0, 1]`.
  *Property test.* Pairwise orthogonality of all four axes — perturbing
  any one does not alter helpers of the other three.

**On amendment discipline.** The three-axis amendment from 2026-05-06 was
right at the time given the evidence; the fourth axis arrives because a
real pass (`NullabilityPass`) needs it. Both amendments are preserved
above, in chronological order. Code and tests cite the most recent
amendment as the V2 contract; earlier amendments are the lineage of the
amendment, not the rule. Future amendments follow this discipline.

## A17 amended (2026-05-06) — E's signature

**Original (V1):** "Project = Π ∘ E."

**Amended (V2):** `Project = Π ∘ E`, where
`E : (Catalog, Policy, Profile) → EnrichedCatalog`. Π consumes the
EnrichedCatalog and produces target-surface artifacts; some Π's may also
consume specific value-typed payloads attached to nodes by E (see A32).

  *Enforcement.* Pass signatures specify the inputs they consume; passes
  that need profile evidence accept `Profile` explicitly.

## T1 amended (2026-05-06) — determinism extends to the triple

**Original (V1):** "Determinism. `Project` is a pure function: same catalog,
same policy, same surface."

**Amended (V2):** `Project` is a pure function on the triple
`(catalog, policy, profile) → surface`. Same triple, same surface,
bit-identical. Refresh on an unchanged triple is idempotent.

  *Property test.* `T1: Project is deterministic on (catalog, policy, profile)`.

---

## A32 (new, 2026-05-06) — Passes may produce values consumed by emitters

Passes are not restricted to producing values consumed by other passes. A
pass may attach a value-typed payload to the EnrichedCatalog (or alongside
it) that an emitter (Π) chooses to consume. Π is not restricted to
consuming only the structural skeleton; it may consume specific values
attached by E.

The masterwork's "dual-mode transform" framing (UAT-Users discovery in
Stage 3, application in Stage 5 INSERT or Stage 6 UPDATE) collapses to this
principle. Discovery is one E-pass producing a `UserRemapContext` value.
Application is two sibling Π's: an INSERT-mode Π consuming `(catalog,
context)` to emit pre-transformed INSERTs, and an UPDATE-mode Π consuming
`(context)` alone to emit standalone UPDATEs.

This becomes the canonical answer for any future "discover something at
one stage, use it at another" pattern. There is no special case; there is
the algebra working correctly.

  *Enforcement.* The EnrichedCatalog (or a sibling ProjectionContext value)
  carries pass-attached values; Π's signature names the values it consumes.
  *Property test.* `A32: discovered value visible to emitter`. Concretely
  for UAT-Users when implemented: discovery pass produces the same
  `UserRemapContext` regardless of which Π consumes it; both Π's agree on
  identity correspondences (a special case of T4).

## A33 (new, 2026-05-06) — Schema-Data Ordering Law

Schema emission uses **deterministic ordering** (alphabetical by SsKey or
stable canonical order); data emission uses **topological ordering**
(FK-dependency-safe). The two ordering disciplines are distinct and the
type system must forbid mismatches: a schema-emission configuration cannot
accept a topological-order input, and vice versa for data emission.

Rationale: schema artifacts must produce reproducible diffs (alphabetical
ordering survives every refactor; .sqlproj files stay clean). Data
artifacts must respect FK constraints (topological ordering prevents
reference violations on apply). Mismatching the two produces either fragile
diffs (topological in schema) or constraint violations (deterministic in
data).

  *Enforcement.* Two ordering value types — `DeterministicOrder` and
  `TopologicalOrder` — are not interchangeable. Emission configs accept
  one or the other, not both.
  *Property test.* Type-level: `SchemaEmissionConfig` cannot type-check
  with a `TopologicalOrder`; `DataEmissionConfig` cannot type-check with
  a `DeterministicOrder`.

## A34 (new, 2026-05-06) — Profile is independent of Catalog and Policy

Profile is structurally independent of Catalog and Policy. Changes to
Profile do not induce changes to either. Passes that do not consume Profile
are unaffected by it (their output is identical for `Profile.empty` as for
any populated Profile). Passes that consume Profile (e.g., the eventual
nullability evaluator, the FK enforcement evaluator) declare their
dependency in their type signature.

Profile carries no back-references to Catalog or Policy. If a future
schema tempts a `profile.entityId` or a `profile.policyMode` field, that
is coupling; resist it. Profile is indexed by coordinate at the boundary,
not at the IR level.

  *Enforcement.* `Profile` record references no Catalog or Policy types.
  *Property test.* `A34: passes that do not read Profile produce identical
  output for Profile.empty and any Profile`.

---

## T11 (new, 2026-05-06) — Sibling Π's commute on shared E-attached values

A specialization of T4 (Sibling functor commutativity) for A32. When two
Π's consume different subsets of values produced by a shared E, their
outputs agree on the values they share. Concretely: if Π_A consumes
`(catalog, X)` and Π_B consumes `(X)` from the same enriched catalog, then
both Π's see the same `X` and any structural correspondence keyed by `X`
holds in both surfaces.

  *Property test.* `T11: sibling Pi's agree on shared E-attached values`.

---

## A18 amended (2026-05-12) — Π consumes evidence subsets, never Policy

**Empirical refinement** surfaced by `DistributionsEmitter` (the third Π;
DECISIONS 2026-05-12). The original A18 (E carries all configuration; Π
carries none) prohibited Policy from flowing into Π. The amendment makes
explicit which inputs **are** available to Π and codifies the asymmetry
the algebra now relies on:

  **A Π consumes whichever subset of `Catalog × Profile` it needs, but
  never `Policy`.** SSDT and JSON take `Catalog -> string`; Distributions
  takes `Catalog -> Profile -> string`; future Π (Faker, anomaly reports)
  consume whichever subset their output requires. The closed denial:
  Policy is never a Π input.

The architectural reasoning, not just the rule:

  - **Catalog and Profile are evidence the system holds.** Catalog is
    structural evidence (kinds, attributes, references — what exists).
    Profile is empirical evidence (null counts, distributions, orphan
    realities — what was observed).
  - **Policy is intent the operator supplies.** Policy says "tighten under
    these gates," "select these kinds," "emit these artifacts." Intent is
    not evidence; it does not flow into projection.
  - **Π's are projections of evidence.** A Π takes the evidence the system
    holds and renders it as a target surface. The render is mechanical
    relative to the evidence; the evidence is the contract.
  - **E's are interpretations of evidence under intent.** Passes
    (NullabilityPass, UniqueIndexPass, etc.) consume Policy because their
    job is to apply operator intent to the evidence — produce a decision
    that depends on what the operator chose. Π does not decide; it
    surfaces.

Future Π authors who reach for Policy should pause and ask whether the
work is really projection or really enrichment. If the operator's intent
is shaping the output, the work belongs in a pass; the pass produces
emitter-consumable values (per A32) that Π then surfaces. The denial is
load-bearing.

  *Enforcement.* Type-level — Π modules' `emit` signatures cannot accept
  `Policy`. Compiler-checked by every existing Π:
  `RawTextEmitter.emit : Catalog -> string`,
  `JsonEmitter.emit : Catalog -> string`,
  `DistributionsEmitter.emit : Catalog -> Profile -> string`.
  *Property test.* `A18 amended: emitter signatures take no Policy
  parameter`.

---

## A24 amended (2026-05-22) — chronological-bind extends to the WriterT-stacked dual writer

**Empirical refinement surfaced by chapter-Cluster-B** (per
`HORIZON.md` H-001 / H-002 / H-053-expansion / H-003 shipped commit
`4c1b994`). The original A24 (chronological-bind law) named the
convention for `Lineage<'a>` alone. The amendment makes explicit that
the same law holds for **`Diagnostics<'a>`** and for the **dual writer
`LineageDiagnostics<'a> = Lineage<Diagnostics<'a>>`** — both
operationally, in code, and structurally, in the algebra.

  **For every writer monad over a list-monoid `(L, ++, [])`,
  `bind f m` produces a carrier whose log is
  `m.log ++ (f m.value).log` — earliest-first.** `Lineage` (over
  `LineageEvent list`), `Diagnostics` (over `DiagnosticEntry list`),
  and `LineageDiagnostics` (the dual writer) all satisfy A24
  symmetrically. The dual writer is itself a writer monad over the
  product monoid
  `(LineageEvent list × DiagnosticEntry list, ⊕, ([], []))`;
  A24 holds layer-wise AND at the product level.

The algebraic reasoning, not just the rule:

  - **`LineageDiagnostics` is `WriterT`-stacked.** In monad-transformer
    notation,
    `LineageDiagnostics<'a> = WriterT[LineageEvent] (WriterT[DiagnosticEntry] Identity) 'a`.
    Both layers carry the same shape of monoid `(List, ++, [])`; the
    bind composes both logs chronologically by virtue of the
    underlying monoids' associativity. The dual writer is itself a
    writer monad over the product monoid — equivalent to a
    `Writer<(LineageEvent list × DiagnosticEntry list)>` carrier —
    and `LineageDiagnostics.bind` is the writer's bind under that
    product monoid.

  - **Monad-law preservation under stacking.** The monad-law triple
    (left identity, right identity, associativity) holds for the
    stacked writer because it holds for each layer. The proof flows
    layer-wise: `LineageDiagnostics.bind` is defined as nested
    `Lineage.bind` over `Diagnostics.bind`; each underlying `bind`
    preserves its layer's laws; composition preserves them at the
    product. The dual-writer's laws are not "additionally true" — they
    are *necessarily* true given the layer-wise truth.

  - **A24 is not specific to `LineageEvent`.** The chronological-bind
    law is a property of the writer monad over any list-monoid (or
    more generally, any monoid where the operation is associative,
    has an identity, and the convention is "first arg first" — which
    list-concat satisfies definitionally). The discipline holds for
    every writer the codebase introduces over the same shape.

  - **Why the stacked-writer naming matters.** Without naming the
    stacking, a future agent extending `Diagnostics<'a>` (e.g.,
    splitting into operator / auditor / developer channels per
    `DECISIONS 2026-05-06`) might assume the stacking generates new
    invariants. It doesn't — the new layer inherits A24
    automatically. The amendment makes that inheritance structural.

  - **Kleisli laws are inherited from the stacked monad's laws.** The
    Kleisli arrow type `Pass<'a, 'b> = 'a -> Lineage<Diagnostics<'b>>`
    (H-003) is the Kleisli arrow over the stacked writer. The Kleisli
    category's identity and associativity laws follow from the
    underlying monad's laws — they are not independent claims.
    `Pass.composeAll`'s correctness (the operational shape of
    `PassChainAdapter.compose`) follows from A24-amended applied at
    the dual-writer's bind.

**Operational consequences delivered in chapter-Cluster-B.**

  - The `Diagnostics` monad-law triple (left identity, right identity,
    associativity) tested for the first time in
    `tests/Projection.Tests/DiagnosticsTests.fs`. Previously only
    `bind`'s chronological-concat shape was tested; the laws
    themselves were aspirational.
  - The `LineageDiagnostics` monad-law triple tested for the first
    time in `DiagnosticsTests.fs` via the `byValueAndBothTrails`
    predicate — asserts payload + lineage-trail + diagnostics-entries
    all match under the law's substitution.
  - The Kleisli laws over `Pass<'a, 'b>` (H-003) tested for the first
    time in `DiagnosticsTests.fs`: identity left/right, associativity,
    empty-list = identity arrow, three-step composition threads both
    writers chronologically.
  - The `lineage { ... }` / `diagnostics { ... }` / `lineageDiagnostics
    { ... }` CE builders (H-001 / H-002) are syntactically safe
    because they desugar to law-preserving primitives — every
    `let!` is `Bind`, every `do!` is `Bind` with a `unit` continuation,
    every `return` is `ofValue`. The CE-equivalence property tests
    in `LineageTests.fs` and `DiagnosticsTests.fs` confirm.

  *Enforcement.* The `bind` implementations in `Lineage.fs` and
  `Diagnostics.fs` use `@` (list concat) for both layers' logs.
  `LineageDiagnostics.bind` in `Diagnostics.fs` threads both layers'
  bind via the standard nested form. The `Lineage<'a>` carrier uses
  `[<CustomEquality; NoComparison>]` projecting through `Value` only
  (A26); the bind's algebraic content is the trail concat operation,
  which the test `` ``A24: bind composes trails as m.Trail ++ f.Trail`` ``
  guards directly. The dual-writer's equivalent —
  `` ``A24-equivalent: LineageDiagnostics.bind concatenates both
  trails chronologically`` `` — guards the stacked shape.

  *Property tests.* Monad-law triples on `Lineage` (chapter-3.1) +
  `Diagnostics` (chapter-Cluster-B) + `LineageDiagnostics`
  (chapter-Cluster-B) + Kleisli laws on `Pass<'a, 'b>`
  (chapter-Cluster-B; H-003). Tests are property-based via FsCheck
  over arbitrary integer payloads (the laws are payload-agnostic).

**Writer-monad trinity (chapter-Cluster-B finale; 2026-05-22).** A24
amended characterizes three structurally-related writer carriers, each
satisfying the chronological-bind law:

- **`Lineage<'a>` — the linear writer.** Append-only trail; the
  in-flight carrier every pass returns. A24 is the original law over
  this shape.

- **`LineageTree<'a>` — the branching writer** (H-005; the **free
  monad over the labeled-list functor** applied to `Lineage<'a>`).
  Leaves are `Lineage<'a>` carriers; Forks are labeled lists of
  subtrees. A24 holds within each leaf AND across the substitution
  boundary: when `LineageTree.bind f leaf` substitutes, the existing
  leaf's trail prepends to every continuation leaf in `f m.Value`
  via the `prepend` primitive — preserving chronological ordering
  across the bind. The bind operation is the free-monad bind:
  recursive leaf-substitution that preserves Fork structure. Monad-law
  preservation under the free-monad construction is standard;
  property-tested in `LineageTests.fs::H-005 LineageTree monad: ...`.

- **`Certificate<'a>` — the terminal projection** (H-004). Structural
  isomorphism with `Lineage<Diagnostics<'a>>` via `ofLineageDiagnostics`
  / `toLineageDiagnostics`. The role at the consumer boundary: a
  `Certificate<SsdtBundle>` is a value plus its witness chain, where
  the witness chain satisfies A24-amended by inheritance from the
  isomorphic dual-writer carrier.

The trinity is closed: every writer-carrier role in the pipeline has
a named type. A24-amended holds across all three.

**Future-extensibility note.** When a third writer is introduced (e.g.,
a perf-trace writer separating `Bench` samples from `Lineage` events;
a constraint-set writer for the typed `Tolerance` taxonomy), the
amendment generalizes: stacking the new writer atop
`LineageDiagnostics` inherits A24 by the same construction. New
carriers similarly extend by stacking atop the trinity (e.g., a
`LineageTreeDiagnostics<'a>` would be `LineageTree<Diagnostics<'a>>`,
inheriting branching + dual-writer chronology). The chapter-close
ritual adds the new writer's monad-law triple in the same commit that
introduces the writer; the law tests are template-shaped because the
underlying algebra is uniform.

---

## Operational principle: structural-commitment-via-construction-validation

**Recognized primitive** surfaced by the truncation-contract finding
(DECISIONS 2026-05-12). Across V2's IR, certain invariants the type
system cannot express directly are enforced by `create` smart-constructors
that reject inputs violating the invariant. The invariant becomes
structural rather than runtime — every value that exists carries the
contract because every path to its existence checked it.

Recognized instances:

  - **A4 — Identity equality is structural.** `SsKey` equality is by
    content; `SsKey.original` validates and rejects malformed inputs.
  - **A22 — Snapshots are content-addressed.** Snapshot identity is
    derived; the constructor enforces the derivation.
  - **A34 — Profile independence.** `Profile` references no Catalog or
    Policy types; the type system rejects accidental coupling.
  - **CategoricalDistribution.create (2026-05-12).** `IsTruncated = false
    ⇒ DistinctCount = Frequencies.Length`; `DistinctCount ≥ 0`; per-value
    counts are non-negative. Every constructed value satisfies the
    truncation contract.
  - **NumericDistribution.create (2026-05-13, session 10).** Percentiles
    are monotonically non-decreasing; `Min ≤ P25 ≤ ... ≤ P99 ≤ Max`;
    sample size meets the percentile-set's confidence floor.

The pattern's shape:

1. Identify an invariant a value type ought to carry but cannot express
   purely at the type level.
2. Make the constructor a smart constructor that returns `Result<'a>` and
   rejects every input violating the invariant.
3. Document the invariant on the type so callers know the contract.
4. Consumers downstream pattern-match without re-validating; the
   invariant rides on every value.

Future evidence types that arrive under "IR grows under evidence" should
adopt the pattern as the default. Every distribution variant carries its
own integrity rules; every constructor enforces them; the algebra's
reliability compounds because every value carries its own truth.

  *Convention.* Every smart constructor returning `Result<'a>` is an
  instance of this pattern. Code reviewers can ask "what invariants does
  this `create` enforce?" of any such constructor and expect a complete
  answer.

---

# Conventions and history

The original A1–A31 / T1–T10 numbering reflects the V1 algebraic spec as
read at scaffold time. V2 amendments are listed above with explicit
rationale; new axioms continue the original numbering. Future amendments
should follow the same discipline:

1. Preserve original numbering and original text.
2. Append the amendment under "## A<n> amended (date) — short title" with
   the new statement and the rationale.
3. Append new axioms / theorems by continuing the numbering (A32, A33, ...).
4. Code and test names cite the amended form by default; the original is
   the lineage of the amendment, not the rule.

The axioms have a history. The history is part of how the system tells its
truth across time.

---

# Amendments scheduled (chapter close)

The following amendments are scheduled for commitment at chapter close.
Each chapter agent writes the amendment text at chapter-close ritual
step 6 ("CHAPTER_N_CLOSE.md scope includes AXIOMS amendments"; per
`DECISIONS 2026-05-14`). The scaffolding here is the **placeholder
list** — chapter agents fill the body when the chapter that earns the
amendment closes.

The scaffolding is itself a discipline: pending amendments live as
named placeholders rather than as memory. A chapter agent reading
this section at chapter close knows immediately which amendments
their chapter is responsible for — the alternative (silent obligation
carried in handoff letters) has produced trigger-fire-without-cash-out
at least once (the transform-registry deferral; `DECISIONS 2026-05-13
— Transform registry cash-out`). The placeholder list is a structural
forcing function: chapter close cannot complete without resolving the
placeholders for the chapter it closes.

The scaffolding entries below are appended in the Stage 0 governance
burst (per `DECISIONS 2026-05-22 — Stage 0 foundation phase ships as
one coherent unit`); the bodies fill at the chapters named in each
heading.

## A42 candidate — decision→emission fidelity (Wave 2, 2026-05-30) — CASHED

**CASHED at Wave-2 slices 2.1–2.4 (2026-05-30).** Promoted to the numbered
axiom **A42** (see "A42 — Decision→emission fidelity" in the main axiom
body below). The scaffold remains as the historical placeholder record.

**A42 (candidate).** The emitted DDL is a faithful projection of the
tightening decision sets. Formally, for a `ComposeState` whose decision
sets are projected by `DecisionOverlay.ofComposeState`:
- every `NullabilityOutcome.EnforceNotNull` attribute is emitted `NOT NULL`,
  and only those (additive-only — a non-enforce decision never loosens
  source truth);
- every `UniqueIndexOutcome.EnforceUnique` index is emitted `UNIQUE`, and
  only those;
- every `ForeignKeyOutcome.DoNotEnforce` reference is suppressed (no inline
  FK), and every `EnforceConstraint (ScriptWithNoCheck _)` reference is
  emitted `WITH NOCHECK` (untrusted).

**Observable identity (the 2.2 safety net).** `DecisionOverlay.empty`
threaded through the emitter is byte-identical to pre-overlay emission —
`ofComposeState (ComposeState.initial c) = empty` (proved in
`DecisionOverlayTests.fs`), so emission with no registered interventions is
unchanged.

**A18 ↔ A42 relationship.** A42 is what A18-amended *permits*: the emitter
consumes `DecisionOverlay` (decisions = evidence-derived facts), never
`Policy` (intent). The curried-prefix threading shape keeps the `Emitter`
port `Catalog`-only. The decision was discharged from intent into evidence
by the passes; the emitter projects the fact.

**Underwriting plan.** Slice 2.1 — `DecisionOverlay` + observable identity
(`DecisionOverlayTests.fs`, landed). Slice 2.2 — curried-prefix threading,
byte-identical with `empty`. Slice 2.3 — NOT NULL + UNIQUE application,
canary-proved via the un-hollowed `PhysicalSchema` (Wave 1). Slice 2.4 — FK
gating + NOCHECK. Slice 2.5 — promote candidate → numbered A42 + cash the
`AxiomTests.fs` body + a `PRODUCT_AXIOMS.md` L3 entry.

## A-DataAdjunction candidate — data-level adjunction (Wave 3, 2026-05-30)

**Scaffolded at Wave-3 slice 3.1.** The data sibling of H-050's schema
adjunction. For `PreservedFromSource` rows transferred onto a blank Sink:
`Ingestion(Projection(rows)) = rows` on the **row-digest axis** (per-row
SHA-256 `PhysicalRow` hashes), modulo the named identity remap
(`SurrogateRemapContext` — the one `OperatorIntent Insertion` site). Already
witnessed (Bucket A) by `TransferCanaryTests` — the data canary asserts
Source ≈ Sink on `PhysicalSchema` including per-row hashes after a Transfer.
Promote to a numbered axiom when the Ingestion/Projection legs are stated as a
formal adjoint pair (§V E3 data half). The CDC pre-flight (slice 3.1) guards
the Execute write path this adjunction rides; the R6 `--execute` authorization
is a PROPOSAL pending operator sign-off (`DECISIONS 2026-05-30`).

## T1 amended (binary normal-form composition) — chapter 3.x close (2026-05-11)

**Cashed at chapter 3.x close** (DacpacEmitter + DockerImageEmitter
under dev-tooling reframe; `CHAPTER_3_X_CLOSE.md` item 8).

Same `(catalog, policy, profile)` triple produces:

- **Byte-identical text-emission output.** Text emitters —
  `SsdtDdlEmitter`, `JsonEmitter`, `DistributionsEmitter`,
  `DecisionLogEmitter`, `OpportunitiesEmitter`,
  `ValidationsEmitter`, `StaticSeedsEmitter`,
  `MigrationDependenciesEmitter`, `BootstrapEmitter`,
  `RefactorLogEmitter`, `ManifestEmitter` — all consume the
  typed-AST stream (or `Utf8JsonWriter` + sorted-key `JsonNode`,
  or `XmlWriter` typed AST) via pinned-options writers. T1 holds
  byte-for-byte. Property tests carry the `T1: ... byte-
  deterministic` form.
- **Content-identical DacFx-model binary-emission output.** Binary
  emitter — `DacpacEmitter` — produces `.dacpac` zip bytes that
  embed wall-clock timestamps in `Origin.xml` + zip-entry headers
  via DacFx's `BuildPackage`. Two emit calls on the same Catalog
  produce **non-byte-identical streams** but **content-identical
  DacFx models**: `DacPackage.Load(stream)` →
  `TSqlModel.LoadFromDacpac` → `model.GetObjects(Table.TypeClass)`
  / `(ForeignKeyConstraint.TypeClass)` / `(Index.TypeClass)`
  enumerations match across emit calls. The algebraic claim
  flows through DacFx's model API, not the stream bytes. Property
  tests carry the `T1 (binary): ... content-deterministic under
  DacFx round-trip` form.

The two forms compose. The unifying predicate
`t1ByteEqualOrModelEquivalent` chooses per emitter kind:
byte-equality for text; DacFx-model-content-equality for binary.
The canary's tier-1 property tests assert the right form for each
emitter; the chapter-3.x slice α `T1 (binary): DacpacEmitter.emit
is content-deterministic under DacFx round-trip` is the worked
example.

**Slice ζ (post-hoc `Origin.xml` canonicalization)** can lift
binary emitters to byte-equality if a snapshot consumer demands
byte-stable artifacts (rewrite Origin.xml timestamps to a pinned
value; recompute the embedded model.xml checksum; re-pack with
pinned zip-entry timestamps). The slice stays deferred-with-
trigger at chapter 3.x close — under the dev-tooling reframe, no
consumer demands byte-stable dacpac artifacts. **Trigger to cash
out**: a snapshot consumer demands byte-stable dacpac artifacts
(e.g., a content-addressable artifact store keyed on dacpac SHA256).

Future binary emitters (`RemediationEmitter` per V2_DRIVER §147
free-corollary table; alternative `.dacpac` variants for future
deploy paths) inherit the same shape — DacFx wrapper + content-
equality T1 + slice ζ deferred-with-trigger.

The CDC-safety property (chapter 4.1.B's `idempotentRedeploy`
green at slice γ) operates independently — `T1 × CDC idempotent-
redeploy` is the composition; chapter 4.1.B's MERGE-based shape
holds byte-determinism on text emissions. The chapter-3.x close
doesn't change CDC's algebraic claim; it adds the binary-emitter
amendment that future binary CDC consumers (none exist today)
would inherit.

## T11 amended (structural type encoding) — chapter 3.5 slices α–δ (2026-05-09)

**Cashed at chapter 3.5 Π port realization slice arc** (slices α
[`RawTextEmitter.emitSlices`], β [`JsonEmitter.emitSlices`], γ
[`DistributionsEmitter.emitSlices`], δ [substring-discipline
retirement; type-theorem worked examples at
`tests/Projection.Tests/SiblingEmitterContractTests.fs`]).

**Statement.** The original T11 (sibling-Π commutativity; "every
Π's output should mention every catalog kind by SsKey root") was
a *substring* property — `Assert.Contains(SsKey.rootOriginal
k.SsKey, output)` across emitted text — in `JsonEmitterTests.fs`
and `RichProfilingEndToEndTests.fs`. The amendment encodes T11
**structurally**:

```fsharp
type ArtifactByKind<'element> = private ArtifactByKind of Map<SsKey, 'element>

module ArtifactByKind =
    let create
        (catalog: Catalog)
        (slices: Map<SsKey, 'a>)
        : Result<ArtifactByKind<'a>, EmitError> = ...
    // Smart constructor enforces strict equality between the
    // slice's keyset and `Catalog.allKinds`'s SsKey set.
    // Missing key → `KindNotProduced`; extra key → `UnexpectedKind`.

type Emitter<'element> =
    Catalog -> Result<ArtifactByKind<'element>, EmitError>
```

T11 holds **by construction**: any two `ArtifactByKind` values
built from the same Catalog have equal keysets, because the smart
constructor refuses to build either with a divergent keyset. Two
sibling Π's running on the same enriched Catalog produce
`ArtifactByKind` values with `Set.equal (keys a) (keys b)`,
regardless of per-element type. The substring discipline retires
because the type proves what the substring tested.

**Operational consequences.**

- `RawTextEmitter.emitSlices : Emitter<Statement list>` produces
  per-kind statement slices keyed by SsKey.
- `JsonEmitter.emitSlices : Emitter<string>` produces per-kind
  JSON-text slices keyed by SsKey (the per-element type is
  `string` for first slice; `JsonObject` ladders up under
  consumer pressure per the chapter-open §8 two-consumer
  threshold).
- `DistributionsEmitter.emitSlices : EmitterWithProfile<string>`
  is the first realization of `EmitterWithProfile<'element>`
  (`Types.fs:55`); the same `ArtifactByKind` smart constructor
  enforces T11 on the Profile-consuming variant.
- Legacy `emit` realizations route through the typed seam — the
  `ArtifactByKind` smart constructor is the canonical path; an
  `Error` from it is a structural invariant breach surfaced as
  `invalidOp` at the realization site (unreachable when fed
  `Catalog.allKinds`'s own keys).
- Substring T11 enforcement at `JsonEmitterTests.fs:96-105` and
  `RichProfilingEndToEndTests.fs:280-289` retires; the
  type-theorem worked examples at `SiblingEmitterContractTests.fs`
  replace them. The surviving `T4` and `T11: physical
  realization` tests test rendering invariants, not kind
  coverage — they stay.

**Verification surface.** `SiblingEmitterContractTests.fs` carries four
worked examples — three per-emitter `emitSlices key-set equals
Catalog.allKinds` tests + one cross-emitter sibling-commutativity
test (`RawText`, `Json`, `Distributions` keysets pairwise equal).
`ArtifactByKindTests.fs` carries the rejection-direction (smart
constructor refuses missing / extra keys with the named
`KindNotProduced` / `UnexpectedKind` error variants). Together
these close the T11 verification surface.

**Companion amendment scheduled.** `T11 amended again (diff-typed
inputs)` extends the type theorem to `EmitterOverDiff<'element> =
CatalogDiff -> Result<ArtifactByKind<'element>, EmitError>` when
chapter 3.5's substantive deliverable (`RefactorLogEmitter` over
`CatalogDiff`) lands. Same discipline; the keyset is bound to
the diff's SsKey set rather than the source Catalog's.

## T11 amended again (diff-typed inputs) — chapter 3.5 slices θ–ι (2026-05-09)

**Cashed at chapter 3.5 substantive deliverable** (slices θ
[`RefactorLogEmitter : EmitterOverDiff<RefactorLogEntry list>`], ι
[`RefactorLogRender.toRefactorLogXml`]; per
`CHAPTER_3_PRESCOPE_REFACTORLOG_AND_CATALOG_DIFF.md`).

**Statement.** Chapter 3.5 introduces `EmitterOverDiff<'element> =
CatalogDiff -> Result<ArtifactByKind<'element>, EmitError>` —
emitters whose input is a Catalog-typed diff rather than a Catalog.
T11's sibling-Π commutativity extends: the diff-typed emitter's
output `ArtifactByKind` keys are typed over the diff's *target*
Catalog. **`RefactorLogEmitter.emit` flows
`Catalog.allKinds (CatalogDiff.target diff)` through
`ArtifactByKind.create`'s strict-equality smart constructor**;
every key in the target Catalog appears in the artifact, with
possibly empty per-key payload (`Unchanged`/`Added`/`Removed` kinds
carry empty `RefactorLogEntry list`; `Renamed` kinds carry exactly
one entry).

The pattern is monotonic. Every new emitter type variable
(`Emitter`, `EmitterWithProfile`, `EmitterOverDiff`, future
variants) is a specialization of the same `ArtifactByKind` shape;
T11 holds for each by the same `Catalog`-vs-target-Catalog binding
through the smart constructor. The amendment names the structural
extension: **T11 commutativity is inherited by every Π-shape that
parameterizes a `Catalog` through `ArtifactByKind.create`.**

**Verification surface.** `RefactorLogEmitterTests.fs:T11 (diff-
typed inputs)` confirms `ArtifactByKind.keys artifact = Set
(Catalog.allKinds target |> List.map _.SsKey)`. The smart
constructor's rejection direction is at `ArtifactByKindTests.fs`
(unchanged from chapter-3.5 cross-cutting close). T11 holds
across four Π's now: RawText / Json / Distributions /
RefactorLog.

**Companion amendment.** `A38 — CatalogDiff exhaustiveness` cashes
in the same chapter close (the diff-side of the same structural
commitment). The chapter 3.5 close ships both amendments together
because the operational shape is one — diff-typed Π over
exhaustively-partitioned diff inputs.

## A1 amended (four-variant SsKey) — Stage 0 + chapter 3.5 (2026-05-09)

**Cashed at Stage 0 (S0.B slice 5.5) and chapter 3.5 substantive
deliverable.** The four-variant `SsKey` DU shipped at Stage 0
(`Identity.fs`); chapter 3.5's `RefactorLogEmitter` is the first
substantive consumer that pattern-matches on the variants for
identity-survives-rename. Per `CHAPTER_3_PRESCOPE_ARTIFACTBYKIND_REFACTOR.md`
slice 5 and Stage 0 item S0.B.

**Statement.** A1's bound on identity-survives-rename through the
JSON path (added session 23; documented at the bottom of A1) names
the JSON-projection-lossiness class — V1's `osm_model.json` strips
SSKey columns, so V2's IR synthesizes SsKeys from name fields rather
than carrying V1's actual SSKey values. The four-variant DU split
codifies the bound **type-stratifically**:

```fsharp
type SsKey =
    | OssysOriginal of System.Guid                       // V1's SSKey, carried as Guid
    | Synthesized of source: string * basis: string      // V2-synthesized from name fields
    | DerivedFrom of parent: SsKey * reason: string      // pass-introduced
    | V1Mapped of v1Sskey: System.Guid * v2Namespace: System.Guid  // cross-version
```

A1's identity-survives-rename guarantee splits along the variants:

- `OssysOriginal g` — honors A1 unconditionally. The V1 SSKey Guid
  is the stable identity; rename changes `Name`, never `SsKey`.
- `Synthesized (source, basis)` — honors A1 **only over name
  preservation**. The synthesis basis IS the name; a rename
  produces a new `Synthesized` key. This is the JSON-projection-
  lossiness bound made structural — pattern-matching code can
  refuse to claim A1 holds for `Synthesized` inputs without further
  evidence.
- `DerivedFrom (parent, reason)` — honors A1 *transitively* through
  the parent. `SsKey.rootOriginal` walks the chain; the leaf's
  variant determines whether A1 is conditional or unconditional.
- `V1Mapped (v1Sskey, v2Namespace)` — honors A1 *within V2*: the
  `v1Sskey` Guid was stamped onto a deployed schema by V1 and read
  back; the `v2Namespace` makes the cross-version origin pattern-
  matchable. UUIDv5 derivation (`UuidV5.create`) is the canonical
  cross-version threading — chapter 4.2 User FK reflow makes the
  variant reachable from production.

**Operational consequences.**

- `SsKey.rootOriginal` is the legacy diagnostic-string accessor;
  preserves emitter-comment text at the V1-prefix form
  (`OS_KIND_<basis>` for `Synthesized "OS_KIND" basis`). Chapter
  3.5 audit Tier-3 considered renaming this surface; deferred per
  `HANDOFF.md`'s "needs DECISIONS amendment first" entry.
- `SsKey.identityKey` (chapter 3.5 candidate; not yet shipped — the
  deterministic Guid projection for diff-time cross-version
  comparison) earns its place when a real consumer (cross-version
  identity threading) demands it; chapter 4.2 territory.
- Adapters use `SsKey.synthesized` directly; `SsKey.original` is
  the back-compat shim for pre-stratification call sites.

**Verification surface.** `SsKeyTests.fs` carries variant-equality
worked examples; the Stage 0 inhabitation tests at `TypesTests.fs`
witness the seam shape. Cross-variant equality is always false
(provenance-preserving); within-variant equality is structural
(records / guid / strings).

**Companion amendment scheduled.** Cross-version identity threading
via `V1Mapped` cashes operationally when chapter 3.2 (`SnapshotRowsets`)
produces `OssysOriginal` Guids at scale and chapter 4.2 (User FK
reflow) reads V1-stamped extended properties. Chapter 3.5's
`RefactorLogEmitter` is bound: its diff-comparison rests on
`SsKey` structural equality, which honors variant tags; the
cross-version reach (V1-deployed kind ↔ V2-emitted kind via
`identityKey`) opens at chapter 4.2.

## A35 — Π's output is a deterministic statement stream (chapter-3.1 close, 2026-05-30)

**Cashed at chapter 3.1 close** (per `DECISIONS 2026-05-28 — Session
34 / A35 cash-out: Π's output is a deterministic statement stream`).

**A35.** Π's canonical output is a typed deterministic stream
(`seq<Statement>` for SSDT). Realization layers (`Render.toText`,
`Deploy.executeStream`) consume the stream and choose their
emission form. Bulk-vs-per-row deploy is realization-layer policy
invisible to Π.

**Statement of the axiom.** For any sibling Π producing structured
output, Π's canonical form is a typed stream `seq<'element>` (or
analogous typed iterator). Realizations are functions from the
stream to a target representation: `fold : seq<'element> ->
'output`. Two realizations of the same stream produce the same
observable post-state up to the realization's tolerance. T1
strengthens to *statement-stream determinism* — identical Catalog
produces identical Statement sequence; `Render.toText` produces
identical bytes from identical streams.

**Worked example.** `RawTextEmitter.statements : Catalog ->
seq<Statement>` is the canonical form. `Render.toText : seq<Statement>
-> string` produces .sql text. `Deploy.executeStream : SqlConnection
-> seq<Statement> -> Task<unit>` produces the deployed target
database. Both consume the same stream; both witness the same
algebra; T1 holds at the stream level.

**Implications.** A18 (Π consumes Catalog × Profile, never Policy)
holds at the stream level — the realization layer chooses bulk-vs-
per-row but cannot reach for Policy. T11 (sibling-Π commutativity)
strengthens via `ArtifactByKind<'element>` for emitters that adopt
the typed structured form (chapter 3.5 Π port realization completes
T11 structurally). Future Π's inherit the stream-output pattern as
their typed canonical form.

## A36 — Bulk-vs-incremental is realization-layer policy (chapter-3.1 close, 2026-05-30)

**Cashed at chapter 3.1 close** (per `DECISIONS 2026-05-28 — Session
34 / A36 cash-out: bulk-vs-incremental is realization-layer policy`).

**A36.** How a realization layer deploys a statement stream — bulk
via `SqlBulkCopy`, per-row via `INSERT INTO … VALUES (…)`, file-
artifact write, network protocol — is *realization-layer policy*
invisible to Π. The algebra at the stream level is invariant under
realization choice.

**Statement of the axiom.** Given a typed stream `seq<'element>`
produced by Π, two realizations `f, g : seq<'element> -> 'output`
that share the same observable post-state contract for `'output`
are equivalent at the algebra level. Π does not know which
realization will consume its output; consumers do not need to know
how Π produced its stream. The stream is the contract.

**Worked example.** `Deploy.executeStream` folds consecutive
`InsertRow` runs (matching `(TableId, columnShape)`) into
`SqlBulkCopy` batches. The bulk path produces the same observable
post-state (rows-in-database) as a per-row INSERT realization
would. Π is invariant under the choice.

**Implications.** Realization-layer optimization is decoupled from
algebra correctness. Bulk-path tuning (`DefaultBulkBatchSize`,
batch-fold heuristics) lives in `Deploy.executeStream` and
`Bulk.copyRows`; T1 byte-determinism rests on stream determinism,
not realization-byte determinism. Future realizations (chapter
4.1's BCP/TVP, chapter 3.x DacpacEmitter's binary form) inherit the
A36 framing — choose your realization; the algebra holds.

## A37 candidate (Π-erased axes named) — TBD chapter 3.4 close

**Scheduled at chapter 3.4 close** (Canary as property-test surface).
Renumbered from A35 candidate at chapter-3.1 close; the original
A35 slot was claimed by session-34's stream-output axiom.

[Body to be written at chapter 3.4 close; promoted from candidate to
A37 if it earns commitment.]

**Anticipated content.** The DACPAC round-trip cannot preserve every
axis of the Catalog — index names, check constraint names, default
constraint names, extended properties, header comments, and several
other axes are erased or normalized by DacFx's model layer. The
canary's `equalModuloDacpacErasure` predicate declares the erasure
set explicitly; A37 candidate names the declaration **the function
IS the axiom**.

The candidate's promotion criterion: **A37 lands as an axiom if and
only if every Π whose output is binary requires the same erasure
declaration** (i.e., the axiom is invariant across Π's, not specific
to DACPAC).

## A38 — CatalogDiff exhaustiveness (chapter 3.5 substantive deliverable, 2026-05-09)

**Promoted from candidate to A38 at chapter 3.5 close.** Renumbered
from A36 candidate at chapter-3.1 close; the original A36 slot was
claimed by session-34's realization-layer-policy axiom. The
`CatalogDiff` smart constructor (slice ζ) is the load-bearing
surface; `RefactorLogEmitter` (slice θ) is its first consumer.

**Statement.** `CatalogDiff` is a private DU produced by `DiffOf
<Catalog> = Catalog -> Catalog -> Result<CatalogDiff, EmitError>`.
Its smart constructor `CatalogDiff.between` enforces an
**exhaustiveness invariant**: every `SsKey` in `Catalog.allKinds
source ∪ Catalog.allKinds target` is in **exactly one** of four
pairwise-disjoint partitions — `Renamed`, `Added`, `Removed`,
`Unchanged`. Coverage and disjointness hold by construction
(`Set.difference` / `Set.intersect` produce disjoint partitions;
`Set.fold` over the intersection partitions further into renamed-
vs-unchanged by `Name` equality).

```fsharp
type CatalogDiff = private CatalogDiff of CatalogDiffData
and CatalogDiffData = {
    Source    : Catalog
    Target    : Catalog
    Renamed   : Map<SsKey, RenameRecord>
    Added     : Set<SsKey>
    Removed   : Set<SsKey>
    Unchanged : Set<SsKey>
}
```

The wrapping `private CatalogDiff of CatalogDiffData` mirrors
`ArtifactByKind`'s smart-constructor discipline: callers cannot
construct it without going through `CatalogDiff.between`; the
type cannot inhabit an inconsistent state.

**Operational consequences.**

- A diff-typed Π consumes `CatalogDiff` (per `EmitterOverDiff
  <'element>`) and produces an `ArtifactByKind` keyed on the
  diff's *target* Catalog. T11 amended again (diff-typed inputs)
  is the structural sibling — the diff's exhaustiveness invariant
  is the input-side guarantee; T11 is the output-side guarantee.
  Together they encode "the algebra holds end-to-end" through
  the diff-typed pipeline.
- A1 amended's per-variant rename semantics extends naturally:
  `Renamed` keys carry both `OldName` and `NewName`; the variant
  tag of the SsKey is preserved so cross-version semantics
  (chapter 4.2 territory via `V1Mapped`) inherit honestly.
- The first-slice scope is kind-level (`Catalog.allKinds`'s SsKey
  set). Attribute-level diffing extends the helper `allKindKeys`
  to `allFlatKeys` walking the kind tree (kinds + attributes +
  references + indexes), without changing the smart constructor's
  partition shape — the closed-DU expansion empirical-test
  discipline applies.

**Verification surface.** `CatalogDiffTests.fs` carries 9 worked
examples + properties: identity diff, empty-source, empty-target,
scope = disjoint-union, pairwise disjointness, determinism,
module-permutation invariance, the four `partitions are pairwise
disjoint` cross-check.

**Big-O.** O(N log N) where N = |source ∪ target|.
`Catalog.allKinds` O(N), `Set.ofList` O(N log N), `Set.difference`
/ `Set.intersect` O(N log N), `Set.fold` over intersection
O(N log N) with O(log N) `Catalog.tryFindKind` lookups.

## A39 — Aggregate-root smart-constructor invariants (chapter-3.1 close, 2026-05-30)

**Promoted from candidate to A39 at chapter-3.1 close** (per
`DECISIONS 2026-05-30 — Session 36 / Aggregate smart constructors`).

**A39.** Aggregate roots in the IR — `Catalog`, `Module`,
`ColumnProfile` — carry their referential-integrity and empirical-
probe invariants at the smart-constructor surface. Consumers that
flow through `create` trust the value; consumers that re-validate
are pattern-matching on a violation that should have been
impossible to construct.

**Statement of the axiom.** Each aggregate-root type `T` exposes a
smart constructor `T.create : … -> Result<T>` enforcing the type's
invariants in one pass with errors aggregated. Direct record-literal
construction continues to work for back-compat, but the invariant
holds only when consumers flow through `create`.

**Worked example.** `Catalog.create : Module list ->
Result<Catalog>` enforces five invariants (Module SsKeys disjoint;
Kind SsKeys disjoint; `Reference.SourceAttribute` ∈ Kind.Attributes;
`Reference.TargetKind` ∈ Catalog; `Index.Columns` ⊆ Kind.Attributes).
`Module.create` enforces within-module disjointness.
`ColumnProfile.create` enforces `0 ≤ NullCount ≤ RowCount`.

**Implications.** `RawTextEmitter.fkDef` /
`PhysicalSchema.toPhysicalForeignKeys` previously each silently
dropped on dangling references; the invariant now lives with the
type, not in the consumer. Future aggregate-root introductions
inherit the smart-constructor discipline.

## A40 — Harmonization-via-parameterization (chapter-3.1 close, 2026-05-30)

**Promoted from candidate to A40 at chapter-3.1 close** (per
`DECISIONS 2026-05-30 — Session 36 / Topological-sort harmonization
via SelfLoopPolicy`).

**A40.** When two implementations of an algorithm diverge on a
single semantic axis, the resolution is to *parameterize the
algorithm on that axis*, produce both projections from a single
implementation, and let consumers choose. Same algorithm; multiple
projections; consumers choose.

**Statement of the axiom.** Given two implementations `f₁, f₂ : T
-> U` that produce the same `U` for inputs where their divergence
axis doesn't apply, the harmonized form is `f : Policy -> T -> U`
with `f Policy₁ ≡ f₁` and `f Policy₂ ≡ f₂`. The two implementations
collapse to one parameterized algorithm; the divergence axis is
named structurally as a `Policy` DU.

**Worked example.** `TopologicalOrderPass.runWith :
SelfLoopPolicy -> Catalog -> Lineage<TopologicalOrder>` where
`SelfLoopPolicy = TreatAsCycle | SkipSelfEdges`. The pass had
the cycle-handling implementation; `RawTextEmitter.emissionOrder`
had the skip-self-edges implementation. Both became one
parameterized pass; emitter consumes `runWith SkipSelfEdges`;
existing cycle-detecting tests consume `run` =
`runWith TreatAsCycle`.

**Implications.** A33 (deterministic-ordered schema emission) is
satisfied structurally — same algorithm, two projections.
Future single-axis-divergent implementations earn one
parameterized algorithm before duplicating. Promotion criterion:
A40 lands when the harmonized form replaces N≥2 duplicate
algorithms (chapter-3.1 satisfies N=2; chapter-4 may surface
another instance).

## A41 — Transform registry totality + canonical strongly-typed shape (chapter A.4.7 close, 2026-05-16)

**Promoted from candidate to A41 at chapter A.4.7 close** (per
`DECISIONS 2026-05-16 (chapter A.4.7 close)`). Underwrites
`PRODUCT_AXIOMS.md` L3-CC-Transform-Totality (D → A at chapter
A.4.7 close).

**A41.** Every transformation site in V2 is enumerated as a
`RegisteredTransform<'In, 'Out>` value (for callable pass-stage
sites) or a `RegisteredTransformMetadata` value (for adapter / strategy
sites that aren't independently callable in the canonical
`Catalog -> Lineage<Diagnostics<...>>` shape) in
`Projection.Core.TransformRegistry`. The registry is canonical
for the transformation's metadata + (where applicable) the typed
transformation function definition; **no parallel enumeration** —
each module's primary public surface is its `.registered` /
`.registeredMetadata` export. The type system:

```fsharp
type StageBinding   = Adapter | Pass | OrderingPolicy | Emitter | Pipeline
type OverlayAxis    = Selection | Emission | Insertion | Tightening | Ordering
                      // = Policy DU axes + Ordering (Q9-trigger-fires worked example)
type Classification = DataIntent | OperatorIntent of OverlayAxis
type TransformSite  = { SiteName : string ; Classification : Classification ; Rationale : string }
type TransformStatus = Active | NotImplementedInV2 of rationale: string
type RegisteredTransform<'In, 'Out when 'Out : equality> = {
    Name : string
    Domain : Domain                       // Schema | Data | Identity | Diagnostics | CutoverSafety | CrossCutting
    StageBinding : StageBinding
    Sites : TransformSite list           // intra-pass classification fidelity (per DECISIONS 2026-05-15 (late) Q11)
    Run : 'In -> Lineage<Diagnostics<'Out>>
    Status : TransformStatus
}
type RegisteredTransformMetadata = { Name; Domain; StageBinding; Sites; Status }
                                  // type-erased projection (drops Run); the registry's enumeration form
```

**Bidirectional contract.** A18 amended forbids `Policy` in
emitters (the Π-side commitment by structural type). A41 enumerates
every transformation site that DOES consume operator-supplied intent
(Policy / rename specs / config overrides / SelfLoopPolicy / etc.) so
the dichotomy holds bidirectionally:

- **Skeleton-purity** (axiom statement): every `LineageEvent`
  emitted under the skeleton view of the registry carries
  `Classification = DataIntent`. The skeleton view filters to
  entries whose every `Site` is `DataIntent`; an operator-intent
  leak fails the property.
- **Overlay-exercise** (axiom statement): every `OverlayAxis`
  value reachable from `OperatorIntent _` in some registered Site
  fires in at least one canary scenario. A dead overlay
  (registered but never exercised) fails the property.
- **Totality coverage** (axiom statement): every transformation
  site in `src/Projection.Core/Passes/*.fs` +
  `src/Projection.Adapters.Osm/CatalogReader.fs` +
  `src/Projection.Core/Strategies/*.fs` has a corresponding
  registry entry. A missing entry fails the property.
- **Harvest-classification coverage** (axiom statement): every
  v1↔v2 transformation gap that V2 chose not to bring forward
  ships as a triple deliverable — Skip test stub citing the
  classification rationale + `Tolerance.fs` entry + registry
  entry with `Status = NotImplementedInV2 of rationale`. A
  Tolerance entry without a registry mirror fails the property.

**Worked example.** Chapter A.4.7 ships the registry at
`src/Projection.Core/TransformRegistry.fs`, classifies
12 passes (slice γ) + 1 adapter (slice δ) + 5 strategies (slice
ε) = 18 transformation sites, and proves the bidirectional
contract via `TransformRegistryCompletenessTests` (4 property
tests + 3 intentional-fail probes at slice θ). The fifth
property — manifest digest round-trip — deferred-with-trigger to
slice η (CLI + manifest extension) per consumer-pressure
principle.

**Implications.**

- **A18 ↔ A41 sibling commitment.** Together they reify the
  data-intent / operator-intent dichotomy as a type-witnessed
  bidirectional contract, not a one-sided convention. A18 is the
  Π-side commitment (no `Policy` in emitters by structural type);
  A41 is the Pass-side commitment (registry totality enumerates
  every operator-intent site by structural type + property test).
- **Pillar 9's structural pair.** Pillar 9 (harvest-dichotomy
  classification) is the meta-discipline at consideration time;
  A41 is the structural commitment that catches its failures. The
  meta-discipline-with-structural-test pattern (pillar 8 +
  pillar 7 amendment + text-builder-as-first-instinct + pillar 9)
  holds across all four meta-disciplines after chapter A.4.7
  close.
- **Fourth cross-cutting concern.** The `TransformRegistry` joins
  Lineage / Diagnostics / Bench as the fourth cross-cutting
  structural-evidence concern. Each plugs into every stage that
  has its kind of activity; each is enforced structurally; each
  has its own writer/observer primitive. Future agents reading
  `V2_DRIVER.md` per-axis stakes table see this four-concern
  framing as canonical.
- **Heterogeneous output types are honored** (per `DECISIONS
  2026-05-16 (chapter A.4.7 slice γ)`). The canonical spec's
  "unified `Catalog → Catalog`" assumption was refined at
  implementation: passes produce six distinct output types
  (Catalog, TopologicalOrder, NullabilityDecisionSet, etc.); the
  `RegisteredTransform<'In, 'Out>` type-parameter shape carries
  the heterogeneity. The metadata-only `RegisteredTransformMetadata`
  view drops Run for non-callable sites (adapter rules, strategies)
  — same registry enumeration; explicit type erasure.
- **OverlayAxis ⊃ Policy DU axes** (per `DECISIONS 2026-05-16
  (chapter A.4.7 slice β)`). The fifth variant `Ordering` ships
  at slice β as the Q9-trigger-fires worked example
  (`TopologicalOrderPass.SelfLoopPolicy` is the named real-evidence
  trigger). Future fifth-variant expansions require the same
  trigger-fires discipline. The `Policy.fs` ↔ `OverlayAxis`
  structural-collapse refactor stays deferred-with-trigger
  (consumer pressure when call-sites consult both vocabularies).

## A41 amendment (execution totality) — chapter A.4.7' close (2026-05-17)

**Cashed at chapter A.4.7' close** (per `CHAPTER_A_4_7_PRIME_CLOSE.md`
ritual step 6). Amends A41 from metadata totality (registry
enumeration; chapter A.4.7 close) to metadata + execution totality
(registry-driven traversal). Underwrites
`PRODUCT_AXIOMS.md` L3-CC-Transform-Totality (Bucket A; underwriting
tightens from metadata-shape to metadata + execution fidelity).

**A41 amended.** Every transformation site in V2 is enumerated as
a `RegisteredTransform<'In, 'Out>` value (per A41 original) AND
the registered chain steps are folded by the production composer
(`Projection.Pipeline.Compose.project`) as the canonical execution
loop. Bypassing the registry is structurally impossible because
the hand-coded pass sequence has retired:

```fsharp
// Pipeline.fs — load-bearing
let project (catalog: Catalog) : Outputs =
    projectFromChain RegisteredTransforms.allChainSteps catalog
```

where `projectFromChain` is the registry-driven traversal kernel,
shipping:

```fsharp
type PassChainAdapter = {
    Name : string
    Apply : ComposeState -> Lineage<Diagnostics<ComposeState>>
}

module PassChainAdapter =
    val liftCatalogPass : RegisteredTransform<Catalog, Catalog> -> PassChainAdapter
    val liftDecisionPass :
        RegisteredTransform<Catalog, 'Decision>
        -> ('Decision -> ComposeState -> ComposeState)
        -> PassChainAdapter
    val compose : PassChainAdapter list -> ComposeState -> Lineage<Diagnostics<ComposeState>>

module RegisteredTransforms =
    val all : RegisteredTransformMetadata list      // 17 Core-resident
    val allChainSteps : PassChainAdapter list       // 12 production passes
    val skeletonChainSteps : PassChainAdapter list  //  4 pure-DataIntent
```

**`ComposeState` aggregate solves heterogeneous output types.** The
chapter-A.4.7-close-named blocker — 12 passes split 6-and-6 across
`Lineage<Catalog>`-returning (chainable) vs `Lineage<'DecisionSet>`-
returning — resolves via a unified `ComposeState` record carrying
the Catalog under transformation + `Option<'DecisionSet>` fields
for every decision-set producer (TopologicalOrder /
NullabilityDecisionSet / UniqueIndexDecisionSet /
ForeignKeyDecisionSet / CategoricalUniquenessDecisionSet /
UserRemapContext). Type erasure happens at the adapter boundary
(`liftCatalogPass` / `liftDecisionPass`), not in
`RegisteredTransform<'In, 'Out>` itself; the typed `Run` field
stays intact on each pass module's `.registered` export.

**Bidirectional contract extends to runtime.** The five
bidirectional property tests now hold at runtime:

1. **Skeleton-purity at filter-shape** (chapter A.4.7 slice θ):
   every Site in `TransformRegistry.skeletonView all` carries
   `Classification = DataIntent`. Preserved.

2. **Skeleton-purity at true-execution** (chapter A.4.7' slice ε,
   NEW): `Compose.runSkeleton` against a representative Catalog
   emits zero `LineageEvent` with `Classification = OperatorIntent _`.
   Promotion from filter-shape to runtime structural enforcement.

3. **Overlay-exercise** (chapter A.4.7 slice θ): every `OverlayAxis`
   reachable from `OperatorIntent _` in `RegisteredTransforms.all`
   fires in at least one canary scenario. Preserved.

4. **Totality coverage** (chapter A.4.7 slice θ): every
   transformation site in `src/Projection.Core/Passes/*.fs` +
   `src/Projection.Adapters.Osm/CatalogReader.fs` +
   `src/Projection.Core/Strategies/*.fs` has a registry entry.
   Preserved.

5. **Registry-digest round-trip** (chapter A.4.7' slice ζ, NEW):
   `TransformRegistry.digest RegisteredTransforms.all` is stable
   across emits; perturbing a single Sites.Rationale changes the
   digest; the manifest carries `registry.digest` for downstream
   audit. Reproducibility + sensitivity halves of the round-trip
   contract.

**Canonical-surface-only discipline.** Chapter A.4.7' slice η
retires each pass module's parallel-exposure transition affordance:
`let run` is now `let private run` in all 12 pass modules; the
public callable is exclusively `.registered.Run`. ~308 call-site
migration completed via per-test-file shape-restoring shims
(test-private; not a module surface) + sed-based bulk substitution.
Production sites (Pipeline.fs:applyRenames) migrated directly with
the new Diagnostics-Errors-to-ValidationError boundary projection.

**Implications.**

- **L3-CC-Transform-Totality underwriting tightens** from metadata
  totality to metadata + execution totality. Bucket A holds with
  stronger structural backing: every pass observable in
  `RegisteredTransforms.all` actually fires through
  `Compose.project`'s fold; a pass that bypasses the registry
  bypasses the production emit path entirely.

- **A18 ↔ A41 sibling commitment, extended.** A18 amended forbids
  `Policy` in emitters (Π-side commitment). A41 (original)
  enumerated operator-intent sites at metadata. A41 (amended) makes
  the enumeration *executable*: the same registry value that
  enumerates the surface is the same value the composer folds.
  Bypassing the type-witnessed surface requires bypassing the
  composer itself.

- **Pillar 9 structural pair, extended.** Pillar 9's
  meta-discipline ("classify every transformation site at harvest
  time") is now paired with both metadata totality (A41 original)
  and execution totality (A41 amended). Misclassification leaks
  surface bidirectionally — through the metadata enumeration AND
  through the runtime trail emitted by the fold.

- **`osm emit --skeleton-only` CLI surface.** The
  skeleton-friendly baseline is reachable as an operator-facing
  toggle (`runEmitSkeletonOnly` in `Projection.Cli/Program.fs`);
  binary toggle per the consumer-pressure principle (per-OverlayAxis
  flags remain deferred-with-trigger).

**Carbon-copy events.** Zero across the chapter. The registry
fold + skeleton view + digest are V2-native primitives with no
V1 precedent; `BACKLOG.md`'s V1 inheritance log remains empty for
this chapter. The chapter shipped 8 substantive slices (α / β / γ /
δ / ε / ζ / η.1 partial / η complete) plus the close commit.

**Worked example: `Compose.runSkeleton` true-execution.** Per
chapter A.4.7' slice ε:

```fsharp
let runSkeleton (catalog: Catalog) : Lineage<Diagnostics<ComposeState>> =
    use _ = Bench.scope "compose.runSkeleton"
    PassChainAdapter.compose
        RegisteredTransforms.skeletonChainSteps
        (ComposeState.initial catalog)
```

`skeletonChainSteps` is derived by joining `allChainSteps` against
`TransformRegistry.skeletonView all` on Name; resolves to four
entries (CanonicalizeIdentity, NamingMorphism,
NormalizeStaticPopulations, SymmetricClosure — the four passes
whose every Site carries `Classification = DataIntent`). The
property test (`SkeletonPurityTests.fs`) asserts the trail emitted
by this fold carries zero `OperatorIntent _` events on a
representative Catalog.

## A42 — Decision→emission fidelity (Wave 2 close, 2026-05-30)

**Promoted from candidate to A42 at Wave-2 slices 2.1–2.4** (the
candidate scaffold under "Amendments scheduled" is now cashed).
Underwrites `PRODUCT_AXIOMS.md` L3-S11 (decision→emission fidelity).

**A42.** The emitted SSDT DDL is a faithful projection of the three
tightening decision sets. With the decisions projected by
`DecisionOverlay.ofComposeState` (an A18-safe value — decisions are
evidence-derived facts, never `Policy`), the emitter applies them
**additively**:

- every `NullabilityOutcome.EnforceNotNull` attribute is emitted
  `NOT NULL`, and only those — `Nullable = source.IsNullable ∧ ¬enforce`
  (a non-enforce decision never loosens a source `NOT NULL`);
- every `UniqueIndexOutcome.EnforceUnique` index is emitted `UNIQUE`,
  and only those — `IsUnique = source.IsUnique ∨ enforce` (a non-enforce
  decision never un-uniques a source-unique index);
- every `ForeignKeyOutcome.DoNotEnforce` reference is suppressed (no
  inline FK), and every `EnforceConstraint (ScriptWithNoCheck _)`
  reference is emitted `WITH NOCHECK` (untrusted).

The additive encoding (`∧ ¬enforce` / `∨ enforce`, never `= decision`)
is structural: it makes A42 a *tightening* axiom — emission can only add
constraint, never remove source truth.

**Observable identity.** `DecisionOverlay.empty` threaded through the
emitter is byte-identical to pre-Wave-2 emission
(`ofComposeState (ComposeState.initial c) = empty`;
`DecisionOverlayTests.fs` + the `statementsWith empty = statements`
seam tests in `AdjunctionLawTests.fs`). The seam opens without changing
bytes; only a populated overlay changes emission.

**A18 ↔ A42.** A42 is what A18-amended *permits*: the emitter consumes
`DecisionOverlay` (decisions) as a curried prefix argument, never
`Policy` (intent). The intent was discharged into evidence by the passes;
the emitter projects the resulting fact. The `Emitter` port stays
`Catalog`-only (`statements` / `emitSlices` are the `empty`-default
wrappers; `statementsWith` / `emitSlicesWith` carry the overlay).

**Verification (the un-hollowed canary pays off).**
- Emission-layer (pure, `DecisionEmissionTests.fs`): emit `statementsWith
  overlay` and read the typed `CreateTable` / `CreateIndex` —
  NOT NULL / UNIQUE / FK-drop / FK-nocheck land only for the keyed
  elements; FsCheck "every EnforceNotNull NOT-NULLs its column, and only
  those."
- Canary-layer (Docker, `CanaryRoundTripTests.fs`): `EnforceNotNull`
  survives emit → deploy → ReadSide as `NOT NULL`; a `DoNotEnforce` FK
  decision keeps the FK out of the deployed schema
  (`PhysicalSchema.ForeignKeys` empty). Wave-1's un-hollowing is the
  precondition — the canary can observe the decision reached the
  deployed schema on the Nullable / ForeignKeys axes.

**FK silent-drop witness (slice-μ retired at 2.5b).** When `fkDef`
returns `None` (the inline FK is dropped) the loss is no longer silent:
`SsdtDdlEmitter.foreignKeyDropDiagnostics` — a **pure sibling** of the
emitter port (the witness rides the `Diagnostics` channel, so
`statements`/`emitSlices` stay `Catalog`-only and byte-identical; A18
holds) — produces a `Warning` per drop, distinguishing
`targetMissingPrimaryKeyDropped` (reachable through `Catalog.create`)
from `unresolvedTargetDropped` (which `Catalog.create` already *rejects*
via `catalog.reference.danglingTarget` — a stronger guarantee, so this
code is defense-in-depth for a smart-constructor bypass). Wired into the
production manifest diagnostics. This is the FK case of
`L3-Boundary-NoSilentDrop` (see `AxiomTests.fs::L3-X7`).

## A32 cash-out — chapter 4.2 close (2026-05-11)

**Cashed out** at chapter 4.2 close (per `CHAPTER_4_2_CLOSE.md` §8).

**Chapter 3.1 partial advance** (preserved): `TopologicalOrderPass.runWith`
produced an emitter-consumable `TopologicalOrder` value; the
`RawTextEmitter` (retired in chapter 4.1.A close arc) consumed it. The
minimal pattern — single pass, single emitter — became the first
structurally-realized instance of A32 in the codebase.

**Chapter 4.2 worked example** (the full-shape instance). `UserFkReflowPass.
discover : UserPopulation<SourceUserId> -> UserPopulation<TargetUserId> ->
UserMatchingStrategy -> Lineage<Diagnostics<UserRemapContext>>` is the
canonical pass shape. `UserRemapContext = { Mapping; Unmatched;
Diagnostics }` is the emitter-consumable value (smart-constructor invariant
`Mapping.Keys ∩ Unmatched = ∅`). Sibling Π's consume the context to
rewrite User-FK column values at row-emission time:

- `MigrationDependenciesEmitter.emitWithUserRemap` (chapter 4.2 slice η)
  is the live consumer.
- `BootstrapEmitter` (chapter 4.1.B slice ζ) has the signature plumbed
  but emits no rows today; future chapters (4.3 Diagnostics +
  chapter-5 cutover-day runbook) supply row sources that consume the
  context.

The discovery-pass / consumer-emitter split is the load-bearing shape:
discovery is one E-pass producing a context value via the
`Lineage<Diagnostics<'a>>` dual writer; application is one or more sibling
Π's consuming `(catalog, profile, context)` evidence. A18 amended holds
structurally — emitters cannot type-check with a `Policy` parameter; only
the discovery pass and the composer touch Policy.

**Property test cash-out.** The scheduled `A32: discovered value visible
to emitter` property test cashes out as the multi-environment commutativity
property at chapter 4.2 slice η (`UserFkReflowIntegrationTests.fs`): same
source population + ByEmail strategy against four distinct target
populations yields four `UserRemapContext` values whose source-keyset
agrees across all four; smart-constructor invariant holds for each;
per-environment differences live entirely in `TargetUserId` values. The
test specializes T4 (sibling functor commutativity) to A32's worked
example.

**Closure of the discipline.** A32 stops being scheduled and becomes a
wired template. Future passes producing emitter-consumable values inherit
the `Lineage<Diagnostics<'a>>` return-shape (per the writer-fidelity
discipline); future emitters consuming such values inherit the
`Catalog × Profile × <context>` signature (A18 amended preserved
structurally).

---

## On the scaffolding discipline

The scheduled amendments span chapters 3.3, 3.4, 3.5, 4.2, and the
chapter-3 cross-cutting close. The scaffolding section above is the
single-source list; each chapter close writes the amendment text
in the placeholder block above and updates the heading from "TBD"
to the close date.

**Chapter 3.1 close (sessions 27–36, 2026-05-30) cashed:**
- A35 — Π's output is a deterministic statement stream.
- A36 — bulk-vs-incremental is realization-layer policy.
- A39 — aggregate-root smart-constructor invariants.
- A40 — harmonization-via-parameterization.
- A32 partial advance via `TopologicalOrderPass.runWith` consumed by `RawTextEmitter`.

**Renumbered at chapter 3.1 close:**
- A35 candidate (Π-erased axes) → **A37 candidate** (chapter 3.4).
- A36 candidate (CatalogDiff exhaustiveness) → **A38 candidate** (chapter 3.5).

**Cashed at chapter 3.5 close (2026-05-09):**
- T11 amended (structural-type encoding) — body at line 716. Π port realization makes T11 structural via `ArtifactByKind<'element>`. Chapter 3.5 slices α–δ delivered.
- T11 amended again (diff-typed inputs) — body at line 796. Chapter 3.5 slices θ–ι delivered the diff-typed sibling.
- A38 — CatalogDiff exhaustiveness — body at line 1001. Promoted from candidate to A38 at chapter 3.5 close.

**Cashed at chapter 3.2 close (2026-05-10):**
- A1 amended (four-variant SsKey) — body shipped at Stage 0 S0.B + chapter 3.5 RefactorLogEmitter (line 833+); chapter 3.2 makes the `OssysOriginal` variant **operationally reachable at the OSSYS-adapter boundary** for the first time, via the `SnapshotRowsets` source variant + `RowsetBundle.ModuleRow.EspaceSsKey` / `KindRow.EntitySsKey` / `AttributeRow.AttrSsKey` Guid-carrying fields. The "JSON-projection-lossiness bound" documented at the bottom of A1 is structurally unblocked. Cross-version `V1Mapped` derivation reserves to chapter 4.2 User FK reflow. See `DECISIONS 2026-05-10 — Chapter 3.2 close` for the slice-by-slice account.

**Cashed at chapter 3 cross-cutting close (2026-05-10):**
- A39 record-extension generalization confirmation — the aggregate-root smart-constructor invariants amendment (line 1176) holds for record extensions too. Chapter 3.2 confirmed empirically: 4 record-extension events + 1 DU-variant event across the chapter; closed-DU expansion empirical-test discipline survives generalization. See `CHAPTER_3_2_CLOSE.md` §"Three chapter-close meta-codifications" #1.

**Still scheduled** (TBD; trigger pre-fire):
- T1 amended (binary normal-form composition) — chapter 3.3 close (chapter not yet open).
- A37 candidate (Π-erased axes) — chapter 3.4 close (chapter not yet open).
- A32 cash-out — chapter 4.2 close (User FK reflow chapter).
- **A41 (Transform registry totality + canonical strongly-typed shape) — CASHED at chapter A.4.7 close 2026-05-16.** See A41 body above. The placeholder below records the pre-cash statement for historical traceability:

   *Every transformation site in V2 is enumerated as a `RegisteredTransform<'In, 'Out>` value in `Projection.Core.TransformRegistry`, where the registry is canonical for both metadata AND the transformation-function definition itself (single definition site; no parallel enumeration; each module's primary public surface is its `<PassName>.registered` value). The type:*

   ```
   type StageBinding   = Adapter | Pass | OrderingPolicy | Emitter | Pipeline
   type OverlayAxis    = Selection | Emission | Insertion | Tightening   // = Policy DU axes exactly; reserved for expansion
   type Classification = DataIntent | OperatorIntent of OverlayAxis
   type TransformSite  = { SiteName : string ; Classification : Classification ; Rationale : string }
   type TransformStatus = Active | NotImplementedInV2 of rationale: string
   type RegisteredTransform<'In, 'Out> = {
       Name : PassName
       Domain : Domain
       StageBinding : StageBinding
       Sites : TransformSite list                  // intra-pass classification fidelity (per Q11)
       Run : 'In -> Lineage<Diagnostics<'Out>>     // typed transformation function (per Q3)
       Status : TransformStatus
   }
   ```

   *Conversely, every registered `Active` transformation fires in at least one canary scenario; every registered `NotImplementedInV2` transformation is referenced by at least one `Tolerance.fs` entry. Registry contents are compile-time-evaluated F# `let` bindings (no runtime reflection); coverage is asserted by bidirectional property tests (`TransformRegistryCompletenessTests`).*

   *The dichotomy is enforced bidirectionally: (a) `Compose.runWithSkeleton` produces zero `LineageEvent` with `Classification = OperatorIntent _` (skeleton-purity property); (b) every registered `OperatorIntent` transformation fires in canary (overlay-exercise property). The skeleton projection (`Project(catalog, Policy.empty, profile)`) is a first-class callable reached via `osm emit --skeleton-only`; `Compose.run` traverses the registry as its execution loop (per Q5).*

   This amendment promotes the data-intent / operator-intent separation from convention (A18 amended forbids `Policy` in Π — the Π-side commitment by structural type) to bidirectional structural enforcement (the Pass-side commitment by registry totality + bidirectional property tests). A18 and A41 are sibling structural commitments: A18 forbids `Policy` in emitters; A41 enumerates every transformation site that DOES consume `Policy` (or other operator-supplied intent — rename specs, config overrides, etc.) so the dichotomy holds bidirectionally.

   `OverlayAxis = Policy DU axes exactly` reifies the principal-PO observation that **Policy is operator intent** (per `DECISIONS 2026-05-15 (late) — Pillar 9`). Ubiquitous-language consistency: `OperatorIntent (Overlay Tightening)` reads as "operator intent expressed via the Tightening axis" — the existing `Policy.Tightening` axis IS an `OverlayAxis` value.

   *Discipline pairing.* A41's structural enforcement holds up only if classification gets done correctly at consideration time. Pillar 9 (harvest-dichotomy classification — `DECISIONS 2026-05-15 (late)`) is the meta-discipline; A41 is the structural commitment that catches its failures. See also `2026-05-13 — Transform registry cash-out` (the prior framing; preserved as historical record under different consumer pressure) and `2026-05-15 — Transform registry re-opened: skeleton-overlay separation` (the predecessor entry refined by the late-day 2026-05-15 codification).

**Per `DECISIONS 2026-05-22 — Stage 0 foundation phase`, the
scaffolding lands at Stage 0 Tier 1 (S0.F) before chapter 3.1
opens.** Future chapters that surface new amendment candidates
append to this section at chapter open with TBD bodies; the
scaffolding grows monotonically with the chapter pre-scopes.
