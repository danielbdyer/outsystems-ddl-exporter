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
owns, executable as a green test for each, and self-describing. But "a witness test exists for
the round-trip" (L1) is not the same as "the round-trip is a faithful isomorphism" (L2), and
neither is the same as "the axes compose into the operation the operator actually runs" (L3).
The bullseye is the matrix at **L3 on every cell** — an **isomorphism ladder**, not a checkbox:

- **L1 — witness present.** A named round-trip test exists and is live (the `matrix-status.sh`
  floor; reached 2026-05-31 at 5/5).
- **L2 — faithful.** The round-trip loses nothing *silently*: `Ingest ∘ Project = id` modulo an
  erasure set that is **named and closed** (a `Tolerance` entry, a structured diagnostic, or a
  fail-loud refusal — never a silent drop). An axis that erases a feature with no surfaced trace
  is L1-but-not-L2.
- **L3 — composed.** The axis is **orthogonal** (no hidden coupling to another axis) and
  participates in the engine's culminating operation: a one-command **A→B migration** with
  minimum-viable touches (diff → rename → CDC-safe deploy → sink-minted transfer → verify).

| Axis the engine owns | L1 — witness | L2 — faithful (erasure named + closed) | L3 — composed into `migrate A B` |
|---|---|---|---|
| **Schema** | ✅ `PhysicalSchema diff` | ◑ *retraction* — 6 facets erased **silently** (user ext-props, FK-trust, identity seed); `CatalogDiff` is kind-level → attribute changes invisible | ⬚ no `diff→ALTER`; full CREATE only |
| **Data** | ✅ `data canary` | ◑ *partial map* — `transfer` drops FK-orphan rows but **exits 0**; cyclic `AssignedBySink` silently wrong; empty-string↔NULL conflated | ⬚ not transactional / resumable |
| **Identity** | ✅ `reload preserves SsKey` | ◑ faithful for `OssysOriginal`; a first-import (`Synthesized`) + rename loses identity **silently** | ⬚ RefactorLog (rename) and Transfer (move) are unreconciled strategies |
| **Time** | ✅ `replayTo genesis` | ◑ *trivial* — `replayTo` is a fetch; `applyDiff` (H-007) unshipped → `applyDiff (between A B) A = B` unproven | ⬚ no minimal-touch emission |
| **Decision** | ✅ `reproduces the DecisionOverlay` (nullability) | ◑ iso on **1 of 3** sub-axes; uniqueness + FK-trust **not read back** | ⬚ tightening can break the Data load (no pre-flight) |
| **— the operation —** | | | **`migrate A B` — EXISTS and runs on SQL Server (2026-06-01, 6.D.1).** `MigrationRun.execute A B cnn` evolves a deployed state-A DB to B in one command — minimum-viable touches (`sp_rename` + logical re-bind, `ALTER`, `ADD`; never a re-CREATE), fail-loud on drops, **data survives**, B' reproduces B (schema-structural), re-run idempotent; records a durable episode whose FTC reproduces B. **Docker A→B canary green** across three channels. T16 (the master equation) is a live witness. Remaining: the `--source-conn`/`--execute` CLI flag wiring + cross-table data transfer. |
| **— meta —** | | | **Self-verification:** the generator reports each cell's *ladder level*, not just witness-presence |

> **This matrix is self-reported.** `scripts/matrix-status.sh` regenerates
> [`NORTH_STAR.matrix.generated.md`](NORTH_STAR.matrix.generated.md) from
> `AxiomTests.fs` + the test tree — an **L1** cell goes green **only** when its
> witness test actually exists and is live (it cannot be asserted by hand). And
> `scripts/verifiability-gate.sh` (slice E1) fails the build if any axiom claims a
> coverage bucket its tests do not support. So criterion 5 (§5) is already
> partially live: **the vision measures its own distance to the bullseye.** Today's
> machine reading (`NORTH_STAR.matrix.generated.md`, 2026-05-31): gate PASS; **L1 = 5/5**
> (Schema, Data, Identity, Time, Decision all witnessed). **L2/L3 is the open work** — the
> six-axis red-team (full record: `AUDIT_2026_05_31_FIVE_AXIS_REDTEAM.md`) measured the gap below
> the floor: every axis is a *partial* iso with at least one **silent** erasure, two axes
> **couple** (Decision→Data, Identity→Schema), the basis does **not span** the operator's
> migration (no Permissions / Transactionality / Connection dimension), and the composed
> `migrate` operation does not exist. The buildable path to L2/L3 is **Wave 6** in
> `EXECUTION_PLAN.md`. **Witness-present ≠ faithful ≠ composed:** the L1 floor is full; the
> ladder above it is the Total Projection's remaining climb.

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

