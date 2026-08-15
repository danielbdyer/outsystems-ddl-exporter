# THE_DATA_SINK — the acquisition-grain ledger (charter proposal)

> **Epistemic status: ADOPTED 2026-08-15** (operator approval of the execution master plan;
> `DECISIONS.md` "The data sink chapter opens"; `CHAPTER_SINK_OPEN.md` is the chapter frame
> and carries the wave map). Originally authored 2026-08-15 as a first-draft charter from the
> operator's architectural prompt plus a four-agent code-and-document audit; the proposal
> grade is superseded. Three adoption amendments are marked inline `[amended at adoption —
> see DECISIONS]`: the persisted grain (§4.1), the store keying (§4.1), and the read seam
> (§4.2). Every code claim below is cited; every platform claim is labeled as such.

---

## 0 — The sentence

Acquisition currently **decides**; it should only **witness**. The data sink is the durable,
source-shaped, per-environment copy of the OSSYS metadata plane — filled by fingerprint-gated
syncs, differenced into an append-only displacement journal, projected by the existing pure
passes — so that Selection stops being a WHERE clause, the estate's observed history stops
evaporating between runs, and the engine can answer *"what is the model?"* without the source
present.

## 1 — The misalignment, named precisely

Three empirical facts, one already-ruled principle.

1. **Nothing at the metadata boundary is ever persisted.** `MetadataSnapshotRunner`
   materializes all 25 rowsets in memory, `toBundle` projects them to `RowsetBundle`,
   `CatalogReader.parse (SnapshotRowsets …)` translates, and the value evaporates
   (`src/Projection.Adapters.OssysSql/MetadataSnapshotRunner.fs`;
   `src/Projection.Pipeline/LiveModelRead.fs:126-186`). The only near-metadata artifact on
   disk is `catalog.snapshot.json` — the **post-pass emitted Catalog**, not the acquisition.
   Every model-bearing verb re-executes the full extraction.

2. **Acquisition-time policy erasure.** `@ModuleNamesCsv`, `@IncludeSystem`,
   `@IncludeInactive`, `@OnlyActiveAttributes`, `@EntityFilterJson` are WHERE clauses at the
   effectful boundary (`Resources/outsystems_metadata_rowsets.sql:111-145,281`). Rows they
   exclude are not marked, not journaled, not recoverable downstream — erased before the
   algebra ever sees them, in a system whose stated soul is *"nothing is ever lost in
   silence"* (`CLAUDE.md §1`).

3. **Every persisted artifact V2 owns is a derived projection.** The estate store holds
   Profile + fingerprints; the proof cache holds green fidelity proofs; the episode store
   holds emitted Catalogs; the golden corpus holds emission bytes; the capture journal holds
   transfer chunks. The source-shaped relational metadata — the thing all of these derive
   from — is the one plane with no durable form and no ledger.

The house has already ruled on the principle once. The session-21 boundary filter was retired
because *"filtering on a lifecycle flag is `OperatorIntent of Selection`, not `DataIntent` —
the disposition mis-placed an operator-intent transformation at the adapter boundary"*
(`DECISIONS 2026-05-16`, slice β; `IsActive` now carried at all three grains, pinned by
`IsActiveCarryThroughTests.fs`). The SQL pushdown is the same decision one layer down —
licensed today by the pushdown ≡ `ModuleFilter.apply` equivalence law
(`OssysExtractionCanaryTests.fs:374`), which is precisely the theorem that makes the sink
safe: acquire total, select purely, and the law already proves the two commute. The sink does
not fight the existing design; it **completes the movement the design already started**.

## 2 — The forcing scenario (post-cutover retrieval)

Platform facts (external; OutSystems 11 documented behavior):

- Deleting an Entity from an eSpace and publishing does **not** drop the physical table.
  The `ossys_Entity` row survives as a tombstone with `Is_Active = 0`; the OSUSR table and
  its data remain until someone runs the DbCleaner API (`Entity_ListDeleted` →
  `Entity_DropTable`). Deletion in Service Studio is a metadata event, not a physical one.
- Re-importing the table through Integration Studio registers an **extension** module (an
  `ossys_Espace` row whose `EspaceKind` marks it as an extension — the case-insensitive
  `"Extension"` marker V2 already discriminates on) and a **new** `ossys_Entity` row with
  `Is_External = 1` pointing at the same physical table. A new row means a new `SS_Key`:
  logical identity does **not** automatically survive the cutover boundary. (Expected;
  empirically unconfirmed — see §7 on the sink as instrument.)

What the current pipeline does with that lifecycle, by default:

