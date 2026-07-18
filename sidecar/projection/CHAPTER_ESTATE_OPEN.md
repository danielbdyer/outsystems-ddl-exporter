# Chapter open — The Estate Chapter: `check estate` and the row-fidelity proofs

> **Opened 2026-07-15** (operator approval of the execution plan, revision 2 — the
> presentation-first re-cut). Requirements: `CUTOVER_RECONCILIATION_IDEATION.md` (PR #668).
> Decisions codified this date in `DECISIONS.md` ("The estate chapter opens" / "The fidelity
> chapter opens"). Axiom candidates A45 + T17 land with their `AxiomTests.fs` stubs in the
> opening commit.

---

## 1 — The strategic frame (the axes this chapter is judged on)

**The KPI is the operator's loop.** The cutover needs one person to be clear-minded,
unambiguous, and decisive across a 300-table estate in three environments, daily:

```
projection check estate            → the board: who diverges, what blocks, the one lever each
  <run the per-env remediation>    → concrete, block-numbered, safe-by-default SQL
projection check estate            → burndown: 12 → 4 → 0; the overlay shrinks
projection publish / migrate       → the ordinary pipeline; the posture is just config
projection check fidelity <flow>   → "N rows identical · M excepted (ledgered) · 0 residual"
```

Five axes, in judgment order:

1. **Presentation** — the board is the product. Findings present by *disposition* (DECIDE /
   REPAIR / RELAX / WATCH), one lever per line, one verdict vocabulary, evidence provenance
   load-bearing. The contract is Appendix A; a finding kind without its contract row is an
   incomplete slice by definition (`finding ⇔ presentation` totality-tested).
2. **Honesty** — evidence age on every decision line; consensus names the blocking environment,
   never a ratio; offline evidence downgrades to advisory; every degradation named.
3. **Algebra** — one findings source, four projections (report · remediation · overlay ·
   probes) under the π-coherence law; consensus = the decision meet over the evidence join
   (`Profile.merge`); A45 espace invariance; T17 row fidelity.
4. **Pay-once** — durable per-env evidence with fingerprint-gated reuse; incremental proof
   caching; the loop must cost minutes, not hours, per iteration.
5. **Register** — every operator-facing line through the Voice catalog, twelve-rule faithful,
   `code ⇔ copy` tested.

## 2 — The wave map (the plan of record)

Track A (the instrument): A0 open → A1 the instrument (verb + findings model + the full board
in rolled-up text + `environments.json`) → A2 consensus + honesty → A2.5 pay-once evidence → A3
detectors, pure wave → A4 detectors, probe/static wave → A5 remediation artifacts → A6 the
posture (relaxations · overlay · probes · the nullability-binder amendment) → A7 burndown +
envelopes → A8 the live board + ease tail.

Track B (the proofs): B0 open → B1 canonical re-basis + the rebuilt aggregate fold → B4a
journal promotion + seed-filter de-silencing (early, independent) → B2 the lockstep comparator
+ `check data --rows` → B3 the HASHBYTES fast-path → B4b the exception-ledger compare + the
three tolerance mintings → B5 the container proof (`check fidelity <flow>`) → B6 proof ease
(incremental cache · apply-order runbook · the board's fidelity tile).

Interleave: `(A0+B0) → {A1 ∥ B1 ∥ B4a} → {A2 ∥ B2} → {A2.5 ∥ B3} → {A3 ∥ B4b} →
{A4 → A5 → A6 ∥ B5} → {A7 ∥ B6} → A8`.

## 3 — Named non-goals (standing for this chapter)

- **No gated remediation executor** (operator decision 2026-07-15): the mode emits artifacts;
  execution stays with `revert` / transfer / migrate under `--go` + `PROJECTION_ALLOW_EXECUTE`.
- **No sub-unanimity apply**; the display never leads with ratios at all.
- **No S8/O4 physical-residue sweep** this chapter (deferred; trigger in DECISIONS).
- **No ledger writes from check verbs**; the burndown history is estate-owned.
- **No partial-estate verdict**: an unreachable environment refuses by name (exit 6).

## 4 — Where truth lives for this chapter

The execution substance (per-slice files, types, tests, risks, caching semantics) is the
approved plan this chapter opened with; its operative content is restated across: this file
(the frame + Appendix A), the two DECISIONS entries (the laws), `AXIOMS.md` A45/T17 (the
candidates), and the ideation document (the requirements). When they disagree, DECISIONS and
the code win.

---

## Appendix A — The presentation contract

Binding on every slice. Each finding kind ships with its lane, its statement pattern, and its
lever, in THE_VOICE's register (stative, agentless, complete sentences, humane numbers, true
verbs, concrete subjects, never negation-as-headline; jargon below the statement line only).
The `finding ⇔ presentation` totality test holds this table to the code. Direction
classification (T1) is not a row — it assigns S-rows to lanes: lag → WATCH, fork → DECIDE,
deployed-ahead drift → DECIDE (first in queue).

**Code-resident since wave A6:** the registry lives in `Core/EstateFinding.fs` —
`EstateFindingKind.specimenOf` (the statement specimen per kind) and
`EstateFindingKind.leverFormOf` (the lever discipline per kind: a DECIDE ruling carries its
imperative; REPAIR composes the block review; RELAX composes the overlay merge; WATCH and the
ACTIVE posture carry none) — both total matches, so a kind cannot land without its row, and
the totality test reads every specimen against the mechanical register laws. When this table
and the registry disagree, the registry is law and this table has a bug.

### A.1 The board (ten regions, fixed order)

```
MASTHEAD    the estate and its basis — target operand · per-environment evidence provenance
VERDICT     one sentence, one glyph — the only verdict vocabulary on the surface
DECIDE      the ruling queue: findings no mechanism resolves — each ends on its ruling
REPAIR      prepared repairs: block-numbered, per environment — each ends on one review lever
RELAX       the interim posture: proposed and active relaxations — probe status beside each
WATCH       advisories: capped, impact-ranked rollups — no lever, by design
MATRIX      environment × plane counts — the drill-down door
BURNDOWN    movement since the last run + the gate streak
ARTIFACTS   the index: one line per artifact naming its role
ACTION      the one next move — the top DECIDE item, else the top REPAIR, else the streak
```

Laws of the surface (each an acceptance test): one verdict vocabulary (`unified / converging /
forked`; environment rows are factual, never a second verdict word) · decision-first grouping ·
one lever per line · homogeneous lanes · impact-ranked, capped, remainder named · evidence
provenance load-bearing · one substrate (`environments.json` ≡ the terminal) · the empty state is a
full surface.

### A.2 The finding contract

| # | Finding kind | Lane | Statement pattern (Voice) | Lever (Action) |
|---|---|---|---|---|
| S1 | Presence, lag | WATCH | "Invoice.ExternalRef exists in dev and is absent downstream — promotion lag; the ordinary publish resolves it." | — |
| S1′ | Presence, fork/drift | DECIDE | "Customer.TaxCode exists in uat alone and no promotion explains it — deployed-ahead drift." | "Rule the attribute: adopt it into the model, or schedule its removal." |
| S2 | Facet fork (type/length/scale/default/collation) | DECIDE | "Order.Discount carries decimal(19,4) in dev and decimal(19,2) in uat; the promotion order explains neither." | "Rule the declared scale, then re-run." |
| S3 | Nullability fork | DECIDE | "Customer.Email is NOT NULL in dev and NULL in uat; uat holds 4,120 NULL rows." | "Rule the target nullability — the repair or the relaxation follows the ruling." |
| S4 | Identity/AutoNumber fork | DECIDE | "Status.Id is minted by the database in dev and declared explicitly in uat — key minting disagrees across the estate." | "Rule the minting; pinned seeds are the prepared path (overlay entry 5)." |
| S5 | Delete-rule fork | DECIDE | "Order→Customer deletes cascade in qa and restricts elsewhere — a delete in qa removes rows the other environments keep." | "Rule the delete behavior." |
| S6 | Index divergence | REPAIR / DECIDE | "IX_Customer_Email is unique in qa alone; dev holds 12 colliding pairs — the index promotes after the pairs resolve." | "Review block 4 of environments.remediation.dev.sql." |
| S7/O3 | Untrusted constraints | REPAIR | "FK_Order_Customer exists untrusted in qa (WITH NOCHECK); re-trusting scans 12,400,000 rows." | "Review block 12 — the re-trust statement and its cost, prepared." |
| S9 | Active/inactive divergence | DECIDE | "Customer.LegacyFlag is inactive in dev and active with data in uat — the model and the estate disagree about its life." | "Rule the attribute's status." |
| S10 | Static-modality divergence | DECIDE | "Country is a static entity in dev and a regular entity in uat — seeding and identity behave differently per environment." | "Rule the modality." |
| D1 | NULLs under NOT NULL | REPAIR / RELAX | "Customer.Email declares NOT NULL; uat holds 4,120 NULL rows." | "Review block 2 of environments.remediation.uat.sql." |
| D1×D5 | The empty-string interplay | REPAIR (refines D1) | "Customer.Email declares NOT NULL; uat holds 4,120 NULLs and 18,300 empty strings that normalize to NULL on publish — the constraint fails at 22,420 rows, and the NULL count alone understates it." | "Review block 2 — it locates both populations." |
| D2 | Duplicates under UNIQUE/PK | REPAIR / DECIDE | "Customer.Code declares unique; dev holds 12 colliding pairs." | "Review block 4 — the pairs, and the re-key path, prepared." |
| D3a | Sentinel-zero orphans | REPAIR | "Order.CustomerId in uat: 3,214,000 orphans, of which 3,101,000 are unset references (value 0). Block 7 clears the unset references to NULL; 113,000 true orphans remain." | "Review block 7 of environments.remediation.uat.sql." |
| D3 | True orphans within band | REPAIR | "OrderLine.ProductId in qa: 4,200 rows reference absent products." | "Review block 9 — locate, re-point, or remove." |
| D3′ | Orphans past the band | RELAX | "Sales.Order→Customer: 113,000 orphans in uat exceed the repair band. The interim relaxation keeps the relationship untracked; the reopen probe retires it at zero." | "Merge overlay entry 3 of environments.overlay.json." |
| D3b | Orphans in every environment | DECIDE | "Order.CustomerId orphans in every environment at similar ratios — the relationship has never held in the data." | "Rule the relationship: declare it optional in the model, or schedule the estate-wide repair." |
| D4 | Length/type overflow | DECIDE | "Customer.Notes holds values to 4,812 characters against a declared 2,000; the 99th percentile is 1,940." | "Rule the width: widen to the envelope (overlay entry 6), or truncate (block 11)." |
| D6 | Collation-sensitive duplicates | REPAIR | "Under the target collation, Customer.Code collapses 240 case-distinct pairs into duplicates — the unique index fails on unification." | "Review block 5 — the colliding pairs, located." |
| D7 | Narrowing residue by tolerance | WATCH | "DecimalScaleTolerated absorbs 4,000,000 rows in uat and none elsewhere — one environment leans on the tolerance." | — |
| D8 | Date sentinels | WATCH | "Order.ShippedOn holds 812,000 rows at 1900-01-01 — the platform's empty-date convention; a NOT NULL reading of the column is satisfied and empty of meaning." | — |
| D9 | Numeric domain drift | WATCH | "Order.StatusCode holds the value 99 in uat alone; every other environment draws from {1, 2, 3, 7}." | — |
| D10 | Static content divergence | REPAIR | "Country diverges from the seed in uat: 2 rows missing, 1 extra, 3 label drifts." | "Review block 14 — the alignment MERGE, matched by business key." |
| D11 | AutoNumber static entities | DECIDE + REPAIR | "Status mints different keys per environment for the same rows — 'Approved' is 3 in dev, 7 in qa, 4 in uat; every inbound reference means something different per environment." | "Rule the seed: pin explicit keys (overlay entry 5); the alignment repair follows the ruling." |
| D12 | Rowcount asymmetry | WATCH | "OrderLine holds 10,400,000 rows in uat and 12,000 in dev — verdicts drawn on dev's evidence are advisory at this asymmetry." | — |
| D13 | Identity headroom | WATCH / DECIDE | "Order.Id stands at 1,340,000,000 of int's 2,147,483,647 — 62% of the ceiling is consumed." | "Schedule the widening to bigint." |
| D14 | User-reference resolution | REPAIR / DECIDE | "1,204 distinct users are referenced in qa; 1,192 resolve by email in the target directory, 8 are ambiguous (duplicate emails), 4 resolve nowhere." | "Review block 16 — the ambiguous and unresolved users, listed." |
| D15 | Uniqueness candidates, unanimous | WATCH | "Customer.LegacyCode is distinct in every observed row of every environment (214,000 of 214,000) — a natural-key candidate." | — |
| I1 | Espace-invariance residue | DECIDE | "Two environments read as different shapes for one model — the espace-invariance law found a residue at Invoice.Triggers." | "Review the residue — the finding names which facets differ." |
| I2 | Lookup-like surrogate correspondence | WATCH (beside D10/D11) | "OrderStatus behaves as reference data — 14 rows, unchanged across 90 days, referenced by 12 relationships; static modality is the prepared ruling." | — |
| I3 | Synthesized identity | WATCH | "3 kinds carry synthesized identity in qa — renames there are unstable until the identity anchors." | — |
| O1 | CDC parity | DECIDE / WATCH | "CDC tracks 12 tables in uat and none below — a cutover write to these tables feeds live consumers in uat alone." | "Rule the CDC plan for the tracked tables." |
| O2 | Grant gaps | DECIDE (about the env) | "uat lacks ALTER — 4 prepared repairs in this environment wait on the grant." | "Grant ALTER on uat, then re-run." |
| T2 | Burndown | (strip) | "Since the last run: 8 findings closed, 1 opened, 4 remain — the oldest is 12 days." | — |
| — | ProofMissing | DECIDE | "The fidelity proof for flow 'uat-load' has not run against the current estate." | "Run: projection check fidelity uat-load." |
| — | ProofStale | DECIDE | "The fidelity proof last ran 9 days ago; 14 kinds have moved since." | "Run: projection check fidelity uat-load." |

S8/O4 (physical residue; unmanaged triggers/computed columns) stay deferred with their named
trigger; when they land, they present as S1′-shaped DECIDE rows.

### A.3 Remediation opportunities → lanes

untrack-FK → D3′ RELAX · keep-nullable + backfill → D1 RELAX (post-ruling on S3) ·
widen-vs-truncate → D4 DECIDE with both paths prepared · demote-unique → D2's RELAX arm ·
pin static IDs → D11 · int→bigint → D13. Every RELAX entry's overlay edit and reopen probe
carry the finding's key; a probe's retirement renders as a completion notice on the next run
("The reopen probe for Sales.Order→Customer reports zero — the relaxation is retirable;
overlay entry 3 can come out and the relationship can track WITH CHECK.").

### A.4 Verdict and masthead specimens (the register the rest follows)

- Unified — "✓ The estate is one shape. 3 environments match the target schema, the data fits
  every declared constraint, the interim posture is empty, and the fidelity proof is green
  (2 days old)."
- Converging — "▲ The estate is converging: 13 findings remain across 3 environments — 4
  awaiting a ruling, 6 carrying a prepared repair, 3 under an interim relaxation. The ruling
  queue is first below."
- Forked — "✕ The estate is forked in 2 places: the environments disagree in ways no promotion
  order explains. Each fork names the environments that disagree and the ruling that resolves
  it."
- Masthead, per env — "uat — evidence captured 2 days ago; fingerprints clean across 214
  kinds." · "dev — 3 kinds moved since capture (Orders, OrderLine, Customer); re-profiled this
  run." · "qa — offline evidence, 9 days old, unprobed; every verdict standing on it is
  advisory."
- Unreadable env (exit 6) — "uat could not be read: the connection was refused. The estate
  verdict needs every named environment. Restore the connection, or narrow readiness.confirm
  deliberately."
- Consensus — "'Customer.Email' tightens nowhere: uat holds 4,120 contradicting rows; dev and
  qa are clean." · "clean in dev (12 rows observed, evidence 2 days old) — advisory; the
  sample is below the decision floor."
- Overlay — "Interim posture: 4 relaxations in environments.overlay.json — each carries the probe
  that retires it. The merge is an operator edit; the engine never applies it."
- Fidelity proof — "Proof green: 17,431,882 rows byte-identical, 3,214 excepted — 3,101 user
  re-keys, 113 sink-minted keys, every exception citing its ledger record — 0 residual.
  Tolerances in force: emptyText, decimalScale." · "No intervention ledger was supplied — this
  proof claims strict byte-identity." · "182 kinds carry clean fingerprints since the last
  proof and were skipped; 32 kinds proved now. --refresh re-proves the estate."
