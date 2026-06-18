# AUDIT 2026-06-17 — Faithfulness / No-Silent-Tightening Sweep

> **STATUS: branch-local working register (NOT yet canonical).** Lives on
> `claude/vector-wave-4-5`. Operator (2026-06-17) approved **all** findings for
> execution, **parked until after** the three active build threads (`Intervene.fs`
> seam, `--pretty` Slice 1, Wizard W0). This is the temporary holding document for
> that deferred work — promote to a canonical `AUDIT_*` + DECISIONS entries when we
> action it. Do not treat any verdict here as load-bearing law until adjudicated.

## Continuation status (2026-06-17 handoff)

Remediation execution is **in progress** — see `HANDOFF_2026_06_17_OPERATOR_INTERVENTION.md`
for the full branch picture. Dispositions so far:

- **F5 — ✅ DONE** (`c196164a`): the decimal default is settled by the V1-donor parity
  contract (`config/type-mapping.default.json`: decimal → (18,0), currency → (37,8)); the
  `(18,4)` outliers aligned to (18,0). 149 tests green, no golden movement.
- **F6 — mostly already addressed**: the advisory-pass thresholds are *already* named
  `let private` constants (the audit overstated "bare literals"). Residue is small: name the
  `QueryHintPass` "interpret as 80" inline assumption + soften the "no operator opinion"
  rationale on the 4 advisory `H-07x` passes. Low priority.
- **Safe/quick batch — ✅ DONE** (2026-06-17, one verified commit): **F8** (the conservation
  guard test), **F11** (in-code ledger of the imposed email/phone widths), **F12** (dormant
  `filterCatalog` flagged with its registration trigger), **F14** (`_comment` on the sample's
  opt-in tightening block), **F15** (`sampleSizeFloor` single-sourced), and **F6 partial** (the
  `QueryHintPass` "interpret as 80" named + its rationale softened). Per-finding dispositions
  below. Gates: fast pure pool 3474/0; DeployableReference 12/0; QueryHint 6/0.
- **Bounded F2/F13 batch — ✅ DONE** (2026-06-17, one verified commit): `filterPlatformAutoIndexes`
  (F2) gains an `OperatorIntent Emission` metadata entry in `RegisteredAllTransforms.all`; the
  static-row hydration adapter (F13, `fullExportHydration`) — already authored but never assembled
  into `all` — is now wired into the totality view. Binding tests added. Gates: fast 3476/0; the
  three registry totality classes (bidirectional 21, completeness 12, digest 7) green. The fuller
  execution↔registration *binding* for F2's emit-seam prune is **audit F3**.
- **F1 collation — ✅ DONE** (2026-06-17, individual heavy commit): full faithful carry
  (IR field + OSSYS read wiring + `COLLATE` emission), golden-neutral, witnessed both sides.
  See the F1 disposition below.
- **F10 IDENTITY — ✅ DONE** (2026-06-17, individual heavy commit): IDENTITY seed/increment
  is now IR-driven (`ColumnRealization.Identity`, default `(1,1)`) not a hardcode; golden-
  neutral; the read-side seed population is the named bound. See the F10 disposition below.
- **F3 totality — ✅ DONE** (2026-06-17, individual heavy commit): the post-chain emission
  rewrites are now a fourth BOUND source (`EmissionSeam`) covered by the `registered ⇔
  executed` proof (E5), closing the F2 counterexample's class. Golden-neutral. See the F3
  disposition below.
- **F4 ingest round-trip — ✅ DONE** (2026-06-17, individual heavy commit): a Docker
  forward-completeness facet-ledger test (source DDL → ReadSide → Catalog) holds every
  physical facet to carried-or-named-erased, closing the adjunction-proof ingest gap. See
  the F4 disposition below.
- **The heavy four (F1, F3, F4, F10) are all DONE.** Remaining: F6 follow-on (advisory-tuning
  config), F7-config-preserve + F7-audit (A37 promotion), F9 (deployed-reality nullability/
  identity divergence diagnostic) — all bounded/medium, no heavy structural or golden work.
- **Still pending.** **Bounded/medium** (F7-config-preserve = extend
  `renderConfig`+parse+the A44 generator to round-trip `tighteningRelaxations`, **touches the A44
  canary**; F6 follow-on = the advisory-tuning config for all four `H-07x` passes) · **heavy**
  (F1 collation → IR field + adapter read + `COLLATE` emission + **golden re-bless**; F3 totality
  → route post-chain rewrites through the registered chain seam, **structural**; F4 ingest
  round-trip → a **Docker** forward-completeness test; F10 IDENTITY seed/increment → IR field +
  goldens; F7-audit A37 promotion). Do the heavy four as individual verified commits, never one
  batch.

