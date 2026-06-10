# CLAUDE.md — V2 Sidecar Navigation

This file is the first-read pointer for fresh agents. It does **not**
substitute for the canonical documents — it points at them. All
substantive disciplines, axioms, and resolved questions live in the
files this document indexes; this file's only job is to make sure
nothing load-bearing is missed.

If you are an agent opening this codebase for the first time, read
the documents in the order this file lists. If you are an agent
returning across sessions, this is the navigation surface; the
substantive surfaces are unchanged.

## Reading order for a fresh agent

`KICKOFF.md` is the fresh-agent first-message brief — read it first as a
5-minute orientation that points at the canonical surfaces in the order
below.

**`THE_USE_CASE_ONTOLOGY.md` is the single index of the target — read it
first of the substantive surfaces.** It is the masterwork that synthesizes
the scattered north-star corpus into one document: the closed amino-acid
alphabet (the atomic moves of change-over-time), the protein catalog (every
operator workflow as an ordered amino-acid chain), the master matrix (every
move × context cell, across all ten axes), the laws (the torsor T12–T16/A43,
the faithfulness ladder, the intent filter, CDC-as-norm minimality), the
glossary, and a completeness checklist. It is **target-first** (the ideal
end state, in full complexity) and declares itself the index that subsumes
the sprawl: `NORTH_STAR.md`, `WAVE_6_ONTOLOGY/_ALGEBRA/_MORPHOLOGY.md`,
`VISION.md`, `PRODUCT_AXIOMS.md`, `SPINE.md`, and the audits each become
**provenance**, indexed in its §6.2. Read it to be oriented; consult the
others for the depth on the fragment they originated. For *where the code
actually stands against this matrix* (the gaps), read
`DEBRIEF_2026_06_02_ISOMORPHISM_CLIMB_AND_BACKLOG.md` (the current-state
ledger): the masterwork is the target, the debrief is the distance to it.

**`NORTH_STAR.md` is the apex vision (now indexed by the masterwork above) —
read it before `VISION.md`.**
It states the bullseye (the Total Projection: the adjunction made total,
executable, and self-describing — fidelity as a theorem the engine proves
about itself) and **supersedes `VISION.md`'s strategic frame**; `VISION.md`
remains valid as the cutover-era operational vision (the first ring of the
bullseye). Per `DECISIONS 2026-05-22 — CLAUDE.md reading-order update` (now
amended for the apex), `VISION.md` follows `NORTH_STAR.md`; the companion
strategic surfaces (`SPINE.md`, `PLAYBOOK.md`, `STAGING.md`) are
**on-demand** references — read when the relevant work surfaces them, not
as part of the canonical first-read pass.

**`AUDIT_2026_05_31_FIVE_AXIS_REDTEAM.md`** is the apex's **substantiation
audit** — the full-fidelity six-agent red-team that established the
isomorphism ladder (L1 witness → L2 faithful → L3 composed), the two added
totalities (T-V orthogonality, T-VI spanning), and the one-command A→B
migration (Promise 8) as the L3 bullseye. It is the source of record for
`EXECUTION_PLAN.md` **Wave 6** (the buildable climb to L2/L3) and the
`NORTH_STAR.md` §1/§3/§4/§5 elevation. Read it before opening any Wave 6
slice — it carries every per-axis finding (file:line), the master severity
table, and the complete acceptance-criteria catalog. Pairs with the
`AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md` (the L1↔L2↔L3 bucket model).

**`WAVE_6_ONTOLOGY.md`** (2026-06-01 masterwork) is the audit's **functional
sibling**: where the audit established *that* the isomorphism is unproven, the
ontology establishes *what the isomorphism is to* — the **core moves of change
over time** (Add / Remove / Rename / Reshape / Reidentify / Move / Accumulate),
grounded bottom-up from physical SQL-Server mechanics (pages / locks / CDC /
identity) through the operator's concrete premise (a **publication-and-provenance
engine** for an evolving relational model — external SSIS consumer; Dev→UAT
rekey; PROD-empty; the eject — *not* a live-PROD deploy engine) up to the
entities the totalities quantify over. Its discipline is **right-by-function,
not by name**: every entity carries a *discriminating predicate* (the input on
which a plausibly-named-but-wrong implementation diverges). It is the source for
`EXECUTION_PLAN.md` Wave 6.F (publication & provenance) and the premise
re-prioritization (PROD-gates 6.C.* deferred behind provenance; 6.A.12 explicit
ALTER repositioned as a lens; DacFx owns the schema ALTER, the engine owns the
data movement measured by CDC). Read it before any Wave 6.A.10+/6.B/6.C/6.D/6.F
slice — alongside the audit.

**`WAVE_6_ALGEBRA.md`** (2026-06-01) is the ontology's **formal reification** —
the change-ontology cast as the domain's algebra, postulated from first
principles so every law is a *balanced equation* with variables in native
form. The revealing move: **State is a torsor over Delta** (`⊖` = `between`,
subtraction; `⊕` = `applyDiff`, the affine action) — so the round-trip /
identity / composition laws are the three Weyl axioms of an affine space (they
balance by construction); `Move`s generate `Delta`; the change-measure `‖·‖`
(physically the CDC capture count) is the norm; `emit` is a norm-preserving
functor; **T16 (the Project square commutes) is the master equation**, with
the schema and data legs its two projections and the iso-ladder its
faithfulness gradient. Reified into the formal system as `AXIOMS.md` **T12–T16
+ A43** (executable witnesses in `AxiomTests.fs`). Read it when you need the
*equation* a slice must balance; read `WAVE_6_ONTOLOGY.md` for the
*interpretation* and `AUDIT_2026_05_31` for *why the climb exists*.

**`WAVE_6_MORPHOLOGY.md`** (2026-06-01) is the **territory** the ontology and
algebra were drawn over — a four-agent structural research pass that read the
calculus *from* the codebase. Its load-bearing finding: **the calculus is
*latent*, not *activated*** — the carriers (nouns of change) are reified and
mature, but the operator-verbs (`Move`/`Delta`/`‖·‖`/`π`/`Torsor`) have no code
home; the diff-machinery (`between`/`applyDiff`/refactorlog/SchemaMigration/
`Lifecycle`) has **zero production callers** (the engine ships `realize(B)`,
not `emit(B ⊖ A)`). **[Superseded 2026-06-04 — the diff-machinery is now wired into
production (`MigrationRun` / `EjectRun` / `TransferRun` / `Pipeline` + SSDT emitters);
this paragraph preserves the 2026-06-01 morphology snapshot. See the debrief +
`DECISIONS.md` 2026-06 `migrate A B` resolution + `AUDIT_2026_06_04_PERIPHERY_ADVERSARIAL.md` §0.]**
And there is **no durable episode** to integrate over (the
FTC runs only in-memory in tests). It maps the **amino acids** (structural
primitives + maturity + file:line), the **proteins** (concrete use cases), and
the **concern-movement field** (the 2-D `∂κ/∂emission` × `∂κ/∂episode` field,
mostly dark). Read it for the concrete *as-is* before building any Wave-6
slice; it names the future-state substrate (`EXECUTION_PLAN.md` 6.H: the
multi-plane `Episode`, the `LifecycleStore`, `CatalogDiff.compose`, the
change-manifest).

**`DEBRIEF_2026_06_02_ISOMORPHISM_CLIMB_AND_BACKLOG.md`** (2026-06-02) is the
**canonical current-state debrief + L2/L3 backlog** — the single reconciled
surface for "where is the isomorphism climb *right now* and what's left." It
supersedes the *status* (not the framing) of the planning docs above: most of
them describe the pre-6.A state the codebase has since climbed past (6.A.* /
6.B.1 / 6.B.2 / 6.D.1 / 6.H.* all landed). It carries (a) the matrix
reconciled against HEAD, cell by cell; (b) a 20-row fidelity ledger (G1–G20)
with file:line, ladder level, and the named refusal or silent erasure each is;
(c) a 10-cluster slice backlog (A–J) with scope, signatures, acceptance
witnesses, deps, and survey-gating; (d) the critical path and the
survey-dependent split. **Read it before opening any Wave-6 slice** — it is the
fastest path to the corrected current state. The other Wave-6 surfaces remain
canonical for their *framing* (ontology / algebra / morphology); the debrief is
canonical for the *state and the backlog*. When the climb advances, update the
debrief first, then propagate.

**`V2_DRIVER.md`** (codified 2026-05-10 chapter 3.7 sidebar; principal-PO
discussion) is the destination-KPI document — the *why* the cutover
ladder bends toward V2-driver mode, the per-axis correctness stakes,
the chapter ownership map. Slowest-rhythm strategic surface after the
manifesto.

**`BACKLOG.md`** (re-canonicalized 2026-05-16 — Bridge wave) is the
operational ledger — *what is in flight, what is scheduled, what is
blocked, what is shipped, and what is sunset*. Interweaves V2_DRIVER's
per-phase chapter sequence with the Bridge wave's gradient transitions,
cross-cutting infrastructure work, V1-side adoption opportunities, and
the wave-wide risk register. Refreshed at every chapter close and at
every Bridge method gradient transition.

Read V2_DRIVER for the destination; read BACKLOG for the path. Both
before any chapter open; BACKLOG before any chapter-mid status review.

1. **`HANDOFF.md`** — bridge letter from the most-recent-closed
   chapter. Short on purpose. Names what is load-bearing and what
   is deferred. Older chapters' handoff letters preserved at
   `HANDOFF_CHAPTER_<N>.md` (currently `HANDOFF_CHAPTER_1.md`).
2. **`VISION.md`** — *cutover-era* strategic frame (superseded as apex by
   `NORTH_STAR.md`; read that first): cutover as forcing
   function; sibling chorus + verification posture; acceptance
   criteria; cutover fallback ladder. Read for the *why*. Companion
   strategic surfaces (`SPINE.md` for the categorical structure;
   `PLAYBOOK.md` for technical guidance; `STAGING.md` for the Stage
   0 foundation phase; `V2_DRIVER.md` for the full ~375-item
   inventory) are referenced on demand.
3. **`CHAPTER_3_1_CLOSE.md`** — chapter-3.1 close synthesis (sessions
   27–36). The canary chapter. Read for the M1–M3 milestone sequence;
   the four meta-codifications (bench-driven optimization, stream-
   realization pattern, five-agent epistemic-tier audit, harmonization-
   via-parameterization); forward signals into chapter 3.2 / 3.5 /
   4.1 / 4.2. **Companion file:** `AUDIT_2026_05_DDD_HEXAGONAL_FP.md`
   carries the chapter-close five-agent DDD/Hexagonal/FP audit with
   Tier 1/2/3/4 backlog by epistemic level + leverage. The next-
   chapter agent reads both at chapter open.
3.5. **`AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md`** + **`PRODUCT_AXIOMS.md`** —
   the L1↔L2↔L3 verifiability triangle. The audit is the integrator's
   view: every existing L2 axiom mapped to L1 structural underwriting
   and L3 product behavior; every gap (unnamed axiom, convention-only
   enforcement, untested formalism) classified by bucket (A/B/C/D); 56
   L3 axioms catalogued; three campaigns proposed (A: cutover-blocker
   unnamed axioms; B: structural fortification subsuming the prior
   slice-2/slice-3 work; C: Tier-2 unnamed axioms + boundary VOs +
   Config strengthening). `PRODUCT_AXIOMS.md` is the constitutional
   sibling — terse L3 axiom statements grouped by core concern
   (schema/data/identity/diagnostics/cutover-safety + cross-cutting).
   Read the audit Part I (framing) + Part IX (campaigns) at chapter
   open; consult Parts III/IV/V/VI/X as reference during slices that
   touch the corresponding surfaces.
