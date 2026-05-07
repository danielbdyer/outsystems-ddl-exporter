# Chapter 2 close — in-flight scaffold

This document is the chapter-2 close synthesis, accumulating findings
across the chapter as it progresses. Chapter 2 opened at session 13
(post-chapter-1 doc-hygiene) and continues in flight.

**Status:** scaffold. Sessions 13–22+ have produced substantive work
that will summarize here when chapter 2 closes. This file is a
**working surface for accumulating findings during the chapter**
(per the session-23 documentation hygiene; the scaffold-early
disposition lets MINOR/OPEN audit findings land before the formal
chapter close).

The chapter-close ritual (`DECISIONS 2026-05-14`) names the seven
load-bearing items every chapter close must execute. This document
will cover them when chapter 2 closes; until then, it accumulates
material the ritual will need.

## Chapter 2 arc (sessions 13–current)

The arc has had three substantive sub-arcs:

  - **Sessions 13–16: Diagnostics writer chapter.** Codified-and-
    extracted across UniqueIndex (session 14), Nullability (session
    15), ForeignKey (session 16). Reached its stability mark at
    session 16 via the heterogeneous third test.
  - **Session 17 onwards: OSSYS adapter implementation chapter.**
    Strategic frame committed at session 17; substantive slices
    across sessions 18–22, 24 (minimal, reference-bearing, external-
    entity, mixed-active, index-bearing, static-entity). **Twenty-
    five translation rules** in the running list across six slices.
    Status: `extracting (in flight, 6 slices)` after session 24.
  - **Sessions 23–25: chapter close runway.** Documentation
    hygiene at session 23 (closed all six session-22 CRITICAL
    findings); subagent #2 dispatch + integration at sessions 23–
    24; static-entity slice + DacFx cash-out + chapter-mid-audit
    refinement at session 24; cross-module FK deferred to fresh
    context per the chapter-3 handoff. Chapter close at session 25
    (planned).

## Findings accumulating for the formal close

### From session 22's cross-document audit (subagent #1)

  - **6 CRITICAL findings** — addressed in session 23 hygiene per the
    instruction. README rewrite, OSSYS ADMIRE status update,
    CHAPTER_CLOSE rename, A1 forwarding pointer (session 23
    commits 1, 3, 4, 5).
  - **21 MINOR findings** — roll into this document when chapter
    closes. Categories: stale test totals, partial framework
    references, intro-paragraph dating drift, "framework was
    designed for chapters that complete in single arcs" gaps now
    closed.
  - **7 OPEN questions** — landing here as accumulating context
    until chapter close decides them.

### From session 23's chapter-mid-audit (subagent #2 — Active deferrals + operating-disciplines audit)

  - **1 CRITICAL finding** — DacFx / DacpacEmitter trigger had
    fired silently across sessions 18–22; same shape as the
    transform-registry miss. **Resolved in session 24 commit 1**
    (re-defer with tighter trigger condition: real Catalog
    flowing end-to-end through pipeline exercising T11 sibling-Π
    commutativity; canary chapter is the natural locus).
  - **9 MINOR findings** — split into two clusters; both roll to
    chapter close synthesis.
    - *Index-snapshot drift cluster (4 items):* `SnapshotRowsets`
      and `LiveOssysConnection` variants missing from index;
      strategy registry count "6 strategy modules" stale (actual: 5);
      `RequireQualifiedAccess` retrofit row's "no modification since
      session 8" partially stale (FK keep-reason got `MissingTarget`
      at session 19); status column header still dated to session 13.
    - *Operating-disciplines table propagation gaps cluster (5 items):*
      Admire-mode spectrum (V1-migration / V2-growth / hybrid)
      codified at `DECISIONS.md:1793` but missing from CLAUDE.md
      table; writer codification stability mark codified at
      `DECISIONS.md:3786` but missing; `opportunityEntry`
      extraction-defer at N=3-distinct-shapes codified at
      `DECISIONS.md:3896` but missing; "Document the false starts"
      pointer is to commit messages rather than a DECISIONS entry;
      "Active deferrals re-checked" pointer is to a section, not
      the codifying entry; ambiguous `DECISIONS 2026-05-11` /
      `DECISIONS 2026-05-09` pointers (multiple entries per date).
  - **3 OPEN questions** — accumulating as context for close
    discussion (see "Open questions accumulating (from session 23
    audit)" below).
  - **Cross-cutting observation** — the propagation gaps are
    systematic: session-14 codifications never propagated into the
    disciplines table; OSSYS-adapter-shipped never propagated into
    a DacFx-trigger evaluation. Session 24 commit 2 absorbed the
    discipline lesson with the Active-deferrals-scan refinement to
    chapter-mid-audit; the table-propagation cluster lands at
    chapter close.

### From session 24's static-entity slice (implicit-coverage finding)

  - **The static-entity translation implementation has shipped at
    `CatalogReader.fs:578` since session 18 but no fixture
    exercised the `isStatic: true` branch until session 24.** Five
    prior fixtures (sessions 18–22) all carried `isStatic: false`.
    Session 24 commit 3 closed the contract gap. The session 24
    DECISIONS amendment names the broader question — should
    chapter-close audits include input-conditional adapter paths
    as a dimension? — to be tested at chapter close by subagent
    #3's OSSYS chapter completeness audit. If subagent #3
    surfaces additional uncovered paths, the discipline earns its
    own DECISIONS row.

