# AXIOMS — The Catalog as Protagonist

The formal system the projection sidecar implements. Code refers to this
document by axiom number in comments and test names. A failing test pointing
at axiom A12 should send a reader directly to the section below.

The original V1 algebraic spec stated thirty-one axioms (A1–A31)
generating ten theorems (T1–T10). V2 has extended the system: A6, A12,
A17, A18, and T1 carry amendments (recorded under "V2 Amendments"
below); A32, A33, A34, and T11 are new. The current count is **A1–A34
generating T1–T11** with five amended originals. The axioms are grouped
into eight thematic clusters; the theorems cluster by what falls out of
the construction. Code and tests cite the **amended** form when both
exist; the original form is the historical lineage of the amendment.

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

  The bound resolves when V2's catalog reader gains access to the
  rowset SSKeys directly — the operator-decided canonical
  resolution is the `SnapshotRowsets` variant of `SnapshotSource`
  (per `DECISIONS 2026-05-15 — OSSYS adapter parse signature` and
  the session-20 amendment to `DECISIONS 2026-05-15 — OSSYS
  adapter translation rules`). The bound applies only to the
  current `SnapshotJson` input path; future input paths (the
  planned `SnapshotRowsets`; an eventual `LiveOssysConnection`)
  honor A1 fully because they have access to V1's SSKey
  primitives.

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

## T1 amended (binary normal-form composition) — TBD chapter 3.3 close

**Scheduled at chapter 3.3 close** (DacpacEmitter + DacFx wrapper;
per `DECISIONS 2026-05-22 — Chapter 3 sequencing`).

[Body to be written at chapter 3.3 close.]

**Anticipated content.** The original T1 (and its 2026-05-06 amendment
to the triple) names byte-determinism: same `(catalog, policy, profile)`
triple → bit-identical surface. The DacpacEmitter chapter introduces
binary artifacts — DACPAC files — whose byte-equality is **not**
deterministic under vanilla DacFx `BuildPackage`. Subagent #4's
pre-scope flags this risk explicitly. The chapter-3.3 amendment
extends T1 to a **binary-normal-form** composition: byte-determinism
holds for text artifacts (SSDT DDL .sql files; JSON; Distributions);
**model-equivalence** (DacFx round-trip equality on the in-memory
`TSqlModel`) is the form determinism takes for binary artifacts. The
two forms compose: the canary's tier-1 property tests assert byte-
determinism on text emissions and model-equivalence on binary
emissions, with the property `t1ByteEqualOrModelEquivalent` as the
unifying canary predicate.