## Why this audit ran

Operator framing (stewardship principle): *"It's my dev team's decision and not mine
to adjust the data model… I'm just a steward of their data model… It should be pure
vanilla output/emission with **tracked** exceptions of when we are overriding it,
period."* — i.e. the faithful **skeleton** (`Project(catalog, Policy.empty, profile)`)
is the floor; every deviation must be a **registered, classified `OperatorIntent`
overlay** (`TransformRegistry.fs`, `OverlayAxis = Selection|Emission|Insertion|
Tightening|Ordering`). "Trustability, formal verifiability, and provenance are
first-class." The sweep hunts **obscured/opaque tightening** (any model deviation that
isn't a faithfully-tracked exception), transforms that belong in the registry but
aren't, and **subjectivity smuggled into objective code**.

Method: 6 parallel read-only audit agents over disjoint lanes — (1) nullability
provenance, (2) sibling structural passes, (3) default policy/config posture,
(4) registry completeness, (5) subjectivity/magic-numbers, (6) ingest+emission
boundary. Findings below carry each agent's **severity · confidence**, provenance,
verdict, and recommended action. A `Disposition:` line is left blank under each for
operator adjudication.

---

## ✅ Tier 0 — Clean bills of health (no action; recorded as proofs)

- **T0.1 — Nullability never tightens silently** (Lane 1). Adapter sets
  `IsNullable = not mandatory` (`OssysRowsetReader.fs:58`, `OssysJsonReader.fs:118`);
  `Composition.fanOut` short-circuits to identity under `Policy.empty` without
  consulting catalog/profile (`Composition.fs:136-140`, gated by
  `TighteningPolicy.nullabilityInterventions`, empty per `Policy.fs:788/922`); the pass
  is `OperatorIntent Tightening` (`NullabilityPass.fs:71`), excluded from `skeletonView`;
  additive-only emission `Nullable = a.Column.IsNullable && not enforceNotNull`
  (`SsdtDdlEmitter.fs:129`); proven at true execution by `SkeletonPurityTests.fs:57-83`.
- **T0.2 — Unique / FK / categorical / topo passes** (Lane 2). All `OperatorIntent`,
  Policy-gated via the same `fanOut` short-circuit, skeleton-excluded; the topo pass's
  mixed-classification (`sortKahn`=DataIntent, `selfLoop`=OperatorIntent Ordering) in
  `TransformRegistry.fs:54-57` matches `TopologicalOrderPass.fs:527-532` exactly.
- **T0.3 — Default out-of-box run = faithful skeleton** (Lane 3). `Policy.empty` is the
  only baseline (`Policy.fs:918-923`); omitted `policy` ⇒ `TighteningPolicy.empty`
  (`Config.fs:412-416`, `TighteningBinding.fs:170`); the shipped `runInit` writes no
  `policy` block (`Program.fs:276-289`). Tested at three layers:
  `TighteningBindingTests.fs:85`, pipeline byte-identity `Pipeline.fs:1522-1538`,
  property-axiom H-052 `PillarNineTests.fs:92` (via `AxiomTests.fs:1438`).
- **T0.4 — Emitters inject no collation/ANSI/filegroup opinion; JSON emitter is a pure
  projection** (Lane 6 B3/B4). `Projection.Targets.SSDT` grep for
  `COLLATE/ANSI_NULLS/filegroup` is clean; `JsonEmitter.fs:29-60` renders exactly the
  Catalog.
- **T0.5 — Discipline templates done right** (Lane 5 contrast cases): `Tolerance.fs`
  (named, closed `ToleratedDivergence` variants, fail-closed parse); the
  `*TighteningConfig`s (thresholds operator-supplied + validated + reasoned); the
  blessed `CorrectionProposer.classify` (`SyntheticCorrection.fs:322-329` — the only
  name-pattern heuristic, handled as an operator-blessed *proposal*). Hold the Tier-2
  items to these.

---

## 🔴 Tier 1 — HIGH: real faithfulness gaps

