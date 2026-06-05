# THE INSTRUMENT — standing inside a system that proves itself

**The future-state vision of the dynamic display.** Where we're going: an
operator who stands inside a system that *knows the truth about its own schema
fidelity*, and navigates that truth — across time, across depth, across every
kind of change — at the speed of thought. Grounded in the change calculus (the
seven moves, the torsor that makes change an algebra) and the operator's own
workflows (the nine proteins and the laws they obey). **Calm by default,
infinite on demand, theirs.**

This is the vision, not the ledger. It describes the instrument as it will feel
to wield — the experience of an export you can trust because you can *see* why
to trust it.

---

## The thesis — the display is where the engine's self-knowledge becomes the operator's intuition

The engine already proves things about itself. Fidelity is not a hope here; it
is a theorem the engine checks continuously — the commuting square that says a
change computed in the model and realized on the substrate land at the same
point; CDC as the literal ruler of how much moved; the round-trip that says what
went in comes back. The engine *knows*.

The instrument is the surface where that knowing becomes the operator's. It does
not show data — it shows the **schema's life, made legible**: every change for
exactly what it is, every state reachable and re-proven, every claim backed by
the evidence that earns it. One substrate (the truth the engine computes), many
lenses (the ways the operator looks). The lenses can never disagree, because
they are projections of one truth — so the operator never wonders whether the
pretty view and the machine view are telling different stories. They are the
same story, seen at different distances.

When it is done, the operator opens the instrument and is **OK in one glance**.
Then they lean in, and the truth opens beneath them — exactly as deep as the
question they're asking, and no deeper. That is the whole art: a surface that
stays calm while the depth stays infinite.

---

## Two readings — confidence without the vocabulary

The deepest thing about the instrument is *who it is for*. It rests on a serious
architecture — a change calculus, an algebra of fidelity, a pure functional core
— and **the developer using it needs to know none of that.** They do not need to
know why it's F#, what a torsor is, or what the faithfulness ladder measures.
They need to know three things: *Is it safe? Did it work? What do I do next?* —
and they need to feel **confident** going in and **satisfied** coming out.

So every surface speaks two languages at once, over the same truth:

- **The plain reading** — for the developer who just wants to ship. *"Your
  rename kept all 2.1M rows. Nothing else changed. Safe to ship."* Human words,
  human stakes. No vocabulary required, ever.
- **The deep reading** — one keypress beneath it, for whoever wants it. The same
  fact in the language of the calculus: `‖rename‖_data = 0`, CDC captures 0, the
  commuting square holds, residual ∅.

They are the *same truth*: the deep reading is what makes the plain reading true,
and the plain reading is the deep reading made kind. The newcomer never needs the
second to trust the first. The expert never finds the first a lie. And a
developer who arrives uninitiated and grows curious becomes initiated **on their
own terms, one keypress at a time** — the architecture rewards understanding
without ever demanding it.

The measure of success is emotional, not technical: *a developer who does not
understand the engine ships a schema change, and feels safe doing it.* The whole
apparatus exists to earn that feeling — and then to get out of the way.

---

## The space — three axes the operator moves through

The operator is always *somewhere* inside the schema's truth, and moving is
free. There are three axes, and the change calculus hands them to us directly:

- **Time — the living timeline.** Not a log; a *shape*. Past runs and episodes
  are points; the present materializes; the line bends toward the **R6 gate**
  (the streak of green canaries that earns the cutover) and, beyond it, toward
  the **eject** — the freeze, where the model stops being OutSystems-derived and
  becomes the operator's own, forever. The timeline is a **lattice**: the same
  estate at different rhythm-points — Dev, Qa, Uat — across versions. The
  operator walks it on both axes, and the shape itself tells them where they
  are in the long arc to cutover.

- **Depth — the question, answered exactly as deep as it's asked.** verdict →
  module → kind → attribute → decision → evidence. One level per breath. The
  operator never faces a wall of a thousand findings; they face a calm verdict,
  and beneath it, on demand, the entire provenance of a single node — every
  transform that touched it, every decision, the profile evidence that drove it,
  the fix that would change it.