The CDC-safety property (revision 1's `idempotentRedeploy`) becomes
**T1 × DacFx idempotent-redeploy**, not T1 alone. The composition
is the cutover-blocking property; the chapter closes when the
composition is structurally enforced and tested.

## T11 amended (structural type encoding) — chapter 3.5 slices α–δ (2026-05-09)

**Cashed at chapter 3.5 Π port realization slice arc** (slices α
[`RawTextEmitter.emitSlices`], β [`JsonEmitter.emitSlices`], γ
[`DistributionsEmitter.emitSlices`], δ [substring-discipline
retirement; type-theorem worked examples at
`tests/Projection.Tests/T11TypeTheoremTests.fs`]).

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
  type-theorem worked examples at `T11TypeTheoremTests.fs`
  replace them. The surviving `T4` and `T11: physical
  realization` tests test rendering invariants, not kind
  coverage — they stay.

**Verification surface.** `T11TypeTheoremTests.fs` carries four
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

## T11 amended again (diff-typed inputs) — TBD chapter 3.5 close

**Scheduled at chapter 3.5 close** (RefactorLogEmitter + CatalogDiff;
per `DECISIONS 2026-05-22 — Chapter 3 sequencing`).

[Body to be written at chapter 3.5 close.]

**Anticipated content.** Chapter 3.5 introduces `EmitterOverDiff
<'element> = CatalogDiff -> Result<ArtifactByKind<'element>,
EmitError>` — emitters whose input is a Catalog-typed diff rather
than a Catalog. T11's sibling-Π commutativity must extend: the
diff-typed emitter's output `ArtifactByKind` keys must be a subset
of `Diff.allKinds` (SsKeys mentioned in either source or target
of the diff), not the full Catalog. The amendment names the
extension: **T11 holds over diff-typed inputs by typing
`ArtifactByKind` over the diff's SsKey set rather than the
Catalog's.**

The smart constructor on `ArtifactByKind` enforces the property at
construction — same discipline as the cross-cutting amendment,
extended to diff-typed inputs. The pattern is monotonic: every new
emitter type variable (Catalog, CatalogDiff, future variants) is a
specialization of the same `ArtifactByKind` shape.

## A1 amended (four-variant SsKey) — TBD chapter 3 cross-cutting close

**Scheduled at chapter-3 cross-cutting close** (the `SsKey`
four-variant DU split; per `CHAPTER_3_PRESCOPE_ARTIFACTBYKIND_REFACTOR.md`
slice 5 and Stage 0 item S0.B).

[Body to be written at the cross-cutting close.]

**Anticipated content.** A1's bound on identity-survives-rename
through the JSON path (added at session 23; documented at the bottom
of A1) names the JSON-projection-lossiness class — V1's
`osm_model.json` strips SSKey columns, so V2's IR synthesizes SsKeys
from name fields rather than carrying V1's actual SSKey values.
The four-variant DU split codifies the bound **type-stratifically**:

```fsharp
type SsKey =
    | OssysOriginal of string             // V1's SSKey, carried verbatim
    | Synthesized of source: string       // V2-synthesized from name fields
    | DerivedFrom of parent: SsKey * reason: string  // existing Derived
    | V1Mapped of original: SsKey * v1: string  // remap envelope
```

A1's identity-survives-rename guarantee splits along the variants:
`OssysOriginal` honors A1 unconditionally; `Synthesized` honors A1
only over name preservation (renames produce new `Synthesized`
keys); `DerivedFrom` honors A1 transitively through the parent;
`V1Mapped` honors A1 over both the V1 envelope and the original.
The amendment codifies the per-variant guarantees and names the
input-path-by-variant correspondence (`SnapshotJson` produces
`Synthesized`; `SnapshotRowsets` produces `OssysOriginal`).

The `SnapshotRowsets` chapter (3.2) is the chapter that produces
`OssysOriginal` SsKeys at scale; the cross-cutting close prepares
the type-system surface, and chapter 3.2 fills the consumer side.

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

## A38 candidate (CatalogDiff exhaustiveness) — TBD chapter 3.5 close

**Scheduled at chapter 3.5 close** (RefactorLogEmitter + CatalogDiff).
Renumbered from A36 candidate at chapter-3.1 close; the original
A36 slot was claimed by session-34's realization-layer-policy axiom.

[Body to be written at chapter 3.5 close; promoted from candidate to
A38 if it earns commitment.]

**Anticipated content.** `CatalogDiff` is a value type produced by
`DiffOf<Catalog> = Catalog -> Catalog -> Result<CatalogDiff, EmitError>`.
Its smart constructor enforces an exhaustiveness invariant: every
SsKey in `source ∪ target` is in **exactly one** of four
disposition variants — `Renamed | Added | Removed | Unchanged`.
The candidate names the invariant as A38 — a structural-commitment
axiom in the same family as A4 (identity equality is structural)
and A34 (Profile independence).

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

## A32 cash-out — TBD chapter 4.2 close (chapter-3.1 partial advance)

**Scheduled at chapter 4.2 close** (User FK reflow as Policy).
**Chapter 3.1 partial advance**: `TopologicalOrderPass.runWith` now
produces an emitter-consumable `TopologicalOrder` value; the
`RawTextEmitter` consumes it (per A40). This is the first
*structurally-realized* instance of A32 in the codebase — passes
producing values consumed by emitters as a wired pattern, not
just a scheduled axiom.

[Full body — including the chapter-4.2 worked example for
`UserFkReflowPass` and `UserRemapContext` — to be written at
chapter 4.2 close.]

**Anticipated content.** A32 (passes may produce values consumed by
emitters; 2026-05-06) named the algebraic shape but had limited
concrete instances. Chapter 4.2 lands the canonical instance:
`UserFkReflowPass` discovers user-mapping context
(`UserRemapContext`) from a `UserMatchingStrategy` DU; sibling Π's
consume the context. The cash-out names the worked example —
"discovery is one E-pass producing a `UserRemapContext` value;
application is two sibling Π's: an INSERT-mode Π consuming
`(catalog, context)` and an UPDATE-mode Π consuming `(context)`
alone." Chapter 3.1's `TopologicalOrderPass.runWith` is the
*minimal* instance (single-pass, single-emitter); chapter 4.2's
will be the *full-shape* instance.

The cash-out also closes the structural property test:
`A32: discovered value visible to emitter` becomes a concrete
xUnit test asserting that both Π's see the same `UserRemapContext`
and that identity correspondences hold across both surfaces (a
special case of T4 sibling-functor commutativity).

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

**Still scheduled** (TBD):
- T1 amended (binary normal-form composition) — chapter 3.3 close.
- T11 amended (structural-type encoding) — chapter 3.5 close (Π port realization makes T11 structural via `ArtifactByKind<'element>`).
- T11 amended again (diff-typed inputs) — chapter 3.5 close.
- A1 amended (four-variant SsKey) — chapter 3 cross-cutting close (already partially documented at A1 footer; full amendment text pending).
- A37 candidate (Π-erased axes) — chapter 3.4 close.
- A38 candidate (CatalogDiff exhaustiveness) — chapter 3.5 close.
- A32 cash-out — chapter 4.2 close.

**Per `DECISIONS 2026-05-22 — Stage 0 foundation phase`, the
scaffolding lands at Stage 0 Tier 1 (S0.F) before chapter 3.1
opens.** Future chapters that surface new amendment candidates
append to this section at chapter open with TBD bodies; the
scaffolding grows monotonically with the chapter pre-scopes.
