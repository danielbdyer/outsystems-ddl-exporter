# Chapter 2 audit — subagent #3 (OSSYS chapter completeness)

**Dispatched:** session 25 (chapter-2 close).
**Scope:** OSSYS chapter completeness audit. Four dimensions:
translation rules vs. adapter implementation; V1 JSON fields vs.
won't-carry-forward list; Active deferrals scan (per session-24
amendment to chapter-mid-audit); chapter-close readiness on the
OSSYS arc.
**Verdict totals:** 1 CRITICAL, 11 MINOR (M1–M13), 7 OPEN (O1–O7).
**Disposition:** all CRITICAL + MINOR resolved at session 25
commits 1, 4, 5, 6; all OPEN resolved at session 25 commit 7
(`DECISIONS 2026-05-21 — Chapter 2 close: OPEN-question
resolutions`). The cross-cutting observation about silent drops
clustering at the V1-input-envelope-not-walked surface produced
the chapter-close ritual's eighth item (V1-input-envelope walk —
codified at session 25 commit 3).

---

## Dimension 1: Translation rules vs. adapter implementation

### CRITICAL findings

None. Every codified rule (1–25) is implemented in
`CatalogReader.fs` consistent with its wording. The implicit-
coverage class that session 24 surfaced has no additional
silently-uncovered branches at the rule level.

### MINOR findings

**M1 — Rule 11 supersession not flagged in the original-list
table.** `DECISIONS.md:4935` still presents rule 11 as
`isExternal: true → ExternalDirect`. The session-20 amendment
explicitly supersedes it with rule 17
(`ExternalViaIntegrationStudio`), and `parseOrigin` at
`CatalogReader.fs:337-338` implements the superseded form. The
amendment-discipline says "preserve original; supersede later";
it works for prose but the running-list table still claims rule
11 as live. A chapter-close hygiene pass should annotate rule 11
as superseded-by-17 inline. Today rule 11 is the only rule in
the table whose stated V2 output diverges from what the adapter
produces.

**M2 — Rule 13's "Ignore" branch not exercised by fixture.** Rule
13 codifies a five-case mapping for `reference_deleteRuleCode`
(`Delete`, `Protect`, `Ignore`, `SetNull`, `null`).
`parseDeleteRule` at `CatalogReader.fs:255-266` implements all
five; the only delete-rule test fixture (`v1ReferenceFixture` at
the reference slice) exercises `"Protect"` only. The other four
branches — including the load-bearing `"Ignore" → NoAction`
collapse that resolves the unreachable-`DeleteRuleIgnored`-keep-
reason finding — ship without fixture coverage. Same shape as
the session-24 static-entity finding: an implementation-
conditional branch the differential test does not enter.

**M3 — Rule 4's "isAutoNumber read but discarded" claim is
stale.** Rule 4's rationale says `isAutoNumber` is "read but
discarded today." The adapter does **not** call `getProperty` /
`getBool` on `isAutoNumber` anywhere in `parseAttribute`; it is
silently ignored, not read. Trivial wording fix; the behavior is
correct.

### OPEN questions

**O1 — Rule 21 ambiguity for the `isPrimary: true, isUnique:
false` subspace.** Rule 21 says `Index.IsPrimaryKey = isPrimary`
(direct). Rule 20 says `Index.IsUnique = isUnique` (direct).
Both are coordinate maps, but V1's domain disallows the
combination (a non-unique PK is not constructible at SQL level),
and the V2 `Index` DU at `Catalog.fs:159-164` makes both bools
independent. The adapter at `CatalogReader.fs:432-484` does not
validate the combination; a malformed input where `isPrimary:
true, isUnique: false` would produce a structurally-incoherent
V2 `Index`. Plausible resolutions: (a) make this a parse error
(`adapter.osm.invalidIndexFlags`); (b) coerce `isPrimary: true ⇒
isUnique := true`; (c) stay literal, document the bound. (c) is
the smallest honest-now choice and matches the existing rule
wording. Worth a short DECISIONS entry at chapter close, even
if no fixture forces it — the question is whose coherence
enforcement this is.

