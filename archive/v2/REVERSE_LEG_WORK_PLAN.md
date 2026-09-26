# Work Plan — Archetype-driven movement at scale (the locked model + the forward slices)

> **What this is.** The sequenced build plan that follows from the operator's confirmed real-estate
> facts (2026-06-15). It sits on top of the shipped Phases 2–4 + NM-58 (the reverse-leg engine,
> reconcile, idempotency, dry-run, reconcile-robustness) and drives the **archetype-as-config-
> disposition** design (`DATABASE_ARCHETYPES.md`) to built+witnessed, plus the **at-scale keymap
> spill**. Canonical decisions live in `DECISIONS.md` (2026-06-15 entries); this is the plan.

---

## 1 — The locked model (what every slice below assumes)

**One engine, two load flows, two sink archetypes** — the real topology that validates the archetype
design:

| | **Flow P — populate the on-prem** | **Flow R — the reverse leg ("last leg")** |
|---|---|---|
| Direction | (source) → **on-prem** | **on-prem** → (cloud) |
| Sink (written into) | **on-prem SQL Server** | **cloud managed env** (empty; this flow fills it) |
| Sink archetype | **`FullRights`-minus-DMV** (verified: CREATE TABLE, ALTER, IDENTITY_INSERT, sink-resident progress; no `VIEW DATABASE PERFORMANCE STATE`) | **`ManagedDml`** (J5: mints keys; no CREATE TABLE / ALTER / IDENTITY_INSERT) |
| How the engine writes | **direct-connect `migrate`** (live, diff-based, no schema change) **and** **emit-artifacts** (SSDT + static seeds + data, operator-applied) | the live streaming reverse-leg transfer (shipped) |
| Identity disposition | **`PreservedFromSource`** (IDENTITY_INSERT) ⇒ **no keymap** | **`AssignedBySink`** ⇒ keymap (mints + remap) |
| Resume mechanism | **sink-resident progress table** (CREATE TABLE) | client-side NDJSON journal + the **`#`-temp keymap spill** |

**Sizing (estate-data on-prem = the reverse-leg source):** 2.0 M key-map-shaped rows (FK-target,
single-IDENTITY-PK) ⇒ **75 MB** resident remap; 3.5 M all-`dbo` ⇒ 134 MB. Host = **64 GB**. The
resident map **fits even at production ~200 M (~4–8 GB ≪ 64 GB)** — so the spill (Slice S) is a
**completeness + headroom** build, not a current necessity; it ships **armed but inert** (configurable
threshold defaulted off at this scale). This is an operator-directed build-for-safety, recorded as such.

---

## 2 — Already shipped (do not redo)

Phases 2–4 + NM-58, all warm-witnessed (62 Docker + 184 pure green): reconcile ∘ streaming + the
validate-user-map pre-write halt; force-journal + journal-address-drift refusals; the movement
DryRun row-count preview; the reconcile-key robustness (blank-key exclusion + duplicate-target
tiebreaker). Plus the operator package (`REVERSE_LEG_OPERATOR_PROBE_SHEET.md`,
`PHASE_1_REAL_WIRE_HARNESS.md`, `NEXT_BUILD_INPUTS.sql`) and the design (`DATABASE_ARCHETYPES.md`).

---

## 3 — The forward slices (sequenced)

Each: **goal · mechanism · gate · witness (the exit test) · depends-on**. Build order: **A → C → S → B**
(A is the foundation; C is the highest near-term value — the direct-connect populate; S is the
scale-safety; B is the verification, valuable but least urgent since the verdicts are already known).

### Slice A — the `Archetype` config disposition *(foundation; byte-identical)*
- **Goal.** Add `type Archetype = FullRights | ManagedDml` + `CapabilityProfile.of : Archetype ->
  CapabilityProfile` (the disposition bundle, §1) + an optional `Environment.Archetype` facet parsed
  from `projection.json`, **defaulting to inferred-from-`Grant`** (`SchemaAndData→FullRights`,
  `DataOnly→ManagedDml`).
- **Mechanism.** Closed DU; `Grant` becomes a *derived* projection of the archetype; `CapabilityProfile.of`
  is the single expansion site (total over the DU — the `ArtifactByKind` discipline). Nothing branches
  on it yet (the survey routing in B is the first consumer).
- **Gate.** None — existing configs are byte-identical (archetype inferred from their `Grant`).
- **Witness (pure).** `(infer ∘ derive-grant)` round-trips; `CapabilityProfile.of FullRights` /
  `ManagedDml` match the confirmed verdicts (on-prem: DDL+IDENTITY_INSERT+sink-resident; cloud: none).
- **Depends on.** Nothing.

