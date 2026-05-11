# Chapter 3.x open — `DacpacEmitter` (developer-tooling sibling Π over DacFx)

**Sessions:** opens with this document (2026-05-11). **Posture:** dev-tooling sibling-Π emitter — **not** on V2-driver KPI's production critical path. **Predecessors:** chapter 4.3 (Operational Diagnostics V2 closed; V2-driver KPI Phase 5 shipped front-to-back). **Pre-scope reference:** `CHAPTER_3_PRESCOPE_DACPAC_EMITTER.md` (subagent #4, session 25; originally framed deploy-path-conditional — this chapter reframes per the operator's 2026-05-11 dev-tooling directive below).

This is the chapter-open document per `DECISIONS 2026-05-15 — Strategic frame`. Companion close synthesis lands at `CHAPTER_3_X_CLOSE.md` when this chapter ends.

---

## Why this chapter — the operator's reframe

The 2026-05-11 operator directive replaces the pre-scope's deploy-path framing:

> "I think the DacpacEmitter is okay to decline in favor of SSDT in terms of deploy path, however I would like to go for it. Let's bring it on in terms of having an emission target so that I can stand up a local copy of the database in no time flat — almost a one-click deploy strategy for my development team to be able to query the database locally or develop on it locally. This would not be used for the production path until we identify a reason why it should."

**Production deploy path stays SSDT-style** (`SsdtDdlEmitter.emitSlices`'s per-module/per-table `.sql` directory bundle, the V1-compatible artifact the operator's Azure DevOps pipeline already consumes; per chapter 4.1.A close). **DacpacEmitter is the dev-tooling artifact** — one-click local stand-up so the dev team can query / develop against the projected schema without provisioning per-file deploys. The artifact format (`.dacpac`) is the deploy currency of `sqlpackage.exe`, Visual Studio's "Publish DAC Package," and `DacServices.Deploy(...)`, all of which the dev team's existing tooling already speaks.

**The Tier-3 `text-builder-as-first-instinct` Active deferral still binds** — DacpacEmitter MUST adopt `Microsoft.SqlServer.Dac` (DacFx); hand-rolling `.dacpac` zip + XML is forbidden regardless of the deploy-path scoping. This chapter discharges that hard requirement under the dev-tooling reframe rather than under the production critical path.

---

## Strategic frame — eight axes named at chapter open

Per the chapter-4.1.A / 4.1.B / 4.2 / 4.3 precedent, multi-session chapters name their load-bearing axes at chapter open before substantive slices begin.

1. **DDD — `DacpacEmitter` is the dev-tooling artifact name.** Concept-shaped, not action-shaped. The bounded context is "schema artifact format" — the `.dacpac` is the **artifact**, the dev team's `DacServices.Deploy` is the **deploy mechanism**. `DacpacEmitter` names what the module emits, not what consumers do with the bytes (per pillar 8). Sibling to `SsdtDdlEmitter` (production) and `JsonEmitter` (snapshot) inside `Projection.Targets.SSDT`.

2. **FP — DacpacEmitter delegates to `SsdtDdlEmitter.statements` and renders to DacFx.** Per A35 (Π's canonical output is a typed deterministic statement stream): the same `seq<Statement>` that feeds the SSDT directory bundle feeds the dacpac. The new module's responsibility is **the DacFx ingestion + serialization side**, not statement generation. Slot: `Catalog -> seq<Statement> -> Result<byte[]>` — DacFx serialization is the only new code.

3. **Hardcore (no-string-concatenation / built-in obligation) — DacFx for the binary surface.** Per pillar 3 + pillar 7 (gold-standard library precedence) + the Tier-3 hard-required deferral: `Microsoft.SqlServer.DacFx` v162.x is the typed-AST library that produces `.dacpac` bytes. The four-question analysis discharges to (1) DacFx — the Microsoft-canonical typed-AST library for `.dacpac`; (2) not yet in codebase (chapter introduces it); (3) cost: one NuGet PackageReference + ~50 LOC wrapper; (4) no structural reason it doesn't apply. **No `System.IO.Packaging` zip surgery; no XML composition; no `StringBuilder`-of-script-bundle as the binary substrate.**

4. **Streaming — bench observability at emit time.** `Bench.scope "emit.dacpac.statements"` records the statement-stream consumption; `Bench.scope "emit.dacpac.buildPackage"` records DacFx's `BuildPackage` call (the dominant cost; ~hundreds of ms on a 300-table catalog per DacFx's documented ms/object).