- **Move — the kind of change.** The seven channels of the calculus. The
  operator never sees an undifferentiated "diff." They see change *typed* — and
  because the channels are provably disjoint, they can hold any one in focus
  while the rest recede.

And a fourth thing, not an axis but a *lens you look through*: the **comparison
regime** — am I looking at the change since last sprint (history), the
divergence of what's deployed from what I meant (reconcile-live), or the cost of
a migration I haven't run yet (preview)? The same surface, three ways of
looking.

---

## Change, made legible — the seven moves, each its own way of seeing

This is the heart of the instrument. Change is not a blob of edits; it is seven
distinct *moves*, and each is a way of seeing. To navigate a change is to see it
resolve into these channels, each rendered in the language that move deserves:

- **Add** — a new thing appears. The operator sees the newly-minted identity and
  where it lands, and — for data — the proof that *exactly one* row entered.
  Additive, safe, faithful.

- **Remove** — a thing is destroyed. This is the most inspection-hungry move,
  and the instrument treats it with the gravity it deserves: it shows the
  **blast radius** — what would be annihilated, the pages deallocated, the scope
  of any deletion — *before* the operator consents. Nothing destructive ever
  happens silently.

- **Rename** — the name changes, the identity does not. The instrument shows the
  **identity-survival trail**, `old → new`, with the identity proven constant
  across the boundary — and, the detail that makes it sing, the proof that the
  rename moved **zero data** (`‖rename‖_data = 0`, the pages preserved). A
  faithful rename is one the operator can *see* kept every byte. A rename that
  lit the data counter would be a destroy-and-recreate wearing a rename's name —
  and the instrument would catch it instantly.

- **Reshape** — a facet changes (a type, a length, a nullability). The operator
  sees the **before → after**, classed by faithfulness at a glance: a
  metadata-only widening (instant, green), a narrowing that rewrites every row
  (amber, with the data-compatibility check), or a change the engine refuses
  (red). They know *which* before they run it.

