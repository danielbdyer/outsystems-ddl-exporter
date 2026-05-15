# HANDOFF — chapter A.0' in flight (slice δ shipped; slice ε next)

**Read this once, then go.** Everything you need to keep going is reachable from the cross-references below. Do not read the whole file front-to-back — pick the next slice, follow the cross-references to the active discipline + commit precedent, and code.

## Where to start

1. **`CHAPTER_A_0_PRIME_OPEN.md`** slice table — the operative backlog for this chapter. Each row carries Status / Mode / Commit. Pick the first 🟡 (next) row; if it's gated, fall back to the first ⚪ (future) row.
2. **Tail of `DECISIONS.md`** — the most recent 2–3 entries describe the disciplines and slices recently codified. The append-only log is the discipline; cross-references from the chapter file land you on the relevant entries.
3. **`git show <commit>`** for the most recent slice's commit message — names the mechanical-edits precedent and the harvest-dichotomy classification recorded in the slice's DECISIONS amendment.

**That's the whole orientation pass.** When in doubt, run the harvest workflow (pillar 9) and write the DECISIONS amendment before coding.

## Status (chapter A.0')

Test baseline: **1182 / 1182 passing** at slice δ (1169 prior + 13 new `SequenceLiftTests`).

| Slice | Subject | Mode | Status | Commit |
|---|---|---|---|---|
| α | `Kind.Description` + `Attribute.Description` | additive | ✅ Shipped | `3c75d00` |
| β | `Module / Kind / Attribute.IsActive` carry-through; retire session-21 silent drop | literal-site audit (semantic shift) | ✅ Shipped (pillar 9 first worked example) | `014d5d1` |
| γ | `Catalog.Triggers : Trigger list` + `Fixtures` builders | builder-mediated | ✅ Shipped (discipline-refinement first worked example) | `16ab57d` |
| δ | `Catalog.Sequences : Sequence list` + `Sequence` value type + `SequenceCacheMode` DU | builder-mediated (additive; mirror of γ) | ✅ Shipped (builder-mediated 2nd worked example; catalog-level extension via slice-γ builder) | (this slice) |
| ε | `Attribute.DefaultValue` + `Attribute.Computed` + `Kind.ColumnChecks` | builder-mediated (additive; three related) | 🟡 Next | — |
| ζ | `ExtendedProperties` on Module / Kind / Attribute / Index | builder-mediated (additive; widest blast radius) | ⚪ Future | — |
| η | `ModalityMark.Temporal of TemporalConfig` | literal-site audit (DU widening) | ⚪ Future | — |
| θ | `TableId.Catalog : string option` | literal-site audit (touches every TableId site) | ⚪ Future | — |
| ι | `IsExternal` / Origin audit + L3-Boundary-NoSilentDrop property test | property tests only | ⚪ Future (chapter close) | — |

## What's load-bearing for every slice

- **Pillar 9 — harvest-dichotomy classification** (DECISIONS 2026-05-15 late). Every transformation is `DataIntent` (skeleton-reachable; lands in IR) or `OperatorIntent of OverlayAxis` (operator intent; lands as overlay). The slice author runs the 4-step harvest workflow and records the classification in the slice's DECISIONS amendment. Worked examples: slice β reclassified session-21's silent drop (OperatorIntent at the wrong layer) → carry-through (DataIntent at the right layer); slice γ classified Trigger carriage as DataIntent.
- **Closed-DU empirical-test discipline refinement** (DECISIONS 2026-05-15). Two modes; slice author picks at slice open and records the choice in the DECISIONS amendment.
  - **Literal-site audit** when the new field carries semantic ambiguity (slice β; future slice η / θ). Test fixtures stay explicit; the agent walks every site so latent assumptions surface.
  - **Builder-mediated** when the field is additive with a sensible default (slice γ onward, by default). Test fixtures use `Fixtures.attribute / kind / module' / catalog` builders so future fields touch only the builder.

## Mechanical-edits precedent

Per slice α / β / γ: record-extension slices follow this workflow.
1. Extend the IR record(s) in `Catalog.fs` + smart constructors (`Module.create` / `Catalog.create` if signature changes).
2. Adapter pickup on both JSON and rowset paths (`CatalogReader.fs`). Cross-source parity is the discipline.
3. Property tests in a new `<Field>LiftTests.fs` file (mirror `DescriptionLiftTests.fs` / `IsActiveLiftTests.fs` / `TriggerLiftTests.fs`).
4. Build, capture FS0764 worklist, run the parameterized fixture-extension script (`/tmp/add_isactive.py --field <Name> --value <Default>` — preserved in the slice-β PR description if needed).
5. Manually fix any sites where the script's inline-close heuristic produces invalid F# (typically `|>` pipe-chains ending at `}`). Use `{ c with ... }` instead.

## Operator-side checks (unchanged)

- **R1** — operator's "document of key evolutions" still pending. Hold UAT-users decisions until it lands.
- **Q2 / Q3 / Q4 / Q7** revisable during touching slices; don't escalate.

## Prior bridge letters

`HANDOFF_ARCHIVE.md` carries all prior session bridge letters (newest at top). `HANDOFF_CHAPTER_1.md` / `HANDOFF_CHAPTER_2.md` carry the closed-chapter synthesis bridges. Read on demand for chronological context only.