### F1 — Collation silently dropped at ingest · High · High *(VERIFIED 2026-06-17)*
- **Provenance:** read at `MetadataSnapshotRunner.fs:219` (comment: *"no V2 consumer"*);
  **no Catalog home** — `ColumnRealization` carries only `ColumnName`+`IsNullable`
  (`Catalog.fs:496-499`, verified); **no emission** — grep `COLLATE`/`Collation` over
  `Projection.Targets.SSDT` returns nothing (verified). `ADMIRE.md:2597-2615` documents
  the `onDisk`-envelope drop as a prose deferral.
- **Concern:** on a fresh deploy the team's chosen non-default collation is genuinely
  lost; this is **not** a closed named erasure (no `Tolerance` entry, no diagnostic).
  `AUDIT_2026_05_31` already classes collation as an L1-not-L2 silent erasure.
- **Recommended action:** give collation a typed `ColumnRealization` home + emit
  `COLLATE` faithfully; **or** register it as a closed `Tolerance` + loud adapter
  `Diagnostic`. Verified fix surface: `Catalog.fs:496` (add field), the SSDT emitter
  column path, and the two adapter readers.
- **Disposition:** ✅ DONE (heavy commit, 2026-06-17) — the **full faithful carry** (the
  preferred option; the `Tolerance` fallback was rejected as ill-fitting — collation is an
  ingest-boundary drop, not a canary *comparison* divergence). Added
  `ColumnRealization.Collation : string option` (`Catalog.fs`); `create`/`fromTyped` keep
  their 2-arg shape and default `None`, so the ~291 smart-ctor call sites are untouched and
  only ~4 record-literal sites changed. READ: the OSSYS rowset path now carries
  `sys.columns.collation_name` — `AttributeRow.Collation` (`OssysRowsetTypes`), populated
  from `cr.CollationName` in the `MetadataSnapshotRunner` reality join, threaded into
  `ColumnRealization` by `OssysRowsetReader`. EMIT: `ColumnDef.Collation`
  (`Statement.fs`) → a `COLLATE <name>` clause via `ScriptDom`'s
  `ColumnDefinition.Collation` (`ScriptDomBuild`). Witnesses: emit-side
  (`ScriptDomRoundTripTests` — COLLATE present↔absent, parse-verified) + read-side
  (`OsmRowsetReaderTests` — collation threads to `ColumnRealization.Collation`, non-collated
  stays `None`). **Golden-neutral**: `None` emits nothing → existing goldens byte-identical
  (master + pruned-platform-auto pass). Follow-on (named, not silent): the deployed-target
  **ReadSide** path still defaults `Collation = None` — reading `INFORMATION_SCHEMA.COLUMNS
  .COLLATION_NAME` there is the next slice; the JSON source does not expose collation.

### F2 — `filterPlatformAutoIndexes`: unregistered, silent, operator-intent catalog mutation on the live path · High · High — Lane 4 F1
- **Provenance:** def `Policy.fs:599-613`; live invocations `Pipeline.fs:582` (main emit)
  and `:1325` (dacpac). `Catalog → Catalog` pruning of `IsPlatformAuto` indexes when
  `IncludePlatformAutoIndexes=false`; **no LineageEvent, no Diagnostic, unregistered.**
- **Concern:** its own comment (`Pipeline.fs:577-582`) calls it `OperatorIntent Emission`
  but self-exempts ("evidence is policy, not catalog") — a rationale that doesn't hold,
  since `LogicalTableEmission` is also policy-driven and **is** registered. Exact
  untracked-operator-intent pattern.
- **Recommended action:** lift into the registered chain (mirror `LogicalTableEmission`)
  or register as a `Pipeline`-stage entry in `RegisteredAllTransforms.all`; bind its
  execution to its registration in a test; at minimum emit lineage.
- **Disposition:** ✅ DONE (bounded F2/F13 batch, 2026-06-17) — the **register** option.
  `filterPlatformAutoIndexes` now has a `RegisteredTransformMetadata.emitter` entry
  (`filterPlatformAutoIndexesMetadata`, `OperatorIntent Emission`) wired into
  `RegisteredAllTransforms.all`, mirroring the conditional emitters (DacpacEmitter /
  ConstraintFormatter — "registered-as-metadata, executed at their own emit-seam site").
  The metadata lives at the Pipeline assembly point, not Core (Policy.fs compiles before
  TransformRegistry.fs). Binding test: `F2 (audit): filterPlatformAutoIndexes is registered
  as an OperatorIntent Emission mutator`. NOTE: this closes "unregistered" — the FULLER
  structural lift (route the post-chain prune through the registered chain seam so
  execution↔registration is *bound*, and emit a per-prune LineageEvent) is **audit F3**,
  which this finding is the live counterexample for; tackle there. No goldens moved.

