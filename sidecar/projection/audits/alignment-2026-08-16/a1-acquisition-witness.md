# Acquisition/witness plane — alignment audit workpaper

Auditor A1 · 2026-08-16 · scope: `src/Projection.Adapters.OssysSql/` (runner, 26-rowset SQL contract, codec), `src/Projection.Adapters.Osm/` (CatalogReader seam, rowset reader/translation), `src/Projection.Pipeline/{LiveModelRead,Sink*}.fs`, plus `Projection.Core/Strategies/PhysicalClaimRules.fs` where the witness vocabulary lands. All paths below are relative to `/home/user/outsystems-ddl-exporter/sidecar/projection/`.

---

## 1 Vocabulary inventory

**Wire acquisition (OBSERVED carriers):**
- `MetadataSnapshotRunner.SnapshotParameters` — 5-field record mirroring the script's declared parameters; `EntityFilterJson : string option` is raw JSON text (`MetadataSnapshotRunner.fs:60-67`). `defaultParameters` = the "show me everything" stance (`:72-79`).
- `ExpectedResultSets = 26` — the whole wire contract as one count (`MetadataSnapshotRunner.fs:815-816`); the walk is a hand-sequenced `read`/`skip` chain (`:958-1013`) with a terminal count assert (`:1036-1039`).
- 16 wire row records (`OssysModuleRow` … `OssysCapabilityRow`, `:145-405`) assembled into `MetadataSnapshot` (`:412-434`). `RowAtRest` capture-then-map discipline (`:460-467`).
- `OssysCapabilityRow` — rowset 26, the typed capability vector (19 `Has*` bools; `:386-405`), fed by the script's own `COL_LENGTH` probes (`Resources/outsystems_metadata_rowsets.sql:205-226`).
- Divergence readers (DERIVED knowledge as `DiagnosticEntry`): `columnRealityDivergences`, `columnStorageDivergences`, `primaryKeyDivergences`, `deleteRuleDivergences` (`:1228-1470`).

**The seam (DERIVED carriers):**
- `toBundle : MetadataSnapshot -> RowsetBundle` (`MetadataSnapshotRunner.fs:1471-1786`) — the known-lossy projection.
- `OssysRowsetReader.normalizeBundle : RowsetBundle -> RowsetBundle * DiagnosticEntry list` — the NAMED-erasure normalization (`OssysRowsetReader.fs:778-821`, codes `adapter.ossys.module.entityLess` / `adapter.ossys.kind.inactiveShadow`).
- `CatalogReader.SnapshotSource` = `SnapshotFile | SnapshotJson | SnapshotRowsets` (`CatalogReader.fs:74-89`); `LiveOssysConnection` reserved-by-comment.
- `LiveModelRead.fromConnectionWith` — the ONE acquisition funnel: run → witness → probe → normalize → scope-prune → parse (`LiveModelRead.fs:106-218`).

