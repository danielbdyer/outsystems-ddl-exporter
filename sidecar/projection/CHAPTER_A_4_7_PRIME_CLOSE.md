# Chapter A.4.7' (prime) CLOSE — Compose.run registry-traversal lands; registry becomes load-bearing for execution

**Status:** CLOSED 2026-05-17. Predecessor: chapter A.4.7 CLOSED 2026-05-16 (commit `bc9fedb`). Successor: TBD. Branch: `claude/review-chapter-close-VnRe8`. Ship commits: `d376ee0` (α) → `4f83325` (β) → `b5a515a` (γ) → `5b90fdf` (δ) → `908e50d` (ε) → `22f26b8` (ζ) → `c58fb11` (η.1 partial) → `11f03e8` (η complete) → this commit (θ).

**Test baseline at close:** 1262 / 1262 non-canary passing; 0 skipped; 0 build warnings under `TreatWarningsAsErrors=true`; lint count 13 (unchanged from chapter A.4.7 close baseline). Eight substantive slices shipped in one session-extended ship.

**A41 amended cashed.** Per `AXIOMS.md` "A41 amendment (execution totality) — chapter A.4.7' close (2026-05-17)". The amendment body fills the placeholder scaffolded at chapter open per the S0.F scaffolding discipline. L3-CC-Transform-Totality's underwriting tightens from metadata totality to metadata + execution totality (Bucket A holds; backing strengthens).

**5/5 bidirectional property tests** met the chapter exit gate. The chapter A.4.7 close handed off four tests (skeleton-purity at filter-shape; overlay-exercise; totality coverage; harvest-classification); this chapter adds the fifth (registry-digest round-trip) plus promotes skeleton-purity to true-execution.

---

## Per-slice ledger

| Slice | Cash | Commit | Δ Tests | Risk → Verdict |
|---|---|---|---|---|
| **α** | `Projection.Core/ComposeState.fs` (aggregate carrying Catalog + 6 decision-set Option fields + `initial` + 7 per-axis `withX` setters); `Projection.Core/PassChainAdapter.fs` (`liftCatalogPass`, `liftDecisionPass` lift constructors) | `d376ee0` | +6 | Low → green. Type-system surface established without registry population. |
| **β** | `Projection.Core/RegisteredTransforms.fs` ships at end of Core compile order. `RegisteredTransforms.all` (17 entries: 12 pass + 5 strategy); `RegisteredTransforms.allChainSteps` (12 PassChainAdapter entries via slice-α lift). Naming: `RegisteredTransforms` concept-shaped plural (the materialized collection); `TransformRegistry` retains type-level surface. Compile-order constraint resolved: TransformRegistry.fs precedes pass modules, so the populated list lives in a separate end-of-order file. | `4f83325` | +5 | Medium → green. Empty/identity defaults feed config-taking factories; metadata is invariant of config. |
| **γ** | `PassChainAdapter.compose : adapters → state → Lineage<Diagnostics<ComposeState>>` — folds adapter list via `LineageDiagnostics.bind`; both writer trails compose chronologically per A24. | `b5a515a` | +5 | Medium → green. Witness test ran `compose RegisteredTransforms.allChainSteps` end-to-end; all 12 decision-set fields populated cleanly. |
| **δ** | `Compose.project` migrated to invoke `PassChainAdapter.compose RegisteredTransforms.allChainSteps` before emitter fan-out; the hand-coded "emit raw catalog" shape retired; the registry is load-bearing. | `5b90fdf` | (no new tests) | High → green. Empirical bet on "Catalog-rewriting passes under empty/identity config are no-ops" paid off; zero byte-shifts across `SiblingEmitterContractTests`, `EndToEndPipelineTests`, `CanaryRoundTripTests`. |
| **ε** | `RegisteredTransforms.skeletonChainSteps` (4 entries; joined on Name against `TransformRegistry.skeletonView all`); `Compose.runSkeleton`; skeleton-purity bidirectional property test promoted from filter-shape (chapter A.4.7 slice θ) to true-execution (this slice). | `908e50d` | +3 | Medium → green. Empirical finding: `NamingMorphism` is in the skeleton (Sites classify as DataIntent; non-identity Morphism captured in factory-time config, not Site classification). |
| **ζ** | `TransformRegistry.digest : RegisteredTransformMetadata list → string` (deterministic SHA256 over sorted-by-Name metadata via explicit DU projection). `ManifestEmitter` extended with `RegistryDigest` field + `registry.digest` JSON output via `buildWith` taking explicit registry. `osm emit --skeleton-only` CLI flag wired. Fifth bidirectional property test (`RegistryDigestRoundTripTests.fs`): reproducibility + sensitivity + permutation-invariance + JSON round-trip. | `22f26b8` | +5 | Medium → green. 5/5 bidirectional property tests met. |
| **η.1** | VisibilityMask `let run` privatized; 18 sites migrated via per-test-file `vmRun` shim + 1 direct call rewrite in ClassificationCarryThroughTests. Partial slice committed as a recoverable checkpoint when the Stop hook fired mid-subagent. | `c58fb11` | (no new tests) | Validated: the playbook (per-test-file shape-restoring shim + bulk sed-replace) is mechanical and the build stays green per pass. |
| **η complete** | All 11 remaining passes privatized (`let private run` in CanonicalizeIdentity, NamingMorphism, NormalizeStaticPopulations, SymmetricClosure, TableRename, TopologicalOrderPass, NullabilityPass, UniqueIndexPass, ForeignKeyPass, CategoricalUniquenessPass, UserFkReflowPass). ~308 call sites migrated via per-test-file shims (`ciRun`, `nmRun`, `nspRun`, `scRun`, `trRun`, `topoRun`, `cuRun`, `nullRun`, `uiRun`, `fkRun`, `ufrRun`) + Pipeline.fs production site migrated directly with the new ValidationError boundary projection. Subagent-driven bulk migration with parent-agent ValidationError boundary fix. | `11f03e8` | (no new tests) | High → green. 39 files changed; 676 insertions, 302 deletions. Zero live `<Pass>.run` references remain. |
| **θ** | This commit. A41 amendment body filled. CHAPTER_A_4_7_PRIME_CLOSE.md (this file) ships. HANDOFF.md updated. Chapter-close ritual executed. | (this commit) | (no new tests) | Low → ritual + documentation. |

