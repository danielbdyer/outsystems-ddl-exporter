# PREFLIGHT — Cloud Insertion / Data Producers (2026-06-09)

> **What this is.** The feature-status **preflight + milestone/acceptance plan** for the
> cloud-insertion capability — writing production-like data *up* into a live cloud OutSystems
> environment (disposition **A**) from the three producers (`synthetic` / `legacy` / `peer`),
> proven under R6. It exists so no milestone, acceptance criterion, or test case is lost on the
> way to the finish line.
>
> **Status sourcing (read this).** The *only* trusted source for "what is still outstanding" is
> **`CONFIRMED_BACKLOG_2026_06_09.md`** (the code-verified ledger). `BACKLOG.md` / `HORIZON.md` /
> `EXECUTION_PLAN.md` / the climb debrief are **stale — not trusted for status**. The design specs
> (`PRESCOPE_TRANSFER.md`, `THE_DATA_PRODUCERS.md`, `TRANSFER_ISOMORPHISM_SUBSTANTIATION.md`, the
> ontology `.acceptance`/`.obligations`) are trusted for **acceptance criteria and design framing
> only**. The current-state table (§1) is code-verified by inspection at HEAD `9debcc0`; the green
> baseline is **claimed** per `CONFIRMED_BACKLOG` ⓪ (pure pool 2943/0) — the Docker canaries were
> **not re-run** this session.
>
> **Canonical home.** The discrete outstanding items are also logged in `CONFIRMED_BACKLOG` **§J**
> (the canonical "what's left" surface); this doc carries the milestone/acceptance/test-case depth
> §J points to. The producer design is `THE_DATA_PRODUCERS.md`; the engine it runs on is
> `PRESCOPE_TRANSFER.md`.

---

## 0. The finish line — what "done" means

The cloud-insertion capability is complete when **all three producers write production-like data
into a live cloud OutSystems environment (`cloud-uat`, disposition A, `grant: data` DML-only) under
R6, each proven by its data canary**, and specifically when:

1. **`synthetic`** — generated-from-profile load, `π ∘ σ ≈ id` green (**DONE**).
2. **`peer` (`golden`)** — a same-estate cell → cloud, **user rows excluded**, their FKs **re-keyed
   by email**, gated by `validate-user-map` before any DML; proven by the cloud→cloud **re-key
   canary** (a re-keyed row is one CDC Update, never Delete+Insert; source user surrogates provably
   absent from the sink).
3. **`legacy` (`preview`)** — the foreign-schema on-prem app **ingested through the logical schema by
   `SsKey`**, every unfaithful translation a **named tolerance**; proven by the **migration-preview
   canary** (round-trips up to the named tolerances).
4. **The gate is real** — the capability survey resolves the per-kind disposition mix (incl. **P10**
   user-directory for the re-key), wired into the run verbs (advisory per R6, S3).
5. **The real-UAT execute path is unblocked** — **OPEN-2** (is the cloud UAT a writable SQL
   connection?) resolved by the ops spike; `--execute` gated by `--go` + `PROJECTION_ALLOW_EXECUTE`,
   dry-run default, preview row-cap.
6. **Every cross-boundary erasure is named** (foreign-schema translation, unmapped user) — none silent.

---

## 1. Current-state preflight (code-verified by inspection, HEAD `9debcc0`)

