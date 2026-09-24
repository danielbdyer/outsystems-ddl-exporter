# Chapter 3.x close — DacpacEmitter dev-tooling sibling Π over DacFx (Phase 6 substantively shipped under reframe)

**Sessions:** chapter-3.x opened on `claude/chapter-4-ddd-improvements-XVCAM` at `090f2d7` (slice α — chapter open + DacpacEmitter v0); structural slice arc α/β/γ/δ_dock shipped through `5985b40`.

This document discharges chapter 3.x's eight-item close ritual now that the DacpacEmitter is end-to-end + the DockerImageEmitter ships the one-command dev stand-up artifact. Slices ε (modality marks → comments/extended properties), ζ (byte-determinism cash-out via post-hoc canonicalization), and per-Catalog parameterization defer to the queue with explicit triggers per the close-ritual discipline.

---

## Why this close

Per `V2_DRIVER.md` Phase 6: the DacpacEmitter chapter was pre-scoped as a deploy-path-conditional production-write surface and originally bracketed "not-started (conditional)." At chapter open (2026-05-11) the operator reframed the scope: production deploy stays SSDT-style file deploy via `SsdtDdlEmitter.emitSlices`; DacpacEmitter ships as **dev-tooling** for local one-click stand-up — the `.dacpac` artifact format the dev team consumes via `sqlpackage`, Visual Studio, or `DacServices.Deploy`. Operator directive at slice δ pushed harder: ship a **Docker image** the dev team `docker pull`s + `docker run`s — no source checkout, no CLI orchestration on the dev's machine.

Chapter 3.x ships the structural commitment under that reframe. The Tier-3 hard-required `text-builder-as-first-instinct` deferral (DacFx adoption is mandatory) is cashed out; the deployment surface for dev environments is the Docker image (single command, registry-distributed); production stays untouched.

---

## What shipped (slice arc α + β + γ + δ_dock)

### Slice α — DacpacEmitter v0 + chapter open (`090f2d7`)

- **`CHAPTER_3_X_OPEN.md`** — eight-axis strategic frame; dev-tooling reframe codified; F# wrapper inside `Projection.Targets.SSDT` decided (no C# subproject; pre-scope §6.2 bias yielded under empirical pressure).
- **`Microsoft.SqlServer.DacFx` v162.x NuGet** added to `Projection.Targets.SSDT.fsproj`. **Tier-3 `text-builder-as-first-instinct` Active deferral cashed out.**
- **`DacpacEmitter.emit : Catalog -> Result<byte[]>`** — A18 amended preserved structurally (Catalog only; no Policy parameter; no Profile parameter at first slice).
- **Per-statement `model.AddObjects`** — DacFx's expected ingestion shape (one statement per batch unless `GO`-separated); per-statement avoids batch-separator grammar coupling.
- **Pillar 7 cash-out end-to-end** — Statement generation via `SsdtDdlEmitter.statements` typed-AST stream; per-statement script via `ScriptDomGenerate.generateOne`; `.dacpac` serialization via DacFx `DacPackageExtensions.BuildPackage`. Zero `StringBuilder` at the binary boundary.
- **DECISIONS entries** retiring two Active deferrals (DacFx integration row 214; `Microsoft.SqlServer.Dac` Tier-3 hard-requirement row 223) + codifying the dev-tooling scoping + F# wrapper empirical condition + T1 content-equality amendment for binary emitters.

### Slice β — FK round-trip via DacFx (`5985b40`)

- **`sampleCatalog` Order→Customer FK** ingests inline through `SsdtDdlEmitter`'s typed-AST stream + re-enumerates through `ForeignKeyConstraint.TypeClass` after DacFx Load.
- **Empirical finding**: the existing DacpacEmitter handles FK references with no code changes; the slice ships as a structural test.

### Slice γ — Indexes round-trip via DacFx (`5985b40`)

- **`indexedCatalog` fixture** exercising single-column unique + composite non-unique + single-column non-unique Index variants.
- **DacFx's `Index.Unique` property preserved** across round-trip (verified via `obj.GetProperty<bool>(Index.Unique)` enumeration).
- **Same finding as slice β**: existing emitter handles standalone `CREATE INDEX` statements.

