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
    across sessions 18–22 (minimal, reference-bearing, external-
    entity, mixed-active, index-bearing). Twenty-three translation
    rules in the running list. Status: `extracting (in flight,
    5 slices)` per the session-23 framework extension.
  - **Sessions 23–24+: chapter close runway.** Documentation
    hygiene + remaining substantive slices (static-entity at
    session 24; cross-module FK deferred to fresh context).
    Chapter close at session 25 (planned).

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
    emission. Deferred since session 1; the OSSYS chapter has not
    surfaced new pressure for it.
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
