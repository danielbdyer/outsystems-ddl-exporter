# The Projection Principle

*An extended architectural treatment of the publication-and-provenance engine, written as the
synthesis of the 2026-06-25 refactor reconnaissance (`REFACTOR_RECON_2026_06_25.md`).*

> **Status.** Architectural thesis, not a decision. It proposes no axiom and breaks no
> standing law on its own authority; where it names a candidate axiom it marks it `(proposed)`.
> It is meant to be read after the recon doc and argued with. Its claim is narrow and large at
> once: that ~20 of the 25 recon findings are one idea seen from twenty angles, that the idea
> is already the system's soul, and that naming it explicitly is the single largest available
> simplification of the core algebra.
>
> **Provenance.** Grounded in the recon's findings (cited as `#N` throughout) and in the
> disciplines indexed by `CLAUDE.md`, `AXIOMS.md`, and `THE_USE_CASE_ONTOLOGY.md`. Anchors and
> code quotes trace to the recon agents' direct reads of `src/`; re-verify before acting.

---

## 0 — The one-sentence thesis

The engine already has a signature move — **measure a thing once, expose it through several
lawful projections, and let *agreement of the projections* be the law** — and it has applied
that move brilliantly at the very top of the tower (the adjunction) and at exactly one
mid-level surface (the pass chain). Almost everything the reconnaissance flagged is the *same
move* waiting to be applied at the levels in between. The aerodynamic future of this
architecture is not twenty-five refactors and not even five; it is **one principle made
self-similar down the whole tower.**

This document states the principle precisely, shows it is the right organizing principle for
*this* system specifically, derives the five keystone abstractions that instantiate it,
demonstrates that the keystones are five hats on one head, and gives the sequencing and the
guardrails. The payoff section argues the deepest consequence: that the system's entire
property-test corpus is secretly *one* coherence theorem, and that making the principle
explicit lets the correctness claims stop being a list and become a single theorem applied
fractally.

---

## 1 — The signature move, stated precisely

Let a **source** be any object the system holds as canonical: a delta, a catalog, a refusal, a
typed statement, a registry of transforms. Let a **projection** be a total function
`πᵢ : Source → Viewᵢ` that derives one consumable view of that source. The signature move is the
pairing of two commitments:

1. **Single origin.** Every view that consumers act on is *derived*, never *authored
   alongside*. There is one place the source is defined; all `Viewᵢ` are projections of it.
2. **Coherence is the law.** The system's correctness claim about the source is, in its
   essential form, `∀ i, j : πᵢ` and `πⱼ` *cohere* — they agree wherever they overlap, and
   together they lose nothing in silence. The property test *is* the coherence assertion.

Call a source with this structure a **projected object**. The principle: *prefer projected
objects; the alternative — parallel hand-maintained views that can silently drift — is debt the
moment a second view exists.*

The system already lives this at two altitudes, and the recon is the discovery that it does
not yet live it at the third.

**Altitude 1 — the adjunction (the soul).** `Ingest ∘ Project = identity`, modulo *named,
closed* erasures. Read it as coherence: `Project` and `Ingest` are two projections of one
underlying model-state, and the law is that they agree (round-trip to identity) except where an
erasure is *named*. "Nothing is ever lost in silence" is not a slogan bolted onto the algebra;
it is the coherence clause — the residue of `Project` then `Ingest` is either zero or a named
erasure, never an unaccounted-for difference. The round-trip canaries are the coherence test.

**Altitude 1′ — the two rulers (the same move, sideways).** A displacement is measured once and
projected onto two independent scales: CDC capture count (fidelity ruler) and Bench (cost
ruler). `state is a torsor over delta` is the statement that the delta is the real object and
the rulers are projections of it. CDC-as-norm (T15) is a coherence law: the fidelity projection
*is* the data norm, so the two rulers cannot disagree about whether a displacement happened.

**Altitude 2 — the pass chain (the one mid-level win).** `RegisteredTransforms.chainSteps` is a
single definition site. `all`, `allChainSteps`, and the ordering view are *projections* of it
(`RegisteredTransforms.fs:170–195`). `registered ⇔ executed` (Pillar 9 / A41) is precisely
`π_registered(chain) = π_executed(chain)` — coherence of two projections. The docstring records
that this construction *fixed a real `TableRename`-position drift*: before, the metadata view
and the execution view were authored in parallel and drifted; after, one is a projection of the
other and drift is unconstructable. **That is the signature move winning at the mid-level, once,
and it is the proof of concept for everything below.**