- The tombstoned entity is dropped **at SQL** when `@IncludeInactive = 0` reaches the script,
  and dropped **again in memory** by `ModuleFilter` under the config default
  `includeInactiveModules: false` (`Projection.Core/ModuleFilter.fs:392-404`;
  `Config.fs:1049-1051`). Its attributes are independently dropped under the default
  `onlyActiveAttributes: true`. V1's CLI couples the two flags
  (`ExtractModelApplicationService.cs:48-57`), so there is no independent inactive-entity
  switch at all on the V1 surface.
- The extension re-registration is visible **only if** the extension module's name is in the
  module allowlist. Entities are reachable exclusively through the `ossys_Espace` join
  (`outsystems_metadata_rowsets.sql:136`).
- Between the deletion publish and the extension import — or whenever the allowlist lags the
  cutover — the entity's model exists **only** as the tombstone row plus the physical catalog
  (`sys.*`). Both are read today; neither is kept.

So the operator's incident decomposes into three needs the current shape cannot serve:

1. **Recoverability** — "give me the last viable model of this entity" after the eSpace
   reference is gone. Today's only artifact is a lossy V1 `model.json` (no SS_Keys, no
   `EspaceKind`, no entity-level `IsSystem`, raw `DataKind` collapsed —
   `CHAPTER_3_PRESCOPE_SNAPSHOT_ROWSETS.md §, SnapshotJsonBuilder.cs:114-209`).
2. **Adjudication** — when a tombstone and an extension registration both claim one physical
   table (or a recreated entity mints a suffixed OSUSR sibling), *which claim is the latest
   edition?* Nothing adjudicates physical-table claims today; there is no entity-level
   analogue of `physical_isPresentButInactive`, and the estate's physical-residue sweep is a
   named deferral (`DECISIONS.md:245`, S8/O4).
3. **Continuity** — detect that the physical table's authoritative claim *changed* and keep
   pulling from the survivor, with the transition on the record.

## 3 — Why this tessellates (the algebra already wants it)

The sink is not a new idea grafted on; it is the existing formal system instantiated at the
one grain it skipped.

- **S8, the ledger star, has a missing grain.** Ledgers exist at episode grain
  (`LifecycleStore`), chunk grain (`CaptureJournal`), and partially at run grain
  (`CONSTELLATION.md §1 S8, §8 R1`). *"The ledger is the torsor made durable"* — but only for
  **published** states. The acquisition grain — what the source actually said, when — has no
  ledger. The sink is S8 at observation grain.
- **The torsor, pointed at the source.** T12–T16 already give state-as-point,
  change-as-displacement, FTC replay (`AXIOMS.md T12-T16`; `Episode.fs`). The sink applies
  the identical algebra to the *observed* timeline: sync states, displacement records,
  `reconstruct(anySyncId)` by fold. A6's one temporal dimension — Lifecycle, *"declared but
  unbuilt"* (`NORTH_STAR.md T-III`) — gets built first for the plane where time actually
  arrives from outside.
- **CDC symmetry.** The engine deliberately owns no universal event log — *"SQL Server's CDC
  is the data plane's ledger and the engine reads it"* (`CONSTELLATION.md §8`). The metadata
  plane has no CDC. The sink journal is its exact analogue, derived rather than owned:
  displacement records obtained by differencing consecutive witnessed states, with the
  journal-entry count as the metadata plane's ‖δ‖ (T15's norm, read at a second grain).
- **The estate pattern, completed.** Per-environment evidence store (Profile plane) +
  fingerprint-gated pay-once reuse + `--offline` downgrades already exist
  (`EstateEvidenceStore.fs`; `Faces/Estate.fs:146-215`). The sink is the same discipline for
  the Catalog plane: `evidence/<env>/…` gets a sibling `sink/<env>/…`, and `--offline` stops
  meaning "advisory evidence only" and starts meaning "full catalog, provenance named."
- **The canonicality decision anticipated it.** The `SnapshotRowsets` ruling names, as an
  explicit advantage, *"independence of V1-side change — V2's adapter takes them in whatever
  persisted form the operational layer provides (multi-rowset JSON, per-table CSV, etc.)"*
  (`DECISIONS.md:5088-5147`). The sink is that operational layer, finally built.
- **The interface question stays closed.** A sink-backed read is a *variant* of the OSSYS
  source, not a second catalog source — exactly the distinction the `ICatalogReader` deferral
  draws (`DECISIONS.md:224`). The change is a closed-DU expansion on `SnapshotSource`
  (`SnapshotFile | SnapshotJson | SnapshotRowsets | …`), the house's cheapest kind of change.
