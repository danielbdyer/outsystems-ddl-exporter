# THE_CLI.md — the operator surface

This document is the **target design of the `projection` command-line surface**: the
collapse of today's verb sprawl into one act — *move a model from one environment to
another* — whose every variation lives in **named config**, not in command variety. It is
a **vision surface**, sibling to `THE_VOICE.md` (the register), `THE_STORYBOARD.md`
(the scene-by-scene), and `THE_VOICE_INTEGRATION.md` (the build plan). It is target-first;
the current CLI (`src/Projection.Cli/Program.fs`) is the provenance, not the contradiction.

Provenance: re-derived 2026-06-08 from `THE_USE_CASE_ONTOLOGY.md` (the ten axes, the nine
proteins, T16), `WAVE_6_ALGEBRA.md` (`emit(B ⊖ A)`, the torsor, the norm = CDC capture
count), and `WAVE_6_ONTOLOGY.md` (the DacFx-owns-schema / engine-owns-data seam, the
publication-and-provenance premise). This revision **supersedes** the 2026-06-07
four-verb-plus-flags shape: a re-grounding against the operator's real workflows showed
the variation does not belong in the *command* — it belongs in *named environments and
flows*. The interface is **a namespace of outcomes over one act, and the namespace is the
config file.**

---

## 1. The one idea

There is one act — **move a model from a source environment to a target environment** —
and the engine computes the minimal correct change to make the target match.

> **Deploy, migrate, load, transfer, and export are the same act.** They differ only in
> *A* (the state already at the target) and in *where the content comes from*. The
> operator never chooses "deploy or migrate." The direction is *put this there*; the
> engine reads *A* and emits `B ⊖ A` — the minimal change, measured in CDC captures.

That sentence is simultaneously the Wave-6 algebra (`emit(B ⊖ A)`, *A* read from the
substrate, the norm `‖·‖` the CDC capture count) and the design principle (it just works).
The same statement is true in both meaning-spaces; that coincidence is how the cut is
known to be right.

The verb sprawl was never the engine's algebra. It was a *namespace of outcomes over one
input* — and a namespace's natural home is **config**, not a tree of verbs or a matrix of
flags. So the named outcomes (lift-and-shift dev, golden-data into UAT, legacy preview)
become **named flows** the operator writes once and runs trivially.

The data feeding a flow comes from one of three **producers** — `synthetic` (generated from a profile),
`legacy` (the foreign-schema on-prem app), and `peer` (a same-estate OutSystems cell, e.g. `cloud-qa`;
formerly "sibling-cloud"). Writing *up* into a live cloud environment (`cloud-uat`) is **cloud insertion**:
the `emit(B ⊖ A)` act rendering the model in its physical `OSUSR_*` disposition (A) rather than the
logical on-prem one (B). The producer trinity, the `golden` user-exclusion-plus-re-key discipline, and the
A/B-as-dispositions reframe are catalogued in `THE_DATA_PRODUCERS.md`.

---

## 2. The cost model — configure once, run rarely

The operator changes things *few and far between*. That fixes the optimization axis: not
expressiveness-per-invocation, but **configure-once, run-trivially**.

- **Configure once** (amortized over months): bear all the complexity here — the
  environments, their permissions, the flows, the rekey map.
- **Run rarely, trivially**: the information-theoretic floor per run is two bits —
  *which flow*, and *preview or for-real*. Everything else is read from config.

So the daily command degenerates toward `projection <flow> [--go]`, and the design's job
is to make that true without losing any capability.

---

## 3. The daily surface

```
projection <flow>                  # preview the flow (default; nothing is committed)
projection <flow> --go             # apply it  (a live write also needs PROJECTION_ALLOW_EXECUTE=1)
projection <flow> --fresh --go     # from-scratch wipe-and-load of this one target (rare, deliberate)
projection <flow> --allow-drops    # accept a declared destructive loss (rare, deliberate)
projection                         # list the flows and their resolved source → target
```

