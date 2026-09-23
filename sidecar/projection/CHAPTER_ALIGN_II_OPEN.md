# CHAPTER ALIGN-II OPEN — The Ruling & the Epistemics (Arcs R + E)

> **Opened 2026-08-16**, immediately after `CHAPTER_ALIGN_I_CLOSE.md` per the
> operator's consecutive-chapters ruling (DECISIONS "THE ALIGNMENT PROGRAM
> OPENS"). Fourteen slices, II.0–II.13, on PR #695. Finding-level ground
> truth: `AUDIT_2026_08_16_GEOMETRIC_ALIGNMENT.md` + the workpapers — **a4**
> (decision strategies: the ruling dialects, abstain honesty, per-subject
> rulings), **a1** (acquisition witness: RowsetContract, toBundle erasures,
> AcquisitionScope, journal read-side), **a7** (verification epistemics:
> typed fingerprints, finding pedigree), **a8** (operator semantics: the
> verb checklist). Where this frame and a workpaper disagree on a
> current-code fact, the workpaper's citation wins; the code wins over both.

---

## 1 — The chapter's two charges

**Arc R (teleological).** The system COLLECTS operator judgment in six
dialects (approvals, signoffs, consents, overrides, suggested-config,
estate levers) but has NO carrier for a RULING — a keyed, anchored,
durable "the operator confirmed/rejected THIS finding on THIS evidence."
K9's demand (a confirmed/rejected S14 correspondence recordable
end-to-end) is inexpressible. The arc lands the `OperatorRuling<'anchor>`
carrier + keyed store (II.1), threads provenance onto tightening
overrides (II.2), makes per-subject index rulings expressible (II.3),
retires the strategy DUs' disguised abstains (II.4), receives rulings on
the estate DECIDE lane (II.5), and ships the `projection rule` verb
(II.6) — record + render ONLY.

**Arc E (epistemic).** Knowledge the house types elsewhere still lives in
prose/strings/convention at six seams: the 26-rowset walk's dispositions
(II.7 RowsetContract), `toBundle`'s erasures (II.8 — the adjunction's
modulus at this seam becomes enumerable), acquisition scope (II.9 —
optional codec field defaulting Total, NO version bump), the sink
journal's read-side classification (II.10), fingerprint readings (II.11),
and finding pedigree (II.12 — evidence standing × magnitude per
contributing environment).

## 2 — Judgment axes (the audit's frame, applied)

- RELATIONAL: a ruling ANCHORS to what it judged (`BasisAnchor = Digest |
  Fingerprint | FindingKey | EvidenceDigest`) — never a floating boolean.
- SEMANTIC: abstain ≠ keep; unreadable ≠ empty; not-profiled ≠ reliable —
  the trichotomies get their own names (II.4, II.10).
- STATE: what the journal/store KNOWS is reachable from what it persisted
  (II.10 read-side; II.11 typed readings).
- HIERARCHICAL: rulings key per-subject (II.3), not per-run.
- Reification: ONTIC carriers (OperatorRuling, RowsetContract), EPISTEMIC
  honesty (ProbeReading, JournalReading, Pedigree), TELEOLOGICAL closure
  (the confirm/reject loop closes end-to-end at II.5/II.6).

## 3 — The wave map (dependencies honored)

```
II.0 open (this doc + A53 Bucket-C stub + matrix)
R-track (serial): II.1 carrier+store → II.2 tightening provenance →
                  II.3 index rulings → II.4 abstain honesty [BEHAVIORAL]
                  → II.5 reception (A53 → LIVE) → II.6 the verb
E-track:          II.7 RowsetContract → II.8 typed erasures → II.9
                  AcquisitionScope   (ONE adapter file — strictly serial)
                  ∥ II.10 journal read-side → II.11 typed fingerprints
II.12 finding pedigree (LAST — EstateFinding collision with II.5)
II.13 close ritual (Release fast solution-wide; full Docker; PR refresh)
```

## 4 — Standing design rulings (bind every slice)

1. **The ruling store is a KEYED replace-by-key store** under
   `<store>/rulings/` — the `ApprovalStore` shape (fail-closed load,
   `Result` save, atomic `tmp + File.Move(overwrite)` write). NOT a fifth
   `LedgerSpec`; append-only ruling HISTORY is a named deferral past
   III.2's ChainAdmission (`BasisAnchor.SinkEdition` widen is its
   trigger).
2. **Record + render ONLY** — no automatic policy application from
   rulings anywhere in this chapter (named deferral with trigger).
3. **The "Witness-" naming freeze is ACTIVE** — new witness-plane names
   take `Witness-` from here on (the X-arc's measured cut needs a fixed
   target).
4. **Exemplary consent surfaces untouched**: WriteSignoff / ActConsent /
   ApprovalWorkflow stay; carriers land at the degraded rungs only.
5. Voice: every operator-visible face code lands with copy + `all` list +
   both VoiceTotality lists in the SAME slice; the 10-step verb checklist
   governs II.6; EstateTests lane⇔lever coherence governs II.5.
6. Behavioral slices (II.4 is one) carry DECISIONS WITH the change;
   goldens re-recorded with the deviance named; consumers enumerated.

## 5 — Non-goals

- No ruling auto-application; no policy mutation from a ruling.
- No LedgerSpec for rulings (ruled above).
- No estate-meter redesign; II.4's applied/declined SETS stay
  byte-identical (lineage becomes epistemically true; membership is
  unmoved).
- The 82 lint sites and the two transfer-leg Docker reds stay OUT
  (operator ruling 2, unchanged).

## 6 — Acceptance

Chapter II closes when: A53 is LIVE (rulings round-trip, fail-closed,
anchored; reception renders them on findings); the `projection rule`
verb records + renders through the full checklist; the six epistemic
seams carry types with their laws (`parseLine ∘ renderLine = id` beside
T19; erasure-totality; scope-subsumption at the S13 gate with behavior
IDENTICAL; codec property R12 — 16-field snapshots deserialize
`Scope = Total`, 17-field round-trips, torn stays fail-closed); and the
close ritual's eight items walk with the known-red Docker set exactly
the two named tests.