- **Generation-neutral by lineage.** V1 and V2 share the rowsets script's ancestry:
  byte-identical at the 2026-07-17 parity audit, independently maintained since the fork
  pins retired (2026-07-21), with V2's copy diverging only additively — `IsPersisted`
  appended last under the append-only column contract, the PK-first attribute reorder
  retired *for V1 authored-order parity*, and two appended rowsets (sequences, temporal;
  contract 23 → 25 in lockstep). A persisted rowset snapshot is therefore legible to both
  generations, and the same disciplines that version the script version the sink's format.
- **The eject, rehearsed.** *"After the eject there is no upstream to re-derive from"*
  (`THE_USE_CASE_ONTOLOGY.md:308-321`). Every sync is a small rehearsal of that terminal
  state: the moment the cloud contract ends, the last sink state **is** the upstream. P-7's
  hand-over persists the published chain; the sink persists the observed one.

Against the grain, honestly: `V2_DRIVER.md:52`'s *"V1's evidence cache is the source of
truth; V2 does not re-extract from SQL Server"* is already superseded in substance by the
live OssysSql path, and the V1 fixture-shape decision (`DECISIONS.md:12315-12373`) declined
disk-persisted rowsets **as a test-fixture idiom** — a different consumer with a different
trigger than operational provenance. Neither is relitigated here; both are noted so the
chapter open can cite them.

## 4 — The shape (proposal, negotiable)

### 4.1 Store layout

Under the existing store root (`EstateStoreLocation.storeDirFrom`; env-var resolution and
"disabled ⇒ live-only, named" semantics inherited unchanged):

    <store>/sink/<connDigest16>/manifest.json                   — latest pointer; env label; capability vector; source identity
    <store>/sink/<connDigest16>/snapshots/<syncId>/snapshot.json — the MetadataSnapshot at rest (typed codec, digest-stamped)
    <store>/sink/<connDigest16>/journal.ndjson                  — append-only displacement records, one per observed transition

[amended at adoption — see DECISIONS] Two amendments over the draft: the persisted grain is
the full **`MetadataSnapshot`** (15 rowsets), not the `RowsetBundle` — `toBundle` is lossy
(it drops `PhysColsPresent` and the FK-reality rowsets, folds `ColumnReality`'s axes, and
collapses raw `Data_Kind` to `IsStatic`), so the bundle is not the source-shaped witness and
the snapshot is; and the store keys on **`connDigest16`** (SHA-256/16 over the normalized
DataSource + InitialCatalog pair — credential-rotation invariant), not the env label, because
the witness hook knows the connection while only the sync verb knows the name. The manifest
carries `EnvLabel`; `projection sync <env>` is the act that makes an environment addressable
by name.

- `syncId` monotone; `capturedAtUtc` from the boundary clock; writes atomic
  (`.tmp` + move), reads fail-closed — all four idioms already worked precedents
  (`EstateEvidenceStore.fs:229-276`, `CaptureJournal.fs`).
- The **capability vector** records what the source's `ossys_*` shape supported at capture
  (the `COL_LENGTH` probe results the SQL already computes,
  `outsystems_metadata_rowsets.sql:205-232`) so platform-version drift is data, not surprise.
- The allowlist is **closed and named**: `ossys_Espace`, `ossys_Entity`, `ossys_Entity_Attr`,
  plus the `sys.*` physical-reality joins the script already performs. Credential-bearing
  platform tables (connection registries, site properties) are out by charter, in the spirit
  of D9's credential-property refusal (`Config.fs:861`).

### 4.2 Seams (all additive)

- [amended at adoption — see DECISIONS] **No `SnapshotSource` variant after all.** A sink
  read is load → `toBundle` → `normalizeBundle` → `parse (SnapshotRowsets …)` — the exact
  live pipeline minus the wire. The persisted-grain amendment makes the DU expansion
  unnecessary, keeps the `ICatalogReader` deferral untouched (a *variant* of the OSSYS
  source, still not a second source), and lets divergence diagnostics replay from the store.
  Provenance rides `Source.Identity` (`sink:…`) and the notices, not the DU.
- `Ref.parse` gains a scheme: `sink:<env>[@<syncId>]` beside `live:` / `ossys:` / `@runId` /
  `json:` (`Ref.fs:28-33`). `projection diff sink:uat@s41 sink:uat@s42` becomes the temporal
  diff the estate board cannot ask for today (`diff` currently warns on two `live:` reads
  precisely because they are not aligned observations).
