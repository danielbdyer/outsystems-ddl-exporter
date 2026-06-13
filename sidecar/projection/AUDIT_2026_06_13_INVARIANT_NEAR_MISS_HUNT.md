# AUDIT 2026-06-13 — The Invariant Near-Miss Hunt

> **Provenance.** Operator-directed bug hunt, 2026-06-13, against HEAD `80f5951`
> (branch `claude/projection-invariant-audit-b6iknj`). A fleet of 12 parallel
> code-reading agents swept the entire `src/` tree (484 `.fs` files) under one
> charter: find **near-miss invariants** — logic that is *almost* a rule the
> system would want to name, enforce, and test, but currently isn't. Each agent
> also **re-verified** the canonical backlog (`CONFIRMED_BACKLOG_2026_06_09.md`)
> in its slice against current HEAD, because the tree moved hard since 2026-06-09
> (WP6 hydration, WP9 provenance, the "card" reconciliation slices, the golden
> corpus). Every `file:line` was read at HEAD; the three highest-stakes items
> were re-verified by hand before publication.
>
> This is an audit surface (provenance), not a backlog. It proposes invariants;
> it does not adjudicate them. `DECISIONS.md` adjudicates; the confirmed backlog
> remains the living ledger. Where this audit and a canonical surface disagree,
> the canonical surface wins and this file has a bug.

---

## 0 — What this is, in one breath

The codebase's soul is **named everything**: every refusal named, every tolerated
divergence witnessed with a retirement trigger, every transform `registered ⇔
executed`, every downgrade loud. A near-miss is the place where that discipline
*almost* holds — a field that incidentally gates behaviour it shouldn't, an
erasure with no `ToleratedDivergence` to its name, a decision computed and then
dropped, a capability built and never wired, a `match` whose catch-all would
swallow tomorrow's variant. The operator's seed example — `model.path` once
gating data emission instead of `model.ossys` — was codified into a named
invariant in commit `2157f47` *hours before this hunt began*. **This document is
the search for its siblings.** We found many.

---

## 1 — Executive summary

**~70 near-miss findings** across eleven thematic clusters. The signal is not a
scatter of unrelated bugs — it is **eight recurring shapes**, each a discipline
the codebase already owns at one site and lets slip at another. The strongest
finding in each shape:

| # | Shape | Headline finding | Sev |
|---|-------|------------------|-----|
| **G** | Silent row/refusal drop | `Reconciliation.reconcileKind` drops a **duplicate source surrogate** with `Error _ -> ()` — the named `surrogateRemap.duplicateSource` error is *built and thrown away*; a non-unique business key re-keys to exit 0 with no signal | **High** |
| **C** | Erasure without its named tolerance | `CatalogDiff.between` compares kinds **by name only** — a changed trigger / CHECK / modality / `IsActive` produces `norm d = 0`, "idempotent redeploy", **migrate emits nothing**; the canary's `PhysicalSchema.diff` *does* see them, so the two surfaces disagree on "what is a change," and no `ToleratedDivergence` names the gap | **High** |
| **E** | Built-but-unwired | `Episode.withProvenance` has **zero production callers** → `ChangeManifest.ToleranceResidual` + `AppliedTransforms` are **always empty** and **never persisted**; the change-accounting "under what equivalence?" plane is dead despite the backlog marking it CLOSED | **High** |
| **A** | Expressible-but-inert config (A44 inverse) | A whole cluster of config keys parse, validate, render — and feed nothing: `policy.insertion`, `policy.selection`, `policy.userMatching`, `EmitSchema`/`EmitDiagnostics`, `validationOverrides.allowMissingSchema`, flow `rekey`, `--streaming`/`--journal` — none carrying the `moduleFilter.flagsInert` named-skip the codebase established as the standard | **High** |
| **B** | Smart-constructor invariant bypass (A39) | Two decoders (`CatalogCodec`, `ProfileCodec`) rebuild aggregates with record-`with`, **bypassing the exact constructor** (`withConstraintState`, `withMoments`) Core designates as the sanctioned ingest path; `Catalog.create` never re-proves the bypassed invariant | **High (mechanism)** |
| **F** | Totality near-miss | A41's "totality coverage" test enforces a **stale hand-list that has drifted 7 passes** from the live `chainSteps` (asserts 18, chain runs 24) — the test passes while guaranteeing the wrong set | **High** |
| **D** | Sibling divergence | `MigrationDependenciesEmitter` omits `SET IDENTITY_INSERT` bracketing its sibling `StaticSeedsEmitter` performs — a **latent deploy failure** for an IDENTITY-PK migration kind, in two emitters whose docstring claims "the same algebra over the same plan shape" | **Med-High** |
| **H** | Fidelity-signal downgrade | `VersionedPolicy.bumpKind` gates the SemVer bump on intervention **IDs**, not content — a `NullBudget 0.0 → 0.5` (a material tightening change) keeps the ID set and **downgrades to a "cosmetic" PatchBump** | **Med** |

Two meta-conclusions for the backlog:
- **Closed faster than recorded.** A3, A6, B1, B3, B5, D2, D3, D4, D8, A2, A5 verified **CLOSED-SINCE** 2026-06-09. B2/B8 substantially closed.
- **Closed shallower than recorded.** `ChangeManifest ToleranceResidual+AppliedTransforms` (marked CLOSED) is type-closed but wiring-open. B4's "exception-leak premise stale" verdict is itself wrong — it held only for `read`; six sibling raw-`Task` readers, *including the CDC integrity gate*, still leak.

---

## 2 — Method, taxonomy, and the template

**The template (commit `2157f47`).** `resolveFlowSpec` classified the
publish-with-provenance arm only when `SourcePath ∧ store ∧ Shaping.Model.Path`
were *all* present — so an OSSYS-sourced config (no `model.path`) never published
with provenance even with a store. The fix made the gate `Path OR Ossys` and
audited every runtime `cfg.Model.Path` read. The bug's signature: **behaviour
keyed on the presence of an incidental field that is a poor proxy for the real
condition.** Every cluster below is graded against that signature and the
codebase's own standing laws.

