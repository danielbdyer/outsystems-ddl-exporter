# CONSTELLATION — The Conservation Ledger (the architectural future, adjudicated)

**Date:** 2026-06-11
**Status:** Architectural thesis. Target-aware, current-state-grounded. Companion-of-method to
`CRYSTALLINE_FORM.md` (the codebase at rest); this document is the codebase **in motion** — where
the structure already in the code is headed, derived from the structure itself.
**Method.** Eight reconnaissance briefs (Core, Pipeline, Adapters, Targets, Tests, CLI/Voice,
horizon docs, DECISIONS 2026-06-04→11) were treated as testimony and spot-checked against source
at HEAD `9658cfa` (`ScriptDomBuild.fs` MERGE row-adds, `AsyncStream.fs` full surface,
`TransferRun.fs:27-63`, `Catalog.fs:77`, `ReadSide.fs:921-922`, `OperatorConsole.fs:91-100`,
`CatalogDiff.fs:933-986`). Every architectural claim below carries a path; every recommendation
carries a counterexample condition, per the `NORTH_STAR.md` discipline. Where this document's own
evidence is thin, §8 says so.

> **The thesis in one sentence.** Every change this engine makes is a quantum of displacement
> that crosses each boundary exactly once, is counted at the crossing by an independent ruler,
> and is appended to a replayable ledger — and the natural culmination of the codebase is to
> finish applying that one discipline to the two planes that do not yet have it: the engine's
> own **execution time** (stages) and the engine's own **runs** (the observability plane). The
> stream is the carrier this happens on. It is not the core model. The ledger is.

---

## Contents