5. **Hexagonal — DacFx is a foreign API; F# wrapper stays inside `Projection.Targets.SSDT`.** Per `DECISIONS 2026-05-09 — Adapter language choice` (the empirical condition): C# was reserved for foreign APIs whose surface was "unfriendly from F#." DacFx's surface for V2's use case is small — `new TSqlModel(SqlServerVersion.Sql160) |> use`, `model.AddObjects(scriptText)`, `use stream = ...`, `DacPackageExtensions.BuildPackage(stream, model, metadata)` — four calls, all `IDisposable`-aware F# handles natively via `use`. ScriptDom is already used directly from F# at `ScriptDomBuild.fs`; the precedent is established. **Decision (codified at chapter open): pure F# wrapper inside `Projection.Targets.SSDT`; no C# subproject.** The pre-scope's bias toward a new C# project yields under the empirical pressure that (a) `Projection.Pipeline` ended up F# and (b) DacFx's surface for our use case is small.

6. **Built-in obligation — DacFx Public Model API end-to-end.** `TSqlModel` + `DacPackageExtensions.BuildPackage` + `DacPackage.Load` (for round-trip tests). No third-party `.dacpac` libraries; no script-text-builder simulating `.dacpac` shape. Pillar 7 holds.

7. **Aggregate-root + smart constructor — `Catalog -> Result<byte[]>` value-typed seam.** `DacpacEmitter.emit : Catalog -> Result<byte[]>` is the public surface. Failure modes (DacFx validation errors — FK without target PK, malformed type, etc.) surface as `Result.failure` with `ValidationError`s carrying DacFx's `ModelError` text. Smart-constructor pattern: callers pattern-match the Result; no exception escapes.

8. **Test-fidelity — three property tests at chapter signature.**
   - **DacFx round-trip (slice α; replaces byte-equality T1 for binary emitters).** `Catalog → emit → DacPackage.Load → TSqlModel.GetObjects → re-derive table count` equals source kind count. The byte stream is not byte-deterministic (DacFx embeds wall-clock timestamps in `Origin.xml`); content equality via DacFx round-trip IS the T1 amendment for binary emitters (pre-scope §6.1 option b).
   - **T11 sibling-Π commutativity.** `SsdtDdlEmitter.emitSlices catalog` and `DacpacEmitter.emit catalog |> DacPackage.Load |> GetObjects` mention the same SsKey-root set. Same Catalog ⇒ same kind-mention set across siblings.
   - **A18 amended honored structurally.** `DacpacEmitter.emit : Catalog -> Result<byte[]>` — no Policy parameter; no Profile parameter (the first-slice scope does not consume distributions; widen the signature when a slice forces it).

---

## Slice scope — chapter ordering

Per the pre-scope §5 sequencing (refined for the dev-tooling framing):

| # | Slice | What |
|---|---|---|
| α | DacpacEmitter v0 (shipped) | Single-Kind Catalog → `byte[]` round-trip via DacFx; T11 commutativity test vs `SsdtDdlEmitter` |
| β | Multi-Kind + FK (shipped) | Inline `FOREIGN KEY ... REFERENCES` across Kinds; DacFx FK validation succeeds (target PK declared per pre-scope §2); ForeignKeyConstraint round-trip test |
| γ | Indexes (shipped) | Single-column + composite + unique + non-unique CREATE INDEX; Index.Unique property preserved through DacFx round-trip |
| δ_dock | DockerImageEmitter (shipped; reframes pre-scope §5 slice δ) | Emits Docker build context (Dockerfile + dacpac + entrypoint.sh + README.md). Builds a self-contained `mcr.microsoft.com/mssql/server:2022-latest`-based image that bakes in the dacpac + installs sqlpackage + entrypoint publishes on container start. **CI/CD-built + registry-published**: dev team `docker pull` + `docker run` with no source checkout. Replaces the original "CLI `dac deploy` verb" framing per operator directive ("single command up; my team doesn't have to have the repository to pull the data fresh each time"). |
| ε | Modality marks → comments / extended properties | Surface `TenantScoped` / `SoftDeletable` annotations on the dacpac (decision: comments first; extended properties when a downstream consumer demands structured access) |
| ζ | Byte-determinism cash-out (deferred) | Post-hoc canonicalization (rewrite `Origin.xml` timestamps; recompute model checksum; re-pack with pinned zip-entry timestamps). **Deferred-with-trigger**: surface when a snapshot consumer requires byte-stable artifacts. Content-equality T1 is sufficient for dev-tooling. |