**The five categories** each finding is tagged with:
1. **Accidental/incidental gating** — behaviour keyed on a field that semantically shouldn't gate it.
2. **Silent downgrade / unnamed refusal** — identity/empty/skip with no named diagnostic.
3. **`{ X.create … with … }` default inheritance** — record-`with` over a smart-ctor result silently inheriting omitted defaults (CLAUDE.md survival rule #7).
4. **Totality near-miss** — a `match`/equality/hand-list that would silently swallow a new case.
5. **Half-wired refactor / one-off** — built-but-unwired; asymmetric encode/decode or forward/reverse.

Findings carry stable IDs (`NM-NN`) for citation. Confidence is the agent's,
spot-checked for the High tier.

---

## 3 — Findings by cluster

### Cluster A — Expressible-but-inert config (the A44 inverse defect)
*A44 says expressible ⇔ reachable. These are config the operator can write,
validate, and see rendered — that reaches no behaviour, and (unlike the model
`moduleFilter.flagsInert` note) carries no named-skip. They are the `model.path`
template inverted: not "an incidental field gates," but "a deliberate field
gates **nothing**, silently."*

- **NM-01 · `policy.insertion` binds into `Policy.Insertion`, no consumer reads it.** `InsertionPolicyBinding.fs:40-61` → `Pipeline.fs:964,987`. The binder's own docstring concedes no emitter/data pass reads `.Insertion`; only policy-algebra/diff do. `TruncateAndInsert` changes the manifest snapshot and zero behaviour, no note. *Cat 5+2 · High.* **Invariant:** a non-default policy axis that no emitter consumes must emit `policy.<axis>.notYetConsumed`, or not parse.
- **NM-02 · `EmitSchema` / `EmitDiagnostics` are inert fields a validator still defends.** `Policy.fs:100-102,467`; never read (`.EmitData` is, at `Pipeline.fs:597,1460`). `EmissionPolicy.create` loudly refuses an all-false policy across three artifact families — but two of the three gate nothing, so the refusal guards a guarantee the config can't express. WP4 flagged these "become real or be removed." *Cat 5 · High.* **Invariant:** every disjunct of the `allFalse` refusal must gate a real emit step.
- **NM-03 · `policy.selection` + `policy.userMatching` parse but never thread into `Policy`.** `Config.fs:1330,1145`; `Pipeline.fs:953-955` self-documents them "dormant pending operator-pull." Operator-writable, silently inert. *Cat 5 · High.*
- **NM-04 · `model.validationOverrides.allowMissingSchema` parses; the only would-be consumer (`ModuleFilter`) explicitly disclaims the axis.** `Config.fs:50,681-684` vs `ModuleFilter.fs:70-71` ("V1 carried a `ValidationOverrides` axis that this port does NOT carry"). Structurally impossible to reach. (`allowMissingPrimaryKey` *is* consumed — so this is one dead key beside a live sibling.) *Cat 5+2 · Med-High. Found independently by two agents.*
- **NM-05 · Dormant sections `cache` / `typeMapping` / `profile.path` / `profiler.mockFolder` parse, consumed by nothing, no named-skip.** `Config.fs:69-103,760,768,788,805`. `profiler.provider` *is* consumed; `mockFolder` is not. Contrast `onlyActiveAttributes`, whose deferral *is* named (`SnapshotScopeBinding.fs:28-33`). *Cat 5+2 · Med-High.*
- **NM-06 · Flow `rekey` field parses → `LoadOpts.Rekey` → consumed by no runner.** `MovementSurface.fs:300,960,593`. The orphaned predecessor of `reconcile`; render round-trips it, reinforcing the illusion it works. *Cat 5+2 · High.* **Invariant:** `LoadOpts.Rekey` either translates to a reconcile or is removed with a redirecting refusal.
- **NM-07 · `--streaming` / `--journal` silently dropped on every non-reverse-leg flow.** `MovementSurface.fs:969-970,1103-1104`; consumed only by `runReverseLegTransfer` (`Program.fs:160`). `planMovement` has `freshNote`/`resumableNote`/`synthesisNote` for exactly this situation but **no** `streamingNote`/`journalNote`; the named refusal lives *inside* the reverse-leg face, so it never fires for `Transfer`/`Migrate`. *Cat 2 · High.*
- **NM-08 · Synthetic-load flow skips `applyModuleFilter` — `model.modules` scope silently ignored.** `SyntheticLoadRun.fs:66` vs `Program.fs:100-107` ("the SINGLE shared module-filter seam"). A `from: synthetic` flow emits the full estate while every sibling action narrows. *Cat 1+2 · High.* Root cause **NM-09**.
- **NM-09 · `ModelResolution.resolveCatalog` bypasses the module filter at its own layer.** `ModelResolution.fs:40-46`. The filter is re-added per-caller (`Program.needCatalog` remembers; `SyntheticLoadRun` forgot, → NM-08). The structural root: the filter is *outside* the shared read primitive and will recur at the next caller. (`readConfigModel` correctly fuses it, `Pipeline.fs:1211`.) *Cat 1 · Med.* **Invariant:** the module filter is applied at the read seam, not by caller convention.
- **NM-10 · Flow names colliding with secondary verbs are silently unreachable.** `MovementSurface.fs:1065-1076`: the `check/explain/diff/seal/report/profile` arms precede the `Map.containsKey first cfg.Flows` arm. A `flows: { "report": … }` parses, renders, lists — and `projection report` runs the verb, never the flow, with no guard. *Cat 1+2 · Med.*

### Cluster B — Smart-constructor invariant bypass (A39 re-validation gaps)
*A39: aggregate-root smart constructors carry the invariants; a decoder/rebuild
must re-prove them. These rebuild with record-`with`, stepping around the one
constructor that proves the law — exactly the survival-rule-#7 hazard.*

- **NM-11 · `CatalogCodec` decode sets `(HasDbConstraint, IsConstraintTrusted)` directly, bypassing `withConstraintState`.** `CatalogCodec.fs:783-789`. Core's docstring (`Catalog.fs:1226-1237`) *names the codec* as a producer that must route through `withConstraintState`, "making the illegal quadrant unreachable in practice." `Catalog.create` does not re-prove `isConstraintStateConsistent` (G14: `¬trusted ⟹ HasDbConstraint`). A hand-edited `(false,false)` deserializes clean. *Cat 4+2 · High mechanism.*
- **NM-12 · `Reference.withConstraintState` normalizer is bypassed by `Reference.create` and the record-`with` override idiom too.** `Catalog.fs:1233-1237` vs `1195-1211`. The guard exists; "every producer routes through it" is convention, not a type guarantee, and `Catalog.create` doesn't validate the quadrant. NM-11 is one instance of this general back door. *Cat 1+5 · Med.* **Invariant:** add `isConstraintStateConsistent` to `Catalog.create`'s invariant set; the illegal quadrant fails construction on every path.
- **NM-13 · `ProfileCodec.readNumeric` reattaches `Moments` via record-`with`, bypassing `withMoments`'s `Min ≤ Mean ≤ Max`.** `ProfileCodec.fs:378-379`. `NumericDistribution.create` always sets `Moments = None`; the codec overwrites it, skipping the only constructor that proves the moment range — contradicting the codec header's "re-proves every leaf invariant." *Cat 4 · High mechanism.*
- **NM-14 · `(Type, SqlStorage)` consistency invariant is asserted in tests, never enforced at construction.** `SqlStorageType.fs:79`, docstring `Catalog.fs:674-677` states `toPrimitiveType storage = Type`; `Catalog.create`'s five invariants don't include it. An adapter can set `Type = Text; SqlStorage = Some BigInt`; the emitter renders `BIGINT` while every type-driven decision uses `Text`. *Cat 5 · Med.*
- **NM-15 · `Attribute.create` default-`Column` derivation `failwithf`s — the only non-total smart constructor in `Catalog.fs`.** `Catalog.fs:1151-1158`. Every sibling returns `Result` and routes over-128-char failures through `ValidationError`; this one throws, mid-IR-build, on a long logical name — while `IdentifierBudget.fit` deterministically *fits* over-budget names elsewhere. A hidden precondition (caller must pre-override `Column`) not encoded in the type. *Cat 2+5 · Med.*

### Cluster C — Erasure without its named tolerance
*The `ToleratedDivergence` registry (with retirement trigger + witnessing fixture)
is the codebase's instrument for "nothing lost in silence." These erase without
an entry — the C2 empty↔NULL case is the *good* example each of these is missing.*

- **NM-16 · Kind-level facets are an unnamed silent erasure in the `CatalogDiff` algebra.** `CatalogDiff.between` compares kinds **only by name** and descends only into attributes/references/indexes/sequences (`CatalogDiff.fs:474-489`, verified by hand). `Kind.Triggers`, `ColumnChecks`, `Modality`, `Description`, `IsActive`, `ExtendedProperties` (`Catalog.fs:1020-1060`) produce **no diff signal**: `isEmpty d = true`, `norm d = 0`, `migrate A B` emits nothing, `SchemaNorm = 0` = "idempotent redeploy." The canary's `PhysicalSchema.diff` *does* compare them — the two surfaces disagree on "what is a change," and **no `ToleratedDivergence` names it.** Violates T16 / "nothing lost in silence." *Cat 2 · High.* **Invariant:** either a `KindFacet` DU mirroring `AttributeFacet` (with `applyDiff` patches), or named `KindTriggersUnreflected` / `KindModalityUnreflected` / `KindChecksUnreflected` tolerances with triggers + fixtures.
- **NM-17 · `CatalogDiff.compose`'s composability bridge uses `isEmpty` — inheriting NM-16's blindness.** `CatalogDiff.fs:986-991`. Two diffs whose intermediate catalogs differ *only* in trigger/check/modality are declared composable (adjacent) when they are not; the net `between` silently absorbs the mismatch. Weakens the T13 groupoid-composition partiality specifically. *Cat 1+2 · Med (depends NM-16).*
- **NM-18 · `SqlLiteral.ofRaw` conflates `""` ↔ NULL for *all* types, including `Text` and zero-length `Binary`, unconditionally.** `SqlLiteral.fs:75-76`: the `raw = "" → NullLit` test fires *before* the type match. The named tolerance `EmptyTextNormalizedToNull` scopes only *text*; a genuinely empty `TextLit ""` (`N''`) can never be emitted, and `BinaryLit []` (`0x`) collapses to NULL — the round-trip law breaks for these and is unnamed. *Cat 2 · High.*
- **NM-19 · `bit`/boolean columns silently bucket as `StringValue "True"` in the evidence cache.** `EvidenceCache.fs:78-96`: the type dispatch has int16/32/64/byte/decimal/float/string/DateTime/byte[] but **no `:? bool` arm** → `other -> StringValue`. OutSystems is bit-heavy; the column surfaces as a 2-value text categorical and mis-keys `HasDuplicates`. The sibling `ReadSide.formatRawValue` *has* a `Boolean` arm — the two adapters disagree. *Cat 4 · Med-High.*
- **NM-20 · `RawValueCodec.parseBoolean` silently returns `false` for any unrecognized input.** `RawValueCodec.fs:87-90`. `"tru"`/`"2"`/`"yes"` → `false`, a real BIT divergence with no diagnostic; every sibling parser (DateTime/Guid) throws on malformed input — boolean is the one axis where a wrong value is indistinguishable from a legitimate one. *Cat 2 · Med.*
- **NM-21 · Synthetic FK to an empty parent pool emits NULL even for a NOT-NULL column, with no Core-level diagnostic.** `SyntheticData.fs:343-357`: `None` unconditionally, regardless of `IsNullable`. The design says "a non-nullable FK to an empty parent is named — the load surfaces it," but σ emits **no** lineage event / diagnostic; it relies on a downstream load-time failure that may not run (DryRun preview). *Cat 2 · Med.*
- **NM-22 · `JsonEmitter` silently drops many IR fields while its registry metadata claims "every IR field maps 1:1."** `JsonEmitter.fs:70-88,260-261`. `writeAttribute` omits `Length/Precision/Scale/IsIdentity/Computed/SqlStorage/…`; the "1:1" assertion is provably false. The FK-feature omission *is* named in metadata; the per-attribute omissions are not. *Cat 2+5 · Med (doc-defect High).*
- **NM-23 · `DecisionLogEmitter` drops catalog-level (`SsKey = None`) diagnostics at the per-kind seam with no count witness.** `DecisionLogEmitter.fs:104-117`. Documented as a deferral, but unlike the SSDT FK-drop witness, no `Warning`/count records that catalog-level entries were shed. *Cat 2 · Med/Low.*
- **NM-24 · `DacpacEmitter` silently drops an unparseable `CreateTrigger` (`_ -> ()`).** `DacpacEmitter.fs:109-116`. The SSDT directory path has a round-trip canary backstop; the DACPAC path has zero signal, unlike the FK-drop witness. *Cat 2 · Med.*

### Cluster D — Sibling divergence / asymmetric pairs
*T11 says siblings agree by construction. These siblings encode different theories
of the same thing, with no shared predicate and no test pinning their agreement.*

- **NM-25 · `MigrationDependenciesEmitter` omits the `SET IDENTITY_INSERT` bracketing its sibling `StaticSeedsEmitter` performs.** `MigrationDependenciesEmitter.fs:190-231` has no `bracketIdentity` at all; `StaticSeedsEmitter.fs:335-336` brackets on `AssignedBySink`. An IDENTITY-PK migration kind emits a MERGE that INSERTs explicit PK values **without `IDENTITY_INSERT ON`** → SQL Server rejects at deploy. The two docstrings claim "the same algebra over the same plan shape." *Cat 5+2 · Med-High (latent).* **Invariant:** identity bracketing is single-sourced through one `needsIdentityInsert` helper both call.
- **NM-26 · `StaticPopulationEmitter` brackets IDENTITY on `hasIdentity` (any IsIdentity attr) while `StaticSeedsEmitter` brackets on `AssignedBySink` (PK only).** `StaticPopulationEmitter.fs:95-96` vs `StaticSeedsEmitter.fs:335-336`. Two predicates for "needs IDENTITY_INSERT," no shared definition, no agreement test. *Cat 5 · Med.*
- **NM-27 · `DistributionsEmitter` writes percentile decimals via `WriteNumber(decimal)` while `ProfileCodec` writes them via `WriteString(inv …)`.** `DistributionsEmitter.fs:91-98` vs `ProfileCodec.fs:118-131`. The Distributions metadata *claims T1 byte-determinism* for exactly these fields while using the non-canonical path (`WriteNumber` preserves trailing-zero scale: `10.0` vs `10`) that its sibling deliberately rejected. *Cat 5+2 · Med.*
- **NM-28 · `PhysicalSchema.toPhysicalForeignKeys` resolves only the *first* PK column of the target.** `PhysicalSchema.fs:645-651`: `targetPkColumnByKey = List.tryFind IsPrimaryKey`. The type's own docstring promises composite handling "by construction"; a composite FK is compared against one target column, so the canary cannot detect drift in the second leg. Also drops unresolvable refs via `List.choose _ -> None` with no diagnostic — the silent-drop shape `Catalog.create` was hardened against, relocated. *Cat 1+5 / 2 · Med (NM-28 / NM-28b).*
- **NM-29 · `SqlTypeCorrespondence.ofSqlDataType` is the lossy inverse named in the round-trip "law," coexisting with the faithful `SqlStorageType.ofSqlType`.** `SqlTypeCorrespondence.fs:80-99`. `BIGINT/SMALLINT/TINYINT → Integer → INT`; a consumer can silently pick the lossy parser. *Cat 5 · Med.*
- **NM-30 · `ReadSide` `attach*` helpers are asymmetric: `attachDefaults`/`attachComputed` early-out on an empty map; `attachAnnotations`/`attachIndexes` always walk.** `ReadSide.fs:1196,1221`. An empty `sys.default_constraints` read (which can mean *permission-filtered*, not *no defaults*) is indistinguishable from "no defaults" and produces a silently default-free catalog. *Cat 5 · Low.*
- **NM-31 · Streaming reverse leg threads `allowDrops` to narration only, not into the engine.** `RunFaces.fs:802-806` / `TransferRun.fs:1449`. The materialized arm offers a pre-write orphan halt; the streaming arm structurally cannot — yet `ReverseLegRealization.choose` presents the two as interchangeable. (Not a silent drop today; a capability asymmetry the selector should name.) *Cat 5 · Low.*

### Cluster E — Built-but-unwired (half-wired refactors, zero/one-caller seams)
*"IR/verbs grow at the second consumer; zero-consumer symmetry-builds get
deleted" (the dead-algebra precedent). These are built, sometimes tested, and
reach production nowhere.*

- **NM-32 · `Episode.withProvenance` has zero production callers → `ChangeManifest.ToleranceResidual` + `AppliedTransforms` are always empty.** `Episode.fs:122-127` (verified: only self-reference at `:94`). Every production `Episode` is built by `Episode.create`/`Migration.toEpisode` (strict, `[]`). The change-accounting "under what equivalence was this accepted?" plane — its byte-determinism sort, its persistence, its docstrings — is dead. *Cat 5 · High.* **Backlog correction:** `ChangeManifest …` is marked CLOSED; the type closed, the wiring did not.
- **NM-33 · `LifecycleStore` neither persists nor restores `Tolerances`/`AppliedTransforms`.** `LifecycleStore.fs:74-101,210`. `durableProjection` *keeps* them (drops only `Profile`), so a stored-then-loaded episode with provenance would not equal its own `durableProjection`. Even if NM-32 were fixed, the next store round-trip would silently drop it. *Cat 5 · High.* (NM-32 + NM-33 are one wound: the provenance plane is both unpopulated and non-durable.)
- **NM-34 · Persisted `run.json` always has empty `Ledgers`, `Artifacts`, and `InputDigest = ""`.** `RunEnvelope.fs:76` (the sole production `Run.save`): `Run.capture` hardcodes `Ledgers = []`, digest `""`, artifacts empty. `RunHistory`/`Run.diff` dedup over `InputDigest` is **structurally inoperative**; the `LedgerRef` codec + reader are exercised only by tests. R1a/R1b's discriminating predicates are unmet at the one site that persists a run. *Cat 5 · High.*
- **NM-35 · `ComposeState.CategoricalUniquenessDecisions` is dead storage — written every run, read by nothing.** `ComposeState.fs:23,76-80`; `RegisteredTransforms.fs:145` wires the writeback; no reader exists. Three of four tightening decision-sets are projected by `DecisionOverlay.ofComposeState`; this fourth is not. The pass docstring claims it's "emitter-consumable" (false). The `SuggestUnique` outcome reaches consumers only via the parallel lineage-event channel; the field is dead. *Cat 4+5 · Med-High.*
- **NM-36 · `runCascadeShockZones` — a fully built operator-facing cascade-delete-risk diagnostic with zero production callers.** `TopologicalOrderPass.fs:718,790`. Public, typed, Bench-labelled, tested — and not in `chainSteps`, not in the registry. The `topology.cascadeShock` warning never runs in production. *Cat 5 (A41) · High.*
- **NM-37 · `View.Trail` is render-defined with zero producers; `runExplain` hand-rolls the trail it was built for.** `View.fs:41,141-146` vs `RunFaces.fs:1218-1224`. The transform-trail surface has no JSON/`--query` lens because it bypasses the View engine in raw `printfn`. *Cat 5 · High.*
- **NM-38 · `ConstraintFormatter.Disabled` (the operator V1-parity/bisect opt-out) is unreachable from any production path.** `Render.fs:148` hardcodes `ConstraintFormatter.Enabled`; no `toTextWith mode` variant exists. The registry classifies all seven sites as `OperatorIntent Emission` — advertising an overlay axis no run can select. *Cat 1+5 · High.*
- **NM-39 · `TransformRegistry.all = []` is a dead placeholder beside the populated `RegisteredTransforms.all` / `RegisteredAllTransforms.all`.** `TransformRegistry.fs:326-333`. A consumer reaching for `TransformRegistry.all` gets an empty registry with no error. *Cat 5 · Med.*
- **NM-40 · `runResumable` / `runReverseLeg` / `runWithEmissionMode` are zero-production-caller seams.** `TransferRun.fs:878,1036,891`; callers only in `MigrationCanaryTests`. Production routes through the `*ThroughConnections*` family. Legitimate test seams — but unbanner'd, a future agent may mistake them for the production path. *Cat 5 · High fact / Low defect.*
- **NM-41 · `valueFidelityFor` is the lone non-`private` member in an otherwise-sealed `SyntheticData`, with one internal caller and no test.** `SyntheticData.fs:206`. Either a leaked `private` or an untested seam. *Cat 5 · Med.* **(Adjacent:** `CycleResolution.neverResolve` `:111` — tested, zero production callers, a legitimate opt-out primitive. *Low.)*

### Cluster F — Totality near-miss (the surface that looks total but isn't)
- **NM-42 · A41 "totality coverage" is enforced against a stale hand-list that drifted 7 passes from the live chain.** `TransformRegistryCompletenessTests.fs:77-90,196-197`: `allRegistrations` is a hand-rebuilt 18-entry list (omits `LogicalTableEmission`, `LogicalColumnEmission`, `CentralityPass`, `BoundedContextPass`, `ProfileAnomalyPass`, `SchemaComplexityPass`, `QueryHintPass`); the live `chainSteps` runs 19 passes + 5 strategies = 24. The test that names itself "every transformation site has a registry entry" passes while asserting a list that *has already drifted*. (The live bidirectional surface, `RegisteredAllTransformsBidirectionalTests.fs:44`, is sound; this completeness file is the trap.) *Cat 4+5 · High.* **Invariant:** project the hand-list from `RegisteredTransforms.all`, or assert equality so drift fails loudly.
- **NM-43 · The reverse direction `registered ⊆ executed` is untested.** `RegisteredAllTransformsBidirectionalTests.fs:322-360` asserts only `executed ⊆ registered`. A typo'd registration name, or a `ChainStep` removed while its metadata lingers, is invisible. *Cat 4+2 · Med.*
- **NM-44 · `passTags` coverage is one-directional — a pass that *should* be group-toggleable but is untagged silently always-runs.** `TransformGroupsBinding.fs:83-97`: `tagsFor` falls back to `Set.empty` ("untagged passes always run"); the coverage test checks only that every `passTags` key exists, never that every group-class pass is tagged. **This is the exact dual of the `model.path` template** — accidental *non*-gating: a future `Tightening`-class pass added untagged ignores `Tightening: false`. *Cat 4+2 · Med.* **Invariant:** derive tags from the pass's `Sites` classification so the tag set cannot drift from the intent.
- **NM-45 · `Lifecycle.netDiff` / `Episode.netSchemaDiff` swallow `compose`'s fail-loud `None` into a silent `directNetDiff` fallback.** `Lifecycle.fs:196-198`, `Episode.fs:255-257`. `compose` is explicitly partial/fail-loud ("never a silently-wrong result"); both consumers convert the loud refusal to a silent substitution, with no test exercising the `None` branch. *Cat 4 · Med.*
- **NM-46 · `Watch` done-frame never renders for a run that halts at its terminal stage.** `Watch.fs:332-334`: `isTerminal = forall (Done _)`; a `Halted` final stage makes it false → no closing frame, the board stops on the red `✕` in silence, violating §13's "name the next move." *Cat 2+4 · Med.*
- **NM-47 · `renderVoicedTo` silently renders nothing for an unvoiced code.** `TtyRenderer.fs:361-363`: `None -> ()`. Every run-face verdict flows through it; a typo'd or new face code is an invisible verdict, not a loud failure. The totality test covers the closed `inScopeCodes` set, not the call sites. *Cat 2 · Med.*
- **NM-48 · Hydration's both-absent case (`Ossys = None ∧ Path = None`) silently yields no diagnostic.** `Hydration.fs:69-78`: the final `None -> []` emits nothing, relying on a distant upstream `pipeline.config.modelNoSource` to be unreachable. The local "named skip, never silence" law holds only by remote guard. *Cat 4 · Low.*
- **NM-49 · `captureChunkDescending` ladder start is silently empty if `preferred ∉ ladder`.** `SurrogateCapture.fs:298`: `skipWhile (<> preferred)` consumes the whole list → `invalidOp "capture ladder exhausted"` mislabels "unknown preferred lane" as "exhausted." Positional, not structural. *Cat 4 · Low.*
- **NM-50 · `CatalogTraversal.mapKinds` is a drop-capable (`List.choose`) combinator on the emission hot path.** `LineageBuffer.fs:84-92` → `LogicalTableEmission.fs:111`. Safe today (visitor always returns `Some`), but a future `None` would silently shrink the catalog with no lineage event — A1 identity preservation rests on convention, not type. *Cat 2+1 · Med.*

### Cluster G — Silent row / refusal drops in the transfer engine (highest stakes)
*The data plane's cardinal law: no silent row drop; every erasure surfaces, every
run. These are the places it bends.*

- **NM-51 · `reconcileKind` drops a duplicate source surrogate with `Error _ -> ()` — the named error is built and thrown away.** `Reconciliation.fs:111` (verified by hand). `SurrogateRemapContext.capture` constructs `surrogateRemap.duplicateSource` (`SurrogateRemap.fs:104`); the call site discards it "keep first." The header makes uniqueness a *precondition assumed but not enforced*: a `MatchByColumn` business-key reconcile or a CSV `--user-map` with a non-unique PK-column value silently discards the second binding — no `Unmatched`, no skip, **exit 0**. *Cat 2 · Med (reachable via MatchByColumn).* **Invariant:** the swallowed `duplicateSource` errors accumulate into a named `ReconciledIdentity.Ambiguous` surfaced on `TransferReport`, or fail-loud at Execute like `validateUserMap`.
- **NM-52 · `reconcileAgainstSink`'s per-kind merge swallows `capture` errors unnamed.** `TransferRun.fs:682`: `Error _ -> ()` while the docstring claims it "carries the construction-time invariant." Unreachable today (NM-51 collapses within-kind dups first), but a defense-in-depth swallow that masks the exact corruption the error was built to flag. *Cat 2 · Low.*
- **NM-53 · Resumable G10 re-run of a completed transfer reports zero drops.** `TransferRun.fs:517`: the marker records completion, not the drop-set, so `if already then return [], []` → `SkippedReferences = []` → exit 0 on a run that previously (legitimately, exit 9) erased FK-orphans. The streaming-journal sibling documents this as per-run; the G10 path carries no such caveat. *Cat 2+5 · Med.*
- **NM-54 · The CDC write-refusal gate leaks exceptions, un-retried.** `ReadSide.cdcTrackedTables` (`ReadSide.fs:1554-1566`) is the integrity gate that refuses an Execute write against a CDC-tracked sink — and has no `try/with`. A transient Azure `SqlException` or a `VIEW DEFINITION` denial propagates unwrapped through the gate as a raw stack trace, not a structured CDC refusal. `cdcCaptureCount` (`:1662`) similarly throws on a missing/custom-named CT table, aborting the data-norm measure. **`Retry`/Polly is applied to the sibling `MetadataSnapshotRunner` but not here.** *Cat 2+5 · High.* **Backlog correction:** B4's "exception-leak premise stale (caught by `read`'s try/with)" held *only for `read`*; six sibling raw-`Task` readers leak.
- **NM-55 · The capability survey downgrades a grant-probe failure to "fully capable."** `CapabilitySurvey.fs:240-248`: `match grantEv with Ok ev -> reconcile … | Error _ -> []`. `Preflight.captureGrantEvidence` returns a *named* `migrate.grantProbeFailed`; the survey discards it, so `Missing = []`, `Reachable = true`, and the view renders "reachable · grant covered" for a place whose grant was never readable. The standalone `survey` exit-7 gate passes too. *Cat 2 · High.* **Invariant:** an unreadable grant on a reachable place is its own `GrantUnreadable` report state and blocks, mirroring `UserDirectoryProbe.absent`.
- **NM-56 · `full-export` swallows a malformed-timeline error into the genesis path with no note.** `RunFaces.fs:58-62`: the operator supplied `--store` (intending a recorded episode); a bad `--env` label is discarded (`Error _`) and the run silently downgrades to store-less genesis (`storeLeg = None`) — the "recorded as episode N" line never prints. The sibling `runFullExportLoad` *does* surface the same error. *Cat 2 · High.*
- **NM-57 · Absent grant facet (`None`) treated as maximally-permissive `SchemaAndData`.** `CapabilitySurvey.fs:103-108`: `(Some SchemaAndData | None) -> full write set`. A place with no declared `grant` is required to hold the largest capability set, inverting the conservative default and producing spurious exit-7 advisories; the coarse peer `requiredFor` has no `None` case, so the two reconciliation surfaces disagree. *Cat 1 · Med.*