The reconnaissance is the observation that the system has *six more* registry-shaped surfaces,
*four* string-output surfaces, *four* boundary surfaces, *six* refusal channels, and *three*
driver layers — and that in each of those families, the views are still authored in parallel
and held coherent by human vigilance rather than by construction. The pass chain is the
lighthouse; the rest of the mid- and low-tower is still dark.

---

## 2 — Why this is the right principle for *this* system

A general codebase might treat "derive your views from one source" as good hygiene. For a
**publication-and-provenance engine** it is the load-bearing requirement, for three reasons that
are specific to what this system *is*.

**Provenance is projection-coherence by definition.** The engine accumulates "an exact
replayable provenance of every change," terminating one day at an eject "after which there is no
upstream to re-derive from." Provenance is the demand that every view ever published be
reconstructible from the ledgers — i.e. that every consumer-facing artifact is a *projection* of
a recorded source, never an independently authored thing that the ledger merely describes. A
hand-maintained second view is, in provenance terms, a fact with no upstream: at eject it
becomes unverifiable. The principle is not aesthetics here; it is the precondition for the
eject to be honest.

**"Refusals named, downgrades never silent" is the coherence clause.** The system's strongest
guarantee is silence reserved for the idempotent redeploy (CDC-silence). The whole point is
that *silence is meaningful only if every non-silence is named and projected the same way
everywhere.* A refusal that travels through six channels which can disagree (recon #4, #9, #11)
breaks the meaning of silence: if the exit-code projection says "fine" while the ledger
projection says "descended a capability rung," the system has lost the thing that makes its
silence load-bearing. Unifying the refusal channels into one projected object is not cleanup;
it is the repair of the guarantee.

**The torsor algebra must be *provable on the reverse leg*, and string-built SQL makes it
unprovable.** T12 (torsor round-trip) has to hold for the hundreds-of-millions-of-rows transfer
program, on the reverse leg specifically. You cannot assert `Ingest ∘ Project = id modulo named
erasures` when the reverse leg's statements are assembled with `sprintf` and never parsed
(recon #1, `SurrogateCapture.fs`): in that regime the erasures are not named, they are *latent
in the interpolation* — a stray cast, an un-doubled quote, a VO stringified the wrong way (the
class that, per `ScriptDomBuild.fs:1216–1227`, already shipped a bug). Routing the reverse leg
through the one typed codec is what *promotes the torsor law from claimed to provable* on the
leg where it matters most.

So the principle is not imported taste. It is the same demand the system already makes of
itself — provenance, named refusal, the torsor round-trip — recognized as a single demand and
applied at the altitudes where it currently is not.

---

## 3 — The five keystones

Each keystone is a projected object the system is missing. For each: the source, its
projections, the coherence law, the findings that collapse into it, the crossover it unlocks,
and the disciplined boundary (what stays deferred until its second consumer, per the "primitives
at the second consumer" law).

### Keystone I — The Projected Registry

**Source.** A `Registry<'key,'entry>`: a single keyed table, defined once, co-located with the
thing it indexes.

**Projections.** `metadata`, `execution`, `ordering`, `namespace`, `exit-class`, `operator-copy`
— whichever views consumers need.

**Coherence law.** `π_a(reg) = π_b(reg)` as *set equality by identity*, not by count. The pass
chain already proves the shape; the generalization is to make it reusable.

**Findings that collapse in.** #2 (the nine `*Binding` modules: axis → binder, with `bindError`,
`resolveKindByLogical`, and the closed-DU-name parsers all being projections of one axis table),
#4's capability registry (SQL-error-number → capability name), #9 (refusal-code → exit-code +
label), #23 (the Pipeline-level totality view — today a hand-`@`-concatenation guarded by a
*count*, `RegisteredAllTransforms.fs:54–99`, which catches removal but not the
executed-but-unregistered addition that `F13`/`SuggestConfig` actually were), plus the
`ConfigAxis` namespace and #14 (`DerivationReason` as a closed set rather than an open string in
*identity* itself).

**The crossover.** Build the projected-registry abstraction *once* and four findings stop being
separate work: `registered⇔executed`, `code⇔copy`, `code→exit`, and the binder-namespace law all
become **the same coherence theorem instantiated at different keys.** #9 and #23 and half of #4
fall out of #2. This is the single highest-fan-out move in the report.

