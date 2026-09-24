# THE_FIDELITY_PROOFS.md — proving an arbitrary estate reproduces, through the artifacts you ship, offline

> **Status: shipped, 2026-07-20** (the estate/fidelity chapter's Track-B proofs; PR on
> `claude/projection-sidecar-extraction-test-ekmbyu`). Two operator capabilities become
> first-class under the existing `check fidelity` umbrella (the `verify-data` token stays
> **retired**, `DECISIONS 2026-07-15`): **Path 1** — prove byte-identical extraction for an
> *arbitrarily shaped and data-filled* estate, across DacFx ⊕ DDL staging and transfer ⊕ lanes
> loading; **Path 2** — verify a target *the operator applied themselves* against a portable
> proof manifest, with **no live source present**. Advisory / read-only, refuse-by-name, no
> `--go` (the Estate chapter's non-goals hold).
>
> **Reading order.** Sits beside `CROSS_ENVIRONMENT_READINESS.md` (the sibling estate-shape
> gate — that proves the *schema* is one shape; this proves the *data* reproduces),
> `THE_USE_CASE_ONTOLOGY.md` (T17 the faithfulness law this generalizes; the acceptance
> ontology's AC-D11/AC-D12), `AXIOMS.md` A47/A48 (the two laws), `DATABASE_ARCHETYPES.md`
> (the archetype→identity projection Path 1 reuses), and `THE_CLI.md` §8 (the `check` verb
> family). The machinery reused is `CatalogRendition.fs` (the rendition pair),
> `FidelityCompareRun.fs` (the one comparator), and the data lanes
> (`DataEmissionComposer.fs`).

---

## 0 — The one idea

The V2 fidelity proof used to say *"the fixture reproduces"*: `check fidelity <flow>` scaffolds
a throwaway container, stages the model's logical shape, loads it through the transfer, and
compares row-by-row against the live source. Two couplings kept it from saying *"**this
operator's estate** reproduces, through the artifacts I actually ship, and I can re-check what
I deployed later, offline"*:

1. **One build path.** The stand-in's schema was always emitted DDL; its rows always the
   journaled transfer; its identity always sink-minting. A real cutover takes *other* paths —
   a DacFx publish, a hand-applied seeds+bootstrap bundle, an on-prem FullRights sink that
   preserves keys.
2. **The live source was always required.** The proof compared against the source *now*; there
   was no committed oracle to re-check a self-applied database against, later, offline.

Path 1 removes coupling (1) by making the build a **product of axes** whose verdict is
invariant (A47). Path 2 removes coupling (2) by promoting the source's per-kind digests to a
**portable manifest** and adding an offline reconcile (A48).

---

## 1 — Using it (operator recipes & agent guidance)

Two audiences read this section: the **operator** running the CLI at a cutover, and an **agent**
driving the sidecar on the operator's behalf. Both need the same three things — the command
surface, which mode fits which intent, and the verdict contract.

### 1.1 The command surface

All of it lives under `check fidelity` (read-only, advisory, no `--go`). One model must be
configured (`model` or `model.ossys` in `projection.json`); a Path-1 flow's `from` must be a
live environment.

| Command | What it proves | Needs |
|---|---|---|
| `check fidelity <flow>` | the estate reproduces via DDL staging + the journaled transfer (the default) | live source |
| `check fidelity <flow> --stage dacfx` | …staging the schema through a DacFx publish instead of DDL | live source |
| `check fidelity <flow> --data lanes` | …loading the rows by applying the emitted seeds+bootstrap instead of the transfer | live source |
| `check fidelity <flow> --capture <path>` | …AND writes the portable proof manifest for a later offline reconcile | live source |
| `check fidelity --against <manifest> --target <ref>` | a self-applied DB matches a captured manifest — **no live source** | the manifest + the target |

Modifiers: `--sample N` caps the NAMED per-kind differences shown (the totals and the verdict
stay exact — default 20); `--refresh` ignores the incremental proof cache and re-proves;
`--format json` emits the machine-readable verdict. `--stage` and `--data` compose freely and
default to `ddl` / `transfer`. Every unknown token refuses by name (A44) — nothing silently
defaults.

### 1.2 Path 1 — prove *this* estate reproduces, the way you actually deploy it

Pick the staging and loading axes to match how the real cutover builds the target:

- **Schema as emitted DDL, data via the tool's transfer** → `check fidelity <flow>` (the
  default). The fastest path; the incremental cache skips it when nothing moved.
- **Your cutover publishes a `.dacpac`** (SSDT / sqlpackage) → add `--stage dacfx`. Proves the
  extraction survives the declarative publish, at the deployed-schema + row grain (never dacpac
  bytes).
- **You hand-apply the shipped `Data/StaticSeeds.sql` + `Data/Bootstrap.sql`** → add `--data
  lanes`. Proves *the artifacts you ship* reproduce the source, not just the tool's transfer.
- **Your real path is both** (DacFx schema + hand-applied data) → `--stage dacfx --data lanes`.

All four combinations return the same verdict on a faithful estate (A47); reach for the pair that
mirrors your production deploy so the proof is about *your* path. Identity handling is automatic
— a FullRights target preserves the source keys (IDENTITY_INSERT), a data-only target lets the
sink mint; you never choose it, the flow's target archetype (`grant`/`archetype` in
`projection.json`) does.

