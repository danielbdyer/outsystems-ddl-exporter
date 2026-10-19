# Chapter 4.7 close — Refactor bundle + sibling-wrapper discipline codification

**Sessions:** chapter 4.7 opened + slices α + β (+ β fix-forward) + cleanup + γ shipped in one session arc (2026-05-17). Branch `claude/review-chapter-close-Rqo0x`. Slice commits: open `85de27f` → α `3d50bfe` → β `9e98ba1` → β fix-forward `6951688` → cleanup `76c557a` → discipline codification `1435e28` → γ sweep `5ee4eaf` → δ (this commit).

This document discharges chapter 4.7's eight-item close ritual. Three refactors shipped + an unprincipled wrapper retired mid-flight + the sibling-wrapper distinguishing test codified as discipline.

---

## Why this close

The chapter's strategic frame named three refactors reducing per-item touch cost for the chapter-4.6-close shortlist's 10 remaining forward-signal items. The session arc delivered all three plus a mid-flight tech-debt discovery + cleanup that sharpened the V2-no-back-compat discipline.

---

## What shipped (slice arc α + β + γ + cleanup + discipline codification)

### Slice α — Adapter consolidations (`3d50bfe`)

- **`CatalogReader.getOptionalIntFlag : JsonElement -> string -> bool -> bool`** — mirrors V1's `ISNULL(<col>, <default>)` SQL idiom; retires the `match getIntFlag with Ok v -> v | Error _ -> default` boilerplate.
- **`CatalogReader.getOptionalBool : JsonElement -> string -> bool -> bool`** — sibling for V1-projected JSON booleans (e.g., `isPlatformAuto`).
- Existing pattern usages migrated: chapter 4.6 slice α `reference_hasDbConstraint` pickup + chapter 4.6 slice β `isPlatformAuto` pickup.

### Slice β + fix-forward — Diagnostics-aware emitter signature (`9e98ba1` + `6951688`)

- **`buildCreateIndex` (canonical)** returns `Diagnostics<CreateIndexStatement>`. Surfaces filter-parse failures as Warning entries (Source=emitter:ssdt; Code=emit.ssdt.index.filterParseFailure) via the existing `tryParseFilterWithDiagnostics` helper.
- **Initial slice β** shipped two surfaces (legacy silent-skip `buildCreateIndex` + Diagnostics-bearing `buildCreateIndexWithDiagnostics`). The operator flagged the unprincipled wrapper; **fix-forward** collapsed to a single canonical Diagnostics-bearing surface. Callers explicitly drop diagnostics via `.Value` at the call site.
- 7 new tests in `BuildCreateIndexDiagnosticsTests.fs`.
- `ScriptDomBuild.buildStatement` dispatcher drops Diagnostics via `(buildCreateIndex idx).Value` with a comment naming the deferred Diagnostics-aware dispatcher.

### Cleanup — Two middle-tier wrappers retired (`76c557a`)

Three-agent audit surfaced ~10 sibling-wrapper candidates across Core / Targets / Adapters / Pipeline / CLI. Careful reread revealed most were principled F# default-argument idioms; only 2 were genuinely overdifferentiated middle-tier wrappers (tech debt):

- **`DataEmissionComposer.composeWithMigration`** retired. Middle-tier between `compose` (defaults both contexts) and `composeFull` (takes both explicitly). 5 test call sites migrated to `composeFull ... UserRemapContext.empty`.
- **`MigrationDependenciesEmitter.emitWithUserRemap`** retired. Middle-tier running TopologicalOrderPass internally before delegating to emitWithTopo. 3 test call sites migrated to explicit `let topo' = (TopologicalOrderPass.runWith TreatAsCycle catalog).Value in emitWithTopo …`.

### Discipline codification (`1435e28`)

- **`DECISIONS 2026-05-17 (chapter 4.7 cleanup) — Sibling-wrapper discipline`** entry codifies the distinguishing test:
  > Does the wrapper **hide information the caller might want** (tech debt; collapse + explicit `.Value`), or does it **supply a private/computed default the caller couldn't otherwise access** (F# default-argument idiom; principled)?
- **N+1 corollary (overdifferentiated middle-tier):** For a callable with N defaultable axes, two surfaces (zero-default + full-explicit) are principled; mid-tier wrappers defaulting subsets are tech debt.
- `CLAUDE.md` operating-disciplines table gains a Sibling-wrapper discipline row pointing at the DECISIONS entry.