### F3 — The totality test cannot catch a mutator outside the bound sources · High · High — Lane 4 F2
- **Provenance:** `RegisteredAllTransforms.fs:54-88` (registry projected from
  chain/emit/read sources); `RegisteredAllTransformsBidirectionalTests.fs:323-361`
  (iterates only `Compose.emitSteps`/`readStep`/`chainSteps`). The `>= 21` count is a
  floor, not a closure.
- **Concern:** `registered ⇔ executed` is proven only over the three bound surfaces; any
  `Catalog → Catalog` running elsewhere is invisible. **F2 is the live counterexample.**
- **Recommended action:** route every post-chain catalog rewrite through the registered
  chain seam so "the chain is the only mutator" is structurally true; then the existing
  proof becomes a run-level totality guarantee. (Closes the whole class — high leverage.)
- **Disposition:** ✅ DONE (heavy commit, 2026-06-17). Introduced a fourth BOUND source —
  the post-chain **`EmissionSeam`** (`src/Projection.Pipeline/EmissionSeam.fs`). Every
  post-chain `Catalog → Catalog` rewrite is a `{ Metadata; Transform }` entry in one
  `rewrites` list; `apply` folds exactly those transforms and `metadata` / `executedNames`
  project from the SAME list, so `registered ⇔ executed` holds for the seam **by
  construction** (the E1 discipline). Both Pipeline call sites (main emit `:582` + dacpac
  `:1325`) now route through `EmissionSeam.apply` instead of a bare
  `filterPlatformAutoIndexes` call, and `RegisteredAllTransforms.all` splices
  `EmissionSeam.metadata`. New bidirectional tests `E5` pin both halves (every executed
  rewrite registered; the seam's registration set = its executed set, non-vacuously). The
  F2 counterexample is now structurally impossible: a post-chain mutator added outside the
  seam is an orphan `apply` would never run, and one added inside is automatically registered.
  **Golden-neutral** — `apply [filterPlatformAutoIndexes]` is byte-identical to the prior
  bare call (master + the seam-exercising pruned-platform-auto goldens pass). Residual bound
  (named): nothing structurally *forbids* a future bare post-chain call — the seam is the
  sanctioned route + the E5 test + the lint discipline, the same enforcement level the pass
  chain itself has; a `NoUnregisteredPostChainMutator` analyzer would be the belt-and-braces
  follow-on.

### F4 — Round-trip / adjunction tests don't close the loop through the OSSYS ingest adapter · High · High — Lane 6 C1
- **Provenance:** `AdjunctionLawTests.fs:160-197` reads via `PhysicalSchemaReader`/
  `PhysicalSchema.ofCatalog`, never `CatalogReader.parse`; the Docker canary
  (`CanaryRoundTripTests.fs`) uses ReadSide; the full Docker adjunction is `Skip`-ped
  (`AdjunctionLawTests.fs:211-219`). `AUDIT_2026_05_31:82` already flags ISO as
  one-directional.
- **Concern:** a deviation introduced at ingest (F1 collation, F9 nullability) is
  invisible to the adjunction proof.
- **Recommended action:** add a forward-completeness check `source DDL → ReadSide/OSSYS →
  Catalog` asserting every physical facet (collation, identity seed, deployed
  nullability) is present in the Catalog **or** named in a closed erasure set.
- **Disposition:** ✅ DONE (heavy commit, 2026-06-17). Added the Docker test `F4
  forward-completeness: each deployed column facet is carried into the Catalog or a named
  ReadSide ingest erasure` (`TransferCanaryTests`). It deploys a column-rich source (a NOT
  NULL identity PK, a NOT NULL column with a non-default `COLLATE`, a nullable column), reads
  it back through the ReadSide ingest, and holds every facet to an explicit LEDGER: **carried**
  (column name, nullability — asserted to round-trip), **erased** (collation = F1's ReadSide
  follow-on; identity seed/increment = F10's — each asserted `None`, the TRIPWIRE that fires
  when a follow-on wires the read, forcing the facet to move CARRIED and the ledger to
  update). This is the ReadSide leg; the OSSYS rowset leg's collation completeness is already
  witnessed by F1 (`OsmRowsetReaderTests`). Closes the "a deviation at ingest is invisible to
  the round-trip proof" gap by making the facet set explicit and tested. 1 green (warm
  container); purely additive (no production change, no goldens).

