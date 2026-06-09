# THE_DATA_PRODUCERS.md — the three origins that feed cloud insertion

> **Status: design (2026-06-09).** Synthesizes a multi-turn operator co-design into one
> surface. One of the three producers (`synthetic`) is **BUILT**
> (`THE_SYNTHETIC_DATA_DESIGN.md`); the other two (`legacy`, `peer`) already exist as
> **named flows** in `THE_CLI.md` §4.3 (`preview`, `golden`) and ride the **Transfer**
> machinery (`PRESCOPE_TRANSFER.md`). This document is the *concept* that unifies them:
> what the three producers ARE, the disposition they render into, and the proof that earns
> each. It does not introduce a new engine — it names a layer already half-built.
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

## 0. The one idea — cloud insertion is the *up* leg into the physical disposition

The engine's daily act is direction-neutral: **`emit(B ⊖ A)`**, put a model where it is
asked, read *A* from the target, emit the minimal change (`THE_CLI.md` §1). *Cloud
insertion* — writing production-like rows **up** into a live cloud OutSystems environment
(`cloud-uat`, a `direct` sink with `grant: data`, DML-only) — is not a new pipeline. It is
that same act with the **sink bound to the cloud** instead of an on-prem target, and the
content sourced from one of three origins.

The reframe that organizes everything below:

> **A and B are not two points in time. They are two *dispositions of one
> identity-stable model.*** `A` = the **physical `OSUSR_*` rendition** (the cloud
> OutSystems realization; `Realization = physical`). `B` = the **on-prem schema-migration
> target** (`Realization = logical`). Same `SsKey` identity, same model, same concern —
> two realizations. B-shaped data is "more production-like" only because B is the migration
> target; it is the same content rendered differently.

This *coincides* with — is "inherently the same as" — the torsor's A/B (`WAVE_6_ALGEBRA.md`:
State is a torsor over Delta, `⊖ = between`, `⊕ = applyDiff`) and with the §3 **Realization**
name-space (`THE_USE_CASE_ONTOLOGY.md` §3 cell schema, physical vs logical). The
consequence is that the physical/logical realization stops being "a steady-state emission
policy plus an out-of-scope cutover regime" and becomes **the two first-class dispositions
the bidirectional model moves between**: moving *up* into cloud renders disposition `A`
(physical); projecting *down* to on-prem renders disposition `B` (logical). A producer
generates *the model's data*; the disposition is just the realization the chosen sink uses.

So "feed cloud insertion" = **realize the model's data in disposition A**, from one of three
origins.

---

## 1. The three producers — what each IS

Per `THE_SYNTHETIC_DATA_DESIGN.md` §1, the flow surface has four source substrates:
`cloud-self`, `peer` (was `sibling-cloud` — §4 naming note), `on-prem-legacy`, and
`synthetic-from-profile` (`THE_CLI.md` §1 now names the producer trinity directly). Of these,
**three are the producers of production-like data into the cloud sink** — distinguished by
*what the origin is*, not by what the engine does with it:

| Producer | Origin (what it IS) | Flow (`THE_CLI.md` §4.3) | Built? |
|---|---|---|---|
| **`synthetic`** | Data *generated* to match a captured `Profile` — never read from a live row. `σ : Profile ⟶ Data`. | `from: synthetic, profile: file:<p>, to: cloud-uat` | **BUILT** |
| **`legacy`** | A **foreign-schema** substrate (the on-prem legacy application) read and mapped *through* the logical schema by `SsKey`. Different metamodel; not OutSystems-shaped. | `preview: onprem-legacy → cloud-uat` | flow exists; Transfer machinery |
| **`peer`** | A **peer cell of the same estate** — another already-deployed OutSystems environment (e.g. `cloud-qa`). `SsKey`-stable, same metamodel; only the per-DB surrogates differ. | `golden: cloud-qa → cloud-uat` | flow exists; Transfer + rekey |

