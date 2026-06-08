# THE VOICE — the backlog (deeply incorporating the register into the rendered TTY)

**The masterwork execution backlog.** `THE_VOICE.md` is the *register* (the twelve
rules, the worked examples); `THE_STORYBOARD.md` is the *surface* (act × stream ×
outcome); `THE_CLI.md` is the *command shape* the voice now speaks on (the **flows**
surface — `projection <flow> [--go] [--fresh] [--allow-drops]`); this document is the
**prioritized, file:line-grounded backlog** — every operator-facing render site that
is still raw, the renderer that voices it, the storyboard act + `THE_VOICE.md` section
it serves, and the order to execute. It is the live successor to `THE_VOICE_BUILD_MAP.md`
§6 (the execution layer): the architecture (§1), the emittable-code inventory (§2), and
the test blast-radius (§8) in that map remain accurate and are *referenced, not repeated*;
this backlog supersedes its `Program.fs` execution refs, which are now twice-removed (the
map predates both the four-verb and the flows re-envisionings).

> **Grounding.** Every `file:line` here is a **2026-06-08 snapshot** taken against the
> flows-surface `Program.fs` (1876 lines) by a full render-surface inventory pass. Treat
> them as "look here," then confirm by reading — line numbers drift. The **codes never
> change, only copy**: the NDJSON event contract (`LogSink`/`EventProjection`/`Config`)
> is the machine channel and is DO-NOT-BREAK (`THE_VOICE_BUILD_MAP.md` §8.2). This is a
> pure rendering project — it adds no runtime write side and changes no event.

---

## 0 — The state of play (what speaks, and what does not)

The voice **machinery is built and proven**; the voice **surface is mostly unwired**. Six
sink families render in register today; almost every executor's success narration and
nearly every refusal is raw prose authored inline. The single sharpest fact:
`Voice.gateSurface` / `TtyRenderer.renderGate` exist, are **total over the closed
`Preflight.GateLabel` DU**, are banned-list-tested — and have **zero callers** in
`Program.fs`. Every pre-flight refusal hand-writes its own string beside a finished
renderer.

### What is VOICED today (the whole list)

| Site | `Program.fs` | Renderer |
|---|---|---|
| Terminal summary panel (the verdict) | `withRun` → `:176` | `TtyRenderer.renderSummary` (verdict via `Voice.verdict`) |
| Readiness board (the §8 ladder) | `runReadiness` → `:942` | `TtyRenderer.renderReadinessBoard` |
| Catalog-diff answer (`explain` diff) | `runDiff` → `:983` | `TtyRenderer.renderAnswer (Comparison.renderCatalogChange)` |
| Refused plan action | `:1770` | `TtyRenderer.renderVoicedError` |
| Model-resolution / argv parse errors | `:1708`, `:1869` | `TtyRenderer.renderVoicedError` |
| Every `printErrors` **body** | `:96-97` | `TtyRenderer.renderErrorsTo` → `Voice.errorsSurface` |

> Note the asymmetry on that last row: the **body** is voiced, but ~25 call sites still
> print a raw `Console.Error.WriteLine "… failed:"` **header** immediately above it — a
> redundant raw lead over a voiced statement. That header is dead weight to delete (Wave 5).

### The renderers that exist and are waiting

These are built, tested, and **uncalled** (or under-called) — wiring them is rendering
work with no new infrastructure:

- **`Voice.gateSurface` / `TtyRenderer.renderGate`** (`TtyRenderer.fs:164-180`) — total
  over all nine `Preflight.GateLabel`s; the drop-in for every refusal in Wave 1.
- **`Voice.errorsSurface` / `renderErrorsTo`** (`Voice.fs`, `TtyRenderer.fs:193`) — already
  the `printErrors` body; reusable for the raw `%A` error tails.
- **`Comparison.renderCatalogDiff` / `renderCatalogChange`** (`Comparison.fs:36/122`) — the
  statement-first per-channel `‖δ‖` panel, proven by `runDiff`; the substrate for Wave 2.
- **`Surface.render`** (`Surface.fs`) — the Statement → Substantiation → Action assembly
  every new surface composes through.

---

## 1 — The §2.2 violations live today (Wave 0 — stop the bleeding)

These are strings on the operator surface **right now** that break the banned list. They
are the smallest, highest-integrity items: each is a surgical string/renderer fix, and the
`VoiceTotalityTests` banned-list guard already encodes the rule they break. Do these first
— a shipped surface that shouts or dumps a DU undercuts the whole register.