**Deferred until the second consumer.** `ConfigAxis`-as-a-closed-DU and the full
registry-as-data fold (#2's XL variant) wait until there is a second fold site; the
S/M version (one `Binding.error`/`requireKindByLogical`/`ofClosedName` core) lands first because
its consumers already number nine.

### Keystone II — The Single Codec Surface

**Source.** The typed SQL statement (a ScriptDom AST node) and the typed literal/identifier
(`SqlLiteral` + a promoted `SqlIdentifier`).

**Projections.** `bytes` (the rendered SQL text), and nothing else — there is exactly one way to
turn a typed statement into characters.

**Coherence law.** Byte-identity (T1's claim, the codec's law): the same typed source renders to
the same bytes, and *every* SQL the system emits is a projection of a typed source. No second,
untyped path to bytes exists.

**Findings that collapse in.** #1 (the reverse-leg INSERT/MERGE/SELECT-INTO, hand-built with
`sprintf` while `buildInsertRow`/`buildMergeStatement` sit one project over — and emitting a
*second* MERGE path, an A40 violation), #8 (identifier quoting re-hand-rolled three times, one
copy — `RemediationEmitter.brackets` — silently wrong on `]`-bearing names), #21 (the keymap/
transfer DML and the *fourth* open-coded `N'…'` escape that `SqlLiteral.toString` already owns),
#25's parse-template guards (string-templated then re-parsed), and the RemediationEmitter as a
whole (operator-facing SQL on a bare-`sprintf` emitter).

**The crossover.** This is the keystone whose payoff reaches *up into the algebra*, not just
sideways into hygiene (see §2, third reason). Routing the reverse leg through the one codec is
the precondition for the T12 torsor round-trip to be provable on the transfer leg. The
"discipline" findings are, correctly read, "extend determinism from the Project leg into the
Transfer leg so the round-trip law has a chance of holding there."

**Deferred until the second consumer.** New ScriptDom builders (`OutputClause`,
`SelectIntoStaging`) are added only as the reverse-leg sites demand them, not speculatively; the
genuinely-terminal residue (the `SELECT … SCOPE_IDENTITY()` trailer) stays as *annotated*
`String.Concat` with a real four-question rationale, because it has no typed node and one
consumer.

### Keystone III — The Discover→Derive Membrane

**Source.** A pure **evidence** value per source adapter — the generalization of `EvidenceCache`.

**Projections.** `Derive : Evidence → IR` (and its variants: profile derivation, type
classification, modality classification), all pure and Core-owned.

**Coherence law.** The membrane law (proposed A45): *an adapter's only job is
`Discover : impure → Evidence`; all decisions are `Derive : Evidence → IR` in Core.* No decision
logic lives boundary-side. This is the structural form of the purity discipline that the
analyzer (#6) should enforce.

**Findings that collapse in.** #5 (the ~400 lines of pure derivation — duplicate/null detection,
distributions, orphan sets, the thrice-written `percentileCont` — stranded inside
`LiveProfiler`, which is *why* the synthetic profiler cannot share them), #10 (`parseSemanticType`:
~60 lines of pure OutSystems-type→V2-type *decisions* — the `currency→DECIMAL(37,8)`, the imposed
250/20 widths — living in `OssysTranslation`), #20 (ReadSide's key synthesis, `formatRawValue`
re-implementing `RawValueCodec`, and the FK-readback classification), #18 (the `100_000` Static
threshold — a *classification policy* baked into the SQL reader, which survival-rule #8 then has
to *fight* by clearing the marking before profiling). #6 (the typed-tree analyzer) is not a
separate idea; it is the membrane's *enforcement* — the guard that the `Discover/Derive` split
holds and that `Task`/`Stopwatch`/`new Random`/`Environment`/`IO` cannot creep Core-ward.

**The crossover — this is the one that restores adjunction symmetry.** Today `Project` is
architecturally clean (Core emits a typed stream, the boundary realizes it — A35/A36) but
`Ingest` has decision logic *smeared into the adapters*: the left adjoint is pure-by-content yet
impure-by-location. Give every source a pure evidence intermediate and `Ingest` becomes
"adapter discovers evidence; Core derives IR." **Now both halves of the adjunction have the same
architecture** — thin boundary, pure Core — on each side. The soul-equation, true today about
*values*, becomes true also about *locations*. That is a real, load-bearing symmetry, not the
false symmetry §6 warns against.