The load-bearing distinction between **`legacy`** and **`peer`** is *not* "which
environment" (both are `FlowSource.Env` in `MovementSpec.fs`). It is the **estate
relationship**:

- `legacy` is a **foreign** substrate — its schema is not the model's schema. Identity must
  be *discovered* (mapped through), values *translated*. It is the hardest fidelity case
  and the natural home of profile-driven preview (hence `synthetic` is "profile the legacy,
  iterate").
- `peer` is the **same `SsKey`-stable estate** at a different cell of the
  `(environment × release-time)` lattice (`THE_USE_CASE_ONTOLOGY.md` §3). Same logical
  identities; only the surrogate keys differ per DB (`Id=280` in QA, `Id=18` in UAT). This
  is *exactly why* sink-minted identity + a **Reidentify re-key** suffices for `peer` and is
  insufficient for `legacy` — the property that earns the name (§1.1 identity facts; the
  Reidentify cell, `THE_USE_CASE_ONTOLOGY.md` §4.3).

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
applies it cloud→cloud within the same estate.

> **Why `golden` is `peer`, not a fourth thing.** The re-key works *because* the source is a
> same-estate peer: the user business keys (emails) and every other logical identity are the
> same set across the two cells — only the surrogates differ. A `legacy` source has no such
> guarantee; its identity reconciliation is a harder, schema-foreign problem. The producer
> taxonomy and the identity discipline are the same cut seen twice.

---

## 3. Validation — the data-level canary, one per producer

Each producer earns cloud insertion the same way every axis is earned: a **forcing-function
canary**, not a claim. The canaries are siblings of the schema canary (`Ingestion ∘
Projection = id`, P-9) extended to the data plane (`PRESCOPE_TRANSFER.md` North Star — the
data-level H-050).

- **`synthetic`** → `π ∘ σ ≈ id` (`THE_SYNTHETIC_DATA_DESIGN.md` §1, `SyntheticCanaryTests`):
  generate from `P`, load, re-profile to `P′`, assert `P′ ≈ P` (L1 structural exact: zero FK
  orphans, counts; L2 marginals within ε). **Green.**
- **`legacy`** → the **migration-preview canary**: read the legacy substrate, project into
  disposition A, read back, assert the logical content round-trips up to the *named*
  translation tolerances (the foreign-schema mappings — each declared, none silent).
- **`peer`** → the **re-key canary** (`THE_USE_CASE_ONTOLOGY.md` §4.3 Reidentify
  measurement): the `(Order → User-by-email)` join is identical across source and sink while
  the source's user surrogates are provably **absent** from the sink — proof the re-key is an
  Update, not a re-import, and that no peer-cell identity leaked.

All three are **preview/migration tooling, not a production write path** — they live inside
R6 (`CLAUDE.md` load-bearing commitments; `PRESCOPE_TRANSFER.md` §8): the cloud sink is
`grant: data` (DML-only), every live write needs both `--go` (intent) and
`PROJECTION_ALLOW_EXECUTE` (authorization), and the data canary gates regression.

---

## 4. Locked decisions (operator, 2026-06-09)

1. **The producer trinity is the cut.** `synthetic` / `legacy` / `peer` are the three
   origins of production-like data feeding cloud insertion, distinguished by *what the origin
   is* (generated / foreign-schema / same-estate peer), not by engine behavior.
2. **`peer` is the concept-shaped name** for what `THE_SYNTHETIC_DATA_DESIGN.md` §1 called
   **`sibling-cloud`**. Per the domain-first naming discipline (`CLAUDE.md` pillar 8): the
   load-bearing property is "a peer cell of the same `SsKey`-stable estate lattice"
   (`THE_USE_CASE_ONTOLOGY.md` §3 lattice vocabulary) — which is *why* sink-minted identity +
   re-key suffices. The rename is **propagated** (2026-06-09): `THE_SYNTHETIC_DATA_DESIGN.md`
   §1 and `THE_CLI.md` §1 now read `peer`.
3. **`golden` = exclude user rows, re-key their FKs** to the sink's user identities by
   `ByEmail` reconcile, gated by `validate-user-map` before any DML (§2).
4. **A and B are two dispositions of one model, not two times** (§0): `A` = physical
   `OSUSR_*` (cloud), `B` = logical on-prem target. This identifies the §3 Realization
   name-space with the `WAVE_6_ALGEBRA.md` torsor's A/B; cloud insertion is the *up* leg into
   disposition A.

---

## 5. Build grounding — reusable machinery (file:line, confirmed 2026-06-09)

The engine for all three producers exists; what is unbuilt is the per-producer *ingest* and
*canary*, not the plan/write/identity core. Anchors:

| Need | Anchor |
|---|---|
| Movement axes (the resolved flow) | `DataOrigin` (`Model`/`Synthetic of profile`/`NoData`/`FromTarget`), `FlowSource` (`Env`/`Model`/`Synthetic`/`NoData`), `Rekey: string option`, `Reconcile: string list`, `Tables: string list` — `src/Projection.Pipeline/MovementSpec.fs` |
| Direction-neutral plan | `DataLoadPlan.build : Catalog → TopologicalOrder → Map<SsKey, StaticRow list> → SurrogateRemapContext → DataLoadPlan` `src/Projection.Core/DataLoadPlan.fs:81` |
| Write seam (sink load) | `writePlan` / `writePlanResumable` / `wipeFkOrdered` `src/Projection.Pipeline/TransferRun.fs:582-597`; realizations `Bulk.copyRows` / `Deploy.executeStream` |
| Topo order (FK-first) | `TopologicalOrderPass.runWith TreatAsCycle catalog` `src/Projection.Pipeline/TransferRun.fs:546` |
| Identity disposition | `IdentityDisposition { PreservedFromSource \| AssignedBySink \| ReconciledByRule }` + `ofKind` `src/Projection.Core/SurrogateRemap.fs:65-83`; `SurrogateRemapContext` + `capture` `:95-152` |
| Re-key by business key (the `peer`/`golden` + P-3 path) | `Reconciliation.reconcileKind` `src/Projection.Core/Reconciliation.fs:42-84`; rules `MatchByColumn \| ManualOverride` (email = `MatchByColumn "Email"`) |
| Fail-loud on dropped rows | `DroppedReferencesExit = 9`, `exitCodeForReport` `src/Projection.Pipeline/TransferRun.fs:408,428-429` (Wave-6 6.A.1, shipped) |
| `synthetic` producer (BUILT) | `SyntheticData.generate` `src/Projection.Core/SyntheticData.fs`; `Transfer.runSynthetic` `TransferRun.fs`; `ProfileCodec` `src/Projection.Targets.Json/ProfileCodec.fs`; canary `tests/Projection.Tests/SyntheticCanaryTests.fs` |
| Capability survey (the gate) | OPEN-2 instrument — `TRANSFER_ISOMORPHISM_SUBSTANTIATION.md` §2; probe **P10** (platform user-directory table readable / how keyed) gates the `peer`/`golden` re-key |
| Data-level canary (the proof) | **planned** — `PRESCOPE_TRANSFER.md` §10 Slice C (the data extension of H-050); the schema sibling is the P-9 canary |

---

## 6. Build slices + acceptance, per producer

Each producer is "done" when its data canary (§3) is green in the warm Docker pool. Listed
in ascending build cost.

### `synthetic` — **BUILT**
Engine + durable codec + CLI + canary all green (`THE_SYNTHETIC_DATA_DESIGN.md`). No new work;
the reference implementation of the producer shape.
**Acceptance:** `SyntheticCanaryTests` (`π ∘ σ ≈ id`) green; privacy property green.

### `peer` (the `golden` flow) — machinery mostly exists; wire + prove
The re-key machinery is the same as P-3 (Dev→UAT); `golden` is the cloud→cloud instance into
disposition A. Slices:
- **PE-1 — user exclusion + email re-key on the `golden` flow.** Exclude the `dbo.User` family
  from the copied set (it is absent from `tables`, so the engine must *not* attempt to load or
  wipe it), and route the user-FK columns through `ReconciledByRule` with
  `Reconciliation.reconcileKind` keyed `MatchByColumn "Email"` against the sink's user inventory.
- **PE-2 — the `validate-user-map` gate (before any DML).** Every source user FK must resolve to a
  valid sink user; an unmapped orphan fails loud (`DroppedReferencesExit`), no silent `NULL`, no
  `FallbackToSystemUser` unless explicitly declared (P-3 faithfulness stakes).
- **PE-3 — the re-key canary.** The `Order → User-by-email` join is identical across source and
  sink **and** the source user surrogates are provably absent from the sink (proof the row is an
  Update, not a re-import; P-REKEY).
**Gate:** survey probe P10 (user-directory readable / how keyed).
**Acceptance:** PE-3 green; a re-keyed row captures **one** CDC Update, never Delete+Insert.

### `legacy` (the `preview` flow) — the hardest; foreign-schema ingest + named tolerances
The legacy app's schema is **not** the model's schema, so identity and values must be mapped
*through* the logical schema (by `SsKey` / business key), not read 1:1. Slices:
- **LE-1 — foreign-schema ingest** that lifts the legacy substrate into the model's logical shape
  by `SsKey` (drift-by-SsKey, `THE_SYNTHETIC_DATA_DESIGN.md` §6 — B-only columns get type-defaults;
  legacy-only columns are ignored), with every translation that cannot be expressed faithfully
  surfaced as a **named tolerance**, never a silent drop.
- **LE-2 — the migration-preview canary**: read the legacy substrate, project into disposition A,
  read back, assert the logical content round-trips **up to the named translation tolerances**.
**Note:** `synthetic` profiled from the legacy substrate (`profile: onprem-legacy`) is the
privacy-safe preview of this same data; `legacy` is the real-row preview. They share the sink and
the disposition; they differ in whether real rows cross the boundary.
**Acceptance:** LE-2 green; every foreign-schema erasure is a named `Tolerance`, none silent.

### Exit criteria (the layer is execute-complete when)
All three producer canaries green; the `golden` flow excludes users and re-keys by email under a
passing P10; every cross-boundary erasure (foreign-schema translation, unmapped user) is named;
all three stay inside R6 (preview/migration tooling; `--go` + `PROJECTION_ALLOW_EXECUTE` for any
live write; the cloud sink is `grant: data`, DML-only).

---

## 7. Disciplines to hold (do not break without writing the amendment first)

- **Direction is a binding, not an identity** (`PRESCOPE_TRANSFER.md` §0): a substrate is a
  `Source` or `Sink` *per Transfer*. Code that hard-codes "cloud" or "the producer" into a
  type is a smell — the producer is a `DataOrigin` / `FlowSource` value, the disposition a
  `Realization`.
- **Identity is the only deep problem.** Schema and value-codec round-trip for free
  (H-050 + `RawValueCodec`); the design lives in surrogate reconciliation under DML-only sink
  rights (`IdentityDisposition` + `SurrogateRemapContext`). `peer` re-keys; `legacy`
  discovers; `synthetic` mints.
- **Total decisions, named skips / no silent drop**: every excluded user, every foreign-schema
  mapping tolerance, every unmapped orphan is **named** (a refusal or a declared tolerance),
  never silently dropped.
- **The canary is the forcing function** (§3): no producer is "done" without its data canary
  green in the warm Docker pool.
- **Operator-facing strings obey `THE_VOICE.md`** (stative, agentless, no pronouns; the
  change-norm / counts surfaced as evidence; the estate is never "your").