## 3. The six totalities — the structural definition of "done"

The matrix in §1 is reached by closing six completeness conditions. The original four (T-I…T-IV)
make the engine correct and self-describing; the 2026-05-31 five-axis red-team established two more
(T-V orthogonality, T-VI spanning) that the original four assumed but did not name — they are the
difference between five witnessed round-trips and a true, composable basis. These six *are* the
definition of done; each is falsifiable.

**T-I — Round-trip totality (the faithfulness ladder).** For every axis (schema, data, identity,
time, decision), `Ingest ∘ Project = identity` modulo a **named, closed** erasure set — and the
adjunction is an *equivalence* (L2 faithful), not merely a one-sided *retraction* (L1 witnessed).
The distinction is load-bearing: a round-trip can have a green witness yet still erase a feature
**silently** — and a silent erasure is strictly worse than no claim, because it manufactures the
illusion of fidelity. *Counterexample condition:* any axis the engine emits that `Ingest` cannot
observe **without surfacing the loss** (today, per the 2026-05-31 red-team: six schema facets
erased silently; `transfer` drops FK-orphan rows but exits 0; a `Synthesized`-key rename loses
identity silently; `replayTo` is a fetch, not a `fold applyDiff`; uniqueness + FK-trust decisions
are not read back). *Done when:* every erasure is a `Tolerance` entry, a structured diagnostic, or
a fail-loud refusal — never a silent drop — and the matrix generator reports the per-axis **L2**
level, not just L1.

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

**T-V — Orthogonality totality (the axes are a basis, not a bundle).** The five axes compose
without hidden coupling: operating on one does not silently perturb another, and where a genuine
dependency exists it is **structural and surfaced**, not implicit. *Counterexample condition:* a
pair of "independent" axes that secretly couple (today, per the red-team: a `Decision` NOT-NULL
tightening on a column whose source rows carry NULLs makes the `Data` transfer fail mid-load with
no pre-flight; an `Identity` rename diverges the physical coordinates the `Data` transfer matches
on, with no RefactorLog consumed by Transfer). *Done when:* each cross-axis dependency is a named
pre-flight or a typed input — a `migrate` that validates source data against the tightened sink
schema *before* it writes; a Transfer that consumes the rename map.

**T-VI — Spanning totality (the basis covers the operation).** The axes span what the operator's
real operation requires — there is no load-bearing dimension the migration needs that lives in no
axis. *Counterexample condition:* a dimension the A→B migration cannot proceed without that the
engine cannot represent (today: **Permissions/Security** — no axis carries grants/roles/RLS, so a
write-denied sink silently transfers zero rows; **Transactionality/Rollback** — a mid-transfer
failure leaves a half-populated target with no atomic boundary, idempotent retry, or rollback;
**Connection pre-flight** — no "both endpoints live + credentialed" check before mutation begins).
*Done when:* the missing dimensions are represented as axes or enforced as pre-flight gates on the
composed operation, and the one-command `migrate A B` is atomic-or-resumable end to end.

**The unification:** T-I and T-III make the engine *correct on every axis, in both directions,
across time*. T-V and T-VI make those axes a **true basis** — faithful and orthogonal enough to
**compose** into the operator's one-command A→B migration (the L3 bullseye), which is the
forcing instance that exercises all six totalities at once. T-II and T-IV make the engine *able to
prove and describe its own correctness without a human re-auditing it*. Together they are the
difference between a tool that is right and a tool that can **show** it is right, continuously.
That difference is the entire reason "replace V1" is a stronger claim than "trust V1" — and it is
why the north star is not "verify the cutover" but "make verification self-sufficient."

