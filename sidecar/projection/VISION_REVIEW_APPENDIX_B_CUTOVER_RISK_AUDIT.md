# Appendix B — Cutover Risk Audit of VISION.md

**Date:** 2026-05-08
**Reviewing:** VISION.md @ commit `2fb51ef`
**Brief:** Database-migration risk audit. Evaluate whether the vision actually meets the operational risk profile of the External Entities cutover scenario it describes. Verdicts: covered / partial / gap / aspirational.
**Synthesis location:** `VISION_REVIEW.md`

---

# V2 Risk Audit — Cutover Vision vs. Current State

**Bottom line:** the strategic vision is coherent; the *current* sidecar implementation covers maybe a third of what the cutover scenario demands. Most of what makes V2 "trustworthy under the cutover" is forthcoming, not shipped. Evidence cited from `VISION.md`, `README.md`, `AXIOMS.md`, `Policy.fs`, and `CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md`.

### 1. Does the canary loop catch the failures that matter? — **Aspirational**

The canary loop is **unbuilt**. `README.md:71-76` shows `Projection.Pipeline/` (canary), `Projection.Adapters.Sql.ReadSide/`, and `Projection.Targets.SSDT.DacpacEmitter/` as *reserved slots, not yet built*. README.md:111-118 confirms "the pipeline canary" is one of the two un-built primitives gating substantive forward work. No deployment, no testcontainers, no read-back today.

Even when built, per the prescope (CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md:304-348), the loop is *emit → DacFx deploy → DacFx extract → SsKey-compare*. That covers:
- Schema correctness against an ephemeral SQL Server.
- DacFx deploy-readiness (FKs resolve, types valid).
- Cross-emitter T11 commutativity.