**Final test baseline:** 1262 / 1262 non-canary passing; 0 skipped; 0 build warnings; lint count 13.

**Slices added across the chapter:** 8 (α, β, γ, δ, ε, ζ, η, θ). Test additions: +24 across the chapter (6 + 5 + 5 + 0 + 3 + 5 + 0 + 0). Prior chapter A.4.7 close baseline at 1238 → A.4.7' close at 1262 + canary; net +24 substantive witnesses.

---

## Meta-codifications

Chapter close-time crystallizations that compound for the next chapter / next agent / next session.

### M1 — Multi-file compile-order constraint resolved at the assembly point, not the type seam

**Codified at slice β.** The constraint: `TransformRegistry.fs` declares the registry types and smart constructor; it compiles BEFORE any pass module (the registry types must be in scope when pass modules declare their `.registered` exports). The populated `all` / `allChainSteps` cannot live in `TransformRegistry.fs` because they require references to pass-module exports compiled later.

**Resolution:** the populated assembly lives in a separate Core-resident file (`RegisteredTransforms.fs`) at the END of the Core compile order, where every pass module's `.registered` is in scope. Naming via pillar 8: `RegisteredTransforms` (concept-shaped plural of `RegisteredTransform`) names the materialized collection; `TransformRegistry` retains the type-level + smart-constructor + filter-helper surface.

**Pattern available for re-use:** when a smart-constructor-bearing type lives early in compile order but consumers need a materialized collection that references later modules' exports, the assembly lives in a sibling file at the end of compile order, named in plural to distinguish from the type-level singular.

