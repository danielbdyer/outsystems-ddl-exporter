# Refactor Reconnaissance — `sidecar/projection`

> **Date:** 2026-06-25. **Status:** reconnaissance only — nothing edited. **Scope:** the
> `src/` tree (77k LOC F#, 12 projects). **Method:** seven parallel read-only recon agents,
> each owning a layer (SQL string-building; Run/Binding duplication; Core domain modeling;
> core/shell boundaries; CLI/presentation; pass architecture; config & error/refusal
> uniformity). This document is the **unabridged** consolidation: overlaps the agents hit
> independently are merged, but the detail (anchors, code quotes, XL variants, blast radius)
> is preserved. 25 findings.
>
> **One-paragraph framing.** The codebase is mature and the disciplines hold. The recurring
> shape of the debt is *not* sloppiness — it is **laws honored by vigilance that want to
> become structural**, plus **three or four migrations that stopped near 50%** (the typed-AST
> migration never reached the Pipeline; the Voice migration never finished in `RunFaces`; the
> `registered ⇔ executed` discipline reached the pass chain but not the binder registry; the
> VO lift reached Catalog but not a handful of leaf fields). Every big swing below is the same
> move: take a discipline the code keeps by hand and make the type system keep it instead.

**Effort key:** S = hours · M = a day or two · L = a focused chapter · XL = a multi-session chapter.
**Tier:** 🟥 big swing · 🟧 high-leverage mid · 🟨 worthwhile · ⬜ quick win / cleanup.

---

## ▶ Execution status — updated 2026-06-27

Work is underway across two branches off `main` (`250811ea`): the typed-AST chapter on
`claude/finish-typed-ast-refactor`, and the recon sweep on `claude/recon-binding-registry`
(this doc now lives here so it merges in with the sweep). **13 findings resolved (12 fully
landed + #7's genuine consolidation, its over-reach remainder declined with reasons), 6
partially landed, 6 untouched.** (#4 and #7 both carry a *reasoned decline* on their
over-reach remainders — see their sections.) Every
partial's open remainder and every untouched item is enumerated below; each `## N.`
section also carries a per-section `> **Status:**` line.

> **Sweep update 2026-06-27 (continuation).** Beyond the table above, this branch added:
> the CLI exit-code seam (`CliExit`, #3 Concern 1), `Fixpoint.iterate` (#19), the
> `LineageEvent.forPass` smart ctor (#15), `Composition.fanOutWithDiagnostics` (#12), and
> the #25 quick-wins (Navigator breadcrumb
> bug, `Catalog.kindIndex` reuse, `LifecycleStore.withLoaded`, `RelaxationStore.persist`
> → `Result`). All verified by clean build + the pure (non-SQL) pool — every test class
> exercising the changes is green, 0 assertion failures. **#14 (`DerivationReason` DU) was
> then approved by the operator and landed (the aggressive close):** `derivedFrom` is total,
> the codec is byte-identical, and AXIOMS A5 + a dated DECISIONS amendment ship in-commit
> per §5. Docker came back up mid-sweep (warm SQL container restarted), so #14 and the prior
> batches are now **Docker-verified end-to-end: pure pool 3723/0, Docker pool 273/273** (the
> round-trip canaries confirm byte-identity).

| # | Finding | Tier | Status | Landed in / what's left |
|---|---|---|---|---|
| 1 | `SurrogateCapture` → ScriptDom | 🟥 | ✅ **done** | typed-AST branch (Tier 2.1) |
| 2 | Binding registry + `ConfigAxis` | 🟥 | ✅ **done** | `af157f61` |
| 3 | `Face` combinator + split `RunFaces` | 🟥 | ◑ **partial** | Concerns 1 (CliExit) + 2 (slice→typed Intent, `argv.[0]` dispatcher deleted) done. **File split (2026-06-28): 7 verb-family files + the `Faces/Common.fs` spine extracted — RunFaces 2699→1672 (−38%):** Slice / Diff / Inspect / Emit / Canary / Approve / Operational(verify-data·drift·eject·readiness·setup), with `Common.nameOf` seeding the shared spine. **Open:** the deeply-coupled core (transfer / migrate / synthetic / deploy / reverse-leg / explain / project-preview / profile / correction / full-export) — grow `FacesCommon` (`narrateTransferReport` / `dispositionName` / the `Face` combinator / the migrate helpers), then relocate. |
| 4 | `CapabilityDescent` + registry | 🟥 | ◑ **partial** | named-refusal registry in (`a10715ab`). **Declined as over-reach (needs approval to force):** the unified descent *combinator* + *record* — the two sites are genuinely different shapes (multi-rung ladder vs attempt-or-skip). |
| 5 | `ProfileDerivation` / `Profiler` port | 🟥 | ✅ **done** | `5efec474`, `c81c88ad`, `81d58b1b` (EvidenceCache + the ~900-line derivation suite now pure Core; `EvidenceCache` is the source-agnostic seam) |
| 6 | Typed-tree boundary analyzer | 🟧 | ✅ **done** | `eb243e6d` (bans capabilities by resolved full name) |
| 7 | `FkGraph` build-once | 🟧 | ✅ **done** / remainder **declined** | The genuine dup (the `addNeighbor` undirected fold) was consolidated into `undirectedAdjacency` (2 consumers). The full `FkGraph` reification is **declined out loud (2026-06-27)**: the 2 remaining "copies" are deliberate, documented carve-outs (Centrality's reverse-adj perf `Dictionary`; the cascade adjacency is classifier-filtered, needs Catalog+`CycleResolution`, can't be an edges-only `FkGraph` method) — reifying them would be a zero-consumer symmetry-build (the §5 dead-algebra anti-pattern). |
| 8 | Shared quoting primitive | 🟧 | ✅ **done** | Core `SqlIdentifier.quote`/`qualified` (byte-verified ≡ `EncodeIdentifier`); routes `LogicalColumnEmission` (fixes its latent `]` bug), `RemediationEmitter`, ReadSide/LiveProfiler (2026-06-27). |
| 9 | Code→exit registry | 🟧 | ○ remaining | — |
| 10 | `parseSemanticType` → Core | 🟧 | ✅ **done** | OSSYS→V2 mapping decisions moved to pure `Core.OssysTypeMapping.tryParse` (option); adapter keeps the `adapter.osm.*` refusal shim + `normalizeAttributeType` (2026-06-27). |
| 11 | Finish Voice migration + unify dispatchers | 🟧 | ○ remaining | — |
| 12 | `fanOutWithDiagnostics` primitive | 🟧 | ✅ **done** | `Composition.fanOutWithDiagnostics` added; Nullability/UniqueIndex/ForeignKey passes' decision→diagnostic tails collapse to it (2026-06-27). |
| 13 | One connection discipline / `Source` port | 🟧 | ◑ **partial** | `ConnectionSpec.openSpec role label spec` landed (2026-06-28) — the one env:/file:/live:/bare opener; the byte-identical `SliceExtractRun.openSource` ≡ `SliceApplyRun.openTarget` pair collapsed onto it. **Open:** fold the env:/file:-only sites (gaining uniform `live:` coverage); the `Substrate` factory; the `LiveModelRead`→`Source`-port collapse (the XL). |
| 14 | `DerivationReason` DU | 🟨 | ✅ **done** | Closed DU (`Inverse`); `derivedFrom` total; codec byte-identical; AXIOMS A5 + DECISIONS amended (operator call, 2026-06-27). |
| 15 | `LineageEvent.forPass` smart ctor | 🟨 | ✅ **done** | `LineageEvent.forPass` smart ctor in `Lineage.fs`; the 16 hand-written 5-field event literals across 13 passes now call it (2026-06-27). |
| 16 | Unify 3 JSON-read helpers | 🟨 | ○ remaining | — |
| 17 | `Comparison.fs` — domain out of render | 🟨 | ○ remaining | — |
| 18 | De-hardcode config knobs | 🟨 | ◑ **partial** | `BoundedContext.maxPropagationRounds` → `AdvisoryTuning.defaults.BoundedContext` (2026-06-27). **Open:** ReadSide `maxRows` (→ Core `Modality.classify`) + ClosureOracle `fuel` (the M SQL-knob variant). |
| 19 | `Fixpoint.iterate` combinator | 🟨 | ✅ **done** | `Fixpoint.iterate` in Core; CentralityPass PageRank + BoundedContextPass label-propagation + ProfileAnomaly Newton-sqrt collapsed onto it (2026-06-27). |
| 20 | ReadSide pure-logic → Core | 🟨 | ✅ **done** | `ForeignKeyReadback` (classify) moved to Core; key synthesis + `formatRawValue` found ALREADY Core-routed (recon anchors stale — documented) (2026-06-27). |
| 21 | Keymap-spill / transfer DML hardening | 🟨 | ✅ **done** | typed-AST branch (Tier 2.2) |
| 22 | `View` DU leaf consolidation | 🟨 | ◑ **partial** | The `Status → presentationOf` consolidation landed (3 parallel matches → 1; 2026-06-27). **Open:** the M `Field`/`PanelRow` leaf merge + the L `Lane`↔`Disclosure` merge (cross-level DU moves; the latter unlocks #17). |
| 23 | Structural `registered ⇔ executed` at Pipeline | 🟨 | ○ remaining | — |
| 24 | Core type-modeling cluster | ⬜ | ○ remaining | — |
| 25 | Quick-wins & incidental bugs | ⬜ | ◑ **partial** | Remediation `]`-bug fixed via #8. **Navigator** breadcrumb bug fixed + `safeMarkupLine` extracted (4× dup); `Catalog.kindByKey` (the existing cached `kindIndex`, 4 sites); `LifecycleStore.withLoaded` (Eject/Report fromStore); `RelaxationStore.persist` → `Result` (2026-06-27). **Open:** `catalogTopologyStep` builder, `parse-template` guards, `OperatorConsole`/`TtyRenderer` global threading, `Watch.fs` wire-format relocation. |

Supporting work also landed: the stale **M1** byte-identity baseline fix (`0bb7feae`, the
`catalog.snapshot.json` 8th-artifact correction) and `THE_PROJECTION_PRINCIPLE.md` (the
cross-cutting algebra treatise this recon produced alongside).

**Verification standard held throughout:** every commit builds clean across
Core/Adapters/Pipeline/Tests; pure pool green (`scripts/test.sh fast`, 3690/0); and any
change touching SQL emission / the reverse leg / the profiler is Docker-deploy-verified
(`scripts/test.sh docker`, 273/273, incl. the 34 LiveProfiler integration witnesses for
#5). Re-verify the same way.

---

## 🤝 Handoff to the next agent

You're picking up an active, well-instrumented refactor. The branches above are green and
ready; pick the next item from the status table and keep going. Read `CLAUDE.md` §4
(survival rules) first — the warm-container and test-pool traps will cost you a session
otherwise.

**The operator's working agreement — these are standing, not per-task:**

1. **Go XL. Do the complete work.** When a finding has an "extra-large version," that is
   the target, not the incremental slice. Take the whole structural move — the per-verb
   split, the full primitive promotion, the complete migration — not the half that's
   comfortable. Slices are a *sequencing* tool for a big move, not permission to stop at
   50%. The recurring debt in this tree is precisely migrations that stopped near 50%;
   don't add another.

2. **Don't defer silently. Raise every deferral for approval *first*.** If you believe a
   piece is genuinely out of scope, too risky, or better split off — do **not** quietly
   drop it, skip it, or leave it for "a later thread." Stop and raise it to the operator
   with your reasoning and get a decision before putting it down. The same applies to a
   pre-existing failing test you discover: you take it forward, you don't punt it. (Recon
   #4's declined combinator is the model — it was declined *out loud, with reasons*, not
   silently skipped.)

3. **Assume full control. Fix what you find.** This is your/our codebase — you are
   deputized over all of it. When a refactor surfaces a bug, a stale doc, a broken
   adjacent thing, a wrong comment — **fix it**, in the same change, and say so. Do not
   write "I didn't do that" or "that's out of scope" about something you've discovered and
   can correct. If the fix is large enough to deserve its own commit or its own approval,
   that falls under rule 2 (raise it) — but the default posture is ownership, not
   disclaimer.

4. **Verify, then claim.** Build + pure pool for everything; Docker-deploy-verify anything
   touching SQL emission, the reverse leg, or the profiler. Report outcomes faithfully —
   if something is verified, say so plainly with the evidence; if a step was skipped, say
   that. Update *this doc's status table and the per-section `> Status:` lines* in the same
   commit as the work, so it never goes stale.

**Where the leverage is right now.** Per the master ranking, the highest-value unstarted /
open moves are: **#3** (finish the `RunFaces` split — top-ranked, only its combinator is
in), then the 🟧 mid-tier cluster **#9–#13** (several of which the Binding registry from
#2 now unlocks — e.g. #9's code→exit registry is a natural extension of `ConfigAxis`).
The cheapest standalone protections are the **#25 Navigator bug** and **#19**
(`Fixpoint.iterate`). Several 🟨 items (#14, #15, #20) are mechanical and high-certainty if
you want momentum between big swings.

Hold the spine. Leave the books balanced.

---

## 1. 🟥 Route `SurrogateCapture` (reverse-leg INSERT/MERGE) through the existing ScriptDom builders

> **Status (2026-06-27):** ✅ **Done** — typed-AST branch (Tier 2.1).

**Anchors:** `src/Projection.Pipeline/SurrogateCapture.fs:124, 132, 139–141, 164, 180, 184, 191, 197, 220, 223`; primitives that already exist: `src/Projection.Targets.SSDT/ScriptDomBuild.fs:741` (`buildInsertRow`), `:1015` (`buildMergeStatement`).

**The tension/smell.** The typed-AST discipline is real and well-enforced *inside* SSDT/Data/Core — but it never reached the Pipeline, and the single highest-stakes path (the hundreds-of-millions-of-rows reverse leg) builds every statement with bare `sprintf`. The MERGE-OUTPUT capture rung:
```fsharp
sprintf "MERGE INTO %s AS T USING %s AS S ON 1 = 0 WHEN NOT MATCHED THEN %s OUTPUT S.[__SRC_KEY], INSERTED.%s;"
    (Render.tableQualified kind.Physical) StagingTable (insertArmOf insertCols) (quotedCol identityAttr)   // :164
```
the rowwise floor:
```fsharp
sprintf "INSERT INTO %s (%s) VALUES (%s); SELECT CAST(SCOPE_IDENTITY() AS BIGINT);"
    (Render.tableQualified kind.Physical) (litGets |> List.map (fst >> quotedCol) |> String.concat ", ")
    (litGets |> List.map (fun (a, get) -> lit get a) |> String.concat ", ")   // :223
```
plus `insertArmOf` (`:139`), the SELECT-TOP-0-INTO staging clone (`:124`, `:180`). The file's header carries a blanket `LINT-ALLOW-FILE` justifying all of it as "terminal SQL text over validated TableIds … the same allowed class as TransferRun" — but unlike SSDT, **none of it goes through ScriptDom**, and `ScriptDomBuild` already ships exactly the primitives it reinvents. The MERGE is hand-built here while the Data lane builds the *same shape* via `buildMergeStatement`: that is single-axis-divergent — **two MERGE-emission paths** (an A40 violation). Literal encoding *is* correctly routed through `SqlLiteral.ofRaw |> SqlLiteral.toString` (`:216–217`) — good — but the statement frame is raw. `ScriptDomBuild.fs:1216–1227`'s own comment names this exact class as "the highest-blast-radius hand-built SQL in the tree, executed raw, with no parse validation" and records that it **already shipped a VO-stringification bug** (`"#seed_" + k.Physical.Table`).

**Proposed refactor.** Lift the staged-MERGE-OUTPUT and rowwise-INSERT arms to ScriptDom: construct `MergeStatement` via `buildMergeStatement` (with an `OUTPUT`-clause builder — ScriptDom models `OutputClause`/`OutputIntoClause`), and the rowwise INSERT via `buildInsertRow`, rendered through `ScriptDomGenerate.generateOne`. The `SELECT … SCOPE_IDENTITY()` trailer and `SELECT TOP 0 … INTO` staging-clone are the genuinely-terminal residue — keep those as annotated `String.Concat`, but with a *real* per-site four-question rationale, not a file blanket.
**Extra-large version.** Make `ScriptDomBuild` own an `OutputClause` builder and a `SelectIntoStaging` builder so the whole capture ladder is typed; the `OUTPUT … INTO` variant (`:184`) then differs from `OUTPUT …` (`:164`) by one typed node, collapsing the A40 divergence structurally.

**Blast radius.** One file; `SurrogateCapture` is consumed by `TransferRun.captureChunks`. Needs the SSDT cross-target dep (already taken by `StagedMerge.fs`/`MigrationDependenciesEmitter.fs`, so precedented). Golden/canary impact: the reverse-leg E2E + capture-ladder tests; MERGE byte output may shift (re-bless).

**What it buys.** Kills the second MERGE-emission path (A40), removes the largest un-annotated raw-SQL surface, aligns the highest-stakes write path with the stated typed-AST discipline, and parse-validates the class of SQL that already shipped a bug. **Effort: XL.**

---

## 2. 🟥 Collapse the `*Binding` family into one typed binder registry (+ `ConfigError`/`ConfigAxis`)

> **Status (2026-06-27):** ✅ **Done** — `af157f61` (the Binding kernel + `ConfigAxis`; the 7 config binders collapsed).

**The single most-corroborated finding — three independent agents hit it.**

**Anchors:** `RenameBinding.fs:22/53`, `TighteningBinding.fs:30/165`, `InsertionPolicyBinding.fs:32/40/57`, `TransformGroupsBinding.fs:144/152/170`, `EmissionFoldersBinding.fs:60/147/179`, `SpecialCircumstancesBinding.fs:54/60/80/134`, `MigrationDependenciesBinding.fs:55/192/230`, `ModuleFilterBinding.fs:61`, `SnapshotScopeBinding.fs:64`; orchestration sites `Pipeline.fs:1199–1300, 1291, 1383`; `Config.fs:521` (`configError`).

**The tension/smell.** Nine `*Binding.fs` modules (~1,100 LOC together) are the *same morphism*: `Config-shape (raw strings) → Result<Core-shape (VOs)>`, aggregating errors. The boilerplate is copied near-verbatim:
```fsharp
// SpecialCircumstancesBinding.fs:54
let private bindError (code: string) (message: string) : ValidationError =
    ValidationError.create (sprintf "pipeline.specialCircumstances.%s" code) message
// EmissionFoldersBinding.fs:60 — identical modulo the namespace literal
let private bindError (code: string) (message: string) : ValidationError =
    ValidationError.create (sprintf "pipeline.emissionFolders.%s" code) message
```
`RenameBinding` spells it `bindingError`; `Config` spells it `configError` — the same function under three names, and the `pipeline.<axis>.` prefix is enforced by nothing (a typo'd `pipline.` passes). `resolveKindByLogical` (catalog-resolution → named refusal) is copy-pasted across four binders — `EmissionFoldersBinding.fs:147` documents it: *"Mirrors `SpecialCircumstancesBinding.resolveKindByLogical`"*; `SpecialCircumstancesBinding` has *two* near-identical copies (`:60` and `:80`) differing only in the refusal code. The closed-DU-name parsers are the same function twice: `InsertionPolicyBinding.fromString` (`:40`) ≡ `TransformGroupsBinding.parseGroupName` (`:152`) — match the case names, else `bindError "unknownVariant"/"unknownGroup"` with a "Known: A | B | C" message. And `Pipeline.fs`'s `bindShapingTriple` *comments* that the hand-listed `policy @ folders @ groups` (`:1291`) "cannot drift apart" — held together by a human keeping two sites in sync.

**Proposed refactor.** A `Binding` core in one module:
```fsharp
[<RequireQualifiedAccess>]
module Binding =
    let error (axis: string) (code: string) (msg: string) : ValidationError =     // replaces 9 bindError copies
        ValidationError.create (sprintf "pipeline.%s.%s" axis code) msg
    let requireKindByLogical (axis) (catalog) (m, e) : Result<SsKey> = …            // replaces resolveKindByLogical ×4
    let ofClosedName (axis) (known: (string * 'a) list) (raw: string) : Result<'a> = …  // replaces fromString / parseGroupName
```
**Extra-large version.** Make every binder a value in a typed registry — `Binder<'cfg,'core>` keyed by overlay axis — so `buildPolicyFromConfig`/`bindShapingTriple` **fold over the registry** instead of hand-listing. Add a closed `ConfigAxis = EmissionFolders | InsertionPolicy | Tightening | …` with `ConfigAxis.prefix`, so the `pipeline.*` namespace vocabulary is total and greppable. This applies the codebase's own Pillar-9 `registered ⇔ executed` discipline to the one axis-keyed surface that escaped it, and the resulting registry becomes the seam the exit-code `classify` (Finding 9) and the metadata-error parity (Finding 24's note) consume by construction.

**Blast radius.** 9 binders + 2 orchestration clusters in `Pipeline.fs`. Public `fromConfig` signatures stay as thin shims, so external callers and tests are untouched; the internals shrink. Each binder keeps its existing tests.

**What it buys.** ~300+ LOC deleted; one place to add a binder; the error-namespace convention becomes unforgeable rather than 9×-retyped; the shaping-triple drift the comment frets about becomes structurally impossible. **Effort: L (XL with the registry-fold).**

---

## 3. 🟥 Split `RunFaces.fs` (2711) into one-module-per-verb over a shared `Face` combinator

> **Status (2026-06-28):** ◑ **Partial — Concerns 1 + 2 closed; only the file split remains.** **Concern 2 (2026-06-28):** the four slice data-portability verbs (`slice-extract`/`slice-apply`/`slice-reset`/`slice-run`) are lifted onto the typed surface — new `Intent.SliceExtract`/`SliceApply (reset, …)`/`SliceFlow` cases, `Command.parse` + `Command.plan` arms, `PlanAction.RunSlice*`, and `runPlan` dispatch — and the `argv.[0]` second dispatcher in `Program.main` is **deleted**: the CLI now runs ONE dispatch plane. Following the codebase's deferred-parse convention (`Check`/`Explain`/`Profile` carry raw `args`), the slice faces keep their bespoke flag/config parsing (which does run-time I/O the pure plan can't), so the action carries the args the way `PublishBundle` carries an unresolved config path. New `MovementSurfaceTests` pin all four routings; build clean, pure pool 3744/0. **Concern 1 closed (2026-06-27):** the 6 hand-rolled `anyCode` exit-ladders are collapsed into one total, test-pinned `CliExit` classifier (`src/Projection.Cli/CliExit.fs`) that delegates gate-axis codes to `Preflight.classify` and owns the CLI-specific `slice.*`/`synthetic.*`/`profile.*`/`correction.*`/`model.*` vocabulary. Layering note: the recon's "register in `classify`" sketch was realized CLI-side — exit-1 artifact-write and exit-2 input-parse codes are *not* pre-flight gates, so cramming them into the `GateLabel` DU would be a category error; the classifier instead *delegates* gate codes to the one `Preflight` seam, so the gate convention keeps a single definition. The three latent divergences the collapse surfaced were reconciled onto that convention (the complete fix, not preserved-as-found): `synthetic.cdcTrackedSink` 7→**9**, `slice.apply.grantProbeFailed` 6→**7**, and the two slice verbs now agree on the `slice.apply.*` sub-codes (`slice-run` no longer flattens the prefix to 6). Aggregation keys off the *primary (first)* error, matching `Preflight.refusalOf` (the six ad-hoc tier-scans were mutually inconsistent, so no single scan reproduced all six; first-error is the seam-aligned rule and is identical to the old `anyCode` on the common single-error refusal). Build clean; pure pool 3723/0 incl. 23 new `CliExitTests`. **Open (the bulk — go XL):** the per-verb file split (`Faces/*.fs`); lift the `slice-*` verbs into the typed `Intent` surface and delete the `argv.[0]` second dispatcher.

**Anchors:** whole file `src/Projection.Cli/RunFaces.fs`; exit-ladder copies `:2459–2461, 2479–2482, 2505–2508, 2538–2542, 2586–2590, 2705–2708`; slice arg-parsers `:2519–2570, 2577–2648, 2654`; the central seam it bypasses, `Preflight.refusalOf`/`classify` (praised in-file at `:770`, `:1942`); dispatch side-channel `Program.fs:455–462`.

**The tension/smell.** Every `run*` face does *all* of: argument parsing, connection/spec validation, `task{}` orchestration with hand-cranked `.GetAwaiter().GetResult()` (**36×**), operator narration, exit-code classification, and bench dumping (`dumpBench` **42×**) — inline, per verb, ~30 times. Three concrete tangles:

*Concern 1 — exit-code classification copy-pasted 7× as a brittle prefix-ladder:*
```fsharp
let anyCode (prefix: string) =
    errors |> List.exists (fun (e: ValidationError) -> e.Code.StartsWith prefix)
if anyCode "synthetic.profileRef" || anyCode "model." then 2
elif anyCode "synthetic.insufficientGrant" || … then 7
else 3
```
This is exactly what the rest of the file routes through `Preflight.refusalOf`/`classify` — the six slice/synthetic/profile/correction faces *bypass* that seam and can drift from it silently.

*Concern 2 — the three `slice-*` verbs hand-roll arg-parsers* (`flagValue`/`hasFlag` over `argv`) and are dispatched **outside** the typed `Intent` surface (raw `argv.[0]` matching in `Program.fs:455–462`) — the one place the CLI runs two dispatch mechanisms.

*Concern 3 — `task{}…GetResult()` ×36 and `dumpBench "verb"` ×42* are the missing `Face` combinator; some faces (`runDrift`, `runInspect`) inconsistently forget `dumpBench`.

**Proposed refactor (incremental).** Route the 6 `anyCode` ladders through `Preflight.refusalOf errors |> .ExitCode`, registering the slice/synthetic codes in `classify` (M); lift the slice verbs into the typed `Command.parse`/`Intent` surface, deleting the bespoke parsers and the `argv.[0]` arms (M→L).
**Extra-large version.** Split into one module per verb-family (`Faces/Migrate.fs`, `Faces/Transfer.fs`, `Faces/Canary.fs`, `Faces/Slice.fs`, `Faces/Inspect.fs`, …) over a shared `Face` combinator that owns the cross-cutting spine — `parse args → validate → run Task<Outcome> → classify exit → dumpBench` — each face declaring only its distinctive middle. `runPlan` (`Program.fs:145–265`) stays the one dispatcher.

**Blast radius.** Confined to `RunFaces.fs` + the `Program` dispatch; exit codes are test-pinned, so the incremental moves are safe. The XL split is mechanical (move declarations) but touches `.fsproj` compile order.

**What it buys.** Turns a 2711-line wall into ~8 focused files; erases ~40 boilerplate sites; structurally enforces the exit-code/bench/narration disciplines that are currently by-convention; one dispatch plane (slice verbs gain the same totality test + `--query`/`--pretty` lenses every sibling enjoys). **Effort: XL (M for each incremental slice).** Highest single-move leverage in the tree.

---

## 4. 🟥 One `CapabilityDescent` combinator + a named-refusal registry

> **Status (2026-06-27):** ◑ **Partial.** The named-refusal registry landed as `CapabilityRefusal` (`a10715ab`). **Declined out loud (raise for approval to force):** the unified descent *combinator* + *record* — the two sites are genuinely different shapes (a multi-rung ladder vs an attempt-or-skip) carrying different information.

**Anchors:** `SurrogateCapture.fs:275` (`isCapabilityRefusal ex = ex.Number = 334`), ladder `:282` (`captureChunkDescending` → `CaptureLane.ladderFrom`, records `LaneDescent`); `TransferRun.fs:692` (`isAlterCapabilityRefusal ex = 1088 || 4902 || 229`), descent `:725` (`restoreFkTrust`, records via free-text `LogSink.recordStageProgress "retrust-skipped"` at `:706`, which already names the typed `ToleratedDivergence.FkTrustNotRestoredOnBulkLoad`).

**The tension/smell.** CLAUDE.md states the capability ladder as standing law: *"descend only on the named capability error; every descent on the report."* It is honored — **twice, differently.** Two hand-rolled SQL-error-number predicates (the comment at `TransferRun.fs:691` cross-references *"`SurrogateCapture.isCapabilityRefusal`"* — they *know* they're siblings); two recording channels (structured `LaneDescent` values vs a free-text stage-progress label); "every descent on the report" lands in two different reports. There is no single combinator expressing "attempt at rung; on a *named* capability refusal, descend and record; on any other error, propagate."

**Proposed refactor.** A `module CapabilityDescent` owning (a) one `CapabilityRefusal` recognizer over a **closed registry** `(SqlErrorNumber → CapabilityName)` — so `334/1088/4902/229` live in one table, not two predicates — and (b) one combinator:
```fsharp
descend : ladder:'rung list -> attempt:('rung -> Task<'r>) -> record:(Descent -> unit) -> Task<'r * 'rung * Descent list>
```
Both `captureChunkDescending` and `restoreFkTrust` become callers.
**Extra-large version.** Unify the descent *record* too: `LaneDescent` and the `"retrust-skipped"` stage become one `Descent` DU carrying from/to-rung + the named capability, and it flows through the `DiagnosticEntry`/`ToleratedDivergence` surface (the typed tolerance already exists — the descent just doesn't emit it). "Every descent on the report" becomes one report shape.

**Blast radius.** 2 producer files + their tests; the recording-channel unification touches `LogSink`/the transfer report.

**What it buys.** The load-bearing discipline becomes structural instead of vigilant — a new descent site *cannot* forget to record, and the refusal-number registry is total. **Effort: L.**

---

## 5. 🟥 Extract a pure `ProfileDerivation` / `Profiler` port out of `LiveProfiler`

> **Status (2026-06-27):** ✅ **Done** — `5efec474`, `c81c88ad`, `81d58b1b`. `EvidenceCache` + the ~900-line derivation suite are now pure `Projection.Core`; `EvidenceCache` is the source-agnostic seam the port asked for.

**Anchors:** `LiveProfiler.fs` — `deriveAttributeReality`/`hasDuplicates`/`hasNulls` (~432–474), `deriveCategoricalDistributions` (~586–653), orphan-value detection (~908–926); the clean pure cache it consumes, `EvidenceCache.fs`; the percentile triplication `LiveProfiler.fs:494–507` **≡** `LiveProfiler.fs:996–1005` (identical bodies in one file; the second's docstring says "Reused from … extracted here" but it was *copied*), plus a third at `Bench.fs:261–267`.

**The tension/smell.** The "discover-once, derive-pure" half is right: `EvidenceCache` is a pure DU (no driver types), and the SQL-issuing discovery (`captureEvidenceCache`) is cleanly separated from derivation. The leak: the **derivations are pure but physically live in the SQL adapter.** `hasDuplicates` (HashSet-over-string-key dedup), categorical frequency-tally-and-sort, orphan detection via set difference — none touch `SqlConnection`; they consume `EvidenceCache` (pure) and emit `Profile` IR (pure). That is the exact signature of a Core pass: `EvidenceCache × Catalog → Profile`. The structural consequence is worse than aesthetics: because the derivations are welded to `LiveProfiler`, the **synthetic profiler cannot share them** — "profile synthetic data the same way we profile live data" would re-derive realities/distributions/orphans a second time against the same type. `percentileCont` (PERCENTILE_CONT linear interpolation) is the duplication made literal — written three times.

**Proposed refactor.** Extract a pure `Projection.Core.ProfileDerivation` module: `EvidenceCache -> Catalog -> Profile` (realities, categorical & numeric distributions, orphan sets). `LiveProfiler` shrinks to *discovery → cache*; Core finishes. Lift `percentileCont` into one `Projection.Core.Statistics` (or `Numeric`) module; the two `LiveProfiler` copies collapse to calls and `Bench` keeps its `int64` flavor.
**Extra-large version.** Formalize a `Profiler` port — `type Profiler = { Discover : Catalog -> Task<Result<EvidenceCache>> }` — with `LiveProfiler` and a `SyntheticProfiler` as the two implementations, both feeding the one Core derivation. Profiling becomes source-agnostic and the derivation property-testable on hand-built caches (no DB).

**Blast radius.** `LiveProfiler.fs` (large, but the cut is along the existing `cache → derive` seam) + one new Core module; the derivation outputs `Profile`, so goldens/AxiomTests over `Profile` are the safety net. `percentileCont` move is `private`-internal, output-identical.

**What it buys.** The least-tested logic in the system becomes property-testable Core; live and synthetic paths converge; `LiveProfiler` stops being a 1,276-line god-adapter; one percentile instead of three (which *will* drift). **Effort: L (XL for the full port); the percentile extraction alone is S.**

---

## 6. 🟧 Harden the boundary analyzer — it catches ~7 names, misses the rest of the law

> **Status (2026-06-27):** ✅ **Done** — `eb243e6d`. Rewritten onto the typed tree; bans capabilities by resolved full name; zero findings on clean Core, catches the constructor/CE/Environment-DU holes the suffix matcher missed.

**Anchors:** `src/Projection.Analyzers/NoUnsafeTimeInCoreAnalyzer.fs:54–63` (the `forbiddenSuffixes` list), `:65–70` (`matchesForbiddenSuffix`), docstring `:8–26`; the legitimately-IO-using sanctioned files `Bench.fs`, `PinnedWriting.fs:13` (`open System.IO`).

**The tension/smell.** The entire "pure core" law (CLAUDE.md §5) is enforced by this analyzer plus audit. But `forbiddenSuffixes` is seven entries:
```fsharp
let private forbiddenSuffixes : (string * string) list =
    [ "DateTime","Now"; "DateTime","UtcNow"; "DateTime","Today"
      "DateTimeOffset","Now"; "DateTimeOffset","UtcNow"
      "Guid","NewGuid"; "Random","Shared" ]
```
It matches a two-part long-id *suffix*, which structurally **cannot** catch: `new System.Random(seed)` (constructor, not `.Shared`), `Stopwatch.StartNew()`/`GetTimestamp()` (the very thing `Bench` uses — nothing stops a *non-Bench* Core module grabbing one), `Environment.GetEnvironmentVariable` (the classic config leak), `File.*`/`Directory.*`/`Stream` writes, and — critically — **any `Task`/`Async`** (the "no Task/Async in Core" half of the law has *zero* analyzer coverage; it is audit-only). The docstring recites the full law; the implementation covers a sliver. The 2026-06-02 widening (adding `DateTimeOffset`) is itself the evidence: a whole family slipped through for months because the list is hand-maintained and name-shaped. Core is clean *today*, so a stricter analyzer should pass on first run — that's the proof it's worth doing.

**Proposed refactor.** Add `Stopwatch`/`StartNew`, a `SynExpr.New` walk for `Random`, `Environment`/`GetEnvironmentVariable`, `File`/`Directory`/`Path` to the list, with a `Bench.fs`/`PinnedWriting.fs` allowlist (the analyzer already special-cases by file path under Core — invert it to *allow* the two sanctioned files for IO/Stopwatch specifically).
**Extra-large version.** Stop name-matching entirely. Run over the **typed tree** (`ctx.TypedTree` / FCS symbol-use) and forbid any Core symbol resolving to `System.IO`, `System.Threading.Tasks`, `System.Diagnostics.Stopwatch`, `System.Random`, `System.Environment`, plus the time members — with a typed allowlist for `Bench`. The difference between "we banned the strings we remembered" and "we banned the capabilities." Also closes the `Task`/`Async` gap.

**Blast radius.** Analyzer + its test project only; no production change (Core is currently clean → green on first run).

**What it buys.** The §5 boundary law becomes machine-enforced instead of audit-enforced; the recurring "analyzer gap pre-Slice-0" scar (cited three times in Core comments) stops recurring. **Effort: S for the suffix additions, XL for the typed-tree rewrite.**

---

## 7. 🟧 Reify an `FkGraph` build-once, retiring 4 copies of adjacency construction

> **Status (2026-06-27):** ✅ **Done (the genuine consolidation); the full reification DECLINED out loud.** The actual duplication — the inline `addNeighbor` undirected fold, copied across `BoundedContextPass` + `TopologicalOrderPass` island-detection — was already consolidated into `TopologicalOrder.undirectedAdjacency` (`57a388ec`, 2 consumers, dedup divergence retired). The recon's "4 copies" framing is stale: of the four, two WERE the same undirected view (now one), and the other two are **deliberately distinct views with documented reasons** (read the `undirectedAdjacency` docstring, which the partial landing wrote): (a) `CentralityPass.buildAdjacency` builds the *directed-reverse* adjacency + out-degree as a mutable `Dictionary` — a **PageRank perf carve-out**, built once and reused across iterations; (b) `runCascadeShockZones.cascadeAdj` is **classifier-filtered** (only `CycleResolution.classify … = Cascade` edges survive), so it needs the `Catalog` + the classifier, NOT just `Edges` — it cannot be an edges-only `FkGraph` method. The passes compute *different* graphs each needed once; there is no redundant re-fold to collapse. A unifying `FkGraph` carrying Forward/Reverse/OutDegree/Undirected would be a **zero-consumer symmetry-build** — the exact dead-algebra anti-pattern §5's retirement precedent forbids. **Raising this for the record (the #4-combinator model): declined, with reasons; the partial + the two carve-outs are the correct end state.**

**Anchors:** `CentralityPass.fs:60–70` (`buildAdjacency` — out-degree + reverse-adjacency), `BoundedContextPass.fs:37–48` (`addNeighbor` + `buildUndirectedAdj`), `TopologicalOrderPass.fs:631–638` (`runIslandDetection`'s inline `addNeighbor` + `undirected`), `TopologicalOrderPass.fs:746–764` (`runCascadeShockZones`'s `cascadeAdj`).

**The tension/smell.** Four of the five graph-analytics passes consume `TopologicalOrder.Edges : (SsKey*SsKey) list` and each **re-derives an adjacency map from scratch**, with a near-identical `addNeighbor` defined inline three separate times:
```fsharp
// BoundedContextPass.fs:37 — dedups
let private addNeighbor m a b =
    let existing = Map.tryFind a m |> Option.defaultValue []
    if List.contains b existing then m else Map.add a (b :: existing) m
// TopologicalOrderPass.fs:631 — function-local redefinition, does NOT dedup
let addNeighbor m a b =
    let existing = Map.tryFind a m |> Option.defaultValue []
    Map.add a (b :: existing) m
```
The dedup-vs-no-dedup divergence is itself a smell (is it load-bearing in one and accidental in the other? it could change island/centrality output if the edge set ever contains duplicates). `CentralityPass` builds the *reverse* adjacency + out-degree; `runCascadeShockZones` builds a *classifier-filtered* adjacency. All four are "fold edges into `Map<SsKey, SsKey list>`."

**Proposed refactor.** An `FkGraph` module in Core (next to `TopologicalOrder.fs`) reifying the edge set once:
```fsharp
type FkGraph = private { Forward: Map<SsKey,SsKey list>; Reverse: …; OutDegree: …; Undirected: … }
module FkGraph =
    let ofEdges : (SsKey*SsKey) list -> FkGraph                       // canonical, sorted, dedup-defined-once
    let neighbors / reverseNeighbors / undirectedNeighbors / outDegree / reachableVia (pred) …
```
**Extra-large version.** Make `TopologicalOrder` *carry* the `FkGraph` (build it once during the topology pass; the four downstream passes read it). This is the "discover-once, derive-pure / Big-O audit at the second derivation" discipline §6 cites — the second derivation here happened four times.

**Blast radius.** Medium: 4 pass bodies + `TopologicalOrder.fs` (if reified onto the record). Pure-core. All four passes have dedicated tests; resolve the dedup divergence deliberately.

**What it buys.** One definition of FK adjacency; the dedup semantics decided once; per-pass `O(edges)` re-fold collapses to one build. The most-duplicated logic in the pass tree. **Effort: M (L if reified).**

---

## 8. 🟧 Promote a shared identifier-quoting primitive (fixes a real `]`-escape bug)

> **Status (2026-06-27):** ✅ **Done.** A Core `SqlIdentifier` module (`quote` + `qualified`) is now THE single Core-reachable identifier quoter — `[ … ]` with doubled `]`. It's **byte-verified ≡ ScriptDom's `Identifier.EncodeIdentifier`** (`SqlIdentifierTests` compares against SSDT's `Render.quote` across the identifier class, incl. `]`-bearing + unicode). Routed: the `LogicalColumnEmission` Core pass (which **fixes its latent `]`-escape bug** — the prior inline `String.Concat("[", s, "]")` didn't escape), `RemediationEmitter.brackets`/`qualifiedTable`, and ReadSide's 3 + LiveProfiler's 1 `encode = EncodeIdentifier` rebindings. `Render.quote` stays the vendor oracle the Core primitive is verified against. Build clean; pure pool 3744/0; Docker pool clean (the SQL-emission + read-leg byte-output paths verified).

**Anchors:** canonical `Render.quote` (`SSDT/Render.fs:45`, wrapping `Identifier.EncodeIdentifier`); local rebindings `ReadSide.fs:895`, `LiveProfiler.fs:181`; **hand-rolled wrong copies** `RemediationEmitter.fs:48` (`brackets`), `LogicalColumnEmission.fs:101–102` (`String.Concat("[", physical, "]")`).

**The tension/smell.** The canonical quoter lives in SSDT's `Render`, but it's needed Core-wide. Adapters that can't depend on SSDT either rebind `Identifier.EncodeIdentifier` locally (fine — same function) or **re-hand-roll it**:
```fsharp
let private brackets (s: string) : string = System.String.Concat("[", s, "]")   // RemediationEmitter.fs:48
```
`EncodeIdentifier` doubles a `]` inside the name per T-SQL spec; `brackets` does **not** escape `]`. A table/column named `Foo]Bar` emits `[Foo]Bar]` — broken (and unsafe) SQL. `RemediationEmitter` *ships SQL to operators* (`manifest.remediation.sql`), so this is the cross-site quoting inconsistency made concrete as a real escape-semantics divergence. Only saved today because OutSystems physical names don't contain `]`.

**Proposed refactor.** Promote a `SqlIdentifier.quote`/`SqlIdentifier.qualified` primitive to a low layer every emitter can reach. Core can't take a ScriptDom dep (purity law), so either a Core-level escaper that doubles `]`, **byte-verified against `EncodeIdentifier`**, or a small shared adapter module. Replace `RemediationEmitter.brackets`, `LogicalColumnEmission`'s bracket literals, and the local `encode` rebindings with it.

**Blast radius.** Wide but mechanical (grep `EncodeIdentifier` + `"[", ` + `brackets`); each replacement is identifier-identical for current names → goldens stable. The Core purity constraint makes this M not S.

**What it buys.** One quoting law instead of three implementations (one of which is wrong on `]`); the structural fix that makes the `RemediationEmitter` and `LogicalColumnEmission` sites safe permanently. **Effort: M.**

---

## 9. 🟧 Co-locate exit-code semantics with the refusal-code mint sites (code→exit registry)

> **Status (2026-06-27):** ○ **Not started.** (The Binding registry from #2 now provides the natural seam — code→exit is a `ConfigAxis`-style registry.)

**Anchors:** `Preflight.fs:566` (`classify (code: string) : int * GateLabel` — a 30-line `if code.StartsWith "transfer.connection" … elif code = "transfer.insufficientGrant" …` chain); mint sites far away, e.g. `TransferRun.fs:69` (`"transfer.reverseLeg.streamingTablesUnsupported"`).

**The tension/smell.** The exit-code is *the* operator contract, but the binding code→exit lives in a prefix-matching chain that re-parses strings the producers spelled by hand. Add a new `transfer.*` refusal and forget to extend `classify` → it silently falls to `(3, UnclassifiedRefusal)`. That default is fail-loud (good), but the coupling is by string convention, not by type; the `ValidationError.create "transfer.reverseLeg.…"` sites carry no exit knowledge.

**Proposed refactor.** A refusal-code registry (DU or `Map<code, exit*label>`) **co-located with the code definitions**, so minting a refusal and its exit-code are one declaration; `classify` becomes a lookup; the prefix-collapse (`migrate.*` and `transfer.*` of the same axis classifying identically) becomes explicit aliasing. Natural to build on the `ConfigAxis`/registry seam from Finding 2.

**Blast radius.** M — touches the classifier + the mint sites + exit-code tests.

**What it buys.** code↔exit is total and greppable; new refusals can't silently fall through; the operator contract stops living in a string-prefix `if/elif`. **Effort: M.**

---

## 10. 🟧 Move `parseSemanticType` (the OutSystems→V2 type decisions) out of the adapter into Core

> **Status (2026-06-27):** ✅ **Done.** The OSSYS→V2 type-correspondence DECISIONS — the 2000-char `(MAX)` threshold, `currency → DECIMAL(37,8)`, the imposed V1-parity `email`/`phone` widths, `longinteger → BIGINT`, the legacy `datetime → DATETIME`, the reference-storage convention — plus `textLength`/`boundedOr` now live in a pure `Projection.Core.OssysTypeMapping` (`tryParse : … -> (PrimitiveType * SqlStorageType) option`). The adapter keeps exactly what's the boundary's: raw-string hygiene (`normalizeAttributeType`) and turning `None` into its `adapter.osm.unmappedDataType` refusal (the error vocabulary stays at the edge; the *mapping data* moved to Core). `tryParse` returning `option` (rather than a Core error) is the clean cut — and a step toward the "mapping as data" XL variant. New `OssysTypeMappingTests` pin the decisions WITHOUT an OSSYS fixture (the recon's headline payoff); the existing OSSYS differential/comprehensive suites confirm byte-identical end-to-end. Build clean; pure 3744/0; Docker clean.

**Anchors:** `src/Projection.Adapters.Osm/OssysTranslation.fs:330–394` (`parseSemanticType`), `:302–310` (`textLength`), `~287–300` (`normalizeAttributeType`); callers `OssysJsonReader.fs:76–78`, `OssysRowsetReader`; the Core vocabulary it produces, `PrimitiveType`/`SqlStorageType`/`SqlTypeCorrespondence.fs`.

**The tension/smell.** `parseSemanticType` is a pure total `string -> int option -> int option -> int option -> Result<PrimitiveType * SqlStorageType>` encoding the *entire* OutSystems-type → V2-type correspondence:
```fsharp
| "longinteger"    -> Result.success (Integer, SqlStorageType.BigInt)
| "datetime"       -> Result.success (DateTime, SqlStorageType.DateTime)   // legacy, NOT DateTime2
| "currency"       -> Result.success (Decimal, SqlStorageType.Decimal (37, 8))
| "email"          -> Result.success (Text, SqlStorageType.VarChar (boundedOr (Bounded 250) length))  // IMPOSED V1-parity
| "phonenumber" | "phone" -> … (Bounded 20) …
```
These are **decisions, not translations**: the 2000-char `MAX` threshold (`textLength`), the `currency → DECIMAL(37,8)` choice, the imposed 250/20 email/phone widths (explicitly flagged "IMPOSED V1-parity", `:362–368`). Core already owns the vocabulary; the mapping that *chooses* between values belongs next to `SqlTypeCorrespondence`, where it can be property-tested without an OSSYS fixture and reused by a second source adapter (rowset reader, a future XML reader). The adapter's job is "hand the raw type string to Core's classifier" — right now the adapter *is* the classifier.

**Proposed refactor.** Move `parseSemanticType` + `textLength` + `boundedOr` into a pure `Projection.Core.OssysTypeMapping` (or fold into `SqlTypeCorrespondence`); the adapter keeps only `normalizeAttributeType` (string hygiene on raw input — arguably still boundary) and calls Core.
**Extra-large version.** Make the mapping *data*, not code — a `Map<string, TypeRule>` in Core that both readers consume, with the imposed-width entries carrying an `Imposition` tag so the optional per-imposition diagnostic (foreseen at `:367`) falls out for free.

**Blast radius.** `OssysTranslation.fs` callers + one new Core module; goldens unaffected (pure move, byte-identical output).

**What it buys.** Testability without DB/JSON fixtures; the type correspondence becomes reusable across every present and future source adapter; the imposed-width policy becomes visible to Core where intent-filtering lives. **Effort: M.**

---

## 11. 🟧 Finish the Voice migration in `RunFaces` and unify `Voice.fs`'s two dispatchers

> **Status (2026-06-27):** ○ **Not started.**

**Anchors:** `RunFaces.fs` (~121 raw-prose emitters vs ~103 voiced — a ~50% migration); duplicated drop-warning literal `RunFaces.fs:660` **≡** `:2454`; un-voiced faces `narrateTransferReport :575–649`, `runApprove :524–544`, `runInspect :1364–1379`, `runPolicyDiff :1706–1717`, `runSuggestConfig :1659–1684`, migrate execute-leg `:2092–2106, :2221–2237`; `Voice.fs:802` (`lookup`, O(n) `List.tryFind`, no duplicate-code detection), `Voice.fs:864–913` (`errorFrame` — 12-branch ordering-dependent `if/elif`, **not in `all`** → untested).

**The tension/smell.** The stated law is "sites emit codes, the Voice owns copy." `RunFaces` is mid-migration: newer faces (`runDeploy`, `runCanary`, `runDrift`, `runEject`) route fully through `TtyRenderer.renderVoicedTo "code"`; older ones emit English inline (`printfn "Preview — %d row(s) would move…"`, `"%d relationship cycle(s) cannot be broken…"`). The drop-warning sentence — `"%d row(s) would be dropped — a relationship points to an unmatched record. Pass --allow-drops…"` — is **duplicated verbatim** at two call sites with no shared code. Some faces mix registers (error path voiced, success path raw `printfn "Applied and verified…"`). Separately, `Voice.fs` has a *second* dispatcher, `errorFrame`, with ordering-dependent shadowing (`code.Contains "connection"` at `:905` must stay below the `transfer.connection.*` prefixes at `:875`/`:881`) that the totality test never reaches because it isn't in `all`.

**Proposed refactor.** Register the transfer/migrate/approve/inspect prose as Voice codes (`transfer.preview`, `transfer.loadPlan`, `transfer.rowsDropped`, `migrate.applied`, …) and route through `renderVoicedTo`; the duplicated drop-warning collapses to one `transfer.rowsDropped`. Unify `errorFrame` onto the "total typed projection over a closed DU" pattern that `gateStatement`/`migrationStopDetail` already prove works *in that very file*, and put it in `all` so `code ⇔ copy` covers it.
**Note:** confirm exactly which surfaces `VoiceTotalityTests` reaches before acting on the "untested" claims (inferred from `all` being the test's input).

**Blast radius.** `Voice.fs` (new entries + the `code ⇔ copy` test) + the named faces. The duplicated `:660`/`:2454` literal is an S quick-win on its own.

**What it buys.** Closes the single largest register-drift gap; the whole transfer/migrate surface becomes testable copy; one error dispatcher. **Effort: L (the dup-literal collapse alone is S).**

---

## 12. 🟧 A `fanOutWithDiagnostics` primitive — retire the decision→diagnostic tail copied across the 3 tightening passes

> **Status (2026-06-27):** ✅ **Done.** `Composition.fanOutWithDiagnostics config benchLabel decisionsOf toDiagnostic catalog policy profile : Lineage<Diagnostics<'decisionSet>>` added — it runs `fanOut`, maps each decision through `toDiagnostic`, `List.choose id`s, and splices the entries into the dual writer inside the primitive. The identical tail at NullabilityPass / UniqueIndexPass / ForeignKeyPass `run` (the `iterMap`/`choose`/`lineageDiagnostics{writeDiagnostics}` boilerplate) collapses to one call each; only the bench label, the `opportunityEntry`, and the `(fun ds -> ds.Decisions)` projection vary. The observable-identity-on-empty-policy guarantee is inherited from `fanOut`; writer-fidelity now lives inside the primitive rather than being re-asserted at three sites. Build clean; Composition + the three passes' tests green (the diagnostic-stream tests pass unchanged).

**Anchors:** `NullabilityPass.fs:133–205, 255–263`, `UniqueIndexPass.fs:113–146, 190–198`, `ForeignKeyPass.fs:186–271, 323–331`; the primitive it bolts onto, `Strategies/Composition.fs` (`fanOut`, `FanOutConfig`, deferral pattern documented `:18–32`).

**The tension/smell.** All three diagnostic-emitting tightening passes share an identical *tail*: run `fanOut`, map each decision through an `opportunityEntry : decision -> DiagnosticEntry option`, `List.choose id`, splice into the dual writer:
```fsharp
// repeated verbatim in Nullability:259, UniqueIndex:194, ForeignKey:327
let entries =
    lineage.Value.Decisions |> Bench.iterMap "pass.<x>.<grain>" (opportunityEntry …) |> List.choose id
lineageDiagnostics {
    let! value = lineage
    do! LineageDiagnostics.writeDiagnostics entries
    return value
}
```
Only the Bench label, the `opportunityEntry` function, and whether it closes over `profile` vary. `FanOutConfig` deliberately produces only `Lineage<'decisionSet>` (no diagnostics) — so the diagnostic layer is bolted on *outside* the primitive at three call sites with the same boilerplate. The deferral trigger ("second consumer of the diagnostic tail") fired at UniqueIndex and was paid down everywhere *except* this shared tail.

**Proposed refactor.** Extend the fan-out vocabulary: `Composition.fanOutWithDiagnostics : FanOutConfig<…> -> benchLabel:string -> (decision -> DiagnosticEntry option) -> Catalog -> Policy -> Profile -> Lineage<Diagnostics<'decisionSet>>`, folding the `iterMap`/`choose`/`writeDiagnostics` tail into the primitive. The three passes' `run` bodies shrink to a `FanOutConfig` + an `opportunityEntry`.

**Blast radius.** 3 pass `run` bodies + `Composition.fs`; `decisionsOf`/`opportunityEntry` stay put. Pure-core; covered by the per-pass tests + `CompositionTests`.

**What it buys.** The decision→diagnostic-stream production becomes a named primitive (currently an un-abstracted convention across 3 files); writer-fidelity is enforced inside the primitive rather than re-asserted per call. **Effort: M.**

---

## 13. 🟧 One connection-acquisition discipline (`ConnectionSpec.open` + `Substrate.sourceFromRef`; collapse `LiveModelRead` onto the `Source` port)

> **Status (2026-06-28):** ◑ **Partial — the opener seam landed.** New `ConnectionSpec.openSpec (role) (label) (spec) : Task<Result<SqlConnection>>` (a small I/O Pipeline module beside the pure `TransferSpec` parser, since `TransferSpec` is deliberately pure and `ConnectionResolver` is in Adapters.Sql below `parseConnectionSpec`) is the ONE home for the `env:`/`file:`/`live:`/bare decode. The recon's primary anchor — `SliceExtractRun.openSource` ≡ `SliceApplyRun.openTarget`, byte-identical but for role+label — collapses onto it (each is now a one-liner). Pure dedup, zero behavior change; build clean, pure pool 3748/0, the 379 slice tests (incl. the Docker-backed connection-opening ones) green. **Open (the XL remainder):** fold the `env:`/`file:`-only inline sites (`ProfileCaptureRun`/`SyntheticLoadRun`/`Hydration`/`LiveModelRead`) through `openSpec` so their `live:` coverage stops drifting (a deliberate behavior change — Docker-verify); a `Substrate` factory for the inline `{ Environment = Named label; Role; ConnectionRef }` construction (~6 sites); and the headline `LiveModelRead` 5-overload → `Source`-port collapse so there is ONE port for "where a catalog comes from."

**Anchors:** `SliceExtractRun.fs:35` (`openSource`) **≡** `SliceApplyRun.fs:91` (`openTarget`) (byte-identical except `SubstrateRole`; `:90` says *"same spec forms as SliceExtractRun.openSource"*); the `env:`/`file:`/`live:`/bare decode in 6 sites with drifting coverage (`ProfileCaptureRun.fs:30–37`, `SyntheticLoadRun.fs:158–162`, `MovementSurface.fs:1075`, `Source.fs:134`); inline `Substrate{Role=Source…}` reconstruction in `Hydration.fs:128–135`, `LiveModelRead.fs:100–108`, +5 run modules; the 5-overload family `LiveModelRead.fs:25–113`; the *good* model `Source.fs` (record-of-functions port).

**The tension/smell.** Two patterns for "get a live connection" coexist: `ConnectionResolver.openSubstrate` (the seam) and raw `new SqlConnection(connStr); OpenAsync()` (inside `Source.ofLive`/`ofOssys`, which also opens a *second* connection for profiling at `Source.fs:215`). The `env:`/`file:`/`live:` decode appears in 6 sites with subtly different coverage — *some handle `live:`, some don't* — a latent inconsistency. The `Substrate` value is reconstructed inline everywhere with only the `Environment` label varying; there's no `Substrate.sourceFromRef` factory. And the Pipeline seams don't share a shape vocabulary: `EmissionSeam` is a function port, `Source` is a record-of-functions port (the *good* one — an in-memory/synthetic source slots in), `Hydration` is a bare async pair, `LiveModelRead` is a 5-overload ladder leaking `SqlConnection` into four signatures with caller-owned `use cnn`.

**Proposed refactor.** Add `ConnectionSpec.open (role: SubstrateRole) (label: string) (spec: string) : Task<Result<SqlConnection>>` beside `ConnectionResolver` (folding the `live:`/bare decode into one tested place) and `Substrate.sourceFromRef : label -> ConnectionRef -> Substrate`; replace the 6+ inline sites. Collapse `LiveModelRead`'s overloads to one `params -> ConnectionRef -> Task<Result<Catalog>>` that owns acquisition+disposal via `ConnectionResolver`.
**Extra-large version.** Make every catalog source a `Source` (the existing record-of-functions port). `LiveModelRead` becomes `Source.ofLive`'s implementation detail; `ModelResolution.resolveCatalog` returns a `Source` and reads it, so there's *one* port for "where does a catalog come from," and live/file/synthetic become uniformly substitutable. Note: the byte-identical `drainRows`/`drainReader` (`ReadSide.fs:276` ≡ `LiveProfiler.fs:82`) is a *consciously-deferred* refusal (`LiveProfiler.fs:80–81` names it) — fold it into this kernel when a third consumer appears.

**Blast radius.** Wide but mechanical and compiler-guided (6+ call sites + `LiveModelRead` callers); low test impact.

**What it buys.** One connection discipline; pool-sizing/timeout policy applies uniformly; the "did this site set Role=Source?" and "did it handle `live:`?" failure modes vanish; one source port. **Effort: S→M for the helpers; M→XL for full `Source`-ification.**

---

## 14. 🟨 Close the `DerivationReason` set — the last open-string field that participates in *identity*

> **Status (2026-06-27):** ✅ **Done (the aggressive DU, by operator decision).** `DerivedFrom`'s `reason` is now a closed `DerivationReason` DU (single case `Inverse`). `SsKey.derivedFrom` is **total** (`SsKey -> DerivationReason -> SsKey`) — the prior `Result` + blank-reason rejection is gone (a malformed reason is unconstructable by type); `SymmetricClosure.deriveInverseKey` drops its unreachable error branch. `derivationReasons : DerivationReason list`; `Reference.isInverse` is a total match. **Codec byte-identical**: `DerivationReason.serialize Inverse = "inverse"` (same length-prefixed token), and `deserialize` routes through `DerivationReason.parse` so an unknown *stored* token now fails loud instead of materializing silently. This was the item I'd **held for an operator call** (it changes an AxiomTests Bucket-B contract + the DECISIONS "reserved reasons" surface, ahead of the original "second reason in use" trigger): **AXIOMS A5 amended** (reason is a closed DU; blank-rejection structural) + its Bucket-B citation, and a dated **DECISIONS amendment** (2026-06-27) added, both in-commit per §5. The two tests that exercised the open-string generality (a second free reason `"shadow"`; the blank rejection) were re-pointed to the closed model. Verified: build clean; pure pool **3723/0**; Docker pool **273/273** (round-trip canaries confirm byte-identity — the one transient CDC-enablement deadlock passed clean on isolated re-run).

**Anchors:** `Identity.fs:54` (`SsKey.DerivedFrom(reason: string)`); the only reserved value `Catalog.fs:1407` (`inverseDerivationReason = "inverse"`), matched `Catalog.fs:1414` (`when reason = inverseDerivationReason`); consumers `derivationReasons`, `isInverse`, `SymmetricClosure`.

**The tension/smell.** The docstring says "Reserved derivation reasons are enumerated in `DECISIONS.md`" and today there is exactly **one** (`"inverse"`). The reason is structural — it participates in deployability filtering and `isInverse` — yet it is an open `string`. A typo in a future pass mints a silently-different identity. In a system whose whole thesis is "identity is a type" (`SsKey`, `Name`, coordinates as VOs), this is the one remaining open-string field in identity itself.

**Proposed refactor.** Conservative: funnel all construction through named constants (already half-done via `inverseDerivationReason`). Aggressive (high conceptual payoff for S effort): replace `reason: string` with a closed `DerivationReason` DU (`Inverse | …`). `isInverse` becomes a total pattern-match instead of a string compare; unregistered reasons become unconstructable — the `ReferenceAction`/`Origin` treatment already applied elsewhere.

**Blast radius.** The serializer (`Identity.fs` `serialize`/`parse` — already length-prefixes the field, so the wire change is mechanical), `SymmetricClosure`, any future deriving pass.

**What it buys.** Unforgeable derivation provenance; total `isInverse`; closes the set that participates in identity conservation. **Effort: S→M.**

---

## 15. 🟨 A `LineageEvent.forPass` smart constructor — retire the 5-field event literal re-declared in ~10 passes

> **Status (2026-06-27):** ✅ **Done.** `LineageEvent.forPass passName version classification : SsKey -> TransformKind -> LineageEvent` smart ctor added to `Lineage.fs` (alongside the type, `[<RequireQualifiedAccess>]`). The 16 hand-written 5-field record literals — `touchedEvent`/`decisionEvent`/`createdEvent`/`skippedEvent`/`removedEvent`/`renamedEvent`/`matchedEvent`/`substitutedEvent`/`physicallyRenamedEvent` across 13 passes (Nullability, UniqueIndex, ForeignKey, CategoricalUniqueness, Canonicalize, NormalizeStatic, NamingMorphism, SymmetricClosure, VisibilityMask, UserFkReflow, LogicalColumn/TableEmission, TableRename, TopologicalOrder) — now call it, so the A23 invariant (every event carries `PassVersion` + `Classification`) is enforced at one site. Pure record construction, no behavior change; build clean, pass tests green.

**Anchors:** `decisionEvent` in `NullabilityPass.fs:73`, `UniqueIndexPass.fs:78`, `ForeignKeyPass.fs:93`, `CategoricalUniquenessPass.fs:75`; `touchedEvent` in `NormalizeStaticPopulations.fs:57`, `CanonicalizeIdentity.fs:101`, `TopologicalOrderPass.fs:407, 719`; `removedEvent`/`renamedEvent`/`createdEvent`/`skippedEvent`/`substitutedEvent`/`physicallyRenamedEvent`/`matchedEvent` across the rest; the analytics-family precedent that already does this internally, `Diagnostics.fs:441–448` (`touchedEpilogue`).

**The tension/smell.** Every pass hand-writes the same 5-field record literal:
```fsharp
let private touchedEvent (key: SsKey) : LineageEvent =
    { PassName = passName; PassVersion = version; SsKey = key
      TransformKind = Touched; Classification = classification }
```
The four tightening passes' `decisionEvent` are identical except which typed `AnnotationDetail` variant wraps the outcome. The `LineageEvent`'s own A23 docstring worries about exactly this drift — `PassVersion` + `Classification` are a convention re-typed 20+ times. The abstraction *exists* for the analytics family (`touchedEpilogue`) but wasn't generalized.

**Proposed refactor.** A `LineageEvent.forPass passName version classification : SsKey -> TransformKind -> LineageEvent` smart ctor (or per-pass partial application `mkEvent = LineageEvent.forPass passName version classification`); every `touchedEvent`/`createdEvent`/etc. becomes `mkEvent key Touched`.

**Blast radius.** Low risk, wide touch (~10 files, mechanical, `replace_all`-style). Pure record construction; no behavior change.

**What it buys.** Kills the most-repeated boilerplate in the pass tree; one site enforces that every event carries `PassVersion` + `Classification`. **Effort: S→M (mechanical breadth).**

---

## 16. 🟨 Unify the three private JSON-read helper copies into one `JsonRead` module

> **Status (2026-06-27):** ○ **Not started.**

**Anchors:** `Config.fs:637` (`getString`), `:651` (`getOptionalString`), `:692` (`getIntOr`), etc. (a clean private `JsonElement → Result<_>` layer with `configError` codes); duplicated in `MigrationDependenciesBinding.fs:62` (`tryNonBlankString`), `:89` (`cellValue`), `:103` (`parseValues`); a third copy admitted by `Config.fs:619`'s docstring — *"Mirror `CatalogReader`'s private helpers"* — in `CatalogReader.fs`.

**The tension/smell.** `Config.fs` has a clean private JSON-helper layer (returning `Result<_>` with named codes). Because the helpers are `private`, the migration-deps binder — which also parses a JSON file — rebuilds its own `tryNonBlankString`/`cellValue`/scalar-projection from scratch, with its own `bindError "cellNotScalar"`. Two (really three) JSON-shape vocabularies for the same primitives, each with its own null/type-mismatch convention.

**Proposed refactor.** Promote the `Config.fs` JSON helpers to an internal `module ConfigJson` (or `JsonRead`) the whole Pipeline shares.
**Extra-large version.** Unify with the third copy in `CatalogReader` (the docstring already names the mirroring) — one `JsonRead` module retires all three.

**Blast radius.** M (the `CatalogReader` unification pushes to L); one error vocabulary across config + migration-deps + catalog reading.

**What it buys.** One JSON-error vocabulary, one null/typeMismatch convention; pairs naturally with Finding 2's error consolidation. **Effort: M→L.**

---

## 17. 🟨 `Comparison.fs` (775) — pull domain risk-classification out of the CLI render layer; kill the build-string-then-reparse

> **Status (2026-06-27):** ○ **Not started.**

**Anchors:** `Comparison.fs:291–311, 328–385` (`attrFacetRewrites`/`refFacetRewrites`/`idxFacetRewrites` — which schema transitions are data-destructive); `:413–416` (`keepChannel`), `:500–507` (`moduleOfItem`) — stringly self-parsing; `:351–380, :560–595` — 4× duplicated reshape-collector skeleton; `:448–473, :636–658` — duplicated grouped-disclosure assembly.

**The tension/smell.** `Comparison.fs` is four modules wearing one hat: the `Comparison<'a,'delta>` capability type, Core diff-traversal + name resolution, **domain risk classification**, and View string-assembly. The risk predicates (`attrFacetRewrites` deciding which transitions are data-destructive) are a domain concern the apply/migrate path also needs — stranded in the CLI render module. Worse, the module `sprintf`s `"column Customer.Email"` then **re-parses it** with `.Split(' ')`/`.StartsWith`/`IndexOf('.')` to filter by channel — "build a string then regex it back." The reshape-collector skeleton (`Map.toList |> collect (find source/target; match Some,Some)`) is repeated per channel, and the grouped-disclosure algorithm ("flat lane if ≤ threshold, else grouped tree with `(-count, name)` tie-break") is written twice.

**Proposed refactor.** Move the risk predicates to Core as `CatalogDiff.dangers : CatalogDiff -> RiskItem list` returning typed risk (not pre-formatted strings) — property-testable and reusable by migrate. Introduce a `LaneItem = { Channel; Module; Text }` record to kill the self-parse. Collapse the 4× reshape collectors into one `ChannelSpec`-driven loop (~80 lines); extract the grouped-disclosure assembly once.

**Blast radius.** `Comparison.fs` + the migrate path (which gains the typed risk); CLI render tests.

**What it buys.** Risk classification becomes property-testable + reusable; no more string-then-regex; ~80+ LOC of channel duplication gone. **Effort: L.**

---

## 18. 🟨 De-hardcode the escaped config knobs (the `AdvisoryTuning` precedent missed three)

> **Status (2026-06-27):** ◑ **Partial.** The lone-holdout analytics knob landed: `BoundedContextPass.maxPropagationRounds` now reads `AdvisoryTuning.defaults.BoundedContext.MaxPropagationRounds` (new `BoundedContextTuning` field), exactly like its `Centrality` sibling — byte-identical (still 50), the one-line exact-precedent fix. **Open (the M SQL-knob variant, deliberately separate):** `ReadSide.maxRows = 100_000` (the load-bearing static/streaming boundary — best modeled as a Core `Modality.classify : rowCount -> Modality` predicate the adapter merely applies, which also defuses the survival-rule-#8 "clear the Static marking" friction) and `ClosureOracle.fuel`. These touch the SQL adapter / the 4.4-trap boundary and warrant their own focused, Docker-verified treatment.

**Anchors:** `BoundedContextPass.fs:32` (`let private maxPropagationRounds : int = 50` — the lone holdout); the precedent every sibling follows, `AdvisoryTuning.defaults` (consumed by `CentralityPass.fs:37`, `ProfileAnomalyPass.fs:32`, `SchemaComplexityPass.fs:35`); `ReadSide.fs:1910` (`let maxRows = 100_000`); `ClosureOracle.fs:132` (`let mutable fuel = 100000`).

**The tension/smell.** The codebase already has the right pattern — `AdvisoryTuning.defaults` single-sources Centrality/ProfileAnomaly/SchemaComplexity tuning, overridable. Three knobs escaped it. `BoundedContextPass.maxPropagationRounds` hardcodes its loop bound while every sibling reads from `AdvisoryTuning`. `ReadSide`'s `maxRows = 100_000` is especially load-bearing — it's the static/streaming round-trip boundary (the bootstrap-MERGE scale ceiling), baked into the reconstruction loop as a bare literal, and operator preference says config is primary. `ClosureOracle`'s `fuel` is a bare closure-fuel ceiling. This is the materialized form of the CLAUDE.md "4.4 trap": the *decision* "a kind under 100k rows is `Static`" is a classification policy baked into the SQL reader (and survival-rule #8 then requires *clearing* that marking before profiling — the adapter's decision actively fights a downstream pass).

**Proposed refactor.** Move `maxPropagationRounds` into `AdvisoryTuning.defaults.BoundedContext` (one-line, exact precedent). Surface `ReadSide`'s `maxRows` and `ClosureOracle`'s `fuel` as config (the `emission`/`profiler` section already carries `MaxRowsPerKind`-shaped knobs at `EvidenceCache.fs:218`).
**Refactor (M variant for ReadSide).** Model the decision as a Core predicate `Modality.classify : rowCount:int -> Modality`, so "what makes a kind Static" is one testable Core function the adapter merely *applies*, and the "clear the Static marking" trap becomes a single inverse call.

**Blast radius.** `BoundedContextPass` is one line; the two SQL-adapter knobs need config threading.

**What it buys.** Removes three magic numbers (one a load-bearing scale boundary); honors config-primary; defuses survival-rule-#8 friction; the modality decision becomes testable. **Effort: S for BoundedContext, M for the SQL knobs.**

---

## 19. 🟨 A `Fixpoint.iterate` combinator — two hand-rolled mutable convergence loops

> **Status (2026-06-27):** ✅ **Done.** `Fixpoint.iterate (maxIters) (step: 's -> 's * bool) (seed) : 's * int` added to Core (leaf, BCL-only, beside `Statistics`). `CentralityPass.runUntilConverged` (PageRank) and `BoundedContextPass.labelPropagation` collapse to one call each; the `ProfileAnomaly` Newton-sqrt — the same scheme with no convergence test — folds in too (the step reports `false`, so the loop is a pure 20-iteration cap). Three `let mutable` triads retired into the one named combinator. Behavior-preserving; build clean, the three passes' tests green.

**Anchors:** `CentralityPass.fs:117–126` (`runUntilConverged` — PageRank), `BoundedContextPass.fs:110–122` (`labelPropagation`); structurally also the Newton sqrt loop `ProfileAnomalyPass.fs:50–54`.

**The tension/smell.** Two passes independently implement the same "iterate a step to a fixed point or a max-iteration cap" mutable driver:
```fsharp
// CentralityPass.fs:117
let mutable rank = initial; let mutable iterations = 0; let mutable converged = false
while not converged && iterations < maxIterations do
    let newRank, maxDelta = pageRankStep …
    rank <- newRank; iterations <- iterations + 1
    if maxDelta < convergenceEps then converged <- true
// BoundedContextPass.fs:110
let mutable labels = initialLabels nodes; let mutable changed = true; let mutable rounds = 0
while changed && rounds < maxPropagationRounds do
    rounds <- rounds + 1
    let newLabels, didChange = propagateOnce …
    labels <- newLabels; changed <- didChange
```
The same recursion scheme wearing two mutable skins, and (unlike the Tarjan/Kahn perf carve-outs) these mutate for *no perf reason*.

**Proposed refactor.** `Fixpoint.iterate : maxIters:int -> step:('s -> 's * bool (*converged*)) -> seed:'s -> 's * int`. PageRank's step returns `(newRank, maxDelta < eps)`; label-prop's returns `(newLabels, not changed)`. Both drivers collapse to one call; the `let mutable` triad lives once, inside the combinator (same "reified mutation behind a typed surface" discipline as `LineageBuffer`).

**Blast radius.** Small: 2 pass bodies + 1 ~6-line module; behavior-preserving; covered by existing tests.

**What it buys.** Removes two copies of an easy-to-get-wrong mutable convergence loop; names the recursion scheme; removes unjustified mutation. **Effort: S.**

---

## 20. 🟨 Bring `ReadSide`'s stranded pure logic home to Core (key synthesis, `formatRawValue`, FK classification)

> **Status (2026-06-27):** ✅ **Done** — with a correction to the recon's anchors (the tree moved since 06-25). Of the three sub-moves: **(3) `ForeignKeyReadback.classify`** (the pure NULL-coordinate `Reconstructable | Unreadable` classifier) **moved to Core** (`Strategies/ForeignKeyReadback.fs`, next to the FK rules) — it was stranded inside the SQL adapter though already DB-free-tested; the two ReadSide call sites resolve it from Core unchanged, and `ForeignKeyReadbackTests` follows it home. **(1) Key synthesis** (`moduleSsKey`/`kindSsKey`/`attributeSsKey`) was **already routed through the Core `SsKey.synthesized` smart constructor** — and must NOT change its basis composition (`synthesized "READSIDE_KIND" "schema.table"` vs a composite list are DIFFERENT identities; altering it would break round-trip/goldens). **(2) `formatRawValue`** **already single-sources through `RawValueCodec`** for every non-trivial format (Boolean/DateTime/Date/Time/Guid — its own docstring states this); the residue (Integer/Decimal/Text/Binary) is adapter `obj`-coercion over driver quirks (`SqlBytes`/`SqlGuid`/time-as-`DateTime`) that cannot leave the adapter, plus a trivial invariant `ToString`. So (1)/(2) were already satisfied; (3) was the real remaining work and is done. Build clean; pure 3744/0; Docker clean (ReadSide reconstruction + round-trip canaries).

**Anchors:** `ReadSide.fs:66–77` (`moduleSsKey`/`kindSsKey`/`attributeSsKey` — `READSIDE_*` synthesis basis), `:149–202` (`ForeignKeyReadback.classify` — `Reconstructable | Unreadable`), `:811–872` (`formatRawValue` — `PrimitiveType -> obj -> string`); the Core codec it reimplements, `RawValueCodec` (`formatDateTime` etc.); the other key-synthesizing adapter, `OssysTranslation` (see Finding 10).

**The tension/smell.** Three instances of pure logic in a SQL reader. **Key derivation** decides the `READSIDE_*` synthesis basis — identity derivation is Core's crown jewel, and *two* adapters (`ReadSide`, `OssysTranslation`) independently choose synthesis bases, which is how identity conservation gets subtle bugs. **`formatRawValue`** reimplements `RawValueCodec`'s type-dispatch to canonical invariant strings instead of calling it. **`ForeignKeyReadback.classify`** is pure NULL-coordinate classification living in a reader.

**Proposed refactor.** Route key synthesis through one Core identity helper shared with `OssysTranslation`; replace `formatRawValue`'s body with calls into `RawValueCodec`; move `classify` to Core next to the FK rules in `Strategies/ForeignKeyRules.fs`.

**Blast radius.** `ReadSide` internals; the `formatRawValue` change must be byte-verified against the codec (goldens catch it).

**What it buys.** One identity-derivation home; one raw-value codec; FK readability testable without a DB. **Effort: M.**

---

## 21. 🟨 Harden the keymap-spill / transfer DML — 4th copy of the `N'…'` escape, `AddWithValue`, hand-built DDL

> **Status (2026-06-27):** ✅ **Done** — typed-AST branch (Tier 2.2; the KeymapSpill DDL/DML typed, the 4th escape copy + `AddWithValue` addressed).

**Anchors:** `KeymapSpill.fs:78` (`createTable` — full `CREATE TABLE … PRIMARY KEY` via sprintf), `:94–101` (`captureMany` — VALUES-list INSERT in a `StringBuilder`, `AddWithValue` at `:98–100`), `:122` (`repointJoin` — `UPDATE … FROM … JOIN`); `TransferRun.fs:270, 295` (`phase2UpdateSql`/`Quantum`), `:376` (`buildRevertScript` chunked `DELETE … IN (...)`), `:361` (`renderKey` — the 4th open-coded `N'…'` escape); the codec it bypasses, `SqlLiteral.toString` (`SqlLiteral.fs:110–111`); the typed builder that already exists, `ScriptDomBuild.fs:1179` (`buildUpdateFromTemp`).

**The tension/smell.** `KeymapSpill` string-builds DDL+DML; values *are* parameterized (good) but via `AddWithValue` (`:98–100, :124`) — the `object`-accepting API CLAUDE.md §6 names (here the values are plain `string`, so low-risk, but it's the disfavored API and leaves SQL-type inference to the driver). The revert path has its *own* literal renderer:
```fsharp
let renderKey (k: string) = match System.Int64.TryParse k with
                            | true, _ -> k
                            | false, _ -> System.String.Concat("N'", k.Replace("'", "''"), "'")   // TransferRun.fs:361
```
a **fourth** open-coded copy of the single-quote-doubling escape `SqlLiteral.toString` already owns.

**Proposed refactor.** Route `renderKey`'s encoding through `SqlLiteral` (it's the integer-or-text decision `SqlLiteral.ofRaw` already makes); convert `AddWithValue` to typed `cmd.Parameters.Add(name, SqlDbType.NVarChar, 450).Value <- …`; route the phase-2 UPDATE and keymap CREATE/repoint through `buildUpdateFromTemp` or a sibling typed builder.

**Blast radius.** Two files; reverse-leg + spill tests. Mostly hardening + de-duplication (the value-parameterization is already correct), not a correctness fix.

**What it buys.** One literal codec instead of four; removes the `AddWithValue` smell; aligns the spill/revert DML with the typed path. **Effort: M.**

---

## 22. 🟨 Consolidate the over-grown `View` DU leaves

> **Status (2026-06-27):** ◑ **Partial — the S consolidation done.** `Status → (glyph, color, tag)` was spread across three parallel `private` matches (`glyphOf` / `colorOf` / `statusTag`) that had to be kept in sync by hand. They now project from ONE `presentationOf : Status -> { Glyph; Color; Tag }` match — adding or re-tuning a status is a single edit; the three helpers stay as thin projections so every call site is unchanged. Pure CLI; the View/Tty/Narration render suite (102 tests) is green. **Open:** the M `Field`/`Meter`/`Action` ↔ `PanelRow.{Labeled,Gauge,Next}` leaf merge (a cross-level DU unification — `Field` is a top-level `View` case, `PanelRow.*` are panel rows, so the merge semantics are non-obvious and touch `writePanel`+`toJson`+render tests) and the L `Lane`↔`Disclosure` merge (which also unlocks #17's grouped-disclosure dedup).

**Anchors:** `View.fs:41–47` (`Field`/`Meter`/`Action`) vs `:59–101` (`PanelRow.Labeled`/`Gauge`/`Next`) — the wire format `toJson :505–510` already maps both to the same kinds; `:88` (`Lane`) vs `:96` (`Disclosure`); `:106–119` (`Status → glyph/color/tag` split across three parallel private matches).

**The tension/smell.** The `View` DU is structurally coherent (one substrate, two lenses — human/JSON, drift-proof) but triplicated at the leaves. `Field`/`Meter`/`Action` and `PanelRow.Labeled`/`Gauge`/`Next` are the same three concepts — `toJson` proves it by mapping them to identical kinds — yet they force parallel matches in `writePanel` + `toJson`. A `Lane` is a `Disclosure` whose detail is flat `Note`s + a glyph + breadth cap; the two ~30-line render arms are near-identical. `Status → (glyph, color, tag)` is split across three parallel `private` matches, so adding a status means editing three places.

**Proposed refactor.** Merge `Field`/`PanelRow` leaf families (M). Merge `Lane` into `Disclosure` (L) — which also enables the grouped-disclosure dedup in Finding 17. Replace the three `Status` matches with one `presentationOf : Status -> {Glyph; Color; Tag}` (S).

**Blast radius.** `View.fs` + `writePanel`/`toJson` + render tests; the wire format already unifies them, so JSON output is stable.

**What it buys.** Add-a-status / add-a-row in one place; ~60 lines of parallel render arms gone; unlocks Finding 17's grouped-disclosure extraction. **Effort: S (Status) / M (leaves) / L (Lane↔Disclosure).**

---

## 23. 🟨 Make `registered ⇔ executed` structural at the *Pipeline* registry (not just the Core chain)

> **Status (2026-06-27):** ○ **Not started.**

**Anchors:** `RegisteredTransforms.fs:107–171` (the Core chain — projects metadata + execution from one `chainSteps`, genuinely drift-proof; `:170–195`); the weak seam `RegisteredAllTransforms.fs:54–99` (Pipeline `all` is a hand-concatenated `@`-chain of ~10 sources, plus a literal list of 4 transfer adapters at `:92–98`); the guard `RegisteredAllTransformsBidirectionalTests.fs:43–52` (a `>= 21` *count* assertion).

**The tension/smell.** The Core chain is drift-proof — `all`/`allChainSteps`/`allChainStepsFor` are all projections of `chainSteps`, and a docstring records this *fixed* a real prior `TableRename`-position drift. But the Pipeline-level `RegisteredAllTransforms.all` is still a hand-maintained `@`-concatenation (`Compose.emitSteps`, `Compose.readStep`, `EmissionSeam.metadata`, `RegisteredDataTransforms.all`, `RegisteredTransforms.all`, + a literal 4-adapter transfer list). The embedded audit history shows this seam *has* drifted repeatedly (`F13 … authored but never wired into this totality view`; `SuggestConfig was previously executed-but-unregistered`), and the guard is a count — it catches *removal* but not *executed-but-unregistered* additions, the exact F13/SuggestConfig failure.

**Proposed refactor.** Push the chain's "project metadata from the execution definition" discipline up to the Pipeline registry: each contributing surface exposes its metadata *as a projection of its executed steps* (the emit/read stages already do — `:62`, `:71`), and assert `registered = executed` **set-equality by identity**, not count. Give the transfer block a `Transfer.executedSteps |> List.map metadata` projection like the others.

**Blast radius.** M — the Pipeline assembly + the transfer registration surfaces + the bidirectional test. High value-to-effort: closes a seam with *documented recurrence*.

**What it buys.** The third Pillar-9 failure mode ("a transformation site missing from the registry") becomes structurally impossible, matching what the Core chain already achieves. **Effort: M.**

---

## 24. ⬜ Core type-modeling cluster — small VO/DU lifts that finish the model

> **Status (2026-06-27):** ○ **Not started.**

A bundle of S findings in `Projection.Core`, each closing one illegal-state gap. The aggregates are already well-factored and guarded (`Catalog.create` enforces 5 invariants); these are the leaf fields the VO/DU migration didn't reach.

- **`DeleteScopeTerm` typed column** — `Policy.fs:53–57` is a raw `{ Column:string; Value:string }` amid a typed IR, resolved via a case-insensitive `columnNameText.Equals(t.Column, OrdinalIgnoreCase)` linear scan (`:82`) that bypasses the `ColumnRealization.columnNameEquals` primitive built to centralize exactly this (the N3 case-sensitivity bug class). Make `Column` a `ColumnName`; route through `columnNameEquals`. XL: model the *resolved* term as `(ColumnName * SqlLiteral)`, keep raw `(string*string)` only at the `Config.fs` boundary. *Buys: removes a stringly column-compare from the pure core. Effort: S–M.*
- **`Sequence.Schema → SchemaName`** — `Catalog.fs:345–357` carries `Schema:string`/`DataType:string` on a top-level logical-IR object (not the deliberately-deferred physical-comparison domain); `Coordinates.fs` even lists this as a known gap. Lift `Schema` to `SchemaName` (deletes a duplicated non-blank check); `DataType` could become a closed DU. *Effort: S (Schema) / M (DataType).*
- **`PhysicalColumn.Computed` string sentinel** — `PhysicalSchema.fs:84–90, 418–420` encodes persistence (a bool) as a magic `"|persisted"` suffix on the comparison key (`String.Concat(expr, "|persisted")`), decoded by string inspection. Defensible *as a comparison key* but a stringly sentinel; split into `Computed:string option` + `IsComputedPersisted:bool` or a `ComputedExpression` DU. *Effort: S.*
- **`MigrationPreview` named records** — `Migration.fs:69–74` uses positional `(SsKey * Name * Name)` and `(SsKey * SsKey * Set<AttributeFacet>)` tuples (you must remember "first Name is old"). Introduce `KindRename = { Key; From; To }` and `AttributeReshape = { Kind; Attribute; Facets }`. *Buys: safe against arg-order swaps. Effort: S.*
- **`CatalogDiff.SynthesizedRenameWarning.SynthesisSource`** — `CatalogDiff.fs:256, 806` is a `string` with a silent `Option.defaultValue ""`; an `Unknown` variant would make the erasure explicit. *Effort: S/M.*

*(Note-only, no action: `EmissionPolicy`'s six sibling bools at `Policy.fs:194–204` are a **deliberate** orthogonal product — the docstring at `:185–188` commits to growing fields rather than packing flags; act only if two ever become mutually exclusive. `KindColumns` projections returning `string list` at `KindColumns.fs:50–66` are likely intentional terminal-boundary flattening.)*

---

## 25. ⬜ Quick-wins & incidental-bugs bundle (S each, high certainty)

> **Status (2026-06-27):** ◑ **Partial.** Remediation `]`-bug fixed (via #8, `c16ad0e3`). **Landed 2026-06-27:** (a) the **🐞 Navigator breadcrumb bug** — extracted `safeMarkupLine console styled plain` (retiring the 4× markup-or-plain idiom); the footer's fallback now writes the full breadcrumb (was dropping it, writing only the legend) and the no-crumb branch is now escaped; (b) **`Catalog.kindByKey`** — the 4 `allKinds |> List.map (fun k -> k.SsKey, k) |> Map.ofList` sites now call the *existing cached* `Catalog.kindIndex` (the recon didn't spot it already exists — even better, it's `ConditionalWeakTable`-memoized); (c) **`LifecycleStore.withLoaded`** — `EjectRun.fromStore` ≡ `ReportRun.fromStore` collapse onto one generic `load→describe→fromChain` combinator (MigrationRun's typed-error + genesis third site is a genuinely different shape, deliberately left); (d) **`RelaxationStore.persist`** now returns `Result<unit,string>` (named cause) instead of a silent `bool` false. **Open:** `catalogTopologyStep` builder, the `parse-template` guards (ScriptDomBuild — touches emission, Docker-gated), `OperatorConsole`/`TtyRenderer` global threading (L), `Watch.fs` wire-format relocation (M).

Small, safe, mostly mechanical — plus two genuine latent bugs surfaced incidentally.

- **`Catalog.kindByKey`** — the `allKinds |> List.map (fun k -> k.SsKey, k) |> Map.ofList` idiom recurs verbatim in 4 passes (`QueryHintPass.fs:50`, `SymmetricClosure.fs:184`, `TopologicalOrderPass.fs:741`, `TableRename.fs`). Add one helper beside the existing `Catalog.nameIndex`; 4 sites collapse. *Trivial, additive.*
- **`requireKindByLogical` lift** — 4 documented copies of resolve-or-named-refusal (subsumed by Finding 2 if that lands; standalone otherwise).
- **`LifecycleStore.withLoaded` combinator** — `EjectRun.fs:59` ≡ `ReportRun.fs:41` (load chain → describe error → run pure `fromChain`); `MigrationRun.fs:196–214` is the same shape a third time inline. One combinator `withLoaded : (chain -> Result<'a>) -> path -> Result<'a,string>` (+ a `withLoadedOrGenesis` variant) deletes a hand-written stringly error path twice over. *Effort: S.*
- **`catalogTopologyStep` builder** — `RegisteredTransforms.fs:135–141, :149–155` inline a raw `ChainStep` record because their pass reads both Catalog and TopologicalOrder; there's a `liftCatalogTopologyPass` but no matching builder, so two of ~21 chain entries break the "one builder call per step" surface and force `SchemaComplexityPass.fs:32` to expose a public `name` mirror + a `registered None` placeholder whose `Run` is never invoked. Add the builder (S); the L "declare input as data" variant retires the placeholder.
- **`parse-template` guards** — `ScriptDomBuild.fs:1148–1153, 1197–1201` (`buildValidateBeforeApplyGuard`/`buildUpdateFromTemp`) `sprintf` a SQL string → `Parser.Parse` → pluck the statement (grammar-validated but string-templated); `:1147` interpolates a THROW message with a hand `.Replace("'", "''")` — another escape copy. Build from typed nodes (the 2026-06-25 atomic-batch refactor is the precedent) and route the message through `SqlLiteral.toString (TextLit msg)`. *Lowest urgency — already parse-validated.*
- **`OperatorConsole`/`TtyRenderer` mutable presentation globals** — `OperatorConsole.fs:52` (`verboseMode`), `:88` (`prettyMode`), and a third sibling in a different file `TtyRenderer.fs:326` (`queryPath`, whose comment admits it's homeless). Consolidate into one `RunPresentation`/`RunContext` record set once in `main` and threaded. *S to consolidate the refs; L for full threading. Buys: kills cross-file global scatter + the test-global-reset hazard.*
- **`Watch.fs` wire-format relocation** — `Watch.fs:208–235` (`parseLine`, `System.Text.Json` deserialization) belongs beside `LogSink.Envelope`, not in the board module; `apply` dispatches on stringly event codes (`code.EndsWith ".started"`, magic payload keys `"stage"`/`"done"`/`"total"`) — a typed `StageEvent` DU would make the "one substrate" claim type-enforced. *Effort: M.*
- **`RelaxationStore.persist` silent `false`** — `RelaxationStore.fs:64–77` returns `false` on any failure, violating "downgrades never silent"; a `Result<unit,string>` carries the named cause. *Effort: S.*
- **🐞 Real bug — `Navigator.fs:302–303` drops the breadcrumb.** The `try MarkupLine` is on `line` (crumb + legend) but the `InvalidOperationException` fallback writes only `navLegend`, dropping the breadcrumb on a non-interactive sink; and unlike sibling sites `:279`/`:293`, `line` isn't `Markup.Escape`'d. One instance of a "markup-or-plain fallback" idiom copy-pasted 4×. Extract `safeMarkupLine console styled plain` — fixes the bug and kills the dup. *Effort: S.*
- **🐞 Real bug — `RemediationEmitter.brackets` doesn't escape `]`** (detailed in Finding 8) — ships operator-facing SQL with a quoter that silently disagrees with the canonical one on `]`-bearing identifiers.

---

## Master ranking

| # | Refactor | Tier | Effort | Why it ranks here |
|---|---|---|---|---|
| 3 | `Face` combinator + split `RunFaces` | 🟥 | XL | Highest single-move leverage; makes 3 disciplines structural; one dispatch plane |
| 2 | Binding registry (+`ConfigError`/`ConfigAxis`) | 🟥 | L–XL | Most-corroborated (3 agents); ~300 LOC + drift-proofing; unlocks #9, #16, #24 |
| 1 | `SurrogateCapture` → ScriptDom | 🟥 | XL | Highest-stakes path; kills A40 dup-MERGE; the bug-class precedent |
| 4 | `CapabilityDescent` combinator + registry | 🟥 | L | Makes a named standing law unforgettable |
| 5 | `ProfileDerivation`/`Profiler` port | 🟥 | L–XL | Source-agnostic profiling; least-tested code → testable Core |
| 6 | Typed-tree boundary analyzer | 🟧 | S→XL | Purity law becomes machine-enforced; pure upside (green on first run) |
| 7 | `FkGraph` build-once | 🟧 | M–L | Biggest pass duplication + resolves a dedup divergence |
| 8 | Shared quoting primitive | 🟧 | M | Fixes a real `]`-escape bug at the root |
| 9 | Code→exit registry | 🟧 | M | Operator contract stops living in a string ladder |
| 10 | `parseSemanticType` → Core | 🟧 | M | Clearest "domain logic in the wrong layer" |
| 11 | Finish Voice migration + unify dispatchers | 🟧 | L | Closes the largest register-drift gap |
| 12 | `fanOutWithDiagnostics` primitive | 🟧 | M | Completes the `Composition` vocabulary; 3 passes shrink |
| 13 | One connection discipline / `Source` port | 🟧 | S→XL | Kills 6-site drift + a latent `live:` inconsistency; one source port |
| 23 | Structural `registered ⇔ executed` at Pipeline registry | 🟨 | M | Closes a seam with documented repeated drift |
| 17 | `Comparison.fs` — domain logic out of render | 🟨 | L | Risk classification testable + reusable; kills string-then-regex |
| 16 | Unify 3 JSON-read helper copies | 🟨 | M–L | One JSON-error vocabulary |
| 20 | ReadSide pure-logic → Core | 🟨 | M | One identity home; one raw-value codec |
| 21 | Keymap-spill / transfer DML hardening | 🟨 | M | 4th escape copy → codec; `AddWithValue` removed |
| 14 | `DerivationReason` DU | 🟨 | S–M | The last open-string field in *identity* |
| 15 | `LineageEvent.forPass` smart ctor | 🟨 | S–M | Widest boilerplate kill (~10 passes) |
| 22 | `View` DU leaf consolidation | 🟨 | S–L | Add-a-status/row in one place; unlocks #17 |
| 18 | De-hardcode config knobs | 🟨 | S–M | One load-bearing scale boundary + the `AdvisoryTuning` holdout |
| 19 | `Fixpoint.iterate` combinator | 🟨 | S | Two mutable loops → one pure combinator |
| 24 | Core type-modeling cluster | ⬜ | S each | Finishes the VO/DU model at the leaves |
| 25 | Quick-wins & incidental bugs | ⬜ | S each | Includes two genuine latent bugs (Navigator, Remediation) |

**If committing to one chapter:** #3 returns the most. **Best effort-to-payoff structural move:** #2 (most independently confirmed). **Cheapest pure-upside protection:** #6. **The two `🐞` bugs in #25** are worth spinning off immediately so they don't get buried behind the big swings.

---

*Provenance: seven parallel read-only recon agents, 2026-06-25. No files were edited. Anchors and code quotes are from the agents' direct reads of `src/`; re-verify line numbers before acting (the tree moves).*