### 1.1 The four `%A` raw-DU dumps on the operator line

`%A` leaks internals onto the statement (§2.2 "leaked internals / raw-DU dumps"). Each
already has a typed error to route through `Voice.errorFrame` / `errorsSurface`, or a typed
value to render.

| Site | `Program.fs` | Raw | Fix |
|---|---|---|---|
| `reportPreviewOutcome` `other` arm | `:1167` | `sprintf "projection migrate: failed: %A" other` | route the `MigrationError` through `Voice.errorsSurface` (Wave 2 folds this in) |
| `reportMigrationError` `other` arm | `:1279` | `sprintf "projection migrate: failed: %A" other` | same — `Voice.errorFrame`/`errorsSurface` |
| `runApprove` store errors | `:575`, `:579` | `Error e -> sprintf "%A" e` | a typed `toView` for the store error, or `errorSurface` if it carries a code |
| `ExplainRegistry` listing | `:1752-1753` | `sprintf "%A" rt.StageBinding` | a typed `StageBinding → string` (operator label), not `%A` |

### 1.2 The four system-shout leads

`REFUSED` / `FAILED` / all-caps verdicts as a **lead** are banned (§2.2); the verdict is a
§3/§6 finding spoken as meaning, with the proof beneath.

| Site | `Program.fs` | Raw lead | Fix |
|---|---|---|---|
| `runCanary` RED | `:460` | `"canary RED"` | route through `Voice.canary.divergence` (§6 proof — exists) |
| `runCanaryCdcSilence` RED | `:524` | `"canary RED"` | same §6 proof copy |
| `runDrift` | `:897` | `"DRIFT DETECTED"` | a §6/§8 drift statement (Wave 3) |
| migrate verify / eject self-verify | `:1409`, `:1426`, `:1575`, `:923` | `"verification FAILED"` / `"package FAILED self-verification"` | a §6 verify-proof statement (Wave 3) |

### 1.3 The lead-with-a-code lines

§10 puts the code in the **substantiation**, never the lead. Three sites lead with it.

| Site | `Program.fs` | Raw | Fix |
|---|---|---|---|
| `narrateIntegrityReport` warnings | `:802` | `"  %s: %s" w.Code w.Message` | statement first; code beneath (Wave 3 data-integrity surface) |
| inexpressible-ALTER | `:1164`, `:1264` | `"[%s] %s" code msg` | §5/§10 frame — code rides under (Waves 1–2) |
| `runExplain` findings | `:1037` | `"%s %s %s %s" glyph code …` | new explain surface (Wave 6) leads with the finding |

**Acceptance for Wave 0:** the four `%A` sites and the four shout leads are gone; the
banned-list guard is extended to scan the relevant new copy; pure pool green.

---

## 2 — Wire the built gate surface (Wave 1 — highest leverage, zero new infra)

**The single highest-value item in this backlog.** `Voice.gateSurface` is total over the
closed `Preflight.GateLabel` DU and totality-tested (`VoiceTotalityTests` — "every gate
label has a non-empty §5 statement", "every actionable gate names a plain imperative",
"every gate surface clears the banned list"). It is **never called**. `Preflight.classify`
already maps a refusal code → `GateLabel`. Wiring is a drop-in at each refusal site:
build the `GateRefusal`, call `TtyRenderer.renderGate <command> refusal`, return the exit.

Every site below is Act 3 (Gates) × §5 (consent, in plain words) and is raw today:

| Refusal site | `Program.fs` | Today | Gate label |
|---|---|---|---|
| `migratePreflights` connection / permission | `:1290`, `:1297`, `:1303` | raw `"… pre-flight refused:"` | `ConnectionUnavailable` / `InsufficientGrant` |
| `tighteningPreflight` (NOT-NULL on NULL data) | `:1330` | raw `"tightening pre-flight refused …"` | `DataViolatesTightening` |
| `reportMigrationError` `RefusedByCdc` | `:1267` | raw | `CdcTrackedSink` |
| `reportMigrationError` `RefusedByTightening` | `:1271` | raw | `DataViolatesTightening` |
| `reportMigrationError` `SchemaReadFailed` | `:1275` | raw | `SchemaReadFailed` |
| undeclared-drop refusals | `:1156-1160`, `:1258-1260` | raw, lists `Migration.lossToken` | `UndeclaredDestructiveChange` |
| `--allow-drops` token refusal (transfer) | `:742-745` | raw | `UndeclaredDestructiveChange` |
| `--allow-drops` token refusal (synthetic) | `:1659-1662` | raw | `UndeclaredDestructiveChange` |

