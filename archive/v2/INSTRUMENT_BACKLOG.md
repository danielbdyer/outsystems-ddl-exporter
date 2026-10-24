# INSTRUMENT_BACKLOG — storyboard → essence → architecture → build

From `THE_INSTRUMENT.md` (the vision) to executable slices. The spine: **the
newcomer is the power user — one essence, infinitely diggable.** Every Wave 6
use case is the *same surface*: a **change, gated and proven, rendered
statement-first**, the depth one level beneath. This doc storyboards the whole
ontology as that one surface, distills the abstraction it shares and the
specializations it takes, sequences the build, and starts slice 1.

---

## §1 The ontology, storyboarded as one surface

The shared surface has three parts: a **statement** (the plain lead line — *safe?
worked? what's next?*), a **substantiation** (the move-typed, proven depth, one keypress
down), in a tunable **voice** (plain ↔ formal). The whole ontology is that one
surface instantiated — the moves and proteins differ only in *what change* they
render; the shape is constant.

**The seven moves** — statement line (what everyone reads), proof beneath:

| move | statement (plain) | substantiation (proof) |
|---|---|---|
| Add | "new things, safely" | the minted identity · CDC +1 = one row in |
| Remove | "this destroys X — confirm" | the blast radius · the declared-loss gate |
| Rename | "renamed; your data stayed put" | `‖rename‖_data = 0` · the `old→new` identity trail |
| Reshape | "widened (instant)" / "narrows — I'll check first" | the before→after facet · faithfulness class |
| Reidentify | "re-keyed; all relationships kept" | the surrogate remap · the FK re-point trail |
| Move | "moved N rows, in the right order" | the dependency plan · the two-phase boundary |
| Accumulate | "here's what changed since last time" | the evolution chain · churn = work − distance |

**The nine proteins** — one rhythm (`Snapshot→Diff→Gate→realize→Measure→Verify→Record`),
differing in the change and the gate:

| protein | essence verdict | gate (plain consent) | proof (plain) |
|---|---|---|---|
| load (P1/P2) | "ready to load" | declared-loss | "CDC = 0 on redeploy" |
| UAT re-key (P3) | "ready, once 3 users are mapped" | validate-user-map (halts pre-SQL) | "matching report clean" |
| SSIS publish (P4) | "here's the delta for your team" | declare every drop | "replays from genesis" |
| idempotent (P5) | "nothing to do — and that's real" | intent-filter complete | "CDC 0, zero ALTERs" |
| migrate (P6) | "14 changes, 1 to confirm" | declared-loss + data-compat | "DB matches your model" |
| eject (P7) | "frozen, provenance complete" | provenance-completeness | "genesis → frozen verified" |
| drift (P8) | "deployed differs in 2 spots" | accept / remediate / escalate | "drift size per channel" |
| canary (P9) | "fidelity holds" | empty-diff else red | "round-trip = identity" |

> The laws ride the substantiation (the proof of any edge is T16: the model leg and the
> substrate leg meet, residual surfaced). The matrix *is* the ladder surface
> ("where am I on L1→L2→L3, and the one lever").

---

## §2 The essence, noticed

**Every Wave 6 use case is a *change*, *gated* (consent before destruction) and
*proven* (verified after), rendered *essence-first*.** So the display is not many
surfaces — it is **one** surface (statement + substantiation over a Change), specialized to
each change-shaped thing.

