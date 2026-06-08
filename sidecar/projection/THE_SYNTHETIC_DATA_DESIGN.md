# THE_SYNTHETIC_DATA_DESIGN.md — profile-driven, FK-aware synthetic data

**Status: ENGINE BUILT — S1/S2/S3 green; CLI flow-wiring + durable codec remain (2026-06-08).**
This is the first-principles design for the synthetic (`--data synthetic` / `from: synthetic`)
data source — the one genuine net-new feature remaining from `THE_CLI.md` §12. The two design
forks are decided (below). The three slices of §8 are built and green:

- **S1 — `SyntheticData.generate`** (pure Core): `src/Projection.Core/SyntheticData.fs`
  (+ `SyntheticConfig`). Tests `tests/Projection.Tests/SyntheticDataTests.fs` (13; the L1 +
  privacy floor — T1 byte-determinism, zero FK orphans, the privacy property, preserve /
  forced-synthesize, null-rate within ε, volume/scale, PK uniqueness, PrimitiveType
  exhaustion). PRNG is splitmix64 (F#'s default arithmetic is unchecked, so the wrapping mix
  is well-defined — the design's "splitmix64-style").
- **S2 — `Transfer.runSynthetic`** (Pipeline): in `src/Projection.Pipeline/TransferRun.fs`
  (reuses `DataLoadPlan.build` → `writePlan` / `wipeFkOrdered`; no source endpoint, no
  `runThroughConnections`). Docker proof `tests/Projection.Tests/SyntheticLoadTests.fs`
  (load lands profiled volume with zero FK orphans through the IDENTITY/AssignedBySink
  capture path; DryRun writes nothing).
- **S3 — the synthetic canary** (`π ∘ σ ≈ id`, the forcing function):
  `tests/Projection.Tests/SyntheticCanaryTests.fs` (warm Docker pool). Capture rode the
  **existing** `LiveProfiler.attach` (the full-Profile assembler already present — §10 gap #1
  resolved). One wrinkle baked into the test: `ReadSide.read` marks every read kind
  `Modality=[Static rows]` and `LiveProfiler` skips static kinds, so the canary strips the
  Static mark before profiling (the data is in the DB, not the catalog).

**Still to build (the operator surface):** the CLI flow-wiring (extend `DataOrigin.Synthetic`
to carry the profile ref; new `PlanAction.SynthesizeAndLoad`; `dataOriginOfSource` /
`planMovement` routing; `runPlan` execution opening the sink and reading the durable profile),
the durable `ProfileCodec` (serialize/deserialize round-trip; §10 gap #2 — still open), and
the capture verb `projection profile <env> --out` (§10 gap #1's I/O front-end —
`LiveProfiler.attach` + `ProfileCodec.serialize`). See §10.

Provenance: derived from the operator's premise ("profile the on-prem legacy application
data to create better synthetic data and start to iterate… preview the migrated legacy
application data in the new form"), the Wave-6 algebra (`π`/`σ`, the faithfulness ladder),
and `THE_CLI.md` (synthetic is the fourth source substrate of the flow surface). Sibling
to `THE_CLI.md` (the surface), `THE_CLI_BACKLOG.md` (the slice ledger, §"§12 follow-ups").

---

## 1. The one idea — synthesis is a *section of profiling*

The engine already projects data to evidence: **profiling**.

```
π : Data ⟶ Profile     (forget the rows; keep per-column marginals, counts,
                         null rates, structural cardinalities)
```

**Synthesis is the approximate right-inverse — the section — of that projection:**

```
σ : Profile ⟶ Data     such that   π ∘ σ  ≈  id_Profile   (within sampling ε)
```

`π` is lossy (many row-sets share one profile); `σ` picks a representative. That single
equation is the **correctness theorem and its canary**: *generate from P, load, re-profile
→ P′; assert P′ ≈ P.* It gives synthetic data its own faithfulness ladder, mirroring the
codebase's L1/L2/L3:

- **L1 — it loads.** Structural integrity is *exact*: valid types, PK uniqueness, **FK
  referential integrity (zero orphans)**, null-rate honored, unique constraints respected.
- **L2 — it re-profiles to ≈ P.** Per-column marginals (categorical frequencies, numeric
  ranges) reproduced within ε; row counts match.
- **L3 — it preserves joint structure** (inter-column correlation). **Out of scope for
  v1** — `Profile` carries marginals + FK structure, not joint distributions. Named
  boundary, not a silent gap (see §7).

In `THE_CLI.md` terms, synthetic is the **fourth source substrate** (cloud-self /
sibling-cloud / on-prem-legacy / **synthetic-from-profile**): a `from` that is *generated
to match another substrate's profile* rather than read from a live DB.

---

## 2. Locked decisions

### 2.1 Value fidelity = **hybrid by cardinality** (operator decision, 2026-06-08)

Per categorical column, branch on the captured `CategoricalDistribution`:

- **Low cardinality** (`DistinctCount ≤ τ` **and not** `IsTruncated`) → **preserve the
  real values**, sampled at their observed `Frequencies`. These are reference data
  (Status / Country / Type) — realistic, low re-identification risk.
- **High cardinality** (`DistinctCount > τ`, **or** `IsTruncated`) → **synthesize fresh
  tokens** preserving the frequency *shape*, **never emitting a real value**. These are
  the likely-PII / free-text / identifier columns. (Truncation ⇒ high-card by
  construction: a capped vocabulary means the tail is unseen, so its values are not
  reproduced.)
- **Per-column override** — a `synthetic` config block forces `preserve` / `synthesize`
  on a named column and sets τ. Default τ conservative (recommend ≤ 50; document it).

**The privacy contract is explicit and testable:** *the generator never emits a real value
from a high-cardinality column.* Property test: synthesize a high-card column → assert none
of the output values appear in the source `Frequencies`.

### 2.2 Capture is a **separate step → durable Profile** (operator decision, 2026-06-08)

Three steps, separated by their nature:

```
projection profile <env> --out legacy.profile.json     # CAPTURE: I/O, once (LiveProfiler → serialized Profile)
# (inspect / tweak legacy.profile.json by hand)
flow: { from: synthetic, profile: "file:legacy.profile.json", to: cloud-uat }   # SYNTHESIZE (pure replay) → LOAD
```

The **durable Profile is the artifact between capture and synthesis** — reviewable,
editable, and replayed deterministically (with a seed). The primary `from: synthetic` path
reads `profile: file:<path>` (the durable form); a `profile: <env>` convenience may
capture-inline-then-synthesize, but the durable path is primary — it is what makes the
operator's *"iterate from there"* real and keeps synthesis in **pure Core**.

---

## 3. The constraint hierarchy — structure dominates, FK-aware is the spine

The point is a *loadable* preview, so structure beats distribution. Per column, in
precedence order:

1. **PK / identity** → a unique surrogate (sequential, type-rendered).
2. **FK** → drawn from the already-generated **parent kind's PK pool** (topological order
   guarantees parents exist first); NULL at the observed rate for optional (nullable) FKs.
   *This is the spine — without it the load fails.* (Fan-out uniform-random in v1;
   `FkSelectivity`-weighted skew deferred — the profiler does not capture it.)
3. **Unique (non-PK)** → distinct values (from the distribution when categorical, else a
   unique synthesizer).
4. **Everything else** → sampled from the column's `Profile` distribution (categorical per
   §2.1 / numeric per the percentile shape), honoring the null rate; a **type-default
   deterministic value** when there is no profile evidence.