---

## 🟠 Tier 2 — MEDIUM: imposed shape & mislabeled subjectivity

### F5 — Inconsistent imposed decimal precision/scale (likely latent bug) · Med · High *(VERIFIED 2026-06-17)*
- **Provenance (4 sites, two answers):**
  - `(18,4)`: `SqlStorageType.ofPrimitiveType:121`; `ScriptDomBuild.dataTypeReference:167`
    (bare-`PrimitiveType`, no facets).
  - `(18,0)`: `SqlStorageType.ofSqlType`→`resolvePrecisionScale:186-187`;
    `OssysTranslation.fs:351` (facet path, scale absent). Currency hardcoded `(37,8)`
    at `OssysTranslation.fs:352`.
- **Concern:** fabricates precision/scale the source never declared, **and the paths
  disagree** for the same input — a probable latent bug, not just style.
- **Recommended action:** one named constant `defaultDecimalPrecisionScale` referenced by
  all four sites; emit a diagnostic when scale is imposed. The consistency fix is
  unambiguous; the **value (4 vs 0) is a steward decision** to raise with the team
  (financial vs counter semantics).
- **Disposition:** ✅ DONE (commit `c196164a`, 2026-06-17). The value question is settled by
  the V1-donor parity contract (`config/type-mapping.default.json`): **decimal → (18,0)**,
  **currency → (37,8)** — so `(18,4)` was simply the outlier, not a real fork. Aligned
  `SqlStorageType.ofPrimitiveType` and `ScriptDomBuild`'s no-precision arm to (18,0);
  currency (37,8) unchanged. 149 storage/emission/golden tests green, **no golden movement**
  (the bare-PrimitiveType fallback is never reached by a real catalog). The named-constant
  single-sourcing + an "imposed default" diagnostic remain a minor follow-on; the
  operator-prompt extension is moot since the config always supplies a default.

### F6 — Advisory `H-07x` passes embed tuning judgment as bare constants while self-classifying "no operator opinion" · Med · High — Lane 5 F1–F4
- **Provenance:** `SchemaComplexityPass.fs:36-40,114-119` (weight vector + caps);
  `QueryHintPass.fs:30-32,59-60` (fill-factor heuristic + an unstated "interpret as 80");
  `ProfileAnomalyPass.fs:30` (`2σ`); `CentralityPass.fs:34-36` (PageRank `0.85`/ε/maxIter).
- **Concern:** the exact "subjectivity mislabeled objective" unease — each is a chosen
  judgment presented under a `DataIntent`/"no operator opinion" rationale.
- **Recommended action:** lift to an optional advisory-tuning config the operator can
  override; soften the rationale to "default operator opinion, overridable." (These are
  advisory/diagnostic outputs, not the faithful projection — but the label is wrong.)
- **Disposition:** ◑ PARTIAL (safe-batch, 2026-06-17). `QueryHintPass`: the unstated
  "interpret as 80" is now a named `assumedServerDefaultFillFactor` constant surfaced in
  the operator-facing diagnostic, and the site rationale changed from "no operator opinion"
  to "a default operator opinion, overridable." REMAINING follow-on: lift the tuning
  constants of all four `H-07x` passes (SchemaComplexity weights/caps, ProfileAnomaly `2σ`,
  Centrality PageRank) to an optional advisory-tuning config, and soften the other three
  rationales the same way. Bounded, no goldens — a clean next batch.

