# Chapter 2 close — synthesis

This document is the chapter-2 close synthesis. **Chapter 2 closed
at session 25** (sessions 13–25 inclusive) covering three sub-arcs:
Diagnostics writer (sessions 13–16); OSSYS adapter implementation
(sessions 17–24); chapter-close runway (sessions 23–25). The chapter
ran the chapter-mid-audit discipline at three points (subagents #1
session 22, #2 session 23, #3 session 25 — the chapter close itself)
plus pre-scoping subagents (#4 DacpacEmitter, #5 SnapshotRowsets) at
session 25.

**Status:** **closed (session 25).** This document is the chapter-2
synthesis and the chapter-3 entry-point document for the OSSYS arc's
forward signals. The scaffold-during-chapter discipline (session 23)
proved its worth — findings landed continuously rather than being
re-derived at close.

The chapter-close ritual (`DECISIONS 2026-05-14`, session-25 amendment
adding item 8 V1-input-envelope walk) names eight load-bearing items
every chapter close must execute. The ritual's execution lives in
the "Chapter-close ritual execution" section below.

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
  - **9 MINOR findings** — split into two clusters; both addressed at session 25 commits 4–5.
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

### From session 25's chapter-close audit (subagent #3 — OSSYS chapter completeness)

**Full report at `CHAPTER_2_AUDIT_3_OSSYS_COMPLETENESS.md`.**
Totals: 1 CRITICAL, 11 MINOR, 7 OPEN. CRITICAL (`onDisk`
silent-drop) resolved at session 25 commit 1. MINOR cluster
addressed at session 25 commits 4–6. OPEN resolved at session
25 commit 7 (`DECISIONS 2026-05-21 — Chapter 2 close: OPEN-
question resolutions`). The cross-cutting observation about
silent drops clustering at the V1-input-envelope-not-walked
surface produced the chapter-close ritual's eighth item
(V1-input-envelope walk — codified at session 25 commit 3).

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

## Chapter-close ritual execution (session 25)

The eight load-bearing items per `DECISIONS 2026-05-14 — Chapter-
close ritual` (session 15; session-25 amendment for item 8). Each
records "clean" or names the remediation entry. Subagents #1, #2,
#3 dispatched at session 22, 23, and 25 covered most of the
ritual's coverage by their direct walks; this section records the
chapter-2-specific findings and the "clean" verdicts.

### Item 1: Active deferrals index scan

**Status:** clean (after session 24 commit 1 cash-out + session
25 commit 4 cleanup).

  - **DacFx / DacpacEmitter trigger** — fired silently sessions
    18–22; cashed out at session 24 commit 1 (re-defer with
    tighter trigger; canary chapter is the locus). The same-shape
    silent-trigger fire as the transform-registry miss; the
    chapter-mid-audit caught it before chapter close.
  - **`SnapshotRowsets` and `LiveOssysConnection` variants** —
    documented as deferred in `CatalogReader.fs:36-68` since
    session 18, never in the index. Added at session 25 commit 4.
  - **Cross-module FK IR refinement** — added at session 25 commit
    4 as the highest-priority deferred slice for chapter 3.
  - **All other rows** — status verified at session 25 commit 4
    with session-tagged status updates.

The Active-deferrals-scan dimension was added to the
chapter-mid-audit dispatch shape at session 24 commit 2 (from
subagent #2's cross-cutting observation that pointer drift and
trigger-fire drift are different cost classes).

### Item 2: Contract-vs-implementation cross-reference walk

**Status:** clean for chapter-2 substantive surfaces. Session 24's
static-entity slice surfaced an implicit-coverage finding (the
adapter's `if isStatic then [Static []] else []` branch had
shipped at session 18 without fixture coverage). The slice
closed the gap; the finding seeded a discipline question
(input-conditional adapter paths) that subagent #3 evaluated at
session 25.

Subagent #3 found one additional implicit-coverage instance:
**rule 13's `deleteRule` mapping has five branches, but only
`Protect` is fixture-exercised** (subagent #3 M2). The other
four branches (Delete, Ignore, SetNull, null) ship without
explicit fixture coverage. Logged for chapter 3's chapter-open
pickup if any of those branches surfaces in a fixture.

### Item 3: CLAUDE.md staleness check

**Status:** clean after session 25 commits 2, 3, 5, 7, 8.

  - Reading order updated (HANDOFF/HANDOFF_CHAPTER_1 pattern;
    CHAPTER_2_CLOSE before CHAPTER_1_CLOSE; A1 forwarding pointer
    noted; OSSYS adapter named in code listing).
  - Operating-disciplines table extended with three previously-
    missing rows (admire-mode spectrum; writer codification
    stability mark; opportunityEntry extraction-defer); two
    pointer-cleanup fixes; three ambiguous-date pointer
    disambiguations; three new chapter-2 contributions added
    (three-class typology; chapter-mid-audit; trace-before-
    fixture; V1-input-envelope walk). Plus chapter-level scope
    deferrals row added.
  - F# feature surface section: no chapter-2 feature changes.
    `Async`/`Task` at the OSSYS boundary noted in the descriptor;
    the boundary's wrapper-overhead question deferred to
    chapter 3 canary (open question O4 in subagent #2's audit
    resolved at session 25 commit 7).

### Item 4: README.md staleness check

**Status:** addressed at session 23 commit 1 (chapter-mid hygiene
work). README rewritten to absorb chapter-2 substantive work:
`Projection.Adapters.Osm` added to layout; `Diagnostics.fs` added
to Core file list; `Projection.Pipeline` and read-side adapter
added to reserved slots. Subsequent chapter-2 work has not
warranted further README updates; the content is current as of
chapter-2 close.

### Item 5: HANDOFF.md / CHAPTER_N_CLOSE.md scope

**Status:** clean. Session 23 commit 4 renamed `CHAPTER_CLOSE.md`
→ `CHAPTER_1_CLOSE.md` and scaffolded `CHAPTER_2_CLOSE.md`
(this document). Session 25 commit 8 renamed `HANDOFF.md` →
`HANDOFF_CHAPTER_1.md` and wrote new `HANDOFF.md` as the chapter-
2-to-chapter-3 letter. Append-only documentation discipline
preserved across both renames.

The naming convention now established: unnumbered names are the
"latest" (active fresh-agent entry point); numbered names are
historical preservations. Future chapter closes follow the same
shape.

### Item 6: Fresh-eye walk

**Status:** clean. Three fresh-eye subagent walks executed across
the chapter:

  - **Subagent #1 (session 22)** — cross-document consistency
    audit. Surfaced 6 CRITICAL, 21 MINOR, 7 OPEN findings.
    CRITICAL fixed at session 23 hygiene work; MINOR rolled into
    this scaffold; OPEN resolved at session 25 commit 7.
  - **Subagent #2 (session 23)** — Active deferrals + operating-
    disciplines audit. Surfaced 1 CRITICAL (DacFx silent fire),
    9 MINOR (split into index-snapshot drift cluster + table
    propagation gaps cluster), 3 OPEN. CRITICAL fixed at session
    24 commit 1; MINOR fixed at session 25 commits 4–5; OPEN
    resolved at session 25 commit 7.
  - **Subagent #3 (session 25 — chapter close itself)** — OSSYS
    chapter completeness audit. Surfaced 1 CRITICAL (`onDisk`
    silent drop), 11 MINOR, 7 OPEN. CRITICAL fixed at session 25
    commit 1; MINOR cluster addressed at session 25 commits 4–6;
    OPEN resolved at session 25 commit 7.

The chapter ran the fresh-eye discipline three times — exceeds
the chapter-close-ritual's single-walk minimum. The
chapter-mid-audit codification (session 23) is itself the
discipline that produced this multi-walk shape.

### Item 7: Operating-disciplines table currency

**Status:** clean after session 25 commit 5. Three previously-
missing rows added (admire-mode spectrum; writer codification
stability mark; opportunityEntry extraction-defer at N=3-of-
distinct-shapes); plus the chapter-2 contributions added in
their respective commits (chapter-mid-audit at session 23;
trace-before-fixture at session 23; three-class typology at
session 25 commit 2; V1-input-envelope walk via the
chapter-close ritual row at session 25 commit 3; strategic-
frame axis-naming at chapter open at session 25 commit 7).

The table now has 22 rows reflecting the cumulative discipline
surface across chapters 1 and 2.

### Item 8: V1-input-envelope walk (added at session 25)

**Status:** clean for OSSYS chapter at chapter-2 close (session
25 commit 6). Subagent #3 walked `SnapshotJsonBuilder.cs`
field-by-field against the OSSYS won't-carry-forward list and
the running translation-rules amendments. Surfaced:

  - **One CRITICAL silent drop** (`attributes[].onDisk`
    envelope, eleven structured fields) — resolved at session
    25 commit 1 with explicit won't-carry rationale +
    re-open trigger.
  - **Three additional MINOR silent drops** — `module.isSystem`,
    `module.isActive`, `attributes[].default`,
    `attributes[].refEntity_isActive` — resolved at session 25
    commit 6 with explicit won't-carry-forward additions.
  - **One additional V1 element** (`module.isSystem` /
    `module.isActive`) — collapsed under entity-level rule 17
    handling for `isSystem`; rule 18's filter extended to
    `module.isActive` per session 25 commit 7's O2 resolution.

The walk discipline operating during its own first execution at
chapter-2 close: this is the meta-pattern (audits-generate-
disciplines) operating in real-time. The discipline surfaced
silent drops; codifying it ensures future V1↔V2 chapters
inherit the practice.

## Chapter-2 substantive deliverables summary

  - **Diagnostics writer** — `Projection.Core/Diagnostics.fs`
    landed at session 14 commit 3 with `Lineage<Diagnostics<_>>`
    composition. Three-pass codification stability mark earned
    at session 16 (UniqueIndex, Nullability, ForeignKey).
  - **OSSYS catalog adapter** — `Projection.Adapters.Osm/CatalogReader.fs`
    (~720 lines). Six substantive slices producing **25
    translation rules** across the JSON path (`SnapshotJson`
    variant of `SnapshotSource`; `SnapshotFile` reserved;
    `SnapshotRowsets` and `LiveOssysConnection` deferred). Six
    embedded V1 fixtures with hand-built expected V2 Catalog
    values; all differential tests pass.
  - **Three-class typology** for V1↔V2 translation findings —
    JSON-projection-lossiness / V2-boundary-discipline /
    alternative-IR-surface. Codified at chapter-2 close
    (`DECISIONS 2026-05-21`).
  - **Audit disciplines** — chapter-mid-audit (session 23);
    trace-before-fixture (session 23); V1-input-envelope walk
    (session 25). The audits-generate-disciplines meta-pattern
    is the chapter-2 closing arc's most distinctive feature.

## Chapter-2 documentation deliverables summary

  - `CHAPTER_1_CLOSE.md` (renamed at session 23 commit 4 — append-only preservation).
  - `CHAPTER_2_CLOSE.md` (this document; scaffolded session 23 commit 4; finalized session 25).
  - `HANDOFF_CHAPTER_1.md` (renamed at session 25 commit 8 — append-only preservation).
  - `HANDOFF.md` (rewritten at session 25 commit 8 as the chapter-2-to-chapter-3 letter).
  - `README.md` rewritten at session 23 commit 1.
  - `CLAUDE.md` extended with five new operating-disciplines rows + propagation-gap fixes.
  - `ADMIRE.md` OSSYS entry transitioned from `chapter-open scoping (session 17)` → `extracting (in flight, N slices)` (session 23) → `extracted (chapter 2 close — JSON path; hybrid mode operating)` (session 25).
  - `AXIOMS.md` A1 forwarding pointer added at session 23 commit 5.
  - `DECISIONS.md` extended with ~40 substantive entries across chapter 2 (counting amendments) covering the Diagnostics writer codification, the OSSYS adapter chapter, the meta-codifications, and the chapter-close OPEN-question resolutions.

## Chapter-2 test baseline at close

**632 passed; 7 skipped; 0 failed across 639 tests** at session-25 close.
Baseline up from chapter-1 close's 585/588 (3 Skip stubs); chapter 2
added Diagnostics writer tests, OSSYS differential tests across six
slices, and various chapter-2-introduced unit tests. The 7 Skips are
intentional (the chapter-2 deferred contracts: `SnapshotFile`
variant; multi-rowset paths; etc.). Failed: 0.

## Closing

Chapter 2 closes with the OSSYS catalog adapter operational on the
JSON path; the Diagnostics writer codified at its stability mark;
the three-class typology for V1↔V2 translation findings complete;
the audit-generates-discipline meta-pattern visible across three
chapter-mid-audits. The chapter ran for 13 sessions (sessions
13–25) producing six substantive slices on the OSSYS arc plus
multiple meta-codifications.

The chapter-2 architect inherited a chapter-1 codebase whose
disciplines held under audit. The chapter-3 agent inherits a
chapter-2 codebase whose disciplines have *grown* under their own
operation. The most distinctive intellectual artifact is the
three-class typology; the most distinctive operational pattern is
that audits generate disciplines (each chapter-mid-audit produced
a refinement of the next audit's dispatch shape — chapter-mid-
audit codification at session 23; active-deferrals scan at session
24; V1-input-envelope walk at session 25). Future chapters
inherit both the typology and the meta-pattern.

The chapter ahead is yours to shape. The disciplines above are
the load-bearing structure that lets the chapter ahead support
more weight than the one behind. **Hold the spine.**

— The session 13–25 architect.

## Forward signals for chapter 3 (final shape)

Three plausible chapter-3 arcs in approximate priority order:

  - **`Projection.Pipeline` canary chapter** (highest leverage).
    Strategic-frame axis-4 from session 17. Multi-session work
    (DacFx, testcontainers, ephemeral SQL Server, read-side
    adapter integration). The DacFx trigger (re-deferred at
    session 24) fires here. Subagent #4 pre-scoped the
    DacpacEmitter chapter (see "Subagent #4 pre-scope summary"
    below); the report is the chapter-open input.
  - **`SnapshotRowsets` implementation chapter** (independent of
    canary; resolves the JSON-projection-lossiness class).
    Subagent #5 pre-scoped the chapter (see "Subagent #5 pre-
    scope summary" below). Subagent #5's recommendation: open
    SnapshotRowsets **parallel-to or before** canary, so that
    canary inherits a Catalog with full SsKey carriage. Reverses
    the chapter-3 priority ordering originally proposed at
    session 24.
  - **Cross-module FK slice** (small completeness step;
    refines OSSYS rule 16's same-module assumption). The
    handoff's caveat: rule 14's "walk attributes[isReference=1]"
    assumption may not hold cross-module because V1's
    `attributes[].refEntityId` is a numeric within-module
    pointer; the cross-module case may force walking
    `relationships[]` instead. Trace-before-fixture applies.

The two pre-scope subagent reports are the chapter-open inputs
for those chapters. Each lives as a chapter-3 entry-point
document.

### Subagent #4 pre-scope summary — DacpacEmitter chapter

**Full report at `CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md`** (the
chapter-3 chapter-open input for DacpacEmitter implementation).
Key findings preserved here as the close synthesis:

**The DacFx API surface.** Four classes V2 needs:
`Microsoft.SqlServer.Dac.Model.TSqlModel` (model construction
via script-text-driven `AddObjects`); `TSqlObject` (loosely-
typed handle for traversal); `Microsoft.SqlServer.Dac.DacPackage`
(the `.dacpac` artifact); `Microsoft.SqlServer.Dac.DacServices`
(the deployment driver). **Critical observation:** DacFx's
public API is script-text-driven, not object-graph-driven —
V2 feeds `CREATE TABLE …` scripts into the model rather than
constructing typed Tables / Columns directly. The trunk's
`Osm.Smo` uses SMO (live-DB-administration), not DacFx; **no
DacFx code exists anywhere in the repository today**.
DacpacEmitter would be the first DacFx integration.

**F# vs C# layering recommendation.** DacFx's idiom (disposable
scopes, mutable model state, exception-driven validation) is
the exact "object-instantiation-heavy, foreign-API-I/O" shape
`DECISIONS 2026-05-09` (adapter-language rule) sends to C#.
Recommendation: **DacFx wrapper lives in C# inside
`Projection.Pipeline` (or a new `Projection.Targets.SSDT.Dacpac`
C# project); F# DacpacEmitter calls across a value-typed seam**
(`Catalog -> Result<byte[]>`).

**Byte-determinism — the critical risk.** Vanilla `BuildPackage`
produces non-byte-deterministic output: `Origin.xml` embeds
wall-clock `Operation` timestamps; the model.xml checksum
derives from that; zip-entry timestamps differ per run. **T1's
"same input ⇒ byte-identical output" does not hold for DACPAC
bytes out of the box.** Three resolution strategies:
(a) post-hoc canonicalization of the zip; (b) redefine T1 for
binary emitters as content-equality via DacFx round-trip;
(c) hybrid. Recommendation: (b) for the algebra; (a) when a
snapshot consumer requires byte-stable artifacts. **Likely
requires a T1 amendment** at chapter open.

**IR-to-DacFx impedance.** Module → no DacFx peer (likely
emitter-side annotation only; Schema comes from
`Kind.Physical.Schema`). Kind → `CREATE TABLE`. Attribute →
column. Reference → FK constraint. Index → index object.
Modality marks (Static / TenantScoped / SoftDeletable) → no
DacFx counterpart; Static populations route to a separate
`StaticSeedsEmitter`; TenantScoped / SoftDeletable shape
tables via pass-time additions. Origin axis → no DacFx peer;
Π is origin-blind once a kind reaches the emitter (Selection
filtered upstream in a pass per A12).

**T11 sibling-Π commutativity.** For DACPAC bytes, T11 requires
load-and-enumerate via `DacPackage.Load` + `model.GetObjects` +
assertion on Table-per-Kind by SsKey root. Heavier than text
emitters' grep-able form; cost acceptable.

**Recommended chapter-open scoping.** Sequencing: read-side
adapter first, DacpacEmitter second (confirms session-24
cash-out's framing). Minimal first slice: single-table Catalog
→ load → enumerate → assert one Table with two Columns and a
PK constraint. Defer byte-determinism; cover content-determinism
via DacFx round-trip. Add T11 commutativity test across
RawTextEmitter and DacpacEmitter agreeing on attribute type
rendering.

**Eight risks / open questions** for the chapter-open document
to explicitly defer or escalate (byte-determinism strategy;
F#/C# wrapper layering; Module → Schema mapping; modality marks
at the dacpac surface; pre/post-deployment scripts; Origin
handling; DacFx version pinning; PackageMetadata choices).

### Subagent #5 pre-scope summary — SnapshotRowsets chapter

**Full report at `CHAPTER_3_PRESCOPE_SNAPSHOT_ROWSETS.md`** (the
chapter-3 chapter-open input for SnapshotRowsets implementation).
Key findings preserved here as the close synthesis:

**V1 rowset shape.** V1's SQL extraction emits **23 rowsets**
(`outsystems_metadata_rowsets.sql:956-1184`). Phase-1 rowsets
1–3 (modules / entities / attributes) are sufficient to
resolve all three known JSON-projection-lossiness members:
`EspaceKind` (rowset 1), `IsSystemEntity` and `EntitySsKey`
(rowset 2), `AttrSsKey` (rowset 3). The lossiness happens at
exactly one layer — V1's `SnapshotJsonBuilder` field selection
plus the `FOR JSON PATH` aggregations in rowsets 19–23. **All
lossy fields travel intact through phase-1 rowsets**;
`SnapshotRowsets` consumes rowsets 1–18 (the structural raw
layer; not the JSON aggregations).

**Multi-rowset deserialization architecture.**
**Recommendation: per-rowset hand-written F# DTO records,
bundle delivered via a single carrier-DU variant**
(`SnapshotRowsets of bundle: RowsetBundle`), with materialization
located inside the OSSYS adapter (preserving F# core's no-I/O
discipline). C# DataReader streaming is the natural language
for the loader, but **the loader lives above the adapter** in
a future `Projection.Adapters.Osm.SqlClient` C# project;
the adapter sees only the value-typed `RowsetBundle`. Tests
construct bundles directly as fixture data, mirroring the
existing `v1MinimalFixture` string-fixture pattern.

**DTO shape.** Hand-written F# records first; type providers
stay deferred. `SqlClientProvider` would require DB
connectivity at compile time — strictly worse CI fragility
than `JsonProvider`. The hand-written DTOs already exist in C#
at `IOutsystemsMetadataReader.cs:71-207`; F# transcription is
mechanical. Hand-maintenance burden is bounded by V1 SQL's
slow evolution cadence.

**Integration with `CatalogReader.parse`.** Add `SnapshotRowsets`
variant to the closed DU; add a third match arm calling new
`parseRowsetBundle`; existing `parseJsonString` /
`parseDocument` paths untouched. The closed-DU empirical-test
discipline predicts F# exhaustiveness errors light up only at
the match site; if more reshape, the seam is wrong.

**SsKey-shape divergence under the rowset path.** Rules 1–3's
synthesis convention (`OS_KIND_<modName>_<entName>`) may
either coexist with Guid-string SsKeys (option 1: per-source
SsKey shape; simpler implementation, loses parity-test
surface) or be canonicalized at translation time (option 2:
both paths emit the same SsKey shape; defers to a future IR
refinement). Subagent #5 recommends option 1 for the first
slice; option 2 is deferred to a follow-on slice when cross-
source parity tests demand it.

**Class-of-lossiness coverage plan (5–6 substantive slices):**
slice 1 SsKey at all three levels (highest leverage; foundational;
resolves the A1 bound); slice 2 reference-bearing under rowset
path; slice 3 `EspaceKind` activation (refines rule 17 from
placeholder to three-way real); slice 4 `isSystemEntity`
activation (likely demands an IR refinement); slice 5 cross-
source parity tests; deferred slices for per-table column
structure, check constraints, future members.

**Coexistence with `SnapshotJson` after `SnapshotRowsets`
ships.** Both paths coexist permanently in the closed DU. No
deprecation path is named. `SnapshotJson` remains the simpler
test surface for fixture-driven slice work that doesn't
exercise lossiness members; `SnapshotJson` is the fallback
when rowsets are unavailable.

**Recommendation: open SnapshotRowsets parallel-to or before
canary, not after.** Reverses the original chapter-3 priority
ordering. Reasoning: the canary's DacFx work needs a Catalog
input; if the canary opens with the JSON path's Catalog, the
SsKey-bound question recurs against DacFx behavior;
SnapshotRowsets resolved first means the canary inherits a
Catalog with full SsKey carriage. SnapshotRowsets is also
structurally smaller scope (no DacFx, no testcontainers, no
ephemeral DB).

**Estimated arc length: 5–6 sessions** (mirroring chapter-2's
six-slice OSSYS arc). First slice is heavier (DTO surface +
variant scaffolding); subsequent slices are lighter (re-
exercising already-traced fixtures under the rowset path);
slice 5 is parity discipline.

**Seven risks / open questions** for chapter-open: SsKey-shape
divergence resolution; `EspaceKind` value semantics; C#-shell
loader project location; whether SnapshotRowsets triggers
`ICatalogReader` interface materialization (subagent #5's
read: it does not — same source, second variant); whether
fixture-driven IR refinements are in scope for the first arc
(`isSystemEntity` activation likely demands one); whether
parity tests are unit or integration; whether the V1 C# DTO
surface should be re-mirrored or re-derived.

### Implication for chapter-3 sequencing

Subagent #5's recommendation (open SnapshotRowsets before
canary) and subagent #4's recommendation (canary opens with
read-side adapter first, then DacpacEmitter) are compatible if
chapter 3 splits into two arcs: **SnapshotRowsets arc** runs
parallel-to-or-before the **canary arc**. The chapter-3 agent
inherits two opens; either or both may run before cross-module
FK lands as a tactical-completeness step.

## How this document was used

  - **Per-session updates** during sessions 23–25 — findings
    landed continuously rather than being re-derived at close.
    The scaffold-early disposition (session 23 hygiene) proved
    its worth.
  - **At chapter close (session 25)** — this document
    incorporated the accumulated material plus the chapter-close
    ritual execution (above).
  - **Append-only discipline applied** — open questions were
    resolved at session 25 commit 7 in DECISIONS rather than
    overwritten here; the OPEN-questions sections remain as
    historical record.