**Deferred-with-trigger pre-named:**
- Module → Schema mapping decision (pre-scope §6.3): for slice α the single-Module / single-Schema fixture sidesteps the question; surface at slice β when multi-Module catalogs land.
- Pre/post-deployment scripts (pre-scope §6.5): `BuildPackage` public API forbids them; static-seed data routes through `StaticSeedsEmitter` + `MigrationDependenciesEmitter` + `BootstrapEmitter` separately (chapter 4.1.A precedent). Not in DacpacEmitter scope.
- DacFx version pinning to production SQL Server version: deferred until cutover-team confirms production version; default `SqlServerVersion.Sql160` (matches V1 trunk's ScriptDom Sql160 pin).

---

## What this chapter does NOT do

Per pillar 4 + IR-grows-under-evidence:
- It does NOT add a Profile parameter; the first-slice signature is `Catalog -> Result<byte[]>`.
- It does NOT take over the production deploy path; SSDT directory bundle stays the production-write surface.
- It does NOT compose with `CatalogDiff` (chapter 4.4 RemediationEmitter is the diff consumer; sequenced after this chapter).
- It does NOT promise byte-determinism on the dacpac (DacFx embeds timestamps; content-equality is the T1 amendment for binary emitters).
- It does NOT introduce a C# subproject; F# wrapper inside `Projection.Targets.SSDT` honors `DECISIONS 2026-05-09 — Adapter language choice` under empirical pressure (DacFx's surface is friendly enough from F#).

---

## Risks / open questions deferred to chapter close

1. **DacFx NuGet weight in `Projection.Targets.SSDT`.** Adds `Microsoft.SqlServer.DacFx` v162.x to a project that today is light on transitive dependencies. Surface at chapter close: did the dependency weight justify itself, or should DacpacEmitter migrate to its own `Projection.Targets.SSDT.Dacpac` subproject for project-cohesion reasons?
2. **DacFx version drift vs SQL Server version.** Per pre-scope §6.7 — DacFx version pin should match production SQL Server version. Slice α defaults to `Sql160`; cash out the version-pin decision at chapter close when the cutover team confirms.
3. **Whether `DacServices.Deploy` belongs in `Projection.Pipeline` or in CLI.** Slice δ decides; default bias: CLI verb invokes Pipeline helper.

---

## Test baseline at chapter open

**Branch:** `claude/chapter-4-ddd-improvements-XVCAM`. **Pre-slice-α baseline:** 1012 non-canary tests passing + ~16 Docker-dependent canary tests; 0 skipped; 0 build warnings under `TreatWarningsAsErrors=true`. Lint clean across 27 rules. Slice α adds ~3 tests (round-trip; T11 commutativity; signature shape).

---

## Pre-scope deltas — what this chapter reframes

| Pre-scope framing | Chapter-3.x framing |
|---|---|
| "Deploy-path-conditional V2-driver KPI critical-path" | Dev-tooling sibling-Π emitter; **off** V2-driver critical path |
| "C# wrapper in `Projection.Pipeline` or `Projection.Targets.SSDT.Dacpac`" | Pure F# wrapper in `Projection.Targets.SSDT` (empirical condition fell in F#'s favor) |
| "Read-side first (DACPAC → Catalog), then emitter" | Emitter only; DACPAC read-side **deferred-with-trigger** (dev-tooling doesn't need round-trip; readside trigger is operator drift detection per `DECISIONS 2026-05-15`) |
| "Canary deploys via `DacServices.Deploy`" | Slice δ CLI `dac deploy` verb (dev-tooling, not canary) |
| "Byte-determinism cash-out in slice 8" | Slice ζ deferred-with-trigger (no snapshot consumer of dacpac bytes today) |
| "T1 byte-equality amendment for binary emitters" | Slice α adopts **content-equality via DacFx round-trip** (pre-scope §6.1 option b) |
