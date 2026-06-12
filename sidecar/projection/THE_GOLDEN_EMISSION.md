# THE GOLDEN EMISSION — the Platonic corpus and the blessing protocol

> **Adopted 2026-06-12** (operator-directed, mid-reconciliation; see
> `V1_FULL_EXPORT_RECONCILIATION_PLAN.md` and the DECISIONS entry of the same
> date). The operator's charge, verbatim in spirit: *the canaries may green
> light, but it's time to study the end goals of each verb form and align on
> the positive and negative invariant cases — a wide end-to-end test that is
> operator-blessed; an arbitrary contrived set of sample outputs that fits
> precisely the intended model and shows all the variations possible; locked
> in stone and git-tracked as comparators, not disposable.*

---

## 1 — What this is, and what it is not

The property tests and canaries prove **laws** — invariants that hold for
*every* catalog. They cannot prove **intent**: that the bytes the engine emits
for a known input are the bytes the operator means. The golden corpus closes
that gap:

- **The Platonic catalog** (`tests/Projection.Tests/GoldenCatalog.fs`) — one
  contrived catalog, authored in F# through the production smart constructors,
  deliberately containing every emission-relevant variance the engine can
  express. It is not a realistic estate; it is the *complete* estate.
- **The golden corpus** (`tests/Projection.Tests/Golden/<scenario>/…`) — the
  byte-exact artifacts the production composition emits for that catalog under
  a small matrix of operator configurations. Committed, git-tracked, reviewed.
- **The comparator** (`tests/Projection.Tests/GoldenEmissionTests.fs`) — fails
  on any byte drift, file addition, or file disappearance.

The corpus turns every behavior change into a **reviewable git diff on the
goldens**. That diff IS the operator-blessing surface: a reconciliation slice
that changes emission lands with its golden diff in the same commit, and the
review question becomes "is this byte change the intended one?" — not "did the
canaries stay green?".

**This is not a replacement for the canaries.** Laws stay property-tested on
arbitrary catalogs; the goldens pin the *one* catalog where every variance is
visible at once. Nor is it a V1-parity fixture: the goldens pin **V2's
intended form** (which the reconciliation program is steering toward V1 parity
where the plan says so — each such slice shows up here as a deliberate diff).

## 2 — The blessing protocol

1. **Goldens are regenerated only deliberately**: `GOLDEN_RECORD=1` on the
   golden test run rewrites the corpus (`scripts/test.sh focus
   GoldenEmission` with the env var, or the bare dotnet test filter). Mirrors
   the `PERF_GATE_RECORD` discipline.
2. **A golden re-record lands only with a DECISIONS note naming why** — the
   commit shows the byte diff; the note names the intent. An unexplained
   golden diff in review is a defect, full stop.
3. **The operator blesses by reviewing the diff.** First blessing: this
   corpus's initial commit captures **current** behavior, including gaps the
   reconciliation plan has not yet fixed (those are listed in §4 as
   known-unblessed). Subsequent slices convert known-unblessed rows into
   blessed bytes, one deliberate diff at a time.
4. **Stale-file discipline**: the recorder clears each scenario directory
   before writing, so removed artifacts surface as deletions in the diff,
   never as orphaned goldens.

## 3 — Corpus layout

```
tests/Projection.Tests/Golden/
  README.md                      — pointer to this document
  default/                       — Config.defaultConfig + all data lanes on
    Modules/<Module>/<Schema>.<Table>.sql   (the per-table SSDT bundle)
    manifest.json
    stream.sql                   — the flat-stream Render.toText realization
                                   (where GO framing + the constraint ladder live)
    Data/seed.sql                — present only when the lanes produce content
  pruned-platform-auto/          — emission.includePlatformAutoIndexes=false
  delete-scope/                  — emission.deleteScope on the scoped kind
```

