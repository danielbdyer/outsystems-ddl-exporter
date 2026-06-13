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
  master/                        — THE one massive emission: the full
                                   Platonic catalog under a kitchen-sink config
    Modules/<Module>/<Schema>.<Table>.sql   (the per-table SSDT bundle)
    manifest.json
    stream.sql                   — the flat-stream Render.toText realization
                                   (where GO framing + the constraint ladder live)
    Data/seed.sql                — the data lanes (incl. the folded-in
                                   delete-scope DELETE arm on the scoped kind)
  pruned-platform-auto/          — a SMALL standalone one-off: a tiny catalog
                                   under emission.includePlatformAutoIndexes=false
```

**The maximal-master + standalone-one-offs layout (DECISIONS 2026-06-13 —
golden corpus, take 2; supersedes the delta layout).** The corpus is one
**master** plus a few small **one-offs**, by the catalog-vs-config
distinction:

- **`master/`** is the one massive, standalone, read-top-to-bottom emission:
  the full Platonic catalog (every catalog-shaped variant — tables, columns,
  references, indexes, constraints, annotations, data lanes) under a
  kitchen-sink config that also folds in every *config*-shaped variant that
  can coexist. `DeleteScope` resolves per kind, so the master carries it: the
  scoped kind renders its `WHEN NOT MATCHED BY SOURCE … DELETE` arm while
  every other static kind stays a plain MERGE — both variants in one file.
- **one-offs** exist only for *globally all-or-nothing* config flags that
  cannot coexist with the master. Each is a **small, self-contained** full
  emission over a tiny purpose-built catalog, so it shows exactly that flag's
  effect and nothing else. Today: `pruned-platform-auto/` (one kind with a
  platform-auto index beside a normal one; `IncludePlatformAutoIndexes` is
  global, so the pruned rendering can't live in the master). Future
  non-foldable flags (WP5 identity-annotation omit; WP6.6 EXCEPT
  validate-before-apply) each add their own small one-off; foldable ones go
  into the master.

Every scenario is a FULL standalone byte-set (the comparator is a per-file
byte-compare + artifact-set drift check); there is no baseline/delta
relationship to mentally reconstruct. The dacpac is **excluded** by design:
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
| The full scalar×DEFAULT enumeration on ONE master table (every `PrimitiveType` with its DEFAULT-able literal: Integer/Decimal/Text/Boolean/DateTime/Date/Time/Guid/Binary + the no-default contrast column) | COVERED (slice 3, `ScalarGallery`) |
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
| Self-referencing FK (`Engagement.ParentId` → `Engagement`) | COVERED (slice 3) |
| Column-inline FK ladder (constraint beneath its attribute at +4/+8/+12; V1 column-suffix shape) | COVERED + BLESSED (slice 3) |
| Composite-key FK | N/A today (composite-FK semantics deferred-with-trigger) |
| FK name length-cap / provided-name round-trip | TODO (plan WP7; matrix row 57 trigger) |

### Constraint placement & the naming budget (slice 3b)
| Variance | Status |
|---|---|
| Column-constraint STACK — several constraints on one column rendered as ONE statement, each laddered beneath the head, comma on the last segment | COVERED + BLESSED (`Tally` DEFAULT+CHECK; `AltCustomerId` DEFAULT+FK) |
| Single-column PK inline / composite PK table-level (2-line) | COVERED (`ScalarGallery.Id` / `Assignment`) |
| Single-column FK inline / non-resolving fallback table-level | COVERED / defensive-only (unreachable under `Catalog.create`) |
| Generated FK name ≤128 — byte-identical pass-through | COVERED (every ordinary FK) |
| Generated FK name >128 — `IdentifierBudget.fit`: 115-char head + `_` + 12-hex SHA-256 of the full name = exactly 128 | COVERED + BLESSED (`Ledger` → `EcrmSnapshot`, visible in the goldens) |
| Generated PK name >128 | Budget applied at the site; catalog example TODO (needs a >120-char table name) |
| Authored index names — pass-through (no budget; source-owned) | COVERED |

### Schema — indexes
| Variance | Status |
|---|---|
| Plain IX / unique UIX | COVERED |
| Composite (multi-attribute) UNIQUE index | COVERED + BLESSED (slice 3b, `UIX_Engagement_CustomerId_Subject`) |
| Composite index with mixed ASC/DESC directions | COVERED + BLESSED (slice 3b, `IX_Engagement_CreatedBy_UpdatedByDesc`) |
| Platform-auto index (OSIDX; present in `master`, absent in the `pruned-platform-auto` one-off) | COVERED |
| Filtered index — FILTER predicate follows the logical substitution (v2) | COVERED + BLESSED (slice 3) |
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
| CHECK constraints (`ColumnChecks`) — definitions follow the logical column substitution (`LogicalColumnEmission` v2) | COVERED + BLESSED (slice 3; authored with physical refs, emitted logical) |
| Single-column CHECK beneath its attribute (structural anchor: exactly one referenced column); multi-column CHECK at table level | COVERED + BLESSED (slice 3b) |
| Triggers | COVERED — definition bodies still carry PHYSICAL table/column references (**known-unblessed**; rewrite is its own slice — they reference table names too) |
| Temporal (system-versioned) tables | TODO |
| Sequences | TODO |

### Stream realization
| Variance | Status |
|---|---|
| GO framed by blank lines on both sides (slice 2) | COVERED (`stream.sql`) |
| Constraint ladder (4/8/12 indentation) on the flat stream | COVERED |
| `EXECUTE [sys].[sp_addextendedproperty]` canonical form | COVERED (documented accepted deviation) |
| Per-table file: V1's rendered form — framed GO between statements (never trailing), constraint ladder, wrapped EXEC | COVERED + BLESSED (slice 3, operator decision — supersedes the prior no-GO contract) |

### Data lanes — the per-lane outputs (WP6 step 3; reconciled with minimize-surface, DECISIONS 2026-06-13)
The pipeline emits the per-lane files `Data/StaticSeeds.sql` /
`Data/MigrationData.sql` / `Data/Bootstrap.sql` alongside the fused global
`Data/seed.sql`, but **self-minimizing**: a per-lane file is written only when
**≥2 lanes carry content**. With a single active lane the fused seed IS that
lane, so a per-lane file would byte-duplicate it — exactly the redundancy the
operator's minimize-surface directive forbids. Consequence for THIS corpus:
the operator-config golden path supplies no migration/bootstrap context (and
hydration doesn't run on the catalog-direct golden path), so the `master`
scenario has only the **static** lane and pins **only `Data/seed.sql`** — no
per-lane files. The per-lane split (StaticSeeds vs MigrationData, distinct
from the fused) is witnessed at the composer level in
`DataEmissionComposerTests` (a two-lane catalog), not pinned in the golden,
because pinning it here would duplicate `seed.sql`. The fused `seed.sql`
already pins the static MERGE/Phase-2 shapes and the IDENTITY_INSERT bracket
(`Tier`). The IDENTITY-PK static kind is COVERED (`Tier`, step 1); the EXCEPT
validate-before-apply prelude rides a later slice (WP6.6 / C2).

### Data lanes
| Variance | Status |
|---|---|
| Static seed MERGE (idempotent, CDC-aware predicate) | COVERED (`Country` rows) |
| Phase-2 deferred-FK UPDATE (nullable FK cycle between static kinds) | COVERED (`RegionA`/`RegionB` cycle) |
| Delete-scope arm (`WHEN NOT MATCHED BY SOURCE … DELETE` under the term predicate) | COVERED — **folded into the `master` scenario** (DeleteScope resolves per kind, so the scoped kind carries the DELETE arm while other static kinds stay plain MERGEs; DECISIONS 2026-06-13 take 2). **First-recording finding:** the term resolves against the POST-CHAIN catalog (after `LogicalColumnEmission`), so under the default logical rendition the term must name the LOGICAL column — the `DeleteScopePolicy` doc's "terms name PHYSICAL columns" is stale for that rendition. Doc/semantics reconciliation rides the plan's WP4 follow-on |
| Static kind with IDENTITY PK (IDENTITY_INSERT handling) | COVERED + BLESSED (WP6 step 1, `Tier` — the MERGE is bracketed by `SET IDENTITY_INSERT … ON/OFF` as ONE GO batch; DECISIONS 2026-06-13) |
| Bootstrap lane content | COVERED at the composer (WP6 step 2 — Bootstrap delegates to the static-seeds renderer; per-lane split witnessed in `DataEmissionComposerTests`). Pipeline content arrives via hydration (step 4); not golden-pinned (single-lane in the golden path) |
| MigrationData lane content | COVERED at the composer (WP6 step 3 — the per-lane split renders the migration lane distinct from static; witnessed in `DataEmissionComposerTests`). Not golden-pinned (the golden path supplies no migration context) |
| Row batching ≥ threshold | TODO (armed perf trigger) |
| EXCEPT validate-before-apply prelude (opt-in) | TODO — plan WP6 step 6 (C2) |

### Negative invariants (asserted on every scenario, not byte-pinned)
1. Per-table `GO` is framed by a blank line on both sides and never trailing (operator decision, slice 3 — supersedes the prior no-GO invariant).
2. No duplicate `(schema, FK constraint name)` across the emitted set.
3. No FK constraint sourced from a derived-inverse reference (no FK on a
   pure-target kind like `User`).
4. Every artifact ends with a newline; no `\r\n` anywhere (slice 3 restored
   the termination — per-table bodies render via `Render.toText`).
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
