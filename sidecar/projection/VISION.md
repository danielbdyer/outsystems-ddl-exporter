# V2 — Vision

This document carries V2's strategic frame across context boundaries. It is distinct from HANDOFF.md (the chapter-bridge tactical letter) and CHAPTER_N_CLOSE.md (the chapter-arc syntheses). This is the strategic substrate underneath both: what V2 is for, what it must do, what it ultimately becomes.

Read it once at session-open. Re-read when sequencing decisions feel unmoored from the larger arc. Extend it when the work surfaces a strategic implication the current text doesn't yet carry.

## The forcing function

A 300-table OutSystems 11 Reactive system on managed AWS, mid-development, facing an External Entities cutover. Every Entity and every Static Entity will be swapped 1:1 — internal management ceded to OutSystems' platform replaced by external management against a team-managed on-prem SQL Server. Schema and data live outside OutSystems' database. Integration Studio declares the External Entities pointing back at the external schema. The platform consumes the swap as if nothing changed; underneath, the entire data plane has migrated.

Four environments — dev, qa, UAT, prod — each with on-prem SQL Server consumption via Azure DevOps PRs. CDC running in production with features depending on it; spurious change records would disrupt those features. User FKs (CreatedBy, UpdatedBy) wired through every entity, requiring environment-specific remapping when data is reflowed (Dev → UAT with user-matching strategy). Migration-team workflow that publishes legacy domain data into the on-prem database. RefactorLog records that need to survive across schema versions. Repeatable cadence — schema and data evolve continuously; the extraction gets re-run regularly as the source of truth evolves.

If V2's emission is wrong: production data integrity corrupted; CDC-dependent features broken, possibly silently; rollback prohibitively expensive across environments and versions; operator trust gone. Worst case: cutover fails after partial completion, leaving the system in a hybrid state that's structurally hard to recover from.

This is what V2 must survive. The algebra is not aesthetic; it is the structural condition for the cutover being trustworthy.

## What V1 already does

V1 (the parent outsystems-ddl-exporter project) has been doing this work — extraction; specializations; opinionated formatting; topologically-sorted two-phase inserts; user FK reflow between environments; profile interventions on the data; standalone domain record injection for legacy migration teams; environment promotion via Azure DevOps PRs. V1 is not a failed predecessor. V1 is the empirical foundation V2 inherits. Every specialization V2 must support exists in V1 because V1 discovered it through the lived work of building it.

V1's correctness is implicit — checked by hand, verified by experience, trusted because nothing has badly failed yet. The cutover scales the stakes past what implicit correctness can carry. V2 makes the correctness explicit, verifiable, and indefinitely repeatable.

The relationship: V2 admires V1; V2 extracts from V1 under empirical pressure; V2 codifies what V1 discovered into algebra. The boundary between V1 and V2 is data, not typed cross-references. ADMIRE.md tracks the extraction; entries transition through admiring (researched) → extracting (in flight) → extracted (differential confirmed) as evidence accumulates.

## The algebraic core

V2 is a metadata projection compiler. The algebra:

```
Project = Π ∘ E
```

E is policy-driven enrichment; Π is structural projection. The composition is the projection.

The four inputs:
- **Catalog** — environment-invariant evidence (the schema; the structural truth)
- **Policy** — environment-specific intent (FK reflow strategy; user-matching strategy; migration data overrides)
- **Profile** — environment-specific evidence (what data lives there; what users exist; what value distributions appear)
- **Lifecycle** — temporal evidence (before, now, after; rename history; version threading)

Π is total: every Catalog kind produces a corresponding artifact element by SsKey root.
Π is pure: no I/O, no Policy reach, no temporal coupling (A18 amended).

The algebra is enforced by F#'s closed-DU + total-pattern-match disciplines plus the empirical-test discipline (adding a DU variant should produce exhaustiveness errors only at match sites; no caller reshaping outside the variant's module). This is not bureaucratic ceremony. It is the structural condition for the algebra holding under modification.

The canary loop closes the gap between artifact and reality. V2 emits artifacts; V2 deploys them to ephemeral docker SQL Server (testcontainers, version pinned to production); V2's read-side adapter reads the deployed schema back as a Catalog; V2 compares the round-tripped Catalog to the source by SsKey root. If the comparison fails, the artifact never publishes. T11's sibling-Π commutativity is the verification surface: every projection must mention every Catalog kind by SsKey root, and the read-side reconstruction must agree with all sibling emissions structurally.

Lineage is constitutive, not decorative. Every decision the system makes — which strategy fired, what evidence informed it, what the rationale was — is carried in the writer monad. Passes return `Lineage<'output>` for decisions only; `Lineage<Diagnostics<'output>>` for decisions plus observer-relevant findings. Auditability is type-system-encoded.

## The five demands and the algebraic moves that meet them

**1. Verifiable correctness.** Every projection must be a tested claim, not an asserted output. The algebraic move: the canary loop. emit → deploy → read-back → compare-by-SsKey. The pipeline becomes self-validating; the team has algebraic proof rather than empirical hope.

