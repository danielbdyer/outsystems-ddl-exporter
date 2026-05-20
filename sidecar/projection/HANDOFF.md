# Handoff letter — 2026-05-20 (Chapter C slice 1 of 6 DONE; A7 verified-already-shipped; Faker deferred to corp-network access)

---

## 📍 Next-agent orientation — DO THIS FIRST

> **You're picking up V2 mid-Chapter-C.** Chapter B.4 closed (Phase B *structural* exit gate green); A7 verified-already-shipped (the slice 5.13 work in 2026-05-18 closed rows 32-36); C.1 (tightening axis wiring) shipped this session. Branch is `claude/review-handoff-docs-M5RVa`; tree clean; CI green; 1793/1793 non-Docker passing.
>
> **Read these, in this order (~25 min):**
>
> 1. **This letter** — 5 min. Names what's done, what's next, the two new operating-disciplines codified this session (TRX-first test-failure capture + Global-MutableState xUnit collection).
> 2. **`DECISIONS 2026-05-20 (slice C.1 — tightening axis config wiring)`** — 10 min. The full C.1 synthesis; names the binder shape, override-resolution discipline, why allChainStepsFor was the right factory shape, the test-collection fix folded inline.
> 3. **`DECISIONS 2026-05-19 (chapter B.4 hygiene strike + axis-survey supplement)`** — 5 min. The Chapter C 6-slice plan + the dormant-config-section sweep that names which Pipeline.Config sections are parse-but-ignore today.
> 4. **`DECISIONS 2026-05-20 (test-failure capture protocol)`** — 3 min. Codified discipline for TRX-first when `dotnet test` reports `Failed!`. Mandatory protocol when you hit a failure; saves cycles.
> 5. **`src/Projection.Pipeline/TighteningBinding.fs`** — 2 min skim. The template every remaining Chapter C slice's binder follows.
>
> **Branch protocol.** Stay on `claude/review-handoff-docs-M5RVa`. Each remaining Chapter C slice = one commit + one DECISIONS entry. Chapter C close ships the chapter-close letter at `HANDOFF.md` (rotate the current to `HANDOFF_CHAPTER_4.md` per convention) + `CHAPTER_C_CLOSE.md` synthesis + the 8-item chapter-close ritual.

---

## What shipped this session (3 commits on `claude/review-handoff-docs-M5RVa`)

| # | Commit | What |
|---|---|---|
| 1 | `9300ad5` | **Slice B.4.6.5** — LogSink + §11 roll-up (~540 LOC; 27 tests) |
| 2 | `3736055` | **Canary tuning** — 300×50k → 150×6.25k; wall ~10-12s → ~5s |
| 3 | `9b1eb42` | **Slice B.4.7** — full-export CLI; Argu per §15.2; 10 integration tests |
| 4 | `f64c692` | **Chapter B.4 CLOSES** — Phase B structural arm green; L3-X11 + L3-X12 promoted; HANDOFF rotated to HANDOFF_CHAPTER_3 |
| 5 | `a31d817` | **Slice C.1** — tightening axis wiring (~300 LOC; 14 tests); test-failure capture protocol codified; Global-MutableState xUnit collection |

**Cluster A7** (Polly transient retry; rows 32-36) — verified ALREADY SHIPPED via slice 5.13.production-wiring-classification + .progress-callback + .command-timeout on 2026-05-18. No new work this session; matrix amendments already in place. Code walk of `MetadataSnapshotRunner.fs` + `MetadataExtractionError.fs` + `Retry.fs` confirms full implementation.

**Faker emitter promotion** — explicitly DEFERRED until corp-network V1 HEAD access opens (per operator direction this session). Trigger remains structurally met; the row stays "TRIGGER STRUCTURALLY MET — awaiting explicit promotion" in DECISIONS Active deferrals.

## Chapter C status

**1/6 slices done; 5 remaining.** Estimated 2-3 weeks for the remainder.

| Slice | Axis | Status | Notes for next agent |
|---|---|---|---|
| **C.1 tightening** | 4 (priority) | ✅ DONE this session | TighteningBinding.fs template for the rest |
| **C.2 special-circumstances** | 5 | next | `Overrides.CircularDependencies` already parses; add consumer pass + `Overrides.AllowMissingPrimaryKey : SsKey list` allowlist + suppress-diagnostic pass that reads it |
| **C.3 emission-folders** | 2 | not-started | New `Overrides.EmissionFolders : Map<SsKey, string>` config section + `SsdtFile.RelativePath` rewrite pass + emit-time validation (folder paths respect SSDT naming conventions) |
| **C.4 tag-groups** | 6 | not-started | Closed `TransformGroup` DU (preset list — NOT operator-defined per `DECISIONS 2026-05-19` decision 3) + `RegisteredTransform.Tags : Set<TransformGroup>` field + `Policy.TransformGroups : Map<TransformGroup, bool>` config + filter at `Compose.runWithConfig` |
| **C.5 insertion semantics** | 9-revised | not-started | `Policy.InsertionPolicy` DU exists at `Policy.fs:77` (SchemaOnly/InsertNew/Merge/TruncateAndInsert); same binding shape as C.1 — config strings → registered intervention. Replaces the struck DynamicData framing |
| **C.6 verbosity flags** | 7a | partial — `--verbose` already in slice 7 | Add `--debug` flag + per-category filters; possibly TtyRenderer wiring (but TtyRenderer micro-chapter is still deferred per chapter B.4 close — verify with principal-PO whether C.6 stays narrow) |