### Cluster H — Fidelity-signal & code↔copy integrity
- **NM-58 · `VersionedPolicy.bumpKind` gates the SemVer bump on intervention *IDs*, not content.** `VersionedPolicy.fs:167-215`. A `NullBudget 0.0 → 0.5` or `EnableCreation true → false` keeps the ID set → `isStructural = false` → falls to **`PatchBump`**, whose docstring says "the structural shape is unchanged." A material tightening change is classified cosmetic — a silent downgrade of the version signal consumers trust. *Cat 1+2 · Med.* **Invariant:** compare intervention content (structural equality / digest), not the ID set, for the Minor-vs-Patch decision.
- **NM-59 · `summary.readiness` and `summary.stageProgress` are emitted on the structured channel but absent from the `knownEmittableCodes` ledger.** `RunFaces.fs:1026`, `LogSink.fs:702`. The `inScopeCodes ↔ Voice.all` bijection holds, but the *emittable-code inventory* the totality test guards has drifted behind two live emitters — so "every emitted code has copy" is enforced against a hand-set, not the real emitted set. *Cat 2+5 · High.* **Invariant:** `knownEmittableCodes` is generated from (or asserted ⊇) the set of code literals reaching `LogSink.envelope`.
- **NM-60 · The registry digest appends free-text `Name`/`SiteName`/`Rationale` with unescaped delimiters.** `TransformRegistry.fs:507-534`. A rationale containing `}` or `|domain=` re-parses the field structure; two distinct registries can serialize to the same buffer (delimiter-injection collision), silently downgrading the manifest's tamper-evidence claim. The perturbation test only appends a suffix, never exercising it. *Cat 5 · Med.* **Invariant:** length-prefix/escape the variable-length fields, as the closed-DU fields already are.
- **NM-61 · `migrate.connectionUnavailable` exit is hardcoded to 7, contradicting `classify`'s connection→6 mapping; the `classify` arm is dead.** `RunFaces.fs:1527-1529,1751-1752` vs `Preflight.fs:444-445`. A dead endpoint exits **6 on `transfer`, 7 on `migrate`** — same axis, same probe, two codes — and the `migrate.connectionUnavailable → 6` arm in `classify` is unreachable (every caller overrides to 7). The A1 precedent (exit 3 should have been 9) in a new guise. *Cat 2+5 · High.* **Invariant:** the migrate face honors `classify`, or `classify`'s dead arm is removed with a DECISIONS note that migrate-connection is deliberately 7.