**2. Multi-environment consistency.** Four environments, same algebra, environment-specific Profile and Policy. The algebraic move: Catalog × Profile × Policy separation (A18 amended). Catalog is environment-invariant; Profile and Policy carry the environment-specific shaping. The same algebra runs against four different (Profile, Policy) pairs; four different artifacts emerge, all proven consistent structurally.

**3. CDC-safe idempotency.** The emission must not generate spurious change records. The algebraic move: T1 projection-language-normal-form. Bytes for text/JSON; loaded TSqlModel structure for binary/DACPAC; content-equality for data emissions. `decimal` as default for continuous statistical evidence (T1 byte-determinism requires it). Topologically-sorted two-phase insertion (V1 implements; V2 inherits and makes algebraic).

**4. Identity preservation.** RefactorLog records carry rename history across schema versions. The algebraic move: A1 (identity-survives-rename) plus UUIDv5 plus RefactorLogEmitter. SsKey identity is stable under attribute/entity/module rename; UUIDv5 deterministically maps V1's persistent identity space (entity SSKey Guids) to V2's. The forthcoming RefactorLogEmitter (sibling Π) generates rename records consumable by SQL Server's refactor log, by Integration Studio's external entity declarations, by GraphQL schema versioning, by anything else needing to track "this identity, formerly X, is now Y."

**5. Provenance and observability.** Every decision traceable. The algebraic move: writer-monad lineage carriage plus structured rationale DUs plus the canonical surfaces (AXIOMS.md, DECISIONS.md, ADMIRE.md, CLAUDE.md). The system carries its own audit trail as a first-class artifact.

## The sibling chorus

T11 sibling-Π commutativity is the chorus's structural backbone: every Π's output mentions every Catalog kind by SsKey root, in the projection language's normal form.

Currently shipped:
- RawTextEmitter (debug oracle; legible diffs; no DacFx dependency)
- JsonEmitter (structural snapshot; deterministic UTF-8 via Utf8JsonWriter)
- DistributionsEmitter (Profile-shaped statistics)

