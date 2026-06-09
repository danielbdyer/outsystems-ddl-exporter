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
3. **`legacy` (`preview`)** — the **reverse (B→A) leg** of the *same* logical model: the migration
   team loads the data into the hosted logical model (B) upstream; `legacy` pipes it **up into the
   physical cloud** (A). Same `SsKey` model, **no foreign schema** (the foreign→logical mapping is the
   migration team's, off-engine); proven by the **B→A round-trip canary**.
4. **The gate is real** — the capability survey resolves the per-kind disposition mix (incl. **P10**
   user-directory for the re-key), wired into the run verbs (advisory per R6, S3).
5. **The real-UAT execute path is unblocked** — **OPEN-2** (is the cloud UAT a writable SQL
   connection?) resolved by the ops spike; `--execute` gated by `--go` + `PROJECTION_ALLOW_EXECUTE`,
   dry-run default, preview row-cap. *(The legacy B→A leg additionally needs cross-rendition
   write-target resolution, M1.5; synthetic/peer do not.)*
6. **Every cross-boundary erasure is named** (an unmapped user; any reconcile drop) — none silent.

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
| `Preflight.all` / `allReporting` aggregate gate | **WIRED** (M4 G0a, 2026-06-09) | `Preflight.fs:364`; first production caller is `runCore`'s pre-plan gate composition (`Preflight.all [cdcGate; spanningGate]`). **= CONFIRMED_BACKLOG A1 (closed for the transfer Execute path; migrate verb still hand-chains)** |
| `AssignedBySink` cyclic / composite-identity | refused (acyclic only) | acyclic SHIPPED 2026-05-31; cyclic self-ref IDENTITY + composite are named refusals → follow-ons |
| Scoped-delete arm (`DeleteScope`) | BUILT, emitters hardcode `None` | `= CONFIRMED_BACKLOG A3` |
| `--resumable` flag | SHIPPED this session | `a317c07` (`= A2`) |

### DESIGN-ONLY — spec'd this session, no code
| Component | Status | Note |
|---|---|---|
| `peer` / `legacy` producers | DESIGN-ONLY | Distinguished by source `rendition` (peer=physical, legacy=logical), not a `FlowSource` variant. `THE_DATA_PRODUCERS.md` |
| `rendition: physical \| logical` env flag | DESIGN-ONLY (to build, M1) | env-metadata; marks A (physical OSUSR) vs B (logical on-prem); distinguishes peer-source from legacy-source |
| `golden` cloud→cloud user-exclusion + email re-key | **BUILT** (M2, 2026-06-09) | `TransferCanaryTests` `PE-3 golden` / `PE-2 golden` (Docker, green); the wipe + LoadSet correctness fixes in `TransferRun.fs` (`wipeTargets`) |
| `legacy` reverse-leg (B→A) transfer | **BUILT** (M1.5/M3, 2026-06-09) | The 6.B.2 two-contract path (`runWithRenames`) resolves table+column rendition by SsKey against the sink contract; LE-2 canary green |
| Cross-rendition write-target resolution (B→A) | MISSING (scoped to legacy) | The transfer writes using the **source** contract's names (`TransferRun.fs:909-918`); physical `OSUSR_*` round-trips as-read (no generator needed). A logical-source→physical-sink (legacy) leg needs the write target resolved per-`SsKey` against the **sink** catalog. Confirmed by probe 2026-06-09. |
| `peer` re-key canary (cloud→cloud) | **BUILT** (M2) | `TransferCanaryTests` `PE-3 golden` (green); pure wipe-set witness `TransferRefusalTests` `PE-1` ×3 |
| `legacy` B→A round-trip canary | **BUILT** (M3) | `TransferCanaryTests` `M3/LE-2` (Docker, green) |

---

## 2. Milestones to the finish line

`M0` is done; `M1`–`M6` are ordered by dependency. Each cites its design origin
(PRESCOPE slice / OPEN-N / `THE_DATA_PRODUCERS` slice / AC-id) and its `CONFIRMED_BACKLOG` §J id.

| Milestone | Scope | Depends on | Origin | §J |
|---|---|---|---|---|
| **M0** ✅ | synthetic producer · Transfer core · data-level canary · Dev→UAT re-key · migrate-with-data | — | Slices 1/A/B/C/C′/D/E (acyclic) | — |
| **M1** | **`rendition` env flag + routing.** Add `rendition: physical \| logical` to each environment (operator decision 2026-06-09); the engine reads it to set source/sink rendition and route the producer semantics (`peer`=physical source, `legacy`=logical source). *(Was the M1 modelling fork — decided: env-metadata flag, not a `FlowSource` variant.)* | M0 | `THE_DATA_PRODUCERS` §4 decision 6; `MovementSpec.fs:150` | J1 |
| **M1.5** ✅ | **Cross-rendition write-target resolution (B→A) — CONFIRMED BUILT (2026-06-09).** The engine half is the **6.B.2 two-contract path** (`runWithRenames` / `runReconcilingWithRenames` → `runCore` with `ingestion=(sourceContract, renameMap)`): it ingests with the source (logical) contract, re-points row column-values onto the sink names by SsKey (rename map), and **resolves the write target table against the SINK contract's `kind.Physical` per-`SsKey`** (the plan is built against the sink `catalog`; the write seam uses `kind.Physical` from it). So both table-name AND column-name rendition resolve for free — no OSUSR generator, no new write-path code. Proven by the LE-2 canary. **Residual (live two-DB flow only, deferred):** ReadSide synthesizes name-derived SsKeys, so two independently-read DBs would not align — needs attribute-scope `V2.SsKey` recovery (`ReadSide.buildAttribute`) OR a shared authored model. The migration-team flow uses an authored model, so this is not on the canary path. | M0 | `TransferRun.fs:782,825` `runWithRenames`; LE-2 canary | J1 |
| **M2** ✅ | **`peer` / `golden` cloud→cloud.** PE-1 user exclusion + email re-key (incl. two correctness fixes: the wipe never deletes a ReconciledByRule kind; the LoadSet keeps reconcile-source kinds); PE-2 `validate-user-map` halts before DML; **PE-3 the re-key canary** (cloud→cloud). *(Built directly on the engine + reconcile — the offline canary did not require the M1 env flag; survey P10 + cell name-coincidence are real-cloud concerns.)* | M0 | `THE_DATA_PRODUCERS` §6 PE-1..3; AC-I2/I5; P-3 | J2 |
| **M3** ✅ | **`legacy` / `preview` — the B→A reverse leg.** LE-1 the reverse-leg transfer (logical on-prem source → physical cloud sink; same model, no foreign ingest) — done via `runWithRenames`; **LE-2 the B→A round-trip canary** — GREEN (`TransferCanaryTests` `M3/LE-2`: logical `[dbo].[Customer].[Email]` → physical `[dbo].[OSUSR_XF_CUSTOMER].[CONTACT]`, data round-trips by SsKey). *(Authored SsKey-stable contracts, per the rename-canary precedent — ReadSide SsKeys are name-derived.)* **Remaining:** M3.b flow-wiring (route a `legacy` flow to `runWithRenames`) + M1 env surface — both deferred as CLI/config surface. | M1.5 | `THE_DATA_PRODUCERS` §6 LE-1/2; LE-2 canary | J3 |
| **M4** ◑ | **The gate is real.** **G0a DONE (2026-06-09):** `runCore`'s pre-plan Execute gates (CDC → spanning G1/G2) now compose through one mandatory `Preflight.all` (= CONFIRMED A1's first production caller); codes + precedence preserved (CDC canary + AC-I5/PE-1 + classify suite green). **Deferred (additive):** G0b the P10 user-directory probe (`ReadSide`/`CapabilitySurvey`); G0c advisory-survey surfacing in the run verbs (R6: warn, never hard-stop). | M0 | `TransferRun.fs` `Preflight.all`; DECISIONS 2026-06-09 (S3); CONFIRMED A1 | J4 |
| **M5** | **Real-UAT execute (OPEN-2).** The ops spike — a throwaway-UAT `Microsoft.Data.SqlClient` connection probing `IDENTITY_INSERT`/INSERT/grants (resolves OPEN-1/2/3/5/6 + the disposition mix); then `--execute` under R6 with `--preview-row-cap`. *(Synthetic/peer execute needs no M1.5; legacy-into-real-UAT additionally needs M1.5 + M3.)* | M4 | PRESCOPE Slice D / §13; EXEC 5.1; OPEN-2 | J5 |
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
| **AC-I2 / P-REKEY** — re-keyed row is an Update not a re-import; source surrogates absent from sink | M2 | **GREEN** (cloud→cloud) | `TransferCanaryTests` `PE-3 golden` |
| **AC-I5** — `validate-user-map` halts **before any DML** on an unmapped orphan | M2 | **GREEN** (cloud→cloud) | `TransferRefusalTests` AC-I5 (pure) + `TransferCanaryTests` `PE-2 golden` |
| **PE-3 re-key canary** — `(Order → User-by-email)` join identical src↔sink, users `RowsWritten=0`, source surrogates absent | M2 | **GREEN** | `TransferCanaryTests` `PE-3 golden` |
| **PE-1 wipe-set** — wipe excludes ReconciledByRule kinds + respects the LoadSet (golden users never wiped) | M2 | **GREEN** (pure) | `TransferRefusalTests` `PE-1` ×3; `Transfer.wipeTargets` |
| **LE-2 B→A round-trip canary** — logical on-prem (B) → physical cloud (A) → readback round-trips (same model, no foreign tolerances) | M3 | **TO-WRITE** | new |
| **LE-2 / cross-rendition** — logical B → physical OSUSR A, table+column rendition by SsKey, exact round-trip (no tolerances) | M1.5/M3 | **GREEN** | `TransferCanaryTests` `M3/LE-2` via `runWithRenames` |
| **AC-D2 / P-DM** — zero CDC captures on idempotent redeploy | M2/M5 | GREEN | `CdcSilenceTests.fs:239` |
| **AC-D4** — exact `capture = k` on a real delta | M2/M5 | GREEN | `CdcMeasureTests` |
| survey `required ⇔ surveyed` totality | M4 | GREEN | `CapabilitySurveyTotalityTests.fs` |
| **AC-G0** — one mandatory composed `Preflight.all` the pre-plan gates route through | M4 | **GREEN** (G0a) | `TransferRun.runCore` → `Preflight.all [cdcGate; spanningGate]`; CDC canary + classify suite witness |
| survey P10 resolves user-directory readability for the re-key | M4 | partial (3 probes today) | `CapabilitySurvey.fs:180-206` (add P10 axis) |
| **OPEN-2** — UAT exposes a writable SQL connection to entity tables | M5 | **TO-WRITE** (ops spike) | PRESCOPE §13; EXEC 5.1 |
| `migrate A B` canary — one execute evolves A→B across 3 channels; data survives; re-run idempotent | M0 | GREEN | `MigrationCanaryTests.fs` |