### Cluster I — Magic constants that gate
*Hot-loop and decision constants with no policy/config surface. Mostly low-severity
(advisory output), inventoried for the "constants that gate" sweep.*

- **NM-62 · `QueryHintPass` `highSelectivityThreshold = 100L` / `suggestedFillFactor = 70`; `ProfileAnomalyPass` `sigmaThreshold = 2.0m`.** `QueryHintPass.fs:30-32`, `ProfileAnomalyPass.fs:30`. Gate which indexes/columns produce advisory entries; no config axis. Advisory only → Low severity. *Cat 5.*
- **NM-63 · `cdcCaptureCount` hardcodes the capture-table name `cdc.[<schema>_<table>_CT]`.** `ReadSide.fs:1662-1681`. A custom `@capture_instance` breaks the derivation with no fallback (the throw side is NM-54). *Cat 4 · Med.*
- **NM-64 · IDENTITY seed/increment hardcoded `(1,1)`.** `ScriptDomBuild.fs:371-379`. Backlog **C1**, by design (matches OutSystems). Still-open, accepted. *Cat 5.*

### Cluster J — Documentation-totality defects (stale comments that will misdirect)
*The aspiration is T-IV: prose that can't lie because it's generated. Until then,
these load-bearing comments are wrong and will cost the next agent.*