### Slice C — the `FullRights` populate forks *(Flow P; the highest operator value)*
- **Goal.** On a `FullRights` sink (the on-prem), the populate flow uses the profile's better mechanisms:
  - **C1 — `PreservedFromSource` default** (IDENTITY_INSERT permitted): write source keys directly —
    **no key capture, no remap, no FK re-point** (the dramatically simpler + faster load). The
    direct-connect `migrate`-into-on-prem path.
  - **C2 — sink-resident progress resume** (CREATE TABLE permitted): checkpoint resume state in a
    sink-side table — durable, queryable, free of the client-journal's filename↔digest coupling.
- **Mechanism.** The identity-disposition default + the resume chooser read `archetype` /
  `CapabilityProfile` instead of assuming the cloud shape. `ManagedDml` (cloud) keeps `AssignedBySink`
  + the client journal unchanged.
- **Gate.** Only a `FullRights` sink takes these forks; verified against the probed grant (Slice B) so
  a mis-declared FullRights that lacks IDENTITY_INSERT refuses rather than mis-loading.
- **Witness (Docker).** A populate into a `FullRights` sink preserves source keys (zero remap, joins
  hold) and checkpoints sink-side; the same into a `ManagedDml` sink keeps the AssignedBySink + journal
  path byte-identical.
- **Depends on.** A.

### Slice S — the reverse-leg `#`-temp keymap spill *(Flow R; the at-scale architecture)*
- **Goal.** Close the one unbounded-RAM hole in the bounded-memory streaming path: when the keymap
  would exceed a configurable threshold, spill it off-heap.
- **Mechanism.** The spill chooser reads the sink archetype + the threshold. For the `ManagedDml` cloud
  sink: a **session `#`-temp keymap table** (temp tables ARE permitted under DML — J5 P5) populated as
  AssignedBySink chunks capture, and a **server-side `UPDATE…JOIN`** for the phase-2 FK re-point
  (replacing the resident `PackedSurrogateRemap` lookup). For a `FullRights` AssignedBySink sink, the
  same but a persistent table (rare — FullRights usually takes `PreservedFromSource`, no keymap).
- **Gate.** Above the configurable RAM threshold — **defaulted inert at current scale** (75 MB ≪ 64 GB),
  so the resident path is byte-identical today; the spill arms when the operator lowers the threshold
  or the estate grows.
- **Witness (Docker).** An equivalence canary: the same leg with the threshold forced LOW (spill on) vs
  default (resident) produces **byte-identical** sink state + joins — the spill is observationally pure.
- **Depends on.** A. (The largest slice; the operator-directed scale-safety build.)

### Slice B — the survey *verifies* the archetype *(the J5 covenant, generalized)*
- **Goal.** Make the declared archetype a *verified* claim (A44 — expressible ⇔ reachable).
- **Mechanism.** Route `CapabilitySurvey.requiredOf` through `archetype.Grant` (identical results — the
  derivation is exact); add the declared-vs-probed **archetype** reconciliation over the new capability
  flags (CREATE TABLE, IDENTITY_INSERT, DMV-read).
- **Gate.** A declared `FullRights` sink missing a probed capability is a **named mismatch** — e.g. the
  on-prem's absent DMV-read is reported as `FullRights`-minus-DMV (a *split*, surfaced, not a
  misdeclaration); a `ManagedDml` sink that unexpectedly permits IDENTITY_INSERT is surfaced too.
- **Witness.** A `FullRights`-declared sink with a denied capability surfaces the named gap; the
  on-prem's real `FullRights`-minus-DMV classifies correctly.
- **Depends on.** A. (Least urgent — the verdicts are already known; this makes them *enforced*.)

---

## 4 — Dependencies & open inputs

- **Dependency graph:** `A → {C, S, B}` (C, S, B independent of each other).
- **No build is blocked.** Every archetype verdict + the sizing + the dual-write scope are confirmed.
  The only *optional* outstanding number is the production (200 M) FK-target count — not needed to
  build (the resident map fits; the spill is threshold-driven), only to confirm the resident headroom,
  which the 75 MB → ~4–8 GB extrapolation already establishes against 64 GB.
- **Discipline carried:** named refusals + a witness + a `DECISIONS.md` entry per slice; promote the
  reserved Skip-stubs where they fit; build/test warm (set `PROJECTION_MSSQL_CONN_STR`; verify Docker
  tests ran via per-test TRX durations — the Windows no-op trap).

---

## 5 — One-line summary

> The estate proved the design: one engine writing to two sink classes (`FullRights` on-prem,
> `ManagedDml` cloud). Build the archetype as a first-class disposition (A), give the FullRights
> populate its simpler keys-preserved + sink-resident-resume path (C), arm the bounded-memory spill for
> the reverse leg at scale (S), and make the declared archetype a verified claim (B) — each a named,
> witnessed slice on top of the shipped reverse-leg engine.
