# Appendix C — Scope and Feasibility Review of VISION.md

**Date:** 2026-05-08
**Reviewing:** VISION.md @ commit `2fb51ef`
**Brief:** Delivery-focused scope review. Separate the load-bearing core from the aspirational frill; report what should be in scope for the next 1-2 quarters. Be ruthless.
**Synthesis location:** `VISION_REVIEW.md`

---

# Scope review: V2 vision vs. cutover-quarter feasibility

## 1. What's load-bearing for the cutover

The vision is explicit about what earns V2's existence: "**The cutover earns V2's existence**" (line 109). Read against the five demands (lines 51–60), the **must-ship-before-cutover** set is small:

- **DacpacEmitter** (binary T1) — without it, V2 emits no deployment artifact. Cutover-blocking by definition.
- **SnapshotRowsets adapter** — resolves the JSON-projection-lossiness class (SsKey, EspaceKind, isSystemEntity per HANDOFF). Without it, the Catalog flowing into emit is structurally lossy. The `CatalogReader.fs` SnapshotJson path is the current source.
- **Projection.Pipeline canary** with read-side adapter and round-trip compare — this *is* demand #1 ("verifiable correctness," lines 52–53). The vision frames the canary as the proof of cutover-trustworthiness; you cannot ship the cutover with implicit correctness because (line 21) "The cutover scales the stakes past what implicit correctness can carry."
- **RefactorLogEmitter** — explicitly tied to demand #4 (line 58: "RefactorLog records carry rename history across schema versions"). The forcing function names it (line 11: "RefactorLog records that need to survive across schema versions").
- **StaticSeedsEmitter / MigrationDependenciesEmitter / BootstrapEmitter** — the data triumvirate is what gets the legacy-domain reflow workflow operational. Demand #3 ("CDC-safe idempotency," line 56) names "Topologically-sorted two-phase insertion" — V1 has it; V2 must inherit.
- **User FK reflow under Policy** — already in the algebra via the four-axis Policy, but the cutover-specific flows (Dev → UAT user-matching strategy) need to be exercised end-to-end through the canary at minimum once per environment.

**Post-cutover trajectory** (the vision agrees explicitly, lines 108–118): GraphQL emitters, FakerEmitter, drift detection on deployed databases, per-developer local environments, AI-agent substrate, recipes, V1 sunset, open-source. The vision's own sectioning (§"The post-cutover trajectory") is the sequencing commitment.

## 2. What's already shipped that the vision underweights

The vision lists the three emitters and waves at "three boundary adapters." This understates how much of the chorus's *infrastructure* is in place:

- The **strategy-layer codification** at its stability mark (README §"The strategy layer") — every future tightening intervention slots in without rework.
- The **Diagnostics writer + Lineage<Diagnostics<'a>> dual composition** (HANDOFF §load-bearing). This is the substrate for the three-channel diagnostics the vision treats as forthcoming. The split is deferred (HANDOFF: "single channel sufficient at all chapter-2 consumers"), but the writer plumbing is done.
- **`SnapshotSource` closed DU with reserved variants** (`SnapshotRowsets`, `LiveOssysConnection`) — adding the canonical rowset variant is variant-addition, not architectural rework.
- **The four-axis Policy** (Selection, Emission, Insertion, Tightening) is built. Policy-driven environment-specific shaping (Profile and Policy carrying environment variance, line 54) is a configuration question, not a build question.
- **T11 sibling-Π commutativity** is a structural property the existing three emitters already honor; new sibling Π's inherit it.
- **The OSSYS adapter (25 translation rules)** — the hard part of catalog production is shipped.

The vision says "Currently shipped: RawText, Json, Distributions" in three lines; what's actually shipped is roughly 60% of the algebraic chorus's invariant scaffolding.

## 3. Risk verdicts on informational widening

- **FakerEmitter with six-dimension quality scoring** (line 75, 101): **(c) likely to get cut** for cutover scope. Already gated on third evidence type per README. Six-dimension quality scoring is research-grade; the cutover doesn't require it. Faker's existence is credible post-cutover; *quality scoring against six dimensions* is speculative-but-cheap-to-keep-on-roadmap.
- **GraphQL schema and resolver emitters** (lines 76, 113): **(b) speculative-but-cheap**. The "isomorphism observation" is real — sibling Π for GraphQL is plausible — but no cutover demand exists. Keep on roadmap; do not build pre-cutover.
- **Post-Integration-Studio external entity declaration emitter** (line 77): **(a) credibly forthcoming**. This is the *cutover's downstream consumption surface* (line 77). It earns its place from the forcing function. Probably ship-or-stub for cutover; could be operationally produced by hand if necessary, but the algebraic move is to emit it.
- **AI-agent substrate / catalog as ontological grounding** (line 103): **(d) red flag for scope drift**. Beautiful framing but zero forcing function. "AI agents are consumers, not just collaborators" is a thesis statement, not a deliverable.
- **Recipes-as-Terraform / docker compose / Playwright invocations** (lines 105, 111): **(b) cheap-to-keep**, with one exception — a docker compose file for the canary's testcontainers setup is a byproduct of the canary work, essentially free. Per-developer Docker SQL Server (line 111) is **(c)** for cutover quarter.