*Discriminating predicate:* a naive design writes a bespoke renderer per verb;
the essence-distilled design writes **one Surface** and specializes it — a new
verb becomes a new specialization, never a new renderer. (This is "one substrate,
many lenses" made architectural for the instrument.)

---

## §3 Dependencies · abstraction · specializations

**The abstraction — `Surface`:** a statement-first rendering of a
change — `essence` (the plain lead) + `dig` (the move-typed, proven depth) +
<!-- the `Surface.fs` fields are still named essence/dig; the statement/substantiation rename lands when Surface is next touched (out of the doc-vocabulary sweep) -->
`voice` (plain ↔ formal). Per the two-consumer discipline it is **extracted once
two specializations exist** — named now, built at slice 4 (not ahead of its
consumers).

**Depends on (all present or shipped — the foundation is ready):**

```
        View (structure) · Theme (style)
                  │
   ┌──────────────┼───────────────┬───────────────┐
 Ref.resolve   Comparison /     GateLabel /     RunHistory /
 (operands)    CatalogDiff      Preflight       EpisodicLifecycle
               (change)         (gate)          (timeline)
                  │                                   │
                  └────────── Surface ────────────────┘   + LogSink.addSubscriber (live, shipped)
                  (essence + dig + Voice — slice 4)        + Voice (essence-language — slice 4)
```

**Specializations — each a `Surface` over a different change:** Changeset (diff)
· Gate · Timeline · Ladder · User-map · the move-lenses (sub-parts of the
changeset's substantiation).

---

## §4 The backlog

Thin slices, one per commit, each with its dependency met and ≥2 real consumers.
The `Surface` abstraction is extracted at slice 4 — after its first two
specializations (1, 3) prove the shape.

| # | ships | kind | depends on | discriminating predicate | ≥2 consumers | acceptance |
|---|---|---|---|---|---|---|
| **1** | `diff <a> <b>` — change, essence-first | S1 | Ref + Comparison | destructive → ▲ essence; additive → ✓; empty → "identical" (naive: panel, no verdict) | diff · migrate-preview · Explore | leads with the verdict + the change; `--format json` falls out |
| **2** | move-typed lanes + reversibility/intent badges | S1+ | 1 + channelCounts | a Rename sits in the rename lane with `‖rename‖_data`; reversibility from `Comparison.Apply` | diff · drift · migrate | 7 lanes; each row badged ↩ + intended/tolerated |
| **3** | the Gate surface | S2 | GateLabel/Preflight | a Remove renders blast-radius + exit code as a walkable surface, not one error string | migrate · eject · drift | refusal renders as a Surface: plain consent + the substantiation |
| **4** | extract `Surface` + the Voice layer | abstraction | 1 + 3 | statement(plain) and substantiation(formal) are one value at two depths; voice-dial doesn't change structure | every surface | changeset + gate refactor onto Surface, output preserved; `--voice expert` flips register |
| **5** | the Timeline (`View.Timeline` + lattice) | S4 | RunHistory | present-marker + R6 streak land at the right (env,version) cell | Glance · Explore · readiness | strip/lattice renders; toJson carries verdicts+present+gate |
| **6** | the Explore navigator (`inspect <runId>`) | the substantiation, navigable | Surface + Timeline | `step` total + bounded-idempotent; `project` shallow→rollup, deep→+1 level | inspect · json-state | `inspect @run` opens statement-first; ↑↓ deeper, ←→ time |
| **7** | the Ladder surface | S3 | matrix/tolerances | "where am I" + the one lever named; per-axis L1/L2/L3 | ladder/readiness · inspect | in-terminal ladder View with the lever |
| **8** | the Watch renderer (live essence) | live | `addSubscriber` (shipped) | the essence fills live; channel-1 routing per §15 | full-export --pretty | live stages + final essence; piped = clean NDJSON |
| **9** | the User-map surface | S5 | reconciliation | re-key as one Update keyed on business id; orphans gate pre-write | migrate · transfer | user-map walkable; gate halts pre-SQL |

---

## §5 Begin — slice 1

**The changeset surface: `diff <refA> <refB>`, change rendered essence-first.**
It is the heart of the ontology (every protein diffs), it stands on `Ref` +
`Comparison` (ready), and it is the first instance of the statement/substantiation shape every
later surface reuses. Slice 1 lands the essence (the plain verdict that leads the
change) + the `diff` verb; slice 2 adds the move-typed lanes and the badges.

*Status: in flight (this commit).*