4. **`CHAPTER_2_CLOSE.md`** — chapter-2 close synthesis (sessions
   13–25). Read for the OSSYS adapter chapter's accumulated
   state (25 translation rules), the three-class typology, the
   meta-codifications (chapter-mid-audit; trace-before-fixture;
   V1-envelope-walk), and the chapter-3 forward signals.
   **Companion files at the projection root:**
   `CHAPTER_2_AUDIT_3_OSSYS_COMPLETENESS.md` (subagent #3's
   full audit report); `CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md`
   (subagent #4's chapter-open input); and
   `CHAPTER_3_PRESCOPE_SNAPSHOT_ROWSETS.md` (subagent #5's
   chapter-open input).
5. **`CHAPTER_1_CLOSE.md`** — chapter-1 close synthesis (sessions
   1–12). Read for historical context. Some priorities listed
   there have been resolved by chapters 2 / 3.1; the disciplines
   and load-bearing commitments persist.
5. **`AXIOMS.md`** — the formal system. A1–A34 / T1–T11 with
   amended originals. A18's amendment near the bottom is the
   load-bearing form for sibling Π's; the original A18 carries a
   forwarding pointer. A1's bound on identity-survives-rename
   through the JSON path is documented at the bottom (added
   session 23). The "Amendments scheduled (chapter close)"
   section at the very bottom carries the placeholder list for
   pending amendments (T1 binary-normal-form; T11 structural-type
   encoding; T11 diff-typed inputs; A1 four-variant SsKey; A35
   candidate; A36 candidate; A32 cash-out) — chapter agents fill
   the bodies at chapter close per `DECISIONS 2026-05-22 — Stage
   0 foundation phase`.
6. **`DECISIONS.md`** — append-only resolved-questions log. Read the
   most recent ten entries first; older entries remain in force
   unless explicitly superseded. Two indexes at the top:
   *Active deferrals* (catches silent-trigger fires across chapters)
   and *Operating disciplines* (cross-cutting practices, pointing
   at the substantive entries). The Stage 0 governance burst
   (2026-05-22) cluster at the bottom carries the five
   pre-chapter-3 entries: Stage 0 commitment, R6 split-brain rule,
   chapter 3 sequencing, CLAUDE.md reading-order, T-30 / T-15
   fallback ladder gates.
7. **`ADMIRE.md`** — the canonical V1-reference register. One entry
   per V1 component admired and placed in V2. Three modes:
   V1-migration / V2-growth / hybrid (`DECISIONS 2026-05-13` —
   admire spectrum). Multi-session chapters use `extracting (in
   flight, N slices)` while in flight (session 23 amendment).
   Under the V2 self-containment discipline (`DECISIONS 2026-05-16
   (later)`), ADMIRE is the editorial inheritance ledger: each
   carbon-copy event records the V1 source, the V2 location, the
   refactor status, and the date inherited. File-header citation
   comments in V2's source link back to the corresponding ADMIRE
   entry.
8. **`README.md`** — surface-level orientation; updated at chapter
   closes. Not the source of truth for any specific question.
9. **The code.** `Projection.sln`. Strategies in
   `src/Projection.Core/Strategies/`; passes in
   `src/Projection.Core/Passes/`; sibling Π emitters in
   `src/Projection.Targets.{SSDT,Json,Distributions}/`; F# adapters
   in `src/Projection.Adapters.{Sql,Osm}/`. The OSSYS adapter at
   `src/Projection.Adapters.Osm/CatalogReader.fs` is chapter 2's
   substantive deliverable.

## Operating disciplines — the cross-cutting practices

These disciplines cut across substantive work. Each links to its
codifying DECISIONS entry; if you find yourself working against one
of them, write the amendment first.

| Discipline | Where to find the rationale |
|---|---|
| **V2-driver as destination KPI (the project's north star)** — V2 reaches V2-driver mode for the cutover by being provably correct on every axis V2 owns (schema, data, identity, diagnostics, and any future sibling), with provable correctness defined as structural-type-level enforcement plus per-axis property tests. Every chapter, every slice, every primitive design biases toward V2-driver. V2-augmented is the gate; V2-driver is the destination. V1 stays warm through cutover+30 as fallback; V1 sunset begins after one full schema-evolution cycle on V2 emissions. The CDC-silence-on-idempotent-redeploy property test (chapter 4.1.B) is the highest-leverage single deliverable in the entire chapter sequence. | `V2_DRIVER.md` (the standalone codification document; supersedes the implicit "V2-augmented as floor; V2-driver as aspirational" framing in `DECISIONS 2026-05-22 — R6: Split-brain governance rule`); `DECISIONS 2026-05-10 — V2-driver as destination KPI` (the formal codification entry) |
| **Domain-first naming and ubiquitous-language consistency (pillar 8; chapter 3.7 sidebar)** — every named type / function / file / module / test in V2 MUST embody the four-question domain-naming analysis BEFORE the name is committed: (1) what domain concept does this represent (articulate it in cutover-business terms); (2) does V2 already name this concept somewhere (use the same name; ubiquitous-language consistency across Core / Adapters / Targets / Pipeline / CLI); (3) is the proposed name concept-shaped (what it IS) or action-shaped (what it DOES); (4) generic-suffix smell test — Helper / Util / Manager / Service / Handler / Processor / Wrapper / Builder / Factory / Provider stop the agent. If #4 fires, find the concept (rename) or restructure (the concept is being squashed). The named failure mode is **domain-blind naming**: when a name answers "what does this DO" rather than "what does this REPRESENT in the domain." Fails to put the domain concept in the type system; the agent feels productive (a name exists; the code compiles) without doing the domain-modeling work. **No lint enforcement** — heuristic syntactic checks misfire on legitimate uses (e.g., `LineageBuffer` is concept-shaped despite the "Buffer" suffix). The discipline-document path catches what the heuristic can't. See `PLAYBOOK.md` decision tree "When you reach for a name" for the executable form. Worked precedents (concept-shaped, ubiquitous): `Catalog` / `Module` / `Kind` / `Reference` / `SsKey` / `RemovalReason` / `AnnotationDetail` / `Coordinates.TableId` / `RawValueCodec` / `SqlTypeCorrespondence` / `BatchSplitter` / `DatabaseNameGenerator` / `EmissionPolicy` / `LineageBuffer`; **Transfer-epic lexicon** (`Projection` / `Ingestion` / `Transfer` / `SubstrateRole` `Source`/`Sink` / `SchemaContract` / `IdentityDisposition` / `SurrogateRemapContext` — `DECISIONS 2026-05-24`). Worked rename: `T11TypeTheoremTests.fs` → `SiblingEmitterContractTests.fs` (chapter 3.7 slice ε; concept-shaped name names what the file IS, not which theorem ID it cites); `PRESCOPE_REVERSE_IMPORT.md` → `PRESCOPE_TRANSFER.md` (2026-05-24; "reverse" framed a bolt-on, but the capability is the other leg of the H-050 adjunction — `Ingestion` is `Projection`'s named peer, not a reversal). | `DECISIONS 2026-05-10` — Domain-first naming and ubiquitous-language consistency (pillar 8); `DECISIONS 2026-05-24` — Transfer-epic vocabulary; `PRESCOPE_TRANSFER.md` |
| **LINT-ALLOW substantive-rationale discipline** (chapter 3.7 sidebar; pillar 7 amendment) — every per-line `LINT-ALLOW` marker on a string-composition / built-in-substitute site MUST embody the four-question analysis BEFORE the marker is committed: (1) what is the use-case-specific library; (2) is it already in the codebase; (3) what is the cost of using it (visibility lift + perf class + dep weight); (4) is there a structural reason it doesn't apply. If #4 is "no," there is no shortcut — there is the work (lift visibility, add helper, refactor call site). The named failure mode is **performance-of-compliance**: a marker shaped like an audit trail without the substance. The lint passes, the vocabulary fits, the tests are green — and the structural commitment is unmet. The discipline document does the catching the heuristic can't. See `PLAYBOOK.md` decision tree "When you reach for a string-composition primitive" for the executable form. Lint Rule 27 maintains an inventory + soft floor; substance lives in the discipline. | `DECISIONS 2026-05-10` — LINT-ALLOW substantive-rationale discipline (worked counterfactual: slice-β `Render.columnSqlType` shortcut → slice-β' ScriptDom delegation; cost was 87 LOC) |
| **Text-builder-as-first-instinct discipline** (chapter 4.1.A close arc; pillar 1 + pillar 7 amendment; Tier-3 codification) — every new SQL- or text-emitting consumer starts on the typed-AST library, not StringBuilder. Protocol: (1) BEFORE the first `StringBuilder()` / `String.Concat` / `sprintf`, articulate the typed-AST library that produces the structure being emitted (ScriptDom `MergeStatement` / `CreateTableStatement` / `InsertStatement`; `Utf8JsonWriter` / `JsonNode`; `XmlWriter` / `XDocument`; `Microsoft.SqlServer.Dac` for .dacpac); (2) cross-check the precedent emitters (`SsdtDdlEmitter.emitSlices` / `StaticSeedsEmitter.renderMerge` / `JsonEmitter.emit` are the patterns); (3) first draft uses the typed AST; (4) LINT-ALLOWs at terminal text boundaries only (`SqlLiteral.toString`'s `'<raw>'` quoting; the GO-batch suffix on a rendered MERGE; cross-platform-deterministic relative-path concatenation). The named failure mode is **text-builder-as-first-instinct**: the agent reaches for StringBuilder as the default, then attaches LINT-ALLOWs once the lint surfaces. Each LINT-ALLOW is individually defensible (per the substantive-rationale discipline); the *aggregate* is the bug — six LINT-ALLOWs at one MERGE site means the typed-AST migration was never attempted in the first place. **Hard-requirement Tier-3 deferrals** (per Active deferrals index): chapter 3.x DacpacEmitter MUST adopt `Microsoft.SqlServer.Dac` (DacFx); chapter 4.1.B slices ε / ζ MUST adopt `ScriptDomBuild.buildMergeStatement` from slice α. The chapter agent reads these entries at chapter open. | `DECISIONS 2026-05-10` — Text-builder-as-first-instinct discipline (worked counterfactuals: chapter 3.7 slice β shortcut + StaticSeedsEmitter slices α/β shortcut; both retired via ScriptDom typed AST) |
| **Totality-contract verification for total functions over the IR** (Wave 6; codec slice) — a function claiming to be *total over the IR* (codec, diff, T11 emitter, full-variant traversal) carries a totality contract whose source of truth is the **IR inventory** (closed DUs + record field lists reachable from `Catalog`). **Verify totality against the inventory; do not assert it** (independent audit for keystone artifacts; exhaustive `match` where the shape allows). The named hazard is **`{ X.create … with … }` default-substitution**: when reconstruction rebuilds an aggregate via record-update over a smart constructor, every field `create` does not set MUST appear in the `with` block, else it silently inherits the constructor default (the compiler does NOT flag the omission — `Index.AllowRowLocks`/`AllowPageLocks` default `true`, `Reference.IsConstraintTrusted` `true`, `Kind.IsActive` `true`). Detection: (#fields set by `create`) + (#fields in `with`) = total field count. This is the bare-value smart-constructor pattern's dual cost; carry it to every Wave-6 IR-reconstruction site (`LifecycleStore` 6.H.2, change-manifest 6.H.4, `migrate` 6.D.1). | `DECISIONS 2026-06-01 — Operating discipline: totality-contract verification for total functions over the IR` |
| **Declarative test inputs + the universal law over a constructed-valid generator** (Wave 6; codec slice; extends text-builder-as-first-instinct to test inputs) — (1) test inputs are **declarative edits of the producer's own valid output** (parse to the typed DOM, mutate, re-serialize), never hand-authored wire format (which duplicates the producer's format so a rename makes the test *lie* not *break*); (2) state the **universal law** (`∀ c. deserialize (serialize c) = Ok c`) as an FsCheck property over a **constructed-valid generator** (draw FK targets / index columns from already-chosen keys — validity is constructed, not generate-and-filtered) that **reuses the per-variant alphabet lists** the example tests define, so the variant space is enumerated once and the example/property suites cannot drift. Both suites belong: the property proves universality + random nesting; the examples pinpoint *which* variant regressed. Pitfalls: `open Xunit` before `open FsCheck` (else `Xunit`→`FsCheck.Xunit`, FS0893); wrap nullable `JsonNode` navigation in a `req` helper. | `DECISIONS 2026-06-01 — Operating discipline: declarative test inputs + the universal law over a constructed-valid generator` |
| **Audit during validation** — when something second-order surfaces during the work, act on it before shipping. Five paydowns across sessions 4, 5, 7, 8, 11; three more during session 14. | `DECISIONS 2026-05-09 — Audits surface things not on the agenda` (line 764) |
| **IR grows under evidence, not speculation** — types, fields, DU variants, and helpers land when a consumer demands them. Two-consumer threshold for helper extraction. | `DECISIONS 2026-05-07` — IR grows under evidence, not speculation |
| **Total decisions, named skips** — strategies return decisions for every input; "no decision" is a named `KeepReason` variant rather than silence. | `DECISIONS 2026-05-11 — Strategy-layer codification: empirical verdict after the fourth instance` (line 1557; refinement 3) |
| **Closed-DU expansion empirical-test discipline** — when adding a variant, F# exhaustiveness errors should light up only at match sites; if callers outside the variant's module need reshaping, the seam is wrong. **Record-extension generalization confirmed at chapter 3.2 close (2026-05-10)**: the discipline holds equivalently for record-field additions — F# field-missing errors light up at literal-construction sites only; semantic interpretation sites unaffected (4 record-extension events + 1 DU-variant event across chapter 3.2; all clean). | `DECISIONS 2026-05-13` — Closed-DU expansion: empirical confirmation; `CHAPTER_3_2_CLOSE.md` — record-extension generalization |
| **Two-consumer threshold for emergent primitives** — extract a helper / primitive at the second consumer, not the first. Codified for `fanOut`; deferred for `fallback` / `accumulate` / `wrap` / `lift`. | `DECISIONS 2026-05-13` — Emergent primitives earn their place through multi-consumer demand |
| **Decimal as default for continuous statistical evidence** — T1 byte-determinism requires it; `float`/`double` arithmetic varies by host. | `DECISIONS 2026-05-13` — Decimal is the default for continuous statistical evidence |
| **Discrete-rationale DUs absorb continuous evidence by adding variants at meaningful inflection points** — don't reach for `confidence: decimal` on a coarser variant; add the variant that names the band. | `DECISIONS 2026-05-13` — Discrete-rationale DUs absorb continuous evidence |
| **Pass return-type codification** — passes return `Lineage<'output>` when they produce only decisions; `Lineage<Diagnostics<'output>>` when they produce decisions plus observer-relevant findings. The shape names the production. | `DECISIONS 2026-05-13` — Pass return-type codification (session 14) |
| **Named accessors for stacked types whose nested access loses self-description** — `lineage.Value.Value` is a smell when readers must count projections to know which writer they're on. Provide module-level accessors. | `DECISIONS 2026-05-13` — Named accessors for stacked types (session 14) |
| **Contract-vs-implementation cross-reference in audits** — any audit walking contract-vs-test must also walk contract-vs-implementation. The "no test, no implementation" finding is a feature gap, not a test gap. | `DECISIONS 2026-05-13` — Audit discipline refinement (session 14) |
| **Active deferrals re-checked at chapter close** — silent-trigger fires get caught by table-scan, not by chronological re-read. The transform-registry deferral fired without cash-out for ~7 sessions; the index exists so it doesn't recur. | `DECISIONS 2026-05-13 — Transform registry cash-out + Active deferrals index` (codifying entry; session 13) — index lives at the top of `DECISIONS.md` |
| **Document the false starts** — preserve the wrong rule alongside the right one. Future agents recognize the temptation when it recurs; documentation captures the discipline's discovery, not just its outcome. | `DECISIONS 2026-05-13 — Pass return-type codification (session 14)` and `DECISIONS 2026-05-13 — Named accessors for stacked types (session 14)` — both carry preserved-false-start prose embodying the discipline |
| **Anticipation vs. speculation in abstraction extraction** — refines the two-consumer threshold with three positions (A/B/C) and an empirical test for "shape visible enough." Position B (structural alignment when the shape is concrete) earns its place; Position A (full extraction) requires both shape visibility and concrete second consumer; Position C (defer fully) is the default. | `DECISIONS 2026-05-13` — Anticipation vs. speculation in abstraction extraction (session 14) |
| **Admire entries fall on a spectrum (V1-migration / V2-growth / hybrid)** — every ADMIRE entry's template choice (what V1 gives us / what V2 adds) is governed by the entry's mode. Three modes named; chapter-2 added the `extracting (in flight, N slices)` status for multi-session chapters in flight (session 23 amendment). | `DECISIONS 2026-05-13 — Admire entries fall on a spectrum (V1-migration / V2-growth / hybrid)` (line 1862; session 23 amendment for in-flight status) |
| **Writer codification stability mark via heterogeneous-third-test protocol** — the dual-writer pattern (Lineage + Diagnostics) reached codification stability when its third real test (FK with maximum heterogeneity) held without API expansion. Four core predictions confirmed (return-type signature, named-accessor surface, opportunityEntry shape, no API expansion). Mirrors the strategy-layer codification stability mark. | `DECISIONS 2026-05-14 — Writer codification reaches its stability mark (heterogeneous third test held)` (line 3929; session 16) |
| **`opportunityEntry` extraction-defer at N=3-of-distinct-shapes** — refines the two-consumer threshold with shape-distinction analysis: surface count of consumers is not enough; if three consumers share two distinct shapes, the third is not a third consumer for extraction purposes. The three opportunityEntry functions across UniqueIndex / Nullability / ForeignKey passes share two shapes (UniqueIndex + ForeignKey are similar; Nullability is structurally different), so extraction defers despite N=3. Mirrors anticipation-vs-speculation as a refinement on the two-consumer threshold. | `DECISIONS 2026-05-14 — opportunityEntry stays inlined: N=3 of two distinct shapes, not N=3 of one` (line 4039; session 16) |
| **Chapter-close ritual** — eight load-bearing items every chapter close must execute (Active deferrals scan; contract-vs-implementation walk; CLAUDE.md / README.md staleness checks; HANDOFF + CHAPTER_N_CLOSE.md scope; fresh-eye walk; operating-disciplines table currency; **V1-input-envelope walk** for V1↔V2 translation chapters — added at session-25 chapter-2-close per the subagent #3 finding that chapters grow won't-carry-forward lists under fixture pressure rather than V1-input pressure). Recurring audits codify into rituals; ad-hoc investigations don't compound. | `DECISIONS 2026-05-14` — Chapter-close ritual (session 15; session 25 amendment for V1-envelope walk) |
| **Strategic-frame axis-naming at chapter open** — multi-session chapters (especially V1↔V2 translation chapters and architectural-arc chapters like `Projection.Pipeline`) name the chapter's load-bearing axes at chapter open, before substantive slices begin. The OSSYS chapter named eight axes at session 17; the framework-extension amendment (session 23) confirms the pattern for multi-session chapters generally. Future chapters (`Projection.Pipeline` canary; `SnapshotRowsets` implementation) inherit. | `DECISIONS 2026-05-15 — Strategic frame for the OSSYS implementation chapter` (session 17) plus the session 23 framework-extension amendment at `DECISIONS 2026-05-13` (admire spectrum) |
| **Chapter-mid-audit** — multi-session chapters dispatch a cross-document consistency audit subagent at intervals during the chapter (typically every 3–5 substantive sessions). Surfaces mid-flight propagation drift before it compounds at chapter close. Findings categorized CRITICAL / MINOR / OPEN; CRITICAL fix in next hygiene work; MINOR rolls to chapter close via CHAPTER_N_CLOSE scaffold; OPEN warrants discussion. **Active deferrals scan is a required dimension** on every dispatch (session 24 amendment): pointer drift and trigger-fire drift are different cost classes; only explicit framing catches the latter. Pairs with the chapter-close ritual. | `DECISIONS 2026-05-19` — Chapter-mid-audit as a routine practice (session 23; session 24 amendment) |
| **Trace-before-fixture** — when writing a new slice in a V1↔V2 translation chapter, trace V1's actual handling first (SQL extraction + JSON projection). Classify the finding into one of three classes (see "Three-class typology" below) before writing the failing test. The classification informs the resolution shape. Slice-level admire-mode; pairs with chapter-level admire from chapter open. | `DECISIONS 2026-05-19` — Trace-before-fixture pattern at slice level (session 23; codified at N=3) |
| **Three-class typology for V1↔V2 translation findings** — JSON-projection-lossiness (V2 can't see X; resolved by input-path expansion); V2-boundary-discipline (V2 sees X but has no axis; resolved by filter / carry-through / IR-refinement); alternative-IR-surface (V2 sees X; primary IR has no axis; parallel V2 surface is the natural home — route there, possibly making V1 input redundant). Each class has different composability and coupling characteristics. The trace-before-fixture pattern operates the classification. | `DECISIONS 2026-05-21` — Chapter 2 close: alternative-IR-surface class (session 25; completes the typology at N=2 per class) |
| **DECISIONS is for resolved questions, not session narrative** — substantive entries (disciplines, refinements, cash-outs, codifications) stay; session-narrative content (commit lists, test baselines, forward signals, rent-paying checks, recaps) lives in commit messages, PR descriptions, HANDOFF.md, CHAPTER_1_CLOSE.md, or the conversation. The substance test: would this entry still be useful in six months? Append-only protects against revisionism; prune-when-wrong protects against narrative drift. | `DECISIONS 2026-05-14` — DECISIONS is for resolved questions (session 15) |
| **Stage 0 foundation phase ships as one coherent unit before chapter 3.1 opens** — the twelve foundation items (S0.A–S0.L per `STAGING.md`) are codified in F# types (Tier 2), structural commitment (Tier 3), and primitive support (Tier 4) before any chapter-3 slice opens. Tier 1 is documentation hygiene + governance burst (S0.F AXIOMS scaffolding; S0.G five DECISIONS entries; S0.J currency checks; S0.L cross-references). The Stage 0 commitment is the structural answer to "should we just start chapter 3.1 and refactor as we go?" Per SPINE inference I6, the contract precedes its instances. | `DECISIONS 2026-05-22` — Stage 0 foundation phase ships as one coherent unit |
| **R6 split-brain governance during dual-track** — V2 emits-but-doesn't-ship while V1 owns the production write path; the canary asserts V1 ≈ V2 modulo named tolerances; disagreement blocks the PR; per-environment-per-artifact-type V2-driver transition is gated on N=10 consecutive green canary runs plus operator sign-off. The four-environment cutover stays per-pair; the gate flips when its evidence supports the flip. The Tolerance taxonomy (S0.E) is the governance surface — every divergence either matches a named tolerance or fails the canary. | `DECISIONS 2026-05-22` — R6: Split-brain governance rule for the dual-track cutover window |
| **T-30 / T-15 cutover fallback ladder gates** — V2-driver mode requires four conditions met by T-30: (a) chapter 3 closed with green canary on full 300-table Catalog; (b) chapter 4.1 (data triumvirate) shipping; (c) chapter 4.2 (User FK reflow) shipping; (d) ≥1 full UAT dry-run. T-30 yellow → V2-augmented (V1 drives, V2 verifies). T-15 unstable (canary CI flake >10%; tolerance churn) → V1-only retreat. Hard rule: V1 stays warm through cutover+30 regardless. The gates determine the ladder rung; R6 governs per-pair progression along the rung. | `DECISIONS 2026-05-22` — T-30 / T-15 cutover fallback ladder gates |
| **AXIOMS amendments scaffolded at chapter open; bodies filled at chapter close** — the "Amendments scheduled (chapter close)" section at the bottom of `AXIOMS.md` is the placeholder list for pending amendments. Chapter 3.1 close cashed A35 (Π's output is a deterministic statement stream), A36 (bulk-vs-incremental is realization-layer policy), A39 (aggregate-root smart-constructor invariants), A40 (harmonization-via-parameterization). Renumbered the prior A35/A36 candidates (Π-erased axes; CatalogDiff exhaustiveness) to A37/A38. Chapter agents fill the body when the chapter that earns the amendment closes. The scaffolding is a structural forcing function: chapter close cannot complete without resolving its placeholders. | `DECISIONS 2026-05-22` — Stage 0 foundation phase (S0.F scaffolding); chapter-3.1 close (sessions 27–36) for A35/A36/A39/A40 |
| **Bench-driven optimization protocol** — performance optimizations at hot paths require three-candidate / 2-refuted / 1-confirmed shape with bench data. Refuted swaps are documented with bench data so the same swap doesn't recur. The bench surface is how V2 earns its perf claims, the same way the canary's PhysicalSchema diff earns its fidelity claims. | `DECISIONS 2026-05-24 — Bench surface caught two wrong-direction canary optimizations` |
| **HANDOFF.md is append-only within a chapter; never overwrite** (codified 2026-05-20 operator-surface feedback) — `HANDOFF.md` accumulates per-chapter letters with newest at the top; older letters in the same chapter stay in the file so the next agent reads the chapter's full bridge-letter history. At chapter close, the entire file rotates to `HANDOFF_CHAPTER_<N>.md` (preserving the chapter's letter history as a historical archive) and a fresh `HANDOFF.md` opens with the chapter-close letter. Within a chapter, new letters **prepend** above prior letters via Read-then-Write-with-concatenation OR via Edit; **never overwrite the file with Write** — doing so destroys the chapter's letter history. The discipline holds even when the prior content is "just one letter" (the chapter-close letter is itself the next chapter's load-bearing backdrop and must be preserved through mid-chapter handoffs). The named failure mode is **history-yeet by Write overwrite**: agent reaches for `Write` to author a new letter, doesn't first Read the current file, replaces the file entirely, and the chapter's prior letters are gone from the working tree (git history preserves them but the operating surface is the file, not git log). | `sidecar/projection/HANDOFF.md` letter-archive pattern; `HANDOFF_CHAPTER_1.md` / `HANDOFF_CHAPTER_2.md` / `HANDOFF_CHAPTER_3.md` rotation precedent |
| **"Handoff message" = forward-looking letter, not backward-looking status report** (codified 2026-05-20 operator-surface feedback) — when asked for an "inline handoff message" / "next-agent handoff" / "set up the next agent for success" / similar framings, the deliverable is a **prose letter addressed to "you" (the next agent)** that orients them on what they need to know to solve the next problem. It is NOT a structured summary of what just shipped (tables of commits, bullet-grids of completed work, status matrices). The existing `HANDOFF.md` "Next-agent orientation — DO THIS FIRST" prose blocks are the model. **Shape**: opens with `To the next agent.` or `You're picking up X mid-Y.` — direct address; names where they are in the spine of the work; tells them what the next slice/decision is, including what's not yet decided; gives a reading order with time estimate; names the load-bearing disciplines they should internalize before writing code; signs off. **Reserve structured tables** for genuinely tabular content (slice queues with status; file paths with line numbers). **The agent failure mode** is "summary-doc-instead-of-letter" — agent defaults to structured-output bias from training, conflates "what shipped" (the easy thing to enumerate from recent context) with "what to do next" (the actually-useful framing), and writes a status report. Diagnostic test: count the second-person pronouns; if the letter doesn't address the next agent directly, it's a status report. Per the operator's framing: "set up the next agent for success in orienting themselves in this codebase to solve a problem." Forward-looking, problem-oriented, second-person. | Existing `HANDOFF.md` letters' "Next-agent orientation" sections; chapter B.4 close letter (2026-05-20) as worked example; slice C.1 mid-chapter letter (2026-05-20) as second worked example |
| **Test-failure capture protocol — TRX-first when `dotnet test` reports `Failed!`** (codified 2026-05-20 operator-surface feedback) — `dotnet test` console output interleaves across parallel test classes; per-test `[FAIL]` markers can scroll past the final summary, making `tail -N \| grep "fail"` unreliable for identifying which tests failed. **Default protocol**: when a sweep's final-summary line reports `Failed:` count > 0, immediately re-run with the TRX logger (`--logger "trx;LogFileName=test-results.trx" -- RunConfiguration.ResultsDirectory=/tmp/test-results`) and grep the TRX file for `outcome="Failed"`. The TRX is deterministic XML with `<UnitTestResult testName="..." outcome="Failed">` per failure + `<Message>` + `<StackTrace>` blocks under nested `<ErrorInfo>`. Extraction one-liner: `grep -oE 'testName="[^"]*"' /tmp/test-results/test-results.trx \| sort -u` for names; `grep -A 30 'outcome="Failed"' /tmp/test-results/test-results.trx` for details. **Triggers**: any time `dotnet test` reports `Failed: N` with N > 0; any time non-deterministic flake suspected (rerun with TRX confirms whether the flake reproduces). **Why the discipline earns its place**: recurring agent failure mode across sessions — wastes cycles on rerunning tests trying to read interleaved console output. The TRX file is the contract; the console is best-effort UX. Pairs with the Canary forcing-function discipline (perf-gate.sh's structured baseline is similarly the structured ground truth over the console output). | TRX logger flag invocation pattern; `DECISIONS 2026-05-20 (test-failure capture protocol)` |
| **Tiered test runner — `scripts/test.sh`, pools never run concurrently** (codified 2026-05-24) — the suite splits into a parallel PURE pool (~2540 tests, ~55s) and a serial DOCKER pool (`Docker-SqlServer` collection, `DisableParallelization`, ~4m). **Never run them as one `dotnet test`**: on the 4-core / 15 GiB / no-swap host, the parallel pure pool concurrent with the serial Docker pool + the SQL container OOM-kills the test host mid-run ("host process exited unexpectedly" + ~700 MB hang dump) — the recurring "tests time out / go unresponsive." Use `scripts/test.sh` (`fast` = pure, the default inner loop; `docker`; `canary`; `all` = fast-then-docker sequential; `list`). Two load-bearing settings: (a) pools run as **separate sequential processes** (OOM fix); (b) the pure pool runs at **`xUnit.MaxParallelThreads=-1`** because ~34 test files are sync-over-async (`task.GetAwaiter().GetResult()`) and xUnit's *bounded* `MaxConcurrencySyncContext` starves their continuations → **intermittent deadlock** (idle CPU; hangs at any bounded width incl. serial; passes at `-1`; thread-pool min-threads does NOT help). The Docker pool stays serial (no sync-context throttle, no CREATE/DROP livelock). **Durable hardening — LANDED 2026-05-24:** the pure-pool sync-over-async sites (21 files) now route their blocking wait through **`TaskSync.run`** (`tests/Projection.Tests/TaskSync.fs` — `Task.Run` offload, so continuations resume off xUnit's capped sync context). The deadlock is fixed at the source: bounded parallelism is safe (full pure pool green at the default cap, ~50s), so `test.sh` no longer needs `MaxParallelThreads=-1`. New SQL/async tests should use `TaskSync.run` (or be `Task`-returning), never bare `(task).GetAwaiter().GetResult()`. Pool separation stays (OOM is independent of the deadlock). Pairs with the test-failure capture protocol (`test.sh` emits TRX + extracts failed names). **Warm-container dev loop — LANDED 2026-06-04:** the Docker pool's per-test-class fixtures cold-start a fresh Testcontainers SQL Server per class (~30-36s × 13 fixture classes). `scripts/warm-sql.sh` owns ONE long-lived `projection-mssql-warm` container; `Deploy.acquireContainer` / `useContainer` honor `PROJECTION_MSSQL_CONN_STR`, so `EphemeralContainerFixture` (warm-honoring) reuses it across every non-CDC class — cold-start paid once per session, not per class. `scripts/test.sh {docker,canary,focus,all}` auto-detect the warm container and export the env var; `test.sh focus <FullyQualifiedName-substring>` is the single-class/method inner loop (e.g. `CanaryDeployTests` warm ≈ 3s vs. cold-start + run). **CDC isolation preserved:** the 4 CDC classes use the new `IsolatedContainerFixture` (always cold-starts a dedicated container, bypasses the warm shortcut) because `sp_cdc_enable_db` flips instance-wide `master.sys.databases.is_cdc_enabled` and would pollute / livelock the shared instance. `Deploy.Docker.ensureRunning` short-circuits `true` when the warm var is set (the warm path needs no local daemon). Full Docker pool stays 168/168 green warm. | `scripts/test.sh`; `scripts/warm-sql.sh`; `tests/Projection.Tests/EphemeralContainerFixture.fs` (`EphemeralContainerFixture` warm-honoring + `IsolatedContainerFixture`); `src/Projection.Pipeline/Deploy.fs` (`acquireContainer`); `tests/Projection.Tests/TaskSync.fs`; `tests/Projection.Tests/TestCollections.fs` (Docker-SqlServer); `DECISIONS 2026-05-24 (Test runner: tiered pools, sync-over-async deadlock, OOM)` |
| **Agent test-execution protocol — run focused tests directly; never `pgrep`-guard a run (2026-06-09)** — to run one class/method warm, invoke `scripts/test.sh focus "<FullyQualifiedName-substring>"` **directly**. Do NOT gate it behind a `pgrep` / `until` wait loop: `pgrep -f "dotnet test"` matches the guard's **own command line** (the pattern is in it) and `pgrep -x dotnet` matches **persistent MSBuild node-reuse workers** (idle `dotnet` nodes lingering ~15 min), so `until ! pgrep …` never proceeds and the run never starts. Don't pipe a long run through `\| tail -N` when watching it (it buffers to EOF — looks hung); stream to the file and `grep`/`wc -l`. A warm focused Docker run lands in **seconds** — an apparent "hang" is almost always the guard or buffering, not the test (`MSBUILDDISABLENODEREUSE=1` avoids stuck nodes; `pkill -9 dotnet` clears them). A dead `projection-mssql-warm` (revive: `scripts/warm-sql.sh restart`) is the cause of a batch of `SqlException: Could not open a connection` failures while `PROJECTION_MSSQL_CONN_STR` is set. **Observability amendment (2026-06-10):** every pool run tees its stream to `/tmp/projection-test-results/<pool>.live.log` and maintains a one-line `<pool>.status` (`RUNNING pid=…` → `PASSED`/`FAILED exit=… failed=<names>`; a signal trap writes `KILLED` so a dead run can't masquerade as in-flight). **`scripts/test.sh status`** answers "alive / stuck / done, and what failed?" in one command — verdict, pid liveness, live-log staleness (>120s flags the warm-container wedge with the restart hint), last output lines. Launch pools bare in the background; poll `status`. | `DECISIONS 2026-06-09 — Agent test-execution protocol`; `DECISIONS 2026-06-10 — The remaining-shelf sweep` (observability amendment); pairs with the **Tiered test runner** + **Test-failure capture protocol** rows. |
| **Iterator-logging is a first-class outcome over time** (codified 2026-05-09 chapter 3.6 sidebar) — every loop / iteration / lazy-stream pull emits a `Bench` sample so per-iteration distribution surfaces in the rollup table, not just per-call totals. The primitives: `Bench.scope` (RAII synchronous timing), `Bench.iterDo` / `Bench.iteriDo` / `Bench.iterMap` (per-element samples on `seq` / `list`), `Bench.streamProbe` (lazy-sequence throughput probe — records `<label>` total ms + `<label>.elements` count on enumeration completion), `Bench.streamTransit` (per-element backpressure samples), `Bench.recordSample` (external counter surfacing). All accumulators thread-safe, lock-protected. Default to `iterDo` / `iterMap` over a bare `for x in xs do`; default to `streamProbe` over a bare `seq` consumer. Stats roll up at every level (nested scopes compose); the rollup table sorts by `TotalMs` descending so expensive operations surface at the top. Operators reading the bench output should see Count + Mean + P50/P95/P99 per label. Adopting the iterator-logging primitives is structurally equivalent to TDD — the perf surface earns its place by being visible in every operator interaction. | Bench primitives at `src/Projection.Core/Bench.fs:103-233`; persist boundary at `src/Projection.Pipeline/BenchSink.fs`; canonical CLI consumer at `src/Projection.Cli/Program.fs:dumpBench` |
| **Canary as load-bearing forcing function** (codified 2026-05-23 + chapter-3.1 close + 2026-05-09 operator-reality amendment) — the canary's PhysicalSchema round-trip diff against an OutSystems-shaped source DDL is V2's primary wide integration surface. Tiers: schema-only canary (`fixtures/canary-gate.sql`, ~1.5s warm) runs on **SessionEnd hook** for the operator-confidence smoke; generator-scale canaries (`Generator bulk: 1k/10k/100k rows/table` in `GeneratorScaleTests`) exercise the bulk realization path; **operator-reality canary** (`Operator-reality canary: 6.25k rows × 150 tables, variegated` — `GenerateSpec.operatorReality`, ~5-6s warm post-2026-05-20 two-pass tuning; was ~10-12s pre-tuning at 50k rows × 300 tables; the row-count cut alone gave ~25% wall reduction, the table-count cut halved deploy + reflection cost on top) is the production-shape baseline; realistic 300-table canary gated behind `PROJECTION_RUN_REALISTIC_CANARY` env var (too slow for unit tests). The canary deploys source to one ephemeral DB, reads back via `ReadSide`, runs V2's emitter on the reconstruction, deploys to a second DB, reads back, asserts source ≈ target on `PhysicalSchema`. Empty diff = structural fidelity holds. **Per-commit + per-Stop-hook gate is operator-reality** (`scripts/perf-gate.sh` invokes `dotnet test --filter "FullyQualifiedName~Operator-reality"` with `PROJECTION_BENCH_DIR=$ROOT`); **per-session smoke is canary-gate.sql** (schema only, ~1.5s); **nightly is bulk100k + realistic 300-table** (full forcing function). Operator decision (2026-05-09): schema-only canary-gate.sql is **inappropriate** for the production-use-case perf baseline; the gate must exercise the production envelope (150 tables, variegated FK density; tuned 2026-05-20 from 300 tables × 50k rows down to 150 tables × 6.25k rows per operator framing "not getting the value of waiting for it all the time" — preserves FK-density envelope at smaller scale while halving deploy + reflection cost; see `DECISIONS 2026-05-20 (canary volume reduction)`) so feature additions can't silently regress under operator-reality conditions. Statistical perf regression gating: per-label `MeanMs + K × σ_effective` (default `K=5.0`; `σ_effective = max(StdevMs, MeanMs × 0.20)` Bayesian floor) against the tracked `bench/baseline-canary.json` μ+σ statistical baseline (computed from N≥5 warm captures via `PERF_GATE_RECORD=1`; the baseline IS the model — there is no rolling history accumulator). The σ-floor prevents brittle gates on small/noisy labels where N=5 underestimates true population σ. New labels (not in baseline) pass with a soft warning and join on the next record cycle. Re-record the baseline when the perf floor legitimately changes; pair with a DECISIONS amendment naming the new floor's rationale per `DECISIONS 2026-05-10 — Perf-gate μ+σ statistical baseline`. Stop-hook timeout in `.claude/settings.json` is 60s (post-2026-05-20-tuning canary ~6s + statistical analysis ~1s + ample buffer; the 60s ceiling stays for cold-cache + Docker-startup margin); do not drop below 30s. | `DECISIONS 2026-05-23 — Source SQL Server with OutSystems semantics`; `DECISIONS 2026-05-09 — Operator-reality canary as the production-baseline perf gate`; `CHAPTER_3_1_CLOSE.md` (canary milestone arc); `.claude/hooks/session-end.sh` (smoke); `scripts/perf-gate.sh` (operator-reality statistical gate; chapter-3.6 cash-out) |
| **Stream-realization pattern (chapter-3.1 contribution)** — Π's canonical output is a typed deterministic stream (`seq<Statement>` for SSDT). Realization layers (`Render.toText`, `Deploy.executeStream`) consume the stream and choose their emission form. The algebra (A18 / T1 / T11) holds at the stream level; bulk-vs-incremental deploy is realization-layer policy invisible to Π (A35 / A36). | `DECISIONS 2026-05-28 — Session 34 / A35 cash-out` and `Session 34 / A36 cash-out` |
| **Writer-fidelity discipline (chapter-3.1 contribution; chapter-Cluster-B CE refinement 2026-05-22)** — pass drivers MUST use `LineageDiagnostics.tellDiagnostics` / `Lineage.ofValueAndEvents` (the canonical primitives); manual record-building is forbidden. **Cluster B refinement (H-001 / H-002)**: the `lineage { ... }` / `diagnostics { ... }` / `lineageDiagnostics { ... }` computation-expression builders are the **syntactic enforcement** of this discipline — inside a CE block, manual record-building is impossible because every value flows through `bind` / `Return` / `write*`. The discipline graduates from "operating discipline pass drivers follow" to "type-level guarantee at any site that opens the CE." New CE primitives: `Lineage.write` / `writeMany`, `Diagnostics.write` / `writeMany`, `LineageDiagnostics.writeLineage` / `writeDiagnostic` / `writeDiagnostics` (each is the value-less `M<unit>` form that `do!` desugars to). The original `tellDiagnostics` / `ofValueAndEvents` shapes remain canonical for non-CE consumers; the two surfaces are equivalent under the monad laws (`LineageTests.fs` + `DiagnosticsTests.fs` H-001 / H-002 CE-equivalence tests confirm). | `DECISIONS 2026-05-30 — Session 36 / Writer-fidelity codification`; CE refinement at `src/Projection.Core/Lineage.fs` (`LineageBuilder`) + `src/Projection.Core/Diagnostics.fs` (`DiagnosticsBuilder` + `LineageDiagnosticsBuilder`); commit `4c1b994` (Cluster B) |
| **Executable AXIOMS via `AxiomTests.fs` (Cluster B contribution; H-100; 2026-05-22)** — every numbered axiom A1–A41+ and theorem T1–T11 in `AXIOMS.md` has a corresponding entry in `tests/Projection.Tests/AxiomTests.fs`. Three forms by verifiability-triangle bucket: (A) **Verified** — `[<Fact>]` calling `citationOf "file" "test-name"` to cross-reference the strongest existing axiom-named test; (B) **Convention-enforced** — no-op `[<Fact>]` whose docstring names the structural witness (smart constructor, closed DU, type signature); (C/D) **Unverified** — `[<Fact(Skip = "Axiom AN: <statement> — Bucket C/D; trigger: <what would promote it>")>]`. The Skip rationale carries the promotion path; when the trigger fires, the agent flips Skip→Fact in the same commit that ships the verifying mechanism. **Discipline**: every new axiom or amendment to `AXIOMS.md` MUST add or amend a corresponding entry in `AxiomTests.fs` in the same commit. The chapter-close ritual audits the AXIOMS.md ↔ AxiomTests.fs alignment by counting `[<Fact>]` + `[<Fact(Skip)>]` decorators and matching the bucket distribution against the verifiability-triangle audit. **Why this earns its place**: AXIOMS.md was prose with code citations; now it is a runnable test catalog. A contributor reads AXIOMS.md, runs `dotnet test --filter "FullyQualifiedName~AxiomTests"`, and sees which claims are green, which are aspirational, and which trigger their promotion. The gap between documentation and behavior becomes structurally visible. Pairs with verifiability-triangle audit cadence. | `tests/Projection.Tests/AxiomTests.fs` (54 entries; commit `4c1b994`); `AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md` for the bucket model |
| **`[<Literal>]` only on CLR-primitive constants (Cluster B IL-hygiene rule; 2026-05-22)** — `[<Literal>]` is for IL-primitive constants only: integral types (`int`, `int64`, `uint32`, etc.), `string`, `bool`, `char`, `float` / `double`, and `IntPtr` / `UIntPtr`. **`decimal` is a struct, not a CLR primitive** — F# attempts to emit `[<Literal>] decimal = 0.95m` as a `DecimalConstantAttribute` whose IL the .NET 9 CLR rejects at module-cctor JIT time, raising `InvalidProgramException` on first reference to the module. The named failure mode is **invisible-Literal-cctor-bomb**: two production files (`FkSelectivityDiagnostics.fs` H-025 + `JointDependencyDiagnostics.fs` H-026) shipped with `[<Literal>] decimal` and all their tests started failing the moment the modules loaded; the failure was hidden inside the cctor's inner exception and survived multiple `dotnet test` runs before being diagnosed. **Discipline**: for non-primitive numeric constants (`decimal`, custom value types), use `let private <name> : <Type> = <value>` — the value is still effectively a constant (private static field initialized once at cctor); the only forfeit is inlining-by-the-CLR, which doesn't matter for threshold constants. **Detection**: if a test file produces `InvalidProgramException` with stack trace pointing at `..cctor()`, search the module for `[<Literal>]` on non-primitive types — that's the bomb. **Why this earns its place**: F# tooling doesn't surface the issue at compile time (the IL is well-formed F#; the rejection happens at CLR load time on .NET 9 specifically). The discipline catches what the F# compiler doesn't. | Cluster B fix at `src/Projection.Pipeline/FkSelectivityDiagnostics.fs:42` + `src/Projection.Pipeline/JointDependencyDiagnostics.fs:43`; commit `4c1b994` |
| **Harmonization-via-parameterization (chapter-3.1 contribution)** — when two implementations of an algorithm diverge on a single semantic axis, parameterize the algorithm on that axis, produce both projections from one implementation, and let consumers choose. Worked example: `SelfLoopPolicy` in `TopologicalOrderPass` (chapter-3.1 collapsed `RawTextEmitter.emissionOrder`'s duplicate Kahn into the pass). Codified as A40. | `DECISIONS 2026-05-30 — Session 36 / Topological-sort harmonization via SelfLoopPolicy` |
| **Five-agent epistemic-tier audit at chapter close (chapter-3.1 contribution)** — multi-agent parallel audit dispatched at chapter close covering tightly orthogonal concerns (UL / Hex / VO / FP / ACL). Each agent classifies findings B&W vs SUBJ + H/M/L; convergence-map is the synthesis primary surface; Tier 1/2/3/4 backlog organizes findings by epistemic level + leverage. Audits are routed (named items in named chapters with named pre-scopes), not piled. Worked example: `AUDIT_2026_05_DDD_HEXAGONAL_FP.md`. | `DECISIONS 2026-05-30 — Session 36 / Five-agent DDD/Hexagonal/FP audit protocol` |
| **Verifiability-triangle audit cadence (2026-05-12 contribution)** — V2's structural-commitment posture is audited along three connected levels: L1 (structural commitments — smart constructors, closed DUs, VOs), L2 (formal axioms in `AXIOMS.md` — A1–A43 + T1–T16; see AXIOMS.md for the current set), L3 (product axioms in `PRODUCT_AXIOMS.md` — operator-meaningful claims). Each L3 axiom must trace down to L2 and L1; each L2 axiom must trace up to a product behavior; each L1 commitment must trace up to an axiom. Audit dispatch protocol: three parallel agents (top-down L3 articulation + L2↔L3 bridge + adversarial gap-hunt as operator) produce a coverage map classifying every axiom into Bucket A (full L1+L2+L3+test), Bucket B (convention-enforced L1), Bucket C (weakness — untested/aspirational/deferred), or Bucket D (unnamed L3 axiom with no L2 backing). Cadence: (a) annual re-audit refresh; (b) chapter-close L3 step — every chapter close adds a one-paragraph audit check naming the L3 axioms its work touched and any new Bucket-D gaps introduced; (c) per-PR L3 review for PRs touching boundary code or adding config/CLI surface. Bucket-D promotions land in `AXIOMS.md` or `PRODUCT_AXIOMS.md` once campaigns operationalize them. Worked example: `AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md`. | `DECISIONS 2026-05-12 — Verifiability-triangle audit methodology` |
| **Pillar 9 — Harvest-dichotomy classification (data-intention vs operator-intention) (2026-05-15 late contribution; codified at supreme-operating-discipline level)** — every transformation site reads under one of two classifications: **`DataIntent`** (preserves data intention; reachable from `Project(catalog, Policy.empty, profile)` without operator opinion; lands in the skeleton — Profile-driven *observations* are DataIntent evidence) or **`OperatorIntent of OverlayAxis`** (expresses operator-supplied intent through `Selection \| Emission \| Insertion \| Tightening`; lands as registered overlay; emits `LineageEvent` carrying the classification). The dichotomy is operative AT HARVEST TIME — every transformation an agent considers (reading v1 for what to bring forward; designing a new pass; auditing an existing seam) gets classified before it lands in v2 thinking. **Policy IS operator intent reified**: OverlayAxis = Policy DU axes exactly (with reserved expansion if a fifth axis is warranted). Harvest workflow (4 steps): identify what changes → determine whose intent is expressed → register or document the harvest decision (transformation in v2 ships as `RegisteredTransform`; transformation NOT in v2 ships as triple deliverable Skip stub + Tolerance entry + `Status = NotImplementedInV2 of rationale` registry entry) → confirm intent against the pillar. **Named failure mode: skeleton-overlay drift** (three sub-modes: misclassification as DataIntent; dead overlay; silent inclusion at harvest) — caught bidirectionally by the skeleton-purity property + overlay-exercise property + harvest-classification coverage tests. Sibling to pillar 8 (catches naming drift), pillar 7 amendment (catches string-composition drift), text-builder-as-first-instinct (catches typed-AST-bypass drift). The four meta-disciplines form the discipline tier; each is applied at consideration time; each is enforced structurally; each protects a class of failure that scales with codebase growth. The `TransformRegistry` is the **fourth cross-cutting structural-evidence concern**, sibling to Lineage / Diagnostics / Bench — each plugs into every stage that has its kind of activity; each is enforced structurally; each has its own writer/observer primitive. Strongly-typed canonical surface: `RegisteredTransform<'In, 'Out>` carries metadata AND the transformation-function definition itself (single definition site; no parallel enumeration — true-by-construction across all four execution contexts per `DECISIONS 2026-06-04 — Registry drives the run` (E0 pass chain via `RegisteredTransforms.chainSteps`; E1 emit phase via `Compose.emitSteps`; E2 read adapter via `Compose.readStep`; E3 transfer + E4 conditional emitters set-level isomorphic) — the `registered ⇔ executed` invariant is enforced by `RegisteredAllTransformsBidirectionalTests`; surfaced + fixed two real mismatches (`SuggestConfigEmitter` executed-but-unregistered; `StaticPopulationEmitter` registered-but-unexecuted, now wired into the production canary)); 5-stage `StageBinding` (`Adapter \| Pass \| OrderingPolicy \| Emitter \| Pipeline`); `Sites : TransformSite list` for intra-pass classification fidelity. | `DECISIONS 2026-05-15 (late) — Pillar 9: harvest-dichotomy classification (DataIntent vs OperatorIntent); registry as cross-cutting concern; canonical strongly-typed registry shape` (refines the same-day re-opening entry with the full pillar-9 framing); `PRODUCT_AXIOMS.md` L3-CC-Transform-Totality (bidirectional axiom statement); `AXIOMS.md` A41 candidate (formal type-system shape); `V2_PRODUCTION_CUTOVER.md` §6.4.7 (A.4.7 workstream; full-sweep retroactive refactor; ∼3 weeks); `V2_DRIVER.md` per-axis stakes (data-intent / operator-intent separation — verification depth Highest, co-equal with CDC silence). |
| **Sibling-wrapper discipline (chapter 4.7 cleanup amendment to V2-no-back-compat)** — when a function has a sibling with a `<Name>With<Axis>` / `<Name><Axis>` suffix (or any parallel-API shape), apply the distinguishing test before assuming tech debt: does the wrapper **hide information the caller might want** (tech debt; collapse to the canonical information-bearing surface — callers explicitly drop via `.Value` / `ignore` at the call site), or does it **supply a private/computed default the caller couldn't otherwise access** (principled F# default-argument idiom — F# lacks `let`-bound parameter defaults; the wrapper IS the idiom)? The named failure mode is **overdifferentiated middle-tier**: when a callable has N defaultable axes, providing 2^N sibling wrappers covering subsets is the anti-pattern; the principled count is 2 (zero-default + full-explicit) plus any genuinely-orthogonal-consumption surfaces. Worked counterfactuals: chapter 4.7 slice β `buildCreateIndex` silent-skip (collapsed); `emitWithUserRemap` + `composeWithMigration` middle-tiers (retired). Preserved-as-principled: `Compose.write` (private defaultFileWriter); `ManifestEmitter.build` (RegisteredTransforms.all default); `*.emit` variants in Static/Bootstrap/MigrationDependencies (each runs TopologicalOrderPass internally). | `DECISIONS 2026-05-17 (chapter 4.7 cleanup) — Sibling-wrapper discipline` |
| **EvidenceCache discovery-then-derive pattern (chapter-B.3 contribution; 2026-05-19)** — when a sequence of slices accumulates per-axis SQL probes against a shared substrate (rows of one source table), the architectural answer is "discover the substrate ONCE into a typed in-memory cache; derive every axis from the cache in pure F#." Concrete shape: a `Cache` record (column-oriented; typed `CachedValue` closed DU) populated by a discovery primitive that issues a bounded number of SQL queries per source unit (chapter B.3: 3 per kind — aggregate + row-stream + nullability reflection); axis-specific `Cache.deriveX` functions are PURE F# (`Array.sort`, `Array.groupBy`, `Set.difference`, `Dictionary` tally). Cross-derivation shared state — index maps, target-set memoization — lives in the cache as precomputed indices OR via a `*With`-overload pattern (`deriveXWith precomputed` + simple wrapper `deriveX`). Net round-trip count drops linearly in the number of derivations: each new axis lands as a pure-F# function, not a new SQL query. Worked example: chapter B.3 `Projection.Adapters.Sql/EvidenceCache.fs` + `LiveProfiler.Cache` submodule (12 derivations covering 9 Profile axes from one cache substrate; ~6000 → ~900 SQL round-trips at 300-table production scale). When you reach for another per-attribute SQL probe over the same source data, ask whether the cache substrate already exists OR is the right architectural answer. | `DECISIONS 2026-05-19 (slice B.3.6.evidence-cache)` — architectural-pivot rationale + cache substrate design; `DECISIONS 2026-05-19 (slice B.3.6b.cache-fold-residuals)` — full fold + Big-O audit codification; `CHAPTER_B_3_CLOSE.md` §"Substantive contributions §2" — the round-trip count delta |
| **Big-O audit at multiple-derivation sites (chapter-B.3 contribution; 2026-05-19)** — when N derivations consume the same cached substrate, plan cross-derivation shared-state explicitly at design time. The recurring failure shape is "each derivation rebuilds an index / map / Set independently" — inadvertent N-pass-reconstruction of work the cache could compute once. Three patterns earn their place: (1) precompute per-substrate-unit indices at construction time (e.g., `CachedKind.ColumnsByKey : Map<SsKey, CachedColumn>` built once in the discovery primitive; eliminates O(C²) per-derivation column-scan); (2) precompute cross-substrate-unit indices at attach-time (e.g., `buildForeignKeyTargetIndex` for FK-orphan derivations sharing target-PK sets; eliminates N-to-1 + 2× duplicate work); (3) `*With`-overload pattern shares precomputed state between derivations that consume it (`derivXWith index` + simple wrapper `derivX`). Per sibling-wrapper discipline: the wrapper supplies a default the caller couldn't otherwise compute — principled F# default-argument idiom. Pairs with bench-driven optimization protocol: Big-O audit = structural optimization at design time; bench-driven optimization = profiling at hot paths. The audit is part of the slice when adding the second derivation over a shared cache (don't defer to a follow-up; the discipline absorbs the refinement while the slice is hot). | `DECISIONS 2026-05-19 (slice B.3.6b.cache-fold-residuals)` — three inline optimizations (`ColumnsByKey` precomputed; target-PK-set memoization via `*With` overloads; single-pass `Dictionary` categorical tally); `CHAPTER_B_3_CLOSE.md` §"Disciplines codified or reinforced" |
| **F#/C# language-role partition + V1 as editorial donor (chapter 0.5 audible; supersedes the 2026-05-16 Bridge wave codification)** — V2 is **self-contained**. The pure algebraic core is F#; F# adapters wrap external libraries (`Microsoft.Data.SqlClient`, `Microsoft.SqlServer.TransactSql.ScriptDom`, etc.) at the boundary; a small, focused, **museum-polish** C# layer exists only where the gold-standard library is irreducibly C#-idiomatic (SMO, DacFx if pursued) and lives in dedicated adapter projects (e.g., `Projection.Adapters.OssysSql` as a C# project for SQL extraction if it's awkward to rewrite). V2 has **zero runtime dependency on V1's trunk** — no `ProjectReference`, no compiled-V1-assembly on V2's classpath, no Bridge wall. V1's role in V2 is **editorial donor**: V2 reads V1's source for inspiration, decides what to keep, **carbon-copies** the source files into V2's domain-structured locations, and refactors freely once they land. Refactor at copy-time or in follow-up commits — pragmatic, not doctrinaire. Naming: V2 vocabulary applies (eventually) on every file; large refactors land already-V2-shaped; minor edits can land with V1 names and rename in a follow-up. The audit trail for a carbon-copy is a **file-header citation comment** naming the V1 source and the date — one-time, never maintained — plus a row in `ADMIRE.md` under the entry for the V1 component. The cherry-pick discipline holds by **self-containment**: V2 has no V1 references; every commit is cherry-pickable into a V1-only trunk by construction. | `DECISIONS 2026-05-16 (later) — V2 self-containment + carbon-copy editorial inheritance; Bridge wave retired`; `BACKLOG.md` operational ledger (V1 inheritance log section); `ADMIRE.md` (the canonical V1-reference register). |
| **Operator-facing voice register (the twelve rules / evidential literalism) (2026-06-06)** — every operator-facing string (verdict, move, gate, proof, error, config, live stage) obeys one register: **authoritative, scientific, mature, humble**, governed by **evidential literalism** and operationalized as twelve rules — no pronouns; direction by imperative; legible statement with the formal substantiation one level beneath; verdicts asserted (the engine states what it proves); the true verb (`Drops`/`Deletes`/`Narrows` — no euphemism, no drama); gentle and direct, never colloquial; neutral reference to the estate (never "your"); every claim grounded in its evidence (no "X, not Y" antithesis); ordered by real structure (perfect aspect + explicit consequence); the exact referent; concrete definite subjects; **stative and agentless** (report states/events, never actions performed — "Model read complete", not "Read the model"; gerunds-in-progress like "Reading the model" excepted). The copy is **structural, not bolted on**: a site emits a coded event (Code + typed payload, no prose) and **declares its copy as a separable, harvested value** (the `TransformRegistry` pattern — declare-at-site, harvest-centrally), held honest by a `code ⇔ copy` totality test; inline prose welded to control flow is forbidden. Read the three anchor docs before writing or reviewing any operator-facing copy / CLI surface: `THE_VOICE.md` (the register + lexicon + banned list), `THE_STORYBOARD.md` (the surface scene-by-scene — nine acts × six streams × positive/negative/edge, the concern-movement field, the live-vs-latent build-readiness map, the P-6 worked proof), `THE_VOICE_INTEGRATION.md` (the build plan — slices 0–5, locked decisions). | `DECISIONS 2026-06-06 — operator-facing voice: twelve-rule register + Event/Aggregate/Voice separation` (amends the 2026-06 Apple-clear-diction entry); `THE_VOICE.md`; `THE_STORYBOARD.md`; `THE_VOICE_INTEGRATION.md`; `HANDOFF_VOICE_2026_06_06.md` |

## Load-bearing commitments — do not break without writing the amendment first

These are not negotiable without an explicit DECISIONS entry that
names the prior commitment and supersedes it. If you find yourself
wanting to break one, write the amendment first.

- **F#-pure-core / no-I/O-in-Core.** `Projection.Core` has zero I/O.
  Audited clean (`CHAPTER_1_CLOSE.md §1.1`).
- **A18 amended.** Π consumes whichever subset of `Catalog × Profile`
  it needs, but never `Policy`. Catalog and Profile are *evidence*;
  Policy is *intent*. If you reach for Policy from inside an emitter,
  you are in the wrong layer — the work belongs in a pass.
- **Data-intent / operator-intent separation (pillar 9 +
  L3-CC-Transform-Totality + A41 candidate).** Every transformation
  site in V2 carries an explicit classification — `DataIntent`
  (preserves data intention; reachable from
  `Project(catalog, Policy.empty, profile)` without operator opinion;
  lands in the skeleton) or `OperatorIntent of OverlayAxis`
  (operator-supplied intent through Selection / Emission / Insertion /
  Tightening; lands as registered overlay; emits classified
  `LineageEvent`). **Policy IS operator intent, reified:** OverlayAxis
  = Policy DU axes exactly (with reserved expansion). A18 amended is
  the Π-side commitment forbidding `Policy` in emitters (structural
  type); A41 (registry totality + bidirectional property tests) is
  the Pass-side commitment enumerating every `OperatorIntent` site
  (structural type + property test). Together they carry the
  dichotomy as a **type-witnessed bidirectional contract**, not a
  one-sided discipline. Strongly-typed canonical registry:
  `RegisteredTransform<'In, 'Out>` carries metadata AND the
  transformation-function definition itself (single definition site).
  **No parallel enumeration — true-by-construction for the pass chain**
  (`DECISIONS 2026-06-04 — Registry drives the run`): one
  `RegisteredTransforms.chainSteps : ChainStep list` is the source from
  which both `all` (metadata) and `allChainSteps` / `allChainStepsFor`
  (execution) project, so metadata↔execution drift is structural, not
  disciplined-against. The emit / read / transfer / dacpac phases (the
  E1–E4 `registered ⇔ executed` isomorphism) **landed 2026-06-04**: E1
  emit via `Compose.emitSteps`, E2 read via `Compose.readStep`, E3
  transfer + E4 conditional emitters set-level isomorphic, all enforced by
  `RegisteredAllTransformsBidirectionalTests`. 5-stage `StageBinding`
  (Adapter / Pass / OrderingPolicy / Emitter / Pipeline);
  `Sites : TransformSite list` for intra-pass classification fidelity.
  The registry is the **fourth cross-cutting structural-evidence
  concern**, sibling to Lineage / Diagnostics / Bench. Baseline
  reachable from CLI via `osm emit --skeleton-only`; manifest names
  every applied overlay per artifact via
  `applied-transforms : (SsKey × OverlayAxis option) list`.
  Bidirectional property tests: skeleton-purity
  (`Compose.runWithSkeleton` emits zero `OperatorIntent` events) +
  overlay-exercise (every registered `OperatorIntent` fires in canary)
  + totality coverage + harvest-classification cross-reference. Tier
  1 (cutover blocker); co-equal load-bearing with CDC silence per
  `V2_DRIVER.md` per-axis stakes table. Lands structurally at A.4.7
  (`V2_PRODUCTION_CUTOVER.md` §6.4.7; ~3 weeks full-sweep retroactive
  refactor); the A.4.7-prelude small slice adds
  `LineageEvent.Classification` field during/after A.0'; pillar 9 +
  L3 axiom + A41 candidate land NOW (this commit) so the discipline
  is operative for in-flight A.0' slice β (IsActive disposition
  classification is the first worked example).
- **Strategy-layer codification (`DECISIONS 2026-05-11`).** Pure
  functions of IR fields; typed function-type seam
  (`StrategyEvaluator<'context, 'config, 'decision>`); structured
  rationale DUs covering the decision space exhaustively; lineage
  events on actual decisions; module name advertises domain
  (`<Domain>Rules` suffix); total decisions with named skips.
- **`Composition.fanOut` for registered-intervention pass drivers.**
  All registered-intervention pass drivers delegate to it.
- **Decimal as default for continuous statistical evidence.**
- **Sibling-Π commutativity (T11).** Every Π's output should mention
  every catalog kind by SsKey root. **Note:** T11 is currently
  *aspirational* not structural — three Π's return `string`. Chapter
  3.5's Π port realization makes T11 structural via the typed
  `ArtifactByKind<'element>` surface.
- **A35 (chapter-3.1).** Π's canonical output is a typed
  deterministic *statement stream* (`seq<Statement>` for SSDT).
  Realization layers consume the stream and choose their emission
  form; the algebra holds at the stream level.
- **A36 (chapter-3.1).** Bulk-vs-incremental is realization-layer
  policy. How a realization deploys (`SqlBulkCopy`, per-row INSERT,
  file write, network protocol) is invisible to Π.
- **A39 (chapter-3.1).** Aggregate-root smart-constructor
  invariants. `Catalog.create` / `Module.create` / `ColumnProfile.create`
  enforce referential-integrity / empirical-probe invariants in one
  pass; consumers that flow through `create` trust the value.
- **A40 (chapter-3.1).** Harmonization-via-parameterization.
  Single-axis-divergent implementations earn one parameterized
  algorithm; same algorithm, multiple projections.
- **Writer-fidelity (chapter-3.1; chapter-Cluster-B CE refinement).**
  `LineageDiagnostics.tellDiagnostics` and `Lineage.ofValueAndEvents`
  are the canonical pass-driver primitives. Manual record-building is
  forbidden. **CE refinement (H-001 / H-002; 2026-05-22):** the
  `lineage { ... }` / `diagnostics { ... }` / `lineageDiagnostics { ... }`
  computation-expression builders are the syntactic enforcement —
  inside a CE block, manual record-building is impossible because
  every value flows through `bind` / `Return` / `write*`. The new
  value-less primitives (`Lineage.write`, `Diagnostics.write`,
  `LineageDiagnostics.writeLineage` / `writeDiagnostic`) desugar from
  `do!`. The discipline becomes a type-level guarantee at adoption
  sites.
- **Kleisli arrow `Pass<'a, 'b>` (chapter-Cluster-B; H-003).** Each
  pass is a Kleisli arrow `Pass<'a, 'b> = 'a -> Lineage<Diagnostics<'b>>`;
  composition is `Pass.compose` / `Pass.composeAll` / the `>=>`
  operator; the identity arrow is `Pass.id`. The pipeline IS a
  Kleisli category over the dual-writer monad. The fold inside
  `PassChainAdapter.compose` IS `Pass.composeAll` modulo per-step
  `Bench.scope` decoration — the registered transform chain is the
  Kleisli closure of the registered arrows. Naming this structurally
  frames H-007 (SchemaDelta as a second category) and the deferred
  H-006 (parallel composition) / H-009 (multi-target fanout) / H-063
  (free-monad scheduling) — each *future* extension adds operators
  rather than re-discovering the structure. (The speculative `product`
  / `&&&` monoidal-product operators that pre-anticipated H-006 were
  retired 2026-06-04 as unused; rebuild under a consumer.) Tests:
  `DiagnosticsTests.fs` Kleisli-law triple (left/right identity,
  associativity; empty-list = identity).
- **A24 amended — chronological-bind extends to the WriterT-stacked
  dual writer (chapter-Cluster-B; 2026-05-22).** The chronological-bind
  law (A24 original — "when `f >>= g`, the trail is `f.Trail ++
  g.Trail`") generalizes to every writer monad over a list-monoid:
  `Lineage`, `Diagnostics`, and the dual writer `LineageDiagnostics`.
  The dual writer is **`WriterT`-stacked** — `WriterT[LineageEvent]
  (WriterT[DiagnosticEntry] Identity)` in monad-transformer notation —
  and is itself a writer monad over the product monoid `(LineageEvent
  list × DiagnosticEntry list, ⊕, ([], []))`. The monad-law triple
  (left identity, right identity, associativity) holds layer-wise; the
  Kleisli laws over `Pass<'a, 'b>` are *inherited* from the stacked
  monad's laws. **When a third writer is introduced** (perf-trace
  channel; constraint-set channel for `Tolerance`; etc.), stacking it
  atop `LineageDiagnostics` inherits A24 by the same construction —
  the chapter-close ritual adds the new writer's monad-law triple in
  the same commit. See `AXIOMS.md` "A24 amended (2026-05-22)" for the
  full statement; tested in `DiagnosticsTests.fs` (Diagnostics +
  LineageDiagnostics monad-law triples + Kleisli laws).
- **Writer monad — `Lineage` (chapter-Cluster-B; 2026-05-22; trinity
  retired 2026-06-04).** The **linear** writer monad `Lineage<'a>` —
  append-only trail per A24-amended — is the in-flight carrier every pass
  uses, stacked with `Diagnostics` as `Lineage<Diagnostics<'a>>`.
  **The speculative "trinity" siblings were retired** (`DECISIONS
  2026-06-04 — Retire the speculative writer-trinity / optics-duo /
  Kleisli-product algebra`): the **branching** `LineageTree<'a>` (H-005;
  its policy-diff consumer shipped on flat-runs-plus-keyed-join and
  declined the free monad) and the **terminal** `Certificate<'a>` (H-004;
  a nominal alias of `Lineage<Diagnostics<'a>>`). Both were zero-consumer
  symmetry-builds; rebuild on demand. New writer-carrier roles stack atop
  `Lineage` / `Diagnostics`, under an explicit consumer — not ahead of one.
- **V2 owns no production write path during dual-track (R6).** Per
  `DECISIONS 2026-05-22 — R6`, V2 emits-but-doesn't-ship while V1
  owns the production write path. The canary asserts V1 ≈ V2 modulo
  named tolerances; disagreement blocks the PR. This eliminates
  split-brain by construction. Per-environment-per-artifact-type
  V2-driver transition is gated on N=10 consecutive green canary
  runs plus operator sign-off; the four-environment cutover stays
  per-pair, never global.
- **V1 stays warm through cutover+30.** Per `DECISIONS 2026-05-22 —
  T-30 / T-15 cutover fallback ladder gates`, V1's emission path
  is preserved as a fallback for thirty days post-cutover regardless
  of which mode the cutover entered. V1 sunset deferred to chapter
  5+ when all four environments have run V2 emissions for one full
  schema-evolution cycle.
- **Stage 0 ships before chapter 3.1 opens.** Per `DECISIONS
  2026-05-22 — Stage 0 foundation phase ships as one coherent unit`,
  the twelve foundation items per `STAGING.md` ship as one unit
  before any chapter-3 slice. Tier 1 (S0.F / S0.G / S0.J / S0.L)
  is the documentation-only governance burst; Tier 2 (S0.A) is the
  type-primitives keystone; Tier 3 (S0.B) is the structural-
  commitment refactor; Tier 4 (S0.C–S0.K) is primitive support
  modules in parallel. The chapter-1 baseline (631 passing tests)
  holds at every Stage 0 step.
- **V2 is self-contained; V1 is editorial donor only.** Per
  `DECISIONS 2026-05-16 (later) — V2 self-containment + carbon-copy
  editorial inheritance`, V2 has zero runtime dependency on V1's
  trunk. No `ProjectReference`, no V1 assembly on V2's classpath, no
  Bridge wall. When V2 wants a V1 capability, V2 carbon-copies the
  V1 source files into V2's domain-structured locations (existing
  F# adapter / new C# adapter project for C#-idiomatic libraries),
  cites the V1 source in a file-header comment + an `ADMIRE.md`
  row, and refactors freely from there. The pure F# core stays
  pure; the C# layer is small, focused, and museum-polish — admitted
  only where the underlying gold-standard library is irreducibly
  C#-idiomatic (SMO, DacFx if pursued). Cherry-pick safety holds
  by construction.

## Programming style — the center target

The codebase has a coherent style. These are the gravitational
patterns; new code lands inside them by default. Each guideline
points at the canonical rationale rather than restating it. Where
the canonical surface is the code itself, the pattern is named.

### Posture

- **The type system is the contract.** Smart constructors return
  `Result<'a>` for every value type that carries an invariant; closed
  DUs make exhaustiveness compiler-checked; identity (`SsKey`,
  `Name`) is a distinct type the compiler refuses to confuse with a
  string. The first place to encode a constraint is the type system,
  not a runtime check. (`AXIOMS.md` operational principle —
  structural-commitment-via-construction-validation.)
- **Determinism is constructed, not validated.** Sort by `SsKey`
  before scanning. Use `decimal` for continuous statistical evidence
  (never `float`/`double`). No `DateTime.Now`, `Random`, or I/O in
  Core — the boundary supplies clock values; passes consume them.
  T1 byte-determinism holds because every choice supports it.
- **Defaults are minimal.** No comments unless the WHY is
  non-obvious. No abstractions unless a second consumer forces
  extraction. No fields, variants, or helpers ahead of evidence. IR
  grows under demand, not speculation. Premature anything is the
  failure mode.
- **Make divergences visible.** When V2 deliberately differs from
  V1, the difference surfaces as a `Skip` test stub at the test-file
  level, not as ADMIRE prose. When a strategy makes "no decision,"
  the named keep-reason variant says so structurally; silence is
  forbidden. Total decisions, named skips.
- **Audit during the work.** When something second-order surfaces,
  act on it before shipping. The codification absorbs refinements
  during validation, not afterward. Five paydowns across sessions
  4–11; three more during session 14. (`DECISIONS 2026-05-09` —
  Audits surface things not on the agenda.)

### Types

- **Records for products; closed DUs for sums.** F# records carry
  PascalCase fields; closed DUs widen only when evidence forces a
  new variant.
- **Smart constructors return `Result<'a>`** (for value types
  carrying invariants beyond what the type system expresses
  directly) — or **return the bare value with defaulted fields**
  (for IR aggregate records whose invariant is "every field has a
  sensible no-evidence default; overrides flow via record-update").
  Downstream consumers pattern-match without re-validating; the
  invariant rides on every value. Worked examples of the
  `Result<'a>` form: `CategoricalDistribution.create`,
  `NumericDistribution.create`, `SsKey.original`, `Name.create`,
  `Module.create`, `Catalog.create`, `ColumnCheck.create`,
  `Trigger.create`, `Sequence.create`. Worked examples of the
  bare-value form (slice 5.13.smart-constructor-lift,
  2026-05-18): `Attribute.create`, `Reference.create`,
  `Index.create`, `Kind.create` — these absorb chapter-A.0' field
  extensions at one site instead of N. Test fixtures in
  `tests/Projection.Tests/IRBuilders.fs` are one-line shims that
  delegate to the production smart constructors; the default
  geometry is shared.
- **`[<RequireQualifiedAccess>]` when case names may collide.**
  Outcome and KeepReason DUs across strategies share generic case
  names (`PolicyDisabled`, `EvidenceMissing`); F# resolves
  ambiguity by picking one, which produces silent miscompilation.
  Add the attribute when names are likely to recur. Worked
  examples: `NullabilityOutcome`, `UniqueIndexOutcome`,
  `ForeignKeyOutcome`.
- **`option` for absence; never null.** `Nullable=enable` plus
  `TreatWarningsAsErrors=true` is the project setting; null escapes
  fail compilation.
- **Identity is a type, not a string.** `SsKey` is a single-case DU
  (`Original of string | Derived of original × reason`); core code
  never holds a string in a place where identity belongs. Names
  (`Name`) are presentation-only.
- **Schema coordinates are typed VOs (2026-06-02 lift slices 5a + 5b).**
  `TableId.Schema : SchemaName`, `TableId.Table : TableName`,
  `ColumnRealization.ColumnName : ColumnName` — the logical-IR
  coordinate triad. Construction flows through `TableId.create` /
  `ColumnRealization.create` (Result-returning, validating non-blank
  + ≤128-char SQL identifier limit). Boundary code unwraps via
  `TableId.schemaText` / `tableText` / `qualifiedParts` and
  `ColumnRealization.columnNameText`. **Compiler gap to remember**:
  `String.Concat` / `String.Join` / `SqlParameter.AddWithValue`
  accept `object` so VO-leak bugs DON'T surface at compile time.
  After every typed-VO field lift, grep
  `String.Concat\|String.Join\|AddWithValue` for VO-bearing
  arguments and unwrap each. Deliberate asymmetry:
  `PhysicalSchema`'s `PhysicalColumn` / `LogicalNameBinding` /
  `PhysicalForeignKey` and `Sequence` stay string-typed by design —
  they're a separate IR domain (physical-comparison surface) where
  string-as-comparison-key is defensible. See `Coordinates.fs`
  top-of-file comment for the full cleavage rationale.
- **Generic algebraic names in the core; domain-prescriptive names
  at the boundary.** `Kind`, `Module`, `Catalog`, `Reference` —
  not `Entity`, `Application`, `Model`, `FK`. The trunk's
  domain-prescriptive vocabulary lives in adapter translation.
- **`[<Literal>]` only on CLR-primitive constants.** Use `[<Literal>]`
  on `int` / `int64` / `string` / `bool` / `char` / `float` only. For
  `decimal` and other non-primitive struct types, write
  `let private <name> : <Type> = <value>` instead — F# / .NET 9 emit a
  `DecimalConstantAttribute` for `[<Literal>] decimal` whose IL the CLR
  rejects at module cctor JIT time, raising `InvalidProgramException`
  on first reference to the module. Failure mode is **invisible-Literal-
  cctor-bomb**: the IL is well-formed F#, the lint passes, but every
  test in the affected module fails the moment it loads. Diagnostic:
  if a test produces `InvalidProgramException` with a stack trace
  pointing at `..cctor()`, grep the module for `[<Literal>]` on
  non-primitive types. (Worked fix: chapter-Cluster-B retired two
  `[<Literal>] decimal` sites in `FkSelectivityDiagnostics.fs` +
  `JointDependencyDiagnostics.fs`; 10 latent test failures cleared.)
- **Kleisli arrow `Pass<'a, 'b>`** (chapter-Cluster-B; H-003) — every
  pass is a Kleisli arrow `'a -> Lineage<Diagnostics<'b>>` and the
  named type alias is in `Diagnostics.fs`. Use `Pass.id` for the
  identity arrow, `Pass.compose` / `>=>` for composition, and
  `Pass.composeAll` for folding a list of endo-arrows. The fold in
  `PassChainAdapter.compose` IS `Pass.composeAll` modulo per-step
  Bench scoping. New code that composes passes should use the named
  primitives rather than re-implementing the fold; the algebra reads
  like the Kleisli structure.

### Functions

- **Pure functions, top to bottom.** Pipe operator `|>` is the
  default; reads as "do this, then this, then this." Mutable state
  only function-local for performance-sensitive algorithms (Tarjan
  SCC, ResizeArray accumulators) — never module-level.
- **Explicit type annotations on public surfaces.** Inferred types
  on private helpers. The canonical pass shape is
  `Catalog -> Policy -> Profile -> Lineage<'output>` (or
  `Lineage<Diagnostics<'output>>` when the pass produces both
  decisions and observer-relevant findings; see pass return-type
  codification).
- **Composition over open-coding.** Use the existing primitives
  (`Composition.fanOut`, `Lineage.bind`, `Diagnostics.tellMany`,
  `LineageDiagnostics.bind`). Don't reinvent. Don't extract a new
  primitive until a second consumer needs it.
- **Result composition for boundary code.** Adapters return
  `Result<'a>`; consumers compose with `Result.bind`. Exceptions
  only for true invariant violations the type system couldn't
  prevent.
- **Named accessors for stacked types whose nested access loses
  self-description.** `lineage.Value.Value` is a smell when readers
  must count projections; `LineageDiagnostics.payload`,
  `LineageDiagnostics.entries`, and domain shortcuts like
  `UniqueIndexPass.decisionsOf` are the discipline.
- **CE form for pass-driver writer tails (2026-06-02 CE-adoption sweep).**
  When a pass-driver `run` function builds a `Lineage<Diagnostics<'a>>`
  return value, prefer the `lineageDiagnostics { ... }` CE form over
  hand-rolled record construction. The CE form makes manual
  `{ Value = result; Entries = entries }` construction syntactically
  impossible — every value flows through `do! writeLineages events` /
  `do! writeDiagnostics entries` / `return value`, so writer-fidelity
  is a type-level guarantee rather than a convention. Worked precedent
  (CE-adoption sweep): `CentralityPass.run`, `BoundedContextPass.run`,
  `SchemaComplexityPass.run`, `QueryHintPass.run`, `ProfileAnomalyPass.run`,
  `TopologicalOrderPass` ×2, plus the canonical Pattern C
  `NullabilityPass` / `UniqueIndexPass` / `ForeignKeyPass`. Seven of those
  ten sites previously hand-built the `Diagnostics<'a>` record literal —
  the CE migration fixed those discipline violations as a byproduct of
  the form-change. The function-form chain
  (`ofLineage |> tellDiagnostics entries`) is still admissible at sites
  using only canonical primitives, but the CE form is the default for
  new pass drivers and any site touching writer construction.
- **Lensed updates for nested IR substructures (2026-06-02 lens-adoption
  sweep).** When a function updates a substructure of a `Catalog`,
  `Module`, `Kind`, or `Attribute`, prefer the lens form (`Lens.over` /
  `Lens.set` with the canonical lenses in `module CatalogLenses` at
  `src/Projection.Core/Optics.fs`) over `{ x with Foo = ... }`
  record-spread. The lens form (a) names the access path so readers
  see `kindsOf`, `referencesOf`, `columnOf` explicitly; (b) makes
  `grep "Lens.over CatalogLenses.kindsOf"` a structural query that
  surfaces every site updating that axis; (c) composes — deeper
  navigation is `Lens.compose outer inner` rather than nested
  `{ ... with ... = { ... with ... = ... } }`. The exception is
  primitives that themselves define the traversal (`Catalog.mapKinds`),
  which live in `Catalog.fs` BEFORE `Optics.fs` in the compile order
  and therefore can't reference the lenses. New nested-update sites
  in pre-Optics files (rare) keep record-spread with a one-line
  comment naming the compile-order constraint; everywhere else, the
  lens form is the default. **Worked precedent (slice that codified):**
  `SymmetricClosure.attachInverses`, `LogicalColumnEmission.substituteAttribute`,
  `CatalogDiff.applyFacet.Nullability`, `Policy.filterCatalog`,
  `CatalogTraversal.mapKinds`, `ModuleFilter.filterModules` (×2),
  `CatalogDiff.addKind`, `NamingMorphism.run`.

### Documentation in code

- **Default to no comments.** Well-named identifiers state WHAT.
  Comments belong only where WHY is non-obvious — a subtle
  invariant, a hidden constraint, a workaround that surprises.
- **Cite the canonical surface.** Comments and docstrings reference
  the axiom (`// A24: trail is f ++ g, earliest-first`) or
  decision (`// per DECISIONS 2026-05-09 — observable identity on
  empty policy`) that justifies the shape. Cross-references
  compound; they keep the canonical docs reachable from the code.
- **Don't restate what the code does.** "Returns the deep payload"
  on `payload` is appropriate; "increments the counter by one" on
  `incrementByOne` is not.
- **No multi-paragraph docstrings; no multi-line comment blocks.**
  Triple-slash F# docstrings on public types and modules are short
  paragraphs that name the algebraic role and the canonical
  reference. Detail belongs in DECISIONS.

### Tests

- **Test names cite the axiom or theorem they enforce.** F#
  backtick-quoted identifiers carry the law:
  `` ``A4: kinds with same SsKey are structurally equal`` ``,
  `` ``T1: Project is deterministic`` ``,
  `` ``A24: trail is chronological under bind`` ``. Failing tests
  point directly at the law they claim to satisfy.
- **`Skip = "..."` for deliberate V2 divergences from V1.** The
  rationale lives in the Skip string. The test appears in test
  discovery so the divergence is structurally visible. Reserve
  contract names via Skip stubs *before* implementation lands; flip
  Skip to `[<Fact>]` when the gating dependency arrives.
- **`Skip` rationale either names the reachability gap (a feature
  not yet built) or the deliberate divergence (V2 chose differently).**
  Don't conflate. A reserved-but-unbuilt contract is different
  from a deliberately-omitted V1 contract.
- **Property tests for combinatorial spaces; example tests for
  specific contracts.** FsCheck.Xunit covers permutation
  invariance, idempotence, deterministic-output-under-shuffling.
  xUnit covers worked examples that name a specific behavior.
- **Per-file test helpers at the top.** `let private mkKey`,
  `let private entry`, etc. — small named constructors for the
  file's fixtures. Avoids boilerplate in each test; keeps the
  test's intent visible.
- **Don't re-validate smart-constructor invariants.** The
  `Result<'a>` from a `create` is unwrapped via `Result.value` in
  test fixtures; the production code trusts the value. Tests for
  the constructor itself test rejection; tests for downstream
  consumers don't.

### Naming

- **Types: generic algebraic names.** `Kind`, `Module`, `Catalog`,
  `Reference`, `Profile`. The codebase serves OutSystems today and
  must accommodate DACPAC, OData, etc.
- **Modules: `<Domain>Rules` for registered-intervention
  strategies.** `NullabilityRules`, `UniqueIndexRules`,
  `ForeignKeyRules`, `CategoricalUniquenessRules`. Other suffixes
  admissible when the call pattern differs (e.g.,
  `CycleResolution` is a structural strategy, not a registered
  intervention).
- **Pass modules under `Passes/` named after the pass.**
  `NullabilityPass`, `UniqueIndexPass`, etc. Pass version is a
  `[<Literal>]` constant inside the module.
- **Source / Code conventions for diagnostics.** `Source` is
  `<PassName>` or `adapter:<adapter-name>` or
  `emitter:<emitter-name>`. `Code` is dot-separated with a
  routing top-prefix (`tightening.*`, `profiling.*`, `adapter.*`).

### Cross-cutting commitments (carried from the operating disciplines table)

- Every transformation runs inside `Lineage<_>` (A25). Every
  pass-produced decision emits one lineage event. Lineage trail is
  earliest-first under bind (A24).
- Profile is independent of Catalog and Policy (A34); no
  back-references. Passes that don't consume Profile produce
  identical output for `Profile.empty` and any populated profile.
- Π consumes whichever subset of `Catalog × Profile` it needs but
  never `Policy` (A18 amended). If an emitter wants what feels
  like Policy, the work is enrichment (a pass) producing
  emitter-consumable values.
- Pass return shape names what the pass produces:
  `Lineage<'output>` for decisions only, `Lineage<Diagnostics<'output>>`
  when decisions plus observer-relevant findings.

## F# feature surface — alignment, conscious omissions, candidates

The codebase uses a deliberate slice of F#'s feature surface. Most
of what's idiomatic F# is either already aligned with V2's posture
or consciously deferred for principled reasons. This section names
each major feature, where it sits, and the trigger that would
re-open the question. The general meta-rule:

  **V2 Core is purity-first; anything that introduces effect, time,
  concurrency, or runtime metaprogramming is consciously deferred
  from Core. Adapters at the boundary may use what Core forbids,
  when the adapter's role demands it.**

### Already used (aligned and load-bearing)

| Feature | Where it appears | Why it's used |
|---|---|---|
| **Closed discriminated unions** | Every IR type (`SsKey`, `Origin`, `TighteningIntervention`, every outcome / keep-reason DU) | The type system is the contract; closed DUs make exhaustiveness compiler-checked. The closed-DU empirical-test discipline (`DECISIONS 2026-05-13`) is itself load-bearing. |
| **Smart constructors returning `Result<'a>`** | `SsKey.original`, `Name.create`, `CategoricalDistribution.create`, `NumericDistribution.create`, `NullabilityTighteningConfig.create`, etc. | Structural-commitment-via-construction-validation principle (`AXIOMS.md` operational principle). Every value carries its own truth. |
| **Records with structural equality** | All IR types | Equality is by content; T1 byte-determinism rests on structural comparison being honest. |
| **Functor + monad operators** (`>>=`, `<!>`) | `Result`, `Lineage`. `Diagnostics` and `LineageDiagnostics` use named functions (`bind`, `map`) at present. | Idiomatic F# for chained computation; reads like the algebraic spec. |
| **Pipe operator `\|>`** | Everywhere | The default composition idiom; reads top-to-bottom. |
| **`[<RequireQualifiedAccess>]`** | Modules whose case names risk collision (`NullabilityOutcome`, `UniqueIndexOutcome`, `ForeignKeyOutcome`, `Lineage`, `Diagnostics`, `LineageDiagnostics`, `Composition`, `Catalog`, `Profile`, `TopologicalOrder`, etc.) | Required when generic case names (`PolicyDisabled`, `EvidenceMissing`) recur across DUs; F# resolves ambiguity by picking one, which produces silent miscompilation. |
| **`let inline` for operators** | `>>=`, `<!>` on `Result` and `Lineage` | Removes the function-call overhead on hot-path operators; enables F# to specialize on the closure shape. |
| **List / sequence comprehensions with `yield`** | `Composition.fanOut`, `TopologicalOrderPass`, list-of-conditional-keys patterns in tests | Idiomatic for building lists with conditional inclusion; clearer than `List.collect`. |
| **FsCheck.Xunit property tests** | Permutation invariance, idempotence, structural-commitment validation | Sweeps combinatorial spaces example-based tests can't reach. |
| **Backtick-quoted test names** | Every test | Tests are prose: `` ``A24: trail is chronological under bind`` ``. |
| **Typed statement-stream Π output** (`seq<Statement>`) | `Projection.Targets.SSDT.SsdtDdlEmitter.statements`; `Render.toText` and `Deploy.executeStream` are realizations. (Chapter-3.1's `RawTextEmitter` retired in chapter 4.1.A close arc; SSDT DDL emitter is the active realization.) | A35 cash-out (chapter-3.1). Π's canonical form is a typed deterministic stream; realizations are sibling consumers. |
| **`AsyncStream<'a> = unit -> Task<'a option>`** | `Projection.Adapters.Sql.AsyncStream` (pull-based streaming primitive); `ReadSide.readRowsStream`. | Async-side streaming (Core stays sync). Combinators: `map`, `mapAsync`, `iter`, `fold`, `bufferUpTo`, `probe`, `batchesOf`. Bench observability via `AsyncStream.probe`. |
| **`Bench.streamProbe` / `AsyncStream.probe`** | `Render.toText`, `Deploy.executeStream`, `SsdtDdlEmitter.emit`, `ReadSide.readRowsStream`. | First-class stream observability. Records `<label>` (total ms) and `<label>.elements` (count) on enumeration completion. |
| **`Array.Parallel.map` for CPU-bound parallelism** | `PhysicalSchema.toPhysicalRows` (per-row SHA256). | Independent per-row work; deterministic output ordering preserved (Set-membership downstream). |
| **`SHA256.HashData` (allocation-free)** | `PhysicalSchema.hashStaticRowBytes`; `RowDigester.hashRowBytes`. | Replaces `SHA256.Create() + ComputeHash` to drop instance allocations on the per-row hashing hot path. |
| **`SqlBulkCopy` realization** (`Bulk.copyRows` + `Deploy.executeStream`) | `Projection.Pipeline.Bulk` + `Deploy.executeStream` folds consecutive `InsertRow` runs. | A36 realization. Bulk-vs-incremental is realization-layer policy; same algebra. |
| **Computation-expression builders** for `Lineage`, `Diagnostics`, `LineageDiagnostics` (chapter-Cluster-B; H-001 / H-002; 2026-05-22; adopted broadly at 2026-06-02 CE-adoption sweep) | `lineage { ... }` (writer over `(LineageEvent list, @, [])`); `diagnostics { ... }` (writer over `(DiagnosticEntry list, @, [])`); `lineageDiagnostics { ... }` (dual-writer / WriterT-stacked). Each builder carries `Bind` / `Return` / `ReturnFrom` / `Zero` / `Combine` / `Delay` / `Run`. **Auto-lift overload (2026-06-02):** `LineageDiagnosticsBuilder.Bind` carries a second overload accepting `Lineage<'a>` directly (lifts via `ofLineage`), so a `Composition.fanOut`-shaped `Lineage<DecisionSet>` flows into `let! value = lineage` without an explicit lift step. CE primitives: `Lineage.write` / `writeMany`, `Diagnostics.write` / `writeMany`, `LineageDiagnostics.writeLineage` / `writeLineages` / `writeDiagnostic` / `writeDiagnostics`. **Production consumers (post-2026-06-02 sweep):** `NullabilityPass.run` / `UniqueIndexPass.run` / `ForeignKeyPass.run` (canonical Pattern C — `let! value = lineage; do! writeDiagnostics entries; return value`); `CentralityPass` / `BoundedContextPass` / `SchemaComplexityPass` / `QueryHintPass` / `ProfileAnomalyPass` / `TopologicalOrderPass` ×2 (Pattern B — sites that previously hand-built `{ Value = result; Entries = [entry] }` records, now `do! writeLineages events; do! writeDiagnostic entry; return result`). | Writer-fidelity is enforced syntactically inside the CE (manual record-building is impossible). The seven Pattern B sites were **direct violations** of the writer-fidelity discipline — the CE form caught what convention had let slip. The discipline graduates from "pass drivers follow" to "type-level guarantee at any adoption site." Equivalence with the explicit `bind` chain is law-checked by H-001 + H-002 CE-equivalence property tests in `LineageTests.fs` + `DiagnosticsTests.fs`. |
| **`Pass<'a, 'b>` Kleisli arrow type** (chapter-Cluster-B; H-003; 2026-05-22; product retired 2026-06-04) | `src/Projection.Core/Diagnostics.fs` (`type Pass<'a, 'b when 'b : equality> = 'a -> Lineage<Diagnostics<'b>>`); `module Pass` with `id` / `compose` / `composeAll`; `module PassOperators` with `>=>`; `PassChainAdapter.Apply` field typed as `Pass<ComposeState, ComposeState>`. | The pipeline IS a Kleisli category over the dual-writer monad. The fold in `PassChainAdapter.compose` IS `Pass.composeAll` modulo per-step `Bench.scope`. Kleisli laws (left/right identity, associativity) property-tested in `DiagnosticsTests.fs`. (The monoidal product `product` / `&&&` / `first` / `second` was retired 2026-06-04 — unused symmetry-build; rebuild with the dynamic SsKey-disjointness check when parallel composition has a consumer. See `DECISIONS 2026-06-04`.) |
| **`DiagnosticLattice.subsumes` predicate** (chapter-Cluster-B follow-on; H-008; 2026-05-22; lattice trimmed 2026-06-04) | `src/Projection.Core/Diagnostics.fs` — `module DiagnosticLattice` with `subsumes` (the kept predicate). | Subsumption rule: code-prefix (separator-bounded) + SsKey-context compatibility. The `DiagnosticRelation` DU + `relations` / `isMinimal` / `minimal` antichain reduction were retired 2026-06-04 (zero consumers); rebuild the minimal-set reduction with the operator `diagnose` verb (likely rollup-with-counts). See `DECISIONS 2026-06-04`. |
| **`Lens<'s, 'a>` bidirectional total accessor** (chapter-Cluster-B follow-on; H-015; 2026-05-22; extracted to `Optics.fs` + adopted broadly at 2026-06-02 lens-adoption sweep) | `src/Projection.Core/Optics.fs` — `type Lens<'s, 'a> = { Get : 's -> 'a; Set : 'a -> 's -> 's }` + `module Lens` with `get` / `set` / `over` / `identity` / `compose`; canonical Catalog lenses in `module CatalogLenses` (`modules`, `kindsOf`, `attributesOf`, `referencesOf`, `columnOf`). Compile-order point: immediately after `Catalog.fs`, before every catalog-manipulating consumer — so the lens vocabulary is visible across the codebase. **Production consumers (post-2026-06-02 sweep):** `modules` — `SymmetricClosure`, `CatalogTraversal.mapKinds` (LineageBuffer), `Policy.filterCatalog`, `NamingMorphism`; `kindsOf` — `SymmetricClosure`, `CatalogTraversal.mapKinds`, `Policy.filterCatalog`, `ModuleFilter` (×2), `CatalogDiff.addKind`; `attributesOf` — `LogicalColumnEmission.substituteKind`; `referencesOf` — `SymmetricClosure.attachInverses`; `columnOf` — `LogicalColumnEmission.substituteAttribute` + `CatalogDiff.applyFacet.Nullability`. (`sequences` / `indexesOf` were deleted 2026-06-04 — never consumed; recreate the 5-LOC lens in the commit that adds a consumer.) | The kept optic — the partial `Prism` dual was retired 2026-06-04 (unused). Three lens laws (get-set, set-get, set-set) property-tested (rewritten 2026-06-04 from a `PassContext` carrier to a tuple). Deep-nested updates compose via `Lens.compose`; the lens form is the **default idiom** for nested IR updates — record-spread is reserved for sites that genuinely live before Optics.fs in the compile order (e.g., `Catalog.mapKinds` itself, which is the traversal primitive). |
| **`Validation` combinators** (chapter-Cluster-B follow-on; 2026-05-22) | `src/Projection.Core/Result.fs` — `module Validation` with `duplicateKeyErrors : code -> msgOf -> keySelector -> items -> ValidationError list`. | Collapses the recurring `groupBy + filter > 1 + map error` boilerplate at aggregate-root smart constructors. Used in `Catalog.create` for module / kind / sequence duplicate-key checks; ~30 LOC saved at 3 sites. Stable order: keys appear in first-occurrence order. |
| **`Catalog` traversal primitives** (chapter-Cluster-B follow-on; 2026-05-22) | `src/Projection.Core/Catalog.fs` — `Catalog.allModulesKinds` / `foldKinds` / `iterKinds` / `mapKinds` / `updateKindsWhere`. | Replaces inline `c.Modules \|> List.collect (fun m -> m.Kinds) \|> ...` boilerplate at 5+ sites. Pairs with the existing `CatalogTraversal.mapKinds` (Lineage-emitting variant). |
| **`TighteningPolicy.filterIntervention`** (chapter-Cluster-B follow-on; 2026-05-22) | `src/Projection.Core/Policy.fs` — private `filterIntervention` / `tryFindIntervention` combinators + per-variant extractors (`extractNullability`, `extractUniqueIndex`, etc.). | Closed-DU filtering primitive collapsing the 8 site-identical `List.choose (fun i -> match i with \| Variant -> Some \| _ -> None)` accessors to one-liners. |

### Aligned but underused (candidates whose trigger has not fired)

| Feature | Where it could fit | Trigger to adopt |
|---|---|---|
| **Function composition `>>` / `<<`** | Helpers like `decisionsOf` (currently `LineageDiagnostics.payload >> ...` pattern available); some `let f x = g x \|> h \|> i` chains could be `let f = g >> h >> i`. | When a private helper is plumbing-only (no parameter name carries documentation value). Don't rewrite existing `\|>` chains on principle; adopt where point-free reads as well or better than parameter-named. |
| **Active patterns** (`(\|Foo\|_\|)`) | Multi-step matches like `opportunityEntry` (match on `Outcome`, then nested match on `KeepReason`); same shape repeated in future passes that emit per-decision diagnostics. | When the same nested-match pattern appears in three or more places (the codebase's two-consumer threshold for primitives, plus one for a recognizable DSL). Would absorb the inner DU traversal into a named pattern: `(\|EnforceUnique\|DoNotEnforce\|)`. Don't pre-extract; surface when the pattern recurs. **H-012 audit (2026-05-22):** the analogous trigger for `SsKey` nested matches is **unfired** — zero call sites open the variant DU. `Identity.fs`'s accessor surface (`isDerived` / `rootOriginal` / `derivationReasons`) IS the active-pattern abstraction in a different form; consumers query through the accessors rather than opening the variant. The discipline holds positively. |
| **Units of measure** (`[<Measure>] type ms`, `[<Measure>] type pct`) | `NumericDistribution`'s percentile fields are `decimal`; nothing prevents passing a count where a percentile is expected. Could be `decimal<pct>`, `int64<rows>`, etc. | When a numeric-mix-up bug surfaces in real fixture data, OR when a strategy starts mixing percentile and count values in the same expression and the type system would help. Today's smart constructors enforce monotonicity; units of measure would add a complementary axis (dimensionality). **H-013 audit (2026-05-22):** trigger remains unfired; no fixture-borne numeric-mix-up observed. Skip stub at `tests/Projection.Tests/AxiomTests.fs::H-013` documents the deferral. |
| **Pattern-matching on records with shape literals** (`{ Foo = Bar }`) | Test fixtures and pattern-matching consumers. Today consumers usually destructure via `record.Field`. | When destructuring the same set of fields recurs across consumers; record-shape patterns make the consumer's intent visible. |
| **`[<NoComparison>]` / `[<NoEquality>]`** | Types where structural equality is misleading (none today; every IR type's structural equality is correct). | When a type carries cached state or order-sensitive payload that should not participate in equality. Surface when an IR refinement breaks the invariant "structural equality = semantic equality." |

### Consciously deferred (re-open triggers explicit)

| Feature | Why deferred | Trigger to re-open |
|---|---|---|
| **Reflection** (`typeof<>`, `GetType()`, attribute scanning) | The strategy registry mechanism (deferred at session 8) is reflection's natural home — find every type implementing `IStrategy` at startup. V2's strategy-layer codification dispatches via `FanOutConfig` directly; no name-keyed lookup is needed. | When a real consumer demands name-keyed strategy dispatch (e.g., a CLI surface that takes a strategy id from operator input). Pairs with the "Strategy registry mechanism" entry in the Active deferrals index. |
| **Object expressions** (`{ new IInterface with ... }`) | The codebase has very few interface boundaries. Polymorphism is via DU pattern matching, not interface dispatch. | When V2 grows interface-based polymorphism (e.g., `IDiagnosticSink` for streaming consumers in adapters; `ICatalogReader` for multiple sources). Object expressions are the right tool; they should land when the abstraction lands. |
| **Type providers** (`JsonProvider`, etc.) | Could provide compile-time access to the `osm_model.json` schema for the OSSYS adapter. Hand-written DTOs are simpler at first; the type-provider story has tooling fragility (CI integration; F# tooling versions). | When the OSSYS adapter ships and JSON-shape evolution becomes a maintenance burden. The OSSYS ADMIRE stub (session 14 commit 8) starts with hand-written DTOs; promotion to a type provider is a later optimization, not a session-15 default. |
| **DU member methods** (DUs carrying their own operation methods) | V2 convention is "types are data; modules carry operations." Coupling them is rejected — modules can be `[<RequireQualifiedAccess>]`'d, replaced, augmented; member methods can't. | Never on principle. The conscious omission is a stylistic load-bearing commitment. |
| **Anonymous records** (`{\| Foo = 1; Bar = 2 \|}`) | Throwaway intermediate values are rare in V2; named records make the intent visible. | When a test / boundary needs to construct a typed value that's truly one-off and doesn't merit its own type definition. Selective adoption; don't introduce as a pattern. |
| **`[<Struct>]` records / DUs** | Memory layout is not a bottleneck; immutability + GC is fine for the IR's scale. | When profiling shows allocation pressure on a hot pass. Premature `[<Struct>]` adoption can slow code by introducing copies; defer until evidence forces it. |

### Out of scope for Core (available in adapters when their role demands)

V2 Core's pure-core / no-I/O / no-time / no-mutation discipline
forbids these from `Projection.Core` regardless of how
idiomatic they are in F#. They may appear in adapters at the
boundary or in downstream consumer surfaces (CLI, streaming
diagnostic consumers, future host shells) when the adapter's role
demands it.

| Feature | Why out of scope for Core | Where it would land |
|---|---|---|
| **`Async<'a>` / `Task<'a>`** | Core is synchronous by design. T1 byte-determinism requires deterministic execution; async introduces scheduler nondeterminism. Strategies are synchronous (DECISIONS 2026-05-13 — Pass return-type codification names this as a stability-mark caveat). | Adapters that hit DB / file system. `Projection.Adapters.Osm.CatalogReader.parse` shipped with the `Task<Result<Catalog>>` shape (session 18; first substantive OSSYS adapter slice); the synchronous core consumes the result, not the Task. |
| **`MailboxProcessor` / actor modeling** | Core has no concurrent state and no message-passing. Mutable state inside Core is strictly function-local for performance-sensitive algorithms. | Adapters that need concurrent state — connection pooling for the OSSYS catalog adapter; streaming Diagnostics consumers in a future host shell that fans entries out to multiple sinks. Never in `Projection.Core`. |
| **FRP / `IObservable<'a>` / Reactive Extensions** | Core has no event streams. Lineage and Diagnostics are writers, not observables — entries accumulate in the value-carrier, they don't propagate by subscription. | A future Diagnostics consumer that streams to operator dashboards lives outside Core (downstream of the writer). The writer's contract is "produce entries"; the consumer's contract is "react to entries." Different responsibilities, different surfaces. |
| **`System.Reflection` for attribute scanning** | The closed-DU + typed-seam codification means dispatch is type-checked at compile time, not discovered at runtime. A reflection-based registry would replace compile-time guarantees with runtime ones. | If a future host shell needs plugin discovery (load strategy DLLs from a directory at startup), reflection lives in the host. The Core's strategy modules continue to be statically linked. |

### How to read this section

This taxonomy is descriptive of session-14's state, not prescriptive
of session-15. Each "underused" candidate has a trigger that
should be respected — don't adopt computation expressions because
they're cool; adopt them when consumer chains have grown long
enough that the operator-style chains are unreadable. Each
"consciously deferred" entry has a re-open trigger; if the
trigger fires, the deferral converts to a DECISIONS entry that
either adopts the feature or re-defers with explicit rationale
(same protocol as the Active deferrals index).

The meta-rule above (purity-first; adapters at the boundary may
use what Core forbids) is the gravitational sort: when in doubt
about a feature, ask whether it introduces effect, time,
concurrency, or runtime metaprogramming. If yes, it lives in an
adapter, not in Core. If no, the question becomes "does the
feature pay its weight at the call sites I have today?" — the
two-consumer threshold and the smell-test apply.

## What this file is not

- It is not a substitute for the canonical docs.
- It is not where new disciplines land. Substantive entries continue
  to land in `DECISIONS.md`; this file's "Operating disciplines"
  table updates to point at the new entry.
- It is not where load-bearing commitments are debated. The list
  above mirrors `HANDOFF.md`'s "What's load-bearing" section; if a
  commitment is removed there, this file updates to match.

## Maintenance

This file's currency is checked at every chapter close per the
**chapter-close ritual** (see Operating disciplines table).
Specifically: the Operating disciplines table must point at
current DECISIONS entries; the F# feature surface must reflect
what the codebase uses; the programming-style center target must
describe patterns visible in the code; the load-bearing
commitments must mirror `HANDOFF.md`. If any has drifted, the fix
lands during the close — not in the next chapter.

CLAUDE.md is at higher drift risk than the other canonical
surfaces because it indexes them. Session-15 codification of the
chapter-close ritual exists to make that risk structural rather
than aspirational.

## Closing

The codebase has earned its current shape because the disciplines
above were operated. The disciplines are not constraints; they are
the load-bearing structure that lets each chapter ahead support
more weight than the one behind. Hold the spine.