1. [The constellation map — eight stars, with evidence](#1--the-constellation-map)
2. [The organizing principles](#2--the-organizing-principles)
3. [The transpositions — what the stars reveal laid across one another](#3--the-transpositions)
4. [The streaming hypothesis, adjudicated](#4--the-streaming-hypothesis-adjudicated)
5. [The recommendation — the Conservation Ledger](#5--the-recommendation)
6. [The staged migration path](#6--the-staged-migration-path)
7. [What not to build](#7--what-not-to-build)
8. [The epistemic ledger of this document](#8--the-epistemic-ledger-of-this-document)

---

## 1 — The constellation map

A star is a primitive already latent or explicit in the architecture: an invariant, a type, a
law, a discipline that recurs across layers and grows more legible at scale. Eight qualify.
Each is named, located, and stated as the law it embodies.

### S1 · Identity as conserved charge — `SsKey`

**Lives:** `src/Projection.Core/Identity.fs:51-55` — the four-variant DU
(`OssysOriginal of Guid | Synthesized of source × basisParts | DerivedFrom | V1Mapped`),
cross-variant equality false by construction.
**Law:** A1 (identity survives rename — unconditionally for `OssysOriginal`, boundedly for
`Synthesized`); A43 (identity is the conserved quantity under every move; Rename is the unique
move that perturbs Designation while conserving Identity).
**Recurrence:** every IR node carries one (`Catalog.fs` — Kind, Attribute, Reference, Index,
Sequence, Trigger, ColumnCheck, `StaticRow.Identifier`); `CatalogDiff` is keyed on it end to end
(`CatalogDiff.fs:192-218`); `LineageEvent.SsKey` (`Lineage.fs:287-293`) makes every provenance
record node-addressed; the readback recovers it from the `V2.SsKey` extended property at table
grain (`ReadSide.fs:992` `recoverKindSsKey`) and re-synthesizes it everywhere else.
**The stratification is load-bearing:** which variant a node carries determines what the diff
can see. A `Synthesized` key turns a rename into Removed+Added, detected only heuristically
(`CatalogDiff.synthesizedRenameWarnings`, `CatalogDiff.fs:589-614`). Identity quality is not
uniform across the estate; it is a gradient the architecture must (and does) name.

### S2 · The torsor — state as a point, change as a displacement

**Lives:** `WAVE_6_ALGEBRA.md` (the postulates); concretely `CatalogDiff.between` / `applyDiff`
(`CatalogDiff.fs:654-680`), `compose` (`:986`, partial groupoid composition defined iff
endpoints meet), `norm` (`:967`), `channelCounts` (`:933`); `Lifecycle.netDiff`
(`Lifecycle.fs:175-198`) as the fold; the FTC `reconstructLatest = genesis ⊕ Σδ` over a
disk-loaded chain (`EpisodicLifecycle`, `Episode.fs:224-244`).
**Law:** T12 (the three Weyl axioms — round-trip, identity, composition are one structure), T13
(replay = fold ⊕), T14 (channel orthogonality as direct sum), T15 (`‖emit(δ)‖ = ‖δ‖`; emission
is an isometry), T16 (the commuting square — the master equation). All Bucket-A executable
(`AxiomTests.fs:852-969`).
**The asymmetry is part of the star, not a defect:** schema δ is a value; data δ is
substrate-fused — the engine emits *the statement that is the diff* (the change-detecting
MERGE) and observes the realized delta post hoc through CDC (`WAVE_6_ALGEBRA.md` §12.4). The
torsor is one algebra with two representation regimes.

### S3 · The quotient — comparison through a deliberately lossy projection

**Lives:** `PhysicalSchema` (string-typed by design; the coarse `PrimitiveType`, no
`SqlStorage`); the named `Tolerance` entries with `@ladder` tags that `matrix-status.sh` reads;
`normalizeDefault`'s canonicalizations. Codified in `CRYSTALLINE_FORM.md` §3.2.
**Law:** the canary's `≅` is *equality after the quotient map π : Intent → Quotient*, and the
quotient's defining relations are exactly the named tolerances. The intent filter (the residual
of T16) demands every observed difference land in `intended ⊕ tolerated` — silence on neither.
**Why it is a star and not a detail:** the 2026-06-04 adversarial wave proved that fusing
Intent and Quotient types either blinds the canary (BIGINT→INT invisible) or breaks every
faithful redeploy. Shared vocabulary, never shared type. The quotient is the engine's
*epistemology*: it states precisely what the engine can know symmetrically about its substrate.

### S4 · Bidirectional totality — the adjunction, instanced everywhere

**Lives:** `Ingest ∘ Project = id` modulo named erasures (the constitution, `NORTH_STAR.md` §2);
`registered ⇔ executed` (`RegisteredAllTransformsBidirectionalTests`; DECISIONS 2026-06-04 —
which caught two real mismatches when first enforced); `expressible ⇔ reachable` (A44,
`MovementIsomorphismTests`, residual ∅ as of 2026-06-10); `code ⇔ copy` (`VoiceTotalityTests`);
`deserialize ∘ serialize = Ok` (`CatalogCodec`, total over the IR inventory); keyset agreement
`keys(artifact) = allKinds(catalog)` enforced in the smart constructor
(`ArtifactByKind.fs:69-82`).
**Law:** every correctness claim in this codebase that has survived is stated as a
section/retraction pair with a named residual, witnessed by a property test. Claims that were
*not* so stated (the speculative writer trinity, the optics duo) did not survive (DECISIONS
2026-06-04, dead-algebra retirement, −716 Core LOC).
**This is the proof discipline of the whole system,** and — as §3.5 argues — there is exactly
one major aggregate to which it has never been applied: the run itself.

### S5 · The named refusal — silence is reserved, never accidental

**Lives:** `ReverseLegRealization.choose` (`TransferRun.fs:37-63`) — pure, total, deterministic
over the request surface; every inadmissible combination returns a named `ValidationError`,
never a silent downgrade. The capability-descent ladder (`SurrogateCapture.fs:246-282`) —
descent fires only on SQL error 334; data errors always propagate; every descent is a named
`LaneDescent` on the report; the lane is sticky per kind. Exit-9 on dropped references; the
nine documented exit codes (`Program.fs:71-81`); `PlanAction.Refused` as a first-class plan
variant; `recordVerified` refusing to ledger an unverified episode (`MigrationRun.fs:271-285`);
`transfer.resume.sourceDrift` on journal fingerprint mismatch (`TransferRun.fs:957-963`).
**Law:** total decisions, named skips. Silence is *reserved* as the strongest possible
guarantee — CDC-silence on idempotent redeploy is the one place absence-of-signal is the proof
(`THE_USE_CASE_ONTOLOGY.md` §4.3 Unchanged: "the log's absence is the proof").

### S6 · Measurement curried into production — two rulers, one concern

**Lives:** `Bench` (`Bench.fs:103-258`) — the *only* sanctioned module-level mutable state in
Core, a lock-protected statistical fold, deliberately not a writer monad (`CRYSTALLINE_FORM.md`
§3.6); `Bench.streamProbe` recording `<label>` + `<label>.elements` on enumeration completion
(`Render.fs:130`, `StaticPopulationEmitter.fs:138`, `Deploy.fs:563`); `AsyncStream.probe`
(`AsyncStream.fs:66`); ~70 source files carry instrumentation; `dumpBench` at the tail of every
CLI run face (`OperatorConsole.fs:54`); the μ+σ committed baseline with soft tiers
(`scripts/perf-gate.sh`, `bench/baseline-canary.json`); the CDC capture count as the data
norm, witnessed at *exact equality* (`ReverseLegScaleTests.fs:322-363`: `3 × 4 = 12` captures
for 12 inserts; the reader itself pinned by `CdcMeasureTests.fs:79-153`).
**Law:** the engine measures itself with two independent rulers — the **fidelity ruler** (CDC
capture count = ‖δ‖, T15) and the **cost ruler** (Bench labels, the perf gate) — and both are
curried into the production functions rather than bolted onto test harnesses. The
`PERF_HARNESS.md` design (resolved, unbuilt) completes the cost ruler into an optimization
instrument: capture-before / change / capture-after, deterministic single-run delta, a named
promotion trigger for noisy scenarios.

### S7 · The stream as realization carrier — A35/A36 and the chunk pipeline

**Lives:** Π's canonical output `seq<Statement>` (A35; `SsdtDdlEmitter.statements`,
`StaticPopulationEmitter.statements` — both lazy), consumed by exactly two realizations
(`Render.toText`, `Deploy.executeStream` with its InsertRow-run folding into `SqlBulkCopy`,
`Deploy.fs:537-663`); `AsyncStream<'a> = unit -> Task<'a option>` (`AsyncStream.fs:23`) with a
deliberately minimal surface — `nextBatch` / `toList` / `probe`, the speculative combinators
(`map`, `fold`, `bufferUpTo`, `batchesOf`) retired 2026-05-30 under the two-consumer rule and
`nextBatch` re-admitted 2026-06-10 when the streaming realization became its consumer;
`writePlanStreaming` (`TransferRun.fs:981-1189`) — per-kind 50k chunks, at most two chunks
resident (one writing, one prefetching, `TransferRun.fs:1074-1098`), journal-guarded,
auto-selected as the dominant realization by `choose` since 2026-06-11.
**Law:** A36 — how a realization consumes the stream (bulk, per-row, text, parallel) is
invisible to Π. The algebra holds at the stream level; the stream is *where displacement quanta
flow*, never where they are defined.

### S8 · The ledger — append-only partial sums with fingerprints, at two grains

**Lives:** at **episode grain**, `LifecycleStore` + `Episode` (durable chain; monotone append;
`recordVerified` gates entry; FTC replay reproduces the latest state across a disk reload) and
the refactorlog (append-only, deduped by deterministic UUIDv5 `OperationKey`,
`RefactorLogEmitter.fs:135-149`, prior-wins `accumulate` at `:373-379`). At **chunk grain**,
`CaptureJournal` (`CaptureJournal.fs:48-81`): client-side NDJSON named
`transfer-<SHA256(marker)[0:16]>.ndjson` over a deterministic plan marker; one `ChunkRecord`
per completed chunk carrying a source fingerprint `(FirstPk, LastPk, RawCount)`;
`File.AppendAllText` as the atomic commit boundary; fingerprint match = skip, mismatch = named
drift refusal; a completed run re-runs as a full skip. At **run grain** — *partially*: the
`RunLedger` (when `PROJECTION_LEDGER_DIR` is set), `BenchSink` JSON snapshots, `LogSink`
envelopes. The run-grain ledger exists as four disjoint sinks, not as one value (§3.5).
**Law:** P-PROV (append-only, never deleted, replay-correct from genesis) and its chunk-grain
analog: a crash leaves no ledger line, so re-execution is safe; a recorded line is a verified
partial sum. The ledger is the torsor made durable.

---

## 2 — The organizing principles

Transposing the stars yields four principles that make the whole cohere. They are descriptive —
each is already operating in the code — and the recommendation in §5 is nothing more than
finishing their application.

**P1 — Conservation accounting.** Every quantum of change crosses each boundary exactly once,
is counted at the crossing, and the count is reconciled against an independent ledger. The
schema plane: move counts per channel (`CatalogDiff.norm`/`channelCounts`) reconciled against
the refactorlog and the ALTER stream (T14 disjointness: a rename appears in exactly one
channel). The data plane: row deltas reconciled against the CDC capture series at exact
equality (T15). The identity plane: SsKeys conserved under every move, with creation and
annihilation the only exceptions (A43). T16 — the commuting square — *is* the books balancing:
the model-plane entry and the substrate-plane entry must agree, modulo the named residual. This
is double-entry bookkeeping for schema and data change, and it is the single deepest fact about
this system: **it is an accounting engine whose audits are theorems.**

**P2 — Carriers reify eagerly; verbs reify at the second consumer.** Codified in
`WAVE_6_ALGEBRA.md` §12.3, enforced empirically by the 2026-06-04 dead-algebra retirement (the
two consumers that finally shipped both *declined* the prebuilt structure), and demonstrated in
miniature by `AsyncStream`'s combinator history (retired at zero consumers, `nextBatch`
re-admitted under a real one). Any architectural future for this codebase that begins "first we
introduce the abstraction" is wrong by this codebase's own measured precedent.

**P3 — Realization is chosen by evidence and downgraded by name.** The realization selector
(`choose`: streaming auto-selected where admissible, because it *dominates on every measured
axis* — ~35.5k vs ~27k rows/sec, bounded memory, journal-resumable), the capability-descent
ladder (descend only on the named capability error, prove every rung's equivalence with a
canary), and the bench-driven optimization protocol (three-candidate / 2-refuted / 1-confirmed)
are one pattern at three timescales: **architecture decisions are downstream of measurements,
and the measurements are committed artifacts.** Selection logic stays pure and total over
request facts plus committed priors — never runtime-adaptive (T1 determinism).

**P4 — Functional distinction over shared vocabulary.** The anti-false-symmetry rule
(`CRYSTALLINE_FORM.md` §2): Intent vs Quotient IR; the three writer sinks; the codec's
asymmetric fields. Surfaces that share vocabulary share a *seam*, not a type. This principle is
what disqualifies the naive form of the streaming hypothesis (§4): `seq<Statement>`,
`AsyncStream<StaticRow>`, `Task<'T list>`, and `CachedValue array` share the vocabulary "data
flowing through" while differing in monad, lifecycle, grain, and consumer — exactly the
configuration where this codebase has learned that a unifying wrapper damages the system.

---

## 3 — The transpositions

Where pairs of stars are laid across one another, structure becomes visible that neither shows
alone. Five transpositions carry the weight of the recommendation.

### 3.1 Stream × Torsor: the chunk is a displacement quantum; the journal is its partial sum

The streaming realization decomposes a data displacement into 50k-row chunks; the journal
records one fingerprinted line per completed chunk; resume is "continue the fold from the last
verified partial sum"; drift is "the base state moved under the fold" — which is precisely the
torsor's no-cheat law (W3 state-dependence) enforced at runtime, at chunk grain. Meanwhile the
schema plane already has the identical structure at episode grain: `LifecycleStore` appends
verified episodes; `reconstructLatest` is the fold; `recordVerified` refuses unverified entries
the way the journal's atomic append refuses uncommitted chunks. **The CaptureJournal and the
LifecycleStore are one concept — the durable partial-sum ledger — at two grains,** built
independently eleven days apart (`Episode`/`LifecycleStore` 2026-06-01; `CaptureJournal`
2026-06-10) without either citing the other. The codebase discovered the same shape twice; the
architecture should name it once. Consequence: **pausability is not a streaming feature. It is
a torsor feature.** Any operation whose progress is a fold over displacement quanta becomes
pausable the moment its partial sums are ledgered — which is why the materialized path's G10
progress table (`TransferRun.fs:420-431`) and the journal, today structurally disjoint resume
mechanisms, should converge on one contract (§6, stage 3).

### 3.2 Stream × Measurement: probe entanglement is a missing stage boundary, and T14 names the fix

The known trap (`PERF_HARNESS.md` §3.8): a lazy `streamProbe`'s wall-time includes consumer
time between pulls, so `emit.staticPopulation.statements.stream` — the top emit label at scale
— is *not* pure emit cost; it is emit cost plus `executeStream`'s SQL round-trips. The harness
works around this with isolating labels added by hand (§3.6). The constellation reading: the
measurement fails to decompose because **the pipeline has no typed stage boundaries** — stages
exist only as display strings (`pipelineStages = ["extract"; "profile"; "emit"]`,
`OperatorConsole.fs:91-100`) for the Watch renderer. T14 already states the law the time plane
is missing: the norm is additive over an orthogonal decomposition. Time should obey the same
equation over stages: `wall(run) = Σ wall(stage) + ε`, with ε the scheduling residual, and the
per-stage attribution structural rather than label-discipline. A stage boundary is a real
boundary crossing — under P1 it must be counted. This converts the harness's per-scenario
labeling work from an ongoing discipline into a one-time structural fact.

### 3.3 Identity × Stream: row identity is currently paid at the wrong grain — and it is the measured bottleneck

Every row crossing the read boundary is built as
`{ Identifier = Synthesized("READSIDE_ROW", [sprintf "%s.%s.%d" …]); Values = Map<Name,string> }`
(`Catalog.fs:77`; `ReadSide.fs:921-922`). The 2026-06-11 measured priors (`PERF_HARNESS.md`
§5): the per-row carrier build costs ~1.85 µs/row — *comparable to the entire SqlClient wire
read floor* (~1.9 µs/row) — and the identity basis `sprintf` alone is ~0.39 µs/row (6× cheaper
as precomputed-prefix concat). The IR's aggregate discipline (typed names, SsKeys everywhere)
is being applied per-row, 100k+ times, to values whose actual identity at row grain is the PK —
which is what the remap, the journal fingerprint, and the MERGE `ON` clause all key on anyway.
S3 names the resolution: this is the **Intent/Quotient cleavage applied to the data plane**.
Kinds carry the intent-rich IR; rows in flight should carry a coarse positional carrier —
column-ordinal arrays against a kind-level column basis established once per stream — with the
typed vocabulary preserved at the stream's *header*, not in every element. The brief's caveat
stands: `StaticRow.Values : Map<Name,string>` is a contract with consumers across
`SurrogateRemap`, `PhysicalSchema`, and the emitters, so this is an IR-adjacent change that
must ride the harness's before/after (slice 2) — but the constellation says it is not merely a
perf tweak; it is the data plane's missing quotient type.

### 3.4 Measurement × Selection: the perf harness is the instrument that mints selector evidence

`ReverseLegRealization.choose` hard-codes streaming's dominance because the reverse-leg benches
proved it (`ReverseLegScaleTests`, `ReverseLegStreamingTests`). That is the template: a
realization selector is a pure function whose *justification* is a committed measurement. The
perf harness generalizes the measurement substrate to every pipeline stage — which means every
future "which realization?" question (batch size, leveled deploy, wavefront parallelism, MERGE
shape for bootstrap scripts) acquires the same resolution protocol: build the isolated
scenario, capture the delta, encode the dominant choice in a pure selector, name the override
flags, never downgrade silently. The harness is not tooling adjacent to the architecture; under
P3 it is the architecture's decision procedure.

### 3.5 Totality × Observability: the run is the one aggregate without a section/retraction pair

Every load-bearing aggregate in the system round-trips: the Catalog (codec), the artifact
keyset (T11), the config (A44), the copy (code⇔copy), the registry (registered⇔executed), the
episode chain (FTC). The **run** does not. A run's truth is scattered across four sinks with
four lifecycles: `LogSink` envelopes (live, subscriber-pushed), `BenchSink` JSON (timestamped
filenames — wall-clock-named, not content-addressed; the wall-clock there is a *deliberately*
reified non-determinism boundary per its LINT-ALLOW, `BenchSink.fs:45-55`, so R1 relocates it
into the Run value's recorded field rather than its address), the `CaptureJournal`
NDJSON, and the `LifecycleStore` episode (plus the optional `RunLedger`). `REPORTING_HORIZON.md`
§1 already states the law this violates: "one event stream, many renderings — every report is a
*projection* of data we already produce." But a projection needs a value to project *from*, and
today there is no `Run` value — the Watch renders live events, the summary panel renders a
different aggregation, `inspect <runId>` (D5) is designed but unbuildable because there is no
durable runId-addressed record to inspect, and the perf harness's before/after diff is
specified as file naming conventions (`bench/perf/<name>/before.json`) rather than as an
operation over run values. The torsor pattern is sitting here unapplied: **runs are points;
before/after deltas are displacements; the harness's `diff` is `⊖` at the observability
plane.** This is the largest structural gap the constellation reveals, and closing it is the
keystone of §5.

---

## 4 — The streaming hypothesis, adjudicated

The seed hypothesis: *the stream as core praxis — a streaming, task-based architecture in which
the stream itself is the globally disambiguated concern, a core data model wrapping the stream
so its nature is explicit everywhere.* Tested against the codebase, the verdict splits cleanly.

**Confirmed — the stream as the canonical carrier of realization.** The trajectory is real and
accelerating: A35 made Π's output a typed deterministic stream (2026-05-30); the reverse leg's
bounded-memory streaming realization shipped (2026-06-10) and was promoted to the
*automatically selected* dominant realization (2026-06-11, `ReverseLegRealization.choose`);
`AsyncStream.nextBatch` was re-admitted under that consumer; the chunk pipeline carries
prefetch overlap and a resume ledger. At the write seam, streaming is no longer an option; it
is the default the flags override.

**Refuted — the stream as the core data model.** Four independent lines of evidence, all from
the codebase's own record:

1. **The experiment was already run.** A rich `AsyncStream` combinator surface (`map`,
   `mapAsync`, `iter`, `fold`, `bufferUpTo`, `batchesOf`) was built speculatively and retired
   2026-05-30 at zero consumers (`AsyncStream.fs` header; CLAUDE.md feature table). The live
   surface is three functions, each consumer-justified. The codebase has measured what a
   stream-first abstraction layer costs here, and deleted it once.
2. **Core is synchronous by commitment, not omission.** T1 byte-determinism is constructed on
   deterministic execution; the no-Task/no-Async rule in Core is load-bearing
   (CLAUDE.md, "out of scope for Core"). A core stream model would either live outside Core
   (where it already lives, as `AsyncStream`) or breach the purity carve-out that the entire
   equational-reasoning discipline rests on.
3. **The delta-representation asymmetry blocks it semantically.** The schema plane's
   displacement is a value (`CatalogDiff`); the data plane's displacement is deliberately
   substrate-fused, observable only as the CDC series (`WAVE_6_ALGEBRA.md` §12.4 — and the
   refinement pass that *corrected* an earlier draft which had made exactly this mistake). A
   universal stream-of-changes model would re-flatten a distinction the algebra spent a
   correction cycle establishing.
4. **P4 disqualifies the unification.** The four stream-shaped representations at the
   boundaries (`seq<Statement>`, `AsyncStream<StaticRow>`, the schema queries' `Task<list>`,
   `EvidenceCache`'s column arrays) differ in monad, laziness, grain, and consumer. Wrapping
   them in one `Stream` type is the false-symmetry move — shared vocabulary mistaken for shared
   semantics — that `CRYSTALLINE_FORM.md` documents as this codebase's one recurring disease.

**Subordinated — to the Conservation Ledger.** The truer star is P1 + S8: the torsor with
durable, fingerprinted partial sums, of which the stream is the carrier and the count-at-the-
crossing is the measurement. The DECISIONS-window evidence is decisive on ordering: what the
recent sessions actually *prove* with property tests is bidirectional totalities and named
refusals; what they *run* is streams. The stream is what flows; the ledger is what is true.

**The four candidate unlocks, re-derived in corrected form:**

| Hypothesized unlock | Corrected form, grounded |
|---|---|
| Data shapes native to streaming | The row-grain **quotient carrier** (§3.3): column-basis arrays per stream, typed vocabulary at the header. Not a stream wrapper — a second IR grain under the existing Intent/Quotient principle. |
| Side effects that strengthen the measurement apparatus | Already real (S6 — Bench is the sanctioned impurity). The completion is **structural stage boundaries** (§3.2) so the measurement decomposes additively instead of by label discipline. |
| Pausability | A **ledger property, not a stream property** (§3.1): any fold over displacement quanta with fingerprinted partial sums is pausable. Generalize the journal/episode contract; do not build a coroutine runtime. |
| Multiplexing across parallelizable jobs | Already licensed by the algebra: T14 orthogonality + the FK-topological order define the parallel-safe groups. The data side shipped it (`composeRenderedLeveled`, canary 3:34→2:22); the schema side has the named trigger (PERF_OPPORTUNITIES); `executeBatchParallel` exists unwired (`Deploy.fs:413-498`) awaiting the composer's safe groups. Wire under harness evidence, not under a scheduler. |

---

## 5 — The recommendation

**Name the system what it already is — a Conservation Ledger — and finish it.** The engine is
an accounting system for change: SsKeys are the account holders (S1); displacements are the
transactions (S2); norms are the amounts, counted by two independent rulers (S6); ledgers are
the books (S8); the adjunction round-trips are the audits (S4); refusals are the controls (S5);
streams are the medium of exchange (S7); the quotient states what the auditor can see (S3).
Five planes already keep correct books: schema (refactorlog + diff channels), data (CDC + the
journal), identity (SsKey conservation), time-as-evolution (the episode chain), decision (the
registry + manifest). Two planes do not: **execution time** has no additive decomposition, and
**the run** has no ledgered value. The architectural future is five commitments, each
falsifiable, ordered by leverage:

**R1 — The Run becomes a value (the fifth aggregate).** One durable, content-addressed record
per execution: input digests (catalog via `CatalogCodec` bytes, policy version,
`RegistryDigest`, scale knobs), the `LogSink` envelope sequence, the final `Bench` snapshot,
refs to any journal and episode the run produced, the verdict and exit code. The law it must
satisfy — its section/retraction pair — is `project (record run) ≡ live view`: the Watch
display, the summary panel, the future `inspect <runId>` TUI, and the harness's before/after
diff all become projections of the same value, with a property test asserting the live and
post-hoc renderings agree on the structured content. The torsor lifts to the observability
plane: `diff runA runB` is `⊖` over runs; the perf harness's per-label delta is its norm
restricted to the Bench channel. *Counterexample condition:* a rendering that shows a fact
absent from the recorded Run value, or a recorded Run that cannot reproduce its summary —
either fails the totality test. (This subsumes D5/D6, the REPORTING_HORIZON run-to-run ledger,
and the R6 N=10 consecutive-green gauge — all currently blocked on exactly this missing value.)

**R2 — Stages become typed boundaries with additive time.** Promote the Watch's stage strings
to a typed stage spine each `run*` face declares; every stage crossing emits its Bench scope
structurally. The law: `wall(run) = Σ wall(stage) + ε` with ε bounded and reported — T14 for
the time plane. This retires the lazy-probe attribution trap as a class and gives the perf
harness its per-stage KeyLabels for free. *Counterexample condition:* a hot path whose cost
appears in no stage's account, or stage sums diverging from run wall-time beyond the named ε.

**R3 — One partial-sum ledger contract at every grain.** Extract the shared shape of
`CaptureJournal` and `LifecycleStore` (append-only; fingerprint-guarded entries; verified-only
admission; replay = fold; drift = named refusal) into one named contract with two existing
instances, then retire the G10 progress table onto it (the materialized path's resume becomes
journal-shaped) and let the harness's before/after captures be its third instance. Pausability
then holds *wherever the contract is implemented*, by construction. *Counterexample condition:*
a resumable operation whose resume semantics cannot be expressed as "fold from last verified
partial sum with fingerprint check" — that operation is evidence the contract is wrong, and
must be named before the contract ships.

**R4 — The data plane gets its quotient carrier.** The row-grain carrier of §3.3:
column-ordinal arrays + per-stream column basis, `StaticRow` retained at the IR/static-
population grain where its typed Map earns its cost. Gated strictly on harness slice 2
reproducing the ~8.8 µs/row prior and attributing the wire/materialize split first
(`PERF_HARNESS.md` §3.6 label 1). *Counterexample condition:* the before/after shows the
carrier is not the dominant non-wire cost at scale, or a consumer (SurrogateRemap,
PhysicalSchema hashing, emitters) cannot be expressed over the basis form without losing a
named invariant — either kills the slice, and the refuted candidate is documented per the
bench protocol.

**R5 — Multiplexing only where the algebra licenses it.** Topological levels are the
parallel-safe unit (proven on the data side); ship the schema-side level grouping and wire
`executeBatchParallel` when its harness scenario shows the win; per-table transfer wavefronts
only if the real-wire bench misses 20k rows/sec (the already-named trigger). All selection
stays in pure selectors over committed evidence (P3). *Counterexample condition:* any
parallelism whose safety argument is not a T14-orthogonality or topological-order fact is
out of scope by construction.

What this future is **not**: it is not event sourcing (the substrate-fused data delta means
the engine deliberately does not own a universal event log — SQL Server's CDC is the data
plane's ledger and the engine reads it); it is not a workflow engine (runs are recorded, not
orchestrated by a scheduler); and it is not a streaming framework (the stream remains a
boundary-layer carrier with a three-function surface that grows only under consumers).

---

## 6 — The staged migration path

Each stage is independently shippable, ordered so that measurement precedes mechanism. Stages
0–1 are already-resolved backlog; the constellation adds 2–5.

**Stage 0 — Build the perf harness as designed** (`PERF_HARNESS.md` §4, slices 0–5; design
RESOLVED). It is the evidence substrate every later stage's acceptance criterion cites. Within
it, slice 1 (the seed-MERGE pair) doubles as a correctness probe: `buildMergeStatementCore`
adds every row to a single `InlineDerivedTable` with no batch split (`ScriptDomBuild.fs:857`;
spot-checked — no chunking exists), so any kind with >1000 static rows likely renders a MERGE
SQL Server refuses at parse time (the transfer side's Msg-10738-class cliff). No test covers
the boundary today. The harness scenario must capture the failure as the BEFORE witness, then
the fix (chunked `INSERT … VALUES` into a stage table + one `MERGE … USING #stage`, preserving
single-MERGE `DeleteScope` semantics — the alternative shape `PERF_HARNESS.md` §1.3c names)
ships under the bench protocol.

**Stage 1 — The isolating labels + ReadSide drain scenario** (harness slices 2–3): the
`readside.rowstream.materialize` label, the batch-size sweep, the bulk100k re-confirmation of
the §5 priors. Output: the wire/materialize attribution R4 is gated on.

**Stage 2 — Typed stages (R2).** Mechanical: the stage spine type in the Pipeline, the Watch
arcs re-derived from it, the additivity property test. Touches `OperatorConsole.fs` /
`RunFaces.fs` / the run faces' Bench scopes. No semantic change to any run.

**Stage 3 — The ledger contract (R3).** Extract; instantiate twice (journal, episode store);
migrate G10; add journal compaction under its named trigger (288M-pair NDJSON). The reverse-leg
queue's open items (reconcile ∘ streaming, WipeAndLoad ∘ journal) land naturally here, since
both are "which ledger governs this fold" questions.

**Stage 4 — The Run value (R1).** The largest slice: define the aggregate, route the four
sinks through it (LogSink envelopes as its event field; BenchSink as its snapshot field —
filenames become content-addressed digests instead of wall-clock timestamps,
`BenchSink.fs:54`), ship `inspect <runId>` and `diff <runA> <runB>` as its first two
projections, and re-express the R6 cutover gauge over it. The Voice/storyboard work (acts 7–8,
the timeline strip) gets its substrate here rather than reading the episode store ad hoc.

**Stage 5 — Licensed parallelism (R5).** Schema-side level grouping; wire
`executeBatchParallel`; wavefronts behind the wire-bench trigger. Last because every win here
must cite a Stage-0/1 measurement.

Throughout: the existing gates hold (perf-gate baseline re-recorded only with a DECISIONS
amendment; pure/Docker pool separation; the FS3511 Release shapes and the ISNULL-vs-CASE trap
stay documented hazards on the write path).

---

## 7 — What not to build

Directions considered against the constellation and rejected, with the discriminating reason.
Several have already been rejected once by this codebase; they are listed so the temptation is
recognized when it recurs.

1. **A core `Stream<'a>` model / Rx / `IObservable` / actor runtime.** Rejected by §4's four
   lines of evidence. The re-open trigger is honest and narrow: a *second* consumer demanding a
   specific combinator re-admits that combinator (the `nextBatch` precedent) — never the layer.
2. **A `Torsor` typeclass / renaming `between`→`⊖`.** Already refused (`WAVE_6_ALGEBRA.md`
   §12.3): the engine must *behave* like the torsor under discriminating witnesses, never be
   *named* like it on speculation. R1 lifts the torsor pattern to runs by building the concrete
   `Run` value and its concrete `diff` — not a generic abstraction.
3. **A model-plane `RowDiff` value.** Rejected by the delta-representation asymmetry (§12.4).
   The data delta's observable is the CDC series; building a value-level row diff re-implements
   what the substrate computes better and breaks the at-target-MERGE policy.
4. **Runtime-adaptive realization selection** (selectors reading live bench feedback). Breaks
   T1 determinism and the "selector is testable without a connection" property
   (`TransferRun.fs:37-46`). Evidence flows into selectors as *committed priors* via the bench
   protocol, with a human decision in the loop — that is P3's whole content.
5. **Free-monad pass scheduling (H-063) / speculative branching execution.** Its prerequisite
   (`LineageTree`) was built once, found zero consumers, and was deleted (DECISIONS
   2026-06-04). The Kleisli composition (`Pass.composeAll`) is sufficient for every shipped
   chain; rebuild only under a consumer that names a branching requirement.
6. **A general job scheduler for multiplexing.** The parallel-safe units are already defined by
   the algebra (topological levels; orthogonal channels); a scheduler would relocate the safety
   argument from the type/order structure into runtime policy — the wrong direction for a
   system whose proofs are structural.
7. **Unifying the four boundary stream representations into one type.** P4. The seam to
   single-source is the *measurement* (probe/scope conventions) and the *ledger contract* —
   the vocabulary, never the types.

---

## 8 — The epistemic ledger of this document

Applying the engine's own discipline to this thesis: what here is verified, what is testimony,
what is conjecture.

**Verified directly** (read in source this session): `ReverseLegRealization.choose`'s full
text; `AsyncStream`'s three-function surface; `StaticRow`'s shape and the `READSIDE_ROW`
basis `sprintf`; the absence of row-chunking around `ScriptDomBuild`'s MERGE `RowValues` adds;
the Watch stage strings; `CatalogDiff.norm`/`channelCounts`/`compose` signatures; the
PERF_HARNESS resolved design and measured priors; the canonical corpus claims cited from
`WAVE_6_ALGEBRA.md`, `NORTH_STAR.md`, `THE_USE_CASE_ONTOLOGY.md`, `CRYSTALLINE_FORM.md`,
`DEBRIEF_2026_06_02`, `HANDOFF.md`.

**Verified post-commit** (second pass, source-read): the §3.1 independence claim —
`CaptureJournal.fs` contains zero references to Episode/Lifecycle and
`LifecycleStore.fs`/`Episode.fs` zero references to the journal; `CaptureJournal.digestOf`
(SHA256 → 16 lowercase hex) and the `append`/atomic-commit comment ("a crash AFTER the append
never re-executes the chunk and a crash DURING the chunk never journals it");
`MigrationRun.recordVerified`'s refusal of unverified outcomes (verbatim, `MigrationRun.fs:271+`);
`BenchSink.persistJson`'s reified wall-clock boundary; `Ingestion.collectInOrder` at
`src/Projection.Adapters.Sql/Ingestion.fs:39`; and the absence of any emitter-side test at the
>1000-row MERGE boundary (the only grep match in the test tree is a timing comment in
`ReverseLegScaleTests.fs:8`).

**Testimony, spot-check-consistent** (recon briefs whose load-bearing siblings verified
clean): the per-file line numbers for `Deploy.fs`, `TransferRun.fs`,
`ReadSide.fs`, `LiveProfiler.fs`, `AxiomTests.fs`, `ManifestEmitter.fs`,
`RefactorLogEmitter.fs`; the four `AsyncStream` materialization sites; the baseline-canary
label statistics.

**Known weaknesses in the proof surface this document relies on** (inherited, not introduced):
`AxiomTests.citationOf` is string-typed — renamed tests silently stale the citation
(`AxiomTests.fs:51-59`); Docker-gated tests that soft-skip are indistinguishable from passes
at summary level; the streaming path's "bounded memory" claim rests on architectural
description, not an asserted RSS bound (`ReverseLegStreamingTests` — the harness should add
the assertion); the ‖δ‖=k norm witness exists at exactly one scale point
(`ReverseLegCdcNormTests`, 12 rows); the 1000-row MERGE cliff is untested in either direction.
Stage 0 addresses all four.

**Conjecture, falsifiable as stated:** that the journal/episode shapes generalize to one
contract without semantic loss (R3's counterexample condition); that stage-additive time holds
with small ε on this host class (R2); that the row carrier is the dominant non-wire cost at
scale (R4 — the priors say yes at loopback; the harness decides); that the Run value's live ≡
post-hoc projection law is satisfiable over the existing LogSink envelope vocabulary (R1).

The constellation stands or falls with these. If the harness refutes R4's premise, the row
carrier stays as it is and this document's §3.3 becomes a documented refuted candidate — which
is itself the system working as designed.

— Recorded for the receiving agent. The stream is what flows; the ledger is what is true; the
audit is a theorem. Hold the spine; balance the books.