### Slice γ — IRBuilders retroactive sweep (`5ee4eaf`)

- **`IRBuilders.mkReference`** added (Reference IRBuilder; was missing; chapter 4.7 slice γ prep step).
- **30 literals migrated** across 15 test files via Python sweep:
  - 9 Index literals (SsdtDdlEmitterTests + OsmCatalogReaderDifferentialTests + DiagnosticsEndToEndTests + UniqueIndexRulesTests + UniqueIndexPassTests).
  - 21 Reference literals (across 14 files).
- Migration approach: regex-based `{...}` block detection + field-set comparison against `IRBuilders.mkX` signature defaults; per-field comment stripping handles inline `//` comments inside literal values; non-default fields preserved as `with`-syntax overrides; default fields dropped.
- -176 LOC net (literals are mechanically more compact via the builder shorthand).
- Future Index / Reference field additions touch ~2 sites (IRBuilders default + optional setter) vs the pre-sweep ~30 literal-site cost — **85%+ reduction in future-touch cost**.

---

## Eight-item chapter-close ritual

### 1. Active deferrals scan

| Deferral | Status |
|---|---|
| **`buildCreateIndex` silent-skip wrapper** (chapter 4.6 slice γ forward signal — "Diagnostics-aware emitter signature") | ✅ **Retired** at slice β fix-forward. |
| **`MigrationDependenciesEmitter.emitWithUserRemap`** middle-tier | ✅ **Retired** at cleanup commit. |
| **`DataEmissionComposer.composeWithMigration`** middle-tier | ✅ **Retired** at cleanup commit. |
| **Sibling-wrapper distinguishing test** | ✅ **Codified** at DECISIONS 2026-05-17 + CLAUDE.md operating-disciplines row. |
| **IRBuilders sweep on Index + Reference** | ✅ **Shipped** at slice γ (30 literals migrated). |
| **Attribute / Kind / Module / Catalog literal sweep** | Untriggered (out of chapter 4.7 scope; Python migration pattern shipped + reusable for future Attribute/Kind/Module/Catalog sweeps). |
| **F# default-argument idiom siblings** (Compose.write; ManifestEmitter.build; *.emit variants in Static/Bootstrap/MigrationDependencies) | **Preserved as principled** per the distinguishing test. Documented in DECISIONS 2026-05-17. |

Three new deferrals codified at this close (forward signals):

- **Attribute / Kind / Module / Catalog IRBuilders sweep** — same Python pattern; richer field sets per type. Trigger: an agent ships the per-type migration when they have time-budget.
- **WithDiagnostics emitter signature lift for buildCreateTable / buildSetExtendedProperty / buildMergeStatement / buildUpdateStatement** — Diagnostics-aware variants for the other ScriptDomBuild emitters when each builder surfaces a Diagnostics source (e.g., CHECK constraint parse validation for buildCreateTable; extended-property name validation; MERGE expression parsing). Per IR-grows-under-evidence.
- **`UserFkReflowIntegrationTests.fs` Reference literal** — left as-is at slice γ due to mid-literal inline `//` comments that don't match the standard pattern. Trigger: agent willing to hand-migrate it.

### 2. Contract-vs-implementation walk

Chapter open §1 named the contract: "three refactors reducing per-item touch cost for the 10 remaining forward-signal items." Every clause is implemented:

- Slice α (adapter consolidations): `getOptionalIntFlag` + `getOptionalBool` shipped; 2 existing pattern usages migrated. Future adapter int-flag pickups consume the new primitives.
- Slice β (Diagnostics-aware emitter signature): canonical `buildCreateIndex` returns `Diagnostics<CreateIndexStatement>`; future emit-time consumers follow the pattern.
- Slice γ (IRBuilders sweep): 30 literals migrated; `mkReference` added; future IR field additions touch ~2 sites instead of ~30.

Plus the unscheduled-but-load-bearing tech-debt cleanup + discipline codification.

### 3. CLAUDE.md staleness check

Operating-disciplines table current; updated at the discipline codification commit with the Sibling-wrapper discipline row.

### 4. README.md staleness check