**The witness (the sink):**
- `SinkStore`: `connDigest16` (`SinkStore.fs:48-54`), `Manifest` (incl. `EnvLabel : string option`, `SourceFingerprints : (string * string) list`, `:71-89`), `WitnessOutcome` = `Persisted | Unchanged | SkippedScoped | Disabled | Failed` (`:238-251`), `isTotalAcquisition p = (p = defaultParameters)` (`:255-256`), `witnessWith` (`:264-378`), `nameEnvironment` (`:395-400`).
- `SinkDisplacement`: `KeyBasis = Native | Positional | Composite` (`:37-41`), `SinkTable` (16 cases, `:45-63`), `SinkRow` (closed sum, `:95-112`), `DomainTransition` (12 cases, `:117-130`), `Displacement` (before/after images + `Domain option`, `:135-144`), `canonical` / `diff` / `applyAll` / `norm`.
- `SinkJournal`: `JournalLine`, the fourth `LedgerSpec` instance (`:49-54`), `renderLine`/`parseLine`, `admitChain` (monotone sync), `replay` (T19).
- `SinkFreshness`: three `dbo.ossys_*` bellwether `targets` (`:29-32`), `render` = `"%d|%s|%s"` (`:41-42`), closed `Miss` taxonomy (6 cases, `:58-72`), pure `decide` table (`:85-119`). `Config.SinkPolicy = Off | Auto | Pinned` + `SinkSection.PerEnvironment` (`Config.fs:625-661`).
- `SinkRead`: `Resolved`, `resolve` (env-label scan; refusals `sink.envUnknown/envAmbiguous/syncNotFound/snapshotUnreadable`), `resolveByConnectionString` (R3's string side), `readCatalog` = load → `toBundle` → `parse`.
- `SinkClaims.assemble/adjudicateAll` → `PhysicalClaimRules` (`PhysicalClaim`, `ClaimSet {Schema; Table; Claims}`, `PhysicalClaimOutcome = Adopted | Contested | TombstoneOnly | Unclaimed`, `CorrespondenceProposal`).
- `SinkResidue.probeUniverse/sweep` (`OSUSR_%` universe minus claimed names). `SinkDiffView.catalogDiffOf` (erasure-witness inequality in the docstring). `SinkSyncRun` (`acquisitionParameters`, `SyncOutcome = Witnessed | Silent`).

## 2 The domain space

Independently of the current code, the acquisition/witness stage must be able to say:

1. **What was asked of the source** — the acquisition's scope, as a semantic fact (total vs scoped, and scoped along WHICH axes: module, system, lifecycle, attribute-activity, entity-filter), because scope decides witnessability, journal admission, and sink-servability.
2. **What the source said** — the source-shaped observation, at the source's own grain (server, catalog, schema, table, row), on the platform version it actually runs (capability drift as data), for any generation reader (V1/V2 legible).
3. **What we kept and what we let go** — every erasure between wire → snapshot → bundle → catalog as a named, closed, typed fact (the system's stated soul: `Ingest ∘ Project = identity` modulo *named, closed* erasures; "nothing is ever lost in silence").
4. **How each carried fact is known** — observed from the wire, derived by join/classification, declared by the operator (labels, policies, parameters), or assumed (defaults) — distinguished in types, since adjudication and diagnosis rank claims by exactly this.
5. **When and how often** — editions as addressable states, displacement between editions as a total row-grain carrier with a domain classification over it, staleness as (age, basis-of-knowledge), and the freshness decision as a closed table.
6. **Who claims physical reality** — metadata claims on physical tables at the grain physical reality actually has (catalog ⊃ schema ⊃ table), adjudicated totally, contested never silently.
7. **The witness's own outcomes** — every way a read can fail to become (or become) a sink state, named; and service with no upstream (offline, post-eject).

The multiplicities the hierarchy dimension demands: many environments; possibly >1 database per environment (OutSystems multi-catalog is domain-real — the IR's `TableId.Catalog` and the wire's `HasDatabaseName` capability both exist for it); ≥1 schema; many platform versions; two generations.

## 3 Findings

| ID | Class | Dimension | Axis | One-line claim | Anchor |
|----|-------|-----------|------|----------------|--------|
| A1-F1 | M6 (+M5) | Relational | Epistemic | `toBundle`'s erasures are folklore: silent reference drops, fabricated `"dbo"`/`"Text"` defaults, folded rowsets — prose-documented, never typed, while sibling `normalizeBundle` returns named erasure notices | `MetadataSnapshotRunner.fs:1619-1664,1497-1500,1566` |
| A1-F2 | M6 (+M2) | State | Epistemic | Acquisition totality is a structural-equality proxy (`p = defaultParameters`), not a reified concept; the "default = total" invariant lives in comments and a value-pin test | `SinkStore.fs:255-256`; `MetadataSnapshotRunner.fs:60-79` |
| A1-F3 | M2 (+M3) | Hierarchical | Ontic | The claims/residue plane collapses physical-table identity to bare NAME with `Schema = "dbo"` declared, though the witnessed edition carries observed schemas and the IR carries catalogs; env = exactly one (server, catalog) digest | `SinkClaims.fs:62,75`; `SinkResidue.fs:76-79`; `SinkRead.fs:65-68` |
| A1-F4 | M7 (+M6) | State | Epistemic | The journal's domain classification is write-only — `renderLine` persists the transition, `parseLine` restores `Domain = None`, and consumers silently default an unreadable ledger to `[]` | `SinkJournal.fs:119-141,305`; `Pipeline.fs:1973-74`; `Faces/Estate.fs:397-98` |
| A1-F5 | M4 | Semantic | Ontic | One domain concept — the extension-module marker — open-coded as three predicates with drift already present (one trims, two don't) on an explicitly unconfirmed empirical semantic | `SinkClaims.fs:54`; `SinkDisplacement.fs:286`; `OssysTranslation.fs:533` |
| A1-F6 | M6 | State | Epistemic | The freshness bellwether reading is erased to a rendered `"count\|maxPk\|content"` string before rest and decision, so `FingerprintMoved` can name the table but never the axis that moved | `SinkFreshness.fs:41-42,110-119`; `SinkStore.fs:88` |
| A1-F7 | M6 (+M5) | Relational | Ontic | The 26-rowset wire contract is positional folklore held by one count assert; rowset identity, disposition, and skip-reasons live in (multiply drifted) comments, not a driving definition site | `MetadataSnapshotRunner.fs:958-1013,1036-39,849,1016` |

### A1-F1 — the wire→bundle seam's erasures are unnamed (deepest)

**Evidence.** `toBundle` (`MetadataSnapshotRunner.fs:1471-1786`) is the projection every catalog-bearing consumer rides (live read `LiveModelRead.fs:166`, sink read `SinkRead.fs:150`, diff view `SinkDiffView.fs:31`). Inside it:
- References that fail the join vanish silently: `List.choose … | _ -> None` (`:1619-1664`) drops any `OssysReferenceRow` whose `RefEntityName` is `None` or whose `AttrId` has no attribute row. No notice, no counter.
- `DbSchema` is fabricated `"dbo"` when no `PhysicalTables` row joins (`:1497-1500`); `DataType` is fabricated `"Text"` when absent (`:1566`) — assumed values indistinguishable, downstream, from observed ones.
- Whole rowsets are dropped or folded — `PhysColsPresent`, `ForeignKeysReality`/`ForeignKeyColumns` (folded to four fields on `ReferenceRow`), `ColumnReality` (folded to `Deployed*` fields), `Capabilities` (deliberately ignored) — the loss stated only in the charter's adoption amendment (`THE_DATA_SINK.md §4.1`) and comments.

**The misalignment.** The system's formal soul is "identity modulo *named, closed* erasures", and the adjacent step already models this honestly: `normalizeBundle : RowsetBundle -> RowsetBundle * DiagnosticEntry list` with stable codes (`OssysRowsetReader.fs:778-821`), surfaced by the live read "so no erasure is ever silent" (`:816-820`). `toBundle : MetadataSnapshot -> RowsetBundle` is the same kind of morphism — strictly lossier — typed as if it were lossless. The sink chapter's entire reason for persisting `MetadataSnapshot` instead of `RowsetBundle` is that `toBundle` is lossy; that load-bearing fact exists nowhere in the type system. The precedent that this class bites is on the record: WP-1a's hardcoded `HasDbConstraint = true` (`:1631-1639`) hid in exactly this seam.

**Candidate primitive.** `toBundle : MetadataSnapshot -> RowsetBundle * BundleErasure list`, `BundleErasure` a closed DU: `FoldedRowset of SinkTable * into` (the static folds, emitted once) | `UnjoinedReference of attrId` | `AssumedSchema of entityId` | `AssumedDataType of attrId`. Live read appends them to the existing rollup beside `normalizeBundle`'s notices (one-line change at `LiveModelRead.fs:165-193`).

**Fluency bought.** The adjunction's erasure set becomes enumerable and testable at the one seam where it is currently prose; K2 parity's structural blind spot (both legs lose identically, so TOTAL catalog parity cannot see the loss) gains an independent witness; the next WP-1a-class bug becomes a visible notice instead of a silent shape.
**Effort** M. **Risk of inaction:** silent data-loss regressions at the highest-traffic seam; the "named, closed erasures" law remains unenforceable exactly where it is most load-bearing.

### A1-F2 — totality is derived by equality, never stated

**Evidence.** `SinkStore.isTotalAcquisition parameters = (parameters = MetadataSnapshotRunner.defaultParameters)` (`SinkStore.fs:255-256`). `SnapshotParameters` (`MetadataSnapshotRunner.fs:60-67`) is a bare record; the invariant "the default IS the total shape" lives in a docstring (`:69-71`), a hardcoded skip-reason string (`SinkStore.fs:279`), and a value-equality pin (`tests/Projection.Tests/SinkStoreTests.fs:244-250`). `LiveModelRead.fs:146` re-derives the same fact a second time to gate the bellwether probe.

**The misalignment.** Totality is the load-bearing epistemic fact of the whole witness plane (it gates journal admission — A49's precondition), and it is a *syntactic* derivation over a value, not a *semantic* property of the type. Two forecloses/hazards: (a) semantically-total-but-syntactically-different parameters (`EntityFilterJson = Some "[]"`; `ModuleNames` explicitly naming every module) read as Scoped — an inexpressible truth; (b) any future `SnapshotParameters` field whose default is not show-me-everything flips the gate's meaning with no compiler or test complaint (the pin compares two values that would drift together). Additionally `SkippedScoped of reason: string` carries a constant sentence where the domain has five nameable scope axes.

**Candidate primitive.** `AcquisitionScope = Total | Scoped of ScopeAxis list` with `ScopeAxis = Modules | System | Lifecycle | AttributeActivity | EntityFilter`, computed by one classifier on `SnapshotParameters` (or stamped by a smart constructor) and carried on `MetadataSnapshot` itself — the witness then gates on the snapshot's own scope, `SkippedScoped` gains `of ScopeAxis list`, and the three-way law's attribute-axis residual (S9/S13) becomes a named axis instead of prose.
**Fluency bought.** "Which axes scoped this read" becomes sayable everywhere the skip is named; the S13 fast-path gate (`onlyActiveAttributes`) reads off a type, not a flag convention.
**Effort** S–M. **Risk:** the totality gate silently changes meaning under parameter evolution; the witness's central honesty claim rests on a comment.

### A1-F3 — the hierarchy tower is pinched at both ends of the claims plane

**Evidence.** (Table end) `SinkClaims.assemble` groups claims by `PhysicalTableName.ToUpperInvariant()` alone and declares `Schema = "dbo"` on every assembled set (`SinkClaims.fs:62,75`) — while the same `snapshot` in hand carries the OBSERVED schema per entity (`OssysPhysicalTableRow.SchemaName`, `MetadataSnapshotRunner.fs:206-222`, joined for exactly this purpose at `toBundle`'s `:1497-1500`). `SinkResidue.sweep` subtracts claimed NAMES from a probed (schema, table) universe, discarding the schema in the comparison (`SinkResidue.fs:76-79`) though `probeUniverse` observes it (`:45-47`). (Environment end) `SinkRead.resolve` refuses a label claimed by two digests (`sink.envAmbiguous`, `SinkRead.fs:65-68`) — an environment is structurally exactly one (DataSource, InitialCatalog) pair.

**The misalignment.** The domain's containment tower is environment ⊃ database ⊃ schema ⊃ module ⊃ kind. The estate's own vocabulary already knows the upper floors — `TableId.Catalog` for cross-database FKs (`CatalogReader.fs:167-168`), `HasDatabaseName` in the capability vector (`MetadataSnapshotRunner.fs:395`) — yet the adjudication plane addresses physical reality by bare name: a multi-schema estate mis-groups rival claims into one set or fabricates `dbo` onto a non-dbo table's DECIDE finding; a cross-schema name collision suppresses residue; a multi-catalog environment cannot be one `sink:<env>` at all (the only expressible outcome is refusal — foreclosure, not refinement). The `Schema` field is a DECLARED constant presented at the same grain as the residue sweep's OBSERVED schema (epistemic conflation inside one type, `PhysicalClaimRules.ClaimSet:55-59`).

**Candidate primitive.** `PhysicalTableRef = { Catalog : string option; Schema : SchemaBasis; Table : string }` with `SchemaBasis = Observed of string | Assumed of string` — one identity for claims, residue, and correspondence; grouping and subtraction key on the full ref. (The env-end fix is separable and later: let a label map to a digest SET — an environment as a composite of witnessed sources — rather than refusing.)
**Fluency bought.** Adjudication speaks at the grain physical reality has; residue subtraction becomes true set-difference; findings stop asserting a schema nobody observed.
**Effort** M. **Risk:** wrong-schema claims on external/multi-schema estates surface as confidently-worded DECIDE findings; the residue detector has a name-collision blind spot no caveat names.

### A1-F4 — the ledger's classification is write-only; its readers degrade silently

**Evidence.** `renderLine` persists the domain token and its payloads (`SinkJournal.fs:119-141,156`); `parseLine` unconditionally restores `Domain = None` (`:305`), and full re-derivation from a line is impossible (`classify` needs both whole snapshots — `SinkDisplacement.fs:309-343` consults `isExtensionModule afterSnapshot` and `tableClaimedByOther beforeSnapshot`). So the transition vocabulary the charter calls the journal's deliverable (`THE_DATA_SINK.md §4.4`) is legible to a human reading NDJSON but not to any typed consumer of `load`. Downstream, both claim-assembly sites convert an UNREADABLE journal into `[]` via `Result.defaultValue` with no notice (`Pipeline.fs:1973-74`; `Faces/Estate.fs:397-98`), after which `firstWitnessedSync` fabricates `1` ("since the beginning") for every claim (`SinkClaims.fs:41`) — the interior-corruption refusal the journal is proud of (`SinkJournal.fs:24-26`) is silently swallowed one layer up, and the adjudication ladder's temporal tie-break runs on fabricated knowledge.

**The misalignment.** M7: an encode without its decode inside one codec whose sibling (the snapshot codec) states `deserialize ∘ serialize = Ok` as its first law. M6/M5: the "downgrades never silent" commitment breaks at the exact consumer the temporal dimension exists FOR.

**Candidate primitive.** (a) Decode the domain token (payloads are already self-contained on the line by construction) so `parseLine (renderLine l) = l`; or split honestly: `Displacement` vs `ClassifiedDisplacement` if read-side `Domain` is truly unwanted. (b) A `JournalReading = Read of lines | Unreadable of error` (or just threading the existing `Result`) at the two claim-assembly sites, so an unreadable ledger is a named degradation on the estate face, and `FirstWitnessedSync` can say `SinceGenesis | AtSync of int | Unknown`.
**Fluency bought.** Journal consumers (sync report, estate board, future history views) can filter and aggregate by transition without replaying snapshots; a corrupt ledger becomes an operator-visible fact instead of a quietly weaker recommendation ordering.
**Effort** S. **Risk:** the operator's transition vocabulary exists only at witness time and in raw files; corruption downgrades adjudication evidence with no trace.

### A1-F5 — the extension marker: one concept, three spellings, live drift

**Evidence.** `OssysTranslation.fs:533` and `SinkDisplacement.fs:286` test `String.Equals(kind, "Extension", OrdinalIgnoreCase)`; `SinkClaims.fs:54` tests `kind.Trim().ToLowerInvariant() = "extension"` — the only site that trims. The marker's real-world value is explicitly unconfirmed ("or whatever the IS-marker turns out to be", `OssysRowsetTypes.fs:30-34`; charter §7 names it as the sink-as-instrument's first empirical question).

**The misalignment.** M4 split vocabulary on an epistemic frontier: a padded or variant value classifies differently between the displacement classifier, the origin translation, and the claims plane — three answers to "is this module an extension" in a system that adjudicates ownership on that predicate. And when the sink's own instrument finally answers the empirical question, three sites must be found and updated in lockstep.
**Candidate primitive.** One predicate (or better, one lift) beside the row type it reads: `OssysRowsetTypes.ModuleRow.espaceKindReading : string option -> EspaceKindReading` with `EspaceKindReading = ESpace | Extension | Other of raw` — raw string retained (the empirical humility stands), classification owned once.
**Fluency bought.** The single site the empirical confirmation will edit; identical adjudication across planes by construction.
**Effort** S. **Risk:** a whitespace-variant marker splits the estate's answer across surfaces — precisely the "one concept under two names" drift the taxonomy exists to catch.

### A1-F6 — the freshness reading is stringly at rest and in the decision

**Evidence.** `TableFingerprint.TableReading` is typed at probe time, then `render` flattens it to `"%d|%s|%s"` with `-` for absent terms (`SinkFreshness.fs:41-42`); the manifest stores `(string * string) list` (`SinkStore.fs:83-88`); `decide` compares opaque strings (`:110-119`), so `Miss.FingerprintMoved of targets` can name WHICH table moved but never WHICH axis (row count vs max-PK vs content hash) — the axes the estate's fingerprint discipline elsewhere treats as distinct signals with distinct caveats (CLAUDE.md survival rule 15: content-hash absence for XML-only kinds, checksum collision). `targets` also hardcodes `Schema = "dbo"` and carries the single-PK platform assumption in a comment (`:29-32`).

**The misalignment.** M6: how staleness is *known* is typed exactly as far as the decision's happy path needs (the closed `Miss` DU is good) and no further — the observational substance is erased before rest, so the taxonomy's leaf is unexplainable. Age itself travels as a bare `int` of days into the voice line (`Pipeline.fs:1952-1960`).
**Candidate primitive.** Persist the typed reading per target (three explicit fields in the manifest — the codec idiom exists) and compare per-axis; `FingerprintMoved of (target * movedAxis list) list`. Rendering stays a display concern.
**Fluency bought.** "Stale because ossys_Entity's content hash moved though counts held" becomes sayable; the content-term caveat family becomes expressible at this plane too.
**Effort** S. **Risk:** low-severity but permanent: every future freshness question at this plane starts by un-rendering a string. (Counterweight honestly held: "one rendering, both sides, equality is string equality" is a deliberate simplicity ruling — this finding proposes typing the *record*, keeping the comparison trivial.)

### A1-F7 — the 26-rowset contract is positional folklore with a count assert

**Evidence.** The walk is a hand-ordered sequence of `read "name" mapper` / `skip "name"` calls (`MetadataSnapshotRunner.fs:958-1013`); rowset identity is asserted nowhere (SequentialAccess reads by ordinal; no column-name check); the only structural verification is the terminal count (`resultSetContractCheck`, `:1036-1039`). The dispositions ("V1-SUNSET", which sets are skipped and why) live in comments that have already drifted: "all 23 rowsets" (`:849`), "documented 23" (`:1016`), "n of 23" (`LiveModelRead.fs:123`), "the 18 V2-skipped" (`:84-88` — now 10), "five parameters… byte-identical to V1" (`MetadataExtractionSql.fs` header vs the post-fork independently-maintained reality, `THE_DATA_SINK.md §3`).

**The misalignment.** The house's own registry law (pillar 9: one `chainSteps` definition site; `registered ⇔ executed` property-tested) is exactly the shape this walk lacks: script and walk are two artifacts whose agreement is checked only in cardinality. An insert+delete script edit preserving count would feed rowsets to wrong mappers — surfacing (if at all) as a mapping exception naming the wrong thing, or worse, cross-mapping compatible column prefixes silently.
**Candidate primitive.** A `RowsetContract` value — `(ordinal, name, disposition)` where `disposition = Parsed of target | Drained of reason` — that DRIVES the walk, the progress labels, and a cheap per-rowset leading-column-name assert; `ExpectedResultSets` becomes `List.length RowsetContract.all`.
**Fluency bought.** Contract drift becomes a named refusal at the exact rowset; every drifted count-comment dies because the counts become projections.
**Effort** M. **Risk:** moderate probability, high blast radius (a mis-mapped acquisition is a wrong witness — the one artifact the post-eject world cannot re-derive).

## 4 Anti-findings

- **SQL Server + `dbo.ossys_*` in the extraction script** (`outsystems_metadata_rowsets.sql:13-20,110,135`) — correct specialization, not M2. The platform catalog on the supported platform IS dbo-rooted; absence THROWs loudly (50010/50011); platform-version drift is absorbed as data by the capability vector, not as failure. The defect is only where fabricated `dbo` displaces an *available observation* (F3), never the script's own platform contract.
- **Persisting 16 of 26 rowsets** looks like a witness-plane erasure; it is not. The 10 drained sets are the V1-SUNSET JSON aggregations — server-side re-projections of the same underlying rows (the session flag even opts out of *building* them, `MetadataSnapshotRunner.fs:844-855`). The kept 16 are the source-shaped total; duplicating derived views at rest would be worse, not more honest. The residual gap is F7 (naming the disposition), not the drop.
- **Ambient reads witness silently** (`LiveModelRead.fs:149-158` surfaces only `Failed`) — looks like swallowed outcomes; it is the ruled posture: the store is advisory, the sync verb is the reporting surface, and CDC-silence-as-strongest-guarantee extends to the witness. The outcome vocabulary itself (`Persisted | Unchanged | SkippedScoped | Disabled | Failed`) covers the witnessing domain at its grain — `Unchanged`'s quiet fingerprint re-anchor (`SinkStore.fs:337-343`) is inside `Unchanged`'s honest meaning (state confirmed identical). Single-writer store assumption (no cross-process lock) is acceptable at the operator-local grain; `applyOne`'s replace-by-key idempotence bounds the damage of a race.
- **`Manifest.EnvLabel : string option` starting `None`** — looks like unreified naming; it is the deliberate two-act design (the passive hook knows the connection, only the sync verb knows the name; the naming act is `nameEnvironment`, `SinkStore.fs:395-400`), with ambiguity a named refusal. The digest-vs-label split is epistemically exactly right.
- **`EntityFilterJson : string option`** — stringly, but the string is the *SQL contract's own declared parameter shape* (pass-through to `@EntityFilterJson`); typing it would reify V1's script surface inside V2 for one caller. It participates in F2's totality problem (a semantic-vs-syntactic gate) without needing its own reification.
- **`ShapeChanged` carrying no payload while `AttributeRetyped` carries facets** — the capability before/after images ride the displacement, so which probe flipped is recoverable; the asymmetry is tolerable at one-row-per-snapshot grain. Noted, not pressed.

## 5 Already-aligned (do not re-propose)

- **`KeyBasis = Native | Positional | Composite`** (`SinkDisplacement.fs:37-41`) — key-basis honesty as a type on every displacement and claim (`SinkClaims` mirrors it: `EntityId` discriminator + `EntityKey : SsKey option`). The plane's best epistemic reification.
- **`DomainTransition` as classification over a total carrier, never a filter** (`SinkDisplacement.fs:8-19,117-130`) — unclassified deltas still journal; the outcome space stays open without losing totality. (F4 is about the *codec's read side*, not this design.)
- **`PhysicalClaimRules`** (`PhysicalClaimRules.fs:29-191`) — the model citizen: total ladder, `Contested`-always (never a silent pick), ordered rivals as recommendation-in-payload, and `proposeCorrespondence` structurally unable to adopt (no catalog in, no `SsKey` out). Teleological reification exactly as the audit brief defines it: every outcome a VALUE.
- **`WitnessOutcome` + `Miss` + `Decision`** — closed outcome vocabularies with a pure total decision table (`SinkFreshness.fs:58-119`); policy governs reuse only (R2), witnessing gated by presence + totality.
- **Raw-at-rest / canonical-only-in-the-algebra** (`SinkStore.fs:283-291`; `SinkDisplacement.canonical`) — the K2-parity discipline as standing law. *Doc drift noted, code is the fact:* `witnessWith`'s docstring still says "canonicalizes" (`SinkStore.fs:261`) and `SinkJournal.replay`'s comment says "the witness writing canonical snapshots" (`SinkJournal.fs:383-385`) — both pre-S7 leftovers contradicting the ruling they sit beside; T19's real statement is `replay = canonical(latest)`.
- **The capability vector as typed rowset 26** (`MetadataSnapshotRunner.fs:378-405`) — platform-shape drift as first-class observed data, persisted with every witness, `toBundle`-invariance property-tested. Precisely the "capability vector as epistemic primitive" the brief asks after: it is one.
- **`RowAtRest` capture-then-map** (`:436-467`) — the one live-reader access site; a wire-contract discipline earned from a named incident.
- **`MetadataSnapshotCodec`** — total (missing array = decode error, "the witness is total or it is not the witness"), versioned head-sniffable, fail-closed, structural-rebuild-only (recovery loads what no longer constructs) — the right epistemic posture for a post-upstream artifact.
- **The one-funnel rule** — `SinkSyncRun` rides `LiveModelRead.fromConnectionWith`, never a second acquisition expression (`SinkSyncRun.fs:87-93`); `SinkRead.readCatalog` is the live pipeline minus the wire. One acquisition vocabulary, two transports.
- **Named refusal set on resolution** (`sink.envUnknown / envAmbiguous / syncNotFound / snapshotUnreadable / storeDisabled / connUnresolvable`, `SinkRead.fs`) — total decisions at the addressing seam.

**Doc drift register** (code is the fact): the count-comments family (F7 evidence); `MetadataExtractionSql.fs` header's "byte-identical to V1" and "five parameters" framing vs the independently-maintained post-fork script; `CatalogReader.fs:35-72`'s docstring still calling `SnapshotRowsets` "Planned" three cases above its implementation; the two canonicalization comments above.