Legend: **BUILT** (shipped + tested, green-claimed) · **WIRED?-NO** (capability built+tested, not
reachable from the production path) · **DESIGN-ONLY** (spec'd, no code) · **MISSING**.

### BUILT — the engine the producers run on
| Component | Status | Anchor |
|---|---|---|
| `synthetic` producer (σ, pure Core) | BUILT | `SyntheticData.generate` `SyntheticData.fs:430`; `SyntheticLoadRun.run:50`; `Transfer.runSynthetic` `TransferRun.fs:690`; `ProfileCodec`; capture verb `ProfileCaptureRun` |
| Synthetic canary `π ∘ σ ≈ id` + privacy property | BUILT (green-claimed) | `SyntheticCanaryTests.fs`, `SyntheticDataTests.fs` |
| Transfer core (direction-neutral plan + write seam) | BUILT | `DataLoadPlan.build` `DataLoadPlan.fs:81`; `writePlan`/`writePlanResumable`/`wipeFkOrdered` `TransferRun.fs:146,283,227`; `runResumable:656` |
| Identity disposition + capture | BUILT | `IdentityDisposition`+`ofKind` `SurrogateRemap.fs:65-83`; `SurrogateRemapContext`+`capture` `:95-152`; AssignedBySink capture `TransferRun.fs:181` |
| Reconciliation (email re-key engine) | BUILT | `Reconciliation.reconcileKind` `Reconciliation.fs:47,61`; `MatchByColumn`/`ManualOverride`; `ByEmail/BySsKey → MatchByColumn` `:141` |
| **Data-level canary (the extended-H-050 proof, "Slice C")** | BUILT (green-claimed) | `TransferCanaryTests.fs` (15 facts): multi-table FK round-trip `:273`, deferred self-FK `:277`, **Dev→UAT user re-key `:330`**, fail-loud drop `:393`, dry-run `:874` |
| `validate-user-map` pre-write gate | BUILT | `validateUserMap` `TransferRun.fs:374`, wired `runCore:601`; `TransferRefusalTests.fs:131` (AC-I5 pre-write halt) |
| Rename-aware re-point + reconcile-with-renames | BUILT | `runWithRenames:782`, `runReconcilingWithRenames:833`, `RenameProjection.repointRows` |
| Dropped-rows fail-loud (exit 9) | BUILT | `exitCodeForReport:981`; routed via `Preflight.refusalOf` (A1 shipped, `24c956b`) |
| `migrate A B` with-data (rename-aware leg) | BUILT | `MigrationRun.executeWithData:599`; A5 rename-aware leg shipped `38be8f5` |
| CLI flow routing (Transfer / SynthesizeAndLoad / MigrateWithData) | BUILT | `PlanAction` `MovementSpec.fs:226-287`; `planMovement` `MovementSurface.fs:320`; routing tests `MovementSurfaceTests.fs` (72) |

### WIRED?-NO — built + tested, not on a production path
| Component | Status | Anchor / note |
|---|---|---|
| Capability survey (OPEN-2 instrument) | BUILT, **not wired into run verbs** | `CapabilitySurvey.fs` (3 probe axes: reachability/grant/CDC); standalone `survey` verb, exit 7 advisory; **not** in Transfer/Migrate pre-flight |
| `Preflight.all` / `allReporting` aggregate gate | BUILT, **zero production callers** | `Preflight.fs:364,494`; verbs hand-chain gates (`spanningPreflight TransferRun.fs:416`). **= CONFIRMED_BACKLOG A1 (OPEN)** |
| `AssignedBySink` cyclic / composite-identity | refused (acyclic only) | acyclic SHIPPED 2026-05-31; cyclic self-ref IDENTITY + composite are named refusals → follow-ons |
| Scoped-delete arm (`DeleteScope`) | BUILT, emitters hardcode `None` | `= CONFIRMED_BACKLOG A3` |
| `--resumable` flag | SHIPPED this session | `a317c07` (`= A2`) |

### DESIGN-ONLY — spec'd this session, no code
| Component | Status | Note |
|---|---|---|
| `peer` / `legacy` as producers | DESIGN-ONLY | No `FlowSource.Peer`/`Legacy` variant; both would ride the generic `Env` source path. `THE_DATA_PRODUCERS.md` |
| `golden` cloud→cloud user-exclusion + email re-key | DESIGN-ONLY (machinery exists) | The *machinery* is proven as the **Dev→UAT on-prem** instance (`TransferCanaryTests.fs:330`); the **cloud→cloud** flow + its canary do not exist |
| `legacy` foreign-schema ingest | MISSING | The legacy app schema ≠ the model's schema; no ingest-through-the-logical-schema path |
| `peer` re-key canary (cloud→cloud) | MISSING | named in design; not written |
| `legacy` migration-preview canary | MISSING | named in design; not written |

---

## 2. Milestones to the finish line

`M0` is done; `M1`–`M6` are ordered by dependency. Each cites its design origin
(PRESCOPE slice / OPEN-N / `THE_DATA_PRODUCERS` slice / AC-id) and its `CONFIRMED_BACKLOG` §J id.

| Milestone | Scope | Depends on | Origin | §J |
|---|---|---|---|---|
| **M0** ✅ | synthetic producer · Transfer core · data-level canary · Dev→UAT re-key · migrate-with-data | — | Slices 1/A/B/C/C′/D/E (acyclic) | — |
| **M1** | **Producer modelling decision + routing.** Decide whether `peer`/`legacy` are `FlowSource` variants or **env metadata** (`estate: same \| foreign`) + flow disciplines; wire the chosen shape so a `golden`/`preview` flow routes with its producer semantics. *(Open decision — §5 D-1.)* | M0 | `THE_DATA_PRODUCERS` §1; `MovementSpec.fs:150` | J1 |
| **M2** | **`peer` / `golden` cloud→cloud.** PE-1 user exclusion + email re-key on the cloud→cloud flow (reuse `ReconciledByRule`+`reconcileKind MatchByColumn "Email"`); PE-2 confirm `validate-user-map` gate fires for the cloud sink; **PE-3 the re-key canary** (cloud→cloud). | M1; survey **P10** | `THE_DATA_PRODUCERS` §6 PE-1..3; AC-I2/I5; P-3 | J2 |
| **M3** | **`legacy` / `preview`.** LE-1 foreign-schema ingest lifting the legacy substrate into the logical shape by `SsKey` (drift-by-SsKey), every unfaithful translation a **named tolerance**; **LE-2 the migration-preview canary**. *(Hardest — foreign schema.)* | M1 | `THE_DATA_PRODUCERS` §6 LE-1/2; `Tolerance.fs` | J3 |
| **M4** | **The gate is real.** Wire the capability survey into the run verbs as **advisory G0** (S3, per R6); wire `Preflight.all` (= CONFIRMED A1) so the survey feeds one composed gate; surface P10 + grant breadth. | M0 | `TRANSFER_ISOMORPHISM` §2; DECISIONS 2026-06-09 (S3); CONFIRMED A1 | J4 |
| **M5** | **Real-UAT execute (OPEN-2).** The ops spike — a throwaway-UAT `Microsoft.Data.SqlClient` connection probing `IDENTITY_INSERT`/INSERT/grants (resolves OPEN-1/2/3/5/6 + the disposition mix); then `--execute` under R6 with `--preview-row-cap`. | M4 | PRESCOPE Slice D / §13; EXEC 5.1; OPEN-2 | J5 |
| **M6** | **Follow-ons (pull under a consumer).** cyclic `AssignedBySink` (6.A.2) + composite-identity capture (6.A.3); `MERGE…OUTPUT` set-based capture (Contribution B — trigger-gated on P4 + measured per-row bottleneck); synthetic `--rows N` / `--seed` (= D8); scoped-delete CLI exposure (A3); user-map walkable Surface (D7); `UserRemapContext→SurrogateRemapContext` merge. | M2/M3/M5 | PRESCOPE §13; CONFIRMED A3/D7/D8/G | J6 |

---

## 3. Acceptance criteria + named test cases (the checklist)

Every milestone's proof. Status: **GREEN** (exists, claimed passing) · **TO-WRITE** (named, not built) ·
**TO-WIRE** (machinery green; the producer-specific assertion is new).

| Test / criterion | Milestone | Status | Anchor / target |
|---|---|---|---|
| `π ∘ σ ≈ id` — synthetic canary | M0 | GREEN | `SyntheticCanaryTests.fs` |
| privacy: no real high-cardinality value emitted | M0 | GREEN | `SyntheticDataTests.fs` |
| data-level canary — `Ingestion(Projection(rows)) ≈ rows` (row-digest equality) | M0 | GREEN | `TransferCanaryTests.fs` |
| `AssignedBySink round-trips modulo SurrogateRemapContext` (acyclic) | M0 | GREEN | `TransferCanaryTests.fs:736` |
| **AC-I2 / P-REKEY** — re-keyed row is one CDC **Update**, not Delete+Insert; source surrogates absent from sink | M2 | **TO-WIRE** (green Dev→UAT; cloud→cloud new) | extend `TransferCanaryTests.fs:330` → cloud→cloud `golden` |
| **AC-I5** — `validate-user-map` halts **before any DML** on an unmapped orphan | M2 | GREEN (confirm for cloud sink) | `TransferRefusalTests.fs:131` |
| **PE-3 re-key canary** — `(Order → User-by-email)` join identical src↔sink, users `RowsWritten=0` | M2 | **TO-WRITE** | new (cloud→cloud) |
| **LE-2 migration-preview canary** — legacy → disposition A → readback round-trips up to named tolerances | M3 | **TO-WRITE** | new |
| every foreign-schema erasure is a **named `Tolerance`**, none silent | M3 | **TO-WRITE** | `Tolerance.fs` (new variants) |
| **AC-D2 / P-DM** — zero CDC captures on idempotent redeploy | M2/M5 | GREEN | `CdcSilenceTests.fs:239` |
| **AC-D4** — exact `capture = k` on a real delta | M2/M5 | GREEN | `CdcMeasureTests` |
| survey `required ⇔ surveyed` totality | M4 | GREEN | `CapabilitySurveyTotalityTests.fs` |
| **AC-G0** — one mandatory composed `Preflight.all` the verbs route through | M4 | **TO-WIRE** (built, zero callers) | `Preflight.fs:364`; wire into `spanningPreflight` |
| survey P10 resolves user-directory readability for the re-key | M4 | partial (3 probes today) | `CapabilitySurvey.fs:180-206` (add P10 axis) |
| **OPEN-2** — UAT exposes a writable SQL connection to entity tables | M5 | **TO-WRITE** (ops spike) | PRESCOPE §13; EXEC 5.1 |
| `migrate A B` canary — one execute evolves A→B across 3 channels; data survives; re-run idempotent | M0 | GREEN | `MigrationCanaryTests.fs` |

---

## 4. Dependency graph + critical path

```
M0 (done) ──┬─► M1 (producer modelling) ──┬─► M2 (peer/golden + PE-3 canary)
            │                              └─► M3 (legacy ingest + LE-2 canary)
            └─► M4 (survey-as-G0 wiring) ──────► M5 (OPEN-2 spike → --execute)
                          │
                          └─ P10 probe gates M2's re-key

M2 / M3 / M5 ──► M6 (follow-ons, each pulled under a consumer)
```

- **Critical path to "all producers proven offline"** (ephemeral / Docker, no real UAT): `M1 → {M2, M3}` — the two new canaries (PE-3, LE-2). This is reachable **now** (no external dependency).
- **Critical path to "real cloud-UAT preview working"**: `M4 → M5` — gated by **OPEN-2**, the single biggest external dependency. Confirm OPEN-2 *first* before committing to M5.
- **M2 is gated by the survey P10 probe** (user-directory readability); until P10, the cloud→cloud re-key canary runs against an ephemeral/Docker stand-in.

---

## 5. Risk & open-decisions register

| # | Item | Why it matters | Disposition |
|---|---|---|---|
| **D-1** | **Producer modelling (M1):** `FlowSource.Peer`/`Legacy` variants vs env metadata (`estate: same\|foreign`) + flow disciplines. | `peer` needs no new ingest (same metamodel); `legacy` needs foreign-schema ingest. A variant for `legacy` may be warranted; `peer` may stay generic `Env` + the golden discipline. | **OPEN — decide at M1.** Lean: env-metadata flag, not a `FlowSource` variant, unless `legacy` ingest forces a distinct route. |
| **OPEN-2** | Does cloud UAT expose a **writable SQL connection** to entity tables (vs platform-API-only)? | Blocks all real-UAT execute (M5). The whole disposition mix (`PreservedFromSource` vs `AssignedBySink`) depends on it. | **Blocks M5. Confirm first** via the ops spike. |
| **OPEN-1** | UAT blank vs pre-populated with Users; permits `IDENTITY_INSERT`? | Sets the per-kind disposition mix and whether `peer` users are excluded vs reconciled. | Resolved by survey P3 + P10 (M4). |
| **OPEN-3/5/6** | UAT CDC-tracked? · bulk-lane two-phase cycle-breaking · CHECK/computed/trigger schema (`NOCHECK`) | Affect the executor's safety + batching. | Resolved by survey P11/P7/P8 (M4); 6.A.2 cyclic is M6. |
| **OPEN-7** | Connection-apparatus scope (how many envs, concurrency). | Sizes `TransferConnections`. | Low; design at M5. |
| **R-1** | **Survey posture** — advisory until the per-pair V2-driver flip, not a hard G0. | Per R6, V2 owns no production write path yet; a survey hard-stop would seize a gate V2 doesn't hold. | **Settled** (DECISIONS 2026-06-09): wire advisory at M4; hard refusal is a per-pair operator action. |
| **R-2** | **AC-G0 / A1 disagreement** — the ontology scorecard says `Preflight.all` HELD; `CONFIRMED_BACKLOG` A1 says OPEN (zero callers). | Determines whether M4 is "wire it" or "already wired." | **Trust CONFIRMED (OPEN).** Verify the live call site at M4 open. |
| **R-3** | **`legacy` foreign-schema ingest (LE-1)** is genuinely hard — the legacy schema is unknown/foreign; drift-by-SsKey only helps where identity is shared. | M3 is the highest-uncertainty milestone. | Scope LE-1 against a real legacy schema sample before committing; `synthetic` profiled from legacy is the privacy-safe fallback preview. |
| **R-4** | All of M2/M3/M5 stay inside **R6** (preview/migration tooling). | A producer that silently became a production write path violates the governance frame. | Hold: `--go` + `PROJECTION_ALLOW_EXECUTE`, dry-run default, cloud sink `grant: data` DML-only. |

---

## 6. Disciplines to hold (carried from `THE_DATA_PRODUCERS.md` §7)

- **Direction is a binding, not an identity** — the producer is a `DataOrigin`/`FlowSource` value, the
  disposition a `Realization`; nothing hard-codes "cloud" into a type.
- **Identity is the only deep problem** — `peer` re-keys (machinery exists), `legacy` discovers
  (foreign-schema ingest), `synthetic` mints.
- **Total decisions, named skips / no silent drop** — every excluded user, foreign-schema tolerance,
  and unmapped orphan is named (refusal or declared `Tolerance`).
- **The canary is the forcing function** — no producer is "done" without its data canary green in the
  warm Docker pool.
- **Operator-facing strings obey `THE_VOICE.md`**; **R6 holds** for every live write.