- **NM-65 · `applyDiff`'s "captured surface" docstring is stale post-C1.** `CatalogDiff.fs:674-679` claims references/indexes/sequences are NOT captured — but C1 added exactly those. The true residual is now modality + the kind facets (NM-16); the comment names the wrong (closed) set and omits the open one, mis-scoping every downstream "modulo the captured surface" claim. *Cat 5 · High (correctness-of-belief).*
- **NM-66 · `StaticSeedsEmitter` still promises a slice-E "suppress the IDENTITY PK" refinement that the WP6 handoff explicitly *overturned*.** `StaticSeedsEmitter.fs:305-307` vs HANDOFF (WP6 step 1: "the PK is KEPT … the slice-E note is overturned"). A future agent reading the comment may re-attempt the cancelled change and break the MERGE `ON`. *Cat 5 · High.*
- **NM-67 · FK-naming length-cap comment says "deferred to slice 6"; the same function already applies the cap.** `SsdtDdlEmitter.fs:232-236` vs `:266` (`IdentifierBudget.fit` shipped at slice 3b). (Adjacent open item: `fkDef` ignores `Reference.Name` when present — WP7 remainder.) *Cat 5 · Med.*
- **NM-68 · `JsonEmitter` metadata's "every IR field maps 1:1" — provably false (see NM-22).** *Cat 5 · High doc-defect.*
- **NM-69 · `THE_CONFIG_CONTROL_PLANE.md §7` asserts `residualActions = ∅` (expressible = reachable); Cluster A is the counter-evidence.** The A44 law may hold for *flows*, but Cluster A's config keys are expressible-but-inert — the inverse defect the doc declares closed. *Cat 5 · Med.*