---

## 4. Dependency graph + critical path

```
M0 (done) ──► M1 (rendition flag) ──┬─► M2 (peer/golden + PE-3 canary)   ← reachable NOW
            │                       └─► M1.5 (cross-rendition target) ──► M3 (legacy B→A + LE-2 canary)
            └─► M4 (survey-as-G0 wiring) ──────► M5 (OPEN-2 spike → --execute)

M2 / M3 / M5 ──► M6 (follow-ons, each pulled under a consumer)
   P10 probe gates M2's re-key (real cloud only)
```

- **Critical path to "all producers proven offline"** (ephemeral / Docker, no real UAT): `M1 → {M2 ; M1.5 → M3}` — the two new canaries (PE-3, LE-2). **M2 is reachable now** (same-rendition OSUSR→OSUSR, no M1.5); M3 needs M1.5 (cross-rendition target resolution).
- **Critical path to "real cloud-UAT preview working"**: `M4 → M5`, gated by **OPEN-2** (the biggest external dependency). Synthetic/peer into real UAT need no M1.5; only `legacy`-into-real-UAT additionally needs M1.5 + M3. Confirm OPEN-2 *first*.
- **M2's re-key against a *real* cloud is gated by survey P10** (user-directory readability) — and by `OSUSR_*` name coincidence across cells (eSpace-id question, OPEN-2 class); the **offline** PE-3 canary (matching OSUSR names on two ephemeral DBs) needs neither.

