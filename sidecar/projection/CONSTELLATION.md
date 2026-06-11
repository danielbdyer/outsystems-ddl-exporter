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
evidence is thin, §12 says so.
**Amended (same day, second pass).** §§4–6 added — the holonic map, the calculus, and the
conceptual thermodynamics; the prior §§4–8 renumbered §§7–11. New evidentiary anchors verified
for the amendment: `ChangeManifest.pathLength` + the live churn witness
(`ChangeManifest.fs:97-98`; `ChangeManifestTests.fs:136`), `changeDetectionPredicate`
(`ScriptDomBuild.fs:782-880`), the monotone-append refusal (`Episode.fs:180-182`;
`MigrationRun.fs:90`), `EpisodeCoordinate` (`Episode.fs:11-16`).
**Amended (same day, third pass).** §9 added — the reification: R1–R5 as signature-grade F#,
each construct anchored to its in-repo precedent; prior §§9–11 renumbered §§10–12.

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
4. [The holonic map — one structure, every grain](#4--the-holonic-map)
5. [The calculus of the constellation — composition, derivatives, integrals](#5--the-calculus-of-the-constellation)
6. [The conceptual thermodynamics — conservation, dissipation, refusal](#6--the-conceptual-thermodynamics)
7. [The streaming hypothesis, adjudicated](#7--the-streaming-hypothesis-adjudicated)
8. [The recommendation — the Conservation Ledger](#8--the-recommendation)
9. [The reification — the future codebase, in F#](#9--the-reification)
10. [The staged migration path](#10--the-staged-migration-path)
11. [What not to build](#11--what-not-to-build)
12. [The epistemic ledger of this document](#12--the-epistemic-ledger-of-this-document)

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
what disqualifies the naive form of the streaming hypothesis (§7): `seq<Statement>`,
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
mechanisms, should converge on one contract (§10, stage 3).

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
keystone of §8.

---

## 4 — The holonic map — one structure, every grain

The transpositions of §3 are pairwise. Laid across one another all at once, they resolve into a
single repeated structure — this section maps it. The term *holon* is used descriptively: a unit
that is a complete whole at its own grain and a constituent part at the grain above. The claim,
stated falsifiably: **the eight stars of §1 are not eight structures. They are five aspects of
one holon — identity, displacement, norm, ledger, comparison — and the system instantiates that
holon at every grain it operates on.** The grains nest on the state side as
facet ⊂ row ⊂ chunk ⊂ kind ⊂ catalog ⊂ episode ⊂ timeline, and on the execution side as
sample ⊂ label ⊂ stage ⊂ run ⊂ run-ledger. What §1 presented as stars and §3 as couplings is,
at fifty feet, one accounting cell tiled down a grain tower.

### 4.1 The tower

| Grain | Identity | Displacement (the quantum) | Norm (the count) | Ledger (durable partial sums) | Comparison (the quotient and its tolerances) |
|---|---|---|---|---|---|
| **facet** | attribute `SsKey` × facet name (`AttributeFacet`, 9 variants; `CatalogDiff.fs:27-61`) | `AttributeChange` | per-facet term in `channelCounts` (`CatalogDiff.fs:933`) | carried by the grain above (the diff channels) | `normalizeDefault`; `CharAnsiPadding` / `DecimalScale` tolerances |
| **row** | the reconciled business key / PK — deliberately *not* an SsKey at rest (§3.3; P-REKEY) | Insert / Update / Unchanged / Delete — the MERGE clauses (`THE_USE_CASE_ONTOLOGY.md` §4.3) | CDC captures: 1 per insert/delete, 2 per update, 0 unchanged — weights pinned exactly (`CdcMeasureTests.fs:79-153`) | the substrate's `cdc.<table>_CT` — the one ledger the engine audits but does not own | the null-safe distinctness predicate over the comparable-column set (`ScriptDomBuild.fs:782-810`); tolerance columns excluded |
| **chunk** | `(kindRoot, chunkIx)` + fingerprint `(FirstPk, LastPk, RawCount)` (`CaptureJournal.fs:66-73`) | one 50k-row chunk through one staged MERGE / bulk batch (`TransferRun.fs:169`) | `RawCount`; rows/sec via probe pairs | `CaptureJournal` NDJSON, append-atomic (`CaptureJournal.fs:81`) | fingerprint equality; mismatch = `transfer.resume.sourceDrift` refusal |
| **kind** | `SsKey`, persisted as `V2.SsKey` at table grain only (`ReadSide.fs:992`) — S1's identity-quality gradient, per grain | the per-kind load / per-kind DDL slice | per-kind capture counts; per-kind Bench labels | refactorlog entries on the rename channel, deduped by UUIDv5 `OperationKey` (`RefactorLogEmitter.fs:135-149`) | per-kind facet comparison + rowset SHA-256 hashes (`PhysicalSchema`) |
| **catalog** | the `SsKey`-keyed aggregate | `CatalogDiff = between A B` (`CatalogDiff.fs:654`) | `CatalogDiff.norm = Σ channelCounts` (`:967`) | becomes the episode's payload — its ledger is the grain above | `π : Intent → Quotient` (`PhysicalSchema.ofCatalog`); the named `Tolerance` set |
| **episode** | `EpisodeCoordinate = Version × Environment × At` (`Episode.fs:11-16`) | the episode edge; `ChangeManifest` (move counts, ‖δ‖, refactorlog xref, CDC series) | ‖δ‖ per edge; `pathLength` vs net displacement (`ChangeManifest.fs:97-98`) | `LifecycleStore` — monotone append, verified-only admission (`Episode.fs:180-182`; `MigrationRun.fs:271+`) | Verify: B′ reproduces B (`isSchemaEqual`); FTC replay equality |
| **run** | **missing as a value** (§3.5 — the holonic gap) | before/after delta on KeyLabels | per-label Δms / Δcount | `BenchSink` + `RunLedger` + `LogSink` + journal refs — four sinks, fragmented | run-to-run jitter threshold (~2×; the 15% noisy-promotion trigger) and the μ+σ gate — the run grain's *named tolerances* (`PERF_HARNESS.md` §3.5; `scripts/perf-gate.sh`) |

The execution tower instantiates the same five aspects with `Bench` as the meter: a sample has
a label (identity), a duration (norm), an accumulator (ledger), and a baseline with a σ-floor
(comparison) — and its one missing aspect is the typed stage boundary (R2), exactly as the
state tower's one missing row is the Run value (R1). The two recommendations are not additions
to the tower; they are its completion.

### 4.2 The holon laws — aggregation commutes with the aspects

The falsifiable content of the map: moving up one grain by aggregation must commute with each
aspect. Six instances, each either witnessed or named open:

1. **Norm, row → kind.** Σ per-row captures = the kind's capture count — witnessed at *exact
   equality* (`ReverseLegCdcNormTests`: 3 kinds × 4 rows = 12 captures; per-operation weights
   pinned by `CdcMeasureTests`).
2. **Norm, channel → catalog.** `‖δ‖ = Σ_c ‖π_c(δ)‖` (T14; the A38 disjoint-partition tests).
3. **Count, chunk → kind.** Σ journaled `RawCount` = the kind's row total — witnessed only
   *indirectly* (the streaming ≡ materialized equivalence test); no direct ledger-sum
   assertion exists. A named gap, cheap to close in the harness.
4. **State, episode → timeline.** `fold ⊕ = between genesis latest` (T13's functor law;
   `LifecycleTests` + `LifecycleStoreTests`, over a disk-loaded chain).
5. **Time, stage → run.** `Σ wall(stage) + ε = wall(run)` — not yet structural (stages are
   display strings, `OperatorConsole.fs:91-100`); this is R2, the execution tower's one open law.
6. **Path vs net, across episodes.** `Σ‖δᵢ‖ ≥ ‖Σδᵢ‖`, slack = churn — witnessed live
   (`ChangeManifestTests.fs:136`).

### 4.3 The deliberate breaks in self-similarity

A holonic map that claimed perfect self-similarity would be decoration. The tower breaks in
four places, and each break is load-bearing:

- **The row grain refuses the SsKey.** Identity at row grain is the reconciled business key;
  the one place rows *do* carry SsKeys (in-flight, `READSIDE_ROW` synthesis) is the measured
  bottleneck §3.3 corrects. The break is correct; the violation of the break is the bug.
- **The row-grain ledger is not the engine's.** CDC is substrate-kept; the engine reads the
  meter rather than keeping the books — the delta-representation asymmetry
  (`WAVE_6_ALGEBRA.md` §12.4) reappearing as a ledger-ownership fact.
- **The chunk grain exists only on the streaming realization.** The materialized path's G10
  marker is a degenerate one-row ledger (run-level, not chunk-level); R3's contract
  unification restores self-similarity rather than inventing it.
- **The run grain's identity and ledger cells are empty or fragmented.** The holonic map makes
  R1 legible as the completion of the tower, not a new idea bolted onto it.

---

## 5 — The calculus of the constellation — composition, derivatives, integrals

The corpus already has a field theory in miniature: `WAVE_6_ALGEBRA.md` §12.1 places every
concern κ at coordinates (emission, episode), with `∂κ/∂emission` integrating to the manifest,
`∂κ/∂episode` integrating to the provenance store, and the mixed partial realized as the
change-manifest series. The holonic map exposes the axis that framework left implicit:
**grain**. The field is κ(emission, episode, grain); the derivative along the grain axis is
refinement (a kind-load differentiates into chunks, a chunk into row-moves); its integral is
aggregation; and §4.2's holon laws are the field's **grain-covariance** — differentiate then
aggregate equals aggregate then differentiate, witnessed wherever the laws are green. Three
axes, three integrals: the manifest (over emission), the ledger (over time), the rollup (over
grain).

### 5.1 The fundamental theorem, per grain

The FTC — *the fold of the displacement quanta recovers the state* — is not one theorem here
but a family, one instance per grain, each running on different machinery:

| Grain | The integral | The machinery |
|---|---|---|
| row | the target table = the source applied through the diff; the predicate computes dδ pointwise and **SQL Server performs the integration at apply** | `changeDetectionPredicate` (`ScriptDomBuild.fs:810`), wired into `WHEN MATCHED` at `:880` — the substrate-fused integral of §12.4 |
| chunk | the loaded kind = the fold of journaled chunks; *resume* = continue the integral from the last verified partial sum; *drift* = the integrand changed under the fold (W3 enforced at runtime) | `writePlanStreaming` + `CaptureJournal` |
| episode | `reconstructLatest = genesis ⊕ Σδ` over a disk-loaded chain | T13; `EpisodicLifecycle` + `LifecycleStore` |
| run | before/after is the **difference quotient** at the observability plane; the antiderivative — the Run value — does not yet exist | the harness's `diff` (`PERF_HARNESS.md` §3.4); R1 supplies the antiderivative |

### 5.2 The operator algebra

The stars, typed as operators; the system's behaviors are their compositions. Signatures are
the code's, not proposals:

```
⊖   between    : State × State → Delta                      CatalogDiff.fs:654
⊕   applyDiff  : State × Delta → State
+   compose    : Delta × Delta ⇀ Delta                      partial — defined iff endpoints meet (:986)
π_c            : Delta → Delta                              channel projection; π_c ∘ π_c' = 0 (T14)
‖·‖ norm       : Delta → ℕ                                  (:967)
π_Q            : Intent → Quotient                          PhysicalSchema.ofCatalog — total, not faithful
emit           : Delta ⇀ Script                             partial — S5 (the named refusal) IS dom(emit)'s complement, enumerated
run            : Script × Substrate → Substrate
probe          : Stream α → Stream α                        identity on the value plane; effect on the meter plane
append         : Ledger × Quantum → Ledger                  monotone monoid action (Episode.fs:180; CaptureJournal.fs:81)
fold           : Ledger → State                             the integral (§5.1)
```

Three identities are the calculus's content:

1. **Equivariance (T16, restated).** `realize(A ⊕ δ) = run(emit δ, realize A)` modulo the named
   residual: `realize` intertwines the model-plane and substrate-plane torsor actions. The
   commuting square is precisely the statement that realization is an **equivariant map**
   between two spaces acted on by displacement, with `emit` the (partial) homomorphism between
   the displacement structures — isometric exactly where T15 holds.
2. **The measurement homomorphism.** `‖·‖ : (Delta, +) → (ℕ, +)` is additive across orthogonal
   channels (T14) and subadditive in general (the triangle inequality, with churn as the slack).
   The same shape recurs on the time plane: `Bench` is the run-grain instance into (ms, +), and
   stage-additivity (R2) is its exactness condition. One measurement law; two rulers (S6);
   stated once.
3. **Non-perturbing observation, with priced back-action.** `probe` (`Bench.streamProbe`,
   `AsyncStream.probe`) is the identity on the value channel — elements pass through unchanged
   — and effects only the meter. Where the meter's own cost is non-negligible the corpus
   already prices it: the harness mandates measuring a per-row scope's overhead "before
   trusting fine deltas" (`PERF_HARNESS.md` §3.6, label 1). Back-action is named, measured,
   and subtractable — never assumed zero.

One closing observation on idiom: these operators already compose in the house style — Kleisli
`>=>` for passes, `|>` pipelines for realizations, folds for ledgers. The calculus is not a
proposed abstraction layer; it is the type signatures the code already has, read as one
grammar. The discipline of §2 P2 still governs it: none of these operators earns a typeclass.
They earn laws with witnesses.

---

## 6 — The conceptual thermodynamics — conservation, dissipation, refusal

This section treats the thermodynamic reading as subject material under the document's own
discipline: each correspondence is cashed to an equation or witness already in the corpus, and
the final paragraph states where the analogy must stop. Nothing here introduces a mechanism;
the reading earns its place because it *predicts the architecture's actual choices* — the
fallback hierarchy, the placement of gates before mutations, the eject's design fork — rather
than decorating them.

### 6.1 The correspondence table

| Concept | The engine's realization | Equation / witness | Where the analogy ends |
|---|---|---|---|
| **Conserved charge** | Identity (`SsKey`) under every move; creation and annihilation only at Add/Remove | A43; Rename perturbs Designation while conserving Identity; `‖rename‖_data = 0` | conservation is postulated and witnessed, not derived from a symmetry principle — though `Realization := Designation` is, precisely, a gauge choice: the policy selecting the coordinate system in which the charge is expressed (`WAVE_6_ALGEBRA.md` §1, the disposition note) |
| **State function vs path function** | net displacement `B ⊖ A` is path-independent (W2/Chasles); accumulated work `Σ‖δᵢ‖` is path-dependent | the codebase wrote the sentence itself: `pathLength` is "churn — work done that did not move the net position" (`ChangeManifest.fs:97`); live witness `ChangeManifestTests.fs:136` | — |
| **Minimal / reversible work** | isometric emission `‖emit(δ)‖ = ‖δ‖`; the change-detecting MERGE is the quasi-static realization — exactly the work the displacement requires | T15; CDC-norm exact equality (`ReverseLegCdcNormTests`) | — |
| **Dissipation** | norm inflation `‖emit(δ)‖ − ‖δ‖ ≥ 0`: complete-replace dissipates `2·\|table\| − ‖δ‖` — "norm-inflating — the precise algebraic reason it is the fallback, not the default"; across time, churn = `Σ‖δᵢ‖ − ‖Σδᵢ‖` (triangle slack) | T15's complete-replace clause; T14 equality-iff-orthogonal; the churn witness | dissipation is **counted, not weighted** — no temperature, no free-energy functional; two equal counts cost the same regardless of what they touch |
| **Equilibrium / zero net flux** | the idempotent redeploy (P-5): `A ⊖ A = 0` ⇒ zero ALTERs, zero captures; drift = measured departure, `‖deployed ⊖ expected‖` per channel (P-8) | W1; the CDC-silence property — the one place the proof is the *absence* of signal ("the log's absence is the proof") | — |
| **Irreversibility, priced at consent** | the abstract groupoid is fully reversible (every generator invertible); irreversibility enters only at realization — Remove/Delete annihilate the charge. The engine's posture is the thermodynamic one: the irreversible act is declared *before* it is performed (Declare(-loss), refuse-unless-declared), priced at consent time, never discovered at recovery time. Reversible moves are cheap by theorem (`sp_rename`'s data-norm is zero) | the emission-asymmetry note (`WAVE_6_ALGEBRA.md` §3); the declared-loss gates; A43's corollary | reversibility lives in the abstract groupoid only; the realization layer's partiality is the ground truth the gates defend |
| **The monotone record** (a second-law analog about *information*, not disorder) | every provenance ledger only grows: the refactorlog never deletes (fresh-deploy replay needs full history); `EpisodicLifecycle.append` "fails rather than silently reordering" (`Episode.fs:180-182`); journals append-only. The arrow of time is structural | P-PROV; `NonMonotonic` (`MigrationRun.fs:90`); `CaptureJournal.append` | the quantity that is monotone is the record's information about the past, not an entropy over states. The eject's named fork — append-forever vs collapsible-at-freeze (P-7) — is exactly the question of **terminal coarse-graining**: may the microhistory be discarded once no consumer will ever publish against an intermediate state? The corpus's discriminating condition is the correct one |
| **The meter and its back-action** | `Bench`/`probe` as the calorimetry of computation; CDC as the write-work meter — two conjugate rulers over one economy | S6; the harness's instruction to measure the probe's own overhead first (`PERF_HARNESS.md` §3.6) | no uncertainty principle: back-action here is additive overhead, measurable and subtractable |
| **Sorting without unaccounted loss** | the intent filter: every observed difference lands in `intended ⊕ tolerated`, nothing unclassified (P-IF); a single unnamed leak immediately manifests as spurious work (a spurious ALTER on idempotent redeploy) | the residual of T16 (`WAVE_6_ALGEBRA.md` §8); P-5's stakes | the sorting is not free — its completeness is a *tested property* paid for in verification work, which is the honest cost of the demonless filter |

### 6.2 What the architecture achieves, in this reading

It converts schema-and-data change from an unmetered phenomenon into a **metered economy**.
Every quantum of change is counted at the boundary it crosses — the first-law discipline P1
already names. Waste is a defined, measured quantity — norm inflation within a run, churn
across runs — rather than an intuition. Equilibrium is a testable state whose witness is
silence. Irreversible acts are priced before they are performed. And the record of the past
grows monotonically, giving the system a structural arrow of time.

Under this lens the isomorphism ladder acquires a thermodynamic statement: **L1** says a meter
exists; **L2** — no silent erasure — says *no flow bypasses a meter*; **L3** — the composed
`migrate` — says the entire economy runs through one circuit whose books balance end to end
(T16). And the recommendation's two structural commitments stop being observability
conveniences: R2 meters the engine's own time (stages as the metered segments), R1 closes the
engine's own runs into comparable budgets. They are the completion of the thermodynamic
program — the engine, having metered the estate's change, finishes by metering itself.

### 6.3 Where the analogy must stop

The lens is a reading of existing equations, never a generator of new mechanisms. It fails —
and should be abandoned at that point — the day a design argument invokes a thermodynamic
concept with no corresponding equation in the engine. Three named non-extensions: dissipation
is unweighted (no cost functional over tables — a "temperature" knob has no referent here);
there is no statistical mechanics beneath the counts (CDC rows are exact, not sampled; the
norm is a census, not an expectation); and no entropy *quantity* over states exists or should
be invented — the monotone thing is the record, not the estate. The counterexample condition
for this entire section: any row of §6.1 that cannot be traced to its cited equation or
witness is decoration, and a future agent should cut it — per the `NORTH_STAR.md` discipline
this document inherits.

---

## 7 — The streaming hypothesis, adjudicated

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

## 8 — The recommendation

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

## 9 — The reification — the future codebase, in F#

The recommendation of §8 names five commitments; this section shows them as code. The register:
**signatures are commitments, bodies are sketches.** Every construct below extends a named
precedent already in the codebase, in the house style (private smart constructors,
`[<RequireQualifiedAccess>]` modules, `Result` at boundaries, Core synchronous, effects at the
edges); every F# feature promoted here cites the `CLAUDE.md` feature-surface row whose trigger
it fires — the corpus's own protocol for adopting language features. Acceptance witnesses are
given as house-style backticked test names, because in this codebase a design is not a diagram;
it is the set of laws its tests will cite.

### 9.1 The proof-token idiom — the one move everything else uses

The codebase already has its deepest pattern in `ArtifactByKind` (`ArtifactByKind.fs:59-82`): a
**private single-case DU whose smart constructor is the law** — a value of the type cannot
exist unless the law held at construction. T11 is not tested there; it is unforgeable. The
reification generalizes this move to two new tokens:

```fsharp
/// A ledger entry that passed its grain's admission check. Minted ONLY by
/// the grain's verifier — the private constructor IS the admission law.
/// Chunk grain: minted on source-fingerprint match (the CaptureJournal
/// check). Episode grain: minted on B' ≡ B (MigrationRun.recordVerified,
/// which today enforces this by convention at one call site).
type Verified<'entry> = private Verified of 'entry

/// A group whose members may execute concurrently. Minted ONLY by the
/// topological pass. DataEmissionComposer.fs:338's docstring obligation —
/// "callers MUST deploy Phase1Levels in order; within a single level…" —
/// becomes a constructor: the MUST dies, the type lives.
type ParallelSafe<'a> = private ParallelSafe of 'a list
```

The immediate payoff is R5: `Deploy.executeBatchParallel` — shipped, proven, and unwired since
perf-sweep-5 (`Deploy.fs:413-498`, "the canary continues using sequential executeBatch until
the composer exposes safe groups") — re-signs as

```fsharp
val executeBatchParallel : SqlConnection -> ParallelSafe<Segment> -> Task<unit>
```

and becomes impossible to miswire: the only producer of its argument is the pass that proves
the safety. Witnesses: `` `R5: leveled deploy ≡ sequential deploy on PhysicalSchema` `` (the
equivalence canary, per the capability-descent rule that every new rung carries one) and the
convention witness that only `TopologicalOrder.levels` mints the token (Bucket B — the
structural kind `AxiomTests.fs` already catalogues).

### 9.2 The ledger contract (R3) — one algebra, the journal and the store as instances

Per no-I/O-in-Core, the contract splits exactly where `Episode` (Core) and `LifecycleStore`
(Pipeline) already split: **Core owns the pure chain algebra; the boundary owns append.**

```fsharp
/// Core — the partial-sum ledger, pure. The journal (chunk grain) and the
/// lifecycle store (episode grain) become its two instances; the harness's
/// before/after captures its third. Record-of-functions, not an interface —
/// the house prefers data over dispatch (object expressions: deferred).
type LedgerSpec<'state, 'quantum, 'fp when 'fp : equality> =
    { Genesis       : 'state
      Apply         : 'state -> 'quantum -> 'state      // ⊕ at this grain
      FingerprintOf : 'quantum -> 'fp }                 // what admission recomputes

type LedgerEntry<'quantum, 'fp> =
    { Position : int; Fingerprint : 'fp; Quantum : 'quantum }

[<RequireQualifiedAccess>]
module Ledger =
    /// The FTC at this grain (§5.1): fold ⊕ over verified entries.
    val replay :
        LedgerSpec<'s,'q,'fp> -> Verified<LedgerEntry<'q,'fp>> list -> 's
    /// Resume = the first position absent from the chain. Drift = a recorded
    /// fingerprint disagreeing with recomputation — a NAMED refusal
    /// (transfer.resume.sourceDrift's shape, generalized), never a silent re-run.
    val resumePoint :
        recorded: LedgerEntry<'q,'fp> list -> recompute: (int -> 'fp)
            -> Result<int, LedgerDrift>
```

The instances, with what each fingerprints:

| Instance | 'state | 'quantum | 'fp | Admission (mints `Verified`) |
|---|---|---|---|---|
| `CaptureJournal` | packed remap + written totals | `ChunkRecord` (`CaptureJournal.fs:18-27`) | `FirstPk × LastPk × RawCount` | source-slice fingerprint match |
| `LifecycleStore` | `Catalog` | the episode edge (`CatalogDiff` + manifest) | the monotone `EpisodeCoordinate` | `recordVerified`: B′ reproduces B |
| harness captures | the baseline | one `Bench.Run` (`Bench.fs:355`) | the `RunInputs` digest (§9.4) | scenario determinism check |

The G10 progress table re-reads as the degenerate single-entry instance (one quantum: "the
whole run"), which is why R3 retires it onto the contract rather than maintaining a second
mechanism. Witnesses: `` `R3: replay = fold ⊕ — the FTC at every instance` `` (one FsCheck
property, three instantiations, per the constructed-valid-generator discipline);
`` `R3: crash at chunk k resumes at k; fingerprint drift refuses by name` ``; and
`` `R3: Σ RawCount over the chain = the kind's row total` `` — which closes §4.2's holon-law-3
gap as a by-product of the contract existing.

### 9.3 The stage spine (R2) — the writer-fidelity graduation, applied to time

Stage identity today is a string-prefix convention over event codes (`Watch.fs:48` keys the
board on "the prefix of the `<stage>.started` / `summary.stageCompleted{stage}` codes") and a
display list (`OperatorConsole.fs:91-100`). The future state types the spine and brackets every
stage structurally — the exact graduation the `lineageDiagnostics` CE performed on 2026-05-22,
when manual record-building went from forbidden-by-discipline to impossible-by-syntax:

```fsharp
type StageName = private StageName of string            // smart ctor: non-blank, dot-free
type RunSpine  = private { Declared : StageName list }  // distinct, non-empty;
                                                        // Watch pre-seeds Pending from it
type Stage<'a> = private Stage of StageName * (StageContext -> Task<'a>)

/// Member set mirrors LineageDiagnosticsBuilder (Diagnostics.fs:478-500).
/// Bind IS the boundary crossing, and the crossing is counted (P1):
///   Bench.scope on the stage name        — structural, not by discipline
///   Envelope <stage>.started/.completed  — LogSink.fs:129; Watch transitions
///   Pending → Active → Done              — Watch.fs:43-46, fed not inferred
/// Run asserts executed ⇔ declared — a missed or extra stage is a named
/// refusal at run end, not a render glitch.
type StagedBuilder(spine: RunSpine) =
    member _.Bind  : Stage<'a> * ('a -> Stage<'b>) -> Stage<'b>
    member _.Return: 'a -> Stage<'a>
    member _.Run   : Stage<'a> -> (RunContext -> Task<StagedOutcome<'a>>)
```

Inside the CE, **unmetered work is syntactically impossible** — there is no way to compute
between stages without being inside one. Witnesses:
`` `R2: declared ⇔ executed — the spine's bidirectional totality` `` (the registry pattern's
fifth instance, after registered⇔executed, code⇔copy, expressible⇔reachable, and the codec);
`` `R2: wall(run) − Σ wall(stage) ≤ ε — T14 on the time plane` ``; and the structural
retirement of the lazy-probe trap — a `streamProbe` label now attributes to the stage whose
bracket encloses its enumeration.

### 9.4 The Run value (R1) — the fifth aggregate, content-addressed

Two feature promotions land here, each by the corpus's own trigger:

```fsharp
[<Measure>] type ms
[<Measure>] type captures
// CLAUDE.md's units-of-measure row defers UoM until "a strategy starts
// mixing percentile and count values in the same expression" (H-013:
// trigger unfired). Run.diff is that expression: Δtime and Δcount flow
// through one delta surface. The trigger fires HERE — and the scope stays
// here: Bench's hot accumulator remains raw int64; only the comparison
// surface is dimensioned.

type RunId = private RunId of string
[<RequireQualifiedAccess>]
module RunId =
    /// Content-addressed: SHA-256 over the input digests — the
    /// CaptureJournal naming move (CaptureJournal.fs:48-52) lifted to run
    /// grain. The wall clock moves INTO the value and OUT of the address
    /// (BenchSink's reified non-determinism boundary, relocated per §3.5).
    val ofInputs : RunInputs -> RunId

type RunInputs =
    { CatalogDigest  : string                 // SHA-256 of CatalogCodec.serialize
      PolicyVersion  : string
      RegistryDigest : string                 // ManifestEmitter.fs:549's digest, reused
      Spine          : StageName list
      Scale          : ScaleKnob option }

type Run =
    { Id            : RunId
      Inputs        : RunInputs
      Events        : Envelope list           // LogSink.fs:129-141 — Envelope.RunId
                                              // already exists; the aggregate it
                                              // foreign-keys finally does too
      Bench         : Bench.Run               // Bench.fs:355
      Ledgers       : LedgerRef list          // journal files; episode coordinates
      Verdict       : Verdict
      CapturedAtUtc : DateTimeOffset }

[<RequireQualifiedAccess>]
module Run =
    /// Total / deterministic / re-validating — the CatalogCodec discipline,
    /// with the round-trip law over a constructed-valid generator.
    val serialize   : Run -> string
    val deserialize : string -> Result<Run, ValidationError list>
    /// ⊖ at the observability plane (§5.1's missing antiderivative): runs
    /// are points; before/after is a displacement; the norm is per-label.
    val diff : before: Run -> after: Run -> RunDelta

type LabelDelta = { Label : string; DMean : decimal<ms>; DCount : int }
```

The consumers collapse onto one value: Watch renders it live; `inspect <runId>` (D5) renders it
post hoc; the perf gate becomes `Run.diff baseline current |> PerfGate.judge` (the μ+σ model
unchanged — its *input* becomes a value instead of a filename glob); the harness's
`capture before/after` becomes `Run.diff` restricted to KeyLabels; the R6 N=10 consecutive-green
gauge is a fold over the run ledger. Witness:
`` `R1: project (record run) ≡ live view — no rendering shows a fact the Run lacks` ``.

### 9.5 The row quantum (R4) — the quotient cleavage, applied to data in flight

```fsharp
/// Kind-grain header: the typed vocabulary, established ONCE per stream —
/// the Intent/Quotient discipline (S3) applied to the data plane.
type RowBasis = private { Kind : SsKey; Columns : ColumnName[]; Ordinals : Map<Name, int> }
[<RequireQualifiedAccess>]
module RowBasis =
    val ofKind : Kind -> RowBasis                          // basis order = attribute order
    val view   : RowBasis -> RowQuantum -> Name -> string  // typed access, preserved

/// Row-grain carrier: positional against the basis. [<Struct>] promotion:
/// CLAUDE.md's own trigger — "when profiling shows allocation pressure on
/// a hot pass" — is fired by the 2026-06-11 priors (carrier build
/// ~1.85 µs/row against a ~1.9 µs/row wire floor; PERF_HARNESS.md §5).
/// A single-field struct over string[] copies one reference: no
/// large-struct copy hazard. RawValueCodec contract unchanged (DBNull = "").
[<Struct>]
type RowQuantum = { Cells : string[] }
```

Streams become `AsyncStream<RowQuantum>` *with the basis carried beside the stream as a pair* —
the element type changes; the three-function `AsyncStream` surface does not (§11's first
refusal holds). Consumer migration: `SurrogateRemap` resolves FK ordinals once per kind through
the basis, then indexes; `PhysicalSchema` row-hashing iterates `Cells` in basis order instead
of walking a per-row `Map`; `StaticRow` remains the IR-grain carrier for static populations —
the quantum is in-flight only. Identity at row grain is the PK cell read through the basis: the
synthesized `READSIDE_ROW` SsKey and its measured per-row basis `sprintf`
(`ReadSide.fs:921-922`, ~0.39 µs/row) are **deleted, not optimized**. Witness:
`` `R4: ofQuantum basis ∘ toQuantum basis = id on StaticRow` ``, gated on harness slice 2 per
§8's counterexample condition.

### 9.6 The composition — one run face, end to end

The future-state migrate-with-data face, every line annotated with the star or recommendation
it realizes — and not one line a new mechanism:

```fsharp
let runMigrateExecute (req: MigrateRequest) : Task<Run> =
    staged Spines.migrateData {                                     // R2 — declared ⇔ executed; metered by syntax
        let! deployed = stage Stages.snapshot (fun _ ->
                            ReadSide.read req.Source)               // S3 — Ingest into the quotient's domain
        let! plan     = stage Stages.diff (fun _ ->
                            Migration.plan deployed req.Target      // S2 — emit(B ⊖ A), the torsor leg
                            |> Gate.declaredLoss req.Consent)       // S5 — irreversibility priced at consent (§6.1)
        let! _        = stage Stages.apply (fun ctx ->
                            Ledger.resumePoint ctx.Journal          // R3/S8 — continue the fold from the last
                            |> Transfer.streamQuanta req.Basis      //          verified partial sum
                                                                    // R4/S7 — RowQuantum flow on AsyncStream
                            |> Wavefront.over plan.Levels
                                 (Deploy.executeLeveled ctx.Sink))  // R5 — ParallelSafe minted by the pass alone
        let! verdict  = stage Stages.verify (fun _ ->
                            Canary.verify deployed req.Target)      // S4 — Ingest ∘ Project = id, mod tolerances
        return verdict                                              // S6 — every bracket above fed two rulers
    }
    |> Run.record req.Inputs                                        // R1 — content-addressed; verified-only admission

// The optimizer's loop, now torsor-shaped at the run grain:
//   Run.diff before after |> PerfGate.judge        — ⊖ on the observability plane
```

### 9.7 The feature-promotion ledger

Each promotion follows the corpus's protocol: the feature-surface row, the trigger, the scope.

| F# construct | Status in `CLAUDE.md`'s feature surface | What fires it | Scope it lands in |
|---|---|---|---|
| CE builder for the spine | precedented — the lineage/diagnostics builders are canonical | R2: metering graduates from discipline to syntax | `StagedBuilder`, Pipeline/CLI |
| private-DU proof tokens | precedented — `ArtifactByKind` is the worked example | R3 admission; R5 parallel safety | `Verified<_>`, `ParallelSafe<_>` |
| `[<Struct>]` records | consciously deferred; trigger = "allocation pressure on a hot pass" | trigger **fired** by the measured row-carrier priors | `RowQuantum` only |
| units of measure | deferred at H-013; trigger = mixed quantities in one expression | trigger **fires** at `Run.diff` (ms × counts, one surface) | the delta surface only; hot accumulators stay raw |
| `ReadOnlySpan<byte>` / `ValueTask` | already precedented (`PhysicalSchema.fs:345`; `Retry.fs:110`) | — | hashing/codec/retry boundaries, unchanged |
| record-of-functions contracts | house-preferred over interfaces and object expressions | R3's two existing consumers + the harness third | `LedgerSpec`, Core |

What stays refused, in one line each: SRTP/typeclass-style generalization (arcane here; the
house proves laws with witnesses, not constraints); free-monad scheduling (H-063 — still
consumer-less); `IObservable` (the `LogSink.addSubscriber` push model suffices); a stream
wrapper type (§11's first refusal — R4 changes the *element*, never the surface).

The dream is disciplined: every construct in this section is one of the codebase's own moves
applied at one more grain. Counterexample condition for the whole section: any sketch whose
precedent column would be empty is speculation, and must wait for its trigger — per §2 P2,
which the reification does not get to suspend.

---

## 10 — The staged migration path

Each stage is independently shippable, ordered so that measurement precedes mechanism. Stages
0–1 are already-resolved backlog; the constellation adds 2–5, and §9 supplies stages 2–5 their
target signatures.

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

## 11 — What not to build

Directions considered against the constellation and rejected, with the discriminating reason.
Several have already been rejected once by this codebase; they are listed so the temptation is
recognized when it recurs.

1. **A core `Stream<'a>` model / Rx / `IObservable` / actor runtime.** Rejected by §7's four
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

## 12 — The epistemic ledger of this document

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

**Amendment claims (same-day second pass, §§4–6).** The holon laws of §4.2 are classified at
entry: laws 1, 2, 4, and 6 carry live witnesses (cited); law 3 (chunk → kind ledger-sum) is
witnessed only indirectly through the streaming ≡ materialized equivalence and is a named gap;
law 5 (stage additivity) is unbuilt by definition — it *is* R2. The calculus of §5 introduces
no new operator: every signature in §5.2 is read from the code, and the three identities
restate T16, T14/T15, and the harness's own back-action note. The thermodynamic
correspondences of §6.1 are readings of existing equations — every row cites its witness; the
section carries its own counterexample condition (§6.3) and three named non-extensions. The
strongest single anchor found in the amendment's verification pass: the codebase independently
wrote the state-vs-path distinction in its own words — `ChangeManifest.fs:97`, "churn — work
done that did not move the net position," with a green witness — so the thermodynamic reading
was latent in the corpus before this document named it.

**Amendment claims (third pass, §9 — the reification).** The code of §9 is signature-grade
commitment, sketch-grade body. Every named precedent was verified in source during this pass:
`Envelope` with its already-existing `RunId` field (`LogSink.fs:129-141`); `StageState` and the
string-prefix stage convention (`Watch.fs:43-48`); `ChunkRecord` (`CaptureJournal.fs:18-27`);
`Bench.Stats`/`Bench.Run` (`Bench.fs:90-101`, `:355-360`); the leveled-deploy docstring
obligation (`DataEmissionComposer.fs:334-359`); the `ReadOnlySpan`/`ValueTask` precedents
(`PhysicalSchema.fs:345`; `Retry.fs:110`); the CE builder member set (`Diagnostics.fs:275-500`).
The two feature promotions each cite the `CLAUDE.md` trigger they claim fires, and both claims
are falsifiable: the `[<Struct>]` promotion dies with R4's gate; the units-of-measure promotion
dies if `Run.diff` ships carrying a single quantity. No sketch introduces an operator absent
from §5.2.

The constellation stands or falls with these. If the harness refutes R4's premise, the row
carrier stays as it is and this document's §3.3 becomes a documented refuted candidate — which
is itself the system working as designed.

— Recorded for the receiving agent. The stream is what flows; the ledger is what is true; the
audit is a theorem. Hold the spine; balance the books.