**O2 — Rule 18's module-level `isActive` deferral is implicitly
different in shape from what the adapter does.** Rule 18 at
`DECISIONS.md:5299` filters inactive entities and inactive
attributes. Rule 18's text at `DECISIONS.md:5301-5308` notes
that module-level `isActive: false` "defers until a fixture
forces the question." But `parseModule` at
`CatalogReader.fs:603-645` does not filter modules by
`isActive` at all; it consumes the module unconditionally. The
adapter behavior on a `module.isActive: false` input today is:
include the module and all its (active) entities — a
potentially-surprising silent default that the won't-carry-
forward list does not name. Worth deciding before chapter 3
whether the adapter should emit a parse error, drop the module,
or carry-through silently.

---

## Dimension 2: V1 JSON fields vs. won't-carry-forward list

I walked `SnapshotJsonBuilder.cs` end-to-end. The fields V1
emits to `osm_model.json` are: top-level `exportedAtUtc`; per-
module `name`, `isSystem`, `isActive`, `entities`; per-entity
`name`, `physicalName`, `isStatic`, `isExternal`, `isActive`,
`db_catalog`, `db_schema`, `meta` (when description present),
`attributes`, `relationships`, `indexes`, `triggers`; per-
attribute `name`, `physicalName`, `originalName`, `dataType`,
`length`, `precision`, `scale`, `default`, `isMandatory`,
`isActive`, `isIdentifier`, `isReference`, `refEntityId`,
`refEntity_name`, `refEntity_physicalName`,
`refEntity_isActive`, `reference_deleteRuleCode`,
`hasDbConstraint`, `external_dbType`,
`physical_isPresentButInactive`, `onDisk` (a structured sub-
object: `isNullable`, `sqlType`, `maxLength`, `precision`,
`scale`, `collation`, `isIdentity`, `isComputed`,
`computedDefinition`, `defaultDefinition`, `defaultConstraint`,
`checkConstraints`), `meta`; per-relationship `viaAttributeId`,
`viaAttributeName`, `toEntity_name`, `toEntity_physicalName`,
`deleteRuleCode`, `hasDbConstraint`, `actualConstraints`; per-
index `name`, `isPrimary`, `kind`, `isUnique`, `isPlatformAuto`,
`isDisabled`, `isPadded`, `fill_factor`, `ignoreDupKey`,
`allowRowLocks`, `allowPageLocks`, `noRecompute`,
`filterDefinition`, `dataSpace`, `partitionColumns`,
`dataCompression`, `columns` (each: `attribute`,
`physicalColumn`, `ordinal`, `isIncluded`, `direction`); per-
trigger `name`, `isDisabled`, `definition`.

The chapter's won't-carry-forward list explicitly names:
`attributes[].originalName`, `attributes[].length / precision /
scale`, `attributes[].isAutoNumber`, `attributes[].isActive`
(resolved into rule 18), `attributes[].external_dbType`,
`attributes[].physical_isPresentButInactive`,
`entities[].relationships[]`, `entities[].triggers[]`,
`entities[].db_catalog`, `entities[].meta`, top-level
`exportedAtUtc`, `attributes[].refEntityId`,
`attributes[].refEntity_physicalName`,
`attributes[].reference_hasDbConstraint`, plus the index-level
fields `kind`, `isPlatformAuto`, storage/perf attrs (`isDisabled`,
`isPadded`, `fill_factor`, `ignoreDupKey`, `allowRowLocks`,
`allowPageLocks`, `noRecompute`), `filterDefinition`,
`dataSpace`, `partitionColumns`, `dataCompression`,
`columns[].direction`, `columns[].physicalColumn`.

### CRITICAL findings

