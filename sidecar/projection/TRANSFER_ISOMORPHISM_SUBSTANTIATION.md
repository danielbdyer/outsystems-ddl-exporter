# Transfer — Isomorphism Substantiation: concordance + the OPEN-2 survey instrument

> **Status / framing.** Companion to **`PRESCOPE_TRANSFER.md`** (canonical) and
> the Wave-6 `EXECUTION_PLAN.md` §III. This doc **introduces no new
> architecture.** It does two things:
>
> 1. Records the **concordance** between an *external design exploration* (the
>    "transfer-engine / isomorphism-substantiation" conversation, developed
>    off-repo) and the **shipped** Transfer epic — the exploration independently
>    re-derived the epic's structure, which is itself a useful triangulation
>    (two independent derivations, one shape).
> 2. Harvests the **two genuinely-additive contributions** the exploration
>    carries that the epic does not yet hold: a concrete **operator-run
>    capability-survey instrument for OPEN-2 / OPEN-1 / OPEN-5 / OPEN-6** (the
>    epic *names* the ops spike but ships no instrument), and a **trigger-gated
>    capture-technique candidate** (set-based `MERGE … OUTPUT` surrogate
>    capture).
>
> Canonical surfaces unchanged: `PRESCOPE_TRANSFER.md`, `Transfer.fs`,
> `SurrogateRemap.fs`, `TransferRun.fs`, `DataLoadPlan.fs`, `Reconciliation.fs`.
> The locked lexicon (`DECISIONS 2026-05-24`) is used throughout.

---

## 0. Why this doc exists (honest framing)

An external design exploration re-derived the bidirectional transfer
architecture from first principles, ignorant of the sidecar. It converged —
independently — on exactly what the epic already ships: a direction-neutral
plan with direction-specific realizations (A35/A36), identity as the only deep
problem, a three-way disposition, surrogate-key capture-and-remap, and an
OutSystems-authority round-trip proof. **That convergence is the signal worth
recording:** the epic's shape is not arbitrary; a second derivation lands on it.

The exploration's *architecture* is therefore redundant with — and less mature
than — `PRESCOPE_TRANSFER.md`. What is **not** redundant is (a) a concrete
instrument for the epic's single biggest external dependency (OPEN-2), and (b)
a capture technique that addresses the per-row/bulk correlation tension
(§5.3 / §6.2, OPEN-5). This doc keeps only those, mapped onto the locked
vocabulary.

---

## 1. Concordance — the external framing maps onto the epic

| External-exploration term | Sidecar canonical (file:line) |
|---|---|
| "Direction is a binding, not architecture" | `SubstrateRole = Source \| Sink` (`Transfer.fs:28-30`); PRESCOPE_TRANSFER §0 habit 2, verbatim |
| "Symmetric plan engine; emit vs live are two actuators" | A35/A36 direction-neutral plan + realizations; `DataLoadPlan` (`DataLoadPlan.fs:49-60`), `Bulk.copyRows` / `Deploy.executeStream` |
| `IdentityStrategy { PreserveExact, PlatformAssigned }` | `IdentityDisposition { PreservedFromSource \| AssignedBySink \| ReconciledByRule }` (`SurrogateRemap.fs:65-83`) — richer (3 variants), `ofKind` classifier (`:78-83`) |
| "Re-key via captured surrogate keys" | `SurrogateRemapContext { Assignments : Map<SsKey, Map<SourceKey, AssignedKey>> }` + `capture` (`SurrogateRemap.fs:95-152`) |
| "Cross-environment user re-key (Dev→UAT)" | `ReconciledByRule` + `Reconciliation.reconcileKind` (`Reconciliation.fs:42-84`); `MatchByColumn \| ManualOverride` |
| "Correlation key — can't map old→new by the surrogate" | PRESCOPE_TRANSFER §5.3 / §6.2, verbatim ("row correlation needs a business/natural key, not the synthesized SsKey") |
| "Fail-loud on dropped rows" | `DroppedReferencesExit = 9`, `exitCodeForReport` (`TransferRun.fs:408`, `:428-429`) — Wave-6 **6.A.1**, shipped |
| "Sync gate, OutSystems-as-authority" | the canary / `PhysicalSchema` / H-050 adjunction; data-level canary (PRESCOPE_TRANSFER §10 Slice C) |
| "Cloud capability survey (DML-only? writable?)" | **OPEN-2** (PRESCOPE_TRANSFER §13; `EXECUTION_PLAN.md` 5.1) — see §2 |
| "Isomorphism substantiation" | the Wave-6 L1→L2→L3 ladder; H-050 extended schema→data (PRESCOPE_TRANSFER §0) |

The exploration adds no lexicon. It is a second witness to the epic's design.