- `ModelResolution.chooseOrigin` (`ModelResolution.fs:26-34`) extends: live primary, sink
  fallback (or sink-pinned under `--offline`), file last — provenance named on every read,
  in the existing `Cached | Refreshed | Offline | Live | Absent` vocabulary.
- A `projection sync <env>` verb — the sink-shaped cash-out of the deferred standalone
  `extract` verb (`DECISIONS.md:229`: trigger *"a CI lane that materializes the snapshot
  without emitting"* — this is that lane).
- Freshness gating reuses `EvidenceFingerprint.probe` pointed at the three `ossys_*` tables
  themselves (`COUNT_BIG` / `MAX(Id)` / `CHECKSUM_AGG(BINARY_CHECKSUM(…))`), under the
  bridge-cache policy vocabulary `off | auto | pinned` and its closed miss taxonomy
  (`BridgeStagingCache.fs:24-53`; `EvidenceFingerprint.fs:56-128`).

### 4.3 Total acquisition, pure selection

`sync` always pulls **unfiltered**: `IncludeSystem = 1`, `IncludeInactive = 1`,
`OnlyActiveAttributes = 0`, no module CSV, no entity filter. (This is `defaultParameters`
already, `MetadataSnapshotRunner.fs:72-79` — the sink makes the default the law of the sync
path.) Selection then lives where the house already ruled it belongs: `ModuleFilter.apply`
plus a named Selection-axis suppression pass — the pass whose trigger the `IsActive` lift
left armed (`DECISIONS 2026-05-16`: *"a Selection-axis suppression pass is
deferred-with-trigger"*). The existing pushdown ≡ in-memory equivalence law extends to a
three-way: pushdown ≡ pure-filter-over-live ≡ pure-filter-over-sink. Live scoped reads keep
their pushdown for interactive latency; the *sync* path never filters.

### 4.4 The displacement journal

Per sync, diff the new bundle against the previous at rowset grain, keyed by `Id`/`SS_Key`
per table; append typed records. The transition vocabulary is the domain's, not CRUD's:

    EntityDeactivated | EntityReactivated | EntityRehomed (espace → espace)
    EntityRegisteredExternal (tombstone counterpart appears under an extension)
    PhysicalTableClaimChanged | PhysicalTableSuperseded (OSUSR sibling minted)
    AttributeRetired | AttributeRetyped | ModuleRetired | ShapeChanged (capability vector moved)

Journal-entry count is the sync's norm; zero entries is the metadata plane's CDC-silence —
*silence reserved as the strongest guarantee*, now at acquisition grain.

### 4.5 Physical-claim adjudication (the "latest edition")

A strategy module in the house shape (`<Domain>Rules`, per-record decisions keyed by SsKey,
total outcomes, named skips): **`PhysicalClaimRules`**. For each physical table, order the
claims — active eSpace-owned > active extension-owned (`Origin = ExternalIndirect`) >
tombstone (`IsActive = false`) — tie-broken by the journal's observed transition order (the
sink supplies the temporal dimension `ossys` doesn't reliably expose). Outcomes:

    Adopted of claim            — the latest edition; lineage event on adoption
    TombstoneOnly of claim      — model recoverable solely from the tombstone; DECIDE finding
    Contested of claim list     — two active claims on one table; named refusal, never silent
    Unclaimed of tableId        — physical residue; feeds §4.6

`Contested` and `Unclaimed` are findings in the estate-board contract (S/D-lane shaped),
not exceptions.

### 4.6 The residue sweep

The sink's sibling read closes deferral row 43 in its named shape: an `INFORMATION_SCHEMA` /
`sys.tables` sweep **beside** the OSSYS read (*"the one place the estate mode deliberately
looks past OSSYS"*, `DECISIONS.md:245`), producing the entity-grain analogue of
`physical_isPresentButInactive`: *present-but-unclaimed*. This is the detector for "the
table is still there and retrievable" that today exists only at attribute grain.

## 5 — INVEST backlog

Each story independent, negotiable, valuable, estimable, small, testable; sized in house
slices; laws named in test-citation style.

| # | Story | Acceptance (the law the tests cite) | Size |
|---|-------|--------------------------------------|------|
| K1 | As the operator I run `projection sync <env>` and get a durable, digest-stamped snapshot of the OSSYS metadata plane under the store root. | Codec round-trip `∀b. load(save b) = Ok b`; atomic write; byte-determinism across identical sources; store-disabled ⇒ named live-only refusal. | S |
| K2 | As the operator I project the model from the sink (`sink:<env>[@id]` refs; `--offline`) with provenance named on every surface. | **Parity:** `parse(SnapshotSink s) ≡ parse(SnapshotRowsets live)` when `s` was synced from that live state — the cross-source parity suite extended to a third source; provenance line mandatory (Voice). | S |
| K3 | As the engine, sync acquires totally; Selection is a pure pass. | Three-way equivalence: pushdown ≡ filter∘live ≡ filter∘sink; no WHERE-clause policy on the sync path (structural assert on `SnapshotParameters`); suppression pass registered (`OperatorIntent of Selection`). | M |
| K4 | As the operator I read an append-only journal of typed metadata displacements per environment. | Journal replay: `fold applyDisplacement genesis = latest` (FTC at acquisition grain); zero-displacement sync appends nothing (metadata CDC-silence); monotone syncIds refuse regression. | M |
| K5 | As the operator I get one adjudicated **latest edition** per physical table, with contested and tombstone-only cases surfaced, never silently resolved. | `PhysicalClaimRules` total over claim sets; `Contested` refusal named; adoption emits lineage; tombstone-only recoverability proven on the edge-case seed (deleted entity, table present). | M |
| K6 | As the operator I see physical tables no metadata claims (present-but-unclaimed) as estate findings. | Sweep diff = `sys` tables ∖ claimed tables, module-scoped exclusions named; finding lands in the estate contract; cashes deferral row 43 in its stated shape. | S |
| K7 | As the operator, unchanged estates cost one probe, not 25 rowsets; `--refresh` forces. | Fingerprint-gated skip under `auto`; forced under `--refresh`; `pinned` skips the probe but a changed declaration still refreshes; miss taxonomy closed. | S |
| K8 | As the operator I run `check estate` / `diff` / `emit` against sink refs with no source connectivity, and every dependent line names its evidence age. | Offline runs refuse nothing they can serve; freshness downgrade wording owned by the Voice; `diff sink:e@a sink:e@b` replaces the mis-aligned two-`live:` read. | S |
| K9 | As the operator I get a proposed identity correspondence across the cutover boundary (tombstoned native entity ↔ extension re-registration), as a DECIDE finding. | Correspondence proposals via physical-table + name evidence; `SsKey` continuity lifted through the existing `DerivedFrom`/`V1Mapped` variants; never auto-adopted. | M |
| K10 | As the future ejected team, the seal/eject package carries the final sink state; the Twin can import evidence from it once there is no upstream. | Eject bundle includes `sink/<env>` terminal snapshot + journal; Twin evidence-import accepts a sink ref (rendition-mapped). | S |

Suggested order: K1 → K2 → K7 → K4 → K5 → K6, then K3 (after K2's parity law exists to
protect it); K8–K10 opportunistic behind their consumers.

## 6 — Non-goals and named risks

- **Not event sourcing, not a second source of truth.** The sink witnesses; it never
  authors. Live wins when reachable and fresh; the sink serves recovery, offline operation,
  history, and adjudication evidence. The anti-goal in `CONSTELLATION.md §8` stands: the
  engine does not own the source's event log — it differences witnessed states.
- **Not a data-row sink.** Bridge staging and the capture journal own data acquisition. The
  sink adjudicates *which tables* the data legs should target (K5 feeds them); it does not
  carry OSUSR rows.
- **Small-data honesty.** The metadata plane is small; a full re-pull is cheap. Diffing is
  for *detection and provenance*, not bandwidth — the journal is the deliverable, the skip is
  a convenience.
- **Schema drift of `ossys_*` itself** is absorbed as data (the capability vector), not as
  failure.
- **Staleness is the recurring hazard**: every sink-served surface must name its evidence
  age (the estate board already has the register for this); a sink with no freshness line is
  a defect, not a feature.
- **Naming owed** (pillar 8, decided in DECISIONS at chapter open): *sink* (operator's term,
  data-engineering resonance) vs *mirror* (DBA resonance) vs *store* (house resonance —
  but overloaded). This document uses the operator's term throughout.

## 7 — The sink as instrument

Three empirical questions the corpus currently flags as unconfirmed become answerable the
day the sink exists, because the raw rows are finally kept: the `EspaceKind` extension-marker
value semantics (*"until a real V1 production sample surfaces a different string"*,
`OssysRowsetTypes.fs:30-34`); whether extension re-registration preserves or re-mints
`SS_Key`s (§2); and the real-world shapes of shadow rows beyond the one-active-survivor case
`normalizeBundle` handles (`OssysRowsetReader.fs:821-908`). The sink is not only a
robustness organ — it is the house's own evidence-collection instrument for the metadata
plane.