**C1 — `triggers[]` has Trigger metadata coverage drift between
ADMIRE and the won't-carry-forward list, but is otherwise
covered.** The OSSYS ADMIRE entry at `ADMIRE.md:2354-2360` names
triggers as won't-carry; rule 1's session-18 won't-carry-forward
list at `DECISIONS.md:4977-4980` also names triggers. Trigger
emission shape (`name`, `isDisabled`, `definition`) is in V1's
JSON. The adapter does not consume it; both surfaces explicitly
say so. This is **not** a CRITICAL gap — the field is properly
named. Mentioning it because the briefing called it out
specifically.

**C2 — Per-attribute `onDisk` sub-object is the largest unnamed
silent drop.** V1 emits the entire `onDisk` object per attribute
(eleven fields: `isNullable`, `sqlType`, `maxLength`,
`precision`, `scale`, `collation`, `isIdentity`, `isComputed`,
`computedDefinition`, `defaultDefinition`, `defaultConstraint`,
`checkConstraints`). The adapter does not read it. The won't-
carry-forward list names `attributes[].length / precision /
scale` (which appear at attribute root) and `ADMIRE.md:2361-2364`
names `ComputedDefinition` for IR-refinement deferral, but the
**structured `onDisk` envelope itself is never named**.
`sqlType`, `maxLength`, `collation`, `isIdentity`,
`defaultConstraint` (with `defaultDefinition`), and
`checkConstraints` carry semantic content (physical-reality
reconciliation that is the whole point of V1's chain) and have
no axis in V2's IR. The drop is silent — neither in DECISIONS'
won't-carry list nor in the ADMIRE list. This is the single
largest information-content drop in the audit. Surface as
**CRITICAL**: a V1 envelope of eleven semantically-loaded fields
disappears at the V2 boundary without a codified rule.
Resolution candidates: (a) name `attributes[].onDisk` on the
won't-carry-forward list with the disposition that the per-
column-reality envelope flows through
`Projection.Adapters.Sql/ProfileSnapshot.fs` rather than through
the OSSYS adapter (matches V2's structure-vs-evidence split);
(b) recognize that V1's `onDisk` is V2's Profile content, name
it explicitly as the JSON-side analog of Profile and route
accordingly. Either resolution is small; the silent drop is the
issue. Location: `SnapshotJsonBuilder.cs` does not write `onDisk`
directly but the SQL emits it at
`outsystems_metadata_rowsets.sql:770-790`; the JSON projection
includes it in `#AttrJson`.

### MINOR findings

**M4 — `module.isSystem`, `module.isActive`, `entity.isSystem`
(note: V1 SnapshotJsonBuilder writes `module.isSystem` and
`module.isActive`; entity-level `isSystemEntity` is in the
rowsets but **not** the JSON, per the lossiness-class entry).**
Module `isSystem` and `isActive` are read by
`SnapshotJsonBuilder` and emitted to JSON; the adapter consumes
neither. `isSystem` is unmentioned in the won't-carry list;
`isActive` at module level is half-mentioned in rule 18's
deferral but not in the adapter's behavior. Location:
`SnapshotJsonBuilder.cs:120-121, 168-169`. The won't-carry-
forward list should name `module.isSystem` (carries the same
semantic content as `entity.isSystemEntity` at module level —
V2's Origin DU collapses both) and `module.isActive` explicitly.

**M5 — Per-attribute `default` field is an unnamed silent drop.**
V1 emits `attributes[].default` (the `DefaultValue` from `#Attr`)
at `outsystems_metadata_rowsets.sql:757`. Carries semantic
content (column default value); V2 IR has no per-attribute
default axis. Not in the won't-carry-forward list. Same shape
as the `onDisk` envelope but at root level. Location: not
addressed in DECISIONS.

**M6 — `relationships[].actualConstraints`,
`relationships[].toEntity_physicalName`,
`relationships[].deleteRuleCode`,
`relationships[].hasDbConstraint`,
`relationships[].viaAttributeId/Name`.** Rule 14 names
`relationships[]` as a whole as won't-carry-forward (the adapter
walks `attributes[isReference=1]`). The per-relationship-element
fields are subsumed; the disposition is correct. Surfacing it
as MINOR only because the disposition rests on rule 14's "if a
future fixture surfaces a relationship in `relationships[]` but
NOT in `attributes[isReference=1]` (or vice versa), the
divergence forces a cross-check." The cross-module FK slice
(deferring to chapter 3) is the natural fixture for this:
cross-module references may not appear in
`attributes[].refEntityId` because V1's resolved-by-id lookup
may fail across modules, in which case `relationships[]` may
carry information `attributes[]` does not. Worth surfacing in
chapter 3's handoff (see Dimension 4).

**M7 — `attributes[].refEntity_isActive` is an unnamed silent
drop.** V1 emits the FK target's `isActive` flag at
`outsystems_metadata_rowsets.sql:765`. Could matter for cross-
module FK or for cases where the source is active but the
target was retired. The adapter ignores it; not in the won't-
carry-forward list.

### OPEN questions

**O3 — `attributes[].onDisk` should join V2 Profile, not the
OSSYS adapter.** The adapter's contract is structure (Catalog);
evidence (Profile) flows through
`Projection.Adapters.Sql/ProfileSnapshot.fs`. V1's `onDisk`
envelope carries physical-reality reconciliation that V2 calls
Profile. But V1 emits it inside `osm_model.json` rather than
through V1's separate profile-extraction pipeline. The OSSYS
chapter has not asked: should the OSSYS adapter co-extract
Profile evidence from `onDisk`, or should it be entirely
dropped at the OSSYS boundary because Profile flows through a
different adapter? Most plausible: drop at OSSYS, named
explicitly in won't-carry-forward, document that V2's structure-
vs-evidence split puts `onDisk` on the Profile side regardless
of which V1 chain emits it. Worth explicit decision before
chapter 3.

---

## Dimension 3: Active deferrals scan

### CRITICAL findings

None silently fired beyond what subagents #1 and #2 already
cashed out.

### MINOR findings

**M8 — `SnapshotRowsets` and `LiveOssysConnection` deferred
variants are not in the Active deferrals index.**
`CatalogReader.fs:36-68` documents both as deferred (one as
canonical-resolution-planned, one as reserved). DECISIONS at
`2026-05-15 — OSSYS adapter parse signature` and the session-20
amendment to the OSSYS translation rules entry codify the
decisions. Neither appears as a row in the Active deferrals
index. This is the same gap subagent #2 flagged. The triggers
are concrete: `SnapshotRowsets` triggers when the JSON-
projection-lossiness class needs unblocking (any of: A1 bound
resolution; `EspaceKind` distinction; `isSystemEntity` evidence;
future class members); `LiveOssysConnection` triggers when V2
needs to operate without V1's chain. Adding both as rows ensures
chapter 3's canary chapter audit catches them. The OPEN question
subagent #2 surfaced — "adapter-boundary deferrals scope in the
Active deferrals index" — is concretely answered by these two:
yes, they belong.

**M9 — Cross-module FK IR refinement is not in the Active
deferrals index.** Rule 16's same-module assumption
(`DECISIONS.md:5051`) names the cross-module case as "re-open
trigger when a fixture surfaces it." The chapter-2 close handoff
explicitly defers the cross-module FK slice to fresh context.
The Active deferrals index has the cross-catalog FK row
(architectural-IR refinement) but not the cross-module FK row,
despite cross-module FK being structurally adjacent (and
arguably the trigger-firing precondition for cross-catalog FK).
Same shape as the `Modality.Inactive`, `IsExternalEntity` Origin
three-way, `physical_isPresentButInactive`, filter-definition-on-
indexes, and auto-number-axis deferrals named in the chapter —
none are in the Active deferrals index, all have explicit re-
open triggers in the OSSYS rules amendments.

**M10 — `RequireQualifiedAccess` retrofit row is more stale than
subagent #2 caught.** Subagent #2 noted "no modification since
session 8" is partially stale because `MissingTarget` was added
at session 19. Reviewing rule 13 again: the same DU keep-reason
space (`ForeignKeyKeepReason`) had `DeleteRuleIgnored` resolved
as unreachable across sessions 18–19 (rule 13's full-table
resolution). This is a substantive modification of the DU's
interpretive contract even if the variants didn't change shape;
the retrofit row still claims "no modification since session 8."
Same family of drift subagent #2 flagged.

### OPEN questions

**O4 — Should "won't-carry-forward list" itself be a tracked
Active-deferrals-shaped surface?** The won't-carry-forward list
grew over six slices and at chapter close still has gaps (C2,
M4, M5, M7 above). The Active deferrals index is currently the
only structural propagation-correction surface. The OSSYS
chapter has produced a parallel surface (the won't-carry-forward
list per the ADMIRE entry plus the rule-amendments) that
operates similarly but does not have the Active-deferrals-index
discipline of "audit must scan it." Worth a chapter-close
decision on whether the won't-carry-forward list deserves the
same discipline, or whether the trace-before-fixture pattern
alone suffices as the propagation-correction practice for V1↔V2
chapters.

---

## Dimension 4: Chapter-close readiness on the OSSYS arc

### MINOR findings

**M11 — `CHAPTER_2_CLOSE.md`'s "From session 22 audit" status
mentions 5 slices via subagent #1 reference; the file's own arc
summary correctly says 6 slices; `ADMIRE.md:2068` says "5
slices" and "Twenty-three rules" (stale by 2 rules + 1 slice).**
ADMIRE's drift is concrete: the entry was last updated mid-
chapter and chapter-close hygiene would normalize to 6 slices
and 25 rules. The chapter-close ritual's "ADMIRE entry currency"
item naturally catches this.

**M12 — Open questions Q4 ("Async/Task at the OSSYS boundary")
is now structurally resolvable.** Q4 in
`CHAPTER_2_CLOSE.md:135-138` says the wrapper is overhead until
DB-touching variants land. With `SnapshotRowsets` chosen as
canonical (session 20) and likely to involve some I/O, the
question's "until then" is now bounded — chapter-3 sequencing
brings the resolution. Worth marking as "addressed: deferred to
chapter 3 canary chapter where the I/O profile crystallizes."

**M13 — Chapter-close ritual's seven items vs the chapter-2
reality.** The ritual (DECISIONS 2026-05-14) names: Active
deferrals scan; contract-vs-implementation walk; CLAUDE.md /
README.md staleness checks; HANDOFF + CHAPTER_1_CLOSE.md scope;
fresh-eye walk; operating-disciplines table currency. The
CHAPTER_2_CLOSE.md scaffold currently accumulates findings but
has not yet structured the seven-item walkthrough. Status is
correct (it's an "in-flight scaffold"); chapter-close synthesis
at session 25 needs to produce the seven-item walkthrough
explicitly.

### OPEN questions / forward-signals to chapter 3

**O5 — Cross-module FK forward-signal needs a richer handoff
than `CHAPTER_2_CLOSE.md` currently carries.** The scaffold
names cross-module FK as "highest-priority deferred slice" and
points at rule 16's same-module assumption. What the chapter-3
handoff should additionally carry:

- The shape of V1's `relationships[]` entries with cross-
  attribute resolution: `viaAttributeId`, `viaAttributeName`,
  `toEntity_name`, `toEntity_physicalName`, plus
  `actualConstraints` (the FK reality from `#FkReality`). Rule
  14 names this as won't-carry but the cross-module FK slice
  may force walking `relationships[]` instead of
  `attributes[isReference=1]` because V1's `RefEntityId` is a
  numeric within-module pointer that cannot resolve cross-
  module. The handoff document should warn the cross-module
  agent: rule 14's "walk attributes" assumption may not hold
  cross-module.
- The `attributes[].refEntityId` numeric ID is V1's internal
  database ID and does not carry module context; cross-module
  resolution likely requires walking `relationships[]` for
  `toEntity_name` plus a module-scope-resolution rule.
- The same-module assumption from rule 16 is structural (same
  module name in the synthesized SsKey); cross-module FK
  requires either a `refEntity_module` field (V1 doesn't emit),
  a cross-module name-collision-handling rule (problematic), or
  the operator's preferred resolution. The trace-before-fixture
  pattern applies; the chapter-3 agent should trace V1's cross-
  module FK encoding before writing the fixture.

**O6 — `SnapshotRowsets` chapter handoff: the lossiness-class
shape should travel with it.** When `SnapshotRowsets` opens
(chapter 3 or later), the agent inherits a class to resolve, not
three bugs. The session-20 strengthening at
`DECISIONS.md:4872-4920` names this. The chapter-2 close
synthesis should preserve this in a forward-signal that's
discoverable from `HANDOFF.md` or the chapter-3 chapter-open
document — the chapter-2 work has earned the canonical-
resolution-as-class framing.

**O7 — DacFx canary as the chapter-3 entry point.** Subagent
#2's CRITICAL was cashed out in session 24 commit 1 with a
tighter trigger condition tied to chapter 3's canary chapter.
The chapter-2 close should make that trigger sequencing
explicit so the chapter-3 agent reads "DacpacEmitter is the
second deliverable in canary, after the read-side adapter
integration."

---

## Cross-cutting observations

**The phenomenon: silent drops cluster at the boundary's "what
V1 sends but V2's IR has no axis for" surface, and the
chapter's instinct is to grow the won't-carry-forward list under
fixture pressure rather than under V1-fields-walked pressure.**
Six fixtures have surfaced six rule-bearing surfaces (modules/
kinds/attrs; references; origin; activity; indexes; static-
modality), but the V1 input surface contains additional
information envelopes — the per-attribute `onDisk` sub-object,
the per-attribute `default`, module-level `isSystem`/`isActive`,
the relationship-level `actualConstraints` and `hasDbConstraint`,
the FK target's `isActive` — that no fixture has yet forced the
chapter to consider, because the fixtures stress V2-IR shape
coverage rather than V1-input shape coverage. The chapter has
implicitly assumed that the trace-before-fixture discipline
catches what matters; the trace traced V1 fields **for the
slice's question** rather than walking V1's full envelope at
chapter open. This is a different discipline gap from the
implicit-coverage finding at session 24. Session 24 was about
V2-implementation-paths-not-fixture-exercised; this is about
V1-input-envelope-not-walked. The won't-carry-forward list grew
opportunistically; it has never been audited against
`SnapshotJsonBuilder.cs` field-by-field until this audit.

The companion observation: **the JSON-projection-lossiness class
and the won't-carry-forward list are dual surfaces that the
chapter has not yet distinguished sharply.** The lossiness class
is "V2 doesn't see X because V1's JSON projection strips it"
(resolved by `SnapshotRowsets`); the won't-carry-forward list is
"V2 sees X but V2's IR has no axis for it" (resolved by IR
refinement under consumer demand). The `onDisk` envelope is an
instructive case: it's emitted to JSON (visible to V2) and V2's
IR has no axis for it (won't-carry); but V2 *does* have a
parallel structure (Profile) that is the natural home for this
evidence. The chapter has not asked whether the OSSYS adapter
should silently drop `onDisk` (current behavior), should
explicitly drop `onDisk` (won't-carry-forward entry), or should
split-and-route `onDisk` to the Profile-construction path. The
trace-before-fixture pattern as currently practiced does not
surface this third option because it traces only the slice's
specific field. A chapter-close audit-by-walk against
`SnapshotJsonBuilder.cs` (the discipline this audit performed)
is the natural complement, and worth codifying as a chapter-
close ritual item for any V1↔V2 translation chapter.
