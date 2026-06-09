# THE_DATA_PRODUCERS.md — the three origins that feed cloud insertion

> **Status: design (2026-06-09, course-corrected).** Synthesizes a multi-turn operator
> co-design into one surface. One of the three producers (`synthetic`) is **BUILT**
> (`THE_SYNTHETIC_DATA_DESIGN.md`); the other two (`legacy`, `peer`) are **design-only** —
> the Transfer engine they run on is built (`PRESCOPE_TRANSFER.md`), the producer wiring +
> canaries are not. This document is the *concept* that unifies them: what the three
> producers ARE, the dispositions they move between, and the proof that earns each.
>
> **Scope guard:** the v2 application at `sidecar/projection` only. V1 is out of scope.
>
> **Reading order.** This sits beside `THE_SYNTHETIC_DATA_DESIGN.md` (the `synthetic`
> producer in full) and `PRESCOPE_TRANSFER.md` (the bidirectional pipeline the `legacy` /
> `peer` producers run on). `THE_CLI.md` §4 is the operator surface; `THE_USE_CASE_ONTOLOGY.md`
> §3 (P-3, the User re-key protein) is the golden-data chain; `WAVE_6_ALGEBRA.md` is the
> torsor whose A/B this document identifies with the realization name-space.
>
> **For the execution plan** — current-state preflight, milestone graph, acceptance/test-case
> matrix, critical path, and risk register — see `PREFLIGHT_CLOUD_INSERTION.md` (indexed in
> `CONFIRMED_BACKLOG_2026_06_09.md` §J, the canonical "what's left" surface).

---

## 0. The one idea — cloud insertion is the *up* leg into the physical rendition

The engine's daily act is direction-neutral: **`emit(B ⊖ A)`**, put a model where it is
asked, read *A* from the target, emit the minimal change (`THE_CLI.md` §1). *Cloud
insertion* — writing production-like rows **up** into a live cloud OutSystems environment
(`cloud-uat`, a `direct` sink with `grant: data`, DML-only) — is not a new pipeline. It is
that same act with the **sink bound to the cloud**, and the content sourced from one of three
origins.

The reframe that organizes everything below:

> **A and B are not two points in time. They are two *renditions of one
> identity-stable model.*** `A` = the **physical `OSUSR_*` rendition** (the cloud
> OutSystems realization; `Realization = physical`). `B` = the **logical on-prem rendition**
> (the hosted schema-migration model; `Realization = logical`). Same `SsKey` identity, same
> model, same concern — two renditions of the one model.

This *coincides* with — is "inherently the same as" — the torsor's A/B (`WAVE_6_ALGEBRA.md`:
State is a torsor over Delta, `⊖ = between`, `⊕ = applyDiff`) and with the §5.8 **Realization**
name-space (`THE_USE_CASE_ONTOLOGY.md`: Identity / Designation / Realization). The realization
stops being "a steady-state emission policy plus an out-of-scope cutover regime" and becomes
**the two first-class renditions the bidirectional model moves between.**

**The full bidirectional flow** (the operator's actual loop):

1. **Down-leg (A→B):** export the cloud schema into the **logical model**; host it on-prem
   (rendition **B**, clean logical names). *(The existing forward projection / publication.)*
2. **Off-engine:** the **migration team loads the data into the hosted logical model.** Any
   foreign→logical mapping is *theirs*, upstream of our engine.
3. **Up-leg (B→A):** the **same model in reverse** pipes that data **back up into the physical
   model** (rendition **A**, the "frozen physical / `OSUSR_*`" version). **This is cloud insertion.**

So "feed cloud insertion" = **realize the model's data in rendition A**, from one of three
origins. An environment's **`rendition: physical | logical`** flag (the env-metadata flag,
operator decision 2026-06-09) names which rendition it bears — that, plus whether the source is
generated, *is* the producer distinction below.

---

## 1. The three producers — what each IS

Per `THE_SYNTHETIC_DATA_DESIGN.md` §1, the flow surface has four source substrates:
`cloud-self`, `peer` (was `sibling-cloud` — §4 naming note), `on-prem-legacy`, and
`synthetic-from-profile` (`THE_CLI.md` §1 names the producer trinity directly). Three are the
producers of production-like data into the cloud sink. **All three move the same `SsKey`-stable
model** — none is foreign schema. They differ by *what the origin is* and *which rendition it
bears*:

