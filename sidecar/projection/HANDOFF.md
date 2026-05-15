# Handoff letter — A.4.7 specced + L3-CC-Transform-Totality axiom landed (chapter A.0' still open, slice β next)

To the next-chapter agent. Read this before anything else in the V2 sidecar. It is short on purpose.

The chapter-1 and chapter-2 handoff letters are preserved at `HANDOFF_CHAPTER_1.md` and `HANDOFF_CHAPTER_2.md` adjacent to this file. Read them after this one if you want the prior architects' framings.

## 2026-05-15 (late) — Transform registry re-opened as L3-CC-Transform-Totality (A.4.7 specced; chapter A.0' continues)

**Branch / baseline.** Continues on `claude/research-v2-direction-zKg9g`. Documentation-only commit; no code changes; test baseline unchanged (1146/1146).

**The principal-PO surfaced an axiomatic finding during a v1-doc review:** the skeleton/overlay separation is the structural seam V2 needs to stay clinical/laboratory-quality as it scales. *Factual/objective/skeletal* output = `Project(catalog, Policy.empty, profile)` (the deterministic baseline reachable without operator opinion). *Opinionated/override/subjective* overlay = the ordered, named, registered set of `Pass` invocations that compose the baseline into the full output. Both halves must be enumerable, recoverable, audit-traceable. A18 amended is the Π-side commitment; the transform registry is the *Pass-side* commitment. The two siblings together carry the decomposition as type-witnessed contract, not discipline.

**What shipped (this session, documentation-only):**

- **`PRODUCT_AXIOMS.md`** — new **L3-CC-Transform-Totality** axiom in Group CC. Tier 1 (cutover blocker); Bucket D pending A.4.7. Co-equal load-bearing with CDC silence per the new V2_DRIVER per-axis stakes row.
- **`AXIOMS.md`** — new **A41 candidate (Transform registry totality)** placeholder in Amendments scheduled; body fills at A.4.7 close.
- **`DECISIONS.md`** — new entry `2026-05-15 — Transform registry re-opened: skeleton-overlay separation as L3-CC-Transform-Totality`. Re-opens the 2026-05-13 cash-out under different consumer pressure (skeleton-overlay separation, not pipeline composition); preserves the prior reasoning while naming the different shape the registry takes under the new pressure. Active deferrals index updated to disambiguate the strategy-registry-mechanism (still deferred under its original framing) from the transform-registry (re-opened under the new framing).
- **`V2_PRODUCTION_CUTOVER.md`** — new workstream **§6.4.7 A.4.7** (Campaign B core; load-bearing for laboratory-quality scale); §12.6 delivery matrix row added; new §13.6 V1-soak debt lane addendum carrying the three v1-side cleanup vectors (EntityFilters wiring; global topo for StaticSeeds; DatabaseSnapshot dedup).
- **`V2_DRIVER.md`** — per-axis stakes table gains a "Skeleton/overlay separation" row at verification depth = Highest; new "V1-soak debt lane" section in the backlog (V1.1 / V1.2 / V1.3 v1-side PRs).
- **`CLAUDE.md`** — operating-disciplines table gets a row pointing at the DECISIONS entry; load-bearing commitments list gains L3-CC-Transform-Totality.
- **`docs/architecture/entity-pipeline-unification-v2.md`** — header banner added clarifying this is a v1-refactor doc (not v2 plan); names the three vectors promoted to V2_DRIVER's v1-soak debt lane.

**Why this is load-bearing and not just a backlog item.** Per the new V2_DRIVER framing: the chapter-4.x scope expansion grows the number of policy-driven mutations monotonically (User FK reflow; operational diagnostics; multi-environment policy/profile parameterization). Without the registry seam, each new pass is one more convention to track in code review. The four-question naming analysis (pillar 8) catches naming drift; the LINT-ALLOW substantive-rationale discipline catches string-composition drift; **the transform registry catches skeleton/overlay drift.** Three sibling disciplines, each preventing a class of failure that scales linearly with codebase growth. CDC silence is the highest-leverage *property test*; transform registry is the highest-leverage *structural-enforcement seam*. Co-equal load-bearing.

**What this re-opening does NOT do** (per the DECISIONS entry's preserved-reasoning protocol):
- It does NOT introduce a single linear `pass1 >> pass2 >> pass3` pipeline. The per-use-case driver pattern stands; the registry is **enumerative**, not **compositional**.
- It does NOT add reflection or name-keyed runtime dispatch. Compile-time `module TransformRegistry` referencing each `Pass` module by name. CLAUDE.md's "reflection is out of scope for Core" holds.
- It does NOT replace `Composition.fanOut`. Strategies fan out *within* a pass; the registry enumerates *passes themselves*. Different granularities, both preserved.
- It does NOT introduce per-pass policy axes the operator can toggle individually. `--skeleton-only` is binary (baseline vs. baseline+all-overlays). Granular toggling deferred-with-trigger.

**Sequencing.** A.4.7 depends on **A.0' close** (chapter A.0' is still in flight; slice α shipped, slice β next per the section below). The registry enumerates against the post-IR-fidelity pass set; opening A.4.7 before A.0' closes would lock in a pass set A.0' is still extending. Concurrent with A.4.5 / A.4.6 acceptable once A.0' closes.

**Continue on the in-flight A.0' chapter slice β next.** The new A.4.7 spec is the *next-chapter* target, not a redirect. The framing below (slice β: `Module.IsActive` + `Attribute.IsActive` + retire boundary filter, with DECISIONS amendment superseding session-21) is still the next slice to land. Operator-side check before slice β: alignment on retiring the inactive-records filter (carries semantic shift; DECISIONS amendment required).

**The three v1-soak debt vectors are independently shippable** by v1 maintainers; they accelerate Phase A.6 soak by removing false-positive disagreement classes (V1 over-fetching; V1's broken StaticSeeds FK order; V1's triple-fetch variability). Each is small, surgical, reversible. Not load-bearing for V2-driver KPI directly; the KPI tracks V2-axis property tests. See `V2_PRODUCTION_CUTOVER.md` §13.6 for the per-vector rationale.

## 2026-05-15 — Chapter A.0' open + slice α shipped

**Branch / baseline.** Branch: `claude/review-handoff-docs-CF2v5`. PR #538 (chapter pre-A.0') merged at `8733d0c`; PR #539 follow-up merged. Chapter A.0' opened at commit `3c75d00`. **Test baseline: 1146 / 1146 passing** (1128 prior + 11 canary tests now visible with Docker running + 7 new `DescriptionLiftTests`). Zero regressions; `TreatWarningsAsErrors=true` clean; lint clean.

**What shipped this session.** The operator picked A.0' (IR fidelity lifts) over A.7.2 (ManifestMatchesDisk). The chapter opens the 7-9-slice arc that promotes L3-S4 through L3-S10 + L3-I10 + L3-CC4 + L3-Boundary-NoSilentDrop from Bucket D → Bucket A:

- `CHAPTER_A_0_PRIME_OPEN.md` — strategic-frame axes (8 numbered), slice plan (α–ι), out-of-scope, success criteria. Use this as the chapter's reading-order item.
- **Slice α — `Kind.Description` + `Attribute.Description`** (commit `3c75d00`). Purely additive. OSSYS adapter populates from JSON `description` field (defensive read via `getOptionalString`) and from extended `KindRow.Description` / `AttributeRow.Description` (rowset DTOs extended too). `Projection.Adapters.Sql.ReadSide` sets `None` — extended-property pickup gates on chapter 4.1.A slice 8. ~170 record-literal sites across 23 test files received `Description = None` per the closed-DU record-extension empirical-test discipline (chapter 3.2 close generalization). 7 new tests in `DescriptionLiftTests.fs` cover JSON-path + rowset-path roundtrip and `None`-default cases.

**The next-most-ready slice: A.0' slice β — `Module.IsActive` + `Attribute.IsActive` (carry-through; retire boundary filter).** Dependencies satisfied (α just shipped). Scope: extend `Module` and `Attribute` with `IsActive : bool` fields; retire the session-21 inactive-records filter at `parseModule` / `parseKind` / `parseModuleRow` / `parseKindRow`; carry the flag through to the IR; downstream emitters decide. **DECISIONS amendment required** superseding session-21's silent-drop disposition. Consider adding `Kind.IsActive` too — §3.3's omission of Kind is likely an oversight (entity-level `Is_Active` exists in V1 OSSYS); without it the entity-level filter stays at the adapter and creates an asymmetry with the L3-Boundary-NoSilentDrop completion criterion. Read `CHAPTER_A_0_PRIME_OPEN.md` axis 4 + the §6.0' / §3.3 spec before deciding.

**Alternative starts** if slice β is too disruptive (the IsActive semantic shift may want operator alignment first):