```
check fidelity onprem-cutover                              # DDL + transfer
check fidelity onprem-cutover --stage dacfx                # DacFx publish + transfer
check fidelity onprem-cutover --data lanes                 # DDL + emitted seeds/bootstrap
check fidelity onprem-cutover --stage dacfx --data lanes   # the full declarative + hand-apply path
```

### 1.3 Path 2 — capture now, verify a self-applied database later, offline

The workflow spans two moments and usually two machines:

1. **At capture time, with the source reachable** — run the proof and write the manifest:
   ```
   check fidelity onprem-cutover --capture cutover-proof.json
   ```
   The manifest is small and portable — the source's per-kind digests + the model hash + capture
   provenance. Commit it, attach it to the change ticket, hand it to whoever applies the database.

2. **Later, on the machine that applied the database, with no source** — reconcile:
   ```
   check fidelity --against cutover-proof.json --target "live:Server=…;Database=…"
   ```
   The target ref is any `env:` / `file:` / `live:` / raw connection. Nothing reads the source;
   the verdict comes from the manifest + the target alone.

Path 2 answers *"is the database I actually deployed byte-identical to what the source held when
we captured?"* — days, or environments, away from the source.

### 1.4 The verdict contract (exit 0 / 5 / 6)

Every `check fidelity` invocation exits on one of three codes — script against them:

- **0** — byte-identical. The estate (or the applied DB) reproduces the source / manifest exactly.
- **5** — a NAMED divergence. The per-kind lines say which kind (and, with a live source, which
  key and columns) differ; the totals are exact. Never a phantom-green — a stale manifest or a
  single flipped cell reds.
- **6** — refuse-by-name. The model or an environment could not be read, the target is
  unreachable, or (Path 2) the manifest's model hash disagrees with the target. A precondition
  failed; nothing was proven.

The digest is *stricter than `=`*: `''` and `NULL` are distinct bytes, and the canonical hash is
collation-blind (survival rule 14). A cell that "looks equal" but reds is usually the
empty-string/NULL split or a collation fold — suspect that before the code.

### 1.5 For the agent driving the sidecar

An agent picks the invocation from the operator's intent and the declared environment, handles
the verdict, and never guesses past a refusal:

- **Choosing Path-1 flags.** Map intent → axes: "prove the DacFx publish" ⇒ `--stage dacfx`;
  "prove the seeds/bootstrap I hand-apply" ⇒ `--data lanes`; "prove the default tool path" ⇒
  neither. Do NOT set `--stage`/`--data` to a value the operator's real deploy doesn't use — the
  proof is only meaningful about the path it runs. Never pass an identity policy; it is derived
  from the target archetype (declare the target's `grant`/`archetype` in `projection.json`).
- **The capture→reconcile handoff.** When the operator will verify a DB later or elsewhere,
  capture at proof time (`--capture <path>`), record where the manifest lives, and reconcile with
  `--against`/`--target` once the applied DB exists — no source needed then. A manifest is
  `RowDigestFold`-plane only; never hand one flow's manifest to a different model's target (the
  model-hash gate reds it, exit 6).
- **Reading the verdict.** Branch on 0 / 5 / 6 (§1.4). On 5, surface the per-kind divergence
  lines — they are the finding. On 6, report the precondition (unreadable model/env, unreachable
  target, model-hash mismatch) and stop; do not retry with different flags hoping for green.
- **What not to do.** These verbs are advisory and read-only — there is no `--go`, they never
  write to the operator's databases (the proof owns a throwaway container; the reconcile only
  reads the target). Don't force `--refresh` unless the estate changed under a cache the
  fingerprint can't see (an in-place UPDATE); don't assume a `dacfx`/`lanes` run reused a cached
  green — it never does, the equivalence is the thing under proof.

