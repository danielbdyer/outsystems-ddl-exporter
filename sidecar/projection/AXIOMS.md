# AXIOMS — The Catalog as Protagonist

The formal system the projection sidecar implements. Code refers to this
document by axiom number in comments and test names. A failing test pointing
at axiom A12 should send a reader directly to the section below.

The system has thirty-one axioms (A1–A31) generating ten theorems (T1–T10).
The axioms are grouped into eight thematic clusters; the theorems cluster
by what falls out of the construction.

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
