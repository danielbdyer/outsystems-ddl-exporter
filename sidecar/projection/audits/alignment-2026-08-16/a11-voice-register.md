# a11 — The Voice register audit (operator directive, 2026-08-16)

> Commissioned mid-Chapter-III by operator directive: *"audit the new Voice entries we are
> publishing in this thread and are seeing in these Inspect FidelityRows outcomes and other
> similar implementations — it's crucial to ensure we are aligned with THE_VOICE.md."*
> Ground truth: `THE_VOICE.md` (the twelve rules §1; the banned list §2.2; the lexicon §2.1).
> Method: every operator-visible string family published or touched by the alignment program
> (Chapters I–III.1), plus the two surfaces the operator named (the fidelity-rows face, the
> Inspect view), plus a full-catalog sweep for each violation class found — a class violation
> anywhere forks the register, so classes are audited catalog-wide, not thread-wide.
> **The operator's assessment is CONFIRMED: the register is broken in five classes.**

## Findings

### V1 — the lazy plural `(s)` — 282 sites, CLASS VIOLATION (rules 3, 12; §12)

`"%s row(s) across %s kind(s)"` · `"4 consecutive unified run(s)"` · `"1 difference(s)"`.
Rule 3 demands a statement *readable aloud*; §12's own examples always write the real form
(`4,210 rows`, `3 accounts`). The house had already ruled this once and then kept publishing
the form: `EstateTests.fs:121-123` pins `Assert.DoesNotContain("difference(s)",
finding.Statement)` for one facet family — the precedent existed and the catalog ignored it.
282 sites across ~40 files (Voice.fs ×30, Estate.fs ×36, EstateFinding.fs ×15,
Faces/Transfer.fs ×60, TransferRun.fs ×22, emitters, boards, HTML views). Root cause: §2.2
never named the form, so nothing enforced it.

**Disposition: fix catalog-wide with an explicit `counted n singular plural` helper (the
`humane` sibling); ban the form in §2.2; freeze with an executable register law (the M16
source-scan idiom) so the class cannot regrow.** Staged: the operator-named surfaces + the
estate finding grammar + the Voice catalog first; the transfer/emitter long tail second;
the freeze widens to total with the tail.

### V2 — engine nouns on operator statements (§2.1: the boundary translates, always)

`kind(s)` in operator copy — `"fingerprints … clean across 214 kind(s)"`,
`"%s row(s) across %s kind(s)"`, `"%s kind(s) in %s number their rows differently"`.
The lexicon is explicit: the operator reads **table**, never `Kind`. Sites ride V1's sweep.

### V3 — the fidelity-rows statement family (rules 4, 8, 10; §2.2 jargon)

- `fidelity.rows.matched/diverged` lead with `"across the physical-to-logical gap"` /
  `"across the gap"` — a coined figure where rule 10 wants the exact referent (the compared
  source rows and target rows).
- `"No intervention ledger was supplied — this proof claims strict byte-identity."` —
  *claims* hedges a finding rule 4 says to assert with its basis.
- `"Tolerances in force: BooleanCanonicalizationTolerated, … — the canonical row form's
  named erasures."` — the tokens are legitimately the operator's own config vocabulary
  (§6 requires each tolerance named; `ToleratedDivergence.name` = the config token), but the
  appositive is jargon on the note. The tokens stay; the frame becomes plain.

### V4 — the reconcile lead (rules 3, 12; §2.2 system-shout)

`Faces/Fidelity.fs:484`: `"Reconciled the target 'tgt' against the manifest captured from
'src' — per-kind pass/fail, NO live source. Escalate to \`check data --rows\` (both live) to
name differing rows."` Four breaks in one line: an agentive past-tense lead (rule 12 — the
instrument performed), a telegraphic fragment (`per-kind pass/fail`, rule 3), a caps shout
(`NO`), and the engine noun (`per-kind`). Rewritten stative + plain, with the imperative
naming the object.

### V5 — align-III.1's decode messages (rules 3, 11; §14) — this thread's own mint

`"coordinate missing required 'at' instant"` / `"record missing required 'at' instant"` —
headless fragments; `"(ISO-8601 form required)"` — a parenthetical where §14 wants the
provision imperative. Articles + `supply an ISO-8601 instant` fix them. (The SyncOrdinal
message and the sink refusal copy grade clean.)

### Held compliant (audited, with citations)

- `estate.rulings.unreadable` / `rule.recorded` / `Estate.rulingText` / the II.2 attribution
  clause — complete stative sentences, located causes, evidential grounding (one refinement:
  `"The board renders without them"` → stative `"The board is rendered without them this
  run"`).
- Inspect's `at` field carrying the ISO instant — a Field row is the substantiation layer;
  rule 3 places notation exactly there. The Hero above it is the statement. HELD.
- `"ROWS — src against tgt"` and the board lane labels (`DECIDE — the ruling queue`) — the
  house masthead form; a label, not a lead sentence. HELD.
- `@sync N` inside claim payload fields — substantiation-layer fields. HELD.
- Ordinal/instant rendering after align-III.1 — byte-identical to the prior register. HELD.

## The repair (align-III.1v, staged; each stage full-gated)

1. **Stage 1 (this commit):** §2.2 gains the lazy-plural ban + a §11 calibration row; the
   `counted` helper lands beside `humane`; the operator-named surfaces and the register's
   heart are rewritten — Voice.fs (all sites), EstateFinding.fs + Estate.fs (the finding
   grammar and mastheads), EstateBoardView.fs, TtyRenderer.fs, GoBoardView.fs,
   ReviewNavigator.fs, Faces/Fidelity.fs, the III.1 messages — with every test pin moved and
   an executable register law (`VoiceRegisterTests`) freezing the fixed files at zero.
2. **Stage 2 (next commit):** the long tail — Faces/Transfer.fs, TransferRun.fs, the
   emitters (Remediation/Summary/DecisionLog/ApplyRunbook/BatchSplitter), the HTML views,
   Compare/Readiness/PeerTransfer/ModelFidelity/GoBoard — and the register law widens to
   the full src tree.

The register's maintenance rule (§15) applied: the banned form is added to the doc FIRST,
then the code conforms, then the law freezes it.