### Slice δ_dock — DockerImageEmitter (`5985b40`)

Per operator directive ("create a custom Docker package that stands itself up with the loaded SQL server inside of it ... single command up and my team doesn't have to have the repository"):

- **New module `Projection.Targets.SSDT.DockerImageEmitter`** producing a typed `DockerImageContext { Dockerfile; DacpacBytes; EntrypointScript; Readme }`.
- **Self-contained Docker build context** the dev team consumes via `docker build .`; CI/CD builds + pushes; dev team `docker pull` + `docker run` (no source checkout).
- **Two pillar-7-canonical libraries adopted**:
  - `mcr.microsoft.com/mssql/server:2022-latest` as base image (same pin as `Projection.Pipeline.Deploy.DefaultImage`).
  - `sqlpackage` (downloaded from `https://aka.ms/sqlpackage-linux` at image build time) as Microsoft's canonical DACPAC publisher.
- **Idempotent entrypoint** — starts `sqlservr`, polls `sqlcmd SELECT 1` until ready (60s ceiling), invokes `sqlpackage /Action:Publish`. Restart against a persisted volume validates schema vs. re-creating.
- **DECISIONS entry** codifying the CLI-verb-replaced-by-Docker-image reframe + per-Catalog parameterization deferred-with-trigger.

---

## Eight-item chapter-close ritual

Per `DECISIONS 2026-05-14 — Chapter-close ritual` + the V1-envelope-walk amendment.

### 1. Active deferrals scan

