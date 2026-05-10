# V2 — Playbook (Technical Guidance for Implementation)

**Date:** 2026-05-08; **chapter-3.1-close update:** 2026-05-30
**Purpose:** Bridge `VISION.md` (the *why*) to `BACKLOG.md` (the *what*) with the concrete patterns, decision trees, and anti-patterns that make every chapter work compound rather than re-derive. This is the document the maintainer reads before opening a slice and consults when stuck.

> **Chapter-3.1-close patterns to consult (per `CHAPTER_3_1_CLOSE.md` meta-codifications):**
>
> - **Bench-driven optimization protocol** — three candidates, two refuted with bench data, one confirmed; refuted swaps documented in code so the same swap doesn't recur. Worked example: chapter 3.1 sessions 30 and 35 (sys.* readside, MARS, parallel hashing, `HashSet.ExceptWith`).
> - **Stream-realization pattern** — Π's output is a typed deterministic stream; realization layers (text, deploy, file artifact) are sibling consumers. Worked example: `RawTextEmitter.statements` + `Render.toText` + `Deploy.executeStream` (A35 / A36).
> - **Five-agent epistemic-tier audit at chapter close** — multi-agent parallel audit covering UL / Hex / VO / FP / ACL; convergence-map as primary surface; Tier 1/2/3/4 backlog by epistemic level + leverage. Worked example: `AUDIT_2026_05_DDD_HEXAGONAL_FP.md`.
> - **Harmonization-via-parameterization** — single-axis-divergent implementations earn one parameterized algorithm. Worked example: `SelfLoopPolicy` in `TopologicalOrderPass` (A40).
> - **Aggregate-root smart-constructor invariants** — `Catalog.create` / `Module.create` / `ColumnProfile.create` enforce referential-integrity invariants in one pass with errors aggregated; consumers flow through `create` to make invariants structural (A39).
> - **Writer-fidelity discipline** — pass drivers MUST use `LineageDiagnostics.tellDiagnostics` / `Lineage.ofValueAndEvents` (canonical primitives); manual record-building forbidden.

**Companion documents:**
- `VISION.md` — strategic frame; cutover forcing function; acceptance criteria.
- `AXIOMS.md` — T1, T11, A1, A18 amended, A33, A34 — the structural commitments.
- `BACKLOG.md` — ~375 items inventoried by chapter, status, disposition.
- `CLAUDE.md` — operating disciplines + F# feature surface + load-bearing commitments.
- `DECISIONS.md` — append-only resolved-questions log.
- Chapter pre-scopes (`CHAPTER_3_PRESCOPE_*.md`, `CHAPTER_4_PRESCOPE_*.md`) — per-chapter slice plans.

This playbook is **complementary**, not redundant: it captures the patterns those documents *imply* but don't always state.

---

## Contents