---

## 2 — One comparator spine, two entry paths

The pure comparator core is reused unchanged by both paths:

- Per-row hash `RowDigester.hashRowBytes` — SHA256 over name-sorted `name=value`, NULL omits
  its pair (stricter than `=`; `''` and `NULL` are DISTINCT bytes).
- Order-independent aggregate `RowDigestFold` → `TableDigest {Aggregate; Count}` — folds
  **every** row (not just `--sample`), content-addressed (so a GUID-keyed kind is covered by
  the aggregate verdict even where per-row naming is skipped).
- Report + face contract `RowFidelityReport.agrees`, exit **0** (byte-identical) / **5**
  (named divergence) / **6** (unreadable / refuse-by-name).

`--sample N` caps only the NAMED differences per kind; `DifferenceTotal` is exact and the fold
is whole-table (`keepDiff`: `(if total < cap then d::diffs else diffs), total + 1L`). This is
why a **whole-estate completeness rung is already present** — see §5's deferral.

---

## 3 — Path 1: arbitrary-estate proof across the staging × loading square

`check fidelity <flow>` grew two orthogonal axes plus an archetype-derived identity policy.
The comparator (step 3) is identical across all of them; only HOW the stand-in is built varies.

| Axis | Values | Seam | Slice |
|---|---|---|---|
| **Schema staging** | `--stage ddl` (default) · `--stage dacfx` | `stageStandIn` / `StagingMode` — emitted DDL batch, or a `DacpacEmitter.emit` published through `Deploy.deployDacpac` | P1-S1 |
| **Row loading** | `--data transfer` (default) · `--data lanes` | `loadViaTransfer` / `loadViaLanes` / `LoadMode` — the journaled transfer, or the live-hydrated StaticSeeds+Bootstrap lanes applied via `Deploy.executeLeveledSeed` | P1-S4 |
| **Identity disposition** | derived, not a flag | `Environment.effectiveArchetype`→`CapabilityProfile.of`: a FullRights `flow.To` ⇒ `PreferPreservedKeys` (IDENTITY_INSERT, keys land directly); else `Structural` | P1-S3 |

**A47 — the proof is staging-and-loading invariant.** Every `(stage, data)` combination, under
either identity policy, reproduces the same source byte-identical and reds the same divergence.
The invariant grain is the **deployed schema + streamed rows**, never dacpac bytes (`BACKLOG.md`
Slice ζ names that deferral). A `dacfx` or `lanes` run never reuses the DDL+transfer proof cache
— the equivalence is the very thing under proof.

**"Prove what I ship" (`--data lanes`) is the publish path, re-aimed.** The lanes load mirrors
`PipelinedBootstrapEquivalenceTests`'s composition: `hydrateCatalog` grafts the Static-marked
kinds' rows; `CatalogRendition.logical` re-renders the grafted catalog to logical names (the
emission passes touch `Kind.Physical`, never `Modality` — A1 — so the static rows survive);
`hydrateBootstrapRows` drains the non-static kinds. Rows key by attribute `Name`
(rendition-invariant), so a physical-source hydrate composes correctly against the logical
target; the lanes bracket `IDENTITY_INSERT` (NM-26), so keys land directly.

**Single-rendition estates align for free.** `LogicalTableEmission`/`LogicalColumnEmission`
self-skip a kind whose logical name equals its physical (`substituteKind`), so a one-shape
estate renders `physical = logical` with an empty rename map — the identity case the
rename-aware leg (LE-3, distinct `OSUSR_L3_*` renditions) strictly generalizes.

