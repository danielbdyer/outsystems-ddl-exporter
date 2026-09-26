# LAPIDARY — The Surgical Backlog Mission (the generation-2 prompt)

> **What this is.** The mission brief for the agent that follows the Constellation session —
> passed by the operator as that agent's opening message, preserved here verbatim for
> provenance (the `KICKOFF.md` precedent: first-message briefs live in-repo). Lineage:
> generation 1 received the Constellation prompt and produced `CONSTELLATION.md` (the thesis)
> and the rebuilt `CLAUDE.md`; generation 2 receives this and produces
> `CONSTELLATION_BACKLOG.md` (the surgical slice plan); generation 3 builds, slice by slice.
> Recorded 2026-06-11, at the close of the Constellation session.

---

**Mission**

Work in `/sidecar/projection/`. You are the second generation of a lineage: the first produced
`CONSTELLATION.md` — the architectural thesis that named the system a Conservation Ledger,
adjudicated its streaming hypothesis, and committed to five recommendations (R1–R5) with a
staged migration path (§10) and a trigger-statused pattern corpus (§9.8). Your single
deliverable is one markdown document — `CONSTELLATION_BACKLOG.md`, at the projection root
beside its thesis, per the house pairing precedent of `INSTRUMENT_BACKLOG.md` — containing the
surgical slice plan that realizes those outcomes. Think of yourself as the thesis's first
surgeon, not its notary: a surgeon re-images before cutting, and where the thesis is wrong or
has aged, your backlog says so and routes around it. That correction *is* realizing the plan.
Everything before the document exists in service of it.

**The Cleavage Method**

A **cleavage plane** is a seam where the codebase already wants to part. The thesis found
several and they are your worked examples: a docstring MUST that wants to be a constructor
(`DataEmissionComposer.fs:389` → `ParallelSafe`); a "modulo" inside a stated law that wants to
be a value (`Pass.composeAll` modulo `Bench.scope` → `Meter.pass`); the same structure built
twice, eleven days apart, with zero cross-references, wanting one name (`CaptureJournal` ∥
`LifecycleStore` → the ledger contract); a string-prefix convention wanting a type
(`Watch.fs:48`'s stage codes → the spine); four sinks wanting one aggregate (the Run). A
**surgical pivot** is the minimal incision along such a plane: small diff, large unlock, green
at every commit, behavior-preserving unless the slice *is* the behavior change — in which case
the witness comes first (the >1000-row MERGE cliff at `ScriptDomBuild.fs:857` is the canonical
case: capture the failing BEFORE, then cut). The standard you are held to is the
**retrospective-obviousness test**: each pivot should look inevitable after it is named and
invisible before. The genius is not in the ambition of the cuts. It is in their smallness —
the reader should repeatedly think *"that's all it takes?"* And hunt for planes the thesis
missed: its own §12 admits an eight-sector survey, not an exhaustive one. A second
journal/episode-grade discovery — the same shape built twice, unnamed — would be your finest
contribution.

**Phase 0 — Orientation**

Read the new `CLAUDE.md` Tier 1 in order (~40 minutes; it was rebuilt 2026-06-11 — trust it,
and obey its §4 survival rules to the letter). Then `CONSTELLATION.md` — §10 first for the
build order, then the whole thesis, then §12 so you know exactly which of its claims were
verified, which are testimony, and which are conjecture you must re-prove before betting a
slice on them. Then the `DECISIONS` 2026-06-10/11 entries if anything touches the write path.
Internalize two laws above all: measurement precedes mechanism (the perf harness gates
everything gated), and no slice opens before its trigger fires (§9.8's
shipped/fired/armed/predicted taxonomy is binding — *armed items wait for their named
consumers*).

**Phase 1 — Re-imaging via subagents**

Use less-powerful subagents liberally, for information-gathering only; critical reasoning
stays exclusively with you. Sector the survey **by recommendation, not by module**: one agent
per R1–R5, one for the harness stages 0–1, one for the corpus's *fired* items, and one for
"what changed since commit `cae3c79`" (git log + the DECISIONS tail — never write a backlog
against a stale HEAD). Require each to return: the seam's exact current state (file:line,
re-verified — the thesis's citations are testimony until you check them); the **blast radius**
(every consumer that must move, counted and named — the `StaticRow.Values` contract's
consumers across `SurrogateRemap`, `PhysicalSchema`, and the emitters are the worked example
of why this matters); hidden couplings; the test surface that pins current behavior; and a
size estimate with a confidence grade. Treat briefs as testimony; spot-check anything
load-bearing against source.

**Phase 2 — The chunking**

Design the slices. Rules: one plane per slice — bundling is the failure mode; every slice
independently shippable with the suite green and the perf gate respected; witness-first for
anything correctness-adjacent; blast-radius honesty (if a slice touches nineteen call sites,
the card says nineteen); the dependency graph drawn explicitly and the **critical path named
and defended**; the operator-gated interrupt encoded (J5 — a writable UAT connection — trumps
everything, and your sequencing must survive that preemption); baseline re-records only with
their DECISIONS amendment.

**Phase 3 — The document**

Required spine (adapt headings freely): the **re-imaging report** — where the thesis aged
since 2026-06-11, claim by claim, with verdicts; the **cleavage-plane inventory** — every
plane, cited at current HEAD, including the ones the thesis missed; the **slice catalog** —
the heart: per-slice cards carrying the plane (file:line), the incision (signature-grade F#,
in the house style §9.8 demonstrates), the unlock, the acceptance witness as a house backtick
test name, size class, dependency edges, trigger status, and the rollback story; the
**dependency graph and critical path**; the **sequencing**, mapped onto `PERF_HARNESS.md`
§4's slices and `CONSTELLATION.md` §10's stages; the **refusals** — slices considered and
rejected, and every armed item listed with its wake condition so the agent whose session
fires the trigger recognizes it; the **risk register** — the traps with teeth (FS3511 Release
shapes, `{create … with …}` default substitution at every reconstruction site, the
DeleteScope arm under chunking, the lazy-probe attribution trap); and your own **epistemic
ledger** — the house now expects every thesis-grade document to account for its own evidence.

**Quality Bar**

Every plane cited at current HEAD, re-verified — never inherited. Every slice falsifiable
through its named witness. Opinionated and committed: one sequencing, one critical path,
tradeoffs named, no hedging. Dry research register. The patient never stops breathing: no
slice may leave the canary red, the pools entangled, or the gate baseline silently moved.
**You write no production code this session** — reads to verify, yes; the deliverable is the
plan. Failure modes to refuse: a renamed copy of `CONSTELLATION.md` §10; a flat list without
dependency physics; bundled slices; speculative cards for armed items; size-class fantasy;
and — most subtle — a backlog that never once disagrees with its thesis. If you find nothing
to correct, you have not re-imaged hard enough.

At session end: prepend your `HANDOFF.md` letter (forward-looking, second-person — the third
generation, the builder who executes your slices one by one, is your reader), and pass this
torch forward as it was passed to you.

This workflow and document is the crowning purpose of the session. The first generation drew
the map of the stars. You cut the stone along its planes. Make the backlog the one I've never
seen.

Hold the spine; balance the books.