Distributions *fill the columns structure leaves free.* An FK column is never sampled from
its own "distribution" — its values are induced by the parent pool.

---

## 4. Determinism — synthesis is a *pure Core* function

`σ` is a pure function of `(Catalog, Profile, SyntheticConfig, seed)` — **no clock, no
`System.Random`, no `float`** (all forbidden in Core by the T1 byte-determinism discipline).
Use a small **host-independent integer PRNG** (splitmix64-style, pure F#): weighted
categorical via cumulative counts + `draw mod total`; numeric via **decimal** arithmetic
over the percentile shape; null via rational (integer) comparison against the null rate.
Reproducible by construction — exactly what the *"iterate from there"* loop needs
(regenerate identical data, tweak, regenerate). The generator therefore lives in
`Projection.Core` (purity-first commitment).

---

## 5. The factoring

```
capture      profile(env)  ⟶  Profile          I/O, slow, ONCE        (LiveProfiler)
synthesize   σ(Catalog,P,cfg,seed) ⟶ rows        PURE, fast, REPEATABLE (Core)
load         rows ⟶ sink                          I/O                    (DataLoadPlan + writePlan)
```

Synthetic has **no source DB**, so it reuses the transfer's **write** seam
(`DataLoadPlan.build` → `writePlan`) but **replaces the ingestion step with generation**.
The Profile is the durable hinge between capture and synthesis.

---

## 6. Schema-drift handling (free, via SsKey)

Generate rows for the **target schema B** (what we load into), drawing evidence from the
`Profile` (captured on substrate A) **by `SsKey`** — A1-stable across renames. B-only
columns (no profile) get type-defaults; A-only columns are ignored. So profiled-legacy →
cloud-preview works even under schema drift, as long as identity is preserved.

---

## 7. Scope boundaries (named, not silent — surface each in `THE_CLI.md` §12)

**In (v1):** marginal distributions + structural integrity + FK-aware + determinism +
drift-by-SsKey + hybrid-by-cardinality privacy.
**Out (v1), each a future refinement with a profiler dependency:**
- L3 joint/correlated synthesis (Profile carries marginals, not joint distributions).
- `FkSelectivity`-weighted FK fan-out (not profiled yet; uniform-random for now).
- Composite / natural PKs (assume the OutSystems single-surrogate-`Id` norm first).
- Volume default = **profiled `RowCount` per kind**, with a `--scale <f>` factor for small
  iteration previews and an optional `--seed <n>` (default a fixed seed).

---

## 8. Architecture + the build slices

### S1 — `SyntheticData.generate` (Core, pure) — the algorithmic heart

`Catalog(B) × Profile × SyntheticConfig × seed → Map<SsKey, StaticRow list>`. Topo-ordered;
PK surrogates; FK drawn from parent pools; hybrid-by-cardinality categoricals; numeric
percentile-shape; null rates; host-independent PRNG; drift-by-SsKey; **values emitted in
`RawValueCodec` raw-string form** (so the load's `SqlLiteral.fromRaw` renders them — do
NOT hand-format SQL; produce the raw canonical string per type). Exhaustive `match` over
`PrimitiveType` (every variant gets a generator + a type-default).
**Unit tests (the L1 + privacy floor):** determinism (same seed → byte-identical output);
FK integrity (zero orphans across the product); null-rate within ε; the **privacy
property** (no real high-card value emitted); volume = profiled `RowCount`; PK uniqueness.

### S2 — `SyntheticLoadRun` (Pipeline) — the synthetic-load runner

`generate` (Core) → `DataLoadPlan.build` → `writePlan` to the sink (reuses the transfer's
write seam; **no source DB / no `runThroughConnections`**). New `PlanAction.SynthesizeAndLoad`
(carry: the resolved Profile source, the target conn, seed/scale, opts). Wire into
`runPlan` in `Program.fs`.

### S3 — capture + wiring + the canary

- **Capture verb:** `projection profile <env> --out <path>` → `LiveProfiler` capture →
  **serialized Profile**. (See §10 gap: a Profile serialize/deserialize round-trip likely
  needs building — `DistributionsEmitter` is serialize-only.)
- **Flow wiring:** `from: synthetic, profile: file:<path>` → `DataOrigin.Synthetic` must
  **carry the profile reference** (today it is nullary — extend to `Synthetic of profile:
  string`); `resolveFlowSpec` threads it; `planMovement` routes a synthetic data origin to
  `SynthesizeAndLoad`.
- **The synthetic canary (the forcing function):** generate from P → load to an ephemeral
  DB → re-profile → assert `P′ ≈ P` (L1 structural exact: zero FK orphans, counts match;
  L2 distributions within ε). This is `π ∘ σ ≈ id` made executable — the proof the feature
  works, and the gate against regression. Lives in the Docker test pool (warm container).

---

## 9. Reusable machinery — file:line anchors (confirmed 2026-06-08)

| Need | Anchor |
|---|---|
| Row shape (what to produce) | `StaticRow = { Identifier: SsKey; Values: Map<Name,string> }` — `src/Projection.Core/Catalog.fs:77` (values are **raw strings per RawValueCodec**) |
| Value rendering (raw → SQL) | `RawValueCodec` `src/Projection.Core/RawValueCodec.fs:30`; `SqlLiteral.fromRaw` `src/Projection.Core/SqlLiteral.fs` (load path renders raw→typed→SQL — emit raw form, never hand-format) |
| Type DU to exhaust | `PrimitiveType` `src/Projection.Core/PrimitiveType.fs:12` (Integer / Decimal / Text / Boolean / DateTime / Date / … — match exhaustively) |
| Profile (evidence) | `Profile = { Columns: ColumnProfile list; … }` `src/Projection.Core/Profile.fs:971`; `ColumnProfile` (RowCount/NullCount) `:97`; lookups `Profile.tryFindColumn :1038`, `tryFindCategorical :1062`, `tryFindNumeric :1075` |
| Categorical evidence | `CategoricalDistribution` (`Frequencies: (string*int64) list`, `DistinctCount`, `IsTruncated`) `src/Projection.Core/Profile.fs:310` |
| Numeric evidence | `NumericDistribution` (`Min/P25/P50/P75/P95/P99/Max: decimal`) `src/Projection.Core/Profile.fs:414` |
| Attribute / FK shape | `Attribute` (`Name`, `Type`, `Column: ColumnRealization`, `IsPrimaryKey`) `Catalog.fs:446`; `ColumnRealization` (`IsNullable`) `:388`; `Reference` (`TargetKind: SsKey`) `:575` |
| Topo order (FK-first) | `TopologicalOrderPass.runWith TreatAsCycle catalog` — used at `src/Projection.Pipeline/TransferRun.fs:546` |
| Load plan | `DataLoadPlan.build : Catalog → TopologicalOrder → Map<SsKey, StaticRow list> → SurrogateRemapContext → DataLoadPlan` `src/Projection.Core/DataLoadPlan.fs:81` |
| Write seam | `runCore`'s write block (`writePlan` / `writePlanResumable` / `wipeFkOrdered`) `src/Projection.Pipeline/TransferRun.fs:582-597`; `WriteOptions` `:489` |
| Profile capture | `LiveProfiler.captureEvidenceCache` `src/Projection.Adapters.Sql/LiveProfiler.fs:338` (+ `captureEvidenceCacheWith :308`); derive full `Profile` via the `Cache.deriveX` pattern (per CLAUDE.md EvidenceCache discipline) |
| Profile serialize | `DistributionsEmitter` `src/Projection.Targets.Distributions/DistributionsEmitter.fs:35` (serialize side; **deserialize is a gap — §10**) |
| FK-aware gen precedent | `FixtureGenerator` (test-only, procedural) `tests/Projection.Tests/Fixtures/FixtureGenerator.fs` (`GenerateSpec :21`) — mirror the FK-density / topo approach, but drive it from the Profile, not RNG-procedurally |
| Data origin axis | `DataOrigin.Synthetic` `src/Projection.Pipeline/MovementSpec.fs` (extend to carry the profile ref); `FlowSource.Synthetic of profile: string option` already parsed in `MovementSurface.fs` (`parseFlowSource`) |

---

## 10. Open anchors to confirm / likely-build

1. **Full-`Profile` capture from `LiveProfiler`. — RESOLVED (2026-06-08).**
   `LiveProfiler.attach cnn catalog Profile.empty` (`src/Projection.Adapters.Sql/LiveProfiler.fs:1231`)
   IS the full-Profile assembler — it captures the `EvidenceCache` once and composes every
   axis (Columns / Categorical / Numeric / FK realities + cardinalities + selectivities /
   joints / composite-unique) via `attachFromCache`. The synthetic canary uses it directly.
   **Caveat for the capture verb:** `LiveProfiler` skips static kinds, and `ReadSide.read`
   marks read kinds `Modality=[Static rows]`; strip the Static mark before profiling (the
   canary does this). The remaining I/O front-end is the capture *verb* (read env → strip
   Static → `attach` → `ProfileCodec.serialize` → file), not the assembler.
2. **Profile serialize/deserialize round-trip (the durable artifact).** `DistributionsEmitter`
   serializes; a **deserialize** (read `legacy.profile.json` back into a `Profile`) is
   likely missing. Build a `ProfileCodec` (mirror `CatalogCodec`) with the universal
   round-trip law (`∀ p. deserialize (serialize p) = Ok p`) per the codec discipline. This
   is load-bearing for "capture once, replay pure."
3. **`DataOrigin.Synthetic` payload.** Today nullary; must carry the profile reference
   (and seed/scale, or thread those via `FlowRunOpts`/config). Decide the shape in S2/S3.

---

## 11. Config + surface shape (target)

```jsonc
{
  "environments": {
    "onprem-legacy": { "access": "direct", "conn": "env:ONPREM_LEGACY_CONN" },
    "cloud-uat":     { "access": "direct", "conn": "env:CLOUD_UAT_CONN", "grant": "data" }
  },
  "flows": {
    "preview-synth": { "from": "synthetic", "profile": "file:legacy.profile.json", "to": "cloud-uat" }
  },
  "synthetic": {                 // the hybrid-by-cardinality policy + volume
    "preserveCardinalityMax": 50,
    "preserve":   ["Status", "Country"],
    "synthesize": ["Email", "FullName"],
    "scale": 1.0
  }
}
```

```
projection profile onprem-legacy --out legacy.profile.json   # capture (once)
projection preview-synth                                       # synthesize → preview (pure replay)
projection preview-synth --go --seed 7                        # apply, reproducibly
```

---

## 12. Disciplines to hold (do not break without writing the amendment first)

- **T1 byte-determinism** — pure Core; **no `float`, no `System.Random`, no clock**. The
  PRNG is a pure host-independent integer generator; continuous evidence is `decimal`.
- **Total decisions, named skips / no silent drop** — every `PrimitiveType` and every
  column-role has a defined generator; unknown table names, missing evidence, and
  out-of-scope cases are **named** (a note or a coded refusal), never silently dropped.
- **IR grows under evidence, not speculation** — build S1→S2→S3 against the `synth` flow
  consumer; don't add Profile fields or knobs ahead of a consumer.
- **The canary is the forcing function** — `π ∘ σ ≈ id` is the acceptance proof; ship S3
  with it green (warm Docker pool).
- **Codec discipline** — the durable Profile round-trip is an FsCheck universal law over a
  constructed-valid generator (`deserialize ∘ serialize = id`); declarative test inputs
  (mutate the producer's own valid output), never hand-authored wire format.
- **Test names cite the law** (`` ``L1: synthetic load has zero FK orphans`` ``,
  `` ``privacy: no real high-cardinality value is emitted`` ``, `` ``T1: same seed → byte-identical`` ``).
- **Operator-facing strings obey THE_VOICE register** (stative, agentless, no pronouns;
  the change-norm / counts surfaced as evidence). See `THE_VOICE.md`.
- **Run `scripts/test.sh fast` (pure) and the warm Docker canary separately** — never
  concurrently (no-swap host OOM). Commit each slice green.

---

## 13. Why this matters (the operator's outcome)

This closes the last source-substrate of the flow surface: the operator can **preview the
migrated legacy application data in the new cloud form without exposing real records** —
generate data that re-profiles to the legacy shape (realistic), reproduces real reference
values but never real PII (hybrid-by-cardinality), loads cleanly (FK-aware), and is
reproducible for iteration (seeded, pure). It is the "swap in synthetic, profile the
legacy, iterate" workflow, made an engine capability with a fidelity theorem behind it.