## 4. Single biggest scope risk

**The FakerEmitter's six-dimension quality scoring** (lines 75, 101–102: "Profile across six metric dimensions: relational, commutative, descriptive, heuristic, correlative, entropic" and "Quality becomes a number… V2 can self-evaluate its synthetic outputs and iterate to threshold").

This is the single item most likely to consume a quarter of effort and contribute zero cutover safety. The cutover does not need synthetic data; it needs *real* migrated data that round-trips correctly. Faker is post-cutover trajectory by the vision's own §"The post-cutover trajectory" framing. The six-dimension scoring is research; defer the *scoring* indefinitely and ship Faker (if at all) with a single dimension when a consumer demands it.

## 5. Single biggest underweighting

**Drift detection / read-side adapter operating against deployed databases continuously** (line 114). The vision lists this as one bullet under "post-cutover trajectory," but operationally it *is* the cutover's safety net. Four environments, CDC-dependent features in production, repeatable cadence — the failure mode named at line 13 ("cutover fails after partial completion, leaving the system in a hybrid state that's structurally hard to recover from") is exactly what continuous drift detection prevents from compounding silently. The read-side adapter is built for the canary anyway (`Projection.Adapters.Sql.ReadSide`); pointing it at the four real databases in a scheduled job is a small additive, not a new chapter. Pull this forward; do not let it sit in "post-cutover trajectory."

Also under-emphasized: **the V1 → V2 cutover's own rollback plan**. ADMIRE.md transitions are mentioned (line 117), but the *operational* question of "if V2 emission is wrong on environment N, how does the team revert that environment to V1 emission within hours" is not in the vision. See §7.

## 6. Recommended Q1/Q2 commitment

Build/ship pre-cutover:

- **`SnapshotRowsets` variant of `SnapshotSource`** in `Projection.Adapters.Osm.CatalogReader` — first; everything downstream inherits a non-lossy Catalog. Per subagent #5's pre-scope, parallel-to-or-before canary.
- **`Projection.Pipeline` canary** with `Projection.Adapters.Sql.ReadSide`, testcontainers SQL Server (version-pinned to prod), DacFx loaded TSqlModel round-trip, and SsKey-rooted compare. This is demand #1 made operational.
- **`Projection.Targets.SSDT.DacpacEmitter`** with T1 amended for binary normal form (subagent #4 flags `BuildPackage` non-determinism — amend T1 explicitly). Plus `RefactorLogEmitter` as a sibling Π.
- **Data triumvirate**: `StaticSeedsEmitter`, `MigrationDependenciesEmitter`, `BootstrapEmitter` with `EmissionPolicy` (AllRemaining / AllExceptStatic / AllData) per session-17 strategic frame.
- **Drift-detection job**: read-side adapter pointed at all four environments, scheduled, surfacing differences as Diagnostics findings into a known channel. Cheap given the canary's read-side is already built; high cutover-safety leverage.

Explicitly defer to post-cutover trajectory: FakerEmitter (and especially the six-dimension scoring), GraphQL emitters, AI-agent substrate, per-developer local environments, recipes-beyond-the-canary's-compose, V1 sunset (run dual-track, sunset later), open-source. These belong in `DECISIONS.md` as named active deferrals with re-open triggers, mirroring the existing discipline.

## 7. Sustainability / fallback

~25 sessions over ~2 weeks with V1 still shipping. Through cutover quarter, dual-track is sustainable *only if* Q1/Q2 commitment is the five items above and the informational-widening surface is genuinely held off. The vision's discipline of "IR grows under evidence, not speculation" applies upward to scope: the chorus grows under cutover pressure, not under aspirational pressure.

**The vision does not name a fallback if V2 is unfinished at cutover time.** This is the gap. Recommend appending a §"Cutover fallback" to VISION.md naming: (a) V1 remains the production emitter for any environment whose canary has not gone green; (b) the cutover proceeds environment-by-environment in promotion order (Dev → QA → UAT → Prod), with V2 gating on green canary per environment and V1 retained as the rollback target; (c) ADMIRE.md `extracted` status requires a green canary, not just a passing differential test; (d) V1 sunset deferred until all four environments have run on V2 emissions for one full schema-evolution cycle. Without this, the cutover has no defined off-ramp, and the forcing-function failure mode (line 13) becomes an unbounded liability.

The vision is intellectually load-bearing. The risk is not the algebra; it is letting the post-cutover trajectory leak into pre-cutover sequencing. Hold the spine; defer the widening.

Files referenced:
- `sidecar/projection/VISION.md`
- `sidecar/projection/README.md`
- `sidecar/projection/HANDOFF.md`
- `sidecar/projection/CLAUDE.md`
- `sidecar/projection/src/Projection.Adapters.Osm/CatalogReader.fs`
- `sidecar/projection/CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md`
- `sidecar/projection/CHAPTER_3_PRESCOPE_SNAPSHOT_ROWSETS.md`
