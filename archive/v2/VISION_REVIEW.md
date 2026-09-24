# V2 Vision — Review and Synthesis

**Date:** 2026-05-08
**Reviewing:** the original `VISION.md` @ commit `2fb51ef` (preserved as the "Historical: revision 1 vision" section at the bottom of `VISION.md`).
**Method:** four parallel subagent evaluations (skeptical critique, cutover risk audit, scope/feasibility review, sequencing plan), one synthesis pass, one reasoning pass on resolvable concerns, then four follow-up subagents on work-smarter angles (canary as property test, V2-as-V1-canary dogfood, radical scope cut, type-system refactor).
**Outcome:** revision 2 of the vision is the body of `VISION.md`. The three appending recommendations from the synthesis below — cutover fallback ladder, governance rule, acceptance criteria — have been folded into revision 2 already.

This document is the synthesis plus eight appendices carrying the verbatim subagent reports. The synthesis is the primary reading path; each appendix is reference material consulted when more detail is needed on a specific axis.

## Contents

- [Synthesis](#synthesis)
  - [Where the four reviewers converged](#where-the-four-reviewers-converged)
  - [Concrete capability gaps the audit surfaced](#concrete-capability-gaps-the-audit-surfaced)
  - [Where reviewers diverged usefully](#where-reviewers-diverged-usefully)
  - [Recommended sequencing](#recommended-sequencing)
  - [Reasoning resolutions R1–R8](#reasoning-resolutions--concerns-dissolved-without-code)
  - [What still needs actual work](#what-still-needs-actual-work-reasoning-wont-ship-it)
  - [Closing assessment](#closing-assessment)
- [Appendix A — Skeptical Critique](#appendix-a--skeptical-critique)
- [Appendix B — Cutover Risk Audit](#appendix-b--cutover-risk-audit)
- [Appendix C — Scope and Feasibility Review](#appendix-c--scope-and-feasibility-review)
- [Appendix D — Implementation Sequencing Plan](#appendix-d--implementation-sequencing-plan)
- [Appendix E — Canary as Property-Test Surface](#appendix-e--canary-as-property-test-surface)
- [Appendix F — V2 Verifies V1 (Dogfood Plan)](#appendix-f--v2-verifies-v1-dogfood-plan)
- [Appendix G — Radical Scope Cut](#appendix-g--radical-scope-cut)
- [Appendix H — Type-System Refactor](#appendix-h--type-system-refactor)

---

# Synthesis

## Where the four reviewers converged

All four — independently — surfaced the same three holes:

**1. The vision names no cutover fallback.** If V2 isn't ready, what happens? Revision 1 closed with "hold the spine," not with "V1 stays warm until cutover+30." Every reviewer flagged this; the planner proposed a concrete three-tier ladder (V1-only / V2-augmented / V2-driver) with a T-30-day decision criterion (Appendix D §6). **Folded into VISION.md revision 2 as the §"Cutover fallback ladder."**

**2. "Cutover is V1's problem" is the elephant.** Revision 1 line 19 listed everything V1 already does — and that list *is* the cutover. V2's unique contributions (canary, DacpacEmitter, RefactorLogEmitter, user-FK reflow as Policy) were unbuilt. The vision asserted implicit-correctness can't carry the stakes; it did not demonstrate a specific cutover sub-task that breaks under V1 and succeeds under V2. **See R1 below — concern dissolved by sharper framing in VISION.md revision 2.**

**3. T1 byte-determinism ≠ CDC safety.** Revision 1 conflated "V2's emission is deterministic" with "SQL Server's deployment produces zero CDC events." The latter is a property of DacFx's diff planner against an existing schema. The right canary primitive — *redeploy against the same schema, assert zero ALTERs* — was unnamed. **See R2 below — folded into VISION.md revision 2 as the canary's `idempotentRedeploy` predicate.**

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
- **Scope reviewer (Appendix C)** flagged the most mis-prioritized item: **drift detection is underweighted.** It's listed as post-cutover trajectory, but it *is* the cutover's safety net. The read-side adapter is built for the canary anyway — pointing it at the four real DBs in a scheduled job is a small additive, not a new chapter. Pull forward. **Folded into VISION.md revision 2 as a chapter-3 byproduct.**
- **Scope reviewer's biggest cut**: the FakerEmitter's six-dimension quality scoring ("relational, commutative, descriptive, heuristic, correlative, entropic" — six adjectives, zero defined metrics). Defer the *scoring* indefinitely; ship Faker with one dimension when a consumer demands it. **VISION.md revision 2 cuts this entirely.**
- **Sequencing planner (Appendix D)** committed to the F#/C# boundary placement: F# `DacpacEmitter` produces T-SQL strings; C# wrapper owns DacFx, testcontainers, and the zip-determinization post-pass. F# Π stays pure. **Folded into VISION.md revision 2's chapter 3 plan.**

## Recommended sequencing

The synthesizing planner + scope reviewer + work-smarter pass converged on this chapter-3 order (revised after Appendix F's V2-verifies-V1 dogfood reframing):

**Chapter 3 — cutover-critical chorus**, in order:
1. **Read-side adapter** + comparator + minimal `Projection.Pipeline` (was 3.2). Has two consumers from day one: V1 verification + drift detection. Ships V2-augmented mode against V1 immediately.
2. **`SnapshotRowsets` adapter variant** (resolves JSON-projection lossiness, unblocks A1 for renames).
3. **`DacpacEmitter`** (F# emits T-SQL strings; C# DacFx wrapper in a new `Projection.Targets.SSDT.Dacpac` C# project owns BuildPackage + zip-determinization post-pass).
4. **Canary closure** — emit → testcontainers → deploy → read-back → SsKey-compare. **Add the redeploy-zero-ALTER assertion** (covers the CDC-safety claim Appendix B found unverified).
5. **`RefactorLogEmitter`** as Π over `CatalogDiff` — UUIDv5 maps V1 SSKey Guids to V2 identities.

**Pulled forward from "post-cutover trajectory":**
- **Drift detection** — the read-side adapter pointed at the four real environments on a schedule, surfacing deltas as Diagnostics findings. Cheap given (1). High cutover-safety leverage.

**Chapter 4 — production-deployment chorus:**
1. **SSDT DDL emitter (sibling Π) + CDC-aware data triumvirate** (StaticSeeds → MigrationDependencies → Bootstrap) with `EmissionPolicy` DU and per-table CDC-awareness. T11 cross-validates DACPAC vs. SSDT.
2. **User FK reflow as a real pass + new Policy axis** (UserMatchingStrategy: ByEmail / BySsKey / ManualOverride / FallbackToSystemUser). Appendix B was clear: this is a current capability gap, not a "make V1's behavior algebraic" inheritance.
3. **Operational diagnostics** (V2 equivalents of decision-log/opportunities/validations consuming the existing `Diagnostics<'a>` writer).
4. **`RemediationEmitter`** (composes read-side + DacpacEmitter or DdlEmitter over `CatalogDiff`).

**Explicit non-goals** for chapters 3–4: GraphQL emitters, FakerEmitter (especially the six-dim scoring), AI-agent substrate, recipes-beyond-the-canary's-compose, V1 sunset. Park as named active deferrals in DECISIONS.md with re-open triggers.

---

## Reasoning resolutions — concerns dissolved without code

Several of the reviewers' concerns dissolve under harder thinking. The following resolutions are on-record clarifications; they do not require implementation work, but should be folded into the canonical surfaces (VISION.md amendment, AXIOMS.md amendments, DECISIONS.md entries) per the append-only discipline. Most of R1–R8 are absorbed into VISION.md revision 2; the implementation work that remains is named in the next section.

### R1. "Cutover is V1's job — what does V2 uniquely add?" (Appendix A §2, Appendix C §1)

**Resolution.** V2's load-bearing differentiator is **the sibling chorus + verification**, not displacement of any single V1 surface. V2 emits multiple synchronized projections from a single Catalog × Policy × Profile triple — SSDT DDL (production deployment, promoted to Azure DevOps integration test), CDC-aware data inserts (`StaticSeeds` / `MigrationDependencies` / `Bootstrap`), DACPAC (fast iteration / baselining), refactor log, distributions. V1 hand-curates one surface; V2 emits the chorus and cross-validates it via T11. Two cutover demands explicitly require capabilities that V1's hand-curated SQL scripts cannot reach by incremental work:

- **Refactor.log records.** SQL scripts cannot communicate "this column was renamed" to a target database. Every redeploy of a renamed column under a script DROPs and CREATEs — losing data and triggering CDC noise. The refactor.log mechanism is the SSDT-native way to carry rename intent so subsequent deploys ALTER instead.
- **Idempotent diff-deploy across four environments on a repeatable cadence.** SQL scripts must be hand-ordered or templatized per environment; declarative artifacts (DACPAC and SSDT-with-refactor-log) let DacFx compute the diff against the current target state and only issue the deltas. This is what makes "schema and data evolve continuously; the extraction gets re-run regularly" tractable across four environments without bespoke per-environment work.

**Action:** done. VISION.md revision 2's §"What V2 uniquely contributes" elevates this as the load-bearing differentiator and articulates the two-lane verification posture (DACPAC fast lane + SSDT/data promoted lane).

### R2. "T1 byte-determinism ≠ CDC safety" (Appendix B §2, Appendix A §4)

**Resolution.** CDC noise comes from SQL Server applying ALTERs, not from V2's emission. The chain is: V2 emits → DacFx diffs vs. target → DacFx generates ALTERs → CDC events fire. T1 controls only the first step.

The right framing is a **composition of two independent properties**:
- **V2-side**: model-equivalence under DacFx round-trip (T1 amended for binary, already proposed in CHAPTER_3_PRESCOPE_DACPAC_EMITTER §3). Two emissions of the same `(Catalog, Policy, Profile)` produce DACPACs that load to identical `TSqlModel` graphs.
- **DacFx-side**: idempotent redeploy. Deploying a model-equivalent DACPAC against a target whose schema already matches issues zero ALTERs.

The canary primitive that verifies the composition is **deploy → redeploy same DACPAC → assert second deploy issued zero ALTERs** (and zero CDC records on tracked tables for the SSDT/data promoted lane).

**Action:** folded into VISION.md revision 2. The `idempotentRedeploy` property test is named in the verification loop; the redeploy-zero-ALTER-and-zero-CDC-record assertion is named for both the DACPAC fast lane (chapter 3) and the SSDT/data promoted lane (chapter 4.1). AXIOMS.md amendment to T1 (or new T13 for the two-property composition) remains a doc task.

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

A discovery pass `Passes.UserFkReflow` consumes `(ProfileSource, ProfileTarget, UserMatchingStrategy)` and produces `UserRemapContext = Map<SsKey, Map<SourceUserId, TargetUserId>>`. The data-emission triumvirate (StaticSeeds/MigrationDependencies/Bootstrap) consumes `UserRemapContext` to rewrite CreatedBy/UpdatedBy values at emission time. A32 already reserves the algebraic space; the F# types and pass are missing but specifiable.

**Action:** chapter 4.2 in VISION.md revision 2's plan. DECISIONS.md entry alongside A32 remains a doc task.

### R4. "Multi-environment machinery aspirational" — partially dissolves (Appendix B §5)

**Resolution.** The gap is smaller than it looks. Policy already models per-environment intent; Profile already models per-environment evidence. Running V2 four times with four `(Profile, Policy)` pairs is operationally a host-shell concern, not a core-IR concern. The IR does *not* need an `EnvironmentId`.

What's actually missing is one **property test predicate**: "for any two environments E1 and E2, `Project(catalog, policy_E1, profile_E1).Catalog ≡ Project(catalog, policy_E2, profile_E2).Catalog` modulo policy/profile-shaped variance." That's a chapter-3 canary test, not new machinery.

**Action:** specified in Appendix E as the `policyOrthogonal` predicate; named in VISION.md revision 2's verification loop section.

### R5. Partial-state recovery — composable from existing parts (Appendix B §6)

**Resolution.** V2 already has the read-side adapter (extracting deployed schema as a Catalog) and the DacpacEmitter (Catalog → DACPAC). Composing them gives a `RemediationEmitter`:

```
RemediationEmitter : (deployed: Catalog, target: Catalog) -> RemediationDacpac
```

The "diff" between `deployed` (extracted from the partially-deployed DB) and `target` (V2's intended Catalog) is itself a Catalog (the missing kinds). DacpacEmitter on the diff produces the corrective artifact. This is a thin composition, not a new chapter.

**Action:** chapter 4.4 in VISION.md revision 2's plan.

### R6. Split-brain governance — derivable rule (Appendix B §7)

**Resolution.** During dual-track, V2 **emits-but-doesn't-ship**. The PR pipeline ships V1's artifact; V2's artifact is fed into the canary, which round-trips both V1 and V2 outputs through an ephemeral DB and asserts they agree on the SsKey-rooted Catalog. Disagreement blocks the PR. V2 → production transitions per-environment-per-artifact-type, gated on N consecutive green canary runs (suggest N=10) and explicit operator sign-off. Eliminates split-brain by construction: V2 never reaches production until V1 has been demonstrated equivalent N times.

**Action:** folded into VISION.md revision 2's §"Cutover fallback ladder" (V2-augmented mode). DECISIONS.md governance entry remains a doc task.

### R7. T11 as decoration vs. structural — encodable (Appendix A §4)

**Resolution.** Type-system-encodable: every emitter returns `Result<ArtifactByKind<'element>, EmitError>` where `ArtifactByKind` is a private DU whose smart constructor enforces "every Catalog kind is in the keyset." T11 becomes a compile-time obligation, not an `Assert.Contains` discipline. Appendix H gives the concrete F# refactor — current emitters are `Catalog -> string` (verified at `RawTextEmitter.fs:183`, `JsonEmitter.fs:140`, `DistributionsEmitter.fs:201`); the type signature change is incremental, behind a discriminator (Appendix H §7).

**Action:** named in VISION.md revision 2's algebraic core. Implementation lives in chapters 4–5 per Appendix H §7's three-phase migration.

### R8. Unfalsifiable rhetoric — replaceable with acceptance criteria (Appendix A §1)

**Resolution.** Each rhetorical claim has a testable analogue:

- "Verifiable correctness" → *Canary catches at least one real emitter bug before publication during the cutover quarter.* Tracked.
- "CDC safety" → *`idempotentRedeploy` property holds across all generated catalogs at tier-2 and across the four real production schemas at tier-3.*
- "Identity" → *`renameSurvives` property holds for every `OssysOriginal` SsKey across the four-environment four-rename test plan.*
- "T11 structural" → *Every emitter signature is `Emitter<'element>`; the substring `Assert.Contains` tests retire.*
- "V1 sunset gate" → *ADMIRE.md `extracted` status requires a green canary, deferred until all four environments have run on V2 emissions for one full schema-evolution cycle.*

**Action:** done. VISION.md revision 2's §"Acceptance criteria" names all five.

---

## What still needs actual work (reasoning won't ship it)

- Read-side adapter must be built (chapter 3.1).
- `SnapshotRowsets` must be built (the A1 bound is a code fix, not a doc fix).
- `DacpacEmitter`, `RefactorLogEmitter`, `SsdtDdlEmitter`, `StaticSeedsEmitter` / `MigrationDependenciesEmitter` / `BootstrapEmitter`, `RemediationEmitter` must be built.
- The type-system refactor (`ArtifactByKind<'element>` + `SsKey` four-variant DU + `CatalogDiff`) must be implemented (Appendix H §7 — incremental over three chapters).
- AXIOMS.md amendments (T1 binary normal-form composition; T11 type-encoded; A1 type-stratified) — doc work, can be drafted concurrently with implementation.
- DECISIONS.md entries (R3 user FK reflow design; R6 cutover-window governance; readside-adapter-promoted-to-3.1) — doc work, can be drafted now.

## Closing assessment

The **load-bearing core** of the vision is the canary loop. If chapter 3 lands a green canary on a real Catalog with the redeploy-zero-ALTER assertion, V2 has a unique contribution V1 cannot reach — verification, not emission. That's the right framing.

The **scope risk** is real and concentrated in the (now-cut) "informational widening" section. Treat it as parking-lot, not roadmap.

The **biggest near-term hole** was governance (fallback + dual-system canonical-source rule). The fallback ladder is now in VISION.md revision 2; the DECISIONS.md governance entry remains to be written.

Of the eight reviewer concerns worked through (R1–R8), seven dissolve under reasoning into specific actions on docs or specifications; one (R7, T11 structural encoding) needs an implementation audit that Appendix H performed (current emitters are `Catalog -> string`; refactor target is `Emitter<'element>`). None require new chapters.

The vision is intellectually load-bearing; the risk was letting the post-cutover trajectory leak into pre-cutover sequencing. Revision 2 holds the spine; the widening is cut.

---

# Appendix A — Skeptical Critique

**Brief:** Pressure-test the strategic vision document. Find the weakest claims, hidden assumptions, and unfalsifiable framing. Goal is to find holes a believer would miss.

## Pressure-Test of VISION.md

### A.1. Unfalsifiable claims — the dogma layer

The document repeatedly leans on rhetoric that cannot be observed to fail:

- **"The algebra is not aesthetic; it is the structural condition for the cutover being trustworthy."** This is unfalsifiable. If V1 (no algebra) ships the cutover successfully, was it not "trustworthy"? The sentence can never be wrong.
- **"Lineage is constitutive, not decorative."** "Constitutive" is doing infinite work here. The testable claim would be "every emitted artifact carries a traceable decision chain a human can audit in N minutes." That's missing.
- **"Auditability is type-system-encoded."** A `Lineage<'a>` wrapper is a writer monad. It encodes that *something* was written, not that what was written is auditable. There is no claim that lineage entries are *readable*, *queryable*, or *complete* relative to a defined audit standard.
- **"V2 is the team's sovereignty over its own metadata."** Unfalsifiable identity claim. What observation would refute it?

### A.2. Forcing-function reality check — the cutover is V1's problem

Per VISION.md line 19, V1 already does: "extraction; specializations; opinionated formatting; topologically-sorted two-phase inserts; user FK reflow between environments; profile interventions on the data; standalone domain record injection for legacy migration teams; environment promotion via Azure DevOps PRs."

That is *the cutover*. Every load-bearing capability the cutover requires is named as already-shipping in V1 (78K LOC, vs. V2's 7K LOC of pure core with adapters and emitters still in flight). The vision concedes this, then pivots: "V1's correctness is implicit... V2 makes the correctness explicit, verifiable."

But the canary loop — the one mechanism that would make correctness "verifiable" — is unbuilt. README line 71-76 lists `Projection.Pipeline` (canary orchestration) and `Projection.Adapters.Sql.ReadSide` as **slots reserved for future sessions, not yet built**. The forcing function is stated to require something the system does not yet contain.

If the cutover is imminent, V1 ships it. V2's "uniquely adds" reduces to: a future canary loop, a future DacpacEmitter, a future RefactorLogEmitter. None gated on calendar; all gated on session sequencing.

### A.3. Claim-to-evidence gaps — the promised-vs-built ledger

Built (per README): RawTextEmitter, JsonEmitter, DistributionsEmitter, OSSYS CatalogReader (in flight), three SQL adapter files. 631 tests passing.

Promised in VISION.md but **not built**: DacpacEmitter, RefactorLogEmitter, StaticSeedsEmitter, MigrationDependenciesEmitter, BootstrapEmitter, FakerEmitter, GraphQL schema/resolver emitters, Post-IS external entity declaration emitter, the canary loop, SnapshotRowsets, six-dimension synthetic-data quality scoring ("relational, commutative, descriptive, heuristic, correlative, entropic" — six adjectives, zero defined metrics), drift detection, Playwright agent integration, Terraform-equivalent recipe emission.

That is the entire load-bearing surface. The shipped emitters are the easy ones (text and JSON serialization of an in-memory DU). DacFx, refactor-log binary format, FK-aware data emission, and ephemeral SQL Server orchestration are the hard ones, and zero of them exist.

### A.4. Algebraic inflation — load-bearing or decorative?

Genuinely load-bearing: closed DUs + total pattern matching, smart constructors returning `Result`, `SsKey` as a distinct type. These produce real compile-time errors.

Decorative: "Π ∘ E", "T11 sibling-Π commutativity," "writer-monad lineage carriage," "structural-commitment-via-construction-validation." Drop the symbols and you have: "separate enrichment from rendering, run the same input through multiple emitters, log decisions as you make them, validate in the constructor." Every competent F# engineer does this. The notation does not generate the discipline; the discipline generates good code, and the notation labels it. The vision conflates the two.

T11 ("every Π's output mentions every Catalog kind by SsKey root") is not a *theorem* — it's a code style guideline for emitters. Calling it a theorem inflates its status.

### A.5. Scope creep — yes, the widening section is scope explosion

The "informational widening" section turns a DDL emitter into:
- Platform-survival layer ("V2 outlives OutSystems")
- Longitudinal analytics platform (Profile-across-time)
- AI-agent substrate (Playwright, test, code, copilot agents)
- Synthetic-data quality scoring system
- Terraform/Pulumi-equivalent IaC tooling
- GraphQL endpoint server
- Drift-detection daemon
- Per-developer local dev environment manager
- Open-source community resource

For a 7K-LOC codebase 25 sessions in, with the canary loop and DacpacEmitter unbuilt, this is not strategic clarity. It is the founder's-deck slide. Each item earns a future chapter; the document treats them as if listing them confers them.

### A.6. The fallback question, unanswered

If V2 isn't ready, the team falls back to V1, which already does the work. The vision never names a failure mode where V1 fails *and* V2 succeeds. "Implicit correctness can't carry the stakes" is asserted, not demonstrated — V1 has done four-environment promotion already; what specific cutover sub-task breaks under V1 that V2 fixes?

### A.7. Other smells

- **"V2 admires V1; V2 extracts from V1 under empirical pressure"** — anthropomorphizing the codebase. ADMIRE.md as ritual is fine; "V2 admires V1" is not a strategic statement.
- **"The cutover is the load test"** — a load test you cannot afford to fail is not a test, it is the production event.
- **"Hold the spine"** — closing rhetoric replaces a metrics-of-success section. There is no acceptance criterion for "V2 earned its existence."
- **A1's bound is a real footnote-to-watch.** AXIOMS.md lines 46-72 admit that identity-survives-rename does not hold through the current input path. VISION.md elevates A1 to "the temporal axiom" without acknowledging the bound. The vision speaks past its own algebra's caveats.

### A.8. Strengths (the one paragraph)

The V1↔V2 separation discipline is genuinely sound: V2 is additive, cherry-pick safe, trunk unaffected. The closed-DU + total-match style and structural-commitment-via-construction validation are real engineering discipline producing real compile-time guarantees. The sibling-emitter cross-validation idea (RawText vs. Dacpac disagreement = bug) is a legitimate testing strategy when both sides exist. Documenting A1's bound openly (AXIOMS.md) shows the project can be honest about its limits — which makes the vision's overreach more conspicuous, not less.

### A.9. Bottom line

VISION.md reads as a manifesto written for an audience of believers, dressed in algebra to look load-bearing. The cutover is V1's job; V2 is a parallel rebuild whose unique contributions (canary, DacpacEmitter, RefactorLog) are unbuilt; the "informational widening" is a roadmap of nine products masquerading as a strategy. The right document is half this length, drops "sovereignty" and "constitutive," and names two acceptance criteria: *the canary loop catches a real artifact bug before publication*, and *V1 is sunset on a date*.

---

# Appendix B — Cutover Risk Audit

**Brief:** Database-migration risk audit. Evaluate whether the vision actually meets the operational risk profile of the External Entities cutover scenario it describes. Verdicts: covered / partial / gap / aspirational.

## V2 Risk Audit — Cutover Vision vs. Current State

**Bottom line:** the strategic vision is coherent; the *current* sidecar implementation covers maybe a third of what the cutover scenario demands. Most of what makes V2 "trustworthy under the cutover" is forthcoming, not shipped. Evidence cited from `VISION.md`, `README.md`, `AXIOMS.md`, `Policy.fs`, and `CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md`.

### B.1. Does the canary loop catch the failures that matter? — **Aspirational**

The canary loop is **unbuilt**. `README.md:71-76` shows `Projection.Pipeline/` (canary), `Projection.Adapters.Sql.ReadSide/`, and `Projection.Targets.SSDT.DacpacEmitter/` as *reserved slots, not yet built*. README.md:111-118 confirms "the pipeline canary" is one of the two un-built primitives gating substantive forward work. No deployment, no testcontainers, no read-back today.

Even when built, per the prescope (CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md:304-348), the loop is *emit → DacFx deploy → DacFx extract → SsKey-compare*. That covers:
- Schema correctness against an ephemeral SQL Server.
- DacFx deploy-readiness (FKs resolve, types valid).
- Cross-emitter T11 commutativity.

It does **not** cover, for the named failure modes:
- **CDC noise in production**: ephemeral DB has no CDC enabled, no prior schema state, no incremental ALTER. The canary verifies clean-slate deploy, not no-op redeploy against an existing CDC-enabled database (see B.2).
- **User FK misreflow**: no machinery exists to *test* user-matching outcomes; the canary compares structure, not data plane content.
- **Partial-state hybrids**: a canary that passes says nothing about half-applied production deploys.
- **RefactorLog gaps**: RefactorLogEmitter doesn't exist (B.3); the canary cannot detect a missed rename record because nothing emits one.
- **Environment drift**: canary runs a single (Catalog, Profile, Policy) triple at a time; cross-environment comparison is not a canary feature, it's a post-hoc analytical use of Profile (VISION.md:99).
- **Two-phase insert ordering bugs**: A33 mandates `TopologicalOrder` for data emission, but the data emitters (`StaticSeedsEmitter` / `MigrationDependenciesEmitter` / `BootstrapEmitter`) are forthcoming (VISION.md:74). Canary today wouldn't deploy data; ordering bugs would not be exercised.

### B.2. CDC-safe idempotency — **Gap (mechanism unnamed)**

VISION.md:56 names "T1 projection-language-normal-form" as the algebraic move. T1 (AXIOMS.md:301-302, T1-amended:452-461) says `Project` is a pure function — same triple, byte-identical surface. **That is determinism of V2's emission, not idempotence of SQL Server's deployment.**

CDC change records are emitted by SQL Server when DacFx deploys an ALTER. DacFx's incremental-deploy planner decides what ALTERs to run based on diff between source DACPAC and target schema. Two byte-identical V2 DACPACs deployed against a target whose schema already matches *should* produce zero ALTERs and zero CDC events — but that's a property of **DacFx's diff planner**, not of T1.

The vision conflates the two. Real CDC safety requires:
- Byte-identical DACPACs (T1 — partially built; CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md:408-414 flags that DacFx embeds wall-clock timestamps in Origin.xml, breaking byte equality, and chapter-open will likely amend T1 to "content-equality via DacFx round-trip").
- DacFx producing zero-ALTER plans on no-op redeploy (an empirical property of DacFx + the target DB state, untested in V2).
- No DacFx options that issue cosmetic ALTERs (`DROP/CREATE` on column reorderings, etc.).

Neither of the latter two is named in VISION.md. The gap is real: **the vision does not name a CDC-noise verification surface**. A canary that runs deploy twice in succession against the same ephemeral DB and asserts the second deploy issues zero ALTERs would be the right primitive; not present, not scoped.

### B.3. Identity preservation across renames — **Gap**

Three problems compound:

(a) **A1 is bounded** through the current `SnapshotJson` input path (AXIOMS.md:47-72). V1's JSON projection strips SSKey columns; V2 *synthesizes* SsKey from name fields. *Renames in the source platform produce different SsKey values in V2's IR through this path.* A1's identity-survives-rename guarantee **does not hold** for renames on the active path.

(b) The fix is `SnapshotRowsets` — the canonical input variant (README.md:82-86, AXIOMS.md:60-68) — which is **operator-decided, not yet built**. Until it lands, V2 cannot honor A1 for a single rename across schema versions through its current adapter.

(c) **RefactorLogEmitter is forthcoming** (VISION.md:73). Even with SnapshotRowsets and stable SsKeys, no sibling Π today emits the rename records SQL Server's refactor log consumes, and no facility threads V1↔V2 identity via UUIDv5.

For the cutover's "RefactorLog records that need to survive across schema versions" demand: **V2 currently supports zero renames across schema versions in production-trustworthy form.** Vision claim and current state are far apart.

### B.4. User FK reflow — **Gap (V2 does not currently do this)**

VISION.md:19 says V1 already does this; VISION.md:56 says V2 "inherits and makes algebraic." Reading `Policy.fs` end-to-end (lines 1-533): the Policy DU has Selection / Emission / Insertion / Tightening only. **There is no UserMatching axis, no UserRemap configuration, no per-environment user-matching strategy type.** `InsertionPolicy` is `SchemaOnly | InsertNew | Merge | TruncateAndInsert` (lines 31-35) — no carrier for user-mapping data.

The algebra has *reserved space*: A32 (AXIOMS.md:465-489) frames the UAT-Users transform as "a discovery pass producing `UserRemapContext`, two sibling Π's consuming it" (DECISIONS.md:258-268). But `UserRemapContext` is referenced only in axioms and decisions — no F# type, no pass, no Π consumes it. Search confirms: zero matches for `UserRemap` / `CreatedBy` / `UpdatedBy` outside docs.

For the cutover today: **V2 cannot perform user FK reflow.** V1 must. The vision claim is aspirational with reserved algebraic space, not implemented capability.

### B.5. Multi-environment Profile/Policy machinery — **Aspirational**

VISION.md:54 frames four environments as "same algebra, four (Profile, Policy) pairs." The algebraic move (A18 amended, A34 — Profile independence) is sound. But **machinery to drive it does not exist**:

- No host / CLI shell — `Projection.Host.Cli/` is a reserved slot (README.md:78), unbuilt.
- No notion of *EnvironmentId* in the IR or in Policy. A `Policy` value carries no environment label; running V2 four times against four distinct Policy values is the operator's responsibility, completely unautomated.
- No machinery to load environment-specific Profile (`ProfileSnapshot.fs` exists in Adapters.Sql, but pointing it at four targets is host-shell concern that's not built).
- No machinery to assert "the four artifacts agree on Catalog and disagree only on (Profile, Policy)-induced fields" — that property is the algebraic claim VISION.md:54 makes, but no test asserts it.

Driving four environments today: an operator scripts four invocations of an unbuilt host, manually managing four Policy values and four Profile sources. That is not "V2 makes this algebraic" — it is "V2 does not yet do this."

### B.6. Rollback / partial-state recovery — **Gap (prevention only)**

VISION.md acknowledges "partial cutover leaves hybrid state structurally hard to recover from" as worst case (line 13) but **never names a recovery move**. The five demands (52-60) are all about *prevention*: verifiable correctness, multi-environment consistency, CDC-safe idempotency, identity preservation, provenance. No demand addresses what happens when the canary passes and prod deploy fails partway.

V2's contribution to recovery is structural-but-passive:
- A22 content-addressed snapshots and T7 snapshot deduplication mean a prior good Catalog can be re-emitted bit-identically. Recovery target exists structurally.
- Lineage and Diagnostics writers carry decisions, so a post-mortem has audit material.

But there is no *rollback artifact*, no "diff the partial deploy and emit a remediation DACPAC," no transactional deploy primitive. **If canary passes and prod deploy fails mid-way, V2 contributes a re-emittable target snapshot and an audit trail; the human operates the rollback.** That's a real gap against "structurally hard to recover from."

### B.7. Dual-system risk — **Gap (no governance named)**

V2 is "additive" (README.md:6-8, "V1 continues to operate, V2 is additive, every commit cherry-pick safe"). ADMIRE.md tracks per-component status (admiring → extracting → extracted) but **VISION.md and the canonical docs do not specify which system is canonical for which artifact at which moment** during the cutover. The phrase "V1 sunset" is post-cutover trajectory (VISION.md:117), not a cutover-window governance rule.

Concrete split-brain risk: V1 emits a DACPAC, V2 emits a DACPAC, they disagree (different identity through the JSON-projection-lossiness path; different rename handling; different defaults), Azure DevOps merges whichever PR lands first. Nothing in V2's surfaces names "V1 ships X for the cutover; V2 ships Y; here is the override rule." The closed-DU-rigorous-extraction discipline ADMIRE tracks is *per-component algebraic placement*, not *per-artifact-type cutover ownership*.

This is the most under-specified risk in the vision relative to the operational scenario.

### B.8. What's actually load-bearing for cutover safety

Must ship for V2 to deliver on the vision's risk claims:

1. **`SnapshotRowsets` adapter variant** + **RefactorLogEmitter** — without these, A1 is bounded, identity isn't stable across renames, and no rename records emit. The cutover's repeatable-cadence demand requires both.
2. **`Projection.Pipeline` canary** with the **redeploy-against-existing-schema CDC-zero-ALTER assertion** explicitly added (not just clean-slate deploy) — the CDC-safety claim is unverified without it.
3. **User FK reflow as a real pass + sibling Π** — the algebraic space (A32, UserRemapContext) is reserved but the F# types, pass, and Π are not written. The cutover scenario names this as a per-environment demand; V2 currently cannot perform it.
4. **Cutover-window governance rule in DECISIONS** naming which system is canonical for each artifact type during dual-operation, with an explicit V1-sunset gate per artifact — to close the split-brain risk before operators are choosing between two PRs that both pass review.

A fifth, lower priority but worth surfacing: a **partial-deploy remediation primitive** (diff partial state vs. target snapshot, emit corrective DACPAC). The vision frames partial-state hybrid as worst case but offers prevention only; recovery is unfunded.

---

# Appendix C — Scope and Feasibility Review

**Brief:** Delivery-focused scope review. Separate the load-bearing core from the aspirational frill; report what should be in scope for the next 1-2 quarters. Be ruthless.

## Scope review: V2 vision vs. cutover-quarter feasibility

### C.1. What's load-bearing for the cutover

The vision is explicit about what earns V2's existence: "**The cutover earns V2's existence**" (line 109). Read against the five demands (lines 51–60), the **must-ship-before-cutover** set is small:

- **DacpacEmitter** (binary T1) — without it, V2 emits no deployment artifact. Cutover-blocking by definition.
- **SnapshotRowsets adapter** — resolves the JSON-projection-lossiness class (SsKey, EspaceKind, isSystemEntity per HANDOFF). Without it, the Catalog flowing into emit is structurally lossy. The `CatalogReader.fs` SnapshotJson path is the current source.
- **Projection.Pipeline canary** with read-side adapter and round-trip compare — this *is* demand #1 ("verifiable correctness," lines 52–53). The vision frames the canary as the proof of cutover-trustworthiness; you cannot ship the cutover with implicit correctness because (line 21) "The cutover scales the stakes past what implicit correctness can carry."
- **RefactorLogEmitter** — explicitly tied to demand #4 (line 58: "RefactorLog records carry rename history across schema versions"). The forcing function names it (line 11: "RefactorLog records that need to survive across schema versions").
- **StaticSeedsEmitter / MigrationDependenciesEmitter / BootstrapEmitter** — the data triumvirate is what gets the legacy-domain reflow workflow operational. Demand #3 ("CDC-safe idempotency," line 56) names "Topologically-sorted two-phase insertion" — V1 has it; V2 must inherit.
- **User FK reflow under Policy** — already in the algebra via the four-axis Policy, but the cutover-specific flows (Dev → UAT user-matching strategy) need to be exercised end-to-end through the canary at minimum once per environment.

**Post-cutover trajectory** (the vision agrees explicitly, lines 108–118): GraphQL emitters, FakerEmitter, drift detection on deployed databases, per-developer local environments, AI-agent substrate, recipes, V1 sunset, open-source. The vision's own sectioning (§"The post-cutover trajectory") is the sequencing commitment.

### C.2. What's already shipped that the vision underweights

The vision lists the three emitters and waves at "three boundary adapters." This understates how much of the chorus's *infrastructure* is in place:

- The **strategy-layer codification** at its stability mark (README §"The strategy layer") — every future tightening intervention slots in without rework.
- The **Diagnostics writer + Lineage<Diagnostics<'a>> dual composition** (HANDOFF §load-bearing). This is the substrate for the three-channel diagnostics the vision treats as forthcoming. The split is deferred (HANDOFF: "single channel sufficient at all chapter-2 consumers"), but the writer plumbing is done.
- **`SnapshotSource` closed DU with reserved variants** (`SnapshotRowsets`, `LiveOssysConnection`) — adding the canonical rowset variant is variant-addition, not architectural rework.
- **The four-axis Policy** (Selection, Emission, Insertion, Tightening) is built. Policy-driven environment-specific shaping (Profile and Policy carrying environment variance, line 54) is a configuration question, not a build question.
- **T11 sibling-Π commutativity** is a structural property the existing three emitters already honor; new sibling Π's inherit it.
- **The OSSYS adapter (25 translation rules)** — the hard part of catalog production is shipped.

The vision says "Currently shipped: RawText, Json, Distributions" in three lines; what's actually shipped is roughly 60% of the algebraic chorus's invariant scaffolding.

### C.3. Risk verdicts on informational widening

- **FakerEmitter with six-dimension quality scoring** (line 75, 101): **(c) likely to get cut** for cutover scope. Already gated on third evidence type per README. Six-dimension quality scoring is research-grade; the cutover doesn't require it. Faker's existence is credible post-cutover; *quality scoring against six dimensions* is speculative-but-cheap-to-keep-on-roadmap.
- **GraphQL schema and resolver emitters** (lines 76, 113): **(b) speculative-but-cheap**. The "isomorphism observation" is real — sibling Π for GraphQL is plausible — but no cutover demand exists. Keep on roadmap; do not build pre-cutover.
- **Post-Integration-Studio external entity declaration emitter** (line 77): **(a) credibly forthcoming**. This is the *cutover's downstream consumption surface* (line 77). It earns its place from the forcing function. Probably ship-or-stub for cutover; could be operationally produced by hand if necessary, but the algebraic move is to emit it.
- **AI-agent substrate / catalog as ontological grounding** (line 103): **(d) red flag for scope drift**. Beautiful framing but zero forcing function. "AI agents are consumers, not just collaborators" is a thesis statement, not a deliverable.
- **Recipes-as-Terraform / docker compose / Playwright invocations** (lines 105, 111): **(b) cheap-to-keep**, with one exception — a docker compose file for the canary's testcontainers setup is a byproduct of the canary work, essentially free. Per-developer Docker SQL Server (line 111) is **(c)** for cutover quarter.

### C.4. Single biggest scope risk

**The FakerEmitter's six-dimension quality scoring** (lines 75, 101–102: "Profile across six metric dimensions: relational, commutative, descriptive, heuristic, correlative, entropic" and "Quality becomes a number… V2 can self-evaluate its synthetic outputs and iterate to threshold").

This is the single item most likely to consume a quarter of effort and contribute zero cutover safety. The cutover does not need synthetic data; it needs *real* migrated data that round-trips correctly. Faker is post-cutover trajectory by the vision's own §"The post-cutover trajectory" framing. The six-dimension scoring is research; defer the *scoring* indefinitely and ship Faker (if at all) with a single dimension when a consumer demands it.

### C.5. Single biggest underweighting

**Drift detection / read-side adapter operating against deployed databases continuously** (line 114). The vision lists this as one bullet under "post-cutover trajectory," but operationally it *is* the cutover's safety net. Four environments, CDC-dependent features in production, repeatable cadence — the failure mode named at line 13 ("cutover fails after partial completion, leaving the system in a hybrid state that's structurally hard to recover from") is exactly what continuous drift detection prevents from compounding silently. The read-side adapter is built for the canary anyway (`Projection.Adapters.Sql.ReadSide`); pointing it at the four real databases in a scheduled job is a small additive, not a new chapter. Pull this forward; do not let it sit in "post-cutover trajectory."

Also under-emphasized: **the V1 → V2 cutover's own rollback plan**. ADMIRE.md transitions are mentioned (line 117), but the *operational* question of "if V2 emission is wrong on environment N, how does the team revert that environment to V1 emission within hours" is not in the vision. See C.7.

### C.6. Recommended Q1/Q2 commitment

Build/ship pre-cutover:

- **`SnapshotRowsets` variant of `SnapshotSource`** in `Projection.Adapters.Osm.CatalogReader` — first; everything downstream inherits a non-lossy Catalog. Per subagent #5's pre-scope, parallel-to-or-before canary.
- **`Projection.Pipeline` canary** with `Projection.Adapters.Sql.ReadSide`, testcontainers SQL Server (version-pinned to prod), DacFx loaded TSqlModel round-trip, and SsKey-rooted compare. This is demand #1 made operational.
- **`Projection.Targets.SSDT.DacpacEmitter`** with T1 amended for binary normal form (subagent #4 flags `BuildPackage` non-determinism — amend T1 explicitly). Plus `RefactorLogEmitter` as a sibling Π.
- **Data triumvirate**: `StaticSeedsEmitter`, `MigrationDependenciesEmitter`, `BootstrapEmitter` with `EmissionPolicy` (AllRemaining / AllExceptStatic / AllData) per session-17 strategic frame.
- **Drift-detection job**: read-side adapter pointed at all four environments, scheduled, surfacing differences as Diagnostics findings into a known channel. Cheap given the canary's read-side is already built; high cutover-safety leverage.

Explicitly defer to post-cutover trajectory: FakerEmitter (and especially the six-dimension scoring), GraphQL emitters, AI-agent substrate, per-developer local environments, recipes-beyond-the-canary's-compose, V1 sunset (run dual-track, sunset later), open-source. These belong in `DECISIONS.md` as named active deferrals with re-open triggers, mirroring the existing discipline.

### C.7. Sustainability / fallback

~25 sessions over ~2 weeks with V1 still shipping. Through cutover quarter, dual-track is sustainable *only if* Q1/Q2 commitment is the five items above and the informational-widening surface is genuinely held off. The vision's discipline of "IR grows under evidence, not speculation" applies upward to scope: the chorus grows under cutover pressure, not under aspirational pressure.

**The vision does not name a fallback if V2 is unfinished at cutover time.** This is the gap. Recommend appending a §"Cutover fallback" to VISION.md naming: (a) V1 remains the production emitter for any environment whose canary has not gone green; (b) the cutover proceeds environment-by-environment in promotion order (Dev → QA → UAT → Prod), with V2 gating on green canary per environment and V1 retained as the rollback target; (c) ADMIRE.md `extracted` status requires a green canary, not just a passing differential test; (d) V1 sunset deferred until all four environments have run on V2 emissions for one full schema-evolution cycle. Without this, the cutover has no defined off-ramp, and the forcing-function failure mode (line 13) becomes an unbounded liability.

The vision is intellectually load-bearing. The risk is not the algebra; it is letting the post-cutover trajectory leak into pre-cutover sequencing. Hold the spine; defer the widening.

---

# Appendix D — Implementation Sequencing Plan

**Brief:** Software-architect implementation plan derived from the vision. Sequence chapters 3 and 4. Address F#/C# boundaries, test strategy per phase, and propose a fallback ladder for the cutover.

> *Note: the chapter 3 ordering here was superseded in part by Appendix F's V2-verifies-V1 reframing, which pulls the read-side adapter to chapter 3.1 (it has two consumers from day one rather than gating on DacpacEmitter). VISION.md revision 2 carries the revised order.*

## Chapter 3 + Chapter 4 implementation plan

### D.1. Chapter 3 sequencing — argued order

Run chapter 3 as a four-phase arc: **(3.1) SnapshotRowsets → (3.2) read-side adapter → (3.3) DacpacEmitter → (3.4) canary closure → (3.5) RefactorLogEmitter**. The ordering is forced by two real dependencies and one risk-management choice.

**3.1 SnapshotRowsets first** (5–6 substantive slices). The DACPAC pre-scope §1 names "real Catalog flowing end-to-end through a pipeline exercising T11 sibling-Π commutativity on real metadata" as the trigger that fired the DacpacEmitter cash-out. That trigger is louder when the Catalog actually carries `EntitySsKey`, `EspaceKind`, and `IsSystemEntity`. The SnapshotRowsets pre-scope §7 is explicit: open this **parallel-to or before canary**, so canary inherits a Catalog with full SsKey carriage rather than the synthesized `OS_KIND_*` SsKeys — otherwise canary's compare-by-SsKey rests on a placeholder identity and any A1 violation surfaces only after the canary chapter closes. SnapshotRowsets also has the smallest surface (no DacFx, no testcontainers); landing it first burns down the JSON-projection-lossiness class before adding canary's mechanical complexity.

**3.2 Read-side adapter second** (the canary's "back" half). Pre-scope DACPAC §5 explicitly recommends "read-side adapter first, then DacpacEmitter" inside the canary chapter, on the argument that the read-side has two consumers from day one (canary round-trip and future drift detection) while DacpacEmitter has only canary as near-term consumer. The read-side defines the round-trip target before the emit shape is committed. Build it under `src/Projection.Adapters.Sql.ReadSide/` (slot already reserved in README §Layout) consuming `DacServices.Extract` output as the input substrate.

**3.3 DacpacEmitter third**. Now there is a Catalog with real SsKeys (3.1) and a round-trip target to be tested against (3.2). Implement against the minimal first slice in DACPAC pre-scope §5: single-table Catalog, content-equality via DacFx round-trip rather than byte-equality. **Defer byte-determinism to a post-pass** (see D.4 below).

**3.4 Canary closure**. Wire 3.1 + 3.2 + 3.3 inside `Projection.Pipeline` (C#): emit dacpac → testcontainers ephemeral SQL Server → `DacServices.Deploy` → read-side `Extract` → compare by SsKey root. T11 commutativity test surface lights up here. This is the chapter's structural deliverable — the algebra goes from claim to proof.

**3.5 RefactorLogEmitter last** in chapter 3. VISION §"five demands and the algebraic moves" couples this to A1 + UUIDv5 + cross-version identity. SnapshotRowsets must land first (the V1 SSKey Guids it surfaces are exactly UUIDv5's input space). Defer to chapter 3 tail rather than chapter 4 because the cutover demand (rename history surviving across schema versions) is in chapter-3 territory, not chapter-4 trajectory territory.

**Cross-module FK** is the tactical-completeness step from `HANDOFF.md`. Land it inside slice 2 or 3 of SnapshotRowsets (rule 16's same-module assumption tests under actual SsKey carriage) — not as a standalone slice.

### D.2. Chapter 4 sequencing

**4.1 Data-emission triumvirate** (StaticSeeds → MigrationDependencies → Bootstrap), in that order. StaticSeeds is structurally smallest: VISION §"sibling chorus" already names it, and DACPAC §2 routes Static populations to it. EmissionPolicy DU (`AllRemaining` default / `AllExceptStatic` / `AllData`) lives in `Projection.Core/Policy.fs` because it is intent, not evidence; emitters dispatch under it but never consume Policy directly (A18 amended). Define the DU when StaticSeeds lands, expand variants when MigrationDependencies and Bootstrap force them.

**4.2 User FK reflow as algebraic Policy**. VISION §"forcing function" names this as cutover-adjacent work; it is post-cutover-substrate machinery. Implement as a pass under `Projection.Core/Passes/UserFkReflow.fs` consuming a new Policy axis (UserMatchingStrategy: ByEmail / BySsKey / ManualOverride). Reflow is environment-specific intent; multi-environment Profile/Policy demand from VISION is satisfied by the same algebra running against four (Profile, Policy) pairs.

**4.3 Drift detection**. Composes 3.2's read-side adapter with 4.1's data-emission baseline: extract deployed schema → compare to source Catalog → surface delta as `Diagnostics` finding. Lives in `Projection.Pipeline` (C#), reuses canary's read-side. No new module.

**4.4 Operational diagnostics V2**. V1's decision-log/opportunities/validations surfaces. Implement as a chapter-4 deliverable consuming the existing `Diagnostics<'a>` writer; the three-channel split (operator/auditor/developer) deferred from chapter 2 fires here if the consumers diverge.

### D.3. Critical files to touch / create

The directory layout: there is no `Projection.Emitters/` — sibling Π's live under `src/Projection.Targets.{SSDT,Json,Distributions}/`. There is no `Projection.Pipeline/` yet (slot reserved, README §Layout). Adapters live under `src/Projection.Adapters.{Sql,Osm}/`.

**Chapter 3**:
- `src/Projection.Adapters.Osm/CatalogReader.fs` — extend `SnapshotSource` DU with `SnapshotRowsets of RowsetBundle`; add `parseRowsetBundle`.
- `src/Projection.Adapters.Osm/RowsetBundle.fs` (new) — F# DTO records for rowsets 1–3 first.
- `src/Projection.Adapters.Sql.ReadSide/` (new F# project) — DACPAC-extract → Catalog translation.
- `src/Projection.Targets.SSDT/DacpacEmitter.fs` (new) — pure F# `Catalog -> Result<byte[]>`.
- `src/Projection.Targets.SSDT/RefactorLogEmitter.fs` (new) — sibling Π for rename records.
- `src/Projection.Pipeline/` (new C# project) — DacFx wrapper, testcontainers harness, canary orchestration.

**Chapter 4**:
- `src/Projection.Core/Policy.fs` — extend with `EmissionPolicy` DU, `UserMatchingStrategy`.
- `src/Projection.Targets.StaticSeeds/StaticSeedsEmitter.fs` (new project + module).
- `src/Projection.Targets.MigrationDependencies/`, `src/Projection.Targets.Bootstrap/` (new projects).
- `src/Projection.Core/Passes/UserFkReflow.fs` (new pass).
- Drift detection lives inside `Projection.Pipeline` (C#); no new F# project.

Tests mirror existing convention under `tests/Projection.Tests/`: `RowsetBundleTests.fs`, `DacpacEmitterTests.fs`, `ReadSideAdapterTests.fs`, `CanaryRoundTripTests.fs`, `RefactorLogEmitterTests.fs`, `StaticSeedsEmitterTests.fs`, `UserFkReflowTests.fs`, `EmissionPolicyTests.fs`. Cross-source parity (JSON ↔ Rowsets) lives in `OsmCatalogReaderDifferentialTests.fs` (extend, don't fork).

### D.4. Architectural trade-offs

**F#/C# boundary for DacFx.** Land it where DACPAC pre-scope §1 placed it: F# `DacpacEmitter` produces T-SQL DDL strings; C# wrapper inside `Projection.Pipeline` (or a dedicated `Projection.Targets.SSDT.Dacpac` C# project) owns `TSqlModel.AddObjects`, `BuildPackage`, `IDisposable` lifetimes, and exception-driven validation. Seam is `Catalog -> Result<byte[]>`. Rationale: DacFx's API is dictionary-of-properties + mutation-by-script-add + disposable-scopes — the exact "object-instantiation-heavy, foreign-API-I/O" shape `DECISIONS 2026-05-09` sends to C#. F# Π stays pure; the algebra holds. **Recommend the dedicated C# project over folding it into `Projection.Pipeline`** for module cohesion (the SSDT target's binary half).

**Canary pipeline language.** C#, per `DECISIONS 2026-05-15` and the slot reserved in README. Testcontainers, DacFx deployment, and ephemeral SQL Server orchestration are all natural-language-of-the-boundary C# territory. F# core consumes the Catalog round-trip result as a value type.

**Determinism boundary.** DACPAC pre-scope §3 names three options; **pick option (b) for the algebra and option (a) for operations**. T1 amends to "byte-determinism for text/JSON; content-determinism via DacFx model-API equality for binary." The byte-canonicalization (Origin.xml timestamp pinning, model.xml checksum recomputation, zip-entry timestamp pinning) lives as a **post-pass inside the C# DacFx wrapper**, not inside the F# emitter. Rationale: the determinization touches zip internals and is fragile under DacFx version bumps; isolating it in C# keeps the F# emitter free of `System.IO.Packaging` surgery, and the post-pass is a clean unit to disable when DacFx eventually ships byte-stable output natively. The compare-by-SsKey canary layer never inspects bytes — it round-trips through the model API, so canary correctness is independent of the determinization pass.

### D.5. Test strategy per phase

Chapter 3 ratio target stays ~1:1.7 source:test.

- **3.1 SnapshotRowsets** — fixture/example tests dominant (rowset literals as F# records, mirroring session-18 minimal fixture). Cross-source parity tests (JSON ↔ Rowsets) are differential. One property test for SsKey-shape divergence handling.
- **3.2 Read-side adapter** — golden-file tests against pinned `.dacpac` fixtures; differential tests asserting `Extract → Catalog` round-trips equal to source Catalog modulo the documented divergences.
- **3.3 DacpacEmitter** — three test classes: (1) golden T-SQL output per Catalog shape; (2) DacFx round-trip property tests (`emit → load → enumerate → equal source`) — this is where property tests fit, sweeping permutation invariance and structural-commitment; (3) T11 commutativity tests asserting `RawTextEmitter` and `DacpacEmitter` agree on the SsKey-root-mention property over generated catalogs.
- **3.4 Canary** — integration tests via testcontainers; expensive, opt-in via `[<Trait("Category", "Integration")>]`. One round-trip integration test per Catalog shape class (single-table, multi-table-no-FK, FK, indexes, composite PK, cross-module). Property tests do not fit here — too slow.
- **3.5 RefactorLogEmitter** — property tests for UUIDv5 determinism (same V1 SSKey Guid → same V2 SsKey, regardless of run); golden-file tests for refactor-log XML.

Chapter 4:
- **4.1 Data-emission triumvirate** — golden-file tests per emitter; property tests for content-equality determinism (T1 binding for data emissions); EmissionPolicy DU exhaustiveness verified by F# match.
- **4.2 User FK reflow** — pure-pass property tests dominate (algebra: same Profile + same Policy → same reflow). Per-strategy golden examples.
- **4.3 Drift detection** — integration tests against drifted ephemeral DBs.
- **4.4 Diagnostics V2** — fixture tests for sink shapes; property tests for writer composition laws.

### D.6. Fallback plan for cutover

VISION names no fallback. Propose:

**V1-only path**: V1's existing extraction + topologically-sorted two-phase inserts + Azure DevOps PR pipeline runs the cutover. Trust mechanism is "implicit correctness verified by experience" (per VISION §"What V1 already does"). Loss: no canary; every emission is hand-verified; cross-version identity held by V1's existing SSKey discipline rather than V2's UUIDv5 + RefactorLog.

**V2-augmented path**: V1 still drives the cutover pipeline; V2 runs the canary as a verification-only sidecar — emit + deploy-ephemeral + read-back + compare-to-V1-output. If canary disagrees with V1's emission, the PR blocks. V2 owns no production write path; V2 owns the verification surface only. Lower risk than V2-as-driver; preserves V1's empirical trust while V2's algebra earns its keep on the verification axis.

**V2-driver path**: V2 emits dacpac + StaticSeeds + MigrationDependencies + Bootstrap; V1 retired from cutover. Highest payoff (full sovereignty per VISION §"What V2 ultimately is"); highest risk (V2's algebra unproven on production-scale 300-table workload).

**Decision criterion at T-30 days**: V2-driver requires (a) chapter 3 closed with canary green on full 300-table Catalog; (b) chapter 4.1 (data triumvirate) shipping; (c) chapter 4.2 (user FK reflow) shipping; (d) at least one full dry-run against UAT environment with cross-environment Profile/Policy pairs producing structurally-consistent artifacts. If any of (a)–(d) is yellow at T-30, drop to V2-augmented (canary-only). If V2-augmented's canary is unstable at T-15, drop to V1-only and ship V2 as post-cutover substrate. **Hard rule: never drop V1 from the path between T-30 and cutover-day**; V1 stays warm even on the V2-driver path until cutover+30 days.

### D.7. Three explicit non-goals

VISION's "informational widening" §95–106 contains GraphQL emitters, FakerEmitter, AI-agent substrate, recipes, and per-developer Docker compose. **Defer all four families through chapter 4**:

1. **GraphQL schema and resolver emitters — defer.** VISION names them as sibling Π's emerging without rebuilds; that claim depends on the algebraic core holding. Cutover does not exercise GraphQL; cutover exercises DACPAC + data emissions + canary. GraphQL pays no cutover dividend. Risk of premature scope: a GraphQL emitter with no consumer drives speculative IR refinements that contaminate the cutover-critical chorus. Re-open trigger: first non-cutover consumer demands a Catalog projection via GraphQL.

2. **FakerEmitter and synthetic-data quality scoring — defer.** Already deferred per `HANDOFF.md` "lower-priority, watch for accidental fires" — gates on third evidence type, which has not landed. Profile-shaped synthesis is post-cutover-substrate, not cutover machinery; production cutover uses real data with environment-specific Profile, not synthesized data. Re-open trigger: third evidence type lands, OR per-developer local Docker SQL Server demand surfaces in chapter 5+.

3. **AI-agent substrate (Playwright agents, code agents, domain copilots) and "recipes" emission (docker compose, provisioning scripts).** VISION §"informational widening" frames these as post-cutover trajectory; they explicitly are not load-bearing for the cutover. Building them inside chapters 3–4 would inflate the cutover-critical surface with capabilities whose consumers do not yet exist (per the IR-grows-under-evidence-not-speculation discipline). Re-open trigger: a real AI-agent consumer (operator-decided) demands a Catalog projection, OR a per-developer local environment becomes a chapter goal.

The defer-justification compounds: each non-goal is a sibling Π or substrate axis whose absence does not block the cutover. The cutover is the load test; everything not on its critical path waits until the load test is passed.

---

# Appendix E — Canary as Property-Test Surface

**Brief:** Design how to make the canary the *primary* verification vehicle by building it as a property-test surface (FsCheck + testcontainers), not as a handful of hand-written integration tests. Goal: heavy property coverage with tiny test code.

## Canary as property-test surface — design memo

### E.1. Generator design

Hierarchy: bottom-up, well-formedness baked into composition, **not** post-filtered. Filtering loses too many samples and breaks shrinking.

```fsharp
module CatalogGen
open FsCheck
open Projection.Core

// Leaves
let genSsKey  : Gen<SsKey> =
    Gen.elements ["k1";"k2";"k3";"k4";"k5"]   // small interned pool — collisions matter
    |> Gen.map (SsKey.original >> Result.value)
let genName   : Gen<Name> = Arb.generate<NonEmptyString> |> Gen.map (fun s -> Name.create s.Get |> Result.value)
let genPrim   = Gen.elements [Integer; Decimal; Text; Boolean; DateTime; Date; Guid; Binary]
let genAction = Gen.elements [NoAction; Cascade; SetNull; Restrict]

// Attribute: PK flag handled at Kind level so we can guarantee >= 1 PK
let genAttrRaw : Gen<Attribute> = gen { ... }

// Kind — enforces "at least one PK" by construction (DacFx requires it)
let genKind ssKey : Gen<Kind> = gen {
    let! attrCount = Gen.choose(1, 6)
    let! attrs     = Gen.listOfLength attrCount genAttrRaw
    let attrs'     = attrs |> List.mapi (fun i a -> { a with IsPrimaryKey = (i = 0) })
    let! idxs      = Gen.listOfLength <|| (Gen.choose(0,3), genIndexFor attrs')
    return { SsKey = ssKey; ...; Attributes = attrs'; Indexes = idxs; References = [] } }

// Module — fresh attribute SsKeys scoped per kind
let genModule moduleKey kindKeys : Gen<Module> = ...

// Catalog — two-phase: first build kinds with no FKs, then *thread*
// References as a topological wiring step. Optional cycles via a
// `genCycleSpec` knob so we can exercise CycleResolution too.
let genCatalog : Gen<Catalog> = gen {
    let! moduleCount = Gen.choose(1, 3)
    let! kindCounts  = Gen.listOfLength moduleCount (Gen.choose(1, 5))
    let kindKeys     = ...                         // fresh, unique per module
    let! bareModules = ...                         // kinds with no References yet
    let allKinds     = bareModules |> List.collect (fun m -> m.Kinds)
    let! refs        = wireReferences allKinds     // picks valid (sourceAttr, targetKind) pairs
    return Catalog.applyReferences refs { Modules = bareModules } }
```

`wireReferences` is the load-bearing step: it picks `(sourceKind, sourceAttr, targetKind)` from the *already-generated set*, so FK targets always exist by construction. Cross-module is just "pick targetKind from any module"; intra-module is the same with a constraint. No filtering needed.

Register one `Arbitrary<Catalog>` via `Arb.register<CatalogArbs>()` in a module-init or per-property `[<Properties>]` attribute. FsCheck's `Gen.listOfLength` is the workhorse; everything composes.

### E.2. Shrinking strategy

FsCheck's default record shrinker is useless here — it would shrink names character-by-character and produce nonsense. Define `Arb.fromGenShrink (genCatalog, shrinkCatalog)`:

```
shrinkCatalog c =
   seq {
     // Outermost first — biggest blast radius wins
     yield! dropOneModule c
     yield! dropOneKindFromAnyModule c
     yield! dropOneReferenceFromAnyKind c   // FKs before constraints
     yield! dropOneIndexFromAnyKind c       // indexes before columns
     yield! dropOneNonPkAttribute c         // never drop the PK
     yield! shrinkOnePrimitiveType c        // Decimal -> Integer -> Text
   }
```

Order is important: `dropModule >> dropKind >> dropReference >> dropIndex >> dropAttribute >> shrinkType`. FsCheck enumerates lazily; the first reproduction wins, and the cheapest reductions are first. Never shrink `SsKey` (identity is the spine; mutating it invalidates A4) and never shrink the PK flag below "exactly one PK" (DacFx invariant).

### E.3. Predicate library

Each predicate is a `Catalog -> bool` (or `Catalog -> Catalog -> bool`) that becomes a `[<Property>]` taking `Catalog` (or pairs).

| Predicate | Statement | Cost |
|---|---|---|
| `roundTripBySsKey` | `emit catalog \|> deploy db \|> read = catalog` modulo Π-erased axes (Origin, Modality) | High (deploy) |
| `idempotentRedeploy` | `deploy c db; let alters = (deployScript c db) in alters.IsEmpty` — covers CDC-safety | High |
| `rawDacpacAgree` (T11) | `RawTextEmitter` and `DacpacEmitter` mention the same `SsKey` set | **Pure**, no DB |
| `siblingChorusAgrees` (T11 generalized) | for every Π in `[raw; json; dacpac]`, projected SsKey-set matches `Catalog.allKinds \|> List.map .SsKey` | Pure |
| `renameSurvives` (A1) | for `c` and `c' = renameRandomAttribute c`, `deployIncremental c c' db` produces zero `DROP COLUMN` (only `ALTER`) | High |
| `t1ByteEqual` (T1, text/JSON) | `emit c = emit c` byte-for-byte over two runs | Pure |
| `t1ModelEqual` (T1, DACPAC) | `loadModel(emit c) ≅ loadModel(emit c)` structurally; bytes deferred per CHAPTER_3 §3 | Pure (DacFx model only) |
| `coproductPreservation` (T2) | `emit (M1 ⊕ M2) = emit M1 ⊕ emit M2` | Pure |
| `policyOrthogonal` (R4) | for `(p1, p2)` perturbed on one axis, the *Catalog reconstructed from deploy* is invariant modulo that axis | High |
| `siblingDeployRoundTrip` | `dacpac \|> deploy \|> extract = dacpac` modulo timestamps | High |
| `wellFormedDeploy` | `deploy c db` never throws; every FK resolves; every PK exists | High |
| `populationRoundTrip` | for kinds with `Static`, after `StaticSeedsEmitter` runs, deployed rows match `populations` | High |

The **pure** rows are the bulk — they exercise the algebra without touching Docker. The expensive rows run as tiered properties.

### E.4. Performance / ergonomics

Three tiers. Run all in CI; only tier 1 in pre-commit / IDE.

**Tier 1 — pure properties (no container).** Predicates that don't need a real server: T1/T11/T2/coproduct/sibling-agreement/wellformed-static-validation. Use `TSqlModel` in-memory only (`new TSqlModel(...) |> AddObjects |> validate`) — DacFx validates without deploying. ~100–500 cases per property, sub-second per property. This is where 80% of the coverage lives.

**Tier 2 — container-pooled deploy.** One `IClassFixture<SqlServerContainer>` xUnit fixture per test class; `deploy` accepts a *fresh `dbName` per case* (cheap CREATE DATABASE / DROP DATABASE inside one container, ~150 ms per cycle vs ~5 s per container). 20–50 cases per property. Use `EndOfMaxTest` shrinking budget — one shrink reproduction is enough.

**Tier 3 — full integration sample.** Hand-curated `[<Theory>][<MemberData>]` cases plus `[<Property>(MaxTest = 5)>]` over the same generator, gated `[<Trait("category","slow")>]`. Run nightly, not per PR.

Recommendation: build tier 1 first (it cashes out 90% of T11/T1/T2 coverage with zero containers), then tier 2 piggy-backing on the existing generator. Tier 3 is a smell-test, not the verification surface.

Container sharing pattern:
```fsharp
type SqlServerFixture() =
    let container = MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build()
    do container.StartAsync().Wait()
    member _.NewDatabase() = ... // returns connection string with fresh dbname
    interface IAsyncLifetime with ...
[<Collection("SqlServer")>]
type CanaryDeployProperties(fx: SqlServerFixture) = ...
```

`Testcontainers.MsSql` (3.x) is the right package; it exposes `MsSqlBuilder` directly.

### E.5. Sequencing impact on chapter 3

- **Reduces hand-written integration tests substantially.** §5 of CHAPTER_3 lists nine slices each requiring a curated fixture + assertion. With the property surface, slices 1–6 collapse to "generator widens to cover this shape, tier-2 property runs against it." The hand-written tests become **regression captures** of failed shrinks, not the verification surface itself. One curated example per slice is still useful as documentation; nine is overkill.
- **Pulls RefactorLogEmitter earlier.** A1 (`renameSurvives`) is the *only* property that requires RefactorLog to exist for incremental deploy not to DROP+CREATE — otherwise the predicate fails trivially on every rename. Once the property surface is built, the fastest way to make it green is to ship RefactorLogEmitter. Currently §5's deferred-slice ordering puts identity-axis work after byte-determinism; the property surface inverts this.
- **Forces an explicit T1-binary axiom amendment now, not later.** §3's "redefine T1 for binary as content-equality" stops being optional once `t1ModelEqual` is a property. The amendment lands before DacpacEmitter's first commit.
- **Exposes one new axiom candidate.** "Π-erased axes are explicitly enumerated" — `roundTripBySsKey` needs to know which Catalog fields the dacpac surface *cannot* preserve (Origin, Modality, ColumnRealization.IsNullable when relaxed by deploy, etc.). Today this is implicit in CHAPTER_3 §2's impedance map; the property forces it into a named function `Catalog.equalModuloDacpacErasure : Catalog -> Catalog -> bool` whose definition is itself part of the algebra. Suggest A35 or T12.
- **Makes `Projection.Pipeline` a *test-host project*, not a separate runtime.** Per CHAPTER_3 §1's F#-vs-C# guidance the DacFx wrapper is C#. The property surface invokes that wrapper directly from F# tests; the canary doesn't need its own orchestrator surface until a CLI consumer arrives. Defer `Projection.Pipeline` as a separate executable; ship it as a test-project assembly first.

### E.6. Concrete file plan

Under `sidecar/projection/tests/Projection.Tests/`, additive (existing 631 stay green):

- `CatalogGen.fs` — `Arbitrary<Catalog>` plus the wiring helpers and shrinker. Sibling to `Fixtures.fs`. ~150 lines.
- `CanaryPredicates.fs` — pure predicates: `siblingChorusAgrees`, `roundTripBySsKey` (pure variant), `coproductPreservation`, `t1ByteEqual`, `t1ModelEqual`. ~100 lines.
- `CanaryPureProperties.fs` — tier 1 properties; `[<Property>]` per axiom (`T1`, `T2`, `T11`, `A18 amended`). Names follow `` ``T11: sibling Pi's mention same SsKey set under generated Catalog`` ``. ~80 lines.

Under a new test project `tests/Projection.Tests.Canary/Projection.Tests.Canary.fsproj` (separate so `dotnet test --filter Category!=slow` skips Docker):

- `SqlServerFixture.fs` — testcontainers wrapper, `IAsyncLifetime`, `NewDatabase()`.
- `DacFxAdapter.fs` (or `.cs` per `DECISIONS 2026-05-09`) — `Catalog -> Result<byte[]>`, `bytes -> dbName -> Result<unit>` deploy, `dbName -> Result<Catalog>` extract.
- `CanaryIntegrationProperties.fs` — tier 2 properties consuming `SqlServerFixture` via `[<Collection>]`. `[<Property>(MaxTest = 30, EndSize = 8)>]` defaults.
- `CanaryRegressionTests.fs` — `[<Theory>][<MemberData>]` capturing every shrunk failure as a permanent example test. Append-only.

Naming convention preserved: `<Subject>Tests.fs` for example tests, `<Subject>Properties.fs` introduced as a paired suffix when the file is property-dominated. ScaffoldingTests precedent for keeping a single trivial test until real ones land — keep it.

Total new code estimate: ~600 lines test + ~200 lines wrapper. Replaces an estimated 2000+ lines of hand-curated DacpacEmitter integration tests across §5's nine slices. Test ratio improves, coverage of generated shape space goes up by orders of magnitude, and every axiom in `AXIOMS.md` that has a structural form gets a property test that exercises it on synthetic catalogs the maintainer didn't have to write.

---

# Appendix F — V2 Verifies V1 (Dogfood Plan)

**Brief:** Make V2 immediately useful by having it *verify V1's outputs* before V2 ships any new emitter. V2 becomes V1's canary right now; value before chapter 3 closes.

V1 emits per-table `.sql` files into `<outputDirectory>/Modules/<Module>/<Schema>.<Table>.sql` (plus a manifest.json), is driven by `BuildSsdtPipeline`/`FullExportPipeline`, exposes a CLI verb `BuildSsdt`/`FullExport`, has SqlClient already wired (`SqlClientOutsystemsMetadataReader`), and already runs testcontainers in-tree (`tests/Osm.TestSupport/SqlServerFixture.cs`, mssql 2022-CU15). The OSSYS source it reads is the `osm_model.json` shape produced by `SnapshotJsonBuilder`. V2 already has `Projection.Adapters.Osm/CatalogReader.fs` consuming exactly that JSON shape. **That is the seam.**

## V2 as V1's canary, before V2 emits anything

### F.1. Architecture

```
                     OSSYS DB (or fixture rowsets)
                         |
            +------------+-------------+
            |                          |
  V1 SnapshotJsonBuilder       (same JSON re-fed)
  (Osm.Pipeline.SqlExtraction)         |
            |                          v
            v               Projection.Adapters.Osm.CatalogReader
   V1 BuildSsdtPipeline  ->  Catalog_expected : Catalog
   (Osm.Pipeline.Orch)              (F#, shipped)
            |                          |
            v                          |
     <outDir>/Modules/                 |
       <Module>/<Schema>.<Tbl>.sql ----+
            |                          |
            v                          |
   *** Projection.Pipeline (NEW C#) ***|
   - spin up SqlServerFixture          |
   - apply each .sql in topo order  ---+
            |                          |
            v                          v
  Projection.Adapters.Sql.ReadSide (NEW F#)
       reads INFORMATION_SCHEMA + sys.* via SqlClient
            |
            v
   Catalog_observed : Catalog
            |
            v
   Comparator (F#)  : equalModulo Tolerance Catalog_expected Catalog_observed
            |
            v
   Diff -> Diagnostics, exit code, PR gate
```

Placement:
- **Projection.Pipeline** (C#, `sidecar/projection/src/Projection.Pipeline/`) — orchestrator. It is the *only* place that touches DacFx/testcontainers/file paths; the F# core stays pure. It depends on Osm.TestSupport's SqlServerFixture pattern (probably extract `MsSqlContainer` boot into `Projection.Pipeline.Ephemeral`).
- **Projection.Adapters.Sql.ReadSide** (F#, `Projection.Adapters.Sql.ReadSide/`) — `Task<Result<Catalog>> readCatalog(connStr, schemas)`. Returns the same `Catalog` the Core already consumes. Same shape as `CatalogReader.parse`; same return-type discipline (CLAUDE.md "Async/Task in adapters only").
- **Comparator** (F#, `Projection.Core/Verification/CatalogEquivalence.fs` or `Projection.Verification/`) — pure. No I/O. Reuses `SsKey` keying and structural equality.
- **Seam between V1 and V2**: V1's emitted `.sql` directory is the only artifact V2 cares about for the input side. For the oracle side, V1's `osm_model.json` (already on disk in V1's `--snapshot` output) feeds CatalogReader. Both already exist; nothing in V1 needs to change.

### F.2. Smallest read-side adapter

**Yes, skip DacFx Extract for now.** DacFx Extract → TSqlModel forces taking on Microsoft.SqlServer.DacFx, a TSqlModel→Catalog reverse projection (which is half of `DacpacEmitter` anyway), and binary-determinism plumbing — exactly what chapter 3.3 is for. The canary doesn't need any of it.

Minimum viable adapter: SqlClient against `INFORMATION_SCHEMA.TABLES`, `INFORMATION_SCHEMA.COLUMNS`, `sys.indexes` + `sys.index_columns`, `sys.foreign_keys` + `sys.foreign_key_columns`, `sys.check_constraints`, `sys.extended_properties`. Nine queries, one transaction, mapped into the same `Module/Kind/Attribute/Reference` records the Core already exposes. Roughly 300–500 lines of F#. Argument:

- The Catalog IR is already source-agnostic — it has no DacFx-shaped fields. Mapping `INFORMATION_SCHEMA` rows is straightforward.
- Two consumers from day one: canary read-back, and drift detection (VISION_REVIEW recommendation 2026-05-08). DacpacEmitter doesn't need this adapter at all.
- It's the same posture as `Projection.Adapters.Osm.CatalogReader` — boundary returns `Task<Result<Catalog>>`; Core stays sync and pure.

When DacpacEmitter lands, an alternative `DacFxReadSide` may earn its place; the minimal adapter is not thrown away — it's the cross-check oracle (see F.6).

### F.3. V1 hooks

**Best case applies.** V1 already produces the two artifacts the canary needs:
- The emitted SSDT directory (`SsdtEmitter.EmitAsync` → `<outDir>/Modules/...`, manifest at `<outDir>/manifest.json`). Path is operator-known via `BuildSsdtVerbOptions`.
- `osm_model.json` from `SnapshotJsonBuilder` (already a `--snapshot` output of `extract-model` / `build-ssdt`).

**No V1 code changes.** The Azure DevOps pipeline gains one step after `build-ssdt`: invoke `Projection.Pipeline verify --emitted <outDir> --snapshot <osm_model.json>`. If snapshot wasn't persisted in a given pipeline lane, add `--persist-snapshot` to V1 — that's the *only* V1 hook, and even that is optional (re-extract from DB in the canary if needed, since `SqlClientOutsystemsMetadataReader` is library-callable).

### F.4. Comparator

Tolerances V1's emission deliberately introduces and V2's expected Catalog will not carry verbatim:
- Index naming conventions (V1 prefixes/suffixes; observed names match V1's convention, expected names are SsKey-derived).
- CHECK constraints emitted by V1 templates that V2's Catalog doesn't model (Static-entity discriminators, system constraints).
- Extended-property metadata (descriptions, MS_Description) — V1 emits, V2 may not produce.
- Collation/ANSI defaults at column level when V2 models them implicitly.
- Computed columns and column ordinals (deploy order ≠ source order).
- Default-constraint *names* (V1 generates deterministic names; SQL Server may have its own).

Shape:

```fsharp
module CatalogEquivalence

type Tolerance = {
    IgnoreIndexNames        : bool        // compare by (columns, uniqueness, filter)
    IgnoreCheckConstraints  : Set<SsKey>  // V1-only checks scoped per-kind
    IgnoreExtendedProperties: bool
    IgnoreDefaultNames      : bool
    AttributeOrderInsensitive: bool
}

type Divergence =
    | KindMissing           of SsKey * Side
    | AttributeMismatch     of SsKey * AttributeDelta
    | ReferenceMissing      of SsKey * Side
    | IndexShapeMismatch    of SsKey * IndexDelta
    | UnexpectedExtra       of SsKey * Side * string

and Side = Expected | Observed

type Diff = { Divergences: Divergence list }   // empty = pass

val equalModulo : Tolerance -> Catalog -> Catalog -> Diff
```

The compare keys on `SsKey` root (T11 sibling-Π commutativity is the surface). Tolerances are *named* per V1 emission choice; the default `Tolerance` profile is calibrated empirically by running the canary against a real V1 output and pruning each false-positive class to a named tolerance with a citation. This converts "V1's quirks" into machine-readable record.

### F.5. Value timeline

**Step 1 — Today, with what's shipped.** V2 verifies *content equivalence* of V1's `osm_model.json` round-trip: read it via `CatalogReader`, project through `JsonEmitter`, diff against V1's persisted snapshot. If V1's JSON is malformed or carries identifiers V2 cannot key, the canary fails. This is JSON-level parity — no read-side, no DB. Catches OSSYS-adapter regressions and JSON-projection drift. Worth wiring this week.

**Step 2 — Read-side adapter lands (chapter 3.x).** The full canary above. V1 emits → ephemeral DB → `INFORMATION_SCHEMA` extract → `equalModulo`. This delivers structural verification of V1's SSDT output without V2 ever generating SQL. **This is the moment V2 earns its keep**: every PR runs this; a real V1 emitter bug surfaces as a `Divergence` rather than as production CDC noise.

**Step 3 — DacpacEmitter (chapter 3.3).** Replace V1's SSDT with V2's DACPAC; re-run the same canary. Now V2 verifies V2. Add the redeploy-zero-ALTER assertion (R2). The cutover-window governance rule (R6) flips from "V2 verifies V1" to "V2 emits, canary verifies V2 against itself plus against a third oracle (the OSSYS source)."

Each step ships independently. Step 2 is the inflection point.

### F.6. V1-bug attribution: triangulate

Single-oracle verification can't find V1 bugs. Use **three Catalogs**, two pairwise diffs:

- `C_ossys` — `Projection ∘ CatalogReader(osm_model.json)` — V2's pure expected.
- `C_v1` — `ReadSide(deploy(V1 SQL))` — what V1 actually built.
- `C_round` — `Projection ∘ ReadSide(deploy(V1 SQL))` (apply V2's passes to the observed catalog).

Three diffs, three different attributions:
- `C_ossys ≡ C_v1`: V1's output matches OSSYS truth. Pass.
- `C_ossys ≢ C_v1` and `C_round ≡ C_v1`: V1 emitted what it *intended* but V1's intent diverges from OSSYS. **V1 bug** (or V1 intentional divergence — surface it for ADMIRE classification).
- `C_ossys ≡ C_round` and `C_ossys ≢ C_v1`: round-trip through V2 reconciles, V1's emitted bytes drifted. **V1 emission bug** (formatting, ordering, encoding).
- All three disagree: read-side adapter or comparator tolerance is wrong. **V2 bug**, fix before promoting any verdict.

Frame the comparator output as `Divergence × AttributedSource`. The CLI surface in `Projection.Pipeline` should print the triangulation, not just a yes/no. This is the same shape ADMIRE.md uses; `extracted (with-divergence)` is its outcome.

### F.7. Chapter-3 re-sequencing

**Yes, pull the read-side adapter to 3.1.** Original Appendix D ordering:
1. SnapshotRowsets, 2. Read-side, 3. DacpacEmitter, 4. Canary, 5. RefactorLogEmitter.

New ordering, justified by "V2-augmented immediately":
1. **Read-side adapter** (`Projection.Adapters.Sql.ReadSide/`) + minimal `Projection.Pipeline` shell + `CatalogEquivalence` comparator + triangulation. This is V2-augmented mode shipping today against V1.
2. SnapshotRowsets — still gates A1 for renames; still chapter 3.
3. DacpacEmitter — V2 now becomes its own input to the same canary, redeploy-zero-ALTER added.
4. RefactorLogEmitter.
5. Canary closure (mostly already built by step 1; this becomes the formal redeploy-assertion + multi-environment property test).

The reasoning: under the original ordering, the read-side existed only to close the loop on V2 emission, so it had to wait for an emitter. Under the V2-as-canary frame, the read-side has a consumer (V1 itself) the moment it ships. Cutover-fallback ladder also benefits — V2-augmented becomes a real, exercised path well before T-30, not a fallback drawn on paper.

DECISIONS entry to write: "Read-side adapter promoted to chapter 3.1 — V2-augmented mode is the immediate-value vehicle; supersedes Appendix D §5 sequencing." Re-open trigger: read-side adapter cannot be built without DacpacEmitter (it can — `INFORMATION_SCHEMA` is sufficient).

---

# Appendix G — Radical Scope Cut

**Brief:** For each "informational widening" item in VISION.md, give a defer-vs-keep verdict with hard triggers, focused on minimizing V2's surface so cutover-critical work ships fast.

| # | Item | Cost | Cutover value | Post-cutover value | Algebraic distance | Verdict | Trigger |
|---|---|---|---|---|---|---|---|
| 1 | V2 outlives OutSystems / Catalog as platform-survival | small (rhetoric only) | none | small (only if migration off OutSystems is real) | trivial (Catalog already source-agnostic; V1↔V2 vocab table proves it) | CUT | — (already implicit in source-agnostic naming convention; no extra surface earns it) |
| 2 | Profile as longitudinal evidence (time/env/population) | medium (Profile-diff IR + storage) | none | medium | expensive (needs temporal/identity over Profile snapshots, env labelling, comparison algebra; not a sibling Π) | DEFER-WITH-TRIGGER | First time a real operator question requires comparing two persisted Profiles (e.g., "did P95 of X drift between QA and prod?") and DistributionsEmitter+grep can't answer it. |
| 3 | Six-dimension synthetic data quality scoring | large (six undefined metrics; research-grade) | none | small (Faker itself is post-cutover) | expensive (six axes have no existing algebra; not a sibling Π) | CUT | — (six adjectives with zero defined metrics; if Faker ever lands, ship one dimension on demand per Appendix C §3) |
| 4 | AI-agent substrate (Playwright/test/code/copilot) | large (per-agent integrations) | none | small-to-medium (speculative) | mostly trivial *as outputs* (Catalog-as-text already exists in RawText/Json), but "substrate" framing implies harness work | CUT framing; KEEP only the byproduct fact that Json/RawText emitters already serve agents | — (no new chapter; document the corollary in one sentence if at all) |
| 5 | Recipes-as-Terraform (compose, provisioning, Playwright) | small for canary's compose; large for the rest | small (canary compose is byproduct) | small | trivial for canary's compose; expensive for full Terraform-class scope | DEFER-WITH-TRIGGER (scoped to canary's compose only) | A second consumer asks for the canary's compose file outside the canary loop (e.g., per-developer SQL Server ask). |
| 6 | Per-developer Docker SQL Server with Profile-shaped synthetic data | large (FakerEmitter + provisioning + per-dev workflow) | none | medium | expensive (depends on Faker, which is itself deferred) | DEFER-WITH-TRIGGER | A developer files an environment-stand-up request that the canary's compose file can't already answer, AND Faker has shipped against a real consumer. |
| 7 | GraphQL schema + resolver emitters | small for schema (sibling Π); medium for resolvers | none | small-to-medium | schema = trivial sibling Π (`Π : Catalog → graphql.sdl text`); resolvers = expensive (runtime, not text) | DEFER-WITH-TRIGGER (schema only; cut resolvers) | A real consumer needs to query the Catalog from outside the F# core (e.g., a tool authoring against the domain). At that point, schema is half a session; resolvers stay deferred. |
| 8 | Drift detection | small (read-side adapter pointed at four DBs, scheduled job) | medium-to-large (Appendix C: cutover safety net, currently underweighted) | large | trivial (read-side adapter is already a canary deliverable; just point it at four DBs) | KEEP-IN-VISION (and *promote* from post-cutover trajectory to cutover-critical, per VISION_REVIEW) | — |
| 9 | CI/CD substrate (canary on every PR, refreshed Playwright plans) | medium for PR-canary; large for Playwright refresh | small (PR canary is the canary loop with a different trigger) | medium | trivial for PR-canary (canary in a workflow file); expensive for Playwright plan refresh | DEFER-WITH-TRIGGER (PR-canary only; cut Playwright refresh) | First post-cutover schema-evolution PR where operator wants pre-merge canary signal. |
| 10 | Personal tooling (V2 packageable per module) | medium (packaging story) | none | small | expensive (host-shell + module-scoped Catalog filtering) | DEFER-WITH-TRIGGER | Maintainer's "flow metrics from code review app" use case becomes a real ask, not a hypothetical. |
| 11 | V1 sunset | small to write the rule; large to execute | none | medium | trivial as policy (an ADMIRE table state); the *execution* is gated on canary track record | KEEP-IN-VISION (one sentence: "ADMIRE.extracted requires a green canary; sunset deferred until all four envs run on V2 emissions for one schema-evolution cycle" — already in VISION_REVIEW Appendix C §7) | — |
| 12 | Open-source / community contribution | medium-to-large (license, governance, sanitization) | none | none | trivial-as-aspiration; expensive-as-action | CUT | — (the "optional; the option emerges" hedge already concedes it doesn't belong) |

**G.7. Platform-survival framing.** Scope inflation. The Catalog being source-agnostic is already structurally true — `Kind`/`Module`/`Catalog` naming and the V1↔V2 vocabulary table at `sidecar/projection/README.md` lines 218–227 prove it. "V2 outlives OutSystems" adds no surface area; it just relabels existing discipline. Migrating off OutSystems would require a *new adapter* (the same shape as `Projection.Adapters.Osm`) and nothing else V2 doesn't already plan to ship. The framing is rhetorical, not load-bearing. CUT the section; the source-agnostic-naming convention in README.md already carries the substance.

**G.8. AI-agent substrate.** All post-cutover trajectory, *except* the trivial fact that JsonEmitter and RawTextEmitter already produce agent-consumable Catalog projections. There is no cheap pre-cutover piece that helps the cutover — agents tightening rules requires Profile + Policy literacy that no current LLM tool consumes. Drop the section entirely; if it earns a sentence anywhere, it's "Json/RawText emitters happen to be agent-legible," recorded as a corollary in DECISIONS.md, not a vision pillar.

**G.9. Recipes framing.** The canary's testcontainers compose file is a credible byproduct, and it gives maybe 30%, not 80%, of "recipes." It's a single SQL Server stand-up scoped to the canary's needs (version pinning, ephemeral, no seed data). The other 70% — Playwright invocations, provisioning scripts, Profile-shaped synthetic data tuning — depends on Faker (deferred) and Playwright integration (no forcing function). Minimum scope worth keeping: *the canary's compose file is documented as a reusable artifact* — one sentence, no chapter. Everything else cuts.

**G.10. What to actually CUT (not just defer) from VISION.md.**

- **"V2 outlives OutSystems" / Catalog as platform-survival** (item 1). The Catalog's source-agnosticism is already structural in the V1↔V2 vocabulary mapping. Re-asserting it as platform-survival inflates rhetoric without adding obligation. Source-agnostic naming earns the property; the section earns nothing.
- **Six-dimension quality scoring** (item 3). Six undefined adjectives. Per Appendix C §4, this is the single biggest scope risk and "research-grade" — it has no metric definitions, no consumer, and no cutover dependency. Faker itself can stay as a post-cutover possibility; the *scoring* leaves the document.
- **AI-agent substrate as a section** (item 4). No cheap piece helps the cutover; the rest is post-cutover speculation. The Catalog being agent-legible is a corollary, not a pillar. One sentence in DECISIONS.md if at all.
- **Open-source / community contribution** (item 12). The vision already hedges with "Possibly" and "the option emerges from the work whether or not it gets exercised" — that's the document conceding the item doesn't belong. Cut and revisit only if a maintainer files an explicit OSS proposal with license/governance/sanitization commitments.
- **Playwright-plan refresh under CI/CD** (sub-item of 9). No forcing function; depends on AI-agent substrate that's also being cut.
- **Per-developer Docker SQL Server with Profile-shaped synthetic data** (item 6) — *consider cutting*; transitively depends on Faker, which is deferred. Defer-with-trigger is defensible, but a cleaner VISION.md would just CUT and let it re-enter via DECISIONS.md if a real developer asks.

**Net effect on VISION.md.** §"The informational widening" shrinks from five paragraphs to two: drift detection (promoted to cutover-critical per Appendix C §5), and a single-sentence corollary that Json/RawText emitters happen to be agent-legible. §"The post-cutover trajectory" shrinks from eight bullets to four: drift detection, schema evolution, GraphQL schema (only, no resolvers), V1 sunset rule. The vision goes from aspirational manifesto to load-bearing spine — which is what `VISION_REVIEW.md` §"Closing assessment" already names as the goal.

---

# Appendix H — Type-System Refactor

**Brief:** Audit current F# emitter signatures and identity-construction patterns; propose a concrete type refactor that turns T11 (sibling-Π commutativity) and A1 (identity-survives-rename) into structural properties enforced by the type system, not code-style discipline.

## Emitter & Identity Refactor Audit

### H.1. Current emitter signatures

All three are `Catalog -> string` (or `Catalog -> Profile -> string`), no `Result`, no `Map`:

- `RawTextEmitter.emit : Catalog -> string` — `RawTextEmitter.fs:183`. Iterates `catalog.Modules` into a single `StringBuilder`.
- `JsonEmitter.emit : Catalog -> string` — `JsonEmitter.fs:140`. Single `Utf8JsonWriter` document.
- `DistributionsEmitter.emit : Catalog -> Profile -> string` — `DistributionsEmitter.fs:201`. Wide signature; a curried `emitFromInput : ProjectionInput -> string` at `:227`.

**T11 enforcement today is discipline.** The actual property tests are substring searches: `JsonEmitterTests.fs:96-105` does `for k in Catalog.allKinds enriched do Assert.Contains(root, ssdt); Assert.Contains(root, json)`. `RichProfilingEndToEndTests.fs:280` does the same across three Π's. There is nothing in the type system that would prevent an emitter from dropping a kind; only the test's `Assert.Contains` catches it, and only if the test's enriched fixture contains every shape in production.

The `DistributionsEmitter` doc comment at `DistributionsEmitter.fs:26-32` claims T11 commutativity as a property; the implementation enforces it by `for m in catalog.Modules` discipline.

### H.2. Current SsKey construction

`Identity.fs:16-18`:
```fsharp
type SsKey =
    | Original of string
    | Derived of original: SsKey * reason: string
```

Smart constructors at `Identity.fs:32` (`SsKey.original`) and `:38` (`SsKey.derived`) return `Result<SsKey>`, rejecting blank input. **No type-level distinction exists between an *OutSystems-rowset SSKey GUID* and a *JSON-path-synthesized SsKey*.** Both are `Original of string`. The bound documented in `AXIOMS.md:47-72` (the JSON adapter strips SSKey columns and synthesizes from name fields) is a runtime fact about the adapter's call site, invisible to the compiler. A property test on a renamed kind that synthesized its key from `Name` cannot be distinguished from one that received an original GUID — both look like `Original "..."`.

### H.3. Current rename handling

`NamingMorphism.fs` is the only rename code path. It threads a `Name -> Name` morphism across the catalog (`:30`) and emits `Renamed` lineage events (`:32-36`); critically, `SsKey` fields are not touched (the doc comment at `:7-10` claims this; the implementation at `:46-78` only assigns to `Name`, never `SsKey`). The single rename test, `CatalogTests.fs:104` "A4: Catalog.tryFindKind survives a rename", is a one-shot example — no FsCheck property over arbitrary morphisms.

**There is no `RenameContext`, `RefactorLog`, `CatalogDiff`, or `MapEmitter` type in the codebase** (verified by grep over `src/`). VISION.md:58 and VISION_REVIEW.md:51 describe `RefactorLogEmitter` + UUIDv5 as forthcoming; nothing has shipped. A1's bound through `SnapshotJson` (`CatalogReader.fs:75`) is precisely that synthesized-from-name keys *don't* survive renames; the bound resolves only when `SnapshotRowsets` lands.

### H.4. T11 as a structural type

```fsharp
namespace Projection.Core

type EmitError =
    | KindNotProduced of SsKey
    | RenderFailed of SsKey * reason: string

/// Slice keyed by SsKey root. Smart constructor below enforces
/// "every Catalog kind is present" — emitters cannot return this
/// type without populating the keyset.
type ArtifactByKind<'element> = private ArtifactByKind of Map<SsKey, 'element>

[<RequireQualifiedAccess>]
module ArtifactByKind =
    let create (catalog: Catalog) (slices: Map<SsKey, 'a>) : Result<ArtifactByKind<'a>, EmitError> =
        let required = Catalog.allKinds catalog |> Seq.map (fun k -> k.SsKey) |> Set.ofSeq
        let provided = slices |> Map.toSeq |> Seq.map fst |> Set.ofSeq
        match Set.toList (Set.difference required provided) with
        | []      -> Ok (ArtifactByKind slices)
        | missing -> Error (KindNotProduced (List.head missing))
    let toMap (ArtifactByKind m) = m

type Emitter<'element> = Catalog -> Result<ArtifactByKind<'element>, EmitError>
```

Concrete signatures become:
```fsharp
let rawTextEmitter   : Emitter<string>           = ...
let jsonEmitter      : Emitter<JsonElement>      = ...
let dacpacEmitter    : Emitter<TSqlObjectScript> = ...
let distributionsEmitter : Catalog -> Profile -> Result<ArtifactByKind<DistributionSlice>, EmitError> = ...
```

T11 becomes a type theorem, not an `Assert.Contains`. The substring tests in `JsonEmitterTests.fs:96` collapse to `Map.dom == Map.dom`, which is true by construction.

**Composition layer** (one per target shape):
```fsharp
[<RequireQualifiedAccess>]
module Render =
    let concatSql (order: SsKey list) (a: ArtifactByKind<string>) : string =
        let m = ArtifactByKind.toMap a
        order |> List.choose (fun k -> Map.tryFind k m) |> String.concat "\n\n"

    let toJsonDocument (a: ArtifactByKind<JsonElement>) : JsonDocument = ...
    let toDacpac (order: SsKey list) (a: ArtifactByKind<TSqlObjectScript>) : DacPackage = ...
```

Per-kind sliceability is independently useful: it enables incremental emit (rebuild only changed kinds), drift detection (pointwise diff of two `ArtifactByKind` maps), partial remediation (R5 in VISION_REVIEW), and structural snapshot caching keyed by `SsKey × content-hash`.

### H.5. A1 as a structural type

```fsharp
type SsKey =
    | OssysOriginal of System.Guid              // from rowsets adapter; A1 holds
    | Synthesized   of source: string * basis: string  // JSON path; A1 bounded
    | DerivedFrom   of parent: SsKey * reason: string  // pass-introduced
```

Now the JSON-path bound at `AXIOMS.md:47-72` is **type-visible**: a property test claiming `rename(n).key = n.key` accepts only `OssysOriginal` (or `DerivedFrom` rooted in one); on `Synthesized`, the same property is the *negation* — it documents the bound. FsCheck generators stratify naturally:

```fsharp
let ``A1: rename preserves OssysOriginal SsKey`` (k: Kind) =
    match k.SsKey with
    | OssysOriginal _ -> rename k = k.SsKey      // property holds
    | Synthesized _   -> ()                       // bounded; not asserted
    | DerivedFrom _   -> rename k = k.SsKey      // inherits root
```

`Identity.fs:32`'s `SsKey.original : string -> Result<SsKey>` becomes two constructors with distinct call sites — the rowsets adapter (`Projection.Adapters.Osm.CatalogReader.parse` when `SnapshotRowsets` lands) calls `SsKey.ossysOriginal : Guid -> SsKey` (no Result; Guid construction is total); the JSON adapter calls `SsKey.synthesized : source:string -> basis:string -> Result<SsKey>` (Result; basis must be non-blank).

**V1→V2 UUIDv5 mapping.** Add a fourth constructor scoped to cross-version threading:
```fsharp
| V1Mapped of v1Sskey: System.Guid * v2Namespace: System.Guid
```
with a smart constructor `SsKey.fromV1 : v1: Guid -> v2Namespace: Guid -> SsKey` that produces a deterministic UUIDv5 *and tags the value as cross-version*. Pattern-matching consumers can distinguish "this identity originated in V1's space" from "this identity is V2-native" — load-bearing for the `RefactorLogEmitter` audit trail and for the cutover risk surfaced in Appendix B §B.3.

### H.6. Refactor-log emission as Π over a diff

```fsharp
type RenameRecord = { OldName: Name; NewName: Name; PassVersion: int }

type CatalogDiff = {
    Source   : Catalog            // the diff is itself Catalog-typed
    Target   : Catalog
    Renamed  : Map<SsKey, RenameRecord>
    Added    : Set<SsKey>
    Removed  : Set<SsKey>
}

[<RequireQualifiedAccess>]
module CatalogDiff =
    /// Total: every SsKey in source ∪ target is in exactly one of
    /// Renamed / Added / Removed / Unchanged. Smart-constructor enforces.
    let between (a: Catalog) (b: Catalog) : Result<CatalogDiff, EmitError> = ...

type RefactorLogEmitter = CatalogDiff -> Result<ArtifactByKind<RefactorLogEntry>, EmitError>
```

The diff is a **Catalog-typed value**, so `ArtifactByKind` over it satisfies T11 by the same theorem — every `SsKey` in the diff's scope appears in the output. `RefactorLogEmitter` stops being "the special emitter that takes a separate rename log"; it's just another Π whose evidence happens to be a diff. This dissolves the asymmetry that VISION.md:58 currently has to narrate prose-style.

### H.7. Migration strategy

**Incremental, behind a discriminator.** The current `Catalog -> string` shape is too pervasive to flip atomically — `JsonEmitterTests.fs`, `RawTextEmitterTests.fs`, `DistributionsEmitterTests.fs`, `RichProfilingEndToEndTests.fs:280-433`, and `EndToEndDifferentialTests.fs` all consume `string`.

Sequence:

1. **Land `ArtifactByKind<'a>` and `Emitter<'a>` types in `Projection.Core`** alongside (not replacing) existing emitters. No consumer change.
2. **Add `RawTextEmitter.emitSlices : Emitter<string>`** next to `emit : Catalog -> string`. The existing `emit` becomes `Render.concatSql topoOrder (emitSlices catalog |> Result.value)`. One new property test: `T11 by type` (`Set.equal (Map.keys (ArtifactByKind.toMap result)) (Set.ofSeq (Catalog.allKinds c |> Seq.map _.SsKey))`).
3. **Same for JSON, then Distributions** — one emitter per chapter slice.
4. **Once all three carry `emitSlices`**, deprecate the old `emit`. Substring `Assert.Contains` tests at `JsonEmitterTests.fs:96-105` and `RichProfilingEndToEndTests.fs:280` retire (the type now proves what they assert).
5. **`SsKey` DU split is a separate refactor.** Big-bang within `Projection.Core` (closed-DU expansion empirical-test discipline at CLAUDE.md — exhaustiveness errors must light up only at match sites). The two adapters (`Static.fs` calling `SsKey.original`, `CatalogReader.fs` likewise) update; the rest of the codebase pattern-matches on the new variants only at the rename property tests and at `RefactorLogEmitter`.
6. **`CatalogDiff` + `RefactorLogEmitter` is the chapter-3-tail slice** that Appendix D names; it lands after `SnapshotRowsets` because the diff needs `OssysOriginal` SsKeys to be honest about A1.

Not one chapter. Three: emitter shape (chapter 4); `SsKey` DU split (chapter 5, gated on `SnapshotRowsets`); diff + `RefactorLogEmitter` (chapter 5 tail).

### H.8. What this enables

- **T11 property test trivializes** to `Set.equal (Map.keys a) (Map.keys b)` across two `ArtifactByKind<_>` results — a one-line theorem, not `for k in allKinds; Assert.Contains`.
- **A1 rename property is type-stratified** — FsCheck generators over `OssysOriginal` assert preservation; over `Synthesized` document the bound. The session-23 prose at `AXIOMS.md:47-72` becomes a type-level fact.
- **Drift detection becomes pointwise.** `ArtifactByKind.compareWith eq deployed target` returns `Map<SsKey, DriftKind>` — sliceable and routable to specific kinds. The current `string`-shaped emitters force whole-document diffing.
- **Partial-state remediation (R5)** becomes `dacpacEmitter (CatalogDiff.between deployed target) |> Render.toDacpac topoOrder` — exactly the per-SsKey shape the production-trustworthy story needs.
- **GraphQL emitter as `Emitter<GraphqlTypeDef>`** drops in trivially T11-compliant — no test rewrite, no commutativity audit; the type carries the obligation.
- **Cutover risk in Appendix B (V1 vs. V2 disagreement on shared SsKeys)** becomes detectable structurally: `Set.equal (v1ArtifactByKind |> Map.keys) (v2ArtifactByKind |> Map.keys)` with per-key value comparison — split-brain shows up as a typed `Map<SsKey, (V1Output * V2Output)>` not as a string-diff.
- **Lineage events on emit failure are pre-routed.** `EmitError.KindNotProduced sskey` carries the identity directly; current emitters can only fail by exception or silently truncate.
- **Snapshot caching** keyed by `(SsKey, contentHash element)` becomes natural; the current monolithic `string` output is opaque to per-kind cache lookup.
