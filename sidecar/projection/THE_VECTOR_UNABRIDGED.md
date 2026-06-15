# THE VECTOR — Unabridged Edition

### From the Witnessed Floor to the Total Projection: the complete annals of the amelioration study

> **What this is.** The unabridged companion to [`THE_VECTOR.md`](THE_VECTOR.md). Same vector, same north star,
> same twenty canonical moves — but with the full apparatus the executive edition compresses away: every one of
> the nine lenses in full (all sixty-one methodologies, with mechanics and risk), the complete construct
> catalogue of all eight facets, the three adversarial verdicts in their own words, and the entire ranked table
> of one hundred and twenty-three recommendations. Read `THE_VECTOR.md` for the argument; read this for the
> evidence behind every sentence of it.
>
> **How the two relate.** The executive edition is the cut stone; this is the lapidary's full notebook — the
> facets considered and rejected, the inclusions mapped, the grain read from every angle. Nothing here
> contradicts the executive edition; everything here *grounds* it. Where the executive edition says "five lenses
> converged on the keystone," this edition shows you the five, in full, and the two reviewers who verified the
> convergence and the one who caught the echo-chamber next door.
>
> **Provenance.** Synthesized 2026-06-15 from a five-phase multi-agent study (run `wf_b432bd10-850`): 8
> structural cartographers · 9 analytic lenses · 3 inverse/opportunistic scouts · 3 adversarial reviewers over
> 123 ranked recommendations. Indexed by `THE_USE_CASE_ONTOLOGY.md`; sits under `NORTH_STAR.md`; cites
> `AXIOMS.md`, `PRODUCT_AXIOMS.md`, `DECISIONS.md`, and the code. Every claim is falsifiable or cited to a
> `path:concept`.
>
> **The standing fact for this reading.** The Reverse-Leg DML execution backlog is treated as **done** — the
> movement engine (streaming, `CaptureJournal`, MERGE-OUTPUT sink-minting, packed surrogate remap,
> `MigrationRun.execute`) is shipped, even where the tree does not yet reflect it. The interesting question is
> not *build it* but *what its completion has now made ready to cash* — and, sharper, *what it has now made it
> possible to corrupt in silence.*

---

## Part 0 — Orientation

### 0.1 How to read this

This edition has a spine and an apparatus. The **spine** (Parts I–IX) is the argument, expanded: what we aim at,
what the engine is, what nine lenses found, where the convergences and the one echo-chamber are, where the
algebra wants more structure, what is absent and what is ready, the toolbox of moves, and the order to make them
in. The **apparatus** (Appendices A–F) is the full record: the method, the move-table, the construct catalogue,
the verdicts, the complete ranked recommendations, and the deferral ledger.

If you have read the executive edition, the new material is concentrated in three places: **Part III** gives all
nine lenses their complete methodology lists (the executive edition summarized them in a table); **Part II**
carries the per-facet construct detail (the executive edition gave only the essence and the principal tension);
and **Appendices C, D, E** are wholly new — the construct catalogue, the adversarial verdicts verbatim, and the
123-row table. If you have *not* read the executive edition, you can read this one standalone; §0.2 is the
one-page digest that the executive edition opens with.

A word on length and on restraint. This edition is long because the research was deep, not because length is a
virtue. The discipline that governs the engine governs this document: every methodology is gated, and a third of
what the lenses proposed was killed, deferred, or revised by the adversarial layer. Those refusals are kept —
they are the most instructive part. A planning document that cannot say "not yet, and here is the trigger" is a
wish-list, and this codebase has earned better than a wish-list.

### 0.2 The vector in one page

The engine is a publication-and-provenance engine racing toward an **eject** after which there is no upstream to
re-derive from. Its soul is one adjunction — `Ingest ∘ Project = identity`, modulo a **named, closed** erasure
set — lifted from states to displacements ("state is a torsor over delta"). Its north star is that adjunction
made **total** (every axis), **faithful** (no silent erasure — L2), and **composed** (the one-command
`migrate A B` — L3). The L1 floor is full (5/5 witnessed); the composed operation exists and runs. What remains
is the L2/L3 climb, and the climb is not "build more" — it is **make the honest machine see further** until what
the engine *claims* and what it can *show* are the same set.

The single most important finding: **the Decision axis claims faithful but cannot be falsified.** FK-trust
(`NoCheckFk`) and uniqueness-promotion (`EnforceUnique`) decisions are emitted into DDL and recovered by
`ReadSide`, but the comparison surface (`PhysicalForeignKey`) has no field to carry them, so the decision is
discarded at the diff boundary — and because all eight `ToleratedDivergence` tolerances tag only Schema/Data, the
matrix's under-claiming mechanism is *structurally inert* on Decision/Identity/Time. Five independent lenses
converged on the fix; the read leg is already free; it is a true uncashed corollary, not a new feature.

The twenty canonical moves fall into five kinds — *raise a ladder rung*, *strengthen a primitive*, *compress
through an IR*, *deepen the algebra*, *convert convention into a fitness function* — plus corollary cashes and
the honest absences. They sequence into four waves: **Wave 0 honesty & fitness** (the cheapest moves, which stop
the over-claim and make the guardrails un-rottable), **Wave 1 the keystone** (the Decision-readback adjunction),
**Wave 2 the reversible algebra and the real-wire proof**, **Wave 3 compression**, **Wave 4 corollary cashes**.
The single move to make first is the pair `M1′ + M2` — name the Decision erasure and the silent trigger-drop —
because an engine whose soul is "fidelity is a theorem it proves about itself" cannot tolerate a green cell that
is green only because the machine has no way to mark it otherwise.

### 0.3 The method, in one paragraph

Thirty agents over five phases produced ~8M tokens of grounded research. The load-bearing move was the
adversarial verification layer: three perspective-diverse reviewers (groundedness, discipline, leverage) ranked,
revised, and killed across the pooled catalogue. They confirmed the keystone as the best-grounded recommendation,
corrected the quantitative claims on disk (112 citation sites, 42 Skip stubs, *seven* emitter targets, the writer
"trinity" is really a linear writer only), and — most valuably — stopped a four-lens convergence from writing a
regression into the masterwork. The full method is Appendix A.

---

## Part I — The Bullseye, Re-derived

### I.1 Why fidelity must be a theorem

Start from the premise, because the premise forces everything. The engine is a **publication-and-provenance
engine for an evolving relational model**, sourced today from an OutSystems estate, publishing schema and data to
on-prem SQL Server and to an external team's SSIS jobs, accumulating an exact replayable provenance of every
change, and **terminating one day at an eject** — after which there is no upstream to re-derive from.

That last clause is the whole reason for the engine's character. OutSystems' own deployment is opaque and
platform-controlled: *trust me*. This engine is the parallel, SQL-native, repo-tracked, PR-reviewed pipeline that
answers *prove it* — and then must hand the proven model and its provenance to another team and freeze it. When
there is no upstream left to regenerate from, "we believe the schema is right" is not a posture you can hold. The
published model and its provenance must be **exactly** right, and the operator must be able to *see* that they
are right without re-auditing by hand.

This is why fidelity cannot be a property a human verifies. A human-verified property degrades to conjecture the
moment the human stops looking, and the eject is precisely the moment everyone stops looking. Fidelity must be a
**theorem the engine proves about itself, continuously** — a checkable state, not an aspiration. Every move in
this document is, ultimately, in service of that one shift: from *asserted* to *shown*.

### I.2 The one law, and the nine corollaries

The engine's formal soul is a single adjunction between a logical Model and a physical Substrate:

```
Project : Model     ──►  Substrate      (Π — the emit leg)
Ingest  : Substrate ──►  Model          (the reader leg)
Law     : Ingest ∘ Project = identity   (modulo a named, closed erasure set)
```

The phrase *modulo a named, closed erasure set* is the entire ethic compressed to four words. The law is not
"nothing is lost" — some things genuinely cannot survive the round-trip, and pretending otherwise is the lie. The
law is **nothing is lost in silence**: every erasure is a `Tolerance` entry, a structured diagnostic, or a
fail-loud refusal. A silent erasure is strictly worse than no claim, because it manufactures the *illusion* of
fidelity — and the illusion is what the eject cannot afford.

From that one law, nine capabilities fall out as corollaries — which is the reason the system can be both small
and complete:

1. **Emitters** (the sibling chorus) are `Project` specialized to a target. They are siblings because they
   project the *same* Model; they agree (T11) because they project the same keyset.
2. **The canary** is the law at runtime: `Ingest ∘ Project = id` checked against an ephemeral database.
3. **Drift detection** is the law's failure surfaced: `Ingest(deployed) ≠ Model` is a diff.
4. **Remediation** is the law applied to a delta: `Project(Model ⊖ Ingest(deployed))`.
5. **Transfer** is the law run across *two* substrates and extended from schema to data.
6. **Refactor logs / evolution** are the law across *time* — the morphisms between Model versions.
7. **The decision overlay** is the law extended to the engine's own *opinions*: a tightened schema, read back,
   must reproduce the decisions that shaped it.
8. **The composed migration** (`migrate A B`) is the law across two *states*: diff → rename → CDC-safe deploy →
   sink-minted transfer → verify.
9. **Self-verification** is the law applied to the engine's *claims*: the coverage map is itself generated from
   the proof.

Nine capabilities, one law. This yields the sharpest planning heuristic the engine owns, and the rule against
which every recommendation in this document was tested: **a proposed feature that is not a corollary of the
adjunction is probably the wrong feature.** When a slice feels like "build the reverse of X," the question is
"what is X's `Ingest` peer, and does the existing direction-neutral plan already serve it?" The answer is almost
always yes. This is how the engine stays small while its coverage grows.

### I.3 The law, lifted: state is a torsor over delta

The engine does not stop at comparing two *states*. Its Wave-6 change algebra lifts the adjunction from states to
the **displacements between them**, and this lift is the most beautiful idea in the codebase. The structure is a
torsor: a set on which a group acts freely and transitively, so that any two elements are related by exactly one
group element. Here the set is *States* (Catalogs) and the group is *Deltas* (`CatalogDiff`):

- `between A B` (written `⊖`) is the unique displacement from A to B — the group element relating the two states;
- `applyDiff` (written `⊕ : State × Delta → State`) is the torsor action — apply a delta to a state, get a state;
- `compose` is the partial group composition of adjacent deltas, *typed by endpoints* (it returns `None` on a
  non-adjacent pair — fail-loud, never silently wrong);
- `norm` is the additive metric on deltas — and the operative minimality measure is **physical**: the CDC capture
  count is the data norm, because a full reload re-touches every row, so minimality is not elegance, it is
  availability.

`AXIOMS.md` reifies this as **T12–T16 + A43**: T12 state-is-torsor-over-delta, T13 the Chasles/fundamental-theorem
replay (`replay = fold ⊕`), T14 direct-sum orthogonality (`δ = ⊕_c π_c δ` over the channels), T15 CDC-count-as-
norm (emission is an isometry), T16 the Project commuting square (`applyTo (plan A B) A = B`, which *is*
`migrate A B`). Each is bound to a *discriminating* witness — the input on which a plausibly-named-but-wrong
implementation diverges (the `applyDiff` that ignores its argument and returns the stored target passes the
happy-path fixture and fails the no-cheat property).

The working gestalt this buys is exact: **the engine is an accounting system for change.** Identities are
conserved charges; displacements are transactions counted by two independent rulers (CDC for fidelity, `Bench`
for cost); append-only ledgers hold the partial sums (the episode store, the capture journal, the refactorlog);
and the round-trip canaries are the audits. Once you see the engine this way, the whole of Part VII reads as a
single instruction: *keep the books balanced, and make the balance checkable.*

### I.4 The shape of "done": the ladder and the six totalities

The bullseye is not "a round-trip test exists." A test can be green while an axis silently erases a feature. So
the target is a **ladder**, climbed per axis:

- **L1 — witness present.** A named round-trip test exists and is live. (Reached: 5/5 across Schema · Data ·
  Identity · Time · Decision.) The matrix cannot mark an L1 cell green by hand: the witness test must physically
  exist for the generator to find it.
- **L2 — faithful.** The round-trip loses nothing *silently*: every erasure is named and closed. The matrix caps
  an axis at L2-partial iff a live `OpenGap` tolerance sits on it — and an L2 cell goes green only when that
  tolerance is *deleted from source*. (This is the crux of the most important finding: the mechanism is
  structurally inert on three axes, §II.4.)
- **L3 — composed.** The axis is orthogonal (no hidden coupling) and participates in the engine's culminating
  operation: the one-command `migrate A B`, atomic-or-resumable, which refuses loudly rather than corrupting the
  target.

Six totalities define when the ladder is fully climbed — and they are *predicates*, each with a counterexample
condition, not slogans:

| Totality | The condition of "done" | Where it bites today |
|---|---|---|
| **T-I** round-trip faithfulness | every axis is `Ingest∘Project = id` modulo a **named, closed** erasure | the Decision axis claims faithful with **zero** named erasures (§II.4) |
| **T-II** executable-axiom totality | every axiom has a green witness or a Skip-with-trigger; a gate forbids phantom claims | `citationOf` is a no-op string trail (§II.5) |
| **T-III** input totality | the four-input algebra `Catalog × Policy × Profile × Lifecycle` is complete | Lifecycle is operational; the binding seam is the live edge (§II.6) |
| **T-IV** documentation-from-proof | the coverage map is *generated* from the proof, not authored | half-built: the matrix is generated, the axiom prose is not (§II.5) |
| **T-V** orthogonality | the axes are a basis, not a bundle — no hidden coupling | Identity↔Schema still partly couples at the reader leg (§II.3) |
| **T-VI** spanning | the basis covers the operation's real dimensions | Transactionality/Rollback has **no machine surface at all** (§VI.1) |

The unification is the thing to hold: T-I and T-III make the engine *correct on every axis, in both directions,
across time*; T-V and T-VI make those axes a true *basis* that **composes** into the operator's one-command
migration; and T-II and T-IV make the engine able to *prove and describe its own correctness without a human
re-auditing it*. That last clause is the entire reason "replace V1" is a stronger claim than "trust V1."

### I.5 The operator's covenant — the eight promises

The matrix is the engine's promise to itself; the covenant is its promise to the operator (the DBA / migration
engineer driving the estate). It is the pillar-9 skeleton/overlay contract generalized, and it is falsifiable end
to end. Worth restating in full because every move in Part VII serves one of these eight:

1. **Ask for the vanilla projection, get a deterministic factual baseline.** `Project(model, Policy.empty,
   profile)` is the skeleton: no opinion, byte-identical across runs, machines, and time. *(skeleton-purity)*
2. **Every opinion you apply is named, classified, and recorded.** Each override is a registered `OperatorIntent`
   overlay on a named axis; the manifest names every overlay that touched every artifact. *(overlay-exercise)*
3. **Deploy it and redeploy it with zero surprise.** Idempotent redeploy emits zero spurious CDC change records;
   the diff-deploy ALTERs only what genuinely changed. *(CDC-silence — the highest-stakes single guarantee)*
4. **Move data in either direction and get it back unchanged — modulo what you declared could change.** The
   data-level adjunction holds; identity is preserved or reconciled by a named rule, never silently.
5. **Rename freely; identity survives.** A physical rename never re-keys a logical entity; refactor intent is
   carried so deploys ALTER, not DROP+CREATE.
6. **Nothing disappears in silence.** Any concept the engine cannot carry surfaces as a structured diagnostic.
7. **At every moment, the engine can show you which of these promises are proven and which are not.** The
   coverage map is generated, current, and machine-checked. *(the keystone of trust)*
8. **Move the whole estate from A to B in one command, touching only what changed.** The composed operation,
   atomic-or-resumable, refusing rather than corrupting. *(the keystone of use; the L3 bullseye)*

Promise 7 is why the engine can say "we can't go wrong": the operator never has to wonder whether a promise holds
— the engine answers, the answer is itself tested. And promise 7 is exactly what the Decision-axis finding
threatens, because on three axes the engine currently answers "proven" when the honest answer is "unfalsifiable."
That is why the keystone move is the keystone.

### I.6 Where we stand on the ladder

The honest position, reconciled against the code: **L1 is full (5/5). The composed operation exists and runs on
SQL Server.** What remains is the L2/L3 climb — and the gap is narrower and sharper than it looks. It is not a
field of missing features. It is a small, named set of places where the engine **says more than it can show**: an
axis marked faithful that has no detector for its own erasure; a deepest law proven on a *model* of the substrate
rather than the substrate; a guardrail enforced by a bash script rather than a type. The vector from here to the
bullseye is therefore not "build more." It is **make the honest machine see further** — extend the named surfaces
until what the engine claims and what the engine can demonstrate are the same set. That is the through-line of
every section that follows, and the reason the first wave of the roadmap is *honesty*, not *features*.

---

## Part II — The Current Nature, in Full

This is the map of the engine as it actually stands, facet by facet: the load-bearing constructs (named, with
their files), the algebra already realized, and the principal tension. The construct lists here are abridged from
the full catalogue in Appendix C; this part reads them into prose. It is a portrait an architect could navigate
by — strengths *and* debt, because a love letter that omits the debt is flattery, and this codebase is too good
to flatter.

### II.1 The compiler heart — the IR and the pass algebra

The core is two interlocking algebras. The **Catalog IR** is a four-level nested coproduct
(`Catalog → Module list → Kind list → (Attribute|Reference|Index|ModalityMark) list`, `Catalog.fs`) where
identity is `SsKey` everywhere, equality-by-identity is a named function (`Kind.byIdentity`, A4), and the
aggregate root `Catalog.create` rejects dangling FKs, illegal trust-quadrants, and storage-type mismatches in
*one fused walk* — so the structural invariants are unforgeable. It is value-pure yet O(1)-indexed via per-
instance `ConditionalWeakTable` caches (`Catalog.kindIndex`, `Kind.attributeIndex`): purity without the
linear-scan tax.

The **passes** are arrows in a Kleisli category over a WriterT-stacked dual writer:
`Pass<'a,'b> = 'a -> Lineage<Diagnostics<'b>>` (`Diagnostics.fs`), with `id`, `compose` (`>=>`), and `composeAll`
whose left/right-identity and associativity laws are *theorems over the bind*, property-tested in
`KleisliLawTests`/`DiagnosticsTests`. The whole Core pipeline is **one value** —
`RegisteredTransforms.chainSteps : ChainStep list` — from which both the metadata registry (`all`) and the
execution chain (`allChainStepsFor`) *project*, so `transform.registered` provably equals what runs and the
three-parallel-list drift the design once suffered is structurally impossible.

The load-bearing constructs:

| Construct | File | Role |
|---|---|---|
| `Catalog / Module / Kind / Attribute / Reference / Index` | `Catalog.fs` | the nested-coproduct IR; `Catalog.create` fuses every aggregate invariant into one walk; `ConditionalWeakTable` caches give value-pure O(1) lookup |
| `Lineage<'a>` + `lineage { }` CE | `Lineage.fs` | the linear writer monad; `[<CustomEquality>]` projects through `Value` only (A26); `bind` concatenates trails chronologically (A24) |
| `Diagnostics<'a>` + `Pass` + `>=>` + `composeAll` | `Diagnostics.fs` | the second writer + the WriterT stack; defines the Kleisli arrow; the laws are theorems over `LineageDiagnostics.bind` |
| `Lens` / `CatalogLenses` | `Optics.fs` | total bidirectional accessors (the three optic laws tested); `Lens.over` is the get-modify-set every catalog-rewriting traversal uses |
| `ComposeState` + `with*` setters | `ComposeState.fs` | the aggregate evidence accumulator: a `Catalog` field + one `Option` per analytic pass, making the registry fold well-typed |
| `PassChainAdapter` + four `lift*` | `PassChainAdapter.fs` | the type-erasure boundary homogenizing heterogeneous `'Out` into `Pass<ComposeState,ComposeState>` |
| `ChainStep` | `PassChainAdapter.fs` | the single-definition-site for a pass: `{ Metadata; Build }` — both metadata and execution project from one value |
| `Composition.fanOut` | `Strategies/Composition.fs` | the earned (4-consumer) intervention combinator; the four tightening passes are thin `FanOutConfig` constructions |
| `TopologicalOrderPass` | `Passes/TopologicalOrderPass.fs` | Kahn + Tarjan + resolver, permutation-invariant by SsKey-sorting every boundary; the canonical mixed-classification example |