Each scenario is the **full** artifact set (not a diff against `default`) so
any golden file is readable standalone. The dacpac is **excluded** by design:
its byte-determinism is an explicitly deferred guarantee (content-equality
only, per the standing deferral) — pinning its bytes would make the corpus
flaky against a non-claim.

Scenario configs are authored in `GoldenEmissionTests.fs` next to the
comparator — the config IS part of the pinned intent.

## 4 — The variance inventory

The checklist below is the corpus's contract: every row is either COVERED
(present in the Platonic catalog and visible in the goldens), TODO (planned;
add the variance and re-record with a DECISIONS note), or N/A (excluded by
design, with the reason). **A new emission capability lands with its
inventory row and its variance in the catalog, in the same commit.**

### Schema — types and columns
| Variance | Status |
|---|---|
| Every `PrimitiveType` realization (Integer/Decimal p,s/Text lengths/Boolean/DateTime/Date/Time/Binary/Guid) | COVERED (`TypeGallery`) |
| Nullable vs mandatory columns | COVERED |
| IDENTITY PK (`IDENTITY (1,1)`) | COVERED (`TypeGallery`, `Task`, …) |
| Non-identity PK | COVERED (`Country`) |
| PK-less kind (heap) | COVERED (`Heap`) |
| DEFAULT — unnamed inline | COVERED |
| DEFAULT — named constraint | COVERED |
| DEFAULT — empty-string Text (the `EmptyTextNormalizedToNull` tolerance, renders `DEFAULT NULL`) | COVERED — **known-unblessed** (named tolerance with retirement trigger) |
| Computed columns | TODO (IR support pending) |
| Collation overrides | TODO |

### Schema — references
| Variance | Status |
|---|---|
| Source-backed FK (`HasDbConstraint=true`), trusted | COVERED (`Task.CreatedBy`) |
| Source-backed FK, untrusted → NOCHECK two-step ALTER pair | COVERED (`Task.UpdatedBy`) |
| Logical-only reference (`HasDbConstraint=false`) — emitted as FK under no intervention | COVERED (`Order.CustomerId`) — behavior pinned; tightening-driven suppression is config-matrix TODO |
| Two forward refs to one target (the CreatedBy/UpdatedBy → User shape; inverse-exclusion visible: NO FK on User) | COVERED |
| ON DELETE Cascade / SetNull / NoAction | COVERED |
| ON UPDATE explicit action (V2 superset over V1) | COVERED |
| Cross-schema FK | COVERED (`audit.ChangeLog` → `dbo`) |
| Composite-key FK | N/A today (composite-FK semantics deferred-with-trigger) |
| FK name length-cap / provided-name round-trip | TODO (plan WP7; matrix row 57 trigger) |

### Schema — indexes
| Variance | Status |
|---|---|
| Plain IX / unique UIX | COVERED |
| Platform-auto index (OSIDX; present in `default`, absent in `pruned-platform-auto`) | COVERED |
| Filtered index | COVERED |
| INCLUDE columns | COVERED |
| DESC key column | COVERED |
| FillFactor / PAD_INDEX / IGNORE_DUP_KEY / disabled / DATA_COMPRESSION | COVERED (`IndexGallery`) |
| Logical IX/UIX name synthesis | TODO (plan WP7 — names currently pass through verbatim; **known-unblessed**) |

### Schema — annotations and adjacent objects
| Variance | Status |
|---|---|
| MS_Description at table + column level (logical names) | COVERED |
| Identity annotations (`V2.LogicalName`/`V2.SsKey`; unconditional today) | COVERED — **known-unblessed** (plan WP5: rename + gate; the rename will be a deliberate golden diff) |
| Index-level extended properties | COVERED |
| CHECK constraints (`ColumnChecks`) | COVERED |
| Triggers | COVERED |
| Temporal (system-versioned) tables | TODO |
| Sequences | TODO |