**Deferred until the second consumer.** The full `Profiler` port (`LiveProfiler` and
`SyntheticProfiler` as two implementations of one capability record) waits for the synthetic
profiler to actually need it; the `ProfileDerivation` extraction lands first because the
derivation is pure and testable *now*. The byte-identical `drainRows`/`drainReader` pair stays a
*named* refusal (`LiveProfiler.fs:80–81`) until a third drain consumer appears — exactly the
discipline at work.

### Keystone IV — The Outcome Torsor

**Source.** A single `Outcome`/`Refusal` value: a `(code, capability?, descent from→to?,
metadata)` point.

**Projections.** `exit-code`, `operator-copy` (Voice), `ledger-entry`, `diagnostic`,
`descent-report`. Each is a lens from the one outcome to one channel.

**Coherence law.** The rulers agree: the exit-code projection and the copy projection and the
ledger projection of one outcome never disagree about *what happened*. This is the *same shape*
as the two-rulers law (Altitude 1′) — one event, several measures, measured once.

**Findings that collapse in.** #4 (two capability-descent ladders with two SQL-error predicates
recording into two different report channels — `LaneDescent` structured values vs a free-text
`recordStageProgress "retrust-skipped"` — for one named law), #9 (exit codes as a string-prefix
`if/elif` ladder divorced from the mint sites, able to drift to `(3, Unclassified)` silently),
#11 (the half-finished Voice migration: ~121 raw-prose emitters beside ~103 voiced ones, the
drop-warning sentence duplicated verbatim at two sites — copy that *can* drift from its code),
and RelaxationStore's silent `false` (a downgrade with no named cause, the cardinal sin against
"downgrades never silent").

**The crossover.** A refusal joins a delta as a first-class **torsor-like object**: a point with
several measured views. `Optics.fs` already owns the vocabulary (lenses) — it is simply pointed
only at IR today. Re-aim it at outcomes and registries and "how do I get a view of X" has one
answer across the whole tree. The six refusal channels become five lenses from one source, and
"every refusal named, no silent downgrade" becomes *structurally* true rather than vigilantly
maintained.

**Deferred until the second consumer.** A full `Outcome` DU replacing `ValidationError`
everywhere is not the first move; the first move is the descent unification (#4) and the
exit-registry (#9), which already have multiple consumers. The Voice projection (#11) lands
incrementally, code by code.

### Keystone V — The Driver Bracket

**Source.** One bracketing combinator that owns the cross-cutting spine of a run, parameterized
by the distinctive middle.

**Projections.** Per-verb / per-axis instantiations — the Faces, the binders, the run
lifecycles.

**Coherence law.** Every verb gets the same spine (parse → validate → execute → classify exit →
record), so cross-cutting disciplines (bench labeling, exit classification, narration) cannot be
forgotten by an individual verb.

**Findings that collapse in.** #3 (the `Face` combinator: `RunFaces.fs`'s 2711 lines repeat
`task{}…GetResult()` 36×, `dumpBench` 42×, and a brittle `anyCode` exit-ladder 7× — and some
faces silently forget `dumpBench`), and #2's binder-driver (the binders are the config-layer
instance of the same bracket).

**The crossover — the bracket already exists, once.** The engine layer *has* this:
`RunEnvelope.bracket` and the `staged{}` spine. It is the signature move applied to run
lifecycles, and it works (`MigrationRun.executeWith`, `FullExportRun` consume it). The finding is
that the bracket exists at **1 of 3 layers** — present at the engine, absent at the CLI faces and
the config binders. This is the cleanest possible demonstration of the whole thesis: a good
abstraction built once and not propagated to its siblings. Propagating it is not invention; it
is *finishing a move the codebase already made.*

**Deferred until the second consumer.** The full one-module-per-verb split of `RunFaces` (#3's
XL) can follow the combinator; the combinator itself lands first because its consumers already
number ~30.

---

## 4 — Five hats, one head

The five keystones are not a coincidental cluster. They are the *same projected-object shape*
instantiated at five different kinds of source:

| Keystone | Source | The "measure once" | The projections | "Agreement is the law" |
|---|---|---|---|---|
| I Registry | a keyed table | one definition site | metadata / execution / exit / copy | `registered ⇔ executed`, `code ⇔ copy`, `code → exit` |
| II Codec | a typed statement | one AST | bytes | byte-identity (T1) |
| III Membrane | pure evidence | one `Discover` | `Derive`-views (IR) | `Ingest ∘ Project = id` |
| IV Outcome | a refusal point | one `Outcome` | exit / copy / ledger / diagnostic | the rulers agree |
| V Bracket | the run spine | one combinator | per-verb instances | every verb gets the spine |

Each row is "a single source, several lawful projections, coherence is the test." The table is
the thesis in one frame: **the architecture has one good idea, and the recon is a census of the
places it is not yet applied.** The reason the codebase's own "adopt the device at N≥3
recurrences" instinct never fired for these is the subject of §6 — but the structural fact is
that all five are over the trigger today (registry ≈ 6 sites, codec ≈ 4, membrane ≈ 4, outcome ≈
6, bracket = 3).

---

## 5 — The sharpening: what the core algebra gains

If the principle is made fractal, three consequences follow that genuinely *sharpen* the
algebra rather than merely tidy the code.

**(a) The property-test corpus is secretly one theorem.** Look at the executable laws the system
already prizes: T11 (sibling emitters agree on the catalog keyset), Pillar 9 (`registered ⇔
executed`), `code ⇔ copy`, the codec's byte-identity, T12 (torsor round-trip), and the
two-rulers coherence. Written today, these are *separate* AxiomTests with separate scaffolding.
Read through the principle, **they are all `πᵢ(x) ≈ πⱼ(x)` — coherence of projections of one
source.** Make the projected-registry and projected-outcome abstractions explicit and a
meaningful fraction of AxiomTests becomes *one parameterized coherence law* instantiated at
different sources. The system's correctness claims stop being a list you maintain and become a
single theorem you apply. This is the deepest available simplification, and it is downstream of
nothing but *naming the principle* — the tests collapse because the objects they test become
instances of one shape.

**(b) `Optics.fs` is the projection engine, currently aimed at one source.** The lens vocabulary
exists for nested IR updates. Every keystone above asks for "a view from a single source" — which
is a lens. Re-aiming optics at registries and outcomes (not only IR) gives the whole tree one
answer to "how do I derive a view," and makes the projections *composable* in the way lenses
compose. The principle does not require new machinery; it requires pointing existing machinery
at all the sources instead of one.

**(c) `Ingest` becomes structurally pure, and the adjunction becomes architecturally
symmetric.** This is restated from Keystone III because it is the crown consequence. The
soul-equation is true today about values; after the membrane it is true about locations. The two
halves of the engine — `Project` (Core → typed stream → realize) and `Ingest` (discover → evidence
→ Core derive) — become mirror images: thin boundary, pure Core, on each side. An engine whose
two adjoint legs have *identical architecture* is the aerodynamic end state the question was
reaching for. The drag today is that `Ingest` looks nothing like `Project` even though the
algebra says they are inverse; removing that asymmetry is the literal meaning of "more
aerodynamic."

---

## 6 — The meta-finding and the guardrail

**The meta-finding: your duplication detector is intra-module; your worst duplication is
inter-module.** The codebase has a sound instinct — adopt the house device (builder, active
pattern, private-constructor module) at N≥3 recurrences. It fired for the pass chain and for the
analytics-family event epilogue. It did *not* fire for the binders, the codecs, the boundary
derivations, the refusal channels, or the driver bracket — even though each is at N≥3 — because
**the recurrence is spread across modules, and the trigger only trips when the repetition is
visible within one file.** The single most useful process change implied by the recon is to give
the N≥3 instinct a *cross-module eye*: a periodic sweep for "the same shape authored in parallel
in K modules" (the `bindError` copies, the `addNeighbor` copies, the `percentile` copies, the
`N'…'`-escape copies). That sweep is the detector that would have surfaced all five keystones
years earlier.

**The guardrail: this must not become symmetry for its own sake.** `CRYSTALLINE_FORM`'s warning
about *false symmetry*, and the standing law "primitives at the second consumer; IR grows under
evidence; verbs at the second consumer; dead-algebra retirement" (the 2026-06-04 deletion
precedent), are the brake. The test for every keystone is not "is it elegant" but "does it have
≥2–3 real consumers *today*." Each keystone passes that test in its S/M form — and each has an XL
form that *fails* it and must wait:

- Registry: build the binder core (9 consumers); **defer** `ConfigAxis`-as-DU and the full
  registry-as-data fold to their second fold site.