Test baseline 1348 → **1354 non-canary** (+7 net across the chapter — slice β added 7; cleanup commits collapsed 1; slice α + γ added 0). Add chapter-4.7-close status section to README.

### 5. HANDOFF.md scope

New chapter-4.7 close prologue at this commit. Names load-bearing (sibling-wrapper discipline; mkReference + sweep recipe; getOptionalIntFlag + getOptionalBool; Diagnostics-bearing buildCreateIndex) + retained forward signals.

### 6. Fresh-eye walk (cross-document drift)

- `V2_DRIVER.md` — chapter 4.7 not previously listed; folded as Phase-5.8 entry in BACKLOG.
- `BACKLOG.md` — adds Phase 5.8 section.
- Chapter 4.6 close doc's "Diagnostics-aware emitter signature" forward signal retired by slice β fix-forward.

### 7. V1-input-envelope walk

No new V1 surfaces consumed at this chapter — refactors operate at V2 layer only.

### 8. AXIOMS.md amendment cash-out

No new amendments. Chapter operates within existing axioms — refactors preserve A18 amended; T1; A39; A40.

---

## Test count

- **1354 non-canary tests passing** (was 1348 at chapter 4.6 close; +6 net across this chapter).
- **~16 Docker-dependent canary tests** (skip-if-no-Docker).
- **Lint clean** across 27 rules.
- **Build clean** under `TreatWarningsAsErrors=true` everywhere.

---

## What's load-bearing going forward

- **Sibling-wrapper distinguishing test** is the canonical discipline future agents apply to "should this wrapper be deleted?" questions. No more re-walking the audit cycle.
- **`IRBuilders.mkReference` + the Python migration pattern** are reusable. Future IR field additions to Index / Reference touch IRBuilders + optional override site. Same pattern scales to Attribute / Kind / Module / Catalog when those sweeps run.
- **`buildCreateIndex` (Diagnostics-bearing)** is the canonical CREATE INDEX emitter contract. Pattern future emit-time Diagnostics consumers follow: each builder with a Diagnostics source returns `Diagnostics<TSqlStatement>`; consumers explicitly drop via `.Value` if they don't surface.
- **`getOptionalIntFlag` + `getOptionalBool`** consolidate the V1-int-flag pickup pattern. Future adapter slices use them without re-implementing.

---

## What's deferred (with explicit triggers)

### Attribute / Kind / Module / Catalog literal sweep

Same Python migration pattern; richer field sets. Trigger: agent willing to invest the time-budget. Expected payoff: similar 85%+ reduction in future-touch cost per IR-field addition for those types.

### WithDiagnostics emitter signature for other ScriptDomBuild builders

`buildCreateTable` / `buildSetExtendedProperty` / `buildMergeStatement` / `buildUpdateStatement` don't currently surface Diagnostics sources. Trigger: a Diagnostics source emerges (e.g., CHECK constraint parse validation; extended-property name validation; MERGE expression parsing). Pattern: each affected builder ships a Diagnostics-bearing canonical surface; consumers explicitly drop via `.Value`.

### `UserFkReflowIntegrationTests.fs` Reference literal

Left unmigrated at slice γ due to mid-literal inline `//` comments. Hand-migration available; defer.

---

## What this close enables

- **Future IR-field-addition touch cost** drops ~85% for Index / Reference. The next IR refinement chapter ships in ~2 sites instead of ~30.
- **Future emit-time Diagnostics consumers** consume the established pattern; no per-consumer wiring decisions to re-litigate.
- **Future sibling-wrapper review** resolves via the codified distinguishing test; agent confidence is independent of session context.

---

## Closing

Chapter 4.7 is **preparatory refactoring with an unscheduled mid-flight discovery + discipline codification**. The three slices delivered as planned (α / β / γ); the operator's "why this back-compat?" question on slice β surfaced a tech-debt pattern that needed sharper discipline; the audits + cleanup + codification deliver that discipline as canonical V2 surface.

Per V2_DRIVER's per-axis correctness stakes, this is **cross-cutting infrastructure work** (Lower per-axis stakes; high cross-chapter leverage). The chapter's slice scope was substantial mechanically (30 literal migrations + emitter signature lift + adapter consolidations + 5 documentation updates) but its leverage compounds across every future IR-field addition.

— Chapter 4.7 closed (2026-05-17).