## Two new operating disciplines codified this session

### 1. Test-failure capture protocol — TRX-first

**Trigger:** any time `dotnet test` reports `Failed: N` with N > 0.

**Protocol:**
```
dotnet test ... --logger "trx;LogFileName=test-results.trx" -- RunConfiguration.ResultsDirectory=/tmp/test-results
grep -oE 'testName="[^"]*"' /tmp/test-results/test-results.trx | sort -u   # names
grep -A 30 'outcome="Failed"' /tmp/test-results/test-results.trx           # details
```

**Why:** `dotnet test` console output interleaves across parallel test classes; per-test `[FAIL]` markers scroll past the final summary; `tail -N | grep "fail"` is unreliable. The TRX XML is the deterministic contract.

Codified in CLAUDE.md operating-disciplines table; `DECISIONS 2026-05-20 (test-failure capture protocol)` carries the full synthesis.

### 2. Global-MutableState xUnit collection

`Projection.Core.Bench` and `Projection.Pipeline.LogSink` are process-scoped mutable singletons. Tests that mutate them must serialize. New `[<CollectionDefinition("Global-MutableState", DisableParallelization = true)>]` in `tests/Projection.Tests/TestCollections.fs`. Currently tagged: BenchTests, LogSinkTests, FullExportCliTests. **Future slices that touch Bench/LogSink state must add `[<Xunit.Collection("Global-MutableState")>]` to the test module.**

## Best-practices the session taught (carry forward)

- **Operator-supplied refs resolve at bind time, not at use time.** TighteningBinding.fs walks the catalog once at config-load to convert textual refs (`"Module.Entity.Attribute"` or `"Schema.Table.Column"`) into typed `SsKey`. Errors surface at config-load time with structured ValidationError codes (`pipeline.tightening.overrideRef.unresolved` / `.shape` / `.kindUnknown` / `overrideAction.unknown`); passes consume the already-typed `TighteningOverride.AttributeKey : SsKey`. **C.3 / C.4 / C.5 binders should follow this shape** — boundary resolution, not pass-time resolution.
- **Single config entry shape across DU variants.** `TighteningInterventionEntry` carries every field from every variant as `option`-typed. Operators see one shape ("here's my interventions list") rather than four separate sections. Trade-off: the binder validates Kind→fields pairing at runtime. **Apply to C.5 insertion semantics** (single InsertionIntervention shape over the 4 InsertionPolicy variants).
- **Cost-driver identification is empirical, not assumed.** Canary tuning lesson from chapter B.4 close: the operator's "reduce row volume" framing assumed row volume was the cost driver; wall-time measurement showed deploy + reflection (proportional to table count) dominated. When an operator asks for a perf tuning, validate the lever before assuming.
- **`allChainStepsFor` is the registry's policy-parameterized form.** Static `allChainSteps` stays for the skeleton-only / no-policy paths; `allChainStepsFor` is for the policy-aware production path. Future Chapter C slices that need to thread additional Policy axes follow the same factory pattern — extend the chain factory's signature, don't mutate the static chain.

## Test baseline post-C.1

- **1793/1793 non-Docker** passing (was 1779 at chapter B.4 close; +14 from TighteningBindingTests).
- **0 build warnings** under `TreatWarningsAsErrors=true`.
- **End-to-end CLI smoke green** at chapter B.4 close (no regression expected from C.1; full-export's pre-existing path goes through `runWithConfig` which now constructs Policy from config — passes when Tightening section absent because `TighteningPolicy.empty` flows through cleanly).
- **Operator-reality canary GREEN** at tuned floor (~5s warm; 230 labels in baseline).

## Open questions for the next slice's opening

1. **C.2 scope decision.** The strategic-exploration entry framed C.2 as "Special circumstances axis (axis 5) — consumer pass for `Overrides.CircularDependencies.AllowedCycles` + `Overrides.AllowMissingPrimaryKey : SsKey list` allowlist + the suppress-diagnostic pass that reads it." Two scope decisions at slice open: (a) does `AllowMissingPrimaryKey` get the same logical-or-physical ref shape as TighteningBinding, or simpler? (b) does the CircularDependencies consumer suppress diagnostics or annotate them? Bring to principal-PO at slice open.

2. **C.6 boundary with TtyRenderer micro-chapter.** Verbosity flags (`--verbose` / `--debug`) are cheap; TtyRenderer is the post-chapter deferred work. The chapter B.4 close-handoff was explicit that TtyRenderer is deferred. C.6's scope is just the flags + per-category filter wiring; TtyRenderer stays separate. Verify at C.6 open.

3. **Chapter C close cadence.** Same 8-item ritual as chapter B.4 close (Active deferrals scan; contract-vs-implementation walk; CLAUDE.md staleness; HANDOFF rotation; CHAPTER_C_CLOSE.md synthesis; fresh-eye walk; operating-disciplines table currency; per-axis-stakes evaluation against V2_DRIVER). Plan one final commit for the chapter-close ritual after C.6 lands.

Hold the spine. Chapter C compounds — C.1's TighteningBinding template + the `allChainStepsFor` factory + the operator-supplied-ref resolution discipline + the Global-MutableState xUnit collection are the substrate every remaining slice consumes. Each slice earned, one at a time.

— The slice-C.1 architect (handoff).