### F7 — "Named erasures" are partly open-ended, not closed · Med · High — Lane 6 C2
- **Provenance:** A37 ("Π-erased axes named") is still a *Candidate* (`AXIOMS.md:1310-1315`);
  collation/`onDisk` are prose deferrals with re-open triggers (`ADMIRE.md:2525,2597-2619`),
  not closed `Tolerance`/`AxiomTests` witnesses; `WAVE_6_ONTOLOGY.md:363,446-448` says
  collation "must be a named tolerance" (i.e. isn't yet).
- **Recommended action:** promote the ingest-boundary erasure set to closed, enforced
  witnesses; promote A37 once closed.
- **Disposition:** ◑ PARTIAL (2026-06-17). **F7-config-preserve — ✅ DONE**: `renderConfig`
  (`MovementSurface.fs`) now round-trips `tighteningRelaxations` — added
  `ProjectionConfig.TighteningRelaxations : string list`, parsed via `getStringArray` and
  emitted when non-empty (omitted when `[]`, so a blessing-free config round-trips to no key
  — A44-neutral). The relax-ALWAYS blessing is now a first-class movement-vocabulary citizen,
  no longer lost on a render→parse cycle; the `RelaxationStore` surgical merge still targets
  the same key (no conflict). Witnesses: `MovementIsomorphismTests` — `A44 clause 1 — F7: the
  tightening relaxations block round-trips` + `… rides ALONGSIDE the movement vocabulary`; the
  A44 property canary (`parse ∘ render = id`) holds. **A37 promotion — ⏸ GATED, not forced**:
  A37 ("Π-erased axes named") is still a *Candidate* whose promotion criterion is "TBD at
  chapter 3.4 close" (`AXIOMS.md`) — it awaits the chapter gate (a finalized statement + an
  `AxiomTests` witness + the canonically-named erasure predicate), NOT any code I can land
  here. F1 (collation carried) and the F4 facet-ledger move the ingest-erasure set toward
  closure, but A37's promotion stays a chapter-close ritual obligation. Left as a forward-dated
  gate, deliberately not promoted prematurely.

---

## 🟡 Tier 3 — LOW / watch-items

- **F8 — SymmetricClosure faithfulness rests on `Reference.isDeployable`** applied at
  *every* emission site · Low(watch) · High — Lane 2 F5. Adds inverse references in the
  skeleton (classified `DataIntent`); kept faithful only by the deployability filter at
  5 verified emission sites (`SsdtDdlEmitter.fs:319,373,986,1033,1078`). A future 6th
  reference-consuming site without the filter would leak inverses as real constraints.
  **Action:** add a structural guard/test that every reference-consuming emission site
  filters `isDeployable`. **Disposition:** ✅ DONE (safe-batch, 2026-06-17). Added the
  CONSERVATION guard `F8 conservation: emitted constraints number exactly the deployable
  references…` (`DeployableReferenceTests.fs`): pins the run-level invariant (created FK
  constraints == deployable references; no ALTER names a never-created constraint) rather
  than the drifting 5-site list, so a future 6th site that leaks an inverse breaks it. 12
  green.
- **F9 — Adapter carries logical OSSYS nullability/identity, discards fetched deployed
  `#ColumnReality`** · Low-Med · Med — Lane 6 A5 (`OssysRowsetReader.fs:57-64` vs
  `MetadataSnapshotRunner.fs:214,220`). Depends which "source" the steward owns; physical
  evidence is read then ignored, undiagnosed. **Action:** operator call + diagnostic on
  divergence. **Disposition:**
- **F10 — `IDENTITY(1,1)` hardcoded** · Low · High — Lane 6 B1 (`ScriptDomBuild.fs:371-379`).
  Faithful for OS-native autonumbers; normalizes external/reflected identity seeds.
  **Action:** carry seed/increment in the IR if external tables are in scope; else note
  the bound. **Disposition:** ✅ DONE (heavy commit, 2026-06-17) — the IR now carries it AND
  the bound is noted. `ColumnRealization.Identity : (int64 * int64) option` (mirrors the F1
  Collation carry — smart ctors default `None`, only the ~4 record-literal sites changed);
  `ColumnDef.Identity` threads it; `ScriptDomBuild` emits `IDENTITY(seed, increment)` from
  the IR with a named `osNativeIdentity = (1L, 1L)` default replacing the bare `"1"`/`"1"`
  hardcode. So the emission is IR-driven and the IR can express a non-default seed, but
  `None` (every column today) emits `IDENTITY(1, 1)` **byte-identically** (goldens unmoved).
  THE BOUND (noted, not silent): no read path yet populates a non-default seed — OS-native
  autonumbers are `(1,1)`, and neither the OSSYS rowset extraction nor ReadSide reads
  `sys.identity_columns.seed_value/increment_value`. The re-open TRIGGER is an external/
  reflected identity column whose deployed seed ≠ `(1,1)` entering scope; wiring that read
  (sibling to the F1 ReadSide-collation follow-on) populates `Identity` and the emission
  already honors it. Witnesses: `ScriptDomRoundTripTests` (`IDENTITY(1000,5)` vs the default
  `IDENTITY(1,1)`, asserted on the re-parsed `IdentityOptions`).