The algebra already realized is deep: a Kleisli category, a WriterT monad-transformer stack over a product
monoid, total lenses, the nested-coproduct module decomposition (T2), endofunctor passes (A19), the fan-out
combinator as a distributive law, reified mutation as a derive-macro (`LineageBuffer.Buffer`), and memoized
value-pure indexing.

**The principal tension** is the `ComposeState` open-product: it grows one `Option` field + one `with*` setter
*per analytic pass* (12 today), and the lift surface has four near-identical `lift*` functions plus two bespoke
inline `ChainStep` literals (`SchemaComplexity`, `cascadeShock`) for the passes that did not fit a builder. Each
new analytics pass costs a record field, a setter, and a lift shape — a linear tax that compounds exactly as the
basis (T-VI) gains its missing dimensions. This is the natural home of a future typed evidence-map, *but that
abstraction has not earned its second shape yet* (Part VII keeps it deferred). A smaller tension: `Types.fs` still
carries a near-vestigial `Pass<'output>` alias colliding in name with the live Kleisli `Pass<'a,'b>` — a naming
collision pillar 6 (no V2 back-compat) would retire.

### II.2 The sibling Π emitters and the typed-AST discipline

`Project` is realized as a family of sibling emitters that all consume a subset of `Catalog × Profile` — **never
Policy** (A18, structurally enforced: the `Emitter<'element>` alias *cannot name* `Policy`) — and produce one of
two canonical description forms: an indexed `ArtifactByKind<'element>` (T11 keyset-by-construction, made
unforgeable by the private-ctor derive-macro) or a flat `seq<Statement>` (A35, the typed deterministic statement
stream whose realization is invisible to Π). Strings genuinely emerge only at terminal boundaries
(`Sql160ScriptGenerator.GenerateScript`, `Utf8JsonWriter`).

| Construct | File | Role |
|---|---|---|
| `Emitter<'element>` / `EmitterWithProfile` / `EmitterOverDiff` | `Types.fs:50` | the Π port; `'element` is the only thing that varies across siblings; encodes A18 at the type level |
| `ArtifactByKind<'element>` | `ArtifactByKind.fs` | private-ctor map whose smart ctor enforces strict keyset equality with `Catalog.allKinds`; makes T11 a type theorem; `perKind` is the universal per-kind fold |
| `EmitError` | `ArtifactByKind.fs:17` | the closed Π error envelope; `OverlappingEmitterCoverage` is the data-triumvirate partition witness |
| `Statement` | `Statement.fs:269` | the ~35-variant closed DU that IS Π_SSDT's typed stream (A35); its `ALTER*` variants are the change-algebra migration leg |
| `ScriptDomBuild.buildStatement` | `ScriptDomBuild.fs` | the total pure `Statement -> TSqlFragment option`; the derive-macro heart; includes the typed MERGE/UPDATE the data emitters consume |
| `ScriptDomGenerate` | `ScriptDomGenerate.fs:50` | the terminal SQL-text boundary with every byte-affecting option pinned (T1 byte-determinism constructed) |
| `SsdtDdlEmitter` | `SsdtDdlEmitter.fs:684` | the flagship sibling; produces both `ArtifactByKind<SsdtFile>` and a flat `seq<Statement>` from the same builders |
| `ConstraintFormatter` | `ConstraintFormatter.fs` | the largest residual string-PARSING surface — a `string -> string` post-processor re-parsing ScriptDom's own emitted text |

> **A count to correct, once.** There are **seven** emitter targets, not six. The often-cited "six siblings"
> omits `Projection.Targets.OperationalDiagnostics` (`RemediationEmitter`, `DecisionLogEmitter`,
> `SuggestConfigEmitter`, `ActionableDiagnostics`, `Routing`) — precisely where two of the nine corollaries live
> as artifacts: *remediation = the law on a delta* and *the decision overlay = the law on the engine's own
> opinions*. `DecisionLogEmitter` already emits the decision overlay as an operator artifact, which matters
> enormously for §II.4: the Decision axis's **emit** half is built and shipped; it is only the **readback** half
> that is missing.

The algebra realized: a per-kind functor (`perKind`), the derive-macro (`ArtifactByKind`), closed-DU dispatch as
a total natural transformation (`Statement -> TSqlFragment`), the free-monad-flavored description/interpretation
split (A35/A36), the coproduct/partition algebra of the data triumvirate, the torsor change-algebra surfacing in
the `ALTER*` variants, and a monoid on `seq<Statement>` under concatenation.

**The principal tension** is `ConstraintFormatter`: the one place the codebase violates its own first pillar. It
re-parses ScriptDom's *already-emitted* SQL line-by-line with `IndexOf`/`Substring`/`Split` to reshape
constraints into V1's elegant form — a string→string transform sitting *after* the typed AST, throwing the typed
structure away and recovering it by string surgery. The file's own header concedes that subclassing the generator
was "considered and rejected (visibility lift cost too high)." This is the single largest gap in the typed-AST
claim. Three smaller siblings: the statement stream is realized by *two* near-identical interpreters
(`Render.toText` and `ScriptDomGenerate.toText`) that the "A40 one rendering algorithm" comment claims unity the
code only partly delivers; the JSON `Utf8JsonWriter → MemoryStream → byte[] → JsonNode.Parse` dance is duplicated
verbatim across four-plus sites; and `DataInsertScript` stores both typed rows *and* their pre-rendered strings,
half-defeating A35.

### II.3 The reader leg and the movement engine (the adjunction made operational)

This is the `Ingest` leg made executable and the change algebra made to run. The reader leg is `ReadSide.read`
(INFORMATION_SCHEMA/`sys.*` → a `Catalog` whose attribute SsKeys are *synthesized from physical coordinates* —
best-effort, not lossless). The differential is a clean two-layer split: the **observational** differential
`CatalogDiff.between` partitions `source ∪ target` SsKeys into four pairwise-disjoint move classes and descends
through five channels; the **operational** differential (`Migration.plan` → `diff→ALTER` + RefactorLog) is the
plan the operator reviews. On top sits the torsor (`norm`/`compose`/`applyDiff`). The movement engine is
`Transfer`/`MigrationRun`: a two-phase NULL-then-FK load with sink-minted identity reconciliation (the
`AssignedBySink` MERGE-OUTPUT capture ladder feeding a `PackedSurrogateRemap`), a streaming bounded-memory
realization with a chunk-resume `CaptureJournal`, and CDC capture count as the data norm.

| Construct | File | Role |
|---|---|---|
| `CatalogDiff.between / applyDiff / compose / norm` | `CatalogDiff.fs` | the observational differential, its action peer (H-007), the partial groupoid composition, and the schema-side norm |
| `ChannelDiff<'change>` | `CatalogDiff.fs:62` | the one generic carrier (Added/Removed/Renamed/Changed) behind all five channels — four byte-identical records collapsed into one |
| `Migration.plan / applyTo` | `Migration.fs` | the plan-time view + the pure T16 master equation; the `LossDeclaration` gate ranges over the complete `SchemaLoss` enumeration |
| `MigrationRun.execute / executeWithData` | `MigrationRun.fs` | live `migrate A B`: emit→preflight→deploy→canary, `sp_rename`+ALTER minimum-viable touches, read-back B' and verify |
| `Transfer.runCore` + `writePlan` / `writePlanStreaming` | `TransferRun.fs` | the data-direction adjunction leg; the two realizations (materialized-with-reconcile vs streaming-no-reconcile, NM-31) |
| `SurrogateRemap` (`SourceKey`/`AssignedKey`, `remapRowFksWith`) | `SurrogateRemap.fs` | the generic identity-crossing carrier, A40-harmonized over an injected lookup |
| `CaptureJournal` | `CaptureJournal.fs` | the client-side chunk-resume ledger; resume replays journaled pairs; drift refuses by name |
| `ReadSide.read` | `ReadSide.fs` | the Ingest leg — best-effort, synthesizes attribute SsKeys, defaults V2-IR-only axes |
| `ChangeManifest.between / pathLength` | `ChangeManifest.fs` | the change-manifest of δ for the SSIS consumer; the CDC capture count as the realized data norm |

The algebra realized here is the richest in the codebase: a groupoid/torsor on `CatalogDiff`, a functor-pair
adjunction (`Transfer`/`ReadSide`), the free generic `ChannelDiff<'change>`, the smart-ctor partition law, the
A40 parameterized-lookup harmonization, a surjection from operator user-match choices onto one reconcile
machinery, a ledger-of-replay, a coproduct-as-gate (`LossDeclaration`/`SchemaLoss`), and the norm (T15). With the
movement engine done, `migrate A B` exists, runs on SQL Server, and composes the whole arc — T16 is a live witness.