- **Slice ε — `Attribute.DefaultValue : SqlLiteral option`** (additive; matches α's pattern). V1 JSON has `"default": null` already; needs a small JSON-to-SqlLiteral parser at the adapter boundary. No prior decisions to supersede. ~130 attribute literal sites need `DefaultValue = None` added (same blast radius as α; see `/tmp/fix_records.py` precedent if helpful).
- **Slice ι — IsExternal / Origin mapping audit** (pure property test; no IR change). Lift the existing `parseOrigin` discipline into a property test asserting V1 `IsExternal=true → V2 Origin ∈ {ExternalViaIntegrationStudio; ExternalDirect}`. Small slice; useful chapter-mid hygiene. Pairs with the chapter-close L3-Boundary-NoSilentDrop property test scaffolding.

**Outstanding (operator-side; same as post-A.7.1):**
- R1 — operator's "document of key evolutions" still pending. Hold UAT-users decisions until it lands.
- Q2 / Q3 / Q4 / Q7 unchanged; revisable during touching slices.

**Mechanical-edits precedent** for record-extension slices: the slice-α experience produced a reusable workflow. Step 1: extend the IR record + the adapter DTOs (`Catalog.fs` + `CatalogReader.fs`). Step 2: build, capture FS0764 worklist. Step 3: sed-pass for inline-close-brace literals (`s/(IsIdentity = (true|false))(\s*)\}/\1; Description = None\3}/g` analog); python-pass for multi-line records (brace-counter walking from the opening `{` to find the matching `}`). Step 4: property test in a new `<SliceName>LiftTests.fs` file added to `Projection.Tests.fsproj`. Step 5: build + test + commit. Slice β / ε / γ / δ inherit. The closed-DU record-extension empirical-test discipline holds across all of them (chapter 3.2 close codified this).

**Load-bearing methodology unchanged from the 2026-05-12 final handoff above.** L1↔L2↔L3 verifiability triangle is the lens for structural work; campaigns are cross-cutting tags; per-PR L3 review for PRs touching boundary code or CLI surface.

**Per-axiom delivery matrix updates** at chapter-A.0' close: cash `L3-S9` descriptions sub-axiom (advances toward full L3-S9; full lands at slice ζ ExtendedProperties). Forward-signal Tolerance retirement: `CommentMetadataUnreflected` is one step closer (the IR now carries descriptions; emitter consumption is chapter 4.1.A slice 8 territory).

## 2026-05-12 — Final handoff (post-A.7.1; PR #538 active)

**PR / branch / baseline.** Pull request: [#538](https://github.com/danielbdyer/outsystems-ddl-exporter/pull/538). Branch: `claude/audit-v1-v2-sidecar-7Ifij`. Subsequent commits to this branch update the PR. **Test baseline: 1128 / 1128 passing** (1121 + 7 new atomic-write property tests; zero regressions; canary excluded from this baseline as usual). All slices below ship clean under `TreatWarningsAsErrors=true`.

**Reading order for an agent picking this up.** Read in this order, top-down:

1. **`AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md` Part I (framing) + Part IX (campaigns)** — the methodology and the implementation plan. ~150 lines total.
2. **`V2_PRODUCTION_CUTOVER.md`** Draft 3 — the canonical plan-of-record with axiom-tagged workstreams. Skim §1 (five Draft-3 insights), §5 (composition algebra), §12 (per-axiom delivery matrix). ~994 lines.
3. **`PRODUCT_AXIOMS.md`** — the L3 axiom catalog; constitutional sibling to `AXIOMS.md`. Reference material.
4. The relevant audit-doc Part IV / Part VI section for the slice you're picking up.

Then the relevant slice-specific files. Do not start coding without reading at least #1 + the §12 row for the axioms your slice operationalizes.

**Two operator decisions locked in this session:**
- **Q14 — Campaign A sequencing:** atomic emission first. **Done at A.7.1 (commit `4e3d944`).**
- **Q15 — Axiom-naming convention:** `L3-Boundary-*` namespace in `PRODUCT_AXIOMS.md`; `AXIOMS.md` A41+ stays reserved for algebra-interior extensions. **Codified in `PRODUCT_AXIOMS.md` Group Boundary.**

**What shipped this session, in commit order:**

| Commit | Slice | Axioms promoted (D → A or B → A) |
|---|---|---|
| `2ab3a8a` → `143a885` | Cutover plan Drafts 1 → 3 (5 commits) | (planning; no code) |
| `491fbb5` | Verifiability-triangle audit doc | (audit; no code) |
| `72ff8a3` | `PRODUCT_AXIOMS.md` sibling + 6 cross-refs | (doc system; no code) |
| `93468a3` | A.0 Config + D9 guardrail | L3-X9, L3-C8 |
| `df18bbf` | A.1 `emit --config` bridge | L3-X9 (CLI) |
| `502592f` | A.4 TableRename + RenameBinding + Compose.runWithConfig | L3-I1, L3-I7, L3-C7 (**R11 dissolved**) |
| `9d578cc` | Slice 1 PhysicallyRenamed variant | (L3-I1 audit-trail typed) |
| `4e3d944` | A.7.1 atomic emission | **L3-Boundary-AtomicEmission (first formalized boundary axiom)** |

**The next-most-ready slice: A.7.2 — `L3-Boundary-ManifestMatchesDisk`.** Dependencies satisfied (A.7.1 just shipped). Scope: change `ManifestEmitter` to consume the path list returned by `Compose.write` rather than the in-memory `Outputs`; add a property test that every manifest entry exists on disk and every file on disk has a manifest entry post-write. Small surgical change; estimated under 1 day. See `V2_PRODUCTION_CUTOVER.md` §6.7 (workstream A.7.2 spec) + `AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md` Part IX Campaign A.

**Alternative starts** if A.7.2 is too small or you prefer parallel work:
- **A.4.5 — Catalog cross-field invariants batch (Campaign B).** Mechanically simple, very high leverage. Extends `Catalog.create` with 7 cross-field invariants (IsIdentity⇒¬IsNullable, IsPrimaryKey⇒IsUnique, Type/Length/Precision/Scale coherence, Length≤8000, PK ordering, single Static modality, physical-name uniqueness). One PR, ~2 weeks, promotes 8 new L3-Catalog-* axioms to Bucket A. See `V2_PRODUCTION_CUTOVER.md` §6.4.5 + audit Part VI.2.
- **A.0' — IR fidelity workstream (Campaign A.2 prerequisite).** Largest single body of work (~3-4 weeks, 5-7 slices). Promotes 8 Tier-1 unnamed axioms (L3-S4 through L3-S10 + L3-I10 + L3-CC4) when complete. Read `V2_PRODUCTION_CUTOVER.md` §3.3 (gap table) + §6.0' (workstream spec).

**Outstanding (unblocked but operator-side):**
- **R1 — Operator's "document of key evolutions"** (focuses on UAT-users; reshapes scope). Until it lands, hold UAT-users decisions; don't pre-scope speculatively. See `V2_PRODUCTION_CUTOVER.md` §13.1.
- Q2 (Argu vs hand-rolled CLI parser), Q3 (legacy positional backward-compat), Q4 (Profile JSON shape coupling), Q7 (sampling thresholds): all revisable during the slices that touch them; don't escalate.

**Load-bearing methodology** (per `DECISIONS 2026-05-12 — Verifiability-triangle audit methodology` + `CLAUDE.md` operating-disciplines row):

- The L1↔L2↔L3 verifiability triangle is the lens for all subsequent structural work. Every workstream carries an axiom-promotion delta (`Δ : axiom_id × bucket_before → bucket_after`); cutover criteria are axiom-bucket-witnessed; campaigns are cross-cutting tags, not parallel phases.
- Per-PR L3 review for PRs touching boundary code or adding config/CLI surface: "which L3 axioms does this touch; are they Bucket A or below; does this strengthen or weaken the structural commitment?"
- Chapter-close L3 step: every chapter close adds a one-paragraph audit check naming axioms touched and new Bucket-D gaps introduced.
- Annual re-audit refresh.

**Deferred-with-trigger** (unchanged):
- `LiveOssysConnection` (chapter 3.2 forward signal) remains reserved.
- Lifecycle temporal axis named in A6-amended but not operationalized (placeholder Group Lifecycle in `PRODUCT_AXIOMS.md`).
- 4 IR concepts deliberately NOT lifted in A.0' (OriginalName / ExternalDatabaseType / per-column IndexColumnDirection / IsPlatformAuto) per `V2_PRODUCTION_CUTOVER.md` §11.5.

**One thing to internalize before coding:** *campaign tags are cross-cutting, not sequential.* A.7.1 was Campaign A. A.4.5 is Campaign B. The next slice you pick up will likely carry a campaign tag too. The campaign isn't a phase to "finish before moving on" — it's a structural-commitment class that the slice belongs to. Read the §12 delivery matrix entry for whatever axioms your slice operationalizes; that tells you the campaign membership.

## 2026-05-12 — V2 cutover plan + verifiability-triangle audit landed (preserved; some items now resolved in the section above)

**Branch:** `claude/audit-v1-v2-sidecar-7Ifij`. **Status:** session-driven, not chapter-driven; pivot from chapter-5 work into a product-readiness audit + structural-commitments campaign plan. Test baseline holds (1121 tests passing post-Slice-1 PhysicallyRenamed).

What landed this session, in order:

- **`V2_PRODUCTION_CUTOVER.md`** (Draft 2.2) — the cutover plan: phase ladder, IR-fidelity workstream (A.0'), config schema, locked-in decisions D1–D12, risks R1–R12, deferral catalog. Currently the canonical plan; campaigns from the audit below operationalize as Phase A workstreams.

- **Slice 1: `PhysicallyRenamed` variant** (commit `9d578cc`) — first "airtight-by-design" slice. `TransformKind` extended with typed `PhysicallyRenamed of PhysicalRename` carrying `{ Before; After }` TableIds. `TableRename` emits the new variant; no-op renames suppressed. 1121 tests green.

- **`AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md`** (1410 lines) — the integrator's view of V2's structural posture across three levels: L1 commitments / L2 axioms (`AXIOMS.md`) / L3 product axioms. Coverage map (Bucket A/B/C/D), 9-tier illegal-states catalog from a 4-agent bottom-up scan, gap-hunt 30 candidate axioms, three proposed campaigns (A: 4 cutover-blocker unnamed axioms; B: structural fortification subsuming the prior slice-2/slice-3 work; C: Tier-2 + boundary VOs + Config strengthening). **Read Part I (framing) + Part IX (campaigns) at minimum.**

- **`PRODUCT_AXIOMS.md`** — the canonical L3 sibling to `AXIOMS.md`. 56 L3 product axioms grouped by core concern (schema/data/identity/diagnostics/cutover-safety + cross-cutting) plus four Tier-1 unnamed boundary candidates pending Campaign A.

- **Cross-reference updates** across `CLAUDE.md` (reading order item 3.5 + operating-disciplines row), `AXIOMS.md` (header pointer), `V2_PRODUCTION_CUTOVER.md` (companion-docs line + §11.4 audit-log entry), `DECISIONS.md` (verifiability-triangle methodology entry), `README.md` (brief pointer).

**Outstanding before next slice begins:**
1. Operator decision on Campaign A ordering (atomic emission vs CDC silence first).
2. Operator decision on axiom-naming convention (extend A41+ in `AXIOMS.md` vs separate `L3-Boundary-*` namespace).
3. Operator's "document of key evolutions" (R1) — will likely reshape UAT-users scope and possibly add a sixth core concern.

**Load-bearing:** the L1↔L2↔L3 verifiability triangle is the lens for all subsequent structural work. Per the new operating discipline in `CLAUDE.md`, every chapter close adds a one-paragraph audit check (which L3 axioms touched; new Bucket-D gaps introduced). Per-PR L3 review for PRs touching boundary code or adding config/CLI surface.

**Deferred-with-trigger:** `LiveOssysConnection` (chapter 3.2 forward signal) remains reserved; Lifecycle temporal axis named in A6-amended but not operationalized (placeholder Group Lifecycle in `PRODUCT_AXIOMS.md`).

## Chapter 5 open + slices ν + θ (added 2026-05-11; FSharp.Analyzers.SDK + Coordinates Stage 2 VOs)

**Branch:** `claude/chapter-4-ddd-improvements-XVCAM`. **Test baseline:** 1072 non-canary tests passing (+12 across slices ν + θ); 0 skipped; 0 build warnings under `TreatWarningsAsErrors=true`. **Lint:** clean across 27 rules (one LINT-ALLOW added on the analyzer's diagnostic message at a terminal text-emission boundary; rationale per `DECISIONS 2026-05-10 — LINT-ALLOW substantive-rationale discipline`).

Chapter 5 (Phase 8 pragmatic close per V2_DRIVER §252) opens as the formal chapter name for the consumer-pressure-driven hygiene + governance queue. **Open-ended**: slices land as separate commits; no single-chapter close fires until the queue empties or stabilizes per V1-sunset milestones.

### Slices ν + θ (chapter open)

| # | Slice | What |
|---|---|---|
| ν | F# Analyzers SDK custom analyzer | New `Projection.Analyzers` project (net8.0; `FSharp.Analyzers.SDK` 0.30.0 pinned for F#-9-SDK compat); one analyzer `Projection001NoUnsafeTimeInCore` (untyped-AST walk; detects `DateTime.Now`/`UtcNow`/`Today`/`Guid.NewGuid`/`Random.Shared` calls under `src/Projection.Core/`); `.config/dotnet-tools.json` registers `fsharp-analyzers`; `scripts/run-analyzers.sh` is the opt-in runner. End-to-end verified: runner walks all 28 Core files, reports zero violations (Core is clean by discipline). |
| θ | Coordinates Stage 2 typed VOs | `SchemaName` / `TableName` / `ColumnName` single-case-DU smart constructors land in `Coordinates.fs`. Reject null / empty / whitespace; reject >128 chars (SQL Server identifier limit). **Record-field migration deferred-with-trigger** (Stage 1 docstring's "real bug" trigger preserved; typed surface is opt-in for new code; existing `string`-field readers compile unchanged). 12 acceptance / rejection / boundary tests. |

### Outstanding queue (post-chapter-5-open)

**Within chapter 5 (deferred-with-trigger; consumer-pressure-driven):**

- **PhysicalRealization / Column.ColumnName record-field migration to Stage 2 typed VOs** — `Coordinates.fs:19-23` Stage 1 trigger preserved.
- **Additional FSharp.Analyzers.SDK analyzers** — false-negative on the grep rules drives new analyzer adoption.
- **CI integration for the analyzers runner** — earns its place when the analyzer set grows beyond one rule.
- **Hex port lifts** (`IArtifactSink`, `IDeployHost`) — under genuine consumer demand.
- **Cutover-day operator runbook** — joint deliverable with solution architect.
- **V1 sunset planning** — after cutover+30 + one full schema-evolution cycle.

**Deferred-with-trigger from chapter 3.x close:**

- Slice ε — Modality marks → comments / extended properties.
- Slice ζ — Byte-determinism cash-out via post-hoc Origin.xml canonicalization.
- Per-Catalog parameterization of DockerImageEmitter Dockerfile + entrypoint.
- Chapter 4.4 RemediationEmitter (V2_DRIVER §147 free-corollary).

**Quietly-deferred queue** — preserved at the chapter 3.x close prologue below.

---

## Chapter 3.x close (added 2026-05-11; DacpacEmitter dev-tooling + DockerImageEmitter; V2-driver KPI Phase 6 substantively shipped under reframe)

**Branch:** `claude/chapter-4-ddd-improvements-XVCAM`. **Test baseline:** 1060 non-canary tests passing (+48 net since chapter 4.3 close; +13 across chapter 3.x); 0 skipped; 0 build warnings under `TreatWarningsAsErrors=true`. **Lint:** clean across 27 rules (zero new LINT-ALLOWs in chapter 3.x).

Chapter 3.x closes the **dev-tooling DACPAC artifact path** end-to-end: V2 Catalog → typed-AST stream → DacFx model → `.dacpac` bytes → Docker image → registry → `docker pull` + `docker run`. The operator's one-command stand-up requirement is structurally green; production deploy stays untouched on the SSDT-style file path. The Tier-3 `text-builder-as-first-instinct` Active deferral is cashed out — DacFx (`Microsoft.SqlServer.DacFx` v162.x) is in the codebase and active inside `Projection.Targets.SSDT`. **AXIOMS T1 binary-emitter amendment cashed** at chapter close: text emitters preserve byte-equality; binary emitters preserve content-equality via DacFx model round-trip; the unifying predicate `t1ByteEqualOrModelEquivalent` chooses per emitter kind.

### Slice arc α + β + γ + δ_dock (this chapter)

| # | Commit | Slice | What |
|---|---|---|---|
| 1 | `090f2d7` | α | DacpacEmitter v0 + chapter open + `Microsoft.SqlServer.DacFx` NuGet + 4 tests; Tier-3 hard-requirement deferral cashed out; DacFx integration deferral cashed out |
| 2 | `5985b40` | β + γ + δ_dock | FK round-trip; Indexes round-trip; **DockerImageEmitter** producing a typed `DockerImageContext { Dockerfile; DacpacBytes; EntrypointScript; Readme }` for one-command dev stand-up |
| 3 | (this commit) | close | CHAPTER_3_X_CLOSE.md (8-item ritual); AXIOMS T1 binary-emitter amendment cashed; three slices deferred-with-trigger (ε modality marks; ζ byte-determinism; per-Catalog parameterization) |

### Outstanding queue (post-chapter-3.x close → Chapter 5)

**Chapter 5 (Phase 8 pragmatic close) opens next.** Consumer-pressure-driven items per V2_DRIVER §252:

- **Slice ν — F# Analyzers SDK custom analyzer** (originally scoped at chapter 3.7). Complements 27 grep lint rules with AST detection.
- **Slice θ — Coordinates Stage 2 typed VOs** (`SchemaName` / `TableName` / `ColumnName`; originally scoped at chapter 3.7). DDD VO win when adapter ripple is acceptable.
- **Hex port lifts** (`IArtifactSink`, `IDeployHost`) — under genuine consumer demand.
- **Cutover-day operator runbook** — joint deliverable with solution architect.
- **V1 sunset planning** — after cutover+30 + one full schema-evolution cycle.

**Deferred-with-trigger at chapter 3.x close:**

- Slice ε — Modality marks → comments / extended properties (trigger: downstream consumer demands structured access to modality marks from the .dacpac model).
- Slice ζ — Byte-determinism cash-out via post-hoc Origin.xml canonicalization (trigger: snapshot consumer demands byte-stable dacpac artifacts).
- Per-Catalog parameterization of Dockerfile / entrypoint (trigger: second consumer with conflicting defaults).

**Quietly-deferred queue (no current consumer; surface at next chapter audit):**

- OSSYS adapter User-kind identification surface (chapter 4.2 close-deferred).
- CSV adapter for `ManualOverride` (UserMapLoader) (chapter 4.2 close-deferred).
- `Attribute.Default` field + DEFAULT constraint emission (chapter 4.1.A close-deferred).
- `Kind.Description` + `Attribute.Description` fields + extended-properties emission (chapter 4.1.A close-deferred).
- Statement DU MERGE/UPDATE promotion (chapter 4.1.B close-deferred; third-consumer trigger).
- Sort-vs-data deferral predicate distinction (chapter 4.1.B close codified discipline).
- Chapter 4.4 RemediationEmitter — V2_DRIVER §147 free-corollary table: "deferred under V2-driver KPI; revisit at chapter 5+ if remediation is operator-needed."
- Chapter 4.3 slices δ (CLI wire-up) + ε (V1 differential test).

---

## Chapter 3.x open + slices α + β + γ + δ_dock (preserved for reference)

**Branch:** `claude/chapter-4-ddd-improvements-XVCAM`. **Test baseline:** 1060 non-canary tests passing (+4 slice α; +2 slice β; +1 slice γ; +6 slice δ_dock = +13 in chapter 3.x; net +48 since chapter 4.3 close baseline); 0 skipped; 0 build warnings under `TreatWarningsAsErrors=true`. **Lint:** clean across 27 rules (DacFx adoption + Docker context emission both pillar-7 right moves; zero new LINT-ALLOWs in the chapter).

Chapter 3.x opens the **DacpacEmitter dev-tooling chapter** — reframing the pre-scope's deploy-path-conditional V2-driver KPI critical-path framing to a dev-tooling sibling-Π emitter per operator directive ("stand up a local copy of the database in no time flat — almost a one-click deploy strategy for my development team"). Production deploy path stays SSDT-style file deploy via `SsdtDdlEmitter.emitSlices`; DacpacEmitter ships the `.dacpac` artifact format the dev team consumes via `sqlpackage`, Visual Studio, or `DacServices.Deploy` to a local SQL Server.

**Slice δ_dock reframes pre-scope slice δ** (CLI `dac deploy` verb) → **DockerImageEmitter** per the operator's follow-up directive: "create a custom Docker package that stands itself up with the loaded SQL server inside of it ... single command up and my team doesn't have to have the repository to pull the data fresh each time." The emitter produces a Docker build context (Dockerfile + dacpac + entrypoint + README) that CI/CD builds into a registry-published image. Dev consumption is `docker pull` + `docker run` — no source checkout required.

**Three Active deferrals retired at chapter open + slice α:**

1. **DacFx integration in `Projection.Targets.SSDT.DacpacEmitter`** (Active deferrals row 214): cashed out — chapter ships under dev-tooling framing.
2. **`Microsoft.SqlServer.Dac` (DacFx) adoption Tier-3 hard-requirement** (Active deferrals row 223): cashed out — `Microsoft.SqlServer.DacFx` v162.x NuGet adopted in `Projection.Targets.SSDT.fsproj`. Pure F# wrapper (no C# subproject; pre-scope §6.2 bias yielded under empirical pressure — DacFx's V2-relevant surface is small, all `IDisposable`-aware calls F# handles via `use`).
3. **T1 amendment for binary emitters** — content-equality via DacFx round-trip (`Catalog → emit → DacPackage.Load → TSqlModel.GetObjects` enumeration matches across invocations), NOT byte-equality. DacFx embeds wall-clock timestamps in Origin.xml; the algebraic claim holds at the DacFx model level.

### Slice arc α + β + γ + δ_dock (this chapter to date)

| # | Slice | What |
|---|---|---|
| α | DacpacEmitter v0 + chapter open + `Microsoft.SqlServer.DacFx` NuGet + 4 tests (non-empty bytes; DacFx round-trip yields one Table per Kind; T1 content-determinism; T11 commutativity vs SsdtDdlEmitter on physical (Schema, Table) pair) |
| β | FK round-trip test — `sampleCatalog`'s Order→Customer FK ingests via DacFx + re-enumerates through `ForeignKeyConstraint.TypeClass` |
| γ | Indexes round-trip — `indexedCatalog` fixture (single-column unique + composite non-unique + single-column non-unique) ingests via DacFx + re-enumerates through `Index.TypeClass`; `Index.Unique` property preserved across the round-trip |
| δ_dock | **DockerImageEmitter** (reframes pre-scope slice δ per operator directive): emits a Docker build context `{ Dockerfile; DacpacBytes; EntrypointScript; Readme }` that CI builds into a self-contained `mcr.microsoft.com/mssql/server:2022-latest`-based image. Image bakes in the dacpac + installs `sqlpackage` at build; entrypoint starts SQL Server, polls until ready, publishes the dacpac. Dev team `docker pull` + `docker run` with no source checkout — "single command up." 6 tests (Dockerfile shape; entrypoint shape; README shape; embedded dacpac round-trips through DacFx; T1 byte-determinism on the static-template fields) |

**A18 amended preserved structurally** — both `DacpacEmitter.emit` and `DockerImageEmitter.emit` take `Catalog -> Result<...>` (Catalog only; no Policy parameter; Profile widening lands when a slice forces it). **T11 keyset coverage** holds across siblings (SsdtDdlEmitter directory bundle and DacpacEmitter model agree on the per-Kind (Schema, Table) set; DockerImageEmitter wraps the dacpac unchanged). **Pillar 7** holds end-to-end (Statement generation via SsdtDdlEmitter typed-AST stream; per-statement script via `ScriptDomGenerate.generateOne`; `.dacpac` serialization via DacFx `DacPackageExtensions.BuildPackage`; SQL Server image via Microsoft's canonical `mcr.microsoft.com/mssql/server`; DACPAC deploy via Microsoft's canonical `sqlpackage`).

### Outstanding queue (post-chapter-3.x slice δ_dock)

**Within chapter 3.x:**

- **Slice ε** — Modality marks → comments / extended properties.
- **Slice ζ** — Byte-determinism cash-out (post-hoc canonicalization). **Deferred-with-trigger** at chapter open: surface only when a snapshot consumer demands byte-stable dacpac artifacts.
- **Per-Catalog parameterization of the Dockerfile / entrypoint** — slice δ_dock ships pinned constants (database name = `ProjectionCatalog`; base image = `mcr.microsoft.com/mssql/server:2022-latest`). Per-Catalog overrides land when an operator workflow demands them (IR-grows-under-evidence).

**Now-unblocked (per V2-driver KPI sequencing + DacpacEmitter dev-tooling reframe):**

- **Chapter 4.4 RemediationEmitter** — schema-level partial-state recovery; composes over `CatalogDiff` + DacpacEmitter's typed model output. Inherits the dev-tooling framing per the chapter 4.3 close `2026-05-11 — Chapter 4.3 close + slices δ + ε deferred-with-trigger` entry.

---

## Chapter 4.3 close (added 2026-05-11; Operational Diagnostics V2 structural arc shipped; V2-driver KPI Phase 5 closed)

**Branch:** `claude/chapter-4-ddd-improvements-XVCAM`. **Test baseline:** 1012 non-canary tests passing + ~16 Docker-dependent canary tests; 0 skipped; 0 build warnings under `TreatWarningsAsErrors=true`. **Lint:** clean across 27 rules.

Chapter 4.3 closes the **operational-diagnostics axis** — the operator-facing surface of V2's diagnostic pipeline. Three sibling-Π emitters under `Projection.Targets.OperationalDiagnostics` route the existing `Diagnostics<'a>` writer's entries into three operator-vocabulary artifacts via a Code-prefix routing table. The work is **projection over substrate, not new algebra** — no new IR, no new pass shape, no parallel writer.

**The chapter-2 "three-channel Diagnostics split" Active deferral was retired at chapter 4.3 open** with the **refuse the split** decision: the three V1 artifacts ARE the three channels (decision-log = audit; opportunities = operator; validations = developer); routing happens at emit time via the Code-prefix table, not via a structural split of `Diagnostics<'a>`.

### Slice arc α + β + γ (this chapter)

| # | Commit | Slice | What |
|---|---|---|---|
| 1 | `bf3770b` | α | DecisionLogEmitter v0 + chapter-2 three-channel-split deferral retired + new `Projection.Targets.OperationalDiagnostics` project |
| 2 | `abe0040` | β + γ | `Routing` primitive + `OpportunitiesEmitter` + `ValidationsEmitter` + chapter-signature **Routing partition property** + R4 multi-environment promotion property test (independent forward-progress per V2_DRIVER.md) |

**A18 amended preserved structurally** — every emitter's signature is `Catalog × DiagnosticEntry list`; never Policy. **T11 keyset coverage** holds across all three siblings (every catalog kind keyed; empty `entries: []` when no diagnostics match). **Pillar 1** holds end-to-end (JsonNode typed seam at the Π port; strings emerge only at terminal `Utf8JsonWriter`).

### Outstanding queue (post-chapter-4.3)

**V2-driver KPI critical-path under V2_DRIVER.md sequencing — closed front-to-back for the unconditional path:**

- ✅ Chapter 4.1.A (production SSDT DDL emitter)
- ✅ Chapter 4.1.B (CDC-aware data triumvirate; KPI's highest-leverage chapter)
- ✅ Chapter 4.2 (User FK reflow; A32 cashed out)
- ✅ Chapter 4.3 (Operational Diagnostics V2; three-channel deferral retired)
- ✅ R4 multi-environment promotion property test (independent forward-progress)

**Remaining critical-path (deploy-path-conditional):**

- **Chapter 3.x DacpacEmitter** — DacFx adoption mandatory per Tier-3 codification. **Conditional on the cutover team's deploy-path choice**: SSDT-style file deploy (already covered by `SsdtDdlEmitter`) vs DACPAC + SqlPackage deploy (requires this chapter). Pre-scope: `CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md`. Active deferral entry at top of `DECISIONS.md`.
- **Chapter 4.4 RemediationEmitter** — schema-level partial-state recovery; composes over `CatalogDiff` + `DacpacEmitter`. **Sequenced after chapter 3.x DacpacEmitter** (inherits the deploy-path conditionality). Pre-scope: `CHAPTER_4_PRESCOPE_DIAGNOSTICS_AND_REMEDIATION.md` Part 2.

**Deferred-with-trigger at chapter 4.3 close (per the close-ritual discipline):**

- **Chapter 4.3 slice δ — CLI wire-up in `Projection.Pipeline`** — operator-UX integration; trigger: real cutover-day operator workflow consuming the three artifacts.
- **Chapter 4.3 slice ε — V1 differential test** — V1 envelope walk; trigger: V1's `OpportunityLogWriter` + `PolicyDecisionLogWriter` + `ValidationReport` writers stabilize as canonical reference shape.

**Independent forward-progress alternatives (no chapter open required):**

- (None substantive — R4 shipped this session; the cutover-ladder structural commitment is structurally encoded.)

**Quietly deferred (no current consumer; reframe at next chapter audit):**

- OSSYS adapter User-kind identification surface (chapter 4.2 close-deferred).
- CSV adapter for `ManualOverride` (UserMapLoader) (chapter 4.2 close-deferred).
- `Attribute.Default` field + DEFAULT constraint emission (chapter 4.1.A close-deferred; rowset-adapter trigger).
- `Kind.Description` + `Attribute.Description` fields + extended-properties emission (chapter 4.1.A close-deferred; rowset-adapter trigger).
- Statement DU MERGE/UPDATE promotion (chapter 4.1.B close-deferred; third-consumer trigger).
- Sort-vs-data deferral predicate distinction (chapter 4.1.B close codified discipline).
- Chapter-3.7 audit-cleanup slice queue (γ traverseCatalog / ζ attach-adapters / η Result-CE adoption / θ Coordinates Stage 2 / ι writer-monad codification / κ Lineage.tell perf audit / λ SsKey.rootOriginal V1 prefix / μ Restrict→NoActionSql Diagnostics / ν F# Analyzers SDK / ξ-π port lifts).

---

## Chapter 4.2 close (added 2026-05-11; User FK reflow shipped end-to-end; V2-driver KPI Phase 4 closed)

**Branch:** `claude/chapter-4-ddd-improvements-XVCAM`. **Test baseline:** 963 non-canary tests passing + ~16 Docker-dependent canary tests; 0 skipped; 0 build warnings under `TreatWarningsAsErrors=true`. **Lint:** clean across 27 rules.

Chapter 4.2 closes the **User FK reflow axis** — the V2-driver KPI's per-axis correctness depth for user-identity reflow across the four-environment cutover. Slice arc α → η shipped end-to-end on the branch:

| # | Commit | Slice | What |
|---|---|---|---|
| 1 | `17930c2` | α | UserMatchingStrategy DU + identity types (UserId/SourceUserId/TargetUserId/Email) + Policy 5th axis |
| 2 | `4678a76` | β + γ | UserPopulation in Profile + UserRemap.fs (UserRemapContext + RemapDiagnostic + smart constructor) |
| 3 | `d2a091d` | δ | UserFkReflowPass.discover (ByEmail real; others deferred-stub) |
| 4 | `a0e9807` | ε | Full strategy DU coverage (BySsKey / ManualOverride / FallbackToSystemUser; recursive composition; lazy indexes) |
| 5 | `693eb13` | ζ | Reference.IsUserFk : bool IR refinement (23 sites updated; closed-DU empirical-test held) |
| 6 | `08a75cf` | η | UserRemapContext wiring into MigrationDependenciesEmitter + multi-environment commutativity property |

**A32 cashed out at chapter 4.2 close** (per AXIOMS.md A32 cash-out body). The pass-produces-emitter-consumable-value pattern is now a wired template — `UserFkReflowPass.discover : ... -> Lineage<Diagnostics<UserRemapContext>>` produces; `MigrationDependenciesEmitter.emitWithUserRemap` consumes; the multi-environment commutativity property test specializes T4.

**Two new Active deferrals codified at this close:**

- **OSSYS adapter User-kind identification surface** — OSSYS adapter currently sets `IsUserFk = false` for every Reference; trigger: real OSSYS-source-V2-target reflow workflow with User-FK columns. Slice η emitter integration is structurally complete; gap is at adapter boundary only.
- **CSV adapter for `ManualOverride` (UserMapLoader)** — ManualOverride works via programmatic construction today; trigger: real operator workflow demands file-format pickup path. Mirrors chapter 4.1.B slice ε NDJSON-adapter deferral.

### Outstanding queue (post-chapter-4.2)

**Critical-path under V2-driver KPI** (per `V2_DRIVER.md`):

- **Chapter 4.3 — three-channel Diagnostics split** (DecisionLogEmitter / OpportunitiesEmitter / ValidationsEmitter). Pre-scope: `CHAPTER_4_PRESCOPE_DIAGNOSTICS_AND_REMEDIATION.md`. The substrate is already shipped (`Diagnostics<'a>` writer); this chapter is projection, not new algebra. **Natural next move** per V2_DRIVER.md sequencing.
- **Chapter 4.1.A slices 6 / 7 / 8** — cross-module FKs / identity + defaults / extended properties. Now-unblocked per chapter 3.2 SnapshotRowsets. Pre-scope: `CHAPTER_4_PRESCOPE_SSDT_DDL_EMITTER.md` §8.

**Hard-requirement Active deferrals (read at chapter open):**

- **Chapter 3.x DacpacEmitter** — MUST adopt `Microsoft.SqlServer.Dac` (DacFx). **Conditional on whether the cutover deploy path requires DACPAC** (product question).

**Independent forward-progress:**

- **R4 multi-environment promotion property test** — uses M4 Tolerance taxonomy `Set<ToleratedDivergence>`; ~150 LOC; chapter 4.2's multi-environment commutativity property is the worked precedent.

**Quietly deferred (no current consumer; reframe at next chapter audit):**

- **OSSYS adapter User-kind identification surface** (chapter 4.2 close-deferred; see DECISIONS entry).
- **CSV adapter for `ManualOverride` (UserMapLoader)** (chapter 4.2 close-deferred; see DECISIONS entry).
- **V1↔V2 differential test for UserFkReflowPass** (pre-scope §9; deferred pending V1 fixture canonicalization).
- **`SourceTag` value-object refactor of SsKey** (chapter 4.2 close-deferred per pre-scope's "what this chapter does NOT do" list).

---

## Chapter 4.1.B close (added 2026-05-11; CDC-aware data triumvirate fully closed end-to-end; V2-driver KPI Phase 3 highest-stakes deliverable shipped)

**Branch:** `claude/chapter-4-ddd-improvements-XVCAM`. **Test baseline:** 893 passing non-canary tests + ~16 Docker-dependent canary tests, 0 skipped, 0 build warnings under `TreatWarningsAsErrors=true`. **Lint:** clean across 27 rules. **Canary suite hang fix:** shipped (Docker-SqlServer xUnit collection + dedicated CdcSilence container).

Chapter 4.1.B closes the **CDC-aware data triumvirate** — the V2-driver KPI's highest-leverage chapter per `V2_DRIVER.md` per-axis correctness stakes table. Slice arc α → κ shipped end-to-end across two close arcs:

- **Slices α/β/γ** shipped at the joint chapter-4.1.A close arc (`CHAPTER_4_1_A_CLOSE.md`). Slice γ — CDC-silence canary GREEN under real SQL Server 2022 CDC — was the chapter signature deliverable.
- **Slices δ → κ** shipped this session arc on branch `claude/chapter-4-ddd-improvements-XVCAM`. See `CHAPTER_4_1_B_CLOSE.md` for the full slice-by-slice synthesis.

**The eight-item chapter-close ritual was operated** at this close (per `CHAPTER_4_1_B_CLOSE.md`); two new deferrals codified at the Active deferrals index (Statement DU MERGE/UPDATE promotion; sort-vs-data deferral predicate distinction).

### Slice arc δ → κ (this session)

| # | Commit | Slice | What |
|---|---|---|---|
| 1 | `23c9d76` | δ + topo v4 | Two-phase insertion / cycle-breaking + `TopologicalOrderPass` v3→v4 self-loop SCC detection + `Kind.tryFindAttribute` lift |
| 2 | `fafa8fd` | (canary fix) | `Docker-SqlServer` xUnit collection + dedicated CdcSilence ephemeral container — closes a canary-suite-hang bug |
| 3 | `44c4871` | η | DataEmissionComposer + EmissionPolicy.DataComposition DU + `StaticSeedsEmitter.emitWithTopo` (hoisted-topo) |
| 4 | `0aa3761` | ε | MigrationDependenciesEmitter (typed AST per Tier-3 hard-requirement Active deferral cash-out) |
| 5 | `9544006` | ζ + θ | BootstrapEmitter (structural stub) + `EmitError.OverlappingEmitterCoverage` + composer partition assertion |
| 6 | `340eb15` | ι + κ | `composeRendered` global Phase-1-then-Phase-2 ordering + `RenderedPhase1`/`RenderedPhase2` split + typed `DataInsertRow.Values : Map<Name, SqlLiteral>` (pillar 1 lift) |

**A18 amended holds structurally** for all three sibling-Π emitters (Static / Migration / Bootstrap) — none can type-check with a Policy parameter; only `DataEmissionComposer` reads `Policy.Emission.DataComposition`. **T11 keyset coverage** holds across all three siblings. **Pillar 1** strengthened at the row level (typed `SqlLiteral` flows through `DataInsertRow.Values`; raw strings emerge only at the absolute terminal `Sql160ScriptGenerator` boundary). **Pillar 7 Tier-3 hard-requirement Active deferrals** for chapter 4.1.B all cashed out.

### Outstanding queue (post-chapter-4.1.B)

**Critical-path under V2-driver KPI** (per `V2_DRIVER.md`):

- **Chapter 4.2 — `UserFkReflowPass` + `UserMatchingStrategy` + `SourceTag` refactor of SsKey.** Pre-scope: `CHAPTER_4_PRESCOPE_USERFK_REFLOW.md`. Plugs into `UserRemapContext` shape that slice ζ established + composer's `composeRenderedFull` pipeline-integration entry. **Natural next move.** Inherits chapter 3.2's `OssysOriginal` operational reachability for cross-version `V1Mapped` UUIDv5 derivation.
- **Chapter 4.3 — three-channel Diagnostics split** (DecisionLogEmitter / OpportunitiesEmitter / ValidationsEmitter). Pre-scope: `CHAPTER_4_PRESCOPE_DIAGNOSTICS_AND_REMEDIATION.md`. Activates Diagnostics writer's deferred channel-routing under real consumer pressure.
- **Chapter 4.1.A slices 6/7/8** (cross-module FKs / identity + defaults / extended properties). **Unblocked by chapter 3.2** (SnapshotRowsets) — IR widening surfaces via the rowset path's SsKey carriage + EspaceKind / IsSystemEntity activation.

**Hard-requirement Active deferrals (read at chapter open per Tier-3 codification):**

- **Chapter 3.x DacpacEmitter** — MUST adopt `Microsoft.SqlServer.Dac` (DacFx). Pre-scope at `CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md`. Active deferral entry at top of `DECISIONS.md`. **Conditional on whether the cutover deploy path requires DACPAC** (product question).

**Two new deferrals codified at chapter 4.1.B close** (read at chapter open):

- **Statement DU MERGE/UPDATE promotion** — third MERGE/UPDATE consumer triggers the cross-target lift (DacpacEmitter Phase-2 path / Faker / Profile-attached rows in chapter 4.3 are candidates).
- **Sort-vs-data deferral predicate distinction** — sibling-but-distinct cycle-question discipline; future emitter agents choose the predicate that fits their semantic question explicitly.

**Independent forward-progress** (no chapter open required):

- **R4 multi-environment promotion property test** — uses M4 Tolerance taxonomy `Set<ToleratedDivergence>`; ~150 LOC; concrete next slice.

**Quietly deferred** (no current consumer; reframe at next chapter audit):

- **Migration adapter (NDJSON / CSV pickup directory)** — chapter 4.1.B slice ε; deferred until real ingestion path consumer surfaces.
- **Bootstrap row sources** (system users / default policies / profile-attached rows) — chapter 4.1.B slice ζ; deferred until chapters 4.2/4.3 supply consumers.
- **Tolerance slice β** (quotient operator on PhysicalSchemaDiff). Slice α variants are about axes PhysicalSchemaDiff doesn't compare; reopen if a new variant lands that requires diff-filtering.
- **Outstanding chapter-3.7 audit-cleanup slice queue** (γ traverseCatalog / ζ attach-adapters / η Result-CE adoption / θ Coordinates Stage 2 / ι writer-monad codification / κ Lineage.tell perf audit / λ SsKey.rootOriginal V1 prefix / μ Restrict→NoActionSql Diagnostics / ν F# Analyzers SDK / ξ-π port lifts) — see chapter-3.7 prologue below for triggers.

---

## Chapter 3.2 close (added 2026-05-10; substantive close + JSON-projection-lossiness class structurally resolved)

**Branch:** `claude/review-ddl-exporter-zB3LF`. **Test baseline:** 882 passing non-canary tests + ~16 Docker-dependent canary tests, 0 skipped, 0 build warnings under `TreatWarningsAsErrors=true`. **Lint:** clean across 27 rules. **Perf-gate:** clean (adapter-only changes; no canary-affecting work).

Chapter 3.2 closes the **JSON-projection-lossiness class** structurally by adding the `SnapshotRowsets` variant of `SnapshotSource` end-to-end. Five substantive slices + post-close bug-fix arc:

- **Slice 1** (`6dab9cd`) — `SnapshotRowsets` DU variant + `RowsetBundle` carrier + `ModuleRow` / `KindRow` / `AttributeRow` records + `parseRowsetBundle` minimum. SsKey at all three levels.
- **Slice 2** (`0354727`) — Reference rowsets (`#RefResolved` ⊕ `#FkReality`). FK SsKey carriage; rule 16 same-module assumption tested under rowset path.
- **Slice 3** (`d5d1812`) — `EspaceKind` activation; `parseOriginFromRowset` three-way real refines rule 17 from JSON-path placeholder.
- **Slice 4** (`6eae21f`) — `IsSystemEntity` activation; new `ModalityMark.SystemOwned` variant. Third lossiness-class member resolved.
- **Slice 5** (`a74b904`) — Cross-source parity tests (JSON ↔ Rowset). Total-equality (no-Guids) + shape-equality (Guid-carrying). Closes the chapter.
- **Post-close bug fix** (`0336795`) — `propagateOrFallback` codification at two-consumer threshold; seven build-failure sites refactored uniformly across both translation paths. Underlying error codes (e.g., `adapter.osm.unmappedDeleteRule`) survive the build-level wrap instead of being swallowed under generic umbrellas.

**A1's JSON-projection-lossiness bound — operationally resolved.** Chapter 3.2 makes A1's `OssysOriginal` variant operationally reachable at the OSSYS-adapter boundary for the first time. AXIOMS.md A1 footer + four-variant amendment updated. See `CHAPTER_3_2_CLOSE.md` for the full substantive synthesis.

**`SnapshotRowsets` Active deferral cashed out** at `DECISIONS 2026-05-10 — Chapter 3.2 close`. One silent-trigger fire scanned + cashed; 16 other active deferrals untriggered.

### Outstanding queue (post-chapter-3.2)

**Critical-path under V2-driver KPI** (per `V2_DRIVER.md`):
- **Chapter 4.1.B slice δ** (two-phase insertion / cycle-breaking). CDC-silence-on-idempotent-redeploy property test is the V2-driver KPI's highest-leverage single deliverable.
- **Chapter 4.1.B slices ε/ζ** (MigrationDependencies + Bootstrap). `ScriptDomBuild.buildMergeStatement` adoption mandatory per Active deferrals row.
- **Chapter 3.x DacpacEmitter**. DacFx adoption mandatory per Active deferrals row.
- **Chapter 4.2 User FK reflow**. Inherits chapter 3.2's `OssysOriginal` operational reachability; cross-version `V1Mapped` UUIDv5 derivation lands here.

**Highest-priority deferred slice (cross-cutting):**
- **Cross-module FK IR refinement** (Active deferrals row). Trigger: fixture exercising cross-module FK. Chapter 3.2 fixtures were all same-module; rowset path is structurally ready for the extension.

**Independent forward-progress** (carried from prior outstanding queue; not chapter-3.2-affected):
- R4 multi-environment promotion property test (uses M4 Tolerance taxonomy).
- Chapter 3.7 audit-cleanup slice queue (γ / ζ / η / θ / ι / κ / λ / μ / ν / ξ / ο / π) — `ξ` (`ICatalogReader` port lift) can now use chapter 3.2's `SnapshotRowsets` as second-source-of-truth precedent.

**Chapter close ritual joint pass** still beneficial across 3.1 / 3.5 / 3.6 / 3.7 / 4.1.A / 4.1.B-in-flight if the next chapter wants to discharge documentation drift before opening new substantive work. Chapter 3.2's own close ritual executed (see `CHAPTER_3_2_CLOSE.md` "Chapter-close ritual execution" section).

---

## Chapter 4.1.A close arc + 4.1.B in-flight prologue (added 2026-05-10; substantive close + V2-driver KPI Phase 2 + Phase 3 highest-stakes deliverable shipped)

## Chapter 4.1.A close arc + 4.1.B in-flight prologue (added 2026-05-10; substantive close + V2-driver KPI Phase 2 + Phase 3 highest-stakes deliverable shipped)

**Branch:** `claude/review-ddl-exporter-ilV0k`. **Test baseline:** 840 passing non-canary tests + ~16 Docker-dependent canary tests (skip-if-no-Docker), 0 skipped, 0 build warnings under `TreatWarningsAsErrors=true`. **Lint:** clean across 27 rules. **Perf-gate:** clean.

This prologue covers a **bundled close arc**: chapter 4.1.A (V2-driver KPI Phase 2; SSDT DDL emitter; in-flight surface closed), chapter 4.1.B (V2-driver KPI Phase 3; CDC-aware data triumvirate; opened + slices α/β/γ shipped — γ is the V2_DRIVER.md highest-stakes deliverable), the **RawTextEmitter retirement arc** (the chapter-3-era one-big-string + raw-INSERT pre-cursor fully retired; -520 LOC), and the **Tier-1/2/3 typed-AST transitions** (six retired LINT-ALLOWs at the StaticSeedsEmitter MERGE site + four canonical typed surfaces shipped + new failure mode codified). Substantive deliverables shipped (load-bearing):

### Chapter 4.1.A — production SSDT DDL emitter (V2-driver KPI Phase 2)

In-flight surface closed; slices 6/7/8 gated on chapter 3.2 SnapshotRowsets.

- **`SsdtDdlEmitter.emitSlices : Emitter<SsdtFile>`** (`Projection.Targets.SSDT/SsdtDdlEmitter.fs`) — per-kind `.sql` files via ScriptDom typed AST + `Sql160ScriptGenerator`. Slices 1+2+3+4+5 cover CREATE TABLE + composite PKs + non-PK indexes + intra-module FKs. RelativePath via cross-platform-deterministic forward slashes (`Modules/<Module>/<Schema>.<Table>.sql`).
- **`SsdtDdlEmitter.statements : Catalog -> seq<Statement>`** — typed-stream surface for canary tests + `Render.toText` consumers. Topologically ordered via `TopologicalOrderPass.runWith SkipSelfEdges` (FK targets emit before referencers). Same algorithm RawTextEmitter used.
- **`ManifestEmitter.emit`** (slice 9) — `manifest.json` per V1 SsdtManifest schema; `Utf8JsonWriter` gold-standard library.
- **`SsdtBundle.compose`** (slice 10) — composition of `(ArtifactByKind<SsdtFile>, Manifest)` into `Map<RelativePath, string>`. F# core never touches the file system; downstream hosts (Pipeline / CLI) consume the map.
- **Slices 6 (cross-module FKs), 7 (identity + defaults), 8 (extended properties) gated** on chapter 3.2 SnapshotRowsets surfacing IR widening.

### Chapter 4.1.B — CDC-aware data triumvirate (V2-driver KPI Phase 3)

Opened with strategic-frame eight-axis discipline; slices α/β/γ shipped; δ-θ pending.

- **Chapter open** (`CHAPTER_4_1_B_OPEN.md`) — strategic-frame axes named per `DECISIONS 2026-05-15`. CDC-silence-on-idempotent-redeploy property test is the highest-leverage single deliverable per `V2_DRIVER.md` per-axis correctness stakes table.
- **Slice α — `StaticSeedsEmitter v0`** (`fd38908`). New `Projection.Targets.Data` project (sibling to Targets.SSDT / Json / Distributions). `DataInsertScript` + `DataInsertRow` typed value foundation. V1-shape MERGE per `StaticSeedSqlBuilder.cs:211-260`. T11 sibling-Π keyset coverage; T1 byte-determinism; A18 amended (Catalog × Profile, never Policy).
- **Slice β — `Profile.CdcAwareness` field + change-detection MERGE predicate** (`2d8210e`). The load-bearing semantic addition. Per-kind dispatch on `CdcAwareness.isEnabled`: CDC-enabled kinds emit the change-detection predicate (`Target.[c] <> Source.[c] OR (Target.[c] IS NULL AND Source.[c] IS NOT NULL) OR (Target.[c] IS NOT NULL AND Source.[c] IS NULL)` per non-key column, all OR-joined); CDC-disabled kinds keep V1's predicate-free WHEN MATCHED. CdcAwareness lives on Profile (A34 alignment), not Policy.
- **Slice γ — CDC-silence canary GREEN** (`cdcd953`). Operationally proves under real SQL Server 2022 CDC that V2's redeploy pipeline does not fire spurious CDC capture entries on identical-content redeploys. Two `[<Fact>]` tests in `CdcSilenceTests.fs` (skip-if-no-Docker gated): positive (post == baseline; 0 new CDC entries) + sensitivity (changed-content redeploy DOES fire CDC; proves the canary mechanism is real). `sys.sp_cdc_scan` Agent-less synchronous capture; `cdc.<schema>_<table>_CT` row count assertion. Empirical finding: SQL Server 2022's MERGE→CDC pipeline doesn't capture no-op UPDATEs even from V1-shape unconditional WHEN MATCHED — V2's predicate is defense-in-depth (correct under any SQL Server version), not the load-bearing fix in 2022 specifically.
- **Slice δ (two-phase insertion / cycle-breaking), ε (MigrationDependenciesEmitter), ζ (BootstrapEmitter), η (DataEmissionComposer + EmissionPolicy.DataComposition DU), θ (partition assertion) pending.** Slices ε/ζ have a **hard-requirement Active deferral** (Tier-3 codification): MUST adopt `ScriptDomBuild.buildMergeStatement` from slice α precedent.

### M4 Tolerance taxonomy slice α — typed equivalence-class definition

`af7b96c`. The R6 split-brain governance + cutover fallback ladder + R4 multi-environment promotion test all depend on this typed surface.

- **`Projection.Core.ToleratedDivergence`** — closed DU enumerating five empirically-grounded divergences (HeaderCommentsOmitted / PostDeployForeignKeysSplit / IndexesUnreflected / StaticPopulationsUnreflected / CommentMetadataUnreflected). Each variant has concrete canary or emitter evidence today.
- **`Tolerance = Set<ToleratedDivergence>`** — value object with smart-constructor encapsulation (`strict` / `permissive` / `withDivergence` / `tolerates` / `divergences` / `isStrict` / `ofSet`). `Set` encoding (over a flat-bool record) per pillar 1 + pillar 8: the `Tolerance` IS the equivalence-class definition; membership says "this divergence is accepted."
- **Closed-DU expansion empirical-test discipline applied**: `coverage` function + `allKnown` cardinality test catch incomplete extensions at compile time + runtime.
- **Slice β** (quotient operator on PhysicalSchemaDiff) **reframed as no-op-until-consumer-pressure**: the slice α variants are all about axes that PhysicalSchemaDiff doesn't compare anyway. Reopen if a new variant lands that requires diff-filtering.
- **R4 multi-environment promotion property test** — uses the `Set<ToleratedDivergence>` encoding; pending; concrete next slice.

### RawTextEmitter retirement arc — chapter-3-era pre-cursor fully retired

`e4936d5` + `d91067a` + `197b9e7`. Net: -520 LOC.

- **Slice 1** — `SsdtDdlEmitter.statements : Catalog -> seq<Statement>` typed-stream surface (the missing piece that unblocked migration).
- **Slice 2** — Migrate all 9 call sites: Pipeline.fs, Cli/Program.fs (runWideCanary), CanaryRoundTripTests (×4), GeneratorScaleTests (×2), ScriptDomRoundTripTests, JsonEmitterTests (×2), RichProfilingEndToEndTests, SiblingEmitterContractTests (×2), SsdtDdlEmitterTests (×2). Topological order preserved via `TopologicalOrderPass.runWith SkipSelfEdges`. Re-baseline of substring assertions that depended on RawTextEmitter's `Provenance` trailing-comment SsKey roots (V2-IR-internal; SsdtDdlEmitter doesn't emit them).
- **Slice 3** — Delete `RawTextEmitter.fs` + `RawTextEmitterTests.fs`. Pillar 8 win: action-shaped name retires; concept-shaped `SsdtDdlEmitter` (chapter 4.1.A) + `StaticSeedsEmitter` (chapter 4.1.B) remain.

### Tier-1 typed-AST transitions — pillar-1 / pillar-7 alignment across the Outputs seam

Four transitions shipped this session (chapter 4.1.A close arc).

- **#4 — `Projection.Core.SqlLiteral` typed expression module** (`08ca554`). The IR→SQL-literal projection lives in Core; closed DU with eight variants (NullLit / IntegerLit / DecimalLit / BooleanLit / TextLit / TemporalLit / GuidLit / BinaryLit) one per PrimitiveType + NULL sentinel. `ofRaw` + `toString` + `formatRaw` convenience. Both consumers (SSDT.Render + Data.StaticSeedsEmitter) flow through the typed middle layer.
- **#1 — MERGE → ScriptDom MergeStatement typed AST** (`bface9a`). `ScriptDomBuild.buildMergeStatement` (~150 LOC of typed-AST construction with `MergeBuildArgs` record + per-column predicate builders); `StaticSeedsEmitter.renderMerge` retired the StringBuilder construction. **6 LINT-ALLOWs retired** at the MERGE site (chapter 4.1.B slice α/β shipped with them; Tier-1 #1 is the cash-out). The change-detection predicate is now a typed `BooleanBinaryExpression(Or)` of `BooleanComparisonExpression(NotEqualToBrackets)` + `BooleanIsNullExpression` AST wrapped in `BooleanParenthesisExpression`.
- **#2 — `Compose.Outputs.Sql : string` → `SsdtBundle : Map<RelativePath, string>`** (`705e31d`). Production-shape per-table file map. Pipeline.write iterates the bundle; Deploy.runEphemeral consumes `Compose.aggregateSsdt` (the `\nGO\n`-joined per-.sql convenience). Chapter-3 single-blob retires.
- **#3 — `Compose.Outputs.Json + .Distributions : string` → `JsonNode`** (`22ecc59`). Typed at the Outputs seam; consumers query the typed tree. Chapter 3.7 slice ε's per-kind typed JsonNode lifted to the Outputs seam. Pillar 1 holds end-to-end across Pipeline composition.

### Tier-3 codification — text-builder-as-first-instinct discipline (third named failure mode)

`23d9d5d`. Substantive DECISIONS entry (~120 lines) + Active deferrals index entries + AGENTS.md + CLAUDE.md operating-disciplines table.

- **Named failure mode**: **text-builder-as-first-instinct** — the agent reaches for StringBuilder as the default for new emitters, then attaches LINT-ALLOWs once the lint surfaces. Each LINT-ALLOW is individually defensible per the substantive-rationale discipline; the AGGREGATE is the bug. Six LINT-ALLOWs at one MERGE site means the typed-AST migration was skipped at construction time. Sibling failure mode to **performance-of-compliance** (chapter 3.7 slice β'' — the LINT-ALLOW shaped like an audit trail without substance) and **domain-blind naming** (chapter 3.7 slice β''' — the name shaped like a placeholder for an absent domain concept).
- **4-step protocol**: (1) articulate the typed-AST library BEFORE the first StringBuilder; (2) cross-check the precedent emitters; (3) first draft uses the typed AST; (4) LINT-ALLOWs at terminal text boundaries only.
- **Two hard-requirement Active deferrals** added to the index for chapter open:
  - **Microsoft.SqlServer.Dac (DacFx) adoption in `Projection.Targets.SSDT.DacpacEmitter`** — chapter 3.x. Hard requirement: the .dacpac ZIP+XML format MUST flow through DacFx.
  - **MigrationDependenciesEmitter + BootstrapEmitter typed-AST adoption from slice α** — chapter 4.1.B slices ε/ζ. Hard requirement: `ScriptDomBuild.buildMergeStatement` precedent. The chapter-close ritual scans the Active deferrals table; future agents at chapter open MUST read these entries.

### Docker probe + verify-before-diagnose discipline (fourth named failure mode)

`6ec4a64` + `b56f558`.

- **`Deploy.Docker.ensureRunning` memoized**; `BringupBudgetMs` lowered 30s→5s. Worst-case suite cost when Docker is down: collapsed `N×30s` (~7.5 min for N=15 canary tests) → 5s (one probe, cached).
- **PreToolUse hook** `.claude/hooks/docker-probe.sh` auto-fires before infra-relevant Bash commands (matches `dotnet test` / `docker *` / `*canary*` / `*Canary*` / `*Testcontainers*` / `*sqlcmd*` / `*mssql*`); reports current Docker state + last session-start hook line via `additionalContext`. NEVER blocks; only informs.
- **Named failure mode**: **infrastructure-blame jumping** — the agent jumps to "X infrastructure is unavailable" without running the cheap verification probe. Codified in AGENTS.md (root) "Verify-before-diagnose for infrastructure" subsection.

### Outstanding queue (post-this-session)

**Highest-leverage:** chapter close ritual for 3.6 + 3.7 + 4.1.A + 4.1.B-in-flight + RawTextEmitter retirement + Tier 1/2/3 (eight items per CLAUDE.md operating-disciplines table; catches cross-cutting drift).

**Independent forward-progress:**
- R4 multi-environment promotion property test (uses M4 Tolerance taxonomy; ~150 LOC; no new chapter open).
- Chapter 3.2 SnapshotRowsets pre-scope review (unblocks 4.1.A slices 6/7/8 + 4.1.B downstream).
- Chapter 4.1.B slice δ (two-phase insertion / cycle-breaking).

**Hard-requirement Active deferrals (read at chapter open):**
- Chapter 3.x DacpacEmitter — DacFx adoption mandatory.
- Chapter 4.1.B slices ε/ζ (MigrationDeps + Bootstrap) — `ScriptDomBuild.buildMergeStatement` adoption mandatory.

**Chapter close ritual deferred for all of:** 3.6, 3.7, 4.1.A, 4.1.B-in-flight, RawTextEmitter retirement, Tier 1/2/3 transitions. Joint pass is the natural next move.

---

## Chapter 3.7 prologue (added 2026-05-10; in flight, audit-cleanup hygiene)

**Branch:** `claude/review-ddl-exporter-ilV0k`. **Test baseline:** 790 passing, 0 skipped, 0 build warnings under `TreatWarningsAsErrors=true`. **Lint:** clean across 27 rules (Rule 27 added this chapter; see below). **Perf-gate:** clean.

Chapter 3.7 is a **B&W audit-cleanup hygiene chapter** picking up Tier-1 / Tier-2 / Tier-3 audit findings still open at chapter-3.6 close. Substantive deliverables shipped (load-bearing):

- **Slice α — `Lineage.Trail [<CustomEquality>]` (A26 cash-out)**. Audit Tier-2 #12. `Lineage<'a>` projects equality through `Value` only; trails are metadata not in equality. `Lineage.byValue` / `Lineage.byValueAndTrail` helpers expose the explicit projections. Monad-laws property tests + operator tests strengthened to `byValueAndTrail`. Two new property/Fact tests cash out A26 directly. Pass / PassWithDiagnostics aliases inherit the `'output : equality` constraint. +30 LOC.
- **Slice β — `Projection.Core.SqlTypeCorrespondence` bounded context (Tier-1 #8 cash-out)**. The forward / inverse PrimitiveType ↔ SQL DDL vocabulary pair previously split across `Projection.Targets.SSDT.Render.columnSqlType` (forward) and `Projection.Adapters.Sql.ReadSide.mapSqlType` (inverse) is consolidated into one closed-DU dispatch surface in Core. Round-trip property + 25 InlineData theory + Fact + property test sweep the recognized SQL Server alias vocabulary. ReadSide.mapSqlType becomes a 1-line alias.
- **Slice β' — `Render.columnSqlType` through ScriptDom typed AST (pillar 7 cash-out)**. Slice-β shipped with four `String.Concat` LINT-ALLOWs in Render that named the boundary without naming the considered alternative — *performance-of-compliance* (the named failure mode; see below). Slice-β' lifted `ScriptDomBuild.dataTypeReference` from `private` to public, added `ScriptDomGenerate.generateDataType : DataTypeReference -> string`, made `Render.columnSqlType` delegate. Output byte-identical (790 tests still green); four LINT-ALLOWs retired; two private helpers retired (`sqlTypeWithLength`, `sqlDecimal`); one unused import retired (`open System.Globalization`). Per-column generator instantiation surfaces via bench label `scriptDom.generateDataType` (perf-gate clean).
- **Slice β'' — LINT-ALLOW substantive-rationale discipline codification**. `DECISIONS 2026-05-10` codifies the four-question analysis as the structural prerequisite for any per-line `LINT-ALLOW` marker on a string-composition / built-in-substitute site. Names the failure mode **performance-of-compliance** (a marker shaped like an audit trail without the substance). Updates: pillar 7 amendment in DECISIONS.md supreme operating discipline section; new operating-disciplines table row in CLAUDE.md; expanded LINT-ALLOW guidance in root AGENTS.md; new sub-bullet in KICKOFF.md supreme-discipline section; new decision tree "When you reach for a string-composition primitive" in PLAYBOOK.md; lint Rule 27 added (per-line concat-aversion LINT-ALLOW inventory + soft floor).
- **Slice ε — Json + Distributions Π typed per-kind JsonNode (audit Tier-1 #7; pillar 1 cash-out)**. `JsonEmitter.emitSlices : Emitter<JsonNode>` (was `Emitter<string>`); `DistributionsEmitter.emitSlices : EmitterWithProfile<JsonNode>` (was `EmitterWithProfile<string>`). Internal serialization path is BCL-typed end-to-end (`Utf8JsonWriter` → `MemoryStream` → `byte[]` → `JsonNode.Parse(ReadOnlySpan<byte>)`); no managed `string` materializes at the per-kind seam. The doc composer's prior `JsonNode.Parse(kindText)` re-parse retires; typed `JsonNode` writes through the indented document writer via `node.WriteTo(writer)`. Added 4 new contract tests in `SiblingEmitterContractTests.fs` (renamed from `T11TypeTheoremTests.fs` — see slice β''' below). T11 fully structural at BOTH axes (keyset + per-kind value type). 794 passing tests.
- **Slice β''' — Domain-first naming discipline codification (pillar 8)**. `DECISIONS 2026-05-10 — Domain-first naming and ubiquitous-language consistency` codifies the four-question domain-naming analysis as the structural prerequisite for any named type / function / file / module / test in V2. Names the failure mode **domain-blind naming** (a name shaped like a placeholder for the absent domain concept). No lint enforcement (heuristic syntactic checks misfire on legitimate uses; the discipline-document path catches what the heuristic can't). Updates: pillar 8 added to DECISIONS.md supreme operating discipline; new top-row in CLAUDE.md operating-disciplines table; pillar 8 added to root AGENTS.md supreme-discipline summary; new pillar 8 paragraph in KICKOFF.md supreme-discipline section; new decision tree "When you reach for a name" in PLAYBOOK.md (with worked-precedents table + worked-anti-patterns table). Worked rename: `T11TypeTheoremTests.fs` → `SiblingEmitterContractTests.fs` (concept-shaped name names what the file IS, not which theorem ID it cites).
- **Docker hook canary-readiness fix**. `session-start.sh` now writes a comprehensive subsystem-status line at end-of-hook (`session-start <READY|DEGRADED|FAIL> <utc> | dotnet=<v> | docker=<state> | image=<state> | warm=<state>`) so agents reading `$HOME/.claude-projection-hook-status` see the FULL canary-readiness picture, not just dotnet. The dotnet-only intermediate status line retired (a fresh agent reading "session-start OK" without seeing the comprehensive verdict mistook the dotnet OK for full canary readiness). Stable subsystem vocabulary (running / cached / ready / missing / failed / not-ready / skipped) makes the file greppable. AGENTS.md "Pre-flight & Alignment" documents the new format + recovery path (re-run `bash .claude/hooks/session-start.sh`; idempotent).
- **V2-driver as destination KPI codification (principal-PO sidebar)**. `V2_DRIVER.md` (new standalone canonical surface) codifies V2-driver as the project's north star, supersedes the implicit "V2-augmented as floor; V2-driver as aspirational" framing in `DECISIONS 2026-05-22 — R6`, AND absorbs the operative backlog (supersedes `BACKLOG.md` which is now a forwarding pointer). The KPI in one sentence: V2 reaches V2-driver mode for the cutover by being provably correct on every axis V2 owns (schema, data, identity, diagnostics, and any future sibling), with provable correctness defined as structural-type-level enforcement plus per-axis property tests. **CDC silence on idempotent redeploy (chapter 4.1.B) is the highest-leverage single deliverable.** Chapters 4.1.B / 4.2 / 4.3 / 3.x / 3.2 are critical-path under V2-driver KPI, not optional. Updates: new top-row in CLAUDE.md operating-disciplines table; new V2-driver paragraph in KICKOFF.md supreme-discipline section; KICKOFF.md strategic-surfaces table now points at `V2_DRIVER.md` (was `BACKLOG.md`); CLAUDE.md reading-order section + VISION.md companion-strategic-surfaces both updated to reference `V2_DRIVER.md`; new DECISIONS entry `2026-05-10 — V2-driver as destination KPI`. The `V2_DRIVER.md` "Executive backlog summary" table is the chapter-by-chapter sequencing under this KPI; per-chapter operational detail continues to live in `CHAPTER_*_PRESCOPE_*.md`.

**Chapter-3.7 slice queue** (in user-preferred order; ε now shipped per the chapter-4.1.A close arc):
- ✅ **α** — `Lineage.Trail [<CustomEquality>]` (A26 cash-out). Shipped at `1f8a617`.
- ✅ **β** — `Projection.Core.SqlTypeCorrespondence` bounded context (Tier-1 #8 cash-out). Shipped.
- ✅ **β'** — `Render.columnSqlType` through ScriptDom typed AST. Shipped.
- ✅ **β''** — LINT-ALLOW substantive-rationale discipline codification. Shipped.
- ✅ **ε** — Json + Distributions Π typed per-kind `JsonNode` (audit Tier-1 #7; T11 fully structural; pillar 1). **Shipped** at chapter-4.1.A close arc (per HANDOFF prologue line 105). T11 is now structural at both axes (keyset + per-kind value type).
- ✅ **β'''** — Domain-first naming discipline codification (pillar 8). Shipped.

**Outstanding chapter-3.7 slice queue:**
- **γ** — `traverseCatalog` natural-transformation primitive (audit Tier-3 #23; FP composition).
- **ζ** — Three `attach` adapters take string JSON → SnapshotSource-shaped (audit Tier-1 #6).
- **η** — `result {}` CE adoption at `ReadSide.fs:540-690` (audit Tier-3 #24).
- **θ** — Coordinates Stage 2 typed `SchemaName` / `TableName` / `ColumnName` VOs (audit Tier-3 #20a).
- **ι** — Lineage / Diagnostics writer-monad codification refresh (audit Tier-2 #18 + #19).
- **κ** — `Lineage.tell` `m.Trail @ [event]` O(N²) audit (perf-class question).
- **λ** — `SsKey.rootOriginal` V1 prefix in emitter output (audit Tier-1 #11; needs DECISIONS amendment first).
- **μ** — `Restrict→NoActionSql` Diagnostics scaffolding (audit Tier-1 #10 + Tier-2 #15).
- **ν** — F# Analyzers SDK custom analyzer (KICKOFF deferral #1; complements 27 grep rules with AST detection). **Note:** consolidates the historical chapter-3.6 slice χ "F# Analyzers SDK" deferral — same item, single queue location going forward.
- **ξ / ο / π** — Port lifts (`ICatalogReader` / `IArtifactSink` / `IDeployHost`); ξ stayed deferred at chapter 3.2 close per the variant-vs-source distinction (`CHAPTER_3_2_OPEN.md` axis 6); will fire when a true second catalog source (DACPAC / OData / in-memory) materializes — likely chapter 3.x DacpacEmitter open.

**Chapter close ritual: substantively discharged at chapter-4.1.A close arc + chapter-3 cross-cutting close (2026-05-10);** see `CHAPTER_4_1_A_CLOSE.md` (joint coverage of 3.6/3.7/4.1.A/4.1.B-α/β/γ) + `CHAPTER_3_2_CLOSE.md` (chapter 3.2 specific) + `CHAPTER_3_5_CLOSE.md` (chapter 3.5 retroactive close).

---

## Chapter 3.6 prologue (added 2026-05-09; substantive close, ritual deferred)

**Branch:** `claude/review-ddl-exporter-EH1lh`. **Test baseline:** 757 passing, 0 skipped, 0 build warnings under `TreatWarningsAsErrors=true`. **Lint:** clean across 26 rules. **Perf-gate:** clean against `bench/baseline-canary.json` baseline.

Chapter 3.6 closed five of the six chapter-3.5-close-deferred items (KICKOFF.md table) plus a comprehensive brittleness audit + library-API audit + Result migration. **Substantive deliverables shipped (load-bearing):**
- **Pillar 6 codified** (no V2-internal back-compat paths). Cashed out `SsKey.original` parser-shim, `SsKey.derived` aliasing forwarder, `LEGACY` source marker, CLI bare-positional-args back-compat. OSSYS adapter flows through `Module.create` / `Catalog.create`.
- **Pillar 7 codified** (gold-standard library precedence + perf-clause). Every refactor SHALL cite perf implications; every hot-path function has `Bench.scope`; every loop flows through `Bench` iterators; every counter via `Bench.recordSample`.
- **`LineageEvent.Removed of RemovalReason` + `Annotated of AnnotationDetail`**: typed-payload widening across 5 producer pass drivers + SymmetricClosure.
- **`SsKey.Synthesized of basisParts: string list`**: typed segments through the DU; `String.concat "_"` survives only at terminal `rootOriginal`.
- **`RawValueCodec`**: V2's canonical raw-value format contract; consolidates Render + Bulk + ReadSide.
- **`ConnectionString.parse`**: typed `SqlConnectionStringBuilder` validation.
- **`BatchSplitter` strategy**: `TSql160Parser` gold-standard with line-fold loud-fallback for `Deploy.executeBatch`.
- **`EmissionPolicy.create`**: A39 invariant.
- **`CatalogTraversal.mapKinds` primitive**: extracted from VisibilityMask + NormalizeStaticPopulations.
- **`BenchSink` port**: `Bench.persistJson` extracted from Core (audit Tier-1 #1).
- **Statistical perf-gate** (`scripts/perf-gate.sh`) + **pre-commit hook** + **Stop hook** (`hookSpecificOutput.additionalContext`): per-label `μ + Kσ` outlier detection across rolling history (N=20); soft-skip on missing Docker/dotnet.
- **`Result<'a>` aliased to `Microsoft.FSharp.Core.Result<'a, ValidationError list>`**: custom DU + `result {}` builder retired. **FsToolkit.ErrorHandling 4.18.0** adopted; `result {}` / `taskResult {}` / `validation {}` CEs now native. `DiagnosticSeverity` qualified.
- **ScriptDom expansion** at boundary sites (`createDatabaseSql`, `readRowsStream`, `readRows.SELECT COUNT`): single source of truth for SQL identifier quoting via `Identifier.EncodeIdentifier`.
- **Bench coverage at every pass entry** (10 passes; iterator-logging-as-first-class-outcome discipline).

**Sole 3.6-deferral remaining:**
- **Slice χ — F# Analyzers SDK custom analyzer** (KICKOFF deferral #1; standalone). **Consolidated with chapter-3.7 slice ν** at the chapter-3 cross-cutting close (2026-05-10) — same item, single queue location going forward (chapter-3.7 slice ν is the canonical reference). Re-open trigger: false-negative surfaces in CI.

**3.6 chapter-close ritual** discharged jointly at chapter-4.1.A close arc + chapter-3 cross-cutting close (2026-05-10). See `CHAPTER_4_1_A_CLOSE.md` "Chapter close ritual — eight items walked".

**Forward signals into 3.7+ / 4.x** (recorded for future agents at `DECISIONS.md` 2026-05-09 chapter-3.6 audit-findings entry): DacFx adoption (chapter 3.x DacpacEmitter — primitives named: `TSqlModel`, `DacPackage`, `DacServices.GenerateDeployScript`, `SchemaComparison`, `DacDeployOptions`), SqlBatch (when SqlClient ≥ 5.5 + canary bottleneck), `SqlConnection.RetryLogicProvider` (canary CI flake), `AsyncSeq` (when streaming readside needs `bufferByCountAndTime`), `JsonObject` typed per-kind (when 2nd `ArtifactByKind<string>` consumer fires), Argu CLI (when CLI grows beyond 3 commands), Verify.XUnit (DacpacEmitter golden-rotation pressure), `Microsoft.Extensions.Logging` (CI structured-logs consumer demand), `Utf8JsonReader` (bench surfaces JSON parse time at scale), incremental `validation {}` adoption at `CatalogReader.parseAttribute / parseKind` for ~80 LoC reduction + better error aggregation, incremental `taskResult {}` adoption at `Deploy.runWideCanaryWithLoader` for ~40 LoC reduction.

**Pillar 7 perf-clause practice**: agents SHALL cite perf implications in every commit message and SHALL identify the perf class before committing (zero / O(1) / O(N) / O(N log N) / O(N²) — with the scaling axis).

---

## Where you are

You have inherited three closed chapters (1, 2, 3.1) of the V2 sidecar. The most recent close — chapter 3.1, the canary chapter — accumulated through sessions 27 → 36. Chapter 3.1's substantive synthesis is at `CHAPTER_3_1_CLOSE.md`; the chapter's epistemic capstone is `AUDIT_2026_05_DDD_HEXAGONAL_FP.md` (the five-agent DDD/Hexagonal/FP audit).

The codebase builds; **all 713 tests pass (697 non-canary + 16 canary)**; the canary's round-trip property holds across the 300-table forcing-function fixture and at 500k rows on the bulk path (27s warm).

You are not starting from scratch. You are continuing a multi-chapter arc whose accumulated judgment is partly in the canonical documents (`AXIOMS.md`, `DECISIONS.md`, `ADMIRE.md`) and partly in `CHAPTER_1_CLOSE.md` / `CHAPTER_2_CLOSE.md` / `CHAPTER_3_1_CLOSE.md` / `AUDIT_2026_05_DDD_HEXAGONAL_FP.md` next to this letter.

## What to read, in order

1. **`CLAUDE.md`** — the navigation surface. Indexes the canonical documents and lists the operating disciplines. Start here.
2. **`HANDOFF.md`** (this file) — the bridge between what chapters 1+2+3.1 know and what you need to know.
3. **`CHAPTER_3_1_CLOSE.md`** — chapter-3.1 close synthesis. Sections of immediate relevance:
   - The chapter-3.1 arc summary (M1–M3 milestone sequence; forcing-function scaling; data plane + at-scale + audit).
   - The four meta-codifications (bench-driven optimization, stream-realization pattern, five-agent epistemic-tier audit, harmonization-via-parameterization).
   - The forward signals into chapter 3.2 / 3.5 / 4.1 / 4.2.
4. **`AUDIT_2026_05_DDD_HEXAGONAL_FP.md`** — the chapter-3.1 close audit. Tier 1 / 2 / 3 / 4 backlog organized by epistemic level (B&W vs SUBJ) and leverage (H/M/L). ~30 findings; 10 acted on at session 36; ~20 routed to named sub-chapters.
5. **`CHAPTER_2_CLOSE.md`** — chapter-2 close synthesis. Read for OSSYS adapter context (25 translation rules, three-class typology, V1-input-envelope walk).
6. **`CHAPTER_1_CLOSE.md`** — historical context. Some priorities resolved by chapter 2 / 3.1; disciplines and load-bearing commitments persist.
7. **`AXIOMS.md`** — the algebra. A1–A40 with V2 amendments appended. **Note:** A35 / A36 cashed at session 34; A37–A40 candidates added at chapter 3.1 close (Coordinates VO, aggregate constructors, harmonization-via-parameterization, writer-fidelity).
8. **`DECISIONS.md`** — chronological operating discipline. Long. Read the most recent ten entries first; chapter-3.1-close entries cluster at the bottom.
9. **`ADMIRE.md`** — V1↔V2 bridge. `EntityDependencySorter` advanced to (advanced — consumed via `TopologicalOrderPass.runWith`); `EntitySeedDeterminizer` (advanced — `StaticRow.Values` raw IR contract closes the loop).
10. **The code.** `Projection.sln`. Strategies in `src/Projection.Core/Strategies/`; passes in `src/Projection.Core/Passes/`; sibling Π emitters in `src/Projection.Targets.{SSDT,Json,Distributions}/`; F# adapters in `src/Projection.Adapters.{Sql,Osm}/`; the canary in `src/Projection.Pipeline/`. Chapter 3.1's substantive deliverables: `Statement.fs` / `Render.fs` / `RawTextEmitter.fs` (typed Π output); `Bulk.fs` / `Deploy.fs` (bulk realization); `AsyncStream.fs` / `ReadSide.fs:readRowsStream` (streaming readside); `Coordinates.fs` (`TableId` value object); `PhysicalSchema.fs` (four-axis fidelity surface).

## What's load-bearing

These commitments are not negotiable without explicit DECISIONS entries amending them. If you find yourself wanting to break one, write the amendment first.

- **F#-pure-core / no-I/O-in-Core.** `Projection.Core` has zero I/O. Audited clean (`CHAPTER_1_CLOSE.md §1.1`); confirmed across chapters 2, 3.1. Chapter 3.1 audit Agent 2 #1 flagged `Bench.persistJson` as an outstanding violation; ⏸ deferred (rolls forward as `BenchSink` port extraction).
- **A18 amended.** Π consumes whichever subset of `Catalog × Profile` it needs, but never `Policy`. Catalog and Profile are *evidence*; Policy is *intent*. If you reach for Policy from inside an emitter, you are in the wrong layer — the work belongs in a pass.
- **A35 (chapter-3.1 contribution).** Π's output is a deterministic *statement stream*, not a string. Realization layers (`Render.toText`, `Deploy.executeStream`) consume the stream and choose their emission form. Bulk-vs-incremental deploy is realization-layer policy invisible to Π.
- **A36 (chapter-3.1 contribution).** Bulk-vs-incremental is realization-layer policy. The algebra (A18, T1, T11) holds at the stream level; how a realization deploys is its own concern.
- **Strategy-layer codification (`DECISIONS 2026-05-11`).** Pure functions of IR fields; typed function-type seam (`StrategyEvaluator<'context, 'config, 'decision>`); structured rationale DUs; lineage events on actual decisions; module name advertises domain (`<Domain>Rules` suffix); total decisions with named skips.
- **`Composition.fanOut` for registered-intervention pass drivers.** All pass drivers delegate to it.
- **Closed-DU expansion empirical-test discipline.** Adding a DU variant should produce F# exhaustiveness errors only at match sites; no caller reshaping outside the variant's module.
- **Decimal as default for continuous statistical evidence.** T1 byte-determinism requires it.
- **Sibling-Π commutativity (T11).** Every Π's output should mention every catalog kind by SsKey root. **Note:** T11 is currently *aspirational* not structural — three Π's return `string`. Chapter 3.5's Π port realization makes T11 structural via the typed `ArtifactByKind<'element>` surface.
- **Pass return-type codification.** Passes return `Lineage<'output>` for decisions only; `Lineage<Diagnostics<'output>>` when producing decisions plus observer-relevant findings. Chapter 3.1's writer-fidelity codification adds: pass drivers MUST use `LineageDiagnostics.tellDiagnostics` / `Lineage.ofValueAndEvents` (the canonical primitives); manual record-building is forbidden.
- **Three-class typology for V1↔V2 translation findings (chapter-2 contribution).** Lossiness / boundary-discipline / alternative-IR-surface. Chapter 3.2 is a translation chapter — operate the typology.
- **Five-agent epistemic-tier audit protocol (chapter-3.1 contribution).** Multi-agent parallel audit at chapter close; convergence map as primary synthesis surface. Tier 1/2/3/4 backlog organizes findings by epistemic level + leverage.
- **Harmonization-via-parameterization (chapter-3.1 contribution).** When two implementations diverge on a single axis, parameterize the algorithm on that axis. Worked example: `SelfLoopPolicy` in `TopologicalOrderPass`.

## What's deferred but might fire under your work

The Active deferrals index at the top of `DECISIONS.md` is the canonical surface; chapter-mid-audits and chapter-close ritual scan it. If your work surfaces the cash-out trigger, log a DECISIONS entry — don't quietly resolve the deferral.

**Chapter-3.5-likely fires:**
- **Π port realization** — three emitters return `string`; `Emitter<'element>` declared but unrealized. Chapter 3.5 RefactorLog is the natural new consumer that earns the typed structured output. Cross-cutting blocker for T11 structural-type encoding.
- **`SsKey.rootOriginal` V1 prefix in emitter output** — needs a DECISIONS amendment first to supersede the chapter-3 pre-scope §3 commitment that the source-prefix form is the stable identifier.
- **`Restrict → NoActionSql` collapse explicit** — paired with a Diagnostics-emission scaffolding for `CatalogReader`. Chapter 3.5 RefactorLog needs this for round-trip-fidelity diff.

**Chapter-3.2-likely fires:**
- **`SnapshotRowsets` variant of `SnapshotSource`** — closes the JSON-projection-lossiness class. Pre-scope at `CHAPTER_3_PRESCOPE_SNAPSHOT_ROWSETS.md`.
- **`ICatalogReader` port** — Position B trigger has fired (two consumers: `Osm.CatalogReader.parse` + `Sql.ReadSide.read`). Chapter 3.2 lifts the surface.
- **Three `attach` adapters take `string` JSON** — should mirror `SnapshotSource` shape. Hidden ports.
- **Silent V1 drops without Diagnostics** — chapter 3.2 adds the Diagnostics-emission scaffolding to `CatalogReader`.

**Chapter-4.1-likely fires:**
- **Type-correspondence module** — owns the 5 inverse functions (`mapSqlType` ↔ `columnSqlType`; `formatRawValue` ↔ `formatSqlLiteral`; `parseRaw` + `clrType`). T1 byte-determinism currently rests on conventional inversion across 3 projects. Chapter 4.1's data triumvirate (StaticSeedsEmitter / MigrationDependenciesEmitter / BootstrapEmitter) is where this lift earns its place.
- **`Bulk` lives in `Pipeline`** but is structurally `Adapters.Sql` concern. Move at chapter 4.1 open.
- **`IDeployHost` port** — wraps Testcontainers + warm-conn + executeStream. Chapter 4.1's data emitters need this seam to swap between live SQL and ephemeral container.
- **Streaming digest cash-out** — `RowDigester` / `PhysicalRowDigest` shipped at chapter 3.1 as scaffolding; chapter 4.1's data triumvirate uses them at scale.

**Chapter-4.2-likely fires:**
- **Identity DU refactor** — `OssysOriginal` / `V1Mapped` parameterized on `SourceTag` value object. The `V1Mapped` variant is reserved for cross-source identity threading; today unreachable from production. Chapter 4.2's User FK reflow makes it reachable.

**Cross-cutting cleanup (any chapter):**
- **`Bench.persistJson` writes from Core** — `BenchSink` port; Bench out of Core.
- **`IArtifactSink` port** — `Compose.write` + `Bench.persistJson` reach `File.WriteAllText` directly.
- **`Lineage.Trail` `[<CustomEquality>]`** — A26 documented but not enforced (default `=` compares Trail).
- **Typed `SchemaName` / `TableName` / `ColumnName` VOs** — Stage 2 of Coordinates.
- **`traverseCatalog` natural-transformation primitive** — 4 consumers hand-rolling mutable `ResizeArray<LineageEvent>` traversals.
- **`result { ... }` computation expression** — `ReadSide.fs:540–690` chains 4–5 deep, beyond the codebase's "bearable three steps" mark.

**Lower-priority (watch for accidental fires):**
- **Composition primitives `fallback`, `accumulate`, `wrap`, `lift`** — zero current consumers each.
- **Strategy registry mechanism** — N=5 strategies; threshold N≥4–6 plus a real consumer demanding name-keyed lookup.
- **Three-channel Diagnostics split** — single channel sufficient at all chapter-2/3.1 consumers.
- **Faker emitter** — gates on third evidence type.

## What you should not do

The accumulated judgment from sessions 1–36 includes specific don'ts:

- **Don't strip "dead code" without checking docstrings.** `ForeignKeyRules.isIgnoreRule` always returns false; `ForeignKeyKeepReason.CrossCatalogBlocked` is reserved-unreachable; `Origin.ExternalDirect` is unreachable from OSSYS today (chapter 4 SnapshotRowsets / DACPAC may reach it). All intentional.
- **Don't delete the OSSYS adapter's deferred `SnapshotSource` variants.** `SnapshotRowsets` and `LiveOssysConnection` are reserved DU variants with explicit re-open triggers. They appear unused until chapter 3.2+; do not delete.
- **Don't treat `RawTextEmitter` as an SSDT replacement.** It is a debug/diff-oracle synthetic-milestone form. DacpacEmitter (chapter 3.x) is the additive sibling, not a replacement.
- **Don't extract speculative composition primitives.** Two-consumer threshold; refined by anticipation-vs-speculation (`DECISIONS 2026-05-13`); refined again by `opportunityEntry` shape-distinction analysis (`DECISIONS 2026-05-14`).
- **Don't reach for Policy from a Π.** A18 amended forbids it.
- **Don't bypass the writer's API.** `LineageDiagnostics.tellDiagnostics` / `Lineage.ofValueAndEvents` are the canonical pass-driver primitives. Manual record-building is forbidden.
- **Don't cash out Active deferrals silently.** The DacFx trigger fired silently across sessions 18–22 and was caught only at session-23 chapter-mid-audit. The lesson is structural: **the index exists so it doesn't recur**.
- **Don't open new substantive slices without classifying the finding into the three-class typology first.** Trace-before-fixture; classify; resolution shape follows.
- **Don't build ports without consumer pressure.** The session-36 audit identified ports declared in Core (`Emitter`, `Compare`, `Render`) that are unrealized. The fix isn't to add more declarations — it's either to *realize* with a real consumer (chapter 3.5 Π port) or to *retire* the declaration (the `Adapter` alias retired at session 36 with no consumer). Closed-DU expansion empirical-test discipline applies to ports too.
- **Don't overwrite this file.** When chapter 3.2 / 3.5 / 4.x closes, this letter becomes `HANDOFF_CHAPTER_3_1.md` and you write the new outgoing letter as `HANDOFF.md`. Append-only documentation discipline.

## Disposition

The dispositions chapter 3.1 inherited from chapters 1–2 hold; chapter 3.1 added these:

- **Bench-driven optimization protocol.** Three-candidate / 2-refuted / 1-confirmed shape; refuted swaps documented with bench data so the same swap doesn't recur.
- **Stream-realization pattern.** Π's typed output stream + realization layers as sibling consumers. Same algebra; multiple realizations.
- **Five-agent epistemic-tier audit at chapter close.** Multi-agent parallel; convergence-map as primary surface; Tier 1/2/3/4 backlog discipline.
- **Harmonization-via-parameterization.** Single-axis-divergent implementations earn one parameterized algorithm.
- **Writer-fidelity discipline.** `LineageDiagnostics.tellDiagnostics` / `Lineage.ofValueAndEvents` canonical; manual record-building forbidden.
- **Audits roll forward as named sub-chapters.** Chapter 3.1's audit (Tier-1 / Tier-2 / Tier-3) didn't dump 30 findings into the next chapter — it routed each finding to the natural sub-chapter (3.2 / 3.5 / 4.1 / 4.2) with explicit pre-scope alignment. The discipline: audit findings are routed, not piled.

## Where to start

Per `CHAPTER_3_1_CLOSE.md`, the chapter-3.2 / 3.5 / 4.1 priorities split based on what unlocks T-30 first (per `DECISIONS 2026-05-22 — T-30 / T-15 cutover fallback ladder gates`):

1. **Read this letter, CLAUDE.md, CHAPTER_3_1_CLOSE.md, AUDIT_2026_05_DDD_HEXAGONAL_FP.md, and the recent DECISIONS entries** — orient. ~45 minutes.

2. **Decide chapter sequencing.** The four plausible next chapters:
   - **Chapter 3.2 — `SnapshotRowsets` adapter.** Closes the JSON-projection-lossiness class. Smaller scope. Pre-scope at `CHAPTER_3_PRESCOPE_SNAPSHOT_ROWSETS.md`. Lifts `ICatalogReader` port (Position B trigger fired).
   - **Chapter 3.5 — Π port realization + RefactorLog / CatalogDiff.** Largest leverage. Realizes the declared `Emitter<'element>` shape; unblocks T11 structural-type encoding. Pre-scope at `CHAPTER_3_PRESCOPE_REFACTORLOG_AND_CATALOG_DIFF.md`. Pairs naturally with the audit-deferred Π-port-realization.
   - **Chapter 3.x — DacpacEmitter.** Re-deferred at chapter-2 close. Inherits chapter-3.5's structured-output pattern. Pre-scope at `CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md`. Eight risks/open-questions still open.
   - **Chapter 4.1 — Data triumvirate.** Pre-scope at `CHAPTER_4_PRESCOPE_DATA_TRIUMVIRATE.md`. Inherits `Bulk` / `RowDigester` / `AsyncStream` from chapter 3.1.

   The pre-scope documents are still current. Subagent #4's (DacpacEmitter) and subagent #5's (SnapshotRowsets) recommendations from chapter-2 close hold; the chapter-3.1 audit sharpens them with the Π-port-realization framing.

3. **Open the chapter you choose** with a chapter-open document naming the strategic-frame axes (`DECISIONS 2026-05-15` shape; the OSSYS chapter is the worked example). Multi-session chapters earn this discipline at chapter open.

After the chapter-open scoping, the substantive work begins. Operate the chapter-mid-audit at every 3–5 substantive sessions; operate the chapter-close ritual at chapter close (eight items, including the V1-input-envelope walk for V1↔V2 translation chapters and the new five-agent audit for architectural-frame chapters).

## Closing

You inherit a codebase whose architectural disciplines hold under audit (the chapter-3.1 audit was the strongest one yet — five agents, ~30 findings, codified as `AUDIT_2026_05_DDD_HEXAGONAL_FP.md`), whose codification is at multiple stability marks, whose audit disciplines have proven generative (each audit produces new disciplines), and whose canonical documents are honest about what's been done and what hasn't.

Chapter 3.1's distinctive intellectual artifact: **the canary as a load-bearing forcing function**. The 300-table fixture is the verification surface for the V1↔V2 round-trip property. Chapter 3.1's distinctive operational artifact: **typed Π output as a stream, with realization plurality**. Chapter 3.1's load-bearing structural innovation: **harmonization-via-parameterization** — single-axis-divergent implementations earn one parameterized algorithm.

The chapter you open is yours to shape. The disciplines above are not constraints; they are the load-bearing structure that lets the chapter ahead support more weight than the one behind. Hold the spine.

— The session 27–36 architect.