| Producer | Origin (what it IS) | Rendition move | Built? |
|---|---|---|---|
| **`synthetic`** | Data *generated* to match a captured `Profile` — never read from a live row. `σ : Profile ⟶ Data`. | → physical cloud (A) | **BUILT** |
| **`peer`** | Another already-deployed cell of the **same model at the same (physical) rendition** — e.g. `cloud-qa`. `SsKey`-stable; only per-DB surrogates differ. | physical → physical (A→A) | design-only |
| **`legacy`** | The **logical on-prem rendition** (B) of the same model — the hosted model the **migration team populated**. The foreign→logical mapping is theirs, upstream. | logical → physical (**B→A, the reverse / up leg**) | design-only |

The load-bearing distinction between **`peer`** and **`legacy`** is the **source's
rendition**, *not* foreign-vs-same schema (both are the same model):

- `peer` reads a cell at the **same (physical) rendition** as the cloud sink — a peer of the
  estate lattice (`THE_USE_CASE_ONTOLOGY.md` §3). Same logical identities; only surrogate keys
  differ per DB (`Id=280` in QA, `Id=18` in UAT). Sink-minted identity + a **Reidentify re-key**
  suffices.
- `legacy` reads the **logical (B) rendition** — the cross-rendition **reverse leg**. The data
  arrived in B via the migration team's upstream mapping; our engine only ever sees the logical
  model, and pipes it up into the physical (A) rendition. Identity is reconciled the same way
  (by `SsKey` / business key), because **it is the same model** — there is no foreign schema for
  the engine to translate.

> The `rendition` env-metadata flag is exactly what tells the engine a `peer` source (physical)
> from a `legacy` source (logical), hence the move's direction and which rendition to read/emit.

---

## 2. The `golden` discipline — exclude users, re-key their FKs

`golden` is the `peer` producer's flagship flow: a subset (`tables: [...]`) of a peer cell's
data promoted into the cloud sink. Its identity contract (operator decision, §4):

> **User rows are not copied. Every FK that *references* a user is re-keyed to the sink's
> own user identities.**

Concretely, against the sink (`cloud-uat`) which already holds its own user inventory:

1. **Exclude** `dbo.User` (and the user-table family) from the copied set — the sink keeps
   its own users; none are overwritten or imported.
2. **Reconcile** each source user to a sink user by **business key (email)** — the
   `ByEmail` rule of P-3 (`THE_USE_CASE_ONTOLOGY.md` §3, the User re-key protein;
   `Reconcile` / `Rekey` on `MovementSpec`).
3. **Reidentify** every `CreatedBy` / `UpdatedBy` / any `dbo.User` FK on the copied rows to
   the sink's matching user surrogate — a re-keyed row is an **Update** (relationship
   preserved modulo surrogate), never Delete+Insert (the P-REKEY discriminating predicate,
   §4.3 Reidentify cell).

The gate is **`validate-user-map` before any DML** (P-3 faithfulness stakes): every
source user FK must resolve to a valid sink identity, or the load fails loud — an unmapped
orphan is a refusal, not a silent `NULL` or a `FallbackToSystemUser` unless that fallback is
explicitly declared. This is the same protocol as the `uat` flow's Dev→UAT re-key; `golden`
applies it cloud→cloud within the same estate. *(The `legacy` reverse leg reuses the same
user-reconcile machinery if the cloud sink owns its own users — same model, same identity rule.)*

---

## 3. Validation — the data canary, one per producer

