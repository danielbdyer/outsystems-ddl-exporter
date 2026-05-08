# Appendix A — Skeptical Critique of VISION.md

**Date:** 2026-05-08
**Reviewing:** VISION.md @ commit `2fb51ef`
**Brief:** Pressure-test the strategic vision document. Find the weakest claims, hidden assumptions, and unfalsifiable framing. Goal is to find holes a believer would miss.
**Synthesis location:** `VISION_REVIEW.md`

---

## Pressure-Test of VISION.md

### 1. Unfalsifiable claims — the dogma layer

The document repeatedly leans on rhetoric that cannot be observed to fail:

- **"The algebra is not aesthetic; it is the structural condition for the cutover being trustworthy."** This is unfalsifiable. If V1 (no algebra) ships the cutover successfully, was it not "trustworthy"? The sentence can never be wrong.
- **"Lineage is constitutive, not decorative."** "Constitutive" is doing infinite work here. The testable claim would be "every emitted artifact carries a traceable decision chain a human can audit in N minutes." That's missing.
- **"Auditability is type-system-encoded."** A `Lineage<'a>` wrapper is a writer monad. It encodes that *something* was written, not that what was written is auditable. There is no claim that lineage entries are *readable*, *queryable*, or *complete* relative to a defined audit standard.
- **"V2 is the team's sovereignty over its own metadata."** Unfalsifiable identity claim. What observation would refute it?

### 2. Forcing-function reality check — the cutover is V1's problem

Per VISION.md line 19, V1 already does: "extraction; specializations; opinionated formatting; topologically-sorted two-phase inserts; user FK reflow between environments; profile interventions on the data; standalone domain record injection for legacy migration teams; environment promotion via Azure DevOps PRs."

That is *the cutover*. Every load-bearing capability the cutover requires is named as already-shipping in V1 (78K LOC, vs. V2's 7K LOC of pure core with adapters and emitters still in flight). The vision concedes this, then pivots: "V1's correctness is implicit... V2 makes the correctness explicit, verifiable."

But the canary loop — the one mechanism that would make correctness "verifiable" — is unbuilt. README line 71-76 lists `Projection.Pipeline` (canary orchestration) and `Projection.Adapters.Sql.ReadSide` as **slots reserved for future sessions, not yet built**. The forcing function is stated to require something the system does not yet contain.

If the cutover is imminent, V1 ships it. V2's "uniquely adds" reduces to: a future canary loop, a future DacpacEmitter, a future RefactorLogEmitter. None gated on calendar; all gated on session sequencing.

### 3. Claim-to-evidence gaps — the promised-vs-built ledger

Built (per README): RawTextEmitter, JsonEmitter, DistributionsEmitter, OSSYS CatalogReader (in flight), three SQL adapter files. 631 tests passing.

Promised in VISION.md but **not built**: DacpacEmitter, RefactorLogEmitter, StaticSeedsEmitter, MigrationDependenciesEmitter, BootstrapEmitter, FakerEmitter, GraphQL schema/resolver emitters, Post-IS external entity declaration emitter, the canary loop, SnapshotRowsets, six-dimension synthetic-data quality scoring ("relational, commutative, descriptive, heuristic, correlative, entropic" — six adjectives, zero defined metrics), drift detection, Playwright agent integration, Terraform-equivalent recipe emission.

That is the entire load-bearing surface. The shipped emitters are the easy ones (text and JSON serialization of an in-memory DU). DacFx, refactor-log binary format, FK-aware data emission, and ephemeral SQL Server orchestration are the hard ones, and zero of them exist.

### 4. Algebraic inflation — load-bearing or decorative?

Genuinely load-bearing: closed DUs + total pattern matching, smart constructors returning `Result`, `SsKey` as a distinct type. These produce real compile-time errors.

Decorative: "Π ∘ E", "T11 sibling-Π commutativity," "writer-monad lineage carriage," "structural-commitment-via-construction-validation." Drop the symbols and you have: "separate enrichment from rendering, run the same input through multiple emitters, log decisions as you make them, validate in the constructor." Every competent F# engineer does this. The notation does not generate the discipline; the discipline generates good code, and the notation labels it. The vision conflates the two.

T11 ("every Π's output mentions every Catalog kind by SsKey root") is not a *theorem* — it's a code style guideline for emitters. Calling it a theorem inflates its status.

### 5. Scope creep — yes, the widening section is scope explosion

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

### 6. The fallback question, unanswered

If V2 isn't ready, the team falls back to V1, which already does the work. The vision never names a failure mode where V1 fails *and* V2 succeeds. "Implicit correctness can't carry the stakes" is asserted, not demonstrated — V1 has done four-environment promotion already; what specific cutover sub-task breaks under V1 that V2 fixes?

### 7. Other smells

- **"V2 admires V1; V2 extracts from V1 under empirical pressure"** — anthropomorphizing the codebase. ADMIRE.md as ritual is fine; "V2 admires V1" is not a strategic statement.
- **"The cutover is the load test"** — a load test you cannot afford to fail is not a test, it is the production event.
- **"Hold the spine"** — closing rhetoric replaces a metrics-of-success section. There is no acceptance criterion for "V2 earned its existence."
- **A1's bound is a real footnote-to-watch.** AXIOMS.md lines 46-72 admit that identity-survives-rename does not hold through the current input path. VISION.md elevates A1 to "the temporal axiom" without acknowledging the bound. The vision speaks past its own algebra's caveats.

### Strengths (the one paragraph)

The V1↔V2 separation discipline is genuinely sound: V2 is additive, cherry-pick safe, trunk unaffected. The closed-DU + total-match style and structural-commitment-via-construction validation are real engineering discipline producing real compile-time guarantees. The sibling-emitter cross-validation idea (RawText vs. Dacpac disagreement = bug) is a legitimate testing strategy when both sides exist. Documenting A1's bound openly (AXIOMS.md) shows the project can be honest about its limits — which makes the vision's overreach more conspicuous, not less.

### Bottom line

VISION.md reads as a manifesto written for an audience of believers, dressed in algebra to look load-bearing. The cutover is V1's job; V2 is a parallel rebuild whose unique contributions (canary, DacpacEmitter, RefactorLog) are unbuilt; the "informational widening" is a roadmap of nine products masquerading as a strategy. The right document is half this length, drops "sovereignty" and "constitutive," and names two acceptance criteria: *the canary loop catches a real artifact bug before publication*, and *V1 is sunset on a date*.

Files referenced: `sidecar/projection/VISION.md`, `sidecar/projection/README.md`, `sidecar/projection/AXIOMS.md`.
