# THE VOICE — the backlog (rendering the instrument's truth into the TTY)

**This is not a punch list. It is the third layer of a self-proving instrument, told as
work.** Before any file:line below means anything, hold the North Star — because a string
that passes every mechanical guard and still shows the operator the *apparatus* instead of
the *truth* is not done, and the guard cannot tell the difference. The whole reason this
work exists is in §0. Read it first; the waves are how it gets built.

> **The doc set, in one breath.** `NORTH_STAR.md` is *why the engine exists* (fidelity as a
> theorem it proves about itself). `THE_INSTRUMENT.md` is *what the operator experiences*
> (one essence, infinitely diggable; the newcomer is the power user). `THE_VOICE.md` is *the
> register* (the twelve rules — how the truth is said). `THE_STORYBOARD.md` is *the surface*
> (act × stream × outcome). `THE_CLI.md` is *the command shape* the voice speaks on (the
> **flows** surface). **This document is where those four become rendered TTY** — the
> prioritized, file:line-grounded work to make the instrument actually speak in register.

---

## 0 — The North Star (why a single raw line matters)

### 0.1 What the engine is

The engine is one **adjunction** between a logical Model and a physical Substrate
(`NORTH_STAR.md` §1):

```
Project : Model     ──►  Substrate     (emit)
Ingest  : Substrate ──►  Model         (read back)
Law     : Ingest ∘ Project = identity  (modulo named, declared erasures)
```

Everything the engine does is a corollary of that one law. The canary *is* the law at
runtime. Drift *is* the law's failure surfaced. A migration *is* the law applied to a delta.
And the deepest thing the engine knows about itself is that this round-trip is **faithful** —
not trusted, *proven*, continuously, about itself. The OutSystems estate's schema and data
**mean exactly what the model says they mean — provably, in both directions, on every axis,
across time.** Fidelity is not a property a human verifies. It is a theorem.

### 0.2 What the Voice is