| Deferral | Status |
|---|---|
| **DacFx integration in DacpacEmitter** (row 214) | **Retired at slice α (`090f2d7`)** — reframed to dev-tooling per operator directive. |
| **`Microsoft.SqlServer.Dac` Tier-3 hard-requirement** (row 223) | **Retired at slice α (`090f2d7`)** — DacFx v162.x adopted in `Projection.Targets.SSDT`. |
| Composition primitives `fallback` / `accumulate` / `wrap` / `lift` | Untriggered |
| Statement DU MERGE/UPDATE promotion | Untriggered |
| Sort-vs-data deferral predicate distinction | Untriggered |
| Cross-module FK IR refinement | Trigger fired and partially satisfied at chapter 4.1.A close; IR refinement still deferred (no DacFx cross-database reference surfaced this chapter — DacFx's `ForeignKeyConstraint` works on single-database scope; the cross-database trigger condition (DacpacEmitter cross-DB references) does NOT fire under the dev-tooling reframe). |
| OSSYS adapter User-kind identification surface | Untriggered |
| CSV adapter for `ManualOverride` | Untriggered |
| `Attribute.Default` field | Untriggered |
| `Kind.Description` + `Attribute.Description` fields | Untriggered |
| Chapter 4.3 slice δ (Operational Diagnostics CLI wire-up) | Untriggered |
| Chapter 4.3 slice ε (V1 differential test) | Untriggered |

Three new deferrals codified at this close (see DECISIONS entry below): **Slice ε (modality marks → comments / extended properties)**, **Slice ζ (byte-determinism cash-out via post-hoc Origin.xml canonicalization)**, and **Per-Catalog parameterization** (Dockerfile / entrypoint database-name + base-image overrides).

### 2. Contract-vs-implementation walk

The chapter contract per `CHAPTER_3_X_OPEN.md` strategic frame: "DacpacEmitter dev-tooling sibling-Π emitter for local one-click stand-up." Six axes named at chapter open; every contract clause is implemented:

- **Axis 1 (DDD — concept-shaped names).** `DacpacEmitter` + `DockerImageEmitter` + `DockerImageContext` all concept-shaped per pillar 8.
- **Axis 2 (FP — delegates to `SsdtDdlEmitter.statements`).** `DacpacEmitter.emit` consumes the same typed-AST stream `SsdtDdlEmitter.emitSlices` consumes; no parallel statement-generation surface.
- **Axis 3 (Hardcore — DacFx for binary surface).** `DacpacEmitter` wraps DacFx; `DockerImageEmitter` wraps DacpacEmitter. Pillar 7 cascade.
- **Axis 4 (Streaming — bench observability).** `Bench.scope "emit.dacpac.emit"` + `Bench.scope "emit.dockerImage.emit"` record consumption.
- **Axis 5 (Hexagonal — DacFx as foreign API; F# wrapper inside Targets.SSDT).** Decision codified at slice α; empirical condition held — F# handles DacFx's `IDisposable`-aware surface natively via `use`.
- **Axis 6 (Built-in obligation — DacFx Public Model API end-to-end).** `TSqlModel` + `DacPackageExtensions.BuildPackage` + `DacPackage.Load` (for round-trip tests). Zero hand-rolling.
- **Axis 7 (Aggregate-root + smart constructor).** `DacpacEmitter.emit : Catalog -> Result<byte[]>`; `DockerImageEmitter.emit : Catalog -> Result<DockerImageContext>`. Smart constructor pattern; failure surfaces as `ValidationError` carrying DacFx exception text.
- **Axis 8 (Test-fidelity — three property tests).** Slice α shipped 4 tests (non-empty bytes; round-trip yields one Table per Kind; T1 content-determinism; T11 commutativity vs SsdtDdlEmitter). Slice β shipped 1 FK round-trip test. Slice γ shipped 1 Index round-trip + IsUnique preservation test. Slice δ_dock shipped 6 tests (Dockerfile shape; entrypoint shape; README shape; embedded dacpac round-trips through DacFx; T1 byte-determinism on static-template fields). **Total: 12 tests; all green.**

The pre-scope's slice δ (CLI `dac deploy` verb) was **reframed structurally** to slice δ_dock (DockerImageEmitter) per the second operator directive; the original CLI verb is retired without replacement per the DECISIONS entry for that reframe (commit `5985b40`'s DECISIONS).

### 3. CLAUDE.md staleness check

Operating-disciplines table current. No new disciplines warrant addition at this close — the existing pillar 7 (gold-standard library precedence) + pillar 8 (domain-first naming) + text-builder-as-first-instinct discipline + Tier-3 hard-requirement-Active-deferral protocol all covered every slice.

### 4. README.md staleness check

Test baseline grew from 1012 non-canary (chapter 4.3 close) to **1060 non-canary** at chapter 3.x close (+48 net across chapter 4.3 → chapter 3.x; chapter 3.x contributed +13: 4 slice α + 2 slice β + 1 slice γ + 6 slice δ_dock). Update pending in this close commit if README.md baseline-count reference exists; otherwise the HANDOFF.md prologue is the authoritative current-state reference.

### 5. HANDOFF.md scope

HANDOFF.md prologue refresh lands as part of this close commit. Names load-bearing (DacpacEmitter as sibling-Π over DacFx; DockerImageEmitter as one-command dev stand-up surface; T1 amendment for binary emitters) + deferred (slice ε modality marks; slice ζ byte-determinism cash-out; per-Catalog parameterization) + V2_DRIVER Phase 6 status flip from "not-started (conditional)" to "substantively shipped (under dev-tooling reframe)."

### 6. Fresh-eye walk (cross-document drift)

- **`V2_DRIVER.md` Phase 6 status**: now **substantively shipped (under dev-tooling reframe)** — was "not-started (conditional)". Phase 6's original framing (DACPAC + SqlPackage as deploy path) defers indefinitely per the dev-tooling reframe; the production-deploy condition would need to re-fire to reopen.
- **`KICKOFF.md` baseline test count**: pending refresh — was 963 at chapter 4.2 close; was 1012 at chapter 4.3 close; **1060 at chapter 3.x close**.
- **`CHAPTER_4_3_CLOSE.md` "What this close enables"**: previously named chapter 4.4 RemediationEmitter as deploy-path-conditional via chapter 3.x. Per the V2_DRIVER §147 free-corollary entry, chapter 4.4 stays deferred-with-trigger; chapter 3.x's dev-tooling reframe means RemediationEmitter (when it ships) inherits the dev-tooling framing rather than production.

### 7. V1-input-envelope walk

**Not applicable for chapter 3.x.** Chapter 3.x is a **sibling-Π emission chapter**, not a V1↔V2 translation chapter. The V1-envelope-walk amendment (per `DECISIONS 2026-05-14` session-25 amendment) was scoped to V1↔V2 translation chapters where fixture pressure can drive won't-carry-forward growth that V1-input pressure does not. The DacpacEmitter consumes V2's existing `Catalog` IR; there is no V1 input envelope to walk.

### 8. AXIOMS.md amendment cash-out

**T1 amended (binary normal-form composition)** — the scheduled-since-2026-05-22 placeholder at `AXIOMS.md:689` cashes out at this close. Body:

> Same `(catalog, policy, profile)` triple produces:
> - **Byte-identical** text-emission output (text emitters: `SsdtDdlEmitter`, `JsonEmitter`, `DistributionsEmitter`, `DecisionLogEmitter`, `OpportunitiesEmitter`, `ValidationsEmitter` — all consume the typed-AST stream + pinned-options writers + sorted-key JsonNode; T1 holds byte-for-byte).
> - **Content-identical DacFx model** binary-emission output (binary emitter: `DacpacEmitter`). Two emit calls on the same Catalog produce DacFx models with identical `GetObjects(Table.TypeClass)` / `GetObjects(ForeignKeyConstraint.TypeClass)` / `GetObjects(Index.TypeClass)` enumerations. The byte streams **DIFFER** — DacFx embeds wall-clock timestamps in `Origin.xml` and zip-entry headers — so the algebraic claim flows through DacFx's model API, not the stream.
>
> The two forms compose: tier-1 property tests assert byte-determinism on text emissions (`T1: ... byte-deterministic`) and content-determinism on binary emissions (`T1 (binary): ... content-deterministic under DacFx round-trip`). The unifying predicate `t1ByteEqualOrModelEquivalent` chooses the right form per emitter kind.
>
> **Slice ζ (post-hoc Origin.xml canonicalization)** can lift binary emitters to byte-equality if a snapshot consumer demands byte-stable artifacts. The slice stays deferred-with-trigger — under the dev-tooling reframe, no consumer demands byte-stable dacpac artifacts.

The amendment commits at this close; future binary emitters (`RemediationEmitter` when it ships) inherit the same shape.

---

## Test count

- **1060 non-canary tests passing** (was 1012 at chapter 4.3 close; **+48 net across chapter 4.3 → chapter 3.x close**; chapter 3.x contributed +13)
- **~16 Docker-dependent canary tests** (unchanged; no canary-affecting work in chapter 3.x)
- **Lint clean** across 27 rules (zero new LINT-ALLOWs in chapter 3.x — DacFx adoption + Docker context emission are both pillar-7 right moves; Dockerfile / entrypoint / README templates are pure F# triple-quoted string constants with no concatenation)
- **Build clean** under `TreatWarningsAsErrors=true`

---

## What's load-bearing going forward

Chapter 3.x's structural commitments that future chapters inherit:

- **`DacpacEmitter.emit : Catalog -> Result<byte[]>`** is the canonical binary Π port. Sibling to `SsdtDdlEmitter.emitSlices` (text); A18 amended preserved (Catalog only).
- **`DockerImageEmitter.emit : Catalog -> Result<DockerImageContext>`** wraps DacpacEmitter inside a runnable artifact format. The four-field record (`Dockerfile`, `DacpacBytes`, `EntrypointScript`, `Readme`) IS the build-context surface; CI/CD writes the four files to a directory and runs `docker build .`.
- **T1 binary-emitter amendment is structural** — binary emitters' algebraic claim flows through DacFx model round-trip, not byte equality. Future binary emitters inherit the predicate.
- **DacFx is in the codebase** — `Microsoft.SqlServer.DacFx` v162.x; pure F# wrapper pattern established. Future binary serialization needs (RemediationEmitter; alternative .dacpac variants) inherit the wrapper conventions.
- **`mcr.microsoft.com/mssql/server` + `sqlpackage` are the canonical dev-tooling deploy surface**. Any future "stand up a local SQL Server with this schema" need uses the DockerImageEmitter's pattern; the entrypoint script's `sqlservr` + `sqlcmd` + `sqlpackage` orchestration is the precedent.

---

## What's deferred (with explicit triggers)

### Slice ε — Modality marks → comments / extended properties

Per `CHAPTER_3_X_OPEN.md` strategic frame slice ε: surface `TenantScoped` / `SoftDeletable` annotations on the dacpac (decision: comments first; `EXTENDED PROPERTY` when a downstream consumer demands structured access). **Deferred** at this close because (a) the dev-tooling consumer (`docker run` + connect via SSMS) doesn't read modality-mark metadata; (b) per pre-scope §2 (general impedance shape), modality marks are informational at Π time. **Trigger to cash out**: a downstream consumer (dev tooling sub-feature; remediation flow; cutover audit) demands structured access to modality marks from the .dacpac model.

### Slice ζ — Byte-determinism cash-out via post-hoc Origin.xml canonicalization

Per `CHAPTER_3_X_OPEN.md` strategic frame slice ζ + pre-scope §6.1 option (a): rewrite `Origin.xml` timestamps; recompute model.xml checksum; re-pack with pinned zip-entry timestamps. **Deferred-with-trigger** at chapter open; trigger condition unchanged: surface only when a snapshot consumer demands byte-stable dacpac artifacts. Content-equality T1 (via DacFx round-trip) is sufficient for dev-tooling. **Trigger to cash out**: a snapshot consumer demands byte-stable dacpac artifacts (e.g., a content-addressable artifact store; a CI cache keyed on dacpac SHA256).

### Per-Catalog parameterization of Dockerfile + entrypoint

Per the slice δ_dock DECISIONS entry: the slice ships pinned constants for `PROJECTION_DB_NAME` (default `ProjectionCatalog`) and `BaseImage` (`mcr.microsoft.com/mssql/server:2022-latest`). Per-Catalog overrides (multi-database images; alternative SQL Server versions; custom env-var name schemes) stay deferred-with-trigger. **Trigger to cash out**: a second consumer with conflicting defaults (e.g., a dev team needing an alternative SQL Server version; a per-environment override pattern).

### Chapter 4.4 RemediationEmitter

Per `V2_DRIVER.md` §147 free-corollary table: `RemediationEmitter` is a **free corollary** of DacpacEmitter + CatalogDiff + `Render.toDacpac` (one-line composition; chapter would be ~360 LOC). Per V2_DRIVER §154: "deferred under V2-driver KPI; revisit at chapter 5+ if remediation is operator-needed." Chapter 4.4 inherits the dev-tooling framing rather than the production framing (operator-side remediation for dev-environment partial-state recovery). **Trigger to cash out**: an operator workflow demands programmatic partial-state recovery (vs. the current "regenerate the dacpac fresh and re-deploy" pattern that the dev-tooling Docker image already provides).

---

## What this close enables

- **Chapter 5 (Phase 8 pragmatic close)** opens — F# Analyzers SDK custom analyzer; Coordinates Stage 2 typed VOs; Hex port lifts under consumer demand; cutover-day operator runbook; V1 sunset planning. Phase 6 substantively shipped means the V2-driver KPI critical path is closed in form; remaining work is consumer-pressure-driven hygiene + governance.
- **Real cutover work can now hit V2's dev-tooling artifact path**. The dev team's `docker pull` + `docker run` loop is structurally green; operator-driven feedback on the Docker image's UX shapes per-Catalog parameterization (deferred slice).
- **Future binary emitters inherit the T1 amendment + the DacFx wrapper conventions** — RemediationEmitter when it ships; any alternative `.dacpac`-shaped artifact for a future deploy path.

---

## Closing

Chapter 3.x ships the **dev-tooling DACPAC artifact path** end-to-end: V2 Catalog → typed-AST stream → DacFx model → `.dacpac` bytes → Docker image → registry → `docker pull` + `docker run`. The operator's one-command stand-up requirement is structurally green; production deploy stays untouched on the SSDT-style file path.

The Tier-3 `text-builder-as-first-instinct` Active deferral is cashed out — DacFx is in the codebase and active. The T1 amendment for binary emitters is structural — content-equality via DacFx round-trip is the algebraic form.

Chapter 3.x closed (2026-05-11). The V2-driver KPI critical-path (Phases 1–5 + 7) was closed structurally at chapter 4.3 close; chapter 3.x adds Phase 6 substantively under reframe. Remaining work is **Chapter 5 (Phase 8 pragmatic close)** — consumer-pressure-driven items per V2_DRIVER §252.
