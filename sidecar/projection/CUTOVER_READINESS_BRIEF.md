# Cutover Readiness Brief

The operator-facing synthesis: **is V2 ready to flip to V2-driver mode?**

Synthesized from the 185-row V1 Parity Matrix + 22 dated DECISIONS entries + the V1 Architecture Compendium + the V2 Patterns Compendium. Audience: principal-PO + cutover ops + agents preparing for T-30 / T-15 ladder gates.

This brief is the decision document for **R6 split-brain governance flip per (environment × artifact-type) pair**.

---

## 1. The cutover ladder

Per `VISION.md` T-30 / T-15 fallback ladder gates + `DECISIONS 2026-05-22`:

| Gate | Condition | Mode |
|---|---|---|
| **T-30 green** | Chapter 3 closed (canary on 300-table Catalog green) + Chapter 4.1 (data triumvirate) shipping + Chapter 4.2 (User FK reflow) shipping + ≥1 full UAT dry-run | **V2-driver** (V2 owns production write path; V1 warm fallback) |
| **T-30 yellow** | Some conditions met; partial confidence | **V2-augmented** (V1 drives, V2 verifies; canary diff blocks PRs) |
| **T-15 unstable** | Canary CI flake >10%; tolerance churn | **V1-only retreat** (V2 emit-but-don't-ship; V1 owns everything) |

**Hard rule:** V1 stays warm through cutover+30 regardless of mode. V1 sunset deferred to chapter 5+ when all four environments have run V2 emissions for one full schema-evolution cycle.

Per `DECISIONS 2026-05-22 — R6: Split-brain governance rule`, the V2-driver flip happens **per (environment × artifact-type) pair**, gated on **N=10 consecutive green canary runs + operator sign-off**. The four-environment cutover stays per-pair, never global.

---

## 2. Per-axis confidence map

Per `V2_DRIVER.md` per-axis correctness stakes. For each axis: where V2 stands; what's load-bearing for flip; what's deferred; matrix-row evidence.

### Axis 1 — SCHEMA (DDL fidelity)

**The question:** Does V2 produce DDL that, when deployed, yields the same physical schema as V1's deployment of the same source?

**Status: 🟢 V2-DRIVER-READY (per-pair flip eligible).** The canary's PhysicalSchema round-trip diff is the load-bearing gate; chapter 4 work shipped substantial parity.

**Shipped (matrix evidence):**
- ScriptDom typed-AST emission canonical (rows 120, 121; `DECISIONS 2026-05-18 (slice 5.3.α.smo)`)
- CREATE TABLE: 95% parity with V1 (row 121 + 182; defer candidates named — column defaults, CHECK constraints, computed columns, single-column PK inline optimization)
- CREATE INDEX: 70% parity (row 122 + 183; defer candidates — IgnoreDupKey, DataCompression, FileGroup/PartitionScheme; paired with rows 55+56)
- Foreign Key emission: full DELETE action + NOCHECK routing (row 123); UPDATE action + per-constraint trusted state deferred (rows 58, 59)
- Extended properties: full multi-level emission (row 124; chapter 4.1.A slice 8 shipped)
- Identifier formatting + constraint naming: deterministic-at-source (rows 126, 127)
- GO-batch formatting via BatchSplitter (row 128; per A35)
- Per-table file writing: identical path convention `Modules/<Module>/<Schema>.<Table>.sql` (row 130; header comment tolerance)
- Type mapping: typed `PrimitiveType` DU + `SqlTypeCorrespondence` round-trip property tested (row 125)
- Index axes shipped per chapters 4.5/4.7/4.8/4.9: Filter + IncludedColumns + IsPlatformAuto + IndexColumnDirection + IndexOptions + Diagnostics-bearing build
- Manifest: 6-field shape with V2-extension fields (EmitterVersion + RegistryDigest) per chapter 4.4 close

**Gated for flip (NOT-MAPPED with concrete cash-out):**
- Trigger DDL emission (row 129) — IR shipped; emission deferred to chapter 4.2 / 5+ coordinated with User FK reflow
- Index partition + data-compression + filegroup (rows 55, 56, 183) — V2 IR fields exist; emit-layer deferred to slice ζ
- Column defaults + CHECK constraints + computed columns in CREATE TABLE (row 182 defer candidates)

**Sunset:**
- SMO scripter library (row 120) — V2 uses ScriptDom canonical
- DMM lens machinery (row 40) — V2 canary subsumes
- V1's `osm_model.json` emission (rows 13, 21, 22, 24-28, 39) — V2 emits SSDT directly; legacy JSON consumption preserved indefinitely

**Acceptance criterion for flip:** N=10 consecutive green canary runs on operator-reality fixture (300 tables × 50k rows, variegated FK density); zero PhysicalSchema diff modulo named Tolerance variants.

**Status indicator:** 🟢 **R6 flip-eligible per pair.** Tolerance.IgnoreHeaderComments is the only outstanding default tolerance; documented and operator-acknowledged.

### Axis 2 — DATA (static seeds + dynamic data + idempotent redeployment)

**The question:** Does V2's emission of static seeds + dynamic inserts produce data scripts whose execution is idempotent (CDC-silent on idempotent redeploy)?

**Status: 🟡 V2-AUGMENTED (gating on chapter 4.1.B closure + global Phase1/Phase2 interleaving).**

**Shipped:**
- Static seed MERGE construction via typed ScriptDom AST (row 163; chapter 4.1.B slice α)
- Phase 1 (INSERT with deferred FKs) + Phase 2 (UPDATE to populate) cycle-breaking per kind (row 160; chapter 4.1.B slice δ)
- TopologicalOrderPass v4 supplies cycle membership (row 158; chapter 3.7 + chapter 4.1.B slice δ self-loop detection)
- Determinism by construction — every emitter-consumable row source pre-sorted (row 161)
- `StaticSeedsEmitter` + `BootstrapEmitter` + `MigrationDependenciesEmitter` sibling Π targets (row 157)
- SQL literal handling via typed `SqlLiteral.fs` IR + ScriptDomBuild.bracketed (row 164; chapter 4.1.B slice κ pillar 1 lift)

**Gated for flip:**
- **Global Phase1/Phase2 interleaving** (row 160 open item per slice η) — cross-emitter global phase ordering (Phase-1-ALL across StaticSeeds + Migration + Bootstrap, then Phase-2-ALL) NOT YET REIFIED; per-kind rendering currently. **Trigger:** chapter 4.2+ migration-dependency at scale.
- **Full FK preflight (orphan rows + cross-module audit)** (row 162) — V2's TopologicalOrderPass.MissingEdges is partial; full orphan + cross-module check deferred to chapter 4.2 slices γ+δ
- **CDC-silence-on-idempotent-redeploy property test** (chapter 4.1.B; highest-leverage single deliverable per V2_DRIVER) — must show green on operator-reality canary

**Sunset:**
- V1's raw-INSERT `DynamicEntityInsertGenerator` (~790 LOC) — V2 emits typed MERGE
- V1's string-composed SQL escaping (`SqlIdentifierFormatter` + `SqlLiteralFormatter`) — V2 typed via ScriptDom + SqlLiteral

**Acceptance criterion for flip:** chapter 4.1.B CDC-silence property test green + global phase ordering reified + 300-table canary with realistic static data shows zero CDC events on idempotent redeploy.

**Status indicator:** 🟡 **gating on chapter 4.1.B slice η + chapter 4.2 FK preflight.**

### Axis 3 — IDENTITY (User-FK reflow + ID preservation)

**The question:** When platform-user identities differ between source and target environments (typical for dev/uat/prod cutover), does V2 produce the correct remapping artifacts?

**Status: 🟡 V2-AUGMENTED (slice δ ByEmail shipped; other strategies deferred).**

**Shipped:**
- Identity axis primitives: `SsKey` 4-variant DU (row 45) + `Name` VO (row 180) + `UserId` newtype + `SourceUserId` / `TargetUserId` orientation markers (row 175)
- `UserMatching` policy DU (chapter 4.2 slice δ ByEmail variant; row 174)
- `UserFkReflowPass` consumer-side IR (chapter 4.2; row 113 + 174)
- `buildEmailIndex` mirroring V1's `TryExactMatch` lookup (row 174)
- Typed `UserRemapContext` IR + `RemapDiagnostic` DU (row 174)

**Gated for flip:**
- **Remaining matching strategies** (BySsKey / Regex / FallbackToSystemUser) — deferred to chapter 4.2 slice ε per pre-scope
- **UAT verification surface** (row 177) — V1 has 3 verifiers + report; V2 verification deferred post-cutover; canary's round-trip diff + tolerance table cover dual-track mode
- **`osm uat-users` CLI verb** (row 113) — cash-out ~1500 LOC; trigger: cutover enters UAT phase
- **Per-FK orphan-sample diagnostics** (row 89) — V2 carries orphan COUNT but not row identifiers; cash-out: add `OrphanSamples` field to `Profile.ForeignKeys` when consumer demands

**Acceptance criterion for flip:** chapter 4.2 slice ε ships remaining matching strategies + UAT dry-run on real inventory CSVs surfaces zero unmatched orphans (or operator-acknowledged manual overrides).

**Status indicator:** 🟡 **gating on chapter 4.2 slice ε + UAT dry-run.**

### Axis 4 — DIAGNOSTICS (operator-visible decision evidence + remediation)

**The question:** When V2's tightening passes contest a decision, do operators see equivalent guidance to V1's per-decision diagnostic + remediation surface?

**Status: 🟡 V2-AUGMENTED (per-pass DiagnosticEntry contract shipped; SummaryFormatter + RemediationEmitter deferred).**

**Shipped:**
- Per-pass `DiagnosticEntry` contract (row 77; `DECISIONS 2026-05-18 (slice 5.4.γ.opportunities) — Per-pass DiagnosticEntry contract`)
- Ternary outcome space across NullabilityOutcome / UniqueIndexOutcome / ForeignKeyOutcome (rows 65 + companion entries; `DECISIONS 2026-05-18 (slice 5.4.β.nullability)`)
- FK exhaustive per-keep-reason emission (row 73; corrects V1's silent-skip bug; `DECISIONS 2026-05-18 (slice 5.4.γ.evaluators)`)
- Typed evidence per Outcome variant (row 69; replaces V1 string rationales)
- Lineage + Diagnostics dual-writer + writer-fidelity codification (chapter 3.1)
- Manifest Coverage / PredicateCoverage / Unsupported sections (chapter 4.4 close)

**Gated for flip:**
- **`SummaryFormatter` consumer** (row 81) — V1's `PolicyDecisionSummaryFormatter` (~439 LOC operator-facing bucket-wise summary tables) has no V2 equivalent; cash-out: SummaryFormatter consumer taking `Diagnostics<DecisionSet> × NullabilityMode` and producing string list mirroring V1's 6-bucket prose. **Trigger:** V2 CLI standardizes on summary output format before cutover.
- **`RemediationEmitter` sibling Π** (row 83; `V2_DRIVER §154` chapter 5+ deferred) — V1's `RemediationQueryBuilder` emits 3-option UPDATE/DELETE/SELECT remediation SQL; V2 has no equivalent. **Risk:** if chapter 5 doesn't ship before cutover, operator UX for mandatory-null-conflict remediation degrades — operators see DiagnosticEntry.Message but must hand-write the UPDATE query. **Mitigation:** DiagnosticEntry.Message + Metadata carry full context (null count, budget, intervention ID); operators can infer fix semantics.
- **`OpportunitiesReport` rollup surface** (row 79) — V1's per-axis summary metrics; V2 lacks; cash-out: thin projection module at emission boundary
- **`RiskClassification` emitter-side module** (row 76) — V1's `ChangeRiskClassifier` 4th axis; V2's outcomes carry no risk-level; cash-out: `riskOf` pure functions per Outcome at emission boundary
- **Operator-debugging telemetry surface during extraction** (row 30) — V1's `SqlMetadataLog` + JSON dump; V2 has no equivalent; trigger: production CLI surface OR cutover-windowed failure demands partial-state context

**Acceptance criterion for flip:** SummaryFormatter ships OR operator confirms DiagnosticEntry stream is sufficient for cutover-window decision review. RemediationEmitter ships OR fallback remediation doc substitutes.

**Status indicator:** 🟡 **gating on SummaryFormatter + RemediationEmitter (chapter 5+).**

### Axis 5 — OPERATOR-AFFORDANCE (CLI surface)

**The question:** Can operators run V2 in the production cutover workflow?

**Status: 🟡 V2-AUGMENTED minimal surface (4 verbs sufficient for cutover-critical work; other verbs deferred).**

**Shipped (V2 CLI has 4 verbs):**
- `projection emit --config <path>` — emit from unified config
- `projection emit [--skeleton-only] <input> <output>` — emit from V1 JSON input (with skeleton-only filter per chapter A.4.7' slice ζ)
- `projection deploy <input>` — deploy emitted artifacts
- `projection canary <ddl>` — canary verification

**Per `DECISIONS 2026-05-18 (slice 5.7.α.cli) — V2 CLI deliberately minimal: production-deferred posture`,** these 4 verbs cover the cutover-critical surface: emit + deploy + canary. Everything else lands when operator demand surfaces.

**Gated for flip (per matrix rows 107-119; ~3780 LOC cash-out across 10 deferred verbs):**

| Verb | Trigger | Est. LOC | Priority |
|---|---|---|---|
| `osm extract` | Chapter 5.1.β production wiring lands | 50 | High (cutover-relevant) |
| `osm profile` | Row 85 LiveProfiler lands | 30 | Med |
| `osm analyze` | Operator pre-emission iteration workflow | 300 | Med (cutover dry-run) |
| `osm policy explain` | CLI-based decision drill-down | 300 | Low |
| `osm uat-users` | Chapter 4.2 slice ε + UAT phase | 1500 | High (axis 3 dependency) |
| `osm verify-data` | Chapter 4.3+ post-deploy verification | 200 | High (post-deploy quality gate) |
| `--open-report` flag | Operator demand for integrated reports | 150 | Low |
| `compare` verb (DMM concept harvest) | Operator ad-hoc schema-diff demand | 500 | Low |
| Option-binder infrastructure | CLI grows beyond 4 verbs with axes | 500 | Low |
| Progress TUI (Spectre.Console adapter) | Chapter 5.1 + operator feedback | 200 | Med |

**Sunset:**
- `dmm-compare` verb (row 109) — V1 DMM lens machinery sunsets; future `compare` verb reserves the concept
- `full-export` (V1 orchestrates everything) — V2 decomposes (emit + deploy + harness-replay chain) per A36

**Acceptance criterion for flip:** the 4 shipped verbs cover the operator's documented cutover workflow OR additional verbs ship per matrix-row triggers. UAT dry-run + post-deploy verification workflows have V2 CLI support.

**Status indicator:** 🟡 **sufficient for cutover-critical work; additional operator UX verbs deferred per principled minimal-CLI posture.**

### Axis 6 — PIPELINE-ORCHESTRATION (the registry + composer)

**The question:** Does V2's registry-driven composition produce the same end-to-end pipeline behavior as V1's BuildSsdt step-pipeline?

**Status: 🟢 V2-DRIVER-READY.** Registry-driven composition shipped at chapter A.4.7'; per-pass behavior matches V1's per-step (modulo the typed-decision-set vs per-column-aggregator divergence per row 71).

**Shipped:**
- Registry-driven composition via `RegisteredTransforms.allChainSteps` + `Compose.project` (row 131; `DECISIONS 2026-05-18 (slice 5.6.α.orchestration) — Registry-driven composition over imperative step-chaining`)
- 12 registered chain steps (6 Catalog-rewriting + 6 decision-set-producing) per chapter A.4.7'
- Skeleton-purity property test + overlay-exercise property test (chapter A.4.7' slice ε)
- `osm emit --skeleton-only` CLI verb (chapter A.4.7' slice ζ)
- Per-pass DiagnosticEntry contract (`DECISIONS 2026-05-18 (slice 5.4.γ.opportunities)`)
- Manifest `applied-transforms` field surface
- `RegistryDigest` for deterministic emission shape signature (chapter A.4.7' slice ζ; row 95 + 139)
- Bootstrap inlined into adapter (`CatalogReader.parse`; row 144)
- Schema+data application via `Deploy.executeStream` (row 142; chapter 3.1.M2 slice α)

**Gated for flip:**
- **PostDeployTemplateEmitter** (row 140; chapter 4.1 slice 9 deferred) — V1 emits `PostDeployment-Bootstrap.sql` with guard logic; V2 deferred
- **`.sqlproj` realizer** (row 141) — V1 emits MSBuild file for Visual Studio / Azure DevOps integration; V2 produces `ArtifactByKind<SsdtFile>` map; realizer not yet shipped
- **EvidenceCacheCoordinator** (row 135; 9-variant invalidation enum) — V2 has no caching layer; deferred until operator-reality canary surfaces evidence-load as bottleneck
- **SQL validation step** (row 136) — V1 uses SMO + DacFx; V2 deferred; cash-out: `Validator` sibling Π consuming SSDT stream + producing ValidationReport at realization layer

**Acceptance criterion for flip:** end-to-end canary pipeline runs green; all registered passes fire in deterministic order; skeleton + overlay property tests hold.

**Status indicator:** 🟢 **R6 flip-eligible; chapter A.4.7' arc shipped the load-bearing pieces.**

---

## 3. Composite readiness assessment

| Axis | Status | Blocking for T-30 green? | Blocking for V2-driver flip? |
|---|---|---|---|
| SCHEMA | 🟢 V2-DRIVER-READY | No | No |
| DATA | 🟡 V2-AUGMENTED | Chapter 4.1.B closure + global phase ordering | Yes (until chapter 4.1.B CDC-silence + phase interleaving land) |
| IDENTITY | 🟡 V2-AUGMENTED | Chapter 4.2 slice ε remaining matching strategies | Yes (until chapter 4.2 + UAT dry-run land) |
| DIAGNOSTICS | 🟡 V2-AUGMENTED | SummaryFormatter for cutover-window decision review | Soft (operator-tolerant if DiagnosticEntry stream + Message field suffice) |
| OPERATOR-AFFORDANCE | 🟡 V2-AUGMENTED | `osm uat-users` + `osm verify-data` for cutover workflow | Yes (per (verb × workflow) gating) |
| PIPELINE-ORCHESTRATION | 🟢 V2-DRIVER-READY | No | No |

**Composite verdict at 2026-05-18 (chapter 5 audit-wave close):**

V2 is **V2-augmented-ready** today (V1 drives; V2 verifies via canary). The four 🟡 axes have concrete cash-out paths with named triggers; none are unbounded research.

For **V2-driver-mode flip** (per (environment × artifact-type) pair):
- ✅ SCHEMA + PIPELINE-ORCHESTRATION are flip-eligible today
- ⏳ DATA flips when chapter 4.1.B CDC-silence property test + global phase ordering land
- ⏳ IDENTITY flips when chapter 4.2 slice ε remaining matching strategies + UAT dry-run land
- ⏳ DIAGNOSTICS + OPERATOR-AFFORDANCE flip when operator-facing surfaces ship per their triggers

The R6 per-pair flip discipline means V2-driver can flip on SCHEMA + PIPELINE-ORCHESTRATION axes for low-risk artifact-types (e.g., one-environment lab) before all axes are ready globally.

---

## 4. The path to T-30 green

Per `VISION.md` T-30 conditions: chapter 3 closed (canary on 300-table Catalog) + chapter 4.1 shipping + chapter 4.2 shipping + ≥1 full UAT dry-run.

**Chapter 3 status:** ✅ closed (chapter 3.1 close shipped canary + chapter 3.2 close shipped IR refinements + chapter 3.5/3.6/3.7 close shipped emitter realization + chapter A.4.7' close shipped registry).

**Chapter 4.1 status:** 🟡 partially shipped (chapter 4.1.A close arc shipped substantial static-seed emission + ternary outcome space + per-pass DiagnosticEntry contract; chapter 4.1.B CDC-silence property test in flight per V2_DRIVER's highest-leverage single deliverable).

**Chapter 4.2 status:** 🟡 consumer-side reflow IR shipped (slice δ ByEmail); remaining matching strategies in flight.

**UAT dry-run status:** ⏳ deferred until chapter 4.2 closes.

**The path:**

1. Ship chapter 4.1.B CDC-silence property test → unblock DATA axis flip
2. Ship chapter 4.2 slice ε remaining matching strategies → unblock IDENTITY axis flip
3. Run 1+ full UAT dry-run → confirm IDENTITY axis on real fixtures
4. Optional: ship SummaryFormatter + `osm uat-users` + `osm verify-data` to round out operator workflow
5. **T-30 green:** R6 per-pair flip eligible on all six axes

---

## 5. Per-environment progression (R6 discipline)

Per `DECISIONS 2026-05-22 — R6`, V2-driver flip happens per (environment × artifact-type) pair. Suggested progression based on risk:

| Environment | Artifact type | Flip eligibility | Notes |
|---|---|---|---|
| **Lab/dev** | Schema-only | ✅ Today | Lowest risk; canary already green per chapter 3.1 |
| **Lab/dev** | Schema + static seeds | ⏳ Post-4.1.B | CDC-silence required |
| **Lab/dev** | Schema + seeds + UAT users | ⏳ Post-4.2 ε | Identity matching required |
| **UAT** | Schema-only | ⏳ Post-N=10 lab flips | Operator sign-off after lab proof |
| **UAT** | Schema + static seeds | ⏳ Post-4.1.B + lab proof | |
| **UAT** | Schema + seeds + UAT users | ⏳ Post-4.2 ε + dry-run | |
| **Staging** | All axes | ⏳ Post-UAT proof + N=10 UAT flips | |
| **Production** | All axes | ⏳ Post-staging proof + N=10 staging flips | The cutover |

**Hard rule:** V1 stays warm through cutover+30 regardless. The flip is reversible per pair until cutover+30.

---

## 6. Open risks at chapter 5 close

### Risk 1: chapter 4.1.B doesn't ship before T-30

**Mitigation:** V2-augmented mode (V1 drives, V2 verifies via canary) is the fallback. The matrix Phase1/Phase2 logic IS shipped per kind; only global cross-emitter ordering is the open item.

**Operator impact:** static seeds emit correctly; idempotent redeploy without CDC noise depends on chapter 4.1.B closure. Acceptable for V2-augmented; not for V2-driver.

### Risk 2: RemediationEmitter doesn't ship before cutover

**Mitigation:** V2's DiagnosticEntry.Message + Metadata carry full context (null count, budget, intervention ID, etc.); operators can infer remediation SQL from the prose + metadata. Fallback remediation doc substitutes V1's `RemediationQueryBuilder` template.

**Operator impact:** mandatory-null-conflict remediation requires manual SQL composition; documented in operator runbook.

### Risk 3: SummaryFormatter doesn't ship before cutover

**Mitigation:** V2's DiagnosticEntry stream is operator-readable (JSON output via `osm emit --config <path>`); operators can grep/filter by Source / Severity / Code to reproduce V1's 6-bucket classification.

**Operator impact:** decision-review workflow uses raw DiagnosticEntry stream instead of formatted bucket prose. Acceptable for cutover; suboptimal UX.

### Risk 4: chapter 4.2 slice ε remaining matching strategies delayed

**Mitigation:** slice δ ByEmail covers the dominant matching case; ManualOverride + FallbackToSystemUser can be substituted via operator CSV (cash-out per matrix row 174).

**Operator impact:** UAT cutover requires operator review of unmatched users; manual remap CSV is the substitute.

### Risk 5: Transient SqlException tolerance — ✅ CLOSED 2026-05-18

**Status: shipped** by slice `5.13.production-wiring-classification`
(matrix rows 32 + 34 + 35 bundled) + slice `5.13.progress-callback`
(matrix row 36).

V2's `Projection.Adapters.OssysSql/Retry.fs` now carries a Polly v8
`ResiliencePipeline` wrapping `command.ExecuteReaderAsync` at the
command-execute boundary. 3 retries with exponential backoff + jitter;
predicate matches `SqlException.Number ∈ {-2, -1, 4060, 18452, 40197,
40501, 40613}` (timeout / network drop / cannot-open-db / auth
transient / Azure SQL service-busy / service-error / db-unavailable).
The closed-DU `MetadataExtractionError` (4 variants) lifts retry
exhaustion to `TransientSqlError` with structured `sqlNumber`
metadata so cutover-window operators can distinguish transient
exhaustion from non-transient SQL errors.

R6 dual-track canary now tolerates cloud-OSSYS transients structurally;
the risk is no longer outstanding. The Polly pipeline is
predicate-parameterized so future seams (connection-open retry; cloud
SQL DBs with new transient classes) can extend without changing the
runner.

See `DECISIONS 2026-05-18 (slice 5.13.production-wiring-classification)`.

---

## 7. Quick-reference: post-cutover (cutover+30 sunset window)

V1 stays warm through cutover+30 regardless of flip mode. During this window:

- **V1 emission path remains operational** — operators can fall back to V1 for any artifact-type that surfaces a V2 issue
- **V1's `osm_model.json` continues to be produced** — V2's `SnapshotJson` input variant continues to consume it for any chapter that still needs V1-extracted catalogs
- **Per-pair flip is reversible** until cutover+30

After cutover+30 (assuming green):
- V1's emission path retires per `DECISIONS 2026-05-17 (slice 5.1.α) — V1's JSON-aggregation rowsets sunset with V1's osm_model.json emission path`
- V1's DMM lens machinery retires (covered by V2 canary; future `compare` verb reserves the concept)
- V1's LoadHarness DMV instrumentation retires (V2 carries Bench surface; DMV becomes post-cutover operator-facing tool per matrix row 178-179)

V1 sunset is deferred to chapter 5+ when all four environments have run V2 emissions for one full schema-evolution cycle.

---

## 8. The bottom line

**Are we ready for V2-driver?**

| Question | Answer |
|---|---|
| Today (chapter 5 close)? | V2-augmented-ready globally. Per-pair V2-driver flip eligible on SCHEMA + PIPELINE-ORCHESTRATION axes for lab/dev environments. |
| At T-30 green? | If chapter 4.1.B (CDC-silence) + chapter 4.2 (slice ε) + UAT dry-run land: V2-driver-ready per-pair across all six axes. |
| At T-15 unstable? | V1-only retreat. V2 emits-but-doesn't-ship; V1 owns production. Reversible per pair. |

**What's blocking?**

Three concrete deliverables gate the T-30 green flip:
1. **Chapter 4.1.B CDC-silence property test** + global Phase1/Phase2 cross-emitter ordering (DATA axis)
2. **Chapter 4.2 slice ε remaining matching strategies** (IDENTITY axis)
3. **≥1 full UAT dry-run** on real inventory CSVs (IDENTITY axis confirmation)

Optional (improves UX but doesn't block):
4. SummaryFormatter (DIAGNOSTICS axis UX)
5. RemediationEmitter (DIAGNOSTICS axis remediation flow)
6. `osm uat-users` + `osm verify-data` CLI verbs (OPERATOR-AFFORDANCE axis)

Each blocking deliverable has a named slice owner + cash-out shape in the V1 Parity Matrix; chapter-close ritual + active-deferrals scan catch trigger fires.

**Operator decision-points:**

- **Now → T-30:** Continue chapter 4.1.B + chapter 4.2 work. Run UAT dry-run when those land.
- **T-30 green:** Flip lab/dev to V2-driver on SCHEMA + PIPELINE; verify N=10 green; expand per-pair coverage.
- **T-15 stable:** Sequence cutover per-environment per-pair; V1 warm fallback through cutover+30.
- **T-15 unstable:** V1-only retreat; preserve V2 emit-but-don't-ship discipline; revisit at next ladder cycle.

---

## 9. Cross-references

- `VISION.md` — strategic frame: cutover as forcing function + sibling chorus + acceptance criteria + fallback ladder
- `V2_DRIVER.md` — destination KPI + per-axis correctness stakes + chapter ownership map
- `V1_PARITY_MATRIX.md` — 185 rows of per-capability evidence + cash-out plans
- `V1_ARCHITECTURE_COMPENDIUM.md` — V1's internal architecture by audit cluster
- `V2_PATTERNS_COMPENDIUM.md` — V2's architectural patterns with worked examples
- `DECISIONS.md` — 22 dated entries codifying structural commitments
- `BACKLOG.md` — operational ledger with chapter-grain status
- `HANDOFF.md` — chapter-close letters

— Brief opened 2026-05-18 at chapter 5 audit-wave close.