A self-proving instrument is worth nothing if the operator cannot *read* the proof. So the
instrument has three cleanly-separable layers (`THE_INSTRUMENT.md`, "How it stays one
instrument"):

- **Structure** (`View`) — *what* is shown.
- **Style** (`Theme`) — *how it looks*.
- **Voice** — *how it speaks*: the register that renders the deep truth as the **essence** —
  the plain human finding a newcomer trusts on sight, with the formal proof one level
  beneath for whoever has the question.

**The Voice is this third layer.** Its whole job is to take the theorem — `residual ∅ ·
Ingest ∘ Project = id` — and say it as *"Verified. The database matches the model."* The
essence is the proof made kind; the proof is the essence made rigorous. They are one truth
at two depths, and **the surface never changes between the newcomer and the master — only
the velocity** (`THE_VOICE.md` §0: "they are the same reader at two velocities").

### 0.3 Why this is the work, and why it matters that it's done *right*

Here is the recognition that makes the register non-negotiable: **the instrument is supposed
to disappear.** "What remains is the schema's truth and the operator's hand on it… You are
not reading a report about an export. You are standing inside a system that already knows"
(`THE_INSTRUMENT.md`, closing). Every place the rendered TTY still shows a raw `%A` dump, a
`norm=` lead, a `REFUSED` shout, a leaked exit code on the statement line — **is a seam where
the instrument fails to disappear.** The apparatus shows through. The operator stops reading
the truth and starts reading the tool. That is the precise failure this backlog closes.

So when §1 below lists a `sprintf "%A"` at `Program.fs:1167`, that is not a lint finding. It
is the migration's *verdict* — the answer to "is it safe? did it work?" — rendered as an
internal DU dump, at the exact moment the operator most needs the essence. The mechanical
fix (route it through `Voice.errorsSurface`) is trivial; what it *restores* is the instrument
disappearing. Hold that distinction through every wave. **The file:line is where; the North
Star is why; the register is how.**

---

## 1 — What "done" means (the bar the guard cannot see)

The `VoiceTotalityTests` banned-list guard is **necessary and not sufficient.** It catches a
pronoun, a shout, a `%A`. It cannot tell you whether the line is *true*, *grounded*, or
*kind*. A line is done when all of these hold — derive them from `THE_VOICE.md`, not from
this paraphrase:

1. **One register, two velocities** (§0). The newcomer reads it as orientation; the master
   reads the *same line* as a glance already past. Never a beginner mode, never an expert
   dump. If it serves only one reader, it is not done.
2. **Essence on top, proof one level beneath** (§3/§6/§10). The lead is a complete plain
   finding, readable aloud with no prior context. The notation — `‖δ‖`, exit codes, the
   commuting square, the gate label — lives in the substantiation, reachable, never on the
   statement line.
3. **It states the finding with grounded authority** (§4 rule 4 + §8 rule 8). What the engine
   *proves* — safe, reversible, idempotent, matched — is asserted plainly; the proof beneath
   earns it. Hedge only on genuine interpretation (a recommendation), never on a theorem.
4. **It ends on the move** (§1 "End on the move"). The lever, the gate, the unmatched record,
   the next command. Nothing terminates at "done"; where nothing remains, the surface *says*
   that.
5. **The instrument disappears.** No internal vocabulary (`Kind`, `SsKey`, `%A`, a file path
   where a name belongs), no apparatus. The operator sees `Country`, not `OS_KIND_Country`;
   the schema's truth, not the tool's plumbing.

**The felt arc is the target, not the string** (`THE_VOICE.md` §7). The nine workflows each
have a composed shape the operator moves through — *alert → consenting → verified → trusting*
for an in-place migrate; *quiet confidence, or one clear divergence* for a round-trip check.
When you voice a surface, you are placing one frame in that arc. Ask what the operator should
*feel* one step before and one step after, and whether your line carries them. A string that
is banned-list-clean but lands the operator nowhere in the arc is mechanically correct and
substantively unfinished.

> **The acceptance question for every item below is not "does the guard pass?" It is: would
> the newcomer trust this on sight, and would the master read it as a glance — and does the
> instrument disappear behind it?** The guard is the floor. This is the bar.

---

## 2 — The state of play (what speaks, and what does not)

The Voice **machinery is built and proven** (slices 1/2/4 + the gate/error surfaces, all
totality-tested). The Voice **surface is mostly unwired** — which is to say the instrument
*can* speak but mostly hasn't been given the words at the render sites. Six sink families
render in register today; almost every executor's success narration and nearly every refusal
is raw prose authored inline, and the sharpest single fact is that `Voice.gateSurface` /
`TtyRenderer.renderGate` — total over the closed `Preflight.GateLabel` DU, banned-list-tested
— sits **uncalled** beside every refusal that hand-writes its own string.

### What is VOICED today (the whole list)

| Site | `Program.fs` | Renderer | The arc frame it serves |
|---|---|---|---|
| Terminal summary panel (the verdict) | `withRun` → `:176` | `TtyRenderer.renderSummary` | §3 verdict — "did it work?" |
| Readiness board (the §8 ladder) | `runReadiness` → `:942` | `renderReadinessBoard` | §8 "where am I, one item in the way" |
| Catalog-diff answer (`explain` diff) | `runDiff` → `:983` | `renderAnswer (Comparison.renderCatalogChange)` | §4 the moves, essence-first |
| Refused plan action | `:1770` | `renderVoicedError` | §10 saying no with candor |
| Model-resolution / argv parse errors | `:1708`, `:1869` | `renderVoicedError` | §14 setup as a choice, not a failure |
| Every `printErrors` **body** | `:96-97` | `renderErrorsTo` → `Voice.errorsSurface` | §10 the located cause |

> The asymmetry on that last row is itself a tell: the **body** is voiced, but ~25 call sites
> still print a raw `Console.Error.WriteLine "… failed:"` **header** above it — a raw shout
> leading a voiced finding. The apparatus, bolted onto the essence. Wave 5 removes it.

### The renderers that exist and wait (the instrument's words, unspoken)

Built, tested, **uncalled** (or under-called) — wiring them is rendering work, no new
infrastructure:

- **`Voice.gateSurface` / `TtyRenderer.renderGate`** (`TtyRenderer.fs:164-180`) — total over
  all nine `Preflight.GateLabel`s. This is the **consent moment** (§5 / `THE_INSTRUMENT.md`
  "The Gate"): trust earned in plain words. The drop-in for every refusal in Wave 1.
- **`Voice.errorsSurface` / `renderErrorsTo`** (`Voice.fs`, `TtyRenderer.fs:193`) — the
  located §10 refusal; reusable for the raw `%A` error tails.
- **`Comparison.renderCatalogDiff` / `renderCatalogChange`** (`Comparison.fs:36/122`) — the
  statement-first per-channel `‖δ‖` panel, proven by `runDiff`; the substrate for the §9
  minimality proof (Wave 2).
- **`Surface.render`** (`Surface.fs`) — the Statement → Substantiation → Action assembly (the
  essence/proof/move shape) every new surface composes through.

---

## 3 — The waves (each is a place the instrument learns to disappear)

Ordered highest-value first; each independently shippable, pure pool green at each step. Every
wave names the **arc frame** it restores, then the mechanics.

### Wave 0 — Stop the live breaches (the apparatus showing through, today)

**Why.** These strings are on the operator surface *right now*, and each is a seam where the
instrument fails to disappear at the worst moment — the verdict rendered as a DU dump, the
proof rendered as a shout. Smallest fixes, highest integrity cost: a shipped instrument that
dumps `%A` at its verdict undercuts the entire claim that it "already knows."

**0.1 — The four `%A` raw-DU dumps on the statement line** (§2.2 "leaked internals"). Each is
a verdict or error rendered as engine internals.

| Site | `Program.fs` | Raw | Fix |
|---|---|---|---|
| `reportPreviewOutcome` `other` | `:1167` | `sprintf "… failed: %A" other` | route the `MigrationError` through `Voice.errorsSurface` (Wave 2 folds it in) |
| `reportMigrationError` `other` | `:1279` | `sprintf "… failed: %A" other` | `Voice.errorFrame` / `errorsSurface` |
| `runApprove` store errors | `:575`, `:579` | `Error e -> sprintf "%A" e` | typed `toView`, or `errorSurface` if it carries a code |
| `ExplainRegistry` listing | `:1752-1753` | `sprintf "%A" rt.StageBinding` | a typed `StageBinding → string` operator label |

**0.2 — The four system-shout leads** (§2.2 — `REFUSED`/`FAILED` as a lead). The proof is a
§6 finding spoken as meaning, never an all-caps tag.

| Site | `Program.fs` | Raw lead | Fix |
|---|---|---|---|
| `runCanary` RED | `:460` | `"canary RED"` | `Voice.canary.divergence` (§6 proof — exists) |
| `runCanaryCdcSilence` RED | `:524` | `"canary RED"` | same §6 proof copy |
| `runDrift` | `:897` | `"DRIFT DETECTED"` | a §6/§8 drift statement (Wave 3) |
| migrate verify / eject self-verify | `:1409`,`:1426`,`:1575`,`:923` | `"verification FAILED"` / `"FAILED self-verification"` | a §6 verify-proof statement (Wave 3) |

**0.3 — The three lead-with-a-code lines** (§10 — the code rides beneath, never leads).
`narrateIntegrityReport` warnings (`:802`, `"%s: %s" w.Code w.Message`); inexpressible-ALTER
(`:1164`, `:1264`, `"[%s] %s"`); `runExplain` findings (`:1037`). Statement first; code in the
substantiation.

**Done when:** no `%A`, no shout lead, no code lead on any operator statement; the banned-list
guard's scanned set is extended to the new copy; and the verdict/proof each reads as a finding
a newcomer trusts.

### Wave 1 — Spend the renderer that's idle: voice the gates (highest leverage)

**Why — this is the consent moment.** A gate is the instrument *stopping to ask* before it
writes (`THE_VOICE.md` §5; `THE_INSTRUMENT.md` "The Gate — consent, in plain words"). It is
where trust is earned in plain words, identically for the newcomer and the master: *state the
consequence as meaning, name the one lever, hand over the imperative, and wait.* Today every
gate hand-writes a raw refusal beside `Voice.gateSurface` — a renderer that is **total over
the closed `Preflight.GateLabel` DU**, banned-list-tested, and **never called**. The
instrument's most carefully-designed sentence is sitting unspoken. `Preflight.classify`
already maps a refusal code → `GateLabel`; wiring is a drop-in.

| Refusal site | `Program.fs` | Today | Gate label |
|---|---|---|---|
| `migratePreflights` connection / permission | `:1290`,`:1297`,`:1303` | raw | `ConnectionUnavailable` / `InsufficientGrant` |
| `tighteningPreflight` (NOT-NULL on NULL data) | `:1330` | raw | `DataViolatesTightening` |
| `reportMigrationError` `RefusedByCdc` | `:1267` | raw | `CdcTrackedSink` |
| `reportMigrationError` `RefusedByTightening` | `:1271` | raw | `DataViolatesTightening` |
| `reportMigrationError` `SchemaReadFailed` | `:1275` | raw | `SchemaReadFailed` |
| undeclared-drop refusals | `:1156-1160`,`:1258-1260` | raw, lists `Migration.lossToken` | `UndeclaredDestructiveChange` |
| `--allow-drops` token (transfer / synthetic) | `:742-745`,`:1659-1662` | raw | `UndeclaredDestructiveChange` |

**The one gap inside this wave — the intent gate has no `GateLabel`.** The `--go` /
`PROJECTION_ALLOW_EXECUTE` consent refusals (`:695`,`:1346`,`:1447`,`:1636`) are the §5/§7
two-gate model — `--go` states intent, `ALLOW_EXECUTE` arms the live write — but have no
`Preflight.GateLabel` variant. Two ways (record the choice in `DECISIONS`): add a
`Preflight.IntentNotStated` variant (keeps closed-DU totality covering it), **or** voice it
through a flat `gate.intent` `errorSurface` code. Recommendation: the flat code — the intent
gate is a CLI-surface consent concern, not an engine pre-flight, so it need not enter the
engine's closed DU.

**Done when:** every refusal renders through `renderGate` (or `errorSurface` for the intent
gate); no raw refusal prose remains in the migrate/transfer/synthetic paths; each gate states
the consequence as *meaning* and hands over one plain imperative (*Approve · Grant · Map ·
Trim · Allow · halt*); the gate⇔copy totality still passes; exit codes unchanged.

### Wave 2 — The §9 ‖δ‖ minimality proof (the soul, said plain)

**Why — this is the deepest thing the engine knows, rendered as `norm=`.** Minimality — "the
touch was exactly what differed, and no others" — is the §6 proof that the migration is
faithful (`THE_VOICE.md` §6: *"312 rows changed — exactly those that differed, and no others.
‖δ‖ = 312 = CDC capture count"*). It is surfaced in three places, **all raw, all leading with
`norm=` or bare counts** — symbolic shorthand on the statement (§1 rule 3 / §2.2), the proof
shown *as* notation instead of *as* a finding. The substrate to fix it is already proven:
`runDiff` renders the per-channel `‖δ‖` panel statement-first through
`Comparison.renderCatalogDiff`. The shape to build mirrors it exactly — a `Surface` whose
*statement* is the Minimality finding, whose *substantiation* is the `‖δ‖ = norm = CDC count`
panel, whose *action* is the apply imperative. The idempotent case is the §6 "already true"
silence: *"Nothing to apply. The database is provably unchanged."*

| Surface | Site | Raw |
|---|---|---|
| Migrate dry-run preview | `reportPreviewOutcome` `:1171-1186` | `"minimum-viable touches (norm): %d"` + per-channel counts |
| `report` bundle | `ReportRun.render` (`src/Projection.Pipeline/ReportRun.fs:49-61`) | `"norm=%d (+%d / −%d / ~%d kinds; %d CDC capture(s))"` |
| Transfer / migrate-with-data CDC counts | `narrateTransferReport` + `:311`,`:1421`,`:1559-1562` | `%d CDC capture(s) measured` in prose |

Fold the two `%A` error tails (Wave 0.1: `:1167`,`:1279`) in here — same two functions. And
correct the one nested register miss: `Comparison.catalogStatement` (`Comparison.fs:66`) still
says `"destroy structure"` (a §5 true-verb miss — *drops/narrows*, not drama) inside the
already-voiced `runDiff`.

**Done when:** the preview and report lead with the Minimality finding, not `norm=`; the norm
rides in the substantiation; `runDiff`'s "destroy structure" is corrected; `ComparisonTests`
copy assertions updated.

### Wave 3 — The §6 proofs: canary, drift, data-integrity (fidelity, made plain)

**Why — this is the soul of the whole instrument** (`THE_VOICE.md` §6: "This is the soul").
The fidelity proofs are where *"It worked, and I checked it matches"* is said to everyone as
plain confidence, the formal proof one dig beneath. The **structured** canary verdict is
already voiced (`canary.diffEmpty`/`canary.divergence`, read by the `withRun` panel) — but the
**direct** human narration in these executors bypasses it and shouts (`"canary RED"`, `"DRIFT
DETECTED"`). The work routes the prose through the existing §6 copy and adds new `Copy`
entries where none exist, with `PhysicalSchema.renderDiff` consistently demoted from the
statement line into a `Disclosure` substantiation.

| Proof | Site | Renderer |
|---|---|---|
| Canary RED / green | `runCanary` `:456-462`, `runCanaryCdcSilence` `:523-531` | route through `Voice.canary.*`; diff → `Disclosure` |
| Drift | `runDrift` `:894-898` | new §6/§8 drift `Copy` (the divergence as finding) |
| Data integrity | `runVerifyData` / `narrateIntegrityReport` `:784-802` | new §6 data-integrity `Surface` |
| Migrate verify / CDC | `:1404-1426`,`:1571-1575` | new §6 verify-proof `Copy` (the round-trip held / diverged) |
| Eject self-verify | `runEject` `:917-923` | §6 freeze-provenance `Surface` |

**Done when:** no §6 proof leads with a shout or a raw diff; each names the fidelity finding
(*"Verified. The database matches the model."* / *"The round-trip returned one difference; it
blocks the commit."*); the structured canary channel is unchanged.

### Wave 4 — §13 lifecycle: the run, recorded (essence at every step)

**Why — the rhythm names the next move; nothing terminates at "done"** (`THE_VOICE.md` §13).
Each completed run is a line in the history that re-proves itself; recording is stated plainly
(*"This run recorded to the history."*). Today every success line is raw (Act 7 Record × §13),
sharing a clear pattern but with no `Copy` entries.

| Site | `Program.fs` | Raw |
|---|---|---|
| `runFullExport` / `runEmit*` success | `:237-247`,`:330-334`,`:356-363` | `"wrote %d artifact(s) to %s"` + per-path + `"recorded episode …"` |
| `runFullExportLoad` | `:311-317` | `"loaded the seed (%d CDC capture(s) measured)"` |
| `runDeploy` | `:380-394` | `"deploy succeeded — database … %d table(s) landed"` |
| migrate / transfer / approve / eject record | `:1404`,`:1559`,`:563-583`,`:917-920` | episode / approval / freeze lines |
| `runCaptureProfile` | `:1683` | `"profile written to %s"` |

New flat `Copy` codes (mechanism 2): `summary.artifactsWritten`, `summary.episodeRecorded`,
`summary.seedLoaded`, `summary.deployLanded`, `summary.profileCaptured` — stative, agentless,
resultative ("N artifacts written", not "wrote N"). Add each to the `code ⇔ copy` in-scope set
as it lands. **And** delete the ~25 redundant raw `"… failed:"` headers above voiced
`printErrors` bodies (`:252`,`:274`,`:280`,`:286`,`:320`,`:338`,`:367`, …) — pure deletion; the
voiced statement is the lead.

**Done when:** success lines render from the catalog (totality-covered); no raw header precedes
a voiced error body; each run ends naming what follows.

### Wave 5 — The §4 move surfaces & the `explain` long tail (the moves, typed)

**Why — change is not a blob; it is the distinct moves, each essence-first** (`THE_VOICE.md`
§4; `THE_INSTRUMENT.md` "Change, made legible"). These need new `Surface` builders and are read
less often than the safety + proof surfaces, so they come after them — once the pattern is
settled.

| Surface | Site | Note |
|---|---|---|
| Transfer report (Move/Insert/Delete) | `narrateTransferReport` `:601-645` | §4 statement per move; "rows dropped" is the Delete move, not drama |
| `explain` node (trail + findings) | `runExplain` `:1013-1041` | trail → `View.Trail`; lead with the finding |
| `explain` suggest-config | `runSuggestConfig` `:1077-1102` | §6 ladder/lever surface |
| `explain` policy-diff | `runPolicyDiff` `:1126-1137` | `Comparison`-style surface |
| `runPlan` unhonored-axis notes | `:1697` | §14 "irrelevant modifier noted" |

### Wave 6 — The synthetic & capture flows (newest surface, voiced last)

**Why — voice it once the pattern is settled, under evidence.** The synthetic path
(`THE_SYNTHETIC_DATA_DESIGN.md`) reaches the operator through `SynthesizeAndLoad`
(`runSyntheticLoad` `:1626`) and `CaptureProfile` (`runCaptureProfile` `:1683`). Its refusals
fold into Waves 0–1; its **success narration** — the `π ∘ σ ≈ id` canary (its own faithfulness
finding), the generated-row counts, the profile round-trip — wants a §6 fidelity statement +
§13 record line. Lowest priority because it is newest and least-trafficked (IR grows under
evidence — voice it once the safety/proof register is established).

---

## 4 — Out of scope by design (register-light, not register-free)

Reference scaffolding, not the instrument speaking about a run: `usageLines` help (`:21-75`) +
the exit-code legend; the `runList` flow menu (`:1820-1832`); `runInit` (`:1778-1794`);
`dumpBench` (`:107-120`). Keep them plain and scannable. If touched, keep them clean of the
four worst breaks (no `%A`, no shout, no lead-code, no pronoun) — but they need no §3/§6
surface. The instrument can be reference-flat here without breaking the spell.

---

## 5 — The storyboard, re-grounded on the flows surface

`THE_STORYBOARD.md` §7 maps the acts to the *migrate*-verb world (pre-flows). The arc is
unchanged — same nine acts, dispatched through `runPlan` over `PlanAction` — only the home
moved:

| Storyboard act | Flows-surface home (executor) | Voiced? |
|---|---|---|
| 0 Arrival / 1 Reading | config resolution + `needCatalog` (`:1708`) + `runReadiness` no-ledger (`:934`) | partial |
| 2 Verdict (what changed) | `reportPreviewOutcome` (`:1152`), `runDiff` (`:983`), `ReportRun.render` | Wave 2 / §4 voiced |
| 3 Gates | `migratePreflights` / `tighteningPreflight` / `--go` & `--allow-drops` refusals | **Wave 1** |
| 4 Making it real | `narrateTransferReport` (`:601`), the write loops | Wave 5 |
| 5/6 Realize / Verify | `runCanary*` / `runDrift` / `runVerifyData` / migrate-verify | **Wave 3** |
| 7 Record | episode-recorded / seed-loaded / approval lines | Wave 4 |
| 8 Where it stands | `runReadiness` board (`:942`) | voiced |

The §8 P-6 worked proof (the per-string exemplars, frame by frame) is the **granularity
target** for the copy each wave writes — derive from it, do not invent.

---

## 6 — Disciplines (hold the vision, not just the guard)

- **The acceptance bar is §1, not the banned-list.** The guard is the floor (no pronoun, no
  shout, no `%A`); the bar is: *newcomer trusts on sight, master reads as a glance, the
  instrument disappears.* A guard-clean line that lands the operator nowhere in the felt arc
  is unfinished. When in doubt, read `THE_VOICE.md` §11 (off → on) and §7 (the felt arc).
- **Derive, never invent** (`THE_VOICE.md` §15). Every string comes from a move (§4), gate
  (§5), proof (§6), error (§10), or config (§14) example — keep the register, fit the
  specifics. If the operator's word isn't in the lexicon (§2.1) yet, add it to `THE_VOICE.md`
  first, then use it.
- **Codes never change, only copy.** The NDJSON event contract is the machine channel
  (`THE_VOICE_BUILD_MAP.md` §8.2 — DO-NOT-BREAK). This backlog touches no event; it is a pure
  rendering project with no runtime write side.
- **Declare-at-site, harvest-centrally; `code ⇔ copy` + gate⇔copy totality.** `Voice.all` is
  the harvest; the totality tests are the sibling of `registered ⇔ executed`. Every new flat
  code enters the in-scope set as it lands; a new `GateLabel` without §5 copy fails the build.
- **IR grows under evidence.** Voice what an executor actually emits; never author copy for a
  surface that does not render yet.
- **Pure-Core holds.** Operator prose never enters `Projection.Core`; `View`/`Surface`/`Voice`
  live in `Projection.Cli`; the 1:1 projection-layer companion is the form when a pure-Core
  pass is ever voiced (the deferred slice-5 `DiagnosticPayload` lift).

---

*The engine proves a theorem about itself; the Voice is the layer that lets the operator read
it. Every wave here takes one more render site from showing the apparatus to showing the
truth — until the instrument disappears and what remains is the schema's truth and the
operator's hand on it. Read `THE_VOICE.md` to know what each line must say, `THE_INSTRUMENT.md`
and `NORTH_STAR.md` to know why it matters, and this to know exactly where it lands. Hold the
voice exact.*