- Codec: route the reverse leg through existing builders (4 consumers); **defer** speculative new
  ScriptDom builders until a site demands each one.
- Membrane: extract `ProfileDerivation` (pure, testable now); **defer** the full `Profiler` port
  until the synthetic profiler needs it; keep `drainRows` a named refusal until the third
  consumer.
- Outcome: unify descent + exit registry (multiple consumers); **defer** an `Outcome` DU
  replacing `ValidationError` wholesale.
- Bracket: build the combinator (~30 consumers); **defer** the one-module-per-verb split.

The principle says *prefer projected objects*; the discipline says *only where a second view
already exists*. A keystone built before its second consumer is exactly the dead symmetry the
codebase has earned the right to delete. The vision is ambitious; the build order is
conservative; both are correct.

---

## 7 — Sequencing and the dependency graph

The keystones are not equal in fan-out, and two of them unlock the others. The recommended
order:

**First wave — the two high-fan-out keystones.** Build *Keystone I (Registry)* and *Keystone II
(Codec)* first. The registry's abstraction makes #9, #23, and half of #4 nearly free; the
codec's consolidation makes #1, #21, the RemediationEmitter, and #25's guards into "route
through the one surface" rather than independent work, *and* it is the precondition for the T12
proof on the reverse leg, so it has algebraic urgency the others lack. These two have the most
arrows pointing out of them.

**Second wave — the symmetry keystone.** *Keystone III (Membrane)* with its enforcement
*(#6, the typed-tree analyzer)* built alongside, because the analyzer is what *proves* the
membrane holds. This is the wave that restores adjunction symmetry — the crown consequence — and
it depends on nothing in the first wave, so it can run in parallel with a second team if one
exists. Order within it: `ProfileDerivation` and `parseSemanticType` extractions first (pure,
testable immediately), the `Profiler` port deferred.

**Third wave — the unifications that ride the first two.** *Keystone IV (Outcome)* naturally
follows the registry (its exit-code and descent registries are registry instances) and the codec
(its diagnostic/ledger projections are now consistent). *Keystone V (Bracket)* can land any time
— it depends on nothing — but is best done after the registry, because the Face's exit
classification *is* a registry projection (#9), so building the bracket after the registry lets
the Face consume the projection instead of re-deriving it.

**The throughline of the order:** build the two keystones with the most downstream unlocks
(Registry, Codec) first; build the symmetry keystone (Membrane + analyzer) for the deepest
algebraic payoff; let the two unifications (Outcome, Bracket) ride the abstractions the first
waves create. Each wave is shippable on its own and leaves the system strictly more coherent than
it found it.

---

## 8 — Coda: what the engine looks like when this is done

Picture the tower with the principle applied at every altitude. At the top, the adjunction — one
model-state, two adjoint projections, coherence the law — unchanged, because it was always the
exemplar. One level down, the two rulers and the torsor — one displacement, two measures —
unchanged. Then, newly: the pass chain joined by *every* registry as a projected object; the
typed statement stream as the *sole* path to SQL bytes, on the reverse leg as much as the
forward; every adapter a thin `Discover` over a pure `Derive`, so `Ingest` mirrors `Project`;
every refusal one point projected onto agreeing channels; every verb the same spine. And beneath
all of it, the property-test corpus contracted from a list of separate laws into *one* coherence
theorem applied at each source.

The system becomes **self-similar**: the move that makes the adjunction true is the same move
that makes the binders, the codecs, the boundaries, the refusals, and the drivers true, all the
way down. That self-similarity is the aerodynamics. Drag, in an architecture, is the energy spent
holding parallel things in agreement by hand — the binder you must remember to register, the exit
code you must remember to classify, the Voice copy you must remember to update, the reverse-leg
SQL you must remember to escape. Each is a place where two views are authored in parallel and a
human is the coherence law. The Projection Principle replaces the human with the type system as
the coherence law, surface by surface, until the only things authored once are the *sources*, and
everything else is a projection that *cannot* drift.

The engine already knew this. It proved it at the top and at one surface in the middle. The work
is to believe its own best idea all the way down.

---

*Companion to `REFACTOR_RECON_2026_06_25.md` (the 25 findings) and indexed by the disciplines in
`CLAUDE.md` / `AXIOMS.md` / `THE_USE_CASE_ONTOLOGY.md`. Proposes A45 (the membrane law) as a
candidate only. No code was changed in the writing of this document.*