It does **not** cover, for the named failure modes:
- **CDC noise in production**: ephemeral DB has no CDC enabled, no prior schema state, no incremental ALTER. The canary verifies clean-slate deploy, not no-op redeploy against an existing CDC-enabled database (see #2).
- **User FK misreflow**: no machinery exists to *test* user-matching outcomes; the canary compares structure, not data plane content.
- **Partial-state hybrids**: a canary that passes says nothing about half-applied production deploys.
- **RefactorLog gaps**: RefactorLogEmitter doesn't exist (#3); the canary cannot detect a missed rename record because nothing emits one.
- **Environment drift**: canary runs a single (Catalog, Profile, Policy) triple at a time; cross-environment comparison is not a canary feature, it's a post-hoc analytical use of Profile (VISION.md:99).
- **Two-phase insert ordering bugs**: A33 mandates `TopologicalOrder` for data emission, but the data emitters (`StaticSeedsEmitter` / `MigrationDependenciesEmitter` / `BootstrapEmitter`) are forthcoming (VISION.md:74). Canary today wouldn't deploy data; ordering bugs would not be exercised.

### 2. CDC-safe idempotency — **Gap (mechanism unnamed)**

VISION.md:56 names "T1 projection-language-normal-form" as the algebraic move. T1 (AXIOMS.md:301-302, T1-amended:452-461) says `Project` is a pure function — same triple, byte-identical surface. **That is determinism of V2's emission, not idempotence of SQL Server's deployment.**

CDC change records are emitted by SQL Server when DacFx deploys an ALTER. DacFx's incremental-deploy planner decides what ALTERs to run based on diff between source DACPAC and target schema. Two byte-identical V2 DACPACs deployed against a target whose schema already matches *should* produce zero ALTERs and zero CDC events — but that's a property of **DacFx's diff planner**, not of T1.

The vision conflates the two. Real CDC safety requires:
- Byte-identical DACPACs (T1 — partially built; CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md:408-414 flags that DacFx embeds wall-clock timestamps in Origin.xml, breaking byte equality, and chapter-open will likely amend T1 to "content-equality via DacFx round-trip").
- DacFx producing zero-ALTER plans on no-op redeploy (an empirical property of DacFx + the target DB state, untested in V2).
- No DacFx options that issue cosmetic ALTERs (`DROP/CREATE` on column reorderings, etc.).

Neither of the latter two is named in VISION.md. The gap is real: **the vision does not name a CDC-noise verification surface**. A canary that runs deploy twice in succession against the same ephemeral DB and asserts the second deploy issues zero ALTERs would be the right primitive; not present, not scoped.

### 3. Identity preservation across renames — **Gap**

Three problems compound:

(a) **A1 is bounded** through the current `SnapshotJson` input path (AXIOMS.md:47-72). V1's JSON projection strips SSKey columns; V2 *synthesizes* SsKey from name fields. *Renames in the source platform produce different SsKey values in V2's IR through this path.* A1's identity-survives-rename guarantee **does not hold** for renames on the active path.

(b) The fix is `SnapshotRowsets` — the canonical input variant (README.md:82-86, AXIOMS.md:60-68) — which is **operator-decided, not yet built**. Until it lands, V2 cannot honor A1 for a single rename across schema versions through its current adapter.

(c) **RefactorLogEmitter is forthcoming** (VISION.md:73). Even with SnapshotRowsets and stable SsKeys, no sibling Π today emits the rename records SQL Server's refactor log consumes, and no facility threads V1↔V2 identity via UUIDv5.

For the cutover's "RefactorLog records that need to survive across schema versions" demand: **V2 currently supports zero renames across schema versions in production-trustworthy form.** Vision claim and current state are far apart.

### 4. User FK reflow — **Gap (V2 does not currently do this)**

VISION.md:19 says V1 already does this; VISION.md:56 says V2 "inherits and makes algebraic." Reading `Policy.fs` end-to-end (lines 1-533): the Policy DU has Selection / Emission / Insertion / Tightening only. **There is no UserMatching axis, no UserRemap configuration, no per-environment user-matching strategy type.** `InsertionPolicy` is `SchemaOnly | InsertNew | Merge | TruncateAndInsert` (lines 31-35) — no carrier for user-mapping data.

The algebra has *reserved space*: A32 (AXIOMS.md:465-489) frames the UAT-Users transform as "a discovery pass producing `UserRemapContext`, two sibling Π's consuming it" (DECISIONS.md:258-268). But `UserRemapContext` is referenced only in axioms and decisions — no F# type, no pass, no Π consumes it. Search confirms: zero matches for `UserRemap` / `CreatedBy` / `UpdatedBy` outside docs.

For the cutover today: **V2 cannot perform user FK reflow.** V1 must. The vision claim is aspirational with reserved algebraic space, not implemented capability.

### 5. Multi-environment Profile/Policy machinery — **Aspirational**

VISION.md:54 frames four environments as "same algebra, four (Profile, Policy) pairs." The algebraic move (A18 amended, A34 — Profile independence) is sound. But **machinery to drive it does not exist**:

- No host / CLI shell — `Projection.Host.Cli/` is a reserved slot (README.md:78), unbuilt.
- No notion of *EnvironmentId* in the IR or in Policy. A `Policy` value carries no environment label; running V2 four times against four distinct Policy values is the operator's responsibility, completely unautomated.
- No machinery to load environment-specific Profile (`ProfileSnapshot.fs` exists in Adapters.Sql, but pointing it at four targets is host-shell concern that's not built).
- No machinery to assert "the four artifacts agree on Catalog and disagree only on (Profile, Policy)-induced fields" — that property is the algebraic claim VISION.md:54 makes, but no test asserts it.

Driving four environments today: an operator scripts four invocations of an unbuilt host, manually managing four Policy values and four Profile sources. That is not "V2 makes this algebraic" — it is "V2 does not yet do this."

### 6. Rollback / partial-state recovery — **Gap (prevention only)**

VISION.md acknowledges "partial cutover leaves hybrid state structurally hard to recover from" as worst case (line 13) but **never names a recovery move**. The five demands (52-60) are all about *prevention*: verifiable correctness, multi-environment consistency, CDC-safe idempotency, identity preservation, provenance. No demand addresses what happens when the canary passes and prod deploy fails partway.

V2's contribution to recovery is structural-but-passive:
- A22 content-addressed snapshots and T7 snapshot deduplication mean a prior good Catalog can be re-emitted bit-identically. Recovery target exists structurally.
- Lineage and Diagnostics writers carry decisions, so a post-mortem has audit material.

But there is no *rollback artifact*, no "diff the partial deploy and emit a remediation DACPAC," no transactional deploy primitive. **If canary passes and prod deploy fails mid-way, V2 contributes a re-emittable target snapshot and an audit trail; the human operates the rollback.** That's a real gap against "structurally hard to recover from."

### 7. Dual-system risk — **Gap (no governance named)**

V2 is "additive" (README.md:6-8, "V1 continues to operate, V2 is additive, every commit cherry-pick safe"). ADMIRE.md tracks per-component status (admiring → extracting → extracted) but **VISION.md and the canonical docs do not specify which system is canonical for which artifact at which moment** during the cutover. The phrase "V1 sunset" is post-cutover trajectory (VISION.md:117), not a cutover-window governance rule.

Concrete split-brain risk: V1 emits a DACPAC, V2 emits a DACPAC, they disagree (different identity through the JSON-projection-lossiness path; different rename handling; different defaults), Azure DevOps merges whichever PR lands first. Nothing in V2's surfaces names "V1 ships X for the cutover; V2 ships Y; here is the override rule." The closed-DU-rigorous-extraction discipline ADMIRE tracks is *per-component algebraic placement*, not *per-artifact-type cutover ownership*.

This is the most under-specified risk in the vision relative to the operational scenario.

---

### What's actually load-bearing for cutover safety — must ship for V2 to deliver on the vision's risk claims

1. **`SnapshotRowsets` adapter variant** + **RefactorLogEmitter** — without these, A1 is bounded, identity isn't stable across renames, and no rename records emit. The cutover's repeatable-cadence demand requires both.
2. **`Projection.Pipeline` canary** with the **redeploy-against-existing-schema CDC-zero-ALTER assertion** explicitly added (not just clean-slate deploy) — the CDC-safety claim is unverified without it.
3. **User FK reflow as a real pass + sibling Π** — the algebraic space (A32, UserRemapContext) is reserved but the F# types, pass, and Π are not written. The cutover scenario names this as a per-environment demand; V2 currently cannot perform it.
4. **Cutover-window governance rule in DECISIONS** naming which system is canonical for each artifact type during dual-operation, with an explicit V1-sunset gate per artifact — to close the split-brain risk before operators are choosing between two PRs that both pass review.

A fifth, lower priority but worth surfacing: a **partial-deploy remediation primitive** (diff partial state vs. target snapshot, emit corrective DACPAC). The vision frames partial-state hybrid as worst case but offers prevention only; recovery is unfunded.