### Stream realization
| Variance | Status |
|---|---|
| GO framed by blank lines on both sides (slice 2) | COVERED (`stream.sql`) |
| Constraint ladder (4/8/12 indentation) on the flat stream | COVERED |
| `EXECUTE [sys].[sp_addextendedproperty]` canonical form | COVERED (documented accepted deviation) |
| Per-table file: no GO, single-line constraints | COVERED — formatting ladder for file bodies is plan WP7 TODO (**known-unblessed**) |

### Data lanes
| Variance | Status |
|---|---|
| Static seed MERGE (idempotent, CDC-aware predicate) | COVERED (`Country` rows) |
| Phase-2 deferred-FK UPDATE (nullable FK cycle between static kinds) | COVERED (`RegionA`/`RegionB` cycle) |
| Delete-scope arm (`WHEN NOT MATCHED BY SOURCE … DELETE` under the term predicate) | COVERED (`delete-scope` scenario). **First-recording finding:** the term resolves against the POST-CHAIN catalog (after `LogicalColumnEmission`), so under the default logical rendition the term must name the LOGICAL column — the `DeleteScopePolicy` doc's "terms name PHYSICAL columns" is stale for that rendition. Doc/semantics reconciliation rides the plan's WP4 follow-on |
| Static kind with IDENTITY PK (IDENTITY_INSERT handling) | TODO — plan WP6 step 1 (**known-unblessed**: today renders without the bracket) |
| Bootstrap lane content | TODO — plan WP6 (today: empty; the goldens pin the emptiness so the lane filling is a visible diff) |
| MigrationData lane content | TODO — plan WP6 |
| Row batching ≥ threshold | TODO (armed perf trigger) |
| EXCEPT validate-before-apply prelude (opt-in) | TODO — plan WP6 step 6 (C2) |

### Negative invariants (asserted on every scenario, not byte-pinned)
1. No bare-line `GO` inside any per-table `Modules/**.sql` body.
2. No duplicate `(schema, FK constraint name)` across the emitted set.
3. No FK constraint sourced from a derived-inverse reference (no FK on a
   pure-target kind like `User`).
4. No `\r\n` in any artifact. (Trailing-newline termination is NOT current
   behavior — per-table bodies end at the last statement's final char; the
   first corpus recording surfaced this. TODO under the WP7 per-table
   formatting work, **known-unblessed**.)
5. The per-table file set keyset equals the catalog keyset (T11 face).
6. `stream.sql` parses into batches under `BatchSplitter` with no empty batch.

### Excluded artifacts (named blockers)
| Artifact | Why excluded | Unblock |
|---|---|---|
| `manifest.json` | the `VersionedPolicy` stamp captures `DateTimeOffset.UtcNow` at the Pipeline boundary — wall-clock-bearing bytes | boundary-injected clock for the golden runner, or stamp normalization |
| `projection.dacpac` | byte-determinism is an explicitly deferred guarantee (content-equality only) | the standing deferral's trigger |
| `projection.json` / `distributions.json` | IR/profile codecs carry their own round-trip suites; wave-2 candidate once the corpus settles | deliberate wave-2 addition |

## 5 — Relationship to the verb forms

The corpus pins the **full-export projection verb** first (`projectWithConfig`
shaping face — bundle artifacts + flat stream). The same discipline extends,
per verb, as the reconciliation program touches them: the load leg (leveled
plan partition text), `compare`/preflight reports (WP9), the reverse leg's
DML. Each verb's golden scenario lands when its slice does — the inventory
grows; it never shrinks silently.

## 6 — Maintenance

- The inventory table above is part of the contract: COVERED rows must be
  visibly present in the goldens; a slice that lands a TODO row flips it in
  the same commit.
- Known-unblessed rows are the reconciliation plan's worklist intersected
  with this corpus; when the plan's slice lands, the row loses the marker in
  the same commit as the golden diff.
- When the C1 rename (plan WP5) lands, every golden carrying `V2.LogicalName`
  / `V2.SsKey` changes in one deliberate diff — that commit is the worked
  example of the blessing protocol.