**The producer / disposition layer (cross-reference, 2026-06-09).** The Transfer's data origin is
one of three **producers** — `synthetic` / `legacy` / `peer` — and its cloud sink is the model's
physical `OSUSR_*` **disposition A** (the on-prem target being the logical **disposition B**); A/B are
two realizations of one identity-stable model (the Realization name-space, not two times). The
"OutSystems-authority round-trip proof" / data-level canary (row 8 above; PRESCOPE_TRANSFER §10 Slice C)
specializes **per producer**: `synthetic` → `π ∘ σ ≈ id` (`SyntheticCanaryTests`, green); `legacy` →
the **B→A reverse-leg round-trip** (the logical on-prem model → physical cloud; same `SsKey` model, no foreign tolerances — the foreign→logical mapping is the migration team's, upstream); `peer` → the re-key canary (the
`Order → User-by-email` join identical across source and sink while the source user surrogates are
provably absent from the sink — `ReconciledByRule` + `Reconciliation.reconcileKind`, row 5/6 above). The
`peer`/`golden` flow **excludes user rows and re-keys their FKs** by email. The catalogue is
**`THE_DATA_PRODUCERS.md`**; this doc remains the survey/capture-technique harvest.

---

## 2. Contribution A — the OPEN-2 capability-survey instrument

**The gap.** OPEN-2 is flagged as *"the single biggest external dependency …
confirm first."* `EXECUTION_PLAN.md` 5.1 names the resolution as *"an ops
spike — attempt a `Microsoft.Data.SqlClient` connection to a throwaway UAT
entity table and test `IDENTITY_INSERT`/INSERT."* That is the right move but a
one-line sketch. The exploration produced the **detailed instrument**: a
sequential probe sheet an operator runs **as the real least-privilege login**
against a **throwaway UAT entity table**, recording each outcome. The point is
not just "can we connect" — it is to discover *which disposition is even
available*, because a DML-only managed surface forecloses `PreservedFromSource`
(no `IDENTITY_INSERT`) and forces `AssignedBySink`.

**Each probe names the OPEN it resolves and the disposition/slice it gates:**

| # | Probe (run as the DML-only login, against a sandbox entity table) | Resolves | Gates |
|---|---|---|---|
| P1 | `sys.fn_my_permissions(NULL,'DATABASE')` + object-scope on the table — enumerate granted perms | OPEN-2 envelope | everything below |
| P2 | `INSERT` omitting the identity column; read the platform-assigned key back | OPEN-1 | `AssignedBySink` viability (`SurrogateRemap.fs`) |
| P3 | `SET IDENTITY_INSERT <t> ON` — expect **denied** on managed tables | OPEN-1 | `PreservedFromSource` viability (`Bulk` `KeepIdentity`) |
| P4 | `MERGE … OUTPUT INSERTED.<pk>, S.<src> INTO @map` (source-column capture) | — | **Contribution B**; set-based capture |
| P5 | `DECLARE @t TABLE(...)` and `CREATE TABLE #t` — `OUTPUT INTO` targets | — | how the key-map is rendered server-side |
| P6 | `DELETE` vs `TRUNCATE` (TRUNCATE needs `ALTER`) | OPEN-6 | fresh-replacement scope-clear |
| P7 | multi-row `VALUES` ceiling; parameter ceiling; `INSERT BULK` (`SqlBulkCopy`) permission | OPEN-5 | live-executor batch sizing |
| P8 | `ALTER … NOCHECK CONSTRAINT` / disable-trigger — expect **denied** | OPEN-6 | loading into a constrained schema |
| P9 | `sys.*` / `INFORMATION_SCHEMA` read | — | reconcile profiling (read Sink population, `reconcileAgainstSink` `TransferRun.fs:208-234`) + `verify-data` |
| P10 | platform user-directory table readable; how keyed | OPEN-7 | `ReconciledByRule` Dev→UAT user re-key |
| P11 | explicit `BEGIN TRAN … COMMIT`; `@@LOCK_TIMEOUT` | OPEN-3 / OPEN-5 | chunk-commit granularity vs CDC (§8.4) |

**Disposition the survey resolves to:** if P3 denied + P2 works ⇒
`AssignedBySink` is the live path; if a managed user directory pre-exists (P10)
⇒ `ReconciledByRule` for the User kind; `PreservedFromSource` survives only if
P3 is permitted (the blank-target case PRESCOPE_TRANSFER §6.1 assumes). A real
load mixes all three (PRESCOPE_TRANSFER §6.4 OPEN-1).

