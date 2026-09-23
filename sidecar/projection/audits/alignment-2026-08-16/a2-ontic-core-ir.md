# Ontic core IR — alignment audit workpaper

Auditor A2 · 2026-08-16 · scope: `src/Projection.Core` identity/structure carriers
(`Catalog.fs`, `Identity.fs`, `Coordinates.fs`, `Lifecycle.fs`, `Episode.fs`, `KindColumns.fs`,
`Types.fs`, `Transfer.fs` (identity portion), `BoundedContext.fs`, `Classification.fs`,
`PrimitiveType.fs`, `Strategies/PhysicalClaimRules.fs` as the sink chapter's Core deposit).
All paths below are relative to `/home/user/outsystems-ddl-exporter/sidecar/projection/`.
Code is the fact; doc drift is noted where found.

## 1 Vocabulary inventory (with file anchors)

**Identity bases** — `src/Projection.Core/Identity.fs`
- `DerivationReason` (closed DU, one case `Inverse`) :58-60; codec `serialize/parse` :66-78.
- `SsKey` four-variant DU :80-84 — `OssysOriginal of Guid` / `Synthesized of source: string * basisParts: string list` / `DerivedFrom of parent * DerivationReason` / `V1Mapped of Guid * Guid`. Per-variant equality; segmentation is identity-bearing (:39-46).
- Recoverable codec (tag + length-prefixed fields) :185-236; projections `rootOriginal` :252-265, `isDerived` :270, `rootKey` :281, `isSynthesizedRoot` :291, `synthesisSource : string option` :301, `derivationReasons` :309, `display` :325.

**Node carriers** — `src/Projection.Core/Catalog.fs`
- `Name` (presentation, never identity) :18; `Origin` (Native | ExternalIndirect | ExternalDirect) :48-55.
- `StaticRow` :82-85 (Map-carried, WP-3 `string option` cells, absent-key = third state); `RowQuantum` :100-101 (positional, `voption`, `[<Struct>]`); `RowBasis` (private ctor + permutation invariant) :118-129.
- Facet carriers: `ExtendedProperty` :245-248, `ColumnCheck` :279-284, `ComputedColumnConfig` :319-322, `Trigger` :348-353, `Sequence`/`SequenceCacheMode` :386-406, `TemporalRetention*`/`TemporalConfig` :455-473.
- `ModalityMark` (Static of populations | TenantScoped | SoftDeletable | SystemOwned | Temporal of config) :480-504.
- `PhysicalRealization = TableId` (alias) :539; `ColumnRealization` :545-566 (typed `ColumnName`, Collation, Identity seed).
- `Attribute` :648-774 (SsKey, Name, `Type: PrimitiveType`, Column, IsPrimaryKey, IsMandatory, Length/Precision/Scale, IsIdentity, Description, IsActive, DefaultValue/DefaultName, Computed, ExtendedProperties, OriginalName, ExternalDatabaseType, `SqlStorage: SqlStorageType option`, Order).
- `ConstraintState` (NoDbConstraint | TrustedConstraint | UntrustedConstraint; quadrant collapse + legacy-boolean wire projection) :802-857; `Reference` :873-906.
- `IndexColumnDirection`/`IndexColumn` :916-929; `DataCompressionLevel` :958-962; `DataSpace` (Filegroup | PartitionScheme) :989-992; `IndexUniqueness` (NotUnique | Unique | PrimaryKey; quadrant collapse) :1006-1046; `Index` (17 fields) :1066-1176.
- `Kind` :1182-1227; `Module` :1232-1253; `Catalog = { Modules; Sequences }` :1257-1268.
- Aggregate invariants in `Catalog.create` :1971-2095 (module/kind/sequence SsKey disjointness, dangling reference source/target, dangling index columns, NM-14 `Type`↔`SqlStorage` agreement).
- Traversals + caches :1745-1917; `nameIndex`/`displayName(In)` (A4-safe display projection) :1927-1951.

**Coordinates** — `src/Projection.Core/Coordinates.fs`
- `CoordinatesLimits` :71-78; `IdentifierBudget.fit` :87-107.
- `SchemaName`/`TableName`/`ColumnName` private single-case DUs :113-125 with smart ctors :131-204.
- `TableId = { Catalog: string option; Schema: SchemaName; Table: TableName }` :218-229; ctors/projections/`normalizedKey(Of)`/case-insensitive equality :237-356. Documented deliberate asymmetry for the physical-comparison domain :52-66.

**State / ledgers** — `src/Projection.Core/Lifecycle.fs`, `Episode.fs`
- `Version` (ordinal + label; "ordinal, not a clock") Lifecycle.fs:9; `Timeline = private Timeline of string` :31; `CatalogSnapshot` :45; `Lifecycle` (private chain, monotone `append`, `replayTo`, `reconstructLatest`, `netDiff`) :56-193.
- `EpisodeCoordinate = { Version; Environment; At }` Episode.fs:11-16; `DataObservation` :34-38; `Episode` (five-plane co-record) :61-93; `EpisodicLifecycle` (parallel chain: genesis/append/head/latest/schemaEvolutionChain/reconstructLatestSchema/netSchemaDiff) :165-279.

**Environment / connection identity** — `src/Projection.Core/Transfer.fs`
- `SubstrateRole` (Source | Sink) :32-35; `Environment` (Dev | Qa | Uat | Prod | Named of string) :49-55 with `name` projection :59-65; `ConnectionRef` :76-80; `Substrate` :84-89 (`Substrate.fromRef` always mints `Environment.Named label` :101); `TransferConnections` :108-135.

**Other structure carriers**
- `KindColumns` (per-kind column vocabulary of the three data lanes; `matchAttributes` missing-PK fallback) KindColumns.fs:29-176.
- Pattern aliases (`Emitter`, `Pass`, `Compare`, `DiffOf`, …) Types.fs:50-114.
- `BoundedContextCandidate`/`Discovery` (derived FK-community analysis) BoundedContext.fs:7-22.
- `OverlayAxis` (5 axes + codec), `Classification` (DataIntent | OperatorIntent), `TransformGroup` — Classification.fs:24-184.
- `PrimitiveType` (9 scalar categories, target-agnostic) PrimitiveType.fs:11-20.
- Sink-chapter Core deposit: `PhysicalClaim` (EntityId positional + `EntityKey: SsKey option` native — key-basis honesty), `ClaimSet`, `PhysicalClaimOutcome` (Adopted | Contested | TombstoneOnly | Unclaimed, total ladder) — Strategies/PhysicalClaimRules.fs:33-106.
- Related worked example outside Core (the standard being audited against): `SinkDisplacement.KeyBasis = Native | Positional | Composite`, src/Projection.Pipeline/SinkDisplacement.fs:38-41.

## 2 The domain space (stated independently of the current code)

The engine models an evolving OutSystems relational estate and must one day be its only
surviving description (post-eject). The situations the ontic core must be able to *say*:

- **Grain tower.** Environment (Dev/Qa/Uat/Prod + ad-hoc stages) ⊃ database (SQL catalog)
  ⊃ schema ⊃ module (eSpace/extension) ⊃ kind (entity) ⊃ attribute ⊃ column-facet
  (collation, identity seed, default, computation). Some levels are model-side (module,
  kind, attribute), some are realization-side (database, schema, table, column), and one —
  environment — is a coordinate of *witnessing*, not of the model.
- **Identity bases.** An identity can stand on a native GUID (survives rename), a positional
  id (survives rename within one estate), or a name-derivation (rename-fragile). The basis
  must be typed and honest wherever identities are minted or compared; two identities minted
  from the same domain object by different readers must either be equal or their inequality
  must be a *named* fact with a reconciliation rule.
- **Multiplicity of realization.** One table may carry 0..N metadata claims (live entity,
  extension re-registration, tombstone whose table survives — proven domain-real by the sink
  chapter); one kind may have secondary physical realizations (a temporal history table);
  tables may exist with no claim (residue).
- **Lifecycle.** Module/kind/attribute active-ness as the source asserts it; tombstones,
  supersession, contested ownership as the *journal* establishes them over time; a coerced
  default ("absent means active") is knowledge of a different kind than an observed flag.
- **Editions and histories.** Per-environment monotone histories of the model; snapshots
  that are fetchable and reconstructible from deltas; acquisitions that are witnessed,
  journaled, addressable (`sink:<env>@<syncId>`), and comparable across time and lanes.
- **Acquisition epistemics.** A catalog value can come from the OSSYS rowset read, the V1
  JSON projection, physical reflection (ReadSide), a codec round-trip, or hand-built
  fixtures — sources with *different expressive capabilities* (no authored order from
  reflection; no collation from JSON; no sequences from JSON today). "This facet is None"
  must be distinguishable from "this source cannot say".
- **Round trip.** Catalog → DDL → deployed database → catalog-again must be spoken in one
  vocabulary, with the comparison quotient (what SQL Server cannot echo back) named.

## 3 Findings

| ID | Class | Dimension | Axis | One-line claim | Anchor |
|---|---|---|---|---|---|
| A2-1 | M4+M5+M6 | Semantic/Relational | Epistemic | `Synthesized.source` is an open string vocabulary — the un-closed sibling of `DerivationReason`; two live conventions already name the same domain object (sequences), and disjointness is argued in a stale comment | Identity.fs:82; OssysRowsetReader.fs:1022 vs OssysTranslation.fs:114-117; Catalog.fs:2076-2080 |
| A2-2 | M4+M5 | Relational | Ontic | The kind's primary key is stated twice — `Attribute.IsPrimaryKey` flags and `Index.Uniqueness = PrimaryKey` — with no agreement law, while the same file carries two celebrated intra-record quadrant collapses | Catalog.fs:653, 1012-1016, 1588-1589, 1971-2095 |
| A2-3 | M3+M6+M5 | State | Epistemic | `ModalityMark.Static` doubles as the observed-rows channel: ReadSide overwrites `Modality = [Static rows]`, so a declared modality and a lifted observation are one value — the reified 4.4 trap | Catalog.fs:482, 1847-1850; ReadSide.fs:1866; CLAUDE.md §4.8 |
| A2-4 | M4+M3 | Hierarchical/State | Ontic | The environment grain is spelled three ways (`Environment` DU, `Timeline` string, sink `EnvLabel` string) and its identity basis is non-canonical (`Named "DEV"` ≠ `Dev`); the canonicalizing parse lives in the CLI, not Core | Transfer.fs:50-55,101; Lifecycle.fs:31; OperatorConsole.fs:121-128; Faces/Migrate.fs:387,516; Faces/Export.fs:56,134 |
| A2-5 | M6 | State | Epistemic | A `Catalog` value carries no acquisition provenance — reader identity is inferred by sniffing SsKey conventions and Static marks, and `None` on fidelity fields is polysemous (observed-absent vs unobservable-by-this-reader) | Catalog.fs:689-693, 760-773; Coordinates.fs footnote; Identity.fs:301 |
| A2-6 | M4 | State | Ontic | `Lifecycle`/`CatalogSnapshot` is a production-dead structural twin of `EpisodicLifecycle`/`Episode` — two chain vocabularies, one concept, no projection between them | Lifecycle.fs:56-193; Episode.fs:165-279; LifecycleTests.fs:52 (only consumers) |
| A2-7 | M5+M2 | Hierarchical | Ontic | `TemporalConfig` speaks a second physical realization as loose `string option` pairs, bypassing the `TableId`/`Name` coordinate vocabulary and admitting meaningless halves | Catalog.fs:467-473 |
| A2-8 | M4 | Semantic/Hierarchical | Ontic | "Catalog" is a homonym — the whole model IR and the SQL database coordinate (`TableId.Catalog: string option`) — and the database grain exists only as that untyped optional | Catalog.fs:1257; Coordinates.fs:218-229, 314-315 |

### A2-1 — The synthesis-convention axis of `Synthesized` identity is open where the house closed its sibling

**Evidence.** `SsKey.Synthesized of source: string * basisParts: string list` (Identity.fs:82).
The adjacent `DerivationReason` was closed 2026-06-27 with the recorded rationale: "a typo can
no longer mint a silently-different identity … New reasons are added HERE, never as free
strings" (Identity.fs:48-57). The `source` axis carries exactly the same load and remains a
free string. Nineteen conventions are live across the tree (`OS_MOD`, `OS_KIND`, `OS_ATTR`,
`OS_REF`, `OS_IDX`, `OS_TRG`, `OS_SEQ`, `OS_CHK`, `OS_IDX_LOGICAL`, `OSSYS_SEQUENCE`,
`READSIDE_MOD/KIND/TRIGGER/CHECK/IDX/SEQUENCE/ROW`, `SYNTH_ROW`, `GOLDEN`, `TWIN_PIN`).
Concretely drifted already:
- The live rowset path mints sequence identity as `SsKey.synthesized "OSSYS_SEQUENCE" (sprintf "%s.%s" r.Schema r.Name)` (OssysRowsetReader.fs:1022) — a dot-joined *single* segment, breaching the chapter-3.6 slice-δ typed-segment discipline stated at Identity.fs:26-30 and OssysTranslation.fs:64-70.
- The declared OSSYS derivation helper is `sequenceSsKey = synthesizedComposite "OS_SEQ" [schema; name]` (OssysTranslation.fs:114-117) — different convention *and* different segmentation, and segmentation is itself identity-bearing (Identity.fs:43-46). It is named in the registered-transform inventory (CatalogReader.fs:158; DECISIONS.md:11827) yet has zero live callers.
- `Catalog.create` justifies sequence/kind key disjointness by comment: "disjoint SsKey-synthesis prefixes (`OS_SEQ_*` vs `OS_KIND_*`), so collisions are not structurally possible" (Catalog.fs:2076-2080) — citing a prefix the live path does not use. The disjointness is convention-in-a-comment, not structure (M6).
- The convention string is behavior-bearing: rename plausibility compares `SsKey.synthesisSource` equality (CatalogDiff.fs:825-830), and the warning carrier is only half-typed (`RenameSynthesisSource.Known of string`, CatalogDiff.fs:832).
- Minor same-family asymmetry: comparison surfaces are deliberately case-insensitive (`TableId.tableTextEquals`, `ColumnRealization.columnNameEquals`), but synthesis-basis equality is case-sensitive — two readers of one physical object differing only in reported case mint distinct identities.

**Misalignment.** The *basis of identity* — which reader, under which naming convention, at
which grain — is exactly the fact the sink chapter reified as `KeyBasis = Native | Positional
| Composite` (SinkDisplacement.fs:38-41). In the core IR that fact is a free string with an
untracked vocabulary: the type says "some convention"; only greps say which. One domain
object reachable by two lanes gets two non-equal identities whose inequality is nowhere a
named fact.

**Candidate primitive.** `SynthesisConvention` — a closed Core DU (or private-constructor
registry) enumerating the conventions with their grain (`Module|Kind|Attribute|Reference|…`)
and reader family, with `token`/`tryParse` (the `OverlayAxis` codec shape) so the SsKey wire
format is unchanged. `Synthesized of SynthesisConvention * basisParts`.

**Fluency bought.** A typo cannot mint identity; the OSSYS_SEQUENCE/OS_SEQ split becomes a
compile-time collision; `synthesisSource` and rename plausibility become total matches;
per-convention grain makes "same convention ⇒ comparable" checkable; the `Catalog.create`
disjointness comment becomes a theorem.

**Effort** M (mechanical DU introduction + wire-token codec; ~22 mint sites).
**Risk-of-inaction.** Cross-lane identity splits stay silent-by-validity; the next adapter
freely mints a twentieth convention; the stale disjointness argument is already false.

### A2-2 — The primary key is one concept with two unsynchronized representations

**Evidence.** PK-as-flags: `Attribute.IsPrimaryKey` (Catalog.fs:653), consumed by
`Kind.primaryKey` (:1588-1589), `KindColumns.pkColumnNames`/`matchAttributes`
(KindColumns.fs:57-91), and the PK-constraint DDL. PK-as-index: `IndexUniqueness.PrimaryKey`
— "the kind's primary-key index" (Catalog.fs:1012-1016) — consumed by `IndexNaming.fs:61`,
`SsdtDdlEmitter.fs:574,645` (filters the PK index *out* of CREATE INDEX emission),
`SchemaMigrationEmitter.fs:318`, `QueryHintPass.fs:76`. `Catalog.create` (:1971-2095) checks
module/kind/sequence disjointness, dangling refs, dangling index columns, and NM-14
`Type`↔`SqlStorage` agreement — but never that a `PrimaryKey`-uniqueness index keys the
flagged attributes, nor that at most one index claims `PrimaryKey`, nor that a kind with
flags has (or lacks) its PK index consistently.

**Misalignment.** A catalog where flags say PK = {Id} while an index `Uniqueness =
PrimaryKey` keys {Email} is constructible and every consumer picks its own truth: DDL emits
the constraint on Id, index emission silently drops the Email index *as* "the PK index", the
data lanes match rows on Id. The same file contains the two worked examples of exactly this
repair — `IndexUniqueness` (:994-1046) and `ConstraintState` (:777-857) each collapsed an
intra-record boolean quadrant — but the *inter-record* quadrant between `Attributes` and
`Indexes` was left open. One domain fact (the kind's key), two vocabularies, no agreement law
(M4), with silent-divergence consumers (M5).

**Candidate primitive.** NM-15-style aggregate invariant in `Catalog.create` — "PK
coherence": at most one `PrimaryKey` index per kind; when present, its column SsKey-set
equals the `IsPrimaryKey` flag-set (message names both sides). Longer form: derive one
representation from the other (flags become the single source; the PK index becomes a
projection), mirroring how `ConstraintState` made the booleans derived.

**Fluency bought.** A red at construction instead of a split-brain deploy; strategy surfaces
may trust either representation; the missing-PK population (`matchAttributes` fallback)
becomes the *only* flagless shape.

**Effort** S (one invariant + witness test) / M (derivation form).
**Risk-of-inaction.** ReadSide constructs indexes with `ofLegacyBooleans isU false`
(ReadSide.fs:1378) while flags come from a different rowset — the two claims already travel
independently through adapters; nothing but adapter discipline keeps them agreeing.

### A2-3 — `ModalityMark.Static` conflates a declared modality with an observed row payload

**Evidence.** `Static of populations: StaticRow list` is defined as "Schema-resident
populations (A7)" — an ontic property of the kind (Catalog.fs:482). ReadSide, reconstructing
from a deployed database, *overwrites* `{ k with Modality = [ Static rows ] }` for every
row-bearing kind it lifted rows from (ReadSide.fs:1866) — an observation ("rows I saw")
stored in the declared-nature slot. The consequences are institutionalized: survival rule 8
("ReadSide marks every reconstructed data-bearing table Static — profiling … yields an empty
evidence cache unless the marking is cleared first. (The 4.4 trap.)", CLAUDE.md §4.8) and a
dedicated antidote `Catalog.stripStaticPopulations` — "the one definition site for the '4.4
trap' strip" (Catalog.fs:1847-1850). Additionally the list shape admits `[Static a; Static b]`
and `Kind.staticPopulations` silently first-picks (`List.tryPick`, Catalog.fs:1606-1611) —
a representable-but-meaningless state resolved by silent selection (M5).

**Misalignment.** Declared-vs-observed is the epistemic axis, and here it is carried by
*convention* (callers must know which catalogs are ReadSide-shaped and strip first) rather
than by type (M6). The concept is also mis-altitude: an observation channel living inside the
model's modality vocabulary (M3). The house has already paid for this twice (the trap's
survival-rule slot; the strip's existence).

**Candidate primitive.** Split the channels: `Static of populations` keeps only the declared
modality; observed row payloads ride a distinct carrier (e.g. `Kind.ObservedRows :
StaticRow list option`, or `Static of populations * PopulationProvenance` with
`PopulationProvenance = Declared | LiftedFromLive`). `stripStaticPopulations` retires;
`LiveProfiler` skips on the declared mark only.

**Fluency bought.** Profiling a ReadSide catalog stops being a trap; the erasure question
("did stripping widen?" — the N2 over-erasure Catalog.fs:1847-1850 guards) dissolves; the
duplicate-mark state becomes unrepresentable or meaningful.

**Effort** M (two adapters + two emit consumers + tests).
**Risk-of-inaction.** Documented and paid: rule 8 exists because this cost an agent a
session; every new ReadSide consumer re-learns it.

### A2-4 — The environment grain: three spellings, a non-canonical identity basis, and canonicalization at the wrong altitude

**Evidence.** Core types the grain once: `Environment = Dev | Qa | Uat | Prod | Named of
string` (Transfer.fs:49-55). But (a) `Substrate.fromRef` always mints `Environment.Named
label` — including for label "DEV" (Transfer.fs:101), while the canonicalizing parse "DEV" →
`Dev` lives in the CLI (`OperatorConsole.fs:121-128`), so `Named "DEV"` and `Dev` are both
live spellings of one environment that are structurally unequal yet `Environment.name`-equal
(:59-65) — and `EpisodeCoordinate` indexes the release lattice on `(Environment × At)`
(Episode.fs:8-10), where the two spellings split one cell into two; `LifecycleStore`
faithfully persists the distinction (LifecycleStore.fs:67-71, 306-310). (b) The schema-plane
ledger keys history by `Timeline = private Timeline of string` (Lifecycle.fs:31), constructed
at four CLI sites by flattening the DU through `Environment.name`
(Faces/Migrate.fs:387,516; Faces/Export.fs:56,134). (c) The sink's acquisition manifests key
the same grain as `EnvLabel : string` beside `ConnDigest` (Pipeline/SinkStore.fs:157-168;
SinkRead.fs:60 filters `m.EnvLabel = Some env`).

**Misalignment.** One real domain thing — a named environment — is representable at least
twice inside the typed vocabulary and three times across the state carriers, with the
identifications made only through a lossy string projection (M4). The smart constructor that
would make the basis canonical exists but at CLI altitude (M3): Core itself mints the
non-canonical form. This is the identity-basis honesty question (the KeyBasis standard) at
the *top* of the grain tower.

**Candidate primitive.** `Environment.parse : string -> Environment` in Core — total,
canonicalizing (rotation names normalize to their cases; anything else `Named`), the only
path from text; `Substrate.fromRef` routes through it; `Timeline` gains
`ofEnvironment : Environment -> Timeline` (or carries the `Environment` outright) so ledger
keys and coordinates provably share one identity space.

**Fluency bought.** `Dev = Named "DEV"` impossible; the episode lattice cannot split an
environment; sink `EnvLabel`, `Timeline`, and `EpisodeCoordinate.Environment` become one
enumerable space — a precondition for the cross-environment programs
(CROSS_ENVIRONMENT_READINESS) to speak of "the same environment" structurally.

**Effort** S. **Risk-of-inaction.** Latent lattice split; every new surface (the sink was the
third) re-invents the string spelling.

### A2-5 — Acquisition provenance of a `Catalog` value is unreified; `None` is polysemous

**Evidence.** The IR's fidelity fields document their epistemic caveats in comments only:
`IsActive` — "V1's SQL coerces missing/null source values to true … absent JSON → true"
(Catalog.fs:689-693), so `true` conflates observed-true with assumed-true; `Order = None` —
"hand-built catalogs and … the ReadSide reflection path (deployed schema carries no
OutSystems authored order)" (:762-767); `SqlStorage = None` — "test fixtures; ReadSide's
structural reflection" (:754-757); `Collation = None` — "the JSON source does not expose
collation, so that path stays None" (:549-555). In each, `None`/default means *either* the
source said nothing *or* the source is constitutionally unable to say — two different kinds
of knowledge collapsed into one value (M6/M5). At catalog grain, consumers distinguish
acquisition paths by sniffing identity conventions (`SsKey.isSynthesizedRoot`,
`synthesisSource`, Identity.fs:291-304) or the Static mark (finding A2-3) — convention, not
type. The system has the missing concept elsewhere: the metadata script self-describes a
capability vector (rowset 26, CHAPTER_SINK_CLOSE.md §3.8), and sink manifests carry
`ConnDigest`/`CapturedAtUtc` — provenance exists at the *stored-edition* grain but not on the
in-flight `Catalog` value.

**Misalignment.** Epistemic status (observed / derived / assumed / unobservable) lives in
docstrings and adapter lore. The concrete cost pattern is the 4.4-trap family: any consumer
that treats an unobservable facet as observed-absent misbehaves silently until a survival
rule is written.

**Candidate primitive.** `AcquiredCatalog = { Catalog : Catalog; Provenance :
CatalogProvenance }` at the adapter boundary, where `CatalogProvenance` names the reader
family (the A2-1 convention set reused) + its capability vector (which facet axes this reader
can assert). Π stays A18-pure (still consumes `Catalog`); boundary consumers (profiler,
diff-over-lanes, the sink) read the envelope.

**Fluency bought.** "This catalog cannot carry Order" becomes a queryable fact instead of a
docstring; adapter-conditional behavior (profiling gates, tolerance selection) keys on typed
capability, not key-shape sniffing; new readers declare themselves.

**Effort** M. **Risk-of-inaction.** Each new facet × each new reader mints a fresh polysemous
`None`; the survival-rule list grows one entry per collision.

### A2-6 — Two chain vocabularies for one history concept; the schema-only twin is production-dead

**Evidence.** `Lifecycle` (Timeline + `CatalogSnapshot` chain; genesis/append/evolutionChain/
replayTo/reconstructLatest/netDiff, Lifecycle.fs:56-193) and `EpisodicLifecycle` (Timeline +
`Episode` chain with the same seven operations re-implemented verbatim at the episode grain,
Episode.fs:165-279). Episode.fs:169-170 claims "an `EpisodicLifecycle` *is* a `Lifecycle`
enriched with the data / time / decision planes" — structurally false: no projection or
sharing exists between them. In `src/`, plain `Lifecycle`'s operations have zero consumers
outside its own file; every production chain is episodic (LifecycleStore.fs:525-571,
MigrationRun.fs:207-334, Pipeline.fs:3079-3082, EjectRun.fs:54); only `LifecycleTests.fs`
exercises it. LifecycleTests.fs:201 asserts "`CatalogDiff.compose` now has a PRODUCTION
caller: `Lifecycle.netDiff`" — the actual production caller is `Episode.netSchemaDiff`
(doc drift; code is the fact).

**Misalignment.** M4 by the house's own precedent: "zero-consumer symmetry-builds get
deleted" (CLAUDE.md §5, dead-algebra retirement 2026-06-04). Two monotone-append laws exist
that must never diverge and are kept equal only by parallel maintenance
(`lifecycle.append.nonMonotonic` vs `episodicLifecycle.append.nonMonotonic`).

**Candidate primitive.** Either retire `Lifecycle` to a projection
(`EpisodicLifecycle.schemaChain : EpisodicLifecycle -> CatalogSnapshot list` for the axioms'
L3-L1/L3-L2 witnesses) or a single private-ctor `MonotoneChain<'point>` parameterized by the
ordinal projection, instantiated twice — one law, one definition site.

**Fluency bought.** One append law; the "is a Lifecycle enriched" sentence becomes a
function; the L3 axioms witness production code instead of a twin.

**Effort** S. **Risk-of-inaction.** Low but corrosive: the next chain-law change (e.g. a
gap-tolerant append) lands on one twin and silently not the other.

### A2-7 — `TemporalConfig` bypasses the coordinate vocabulary for a real second realization

**Evidence.** `TemporalConfig = { HistorySchema : string option; HistoryTable : string
option; PeriodStart : Name option; PeriodEnd : Name option; Retention }` (Catalog.fs:467-473).
The history table is a genuine second physical realization of the kind — a table the engine
may have to emit, diff, read back, and (per the sink chapter's claims plane, which
adjudicates `Schema × Table` coordinates, PhysicalClaimRules.fs:55-59) see claimed. It is
spoken as two independent raw string options, not the `TableId` VO built for exactly this
(Coordinates.fs:218-229); `Some schema, None table` and a lone `PeriodStart` are
representable, meaningless halves (M5). The 2026-06-02 deliberate-asymmetry ruling
(Coordinates.fs:52-66) scopes the string-defer to `PhysicalSchema` and `Sequence` — slice-η's
`TemporalConfig` post-dates it and is drift, not ruled scope.

**Misalignment.** The one-kind-one-table assumption (`Kind.Physical : PhysicalRealization`,
exactly one, Catalog.fs:1187) is softened for temporal kinds only through this untyped side
door — the IR can *mention* the second table but not *address* it in the coordinate algebra
(M2-lite: emission can special-case it; the claims/diff planes cannot see it).

**Candidate primitive.** `HistoryTable : TableId option` + `Period : (Name * Name) option`
(pair or dedicated `TemporalPeriod`), constructed via `TableId.create`.

**Fluency bought.** History tables enter the same coordinate space as every other
realization — diffable, claimable, residue-sweepable (the S12 finer-grain residual,
CHAPTER_SINK_CLOSE.md §5, meets them there).

**Effort** S. **Risk-of-inaction.** When temporal emission lands, the coordinates will be
re-validated ad hoc at the emitter; half-specified configs pass construction today.

### A2-8 — "Catalog" is a homonym; the database grain exists only as an untyped optional

**Evidence.** `type Catalog = { Modules; Sequences }` — the whole model (Catalog.fs:1257) —
versus `TableId.Catalog : string option` — the SQL db-catalog prefix, i.e. the *database*
coordinate (Coordinates.fs:218-229), with `TableId.withoutCatalog` (:314-315) reading, in
model vocabulary, as "without the model". One load-bearing word, two concepts, same namespace
(M4, pillar-8 concept-shaped-names). The database level of the grain tower (environment ⊃
**database** ⊃ schema ⊃ …) is otherwise absent: no `DatabaseName` VO (named defer with
trigger, Coordinates.fs:221-225), no model-side carrier; the sink manifests spell it a third
way (`SourceDatabase : string`, SinkStore.fs:165).

**Misalignment.** Semantic collision at the exact word an operator and an agent must use most
(greps for `Catalog` span both concepts); the tower's database rung is spoken only in
physical-prefix and manifest dialects.

**Candidate primitive.** When the documented trigger fires, land the VO as `DatabaseName` and
rename the field `TableId.Database : DatabaseName option` (wire/codec names can stay) — the
grain-tower word, not the SQL-Server homonym.

**Fluency bought.** "Which database" becomes one askable, typed question across TableId, the
sink's `SourceDatabase`, and future cross-database FKs; the homonym dies.

**Effort** S (rename + VO under the existing defer). **Risk-of-inaction.** Low today
(cross-database refs rare); the confusion cost is paid in reading, and compounds if the
cross-database trigger ever fires while the homonym stands.

## 4 Anti-findings (look misaligned, are correct specialization)

1. **`Catalog` carries no environment/database field.** Correct altitude: the catalog is the
   *model*; environment is a coordinate of witnessing, reified where witnessing is reified —
   `EpisodeCoordinate` (Episode.fs:11-16), sink manifests, `sink:<env>@<syncId>` addressing.
   Pinning an environment inside `Catalog` would be the M3.
2. **`PhysicalSchema`'s string-typed comparison plane** (PhysicalSchema.fs:53-142) is not the
   M7 "two tongues" defect: the readback leg reconstructs a full typed `Catalog` through the
   *same* IR vocabulary (ReadSide → Kind/Attribute/TableId), and `PhysicalSchema` is a
   deliberate quotient carrier — scope-ruled with a revisit trigger (Coordinates.fs:52-66),
   excluded axes named (`ToleratedDivergence.IndexOptionsUnreflected`). Residual watch-item,
   not a finding: the `KeyColumns` `[col:ASC]` and `Computed` `"expr|persisted"`
   micro-encodings (PhysicalSchema.fs:84-90, 139-142) are opaque comparison keys built
   identically by both sides — fine until anything ever *parses* them back.
3. **Forward/readback identity asymmetry** (OssysOriginal GUIDs out, `READSIDE_*` synthesized
   back) is named and mechanized: the SsKey codec exists precisely to persist identity into
   the frozen schema so Transfer reads it back instead of re-synthesizing (Identity.fs:149-157).
   The asymmetry is the domain's (SQL Server does not echo SS_Keys), and the bridge is built.
4. **`PhysicalRealization = TableId` as an alias** (Catalog.fs:539) is not M4 aliasing — it is
   the recorded *unification* of a previously-triplicated shape (Coordinates.fs:3-17); the
   alias names a role, the type stays single.
5. **`IsActive : bool` rather than a wider lifecycle DU.** Faithful carriage of the source's
   own vocabulary (`ossys_*.Is_Active` is a bit); the richer lifecycle space — tombstone,
   superseded, contested, unclaimed — is reified at the grain where the temporal dimension
   actually exists: the journal/claims plane (`PhysicalClaimOutcome`,
   PhysicalClaimRules.fs:64-79; `DomainTransition`, SinkDisplacement.fs:125-130). Widening the
   IR bool would fabricate assertions the source cannot make. (The absent→true coercion is the
   epistemic residue — covered in A2-5, not a reason to widen this DU.)
6. **Two kinds claiming one `TableId` is representable** (no cross-kind physical-uniqueness
   invariant in `Catalog.create`). Correct: the sink chapter proved two-claims-on-one-table is
   domain-real; foreclosing it at construction would be the M2. Adjudication lives at the
   right altitude (claims plane), not in the model invariants.
7. **`BoundedContextCandidate`** (BoundedContext.fs:7-22) reifies a *derived hypothesis*
   (community detection) and says so — anchor + members + edge counts, no containment
   assertion. That is the honest epistemic status for a discovered hierarchy; promoting it to
   a model-side containment grain without operator adoption would overclaim.

## 5 Already-aligned (exemplary reifications)

- **`SsKey` itself** — the four identity bases as a closed DU with per-variant equality,
  provenance-preserving cross-variant inequality, typed basis segments, and a recoverable
  length-prefixed codec (Identity.fs:80-236). The grain-level ancestor of the sink's
  `KeyBasis` honesty.
- **`DerivationReason`** — the closure precedent this audit's A2-1 asks to be applied to its
  sibling axis: closed DU + wire codec + fail-loud parse (Identity.fs:58-78).
- **`ConstraintState` and `IndexUniqueness`** — boolean quadrants collapsed to 3-state DUs
  with legacy-boolean wire projections and round-trip laws; illegal states unrepresentable
  (Catalog.fs:777-857, 994-1046).
- **`PhysicalClaimRules`** — the sink chapter's Core deposit: total adjudication ladder,
  closed outcome DU where Contested is a value (never a silent pick), and key-basis honesty
  inside the claim (`EntityId` positional + `EntityKey : SsKey option` native)
  (Strategies/PhysicalClaimRules.fs:33-106).
- **The coordinate VO triad + `TableId`** — private single-case DUs, construction-validated,
  with the case-insensitive comparison policy named once (`normalizedKey`,
  `tableTextEquals`) instead of scattered `=` (Coordinates.fs:113-356).
- **`RowBasis`/`RowQuantum`/`StaticRow`** — the WP-3 three-state cell semantics (NULL vs
  empty vs not-provided) held at the right grain in each carrier, with the IR↔quantum
  round-trip law and the omit-distinction deliberately unrepresentable positionally
  (Catalog.fs:82-237).
- **`Lifecycle`/`EpisodicLifecycle` append refusals** — non-monotone history is a *named*
  validation failure with metadata, never a silent reorder (Lifecycle.fs:85-96,
  Episode.fs:202-213); `netDiff`'s unreachable branch surfaces as a named `EmitError`
  rather than a silent fallback (NM-45).
- **`Catalog.create` aggregate invariants** — cross-record agreement laws live with the type
  (NM-14 `Type`↔`SqlStorage`), the pattern A2-2 asks to be extended to the PK quadrant
  (Catalog.fs:1971-2095).
- **`nameIndex`/`displayNameIn`** — display-by-name as a terminal projection that cannot
  become lookup-by-name, shared so consent tokens and narration cannot drift (Catalog.fs:1927-1951).
