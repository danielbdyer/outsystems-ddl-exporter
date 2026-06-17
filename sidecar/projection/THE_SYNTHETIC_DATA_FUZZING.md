# THE_SYNTHETIC_DATA_FUZZING.md — high-fidelity, coverage-correcting, anonymizing synthesis governed by a blessed correction artifact

> **Status: DESIGN + IN BUILD (2026-06-16, operator co-design).** The spine is built — **F0a** (the
> blessed-correction Core substrate), **F0b** (the durable codec, round-trip law green), and **F1** (per-kind
> arbitrary-scale volume) landed this session (see §7 for the per-slice status + commits). The remaining
> slices (F0c operator surface, F2 Faker, F3 coverage, F4 boundary rotation, F5 evidence-wiring, F6 fitting)
> are designed, not built. This is the design surface for the *advanced* synthetic-data program the operator
> named: *"production-alike data at arbitrary sizes from
> advanced at-scale inferences, professional distribution-analysis quality, round-robin rotation in
> anonymizing ways, PII selection/fine-tuning + Faker assimilation for PII elements… extremely high
> quality synthetic data (as much quality as can be naturally derived from the quality of the source)
> from a massive production corpus."*
>
> **It builds on a BUILT floor.** `THE_SYNTHETIC_DATA_DESIGN.md` (BUILT 2026-06-08) is the v1 synthesis
> capability: the pure-Core σ (`SyntheticData.generate`), the durable `ProfileCodec`, the `projection
> profile` capture verb + `from: synthetic` flow, and the `π ∘ σ ≈ id` canary. This document is the v2
> program *layered on top of* that floor — it does not rebuild it. Read that doc first; it owns the
> built shapes this one extends.
>
> **Provenance.** Operator co-design this session: confirmed the two-plane framing (π/boundary inference +
> Faker; pure-Core σ; Faker seeded from Core's deterministic tokens) with two named adjustments — (1) the
> blessed *correction artifact* as a first-class durable surface, and (2) *coverage* as a named fidelity
> axis that deliberately departs from source fidelity where the source corpus is patchy. Sits beside
> `THE_SYNTHETIC_DATA_DESIGN.md` (the built floor), `THE_DATA_PRODUCERS.md` (the producer frame),
> `WAVE_6_ALGEBRA.md` (the torsor / faithfulness ladder), and `DECISIONS.md` (the durable-artifact +
> named-divergence precedents: RefactorLog, Tolerance, the golden blessing, A41 OperatorIntent).

---

## 0. The one idea — synthesis *modulo blessed corrections*

The engine's soul is one adjunction: **`Ingest ∘ Project = identity`, modulo *named, closed* erasures**
(`CLAUDE.md` §1). v1 synthesis already lives inside that frame: σ is the approximate right-inverse (the
*section*) of profiling, `π ∘ σ ≈ id_Profile` (within sampling ε) — `THE_SYNTHETIC_DATA_DESIGN.md` §1.

The v1 section is **faithful**: it reproduces the source's profile, skew and all. The operator's program
adds a deliberate, *governed* departure from faithfulness:

```
σ : Profile × Correction ⟶ Data       such that   π ∘ σ ≈ id_Profile   MODULO the blessed Correction
```

The **Correction** is a durable, operator-blessed artifact that records every intentional departure from
naive fidelity — coverage widening, PII replacement, anonymizing rotation, distribution overrides — each
**named** (a coded, reviewable entry), each **closed** (the artifact is the complete enumeration; nothing
diverges silently). That is *exactly* the core adjunction's "named, closed erasures," applied to the
synthesis section. The Correction is the synthesis-side sibling of the **RefactorLog** (durable
schema-intent) and the **Tolerance** registry (named, accepted diff divergences): a place where operator
judgment is recorded once, blessed, version-controlled, and replayed deterministically — *intent furnished
into the codebase*, not re-litigated per run.

**The correctness theorem becomes two-armed, and both are canaries:**
- **Fidelity (unchanged where uncorrected):** `π ∘ σ ≈ id` on every column/axis the Correction does not touch.
- **Correction (held where blessed):** every Correction entry's *intent* is met — a coverage floor is
  reached, a PII column emits no real value, a rotation breaks row-linkage — and the departure from fidelity
  is *exactly* the blessed set, never more.

---

## 1. The two planes (confirmed) + the artifact between them

The program spans two planes the engine already keeps apart; the Correction artifact is the **blessed hinge**
between them.

```
   π  (INFERENCE plane — the boundary)              CORRECTION (the blessed hinge)            σ  (SYNTHESIS plane — pure Core)
   ───────────────────────────────────             ──────────────────────────────           ───────────────────────────────
   LiveProfiler over the massive corpus    ──P──▶   capture → REVIEW → bless → replay  ──▶    SyntheticData.generate
   streaming SQL aggregation; float OK              durable, version-controlled,              pure; T1 byte-deterministic;
   distribution fitting; PII detection              operator-blessed override/intent          decimal-only; splitmix64
   (the corpus never leaves this plane)             (the named-divergence closure)            (consumes Profile ⊕ Correction)
                                                                                                       │
                                                                                              realized rows (raw form)
                                                                                                       │
                                                                            BOUNDARY realization: seeded-deterministic Faker
                                                                            assimilation over PII-typed / field-set columns
```

- **π — inference (boundary).** `LiveProfiler` (`src/Projection.Adapters.Sql/LiveProfiler.fs`, `attach`
  :1231) over the production corpus via streaming SQL. This plane may use `float`, heavy statistics, and
  SQL-side aggregation freely — it is an *adapter*, not Core. Its output is the bounded `Profile` summary;
  **the corpus never enters Core.** This is the home for *at-scale inference*, *professional distribution
  fitting*, and *PII detection*.
- **σ — synthesis (pure Core).** `SyntheticData.generate` (`src/Projection.Core/SyntheticData.fs`) is a
  **pure, T1-byte-deterministic replay** — no `float`, no `System.Random`, no clock (splitmix64 +
  `decimal`; design §4). It consumes `Profile ⊕ Correction` and samples deterministically. It never
  computes statistics and never calls Faker.
- **Correction — the blessed hinge.** A durable typed artifact (the new surface this program introduces)
  layered between π and σ. σ consumes the *corrected* evidence; the corpus and the heavy compute stay on
  the π side.
- **Faker realization (boundary).** σ emits deterministic placeholder *tokens* for PII / replaced
  field-sets (as it already does: `syn:<attrhash>:<bucket>`, `SyntheticData.fs` :270). A boundary pass maps
  each token → a realistic value via **Bogus seeded from the deterministic token**, so Faker never enters
  Core, `π∘σ≈id` and T1 still hold in Core, and the operator gets production-alike PII. (Confirmed design
  decision — see §5.)

---

## 2. The blessed correction artifact (the load-bearing new concept)

### 2.1 What it is

A durable, reviewable, version-controlled, operator-**blessed** record of every judgment call layered onto
the captured `Profile` to govern synthesis. It is to synthesis what the **RefactorLog** is to schema
evolution and what **Tolerance** is to diffing: the place intent is recorded once and replayed
deterministically. It *furnishes intent into the codebase* — an operator (or the next agent) reads it and
sees exactly why synthetic output departs from the raw source shape.

### 2.2 The capture → review → bless → replay loop

```
projection profile <env> --out legacy.profile.json            # π: capture the raw evidence (BUILT)
projection synth-correct legacy.profile.json --out corr.json  # propose a correction artifact from heuristics (NEW)
#   (operator REVIEWS + EDITS corr.json: PII typing, coverage floors, rotation, Faker field-sets)
#   (operator BLESSES it — the artifact becomes a trusted, version-controlled input)
flow: { from: synthetic, profile: file:legacy.profile.json, correction: file:corr.json, to: cloud-uat }   # σ replay (extend)
```

The artifact is **proposed by heuristics, perfected by the operator, then blessed** — mirroring the golden
blessing discipline (`GOLDEN_RECORD=1` + a DECISIONS note). A blessed artifact is a durable input; a re-run
consumes it verbatim, so judgment calls are never re-litigated.

### 2.3 The typed shape (sketch — IR grows under evidence; build per-axis as a consumer lands)

A closed set of **named correction entries**, each `[<RequireQualifiedAccess>]`, smart-constructed,
keyed by `SsKey` (never by name lookup) — so a correction is unforgeable and survives rename (drift-by-SsKey,
design §6). Each entry is a *named divergence* with its rationale (the RefactorLog/Tolerance discipline):

| Correction entry | What it overrides | Why (the named departure) |
|---|---|---|
| `PiiClass of SsKey × PiiKind` | the coarse hybrid-by-cardinality proxy (`SyntheticData.fs` :254) | explicit PII typing (`Email` / `PersonName` / `Phone` / `Address` / `FreeText` / `Reference` / `None`) drives realization |
| `CoverageFloor of SsKey × CoverageRule` | the source's frequency shape | "include all important values even if patchy/absent in source" — exhaustive permutation, variety injection, or a minimum distinct-count floor |
| `Fidelity of SsKey × ValueFidelityMode` | the per-column preserve/synthesize/**rotate** decision | operator override of the default fidelity mode (adds the new `Rotate` mode, §4) |
| `FakerFieldSet of SsKey list × FakerProfile` | several columns at once | coherent field-set replacement (e.g. `{First, Last, Email}` → one fake *person*, referentially consistent) |
| `Volume of SsKey × VolumeTarget` | the `Scale`-over-observed default (`SyntheticData.fs` :463) | absolute-N / total-corpus-size / multiplier targeting (§3) |
| `DistributionOverride of SsKey × ShapeHint` | the captured numeric/categorical shape | operator-supplied or fitted shape (parametric family, histogram) — §6 |

`SyntheticConfig` (the v1 inline config, `SyntheticData.fs` :62) becomes the *un-blessed default layer*;
the Correction artifact is the *blessed override layer* on top. `effectiveEvidence = Profile ⊕ Correction`
is a **pure** fold (the corpus stays on the π side), so σ's determinism is untouched.

### 2.4 Codec + canary discipline (inherited)

The Correction artifact gets the same treatment as `ProfileCodec` (`src/Projection.Targets.Json/ProfileCodec.fs`):
a **total, SsKey-stable, re-validating** durable codec with the universal round-trip law
`∀ c. deserialize (serialize c) = Ok c` (FsCheck over a constructed-valid generator + a real example). The
artifact is declarative, hand-editable JSON; never a hand-authored wire format in tests.

---

## 3. Coverage as a named fidelity axis (the quality gate)

### 3.1 The problem the operator named

The production source skews to *patchy representational coverage* — it over-represents some members and
under-represents or omits others. Naive σ faithfully reproduces this skew (`π∘σ≈id`), which is *wrong* for
fields where the operator needs the full space exercised (a test/preview that never sees a rare-but-valid
Status is a weak preview). **Coverage is a distinct quality from distributional fidelity**, and where they
conflict, the operator — via a blessed `CoverageFloor` — chooses coverage.

### 3.2 The faithfulness-ladder extension

v1 has L1 (loads / structural) / L2 (re-profiles to ≈P) / L3 (joint — out of v1). This program adds a
**coverage rung** between L2 and L3:

- **L1 — it loads.** Structural integrity exact (unchanged).
- **L2 — fidelity.** Per-column marginals reproduced within ε *where uncorrected*.
- **L2-cov — coverage.** Every blessed `CoverageFloor` is met: all enumerated values appear; the
  variety/permutation target is reached; the minimum distinct-count holds. **This is a deliberate, named
  departure from L2** — the Correction artifact records exactly where, so the divergence is closed, not silent.
- **L3 — joint structure.** Inter-column correlation (the `JointDistribution` axis already exists — §6).

### 3.3 The three coverage mechanisms (the operator's words, made precise)

- **Exhaustive permutations.** For a column (or a field-set) whose value-domain is known/important,
  *enumerate the full domain* (or the cross-product of a field-set) instead of frequency-sampling — every
  value/combination gets at least one row. Bounded by a named cap (a refusal when the cross-product
  explodes, never a silent truncation).
- **Additional variety.** Inject values *not present* in the source — from a declared domain or
  Faker-generated — to widen coverage past the patchy corpus. The privacy contract is *strengthened* by
  this (injected values are by construction not real source values).
- **Selective Faker replacement of field-sets.** Replace a whole field-set with coherent Faker variety
  (§5), breaking from the source's patchy real values entirely where the operator blesses it.

Each mechanism is a `CoverageRule` variant on a `CoverageFloor` correction entry; each ships with a
property-test asserting the floor is met *and* that uncorrected columns still satisfy L2.

---

## 4. Anonymizing rotation — a BOUNDARY operation, not a Core-σ mode (corrected 2026-06-16)

> **Correctness revision.** An earlier draft made anonymizing rotation a third `ValueFidelityMode` in
> pure-Core σ. That is a category error, and the reason is illuminating: **Core σ is marginal-only — it
> never sees a real source row.** It rebuilds each row *independently* from the captured `Profile`
> marginals, so the existing `Preserve` mode *already* emits real values with **no row-linkage to the
> source** (a synthesized row's combination of values is assembled from independent per-column draws, not
> copied from any real row). There is nothing in Core for a `Rotate` mode to anonymize that `Preserve`
> hasn't already broken.

**Where rotation genuinely lives: the boundary.** "Round-robin rotation in anonymizing ways" — *permute
the real corpus rows so each emitted row keeps a real, internally-coherent value-combination but no longer
belongs to its original subject* — operates over the **actual corpus rows**, which exist only on the
π/boundary plane (the same plane as Faker, §5). So rotation is a **boundary realization** over real rows
(a deterministic, seeded permutation that breaks the subject↔row linkage), governed by a blessed correction
entry (e.g. a `Rotate` / `Anonymize` `FakerFieldSet` sibling), **not** a Core `ValueFidelityMode`. Its
witness is a **linkage-breaking property** at the boundary: no emitted row reproduces a real subject's full
quasi-identifier tuple, while each column's marginal is exact (rotation is a permutation).

This is the same discipline as Faker assimilation: **the heavy, corpus-touching work stays at the boundary;
Core σ stays a pure marginal replay.** Core's contribution to anonymization is `Synthesize` (fresh tokens)
and `Preserve` (real values, already linkage-free by independent synthesis); the corpus-rotation channel is
boundary-only.

Stronger anonymization variants (k-anonymity over a quasi-identifier set; differential-privacy noise on
numeric columns) are named as **future boundary/evidence variants, deferred until the operator names the
threat model** — IR grows under evidence.

> **Open adjustment to confirm with the operator (privacy posture).** Whether the privacy bar is
> linkage-breaking corpus-rotation only, or extends to k-anonymity / DP-noise. The doc reserves the slots;
> the threat model decides which fire.

---

## 5. Faker assimilation — boundary, seeded-deterministic, referentially consistent

**The decision (confirmed framing).** Faker (Bogus, `Bogus` NuGet — the .NET Faker) uses an RNG and is
therefore **forbidden in Core** (T1). It lives at the **boundary realization** layer:

1. Core σ emits a **deterministic placeholder token** for every PII-typed / Faker-field-set column (it
   already emits exactly this shape for `Synthesize`: `syn:<attrhash>:<bucket>`, `SyntheticData.fs` :270).
2. A boundary pass (`Projection.Adapters.*` or the realization layer) maps each token → a realistic value
   via **Bogus seeded from the token** (`new Faker { Random = new Randomizer(seedFromToken) }`). Same token
   → same fake value → **determinism preserved**, so `π∘σ≈id` and T1 hold in Core, and the canary still passes.

**Referential consistency (the operator's "field-set" emphasis).** A `FakerFieldSet` correction binds
several columns to one fake *entity* (e.g. `{FirstName, LastName, Email}` → one coherent fake person; the
email derives from the name). The token therefore encodes the **entity identity** (the row's PK-derived
`SYNTH_ROW` SsKey, `SyntheticData.fs` :451), so the *same* fake person appears consistently wherever that
entity is referenced across tables — coherence across the FK graph, not per-column noise.

**Locale / format control** rides the `FakerProfile` on the correction entry (locale, format mask,
nullability already from the profile). All of it is boundary config; **Core stays pure.**

---

## 6. Corpus / at-scale handling (the selected adjustment)

### 6.1 π over a massive corpus (boundary; the existing EvidenceCache discipline)

The inference plane already follows *discover-once, derive-pure* (`LiveProfiler` → `EvidenceCache`,
CLAUDE.md §6). For a *massive* corpus this plane owns the at-scale concerns, all SQL-side / `float`-OK:

- **Streaming aggregation** — marginals, moments, and distinct-value frequencies computed by SQL
  `GROUP BY` / window aggregates, never by materializing the corpus.
- **Sampling strategy** — a named, blessed sampling rule (full-scan vs. TABLESAMPLE vs. top-N-by-frequency)
  when full aggregation is too costly; the sampling fact is *recorded in the Profile* (no silent
  approximation).
- **`IsTruncated` thresholds** — the categorical/joint/FK-selectivity axes already carry `IsTruncated`
  (`Profile.fs` :341/:872/:936); a capped vocabulary is *named* (truncation ⇒ synthesize, never reproduce
  an unseen tail). The cap becomes a blessed knob.
- **Professional distribution fitting** — histograms, fitted parametric families (normal / lognormal /
  Pareto / empirical-CDF), multimodality. `NumericDistribution` today carries percentiles + `Mean/StdDev/CV`
  (`Profile.fs` :434/:407); the program **enriches the evidence axis** with a fitted-shape carrier
  (`ShapeHint`) computed on the π side, consumed by σ's numeric sampler (`sampleNumeric`,
  `SyntheticData.fs` :298, today a percentile-segment interpolation).

### 6.2 σ extrapolation *beyond* the source size (pure Core)

"Arbitrary sizes" means generating **more rows than the corpus**. Volume targeting moves from `Scale ×
observed` (`SyntheticData.fs` :463) to a `VolumeTarget` correction: **absolute-N**, **total-corpus-size**,
or **multiplier**. Up-sizing past the observed distinct-value set forces the coverage question (§3): beyond
the observed support, σ either resamples the fitted shape (more of the same distribution) or widens via a
blessed `CoverageFloor` (variety injection / Faker). Each path is **named** in the Correction; never a
silent fabrication.

### 6.3 Already-captured-but-unconsumed evidence (cheap wins)

A grounding finding: **the profiler already captures more than σ consumes.** Two "advanced" capabilities
are *"teach σ to read evidence π already captures,"* not net-new inference:

- **`ForeignKeySelectivity`** (`Profile.fs` :866) — captured; σ ignores it and draws FK fan-out
  uniform-random (`SyntheticData.fs` :421). Wiring σ to it yields realistic skewed FK fan-out.
- **`JointDistribution`** (`Profile.fs` :927) — captured; σ ignores it (it samples columns independently).
  Wiring σ to it yields L3 correlated synthesis.

These are lower-cost than the framing first suggested and should sequence early among the σ-side work.

---

## 7. The slice plan (IR grows under evidence; each slice ships with its canary)

Sequenced so the **blessed-correction substrate lands first** (it is the spine every later slice hangs on),
then the operator-priority quality work. Each slice is independently shippable, green, and carries its
faithfulness-ladder witness.

| # | Slice | Plane | Ships | Status |
|---|---|---|---|---|
| **F0a** | **Correction Core substrate** | hinge | the `PiiKind` / `CorrectionEntry` closed DUs + smart-constructed `Correction` (conflict refusal) + the pure `Profile ⊕ Correction` fold onto `SyntheticConfig` (`SyntheticCorrection.fs`) | ✅ **landed** 2026-06-16 (`9e67158f`) |
| **F0b** | **Durable CorrectionCodec** | hinge | total / deterministic / re-validating `Correction ↔ JSON` (`CorrectionCodec.fs`); round-trip law + A39 decode refusal | ✅ **landed** 2026-06-16 (`d530badd`) |
| **F0c** | **Operator surface** | CLI / flow | `correction: file:<path>` flow wiring + the `synth-correct` propose verb (the A44 control-plane cascade) | ⬜ remaining |
| **F1** | **Explicit PII typing + per-kind volume** | σ + config | `Pii` correction ⇒ Synthesize (F0a fold); `Volume` correction + `VolumeTarget` (Absolute/Multiplier) consumed by `rowCountFor` — arbitrary scale | ✅ **landed** 2026-06-16 (`147421de`) |
| **F2** | **Faker assimilation (boundary)** | boundary | seeded-deterministic Bogus realization over PII-typed columns; `FakerFieldSet` referential consistency | ⬜ (F1 PII typing exists) |
| **F3** | **Coverage corrections** | σ | `CoverageFloor` (exhaustive permutation / variety injection / distinct-floor) + the **L2-cov** canary | ⬜ (an operator coverage need) |
| **F4** | **Anonymizing rotation** | **boundary** | corpus-row permutation (linkage-breaking) over real rows — **NOT** a Core `ValueFidelityMode` (§4 revision: `Preserve` is already linkage-free in marginal-only σ) | ⬜ (a named threat model) |
| **F5a** | **Wire σ to `ForeignKeySelectivity`** | σ | rank-mapped skewed FK fan-out (was uniform) | ✅ **landed** 2026-06-16 (`3f552f45`) |
| **F5b** | **Wire σ to `JointDistribution`** | σ | correlated FK-tuple synthesis (L3) — per-position rank-mapped, co-occurrence preserved on synthetic keys | ✅ **landed** 2026-06-16 |
| **F6** | **Distribution enrichment** | π + σ | `ShapeHint` evidence axis (histograms / fitted families / multimodality) at π + richer `sampleNumeric` | ⬜ (the "professional fitting" lift; largest) |

**Landed this session:** F0a + F0b + F1 + F0c-propose + F5a + F5b — the blessed-correction spine (carrier +
smart ctor + fold), its durable codec (round-trip law), the heuristic PII proposer, per-kind arbitrary-scale
volume, and σ wired to BOTH captured FK-fidelity axes (selectivity skew + joint correlation). The operator can
author + bless a correction artifact (programmatically / by file), have a first draft proposed, and drive PII
typing + arbitrary scale + skewed/correlated FK fan-out through σ.

**Recommended next:** **F2** (Faker, needs a Bogus NuGet dep) is the highest-visibility production-alike PII
win; **F0c-I/O** (durable write + `synth-correct` verb + `correction: file:` flow wiring — the A44 cascade) is
the operator-surface that makes the blessed loop end-to-end; **F3** (coverage corrections + L2-cov canary) is
the "ensure all values included" quality gate. **F6** (professional distribution fitting) is last — largest,
π + σ.

---

## 8. Disciplines to hold (do not break without writing the amendment first)

- **T1 byte-determinism in Core.** σ stays pure: no `float`, no `System.Random`, no clock. All heavy
  statistics and all Faker live on the **boundary**; Core consumes their *summaries* (Profile ⊕ Correction),
  which are `decimal`/typed. Faker is seeded from σ's deterministic token so realization stays reproducible.
- **Named, closed divergences.** Every departure from `π∘σ≈id` is a *blessed Correction entry* with a code
  + rationale (the RefactorLog/Tolerance/`SyntheticDiagnostic` precedent, `SyntheticData.fs` :90). The
  Correction artifact is the *complete* enumeration — coverage/PII/rotation never diverge silently.
- **The privacy contract holds and strengthens.** The `Synthesize` guarantee (no real high-card value
  emitted, true by construction via the per-attribute namespace, `SyntheticData.fs` :270) extends to PII
  typing, variety injection (injected ≠ real), and `Rotate` (linkage-breaking). Each carries a property test.
- **Two-armed canary as the forcing function.** Ship each slice with its faithfulness witness: fidelity
  (`π∘σ≈id` where uncorrected) *and* correction (the blessed intent met, and the divergence exactly the
  blessed set). Lives in the warm Docker pool beside `SyntheticCanaryTests`.
- **Codec discipline.** The Correction artifact round-trips under the universal law
  (`deserialize ∘ serialize = id`), FsCheck over a constructed-valid generator; declarative test inputs,
  never hand-authored wire.
- **IR grows under evidence.** Build F0 first, then per-slice against a real consumer/blessing. Do **not**
  add Profile axes, Correction variants, or knobs ahead of a consumer — the `JointDistribution`/`ForeignKeySelectivity`
  "captured but unconsumed" gap is the cautionary precedent (evidence outran its consumer).
- **Domain-first naming (pillar 8).** Concept-shaped names — `Correction`, `CoverageFloor`, `PiiClass`,
  `FakerProfile`, `ValueFidelityMode.Rotate` — not `Helper`/`Manager`/`Processor`. The vocabulary must
  mirror the operator's: *correction*, *coverage*, *blessing*, *rotation*, *assimilation*.
- **Test pools never concurrent** (`scripts/test.sh fast` vs. the warm Docker canary; no-swap host OOM);
  commit each slice green.

---

## 9. Why this matters (the operator's outcome)

It turns synthetic data from a *faithful but skew-inheriting* preview into a **governed, high-quality
corpus the operator authors intent over**: production-alike PII (Faker, coherent across the FK graph),
*full* coverage of important value-spaces even where the source is patchy (the quality gate the source
cannot supply itself), anonymization that keeps marginals exact while breaking linkage, and arbitrary
scale — all reproducible (seeded, pure), all *blessed* (the judgment calls persist, version-controlled,
re-litigation-free), and all *named* (every departure from the source is a closed, reviewable Correction
entry). It is "extremely high quality synthetic data, as much as the source quality allows — and, where
the source quality is lacking, exactly the corrections you blessed to make up the difference" — made an
engine capability with a two-armed fidelity theorem behind it.
