# NORTH STAR — Fidelity as a Theorem (the Total Projection)

> **This is the apex document.** It supersedes the *strategic frame* of `VISION.md`
> (revision 2 — "V1 ships the cutover; V2 makes it verifiable, reversible, repeatable").
> That frame is not wrong; it is a **means statement** written when the canary was unbuilt
> and the discipline of the hour was scope restraint ("hold the spine; cut the rest"). The
> spine has been built. This document names the **bullseye** that frame was reaching toward,
> now that we can see it with precision.
>
> `VISION.md` rev 2 remains valid as the **cutover-era operational vision**; `V2_DRIVER.md`
> remains the destination KPI; `PRODUCT_AXIOMS.md` remains the L3 contract; `AXIOMS.md` the
> formal system; `EXECUTION_PLAN.md` the buildable path. This document sits above all of them
> and says what they are collectively *for*.
>
> **Discipline of this document (so the vision itself can't go wrong).** Per the rev-2 review
> (`VISION_REVIEW.md` Appendix A, which rightly killed "sovereignty," "constitutive," "the
> cutover is the load test" as unfalsifiable rhetoric): **every claim below has a counterexample
> condition.** The soul of the north star is one sentence; the bullseye is a matrix of property
> tests the engine runs against itself. If a section here cannot be reduced to a green test or a
> named trigger, it does not belong, and a future agent should cut it.

---

## 1. The bullseye

**The soul (one sentence).**

> The OutSystems estate's schema and data mean exactly what the model says they mean —
> provably, in both directions, on every axis, across time — and the engine proves this about
> *itself*, continuously, so that trust is never required, only verification.

**The bullseye made precise (so we can't go wrong).**

The engine is one **adjunction** between a logical Model and a physical Substrate:

```
Project  : Model     ──►  Substrate          (Π — emit)
Ingest   : Substrate ──►  Model              (the reader leg)
Law      : Ingest ∘ Project = identity       (modulo named, declared erasures)
```

The north star is reached when that adjunction is **total** — true on every axis the engine
owns, executable as a green test for each, and self-describing. Concretely, the bullseye is a
single matrix, fully green and self-checked:

| Axis the engine owns | Project (emit) | Ingest (read-back) | **Round-trip = identity** (the proof) |
|---|---|---|---|
| **Schema** (tables, columns, types, indexes, FKs, triggers, sequences, defaults, computed, checks, ext-props) | ✅ shipped | ◑ hollow for 6 features | **the canary** — closes when Ingest is un-hollowed |
| **Data** (static seeds, migration deps, bootstrap, transfer payloads) | ✅ shipped | ✅ row reader | **the data-level canary** (Transfer) |
| **Identity** (SsKey rename-stable; surrogate keys; user remap) | ◑ display-name only | ◑ synthesized, not recovered | closes with `V2.SsKey` persistence |
| **Time / Lifecycle** (version chains, refactor logs, evolution, drift) | ◑ single-diff only | ✗ no replay | closes when Lifecycle is operational |
| **Decision** (evidence-gated tightening verdicts + their classification) | ✗ decided, not emitted | ✗ not recovered | **the deepest cell** — proves the engine's *opinions* survive the round-trip |
| **— meta —** | | | **Self-verification:** a proof that every cell above has a witness, and that the engine's own coverage map is generated from those witnesses |

> **This matrix is self-reported.** `scripts/matrix-status.sh` regenerates
> [`NORTH_STAR.matrix.generated.md`](NORTH_STAR.matrix.generated.md) from
> `AxiomTests.fs` + the test tree — a round-trip cell goes green **only** when its
> witness test actually exists and is live (it cannot be asserted by hand). And
> `scripts/verifiability-gate.sh` (slice E1) fails the build if any axiom claims a
> coverage bucket its tests do not support. So criterion 5 (§5) is already
> partially live: **the vision measures its own distance to the bullseye.** Today's
> machine reading: L2 axioms 69·A / 8·B / 6·C / 11·D (gate PASS); round-trip
> witnesses present for Schema + Data, open for Identity, Time, and Decision.

Two properties hold *across every cell*, by construction:
- **Determinism (T1).** Same inputs → byte-identical text/JSON, model-equivalent binary. No clock, no randomness, no I/O in the core.
- **Classification totality (pillar 9).** Every transformation is `DataIntent` (the factual skeleton) or `OperatorIntent` (a named, recorded overlay). Nothing is unclassified; nothing is silent.

**When the matrix is fully green and the meta-cell holds, the engine does not make the cutover
trustworthy. It makes trust unnecessary** — because every promise is a theorem the engine
proves about itself, and the engine can show you which promises are proven and which are not.

That is the bullseye. It is a checkable state, not an aspiration. We cannot go wrong because we
can always ask the engine where on the matrix we are.

---

## 2. The constitution: one law, everything a corollary

Rev 2 framed the contribution as "a sibling chorus + verification." The truer, simpler frame:
**the engine is an adjunction, and every capability is a corollary of one structural law.**
This is not decoration — it is the reason the system can be both small and complete.

- **Emitters (the chorus)** are `Project` specialized to a target (SSDT, DACPAC, JSON, data,
  diagnostics). They are siblings because they are projections of the *same* Model; they agree
  (T11) because they project the same keyset.
- **The canary** is the law at runtime: `Ingest ∘ Project = id` checked against an ephemeral DB.
- **Drift detection** is the law's *failure* surfaced: `Ingest(deployed) ≠ Model` is a diff.
- **Remediation** is the law applied to a delta: `Project(Model ⊖ Ingest(deployed))`.
- **Transfer** is the law run across *two* substrates and extended from schema to data:
  `Project_sink ∘ Ingest_source`.
- **Refactor logs / evolution** are the law applied across *time*: the morphisms between Model
  versions (`CatalogDiff`), composed along a timeline.
- **The decision overlay** is the law extended to the engine's *opinions*: an emitted, tightened
  schema, read back, must reproduce the decisions that shaped it.

Nine capabilities, one law. The discipline this gives us: **a proposed feature that is not a
corollary of the adjunction is probably the wrong feature.** When a slice feels like "build the
reverse of X," the question is "what is X's `Ingest` peer, and does the existing direction-neutral
plan already serve it?" The answer is almost always yes. This is how the engine stays small while
its coverage grows.

---

## 3. The four totalities — the structural definition of "done"

The matrix in §1 is reached by closing four completeness conditions. These *are* the definition
of done; each is falsifiable.

**T-I — Round-trip totality.** For every axis (schema, data, identity, time, decision),
`Ingest ∘ Project = identity` modulo a **named, closed** erasure set. *Counterexample condition:*
any axis the engine emits that `Ingest` cannot observe (today: six schema features the canary is
blind to; the decision axis entirely). *Done when:* the canary's read-back is total and the
decision-layer adjunction (read-back reproduces the `DecisionOverlay`) is green.

**T-II — Executable-axiom totality.** Every formal axiom (A1–A42+, T1–T11) and every product
axiom (L3) has a green witness, a named convention-witness, or a `Skip` carrying its promotion
trigger — and a CI gate refuses to let any surface claim a coverage bucket its tests do not
support. *Counterexample condition:* a claimed-verified axiom with no executable test (today: the
DACPAC round-trip claimed "Bucket A" with no test). *Done when:* the verifiability gate is in CI.

**T-III — Input totality.** The four-input algebra `Project = Π ∘ E` over
`Catalog × Policy × Profile × Lifecycle` is complete — all four inputs are operational.
*Counterexample condition:* an input that is declared but unbuilt (today: `Lifecycle`, named since
chapter 1, type still absent). *Done when:* Lifecycle is operational and has a real consumer —
which is also what gives the speculative-execution substrate (`LineageTree`, `PolicyDiff`,
`VersionedPolicy`) its first true consumer.

**T-IV — Documentation totality.** The engine's description of its own coverage — the readiness
map, the per-axis verdict, the backlog status — is **generated from the proof** (the axiom-test
buckets and the transform registry), not authored by hand. *Counterexample condition:* a canonical
surface that disagrees with the code (today: three "shipped but docs say deferred" + one
"claimed-A with no test"). *Done when:* the surfaces are regenerated artifacts and a drift is a
build failure.

**The unification:** T-I and T-III make the engine *correct on every axis, in both directions,
across time*. T-II and T-IV make the engine *able to prove and describe its own correctness
without a human re-auditing it*. Together they are the difference between a tool that is right and
a tool that can **show** it is right, continuously. That difference is the entire reason "replace
V1" is a stronger claim than "trust V1" — and it is why the north star is not "verify the cutover"
but "make verification self-sufficient."

---

## 4. The operator's covenant — what the engine promises, for whom

The matrix is the engine's promise to itself. This is its promise to the **operator** (the DBA /
platform / migration engineer driving the estate). It is the pillar-9 skeleton/overlay contract,
generalized, and it is falsifiable end to end.

1. **Ask for the vanilla projection, get a deterministic factual baseline.**
   `Project(model, Policy.empty, profile)` is the skeleton: no opinion, byte-identical across runs,
   machines, and time. *(skeleton-purity property.)*
2. **Every opinion you apply is named, classified, and recorded.** Each override is a registered
   `OperatorIntent` overlay on a named axis; the manifest names every overlay that touched every
   artifact. *(overlay-exercise + transform-totality properties.)*
3. **Deploy it and redeploy it with zero surprise.** Idempotent redeploy emits zero spurious CDC
   change records; the diff-deploy ALTERs only what genuinely changed. *(CDC-silence property — the
   highest-stakes single guarantee.)*
4. **Move data in either direction and get it back unchanged — modulo what you declared could
   change.** The data-level adjunction holds; identity is preserved or reconciled by a named rule,
   never silently. *(Transfer data canary.)*
5. **Rename freely; identity survives.** A physical rename never re-keys a logical entity; refactor
   intent is carried so deploys ALTER, not DROP+CREATE. *(A1 + RefactorLog round-trip.)*
6. **Nothing disappears in silence.** Any concept the engine cannot carry surfaces as a structured
   diagnostic, never a silent drop. *(no-silent-drop boundary axiom.)*
7. **At every moment, the engine can show you which of these promises are proven and which are
   not.** The coverage map is generated, current, and machine-checked. *(self-verification.)*

Promise 7 is the keystone and the genuinely new one. The other six are guarantees; promise 7 is
the guarantee *about the guarantees*. It is what makes "we can't go wrong" literally true: the
operator never has to wonder whether a promise holds — the engine answers, and the engine's answer
is itself tested.

---

## 5. Falsifiable acceptance criteria

The north star is reached when all of the following hold simultaneously. Each is a test or a
generated artifact, not a judgment.

1. **The round-trip matrix is fully green.** Every (axis × round-trip) cell of §1 is a passing
   property test at canary scale: schema (all features, not six-blind), data, identity, time, and
   decision. Tracked as a green count with zero `Skip` on Tier-1 cells.
2. **The decision adjunction holds.** `Ingest(deploy(Project(C, overlay)))` reproduces `overlay`
   on the tightening axes — the engine's opinions survive the round-trip. (The "stronger than V1"
   theorem; today unbuilt.)
3. **The verifiability gate is in CI.** No surface can claim a coverage bucket its `AxiomTests.fs`
   evidence does not support; the build fails on a phantom claim.
4. **The four-input algebra is complete.** `Lifecycle` is operational with a real consumer; a
   2-version evolution chain replays and composes refactor logs.
5. **The coverage surfaces are generated.** The readiness map and per-axis verdict are projections
   of the axiom-test buckets + the transform registry; a hand-edit that diverges is a build failure.
6. **Determinism and classification totality hold across every cell** (T1; pillar 9 skeleton-purity
   + overlay-exercise + transform-totality) — already largely green; the standard does not relax.
7. **The cutover-era criteria (preserved from rev 2) all hold** — the canary catches a real emitter
   bug with zero false negatives over the quarter; CDC-silence holds at tier-2 and tier-3;
   rename-survives holds for every `OssysOriginal` SsKey; every emitter is `Emitter<'element>`; V1
   sunset is gated on a green canary across a full schema-evolution cycle.

Criteria 1–5 are the *new* north star (the Total Projection). Criterion 7 is the rev-2 north star,
now a **strict subset** — the cutover is the first ring of the bullseye, not the bullseye.

---

## 6. What this supersedes, what it preserves

**Supersedes:** the rev-2 framing that V2's purpose is to *verify V1's cutover*. That was the
correct means when the proof engine was unbuilt; it under-describes the target now that it is ~80%
built. The bullseye is not "verify the cutover" — it is "the adjunction is total and
self-verifying," of which a verified cutover is the first and forcing instance.

**Preserves (unchanged, load-bearing):**
- The **adjunction** (H-050) — now elevated from one inference among many in `SPINE.md` to the
  constitution.
- The **cutover fallback ladder** (V1-only / V2-augmented / V2-driver), **R6** split-brain
  governance, the **T-30/T-15 gates**, **V1 stays warm through cutover+30**. The north star does not
  loosen a single cutover-safety commitment.
- The **V2-driver KPI** (`V2_DRIVER.md`) as the per-axis correctness destination — it is the
  cutover-scoped projection of this matrix.
- The **eight pillars + named failure modes** + the **two-consumer threshold** + **IR-grows-under-
  evidence**. The north star is reached by *closing the matrix*, not by speculative building; every
  slice still earns its place under evidence.

---

## 7. What remains cut (no relapse into the widening)

Rev 2 cut the "informational widening" (platform-survival rhetoric, AI-agent substrate as a pillar,
six-dimension Faker scoring, GraphQL, open-source). **Those stay cut.** The discipline holds: a
capability enters the north star only as a **corollary of the adjunction with a forcing function**,
never as aspiration.

The honest structural facts (stated as corollaries, not destinies):
- **Source-agnosticism is real but unclaimed beyond its proof.** The Model is source-typed; an
  adjunction does not care that the Model came from OutSystems. A second source adapter (a DACPAC
  reader; another platform) would make the engine general *by construction* — but the generality is
  claimed only as far as a second adapter proves it. *Trigger:* a real second source. Until then,
  "outlives OutSystems" is not in the vision.
- **History-awareness is the Lifecycle corollary.** Schema-evolution dashboards, longitudinal
  drift, cross-version replay fall out of T-III for free — *once Lifecycle has a consumer*. Not before.
- **Policy intelligence is the Lifecycle × speculative-execution corollary.** "What would this policy
  change?", policy diffing, approval-gated promotion become load-bearing *once there is a timeline to
  diff across*. The substrate is built; the consumer is Lifecycle. Not before.

Each is named so a future agent recognizes it when its forcing function fires — and refuses it until
then.

---

## 8. How to hold this north star

- **Read it first.** It is the apex; `KICKOFF.md` and `CLAUDE.md` reading order should place it
  above `VISION.md` (a follow-up wiring change).
- **Consult it when** a slice's purpose feels unmoored: ask which matrix cell it advances, or which
  totality (T-I…T-IV) it closes. If it advances none, it is probably not on the path.
- **Extend it only** when a new structural truth earns its place — a new axis the engine genuinely
  owns, or a new corollary whose forcing function has fired. Append; preserve; never inflate.
- **Hold it against itself.** This document's own legitimacy is criterion 5: when the coverage
  surfaces are generated from the proof, *this north star's matrix status becomes a generated
  artifact too* — the vision will be able to show, in CI, exactly how green it is. A north star that
  can report its own completion is the final form of "so we can't go wrong."

---

## 9. Closing

The previous vision said: *V1 ships the cutover; V2 makes it verifiable, reversible, repeatable.*
That earned the engine its existence. The truest north star is one turn deeper:

**The Model and its Substrate are two presentations of one truth; the engine is the proof that the
translation between them — in both directions, on every axis, across time, including the engine's
own opinions — is faithful, deterministic, complete, and self-describing. Fidelity is not a property
a human verifies. It is a theorem the engine proves, continuously, about itself.**

The cutover is the first ring. The Total Projection is the bullseye. We can see it precisely now,
and we have a matrix that tells us, at any moment, exactly how close we are.

Hold the spine. Complete the matrix.

— Recorded for the receiving agent. (North Star, revision 3 of the strategic frame.)