**Worked example:** the `RegisteredTransforms` name is the precedent. Sibling pattern candidates if future chapters generate them: `RegisteredAdapters` (for boundary-translation adapters), `RegisteredEmitters` (for emitter modules' fan-out registry).

### M2 — Empirical-bet method at refactor inflection points

**Codified at slice δ (the highest-risk slice).** When a refactor introduces structural changes that may or may not produce byte-shifts in existing test fixtures (e.g., introducing pass execution into the production emit path for the first time), the empirical-bet method is: ship the change with skeleton-friendly defaults; run the full non-canary suite; if byte-identical, accept; if not, audit per-pass behavior under empty config.

**The chapter's worked example:** wiring `compose RegisteredTransforms.allChainSteps` into `Compose.project` introduced the registry-fold for the first time at the production emit path. The chapter-open framed this as "highest-risk slice" because two passes (`NormalizeStaticPopulations` and `SymmetricClosure`) might mutate the Catalog under empty config. The empirical verdict: zero byte-shifts across 1255 tests.

**Pairing:** with the user-survey discipline (AskUserQuestion). When the chapter-open framing flags an inflection point as high-risk, surface the disposition options to the user before committing — empirical / minimal-shim / strict-subset / pause-and-audit. The user picks; the agent executes. This pattern is sibling to the chapter-mid-audit dispatch (`DECISIONS 2026-05-19` — multi-session chapters).

### M3 — Per-test-file shape-restoring shim is the canonical migration pattern for visibility-flip slices

**Codified at slice η.** When a public function is privatized in favor of a typed-registry surrogate (`Pass.run` → `(Pass.registered config).Run`), and the surrogate returns a different writer-shape than the original (e.g., `Lineage<Catalog>` → `Lineage<Diagnostics<Catalog>>`), the migration pattern is:

1. **Per-test-file shape-restoring shim** at the top of each consumer file:
   ```fsharp
   let private <pp>Run <args> : <original-shape> =
       (<Pass>.registered <config>).Run <catalog>
       |> Lineage.map (fun d -> d.Value)  // when the writer-shape differs
   ```
2. **Bulk sed-replace** at the call sites: `sed -i 's/<Pass>\.run /<pp>Run /g' <file>`.
3. **Production sites:** migrated directly with the new boundary projection (e.g., Diagnostics-Errors → ValidationError list in `Pipeline.fs:applyRenames`).

**The shims are test-private; not a module-exposed parallel surface.** The pass module's only public callable becomes `.registered.Run`; the test-file shims preserve existing assertion shapes (`result.Value`, `result.Trail`) without rewriting hundreds of tests structurally.

**Five shape categories** at this chapter's worked example: `Lineage<Catalog>` (5 passes), `Lineage<TopologicalOrder>` (1), `Lineage<CategoricalUniquenessDecisionSet>` (1), `Lineage<Diagnostics<_>>` (3, shape-compatible — no shim needed beyond pure rename), `Result<Lineage<Catalog>>` (1, Result wrapper requires boundary projection).

**Pairing:** with the subagent-mechanical-edits precedent (chapter A.0' XXXXL). Visibility-flip slices with N>50 call sites are subagent-eligible; the parent agent ships the playbook on one trial pass first, then dispatches the subagent for the bulk. Parent agent retains responsibility for production-site fixes (which require boundary-translation reasoning beyond mechanical edits).

### M4 — Stop-hook respect via partial-commit-with-explicit-deferral

**Codified at slice η.1 partial.** When a Stop hook fires mid-flight on a multi-step migration (subagent or otherwise), the recovery pattern is:

1. Identify the largest STABLE subset of changes (the slice that builds + tests cleanly).
2. Revert all in-flight changes outside that subset.
3. Commit the stable subset with explicit framing: "partial cash-out + deferred-with-trigger".
4. Document what remains and what trigger fires re-resumption (in the commit message + chapter close).

**Worked example:** slice η.1 partial commit (`c58fb11`) shipped VisibilityMask only after the subagent's in-flight work broke the build. The deferred-with-trigger entry sat in the commit message; the resumption in the same session (after the user's "do the remainder and then some") brought slice η to completion at `11f03e8`.

**Pattern available for re-use:** any multi-step migration where the Stop hook fires mid-flight. The discipline preserves the green baseline as a recoverable checkpoint while preserving optionality on the deferred work.

---

## Forward signals

Things the chapter surfaced that are NOT in scope and remain queued.

1. **`applied-transforms : (SsKey × OverlayAxis option) list` per-artifact manifest field.** Deferred from slice ζ per consumer-pressure principle. Trigger: a manifest consumer (operator dashboard, audit reader) demands per-artifact overlay-axis enumeration. Today only the registry-level digest is surfaced.

2. **Per-OverlayAxis CLI flags** (`--no-tightening`, `--no-selection`, etc.). Deferred-with-trigger from chapter A.4.7 open Q8. Today the binary `--skeleton-only` toggle suffices for the consumer surface. Trigger: real operator demand for granular overlay toggling.

3. **`Policy.fs` ↔ `OverlayAxis` structural collapse.** Preserved deferral from chapter A.4.7. The collapse trigger (call sites consulting both vocabularies) still doesn't fire; chapter A.4.7' didn't add Policy-consumption sites that would trip it.

4. **Emitter-as-chain-step.** Deferred from chapter A.4.7' axis-naming. Emitters return `ArtifactByKind<'element>` (heterogeneous `'element`); they don't compose into `ComposeState` the way passes do. Future chapter could unify if a fourth emitter lands or a consumer demands runtime classification of emitter sites.

5. **Adapter-as-chain-step.** Deferred from chapter A.4.7' axis-naming. The OSSYS adapter produces the input Catalog from raw rowsets, not a transformation OF the Catalog. Trigger: V2 needs to compose multiple adapters (e.g., DACPAC adapter alongside OSSYS).

6. **`Compose.run` async-streaming form.** Deferred from chapter A.4.7' axis-naming. Today's `PassChainAdapter.compose` is synchronous; if chain-level streaming becomes a perf concern at 300-table scale, a `AsyncStream`-based traversal becomes a candidate.

7. **`ComposeState.Profile` field consideration.** Open at chapter A.4.7' open: should `ComposeState` carry an explicit `Profile` field? Decision empirically deferred — slice ε's `runSkeleton` doesn't need it; slice δ's `project` captures `Profile` at factory time per pass. Trigger: a consumer needs `ComposeState.Profile` for runtime inspection.

8. **`runChain` placement.** The chapter shipped `PassChainAdapter.compose` (general primitive) instead of `TransformRegistry.runChain` (specific consumer). Per two-consumer threshold satisfied at slice γ + slice ε both consuming `compose`; the general primitive earned its place. Trigger to revisit: if a third consumer demands a per-stage traversal variant.

---

## Chapter-close ritual (8 items)

Per `DECISIONS 2026-05-14` — Chapter-close ritual; refined by `DECISIONS 2026-05-19` chapter-mid-audit amendment for Active deferrals scan.

1. **Active deferrals scan.** No silent-trigger fires. The eight forward signals above are all explicitly deferred-with-trigger. The chapter A.4.7 close's "Compose.run registry-traversal refactor" forward signal #3 is now cashed in full (this chapter). `BACKLOG.md` Active deferrals index entry: A.4.7' deferred-with-trigger items added.

2. **Contract-vs-implementation walk.** Every load-bearing contract introduced this chapter has a test:
   - ComposeState: `ComposeChainAdapterTests.fs` (initial + setters + lift round-trips + Lineage trail + Diagnostics propagation).
   - PassChainAdapter.compose: `PassChainAdapterComposeTests.fs` (base case + A24 trail order + allChainSteps end-to-end + T1 determinism + linear trail growth).
   - RegisteredTransforms.all / .allChainSteps: `RegisteredTransformsTests.fs` (count = 17 / 12; chain-step Names subset of metadata Names; validates through smart constructor).
   - Compose.runSkeleton: `SkeletonPurityTests.fs` (skeleton chain steps = 4; zero OperatorIntent events; every event DataIntent).
   - registry.digest: `RegistryDigestRoundTripTests.fs` (5 facets: identity-across-emits; JSON serialize→parse→extract round-trip; perturbation sensitivity; JSON-extracted perturbation; permutation invariance).

3. **CLAUDE.md staleness check.** No new operating disciplines this chapter; the four meta-codifications above (M1–M4) are pattern records, not load-bearing disciplines. CLAUDE.md unchanged.

4. **README.md staleness check.** README's "current state" pointer should reflect the registry as load-bearing for execution. (Not edited in this commit; minor follow-up update; chapter-close ritual flag for next agent's hygiene pass.)