**One gap inside this wave — the intent gate has no `GateLabel` yet.** The `--go` /
`PROJECTION_ALLOW_EXECUTE` consent refusals (`:695`, `:1346`, `:1447`, `:1636`) are §5/§7
("the two-gate model" — `--go` states intent, `ALLOW_EXECUTE` arms the live write) but have
no `Preflight.GateLabel` variant. Resolve one of two ways (record the choice in `DECISIONS`):
add a `Preflight.IntentNotStated` variant (keeps the closed-DU totality covering it), **or**
voice it through a `gate.intent` code on the flat `errorSurface` path. Recommendation: the
flat `errorSurface` code — the intent gate is a CLI-surface concern, not an engine pre-flight,
so it need not enter the engine's closed DU.

**Acceptance for Wave 1:** every site above renders through `renderGate` (or `errorSurface`
for the intent gate); no raw refusal prose remains in the migrate/transfer/synthetic paths;
the gate⇔copy totality test still passes; the exit codes are unchanged (`Preflight.classify`
owns them).

---

## 3 — The §9 ‖δ‖ minimality preview & report bundle (Wave 2)

The norm — the minimality proof, the heart of `THE_CLI.md` §9 — is surfaced in **three
places, all raw**, all leading with `norm=` or bare counts (symbolic shorthand on the
statement, §1 rule 3 / §2.2). This is Act 2 (the verdict / what-changed) × §6 (the
Minimality proof). The substrate to fix it **exists and is proven**: `runDiff` already
renders the per-channel `‖δ‖` panel statement-first through `Comparison.renderCatalogDiff`.

| Surface | Site | Raw |
|---|---|---|
| Migrate dry-run preview | `reportPreviewOutcome` `:1171-1186` | `"minimum-viable touches (norm): %d"` + per-channel counts + `"emitted: %d ALTER/ADD …"` |
| `report` bundle | `ReportRun.render` (`src/Projection.Pipeline/ReportRun.fs:49-61`) | `"Total schema churn since genesis: %d move(s)."` + `"norm=%d (+%d / −%d / ~%d kinds; %d CDC capture(s))"` |
| Transfer / migrate-with-data CDC counts | `narrateTransferReport` + success lines `:311`, `:1421`, `:1559-1562` | `%d CDC capture(s) measured` embedded in prose |