Forthcoming (chapter 3 onward):
- DacpacEmitter (deployment artifact; T1 amended for binary normal form)
- RefactorLogEmitter (rename history; identity propagation)
- StaticSeedsEmitter / MigrationDependenciesEmitter / BootstrapEmitter (data-emission triumvirate per session-17 strategic frame; composition policy via EmissionPolicy: AllRemaining default / AllExceptStatic / AllData)
- FakerEmitter (Profile-shaped synthesis; quality scoreable against Profile across the six metric dimensions: relational, commutative, descriptive, heuristic, correlative, entropic)
- GraphQL schema and resolver emitters (the isomorphism observation: same algebra projects to GraphQL targets)
- Post-Integration-Studio external entity declaration emitter (the cutover's downstream consumption surface)

The chorus is operational, not academic. Same Catalog, many forms, each form shaped by what its consumer needs. Cross-validation is implicit: if RawTextEmitter and DacpacEmitter disagree on attribute type rendering, one of them has a bug. The sibling structure is itself a verification surface.

## The temporal axis

A1 (identity-survives-rename) is the temporal axiom. SsKey identity is stable across schema evolution. The current bound is named in A1's session-23 forwarding pointer; SnapshotRowsets resolves the bound when its forcing function fires (real refactor.log consumer; real cross-version identity demand; EspaceKind activation; isSystemEntity activation).

When SnapshotRowsets lands and RefactorLog is implemented, V2 becomes a history-aware system. UUIDv5 deterministically maps V1 SSKeys to V2 identities; renames are first-class; cross-version comparison is possible. V2 is not a snapshot; V2 is a thread of snapshots that knows its own continuity.

The Lifecycle dimension reserved space for this from chapter 1. Catalog is "what." Policy is "intent." Profile is "shape." Lifecycle is "when." V2 holds all four.

## The informational widening

V2 is not only a build tool. V2 is the team's information layer over OutSystems.

Engineering teams running OutSystems typically do not have direct sovereignty over the structured information about their own application. The platform has more accurate, more queryable, more current metadata about the team's app than the team has direct access to — or rather, the team has to query the platform through limited interfaces to know what its own system contains. V2 inverts this. The Catalog is canonical, queryable, exportable, projectable, version-traceable. The team owns its own informational reality.

What follows from this:

The Catalog is platform-survival. If the team migrates off OutSystems entirely (different platform; in-house build; different SaaS), the Catalog persists. The Catalog represents the domain regardless of source. OutSystems is the current source for evidence; the Catalog is the truth; whatever comes after consumes it the same way. V2 outlives OutSystems' role in the team's architecture.

Profile is longitudinal evidence. Profile across time = data evolution. Profile across environments = drift. Profile across populations = behavioral signal. Profile becomes an analytical artifact, not just a Faker shaping input.

Synthetic data quality is algebraically scoreable. Faker's output measured against Profile across six metric dimensions. Quality becomes a number. Synthetic data isn't "fake"; it's Profile-faithful. V2 can self-evaluate its synthetic outputs and iterate to threshold.

AI agents are consumers, not just collaborators in construction. Playwright agents need domain understanding — that's a Catalog projection. Test agents need realistic data — Profile-shaped Faker. Code agents need to query — GraphQL endpoint emerging from the Catalog. Domain copilots need ontological grounding — the Catalog as substrate. V2 produces what AI agents need to operate intelligently against the team's domain.

V2 emits recipes, not just artifacts. Docker compose files for SQL Server stand-up. Provisioning scripts. Playwright test plan generation invocations. Synthetic data generation parameters tuned to Profile. V2 is closer to Terraform/Pulumi for infrastructure-as-code than to most schema-emitters. The team gets deployable working environments, not just deployable artifacts.

## The post-cutover trajectory

The cutover earns V2's existence. Post-cutover is where V2 becomes the substrate the team builds on:

- **Local development.** Per-developer Docker SQL Server with Profile-shaped synthetic data, queryable via local GraphQL, testable via local Playwright agents, completely disconnected from shared environments. Iteration speeds up; experimentation costs near-zero.
- **Schema evolution.** Repeatable across all four environments via the same machinery that drove the cutover. User FK reflow and migration-data injection workflows continue running through V2.
- **GraphQL endpoints.** Sibling Π emerging without rebuilds. Catalog plus new emitter delivers it. Packageable per module.
- **Drift detection.** Read-side adapter operating against deployed databases compares back to source Catalog continuously; drift surfaces as a Diagnostics finding rather than a production incident.
- **CI/CD substrate.** Every PR triggers canary; every Profile update refreshes Playwright test plans; every schema change propagates to GraphQL/faker/refactor logs.
- **Personal tooling.** V2 packageable per module means anyone with an OutSystems module and a question can point V2 at it. The maintainer's flow-metrics-from-the-code-review-app use case is canonical.
- **V1 sunset.** ADMIRE.md transitions every V1 component to extracted-and-verified. V1 becomes historical reference. V2 is the live system.
- **Possibly: community contribution.** If the abstractions hold up, V2 could open-source as a shared resource for any team running OutSystems with external-schema or analytical needs. Optional; the option emerges from the work whether or not it gets exercised.

## What V2 ultimately is

V2 is the team's sovereignty over its own metadata.

Full canonical access to the domain model, with self-validation, history-awareness, multi-target projection, multi-environment consistency, AI-agent legibility, and indefinite extensibility. The cutover is the moment the sovereignty earns its existence; everything after is the team operating from sovereignty rather than dependency.

The OutSystems platform is one source for the Catalog's evidence; the Catalog is the truth; V2 is the team's instrument for working with that truth across every surface that needs it — including surfaces that don't yet exist, for consumers that haven't been imagined yet.

The cutover is the load test. The information sovereignty is what V2 ultimately is. The trajectory afterward is compound interest on the sovereignty, paid back in capabilities the team didn't know it would have.

## How to hold this vision

This document is strategic substrate. Tactical decisions live in DECISIONS.md; chapter-bridge context lives in HANDOFF.md and CHAPTER_N_CLOSE.md; algebraic claims live in AXIOMS.md; V1↔V2 placement lives in ADMIRE.md. This document carries the *why* behind all of those.

Consult it when:
- A sequencing decision feels unmoored from the larger arc.
- A new sibling Π is being scoped and the chorus needs reorienting.
- The work surfaces an implication that fits in this document's frame but isn't yet recorded.
- An audit asks "what is V2 for, ultimately?" and the answer needs to be load-bearing rather than aesthetic.

Extend it when:
- A new dimension of the vision earns its place through empirical demonstration. Append, don't rewrite. Append-only documentation discipline applies here as it does to the HANDOFF and CHAPTER_N_CLOSE letters.
- A new corollary falls out of the work that was not previously visible. Surface it briefly; cross-reference to the DECISIONS entry that resolved the underlying question.

Do not consult it when:
- The work is tactical (HANDOFF, DECISIONS, ADMIRE are the right surfaces).
- The decision is local to a slice (the slice's chapter-open document is the right surface).

The vision is the load-bearing structure that lets the chapters ahead support more weight than the ones behind. Hold it lightly when the work is tactical; hold it firmly when the work asks "is this still V2."

## Closing

V2 inherits empirical foundation from V1, faces the External Entities cutover as its forcing function, holds algebraic discipline as the structural condition for cutover trustworthiness, widens through the sibling chorus into informational sovereignty, and sustains the team's right to know what they have built across the trajectory beyond the cutover.

The codebase is the artifact. The cutover is the load test. The disciplines are the contribution. The collaboration pattern (V1 was AI-collaborative; V2 sustains the pattern across multi-instance Claude horizons) is the worked example.

Hold the spine.

— Recorded for the receiving agent.
