# WAVE 6 ALGEBRA — The Change Calculus (the domain reified)

> **What this is.** The reification of `WAVE_6_ONTOLOGY.md` (entities · moves · predicates) into the **algebra of
> the domain** — postulated from first principles so every law is a *balanced equation* (`LHS = RHS`, both sides
> in the carrier's native operations), with the variables in their **most revealing native form.** It is the
> source for the change-algebra theorems in `AXIOMS.md` (**T12–T16, A43**) and the executable witnesses in
> `AxiomTests.fs`. Pairs with the ontology (the prose grounding), `AXIOMS.md` (the formal catalog), and the code
> (the operations these laws quantify over: `between` / `applyDiff` / `emit` / the CDC norm).
>
> **Discipline.** Each law is tagged **[law]** / **[policy]** / **[target]** and anchored to a *witness* (live
> test) or a *trigger* (the slice that earns it). The algebra is not decoration: it is the statement of which
> equations the code must balance, in the form that makes *why* they balance self-evident.

---

## 0. The revealing move — State is a torsor over Delta

Through the whole reasoning session one native form kept surfacing: a **Delta is not a "diff record" — it is the
*difference of two States*,** and States form an **affine space (a torsor)** over the group of Deltas. Postulate
the two primitive operations as **subtraction** and the **affine action**:

```
⊖  (between) : State × State → Delta          δ = B ⊖ A      "the change from A to B"
⊕  (apply)   : State × Delta → State          B = A ⊕ δ      "A, changed by δ"
```

Everything else is *forced* by the affine (Weyl) axioms — which is precisely why the equations balance: we are
not asserting laws, we are reading off the axioms of an affine space. The round-trip law, the identity diff, and
composition-over-time are **not three facts; they are the three torsor axioms** (§2). This is the "most revealing
and native expression of the relationships": change is *displacement*, and a database state is a *point*.

The structure is, precisely, a **groupoid** (objects = States, arrows = Deltas, every arrow invertible,
composition partial — a Delta is typed by its endpoints); *locally*, the Deltas out of any point form a group and
the States are a torsor over it (the affine picture). The partiality is real and load-bearing — it is where the
emission functor's faithfulness ladder lives (§6).

---

## 1. The carriers (the variables, in native form)

| Symbol | Native form | Code surface |
|---|---|---|
| `A, B, C : State` | **points** of the affine space | `Catalog` (schema plane) / `RowSet` keyed by reconciled identity (data plane) |
| `δ : Delta` | **displacement vectors** (the group the torsor acts under) | `CatalogDiff` (schema, a value); data δ is **substrate-fused** (the at-target MERGE) — observed via the CDC series, not a value (§12.4) |
| `m : Move` | the **generators** of `Delta` (the alphabet of change) | Add/Remove/Rename/Reshape/Reidentify (schema) · Insert/Delete/Update/Reidentify (data) |
| `‖·‖ : Delta → ℕ` | the **norm** (length) of a displacement | move-count; **physically: the CDC capture-row count** (data plane) |
| `Identity` | the **conserved charge** | `SsKey` |
| `Designation` / `Realization` | **coordinates** of a thing | `Name` (logical) / `Column.ColumnName`·`Physical.Table` (with `Realization := Designation`, policy) |
| `σ : Script` | the **realization** of a displacement on the substrate | `emit(δ)` — refactorlog + ALTER (schema) / change-detecting MERGE (data) |
| `Â : Substrate` | the **deployed point** | the live database |

---

## 2. The torsor axioms — **T12** (round-trip · identity · composition are one structure)

The three Weyl axioms of an affine space, read in the domain's operations. These *are* the round-trip law, the
identity diff, and evolution — unified.

- **W1 — identity [law].** `A ⊕ 0 = A`, where `0 := A ⊖ A`. *(The empty diff is the zero displacement;
  `between(A,A)` is the identity arrow; applying it is a no-op.)*
- **W2 — composition / Chasles [law].** `(A ⊕ δ₁) ⊕ δ₂ = A ⊕ (δ₁ + δ₂)` and `(B ⊖ A) + (C ⊖ B) = C ⊖ A`.
  *(Displacements compose; evolution over time is vector addition along the path A→B→C. `+` is the groupoid
  composition — associative, partial, invertible, not commutative in general; commutative across orthogonal
  channels, §5.)*
- **W3 — uniqueness / round-trip [law].** `A ⊕ (B ⊖ A) = B` and `(A ⊕ δ) ⊖ A = δ`.
  *(`⊖` and `⊕` are mutually inverse pointwise: for fixed A, `between(A,-)` and `apply(-,A)` are a bijection
  between displacements-out-of-A and reachable points. This is the section/retraction pair.)*

**State-dependence is forced (the no-cheat law) [law].** W3 entails `apply` is a *genuine action* — `A ⊕ δ`
depends on `A`. An implementation `apply(δ) = const` (e.g. `applyDiff base d = target d`, ignoring `base`)
violates W3's uniqueness: it would make `A' ⊕ δ = A ⊕ δ` for `A' ≠ A`, collapsing the torsor. The discriminating
witness `∃ A' ≠ source. A' ⊕ δ ≠ target(δ)` is not an extra test — it is W3 made falsifiable.

*Witnesses:* `Time: applyDiff (between A B) A = B (evolution round-trip law)`; `applyDiff threads the passed-in
catalog, not the recorded target (no-cheat)`; `applyDiff (between A A) A = A` (W1).

---

## 3. The grading by moves — Delta is generated by the alphabet of change

`Delta` is **graded by `Move`**: every displacement factors into a multiset of moves, and the moves are the
generators. Each generator is **invertible** — `Add⁻¹ = Remove`, `Rename⁻¹ = reverse-Rename`, `Reshape⁻¹ =
reverse-Reshape`, `Insert⁻¹ = Delete`, `Update⁻¹ = reverse-Update`, `Reidentify⁻¹ = reverse-reconcile`. Hence the
arrows are invertible (the groupoid of §0) and `‖δ⁻¹‖ = ‖δ‖`. The move-alphabet is the same one plane apart
(schema moves ∥ data moves; `WAVE_6_ONTOLOGY.md` §5 ∥ §12.3) — the grading is the structural isomorphism between
the legs.

> **Note — emission asymmetry.** The *abstract* groupoid is clean (every move invertible). The *emission* functor
> (§6) is **not** total: destructive generators (Remove/Delete) are refused, narrowing realizations warn. The
> abstract reversibility and the emission's partiality are different facts; conflating them is a category error
> (the comparison is total; the realization is partial).

---

## 4. The norm — **T15** (conservation · minimality · CDC = the norm made physical)

Define `‖·‖ : Delta → ℕ`, `‖δ‖ = |moves(δ)|` — the count of moves. Its laws: `‖0‖ = 0` (W1); `‖δ⁻¹‖ = ‖δ‖`;
`‖δ₁ + δ₂‖ ≤ ‖δ₁‖ + ‖δ₂‖` (triangle; equality across orthogonal channels, §5).

**The physical identification [law/instrument].** On the data plane the norm is **realized by CDC**: a faithful
realization writes exactly one capture row per move, so

```
‖δ_data‖  =  |capture(run(emit(δ_data), Â))|          (the CDC capture count IS the norm)
```

**Conservation — emission is an isometry [law / the operator's balance].** The preferred realization neither
inflates nor deflates the norm:

```
‖emit(δ)‖  =  ‖δ‖
```

- **CDC-silence** is the `‖δ‖ = 0 ⟹ |capture| = 0` instance (W1 under emission).
- **Minimum data diff** is *isometric* emission (the change-detecting MERGE: capture = `|changed rows|`).
- **Complete replace** is *non-isometric* — `‖replace‖ = 2·|table| ≫ ‖δ‖` — correct but norm-inflating; this is
  the precise reason it is the *fallback*, not the preferred mode. The operator's "minimum viable data
  movements" is exactly "choose the isometric emission."

*Witnesses:* `Slice γ: CDC-silence … emits zero CDC capture rows on idempotent redeploy` (the `=0` instance);
`Slice γ sensitivity: changed-content redeploy DOES fire` (the norm is not vacuously zero). *Trigger:* the general
`‖δ‖ = k` case (`EXECUTION_PLAN.md` 6.F.3-data(a)).

---

## 5. The channel decomposition — **T14** (orthogonality as a direct sum)

A displacement is the **direct sum of its channel projections**:

```
δ  =  ⊕_c  π_c(δ)            with   π_c ∘ π_{c'} = 0  (c ≠ c'),   Σ_c π_c = id
```

schema channels = Rename ⊕ Reshape ⊕ Add ⊕ Remove ⊕ Reidentify; data channels = Insert ⊕ Update ⊕ Delete ⊕
Reidentify. **Orthogonality** (T-V) is exactly "the projections are disjoint and covering," and the norm is
**additive over the decomposition** (the channels are orthogonal axes):

```
‖δ‖  =  Σ_c ‖π_c(δ)‖
```

This subsumes **A38** (CatalogDiff kind-level exhaustiveness — the `Renamed ⊎ Added ⊎ Removed ⊎ Unchanged`
partition) and generalizes it to the attribute and data planes. The Rename ⊥ Reshape disjointness we built (a
renamed element carries no shape facet → the refactorlog and ALTER channels never touch the same element) is the
first non-trivial instance.

*Witnesses:* `CatalogDiff exhaustiveness: scope equals disjoint union of partitions` (A38, kind-level); `migration:
a rename alone emits no ALTER (renames are the RefactorLog channel)` (channel disjointness). *Trigger:* the full
multi-channel partition at the `migrate` composition (6.D.1).

---

## 6. The realization functor — **T16** (the commuting square; the master equation)

`emit : Delta_Model → Script_Substrate` is a functor; `realize` (= Project) maps `State_Model → Substrate`. The
**master balance equation** is that the Project square commutes:

```
              δ = B ⊖ A
       A ───────────────────────► B                 (Model plane:  A ⊕ δ = B,  T12)
       │                          │
   realize                    realize                (Project : Model → Substrate)
       │                          │
       ▼        emit(δ)           ▼
   realize(A) ───────────────► realize(B)            (Substrate plane: run(emit(δ), realize(A)) = realize(B))
```

```
run( emit(B ⊖ A), realize(A) )  =  realize(B)        modulo residual = (erasure ⊎ tolerance)
```

This *single equation* is the whole system's faithfulness: a change computed in the Model and realized on the
Substrate lands at the same point. **The schema leg and the data leg are its two projections** (the square
restricted to the schema and data sub-states). `emit` is **partial** (refusals — `emit` undefined on a
destructive move) and **lossy** (warnings — `emit` defined but not norm-preserving on that move); the
**iso-ladder L1/L2/L3 measures the totality/faithfulness of `emit`**, and the residual is the intent-filter's
tolerated bucket (§8). This is the H-050 adjunction (`Ingest ∘ Project = id`) **lifted from points to
displacements**: `between`/`apply` are Ingest/Project on arrows.

*Witnesses (sub-squares):* `migration canary: a widening ALTER COLUMN executes on SQL Server and preserves data`
(a schema move realized, data conserved — the cross-plane corollary §7); `Slice γ` (the data sub-square, norm 0).
*Trigger (the full square):* the one-command `migrate A B` canary (6.D.1) — when it is green under T12–T15, T16
holds end-to-end and the engine is structurally isomorphic to the shape of change (`NORTH_STAR` L3).

---

## 7. Identity as the conserved charge — **A43** (and the algebraic *why* of the refactorlog)

[law] Under every move, **Identity (`SsKey`) is the conserved quantity**, with one creation/annihilation pair:

| Move | Identity | Designation | Facets / cells |
|---|---|---|---|
| Rename | **conserved** | changed | — |
| Reshape / Update | **conserved** | conserved | changed |
| Reidentify | **correspondence reconstructed** across substrates (surrogate Realization differs; matched by business key) | — | — |
| Add / Insert | **created** (new charge) | — | — |
| Remove / Delete | **annihilated** | — | — |

[policy] `Realization := Designation` (V2 emits the logical name as the physical object). So **Rename is the
unique move that perturbs Designation while conserving Identity**, and its faithful realization is `sp_rename`
(via the refactorlog), not DROP+ADD.

**The cross-plane corollary — the refactorlog is *derived*, not stipulated [law].** A faithful schema Rename
must induce **zero data moves**:

```
‖ emit_substrate( π_Rename(δ_schema) ) ‖_data  =  0
```

Because `sp_rename` conserves the rows (the data norm is untouched), whereas an unfaithful realization (DROP+ADD)
induces `‖·‖_data = 2·|table|`. So "use the refactorlog for renames" is **not an SSDT convention we adopt — it is
forced** by Identity-conservation across the schema→data coupling: *emit schema renames so the induced data-norm
vanishes.* The data plane's norm-conservation (T15) and the schema plane's identity-conservation (A43) meet
exactly at the refactorlog.

*Witnesses:* `RefactorLogEmitter: a column rename produces a SqlSimpleColumn entry` (Designation changes, Identity
conserved, emitted as sp_rename); the re-key canary `(Order → User-by-email)` (Reidentify reconstructs the
correspondence, relationship conserved modulo surrogate). *Trigger:* the cross-plane `‖rename‖_data = 0` canary
(deploy a rename, assert zero data-CDC capture) — rides 6.D.1.

---

## 8. The intent filter as an orthogonal projection — (the residual of T16)

[law] The raw observation decomposes:

```
observe(A, B)  =  (B ⊖ A)  ⊕  tolerate(A, B)         intended displacement  ⊕  tolerated noise (orthogonal)
```

The `tolerate` summand is the **residual modulus** of the commuting square (§6) — substrate/tooling noise (DacFx
normalization, auto-named constraints, empty-string↔NULL, ANSI-padding, decimal scale, collation). Pillar 9 at
the algebra level: every observed difference projects onto *exactly one* of `intended` / `tolerated`; nothing is
unclassified, and `emit` acts only on the intended summand. *Witness floor:* the canary's "modulo named
tolerances." *Trigger:* the structured intent/tolerance projection (6.A.4 + the data-plane P-DIFF hardening).

---

## 9. The reified theorems (the map into `AXIOMS.md`)

| Thm | Statement (balanced equation) | Native reading | Witness / Trigger |
|---|---|---|---|
| **T12** | `A ⊕ (B ⊖ A) = B`; `A ⊕ 0 = A`; `(A⊕δ₁)⊕δ₂ = A⊕(δ₁+δ₂)` | the torsor (Weyl) axioms — round-trip, identity, composition are one | ✅ round-trip + no-cheat + identity-diff |
| **T13** | `replay(t) = genesis ⊕ (δ₀ + … + δ_t)` = fold ⊕ | evolution over time = vector addition along the timeline (Chasles) | ✅ `reconstructLatest`; ⬚ `compose` operator |
| **T14** | `δ = ⊕_c π_c(δ)`, `π_c π_{c'} = 0`, `‖δ‖ = Σ‖π_c δ‖` | orthogonality = direct-sum decomposition; subsumes A38 | ✅ A38 + rename⊥reshape; ⬚ full multi-channel |
| **T15** | `‖emit(δ)‖ = ‖δ‖`; `‖δ‖_data = \|capture\|`; `‖0‖ = 0` | emission is an isometry; CDC is the norm; minimality = isometry | ✅ CDC-silence floor + sensitivity; ⬚ `‖δ‖ = k` |
| **T16** | `run(emit(B⊖A), realize(A)) = realize(B)` mod (erasure ⊎ tolerance) | the Project square commutes — the adjunction lifted to displacements | ◑ sub-squares; ⬚ the `migrate` canary (6.D.1) |
| **A43** | Identity conserved under all moves; Rename: `Designation` changes, `Identity` const, `‖rename‖_data = 0` | Identity is the conserved charge; the refactorlog is *derived* | ✅ column-rename + re-key; ⬚ `‖rename‖_data=0` canary |

---

## 10. Why this is balanced and native (the meta)

- **Every law is an equation `LHS = RHS`** with both sides in the carriers' native operations (`⊕`, `⊖`, `+`,
  `‖·‖`, `π`, `emit`, `realize`) — no one-sided assertion, no "this should roughly hold."
- **The torsor framing fuses three facts into one structure.** Round-trip, identity, and composition are not
  independently postulated; they are W1–W3 of an affine space. That fusion *is* the "most revealing and native"
  form — you cannot state the round-trip without also stating composition, because they are the same structure.
- **CDC = the norm** is the identification that turns "minimum viable touches" from a slogan into a measured,
  enforceable equality (`‖emit(δ)‖ = ‖δ‖`).
- **The commuting square (T16) is the single master equation;** T12 (its top edge), T15 (its norm), T14 (its
  domain decomposition), T13 (its iteration over time), and A43 (its conserved charge) are its facets. The
  schema and data legs are its two projections. The iso-ladder is its faithfulness gradient.

So the engine is *right by function* when these equations balance with the smallest possible residual — which is
the same as saying the engine is structurally isomorphic to the shape of change.

---

## 11. How to use this document

- **Designing a Wave-6 slice?** Name the equation it makes balance (T12–T16 / A43) and the *residual* it shrinks.
  If a slice doesn't move a term of one of these equations, question whether it's on the path.
- **Adding an operation?** State it in the native algebra first (`⊕` / `⊖` / `+` / `‖·‖` / `π` / `emit`). If it
  doesn't compose with the existing operations, the carrier is wrong.
- **Reify discipline:** every theorem here has an `AxiomTests.fs` entry that is its *discriminating* witness (the
  input where a plausibly-named-but-wrong implementation breaks the equation), per `WAVE_6_ONTOLOGY.md` §8.

---

## 12. Addendum (2026-06-01) — the concern-movement field, and latent vs activated

The four-agent structural research (`WAVE_6_MORPHOLOGY.md`) read the calculus *from* the codebase and sharpened
three things. They extend the calculus without altering T12–T16/A43.

### 12.1 The concern-movement field (a 2-D partial-derivative extension)

A concern κ ∈ {Schema, Data, Identity, Time, Decision} occupies a position in a **2-D field**: an *emission
coordinate* (which artifact) and an *episode coordinate* (which time-step). Its movement is two partial
derivatives, each with an integral:

```
∂κ/∂(emission)              how κ distributes/changes across artifacts at a fixed episode
   ∫ over emission space  = the MANIFEST                         (the emission-integral)

∂κ/∂(episode)               how κ changes across episodes (the displacement between adjacent episodes)
   ∫ over time            = the PROVENANCE (refactorlog + CDC log + snapshot chain = a LifecycleStore)
   and the FTC (T13):       reconstructLatest = genesis ⊕ Σδ   (the integral of the derivative recovers state)

∂²κ/∂(emission)∂(episode)   how κ's emission-distribution changes across episodes
                          = the CHANGE-MANIFEST SERIES (the manifest-of-δ over time)
```

"Observe the movement of concerns during **multi-episodic recombination**" = observe `∂κ/∂(episode)` and the
**cross-concern recombination** (κ₁ at episode i × κ₂ at episode j) — a *join* over a provenance store that
co-records all five concerns per episode (a multi-plane `Episode`). Today only the `∂κ/∂(emission)`-of-*state*
slice is lit; `∂κ/∂(episode)` is dark (no durable episode) and the manifest integrates *state*, not displacement
δ (see `WAVE_6_MORPHOLOGY.md` §2–§3). T15's norm `‖δ‖` is the magnitude of the episode-derivative on the data
plane; the CDC capture series is its time-integral.

### 12.2 Latent vs activated (re-reading the status of every law)

A law is **latent** when its equation is proven *in isolation* (a unit/canary witness over in-memory values) but
its operations are **unwired** in production and/or its substrate is **not persisted**; it is **activated** when
the operations are wired and the substrate durable, closing the residual. The research's finding: **the calculus
is correct and latent.** Re-reading §9:
- **T12** — activated (between/applyDiff wired into Lifecycle + the SchemaMigration/RefactorLog emitters exist).
- **T13** — *latent*: `reconstructLatest` runs only over in-memory values in tests; **there is no durable episode
  to integrate over**, and no `compose` (`+`) operator. *Activation:* the `Episode` + `LifecycleStore` +
  `CatalogDiff.compose` (`WAVE_6_MORPHOLOGY.md` §4 F1–F3; `EXECUTION_PLAN.md` 6.H).
- **T14 / T15** — *latent at the type level*: the channel projection `π` and the norm `‖·‖` have **no code
  carrier** (witnessed only by test assertions). *Activation:* the **norm** `‖·‖` reifies as a measurement
  carrier over the *realized* delta — the CDC capture series on the data plane (6.F.3-data), the move-count on
  the schema plane; the value-level `π`/`Delta` reify (concretely as `CatalogDiff`) only at the temporal
  multi-version schema use (6.H), **not** the data leg, whose δ is substrate-fused (§12.4).
- **T16** — dark differential leg: the engine ships `realize(B)`, not `emit(B ⊖ A)` (the diff emitters have zero
  production callers). *Activation:* `migrate A B` (6.D.1).
- **A43** — Identity survives an episode boundary (`V2.SsKey` round-trip), but only as a current value, not a
  chain; the `‖rename‖_data = 0` corollary is unwitnessed live. *Activation:* the change-manifest + the rename
  canary.

### 12.3 The noun/verb reification principle (the spine rule, grounded)

The codebase reifies the **carriers** (nouns of change — `Catalog`, `CatalogDiff`, `SsKey`, `Lifecycle`)
eagerly, and leaves the **operators** (verbs — `Move`, an abstract `Delta`, the norm `‖·‖`, the channel `π`, the
`Torsor`) as **functions and test-witnessed laws** with no type. This is *correct discipline* (§11; the
two-consumer threshold), and the morphology research shows *why*: **the verbs are absent because the second
consumer has not been built, not by oversight.** The principle, sharpened:

> **Carriers reify eagerly; operator-verbs reify at the second consumer.** `Move` / abstract `Delta` / `‖·‖` /
> `π` / `Torsor` earn a code home only when a genuine second consumer appears — and **not before.** The
> **spine-breaker to refuse** is the speculative torsor refactor (renaming `between`→`⊖`, a `Torsor` typeclass).
> The algebra is the *spec the witnesses check*, not a shape to force the code into; the engine must *behave*
> like the torsor (proven by the discriminating witness), never be *named* like it on speculation.

### 12.4 The schema/data delta-representation asymmetry (a refinement-pass correction)

An earlier draft located the value-level torsor's "second consumer" in the data leg (a model-plane `RowDiff`).
The morphology research corrects this: **the schema and data planes differ in how their delta is *represented*,
even though their moves and norm are analogous.**

- **Schema δ is a value.** `CatalogDiff = between(A, B)` is a model-plane value; `applyDiff` acts on it; the
  emitters consume it (`EmitterOverDiff`). `between`/`apply`/`compose` are value-level operations.
- **Data δ is substrate-fused.** Per the ontology's at-target-MERGE policy, the engine emits *the statement that
  is the diff* (the change-detecting MERGE); SQL Server computes the delta at apply; the comparable-column set +
  the null-safe predicate ARE the comparison and the tolerance. There is deliberately **no model-plane
  `RowDiff` value** — building one is the speculative-abstraction trap. The data δ's *observable* form is the
  **realized CDC capture series** (the post-hoc delta), which the change-manifest records.

**Consequences (so the builder doesn't over-abstract):**
1. The **data leg is not a value-level second consumer** of `between`/`apply`; it does not trigger a shared
   `Delta`/`π` type. What it reifies is the **norm** `‖·‖` (the CDC capture count) — a measurement carrier over
   the realized delta, the data analog of the schema move-count.
2. The value-level verbs (`between`/`apply`/`compose`, and any abstract `Delta`/`π`) have their second consumer,
   *if anywhere*, at the **temporal multi-version schema** use (6.H — composing `CatalogDiff`s across episodes),
   and even there reification stays **concrete** (`CatalogDiff` + `compose`), not a generic `Torsor`.
3. The schema∥data isomorphism (§3) holds at the **move** and **norm** level (Add/Reshape/Remove ∥
   Insert/Update/Delete; ALTER-not-CREATE ∥ CDC-minimal), **not** at the delta-representation level. The "same
   shape one plane apart" is a statement about the generators and the measure, not about how each plane stores
   its displacement.

---

— Recorded for the receiving agent. The algebra is the equation; `WAVE_6_ONTOLOGY.md` is its interpretation;
`WAVE_6_MORPHOLOGY.md` is the territory it was drawn over (latent until activated); `AXIOMS.md` T12–T16/A43 is its
catalog entry; `EXECUTION_PLAN.md` Wave 6 + 6.G/6.H is the route that shrinks each residual to zero;
`NORTH_STAR.md` is the bullseye where every equation balances.