**The principal tensions** are three. First, **Schema and Identity still couple at the reader leg** (a T-V
violation): `ReadSide` synthesizes attribute/kind SsKeys from physical coordinates, so on a non-V2-authored
source a rename changes the key and lands in `Removed + Added` rather than `Renamed`. `recoverKindSsKey` recovers
*kind* identity from the V2.SsKey extended property, but attribute SsKeys still re-synthesize — so the residue is
at the **attribute grain only** (§IV.3 will show why the chorus's first fix for this was wrong). Second, **two
near-identical write engines** duplicate the two-phase orchestration across row carriers — a clean compression
candidate, and the reason the streaming arm cannot yet offer the sink-untouched guarantee (NM-31). Third,
**`compose` recomputes `between` twice**, discarding the channel structure it already holds — an O(N log N)
re-derivation where a true torsor `+` would patch the channels (Part V picks this up).

### II.4 Policy, the decision algebra, and the silent fifth column

Policy is a five-axis orthogonal product (`Selection · Emission · Insertion · Tightening · UserMatching`, A12
amended) whose neutral element `Policy.empty` is a first-class input and whose composition is two homomorphic
monoids (`merge`, and the `PolicyExpr` combinator DSL with a structure-preserving `eval`). The Tightening axis is
not a mode enum but a *registry of named interventions* (`Nullability | UniqueIndex | ForeignKey |
CategoricalUniqueness`), each a thin `Composition.fanOut` config — the traversal collapsed, the strategies
injected. The decision sets collapse into `DecisionOverlay` (A18-safe: a fact derived from evidence-under-intent,
consumed by the SSDT emitter as a curried prefix so the emitter port stays Catalog-only).

| Construct | File | Role |
|---|---|---|
| `Policy` (5-axis) + `merge` | `Policy.fs` | the orthogonal product; `merge` is the applyDelta monoid (commutative on disjoint axes) |
| `PolicyExpr` + `eval`/`simplify` | `PolicyExpr.fs` | the typed combinator DSL; `eval` is the structure-preserving homomorphism; And/Or are the Selection lattice |
| `TighteningIntervention` (closed DU) | `Policy.fs` | the registry-of-named-interventions replacing V1's mode enum |
| `Composition.fanOut` + `StrategyEvaluator` | `Strategies/Composition.fs` | the single parameterized evaluator all four tightening passes share |
| `DecisionOverlay` + `ofComposeState` | `DecisionOverlay.fs` | the A42 decision→emission fidelity carrier; `ofComposeState (initial c) = empty` is the byte-identity seam |
| `Tolerance` + `ToleratedDivergence` | `Tolerance.fs` | the erasure-naming algebra: 8 named variants, each with a machine-parsed `@ladder` tag |
| `VersionedPolicy.bumpKind` | `VersionedPolicy.fs` | the discrete change-delta classifier; `digestOf` uses `sprintf "%A"` (the one determinism-by-luck digest) |

The algebra is genuinely rich: a commutative monoid, a free term algebra with an evaluation homomorphism, a
bounded lattice on Selection with a De Morgan duality, a parameterized catamorphism (`fanOut`), closed-DU
coproducts with coverage retractions, a quotient by a named congruence (Tolerance), a writer monad over the
decision algebra, a graded/versioned object, and smart-constructor refinement types (`NullBudget ∈ [0,1]`).

**The principal tension is the most important single finding in this document.** *The decision adjunction is not
read back.* The SSDT emitter consumes all four overlay sets and emits `WITH NOCHECK` (untrusted FK,
`SsdtDdlEmitter.fs:377`) and `UNIQUE` (promoted index, `:451`) from the `DecisionOverlay`. The reader leg
*already* recovers the trust bit (`ReadSide.fs:1171` reads `is_not_trusted` and threads it into the Catalog as
`Reference.IsConstraintTrusted`). But the engine's **general comparison surface is decision-blind**:
`PhysicalForeignKey` (`PhysicalSchema.fs:153`) is a six-field record with **no trust field**;
`toPhysicalIndexes` (`:494`) reads the *catalog's* uniqueness, not the overlay's; and `ofCatalog` (`:655`) never
takes a `DecisionOverlay`. So a decision is emitted, survives the deploy, is recovered by `ReadSide` — and then
**discarded at the diff boundary**, the only comparator the live canary runs.

And the sharp edge: the matrix's honesty mechanism caps an axis at L2-partial *only if* a `ToleratedDivergence`
tagged for that axis exists — and all eight tolerances tag only Schema or Data (`Tolerance.fs`, lines
47/58/74/92/115/132/149/165). There is no Decision-axis tolerance variant. So "Decision ✅ faithful / ✅ L3" is
not *proven* — it is **unfalsifiable by construction**: the machine has no way to mark it partial. The engine,
whose entire credibility rests on "the generator under-claims, never over-claims," over-claims here — and on
three of five axes (Decision, Identity, Time have *zero* tolerances), the under-claiming guarantee is vacuous.
The matrix footer itself admits the "3-axis Decision adjunction" is an "unwitnessed sub-axis." The gap between
that admission *in prose* and the tolerance set *in code* is the gap this document most wants closed.

Two smaller tensions: three structurally-dead FK config bits (`AllowCrossCatalog`,
`TreatMissingDeleteRuleAsIgnore`, `DeleteRuleIgnored`/`isIgnoreRule = false`) survive on a "V1 parity"
justification that Pillar 6 arguably forbids; and `VersionedPolicy.digestOf` uses `sprintf "%A"` —
determinism-by-luck, in a corpus whose whole digest discipline (`TransformRegistry.digest`) is painstaking
length-prefixed determinism-by-construction.

### II.5 The self-verification machine — honesty that is reachable-but-unreached

The engine's epistemic spine is rare and real: a self-report that **structurally under-claims** plus a pure-bash
honesty gate (`verifiability-gate.sh`) that makes "a `Skip` entry claiming Bucket A/B" a build failure — closing
the historical phantom-coverage defect. `matrix-status.sh` regenerates the L1/L2/L3 matrix from exactly two proof
surfaces (backtick-quoted test names + the `@ladder` tags in `Tolerance.fs`), and `MatrixLadderTests` pins the
generator's keystone.

| Construct | File | Role |
|---|---|---|
| `AxiomTests.fs` | `tests/.../AxiomTests.fs` | the executable `AXIOMS.md`: live Facts + Skip-stubs, each naming its verifiability-triangle bucket + trigger |
| `verifiability-gate.sh` | `scripts/` | the hard honesty gate: FAILs the build if any Skip claims Bucket A/B (the phantom defect) |
| `matrix-status.sh` + `NORTH_STAR.matrix.generated.md` | `scripts/` | the self-reporting ladder generator; deterministic so a git-diff = a coverage shift |
| `ToleratedDivergence` + `@ladder` tags | `Tolerance.fs` | the L2 proof surface; an `OpenGap` caps its axis; retiring a variant auto-flips the cell |
| `citationOf` | `AxiomTests.fs` | the cross-reference helper — its body is `ignore (...)`; 112 citations with no existence check |
| `NoUnsafeTimeInCoreAnalyzer` | `Analyzers/` | the AST-level determinism guard (PRJ001); opt-in, off-CI, no unit test of its own walker |

The algebra here is elegant: the adjunction-as-test, Tolerance as a Galois/quotient erasure set, a partial
lattice under set-inclusion, the torsor + norm laws each bound to a discriminating witness, the closed DU as an
unforgeable law, and Skip-stubs-as-a-deferral-monad.

**The principal tension** is that the honesty is bounded by the *reach* of the named surfaces, and three
boundaries are crossable today. First, `citationOf` is a no-op — **112 citations are a pure string trail with no
compile-time or runtime existence check** (verified on disk: 112 sites, 42 Skip stubs). A renamed or deleted
cited test leaves the axiom green; the comment admits existence was grep-verified only at first commit. This is
the named failure mode "performance-of-compliance" lurking in the heart of the proof machine. Second, the L1/L3
witness binding is a *substring* grep, not a structural by-Name binding. Third, T-IV is half-built: the matrix is
generated, but `AXIOMS.md`/`PRODUCT_AXIOMS.md` prose is hand-maintained — the gate's own header names "generate
the L3 surfaces FROM `AxiomTests.fs`" as the way to "close the phantom class for good," and it is unbuilt.

A correction the synthesis owes its reader: the **writer "trinity"** (`Lineage` / `LineageTree` / `Certificate`)
is cited across the doc corpus as architecture, but only `Lineage` (the linear writer) exists in `src/`.
`LineageTree` (branching free monad) and `Certificate` (terminal) are **documentation horizon, not code**. By the
dead-algebra discipline, doc-only algebra with zero code consumers should be marked aspirational, not described
as built — and a recommendation proposing a "first consumer" for `LineageTree` violates evidence-gating, because
there is no algebra there to consume.

### II.6 The orchestration, config control-plane, and the operator instrument

Runs are bracketed by `RunEnvelope.bracket` (fresh runId, mandatory terminal `summary.runComplete`), and the
stage arc is a typed program over `RunSpine` driven by a `staged spine { }` CE — a free-monad-flavored program
over time that guarantees `declared ⇔ executed ∪ aborted` (an open stage at run-end becomes a *named* Aborted,
not a board hang). This is the writer-monad trinity's *temporal* sibling. Config flows through nine `*Binding`
modules (each a Kleisli arrow `Config.<Section> → Result<CoreType>` over the validation applicative), and the
operator surface is a clean functor `Code → Copy → Surface → View` where the View is a coalgebra rendered three
ways (pretty, plain, JSON) that can never drift. A44 names the control-plane adjunction: `render ∘ resolve = id`
on the config⟷spec pair.

| Construct | File | Role |
|---|---|---|
| `RunEnvelope.bracket` | `RunEnvelope.fs` | the single run-envelope owner; always emits the terminal summary, even on throw |
| `RunSpine` / `StageName` / `StagedOutcome` | `RunSpine.fs` | the typed stage arc; aborted-at-stage is a first-class outcome |
| `staged spine { }` CE | `RunSpine.fs` | writer-fidelity applied to time; closes the books total (`declared ⇔ executed ∪ aborted`) |
| the `*Binding` family (nine modules) | `Pipeline/*Binding.fs` | boundary translators; each carries the same opt-in-gate / structured-error / aggregate discipline, hand-written per axis |
| `Voice` catalog + `errorFrame` / `gateStatement` | `Cli/Voice.fs` | the Code → copy projection; `gateStatement` is total over the closed `GateLabel`; `errorFrame` is a string-prefix router (convention) |
| `Surface` / `View` | `Cli/Surface.fs`, `View.fs` | the one statement-over-substantiation shape; pretty/plain/json are projections of one value |
| `CapabilitySurvey` | `Pipeline/CapabilitySurvey.fs` | the `required ⇔ surveyed` structural totality — the gold-standard A44's reachability canary should mirror but doesn't |

The algebra: a free-monad-flavored writer CE over time, the A44 adjunction instance, the smart-ctor derive-macro
on `StageName`, the Voice functor and View coalgebra, the `*Binding` Kleisli arrows over the validation
applicative, the monoid-identity-by-default discipline (every binding's empty case is the all-permissive
identity, guaranteeing T1 on the unconfigured run), and closed-DU totality (`required ⇔ surveyed` / `code ⇔ copy`
/ `gate ⇔ copy`).

**The principal tension** is duplication-by-convention: the nine `*Binding` modules re-implement the same
opt-in-gate + `bindError` + `aggregate` skeleton with a hand-rolled four-way `Ok/Error` applicative match, and —
more dangerously — the binding *convergence* is written three times (a 4-Result tuple in `runWithConfigCore`, a
3-Result tuple in each of two shaped-catalog runners), free to drift on which bindings each path threads. This is
the exact seam `THE_CONFIG_CONTROL_PLANE.md §6` flags as riskiest (`applyModuleFilter` on the model-read path but
not the live/docker path). And A44 itself is enforced by convention + scattered sweeps, not by one closed-DU
reachability witness analogous to `CapabilitySurveyTotalityTests`. Two smaller seams: the `Voice.errorFrame`
string-prefix router over an open code space (a new prefix falls through to the generic frame — the
performance-of-compliance risk the closed-DU `gateStatement` avoids), and `RunFaces` (a 2096-line file where a
dozen faces repeat a near-identical parse → env-gate → apparatus → runBody → exit scaffold).

### II.7 Identity, the registries, and the scalar primitives

This is the conserved-charge layer. `SsKey` is a closed four-variant DU
(`OssysOriginal | Synthesized | DerivedFrom | V1Mapped`) whose variant tag carries A1's faithfulness bound
*type-visibly* — unconditional for GUID roots, bounded for name-synthesized roots — and a recursive length-
prefixed serializer makes it a recoverable on-disk identity. Three orthogonal name-spaces sit side by side and
are kept apart *by type*: **Identity** (`SsKey`), **Designation** (`Name`), **Realization** (the physical-
coordinate VOs in `Coordinates.fs`).

| Construct | File | Role |
|---|---|---|
| `SsKey` + `serialize`/`deserialize` | `Identity.fs` | the conserved charge; the recursive length-prefixed codec (`deserialize ∘ serialize = id`, injective) |
| `Coordinates` (`SchemaName`/`TableName`/`ColumnName`/`TableId`) | `Coordinates.fs` | the Realization name-space VOs with the 128-char identifier budget; `TableId.Catalog` is the one stringly-typed deferral |
| `UuidV5` | `UuidV5.fs` | RFC-4122 §4.3 deterministic GUID via incremental `SHA1.TransformBlock` (the data-structure-oriented form) |
| `TransformRegistry` + `digest` | `TransformRegistry.fs` | Pillar-9/A41 reification; the type-erased metadata projection; `digest` is length-prefixed (NM-60 injective) |
| `RawValueCodec` / `SqlLiteral` / `SqlTypeCorrespondence` | `Core/` | the codec trinity collapsing parallel parse/emit sites into single law-bearing surfaces |
| `PrimitiveType` / `SqlStorageType` | `Core/` | the two-layer scalar split (9 semantic over 28 concrete) with the consistency witness `toPrimitiveType ∘ ofSqlType` |
| `Ledger` / `Episode` | `Ledger.fs`, `Episode.fs` | the R3 append-only chain algebra; `Verified` is a private-ctor proof token; `Episode` co-records the five planes |

The algebra: the closed-DU sum as conserved charge (with the recursive `DerivedFrom` arm as a catamorphism over
the derivation spine), codec adjunctions with round-trip laws, a Galois-shaped semantic↔concrete pair, the
private-ctor derive-macro, the fold-from-genesis monoid (FTC), orientation newtypes as a phantom-typed torsor
guard, and length-prefixed injective serialization.

**The principal tensions** are small and local: `SourceKey`/`AssignedKey` are bare `of string` with no smart
constructor while their user-id siblings wrap a validated `UserId`; `TableId.Catalog` degrades to `string option`
(the Realization name-space is type-complete on schema/table/column but stringly-typed on the catalog axis); the
registry knows *which axis* a transform touches but not *which identities*, so identity-level and transform-level
provenance are two unjoined tables; and `OperationKey` (the refactorlog identity) is a bare `System.Guid` against
a codebase where lesser identities are VOs.

### II.8 The testing surface — the engine's self-knowledge organ

The suite's deepest move is that **proof and proof-claim live in one gated artifact**: `AxiomTests.fs` is a
literal coverage map, and the bash gate makes it unable to lie about its own coverage. Around that spine sits a
clear taxonomy — totality tests (`required ⇔ surveyed`, `code ⇔ copy`, `registered ⇔ executed`, the same
bidirectional-subset proof instantiated four times), the adjunction law realized in-process, codec isomorphisms
over the constructed-valid `catalogGen`, byte-for-byte golden corpora, Docker-gated canaries, and intentional-
fail probes that prove each totality property actually catches its named failure mode.

| Construct | File | Role |
|---|---|---|
| `AxiomTests.fs` | `tests/` | the single audit surface; 52 axiom/theorem entries (the 112 figure folds in horizon stubs + Wave-6 theorems) |
| `CatalogCodec.catalogGen` | `CatalogCodecTests.fs` | the one real recursive Catalog generator (referentially valid by construction); the template every other axis lacks |
| `AdjunctionLawTests` | `tests/` | the law at FsCheck scale — in-process via `ofStatementStream`; the real-wire Docker sweep is a single Skip-stub |
| `CapabilitySurveyTotalityTests` / `VoiceTotalityTests` | `tests/` | the totality genre's exemplars; completeness-by-construction over a closed DU |
| `GoldenEmissionTests` + Golden corpus | `tests/` | byte-for-byte emission goldens (the T1 byte-identity proof made example-concrete) |
| `Bench.fs` + `PerfHarnessScenarios` | `Core/`, `tests/` | the perf self-knowledge layer; real production entry points replayed as scale-parameterized scenarios |

The algebra: the totality functor (the same bidirectional-subset proof four times), the adjunction-as-property,
the round-trip codec isomorphism over a constructed-valid generator, generated-documentation-as-a-functor (T-IV),
the Skip-stub-as-deferral-monad, and intentional-fail probes as negation witnesses (the test of the test).

**The principal tension** is the Fact:Property ratio (~24:1) — example-testing dominates, FsCheck is concentrated
in ~14 files, and the youngest layer (the Wave-6 change algebra T12–T16) is the *least* swept: the T16 master
equation is cited to a single hand-built fixture, and `genCatalogPair` does not exist. The deepest law's
strongest proof is in-process, not real-wire. **With the reverse-leg engine done, that deferral's trigger has
fired** — and it is the highest-value unfired proof in the system (§VI.2). A second tension self-terminates: the
~25-file OSSYS parity inventory measures V1↔V2 differential, and 8+ rows are explicitly V1-SUNSET — the day V1
sunsets (the engine's stated terminus), the largest single test cluster loses its meaning or must be re-grounded
against the live OSSYS source directly.

### II.9 Five dimensions the first pass missed — the engine auditing its own audit

A completeness pass over the decomposition surfaced five whole dimensions the structural map had not named.
Recording them is itself in the spirit of the engine — the audit that finds what the audit missed — and each is
expanded here because a *complete* portrait must hold them in view.

- **Concurrency / parallel deployment.** `TopologicalOrder.fs:153` defines `ParallelSafe<'a>` — a private-ctor
  derive-macro whose docstring says "the comment-borne MUST [deploy levels in order; within a level segments are
  independent] dies, the type lives." It is minted *only* by `TopologicalOrder.levels` and consumed by
  `Deploy.executeBatchParallel` (real `SemaphoreSlim`-bounded fan-out with DMV-probed parallelism and a pool cap)
  and `PhysicalSchema.fs:588`'s `Array.Parallel.map` on the row-hash path. It is arguably the *cleanest* example
  in the codebase of a MUST-comment becoming a type theorem, it governs the live deploy hot path (now on the
  critical path because `migrate A B`'s CDC-safe deploy rides it), and it raises a real open question: **is
  within-level-parallel deploy outcome-deterministic regardless of completion order?** The `ParallelSafe` token
  asserts independence; nothing cited proves the canary's `B'≡B` holds under reordered within-level execution.
- **Cost / observability.** `Bench` (`Bench.fs:103`) is the *one* sanctioned module-level mutable — a global
  `Dictionary<string, ResizeArray<int64>>` under `lock` — and therefore the most load-bearing impurity in the
  system. It is hammered concurrently by `executeBatchParallel` (a real, if tiny, lock-contention point on the
  parallel hot path), it is deliberately T1-exempt (the one non-deterministic production value, a fact that
  interacts with the determinism story no lens examined), and the perf-gate is its regression fitness function.
  Pillar 7's "every hot path has `Bench.scope`" is one of the eight supreme disciplines; the cost dimension is
  where the estate-scale transfer lives or dies. An open question worth answering: is the perf-gate CI-gated like
  lint, or opt-in like the analyzer?
- **Security / trust boundary.** The engine's premise is *ingesting an untrusted external estate* and writing to
  SQL Server with elevated rights. There is a genuine, uncredited strength here — a **three-layer injection
  defense**: typed `Coordinates` exclude hostile input structurally (`LiveProfiler.fs:104` reasons about this
  explicitly), `Identifier.EncodeIdentifier` (vendor-supplied canonical quoting) wraps every dynamic-SQL
  identifier site, and `Parameters.AddWithValue` binds values. Paired with it is an unaudited credential-flow
  risk: passwords flow through `DockerImageEmitter.fs:217` (`/TargetPassword:"$MSSQL_SA_PASSWORD"`), connection
  strings through the Voice/journal/`Bench` — is there a credential-redaction discipline, or can a password reach
  an operator log or an NDJSON journal? "The estate is untrusted input" is a load-bearing assumption no axiom or
  tolerance encodes.
- **Failure-mode / retry.** `Projection.Adapters.OssysSql/Retry.fs` is a Polly transient-classification layer
  (the transient SQL numbers, bounded exponential backoff) — built because V1 had no retry policy and V2's
  dual-track canary must tolerate transient cloud-OSSYS exceptions without a false-positive divergence. It
  documents a *named un-retried gap*: mid-stream transients on `ReadAsync`/`NextResultAsync` after the reader
  opens "cannot be retried without re-running the full query; that surface is left un-retried for this slice." On
  the now-shipped streaming reverse leg at estate scale, that is a real resilience hole exactly where a transient
  is most likely and most expensive — with `CaptureJournal` (which already journals chunks) sitting right there
  as a latent checkpoint-resume mitigation. The exit-code taxonomy (`Preflight.refusalOf → ExitCode`) is a
  refusal-classification algebra that maps disposition→axis→exit code, the operator's machine-readable failure
  contract, never analyzed as such.
- **Persisted-state evolution (Time, applied to the engine's *own* durable state).** The engine accumulates
  replayable provenance in durable NDJSON stores (`LifecycleStore` episodes, `CaptureJournal`, frozen-schema
  V2.SsKey extended properties, the refactorlog). Several high-leverage moves change a *serialized form*
  (`ConstraintState`, the persisted digest, the SsKey basis). There is a torsor on Catalog deltas — but is there
  any notion of a *store-schema* delta, a journal-format version, a forward-compat codec discipline? The one
  extant discipline (NM-34: a missing field reads as `None`) is the de-facto store-codec contract, and it
  deserves to be named as such and made a gating checklist. An engine whose soul is replayable provenance
  terminating at an eject lives or dies on whether yesterday's store can be read by tomorrow's binary.

These five are not gaps to be filled this quarter. They are the dimensions a complete planning document must hold
in view, and Part VII sequences the moves that touch them with the right gates.

---

## Part III — The Nine Lenses, in Full

The study derived improvement methodologies through nine independent lenses. This part gives each its complete
record: the thesis, the full methodology catalogue (sixty-one in all), the primitives each lens would strengthen,
and its sharpest grounded observations. The point of nine lenses is not nine answers; it is to see whether the
*same* answers arrive through unrelated doors — and Part IV reads the convergences. Here, each lens speaks for
itself. Recommendation ids (e.g. `L4.M1`) cross-reference the ranked catalogue in Appendix E; the adversarial
verdict on each is folded in where it changes the disposition.

### III.1 Domain & ubiquitous-language fidelity

**Thesis.** The V2 sidecar is, at the type level, one of the most domain-faithful F# codebases imaginable:
identity is a conserved-charge DU (`SsKey`), the three name-spaces are kept apart by type, the named-erasure
algebra (`ToleratedDivergence`) speaks the operator's exact dialect, and generic-suffix domain-blind naming is
essentially absent (every `Helper`/`Factory`/`Processor` hit is a comment or a V1/BCL interop name, never an
F#-owned type). The fidelity gaps are therefore *second-order*: (1) the operator's move alphabet (Add/Remove/
Rename/Reshape/Insert/Update/Unchanged/Delete/Reidentify/Move/Accumulate) and the protein-workflow catalogue
(P-1…P-9) are the ubiquitous language of the target, yet they live only in prose — in code the same move wears
two names across modules (`Reshape ↔ Changed`, `Insert/Update ↔ Phase1Merges/Phase2Updates`); (2) several
load-bearing identities are still primitive-typed (`OperationKey:Guid`, `RefactorLogRef:string`); (3) the
ReadSide ingest leg re-synthesizes identity with a flat basis; (4) the Decision concept (FK trust) is emittable
but unnamed in the round-trip. The high-leverage move is to lift the move alphabet from prose into one closed
`SchemaMove`/`DataMove` DU the whole pipeline projects from.

**Methodologies.**

- **`L1.M1` Lift the move alphabet into one closed DU.** Define `SchemaMove` (Add|Remove|Rename|Reshape ×
  {Kind,Attribute,Reference,Index,Sequence}) and `DataMove` (Insert|Update|Unchanged|Delete), naming exactly the
  operator's alphabet, and have `ChannelDiff`, `SchemaLoss`, the `Statement` emitter, `ChannelCounts` (the norm),
  and the manifest all *project from* them. *Mechanics:* a `RequireQualifiedAccess` module with `count`/`toLoss`/
  `toStatement` projections + a generic `ChannelSpec<'entity,'facet>`. *Effort:* L. *Cell:* T-V + Schema L2→L3.
  *Verdict: scored 0 — deferred.* Highest blast radius; the four enumerations *agree today*, so per IR-grows-
  under-evidence this waits for a divergence. **Do the zero-risk first step now:** rename `ChannelDiff.Changed →
  Reshaped` so the diff and migration layers speak one word.
- **`L1.M2` Give the protein workflows a typed identity.** A closed `Protein` DU (`DevToOnPremLoad |
  UatRekeyLoad | SsisPublication | IdempotentRedeploy | InPlaceEvolution | TerminalEject | DriftRemediation |
  SelfCheckCanary`) carried by `PlanAction`, with a total `MovementSpec.protein` classifier and a
  `code ⇔ protein` totality test. *Why:* the run could name the workflow the operator recognizes ("P-3 failed at
  Reconcile") and the protein×amino-acid matrix becomes generated, not hand-maintained. *Effort:* M. *Cell:*
  T-IV + T-VI. *Gate:* the protein set has been stable since the ontology landed.
- **`L1.M3` Unify SsKey synthesis at the ReadSide boundary onto the typed-segment composite.** *Verdict: scored
  0 — downgraded to an open adjudication (§IV.3); the four-lens convergence on this was an echo-chamber.* The
  mechanical claim (flat `sprintf` basis vs OSSYS's typed segments) is true, but the *should-we* is contested:
  ReadSide-recovered identity is a deliberately separate IR domain.
- **`L1.M4` Name the FK-trust / decision-readback concept** in the comparison surface (or name its erasure). The
  domain framing of the keystone: a `NoCheckFk` decision is a concept the engine can *say* but cannot *name* in
  the round-trip. *Effort:* M (field) / S (tolerance). *Cell:* Decision L2. *This is the keystone, M1 in Part VII.*
- **`L1.M5` Promote the remaining primitive-typed identities to VOs** — `OperationKey` (Guid → private-ctor VO,
  the refactorlog's dedup key and T1 anchor) and `RefactorLogRef` (string → typed handle, the one stringly-typed
  plane on the otherwise-fully-typed five-plane Episode). *Effort:* M. *Cell:* Identity L2. *Gate:* do
  `OperationKey`/`RefactorLogRef` now (clear second consumers); defer the surrogate-pair unification.
- **`L1.M6` Introduce a glossary/ubiquitous-language boundary type** binding adapter terms to Core concepts
  (Entity↔Kind, Espace↔Module), extending the exemplary `Origin` DU pattern. *Effort:* S. *Cell:* T-III.
  *Risk:* low — mostly relocation + documentation; resist over-engineering.
- **`L1.M7` Rename the data-load artifact off the realization mechanism onto the domain move** —
  `DataInsertScript`/`Phase1Merges`/`Phase2Updates` name the two-phase load *mechanism*, not the operator's
  Insert/Update/Unchanged move (and `DataInsertScript` emits MERGE, so "Insert" under-names it). *Effort:* S.
  *Cell:* T-V (Schema∥Data move symmetry). *Risk:* low — internal rename, golden-test gated.

**Primitives to strengthen.** The move alphabet as a type; `Protein`/`OperatorFlow`; `OperationKey` (bare Guid →
VO); Decision-axis FK trust in the comparison surface; the ReadSide synthesis basis (contested); `RefactorLogRef`
(string → VO).

**Sharpest observations.** The `Reshape ↔ Changed` split is a *literal* translation
(`ad.Changed |> List.map … → ReshapedAttributes`). Domain-first naming (pillar 8) is genuinely honored — a sweep
for generic suffixes finds *no* F#-owned domain type carrying one. The `Origin` DU is a model example of
deliberate V1-vocabulary decoupling, comment and all ("V1's product names never enter the Core DU"). The
named-erasure algebra and the user-identity bounded context are exemplary ubiquitous-language fidelity.

### III.2 Epistemic — the knowing machine

**Thesis.** The engine's epistemic spine is genuinely rare: a self-report that structurally under-claims and a
phantom-coverage gate that makes "Skip yet claims verified" a build failure. But the honesty is bounded by the
*reach* of the named surfaces, and three boundaries are crossable today. (1) The matrix can cap an axis at
L2-partial only if a `ToleratedDivergence` tagged for that axis exists — and all eight tolerances are Schema or
Data, so "Decision ✅ faithful" and "Identity ✅ faithful" are *unfalsifiable by construction*, not proven. (2)
The Decision axis is the sharpest case: the decision travels all the way to emission and back into the model,
then is discarded at the diff boundary, while the matrix reports the axis faithful. (3) A genuinely silent
erasure persists: the SSDT interpreter skips an unparseable `CreateTrigger` with a comment appealing to a canary
that has no detector for it — the one unforgivable epistemic sin. The amelioration is not to add tests but to
*extend the named surfaces so the honest machine can see further*.

**Methodologies.**

- **`L2.M1` Give the Decision axis a real readback comparator.** Add `IsTrusted:bool` to `PhysicalForeignKey`;
  have `ofCatalog` take the `DecisionOverlay`; do the same for `PhysicalIndex.IsUnique` under `EnforceUnique`.
  Then `PhysicalSchema.diff` compares trust + promoted-uniqueness as a matter of course — one diff, five axes, no
  new comparator. ReadSide already reads `is_not_trusted`, so the read side is free. *Effort:* M–L. *Cell:*
  Decision L2. *The keystone.*
- **`L2.M2` Name the Decision and Identity erasures as `ToleratedDivergence` variants** so the matrix can cap them
  honestly (until M1 lands): `FkTrustUnreflected`, `UniquePromotionUnreflected`, `AttributeSsKeyResynthesizedOn
  Readback`, with `@ladder Decision/Identity OpenGap`. *Effort:* S. *Cell:* Decision/Identity honesty. *Risk:* a
  tolerance with no detector is a performance-of-compliance risk unless paired with M1 — keep it explicitly
  `OpenGap` (a debt), not `AcceptedFaithful`.
- **`L2.M3` Convert the `CreateTrigger` silent skip into a named Tolerance + DiagnosticEntry.** Replace the bare
  `()` at `Render.fs:102-107` with a structured diagnostic (`emitter.trigger.unparseable`) + a
  `TriggerDefinitionUnreflected` tolerance + a `CanaryResidual` detector. *Effort:* S–M. *Cell:* Schema L2 (the
  named-erasure discipline restored). *The one unforgivable violation closed.*
- **`L2.M4` Promote `citationOf` from a no-op to a gated existence check.** Emit the citations as a discoverable
  typed value and add one test asserting every cited backtick test exists; wire it into the gate. *Effort:* S.
  *Cell:* T-II (close the phantom class for good). *Risk:* the check verifies presence, not coverage — label it
  "citation exists," not "axiom verified."
- **`L2.M5` Generate `AXIOMS.md`/`PRODUCT_AXIOMS.md` bucket lines from the gated test surface** (the open half of
  T-IV). Lift the registry into a typed `AxiomEntry` list; a generator renders the doc footers; the gate reads
  the structured `Bucket` field (also fixing the multiline grep blind-spot and reconciling the count opacity).
  *Effort:* L. *Cell:* T-IV. *Risk:* product axioms carry narrative not derivable from tests — fence the
  generated region carefully.
- **`L2.M6` Make the L1/L3 witness binding structural** (by exact test name from the registry) rather than
  substring grep. *Effort:* M. *Cell:* T-I ladder integrity. *Risk:* exact-name coupling makes a rename a matrix
  break — but that is the point (a coverage shift should diff).
- **`L2.M7` Add a torsor-law property file for the Wave-6 change algebra.** Extend `catalogGen` to `(A,B)` pairs
  and sweep `applyDiff (between A B) A = B`, the no-cheat property, norm additivity, and T16. *Effort:* M. *Cell:*
  Time L2 / T-II. *(Converges with `L3.M4`, `C2.F5`.)*

**Primitives to strengthen.** `PhysicalForeignKey`/`PhysicalIndex` (the readback surface); `ToleratedDivergence`
(extend to all five axes); `citationOf` (no-op → checked value); `CanaryResidual.Collector` (one detector → one
per axis-tolerance); the axiom registry (four hand-maintained surfaces → one typed source).

**Sharpest observations.** The matrix marks Decision/Identity faithful but those axes have *no tolerance that
could ever cap them*. The sole Decision fidelity witness proves only the Nullability sub-axis (its own comment
concedes "FK readback has a known container gap" and "unique-index readback is deferred"), and the L3 Decision
witness is a *refusal* test, not a fidelity round-trip. FK-trust justification travels to emission and back into
the Catalog IR but is discarded at the diff boundary. The count opacity (112 vs 52) is reconciled by no gate.

### III.3 The testable surface — raising the ladder

**Thesis.** The self-verification machine is the rarest kind — it under-claims structurally and cannot be
hand-marked green. But three classes of green test are missing, and each maps to a *specific* over-claim in the
matrix. (1) The Decision axis is proven at emission, not readback. (2) The change algebra (T12–T16) is
fixture-proven, not generator-swept. (3) The deepest law is proven on a *model* of the substrate
(`ofStatementStream`); the real-wire roundtrip is a single Docker fixture with the full sweep deferred — now
unblocked by the shipped reverse-leg engine. The methodology: turn each named over-claim into either a green
readback property or a named `OpenGap` tolerance, and replace fixture-bound laws with generator sweeps.

**Methodologies.**

- **`L3.M1` Decision-axis readback adjunction property (FK trust).** The keystone as a *property*: for any catalog
  and any subset of FK keys, `ofCatalog (overlay NoCheckFk=S) c` and `ofStatementStream (statements …)` agree on
  every `PhysicalForeignKey` including `IsTrusted` — i.e. `PhysicalSchema.diff` is empty. *Effort:* M. *Cell:*
  Decision L2 genuine.
- **`L3.M2` Decision-axis readback adjunction property (uniqueness promotion).** Add `Indexes` recovery to
  `ofStatementStream` (it currently recovers only Columns+FKs) and make `toPhysicalIndexes` overlay-aware.
  *Effort:* M. *Cell:* Decision L2 + T-V.
- **`L3.M3` `DecisionNotReadBack` OpenGap tolerance (the honest fallback).** If the readback properties are not
  yet built, a `DecisionTrustUnreflected` tolerance auto-flips Decision to L2-partial — making the over-claim
  impossible. *Effort:* S. *Cell:* Decision honesty. *Scored +5; M1′ in Part VII.*
- **`L3.M4` Generator-of-valid-deltas torsor sweep (T12–T16).** Lift `catalogGen` to `Gen<(Catalog,Catalog)>`
  sharing an SsKey universe; sweep the torsor laws + T16. *Effort:* L. *Cell:* Time L2 (property-grade). *Risk:*
  the generator quality is the gating work — a naive generator produces only trivial deltas.
- **`L3.M5` Docker-bound full adjunction FsCheck sweep (unblocked by the reverse leg).** Parameterize the
  in-process property over an inverse-leg backend (`ofStatementStream` | `deploy + ReadSide.read` on a container).
  *Effort:* L. *Cell:* T-I Schema/Data L2→L3 on real substrate — *the matrix's named highest-value unfired
  proof.* *Risk:* each iteration against a real container is expensive — needs a pool or curated fixtures.
- **`L3.M6` Attribute-rename identity-threading property + extended-property recovery.** Persist attribute V2.SsKey
  extended properties (extend `recoverKindSsKey` to attributes); prove a renamed attribute threads as `Renamed`,
  not `Removed`+`Added`. *Effort:* M. *Cell:* Identity L2 (attribute grain) + T-V. *This is the genuine residue
  the §IV.3 adjudication isolates — not the flat-synthesis change.*
- **`L3.M7` No-silent-drop property for unparseable triggers** (= `L2.M3` as a property). *Effort:* S. *Cell:*
  Schema L2.
- **`L3.M8` T-VI spanning totality tests (Transactionality/Rollback, Connection pre-flight).** *Verdict: revised —
  build only the Connection-preflight one now (`G1/spanningPreflight` exists); the rollback one when an envelope
  is actually scheduled* (IR-grows-under-evidence). *Effort:* M. *Cell:* T-VI.
- **`L3.M9` `Totality<'code>` combinator + `AxiomEntry` registry** — factor the four structurally-identical
  totality tests into one combinator + four ~10-line instantiations, and promote `citationOf` to a checked
  registry. *Effort:* M. *Cell:* T-II + T-IV. *Risk:* keep the explicit `expected` enumeration caller-supplied so
  drift stays human-visible.

**Primitives to strengthen.** `PhysicalForeignKey` (add `IsTrusted`); `PhysicalSchema.ofCatalog` (take the
overlay, curried, default empty); `catalogGen` (→ `genCatalogPair`); `ToleratedDivergence` (Decision variants +
`TriggerUnparseable`); `citationOf` (→ a checked `CitedTest` list the matrix also reads).

**Sharpest observations.** A42's only witness (`DecisionEmissionTests`) tests *emission* (`emittedColumns` reads
the Statement stream the emitter produced), never reading the decision back through `PhysicalSchema.diff`.
`ReadSide` *does* read `is_not_trusted` (`:1171`) — so closing the gap needs only a *field* on
`PhysicalForeignKey`, not new SQL. The change-algebra round-trip laws are cited to single hand-built fixtures
while `catalogGen` exists and is never lifted. Example-testing dominates ~24:1; the analytics passes and the
strategy-rule decision logic are example-tested only, with no mutation-testing floor — a mutated rule could pass
undetected.

### III.4 F# idiom & gold-standard-library leverage

**Thesis.** The sidecar is already idiomatic at a level most codebases never reach — typed-AST emission,
private-ctor derive-macros, a Kleisli/WriterT pass algebra, units of measure already fired on the Run-delta
surface, FsToolkit already in the tree. The high-leverage moves are therefore *not new abstractions*; they are
*finishing* the application of idioms and libraries the codebase has already sanctioned but left partially
adopted. Three patterns recur: (1) a library used at one consumer while the second hand-rolls the shape it would
collapse; (2) an idiom forbidden in one file but still live in a sibling; (3) an imperative BCL scaffold
verbatim-duplicated across siblings with no shared seam. Closing these strengthens the exact pillars the engine
names as supreme, removes ~150–300 LOC, and converts asserted invariants into compiler-enforced ones.

**Methodologies.**

- **`L4.M1` Adopt FsToolkit `validation { }` at the binding boundary.** Replace the nine hand-rolled 4-way
  `match aR, bR` applicative joins in the `*Binding` modules (and the convergence tuple-matches in `Pipeline`)
  with `validation { let! a = .. and! b = .. }`. The package is already referenced and the idiom is already live
  at `UserRemap`/`ReadSide` — this is finishing an adoption. It also makes the binding-convergence seam read
  uniformly so the model-read-vs-live divergence is visible. *Effort:* M. *Cell:* T-III / A44. *Risk:* low —
  behavior-preserving (same accumulate-all-errors semantics).
- **`L4.M2` Replace `VersionedPolicy.digestOf` `sprintf "%A"` with a length-prefixed typed projection** — the
  digest the codebase already owns next door (and forbids `sprintf "%A"` there). Makes the digest cross-runtime
  stable, so `bumpKind`'s "possibly changed" caveat becomes "definitely unchanged." *Effort:* M. *Cell:* T1 /
  T-II. *Risk:* every axis (incl. the recursive UserMatching DU) needs a deterministic token; canonicalize the
  Map ordering.
- **`L4.M3` Extract a `JsonDocumentWriter` seam** for the `Utf8JsonWriter → MemoryStream → byte[] →
  JsonNode.Parse(span)` dance (4+ byte-identical copies; the pinned options are already centralized — only the
  imperative scaffold is duplicated). *Effort:* S. *Cell:* T11 / Schema L2. *Risk:* low — golden tests gate
  byte-identity; 4 live consumers plainly exceed the second-consumer bar.
- **`L4.M4` Collapse the four `PassChainAdapter` lift functions + two inline-literal steps into one `lift (read)
  (writeBack)` combinator.** The four lifts already share `LineageDiagnostics.map` verbatim; the only variance is
  the reader projection. Factoring `(read, writeBack)` as data means adding a T-VI dimension costs one reader, not
  a new lift shape + an inline literal. *Effort:* M. *Cell:* T-II / L2→L3.
- **`L4.M5` Put `[<Struct>]` + smart constructors on `SourceKey`/`AssignedKey`** — consumed per-row in
  `remapRowFksWith` across the 288M-row estate, the exact hot path where `RowQuantum`'s neighbor earned
  `[<Struct>]` from a measured finding. *Effort:* S. *Cell:* cost / Identity hot path. *Gate:* measure-then-
  promote per the §9.7 ritual. *Risk:* `[<Struct>]` changes equality/boxing subtly — verify no site relies on
  reference equality.
- **`L4.M6` Active-pattern the string-prefix dispatch routers** (`Voice.errorFrame` and siblings) at N≥3.
  *Effort:* S. *Cell:* the convention→structure conversion on the open code space.
- **`L4.M7` Render `tableQualified` through a ScriptDom `SchemaObjectName`** instead of `EncodeIdentifier +
  String.Join` — a near-miss on the typed path for the identifier boundary (the file's comment concedes it). 
  *Effort:* S. *Cell:* pillar-1.
- **`L4.M8` Extend the units-of-measure delta discipline to CDC and row/null counts** (= `C2.F3`). *Effort:* S.
  *Cell:* cost.

**Primitives to strengthen.** `SourceKey`/`AssignedKey` (struct + validation); the `VersionedPolicy` digest
input; the `PassChainAdapter` lift surface; the `*Binding` applicative join.

**Sharpest observations.** `sprintf "%A"` is *forbidden* in `TransformRegistry.digest` and *load-bearing* in
`VersionedPolicy.digestOf` — the one determinism-by-luck digest in a determinism-by-construction corpus. The
JSON-writer dance is byte-identical across siblings with the pinned options already centralized. FsToolkit is
already referenced and live at one consumer while nine others hand-roll the join.

### III.5 Advanced algebra & category theory

**Thesis.** The engine is already categorically mature where it counts (Kleisli passes, WriterT-over-product-
monoid, a concrete torsor on `CatalogDiff`, a bounded Selection lattice, `perKind`-as-natural-transformation), so
"more algebra" pays rent in exactly four narrow places where a structure is half-realized and the forcing
function has demonstrably fired — and several *tempting* category-theory moves are correctly **dead**.

**Methodologies.**

- **`L5.M1` Reify the diff channel as data: one `ChannelSpec`, one descend fold, one apply fold.** The four
  channels (attribute/reference/index/sequence) are byte-identical modulo entity accessors across *three*
  surfaces — the `between` fold-blocks, the `*Diff` builders, and the `apply*Diff` patchers — so a single
  `ChannelSpec<'entity,'change,'facet>` collapses ~8 functions + 4 fold-blocks into 4 small spec values. The
  code's own comment ("mirrors `attributeDiff` EXACTLY") is the fired trigger. *Effort:* M. *Cell:* T-V (the move
  basis at the schema-diff grain).
- **`L5.M2` Name the metric: make the triangle inequality a property test (a free invariant).** `CatalogDiff.norm`
  already satisfies `‖δ₁∘δ₂‖ ≤ ‖δ₁‖ + ‖δ₂‖`, and the code already computes both sides (`pathLength` vs `‖net‖`,
  the churn) — but never names the law. One property over generated adjacent `(A,B,C)` triples turns a free
  invariant into a theorem, making the norm a provable *metric* (which "minimality is measured" depends on).
  *Effort:* S.
- **`L5.M3` Retire the vestigial `Result` on `between`; make `compose` total-modulo-adjacency.** `between` is
  total (one `Ok` return-site) yet returns `Result`, so `compose` threads two dead error branches. Drop it and
  rewrite `compose` as `between (target d1) (source d2)`. *Effort:* S.
- **`L5.M4` Add a `Traversal` optic to unify single-focus `Lens` and bulk `mapKinds`.** The over-only profunctor
  optic generalization; `Lens.toTraversal`, `Catalog`-wide traversal. *Verdict: gated on the compile-order split*
  that is the duplication's named cause. *Effort:* M.
- **`L5.M5` Lift the four tightening passes into one `TighteningStrategy` value (the `fanOut` residue).** *Verdict:
  deferred (= `L6.M1`) — the forcing function (a fifth intervention or a divergence) has not fired.* *Effort:* M.

**Correctly dead** (recorded so the synthesis does not resurrect them): the tightening `Cautious ⊑ EvidenceGated ⊑
Aggressive` Galois chain was deliberately collapsed (no lattice to connect); the `LineageTree`/`Certificate`
thirds of the writer "trinity" do not exist in `src/` (recommending a consumer for absent algebra is the textbook
evidence-gating violation).

**Primitives to strengthen.** `CatalogDiff` (the torsor element — drop the vestigial `Result`); the diff channel
(`ChannelDiff<'change>` + its four builders/appliers → one `ChannelSpec`); `norm`/`channelCounts` (name the
metric law); `Lens` (→ a `Traversal` sibling, gated).

**Sharpest observations.** The code computes *both* sides of the triangle inequality without naming the law. The
four channel builders carry the literal comment "mirrors `attributeDiff` EXACTLY." `between` has exactly one `Ok`
return-site and `compose` recomputes it twice with dead `Error` arms. No `inverse`/`negate` exists on
`CatalogDiff` — the groupoid has its composition and metric but not its defining arrow (§V picks this up as the
single highest-value algebraic move).

### III.6 IR compression & reduction

**Thesis.** The engine has internalized "nine capabilities, one law" *philosophically*; the remaining work is to
internalize it at the *implementation* level. The codebase is unusually advanced here — `ArtifactByKind.perKind`,
`Composition.fanOut`, `ChannelDiff<'change>`, and `RegisteredTransforms.chainSteps` have each already collapsed a
major duplication. So the thesis is the sharper, second-order observation: **the engine keeps collapsing the
*traversal* while leaving the *descriptor* duplicated.** `fanOut` unified the walk but left four near-identical
pass files; `perKind` unified the per-kind map but left two byte-identical `kindJsonNode` dances; `ChannelDiff`
unified the carrier but left three byte-identical builders and three apply-patchers. The move is to push each
already-discovered shape one level up: from "shared traversal, duplicated descriptor" to "the descriptor IS data,
traversed once." The inverse hunt — splitting two algebras forced into one shape — is real but narrow.

**Methodologies.**

- **`L6.M3` Drive the kind-scoped diff channels from a `ChannelSpec` value traversed once** (the canonical IR move;
  the same as `L5.M1` from the compression side). *Effort:* M. *Cell:* T-V.
- **`L6.M2` Extract a `JsonDescription` seam (`writeToNode`/`renderDocument`) for the JSON siblings** (= `L4.M3`).
  *Effort:* S.
- **`L6.M5` Collapse the four `PassChainAdapter` lifts into one `(read, writeBack)` combinator** (= `L4.M4`).
  *Effort:* M.
- **`L6.M4` Retire the dead `ScriptDomGenerate.toText` renderer; make `Render` the one interpreter.** Confirm zero
  production callers (production routes through `Render.toTextWith`), then delete — making the "A40 one rendering
  algorithm" claim literally true. *Effort:* S.
- **`L6.M1` Lift the four tightening passes into a `TighteningStrategy` descriptor traversed once.** *Verdict:
  deferred (`L6.M7`) — real, but no fired forcing function.* *Effort:* M.
- **`L6.M6` Split `EmitError` into three layer-scoped error types.** *Verdict: scored 0 — the inverse move
  (splitting, not merging); premise unverified by the reviewers; defer.* *Effort:* M.
- **`L6.M7` Defer the `ComposeState`→keyed-evidence-map and the `SchemaLoss↔Statement` merge until a forcing
  function fires.** *Scored +4 — the disciplined deferral, rewarded by the reviewers.* **Recording the deferral
  *is* the methodology**: the `ComposeState` open-product wants a typed evidence-map, but the abstraction has not
  earned its second shape; do not build it on speculation.

**Primitives to strengthen / preserve.** `Composition.fanOut`, `ArtifactByKind.perKind`, `ChannelDiff<'change>`,
the `PassChainAdapter` lift family (all already-earned combinators to *extend*, not re-derive); `EmitError` (the
one candidate to *split*, deferred).

**Sharpest observation.** The compression thesis here is the opposite of "the engine is full of copy-paste." It is
that the engine is so disciplined about collapsing traversals that the *remaining* duplication is always one
level up, in the descriptor — and that the right move is therefore surgical (a `ChannelSpec`, a `JsonDescription`
seam) rather than sweeping. Compression is not a monotone good: the same judgment that merges four channels
*splits* `EmitError` and *defers* the evidence-map.

### III.7 Architectural fitness & conformance

**Thesis.** The intended hexagon (Core pure → Adapters/Targets at the boundary → Pipeline orchestrate → CLI
render) is largely **real** in the dependency graph: Core has zero project references and verifiably no I/O, A18
is genuinely structural (the `Emitter<'element>` alias cannot name `Policy`), and A41 (`registered ⇔ executed`) is
property-tested bidirectionally. The architecture's weakness is not its shape but the **substrate of its
guardrails**: the load-bearing conformance commitments are enforced by an out-of-band bash+grep tier whose
authority is asymmetric — lint + verifiability are CI-gated, but the one true AST analyzer
(`NoUnsafeTimeInCoreAnalyzer`) is opt-in, off-CI, has a non-exhaustive walker, and has zero unit tests of its own
logic. The grep rules have no self-test, the hexagonal rule is a text-match over `open` that the LINT-ALLOW escape
can wave through, and the migrate/transfer leg forks its own orchestration outside the `chainSteps` single-source-
of-truth. The high-leverage move is to convert these standing commitments into **in-assembly fitness functions**
living in the test project (which already references all 11 projects and is reflection-exempt).

**Methodologies.**

- **`L7.M1` In-assembly layer-dependency fitness Fact** — a reflection-based assertion replacing grep Rules
  20/21/22 over `open` directives. *Effort:* M. *Cell:* T-II / architecture.
- **`L7.M3` Analyzer-walker unit test + CI promotion of `run-analyzers.sh`** — the determinism guard is itself
  unverified and not gating; test its `collectForbidden`/`matchesForbiddenSuffix`/`isCoreFile` logic and put it on
  CI. *Effort:* M. *Cell:* determinism.
- **`L7.M4` Linter self-test: a fixture corpus that proves the grep rules still bite** — a regex that silently
  stops matching is undetectable today. *Effort:* S.
- **`L7.M7` Widen `NoUnsafeTimeInCoreAnalyzer` from suffix-grep to a full syntax visitor** — the walker is
  non-exhaustive. *Effort:* M.
- **`L7.M2` Emitter-shape totality Fact** — make A18 a closed-set proof (every shipped emitter is `Emitter<_>`-
  shaped), not a per-emitter convention. *Verdict: revised — reflection can prove the shape but the strong form
  overreaches; scope it carefully.* *Effort:* S.
- **`L7.M5` Bind the migrate/transfer leg to the registry: a same-source coverage Fact** — the migrate leg forks
  its own orchestration with no fitness function binding it to the registered transform set. *Verdict: the premise
  rests on the standing reverse-leg fact rather than on-disk code; revise accordingly.* *Effort:* M.
- **`L7.M6` Promote the Data→SSDT cross-target edge into a structural fix (a neutral `Sql` kernel) once it earns
  its second consumer** — ScriptDom is the de-facto shared SQL kernel but lives in the SSDT target, so every
  SQL-emitting sibling depends on SSDT. *Gate:* second consumer. *Effort:* M.

**Primitives to strengthen.** The hexagonal layer boundary (grep → in-assembly Fact); A18 (convention+signature →
closed-set proof); the determinism guard (off-CI suffix-grep → CI'd full visitor with its own tests); the
conformance scripts themselves (un-self-tested → fixture-tested); the single-source pass chain (extend the A41
coverage Fact to the migrate leg).

**Sharpest observation.** The hexagon is *un-rottable in principle* and *rottable in practice* — the difference is
entirely the guardrail substrate. The codebase holds its *runtime* invariants to the closed-DU/derive-macro
standard but holds its *architectural* invariants to a bash standard a contributor can forget to install. Bringing
the second to the first is the whole move, and it costs four Facts in a test project that already has every
reference it needs.

### III.8 Creativity & adjacent scaffolds

**Thesis.** The movement engine's completion does **not** unlock a new headline operation — `migrate A B` and its
dry-run already exist (`RunFaces.runMigratePreview`/`runMigrateFromStore`/`runProjectLivePreview` are shipped).
What it unlocks is the *closure of half-built provenance loops* into operator-facing corollaries, plus one
substrate-polymorphism move. Three candidates survive disciplined scrutiny as real adjunction-corollaries with
fired forcing functions; three are rejected.

**Methodologies (the survivors).**

- **`L8.M2` A machine (JSON) lens over the `ChangeManifest` series for the SSIS consumer** (= `C2.F2`). `ReportRun`
  has `render : ReportBundle -> string list` and **no `toJson`**, even though the whole engine treats human/machine
  as two lenses on one typed value. The CDC-norm is now a real measured quantity; making it queryable turns the
  engine's #1 fidelity surface into a contract the downstream team can diff sprint-over-sprint. *Effort:* S.
  *Cell:* reporting. *Lowest-risk corollary on the board.*
- **`L8.M1` Join the policy-version + approval plane into the durable episode (the recorded Decision axis).**
  Every primitive exists (`VersionedPolicy`, `ApprovalWorkflow`, `PolicyDiff`, `EpisodicLifecycle`,
  `ChangeManifest`) but `MigrationRun.recordVerified` stores schema+data+overlay-artifacts and *not* the
  `VersionedPolicy` digest or `ApprovalRecord` — so the timeline knows *what* changed but not under *which
  approved policy*. Closing this makes "decision = the law on the engine's own opinions" a recorded, diffable
  axis. *Verdict: latent, not movement-fired — gated on a cross-run digest-comparison need + `L8.M4`.* *Effort:*
  M. *Cell:* Decision provenance.
- **`L8.M4` Promote `VersionedPolicy.digestOf` to an explicit projection** (= `L4.M2`/`L9.M4`) — the prerequisite
  enabler for the policy-timeline join (a digest you persist must be cross-runtime stable). *Effort:* S/M.

**Methodologies (rejected — recorded for the discipline).** `migrate --dry-run` (already shipped, not a
corollary); the `LineageTree` speculative-execution substrate (does not exist in `src/` — recommending a consumer
for absent algebra violates evidence-gating); any net-new operation `NORTH_STAR §7` already cut.
`L8.M3` (a DACPAC reader — `Ingest` specialized to a second substrate) is a *real latent corollary* that would
make the engine general-by-construction, but its trigger is "a second catalog source materializes," and the
movement engine is not a second source — **deferred-until-materialized**, scored +1.

**Primitives to strengthen.** `Episode` (add the policy-version/approval planes); `ChangeManifest` + `ReportRun`
(add `toJson`); `Ingest` (the latent second-substrate generality); `VersionedPolicy.digestOf` (stability fix as
the enabler).

**Sharpest observation.** The disciplined creativity here is mostly *subtractive*: the movement engine made the
Time axis executable, and the corollaries make the *other* provenance planes (Decision, machine-queryable Δ) as
recorded and queryable as Time now is — but three of the most tempting "new capabilities" are correctly refused
because they are not adjunction-corollaries with fired triggers. The single shippable delight is `toJson` on the
change-manifest: the typed total value already exists, sorted-for-T1; only the second lens is missing.

### III.9 Code quality & primitive strength

**Thesis.** This codebase has already won the hard battles of primitive strength: `SsKey`/`Name`/`Email`/
`SchemaName`/`TableName`/`ColumnName` are all closed DUs or validated single-case newtypes with `Result`-
returning smart constructors, and `Catalog.create` fuses every aggregate invariant into one walk. The remaining
quality surface is not a field of weeds but a **short, named list** of sites where an invariant is enforced by
*convention* (a runtime predicate, a normalizing setter, a property test, a routing comment) when the same
invariant could be made a **type theorem**. The single highest-leverage move is to convert the three places where
"unrepresentable-by-type" is one refactor away from "unreachable-in-practice."

**Methodologies.**

- **`L9.M1` Make the constraint-trust quadrant unrepresentable.** The `(HasDbConstraint, IsConstraintTrusted)`
  `bool × bool` quadrant → a 3-variant `ConstraintState` DU (`NoConstraint | Trusted | Untrusted`), eliminating
  the illegal `(false, true)` state. *Scored +6 — tied for top of the entire catalogue.* *Effort:* M. *Cell:*
  code-quality / Decision. *Gate:* changes the catalog codec — sequences behind the persisted-state-migration
  story.
- **`L9.M4` Replace the policy digest's structural-printer with an explicit token projection** (= `L4.M2`).
  *Effort:* S/M. *Cell:* T1.
- **`L9.M3` Validate the surrogate orientation pair to its siblings' standard** — `SourceKey`/`AssignedKey` bare
  `of string` → smart-ctor newtypes matching `SourceUserId`/`TargetUserId`. *Effort:* S. *Cell:* Identity safety.
- **`L9.M6` Make the channel-diff `Renamed` folds total-by-type** — retire a partial-function corner. *Effort:* S.
- **`L9.M2` Restore identity round-trip by typed-segment ReadSide synthesis.** *Verdict: scored 0 — the
  echo-chamber (§IV.3); downgraded.* The real Identity residue is at the attribute grain (`L3.M6`), not the flat
  fallback's segmentation.
- **`L9.M5` Retire the dead foreign-key config algebra** (`AllowCrossCatalog`, `TreatMissingDeleteRuleAsIgnore`,
  `isIgnoreRule = false`). *Scored −1 — one reviewer flagged it.* The dead-algebra-retirement precedent and
  Pillar 6 both argue for deletion, but the "V1 parity" justification and the `ADMIRE.md` deferred-with-trigger
  note make this contested — **revise to: delete only with a DECISIONS amendment naming the parity obligation as
  discharged.**

**Primitives to strengthen.** The reference constraint-trust state (`bool × bool` → `ConstraintState` DU); the
`VersionedPolicy` content digest (structural-printer → projection); `SourceKey`/`AssignedKey` (bare → validated);
`PhysicalForeignKey` (the round-trip comparison surface — add `IsTrusted`); the ReadSide-recovered SsKey
(contested).

**Sharpest observation.** The quality surface is *short and named*, which is itself the highest compliment: a
codebase whose remaining quality work is "three sites where convention could become a type theorem" has already
done the work most codebases never start. The `ConstraintState` move (scored +6) is the quintessential example —
making the illegal `(false, true)` state unrepresentable retires a class of runtime checks at exactly the surface
the Decision-readback keystone also touches, which is why the two top-scored moves are neighbors.

---

## Part IV — The Convergences and the Adjudications

Nine lenses, sixty-one methodologies. The synthesis is not their union — it is the *structure* of their
agreement and their tension, adjudicated by the adversarial layer. This part reads that structure.

### IV.1 The three convergences

**Convergence 1 — the Decision-axis readback (the keystone).** Five lenses arrived independently at the same move:
domain (`L1.M4` — name the FK-trust concept), epistemic (`L2.M1` — a real readback comparator), testable-surface
(`L3.M1`/`L3.M2` — the readback properties), code-quality (`L9.M1` — make the trust quadrant unrepresentable), and
both inverse-space (`C1.F5` — the erasure has no name) and opportunistic (`C2.F1` — route through the general
diff). The adversarial layer confirmed it is the **best-grounded set in the entire catalogue**: every cited line
checks out, and crucially *the read leg is already free* (`ReadSide` recovers `is_not_trusted` today), so it is a
true uncashed corollary, not a new feature. When five unrelated analytical stances and the ground truth all point
at one ~M-sized move, that move is not an opinion — it is the next facet to cut. It is the spine of Part VII and
the first real cash of Part VIII.

**Convergence 2 — the descriptor-is-data compression.** The algebra lens (`L5.M1`) and the IR-compression lens
(`L6.M3`) arrived from opposite directions — one counting categorical structures, one counting duplicated source
— at the same `ChannelSpec` move. Convergence from a *structural* lens and a *mechanical* lens on one site is a
strong signal that the abstraction is real (it pays rent in collapsed code), not speculative. The same pair also
converged on the `PassChainAdapter` lift collapse (`L4.M4`/`L6.M5`) and the JSON-writer seam (`L4.M3`/`L6.M2`).

**Convergence 3 — convention → type theorem.** The architectural-fitness, code-quality, and epistemic lenses all,
in their own vocabularies, said the same thing: the engine's standard is "make the invariant structural" (the
private-ctor derive-macro, the closed-DU totality, the smart constructor) — and there is a short list of
load-bearing invariants currently held by *convention* (a grep rule, a runtime predicate, a `sprintf "%A"` digest,
a no-op citation) that have not yet been brought to that standard. The amelioration is to hold those sites to the
codebase's *own* bar.

### IV.2 The dedup mandate

The reviewers noted that the same primitive was recommended four-to-seven times under different lens framings: the
Decision-axis FK-trust field appears in ten distinct recommendations (`L1.M4`, `L1.P4`, `L2.M1`, `L2.P1`, `L3.M1`,
`L3.P1`, `L8.P1`-adjacent, `L9.P5`, `C1.F5`, `C2.F1`); the digest in four (`L4.M2`, `L8.M4`, `L9.M4`, `L9.P4`);
the trigger skip in three (`L2.M3`, `L3.M7`, `L6.M4`-adjacent); the surrogate struct in three (`L4.M5`, `L4.P1`,
`C2.F4`). Convergence is signal — but the synthesis must dedupe to **one canonical move per primitive**, or the
roadmap inflates a single facet into a dozen tasks. Part VII does exactly this: each canonical move (M1–M20) is
the deduplicated representative of its convergence cluster, scored by the adversarial panel.

### IV.3 The one echo-chamber — adjudicated

Four analyses (facet-3, facet-6, the domain lens `L1.M3`, the code-quality lens `L9.M2`) converged on: "ReadSide
synthesizes SsKeys with a flat `sprintf` basis while OSSYS uses typed segments, so a recovered identity can never
equal its source twin — a T-I Identity faithfulness leak; switch it to `synthesizedComposite`." This is the
**echo-chamber the completeness critic caught** (`C3.F7`), and the adversarial reviewers verified the catch on
disk:

- The OSSYS basis is `[module; entity]` (`OssysTranslation.fs:76`) and ReadSide's is `[schema; table]`
  (`ReadSide.fs:70`) — **different namespaces**. Segmentation alone cannot make a recovered key equal an authored
  one.
- `PhysicalSchema.fs:44` explicitly does **not** compare SsKeys.
- ReadSide-recovered identity is *deliberately a separate IR domain* — you recover a schema you did not author;
  its identities are physical-coordinate-derived and are not supposed to alias OSSYS-authored ones. The sanctioned
  bridge for schemas you *did* author is the V2.SsKey extended-property recovery, which covers **kinds**.

So the unanimous "fix" was a regression in disguise: changing the flat fallback's segmentation would make
foreign-schema identities masquerade as authored ones. The synthesis **downgrades it from a recommendation to an
open adjudication**: the real T-I question is whether *authored-schema attribute* round-trip via the V2.SsKey
extended property holds today (`recoverKindSsKey` covers kinds, not attributes) — and *if* it fails for
attributes, the fix is **per-attribute extended-property emission** (`L3.M6`), not changing the flat fallback's
segmentation.

This is the single clearest demonstration in the whole study of why the adversarial layer earns its cost: it
stopped four converging voices from writing a regression into the masterwork. Convergence is signal — but
convergence-by-shared-surface-observation is a hazard, and telling the two apart is exactly the completeness
critic's job.

### IV.4 The corrected record

The reviewers also corrected the chorus's quantitative and structural claims, and the synthesis states the
verified figures once: **112 citation sites** (not 110 / "62 of 78"); **42 Skip stubs** (not 34); **seven** emitter
targets (not six — `OperationalDiagnostics` was omitted); and the writer "trinity" is realized as a **linear
writer only** — `LineageTree` and `Certificate` are documentation horizon, not code. That last is itself a
finding: the dead-algebra discipline says doc-only algebra with zero code consumers should be marked aspirational,
not described as built. The corpus carried an internal contradiction (some facets cited the trinity as real while
the creativity lens proved two-thirds of it absent); the synthesis resolves it in favor of the verified absence.

---

## Part V — The Algebra Deepened, the IR Compressed

This is the intellectual core: where *more* algebra frees an invariant or collapses code, and where N parallel
implementations are secretly one fold over a typed intermediate shape. The governing constraint is strict and the
codebase enforces it on itself: **no structure pays rent in elegance.** It must compress real code or free a real
invariant, and its forcing function must have fired.

### V.1 The descriptor wants to become data

The engine has already internalized "nine capabilities, one law" *philosophically*; the remaining work is at the
*implementation* level. The thesis (§III.6): the engine keeps collapsing the *traversal* while leaving the
*descriptor* duplicated. Three sites are ripe, forcing functions fired:

- **The diff `ChannelSpec`** (`L5.M1`/`L6.M3`). Reify the kind-scoped diff channel as
  `ChannelSpec<'entity,'facet> = { entitiesOf; keyOf; nameOf; changedFacets; mkChange; applyFacet }`. The four
  `*Diff` builders + four `apply*Diff` patchers + four fold-blocks collapse to one fold per direction over four
  small spec values. The fired trigger is in the source: the comment "mirrors `attributeDiff` EXACTLY." This is
  the genuine `applyMove` fold the change ontology asks for, *at the schema-diff grain*.
- **The `JsonDocumentWriter` seam** (`L4.M3`/`L6.M2`). The `Utf8JsonWriter → MemoryStream → byte[] →
  JsonNode.Parse(span)` dance is verbatim across `JsonEmitter`, `DistributionsEmitter`, `CatalogCodec`,
  `ProfileCodec`. One pair of helpers (`writeToNode`, a node→indented-string renderer) restores the
  description-first shape for every JSON sibling.
- **The binding interpreter** (`L4.M1`/`L4.M4`). The nine `*Binding` modules differ only in *data*; the control
  flow is identical. One interpreter + nine declarations removes eight copies of `bindError`/`aggregate`/
  opt-in-gate, and collapsing the three divergent bind-convergences into one `bind-all` **structurally closes**
  the model-read-vs-live `applyModuleFilter` divergence the config-control-plane doc names as its riskiest seam.

One move correctly **deferred**: the four tightening passes (`L6.M1`) want to be one `TighteningStrategy`
descriptor — but the forcing function (a fifth intervention or a divergence) has not fired, so per
*IR-grows-under-evidence* it waits. The deferral is itself a methodology. The inverse hunt — splitting two
algebras forced into one shape — is real but narrow: `EmitError` and the `SchemaLoss ↔ Statement`
`RequireQualifiedAccess` collision are the two named sites. Compression is not a monotone good; the same judgment
that merges four channels splits these two.

### V.2 The torsor wants its missing arrow

The change algebra is *almost* a groupoid. `CatalogDiff` has the metric (`norm`), the partial composition
(`compose`, typed by endpoints), and the action (`applyDiff`). A groupoid is a category in which **every arrow is
invertible** — and the one piece never built is the inverse arrow. This is the directly-uncashed corollary of
T12–T16, and it is nearly free:

```fsharp
let inverse (d: CatalogDiff) : CatalogDiff = between (target d) (source d)   // the endpoints are stored fields
```

Three things fall out of those ~3 lines. First, a **freed property**: `applyDiff (inverse d) (target d) = source d`
— the rollback witness the engine *cannot currently state*. Second, the **groupoid law**
`compose d (inverse d) = identity-at-source` becomes a green test, completing the algebra's defining structure.
Third — the connection the chorus analyzed only in halves — the inverse is exactly the object a
**compensating-DDL down-migration** *is*: the displacement that returns B to A. That makes it the prerequisite for
a clean rollback arm, which is the *cheaper* transactionality arm (a compensating undo rather than a grant-gated
giant transaction). The rollback gap (a runtime concern, `C1.F1`) and the absent inverse (an algebraic concern,
`C1.F2`) are **the same gap viewed twice** — and naming them as one is the kind of cross-cutting insight only a
synthesis can reach, because each lens saw only its own half. (The completeness critic flagged this explicitly:
"the chorus analyzed each in isolation.")

Two more algebraic moves, both small, both rent-paying:

- **The triangle inequality, made a theorem** (`L5.M2`). `CatalogDiff.norm` *already* satisfies
  `‖δ₁ ∘ δ₂‖ ≤ ‖δ₁‖ + ‖δ₂‖`, and the code already computes both sides — but never names the law. One property
  test makes a free invariant a theorem and makes the norm provably a *metric* (which "minimality is measured"
  depends on).
- **Drop the vestigial `Result` on `between`** (`L5.M3`). `between` is **total** — one `Ok` return-site — yet
  returns `Result`, so `compose` threads two dead error branches. Removing it simplifies the algebra and lets
  `compose` be `between (target d1) (source d2)` directly.

And one move correctly **gated**: a `Traversal` optic (`L5.M4`) would unify the single-focus `Lens` with the
hand-rolled `mapKinds` bulk-map — a real unification, gated on resolving the compile-order split that is the
duplication's named cause.

### V.3 What is correctly dead — the algebra not to build

The discipline is visible precisely in the moves the codebase *declined*, and the synthesis must not resurrect
them: the tightening `Cautious ⊑ EvidenceGated ⊑ Aggressive` Galois chain (deliberately collapsed — no lattice to
connect); the `LineageTree`/`Certificate` thirds of the writer "trinity" (zero definition in `src/` — consuming
absent algebra is the textbook evidence-gating violation); and the full `SchemaMove` unification (beautiful,
blast-radius-maximal, no fired trigger — stays cut beyond the zero-risk `Changed → Reshaped` rename). The lesson of
Part V is the lesson of the whole engine: the algebra is not there to be admired; it is there to collapse code and
free invariants, and the same rigor that *adds* the inverse arrow *refuses* the Galois chain.

---

## Part VI — Inverse Space and Opportunistic Horizons

Two negative-space questions sharpen the vector. **Inverse space:** what is *absent*, and is its absence a gap or
a discipline? **Opportunistic search:** which forcing functions have *now fired* — chiefly because the movement
engine is done — so a corollary is ready to cash? The discipline that governs both is the same one that runs the
whole engine: an absence is a gap only if filling it is an adjunction-corollary with a fired trigger; otherwise
its absence is the correctness.

### VI.1 The inverse space — the worthiness of what is absent

**The Decision-axis erasure has no *name* (`C1.F5`, scored +5).** The deepest inverse-space finding, and it sits
*inside* the honesty machine. The matrix caps an axis at L2-partial only if a `ToleratedDivergence` tagged for it
exists — and all eight tolerances tag Schema or Data. So the real Decision-axis silent drop (§II.4) cannot be
marked, and "Decision faithful" is unfalsifiable-by-construction. The named-erasure law says `Ingest∘Project = id`
modulo a **named, closed** erasure set; an erasure that is real but *not in the set* means the set is not actually
closed over that axis. The absence of a single closed-DU variant is what lets the over-claim stand — which makes
adding it the **cheapest honesty correction in the codebase**: it converts an unfalsifiable green cell into
tracked, auto-retiring debt.

**The Transactionality/Rollback axis: A3 is a comment, not a value (`C1.F1`, scored +4).** `Preflight.fs:345-357`
is a *paragraph of prose* where every sibling gate (A1 connection, A2 permission) is a real pure-gate-plus-probe
pair. `MigrationRun.execute` has zero transaction wrapper, and `GateLabel` — the closed DU whose totality the
codebase prizes — has no `MidWriteNotProtected` variant, so a half-populated target after a mid-Phase-2 crash
classifies as the *generic* `UnclassifiedRefusal (exit 3)`. It is a corollary of the adjunction at the deepest
level: the torsor action `applyDiff : State × Delta → State` is only a well-defined *function* if it is
all-or-nothing; a partially-applied delta lands the substrate in a State that is *no* `applyDiff δ A` for any A —
it falls outside the image of the action entirely, breaking T12 **at runtime**. The honest, buildable half (a
`GateLabel.MidWriteNotProtected` variant + its exit-9 classify arm + a pure `transactionalityViolations` gate) is
*not* deferred. The *live* atomic `BEGIN TRAN` wrapper **is** deferred — `Preflight.fs` itself records it as a
survey-gated deferral with a stated trigger (whether the managed login permits one giant transaction vs forces
resumable-upsert), and building it now would violate the codebase's own recorded deferral.

**The groupoid inverse (`C1.F2`, scored +4).** Treated above (§V.2) as the algebraic twin of the transactionality
gap. The endpoints are stored, so `inverse (between A B) = between B A` is nearly free, and it is exactly the
object a rollback DDL plan *is*. The absence is load-bearing because it is *why* the transactionality gap has to
fall back to grant-gated resumability rather than a clean compensating-DDL undo.

**Permissions is gated, but it is not an axis (`C1.F3`).** The subtlest T-VI absence, because it is half-present
and the half masks the gap. The engine *probes* grants and *gates* on them (A2), but grants/roles/RLS are not an
axis — no `Grant` facet in the IR, no `GRANT` in the `Statement` DU, no permission channel in `CatalogDiff`, no
permission readback in `PhysicalSchema`. So the engine can *refuse* a write-denied sink but cannot *project* a
grant set, *diff* two grant configs, or *round-trip* a permission decision. The matrix lists five axes and does
not list Permissions at all — the gate's existence makes a whole dimension *look* closed while it lives only in a
pre-flight. The honest interim is a `PermissionsGatedNotProjected` tolerance (a sixth matrix row at L2-partial);
the full axis fires only when a flow must *publish* grants — e.g. the eject.

**The canary is a property, not a CI gate (`C1.F4`).** The engine's epistemic spine proves the *formal* system,
not the *live* adjunction continuously: the deepest law is property-swept in-process and example-proven on the
substrate by a *single* Docker fixture; a scheduled job that takes a generated `(A,B)` pair, runs the real
`migrate A B` against an ephemeral container, and asserts `B'≡B` is absent. The canary IS "the law at runtime"; a
law you can only check on one example is an example, not a law. With the reverse-leg engine shipped, the real
inverse leg exists — this is the highest-value unfired proof (§VI.2).

**What must stay absent — the discipline that keeps the spine (`C1.F6`, scored +5).** The inverse-space charter's
second half is to distinguish gaps from absences that *are* the discipline. These stay cut, and a future agent
scanning for "gaps" should recognize them as cuts: the `NORTH_STAR §7` widenings (platform-survival rhetoric,
AI-agent substrate as a pillar, six-dimension Faker scoring, GraphQL, open-source framing); the `LineageTree`
consumer (zero-consumer algebra is *deleted*, not consumed); and the full `SchemaMove` unification until a real
divergence fires. Each fails the adjunction-corollary test, and each would dilute the matrix from a closed set of
falsifiable cells into "informational widening." That this finding scored +5 — as high as the keystone's honest
fallback — is the study telling you that *naming what must not be built* is as high-leverage as building.

### VI.2 The opportunistic horizon — forcing functions that have now fired

The movement engine being done is itself a forcing function. It fired a precise, bounded set of corollaries — and,
just as importantly, did **not** fire several look-alikes.

**Fired and ready to cash:**

- **The Decision-readback adjunction (`C2.F1`, scored +6).** Both legs are now live; the only uncashed work is
  routing the recovered decision through the *general* comparator. Half-cashed: the read leg is free. *The
  keystone.*
- **The swept change-algebra / T16 master equation (`C2.F5`, scored +4).** With the real inverse leg existing,
  the in-process/Docker split collapses to a *backend choice for one property*. The highest-value unfired proof:
  it raises the change algebra from fixture-witnessed to property-witnessed, and — with a container pool — the
  round-trip from L2-in-process to L3-on-substrate.
- **`ChangeManifest.toJson` (`C2.F2`, scored +3).** The CDC-norm (T15) is now a real measured value flowing to
  `ReportRun.render` — *as human prose only*. The typed total `ChangeManifest` already exists, sorted-for-T1; only
  the second lens is missing. Lowest-risk corollary; turns the #1 fidelity surface into a queryable contract for
  the SSIS consumer (`REPORTING_HORIZON §7.7` priority-1).
- **`[<Struct>]` on the surrogate orientation pair (`C2.F4`, scored +3).** `CONSTELLATION §9.7` records the
  trigger as fired and landed for `RowQuantum`; the movement engine puts `SourceKey`/`AssignedKey` on the *same*
  per-row hot path. The measured trigger now has a second consumer.
- **Units of measure `row` on the data-norm deltas (`C2.F3`, scored +3).** The `[<Measure>] ms` idiom shipped on
  `Run.diff`; `cdcDelta`/`RowCountDeltas`/`NullCountDeltas` are the same delta surface and now carry real data.

**Looks fired, but is not — excluded to honor the discipline:**

- **H-007 (the `CatalogDiff` compose operator) and the Lifecycle/T-III consumer** are *already cashed on disk* —
  `compose` ships, `A-Lifecycle-4` was promoted to Bucket A, the FTC is consumed by `MigrationRun`'s canary. The
  genuinely-open remnant (the `SchemaDelta` delta-*pass* Kleisli category) has *not* fired — the movement engine
  consumes diff/apply/reconstruct, not delta-passes.
- **The DACPAC reader (`L8.M3`)** — a real latent corollary, but its trigger is "a *second catalog source*
  materializes," and the movement engine is not a second source. Deferred-until-materialized.
- **The policy-version plane joined into the Episode timeline (`L8.M1`)** — latent, but nothing about the movement
  engine fires it; gated on a cross-run digest-comparison need plus the `digestOf` stability fix.

The opportunistic horizon, read honestly, is short and sharp: five fired corollaries to cash, three look-alikes to
leave alone. That ratio — more excluded than included — is the discipline working.

---

## Part VII — The Methodologies of Amelioration (the toolbox)

The deduplicated catalogue: one canonical move per primitive, organized by **kind of move**, each scored against
the adversarial panel and carrying its gate. The five kinds are the five ways to raise the engine's quality of
execution. Effort is S/M/L; "Cell" is the matrix cell or totality advanced; "Gate" names the forcing function — a
move with an unfired gate is **not** scheduled. (Appendix B is the at-a-glance table; Appendix E is the full
123-row source.)

### Kind I — Raise a ladder rung

- **M1 · The Decision-readback adjunction** *(the keystone — top-scored, three reviewers' top pick, zero red
  flags).* Add `IsTrusted : bool` to `PhysicalForeignKey`; thread `DecisionOverlay` (curried prefix, default
  `empty` to preserve T1) into `PhysicalSchema.ofCatalog`, so `toPhysicalForeignKeys` sets `IsTrusted = false` for
  `overlay.NoCheckFk` and `toPhysicalIndexes` sets `IsUnique = isUnique || EnforceUnique`. The widened set-
  difference in `PhysicalSchema.diff` picks the field up with **no comparator change**; the read leg is free
  (`ReadSide.fs:1171`). Add a decision-readback property. *Cell:* Decision L1→L2/L3. *Effort:* M. *Gate:* **fired**
  (emit + ingest both carry the decision). *The single highest-leverage move in the document.*
- **M1′ · The honest fallback** *(the cheapest move on the board).* Add `FkTrustUnreflected` +
  `UniquePromotionUnreflected` to `ToleratedDivergence` with `@ladder … Decision OpenGap`. FS0025 forces
  exhaustiveness; `matrix-status.sh` drops Decision to ◑ L2-partial with **no script change**; auto-retires when
  M1 lands. *Cell:* T-I honesty. *Effort:* S. *Gate:* fired.
- **M2 · Name the silent `CreateTrigger` drop.** Replace the bare `()` at `Render.fs:102-107` with a
  `ToleratedDivergence` variant + a `DiagnosticEntry` + a `CanaryResidual` detector — the one unforgivable
  named-erasure violation. *Cell:* Schema L2. *Effort:* S. *Gate:* fired.
- **M3 · The swept change-algebra + the real-wire canary.** `genCatalogPair`; one `[<Property>]` over a swappable
  inverse-leg backend asserting `applyTo (plan A B) A = B`, the no-cheat property, and `norm = Σ channel counts`.
  *Cell:* T-I/T-II Time + real-wire round-trip. *Effort:* M–L. *Gate:* fired for the in-process sweep; the Docker
  N≥20 pool stays separately gated.

### Kind II — Strengthen a primitive (make illegal states unrepresentable)

- **M4 · Make the constraint-trust quadrant unrepresentable.** `(HasDbConstraint, IsConstraintTrusted)` → a
  3-variant `ConstraintState` DU, eliminating the illegal `(false, true)`. *(Top-scored, tied with M1.)* *Cell:*
  code-quality/Decision. *Effort:* M. *Gate:* fired — **changes the catalog codec**, so it sequences behind the
  persisted-state-migration story.
- **M5 · Retire the one `sprintf "%A"` digest.** `VersionedPolicy.digestOf` → an explicit length-prefixed token
  projection (the discipline `TransformRegistry.digest` already owns). *Cell:* determinism (T1). *Effort:* S.
  *Gate:* fired; a **prerequisite** if the digest is persisted into episodes.
- **M6 · `[<Struct>]` (+ optional smart ctor) on `SourceKey`/`AssignedKey`.** Removes per-row DU-wrapper
  allocation on the FK re-point loop. *Cell:* cost/perf. *Effort:* S. *Gate:* fired (the estate-scale remap path
  is live); measure-then-promote per §9.7.

### Kind III — Compress through an IR (the descriptor IS data, traversed once)

- **M7 · The diff `ChannelSpec`.** Collapse the four `*Diff` builders + four `apply*Diff` patchers into one fold
  per direction over four spec values. *Cell:* T-V. *Effort:* M. *Gate:* fired ("mirrors `attributeDiff` EXACTLY").
- **M8 · The `JsonDocumentWriter` seam.** One pair of helpers retires the four-plus-site
  `Utf8JsonWriter → JsonNode` dance. *Cell:* pillar-1 / T-II. *Effort:* S. *Gate:* fired (≥4 consumers).
- **M9 · The binding algebra.** FsToolkit `validation { }` at the nine `*Binding` sites; collapse the three
  bind-convergences into one `bind-all`, closing the model-read-vs-live divergence. *Cell:* T-III / A44. *Effort:*
  M. *Gate:* fired.
- **M10 · The `TighteningStrategy` descriptor — *deferred-with-trigger*.** Real, but the forcing function (a fifth
  intervention or a divergence) has not fired. **Recording the deferral is the methodology.** *Gate:* unfired — do
  **not** schedule.

### Kind IV — Deepen the algebra

- **M11 · The triangle inequality, made a theorem.** One property over generated `(A,B,C)` triples; the norm
  becomes a provable metric. *Effort:* S. *Gate:* fired.
- **M12 · The groupoid inverse.** `let inverse d = between (target d) (source d)` + the freed rollback property +
  the groupoid law. The prerequisite for the cheaper transactionality arm. *Cell:* Time (reversible evolution).
  *Effort:* S. *Gate:* fired (paired with M20).
- **M13 · Drop the vestigial `Result` on `between`.** *Effort:* S. *Gate:* fired (`between` is total).
- **M14 · The `Traversal` optic — *gated*.** Unifies single-focus `Lens` and bulk `mapKinds`; gated on resolving
  the compile-order split. *Effort:* M. *Gate:* unfired (compile-order).

### Kind V — Convert convention into a machine-checked fitness function

- **M15 · The architecture's fitness functions.** In-assembly Facts in the test project: a reflection-based
  layer-dependency Fact, a type-level emitter-shape proof, an analyzer-walker unit test, a migrate-leg coverage
  Fact. Promote `run-analyzers.sh` and the perf-gate to **CI**. *Cell:* T-II / architecture. *Effort:* M. *Gate:*
  fired.
- **M16 · `citationOf` → gated existence check; matrix reads the structured list.** One traversal asserts every
  `File::Name` exists; `matrix-status.sh` reads the same list (structural by Name, not substring). The open half
  of T-IV. *Effort:* M. *Gate:* fired.
- **M17 · The totality-test functor.** The four totality tests → one parameterized module + four ~10-line
  instantiations. *Effort:* S. *Gate:* fired (4 consumers).

### Corollary cashes (operator-facing, low-risk)

- **M18 · `ChangeManifest.toJson`.** The CDC-norm machine-queryable for the SSIS consumer. *Effort:* S. *Gate:*
  fired; lowest-risk corollary.
- **M19 · Units of measure `row` on the data-norm deltas.** Extend the shipped `ms` idiom. *Effort:* S. *Gate:*
  fired; low-ceremony.

### The honest absences (the moat)

- **M20 · The Transactionality honest-naming half.** `GateLabel.MidWriteNotProtected` + its exit-9 classify arm +
  the pure `transactionalityViolations` gate. Pairs with M12. *Effort:* S. *Gate:* fired for the *naming*; the
  **live atomic wrapper stays deferred** per the recorded survey-gate.
- **Stay cut / downgraded:** the full `SchemaMove` unification (do only the zero-risk `Changed → Reshaped`); the
  DACPAC reader (deferred-until-materialized); the `LineageTree` consumer (zero-consumer); the §7 widenings; the
  ReadSide flat-synthesis "fix" (echo-chamber — downgraded to the attribute-grain adjudication, M-equivalent
  `L3.M6`); and the dead-FK-config retirement (revise to: delete only with a DECISIONS amendment).

### The cross-cutting prerequisite

- **The persisted-state-evolution discipline.** M4, M5-as-persisted, and any serialized-form change sequence
  behind a store-migration story. Name **NM-34** (a missing field reads as `None`) as the de-facto store-codec
  contract; ask whether journal/episode formats carry a version stamp; make "this change touches a serialized
  form" a gating checklist item.

---

## Part VIII — The Sequenced Vector

A toolbox is not a plan; the order is the plan. The moves sequence into four waves whose ordering is *forced* by
dependency: **honesty before features** (you cannot raise a rung you cannot see, and you must not build atop an
over-claim), **fitness functions early** (they protect everything after), **the keystone before its dependents**,
and **serialized-form changes behind the store-migration story**. Each wave's exit criterion is a green test or a
generated artifact — never a judgment.

### Wave 0 — Honesty & fitness (protect everything after)

*All S/M, no new capability — this wave only makes the engine tell the truth about itself and makes its guardrails
un-rottable.*

- **M1′** (DecisionNotReadBack tolerance) · **M2** (CreateTrigger tolerance) — stop the silent erasures; the
  matrix drops Decision to an honest ◑ L2-partial.
- **M16** (`citationOf` gate + structural matrix binding) · **M15** (in-assembly fitness functions; analyzer +
  perf-gate to CI).
- The count corrections enter the canonical surfaces: 112 citations, 42 Skip stubs, **seven** emitter targets, the
  writer "trinity" recorded as a linear writer only.

> **Exit.** No green matrix cell is unfalsifiable-by-construction; every load-bearing guardrail is an in-assembly
> Fact; `matrix-status.sh` binds witnesses by Name. *The "under-claims, never over-claims" guarantee is restored
> on all five axes.*

### Wave 1 — The keystone (the bullseye's fifth column)

- **M1** — `PhysicalForeignKey.IsTrusted` + overlay-aware `ofCatalog` + the decision-readback property.
  **Auto-retires M1′.**

> **Exit.** The Decision cell is *showable*, not asserted: the live canary witnesses `NoCheckFk`/`EnforceUnique`
> survival through emit→deploy→read-back.

### Wave 2 — The reversible algebra and the real-wire proof

- **M11** (triangle-inequality theorem) · **M13** (drop vestigial `Result`) · **M12** (the groupoid inverse) ·
  **M3** (genCatalogPair + swept T16, in-process backend first) · **M20** (Transactionality honest-naming half).

> **Exit.** The groupoid laws are green; `applyDiff (inverse d) (target d) = source d` is a stated rollback
> witness; the master equation is swept; the destructive failure mode is classified, not generic.

### Wave 3 — Compression & idiom (smaller as it grows)

- **M7** (diff `ChannelSpec`) · **M8** (`JsonDocumentWriter` seam) · **M9** (binding algebra; close the riskiest
  config seam) · **M17** (totality functor) · **M6** (`[<Struct>]` surrogate keys) · **M19** (`row` UoM) · the
  zero-risk `Changed → Reshaped` rename.

> **Exit.** The descriptor-duplication retired at the three ripe sites; the model-read-vs-live binding divergence
> structurally impossible; the data norm unit-typed. The engine is measurably smaller.

### Wave 4 — Corollary cashes & gated deepenings (operator payoff)

- **M18** (`ChangeManifest.toJson`) · **M14** (the `Traversal` optic, *after* the compile-order split is resolved)
  · **M4** (`ConstraintState` DU, **behind the store-migration story**) · **M5** (digest projection, prerequisite
  if persisting).
- The persisted-state-evolution discipline is written down: NM-34 named; the serialized-form gating checklist
  active.

> **Exit.** The provenance ledger is a machine-queryable contract; every serialized-form change passes the
> store-migration checklist; the engine's durable state can be read by tomorrow's binary.

### Deferred-with-trigger (the moat, as a ledger)

| Deferred | Re-open trigger |
|---|---|
| Full `SchemaMove` unification | a real divergence between the four move enumerations fires |
| The live atomic `BEGIN TRAN` wrapper | the managed-login grant survey resolves (per `Preflight.fs`) |
| `TighteningStrategy` descriptor (M10) | a fifth tightening intervention, or a divergence between the four |
| DACPAC reader / second `Ingest` source | a second catalog source materializes (per `DECISIONS`) |
| Policy-version plane → Episode | a cross-run digest-comparison need + M5 (digest stability) |
| `LineageTree` consumer | a pass that genuinely branches its lineage materializes |
| Permissions as a full axis | a flow must *publish* grants (the eject) |
| Docker N≥20 real-wire FsCheck sweep | coverage-guided fixtures **or** an N≥20 container pool |
| `EmitError` split into layer-scoped types | a second consumer needs the layer distinction |
| Dead FK-config retirement | a DECISIONS amendment names the V1-parity obligation discharged |

### The single move to make first

If only one thing is done this week, do **M1′ together with M2** — the two cheapest moves on the board (a handful
of closed-DU variants). They cost almost nothing and they immediately stop the engine from over-claiming on three
of five axes, restoring the one guarantee the whole epistemic spine rests on: *the generator under-claims, never
over-claims.* An engine whose soul is "fidelity is a theorem it proves about itself" cannot tolerate a green cell
that is green because the machine has no way to mark it otherwise. Everything else in this document is a facet to
cut; **this** is removing the one smudge on the stone. Then — the same week if possible — **M1**, the real cash,
which turns the honest tolerance back to an earned green. Honesty first, then the theorem.

---

## Part IX — Coda: what I really heard you asking for

The brief asked, in its own register, for the methodologies of improvement that would ameliorate the engine's
quality of execution in service of the flows its verbs and ontology describe. Underneath the words were three
things — and the study answered each.

**You were asking the engine to be held to its own standard.** Not to a generic notion of "good code" — this
codebase is already past that — but to the bar it sets for *itself*: the closed DU, the smart constructor, the
named refusal, the law that pays rent. Nearly every move in Part VII is the same move: *take an invariant the
engine currently holds by convention, and make it structural the way the engine makes everything else
structural.* The trust quadrant becomes a `ConstraintState` DU; the grep guardrail becomes an in-assembly Fact;
the `sprintf "%A"` digest becomes a projection; the silent `()` becomes a named tolerance; the unfalsifiable green
cell becomes tracked debt. The engine's deepest aesthetic is "make illegal states unrepresentable and make every
claim showable" — and the amelioration is simply to apply that aesthetic to the last few places it has not yet
reached.

**You were asking where the engine *says more than it can show*** — and that turned out to be the single unifying
thread. The north star is "fidelity is a theorem the engine proves about itself," and the gap from L1 to L3 is not
missing features; it is the small, named set of places where the claim outruns the proof: an axis marked faithful
with no detector for its own erasure, a deepest law proven on a model of the substrate, a coverage map citing
tests it never checks exist. The whole vector reduces to one instruction — *extend the named surfaces until what
the engine claims and what the engine can demonstrate are the same set.* That is why Wave 0 is honesty and Wave 1
is the keystone: you do not climb the ladder by building; you climb it by making the honest machine see further.

**And you were asking me to reach past your words — including by showing you the discipline of restraint.** The
most loving thing this study did was *not* recommend things. It killed a four-lens convergence that was a
regression in disguise (the ReadSide echo-chamber), it deferred the most elegant idea in the catalogue (the
`SchemaMove` unification) because its forcing function has not fired, and it excluded three opportunistic
look-alikes that the movement engine did *not* actually unlock. A codebase whose central discipline is "IR grows
under evidence; a feature that is not a corollary of the adjunction is probably the wrong feature" deserves a
planning document that holds *itself* to that discipline — that says "not yet, and here is the trigger" more often
than it says "build this." The refusals in this document are not its caution; they are its respect. The vector
points at a small number of facets precisely because the stone is already so well cut that adding more would dull
it.

Hold the spine. Cut the last facets. Make every promise a theorem the engine can show — and leave the books
balanced.

*— Recorded for the receiving agent. THE VECTOR, unabridged, revision 1.*

---

## Appendix A — Provenance and method

This treatise was synthesized from a five-phase multi-agent study (run `wf_b432bd10-850`):

1. **Decompose** — 8 structural cartographers mapped the codebase facets into typed facet-maps (Part II,
   Appendix C).
2. **Lens** — 9 analytic lenses derived improvement methodologies from the decomposition (Part III, the 61
   methodologies).
3. **Inverse & Opportunistic** — 3 scouts mapped the negative space, the fired-trigger horizon, and a
   completeness critique (Part VI, §II.9).
4. **Verify** — 3 perspective-diverse adversarial reviewers (groundedness · discipline · leverage) ranked,
   revised, and killed across 123 pooled recommendations; their verdicts are Appendix D, their scored table is
   Appendix E, and their adjudications are folded throughout Parts III–VII.
5. **Draft** — the section drafting hit a transient provider rate-limit, so the treatise was assembled directly
   from the verified corpus in one authorial voice — which had the happy side effect of letting the contradictions
   the completeness critic flagged (the writer "trinity," the citation counts, the ReadSide echo-chamber) be
   *resolved* in the text rather than stitched.

Usage across the study: 30 agents, ~8M subagent tokens, 667 tool calls. The method's load-bearing move was the
adversarial verification layer: it confirmed the keystone (M1) as the best-grounded recommendation, corrected the
quantitative claims on disk, and stopped a four-lens convergence (the ReadSide "fix") from becoming a regression.
Convergence was treated as signal; convergence-by-shared-surface-observation was treated as a hazard to
adjudicate. Every recommendation that survived carries a fired forcing function or is explicitly held behind its
trigger. The reusable workflow script lives at `.claude/amelioration-workflow.mjs` (re-runnable via
`resumeFromRunId`).

## Appendix B — The canonical moves at a glance

| # | Move | Kind | Effort | Cell | Gate |
|---|---|---|---|---|---|
| **M1** | Decision-readback adjunction (`PhysicalForeignKey.IsTrusted` + overlay-aware `ofCatalog`) | rung | M | Decision L2/L3 | **fired (keystone)** |
| M1′ | `DecisionNotReadBack` / `FkTrustUnreflected` tolerance (honest fallback) | rung | S | T-I honesty | fired |
| M2 | Name the silent `CreateTrigger` drop | rung | S | Schema L2 | fired |
| M3 | `genCatalogPair` + swept T16 / real-wire canary | rung | M–L | Time, real-wire | fired (in-proc) |
| M4 | `ConstraintState` DU (retire the trust quadrant) | primitive | M | code-quality | fired · store-gated |
| M5 | Retire `VersionedPolicy.digestOf` `sprintf "%A"` | primitive | S | T1 | fired |
| M6 | `[<Struct>]` on `SourceKey`/`AssignedKey` | primitive | S | cost | fired |
| M7 | Diff `ChannelSpec` (descriptor-as-data) | IR | M | T-V | fired |
| M8 | `JsonDocumentWriter` seam | IR | S | pillar-1 | fired |
| M9 | Binding algebra (FsToolkit `validation`; close the seam) | IR | M | T-III / A44 | fired |
| M10 | `TighteningStrategy` descriptor | IR | M | — | **deferred** |
| M11 | Triangle inequality as a theorem | algebra | S | metric | fired |
| M12 | The groupoid inverse | algebra | S | Time (reversible) | fired |
| M13 | Drop vestigial `Result` on `between` | algebra | S | — | fired |
| M14 | `Traversal` optic | algebra | M | — | gated (compile-order) |
| M15 | In-assembly architectural fitness functions | fitness | M | T-II | fired |
| M16 | `citationOf` existence gate + structural matrix binding | fitness | M | T-IV | fired |
| M17 | Totality-test functor | fitness | S | T-V | fired |
| M18 | `ChangeManifest.toJson` (CDC-norm for SSIS) | corollary | S | reporting | fired (lowest risk) |
| M19 | `[<Measure>] row` on data-norm deltas | corollary | S | cost | fired |
| M20 | Transactionality honest-naming half + `GateLabel` variant | moat | S | T-VI | fired (naming) |

## Appendix C — The construct catalogue

The load-bearing constructs of each facet, with files. This is the full inventory the decomposition produced; Part
II reads a selection of these into prose.

**C.1 — The IR and the pass algebra (`Projection.Core`).** `Catalog/Module/Kind/Attribute/Reference/Index`
(`Catalog.fs` — the nested-coproduct IR; `Catalog.create` fuses every invariant into one walk; `ConditionalWeakTable`
caches give value-pure O(1) lookup) · `Lineage<'a>` + `lineage { }` (`Lineage.fs`) · `Diagnostics<'a>` +
`LineageDiagnostics` + `Pass` + `>=>` + `composeAll` (`Diagnostics.fs`) · `Lens`/`CatalogLenses` (`Optics.fs`) ·
`Pass`/`Emitter` aliases (`Types.fs`) · `ComposeState` + `with*` (`ComposeState.fs`) · `PassChainAdapter` + four
`lift*` + `compose` (`PassChainAdapter.fs`) · `ChainStep` (`PassChainAdapter.fs`) ·
`RegisteredTransforms.chainSteps`/`all`/`allChainStepsFor` (`RegisteredTransforms.fs`) ·
`RegisteredTransform`/`TransformRegistry` (`TransformRegistry.fs`) · `Composition.fanOut`/`FanOutConfig`
(`Strategies/Composition.fs`) · `CatalogTraversal.mapKinds`/`mapKindsTotal` + `LineageBuffer.Buffer`
(`LineageBuffer.fs`) · `TransformKind`/`LineageEvent`/`Classification` (`Lineage.fs`) · `TopologicalOrderPass`
(`Passes/TopologicalOrderPass.fs`).

**C.2 — The sibling Π emitters (`Projection.Targets.*`).** `Emitter<'element>`/`EmitterWithProfile`/
`EmitterOverDiff` (`Types.fs:50`) · `ArtifactByKind<'element>` (`ArtifactByKind.fs:71`) · `EmitError`
(`ArtifactByKind.fs:17`) · `Statement` (`Statement.fs:269`) · `ScriptDomBuild.buildStatement`/`buildMergeStatement`
(`ScriptDomBuild.fs`) · `ScriptDomGenerate.generateOne`/`pinnedOptions` (`ScriptDomGenerate.fs:50`) · `SqlLiteral`
(`SqlLiteral.fs`) · `SsdtDdlEmitter` (`SsdtDdlEmitter.fs:684`) · `JsonEmitter`/`DistributionsEmitter` (`kindJsonNode`)
· `DataInsertScript`/`DataInsertRow` (`DataInsertScript.fs`) · `ConstraintFormatter` (`ConstraintFormatter.fs`) ·
`Render.toText`/`toTextWith`/`toSql` (`Render.fs:81`) · `StructuredString`/`Inv` (`StructuredString.fs`) · the
seventh target `Projection.Targets.OperationalDiagnostics` (`RemediationEmitter`/`DecisionLogEmitter`/
`SuggestConfigEmitter`/`ActionableDiagnostics`/`Routing`).

**C.3 — The reader leg and movement engine.** `CatalogDiff.between`/`applyDiff`/`compose`/`norm` (`CatalogDiff.fs`)
· `ChannelDiff<'change>` (`CatalogDiff.fs:62`) · `Migration.plan`/`applyTo`/`SchemaLoss`/`LossDeclaration`
(`Migration.fs`) · `MigrationRun.execute`/`executeWithData`/`renameStatements` (`MigrationRun.fs`) ·
`Transfer.runCore`/`writePlan`/`writePlanStreaming` (`TransferRun.fs`) · `DataLoadPlan.build` (`DataLoadPlan.fs`) ·
`SurrogateRemap` (`SourceKey`/`AssignedKey`/`IdentityDisposition`/`remapRowFksWith`, `SurrogateRemap.fs`) ·
`Reconciliation.reconcileKind`/`ReconciliationStrategy` (`Reconciliation.fs`) · `SurrogateCapture`
(`SurrogateCapture.fs`) · `PackedSurrogateRemap` (`PackedSurrogateRemap.fs`) · `CaptureJournal` (`CaptureJournal.fs`)
· `ReadSide.read`/`readRowsStream`/`ForeignKeyReadback` (`ReadSide.fs`) · `ChangeManifest.between`/`series`/
`pathLength` (`ChangeManifest.fs`) · `EmissionMode` (`EmissionMode.fs`).

**C.4 — The self-verification machine.** `AxiomTests.fs` · `verifiability-gate.sh` · `matrix-status.sh` +
`NORTH_STAR.matrix.generated.md` + `MatrixLadderTests.fs` · `ToleratedDivergence` + `@ladder` tags + `Tolerance`
smart-ctor (`Tolerance.fs`) · `citationOf` (`AxiomTests.fs`) · `PillarNineTests.fs` ·
`NoUnsafeTimeInCoreAnalyzer` (`Analyzers/`) · the change-algebra axiom block (T12–T16, A43) · the
verifiability-triangle audit corpus (`AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md`).

**C.5 — Policy and the decision algebra.** `Policy` (5-axis) + `merge` (`Policy.fs`) · `PolicyExpr` + `eval`/
`simplify`/`diff` (`PolicyExpr.fs`) · `TighteningIntervention` + `TighteningPolicy` (`Policy.fs`) ·
`Composition.fanOut` + `StrategyEvaluator` (`Strategies/Composition.fs`) · `NullabilityRules`/`ForeignKeyRules`/
`UniqueIndexRules`/`CategoricalUniquenessRules` (`Strategies/*.fs`) · `DecisionOverlay` + `ofComposeState`
(`DecisionOverlay.fs`) · `Tolerance`/`ToleratedDivergence` (`Tolerance.fs`) · `Classification`/`OverlayAxis`
(`Classification.fs`) · `VersionedPolicy.bumpKind`/`digestOf` (`VersionedPolicy.fs`) · `TighteningBinding.fromConfig`
(`TighteningBinding.fs`).

**C.6 — Orchestration, config, and the operator instrument.** `RunEnvelope.bracket` (`RunEnvelope.fs`) ·
`RunSpine`/`StageName`/`StagedOutcome` + `staged spine { }` (`RunSpine.fs`) · the `*Binding` family (nine modules) ·
`buildPolicyFromConfig`/`runWithConfigCore` (`Pipeline.fs`) · `Voice` catalog + `errorFrame`/`gateStatement`
(`Voice.fs`) · `Surface`/`View` (`Surface.fs`/`View.fs`) · `CapabilitySurvey` (`CapabilitySurvey.fs`) ·
`resolveFlowSpec` (`MovementSurface.fs:605`) · `OperatorConsole.withRun`/`RunFaces.run*` (`OperatorConsole.fs`/
`RunFaces.fs`).

**C.7 — Identity, registries, codecs, primitives.** `SsKey` + `serialize`/`deserialize` + `isSynthesizedRoot`
(`Identity.fs`) · `Coordinates` (`SchemaName`/`TableName`/`ColumnName`/`TableId`/`IdentifierBudget`,
`Coordinates.fs`) · `UuidV5` (`UuidV5.fs`) · `TransformRegistry` + `digest` (`TransformRegistry.fs`) ·
`RegisteredTransforms.chainSteps` (`RegisteredTransforms.fs`) · `RawValueCodec` (`RawValueCodec.fs`) · `SqlLiteral`
(`SqlLiteral.fs`) · `PrimitiveType`/`SqlStorageType` (`PrimitiveType.fs`/`SqlStorageType.fs`) ·
`SqlTypeCorrespondence` (`SqlTypeCorrespondence.fs`) · `SurrogateRemap` (`SurrogateRemap.fs`) · `UserRemap`
(`UserRemap.fs`) · `Ledger` (`LedgerSpec`/`Verified`/`writeAdmit`/`resumeAdmit`/`replay`, `Ledger.fs`) ·
`Episode`/`EpisodicLifecycle` (`Episode.fs`).

**C.8 — The testing surface.** `AxiomTests.fs` · `CatalogCodec.catalogGen` (`CatalogCodecTests.fs`) ·
`AdjunctionLawTests.fs` · `CapabilitySurveyTotalityTests`/`VoiceTotalityTests` · `TransformRegistryCompletenessTests`/
`RegisteredAllTransformsBidirectionalTests` · `OssysRowsetParityInventoryTests` (the ~25-file V1↔V2 parity
inventory) · `GoldenEmissionTests` + Golden corpus · `PolicyParserFuzzTests` · `Bench.fs` + `PerfHarnessScenarios` +
`PERF_HARNESS.md` · `TestCollections.fs` (the `Docker-SqlServer` + `Global-MutableState` serialization discipline).

**C.9 — The five cross-cutting dimensions the first pass missed (§II.9).** `ParallelSafe<'a>`
(`TopologicalOrder.fs:153`) + `Deploy.executeBatchParallel` (`Deploy.fs:468`) [concurrency] · `Bench` (`Bench.fs:103`)
+ the perf-gate [cost] · the three-layer injection defense (`Coordinates` + `Identifier.EncodeIdentifier` +
parameterization) + `DockerImageEmitter` credential flow [security] · `Retry.fs` (Polly transient classification +
the named mid-stream gap) + `Preflight.refusalOf → ExitCode` [failure-mode] · `LifecycleStore`/`CaptureJournal`
NDJSON + NM-34 forward-compat [persisted-state].

---

## Appendix D — The adversarial verdicts

Three perspective-diverse reviewers reviewed the full pooled catalogue before any synthesis. Their verdicts are
the reason the recommendations in Parts VI–VII carry the dispositions they do. Summarized faithfully from each
reviewer's record.

### D.1 — Groundedness ("is each grounded in real code, or hallucinated?")

> "I spot-checked every load-bearing cited type, file, line, and behavior against the actual F# sidecar. Verdict:
> the catalog is overwhelmingly well-grounded. **I KILLED nothing for being hallucinated** — no recommendation
> rests on a non-existent construct."

Confirmed on disk: `PhysicalForeignKey` is exactly a 6-field record with no trust field and `ofCatalog` takes no
`DecisionOverlay`; `ToleratedDivergence` has exactly 8 variants all `@ladder`-tagged Schema/Data with **zero**
Decision/Identity/Time tags; `ChannelDiff<'change>` is unified but its four builders are byte-identical copies
with the "mirrors `attributeDiff` EXACTLY" comment; `between` returns `Result` with exactly one `Ok` return-site
and `compose` recomputes `between` twice with dead `Error` arms; no inverse/negate exists; `ParallelSafe` is a
private-ctor derive-macro; `Bench` is the one module-level mutable `Dictionary` under lock; the `CreateTrigger`
None-skip is a literally-silent `()`; `citationOf` is a no-op with **exactly 112 sites and 42 Skips** (confirming
the count corrections precisely); `VersionedPolicy.digestOf` uses `sprintf "%A"`; `SourceKey`/`AssignedKey` are
bare unvalidated DUs while `RowQuantum` IS `[<Struct>]`; `genCatalogPair` is genuinely absent while `catalogGen`
exists; the H-050 Docker sweep is a Skip-stub.

Marked **revise** (not kill) where a premise was unverified (`OperationKey:Guid`; `EmitError`'s three-layer
bundle), where the strong form overreaches what reflection can prove (the A18 emitter-shape Fact), where a finding
depends on the migrate-leg-as-live-pipeline premise (which rests on the standing reverse-leg fact, not on-disk
code), and where the Transactionality-totality half is premature until an envelope is scheduled. **"The single
most valuable critic finding is `C3.F7`":** it correctly catches the four-way echo-chamber recommending the
ReadSide flat→composite change as a "faithfulness fix" when the `Identity.fs` comments flag ReadSide identity as a
deliberately separate IR domain.

### D.2 — Discipline ("does each respect the house moat?")

The disciplines defended, in priority order: (1) IR-grows-under-evidence / second-consumer / no-zero-consumer-
algebra — *the single most-violated discipline in the catalogue*; (2) adjunction-corollary-or-it-is-the-wrong-
feature; (3) the cutover-safety commitments do not loosen; (4) Core purity, typed-AST-over-string-builder,
named-erasure-never-silent, and the LINT-ALLOW-shaped-audit-trail-without-substance.

Sharpest interventions: **KILL/defer the full `SchemaMove` unification** (`L1.M1` — four enumerations agree today,
zero fired trigger, highest blast radius); **REVISE `C1.F1`'s transactionality to the honest-naming half only**
(`Preflight.fs:345-357` explicitly records the live wrapper as a survey-gated deferral with a stated trigger —
building it now violates the recorded deferral); **DEFER `L8.M3` (the DACPAC reader)** per `DECISIONS` (deferred-
until-materialized); gate the surrogate-key/Traversal/digest-persistence moves on their genuine second consumers;
and **CONFIRM `C1.F6` + `C3.F8`** as the catalogue's two explicit moat-defenses (what must stay absent; the
persisted-state-migration discipline). The recurring failure mode: the same primitive recommended 4–7 times under
different lens framings — **the synthesis should dedupe to one canonical move per primitive** (done in Part VII).

