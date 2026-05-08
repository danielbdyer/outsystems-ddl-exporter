# V2 Vision — Review and Synthesis

**Date:** 2026-05-08
**Reviews:** VISION.md @ commit `2fb51ef`
**Method:** four parallel subagent evaluations (skeptical critique, cutover risk audit, scope/feasibility review, sequencing plan), one synthesis pass, one reasoning pass on resolvable concerns, then four follow-up subagents on work-smarter angles (canary as property test, V2-as-V1-canary dogfood, radical scope cut, type-system refactor).

**Outcome:** revision 2 of the vision lives in `VISION_REVISION_2.md`. The original `VISION.md` is preserved as historical context. Read `VISION_REVISION_2.md` first when sequencing work.

This document is the synthesis. Each subagent's full report is preserved verbatim in an appendix:

- **Appendix A** — `VISION_REVIEW_APPENDIX_A_SKEPTICAL_CRITIQUE.md` — pressure-test of the document's rhetorical and claim structure.
- **Appendix B** — `VISION_REVIEW_APPENDIX_B_CUTOVER_RISK_AUDIT.md` — operational risk audit against the External Entities cutover scenario.
- **Appendix C** — `VISION_REVIEW_APPENDIX_C_SCOPE_FEASIBILITY.md` — delivery-focused scope review for the cutover quarter.
- **Appendix D** — `VISION_REVIEW_APPENDIX_D_SEQUENCING_PLAN.md` — initial implementation plan for chapters 3 and 4 (superseded in part by Appendix F's chapter-3 re-sequencing).
- **Appendix E** — `VISION_REVIEW_APPENDIX_E_CANARY_PROPERTY_TESTS.md` — canary as FsCheck property-test surface; tier-1 pure / tier-2 container-pooled / tier-3 nightly. ~600 lines test code replaces ~2000+ lines of curated integration tests.
- **Appendix F** — `VISION_REVIEW_APPENDIX_F_V2_VERIFIES_V1.md` — V2 as V1's canary, before V2 emits anything. Read-side adapter via `INFORMATION_SCHEMA`/`sys.*` (not DacFx Extract); triangulation comparator over three Catalogs. Pulls read-side adapter to chapter 3.1.
- **Appendix G** — `VISION_REVIEW_APPENDIX_G_RADICAL_SCOPE_CUT.md` — defer-vs-keep table on every "informational widening" item, with hard triggers. Cuts platform-survival, six-dim Faker scoring, AI-agent substrate as a section, and OSS contribution.
- **Appendix H** — `VISION_REVIEW_APPENDIX_H_TYPE_SYSTEM_REFACTOR.md` — `ArtifactByKind<'element>` private DU + `SsKey` four-variant split + `CatalogDiff`. Turns T11 and A1 into type theorems, not disciplines. Migration sequence is incremental over three chapters.

---

## Where the four reviewers converged

All four — independently — surfaced the same three holes:

**1. The vision names no cutover fallback.** If V2 isn't ready, what happens? The doc closes with "hold the spine," not with "V1 stays warm until cutover+30." Every reviewer flagged this; the planner proposed a concrete three-tier ladder (V1-only / V2-augmented / V2-driver) with a T-30-day decision criterion (Appendix D §6).

**2. "Cutover is V1's problem" is the elephant.** VISION.md line 19 lists everything V1 already does — and that list *is* the cutover. V2's unique contributions (canary, DacpacEmitter, RefactorLogEmitter, user-FK reflow as Policy) are unbuilt. The vision asserts implicit-correctness can't carry the stakes; it does not demonstrate a specific cutover sub-task that breaks under V1 and succeeds under V2. (See Reasoning Resolution §1 below — this concern is dissolvable.)

**3. T1 byte-determinism ≠ CDC safety.** The vision conflates "V2's emission is deterministic" with "SQL Server's deployment produces zero CDC events." The latter is a property of DacFx's diff planner against an existing schema. The right canary primitive — *redeploy against the same schema, assert zero ALTERs* — is unnamed in the vision. (See Reasoning Resolution §2 below.)

## Concrete capability gaps the audit surfaced

| Vision claim | Current state |
|---|---|
| Canary loop catches failures | **Unbuilt.** `Projection.Pipeline/`, `Projection.Adapters.Sql.ReadSide/` are reserved slots only. |
| Identity preservation across renames (A1) | **Bounded today.** Through `SnapshotJson` adapter, V2 *synthesizes* SsKey from name fields — renames produce different SsKeys. Fix is `SnapshotRowsets`, unbuilt. |
| User FK reflow as Policy | **Algebraic space reserved (A32, `UserRemapContext`), zero implementation.** Policy DU has Selection/Emission/Insertion/Tightening — no UserMatching axis, no environment label. |
| RefactorLog records survive across versions | **Zero renames supported in production-trustworthy form** until SnapshotRowsets + RefactorLogEmitter both ship. |
| Multi-environment (Profile, Policy) machinery | **Aspirational.** No host shell, no test asserts the four-artifact Catalog-equivalence claim. |
| Partial-state recovery | **Prevention only, no recovery.** No rollback artifact, no diff-and-remediate primitive. |

## Where reviewers diverged usefully

- **Skeptical critic (Appendix A)** isolated the unfalsifiable rhetoric ("constitutive," "sovereignty," "auditability is type-system-encoded" — a `Lineage<'a>` writer encodes that *something* was written, not that it's auditable) and the algebraic inflation (T11 framed as theorem rather than emitter obligation).
- **Scope reviewer (Appendix C)** flagged the most mis-prioritized item: **drift detection is underweighted.** It's listed as post-cutover trajectory, but it *is* the cutover's safety net. The read-side adapter is built for the canary anyway — pointing it at the four real DBs in a scheduled job is a small additive, not a new chapter. Pull forward.
- **Scope reviewer's biggest cut**: the FakerEmitter's six-dimension quality scoring ("relational, commutative, descriptive, heuristic, correlative, entropic" — six adjectives, zero defined metrics). Defer the *scoring* indefinitely; ship Faker with one dimension when a consumer demands it.
- **Sequencing planner (Appendix D)** committed to the F#/C# boundary placement: F# `DacpacEmitter` produces T-SQL strings; C# wrapper owns DacFx, testcontainers, and the zip-determinization post-pass. F# Π stays pure.

## Recommended sequencing (synthesizing planner + scope reviewer)

**Chapter 3 — cutover-critical chorus**, in order:
1. **`SnapshotRowsets` adapter variant** (resolves JSON-projection lossiness, unblocks A1 for renames).
2. **Read-side adapter** (`Projection.Adapters.Sql.ReadSide/`) — has two consumers from day one: canary round-trip, and drift detection.
3. **`DacpacEmitter`** (F# emits T-SQL strings; C# DacFx wrapper in a new `Projection.Targets.SSDT.Dacpac` C# project owns BuildPackage + zip-determinization post-pass).
4. **Canary closure** in `Projection.Pipeline` (C#) — emit → testcontainers → deploy → read-back → SsKey-compare. **Add the redeploy-zero-ALTER assertion** (covers the CDC-safety claim Appendix B found unverified).
5. **`RefactorLogEmitter`** as sibling Π — UUIDv5 maps V1 SSKey Guids to V2 identities.

**Pulled forward from "post-cutover trajectory":**
- **Drift detection** — the read-side adapter pointed at the four real environments on a schedule, surfacing deltas as Diagnostics findings. Cheap given (2). High cutover-safety leverage.

**Chapter 4 — operational substrate:**
1. Data-emission triumvirate (StaticSeeds → MigrationDependencies → Bootstrap) with `EmissionPolicy` DU.
2. **User FK reflow as a real pass + new Policy axis** (UserMatchingStrategy: ByEmail / BySsKey / ManualOverride / FallbackToSystemUser). Appendix B was clear: this is a current capability gap, not a "make V1's behavior algebraic" inheritance.
3. Operational diagnostics (V2 equivalents of decision-log/opportunities/validations consuming the existing `Diagnostics<'a>` writer).

**Explicit non-goals** for chapters 3–4: GraphQL emitters, FakerEmitter (especially the six-dim scoring), AI-agent substrate, recipes-beyond-the-canary's-compose, V1 sunset. Park as named active deferrals in DECISIONS.md with re-open triggers.

---

## Reasoning resolutions — concerns dissolved without code

Several of the reviewers' concerns dissolve under harder thinking. The following resolutions are on-record clarifications; they do not require implementation work, but should be folded into the canonical surfaces (VISION.md amendment, AXIOMS.md amendments, DECISIONS.md entries) per the append-only discipline.

### R1. "Cutover is V1's job — what does V2 uniquely add?" (Appendix A §2, Appendix C §1)

**Resolution.** V1 emits **SQL scripts** (imperative DDL + DML). V2 emits **DACPACs** (declarative deployment artifacts). That's an artifact-class difference, not a polish-the-existing-thing difference. Two cutover demands explicitly require DACPAC-class artifacts:

- **Refactor.log records.** SQL scripts cannot communicate "this column was renamed" to a target database. Every redeploy of a renamed column under a script drops and recreates — losing data and triggering CDC noise. The refactor.log mechanism is a DACPAC feature; it's how SSDT tracks renames so subsequent deploys ALTER rather than DROP+CREATE.
- **Idempotent diff-deploy across four environments on a repeatable cadence.** SQL scripts must be hand-ordered or templatized per environment; DacFx computes the diff against the current target state and only issues the deltas. This is what makes "schema and data evolve continuously; the extraction gets re-run regularly" tractable across four environments without bespoke per-environment work.

VISION.md asserts this implicitly ("RefactorLog records that need to survive across schema versions") but never names *why this requires a different artifact class V1 cannot reach incrementally*. **Action:** elevate this into the "What V2 ultimately is" section as the load-bearing differentiator.

### R2. "T1 byte-determinism ≠ CDC safety" (Appendix B §2, Appendix A §4)

**Resolution.** CDC noise comes from SQL Server applying ALTERs, not from V2's emission. The chain is: V2 emits → DacFx diffs vs. target → DacFx generates ALTERs → CDC events fire. T1 controls only the first step.

The right framing is a **composition of two independent properties**:
- **V2-side**: model-equivalence under DacFx round-trip (T1 amended for binary, already proposed in CHAPTER_3_PRESCOPE_DACPAC_EMITTER §3). Two emissions of the same `(Catalog, Policy, Profile)` produce DACPACs that load to identical `TSqlModel` graphs.
- **DacFx-side**: idempotent redeploy. Deploying a model-equivalent DACPAC against a target whose schema already matches issues zero ALTERs.

The canary primitive that verifies the composition is **deploy → redeploy same DACPAC → assert second deploy issued zero ALTERs**. Add that assertion to the chapter 3 canary; CDC-safety becomes testable rather than asserted. **Action:** amend AXIOMS.md T1 (or add T1' / T13) to name the two-property composition explicitly; mandate the redeploy-zero-ALTER canary test.

### R3. User FK reflow — designable now (Appendix B §4)

**Resolution.** The shape is concrete. Add a Policy axis:

```fsharp
type UserMatchingStrategy =
    | ByEmail
    | BySsKey
    | ManualOverride of Map<SourceUserId, TargetUserId>
    | FallbackToSystemUser of TargetUserId

type Policy = {
    Selection : SelectionPolicy
    Emission  : EmissionPolicy
    Insertion : InsertionPolicy
    Tightening: TighteningPolicy
    UserMatching : UserMatchingStrategy   // new
}
```

A discovery pass `Passes.UserFkReflow` consumes `(ProfileSource, ProfileTarget, UserMatchingStrategy)` and produces `UserRemapContext = Map<SsKey, Map<SourceUserId, TargetUserId>>`. The data-emission triumvirate (StaticSeeds/MigrationDependencies/Bootstrap) consumes `UserRemapContext` to rewrite CreatedBy/UpdatedBy values at emission time. A32 already reserves the algebraic space; the F# types and pass are missing but specifiable. **Action:** record the design under DECISIONS.md alongside A32.

### R4. "Multi-environment machinery aspirational" — partially dissolves (Appendix B §5)

**Resolution.** The gap is smaller than it looks. Policy already models per-environment intent; Profile already models per-environment evidence. Running V2 four times with four `(Profile, Policy)` pairs is operationally a host-shell concern, not a core-IR concern. The IR does *not* need an `EnvironmentId`.

What's actually missing is one **property test predicate**: "for any two environments E1 and E2, `Project(catalog, policy_E1, profile_E1).Catalog ≡ Project(catalog, policy_E2, profile_E2).Catalog` modulo policy/profile-shaped variance." That's a chapter-3 canary test, not new machinery. **Action:** specify the predicate as an explicit canary-closure test in the chapter-3 plan.

### R5. Partial-state recovery — composable from existing parts (Appendix B §6)

**Resolution.** The vision frames partial-state as worst case but offers prevention only. Reasoning the recovery primitive: V2 already has the read-side adapter (extracting deployed schema as a Catalog) and the DacpacEmitter (Catalog → DACPAC). Composing them gives a `RemediationEmitter`:

```
RemediationEmitter : (deployed: Catalog, target: Catalog) -> RemediationDacpac
```

The "diff" between `deployed` (extracted from the partially-deployed DB) and `target` (V2's intended Catalog) is itself a Catalog (the missing kinds). DacpacEmitter on the diff produces the corrective artifact. This is a thin composition, not a new chapter — and it answers the worst-case framing the vision raised but didn't close. **Action:** add `RemediationEmitter` as a chapter-3-tail or chapter-4-head deliverable; it composes existing pieces.

### R6. Split-brain governance — derivable rule (Appendix B §7)

**Resolution.** During dual-track, V2 **emits-but-doesn't-ship**. The PR pipeline ships V1's artifact; V2's artifact is fed into the canary, which round-trips both V1 and V2 outputs through an ephemeral DB and asserts they agree on the SsKey-rooted Catalog. Disagreement blocks the PR. V2 → production transitions per-environment-per-artifact-type, gated on N consecutive green canary runs (suggest N=10) and explicit operator sign-off. Eliminates split-brain by construction: V2 never reaches production until V1 has been demonstrated equivalent N times. **Action:** record as a DECISIONS.md governance entry; reference from the cutover-fallback section of VISION.md.

### R7. T11 as decoration vs. structural — encodable (Appendix A §4)

**Resolution.** The skeptic was right that T11 currently reads as code-style discipline. But it can be **type-system-encoded**: if every emitter has signature `emit : Catalog -> Map<SsKey, ArtifactElement>` (or equivalent total map), then "mentions every Catalog kind by SsKey root" becomes a compile-time obligation — you cannot return the type without populating the keyset. Whether the current emitters use this shape, an implementation audit would settle. If not, that's the right refactor target. The algebra moves from labeled-discipline to structural. **Action:** open a chapter-3 spike to audit current emitter signatures and propose the refactor if the gap exists.

### R8. Unfalsifiable rhetoric — replaceable with acceptance criteria (Appendix A §1)

**Resolution.** Each rhetorical claim has a testable analogue:

- "V2 is the team's sovereignty over its own metadata" → *V2 answers three named operational questions about the domain in <M minutes without OutSystems Studio.* Name the questions.
- "Auditability is type-system-encoded" → *Every Diagnostics-bearing pass's output reconstructs the per-decision rationale without re-running the pass. Property-tested.*
- "Verifiable correctness" → *Canary catches at least one real emitter bug before publication during the cutover quarter.* Tracked.

**Action:** append "Acceptance criteria" section to VISION.md naming the testable analogues; commit to tracking them through the cutover quarter.

---

## What still needs actual work (reasoning won't ship it)

- Canary loop must be built.
- `SnapshotRowsets` must be built (the A1 bound is a code fix, not a doc fix).
- `DacpacEmitter`, `RefactorLogEmitter` must be built.
- The fallback ladder (Appendix D §6), the governance rule (R6 above), and the redeploy-zero-ALTER canary primitive (R2 above) can be **drafted in docs now** — they're free.

## The three things to append to VISION.md

The reviewers converged on three appendings the vision needs to earn its load-bearing claim:

1. **A "Cutover fallback" section** with the three-tier ladder (V1-only / V2-augmented-as-canary-only / V2-driver) and a T-30-day decision criterion. Appendix D §6 has the draft.
2. **A "Cutover-window governance" rule in DECISIONS.md** naming which system is canonical for which artifact during dual-operation. R6 above has the rule.
3. **An "Acceptance criteria" section** replacing unfalsifiable rhetoric with testable analogues. R8 above has the candidates.

## Closing assessment

The **load-bearing core** of the vision is the canary loop. If chapter 3 lands a green canary on a real Catalog with the redeploy-zero-ALTER assertion, V2 has a unique contribution V1 cannot reach — verification, not emission. That's the right framing.

The **scope risk** is real and concentrated in the "informational widening" section. Treat it as parking-lot, not roadmap.

The **biggest near-term hole** is governance (fallback + dual-system canonical-source rule). It's free to write and existential if the cutover slips.

Of the eight reviewer concerns I worked through (R1–R8), seven dissolve under reasoning into specific actions on docs or specifications; one (R7, T11 structural encoding) needs an implementation audit to confirm. None require new chapters. The vision is intellectually load-bearing; the risk is letting the post-cutover trajectory leak into pre-cutover sequencing. Hold the spine; defer the widening.