### Cluster K — Owed-by-plan, trigger already fired
*Not accidental — scheduled future slices. Included because each carries a trigger
the reconciliation plan itself marks **fired**, making them owed, not speculative.*

- **NM-70 · WP5 `EmissionPolicy.IdentityAnnotations: emit|omit` gate — named follow-on, entirely unwired.** `Policy.fs:99-114` (no field). The `Projection.*` rename shipped; the gate and its "named downgrade" diagnostic did not; annotations emit unconditionally. *High open.*
- **NM-71 · WP9 `projection compare` verb — trigger fired (corporate run, event-ledger #1), unimplemented.** No `DiffSource`, no `compare` in the verb table. *High open.*
- **NM-72 · WP8 `Order_Num` / Service-Studio column ordering — unimplemented; trigger named "crucial business case."** No `Order : int option` on `Attribute`; emission order is SsKey-derived, diverging from the operator's stated requirement. *High open.*
- **NM-73 · WP6.6 EXCEPT validate-before-apply (`emission.dataVerification`) — the operator-requested conservative fallback until J5, unbuilt.** The safety override C2 promised is currently unavailable. *High open.*
- **NM-74 · Per-lane `Data/*.sql` emit gated on `≥ 2 lanes`; the plan said unconditional, so the single-lane golden never pins per-lane bytes.** `Pipeline.fs:613`. The four-artifact integration pin the plan demanded is not satisfiable on the golden path. *Cat 1 · Med.*

---

## 4 — The distilled invariants (the operator's core ask)

Phrased as nameable, testable rules — the "invariant cases not yet defined but
very close." Ranked by leverage:

1. **I-GATE-NULLITY.** No `model`-shaped *presence/absence* check may gate
   emission, provenance, or scope behaviour unless that field is the *semantic*
   condition. `model.path` is fixed (`2157f47`); NM-08/09 (module filter),
   NM-44 (passTags non-gating dual), NM-57 (absent grant ⇒ max) are the
   surviving siblings. *Make the filter live at the read seam (NM-09).*
2. **I-INERT-NAMED.** Every config key that parses must either reach behaviour or
   emit a named `<axis>.notYetConsumed` skip (the `moduleFilter.flagsInert`
   standard). Closes all of Cluster A.
3. **I-CTOR-TOTAL.** Every decode/rebuild of an aggregate routes through its
   smart constructor; `Catalog.create` re-proves `isConstraintStateConsistent`
   and `(Type, SqlStorage)` agreement. Closes Cluster B.
4. **I-DIFF-TOTAL.** `CatalogDiff.norm d = 0 ⟺ A and B are byte-identical on the
   *whole* `Kind`* — every facet is either a diff channel or a named
   `ToleratedDivergence` with a trigger and a fixture. Closes NM-16/17 and the
   stale docstring NM-65. **This is the single highest-leverage invariant** —
   it restores the CDC-as-norm isometry the change algebra rests on.
5. **I-PROVENANCE-LIVE.** If a `ChangeManifest`/`Episode` field is durable and
   rendered, it is populated by a production caller and survives the store
   round-trip — or it is deleted. Closes NM-32/33/34/35.
6. **I-REGISTERED-EXECUTED-BOTH-WAYS.** `registered ⇔ executed` is projected
   from `chainSteps` (not a hand-list) and tested in both directions. Closes
   NM-42/43; surfaces NM-36/39.
7. **I-NO-SILENT-DROP-EVERY-RUN.** Every captured-error swallow on the data path
   becomes a named report entry; every per-run drop verdict is replayed on a
   no-op re-run. Closes NM-51/52/53; the integrity-gate leak NM-54.
8. **I-CODE-COPY-GENERATED.** `knownEmittableCodes` is the *generated* set of
   codes reaching `LogSink.envelope`, not a hand-list. Closes NM-59; surfaces
   NM-47.

---

## 5 — Backlog re-verification delta (vs `CONFIRMED_BACKLOG_2026_06_09.md`)

**CLOSED-SINCE (backlog still lists OPEN/PARTIAL — update it):**
`A2` resumable CLI · `A3` scoped-delete arm · `A5` rename-aware migrate-with-data
· `A6` top-level `diff` verb · `B1` `ArtifactByKind.perKind` (8 sites) · `B3`
ReadSide `drainRows` (the "17 loops" count is stale) · `B5` `ChannelDiff<'change>`
· `D2` ladder one-lever · `D3` setup probe breadth · `D4` `--from empty` · `D8`
synthetic `--seed`/`--scale`. **Substantially closed:** `B2` (5/8 passes on
`touchedEpilogue`; the 3 remaining are correctly excluded) · `B8`
(`CatalogResolution` now holds all four lookups).

**CLOSED-but-shallower-than-recorded (the type closed, the wiring didn't):**
- `ChangeManifest ToleranceResidual + AppliedTransforms` — marked CLOSED; **effectively OPEN** (NM-32/33: no caller, not persisted).
- `B4` "exception-leak premise stale" — **the verdict is itself wrong**; it held only for `read`. Six sibling raw-`Task` readers leak, including the CDC integrity gate (NM-54). Retry gap confirmed open.
- `D5` `inspect <runId>` — store-rendering landed (card R1d), but raw `printfn`, outside the Intent surface, no Explore TUI; `Intent.Inspect` still absent.
- `J4` capability survey G0 — wired, but undermined by the grant-probe silent downgrade (NM-55).
- `A1`/`A41` — wired/live, but A1's single-sourcing is defeated on the migrate exit path (NM-61) and A41's completeness test guards a stale list (NM-42).

**STILL-OPEN, confirmed:** `B6` (`mustOk` private, 74 files) · `B9` IRBuilders tail
· `C1`–`C4` (C4 partially mitigated via `withConstraintState`, still bypassable —
NM-12) · `D6`/`D7` · `E3` topo O(\|scc\|²) · `J5` (ops-gated) · the `IndexOptionsUnreflected`
tolerance (trigger not fired) · the F-cluster perf items.

**Fired triggers the backlog/docs predate (newly owed):** the WP5/WP6.6/WP8/WP9
reconciliation slices (NM-70–73) — `AUDIT_2026_06_10`'s "wiring shelf EMPTY" and
`THE_CONFIG_CONTROL_PLANE §7`'s `residualActions = ∅` are both stale.

---

## 6 — Appendix

**Fleet.** 12 agents, non-overlapping slices: Core IR/Catalog · Policy/Strategies/Passes
· Transfer/Migration/Remap · Emitters/Targets (+Json/Distributions sub-agent) ·
Pipeline config/wiring · Adapters/ReadSide/Profiler · Preflight/Lifecycle/Episode
· CLI/Voice/Diagnostics · Change-algebra/Tolerance/CDC · Registry/combinators ·
Docs-vs-code drift. Each returned a findings list + a backlog re-verification for
its slice; raw per-agent reports are archived in the session transcript.

**Severity tally.** High: NM-01,02,03,06,07,08,16,18,25(Med-High),32,33,34,36,37,38,42,51(Med),54,55,56,59,61,65,66,70,71,72,73 · Med: the bulk · Low: NM-30,31,41(neverResolve),48,49,52,62.

**Caveats.** `file:line` anchors were read at HEAD `80f5951`; the tree moves —
re-confirm before acting. This audit is aggressive by request: Med/Low findings
include by-design-adjacent items, each labelled. Three withdrawn on inspection
(SequenceFacet tuple-fold; `ModalityMark` exhaustive-but-lossy; an FK round-trip
that proved consistent) are noted in the agent reports, not here. Nothing was
edited; this is reconnaissance.

*Hold the spine — and name the gate.*