- **Reidentify** — the same logical entity, re-keyed across substrates (Dev's
  `Id=280` becomes Uat's `Id=18`). The operator sees the **surrogate-remap
  table** and the **FK re-point trail** — proof that every relationship survived
  the re-key, matched on business identity, not on the surrogate that was always
  going to differ.

- **Move** — data flows between substrates, identity-reconciled. The operator
  watches the **ordering plan** (what loads before what), the two-phase boundary
  where deferred FKs resolve, and the running count as the data lands.

- **Accumulate** — history itself grows. Each episode appends; the refactorlog
  is never deleted; the past stays replayable. This is the move *over time* — the
  axis the whole instrument scrolls — and it carries the operator's single most
  honest metric: **churn**, the gap between the work done (every move made) and
  the distance moved (the net displacement). "Forty-seven moves, netted twelve"
  tells the operator something no count of edits ever could.

The operator who learns to *read in moves* stops seeing a diff and starts seeing
intention — what the schema was trying to become.

---

## The journeys — the operator walks any workflow at their own tempo

Every real workflow the operator lives — laying down a baseline, promoting to
UAT with a user re-key, evolving a live database, publishing the delta to the
SSIS team, ejecting to the frozen artifact, catching drift, running the
self-check — is the *same rhythm*, walked at the operator's pace:

> **Snapshot → Diff → Gate → realize, in dependency order → Measure → Verify → Record**

Two moments in that rhythm are where the operator *lives*, and the instrument
makes both first-class:

- **The Gate — the moment of consent.** Before anything is written, the operator
  faces the change and decides: proceed, declare the loss, or stop. The
  instrument lays the decision out — the blast radius, the named loss, the exact
  consequence — and *will not move* until the operator clears it. Destructive
  change requires a human looking at what will be destroyed and saying yes. The
  re-key's gate will not clear until every orphaned user is accounted for. The
  eject's gate will not clear until the entire provenance chain reconstructs.
  The gate is where trust is earned.

- **The Measure / Verify — the proof.** After the change lands, the operator
  *watches it be true*: the CDC count settling at the minimum (and at *zero* on
  an idempotent redeploy), the read-back meeting the model, the residual
  surfaced and named. They don't take "done" on faith; they see it.

Walk a few, and feel the shape:

- **The load.** Lay down the baseline; watch the data settle; redeploy and watch
  the CDC count fall to **zero** — the green hush of a system that knows nothing
  changed.
- **The UAT re-key.** The user-map is laid out before a single row moves — every
  match by email, every manual override, every orphan flagged. The operator
  walks it, resolves the orphans, and only then does the gate clear and the
  promotion run.
- **The migrate.** Preview the touches, channel by channel. Consent at the gate.
  Watch it land in dependency order. See the read-back reproduce the target,
  the seeded data preserved, the re-run idempotent.
- **The eject.** The last gate. The whole history reconstructs from genesis,
  re-verified, before the freeze. The operator hands off a self-contained
  artifact whose provenance they have *seen* be complete.

---

## Fidelity you can SEE — the laws made visible

This is the soul of the instrument, and the thing no ordinary tool offers: the
operator does not *trust* the engine — they *watch it prove itself*. The laws of
the calculus are not footnotes; they are the surfaces:

- **The commuting square.** Compute the change in the model; realize it on the
  substrate; the instrument shows the two legs *meeting*, residual surfaced.
  This single picture is the whole faithfulness claim — and it is something the
  operator can look at.

- **CDC-silence — the green hush.** Redeploy a model that's already correct and
  the instrument shows you *nothing moved* — and shows you the nothing is
  **real**, not a dead check. The absence is verified. This is the highest-stakes
  guarantee in the system, and it has a face.

- **`‖rename‖_data = 0`.** A rename that kept every page, shown as a proof, not
  a promise.

- **The ladder.** "Where am I?" answered in the terminal, per axis — schema,
  data, identity, diagnostics, provenance — each at its rung (L1 witnessed → L2
  faithful → L3 composed), with the **one lever** to climb named outright. The
  most trust-relevant thing the system knows about itself becomes something the
  operator can simply *ask* and *see*.

- **The intent filter.** Every difference the instrument shows is either
  **intended** (the engine will emit it) or **tolerated** (substrate noise — an
  auto-named constraint, an empty-string-versus-null, a collation quirk) — and
  the tolerated bucket is **itemized, never hidden**. Silence on neither. The
  operator is never left wondering whether a difference was swallowed.

This is the difference between a tool that says "done" and an instrument that
shows you *why "done" is true* — and lets you disbelieve it until you've seen the
proof. Fidelity stops being a marketing word and becomes something you watched
happen.

---

## The timeline — the schema's whole life, in one shape

Step back, and the whole arc is one image. The lattice of episodes across
environments and versions. The **churn** beside the **net** — the work done
beside the distance moved. The streak of green canaries filling toward the R6
gate. The trajectory bending toward the eject. And the past is not a dead log:
every historical state is **replayable and re-verified the moment the operator
lands on it** — so they can scrub the timeline and *trust every frame*, because
each one re-proves itself on arrival. The CDC capture log is the history *and*
the proof of minimality, the same construct one plane apart from the
refactorlog. The schema's whole life — every move it ever made, every state it
ever held — sits in one navigable shape the operator can walk from genesis to
freeze.

---

## The texture — the surfaces, drawn

Illustrative sketches (glyphs are the `Theme` tokens — `✓ ▲ ✕ ○ → · ●`, the
`▇░` meter; every color rides a glyph). Not pixel-final — they exist so the
*feel* is concrete.

**Change, typed — what the operator sees instead of a diff:**

```
  @run-9 → @run-10                                ‖δ‖ 14 · net 12 · churn 2
  ───────────────────────────────────────────────────────────────────────
  ⟲ rename   2                                                    ↩ green
     → OrderHeader        → SalesOrder    · ‖rename‖_data 0 ✓  (pages kept)
     → OrderHeader.Cust   → CustomerId    · ‖rename‖_data 0 ✓
  ≠ reshape  3                                                    ↩ amber
     ~ Customer.Email   nvarchar(100)→(256)   widen  · meta-only ✓
     ~ Order.Status     nvarchar(50)→(20)     NARROW · ▲ rewrite + data-check
  + add      6                                                    ↩ green
     + Invoice (table) 4 attrs  · + Order.DiscountPct · …4 more   ↓
  − remove   1                                                    ✕ gate
     − IX_Order_Stale (index)   destroys structure — confirm at the gate
  ───────────────────────────────────────────────────────────────────────
  ⊘ tolerated 3   DacFx auto-names · ANSI pad · collation       (itemized →)
  ↑↓ walk · → evidence · / one move · g the gate · ← → across runs
```

**The gate — the moment of consent:**

```
  ▲ GATE — this change destroys structure         migrate → @uat
  ───────────────────────────────────────────────────────────────────────
   ✕ remove  index  IX_Order_Stale
        blast radius  → 1 index dropped (pages deallocated)
                      → 0 rows touched · 0 downstream FKs
   the other 13 moves are safe (2 rename · 3 reshape · 6 add · 2 insert).

   declare:  --declare-loss IX_Order_Stale   ·   --declare-all
  ───────────────────────────────────────────────────────────────────────
  d declare this · a declare all · q abort
```

**The ladder — "where am I, and what's the one lever":**

```
  cutover ladder                                 UAT · v12
  ───────────────────────────────────────────────────────────────────────
  R6   ▇▇▇▇▇▇▇░░░  7/10 green        ✕ run 5 broke the streak
  ───────────────────────────────────────────────────────────────────────
   axis           L1  L2  L3    open
   schema         ✓   ✓   ✓     —
   data · CDC     ✓   ✓   ✓     —
   identity       ✓   ✓   ▲     by-email fallback on 3 users        ↓
   diagnostics    ✓   ▲   ○     IndexOptionsUnreflected · tol#7     ↓
   provenance     ✓   ✓   ✓     —
  ───────────────────────────────────────────────────────────────────────
  → the one lever to L3: close the identity by-email fallback (3 users)
```

**The user-map — every identity accounted for before a row moves:**

```
  user-map · QA → UAT                          halts before any SQL
  ───────────────────────────────────────────────────────────────────────
  142 users   ✓ 138 by-email   ● 1 manual   ▲ 3 unmapped
   ✓ jdoe@corp        QA 280 → UAT 18      re-key (Update, keyed on email)
   ● legacy_svc       QA 14  → UAT 7       manual override
   ▲ tmp_import_99    QA 991 → ?           unmapped — FallbackToSystemUser?
  ───────────────────────────────────────────────────────────────────────
  the gate will not clear — and no SQL runs — until every orphan is mapped.
  ↑↓ walk · → FK re-point trail · m manual-map · f fallback · g clear gate
```

---

## Storyboard — a first change, by someone who's never seen this

Meet a developer who has never opened this, doesn't know it's written in F#, and
has a schema change to ship. She is a little nervous. Watch the instrument carry
her — plain words, the depth waiting quietly underneath.

**1 · It starts, and it talks like a person.**
```
  Working on your change…
    ✓ Read your model — 312 tables
    ✓ Compared it to what's live
    ⣷ Working out what's safe to change…
```

**2 · The verdict, in one breath — plain confidence.**
```
  ✓  Safe to ship
     14 changes · 13 easily reversible · 1 worth a look · no data lost
     → look at the 1, or ship now
```
No jargon. She knows exactly where she stands and what to do next.

**3 · The one risky thing warns her — kindly, without assuming.**
```
  ▲  One change removes something
     You're dropping an index, "IX_Order_Stale".
       · it holds no data — nothing of yours is lost
       · but it can't be auto-undone, so I'm asking first
     ship it?   [y] yes   [n] skip it   [?] what's an index — and is this ok?
```
She doesn't know what an index is. `?` will tell her, plainly. The instrument
never makes her feel small for not knowing.

**4 · She ships, watches it happen — and it double-checks itself.**
```
  Shipping…
    ✓ Renamed 2 things        your data stayed exactly where it was
    ✓ Widened 3 columns       instant — no rewrite
    ✓ Added 6 new things
    ✓ Moved 4,210 rows
  ✓ Done — and I checked: your database now matches your model exactly.
```
It worked. It told her clearly. It *verified itself.* That is the satisfaction —
the quiet *"…huh, that was nice."*

**5 · Curiosity, rewarded on her terms (entirely optional).** She wonders *how do
you know it matches?* and presses `→`.
```
  I deployed your model to a clean copy, read it back, and compared them.
  Identical.                                    the proof ↓  (or leave it — your call)
  ‖δ‖ verified · commuting square holds · residual ∅ · CDC 4,210 = exactly the rows that changed
```
The depth was always there. She never needed it to trust the result — but now
she's a half-step more initiated, because she chose to be.

That arc — **nervous → clear → consenting → satisfied → a little curious** — is
the design target. Everything deep in the architecture exists to produce it for
someone who will never read this document.

---

## How it stays theirs to shape — the Voice layer

Built so it can be refined forever without disturbing the truth beneath. Three
layers, cleanly separable — the structural reason the Apple-edition polish can
keep going indefinitely:

- **Structure** (`View`) — *what* is shown: the verdict, the changeset, the gate,
  the timeline. One ADT; every surface a projection of it; the human and machine
  lenses can't drift because they're the same value.
- **Style** (`Theme`) — *how it looks*: glyphs, semantic color, meters, dots.
  Re-themeable wholesale; every signal survives a colorblind reader or `NO_COLOR`
  because color always rides a glyph.
- **Voice** — *how it speaks*: the register that turns the deep truth into the
  plain reading. "Your rename kept all your rows" and `‖rename‖_data = 0` are one
  structure, two voices.

Because the three are separate, the instrument is **tuned, not rebuilt**: dial
the Voice from *plain* to *expert* without touching the structure; re-theme for a
new house style; add a surface as a new `View` projection; re-word for a
different audience entirely — all without going near the engine that computes the
truth. The depth is fixed and proven; the **presentation stays soft** — the
operator's to shape as the product grows and new developers, of every level,
arrive. The surface can be refined a thousand times, and the truth underneath
never moves. *Scalable and adjustable* is not a hope here; it is the shape of the
three layers.

---

## The feeling — calm, deep, theirs

Open it: you are **OK in one glance** — the verdict, the streak, the one thing
that wants attention, and nothing else clamoring.

Lean in — press down — and the truth opens beneath you, **exactly as deep as the
question**. A module. A kind. A single attribute and the entire story of why it
decided what it decided. No wall. No noise. Just the next layer, when you ask
for it.

Move left, into the past, and every state **re-proves itself as you arrive** —
so the history is not a record you hope is right; it is a sequence you can trust
frame by frame.

And every surface, everywhere, **ends knowing the next move** — the lever to
pull, the gate to clear, the orphan to map. You are never told "done" and left
there; you are always shown what's true and what's next.

The instrument disappears. What remains is the schema's truth, and your hand on
it. You are not reading a report about an export. You are **standing inside a
system that already knows** — and now, so do you.

---

*Grounded in the change calculus (`WAVE_6_ONTOLOGY.md` — the seven moves;
`WAVE_6_ALGEBRA.md` — the torsor, the norm, the commuting square) and the
operator workflows (`THE_USE_CASE_ONTOLOGY.md` — the nine proteins, the ten-axis
matrix, the laws). The realized lenses are projections of the `View` substrate
(`DYNAMIC_DISPLAY.md` — the Glance / Watch / Explore form).*