The verb is implied: the first token is a **flow name** unless it is one of the small,
closed set of secondary verbs (`check` / `explain` / `seal` / `report` / `init`). An
unknown first token is refused with the known flow + verb list named.

Three per-run words, and only three, because they are the only decisions that genuinely
vary at the moment of action and must never be persisted in a file:

| Word | What it is | Why it is per-run, never config |
|---|---|---|
| `--go` | the operator's **intent** to apply a live write | a config that auto-commits is a footgun; intent is stated at the moment |
| `--fresh` | the **wipe-and-load** posture (`realize(B)`, the non-minimal fallback) | "this target always wipes" is a footgun; the destructive posture is chosen each time |
| `--allow-drops` | acceptance of a **declared loss** (drop / narrow / scoped delete) | the loss is affirmed at the moment, never defaulted out of a file |

Everything else — where content comes from, the rekey map, the table subset, the target's
permission and delivery mechanism — is a **fact about the flow or the environment**, and
lives in config.

---

## 4. Two config layers — environments and flows

`projection.json` (or `$PROJECTION_CONFIG`) has two blocks. **Environments** are the
*places* (defined once, with their connection reference and permissions). **Flows** are
the *movements* (named source→target recipes, each conceptually a `Move`).

### 4.1 Environments — the places

Each environment carries two permission facets and an address:

- **`access`** — how the target is *reached*:
  - `bundle` — no direct write. Produce an SSDT bundle (CREATE files + RefactorLog +
    pre/post-deploy scripts + data scripts) into `out`, for **Octopus** to apply.
  - `direct` — a live connection (`conn`) the engine writes to.
  - `docker` — an ephemeral one-touch database, deployed and verified.
- **`grant`** — what may *change* there (a refusal gate, ontology axis 9):
  - `schema+data` — DDL+DML permitted; the full create/alter + data.
  - `data` — DML-only; schema must already agree. A schema-changing flow against a
    `data` target is a **type mismatch**, refused loudly (never half-applied).

D9 holds: an environment carries a connection **reference** (`env:<VAR>` / `file:<path>`),
**never a literal connection string**. Secrets stay out-of-band; only addressing lives in
the committed file.

### 4.2 Flows — the movements

A flow is a named `Move`: rows (and optionally schema) flow from a `from` environment to a
`to` environment, identity-reconciled, minimality CDC-measured. Fields:

| Field | Meaning | Default |
|---|---|---|
| `from` | the source environment (where B's content originates) — or `model` / `synthetic` / `none` | the model |
| `to` | the target environment (the destination) | required |
| `rekey` | a user-map file → **Reidentify** (Dev-cloud → UAT identity reconciliation) | off |
| `tables` | a declared subset (the partial golden-data refresh) | all |
| `profile` | for `from: synthetic` — a source environment to profile for better synthetic data | off |

### 4.3 Worked `projection.json`

```jsonc
{
  "environments": {
    "cloud-dev":     { "access": "direct", "conn": "env:CLOUD_DEV_CONN" },     // sources: read-only reach
    "cloud-qa":      { "access": "direct", "conn": "env:CLOUD_QA_CONN"  },
    "onprem-legacy": { "access": "direct", "conn": "env:ONPREM_LEGACY_CONN" },

    "onprem-dev":    { "access": "bundle", "out": "dist/onprem-dev", "grant": "schema+data" },  // SSDT → Octopus
    "onprem-qa":     { "access": "bundle", "out": "dist/onprem-qa",  "grant": "schema+data" },
    "onprem-uat":    { "access": "bundle", "out": "dist/onprem-uat", "grant": "schema+data" },

    "cloud-uat":     { "access": "direct", "conn": "env:CLOUD_UAT_CONN", "grant": "data" },     // direct, DML-only

    "docker":        { "access": "docker", "grant": "schema+data" }
  },
  "flows": {
    "dev":     { "from": "cloud-dev",     "to": "onprem-dev" },                            // lift-and-shift, unchanged
    "qa":      { "from": "cloud-qa",      "to": "onprem-qa"  },
    "uat":     { "from": "cloud-dev",     "to": "onprem-uat", "rekey": "file:users.csv" }, // dev-cloud → UAT, rekeyed
    "golden":  { "from": "cloud-qa",      "to": "cloud-uat",  "tables": ["Customer","Order"] }, // peer cell → cloud; users excluded, their FKs re-keyed by email (THE_DATA_PRODUCERS §2)
    "preview": { "from": "onprem-legacy", "to": "cloud-uat" }                              // on-prem legacy → cloud
  }
}
```

```
projection uat                   # cloud-dev → onprem-uat, rekeyed — preview the SSDT bundle
projection uat --go              # produce the bundle (bundle = file production; see §7)
projection dev --fresh --go      # from-scratch reset of onprem-dev (one target, deliberate)
projection golden --go           # cloud-qa → cloud-uat, subset (a gated live write)
projection preview               # onprem-legacy → cloud-uat — preview legacy data in UAT shape
projection docker-spin           # a flow whose `to` is docker
```

### 4.4 Discovery and precedence

- The config path is `projection.json` at the repo root, or `$PROJECTION_CONFIG`.
- A flow's specialization comes from the flow entry; the target's `access`/`grant`/`out`/
  `conn` come from the environment it names.
- `projection init` scaffolds a `projection.json` (refuses to overwrite; D9-safe refs).
- `projection` with no args lists every flow as `name: from → to (specialization)` — the
  config *is* the menu; discoverability is the listing, not a flag forest.

---

## 5. The baseline *A* — first-class, one concept, three sources

The torsor is `B ⊖ A`. A flow names where **B**'s content comes from (`from`) and where it
goes (`to`); the **baseline A is the state already at the target**, and naming it is the
refinement that closes the gap to the algebra. There is one concept — *the baseline* —
with three sources, and they unify everything previously treated as separate:

| Surface | A is | Reduces to |
|---|---|---|
| default (minimal-churn) | the target's current deployed state (read back) | `emit(B ⊖ A_now)` — minimal, CDC-aware MERGE + RefactorLog |
| `--fresh` | empty | `realize(B)` — wipe-and-load, the non-minimal `2·|table|` fallback |
| `report` (since the last schema change) | the last **sealed episode** | `emit(B ⊖ A_prior)` — the change bundle (§8) |

This is the ontology's *comparison regime* axis (§5.10): which A. It costs no new daily
surface — it is already latent in the verbs. On an empty target the default is a full
create; on an evolved one it is the differential; on an unchanged one it is **nothing**
(`‖B ⊖ A‖ = 0`, CDC-silent — idempotent redeploy falls out for free). The magic is safe
because live writes preview by default (§7): the diff is seen before it is applied.

---

## 6. `access` and `grant` — the permission axis

This is the axis the operator led with ("direct connection or file production… different
permission sets — DDL+DML, DML-only"). It is a property of the **environment**, and it
*selects the engine's whole approach*:

- **`access: bundle`** → the **declarative** path. The engine emits the adjusted
  `CREATE TABLE` + the `.refactorlog`; **DacFx computes the schema ALTER at publish**. The
  engine does not hand-emit ALTERs — it owns the *data* movement (pre/post-deploy scripts,
  measured by CDC), DacFx owns the *schema* computation. The bundle is delivered to Octopus.
- **`access: direct`** → a live write. Schema via the same declarative artifact path where
  applicable; data via the CDC-aware MERGE (default) or wipe-and-load (`--fresh`).
- **`access: docker`** → the ephemeral one-touch DB (deploy + verify), full control.

`grant` is a **refusal gate, not a setting**:

- `grant: data` against a flow that would change schema → **refused** with a named exit
  code. Schema must already agree. For `golden`/`preview` into a `data` target, the engine
  **verifies schema agreement first** and refuses on divergence — the operator's "so long
  as the schemas agree" is a gate, not a hope.
- `grant: schema+data` → schema changes permitted (via the declarative artifact DacFx
  applies; `grant` is permission, never a claim that the engine emits ALTER).

This corresponds to the ontology's **DacFx-owns-schema / engine-owns-data-measured-by-CDC**
seam (`WAVE_6_ONTOLOGY.md` §4) and to gate axis 9 (`THE_USE_CASE_ONTOLOGY.md` §4.6).

---

## 7. The safety model

In the register of `THE_VOICE.md` (stative, agentless, imperative direction):

- **A `bundle` is always-safe file production.** Producing the SSDT bundle for Octopus
  writes only files; it needs no gate. `projection uat` produces the bundle, full stop.
- **A `direct` live write previews by default.** `projection golden` states the plan and
  stops: *"Preview only. Re-run with `--go` to apply."* `--go` commits.
- **Two gates guard a live write, by design distinct.** `--go` is the operator's
  **intent**; `PROJECTION_ALLOW_EXECUTE=1` is the environment's **authorization** (R6). A
  live write needs both; the refusal names which is absent. So the gate *attaches itself*
  based on `access`: `bundle` needs neither, `direct` needs both, `--fresh`/`docker` are
  governed the same way `direct` is.
- **Loss is declared, never silent.** A destructive move (drop / narrow / scoped delete)
  refuses with the exact token to re-run: *"2 row(s) would drop
  (transfer.droppedReferences). Re-run with `--allow-drops` to accept the loss."*
- **An irrelevant modifier is noted, not silently ignored and not hard-failed**
  (no silent-drop; usability): *"`--fresh` does not apply to a bundle target; ignored."*

---

## 8. The secondary verbs — and the provenance pair

```
check                         # canary: round-trip fidelity on an ephemeral pair (default)
check drift <flow>            # the deployed target vs the model
check data  <flow>            # row-count + null-count integrity
check ready                   # the run-ledger readiness gauge

explain <flow>                # the dry-run plan: what B ⊖ A would change, before it lands
explain diff <a> <b>          # the change between two models
explain policy <a> <b>        # how two policies project differently

seal <flow>                   # eject / freeze: record this published state as a durable episode
seal approve <version>        # record a policy-version approval decision

report <flow>                 # the change bundle for the on-prem migration team
```

`check` answers *is it right?* `explain` answers *what would change?* `seal` answers *make
this permanent / sign it off.* `report` answers *what changed since last time, for the
people downstream?*

**`seal` → episode → `report` is the load-bearing pair.** `report`'s job — "what changed
since the last time the schema changed," for the on-prem migration team — is computable
only against a stored prior state. That state is exactly what `seal` records (a snapshot +
appended refactorlog = an **episode**). So:

- **`seal <flow>`** freezes "this is the published schema state now" as a durable episode.
- **`report <flow>`** (default `--since` = the last seal) emits `B ⊖ A_prior` as the
  hand-off bundle: the refactorlog deltas + the change-manifest + the move/CDC counts.

This lights up the ontology's **provenance plane** (`Accumulate`) — the plane the prior
design left dark — and gives `report` an anchor instead of leaving it floating.

---

## 9. The change-norm, surfaced

Both the preview and `report` state the norm `‖δ‖` — *"this run captures N CDC rows /
moves M rows / emits K DDL statements."* That is the **minimality proof** the operator can
read directly: minimal-churn is not a claim, it is a measured number. It is free — the
MERGE already produces the captures (`WAVE_6_ALGEBRA.md` §4: the CDC capture-row count
*is* the norm). `--fresh` shows the inflated `2·|table|` figure, so the cost of the
fallback is visible at the moment it is chosen.

---

## 10. Exit codes (carried forward, stable)

| Code | Meaning |
|---|---|
| 0 | succeeded |
| 1 | argv error (unknown flow / verb; missing input) |
| 2 | parse error (model JSON; spec; config-parse) |
| 3 | execution error (SQL rejected the change; connection open; unbreakable cycle) |
| 4 | Docker unavailable (`access: docker`; `check`) |
| 5 | fidelity divergence (`check` canary / `check drift`) |
| 6 | config error (file missing / unparseable / D9; connection-ref resolve) |
| 7 | gate refusal (`--go` without `PROJECTION_ALLOW_EXECUTE=1`; permission pre-flight) |
| 8 | data divergence (`check data` row / null) |
| 9 | refused, fail-loud (undeclared drop; **`grant: data` vs a schema change**; schema-disagreement on a data flow; tightening; verify-failed) |

---

## 11. The ontology mapping

Where each of the ten axes (`THE_USE_CASE_ONTOLOGY.md` §4.1) lands in this design:

| Axis | Where it lives |
|---|---|
| 1. Move | a **flow** (`from` → `to`), identity-reconciled by `rekey` |
| 2. Plane | schema + data via `grant`; identity via `rekey`; **provenance via `seal`/`report`** |
| 3. Deploy mode | `access` (bundle = declarative / direct = in-place) × `--fresh` (wipe-and-load) |
| 4. Temporal phase | genesis (`--fresh`) · steady-state (default) · eject (`seal`) |
| 5. Comparison regime | the **baseline A** — target-now (default) / empty (`--fresh`) / last-seal (`report`) |
| 6. Environment-lattice | the **environments** block (each a cell; substrate named in the name) |
| 7. Consumer / terminus | `access`: bundle → Octopus · direct DB · docker; `report` → the migration team |
| 8. Ordering | engine-internal (schema before data; FK two-phase; rename before reshape) |
| 9. Gate / safety | `grant` refusal · `--go` × `PROJECTION_ALLOW_EXECUTE=1` · `--allow-drops` · pre-flight refusals |
| 10. Measurement & proof | the norm `‖δ‖` surfaced (§9); `check`; `report` |

Proteins (`THE_USE_CASE_ONTOLOGY.md` §3), each a sentence:

```
P-1  Dev lift-and-shift     projection dev --go
P-2  QA  lift-and-shift     projection qa --go
P-3  UAT with re-key        projection uat --go            (rekey lives in the flow)
P-4  Migration-team bundle  report uat                     (the change since the last seal)
P-5  Idempotent redeploy    projection dev --go            (re-run; B⊖A = 0 ⇒ CDC-silent)
P-6  In-place migrate       projection dev --go            (same command; A is non-empty)
P-7  Eject / freeze         seal dev
P-8  Drift detection        check drift dev
P-9  Self-check canary      check
     —
     Docker one-touch       projection <flow-to-docker>
     Cloud golden data      projection golden --go
     Legacy preview         projection preview
     Synthetic (profiled)   a flow with from: synthetic, profile: onprem-legacy
```

P-5 and P-6 collapsing into the **identical** command as P-1 is the headline: the operator
stopped needing to know the difference. The engine reads *A* and the difference disappears.

---

## 12. Fidelity status — backed today vs in-flight engine work

The CLI is the correct **surface**; some flows lean on engine work still climbing the
isomorphism ladder (`AUDIT_2026_05_31_FIVE_AXIS_REDTEAM.md`; the Wave-6 L2/L3 backlog).
This document does not over-promise — the split is:

- **Fully backed today.** Lift-and-shift (schema+data via the SSDT bundle); `--fresh`
  (`realize(B)`); data-only `golden`/`preview` transfer; the **declared `tables` subset** on
  the data leg (golden data — only listed kinds load, the rest of the sink untouched);
  `check` (canary / drift / data / ready); the **`explain <flow>`** live preview (B vs the
  target's last sealed episode); `seal` eject; the two-gate safety model; `grant` refusal;
  the **pre-flight gates** (CDC-tracked sink + data-compat NOT-NULL tightening, refuse exit 9,
  `--allow-cdc` / `--allow-drops` to override); **attribute-level `CatalogDiff` + column-rename
  RefactorLog** (6.A.10 / 6.A.12, incl. the rename ⊥ reshape composition); **`report <flow>`**
  — the migration-team change bundle (the recorded `ChangeManifest` series from the target's
  durable store, a live `--go` recording into it).
- **Remaining, by design:**
  - **`from: synthetic --profile` (the Faker source)** — the one genuine net-new feature
    left. Profile-driven, FK-aware generation needs three new parts: a pure generator
    (`Profile` × `Catalog` × seed → `Map<SsKey, StaticRow list>`, FK keys drawn in topo
    order, distributions + null-rates honored, deterministic), a **synthetic-load runner**
    (synthetic has no source DB, so it cannot reuse `runThroughConnections` — it generates
    rows then `DataLoadPlan.build` → write to sink), and the profile capture (`LiveProfiler`
    against the `--profile` env). Scoped, not yet built.
  - **Rename-aware migrate-with-data** — the pure rename-aware transfer exists
    (`runWithRenames`, source-A → sink-B re-point); `MigrationRun.executeWithData` documents
    its precondition (the data source is at contract B). The combined rename + migrate-with-
    data re-point has no current flow consumer, so it is not built ahead of one (IR grows
    under evidence, not speculation); `runWithRenames` is ready when a flow needs it.

The discipline: **the surface ships whole; the flows that depend on a ladder rung are
named here and refuse cleanly until the rung lands** (total decisions, named skips — never
a silent half-result).

---

## 13. Decisions — locked (2026-06-08)

1. **The act is one: move a model from `from` to `to`.** The interface is a namespace of
   outcomes, and the namespace lives in **config** (environments + flows), not in verbs or
   a flag matrix.
2. **The daily command is `projection <flow> [--go] [--fresh] [--allow-drops]`** — verb
   implied; the first token is a flow unless it is a closed secondary verb.
3. **Two config layers** — `environments` (places; `access` ∈ {bundle, direct, docker} ×
   `grant` ∈ {schema+data, data}; D9 conn refs) and `flows` (named `Move` recipes:
   `from`/`to`/`rekey`/`tables`/`profile`).
4. **The baseline A is first-class — one concept, three sources** (target-now default /
   empty via `--fresh` / last-seal via `report`).
5. **Per-run words are exactly the dangerous/transient ones** — `--go` (intent), `--fresh`
   (wipe-and-load), `--allow-drops` (declared loss). Everything else is config.
6. **`access` selects the approach; `grant` is a refusal gate** — bundle = declarative
   (DacFx computes the ALTER; engine owns data measured by CDC); `grant: data` vs a schema
   change is a refused type mismatch.
7. **`seal` → episode → `report`** is the provenance pair; `report` is the migration-team
   change bundle, anchored on the last seal.
8. **The norm `‖δ‖` is surfaced** in preview and report as the minimality proof.
9. **The surface ships whole; ladder-dependent flows refuse cleanly** until their engine
   rung lands (§12).

---

## 14. What this becomes in the code

The engine is unchanged — `MovementSpec` is the serialized shape of a resolved flow (source
*A* × destination × legs × strategy × data-origin × identity), and a flow is a
partially-applied `MovementSpec` that `--go`/`--fresh`/`--allow-drops` finish. The build:

- **Config** grows the two-layer `environments` + `flows` schema (extend `TargetConfig` /
  `MovementSurface.fs`; D9-guarded), with `access`/`grant` on the environment.
- **Parse** resolves `projection <flow>` → an `Intent` carrying the resolved
  `MovementSpec` (most of the current flag-reading machinery moves into config parsing).
- **Plan** (`Command.plan`, pure, totality-tested) routes to `PlanAction`, adding the
  `grant` refusal and the schema-agreement gate as named `Refused` actions.
- **Run** (`runPlan`) delegates to the proven engine faces; `bundle` → folder/SSDT,
  `direct` → live (two-gate), `docker` → deploy. `seal` writes the episode; `report` diffs
  against it.

Surface types live in `src/Projection.Pipeline/MovementSpec.fs` + `MovementSurface.fs`;
the runner in `src/Projection.Cli/Program.fs`; tests in `MovementSurfaceTests.fs`. The
slice plan is `THE_CLI_BACKLOG.md`.