**Governance.** Running the survey is an **ops action**, not a write path — it
touches a throwaway table and is the spike `EXECUTION_PLAN.md` 5.1 already
sanctions. It does not engage `--execute` (still gated by
`PROJECTION_ALLOW_EXECUTE=1`, `TransferArgs.fs:21`) and changes no R6 posture.
Until results land, the dry-run `PreservedFromSource` path remains the
deliverable (PRESCOPE_TRANSFER §2; `EXECUTION_PLAN.md` 5.1 status).

---

## 3. Contribution B — set-based `MERGE … OUTPUT` surrogate capture (trigger-gated candidate)

**Today (shipped).** `AssignedBySink` capture is per-row: `insertCaptureRow`
issues `INSERT … OUTPUT inserted.<pk>` one row at a time, omitting the identity
column so the Sink mints the key, then feeds `SurrogateRemapContext.capture`
(`TransferRun.fs:101-130`, `:171-175`). Per-row correlation is trivial (you
insert one row, you get its key) — but it is **O(rows) round-trips**, Slice E
shipped **acyclic only**, and the high-throughput `Bulk`/`SqlBulkCopy` lane
**returns no assigned ids**, so it cannot capture at all (PRESCOPE_TRANSFER
§5.3 / §6.2; OPEN-5). That is the tension: *correlatable but slow* (per-row) vs
*fast but uncorrelatable* (bulk).

**The technique.** `MERGE`'s `OUTPUT` clause may reference columns of the
`USING` **source** that were not inserted — which `INSERT`'s `OUTPUT` cannot.
So a single set-based statement captures `(source → assigned)` **without a
natural key**:

```
MERGE INTO <sink> AS T
USING (VALUES (<old_key>, <cols…>), …) AS S(old_key, <cols…>)
   ON 1 = 0                                   -- force NOT MATCHED ⇒ pure insert
WHEN NOT MATCHED THEN
   INSERT (<cols…>) VALUES (S.<cols…>)        -- identity omitted; Sink mints it
OUTPUT INSERTED.<pk>, S.old_key INTO @map;    -- S.old_key is the correlation
```

`@map` is exactly the `(SourceKey → AssignedKey)` pairing
`SurrogateRemapContext.capture` already consumes. **This is a realization-layer
swap only** (A36 — bulk-vs-incremental is realization policy): it feeds the
same `SurrogateRemapContext`, threads through the same `writePlan` phase-2
re-point (`TransferRun.fs:155-182`), and changes **no IR and no lexicon**.

**Trigger (IR grows under evidence — do not pre-build).** Build it only when
**both** fire: (a) survey **P4** confirms `MERGE` + `OUTPUT INTO @table` is
permitted under the real UAT grant; **and** (b) a real `AssignedBySink` load at
volume makes per-row round-trips the measured bottleneck (OPEN-5). Until then
the shipped per-row `INSERT … OUTPUT` is correct and sufficient.

**Caveats (named, not hand-waved).** `MERGE` carries historical bug surface —
target compat 160, test against the canary substrate. `@table`-var availability
is itself survey **P5**. This does **not** resolve **6.A.2** (cyclic
`AssignedBySink` keys phase-2 on the source PK, gone once the Sink mints — an
independent fix) nor **6.A.3** (composite-identity capture). It is one
throughput/correlation refinement, scoped precisely.

---

## 4. What this does NOT change

- **No new architecture or lexicon.** `PRESCOPE_TRANSFER.md` +
  `SurrogateRemap.fs` + `TransferRun.fs` + `DataLoadPlan.fs` remain canonical;
  the locked terms (`DECISIONS 2026-05-24`) are used as-is.
- **R6 holds.** Transfer stays UAT-preview, dry-run-default; `--execute` stays
  gated by `PROJECTION_ALLOW_EXECUTE=1`. The survey is an ops spike, not a write
  path.
- **D9 holds.** Connection references stay out of `Config`; the survey uses the
  real login out-of-band.
- **Both contributions are trigger-gated candidates,** not speculative builds.
  Contribution A is an *ops deliverable* (run + relay); Contribution B builds
  only on the P4 + OPEN-5 double-trigger.
- **No axiom cashed.** This de-risks OPEN-2 and thereby unblocks the §8.6
  data-level-adjunction substantiation; it does not promote it. The adjunction
  axiom is scaffolded at the Transfer chapter open and cashed at its close
  (PRESCOPE_TRANSFER §8.6), not here.

---

## 5. Provenance

This doc folds an external design exploration into the sidecar under the
domain-first-naming + IR-grows-under-evidence disciplines. The architecture it
references is the epic's, pre-existing and more mature; the additive content is
the OPEN-2 survey instrument (§2) and the `MERGE … OUTPUT` capture candidate
(§3). Read `PRESCOPE_TRANSFER.md` for the canonical epic; read this for the
survey to run next and the capture refinement to hold in reserve.