5. **HANDOFF.md scope.** Bridge letter for next chapter updated in this commit. Names: registry is load-bearing; `Compose.project` routes through `RegisteredTransforms.allChainSteps`; `let run` private across all 12 passes; ManifestEmitter carries `registry.digest`; CLI exposes `--skeleton-only`. Eight forward signals carried over.

6. **AXIOMS amendment body.** A41 amendment (execution totality) filled per the S0.F scaffolding discipline. The placeholder added at chapter A.4.7' open is now resolved.

7. **Fresh-eye walk.** Files added: `CHAPTER_A_4_7_PRIME_OPEN.md`, `CHAPTER_A_4_7_PRIME_CLOSE.md` (this), `ComposeState.fs`, `PassChainAdapter.fs`, `RegisteredTransforms.fs`, 4 new test files. Files modified: `AXIOMS.md`, `TransformRegistry.fs`, `ManifestEmitter.fs`, `Pipeline.fs` (Compose module), `Program.fs` (CLI), 12 pass modules, ~14 test files. Total: 7 substantive commits + this close commit.

8. **Operating-disciplines table currency.** No new entries to CLAUDE.md's table. The Empirical-bet method (M2 above) and Per-test-file shim pattern (M3 above) are chapter-close meta-codifications, not stand-alone disciplines yet — if they recur in a future chapter, promote to the operating-disciplines table at that chapter's close. (Two-consumer threshold for primitive promotion preserved.)