### D.3 — Leverage ("genuinely high-leverage, or busywork?")

Verified on disk: `ReadSide` already recovers `is_not_trusted` into the Catalog (`ReadSide.fs:1171`) so the read
leg is free; all 8 tolerances tag only Schema/Data, so the under-claiming mechanism is **inert** on Decision/
Identity/Time; `ReportRun` has `render` but no `toJson`; the trigger None-skip is real; `OperationalDiagnostics` is
a real **seventh** target; `ParallelSafe` is a real private-ctor primitive on the live deploy path. **Critically:
the OSSYS basis is `[module; entity]` while ReadSide's is `[schema; table]` — different namespaces — so the
four-times-converged "switch ReadSide to `synthesizedComposite`" is false; the real round-trip runs through
`recoverKindSsKey` (kinds, not attributes).** Top picks close the Decision-readback axis and raise the deepest
unraised rung (round-trip to real substrate / property-swept change algebra). Renames the operator already knows,
and abstractions with no fired trigger, are long-tail.

### D.4 — The completeness critic's net-new findings

Beyond adjudicating the chorus, the critic surfaced the five unmapped dimensions now in §II.9 (Concurrency/
`ParallelSafe`, Cost/`Bench`, Security/trust-boundary, Failure-mode/`Retry`, Persisted-state), the seventh emitter
target, the corrected counts, the connection between the transactionality gap and the absent groupoid inverse ("the
same gap viewed twice"), and the persisted-state-evolution discipline ("every fix that touches a serialized form
needs a migration story the chorus hand-waves"). It also flagged two dimensions worth a sentence each that no lens
touched: the **build/compile-order** dimension (repeatedly cited as the *reason* for duplication) and the
**`global.json` SDK pin** and the BCL-version-floor assumptions the built-in-obligation pillar rests on
(`CreateVersion7` present, `CreateVersion5` absent → `UuidV5` hand-rolled).

## Appendix E — The complete ranked recommendation catalogue

All 123 recommendations, by adversarial reviewer score (`+N` = net confirm/top-pick minus kill/red-flag; `pN` =
top-picks; `fN` = red-flags). Ids cross-reference Part III. This is the full source from which the twenty canonical
moves of Part VII were deduplicated.

| id | score | source | recommendation |
|---|---|---|---|
| L9.M1 | +6 (p3) | code-quality | Make the constraint-trust quadrant unrepresentable (→ M4) |
| C2.F1 | +6 (p3) | opportunistic | Decision-readback adjunction: route FK-trust + uniqueness through the general `PhysicalSchema.diff` (→ M1) |
| L3.M3 | +5 (p2) | testable | `DecisionNotReadBack` OpenGap tolerance (the honest fallback) (→ M1′) |
| C1.F5 | +5 (p2) | inverse | The Decision-axis erasure has no NAME — the one silent drop the algebra can't see (→ M1′) |
| C1.F6 | +5 (p2) | inverse | What must STAY absent: the §7 cuts are load-bearing in their absence (the moat) |
| L2.M3 | +4 (p1) | epistemic | Convert the `CreateTrigger` silent skip into a named Tolerance + DiagnosticEntry (→ M2) |
| L2.M4 | +4 (p1) | epistemic | Promote `citationOf` to a gated citation-integrity check (→ M16) |
| L3.M1 | +4 (p1) | testable | Decision-axis readback adjunction property (FK trust) (→ M1) |
| L4.M1 | +4 (p1) | F#-idiom | Adopt FsToolkit `validation { }` at the binding boundary (→ M9) |
| L4.M3 | +4 (p1) | F#-idiom | Extract a `JsonDocumentWriter` seam (→ M8) |
| L4.M5 | +4 (p1) | F#-idiom | `[<Struct>]` + smart ctors on `SourceKey`/`AssignedKey` (→ M6) |
| L6.M7 | +4 (p1) | IR-compress | Defer the `ComposeState`→evidence-map + `SchemaLoss↔Statement` merge (the disciplined deferral) |
| L7.M1 | +4 (p1) | fitness | In-assembly layer-dependency fitness Fact (→ M15) |
| L7.M3 | +4 (p1) | fitness | Analyzer-walker unit test + CI promotion of `run-analyzers.sh` (→ M15) |
| C1.F1 | +4 (p2) | inverse | The Transactionality/Rollback axis: A3 is a comment, not a value (→ M20) |
| C1.F2 | +4 (p2) | inverse | The groupoid inverse: `compose`+`norm` exist, no Delta has a negation (→ M12) |
| C1.F4 | +4 (p1) | inverse | The canary is a property, not a CI gate (→ M3) |
| C2.F5 | +4 (p1) | opportunistic | Swept change-algebra / T16 over generated `(A,B)` pairs (→ M3) |
| C3.F6 | +4 (p1) | completeness | Overstated/loose claims (citation counts 112/42, seven targets) — corrected |
| C3.F8 | +4 (p1) | completeness | The persisted-state-migration dimension is under-mapped (the cross-cutting prerequisite) |
| L1.M4 | +3 (p1) | domain | Name the FK-trust / decision-readback concept in the comparison surface (→ M1) |
| L1.M7 | +3 (p1) | domain | Rename the data-load artifact off the realization mechanism onto the domain move |
| L2.M2 | +3 (p1) | epistemic | Name Decision/Identity erasures as `ToleratedDivergence` variants (→ M1′) |
| L2.M7 | +3 | epistemic | Add a torsor-law property file for the Wave-6 change algebra (→ M3) |
| L2.P2 | +3 (p1) | epistemic | `ToleratedDivergence` (the closed named-erasure set — extend to all axes) |
| L3.M4 | +3 | testable | Generator-of-valid-deltas torsor sweep (T12–T16) (→ M3) |
| L3.M5 | +3 | testable | Docker-bound full adjunction FsCheck sweep (unblocked by reverse-leg) (→ M3) |
| L3.M7 | +3 | testable | No-silent-drop property for unparseable triggers (→ M2) |
| L4.M2 | +3 | F#-idiom | Replace `digestOf` `sprintf "%A"` with a length-prefixed typed projection (→ M5) |
| L4.M4 | +3 | F#-idiom | Collapse the four `PassChainAdapter` lifts + two inline steps into one combinator (→ M7-adjacent) |
| L5.M2 | +3 | algebra | Name the metric: make the triangle inequality a property test (→ M11) |
| L5.M3 | +3 (p1) | algebra | Retire the vestigial `Result` on `between` (→ M13) |
| L6.M2 | +3 | IR-compress | Extract a `JsonDescription` seam for the JSON siblings (→ M8) |
| L6.M5 | +3 | IR-compress | Collapse the four `PassChainAdapter` lifts into one `(read, writeBack)` combinator |
| L6.P2 | +3 (p1) | IR-compress | `ChannelDiff<'change>` (the carrier to extend into a `ChannelSpec`) |
| L7.M4 | +3 | fitness | Linter self-test: a fixture corpus proving the grep rules still bite |
| L7.M7 | +3 | fitness | Widen `NoUnsafeTimeInCoreAnalyzer` from suffix-grep to a full syntax visitor |
| L8.M1 | +3 (p1) | creativity | Join the policy-version + approval plane into the durable episode (latent) |
| L8.M2 | +3 | creativity | A machine (JSON) lens over the `ChangeManifest` series for SSIS (→ M18) |
| L8.M4 | +3 | creativity | Promote `digestOf` to an explicit projection (prereq for the policy-timeline join) (→ M5) |
| L9.M6 | +3 | code-quality | Make the channel-diff `Renamed` folds total-by-type |
| C2.F2 | +3 | opportunistic | `ChangeManifest.toJson` — the CDC-norm as a machine-queryable artifact (→ M18) |
| C2.F3 | +3 | opportunistic | Units-of-measure extension to the data-norm deltas (→ M19) |
| C2.F4 | +3 | opportunistic | `[<Struct>]` promotion of the surrogate orientation pair (→ M6) |
| C3.F1 | +3 | completeness | The Concurrency/parallel-deployment dimension is unmapped (`ParallelSafe`) (→ §II.9) |
| C3.F2 | +3 | completeness | A seventh sibling-emitter target (`OperationalDiagnostics`) was omitted (→ §II.2) |
| C3.F7 | +3 | completeness | The ReadSide flat-synthesis "leak" may be correct-by-design (the echo-chamber catch) (→ §IV.3) |
| L2.M1 | +2 | epistemic | Give the Decision axis a real readback comparator (→ M1) |
| L2.M6 | +2 | epistemic | Make the L1/L3 witness binding structural (by exact test name) (→ M16) |
| L2.P1 | +2 | epistemic | `PhysicalForeignKey`/`PhysicalIndex` (the readback comparison surface) (→ M1) |
| L2.P3 | +2 | epistemic | `citationOf` (the axiom cross-reference helper) (→ M16) |
| L3.M2 | +2 | testable | Decision-axis readback adjunction property (uniqueness promotion) (→ M1) |
| L3.M6 | +2 | testable | Attribute-rename identity-threading property + extended-property recovery (the real Identity residue) |
| L3.P1 | +2 | testable | `PhysicalForeignKey` (the canary's FK comparison surface) (→ M1) |
| L3.P3 | +2 | testable | `catalogGen` (the one real recursive Catalog generator → `genCatalogPair`) (→ M3) |
| L3.P4 | +2 | testable | `ToleratedDivergence` (the L2 proof surface / named-erasure DU) |
| L3.P5 | +2 | testable | `citationOf` (the axiom→test cross-reference) (→ M16) |
| L4.M6 | +2 | F#-idiom | Active-pattern the string-prefix dispatch routers (N≥3 match-macro) |
| L4.M7 | +2 | F#-idiom | Render `tableQualified` through a ScriptDom `SchemaObjectName` |
| L4.M8 | +2 | F#-idiom | Extend the units-of-measure delta discipline to CDC and row/null counts (→ M19) |
| L5.M1 | +2 | algebra | Reify the diff channel as data: one `ChannelSpec`, one descend fold, one apply fold (→ M7) |
| L5.M5 | +2 | algebra | Lift the four tightening passes into one `TighteningStrategy` value (→ M10, deferred) |
| L5.P1 | +2 | algebra | `CatalogDiff` (the SchemaDelta carrier / torsor element) |
| L5.P2 | +2 | algebra | The diff channel (`ChannelDiff<'change>` + its four builders/appliers) (→ M7) |
| L5.P3 | +2 | algebra | `norm`/`channelCounts` (the data/schema metric) (→ M11) |
| L6.M1 | +2 | IR-compress | Lift the four tightening passes into a `TighteningStrategy` descriptor (→ M10, deferred) |
| L6.M3 | +2 | IR-compress | Drive the kind-scoped diff channels from a `ChannelSpec` value traversed once (→ M7) |
| L6.M4 | +2 | IR-compress | Retire the dead `ScriptDomGenerate.toText` renderer; make `Render` the one interpreter |
| L6.P1 | +2 | IR-compress | `Composition.fanOut` (already-earned combinator) |
| L6.P3 | +2 | IR-compress | `ArtifactByKind.perKind` (already-earned combinator) |
| L6.P4 | +2 | IR-compress | `PassChainAdapter` lift family (→ M7-adjacent) |
| L8.M5 | +2 | creativity | Timeline-aware policy churn series (the policy companion to `ChangeManifest.pathLength`) |
| L8.P1 | +2 | creativity | `Episode` (the durable provenance snapshot) |
| L8.P2 | +2 | creativity | `ChangeManifest` + `ReportRun` (→ M18) |
| L8.P4 | +2 | creativity | `VersionedPolicy.digestOf` (the policy identity) (→ M5) |
| L9.M3 | +2 | code-quality | Validate the surrogate orientation pair to its siblings' standard (→ M6) |
| L9.M4 | +2 | code-quality | Replace the policy digest's structural-printer with an explicit token projection (→ M5) |
| L9.P1 | +2 | code-quality | Reference constraint-trust state (→ M4) |
| L9.P4 | +2 | code-quality | `VersionedPolicy` content digest (→ M5) |
| L9.P5 | +2 | code-quality | `PhysicalForeignKey` (the round-trip comparison surface) (→ M1) |
| C3.F4 | +2 | completeness | The Security/trust-boundary dimension is unmapped (→ §II.9) |
| C3.F5 | +2 | completeness | The Failure-mode/retry dimension is fragmented and never assembled (→ §II.9) |
| L1.M2 | +1 | domain | Give the protein workflows a typed identity (`Protein`/`Flow` DU) |
| L1.M6 | +1 | domain | Introduce a Glossary/Ubiquitous-language boundary type |
| L1.P2 | +1 | domain | `Protein` / `OperatorFlow` |
| L1.P5 | +1 (f… ) | domain | ReadSide identity synthesis basis (contested) |
| L1.P6 | +1 | domain | `RefactorLogRef` (the provenance artifact handle on Episode) |
| L2.M5 | +1 | epistemic | Generate `AXIOMS.md`/`PRODUCT_AXIOMS.md` bucket lines from the gated test surface (→ M16) |
| L3.M9 | +1 | testable | `Totality<'code>` combinator + `AxiomEntry` registry (→ M17) |
| L3.P2 | +1 | testable | `PhysicalSchema.ofCatalog` (the model-side projection — take the overlay) (→ M1) |
| L4.P1 | +1 | F#-idiom | `SourceKey`/`AssignedKey` (→ M6) |
| L4.P2 | +1 | F#-idiom | `VersionedPolicy` digest input (→ M5) |
| L4.P3 | +1 | F#-idiom | The `PassChainAdapter` lift surface |
| L4.P4 | +1 | F#-idiom | The `*Binding` applicative join (→ M9) |
| L5.M4 | +1 | algebra | Add a `Traversal` optic to unify single-focus `Lens` and bulk `mapKinds` (→ M14, gated) |
| L5.P4 | +1 | algebra | `Lens` (the optic vocabulary) |
| L7.M2 | +1 | fitness | Emitter-shape totality Fact (A18 as a closed-set proof) (→ M15) |
| L7.M5 | +1 | fitness | Bind the migrate/transfer leg to the registry: a same-source coverage Fact (→ M15) |
| L7.M6 | +1 | fitness | Promote the Data→SSDT cross-target edge into a neutral `Sql` kernel (gated on 2nd consumer) |
| L7.P1 | +1 | fitness | The hexagonal layer boundary (→ M15) |
| L7.P3 | +1 | fitness | The determinism guard (→ M15) |
| L7.P4 | +1 | fitness | The conformance scripts themselves (→ M15) |
| L8.M3 | +1 | creativity | A DACPAC reader: `Ingest` specialized to a second substrate (deferred-until-materialized) |
| L8.P3 | +1 | creativity | `Ingest` (the reader leg of the adjunction) |
| L9.P3 | +1 | code-quality | `SourceKey`/`AssignedKey` surrogate orientation pair (→ M6) |
| C1.F3 | +1 | inverse | Permissions has a gate but no AXIS — grants surveyed, never projected/diffed |
| C3.F3 | +1 | completeness | `Bench` analyzed only as a test nuisance, never as the cost/observability monoid (→ §II.9) |
| L1.M1 | 0 | domain | Lift the move alphabet into one closed `SchemaMove`/`DataMove` DU (deferred; do the zero-risk rename) |
| L1.M3 | 0 | domain | Unify SsKey synthesis at ReadSide onto the composite form (echo-chamber — downgraded) |
| L1.M5 | 0 | domain | Promote the remaining primitive-typed identities to VOs (`OperationKey`, `RefactorLogRef`) |
| L1.P1 | 0 | domain | The move alphabet as a type (`SchemaMove`/`DataMove`) (deferred) |
| L1.P3 | 0 | domain | `OperationKey` (refactorlog operation identity) |
| L1.P4 | 0 | domain | Decision-axis FK trust (NoCheck) in the comparison surface (→ M1) |
| L2.P4 | 0 | epistemic | `CanaryResidual.Collector` + the detector set (the `observed` population) |
| L2.P5 | 0 | epistemic | The axiom registry (`AxiomTests.fs` + the matrix/gate surfaces) (→ M16) |
| L3.M8 | 0 | testable | T-VI spanning totality tests (Transactionality/Rollback, Connection pre-flight) (revise: connection only) |
| L6.M6 | 0 | IR-compress | Split `EmitError` into three layer-scoped error types (the inverse move; deferred) |
| L6.P5 | 0 | IR-compress | `EmitError` (the one candidate to split, deferred) |
| L7.P2 | 0 | fitness | A18 (Π never consumes Policy) (→ M15) |
| L7.P5 | 0 | fitness | The single-source-of-truth pass chain (`RegisteredTransforms.chainSteps`) |
| L9.M2 | 0 | code-quality | Restore identity round-trip by typed-segment ReadSide synthesis (echo-chamber — downgraded) |
| L9.P2 | 0 | code-quality | ReadSide-recovered SsKey (contested) |
| L9.M5 | −1 | code-quality | Retire the dead foreign-key config algebra (revise: delete only with a DECISIONS amendment) |

---

*End of the unabridged edition. For the argument without the apparatus, see [`THE_VECTOR.md`](THE_VECTOR.md).*
*Hold the spine.*