Surface: `check fidelity <flow> [--stage ddl|dacfx] [--data transfer|lanes] [--sample N]
[--refresh] [--capture <path>]`. Every mode is A44-reachable — parse-refused by name on an
unknown token (`MovementSurfaceTests`).

---

## 4 — Path 2: offline verification against a portable proof manifest

| Step | Verb | What it does | Slice |
|---|---|---|---|
| **Capture** | `check fidelity <flow> --capture <path>` | Promote the source's per-kind `RowDigestFold` digests (write-only telemetry until now) to a durable, portable `ProofManifest` — versioned, plane-tagged (`rowDigestFold`), model-hash + capture provenance. Forces a full proof run (a cache hit carries no digests). | P2-S1/S2 |
| **Reconcile** | `check fidelity --against <manifest> --target <ref>` | Fold **only** the target, compare against the manifest's stored source digest. **No source connection is opened.** | P2-S3 |

**A48 — the reconcile is sound.** Agrees (exit 0) ⟺ every kind's live-folded target digest
equals the recorded source digest. A stale digest reconciles to a **named** per-kind divergence
(exit 5) — never a phantom-green; an unreachable target or a model-hash mismatch refuses (exit
6). The manifest plane is `RowDigestFold` end-to-end, so a capture and a reconcile are two reads
of one canonical form. Each reconcile verdict is `NamingSkipped` (the honest degradation — no
per-row naming without the live source; escalate to `check fidelity <flow>` when the source is
present). `ProofManifest.tryParse` is fail-closed (a foreign version / plane / garbage document
is a named `Error`, never a silent empty).

The headline: capture a manifest at cutover, carry it anywhere, and later — with the source torn
down — prove the database you actually deployed is byte-identical to what the source held.

---

## 5 — Deferred, named (never silent)

- **P1-S2 · the ServerDigest whole-estate completeness rung — DEFERRED as redundant for
  correctness.** The rung's premise (a divergence *outside* the `--sample` window is missed) is
  false: `RowDigestFold` folds every row and `DifferenceTotal` is exact (§2), so an off-sample
  divergence already reds the verdict, and GUID-keyed kinds are already covered by the
  content-addressed aggregate. `ServerDigest`'s only distinct value is **zero-transfer** (a
  server-side `CHECKSUM_AGG`-style fast path — perf at estate scale), and wiring it mutates the
  Core verdict types (`KindRowVerdict`, `RowFidelityReport.agrees`). Re-open trigger: a measured
  need to prove a table whose row stream is too large to fold client-side.
- **Business-key-anchored manifest** (surrogate-excluded): for a sink-minting target whose keys
  differ from source. Today the manifest digest INCLUDES surrogate keys (decision 4: on-prem
  FullRights preserves them). A `GoldenCodec`-style logical manifest is the named extension.
- **Offline per-row naming**: embed `GoldenDataset` rows in the manifest so a reconcile can name
  *which* cell diverged offline (today it names the *kind*; per-row naming escalates to the live
  `check fidelity <flow>`).
- **Non-OutSystems source estates** (decision 1): the three de-OSSYS gating seams (the `" "`→NULL
  sentinel, Static-marking, the IsUserFk rekey) become archetype/config-gated. Out of scope until
  a non-OSSYS source is required.

---

## 6 — Where the truth lives

| Question | Owner |
|---|---|
| The two laws | `AXIOMS.md` A47 (staging×load invariance), A48 (reconcile soundness) + `AxiomTests.fs` pointers |
| The acceptance criteria | `THE_USE_CASE_ONTOLOGY.acceptance.md` AC-D11 (Path 1), AC-D12 (Path 2) |
| The witnesses | `ReverseLegCanaryTests` (B5 / P1-S1 / P1-S3 / P1-S4), `FidelityRowsDockerTests` (P2-S3), `ProofManifestTests` + `MovementSurfaceTests` (pure) |
| What was decided | `DECISIONS.md` — the 2026-07-19/20 entries (P1-S1, P2-S1/S2, P2-S3, P1-S3, P1-S4) |
| The command surface | `THE_CLI.md` §8 (`check fidelity`); A44 round-trips in `MovementSurfaceTests` |