---

## Verifiability-triangle audit step (per `DECISIONS 2026-05-12`)

**L3 axioms touched:**

- **L3-CC-Transform-Totality** — Bucket A preserved; underwriting tightens from metadata totality (chapter A.4.7) to metadata + execution totality (this chapter). A41 amendment body cashes the strengthening.

**Bucket-D gaps introduced:** zero. No new L3 axioms surfaced; no existing axiom slipped to a weaker Bucket.

**Pillar-9 audit:**

- **No new transformation sites.** The 18 classifications established at chapter A.4.7 stay; no reclassifications.
- **Skeleton-purity now structurally enforced at runtime** (chapter A.4.7' slice ε), not just at metadata (chapter A.4.7 slice θ). The corresponding property test fires on every emit-chain run; misclassification produces a test failure, not a silent drift.
- **Overlay-exercise unchanged from chapter A.4.7.** The 5 OverlayAxis variants (Selection / Emission / Insertion / Tightening / Ordering) all fire in the existing canary scenarios.

---

## Cherry-pick / carbon-copy events

**Zero.** The chapter shipped only V2-native primitives:

- `ComposeState` aggregate type — V2-native (no V1 precedent).
- `PassChainAdapter` type-erasure boundary — V2-native.
- `RegisteredTransforms.all` / `.allChainSteps` materialized assembly — V2-native.
- `TransformRegistry.digest` — V2-native (SHA256 over sorted metadata).
- `ManifestEmitter` `registry.digest` field — V2-native extension to the V1-mirror manifest shape.
- `Compose.runSkeleton` — V2-native.
- `osm emit --skeleton-only` CLI — V2-native.

`BACKLOG.md` V1 inheritance log: zero new entries.

---

## Closing notes

The chapter shipped in one continuous session-extended ship — 8 substantive slices + close — totaling ~9 commits + ~676 net line additions across ~50 files. The slice-η bulk migration via subagent (with parent-agent ValidationError boundary fix) demonstrated the limits of mechanical-edits-at-scale: subagent + parent collaboration is a reliable pattern at N=300+ call sites; the Stop-hook recovery pattern (M4) is now codified.

The structural commitment promised at chapter open — **the registry is load-bearing for execution; bypassing it is structurally impossible** — is fully met. Phase-A exit gate's pillar-9 commitment is closed.