### Open questions accumulating (from session 22 audit)

  1. **A1 forwarding pointer style** — session 23 commit 5 lands a
     basic forwarding pointer paralleling A18's session-13 fix. Open:
     should A1 receive a *substantive amendment* (parallel to A18's
     own amendment at the bottom of AXIOMS) rather than just a
     pointer? The bound is significant; A18 got a full amendment;
     A1's bound is structurally similar.

  2. **Admire entry framework — "extracted" semantics for hybrid
     mode** — the session-23 framework extension added the
     `extracting (in flight, N slices)` status. When hybrid-mode
     entries close, do they transition to `extracted (differential
     confirmed)` or to a hybrid-aware close-status? V1-migration
     entries used `extracted (differential confirmed)`; V2-growth
     used `extracted (V2-growth confirmed)`. Hybrid mode at close
     hasn't been seen yet.

  3. **Operating-disciplines table maintenance** — session 22's audit
     noted that some disciplines codified in DECISIONS aren't in
     CLAUDE.md's table (the writer codification stability mark; the
     opportunityEntry extraction-defer entry). Session 23 doesn't
     address this; chapter-close ritual's "operating-disciplines
     table currency" item is the right place.

  4. **Async/Task at the OSSYS boundary** — `parse` returns
     `Task.FromResult(...)` synchronously. Half-realized async story.
     Real DB-touching variants (LiveOssysConnection) would change
     this; until then, the wrapper is overhead. Cosmetic; not
     blocking.

  5. **CHAPTER_2_CLOSE.md scope vs CHAPTER_1_CLOSE.md scope** —
     chapter-1's close was an audit-by-subagent synthesis after
     12 sessions of work. Chapter 2's close should mirror or
     evolve. The framework extension for in-flight status implies
     more material than chapter 1 had. Ritual TBD.

  6. **OPEN findings 6 and 7 from the audit** — preserved here for
     close synthesis. (Audit categorized them; the categorization
     is in DECISIONS via session 22's audit summary.)

### Open questions accumulating (from session 23 audit — subagent #2)

  7. **Adapter-boundary deferrals scope in the Active deferrals
     index.** Sessions 18–22 produced 10+ adapter-translation
     deferrals with explicit re-open triggers (auto-number axis;
     cross-module FK; `IsExternalEntity` Origin three-way;
     `Modality.Inactive` carry-through; filter-definition indexes;
     `physical_isPresentButInactive`; etc.). The index's scope
     statement (`DECISIONS.md:65-88`) admits architectural IR
     refinements but is silent on adapter-boundary deferrals.
     Cross-module FK and `Modality.Inactive` are at the same
     architectural level as cross-catalog FK detection (already
     in the index); a scope decision is warranted at chapter close.

  8. **Chapter-level scope deferrals discipline-table entry.**
     The OSSYS strategic-frame entry (`DECISIONS.md:4032`)
     codifies a chapter-open pattern (strategic-frame axes named
     at chapter open) that other future chapters (Pipeline canary;
     SnapshotRowsets) are explicitly named as inheriting from. It
     is structurally a discipline — "do strategic-frame axes
     naming at chapter open" — but it is also a one-off
     enumeration of axes specific to OSSYS. The session-23
     framework extension amendment (line 1854) refines it. Should
     it earn a discipline-table entry, or is it intentionally
     chapter-specific?

  9. **Trace-before-fixture pointer suffix drift.** Row at
     `CLAUDE.md:66` cites `DECISIONS 2026-05-19 — Trace-before-
     fixture pattern at slice level (session 23)`. The DECISIONS
     entry header reads `## 2026-05-19 — Trace-before-fixture
     pattern at slice level (codified at N=3)`. The "(session 23)"
     suffix doesn't match the entry title; gentle suffix-
     convention drift, same shape session 22's audit flagged for
     other chapter-2 entries. Fresh agents can resolve the
     pointer; future hygiene normalizes the convention.

### Forward signals for chapter 3 (named by chapter 2's work)

The chapter-close ritual will identify these formally; preserved
here as in-flight observations:

  - **Cross-module FK slice** — defers to fresh context per the
    session-23 runway plan. Highest-priority deferred slice for the
    new context. Refines the same-module assumption from rule 16
    (session 19).
  - **`SnapshotRowsets` implementation chapter** — operator-decided
    canonical resolution; lands as its own multi-session arc.
    Resolves the JSON-projection-lossiness class.
  - **`Projection.Pipeline` canary chapter** — strategic-frame axis
    from session 17. Substantial multi-session work (DacFx,
    testcontainers, ephemeral SQL Server, read-side adapter).
  - **DacpacEmitter** — sibling Π for real CREATE TABLE / DacFx
    emission. Originally deferred since session 1; the deferral's
    trigger fired silently across sessions 18–22 (caught by
    subagent #2 audit). Session 24 commit 1 cashed out as a
    re-defer with tighter trigger condition (canary chapter is the
    natural locus). When `Projection.Pipeline` opens substantive
    deployment-arc work, this trigger fires and DacpacEmitter
    implementation lands.
  - **Faker emitter** — synthetic-data Π consuming Profile.
    Deferred per `CHAPTER_1_CLOSE.md §4 priority 8`; chapter 2
    has not surfaced demand for the third evidence type that
    would unblock it.

## How to update this document

  - **Per-session updates** to the in-flight scaffold are
    encouraged — when a session surfaces an open question or
    finding that belongs at chapter close, append it here rather
    than deferring to chapter close to rediscover.
  - **At chapter close** (session 25 planned), this document gets
    a synthesis-by-subagent pass mirroring chapter-1's close,
    incorporating the accumulated material.
  - **Append-only discipline applies**: don't overwrite open
    questions when they're answered; mark them resolved with the
    DECISIONS reference.

The scaffold-early disposition is itself a session-23 hygiene
discipline: instead of letting findings accumulate as informal chat
context that gets lost between sessions, they land in a working
surface that survives the conversation.