**The shape to build** (mirrors `runDiff` exactly): a `migrate`/`report` `Surface` whose
*statement* is the §6 Minimality finding ("312 rows changed — exactly those that differed,
and no others") with `‖δ‖ = norm = CDC-capture-count` as the *substantiation*
(`Comparison.renderCatalogDiff` as the panel), and the apply imperative as the *action*. The
idempotent case ("nothing to do — zero minimum-viable touches") is the §6 "already true"
statement. Fold the two `%A` error tails from Wave 0 (`:1167`, `:1279`) into this wave since
they live in the same two functions.

**One nested register fix:** `Comparison.catalogStatement` (`Comparison.fs:66`) still says
`"destroy structure"` — a §5 true-verb miss (drops/narrows, not drama) that rides inside the
already-voiced `runDiff`. Correct it here while in the file.

**Acceptance:** the preview and report lead with the finding, not `norm=`; the norm rides in
the substantiation; `runDiff`'s "destroy structure" is corrected; the `ComparisonTests`
copy assertions are updated (UPDATE set, `THE_VOICE_BUILD_MAP.md` §8.1).

---

## 4 — The §6 proofs: canary, drift, data-integrity (Wave 3)

The fidelity proofs (Act 5/6 × §6) are raw prose, several with the Wave-0 shout leads. The
**structured** canary verdict is already voiced (`canary.diffEmpty` / `canary.divergence`
via `EventProjection.canaryEnvelopes`, read by the `withRun` panel) — but the **direct**
human narration in these executors bypasses it. The work is to route the prose through the
existing §6 copy and add new `Copy` entries where none exist.

| Proof | Site | Today | Renderer |
|---|---|---|---|
| Canary RED / green | `runCanary` `:456-462`, `runCanaryCdcSilence` `:523-531` | raw; `PhysicalSchema.renderDiff` inline as lead | route through `Voice.canary.*`; the diff → a `Disclosure` substantiation, never the lead |
| Drift | `runDrift` `:894-898` | raw `"DRIFT DETECTED …\n%s"` | new §6/§8 drift `Copy` (statement = the finding; diff under `Disclosure`) |
| Data integrity | `runVerifyData` / `narrateIntegrityReport` `:784-802` | raw tabular; warnings lead with code | new §6 data-integrity `Surface` (divergence table = substantiation) |
| Migrate verify / CDC | `:1404-1426`, `:1571-1575` | raw; shout leads | new §6 verify-proof `Copy` (the round-trip held / diverged) |
| Eject self-verify | `runEject` `:917-923` | raw; shout lead | §6 freeze-provenance `Surface` |

`PhysicalSchema.renderDiff` is the right *substantiation* content throughout — the fix is
consistently demoting it from the statement line into a `View.Disclosure`, with the §6
finding on top.

**Acceptance:** no §6 proof leads with a shout or with the raw diff; each has a statement
that names the finding; the canary structured channel is unchanged.

---

## 5 — §13 success narration + drop the redundant raw headers (Wave 4)

Every executor's success line is raw (Act 7 Record / Act 8 Where-it-stands × §13). They
share a clear pattern but have no `Copy` entries. And ~25 voiced `printErrors` calls still
carry a redundant raw header above the voiced body.

### 5.1 The success narration → §13 lifecycle copy

| Site | `Program.fs` | Raw |
|---|---|---|
| `runFullExport` success | `:237-247` | `"wrote %d artifact(s) to %s"`, per-path, `"recorded episode …"` |
| `runFullExportLoad` | `:311-317` | `"published … loaded the seed (%d CDC capture(s) measured)"`, `"episode recorded …"` |
| `runEmit` / `runEmitSkeletonOnly` | `:330-334`, `:356-363` | `"wrote %d … artifact(s) to %s"` + per-path |
| `runDeploy` | `:380-394` | `"spinning up …"`, `"deploy succeeded — database … %d table(s) landed"` |
| migrate / transfer episode-recorded | `:1404`, `:1559` | `"recorded episode …"` |
| `runApprove` / `runEject` provenance | `:563-583`, `:917-920` | approval / freeze lines |
| `runCaptureProfile` | `:1683` | `"profile written to %s"` |

New flat `Copy` codes (mechanism 2, the `Voice.all` catalog): `summary.artifactsWritten`,
`summary.episodeRecorded`, `summary.seedLoaded`, `summary.deployLanded`,
`summary.profileCaptured`. The §13 register: stative, agentless, resultative on completion
("N artifacts written to …", not "wrote N"). Add each to the `code ⇔ copy` totality's
in-scope set as it lands.

### 5.2 Delete the redundant raw headers

The ~25 `Console.Error.WriteLine "… failed:"` headers immediately above voiced `printErrors`
bodies (e.g. `:252`, `:274`, `:280`, `:286`, `:320`, `:338`, `:367`, …) are a raw lead over a
voiced `Hero`. Delete the header; the voiced statement is the lead. Pure deletion — no new
copy.

**Acceptance:** success lines render from the catalog (totality-covered); no raw header
precedes a voiced error body.

---

## 6 — The §4 move surfaces & the long tail (Wave 5 — lower priority)

Act 2/4 × §4 (the moves) and the remaining `explain` faces. These need new `Surface`
builders but are read less often than the safety + proof surfaces above.

| Surface | Site | Note |
|---|---|---|
| Transfer report (Move/Insert/Delete) | `narrateTransferReport` `:601-645` | §4 statement per move; "rows dropped" is the Delete move, not drama |
| `explain` node (trail + findings) | `runExplain` `:1013-1041` | trail → `View.Trail`; findings lead with the finding, code beneath |
| `explain` suggest-config | `runSuggestConfig` `:1077-1102` | §6 ladder/lever surface |
| `explain` policy-diff | `runPolicyDiff` `:1126-1137` | `Comparison`-style surface |
| `runPlan` unhonored-axis notes | `:1697` | §14 "irrelevant modifier noted" copy |

---

## 7 — The synthetic & capture flows (Wave 6 — new-surface territory)

The synthetic-data path (`THE_SYNTHETIC_DATA_DESIGN.md`, built) reaches the operator
through `SynthesizeAndLoad` (`runSyntheticLoad` `:1626`) and `CaptureProfile`
(`runCaptureProfile` `:1683`). Its refusals are folded into Waves 0–1 (the `--allow-drops`
and `ALLOW_EXECUTE` gates); its **success narration** (the `π ∘ σ ≈ id` canary, the
generated-row counts, the profile round-trip) wants its own §6 fidelity statement + §13
record line. Lowest priority because the flow is newest and least-trafficked; voice it once
the safety and proof surfaces (Waves 0–4) are in register, so the pattern is settled.

---

## 8 — Out of scope by design (register-light)

These are intentionally **not** held to the full register — they are reference scaffolding,
not the instrument speaking about a run:

- `usageLines` help page (`:21-75`) and the exit-code legend — keep it plain and scannable.
- `runList` flow menu (`:1820-1832`) — `name: from → to` listing.
- `runInit` (`:1778-1794`) — config scaffolding prose.
- `dumpBench` (`:107-120`) — a dev/diagnostic surface.

If touched, keep them clean of the four worst breaks (no `%A`, no shout, no lead-code, no
pronoun) — but they need no §3/§6 surface.

---

## 9 — Sequencing & the storyboard re-grounding

**Order of execution** (each wave is independently shippable, pure pool green at each step):

1. **Wave 0** — kill the four `%A` dumps + four shout leads + three code-leads (§2.2 live
   breaches; smallest, highest integrity cost).
2. **Wave 1** — wire `renderGate` into every refusal (zero new infra; the built renderer).
3. **Wave 2** — the §9 ‖δ‖ preview + report bundle (reuse `Comparison`; fold the two `%A`
   error tails).
4. **Wave 3** — the §6 proofs (canary/drift/verify/eject).
5. **Wave 4** — §13 success copy + delete redundant headers.
6. **Wave 5** — the §4 move surfaces + `explain` long tail.
7. **Wave 6** — the synthetic/capture surface.

**Re-grounding the storyboard §7 build-readiness map.** `THE_STORYBOARD.md` §7 describes
the *migrate*-verb surface (the pre-flows world). The acts map cleanly onto the flows
surface — they are the same arc, dispatched differently:

| Storyboard act | Flows-surface home (the executor) |
|---|---|
| 0 Arrival / 1 Reading | config resolution + `needCatalog` (`:1708`) + `runReadiness` no-ledger (`:934`) |
| 2 Verdict (what changed) | `reportPreviewOutcome` (`:1152`), `runDiff` (`:983`, voiced), `ReportRun.render` |
| 3 Gates | `migratePreflights` / `tighteningPreflight` / the `--go`/`--allow-drops` refusals (Wave 1) |
| 4 Making it real | `narrateTransferReport` (`:601`), the write loops |
| 5/6 Realize / Verify | `runCanary*` / `runDrift` / `runVerifyData` / migrate-verify (Wave 3) |
| 7 Record | the episode-recorded / seed-loaded / approval lines (Wave 4) |
| 8 Where it stands | `runReadiness` board (`:942`, voiced) |

The §8 P-6 worked proof (the per-string exemplars) is the **target granularity** for the
copy each wave writes — derive from it, do not invent.

---

## 10 — Disciplines (hold every wave)

- **The twelve rules + banned list over every string** (`THE_VOICE.md` §1 + §2.2),
  enforced by the `VoiceTotalityTests` banned-list guard. A line that breaks a rule is not
  done. Extend the guard's scanned set as each wave adds copy.
- **Codes never change, only copy.** The NDJSON event contract is the machine channel
  (`THE_VOICE_BUILD_MAP.md` §8.2 DO-NOT-BREAK). This backlog touches no event.
- **Declare-at-site, harvest-centrally; `code ⇔ copy` totality.** `Voice.all` is the
  harvest; the totality test is the sibling of `registered ⇔ executed`. Every new flat code
  enters the in-scope set as it lands.
- **Gate⇔copy totality** (the closed-DU analog) covers every `Preflight.GateLabel`; a new
  variant without §5 copy fails the build.
- **IR grows under evidence** — voice what an executor actually emits; don't author copy for
  a surface that doesn't render yet.
- **Pure-Core holds** — operator prose never enters `Projection.Core`; `View`/`Surface`/
  `Voice` live in `Projection.Cli`; the 1:1 projection-layer companion is the form when a
  pure-Core pass is ever voiced (the deferred slice-5 `DiagnosticPayload` lift).

---

*The machinery is whole; the surface is the work. Wave 0 stops the live breaches, Wave 1
spends a finished renderer that's sitting idle, and the rest brings each executor's verdict,
proof, gate, and record line into the register one act at a time. Read `THE_VOICE.md` to
know what each line must say; read this to know exactly where it lands. Hold the voice exact.*