- **F11 — email/phone/text-width imposition** · Low · High — Lane 6 A3
  (`OssysTranslation.fs:362-364`, `textLength` 306-310). V1-parity faithful but an
  undiagnosed inference relative to raw source. **Action:** ledger entry; optional
  diagnostic. **Disposition:** ✅ DONE (safe-batch, 2026-06-17). Ledgered IN-CODE at
  `OssysTranslation.parseSemanticType` (the email/phone arms): a named comment marks the
  250/20 widths as IMPOSED V1-parity inferences (the source declares no width), notes the
  explicit declared `length` always overrides, and flags the per-imposition diagnostic as
  the optional follow-on.
- **F12 — `SelectionPolicy.filterCatalog` dormant unregistered mutator** · Low · High —
  Lane 4 F3 (`Policy.fs:506-510`, no pipeline wiring). **Action:** register as
  `OperatorIntent Selection` when wired; track the trigger in DECISIONS. **Disposition:**
  ✅ DONE (safe-batch, 2026-06-17). Flagged IN-CODE at `SelectionPolicy.filterCatalog`:
  named DORMANT/unregistered with the explicit re-open TRIGGER — the first live invocation
  MUST register as `OperatorIntent (OverlayAxis Selection)` and bind execution↔registration
  in a test (mirroring the F2 `filterPlatformAutoIndexes` lift). No wiring today, so no
  registry entry is owed yet; the trigger now lives at the definition site.
- **F13 — `Hydration.graftStaticPopulations` unregistered adapter-side mutation** · Low ·
  Med — Lane 4 F4 (`Hydration.fs:43-54`). Benign `DataIntent` row carriage, but a
  model-touching transform absent from the registry. **Action:** register as an
  Adapter/`DataIntent` site (sibling to `ossysCatalogReader`). **Disposition:** ✅ DONE
  (bounded F2/F13 batch, 2026-06-17). The audit slightly overstated: the graft was already
  *authored* as a `DataIntent` adapter registration (`Hydration.registeredMetadata` =
  `fullExportHydration`, whose `staticRowHydration` site explicitly describes grafting rows
  onto the Static populations). The real gap was that this metadata was **never assembled
  into `RegisteredAllTransforms.all`** — registered in isolation, invisible to the unified
  totality view. Wired it into `all`; binding test `F13 (audit): the static-row hydration
  adapter (which grafts) is in the totality view as a DataIntent mutator`. No goldens moved.
- **F14 — `examples/projection.sample.json` ships an opt-in nullability intervention** ·
  Low(doc) · High — Lane 3 F6 (`:12-14`). Tracked + registered (consistent with the
  principle) but a copy-paste sharp edge. **Action:** add a clarifying comment that the
  `tightening` block is opt-in, not baseline. **Disposition:** ✅ DONE (safe-batch,
  2026-06-17). Added a `_comment` key inside `policy` (the parser ignores unknown keys, so
  it is safe for an operator who copies the file): "OPT-IN, not baseline. Omit this whole
  block for faithful vanilla emission… each interventions entry is a TRACKED operator
  override."
- **F15 — Minor DRY** · Low · Med — Lane 5 F6. `NumericDistribution.sampleSizeFloor=5`
  re-inlined as `5L` at `LiveProfiler.fs:525`; LiveProfiler caps (50/50/100) as separate
  literals. **Action:** single source of truth. **Disposition:** ✅ DONE (safe-batch,
  2026-06-17). `LiveProfiler.fs:525` now references `NumericDistribution.sampleSizeFloor`
  (the owner in `Profile.fs`) instead of a re-inlined `5L`. The 50/50/100 LiveProfiler
  capture caps are a separate, smaller DRY left as a noted follow-on (distinct concern —
  capture limits, not a shared statistical floor).

---

## Operator triage read (2026-06-17)

- **Genuine bug:** F5 (conflicting decimal defaults) — *verified*.
- **Genuine silent deviations a steward cares about most:** F1 (collation, *verified*),
  F2 (unregistered index prune).
- **Highest-leverage structural fixes** (catch the whole class): F3 (totality blind
  spot), F4 (ingest round-trip not closed).
- **Labeling/discipline:** F6, F7. **Bounded/benign:** Tier 3.

## Execution note

All approved; parked until the three build threads land. F1 and F5 are verified with
their fix surfaces mapped above and are ready for quick work when we return to this.