> **What the totalities quantify over.** The six totalities are predicates; the entities they range
> over — State · Comparison · Intent-Filter · Plan · Channel · Gate · Execution, and the *core moves
> of change* (Add / Remove / Rename / Reshape / Reidentify / Move / Accumulate) — are pinned in
> `WAVE_6_ONTOLOGY.md` (the 2026-06-01 masterwork). T-I quantifies over the comparison + the emission
> functor's partiality; **T-V** over the channels (partition + ordering); **T-VI** over the gates
> (completeness *is* "spanning"). The ontology is *right-by-function* armor: each entity carries a
> **discriminating predicate** (the input on which a plausibly-named-but-wrong implementation
> diverges) so the engine is structurally isomorphic to the shape of change, not merely named after
> it. Read it before opening any Wave 6.A.10+/6.B/6.C/6.D/6.F slice.

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
8. **Move the whole estate from A to B in one command, touching only what changed.** The composed
   operation — `migrate A B` — diffs the two states, renames rather than drops, deploys only the
   minimal delta CDC-silently, transfers the data with identity preserved-or-reconciled, and
   verifies the result; it is atomic-or-resumable, and it refuses (loudly) rather than corrupting
   the target. *(the L3 bullseye; composes promises 3/4/5/6 into one act. Today: unbuilt — see
   the `migrate` slice in Wave 6.)*

Promise 7 is the keystone of *trust* (the guarantee about the guarantees); promise 8 is the keystone
of *use* — the operator's actual day, reduced to one verb. Promise 8 is the L3 face of the whole
matrix: it cannot hold until every axis is L2-faithful and T-V/T-VI orthogonality + spanning are
closed, which is precisely why it is the forcing instance that pulls the entire ladder up. It is
what makes "we can't go wrong" literally true: the operator never has to wonder whether a promise
holds — the engine answers, the answer is itself tested, and the migration either completes
verifiably or refuses without damage.

---

## 5. Falsifiable acceptance criteria

The north star is reached when all of the following hold simultaneously. Each is a test or a
generated artifact, not a judgment.

1. **The round-trip matrix is fully green — at L2, not just L1.** Every (axis × round-trip) cell of
   §1 is a passing property test at canary scale: schema (all features, not six-blind), data,
   identity, time, decision. And each is **faithful (L2)**: the matrix generator reports the
   ladder level, every erasure is a `Tolerance` / diagnostic / fail-loud refusal (no silent drop),
   and the per-axis red-team counterexamples (silent schema erasures, exit-0 row drops,
   `Synthesized`-rename identity loss, `replayTo`-is-a-fetch, unread uniqueness/FK-trust) are
   closed. Tracked as a green count with zero `Skip` on Tier-1 cells.
1b. **The axes are an orthogonal, spanning basis (T-V + T-VI).** No "independent" pair couples
   silently (Decision↔Data and Identity↔Schema dependencies are surfaced pre-flights, not implicit
   failures); and every dimension the migration needs is represented or gated — Permissions,
   Transactionality/Rollback, Connection pre-flight.
1c. **The composed operation exists and round-trips (L3).** `migrate A B` runs the full
   diff→rename→deploy→transfer→verify in one command with minimum-viable touches, atomic-or-
   resumable, and a green A→B canary witnesses that B reproduces A modulo the named, declared
   changes. *(Promise 8; the L3 bullseye.* **LANDED 2026-06-01, 6.D.1 — including live execution
   on SQL Server:** `MigrationRun.execute A B cnn` evolves a deployed state-A database to B in one
   command (`sp_rename` + logical re-bind, `ALTER`, `ADD` — minimum-viable, never a re-CREATE),
   fail-loud on drops, with **data preserved**; B' reproduces B at the schema-structural level and
   the re-run is idempotent; the run records a durable episode whose FTC reproduces B. **Column
   renames** (`sp_rename … 'COLUMN'`) and the **cross-substrate data load** (`executeWithData` —
   schema-migrate the sink, then transfer + re-key the rows) also run live: `migrate` moves schema
   *and* data in one composition. The **Docker A→B canary** witnesses it. T16 (the master equation)
   is a live witness. The remaining reach is the `--source-conn`/`--sink-conn`/`--execute` CLI flag
   wiring — the live square commutes, schema and data.*)
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

Criteria 1–5 (including 1b/1c) are the *new* north star (the Total Projection); 1/1b/1c are the
**isomorphism-substantiation climb** (L1 floor → L2 faithful → L3 composed) that the 2026-05-31
red-team scoped and Wave 6 builds. Criterion 7 is the rev-2 north star, now a **strict subset** —
the cutover is the first ring of the bullseye, not the bullseye.

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
  totality (T-I…T-VI) it closes, or which rung of the isomorphism ladder (L1→L2→L3) it raises. If
  it advances none, it is probably not on the path.
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