- [The bigger picture in one paragraph](#the-bigger-picture-in-one-paragraph)
- [The algebraic spine — three theorems that compound](#the-algebraic-spine--three-theorems-that-compound)
- [The five compounding wins](#the-five-compounding-wins)
- [The shape of things — recurring patterns](#the-shape-of-things--recurring-patterns)
- [The F#/C# boundary contract](#the-fc-boundary-contract)
- [Decision trees](#decision-trees)
- [The discipline operating cycle](#the-discipline-operating-cycle)
- [Anti-patterns — traps to avoid](#anti-patterns--traps-to-avoid)
- [Work-smarter checklist](#work-smarter-checklist)
- [Per-chapter strategic notes](#per-chapter-strategic-notes)
- [Closing — hold the spine](#closing--hold-the-spine)

---

## The bigger picture in one paragraph

V1 ships the cutover. V2's job is to make it **verifiable, reversible, and repeatable** through a sibling chorus of synchronized projections (SSDT DDL, CDC-aware data inserts, DACPAC, refactor log, distributions, diagnostics) emitted from a single `Catalog × Policy × Profile × Lifecycle` algebraic core. The multiplier — what makes 375 backlog items tractable for one maintainer — is that **the algebra is type-encoded**, so every emitter is a small `Catalog -> Result<ArtifactByKind<'element>, EmitError>` function with T11 free, every adapter is `Task<Result<Catalog, _>>` with A18 amended enforced by signature, and every property test is a one-line FsCheck predicate over the same generator. The hard work is the *foundation* (chapter 3 cross-cutting refactor: `ArtifactByKind` + four-variant `SsKey` + `CatalogDiff`); after that, every chapter is incremental composition over the foundation. Hold the spine; cut the rest.

---

## The algebraic spine — three theorems that compound

Three load-bearing structural commitments. Encoded as types, they make wide classes of bugs **impossible to write**. Encoded as discipline, they make the same bugs *avoidable in code review*. The chapter-3 cross-cutting refactor turns the three from discipline into types — that's the foundation step.

### T1 — Determinism

**Discipline form:** "Same `(Catalog, Policy, Profile)` triple → same output."

**Type form:**
```fsharp
type Emitter<'element> = Catalog -> Result<ArtifactByKind<'element>, EmitError>
```
Pure function signature; no `IO`, no `Task`, no `Random`, no `DateTime.Now`. Compile-time enforced.

**Tier-2 binary refinement:** for DACPAC, byte-determinism is too strict (DacFx's `BuildPackage` writes wall-clock timestamps). T1 amends to *content-determinism via DacFx model-API equality*. Byte-canonicalization is a post-pass in C# wrapper.

**Compounding:** every property test that depends on T1 is one line: `emit c = emit c`.

### T11 — Sibling-Π commutativity

**Discipline form:** "Every emitter mentions every Catalog kind by SsKey root."

**Type form:**
```fsharp
type ArtifactByKind<'element> = private ArtifactByKind of Map<SsKey, 'element>

[<RequireQualifiedAccess>]
module ArtifactByKind =
    let create (catalog: Catalog) (slices: Map<SsKey, 'a>) : Result<ArtifactByKind<'a>, EmitError>
```
Smart constructor enforces "every Catalog kind in the keyset; no extras." T11 becomes a structural consequence: any two `ArtifactByKind` values built from the same Catalog have equal key-sets *by construction*.

**Compounding:** the substring `Assert.Contains` tests at `JsonEmitterTests.fs:96-105` and `RichProfilingEndToEndTests.fs:280` retire. Every new emitter (DacpacEmitter, SsdtDdlEmitter, RefactorLogEmitter, RemediationEmitter, future GraphQL) inherits T11 with zero test code. The composition layer (`Render.concatSql`, `Render.toSsdtDirectory`, `Render.toDacpac`) is per-target-shape but per-emitter overhead is zero.

### A1 — Identity-survives-rename

**Discipline form:** "SsKey identity is stable under attribute/entity/module rename."

**Type form:**
```fsharp
type SsKey =
    | OssysOriginal of System.Guid                     // A1 holds unconditionally
    | Synthesized of source: string * basis: string    // A1 BOUNDED — bound is type-visible
    | DerivedFrom of parent: SsKey * reason: string    // A1 inherits from root
    | V1Mapped of v1Sskey: System.Guid * v2Namespace: System.Guid  // cross-version threading
```
The bound documented in prose at `AXIOMS.md:47-72` becomes a compile-time fact. Property tests asserting A1 accept only `OssysOriginal` (and `DerivedFrom` rooted in one); on `Synthesized`, the same property documents the bound.

**Compounding:** `RefactorLogEmitter` becomes "Π over `CatalogDiff`" rather than a special emitter. `UUIDv5` mapping from V1 SSKey Guids to V2 identities is one constructor. Cross-version diff via `SsKey.identityKey` is one helper.

### Why these three, why now

These three aren't aesthetic. Each maps to a cutover-blocking property:
- **T1** → byte-deterministic DACPACs; the V2-side of CDC-safety.
- **T11** → sibling chorus consistency; if SSDT DDL and DACPAC disagree, one has a bug.
- **A1** → renames don't trigger DROP+CREATE; the cutover demand for refactor logs.

The chapter-3 cross-cutting refactor (`CHAPTER_3_PRESCOPE_ARTIFACTBYKIND_REFACTOR.md`) is the *foundation step*. Land it first; every later chapter compounds on it.

---

## The five compounding wins

Each is independently load-bearing; together they make the cutover quarter plausible.

### W1 — V2 verifies V1 starting today

`Projection.Adapters.Osm.CatalogReader` (shipped) + `Projection.Targets.Json.JsonEmitter` (shipped) → V1's `osm_model.json` round-trips through V2. **No new code needed.** Slice 1 of chapter 3.1 wires this as an integration test.

Step 2 is the inflection: `Projection.Adapters.Sql.ReadSide` over `INFORMATION_SCHEMA` (~300-500 LOC F#) gives full V2-augmented mode against V1's SSDT directory. **DacFx Extract is not needed** — `INFORMATION_SCHEMA` + `sys.*` is sufficient and simpler. (Appendix F §F.2.)

### W2 — Read-side adapter is the chapter 3 keystone

Two consumers from day one:
- Canary's round-trip back-half.
- Drift detection (the "post-cutover trajectory" capability VISION pulled forward).

DacpacEmitter is gated on the read-side, not the inverse. Sequence is **3.1 → 3.2 → 3.3 → 3.4 → 3.5**, not the original 3.2-first plan.

### W3 — Canary as property-test surface, not curated integration

~600 LOC test code (FsCheck `CatalogGen.fs` + predicate library + tier-2 fixture) replaces ~2000+ LOC of curated DacpacEmitter integration tests. Tier-1 pure properties (T1, T11, T2 coproduct, A18 policy-orthogonality) cover ~80% of axiomatic coverage **with zero Docker**. Sub-second per property; runs in pre-commit. (Appendix E.)

The `idempotentRedeploy` predicate is the cutover-blocking property: deploy → redeploy same DACPAC → assert `DacServices.GenerateDeployScript` returns empty. This *names* the previously-implicit CDC-safety claim.

### W4 — `ArtifactByKind<_>` makes T11 a type theorem

Every new emitter drops in T11-compliant by signature. Drift detection becomes pointwise per-SsKey diff. Partial-state remediation becomes `dacpacEmitter (CatalogDiff.between deployed target)` — exactly the per-SsKey shape. (Appendix H §H.4.)

### W5 — Triangulation comparator: three Catalogs, two diffs

`C_ossys` (V2's expected from OSSYS) + `C_v1` (read-side from V1's deploy) + `C_round` (V2 passes applied to `C_v1`). Pairwise diffs attribute every divergence to V1 / V2 / comparator. Solves "V2 inherits V1 bugs" structurally. (Appendix F §F.6.)

---

## The shape of things — recurring patterns

The backlog's ~375 items resolve to a small set of patterns. Internalize these and most chapters become incremental.

### Pattern: an emitter

```fsharp
namespace Projection.Targets.<Name>

[<RequireQualifiedAccess>]
module <Name>Emitter =
    [<Literal>]
    let version : int = 1

    /// <Element type doc — what this emitter produces per kind>
    let emit : Emitter<'element> = fun catalog ->
        let slices =
            Catalog.allKinds catalog
            |> List.map (fun k -> k.SsKey, renderSlice catalog k)
            |> Map.ofList
        ArtifactByKind.create catalog slices

    let private renderSlice (catalog: Catalog) (k: Kind) : 'element = ...
```

**Invariants:**
- Pure (no `Task`, no `IO`, no `Random`).
- A18 amended — does not consume `Policy`.
- T11 — keyset is the catalog's; smart constructor enforces.
- T1 — same input → same output.

**Variants:**
- Profile-consuming emitter: signature widens to `Catalog -> Profile -> Result<ArtifactByKind<'a>, EmitError>` (e.g., `DistributionsEmitter`, CDC-aware data emitters).
- Diff-consuming emitter: signature is `CatalogDiff -> Result<ArtifactByKind<'a>, EmitError>` (e.g., `RefactorLogEmitter`, `RemediationEmitter`). Same T11 theorem applies to the diff's target Catalog.

### Pattern: an adapter

```fsharp
namespace Projection.Adapters.<Source>

[<RequireQualifiedAccess>]
module CatalogReader =
    type ReadSideError =
        | ConnectionFailed of detail: string
        | QueryFailed of queryName: string * detail: string
        | TypeMismatch of column: string * expected: string * actual: string
        | UnmappedDataType of sqlType: string * column: string

    val readCatalog
        : connStr: string
        -> schemas: string list
        -> System.Threading.Tasks.Task<Result<Projection.Core.Catalog, ReadSideError>>
```

**Invariants:**
- Lives at the boundary; `Task<Result<_, _>>` (CLAUDE.md "Async/Task in adapters only").
- Returns Core types (`Catalog`, `Profile`, `Module`, etc.) — never adapter-specific shapes leaking into Core.
- Errors are typed DUs; CLI/host maps to exit codes.

### Pattern: a pass

```fsharp
namespace Projection.Core.Passes

[<RequireQualifiedAccess>]
module <Name>Pass =
    [<Literal>]
    let version : int = 1

    let run
        (catalog : Catalog)
        (policy  : Policy)
        (profile : Profile)
        : Lineage<Diagnostics<'output>> = ...
```

**Pass return-type codification (per `DECISIONS 2026-05-13`):**
- `Lineage<'output>` if pass produces only decisions.
- `Lineage<Diagnostics<'output>>` if pass produces decisions plus observer-relevant findings.

**Invariants:**
- Pure (synchronous).
- Each decision emits one `LineageEvent` with PassName + SsKey + rationale.
- Each observable finding emits one `Diagnostics` entry with `Source = "<passName>"`, structured `Code` (`tightening.<area>.<kind>`), Severity, and Metadata.

### Pattern: a property test

```fsharp
[<Properties(Arbitrary = [| typeof<CatalogArbs> |], MaxTest = 100)>]
module <Name>Properties =

    [<Property>]
    let ``<Axiom>: <statement>`` (c: Catalog) =
        // one-line property body
        predicate c
```

**Three tiers:**
- **Tier 1 (pure, no Docker):** T1, T2 coproduct, T11 sibling-chorus, A18 policy-orthogonality. ~100 cases per property; sub-second. Pre-commit budget.
- **Tier 2 (testcontainers, fresh DB per case):** `roundTripBySsKey`, `idempotentRedeploy`, `renameSurvives`, `policyOrthogonal-on-deploy`. ~30 cases × ~150ms each. CI budget.
- **Tier 3 (full-integration, hand-curated `[<Theory>]`):** captures every shrunk failure as permanent regression. Append-only. Nightly budget.

### Pattern: a tolerance entry

When V2 deliberately diverges from V1 at the comparator boundary:
1. Add a flag to `CatalogEquivalence.Tolerance` record.
2. Default to `true` (permissive) initially.
3. Write a DECISIONS.md entry citing the V1 file:line + V2 rationale + re-open trigger.
4. Reference the DECISIONS entry in a doc comment on the Tolerance flag.

Each Tolerance flag is a documented divergence; the comparator's default profile is calibrated against real V1 fixtures.

### Pattern: a Skip-stubbed test

When V2 can't yet deliver something (gating dependency) or deliberately won't:
```fsharp
[<Fact(Skip = "subsumed by CanaryPureProperties.siblingChorusAgrees")>]
let ``T11: substring discipline`` () = ...
```
The Skip string captures the rationale. Tests stay in test discovery so the divergence is structurally visible. Remove the `Skip` only after one full chapter passes without surprise regressions in the area.

### Pattern: a CatalogDiff consumer

```fsharp
type RemediationDacpac = {
    Bytes : byte[]
    Diff : CatalogDiff
    Lineage : Lineage<unit>
    Manifest : RemediationManifest
}

let emit (deployed: Catalog) (target: Catalog) : Result<RemediationDacpac, EmitError> =
    match CatalogDiff.between deployed target with
    | Error e -> Error e
    | Ok diff -> dacpacEmitter (CatalogDiff.toRemediationCatalog diff)
                 |> Result.map (fun bytes -> { ... })
```

The composition is a thin wrapper over read-side + DacpacEmitter + CatalogDiff. Every "diff-aware" feature (drift detection, remediation, RefactorLogEmitter) follows this shape.

---

## The F#/C# boundary contract

Per `DECISIONS 2026-05-09` (Adapter language choice). **Codify so the maintainer doesn't have to re-decide.**

### F# always, no exceptions

- `Projection.Core` — IR, passes, axioms, Result/Lineage/Diagnostics writers, smart constructors.
- All Π emitters that produce text/structured-values: `RawTextEmitter`, `JsonEmitter`, `DistributionsEmitter`, `SsdtDdlEmitter`, `RefactorLogEmitter`, `OperationalDiagnostics.*Emitter`, `StaticSeedsEmitter`, `MigrationDependenciesEmitter`, `BootstrapEmitter`. Even when they consume `Profile`.
- `Projection.Adapters.Osm.CatalogReader` — JSON parsing + rowset parsing.
- `Projection.Adapters.Sql.ReadSide.CatalogReader` — `INFORMATION_SCHEMA` + `sys.*` reads via SqlClient (the SqlClient API is library-callable from F# without ceremony).
- `Projection.Core.Verification.CatalogEquivalence` — pure comparator.
- All test projects (`Projection.Tests`, `Projection.Tests.Canary`).

### C# only when foreign-API mutation-heavy

- `Projection.Pipeline` — testcontainers boot, file-system writes, CLI host, exit-code mapping.
- `Projection.Targets.SSDT.Dacpac` — DacFx `TSqlModel.AddObjects`, `BuildPackage`, `IDisposable` lifetimes, exception-driven validation.
- DACPAC byte-canonicalization post-pass — `System.IO.Packaging` zip surgery.
- Anything dispatching ≥3 separate `IDisposable` lifetimes simultaneously.

### The seam

F# and C# meet at **value-typed seams**:
- `byte[]` — raw DACPAC bytes from C# wrapper.
- `Result<T, E>` — Core's Result type (or per-adapter typed errors).
- `Map<RelativePath, string>` — directory-as-map for the C# host to write.
- `Catalog` — Core IR; C# treats as opaque, F# treats as algebraic.

**Never:** F# code calling DacFx directly. F# code touching `IDisposable` chains. C# code mutating Catalog. C# code reading Policy.

### Decision tree for "is this F# or C#?"

```
Does the work involve…
├─ Pure data transformation? → F#
├─ Reading a typed value from a foreign source via SqlClient/JSON parser? → F# (boundary adapter)
├─ Calling a C# library API that mutates an object graph (DacFx, SMO)? → C# wrapper
├─ Boot/teardown of an external resource (Docker container, SQL connection pool)? → C# host
├─ File-system writes? → C# host (the F# core never touches filesystem)
├─ CLI parsing, argument validation, exit-code dispatch? → C# host
├─ Test orchestration (per-test fixture, parallel test runs)? → F# test project (xUnit + FsCheck.Xunit)
└─ Anything else? → F# default
```

---

## Decision trees

### When to add a new IR field

Per `DECISIONS 2026-05-07` (IR grows under evidence, not speculation):

```
Is there a V2 consumer that *now* needs this field?
├─ No → DON'T ADD. Wait for the second consumer (per DECISIONS 2026-05-13 — two-consumer threshold).
├─ Yes → Is there a V1 trace fixture demonstrating the field?
│   ├─ No → Trace V1 first (per DECISIONS 2026-05-19 — trace-before-fixture).
│   └─ Yes → Classify into the three-class typology:
│       ├─ JSON-projection-lossiness → fix is at adapter (rowsets path).
│       ├─ V2-boundary-discipline → fix is in IR (add field; closed-DU expansion if needed).
│       └─ Alternative-IR-surface → may not belong in primary Catalog; consider sibling structure.
```

**Examples:**
- `Reference.IsUserFk : bool` (item 1.17, 4.34) — boundary-discipline; OSSYS adapter resolves; lands at chapter 4.2 slice 6.
- `Attribute.IsIdentity : bool` (item 1.6, 3.8) — gated on `SnapshotRowsets` (chapter 3.2) which surfaces the flag from rowsets.
- `Index.IsDisabled : bool` (item 1.10, 3.21) — same gating; emitted as `ALTER INDEX … DISABLE` once IR carries.

### When to add a new DU variant

Per `DECISIONS 2026-05-13` (Closed-DU expansion empirical-test discipline):

```
Adding a new variant to a closed DU:
1. Add the variant in the DU's defining module.
2. Compile and run the test suite.
3. F# exhaustiveness errors should appear ONLY at match sites within the variant's module.
4. If callers OUTSIDE the variant's module need reshaping → the seam is wrong; halt and fix the seam first.
5. If exhaustiveness errors appear only at the expected match sites, proceed.
```

**Worked examples:**
- `SsKey` four-variant expansion — exhaustiveness errors appear at exactly four sites: `Identity.fs:44-47, 50-53, 57-60`, `SymmetricClosure.fs:39-42`, and one test. Per the discipline, this confirms the seam is correctly positioned. (Appendix H §H.5.)
- `UserMatchingStrategy` four-variant — adds at the strategy module; `UserFkReflowPass.discover` is the only consumer pattern-matching on it.

### When to extract a primitive

Per `DECISIONS 2026-05-13` (Two-consumer threshold + Anticipation vs speculation):

```
Tempted to extract a helper / module / abstraction?
1. Count the call sites.
   ├─ One → DON'T extract; inline.
   ├─ Two → Are the shapes structurally identical?
   │   ├─ No (e.g., 3 of 2 distinct shapes) → DON'T extract; inline both.
   │   └─ Yes → Extract; the second consumer earned it.
   └─ Three+ → Extract.
2. After extraction, verify: the abstraction's name describes the function's *role*, not its *implementation*.
```

**Anti-example:** the `opportunityEntry` function across UniqueIndex / Nullability / ForeignKey passes — three consumers but two shapes (UniqueIndex + ForeignKey similar; Nullability structurally different). The discipline declines extraction. Inline both.

### When to write an axiom amendment

Per chapter-close ritual (`DECISIONS 2026-05-14`):

```
Closing a chapter that introduced a structural commitment?
1. List every type / function / property the chapter shipped that consumers will rely on.
2. For each, ask: "Is this a behavior or a structural fact?"
   ├─ Behavior (passes might evolve) → no axiom; document in DECISIONS.
   └─ Structural fact (consumers depend on it; breaking it breaks them) → AXIOMS amendment.
3. Format: extend an existing axiom with "X amended (chapter N close)" or add a new axiom Aᴺ.
4. Cite the property test that exercises the amendment.
```

**Worked examples:**
- T1 amendment for binary normal form (chapter 3.3 close) — consumers depend on byte-vs-content equality contract.
- A1 amendment for four-variant SsKey (chapter 3 cross-cutting close) — the bound becomes type-visible.
- A35 candidate for Π-erased axes (chapter 3.4 close) — `Catalog.equalModuloDacpacErasure` IS the axiom.

### When to add a Tolerance entry to the comparator

```
V2's emission diverges from V1 in a way the comparator catches?
1. Is the divergence intentional (V2 deliberately differs)?
   ├─ No → It's a V2 bug; fix the emitter.
   └─ Yes → Is it correctable in V2 without losing a property?
       ├─ Yes (cheap fix) → Fix.
       └─ No (V2 chose differently for a reason) → Add Tolerance entry:
           a. Add flag to `CatalogEquivalence.Tolerance` record (default true initially).
           b. Write DECISIONS.md entry citing V1 file:line + V2 rationale + re-open trigger.
           c. Reference DECISIONS entry in doc comment on the flag.
           d. Tier-2 property test demonstrates the divergence holds across generated catalogs.
```

### When to use F# vs C#

Already covered in [the F#/C# boundary contract](#the-fc-boundary-contract). Default F#; use C# only when foreign-API mutation-heavy.

### When you reach for a name

Per `DECISIONS 2026-05-10 — Domain-first naming and ubiquitous-language consistency` (pillar 8; chapter 3.7 sidebar). The trigger: you're about to introduce a new type, function, file, module, or test. Before drafting the name, walk the four questions. The discipline lives entirely in the document — no lint enforcement (heuristic syntactic checks misfire on legitimate uses; the discipline is inherently semantic).

```
1. What domain concept does this represent?
   Articulate it in cutover-business terms. The cutover vocabulary
   is sourced from:
     - operators / DBAs    — "schema-fidelity", "rollback window",
                             "split-brain risk", "tolerance flag"
     - OutSystems platform — "Espace", "Entity", "External Entity",
                             "Static Entity", "Application", "Module"
     - CDC + SQL Server    — "RefactorLog", "DACPAC", "INFORMATION_SCHEMA",
                             "DATA_TYPE", "OUTPUT INTO"
     - V2 algebra          — "Catalog", "Π", "Pass", "Lineage",
                             "Diagnostics", "ArtifactByKind"
   If the concept doesn't fall in one of these vocabularies,
   ask: "Is this concept domain-meaningful at all?"
   If no → the abstraction is itself wrong; restructure.
   If yes but not yet named → propose a name aligned with the
                              nearest vocabulary above.

2. Does V2 already name this concept somewhere?
   ├─ Search the codebase: `grep -rn "ConceptCandidate" src/ tests/`
   ├─ YES → use the same name. Cross-surface drift (Core uses
   │        `SsKey`, Adapter uses `Identifier`) is a structural
   │        failure; readers must mentally translate, and the
   │        translation cost compounds.
   └─ NO  → pick a name that mirrors a stakeholder vocabulary
            (see #1).

3. Concept-shaped or action-shaped?
   ├─ CONCEPT-SHAPED ("what this IS in the domain") — default for
   │  types, modules, files.
   │  Examples: `Catalog`, `Module`, `Kind`, `Reference`,
   │            `RemovalReason`, `AnnotationDetail`,
   │            `SqlTypeCorrespondence`, `RefactorLog`,
   │            `BatchSplitter`, `SiblingEmitterContract`.
   └─ ACTION-SHAPED ("what this DOES") — acceptable for function
      names when the verb names a *domain* operation.
      Domain verbs: canonicalize, normalize, mask, render, emit,
                    project, attach, traverse, classify, partition.
      NOT-domain verbs (CS-shaped): process, handle, manage, run,
                                    execute, do, perform.
      The function name `processItem` answers nothing about the
      domain; the function name `canonicalizeKind` answers
      "this is the canonicalize-identity pass operating on a kind."

4. Generic-suffix smell test.
   If the proposed name ends in:
     - Helper / Util / Utils / Utility / Utilities
     - Manager / Service / Handler / Processor / Wrapper
     - Builder / Factory / Provider / Strategy
       (when not BCL-mandated; e.g., StringBuilder is BCL,
        FluentValidationBuilder is fine, but a custom MyBuilder
        is suspicious)
   STOP. The generic suffix is a placeholder for "I haven't
   identified the domain concept yet." Two corrections:
     a. The concept exists but isn't named domainally.
        Find the concept (rename to the domain term).
     b. The concept is being squashed into something else.
        It doesn't deserve a wrapper around an unnamed thing —
        restructure.

   Note: legitimate uses exist. `LineageBuffer` is concept-shaped
   despite "Buffer" — the buffer IS the reified mutation surface
   (a domain primitive in the FP-strict-mode discipline). The
   heuristic misfires; the discipline document catches what the
   heuristic can't. The structural test: does the suffix name
   the *role* (Buffer = accumulator), or does it launder the
   absence of a domain term (Manager = ???)?
```

**Domain-blind naming is the named failure mode**: a name shaped like a placeholder for the absent domain concept. The agent feels productive (a name exists; the code compiles; tests pass) without doing the domain-modeling work that makes the name structurally accountable. The cutover stakes are the forcing function — **verifiability rests on the V2 vocabulary mirroring the cutover vocabulary**.

**Worked precedents in V2 (concept-shaped, ubiquitous-language-consistent):**

| Name | What it represents (cutover-business) | Why this is concept-shaped |
|---|---|---|
| `Catalog` | The V2 IR for a fully-projected, V2-internal model of an OutSystems schema | Generic algebraic name at Core; mirrors the domain-prescriptive `Application` at the boundary |
| `Kind` | A typed entity class within a Catalog (Entity, Static Entity, View, etc.) | Generic algebraic name; the concrete kinds are domain-named at the boundary |
| `SsKey` | The V2 identity for a kind, attribute, reference, or module across adapter / pass / emitter surfaces | Concept-shaped (it IS the identity); the variants (`OssysOriginal`, `Synthesized`, `V1Mapped`) name provenance classes |
| `RemovalReason` | The typed predicate that fired when a filtering pass dropped a kind | Concept-shaped; the variants (`OriginPredicate`, `ExplicitKeyList`, `ModalityPredicate`) name the rule classes |
| `AnnotationDetail` | The typed payload an intervention pass attaches to a kind / attribute | Concept-shaped; the variants name the decision classes (`NullabilityDecision`, `UniqueIndexDecision`, `ForeignKeyDecision`, `CategoricalUniquenessDecision`, `ClosureSkipped`) |
| `Coordinates.TableId` | The schema-coordinate value object for a kind's physical realization | Concept-shaped (it IS the coordinate) |
| `RawValueCodec` | V2's canonical raw-value format contract; consolidates Render / Bulk / ReadSide | Concept-shaped (it IS the codec — encode + decode pair) |
| `SqlTypeCorrespondence` | The round-trip pair `PrimitiveType ↔ SQL DDL base name` | Concept-shaped (it IS the correspondence) |
| `RefactorLog` | The SSDT refactor-log artifact carrying schema-evolution semantics | Concept-shaped, sourced from SSDT vocabulary |
| `CatalogDiff` | The exhaustive partition (`Renamed` / `Added` / `Removed` / `Unchanged`) between two Catalogs | Concept-shaped, V2-algebraic |
| `BatchSplitter` | The strategy for splitting deploy SQL batches at GO boundaries | Concept-shaped (it IS the splitter; the role IS the structural commitment) |
| `DatabaseNameGenerator` | Reified non-determinism boundary for ephemeral DB names | Concept-shaped (it IS the generator; the boundary IS reified per FP strict-mode discipline) |
| `EmissionPolicy` | The A39 invariant — at least one artifact family enabled | Concept-shaped (it IS the policy; A39 names the invariant structurally) |
| `LineageBuffer` | The reified pass-driver event accumulator | Concept-shaped despite the "Buffer" suffix — the buffer IS the reified mutation surface; the suffix names the algebraic role |
| `SiblingEmitterContractTests` | The contract every sibling Π emitter satisfies (chapter 3.7 slice ε rename) | Concept-shaped; was `T11TypeTheoremTests` (theorem-ID-shaped) — the rename illustrates the domain-first naming discipline at work |

**Worked anti-patterns** (what V2 does NOT do — these names would fire pillar 8 and would be rejected):

| Anti-pattern | Why it fails |
|---|---|
| `JsonHelper.fs` | "Helper" launders the absent concept; what does it actually represent? Probably `JsonNodeBuilder` or `JsonRender` or part of `JsonEmitter` |
| `Utils.fs` | Generic; no domain content. If the helpers are about identity, name them `Identity.fs`; about JSON, name them `JsonRender.fs` |
| `KindManager` | Manages what? "Manager" is a placeholder for the unknown domain operation; if it transforms kinds, it's a `Pass`; if it queries them, it's a query function on `Catalog` |
| `EmitterService` | "Service of what?" Emitters ARE the service surface; the suffix is redundant (and pillar-8-flagged) |
| `ConfigHandler` | "Handles" config — but what's the operation? `Policy`-decoder? `Tolerance`-evaluator? Name the operation |
| `SqlBuilder` (when not BCL-mandated) | If it builds SQL, name what it builds (`Sql160ScriptGenerator` from BCL is fine; a custom V2 `MyBuilder` would need to name what it builds) |

If you find yourself drafting a name that ends in a generic suffix, the right move is *not* to add a `LINT-ALLOW` (pillar 8 has no syntactic enforcement) — the right move is to walk back to question #1 and identify the domain concept. The name comes from the concept; the concept comes from the cutover vocabulary; the cutover vocabulary is documented at every stakeholder surface.

---

### When you reach for a string-composition primitive

Per `DECISIONS 2026-05-10 — LINT-ALLOW substantive-rationale discipline` (chapter 3.7 sidebar; pillar 7 amendment). The trigger: you're about to write `String.Concat`, `String.concat`, `String.Format`, `sprintf`, `String.Join`, an interpolated string `$"…"`, or a `+` between strings — the lint will fire and you're considering whether to refactor or to add a `LINT-ALLOW` marker.

**Stop.** Do not draft the `LINT-ALLOW` text yet. Walk the four questions:

```
1. Use-case-specific library for THIS output structure?
   ├─ SQL DDL / DML?      → ScriptDom (SqlDataTypeReference / TSqlStatement)
   │                        + Sql160ScriptGenerator
   ├─ XML?                → XmlWriter / XDocument
   ├─ JSON?               → Utf8JsonWriter / JsonNode
   ├─ Connection string?  → SqlConnectionStringBuilder
   ├─ Filesystem path?    → Path.Combine
   ├─ SQL identifier?     → Identifier.EncodeIdentifier (ScriptDom)
   ├─ Namespaced GUID?    → UuidV5 (RFC 4122)
   ├─ DACPAC artifact?    → DacFx (TSqlModel / DacPackage / DacServices)
   ├─ Golden-file diff?   → Verify.XUnit
   ├─ T-SQL batch split?  → BatchSplitter (TSql160Parser line-fold fallback)
   └─ ... (extend; if you don't see your case, ask "what library would
            Microsoft / SQL Server vendor / OutSystems vendor use here?")

2. Already in the codebase?
   ├─ YES → name the existing consumer site:
   │        e.g., "ScriptDomBuild.dataTypeReference at line 90 already
   │              builds the typed AST; Sql160ScriptGenerator is loaded
   │              and used by ScriptDomGenerate.generateOne".
   └─ NO  → name the package + version that would land it.
            e.g., "Microsoft.SqlServer.DacFx 170.x; ~30 MB transitive
                   per chapter 3.x DacpacEmitter pre-scope".

3. Cost?
   ├─ Visibility lift   → "private → public" (~N LOC)
   ├─ Helper to add     → "generateDataType : DataTypeReference -> string"
   │                       (~10 LOC mirroring generateOne)
   ├─ Perf class        → "O(1) per-call generator instantiation; ~5000
   │                       calls per canary; bench label
   │                       scriptDom.generateDataType surfaces it"
   └─ Dep weight        → "no new package dep" / "+30 MB transitive"

4. Structural reason it doesn't apply?
   ├─ NO  → THERE IS NO SHORTCUT. Do the work:
   │        a. Lift visibility.
   │        b. Add the helper.
   │        c. Refactor the call site.
   │        d. The LINT-ALLOW does not get drafted; the marker comes down.
   │
   └─ YES → marker text MUST name the SPECIFIC reason — NOT generic
            vocabulary alone.
            ├─ GOOD: "writer-monad tell algebraic primitive; pass drivers
            │         use LineageBuffer for high-rate accumulation, tell
            │         is terminal annotation only"
            │   (names role + alternative for hot path)
            ├─ GOOD: "terminal text-emission boundary; HexLiteralPrefix is
            │         the canonical typed segment, raw is already vetted hex"
            │   (names boundary + typed segment source)
            └─ BAD:  "terminal SQL DDL emission boundary; both segments are
                      typed (closed-DU dispatch + literal)"
                ↳ uses pillar vocabulary without naming the considered
                  alternative (Sql160ScriptGenerator) or the structural
                  reason it doesn't apply. THE MARKER IS THE FAILURE
                  MODE — performance-of-compliance.
```

**The named failure mode is performance-of-compliance**: a marker shaped like an audit trail without the substance. The lint passes, the vocabulary fits, the tests are green — and the structural commitment is unmet. The marker's audit-trail shape masks the absence of the audit. If you find yourself drafting language that *sounds* substantive without performing the analysis, stop — the answer to question #4 is almost certainly "no" and the right move is the work.

**Worked counterfactual** (`DECISIONS 2026-05-10`): slice-β added four `String.Concat` LINT-ALLOWs in `Render.columnSqlType` reading "terminal SQL DDL emission boundary; both segments are typed (closed-DU dispatch + literal)" — discipline vocabulary, no substance. Operator caught it on review. Slice-β' lifted `ScriptDomBuild.dataTypeReference` from `private` to public, added `generateDataType : DataTypeReference -> string`, made Render delegate. Cost: 87 LOC across 3 files; output byte-identical (790 tests still green); perf-gate clean; four LINT-ALLOWs retired; two private helpers retired (`sqlTypeWithLength`, `sqlDecimal`); one unused import retired. The "do the work" path was trivial compared to the structural drift the shortcut would have introduced over time.

**Lint Rule 27 maintains an inventory** of every per-line concat-aversion `LINT-ALLOW` (printed at the end of every clean run) AND enforces a soft floor (≥30 chars after the colon, at least one substantive-vocabulary token). Heuristics can't catch performance-of-compliance reliably; the discipline document does. The inventory is the audit surface for chapter-close ritual + PR review.

---

## The discipline operating cycle

The maintainer operates ~14 named disciplines (per `CLAUDE.md` operating-disciplines table). They cluster into three phases.

### Chapter open

1. **Read VISION.md** for the larger arc.
2. **Read the chapter pre-scope** (`CHAPTER_X_PRESCOPE_*.md`).
3. **Cross-reference BACKLOG.md** by chapter index — surface any items the pre-scope omits.
4. **Verify ADMIRE.md currency** — confirm chapter-N-1 entries are marked correctly.
5. **Verify AXIOMS.md currency** — confirm prior amendments are committed.
6. **Strategic frame at chapter open** (per `DECISIONS 2026-05-15`): for multi-session chapters, name the chapter's load-bearing axes before substantive slices begin.
7. **Deferred items scan** — per `DECISIONS 2026-05-19` chapter-mid-audit amendment, include active deferrals scan as a required dimension.

### Chapter middle (per chapter-mid-audit, `DECISIONS 2026-05-19`)

Every 3–5 substantive sessions, dispatch a cross-document consistency audit:
- Active deferrals scan (mandatory dimension, per session-24 amendment).
- Contract-vs-implementation walk for newly-shipped types.
- CRITICAL findings → fix in next hygiene work.
- MINOR findings → roll to chapter close.
- OPEN findings → discuss; don't silently close.

### Chapter close (per chapter-close ritual, `DECISIONS 2026-05-14` + session-25 V1-envelope amendment)

Eight load-bearing items. **None optional.**

1. **Active deferrals scan** — table-scan, not chronological re-read. Trigger-fire detection.
2. **Contract-vs-implementation walk** — every shipped contract has a test; every test has a shipped contract.
3. **CLAUDE.md staleness check** — operating disciplines table; F# feature surface table; load-bearing commitments mirror HANDOFF.md.
4. **README.md staleness check** — if the chapter changed shipped capabilities, update README.
5. **HANDOFF.md scope** — write `HANDOFF_CHAPTER_N.md` for the next chapter; preserve current `HANDOFF.md` as historical.
6. **CHAPTER_N_CLOSE.md scope** — synthesis of the chapter arc; AXIOMS amendments committed; DECISIONS entries written.
7. **Fresh-eye walk** — read the chapter's deliverables as if seeing for the first time; surface any naming / shape / discipline drift.
8. **V1-input-envelope walk** (V1↔V2 translation chapters only) — trace V1's inputs to confirm V2's coverage; surface won't-carry-forward items.

---

## Anti-patterns — traps to avoid

The maintainer will be tempted to take these shortcuts. Each is a specific failure mode with a citation to where the discipline names it.

### A1. Adding IR fields ahead of evidence

**Shape:** "I know the cutover will eventually need `Index.FilterDefinition`, let me add it now while I'm here."

**Failure mode:** the field has no consumer, so its serialization, parsing, validation, and tests are all *speculative*. When a real consumer arrives, the speculative shape is usually wrong and must be reshaped. Two-cycle waste.

**Discipline:** `DECISIONS 2026-05-07` — IR grows under evidence. Wait for the second consumer (or a fixture that demonstrates the field is needed *now*).

**Recovery:** if you've already added it, scan whether anything actually consumes it. If not, delete.

### A2. Implementing without tracing V1

**Shape:** "I'll implement the change-detection MERGE predicate from first principles; it's straightforward."

**Failure mode:** V1 has subtle behaviors (escape rules, NULL handling, batch boundaries) that V2's first-principles implementation misses. The differential test catches it eventually but the round-trip is wasteful.

**Discipline:** `DECISIONS 2026-05-19` — trace-before-fixture. Walk V1's actual handling first; classify into the three-class typology; *then* write the failing test.

**Recovery:** when stuck, drop into `src/` and grep for the V1 capability. The V1 behavior is the spec.

### A3. Putting evidence in Policy or intent in Profile

**Shape:** "CDC-enabled status is intent — it represents the operator's choice to enable CDC. Let me put it on Policy."

**Failure mode:** A18 amended says emitters consume `Catalog × Profile` subsets, never `Policy`. If `CdcAwareness` is on Policy, the emitter literally cannot consume it without violating A18. The next chapter has to rip the field out and put it on Profile.

**Discipline:** A18 amended. Policy is *what the operator decided*. Profile is *what the data shows*. CDC-enabled is what the data shows (the deployed schema reveals it); the operator's decision was made before V2 ran.

**Recovery:** when in doubt about Policy vs Profile, ask: "Did the operator type this somewhere, or did V2 discover it?"

### A4. Using `Original of string` instead of the four-variant DU

**Shape:** "The new adapter just needs an SsKey for the kind it found. Let me call `SsKey.original "DEPLOYED_KIND_..."`."

**Failure mode:** `Original` is a legacy-shape constructor; it should not exist after the chapter-3 cross-cutting refactor. Using it bypasses the type-stratification of A1; property tests over `OssysOriginal` no longer hold.

**Discipline:** Appendix H §H.5. Pick the right variant: `OssysOriginal` if you have a Guid, `Synthesized` if you're hashing names, `V1Mapped` if it's a V1 SSKey read back from a deployed schema, `DerivedFrom` if a pass introduced it.

**Recovery:** grep for `SsKey.original` in adapters; convert each to the right variant.

### A5. Speculative abstraction extraction

**Shape:** "These two functions look similar; let me extract a helper."

**Failure mode:** Per `DECISIONS 2026-05-13` (anticipation vs speculation), one consumer doesn't earn an abstraction. The "shape" you anticipate often diverges from the second consumer's actual shape, and the abstraction needs reshaping or rollback.

**Discipline:** Two-consumer threshold. Extract at the *second* consumer if shape is concrete, not the first. Position B (structural alignment when shape is concrete) earns its place; Position A (full extraction) requires both shape visibility AND concrete second consumer.

**Recovery:** if you've extracted prematurely, leave the abstraction in place but defer using it; observe whether the second consumer actually fits its shape.

### A6. Silent Profile dependence

**Shape:** A pass that nominally doesn't consume Profile but secretly reads `profile.SomeField` for a small optimization.

**Failure mode:** A34 (Profile independence) requires that passes which don't consume Profile produce identical output for `Profile.empty` and any populated Profile. Silent dependence breaks A34; property tests catch it but only after running a hundred cases.

**Discipline:** A34. If a pass needs Profile evidence, declare it in the signature: `Catalog -> Policy -> Profile -> Lineage<...>`. If not, the signature is `Catalog -> Policy -> Lineage<...>` (no Profile parameter).

**Recovery:** F# type system catches this at compile time if you remove unused parameters.

### A7. Forgetting AXIOMS amendments at chapter close

**Shape:** Chapter ships; you write the HANDOFF letter; you forget that the chapter's structural commitment needed an AXIOMS amendment.

**Failure mode:** The next chapter's agent has no record of the new structural fact. Six months later, someone "discovers" the constraint and either re-derives it or accidentally violates it.

**Discipline:** Chapter-close ritual item 6 — CHAPTER_N_CLOSE.md scope includes AXIOMS amendments committed. The PLAYBOOK pattern "When to write an axiom amendment" gives the test (structural fact vs behavior).

**Recovery:** at chapter-N+1 open, scan AXIOMS.md for missing amendments named in chapter-N pre-scope. Write them now.

### A8. Drift on Tolerance entries without DECISIONS

**Shape:** Adding a Tolerance flag to the comparator without a DECISIONS entry citing the V1 source.

**Failure mode:** Six months later, someone removes the Tolerance flag thinking V2 is "now correct" — but the flag was masking a real divergence the comparator should still tolerate. Cutover surprise.

**Discipline:** The Tolerance pattern (above). Each flag is a documented divergence with citation + re-open trigger.

**Recovery:** scan `CatalogEquivalence.Tolerance` flags; for each, confirm a DECISIONS entry exists citing the V1 source and rationale.

### A9. Letting Projection.Pipeline grow unmanaged

**Shape:** Five chapters add responsibilities to Projection.Pipeline without a coherent contract.

**Failure mode:** Projection.Pipeline becomes a "junk drawer" of canary, CLI, testcontainers boot, manifest writing, deployment dispatch, exit-code mapping, drift-detection scheduling. Maintenance burden compounds.

**Discipline:** Item 7.32–7.37 in BACKLOG names this risk. Each chapter that adds to Projection.Pipeline writes a CHAPTER_N_CLOSE entry naming exactly what new responsibilities it owns.

**Recovery:** at any point, if Projection.Pipeline has >3 namespaces, audit; consider extracting a sibling project.

### A10. Won't-carry-forward without an explicit cut

**Shape:** "V1 emits triggers; V2 ignores them implicitly."

**Failure mode:** the differential test surfaces V1's trigger output; the maintainer wonders if V2 has a bug. Time-wasted investigation.

**Discipline:** Won't-carry-forward items get explicit Skip-stub tests (per CLAUDE.md test discipline) that name the deliberate divergence. Tolerance flag default-true is the comparator-side equivalent.

**Recovery:** for every "missing in V2" item in BACKLOG with disposition `won't-carry-forward`, ensure a Skip stub or Tolerance entry exists citing the rationale.

### A11. Emitting "raw SQL" instead of structured values

**Shape:** Static seed emitter emits `string` directly; topological ordering becomes hand-managed.

**Failure mode:** Cross-emitter composition (interleaving Static + Migration + Bootstrap rows under one topological order) requires re-parsing the strings. Loss of typed structure.

**Discipline:** `DataInsertScript = { Phase1Merges: DataInsertRow list; Phase2Updates: DataInsertRow list; Rendered: string }` (chapter 4.1.B pre-scope §2.4). Structured shape with rendered T-SQL alongside.

**Recovery:** if emitter is producing string only, refactor to structured DataInsertRow + rendering layer.

### A12. F# mutation in Core

**Shape:** Mutable cache, dictionary, or list inside Core for a "small optimization."

**Failure mode:** T1 byte-determinism breaks under thread races or insertion-order divergence. Property test catches but with confusing failures.

**Discipline:** F# pure core / no-I/O-in-Core. Mutable state only function-local for performance-sensitive algorithms (Tarjan SCC, ResizeArray accumulators) — never module-level.

**Recovery:** F# compiler with `Nullable=enable` + `TreatWarningsAsErrors=true` catches most. Audit module-level `let mutable` declarations.

---

## Work-smarter checklist

Carry these into every chapter. Each is a compounding move.

- [ ] **Land the foundation step first.** Chapter 3 cross-cutting refactor (`ArtifactByKind` + four-variant `SsKey` + `CatalogDiff`) before any new emitter chapter.
- [ ] **Trace V1 before writing the failing test.** No exceptions; even when the V1 file is obvious, the trace surfaces edge cases.
- [ ] **Tier-1 properties first, tier-2/3 second.** Pure properties cover ~80% with sub-second feedback; Docker-bound integration tests are the smell-test, not the verification surface.
- [ ] **Use FsCheck generators across chapters.** `CatalogGen.fs` lands once at chapter 3.4; chapters 4.1.A, 4.1.B, 4.4 inherit. New shape variants extend the generator, not fork it.
- [ ] **Smart-construct everywhere.** Every value-typed invariant rides on the value via `Result<'a>`. Downstream consumers don't re-validate.
- [ ] **Closed DUs over flags.** Two booleans with a relationship is a DU with three variants. Pattern-match exhaustively.
- [ ] **Code prefix routing over channel splits.** Diagnostics entries route by `Code` prefix at emit time; one writer, three artifacts. (Item 7.22.)
- [ ] **Backtick-quoted test names citing the axiom.** `` ``A1: rename preserves OssysOriginal SsKey`` ``. Failing tests point at the law.
- [ ] **Skip stubs over silent omission.** When V2 deliberately doesn't carry V1 behavior, write a `[<Fact(Skip = "...")>]` so the divergence is visible.
- [ ] **Tolerance flags with DECISIONS entries.** Every comparator divergence is documented at flag-creation time, not retroactively.
- [ ] **Per-emitter `Render.toX` composition layer.** Emitter produces `ArtifactByKind`; renderer composes into the final shape (string, JsonDocument, DACPAC, directory map). Per-kind sliceability is structural.
- [ ] **Test the pass at the writer-monad layer.** `Lineage.value`, `Diagnostics.entries`, `LineageDiagnostics.payload` accessors expose the inner state without re-running the pass.
- [ ] **Audit during validation.** When something second-order surfaces, act on it before shipping the slice.
- [ ] **Active deferrals scan at every chapter-mid-audit.** Trigger-fires get caught by table-scan, not chronological re-read.
- [ ] **One CHAPTER_N_CLOSE.md per chapter.** Synthesizes the chapter arc; AXIOMS amendments committed; DECISIONS entries written; HANDOFF letter handed forward.

---

## Per-chapter strategic notes

Beyond the slice list each pre-scope provides — the *why* and *how to think about* each chapter.

### Chapter 3.1 — Read-side adapter + comparator + Projection.Pipeline shell

**Strategic frame:** This is the **inflection point** — the slice that turns V2 from "a side project" into "V1's verifier." Every subsequent chapter compounds on it. Land it well; don't rush to chapter 3.3.

**Sequencing:** JSON canary → INFORMATION_SCHEMA read-side → CatalogEquivalence comparator → Projection.Pipeline orchestrator. The comparator's Tolerance profile is calibrated against a real V1 fixture in slice 5; budget for false-positive triage.

**Watch for:** SsKey synthesis question (item 1.1 from CHAPTER_3_PRESCOPE_READSIDE_ADAPTER.md). Recommendation: physical-coordinate matching, not SsKey matching, for the deployed-side comparator.

**Leverage:** The triangulation comparator (C_ossys / C_v1 / C_round) attributes divergences to V1 / V2 / comparator. Without this attribution, every divergence is a debug session.

### Chapter 3.2 — SnapshotRowsets adapter variant

**Strategic frame:** Resolves the JSON-projection-lossiness class. After this chapter, A1's bound through `SnapshotJson` is *historical* — every new fixture flows through `SnapshotRowsets` with `OssysOriginal` SsKeys.

**Sequencing:** Rowsets 1-3 first (Module + Entity + Attribute) — the load-bearing identity-carrying triple. Rowsets 4-6 (References + Physical + Column reality) come after. Rowsets 7-23 (CHECK constraints, triggers, JSON aggregates) are deferred per the V2-scope decisions.

**Watch for:** Cross-module FK rule 16 — V1's same-module assumption surfaces here. Land it inside slice 2 or 3, not as a standalone slice.

**Leverage:** Once `OssysOriginal` SsKeys flow, `RefactorLogEmitter` (chapter 3.5) becomes structurally meaningful. Until then, A1 is bounded; tests document the bound.

### Chapter 3.3 — DacpacEmitter + DacFx wrapper

**Strategic frame:** The fast-iteration surface. This chapter is gated on the chapter-3 cross-cutting refactor (`ArtifactByKind`); land that first.

**Sequencing:** F# emits T-SQL strings; C# `Projection.Targets.SSDT.Dacpac` C# project owns DacFx. The byte-canonicalization post-pass (Origin.xml timestamp pinning, model.xml checksum recomputation) lives in the C# wrapper, not in F#.

**Watch for:** `BuildPackage` non-determinism. T1 amends to "model-equivalence" for binary, not byte-equality. The amendment is load-bearing.

**Leverage:** Once shipped, every property in chapter 3.4's tier-2 surface uses this emitter. Get it right; the property surface compounds on it.

### Chapter 3.4 — Canary as property-test surface

**Strategic frame:** This is *the* verification multiplier. ~600 lines of test code replaces ~2000+ lines of curated integration tests. Tier-1 pure properties run in pre-commit; tier-2 in CI; tier-3 nightly.

**Sequencing:** CatalogGen.fs first (the generator). Then tier-1 properties (T1, T11, T2 coproduct). Then tier-2 properties (`roundTripBySsKey`, `idempotentRedeploy`). Then tier-3 regression captures (append-only).

**Watch for:** Generator size knobs. Catalogs of 1–3 modules × 1–4 kinds × 1–6 attributes is the right tier-2 budget (~150ms per case). Don't widen prematurely.

**Leverage:** Every chapter-4 chapter inherits the predicate library. Adding `policyOrthogonal` for chapter 4.2's UserMatching axis is a one-line property extension.

### Chapter 3.5 — RefactorLogEmitter + CatalogDiff

**Strategic frame:** The slice that closes A1 in operational terms. Without `RefactorLogEmitter`, every renamed column on redeploy DROPs and CREATEs; with it, DacFx applies `sp_rename`.

**Sequencing:** `CatalogDiff` smart constructor (with exhaustiveness invariant) → `SsKey.fromV1` UUIDv5 mapping → `RefactorLogEntry` shape → `RefactorLogEmitter` as Π over diff → `Render.toRefactorLogXml` rendering layer.

**Watch for:** `ChangeDateTime` is the one non-deterministic field SSDT's `.refactorlog` writes by default. V2 pins it to `2000-01-01T00:00:00Z` for T1 byte-determinism. DacFx ignores `ChangeDateTime` for refactor application.

**Leverage:** RefactorLogEmitter is structurally **just another Π** — same `Emitter<'a>` shape, same T11 theorem. The "RefactorLog is special" framing is dissolved.

### Chapter 3 cross-cutting — ArtifactByKind + SsKey DU split + CatalogDiff

**Strategic frame:** The foundation step. Land before any chapter that ships a new emitter.

**Sequencing:** Per Appendix H §H.7's six-step plan: ArtifactByKind types → migrate RawTextEmitter → migrate JsonEmitter → migrate DistributionsEmitter → SsKey DU split (big-bang within Core) → retire substring T11 enforcement → CatalogDiff + RefactorLogEmitter (chapter 3.5 tail).

**Watch for:** Strict-equal smart constructor (extra keys are `UnexpectedKind` errors, not benign). UUIDv5 namespace stability — once chosen, can never change without invalidating every `V1Mapped` value.

**Leverage:** Every downstream emitter is T11-by-type-construction. Drift detection becomes pointwise per-SsKey diff. Partial-state remediation is `dacpacEmitter (CatalogDiff.between deployed target)`.

### Chapter 4.1.A — SSDT DDL emitter + Manifest

**Strategic frame:** The production-deployment surface. Promoted into Azure DevOps integration-test lane. T11 cross-validates against DACPAC.

**Sequencing:** 10 slices in `CHAPTER_4_PRESCOPE_SSDT_DDL_EMITTER.md`. Most are inherited V1 conventions (per-table, naming, ordering, manifest). Slices 7-8 are gated on chapter 3.2 (IR widening for IsIdentity / Default / Description).

**Watch for:** Newline normalization (V2 LF unconditional vs V1 OS-dependent — Tolerance entry). Manifest's `Fingerprint` algorithm divergence (Tolerance entry). Cross-module FK resolution depends on chapter 3.2.

**Leverage:** Cohabit `Projection.Targets.SSDT/`; share `SqlTypeMap.fs`, `IndexNameFactory.fs`, `ForeignKeyNameFactory.fs` with `RawTextEmitter` and `DacpacEmitter`. T11 cross-validation is a one-line property test.

### Chapter 4.1.B — CDC-aware data triumvirate

**Strategic frame:** The cutover-blocking property lives here — `redeploy-zero-CDC-record` on the promoted lane. V2's load-bearing differentiator over V1 is the change-detection MERGE predicate.

**Sequencing:** `EmissionPolicy.DataComposition` DU + composer skeleton → `CdcAwareness` discovery in read-side adapter → StaticSeedsEmitter minimal → topological ordering → two-phase insertion for cycles → **idempotent MERGE pattern (the load-bearing slice)** → MigrationDependenciesEmitter → BootstrapEmitter → promoted-lane integration test.

**Watch for:** SQL Server's three-valued logic on NULL. The change-detection predicate must be `(Target.[Col] <> Source.[Col] OR null-state-different)` per non-PK column. Plain inequality misses null-state changes.

**Leverage:** `CdcAwareness` is on Profile (evidence), not Policy (intent). The dispatch is per-kind via `Profile.CdcAwareness`. Four-environment cutover with different CDC subsets per environment falls out of A18 amended for free.

### Chapter 4.2 — User FK reflow as Policy

**Strategic frame:** Cashes out V1's UAT-Users 7-step pipeline as a single F# pure pass plus structured Policy axis. The seven V1 steps collapse into: (1) `Profile.SourceUsers` + `Profile.TargetUsers` populated by adapter; (2) `UserFkReflowPass.discover` produces `UserRemapContext`; (3) data-emission triumvirate consumes the context.

**Sequencing:** `UserMatchingStrategy` DU + identity types → `UserPopulation` in Profile → `UserRemapContext` shape → discover pass with `ByEmail` only → add other strategies → `IsUserFk` flag on `Reference` → wire into emitters.

**Watch for:** Identity ambiguity (two source users with same email). V2's choice: first-match-with-Warning. `Map<SourceUserId, TargetUserId>` not the masterwork's nested `Map<SsKey, Map<...>>` (single user kind degenerates the outer map).

**Leverage:** V1's 7-step pipeline distillation into one pass + one DU + one IR flag is a 4× LOC reduction. The pass is pure; tier-1 properties cover most coverage.

### Chapter 4.3 — Operational diagnostics V2

**Strategic frame:** Three new sibling Π's projecting V2's existing `Diagnostics<'a>` payload into the three V1 operator-facing JSON files. The work is *projection*, not new algebra.

**Sequencing:** V1 schema documentation → `DecisionLogEmitter` minimal → `OpportunitiesEmitter` (Code-prefix routing) → `ValidationsEmitter` → CLI wire-up → V1 differential test.

**Watch for:** Refusal of three-channel split (per pre-scope §1.4). The three artifacts ARE the three channels; routing happens at emit time via Code-prefix table. No `DiagnosticChannel` DU. Document this decision in DECISIONS.md to prevent re-emergence.

**Leverage:** Code-prefix routing is a 30-LOC shared module consumed by all three emitters. The differential test against V1 surfaces every collapse-of-V1-flag-into-V2-DU as a named tolerance.

### Chapter 4.4 — RemediationEmitter

**Strategic frame:** Closes the partial-state recovery gap (R5 from VISION_REVIEW). Thin composition over read-side + DacpacEmitter + CatalogDiff. Zero new algebra; ~360 LOC.

**Sequencing:** Additive-only minimal slice → refactor.log composition → subtractive support behind `--allow-subtractive` flag → promoted-lane integration test for partial-deploy failure modes.

**Watch for:** Subtractive remediation is the data-loss surface. Layered gates: structural (Error variant) + documentation (in-DACPAC manifest comment) + property tests. Default-deny; operator opts in.

**Leverage:** The convergence property is the chapter's signature claim — `deploy target → corrupt schema → remediate → deploy remediation → assert deployed = target`. This *is* the proof that R5 is closed.

---

## Closing — hold the spine

The maintainer is one person plus AI collaboration, facing 375 backlog items, 10 chapter pre-scopes, and a real-world cutover whose CDC-dependent features cannot tolerate spurious change records. The work is large.

**The work is also tractable** because the algebra compounds:
- Three type theorems (T1, T11, A1) eliminate whole classes of bugs at compile time.
- One foundation chapter (`ArtifactByKind` + `SsKey` DU + `CatalogDiff`) makes every later chapter incremental.
- One property-test surface (FsCheck + tiered predicates) verifies each axiom across hundreds of generated cases.
- One V1-as-oracle pattern (triangulation comparator) attributes every divergence.
- One discipline operating cycle (chapter open / mid / close) catches drift before it compounds.

**When stuck, return to the algebra.** The forcing question is always: *which axis owns this concern?* — Catalog (structure) / Policy (intent) / Profile (evidence) / Lifecycle (time). Most apparent ambiguities resolve once the axis is named.

**When tempted to shortcut, remember the disciplines.** Trace V1 before writing tests. Wait for the second consumer before extracting. Add the AXIOMS amendment at chapter close. Each shortcut taken is a debug session deferred.

**When the work feels heavy, look at the multiplier.** A new emitter is 100-200 LOC F#. A new pass is 80-150. A new property test is one line. The patterns are documented; the algebra carries the weight; the chapter pre-scopes carry the slice plans; the backlog tracks the gaps. The hard work is the foundation step. Land it; the rest compounds.

V1 ships the cutover. V2 makes it verifiable. Three type theorems, one foundation refactor, one property surface, one triangulation comparator, one fallback ladder, ten chapters. Hold the spine. The rest follows.

— Recorded for the receiving agent.