---

## 5. Risk & open-decisions register

| # | Item | Why it matters | Disposition |
|---|---|---|---|
| **D-1** | **Producer modelling (M1).** | How `peer`/`legacy` are distinguished and routed. | **SETTLED (2026-06-09):** an env-metadata flag **`rendition: physical \| logical`**, *not* a `FlowSource` variant. Both producers move the same `SsKey` model; the source's rendition (physical peer cell vs logical on-prem) is the cut. "estate" rejected as the name. |
| **OPEN-2** | Does cloud UAT expose a **writable SQL connection** to entity tables (vs platform-API-only)? | Blocks all real-UAT execute (M5). The whole disposition mix (`PreservedFromSource` vs `AssignedBySink`) depends on it. | **Blocks M5. Confirm first** via the ops spike. |
| **OPEN-1** | UAT blank vs pre-populated with Users; permits `IDENTITY_INSERT`? | Sets the per-kind disposition mix and whether `peer` users are excluded vs reconciled. | Resolved by survey P3 + P10 (M4). |
| **OPEN-3/5/6** | UAT CDC-tracked? · bulk-lane two-phase cycle-breaking · CHECK/computed/trigger schema (`NOCHECK`) | Affect the executor's safety + batching. | Resolved by survey P11/P7/P8 (M4); 6.A.2 cyclic is M6. |
| **OPEN-7** | Connection-apparatus scope (how many envs, concurrency). | Sizes `TransferConnections`. | Low; design at M5. |
| **R-1** | **Survey posture** — advisory until the per-pair V2-driver flip, not a hard G0. | Per R6, V2 owns no production write path yet; a survey hard-stop would seize a gate V2 doesn't hold. | **Settled** (DECISIONS 2026-06-09): wire advisory at M4; hard refusal is a per-pair operator action. |
| **R-2** | **AC-G0 / A1 disagreement** — the ontology scorecard says `Preflight.all` HELD; `CONFIRMED_BACKLOG` A1 says OPEN (zero callers). | Determines whether M4 is "wire it" or "already wired." | **Trust CONFIRMED (OPEN).** Verify the live call site at M4 open. |
| **R-3** | **`legacy` is the B→A reverse leg, *not* foreign-schema ingest** (course-corrected 2026-06-09). The migration team does the foreign→logical mapping upstream; the engine only sees the same logical model. | Earlier framing (foreign ingest + translation tolerances) was wrong and is corrected across the corpus. M3 collapses to a same-model reverse-leg transfer + canary. | **Resolved framing.** Residual uncertainty is M1.5 (physical-rendition emission), not foreign ingest. |
| **R-5** | **Cross-rendition write-target resolution (M1.5)** — the transfer writes using the *source* contract's names (probe 2026-06-09, `TransferRun.fs:909`). A logical-source→physical-sink (legacy B→A) leg must resolve targets against the *sink* catalog by `SsKey`. | Scoped to M3/legacy only. M2/peer (A→A) is unaffected — same rendition, source names are the right target. Real-cloud caveat: `OSUSR_*` names embed an eSpace id that may differ across cells (a survey/OPEN-2 question, not an offline-canary blocker). | **Build at M3.** M2's offline canary proves the transfer mechanics on matching OSUSR names now. |
| **R-4** | All of M2/M3/M5 stay inside **R6** (preview/migration tooling). | A producer that silently became a production write path violates the governance frame. | Hold: `--go` + `PROJECTION_ALLOW_EXECUTE`, dry-run default, cloud sink `grant: data` DML-only. |

---

## 6. Disciplines to hold (carried from `THE_DATA_PRODUCERS.md` §7)

- **Direction is a binding, not an identity** — the producer is a `DataOrigin`/`FlowSource` value, the
  disposition a `Realization`; nothing hard-codes "cloud" into a type.
- **Identity is the only deep problem** — `peer` re-keys (surrogates differ across cells), `legacy`
  reconciles the same-model identities on the B→A reverse leg, `synthetic` mints. None translates
  foreign schema — that is the migration team's, upstream.
- **Total decisions, named skips / no silent drop** — every excluded user, every unmapped orphan,
  every reconcile drop is named (refusal or declared `Tolerance`).
- **The canary is the forcing function** — no producer is "done" without its data canary green in the
  warm Docker pool.
- **Operator-facing strings obey `THE_VOICE.md`**; **R6 holds** for every live write.