Each producer earns cloud insertion the same way every axis is earned: a **forcing-function
canary**, not a claim. The canaries are siblings of the schema canary (`Ingestion ∘
Projection = id`, P-9) extended to the data plane (`PRESCOPE_TRANSFER.md` North Star — the
data-level H-050; the engine's data-level canary, `TransferCanaryTests`, already exists).

- **`synthetic`** → `π ∘ σ ≈ id` (`THE_SYNTHETIC_DATA_DESIGN.md` §1, `SyntheticCanaryTests`):
  generate from `P`, load, re-profile to `P′`, assert `P′ ≈ P` (L1 structural exact: zero FK
  orphans, counts; L2 marginals within ε). **Green.**
- **`peer`** → the **re-key canary** (`THE_USE_CASE_ONTOLOGY.md` §4.3 Reidentify measurement):
  the `(Order → User-by-email)` join is identical across source and sink while the source's user
  surrogates are provably **absent** from the sink — proof the re-key is an Update, not a
  re-import, and that no peer-cell identity leaked. *(The Dev→UAT instance of this is green;
  the cloud→cloud `golden` instance is to write.)*
- **`legacy`** → the **reverse-leg (B→A) round-trip canary**: read the logical on-prem model (B),
  pipe up into the physical cloud (A), read back, assert the data round-trips. Same `SsKey` model
  — **no foreign-schema tolerances** (the foreign→logical mapping was the migration team's,
  upstream); identity reconciled as for any same-model move.

All three are **preview/migration tooling, not a production write path** — they live inside
R6 (`CLAUDE.md` load-bearing commitments; `PRESCOPE_TRANSFER.md` §8): the cloud sink is
`grant: data` (DML-only), every live write needs both `--go` (intent) and
`PROJECTION_ALLOW_EXECUTE` (authorization), and the data canary gates regression.

---

## 4. Locked decisions (operator, 2026-06-09)

1. **The producer trinity is the cut.** `synthetic` / `legacy` / `peer` are the three
   origins of production-like data feeding cloud insertion, distinguished by *what the origin
   is* and *which rendition it bears* — **generated** / the **logical-rendition reverse leg** /
   a **same-rendition peer cell** — not by engine behavior. All three move the same `SsKey` model.
2. **`peer` is the concept-shaped name** for what `THE_SYNTHETIC_DATA_DESIGN.md` §1 called
   **`sibling-cloud`**. Per the domain-first naming discipline (`CLAUDE.md` pillar 8): the
   load-bearing property is "a peer cell of the same `SsKey`-stable estate lattice"
   (`THE_USE_CASE_ONTOLOGY.md` §3 lattice vocabulary) — which is *why* sink-minted identity +
   re-key suffices. The rename is **propagated** (2026-06-09): `THE_SYNTHETIC_DATA_DESIGN.md`
   §1 and `THE_CLI.md` §1 now read `peer`.
3. **`legacy` is the reverse (B→A) leg of the same model — not foreign schema.** The migration
   team loads the data into the hosted **logical** model (B) upstream of the engine; `legacy`
   pipes that data **back up into the physical** cloud model (A, the "frozen physical / `OSUSR_*`"
   version). The engine only ever sees the one logical model; there is no foreign-schema ingest
   and no translation tolerance on the engine's side.
4. **`golden` = exclude user rows, re-key their FKs** to the sink's user identities by
   `ByEmail` reconcile, gated by `validate-user-map` before any DML (§2).
5. **A and B are two renditions of one model, not two times** (§0): `A` = physical
   `OSUSR_*` (cloud), `B` = logical on-prem. This identifies the §5.8 Realization name-space
   with the `WAVE_6_ALGEBRA.md` torsor's A/B; cloud insertion is the *up* leg into rendition A.
6. **`rendition: physical | logical` is an env-metadata flag, not a `FlowSource` variant**
   (operator decision). Each environment is marked with the rendition it bears (physical OSUSR
   cloud / logical on-prem). The flag distinguishes a `peer` source (physical) from a `legacy`
   source (logical) and tells the engine the move's direction + which rendition to read/emit.
   "estate" was rejected as the flag name; the values are `physical | logical`.

---

## 5. Build grounding — reusable machinery (file:line, confirmed 2026-06-09)

The Transfer engine all three producers run on is built; what is unbuilt is the per-producer
*wiring* + *canary*, the `rendition` flag, and — for any physical-cloud sink — the physical
(`OSUSR_*`) rendition emission. Anchors:

| Need | Anchor |
|---|---|
| Movement axes (the resolved flow) | `DataOrigin` (`Model`/`Synthetic of profile`/`NoData`/`FromTarget`), `FlowSource` (`Env`/`Model`/`Synthetic`/`NoData`), `Rekey: string option`, `Reconcile: string list`, `Tables: string list` — `src/Projection.Pipeline/MovementSpec.fs` |
| Direction-neutral plan | `DataLoadPlan.build : Catalog → TopologicalOrder → Map<SsKey, StaticRow list> → SurrogateRemapContext → DataLoadPlan` `src/Projection.Core/DataLoadPlan.fs:81` |
| Write seam (sink load) | `writePlan` / `writePlanResumable` / `wipeFkOrdered` `src/Projection.Pipeline/TransferRun.fs:582-597`; realizations `Bulk.copyRows` / `Deploy.executeStream` |
| Topo order (FK-first) | `TopologicalOrderPass.runWith TreatAsCycle catalog` `src/Projection.Pipeline/TransferRun.fs:546` |
| Identity disposition | `IdentityDisposition { PreservedFromSource \| AssignedBySink \| ReconciledByRule }` + `ofKind` `src/Projection.Core/SurrogateRemap.fs:65-83`; `SurrogateRemapContext` + `capture` `:95-152` |
| Re-key by business key (the `peer`/`golden` + P-3 + `legacy` path) | `Reconciliation.reconcileKind` `src/Projection.Core/Reconciliation.fs:42-84`; rules `MatchByColumn \| ManualOverride` (email = `MatchByColumn "Email"`) |
| Fail-loud on dropped rows | `DroppedReferencesExit = 9`, `exitCodeForReport` `src/Projection.Pipeline/TransferRun.fs:408,428-429` (Wave-6 6.A.1, shipped) |
| Data-level canary (the proof shape) | **BUILT** — `tests/Projection.Tests/TransferCanaryTests.fs` (the data extension of H-050; incl. a Dev→UAT user re-key at `:330`); the schema sibling is the P-9 canary |
| `synthetic` producer (BUILT) | `SyntheticData.generate` `src/Projection.Core/SyntheticData.fs`; `Transfer.runSynthetic` `TransferRun.fs`; `ProfileCodec` `src/Projection.Targets.Json/ProfileCodec.fs`; canary `tests/Projection.Tests/SyntheticCanaryTests.fs` |
| Capability survey (the gate) | OPEN-2 instrument — `TRANSFER_ISOMORPHISM_SUBSTANTIATION.md` §2; probe **P10** (platform user-directory table readable / how keyed) gates the `peer`/`golden` re-key |
| **`rendition: physical \| logical` env flag** | **to build (M1)** — env-metadata on the environment; distinguishes `peer` (physical source) from `legacy` (logical source); marks the source/sink rendition for the move |
| **Cross-rendition write-target resolution (M1.5)** | **scoped to `legacy`** (probe 2026-06-09) — the transfer writes using the **source** contract's names (`TransferRun.fs:909-918`); physical `OSUSR_*` round-trips as-read (no OSUSR generator needed). Only the `legacy` B→A leg (logical source → physical sink) needs the write target resolved per-`SsKey` against the **sink** catalog. `peer` (A→A) is unaffected. |

---

## 6. Build slices + acceptance, per producer

Each producer is "done" when its data canary (§3) is green in the warm Docker pool. Listed
in ascending build cost.

**Cross-rendition note (scoped to `legacy`; probe 2026-06-09):** the transfer writes using the
**source** contract's names (`TransferRun.fs:909-918`), so physical `OSUSR_*` names round-trip
as-read — there is no OSUSR *generator* and none is needed. The one gap is the `legacy` B→A leg
(logical source → physical sink): the write target must be resolved per-`SsKey` against the **sink**
catalog (M1.5). `peer` (A→A, same rendition) and `synthetic` need no such resolution — the matching
physical names are the right target. So **M2 below is reachable now**; M1.5 is an M3 prerequisite.

### `synthetic` — **BUILT**
Engine + durable codec + CLI + canary all green (`THE_SYNTHETIC_DATA_DESIGN.md`). The reference
implementation of the producer shape.
**Acceptance:** `SyntheticCanaryTests` (`π ∘ σ ≈ id`) green; privacy property green.

### `peer` (the `golden` flow) — machinery mostly exists; wire + prove
The re-key machinery is the same as P-3 (Dev→UAT); `golden` is the cloud→cloud (A→A) instance.
Slices:
- **PE-1 — user exclusion + email re-key on the `golden` flow.** Exclude the `dbo.User` family
  from the copied set (it is absent from `tables`, so the engine must *not* load or wipe it), and
  route the user-FK columns through `ReconciledByRule` with `Reconciliation.reconcileKind` keyed
  `MatchByColumn "Email"` against the sink's user inventory.
- **PE-2 — the `validate-user-map` gate (before any DML).** Every source user FK must resolve to a
  valid sink user; an unmapped orphan fails loud (`DroppedReferencesExit`), no silent `NULL`, no
  `FallbackToSystemUser` unless explicitly declared (P-3 faithfulness stakes).
- **PE-3 — the re-key canary.** The `Order → User-by-email` join is identical across source and
  sink **and** the source user surrogates are provably absent from the sink (the row is an
  Update, not a re-import; P-REKEY).
**Gate:** survey probe P10 (user-directory readable / how keyed).
**Acceptance:** PE-3 green; a re-keyed row captures **one** CDC Update, never Delete+Insert.

### `legacy` (the `preview` flow) — the reverse (B→A) leg of the same model
Not foreign-schema ingest. The migration team has already loaded the data into the hosted
**logical** model (B); `legacy` pipes it **up into the physical** cloud (A). For the engine this
is a direction-neutral Transfer with **source rendition = logical, sink rendition = physical** of
the same `SsKey` model. Slices:
- **LE-1 — the reverse-leg transfer.** Read from the logical on-prem source (B), write to the
  physical cloud sink (A); same model, no foreign mapping. Identity reconciled by `SsKey` /
  business key as for any same-model move (reuse the §5 re-key machinery if the cloud owns its
  own users). Depends on the `rendition` flag (M1) + cross-rendition write-target resolution (M1.5).
  - **Engine + canary GREEN (M3/LE-2, 2026-06-09):** the engine half is the 6.B.2 two-contract
    `runWithRenames` path — it ingests with the logical (source) contract, re-points row column-
    values onto the sink names by SsKey, and resolves the write target against the SINK contract's
    `kind.Physical` per-`SsKey`. So table AND column rendition resolve for free; the LE-2 canary
    proves a logical `[dbo].[Customer].[Email]` round-trips up into physical
    `[dbo].[OSUSR_XF_CUSTOMER].[CONTACT]`. No OSUSR generator, no new write-path code.
  - **Flow-recognition FACE landed (M3.b, 2026-06-09):** `Command.reverseLegOf` (pure, tested)
    recognizes a flow as the B→A reverse leg from the M1 `rendition` flag (live `logical` source →
    live `physical` sink) and resolves its two connections; `TransferRun.runReverseLeg` is the thin
    engine face that delegates to the LE-2-proven `runWithRenames` **given the two contracts**.
  - **The remaining piece (the per-flow runner arm — deferred):** *where do the two SsKey-aligned
    contracts come from for a real configured flow?* `runWithRenames` needs a source contract at
    the **logical** rendition and a sink contract at the **physical** (OSUSR) rendition whose
    SsKeys **align by construction**. The LE-2 canary supplies them as authored stable-SsKey
    contracts (the rename-canary precedent). For a live two-DB flow, ReadSide SsKeys are
    **name-derived**, so reading the two DBs independently does **not** align them — that needs
    either a **shared authored model rendered in both renditions** (the migration-team flow already
    uses an authored model) OR **attribute-scope `V2.SsKey` recovery in ReadSide**
    (`ReadSide.buildAttribute`, the durable alternative — a separate, larger piece). The per-flow
    runner arm that picks up the two contracts and calls `runReverseLeg` is the remaining wiring.
- **LE-2 — the reverse-leg (B→A) round-trip canary.** Pipe B→A, read back, assert the data
  round-trips. **No foreign-schema tolerances** — same model both ways.
**Note:** `synthetic` profiled from the same on-prem data (`profile: onprem-...`) is the
privacy-safe preview; `legacy` is the real-row reverse leg. They share the sink and the rendition;
they differ in whether real rows cross.
**Acceptance:** LE-2 green; the B→A round-trip preserves the data (no silent drop); identity
reconciled, not re-imported.

### Exit criteria (the layer is execute-complete when)
All three producer canaries green; the `golden` flow excludes users and re-keys by email under a
passing P10; the physical (`OSUSR_*`) rendition is emitted/matched for the cloud sink; every
cross-boundary erasure (an unmapped user; any reconcile drop) is named; all three stay inside R6
(preview/migration tooling; `--go` + `PROJECTION_ALLOW_EXECUTE` for any live write; the cloud sink
is `grant: data`, DML-only).

---

## 7. Disciplines to hold (do not break without writing the amendment first)

- **Direction is a binding, not an identity** (`PRESCOPE_TRANSFER.md` §0): a substrate is a
  `Source` or `Sink` *per Transfer*; an environment bears a `rendition` (physical / logical).
  Code that hard-codes "cloud" or "the producer" into a type is a smell — the producer is a
  `DataOrigin` / `FlowSource` value, the rendition an env-metadata flag.
- **Identity is the only deep problem.** Schema and value-codec round-trip for free
  (H-050 + `RawValueCodec`); the design lives in surrogate reconciliation under DML-only sink
  rights (`IdentityDisposition` + `SurrogateRemapContext`). `peer` re-keys (surrogates differ
  across cells); `legacy` reconciles the same-model identities on the reverse leg; `synthetic`
  mints. None translates foreign schema — that is the migration team's, upstream.
- **Total decisions, named skips / no silent drop**: every excluded user, every unmapped orphan,
  every reconcile drop is **named** (a refusal or a declared tolerance), never silently dropped.
- **The canary is the forcing function** (§3): no producer is "done" without its data canary
  green in the warm Docker pool.
- **Operator-facing strings obey `THE_VOICE.md`** (stative, agentless, no pronouns; the
  change-norm / counts surfaced as evidence; the estate is never "your").
