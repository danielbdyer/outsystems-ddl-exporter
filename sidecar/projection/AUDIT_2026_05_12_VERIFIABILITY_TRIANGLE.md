# Verifiability Triangle Audit — 2026-05-12

**Companion document to:** `V2_PRODUCTION_CUTOVER.md`, `AXIOMS.md`, `VISION.md`, `CLAUDE.md`.
**Author voice:** synthesis of a three-agent parallel audit (L3 articulation + L2↔L3 bridge + L3 gap-hunt) plus a prior-round structural-commitments inventory and three-agent illegal-states audit. Integrated by the session's principal voice (the agent driving the airtight-by-design directive at the user's instruction).
**Scope:** the V2 OutSystems-DDL-exporter sidecar at `sidecar/projection/`. Not the trunk V1 (`src/`); not the operator's deployment harness; not the future Phase-B SQL-side ports.
**Status:** Draft 1 — captures the audit state at 2026-05-12. Subject to amendment as the campaigns described in Part IX execute. The campaigns themselves will produce per-slice close notes that append to Part XII below.
**Reading order for fresh agents:** read Part I (framing) first; skim Parts II–V (the inventory by level); read Parts VI–VIII (the gap analysis) substantively; treat Parts IX–X as the action plan.

---

## Table of Contents

1. [Preamble](#preamble)
2. [Part I — The Verifiability Triangle](#part-i--the-verifiability-triangle)
   - 1.1 The four levels (L0–L3)
   - 1.2 The two failure modes
   - 1.3 Methodology — top-down + bottom-up + adversarial
   - 1.4 Why this matters now
3. [Part II — The Five Core Concerns](#part-ii--the-five-core-concerns)
4. [Part III — L3 Product Axiom Catalog](#part-iii--l3-product-axiom-catalog)
5. [Part IV — L2 Existing Axiom Coverage Map](#part-iv--l2-existing-axiom-coverage-map)
6. [Part V — L1 Structural Commitments Inventory](#part-v--l1-structural-commitments-inventory)
7. [Part VI — The Catalog of Illegal States Still Representable](#part-vi--the-catalog-of-illegal-states-still-representable)
8. [Part VII — The Three Rings of Weakness](#part-vii--the-three-rings-of-weakness)
9. [Part VIII — The Gap-Hunt Findings](#part-viii--the-gap-hunt-findings)
10. [Part IX — Proposed Implementation Campaigns](#part-ix--proposed-implementation-campaigns)
11. [Part X — Per-Concern Coverage Matrix](#part-x--per-concern-coverage-matrix)
12. [Part XI — Methodology Reflections](#part-xi--methodology-reflections)
13. [Part XII — Addenda (append-only)](#part-xii--addenda-append-only)
14. [Appendices](#appendices)

---

## Preamble

This audit answers a question the user posed in two halves over the course of one session:

> First half: "There's still places where illegal states are representable in V2. Walk the codebase and find them. The ship should be airtight by design."
>
> Second half: "There are some axiomatic truths of what must be true from a product-requirements standpoint if we were to go end-to-end verifiable with each of the core concerns of the codebase. There is a way we can connect these structural commitments to those, and possibly even discover more."

The answer required triangulation. The bottom-up direction — finding representable illegal states in the F# code — produces a catalog of refactor opportunities but doesn't tell you *which* are load-bearing. The top-down direction — articulating what V2 must verifiably guarantee as V2-driver — produces a set of product axioms but doesn't tell you whether they're operationally honored. Only the **bidirectional view** — every L3 axiom traced down through L2 and L1 to code, every L1 commitment traced up through L2 to a product behavior — reveals which gaps matter and which don't.

This document captures the bidirectional view at a fixed moment in time (post-Slice-1, mid-campaign-planning). It is intentionally comprehensive: every axiom, every commitment, every gap, every cross-reference. It is **not** the canonical home for any of these surfaces — `AXIOMS.md` remains canonical for L2 axioms; `V2_PRODUCTION_CUTOVER.md` remains canonical for the cutover plan; the code itself remains canonical for L1 commitments — but this document is the integrator's view. Future audits append to Part XII; campaigns close in their own dated documents and reference back here.

The document's *function* is to be unreadably-comprehensive in one place so subsequent decisions can reference a single source of truth. The document's *form* — long, encyclopedic, with tables — is in service of that function.

---

## Part I — The Verifiability Triangle

### 1.1 The four levels (L0–L3)

```
Level 3 — Product axioms          What must be verifiable end-to-end
            ↑                        for V2 to ship as V2-driver
            │
            │ underwritten by
            ↓
Level 2 — AXIOMS.md               The formal system (A1–A40, T1–T11
            ↑                        + amendments)
            │
            │ realized by
            ↓
Level 1 — Structural commitments  Smart constructors, closed DUs,
            ↑                        value objects, type-system contracts
            │
            │ enforced in
            ↓
Level 0 — Code                    F# files in sidecar/projection/src/
```

**Level 0 (code).** The substantive surface. Every type, every function, every test. The codebase has roughly 50,000 LOC of F# across `Projection.Core`, `Projection.Adapters.{Sql,Osm}`, `Projection.Targets.{SSDT,Json,Distributions,Data,OperationalDiagnostics}`, `Projection.Pipeline`, `Projection.Cli`, and the test project. Not directly the subject of this audit, but the substrate.

**Level 1 (structural commitments).** Type-system-level contracts. A smart constructor `Catalog.create : Module list -> Result<Catalog>` is a structural commitment: by the time you have a `Catalog` value, the smart constructor's invariants are guaranteed. A closed discriminated union `Origin = OsNative | ExternalViaIntegrationStudio | ExternalDirect` is a structural commitment: exhaustiveness is compiler-checked. A value object `SsKey = OssysOriginal of Guid | Synthesized of string * string list | ...` is a structural commitment: the compiler refuses to confuse identity with a raw string. Commitments at this level are *enforced by construction* — you cannot, syntactically, produce a value that violates them.

**Level 2 (formal axioms).** The contents of `AXIOMS.md`. A1–A40 are the foundational axioms; T1–T11 are theorems derived from them. Each axiom captures a load-bearing claim about the system's behavior: A1 says identity survives rename; T1 says projection is byte-deterministic; T11 says sibling-Π outputs agree on every catalog kind. Some axioms are operationalized at L1 by direct structural commitment (A4 → `SsKey` DU); some are operationalized at L2 only (A20 — pass-order matters — has no direct L1 enforcement; tests verify it instead).

**Level 3 (product axioms).** The end-to-end claims the operator implicitly trusts V2 to satisfy before they sign off on cutover. *"Re-running V2 with the same input produces byte-identical output."* *"V2 never silently drops a schema feature my V1 model uses."* *"A rename followed by re-emission produces SSDT that DacFx recognizes as a rename, not a drop+create."* These are operator-meaningful claims phrased in cutover vocabulary. Some are stated explicitly in `VISION.md` and `V2_PRODUCTION_CUTOVER.md`; many are *silent dependencies* — properties V2 must honor but hasn't named.

### 1.2 The two failure modes

The verifiability triangle exposes two distinct failure modes the codebase can suffer:

**Failure mode 1: An L3 axiom with no L2 axiom or L1 commitment underwriting it.** This is the dangerous case. The operator depends on the property (it's part of their mental model of V2); V2 happens to satisfy it by accident or by convention; nothing in the code structurally guarantees it. A future refactor breaks the property silently. The operator's CI passes; production diverges.

**Worked example.** Suppose the operator believes "if V2 emit fails partway through, the output directory is left untouched — no half-written files." This is L3-Boundary-AtomicEmission. The operator never asks; V2 happens to mostly behave this way because `Compose.write` writes files sequentially and a SqlException during the third file write leaves the first two on disk. There is no L2 axiom for atomic emission; there is no L1 commitment (`Compose.write` doesn't use temp-then-rename); there is no test. The operator hits a midnight oncall scenario where V2 fails partway, they delete the output dir to retry, and discover the SSDT project they thought was clean was actually a mix of v1.0 and v1.1 files. They lose trust in V2.

**Failure mode 2: An L2 axiom with no L1 commitment underwriting it.** This is the slower-burning case. The axiom is stated, the property is tested, but the type system doesn't structurally prevent violation. A new contributor reads the axiom, agrees with it intellectually, then writes code that violates it. Tests catch it (sometimes); reviewers catch it (sometimes); the discipline holds (usually). One day it doesn't. The axiom degrades into folklore.

**Worked example.** A25 says "every IR transformation runs inside the lineage monad." Tests verify it for every existing pass. A new contributor writes a "small utility pass" that returns `Catalog` directly, not `Lineage<Catalog>`. The CI doesn't catch it because no test exercises that path. Code review approves because the change is small. Three months later, downstream code that expects `Lineage<Catalog>` from every pass breaks when it encounters the bare-Catalog return.

The verifiability triangle's purpose is to systematically catch both failure modes. Coverage Bucket A (Part IV.1) is the safe zone: L3 ✓ L2 ✓ L1 ✓ test. Coverage Bucket B (Part IV.2) is failure-mode-2 territory: L3 ✓ L2 ✓ test ✓, L1 ✗. Coverage Bucket D (Part IV.4) is failure-mode-1 territory: L3 ✓, but no L2 axiom, no L1 commitment.

### 1.3 Methodology — top-down + bottom-up + adversarial

The audit was conducted across two rounds of parallel agents on 2026-05-11 and 2026-05-12.

**Round 1 (2026-05-11) — bottom-up scan.**
Four agents in parallel, each scoped to a different surface:
- Agent 1.1: Inventory of existing L1 commitments (smart constructors, closed DUs, VOs, `[<RequireQualifiedAccess>]` modules).
- Agent 1.2: Hunt illegal states in Catalog IR (`Catalog.fs`, `Coordinates.fs`, `Identity.fs`, `Types.fs`).
- Agent 1.3: Hunt illegal states in Profile + Lineage + Diagnostics + Passes (`Profile.fs`, `Lineage.fs`, `Diagnostics.fs`, `LineageBuffer.fs`, `Passes/*.fs`).
- Agent 1.4: Hunt illegal states at boundaries (adapters, emitters, CLI, Config, RenameBinding).

Each agent returned 15–30 numbered opportunities with file:line citations, current state, illegal state representable, proposed structural commitment, blast radius, and leverage. The synthesis (Part VI of this document) produced a 9-tier catalog covering ~40 distinct opportunities.

**Round 2 (2026-05-12) — top-down + adversarial.**
Three agents in parallel:
- Agent 2.1: Articulate L3 product axioms from the strategic docs (`VISION.md`, `V2_DRIVER.md`, `V2_PRODUCTION_CUTOVER.md`). Group by core concern (schema, data, identity, diagnostics, cutover safety). Produce ~50 axioms with tiers, currently-named status, and underwriting L2 axioms where identifiable.
- Agent 2.2: Bridge L2 → L3 for every existing axiom in `AXIOMS.md`. For each A1–A40 + T1–T11 + amendment: state the product behavior it underwrites, name the structural underwriting (L1), enumerate test coverage, classify coverage rating (Bucket A/B/C). End with a three-bucket summary.
- Agent 2.3: Adversarial gap-hunt. Pretend to be the operator pre-cutover. List the questions an operator would ask the V2 architect before saying yes. Each question is a candidate L3 axiom. Tag each: currently named? tier? verifiable?

Each agent returned ~30 entries. Agents 2.1 and 2.3 returned synchronously; Agent 2.2 ran longer because the L2 surface is larger.

**Triangulation.** The synthesizing voice (this document) maps every Agent 2.1 axiom against Agent 2.2's coverage map and Agent 2.3's gap-hunt candidates. Three failure-pattern signatures emerge:
- **Convergence**: L3 axiom appears in Agent 2.1, has an L2 axiom in Agent 2.2's Bucket A, no gap in Agent 2.3 → safe zone.
- **Single gap**: L3 axiom appears in Agent 2.1, has L2 axiom in Agent 2.2's Bucket B or C, may or may not appear in Agent 2.3 → structural-fortification candidate (Campaign B).
- **Double gap**: L3 property appears in Agent 2.3 only, no L2 axiom in Agent 2.2, no L1 commitment in Round 1's inventory → unnamed-axiom (Bucket D), Campaign A territory.

### 1.4 Why this matters now

V2's cutover sequence (per `V2_PRODUCTION_CUTOVER.md`) involves four environments × four artifact types × N=10 consecutive green canary runs before V2-driver mode flips on. Every divergence between V1 and V2 either matches a named tolerance or fails the canary. The structural-commitment posture of V2 is what determines whether the canary catches every meaningful divergence or whether divergences leak past it.

A structurally-weak axis fails *silently*: V2's output looks right, the canary passes (because the canary doesn't check the thing that's wrong), and the divergence shows up in production three weeks later. A structurally-strong axis fails *loudly*: the type system rejects the bad value at construction; the canary doesn't even get to run.

The campaigns proposed in Part IX are the work to lift V2 from "structurally strong on the IR core; conventionally strong on the boundaries" to "structurally strong throughout." After those campaigns ship, the surface area of "things that could go wrong in cutover that the canary wouldn't catch" shrinks to a small, named, monitored set.

---

## Part II — The Five Core Concerns

V2's responsibility surface decomposes into five core concerns plus one cross-cutting band. Each concern has its own L3 product axioms, its own L2 underwriting (or gaps), and its own L1 structural commitments (or gaps).

### 2.1 Schema concern

V2 produces SSDT artifacts (per-table `.sql` files + `manifest.json`), DACPAC binary, and JSON IR snapshots. The operator deploys these to SQL Server via DacFx or `sqlcmd`. The product question: *is the schema V2 emitted the same schema V1 would have emitted?*

Surfaces touched: `Projection.Targets.SSDT`, `Projection.Targets.Json`, `Projection.Pipeline.Compose`, `SsdtBundle`, `ManifestEmitter`, `DacpacEmitter`, `RefactorLogEmitter`.

Operator-meaningful questions: Can I diff V2's SSDT against V1's SSDT and trust the diff? When I rename a table, does V2 produce DDL that DacFx recognizes as a rename (not drop+create)? Does V2 emit every schema feature my V1 model uses, or does it silently drop some? Are the per-table `.sql` files byte-identical across re-runs? Does V2's DACPAC round-trip through DacFx without losing structure?

### 2.2 Data concern

V2 produces static-seed MERGE statements, migration-dependency INSERT/MERGE statements, and bootstrap data. The product question: *is the data V2 emitted the data the operator's V2 deployment will actually have?*

Surfaces touched: `Projection.Targets.Data` (StaticSeedsEmitter, MigrationDependenciesEmitter, BootstrapEmitter), `DataEmissionComposer`, the chapter-4.1.B canary on CDC silence.

Operator-meaningful questions: Will V2's MERGE statements be idempotent — can I re-run them safely after a failed deploy? Are PKs assigned deterministically for migration-dependency rows? Will the order of rows respect FK constraints (parent tables before child tables)? When I redeploy the same artifacts, does CDC fire (it shouldn't) or stay silent (it should)?

### 2.3 Identity concern

OutSystems entities have stable identity (`Is_External` flags and `SS_KEY` GUIDs in the V1 metadata) that must survive every transformation V2 applies: rename, schema-version evolution, refactor, redeploy. The product question: *can downstream consumers (FK joins, refactor logs, audit trails) follow the identity of a kind across every transformation V2 might apply?*

Surfaces touched: `SsKey` (four-variant DU), `Name`, `Origin`, `Catalog.tryFindKind`, `TableRenamePass`, `RefactorLogEmitter`, `CatalogDiff.between`.

Operator-meaningful questions: If I rename a table in my V2 config, do FK references continue to resolve? Does V2's V1Mapped SsKey correctly correspond to V1's SS_KEY GUID? When V2 emits SSDT, will SSDT's refactor-log mechanism preserve the rename as a rename (so data isn't dropped)? Across two V2 runs of the same input, do SsKeys remain stable?

### 2.4 Diagnostics concern

V2 emits DecisionLog (audit trail of every pass decision), Opportunities (operator-actionable findings — orphaned FKs, nulls-in-mandatory, unique-index duplicates), Validations (invariant confirmations and structured failures). The product question: *can the operator trust the diagnostic logs as the audit trail of V2's decisions, and act on them?*

Surfaces touched: `Lineage`, `LineageEvent`, `Diagnostics`, `DiagnosticEntry`, `DecisionLogEmitter`, `OpportunitiesEmitter`, `ValidationsEmitter`, `Routing.route` (code-prefix dispatch).

Operator-meaningful questions: When V2 reports "orphaned FK," does the report name the specific FK so I can find it in a 300-table schema? Are decision-log entries sorted in a way I can reason about? If I grep diagnostic codes today, will the same codes still exist when V2 v2.1 ships next month? Can I replay V2's reasoning from the log alone, or do I need to read source?

### 2.5 Cutover safety concern

V2 and V1 run in parallel during the cutover window. The canary asserts V1 ≈ V2 modulo named tolerances. Disagreement blocks the PR. R6 ("V2 emits but doesn't ship; V1 owns the write path during dual-track") eliminates split-brain by construction. T-30 and T-15 gates control the fallback ladder. V1 stays warm through cutover+30. The product question: *what verifiable claims about V2 are necessary preconditions for the cutover ladder to step?*

Surfaces touched: the canary (`fixtures/canary-gate.sql`, `GeneratorScaleTests`, `RichProfilingEndToEndTests`, `OperatorRealityCanary`), the Tolerance taxonomy, the V2_DRIVER.md phase ladder, R6 governance.

Operator-meaningful questions: Is V2 emitting the same artifacts V1 would? Are every divergence either a named tolerance or a canary failure? Does V2 own zero production write paths during dual-track? Has every environment-artifact pair accumulated N=10 green canary runs before V2-driver flips on?

### 2.6 Cross-cutting

Some axioms cross all five concerns: byte-determinism (T1), sibling-Π commutativity (T11), aggregate-root smart-constructor invariants (A39), lineage compositionality (A24). These appear in Part III's cross-cutting subsection rather than being assigned to a single concern.

---

## Part III — L3 Product Axiom Catalog

**Canonical location for L3 axiom statements:** `PRODUCT_AXIOMS.md`. This audit doc retains the per-axiom analytical context (tier, currently-named, verifiability, failure mode if violated) because that's the audit metadata; the *statements themselves* moved into the constitutional sibling alongside `AXIOMS.md`. When refining an L3 axiom's wording, edit `PRODUCT_AXIOMS.md`; when refining its bucket assignment or coverage status, edit this audit doc.

Each axiom carries a stable identifier (`L3-S#` for schema, `L3-D#` for data, `L3-I#` for identity, `L3-X#` for diagnostics, `L3-C#` for cutover safety, `L3-CC#` for cross-cutting). Tier is 1 (cutover blocker), 2 (strongly desired), 3 (nice-to-have). "Currently named" indicates whether the property is stated as a formal L2 axiom; "verifiability" indicates whether a property test or canary exists today.

### 3.1 Schema (L3-S)

**L3-S1: SSDT emit is byte-deterministic.**
Given identical `osm_model.json` input + `Catalog` + `Policy.empty` + `Profile.empty`, `SsdtDdlEmitter.emitSlices` produces identical per-table `.sql` files byte-for-byte across reruns, processes, machines, OSes, and time.
- Tier 1.
- Currently named: partial. T1 covers byte-determinism for the projection function; doesn't explicitly cover the cross-platform / cross-time extensions.
- Verifiability: tested (`EndToEndPipelineTests`, `SsdtDdlEmitterTests`); cross-platform extension untested.
- Failure mode if violated: operator runs V2 on Windows for the dev environment; CI on Linux fails on whitespace diff because CRLF leaked. Or: operator re-runs V2 against the same input on Tuesday and gets a different hash than Monday's run because a `DateTime.Now` slipped into a generated comment.

**L3-S2: DACPAC round-trip equality.**
Given a `Catalog` C, `DacpacEmitter.emit` produces a `.dacpac`; DacFx deserializes it to a `TSqlModel`; `ReadSide.toCatalog` reconstructs a `Catalog` C'. Then `Catalog.equivalent C C' = true` modulo the DacFx-erasure predicate (A37 candidate; named at chapter-3.4 close).
- Tier 1.
- Currently named: partial. T1 (binary-normal-form variant) and T11 underwrite the structural commitment; A37 names the erasure set but is pending promotion.
- Verifiability: `DacpacRoundTripTests` cover the round trip; the erasure predicate `equalModuloDacpacErasure` is in flight.
- Failure mode if violated: V2 emits a DACPAC, the operator imports it into Visual Studio SSDT, certain index names disappear, the operator's reproducible-build assumption breaks.

**L3-S3: SSDT and DACPAC sibling-Π agreement.**
Every Kind by SsKey in the Catalog appears in both `SsdtDdlEmitter.emitSlices` output and `DacpacEmitter.emit` output. The keysets agree exactly (T11 structural). Per-kind contents agree on identity-keyed structure (every column, index, FK present in SSDT is present in DACPAC, possibly with DacFx normalization).
- Tier 1.
- Currently named: yes (T11 + A18-amended).
- Verifiability: `SiblingEmitterContractTests` enforce keyset equality structurally via `ArtifactByKind<'element>` smart constructor.
- Failure mode if violated: SSDT lists a table; DACPAC omits it. Operator's deploy via DacFx leaves a phantom table. (Cannot happen with the current `ArtifactByKind` structural enforcement.)

**L3-S4: Trigger definitions are complete.**
Every Trigger present in `Catalog.Triggers` (or `Kind.Modality` carrying trigger marks per the A.0' IR lift) with `IsDisabled = false` appears in emitted DDL as a `CREATE TRIGGER` statement with identical `Definition` text. Disabled triggers emit with their disabled state preserved.
- Tier 1.
- Currently named: no. The Trigger lift is part of Phase A.0' per `V2_PRODUCTION_CUTOVER.md` §5.2; the round-trip axiom that follows is not yet formalized.
- Verifiability: no test today; gated on the IR lift.
- Failure mode if violated: V2 emits DDL that omits triggers. Operator's data-validation triggers silently disappear at cutover; constraint checks the application relied on are gone.

**L3-S5: Sequence definitions are complete.**
Every Sequence in `Catalog.Sequences` appears in emitted DDL as a `CREATE SEQUENCE` statement with StartValue, Increment, Min, Max, IsCycleEnabled, CacheMode preserved.
- Tier 1.
- Currently named: no. Gated on Phase A.0' Sequence lift.
- Verifiability: no test today.
- Failure mode if violated: applications depending on sequence-generated IDs fail at cutover because the sequences are missing from the target schema.

**L3-S6: DEFAULT values round-trip.**
Every Attribute with `DefaultValue` present produces a column definition with `DEFAULT` clause in DDL. Round-tripped Catalog restores the `DefaultValue`.
- Tier 1.
- Currently named: no. Gated on Phase A.0' DEFAULT lift.
- Verifiability: no test today.
- Failure mode if violated: column defaults silently disappear at cutover; subsequent INSERT statements that relied on defaults produce NULLs or fail.

**L3-S7: Computed columns render correctly.**
Every Attribute with `IsComputed = true` and `ComputedDefinition` renders as a computed-column DDL; round-trip restores the computed state and definition.
- Tier 1.
- Currently named: no. Gated on Phase A.0' Computed-column lift.
- Verifiability: no test today.
- Failure mode if violated: computed columns become regular nullable columns at cutover.

**L3-S8: CHECK constraints are emitted.**
Every ColumnCheck in `Kind.ColumnChecks` appears as a CHECK constraint in the emitted DDL. Orphaned checks (naming nonexistent columns) fail at validation rather than emit-time.
- Tier 1.
- Currently named: no. Gated on Phase A.0' CHECK lift.
- Verifiability: no test today.
- Failure mode if violated: business-logic constraints disappear; applications that relied on database-side validation start accepting invalid data.

**L3-S9: ExtendedProperties round-trip.**
Every ExtendedProperty in Module / Kind / Attribute / Index appears in SSDT extended-property comments and round-trips via DacFx.
- Tier 2.
- Currently named: no. Gated on Phase A.0' ExtendedProperties lift.
- Verifiability: no test today.
- Failure mode if violated: documentation embedded in extended properties (MS_Description and custom keys) disappears; institutional knowledge lost.

**L3-S10: Catalog-coordinate identity (cross-database FKs).**
Every cross-database FK (TableId.Catalog differs between source and target Kind) is either emitted unchanged or rejected with a structured error. No silent downgrade to same-database.
- Tier 2.
- Currently named: no. Gated on Phase A.0' Catalog-coordinate lift.
- Verifiability: no test today.
- Failure mode if violated: multi-database architectures silently lose cross-DB constraints.

### 3.2 Data (L3-D)

**L3-D1: CDC silence on idempotent redeploy.**
Deploy `StaticSeedsEmitter` + `MigrationDependenciesEmitter` + `BootstrapEmitter` output (MERGE statements) to a production-shaped SQL Server with CDC enabled on tracked tables. Redeploy the same MERGE statements without modifying source data. Assert zero records inserted into `cdc.change_tables` for every CDC-tracked table on redeploy.
- Tier 1. **Highest-leverage single deliverable in the entire chapter sequence (per V2_DRIVER.md).**
- Currently named: partial. T1 (byte-determinism) and topological-sort idempotence underwrite. Not formally stated as an L2 axiom; the chapter-4.1.B canary is the realization.
- Verifiability: `CdcSilenceTests` are in flight (chapter 4.1.B).
- Failure mode if violated: every redeploy of V2 artifacts triggers spurious CDC change events. Downstream ETL pipelines that consume CDC interpret these as real data changes and corrupt their replicas.

**L3-D2: Static-seed rows are deterministically ordered.**
Given a static-entity definition with N rows, `StaticSeedsEmitter` produces a MERGE statement that applies rows in a canonical order (by PK if available; by content hash if not). Re-running with the same Catalog produces identical MERGE statement.
- Tier 1.
- Currently named: yes (T1 + D12 canonical-sort discipline).
- Verifiability: `StaticSeedsEmitterTests.\`\`deterministic-merge-order\`\``; property tests on row permutations.
- Failure mode if violated: two V2 runs produce different MERGE statements for the same data, diffs are noisy, operator can't tell signal from noise.

**L3-D3: MigrationDependencies PK assignment is deterministic.**
Given a `MigrationDependencyContext` with auto-PK rows, the emitter produces MERGE statements where every PK value is a literal constant. For the same input set, re-emission produces identical PK literals.
- Tier 1.
- Currently named: yes (T1 + D5 — PK pre-computed at emit time).
- Verifiability: `MigrationDependencyPkTests`; identity-overflow edge cases pending.
- Failure mode if violated: operator runs the migration twice (e.g., dev then UAT) and gets different IDs, breaking referential integrity across environments.

**L3-D4: Topological order preserves FK validity.**
`StaticSeedsEmitter` and `BootstrapEmitter` emit rows in an order such that every FK reference appears after the referenced table's rows are inserted. No FK violation during incremental insert.
- Tier 1.
- Currently named: yes (A25 + A33 + `TopologicalOrderPass`).
- Verifiability: tested in `TopologicalOrderTests`; FK-validity property test in flight.
- Failure mode if violated: deploy fails midway with FK constraint violations; operator can't tell which insert order was wrong.

**L3-D5: Cycles are broken via user-supplied allowlist.**
When a cycle exists in the FK graph, `TopologicalOrderPass` either rejects (with structured error) or breaks the cycle via the `allowedCycles` config entry. Cycles not in the allowlist produce a structured validation error rather than nondeterministic ordering.
- Tier 2.
- Currently named: partial (A33 + `SelfLoopPolicy` per A40).
- Verifiability: `CycleResolutionTests`, `CycleDetectionTests`.
- Failure mode if violated: V2 silently picks an order for a cycle that fails at FK-enforcement time; operator can't reproduce because the order depends on hash iteration.

**L3-D6: User FK reflow maps every CreatedBy/UpdatedBy FK.**
`UserFkReflowPass` discovers all Attributes marked as `IsUserFk = true` (CreatedBy / UpdatedBy / similar), maps each source User ID to target User ID via `UserMatchingStrategy`, and produces a `UserRemapContext` covering every FK in the target environment. Missing mappings fail at validation.
- Tier 1.
- Currently named: partial. Chapter 4.2 establishes the pass; the totality axiom (every UserFk mapped) is not formally stated.
- Verifiability: `UserFkReflowPassTests`, `UserFkReflowIntegrationTests`.
- Failure mode if violated: rows reference orphan user IDs in the target environment; FK constraints fail or (worse) data resolves to the wrong user.

**L3-D7: StaticData fixture JSON is schema-validated.**
`StaticDataLoader` parses operator-supplied static-data JSON, validates column existence + type compatibility, and rejects unknown tables/columns with structured errors. Rows are emitted in declaration order within each table.
- Tier 2.
- Currently named: no. Gated on Phase A.3 implementation.
- Verifiability: no test today.
- Failure mode if violated: operator writes a static-data JSON referencing a renamed column; V2 silently drops it; production deploys with incomplete seed data.

**L3-D8: MigrationDependencies JSON is schema-validated.**
`MigrationDependencyLoader` parses JSON, resolves kind keys to Catalog kinds, validates column existence, rejects unknown columns/types. Rows emitted with auto-assigned PKs in canonical order.
- Tier 2.
- Currently named: partial. Phase A.3 + D5.
- Verifiability: no test today.
- Failure mode if violated: same as L3-D7 plus PK collisions if the auto-PK pass observes a stale MAX(Id).

**L3-D9: Bootstrap emitter covers all remaining rows.**
`BootstrapEmitter` (when `EmissionPolicy = AllRemaining`) discovers every row in the profile-scanned database that is not covered by `StaticSeedsEmitter` or `MigrationDependenciesEmitter`. Set-union property: static + migration + bootstrap covers the full profile population. No row is emitted twice; no row is omitted.
- Tier 2.
- Currently named: no. Chapter 4.1.B partition-assertion is the closest.
- Verifiability: `DataEmissionComposerTests` enforce partition disjointness.
- Failure mode if violated: rows are emitted twice (deploy fails on PK violation) or rows are omitted (production database is missing data).

**L3-D10: Emission policy gates work correctly.**
Config-driven `emission.staticSeeds`, `emission.migrationDependencies`, `emission.bootstrap` toggles produce the expected MERGE statements. Toggling any subset off produces the output minus that emitter's rows; toggling all off produces zero data emission.
- Tier 2.
- Currently named: no. Gated on Phase A.2 emitter wiring.
- Verifiability: no test today.
- Failure mode if violated: operator's intent (e.g., "schema-only emit for this environment") is silently ignored; data ends up in the wrong environment.

### 3.3 Identity (L3-I)

**L3-I1: SsKey is stable under physical rename.**
For any Catalog C, rename spec R (per D12 canonical-sort order), `TableRename.run R C = C'`, every Kind's SsKey is identical in C and C' (only the Physical TableId differs).
- Tier 1.
- Currently named: yes (A1).
- Verifiability: `TableRenameTests.\`\`A1: rename preserves Kind.SsKey while rewriting Kind.Physical\`\``. **Shipped 2026-05-12 in commit 502592f.**
- Failure mode if violated: rename breaks FK joins, refactor logs, audit trails — every downstream consumer that resolves by identity gets a different identity for the same row.

**L3-I2: OssysOriginal SsKeys survive extraction.**
Every Kind with `SsKey = OssysOriginal(guid)` in the input osm_model.json round-trips through V2 with identical guid. (Bound: JSON projection lossiness is documented in A1's amendment; when SnapshotRowsets lands in chapter 3.2, OssysOriginal becomes unconditional.)
- Tier 1.
- Currently named: yes (A1 + bound).
- Verifiability: `SsKeyRoundTripTests`; bound documented in `AXIOMS.md`.
- Failure mode if violated: V2 invents new identity for a kind that V1 already had identity for; cross-version diffs break.

**L3-I3: SsKey synthesis is deterministic.**
For Kinds with `SsKey = Synthesized(source, basis)`, the synthesis function is pure. Same Catalog + module name + entity name + attributes produce identical Synthesized SsKey across reruns. No randomness, no `Guid.NewGuid`.
- Tier 1.
- Currently named: yes (T1).
- Verifiability: `SsKeySynthesisTests`; property: `synthesis(x) = synthesis(x)`.
- Failure mode if violated: cross-run identity instability; every re-extraction looks like a different schema.

**L3-I4: RefactorLog records rename history.**
Every rename (via `TableRenamePass` or via cross-version diff in `CatalogDiff.between`) produces a `RefactorLog` entry with from/to identity, reason tag, and (optional) timestamp. Composing `diff(V0→V1) + diff(V1→V2)` produces a transitive `diff(V0→V2)` modulo loss-free paths.
- Tier 2.
- Currently named: partial (T9 — refactor freedom under rename — close but not specific).
- Verifiability: `RefactorLogEmitterTests`, `RefactorLogRenderTests`.
- Failure mode if violated: SSDT's refactor-log mechanism doesn't recognize the rename; DacFx treats it as drop+create; data lost.

**L3-I5: V1Mapped SsKeys enable legacy identity tracking.**
For any V1 SSKey GUID in an extracted model, V2 can construct `SsKey = V1Mapped(v1Guid, namespace)` and track it through the pipeline. `DerivedFrom` chains connect splits/merges back to originals.
- Tier 2.
- Currently named: yes (A1 four-variant DU).
- Verifiability: `IdentityTests.\`\`V1Mapped construction\`\``; chapter 3.5 θ/ι.
- Failure mode if violated: legacy identity tracking breaks; V1 ↔ V2 audit trails diverge.

**L3-I6: Identity is stable across schema evolution.**
Given Catalog C_v0 and post-evolution Catalog C_v1 (after rename, attribute add, delete), every Kind that persists across versions has an SsKey such that `Kind_v0.SsKey` relates to `Kind_v1.SsKey` via `RefactorLog`. Persistent kinds are not re-keyed.
- Tier 2.
- Currently named: yes (A1 + A4).
- Verifiability: `SchemaEvolutionTests` in flight.
- Failure mode if violated: schema-evolution dashboard treats every kind as new; rename history disappears.

**L3-I7: Name changes don't affect SsKey.**
Renaming a Kind's `Name` from "Order" to "SalesOrder" updates `Kind.Name` (presentation) but not `Kind.SsKey` (identity) or `Kind.Physical.Table` (physical name).
- Tier 1.
- Currently named: yes (A2 + A3 + A15).
- Verifiability: `NameStabilityTests`, `CatalogTests.\`\`A2: SsKey and Name are independently constructed and validated\`\``.
- Failure mode if violated: rename of presentation Name accidentally re-keys the Kind; downstream identity lookups break.

**L3-I8: SsKey uniqueness within Catalog.**
No two Kinds in a Catalog have the same SsKey. Enforced by `Catalog.create` smart constructor.
- Tier 1.
- Currently named: yes (A39).
- Verifiability: `CatalogCreationTests`; property test on SsKey-set cardinality = Kind count.
- Failure mode if violated: identity collisions; `Catalog.tryFindKind` returns the wrong Kind.

**L3-I9: Attribute identity persists under column rename.**
Each Attribute within a Kind carries its own SsKey. When a column is renamed, its Attribute.SsKey is stable.
- Tier 2.
- Currently named: partial (A1 generalization).
- Verifiability: gated on chapter 3.2 (SnapshotRowsets).
- Failure mode if violated: column rename re-keys the Attribute; per-column audit trails break.

**L3-I10: Cross-catalog reference identity is consistent.**
When Catalog coordinates include per-database scope (`Kind.TableId.Catalog`), every Reference to a Kind in a different catalog preserves its Catalog identity. Cross-catalog FKs don't collapse to same-database.
- Tier 2.
- Currently named: no. Gated on Phase A.0' Catalog-coordinate lift.
- Verifiability: no test today.
- Failure mode if violated: multi-database FKs silently degrade; cross-DB constraints lost.

### 3.4 Diagnostics (L3-X)

**L3-X1: Every pass decision is emitted as a LineageEvent.**
Every decision made by any pass produces exactly one LineageEvent in the output Lineage. Silence = no decision made (the pass observed but didn't change; a named `KeepReason` variant says so).
- Tier 1.
- Currently named: yes (A25).
- Verifiability: implicit in every pass test; property tests on writer-monad laws.
- Failure mode if violated: V2 makes a decision that doesn't appear in the audit trail; operator can't reconstruct V2's reasoning.

**L3-X2: Opportunity routing is complete and correct.**
Every opportunity-producing strategy (orphaned FK, mandatory-column-with-nulls, duplicate-in-unique-index) routes its finding to `DecisionLogEmitter` (Tier 1: always), `OpportunitiesEmitter` (Tier 2: operator-focused), and `ValidationsEmitter` (Tier 3: auditor-focused). Each channel receives correctly-formatted entries.
- Tier 1.
- Currently named: partial (Routing.fs implements the code-prefix routing; the totality axiom is convention-enforced).
- Verifiability: `OperationalDiagnosticsRoutingTests`; chapter 4.3.
- Failure mode if violated: operator sees a finding in DecisionLog but not in Opportunities; misses the cue to act.

**L3-X3: Opportunity severity matches consequence.**
Orphaned FKs are `Severity = High` (data loss risk); nulls-in-mandatory are `Medium`; unique-index duplicates are `High`. Re-sorting by severity produces a stable, operator-meaningful ordering.
- Tier 2.
- Currently named: partial (discrete-rationale DUs absorb continuous evidence per DECISIONS 2026-05-13).
- Verifiability: no dedicated test; gated on chapter 4.3.
- Failure mode if violated: operator triages findings by severity and acts on low-severity items while critical findings sit in the backlog.

**L3-X4: Lineage trail is earliest-first.**
When passes compose (A → B → C), LineageEvents from A appear before B and before C in the final trail (A24: trail is `f ++ g`, earliest-first, under bind).
- Tier 1.
- Currently named: yes (A24).
- Verifiability: `LineageTests`; property test on bind-composition order.
- Failure mode if violated: audit trail reads in reverse chronology; debugging is harder than it should be.

**L3-X5: Validation failures include remediation guidance.**
Every validation failure entry includes a `RemediationSuggestion` field naming the operator action (add PK, resolve orphan, remove duplicate, etc.). Suggestions are actionable without requiring V2 source reading.
- Tier 2.
- Currently named: no.
- Verifiability: no test today; gated on chapter 4.3.
- Failure mode if violated: operator reads "validation failed" but has no idea what to do.

**L3-X6: Detection pass triggers are traceable.**
If a detection pass produces an opportunity, the Lineage includes the source evidence (which table, column, FK target) such that an operator can navigate from the opportunity back to the schema element.
- Tier 2.
- Currently named: partial (A23 — events carry SsKey).
- Verifiability: `NullabilityPassTests`, `ForeignKeyPassTests` partial coverage.
- Failure mode if violated: opportunity says "orphan FK" but doesn't name which one in a 300-table schema.

**L3-X7: Silent V1 drops are documented.**
When V1 carried a capability that V2 does not (e.g., a metadata field V2's adapter doesn't carry), the absence is logged as `Severity = Info` in Validations — not omitted from the trail.
- Tier 2.
- Currently named: no.
- Verifiability: no test today; gated on chapter 3.2 + 4.3.
- Failure mode if violated: V2 silently drops fields V1 had; operator never knows.

**L3-X8: Tolerance taxonomy decisions are recorded.**
Every divergence between V1 and V2 output that matches a named tolerance entry (per R6 split-brain governance) is logged with the tolerance tag in DecisionLog. Non-tolerances block the PR.
- Tier 2.
- Currently named: yes (R6 + Tolerance taxonomy).
- Verifiability: tolerance-logging tests in flight (chapter 4.1.A M4).
- Failure mode if violated: operator can't audit which V1↔V2 differences were accepted; can't tell silent drift from explicit allowlist.

**L3-X9: Config validation errors are structured.**
When unified config fails to parse or validate, the error includes (path, reason, suggestion). Operator can fix the config without reading JSON schema.
- Tier 2.
- Currently named: yes (D9 — secret-free by construction; structured errors mandated).
- Verifiability: `ConfigTests`; structured-error tests for every error code in `Config.fs`.
- Failure mode if violated: operator gets `JsonException: unexpected token` and has no idea where in their 200-line config the problem is.

**L3-X10: Multi-environment policy diffs are signaled.**
When policy diverges between environments (different `UserMatchingStrategy` per env, different `EmissionPolicy`), the Lineage includes a policy-delta entry naming what diverged.
- Tier 2.
- Currently named: no.
- Verifiability: gated on chapter 4.1.A M4.
- Failure mode if violated: operator deploys with different policy in UAT than PROD without noticing.

### 3.5 Cutover safety (L3-C)

**L3-C1: Canary asserts V1 ≈ V2 modulo named tolerances.**
When both V1 and V2 emit artifacts from the same source, the canary compares outputs byte-for-byte on SSDT and set-for-set on data. Every divergence either matches a Tolerance taxonomy entry or fails the canary.
- Tier 1.
- Currently named: yes (R6).
- Verifiability: `CanaryDeployTests`, `CanaryRoundTripTests`; chapter 3.4.
- Failure mode if violated: silent divergence passes the canary; V2 ships with bugs that don't surface until production.

**L3-C2: Fallback ladder is enforced at T-30 and T-15.**
At T-30 pre-cutover, if any precondition (chapter 3 closed, chapter 4.1 shipping, chapter 4.2 shipping, ≥1 UAT dry-run) is unmet, drop to V2-augmented or V1-only per the gate. At T-15, if canary flake >10% or tolerance churn is uncontrolled, drop to V1-only. V1 stays warm through cutover+30 regardless.
- Tier 1.
- Currently named: yes (T-30/T-15 gates in DECISIONS 2026-05-22).
- Verifiability: governance, not code.
- Failure mode if violated: operator ships V2 despite a yellow canary; production breaks.

**L3-C3: V2 owns no production write path during dual-track.**
During the V2-augmented window, V2 emits but doesn't deploy. V1 owns the deploy. The canary reads V1-deployed schema via `ReadSide`, compares to V2's expected Catalog, asserts agreement modulo tolerances.
- Tier 1.
- Currently named: yes (R6).
- Verifiability: governance + `ReadSide` tests.
- Failure mode if violated: split-brain — both V1 and V2 writing to production; reconciliation impossible.

**L3-C4: Per-environment-per-artifact-type V2-driver transition.**
V2-driver transition is gated per (environment, artifact type) pair, not global. Dev/SSDT may transition while UAT/DACPAC stays on V2-augmented. Each pair has its own N=10 green-run counter + operator sign-off.
- Tier 1.
- Currently named: yes (R6).
- Verifiability: governance.
- Failure mode if violated: operator over-commits to V2-driver before evidence is in.

**L3-C5: Canary green for ≥30 generated cases and ≥4 production cases.**
Before declaring canary green on an axis, run the property test on ≥30 generated synthetic catalogs (FsCheck) and on ≥4 real production catalogs.
- Tier 1.
- Currently named: yes (chapter 3.4).
- Verifiability: `GeneratorScaleTests`, `CanaryRoundTripTests`, the operator-reality canary.
- Failure mode if violated: synthetic-only canary misses production-specific edge cases.

**L3-C6: Idempotent-redeploy holds across all CDC-tracked tables.**
(See L3-D1 — co-located with this concern as a cutover-safety axiom because it gates the cutover ladder.)
- Tier 1.
- Currently named: partial.
- Verifiability: `CdcSilenceTests` in flight.
- Failure mode if violated: redeploy triggers spurious CDC events; downstream ETL corruption.

**L3-C7: Canonical ordering prevents determinism regression.**
Config validation applies canonical sort to user-supplied collections at parse time. Two configs with the same entries in different orders produce identical output.
- Tier 1.
- Currently named: yes (D12).
- Verifiability: property tests with FsCheck on rename-order invariance (`TableRenameTests`).
- Failure mode if violated: operator reorders config; output diff is non-empty; operator can't tell signal from noise.

**L3-C8: No credentials in unified config.**
Static analyzer rule forbids `connectionString` / `password` / `accessToken` properties anywhere in the Config type tree. Connection strings sourced from env vars or separate non-checked-in files.
- Tier 1.
- Currently named: yes (D9).
- Verifiability: `ConfigTests.\`\`D9: credential property anywhere is rejected\`\``; parser-level enforcement.
- Failure mode if violated: operator checks credentials into git; secret-rotation cost; possible breach.

**L3-C9: Dry-run completeness before cutover.**
≥1 full end-to-end dry-run on a production-shaped environment completes successfully and produces artifacts that pass the canary before cutover begins.
- Tier 1.
- Currently named: yes (V2_DRIVER.md phase ladder).
- Verifiability: governance.
- Failure mode if violated: cutover hits novel scenarios in production that no dry-run exercised.

**L3-C10: V1 sunset begins only after cutover+30 + one schema-evolution cycle.**
V1 can be archived only after cutover+30 elapsed AND all four environments have run V2 emissions for ≥1 full schema-evolution cycle AND canary green throughout AND operator confirmation.
- Tier 1.
- Currently named: yes (R6 + V2_DRIVER.md).
- Verifiability: governance.
- Failure mode if violated: V1 archived too early; rollback impossible.

### 3.6 Cross-cutting (L3-CC)

**L3-CC1: Π contract is universal.**
Every emitter has the signature `Catalog -> Result<ArtifactByKind<'element>, EmitError>` where `ArtifactByKind` enforces "every Catalog kind appears in the output keyset" at compile time.
- Tier 1.
- Currently named: yes (T11 + Stage 0 S0.B).
- Verifiability: type system enforces; `SiblingEmitterContractTests`.

**L3-CC2: Pass contract is universal.**
Every pass has the signature `Catalog -> Policy -> Profile -> Lineage<'output>` or `Catalog -> Policy -> Profile -> Lineage<Diagnostics<'output>>`. A18-amended: no pass consumes Policy from inside its implementation.
- Tier 1.
- Currently named: yes (pass-return-type codification per DECISIONS 2026-05-13).
- Verifiability: signature-level enforcement.

**L3-CC3: Lineage is monotonic under composition.**
When passes compose (A then B then C), the Lineage from the composition is `A.lineage ++ B.lineage ++ C.lineage` — earliest-first concatenation. No reordering, no loss, no duplication.
- Tier 1.
- Currently named: yes (A24).
- Verifiability: writer-monad-laws property tests; `LineageTests`.

**L3-CC4: IR fidelity is complete for production workloads.**
Every schema concept in V1's `OsmModel` that appears in the operator's four production environments is representable in V2's `Catalog` and survives the emit→read→reconstruct round-trip. Concepts deliberately excluded (e.g., per A13) are documented with rationale.
- Tier 1.
- Currently named: partial (Phase A.0' workstream).
- Verifiability: `IrFidelityTests` in flight.
- Failure mode if violated: silent feature loss at cutover.

**L3-CC5: Performance regression is caught before merge.**
Every hot-path function is gated by the statistical perf baseline (`bench/baseline-canary.json`). Perf regression beyond μ + 5σ blocks CI. Baseline updates explicit (`PERF_GATE_RECORD=1`) and paired with a DECISIONS amendment.
- Tier 1.
- Currently named: yes (bench-driven-optimization protocol).
- Verifiability: `scripts/perf-gate.sh`.

**L3-CC6: Domain-first naming is enforced at review time.**
Every new type/function/file/module name passes the four-question naming analysis. Performance-of-compliance and domain-blind-naming failure modes caught at review.
- Tier 1 (operationally); lint partial.
- Currently named: yes (DECISIONS 2026-05-10 pillar 8).
- Verifiability: code review + chapter-close ritual.

---

## Part IV — L2 Existing Axiom Coverage Map

The audit triangulates the 40 axioms and 11 theorems in `AXIOMS.md` plus the amendments. Each is rated as one of four coverage buckets:

- **Bucket A**: Full L3 + L2 + L1 + test coverage. Type system structurally prevents violation.
- **Bucket B**: L3 + L2 + test coverage. Convention enforces L1 (signature or pattern); type system doesn't structurally forbid.
- **Bucket C**: Weakness. Untested, hidden assumption, aspirational, deferred, subsumed without dedicated test, or scope-boundary.
- **Bucket D**: Unnamed axioms (Bucket D doesn't apply to existing L2 axioms; it's the parallel surface of L3 properties without L2 backing — Part VIII).

### 4.1 Bucket A — Full coverage (the model: L3 ✓ L2 ✓ L1 ✓ test)

**18 axioms in this bucket.** These are the structural commitments the codebase has earned. Every axiom here passes all four checks: there's an operator-meaningful L3 product behavior, a stated L2 axiom, an L1 structural commitment, and a test (or test family) that verifies the property end-to-end.

| Axiom | L3 product behavior | L1 structural underwriting | Test coverage |
|---|---|---|---|
| **A1** (identity survives rename) | Operator renames a table; FK joins, refactor logs, audit trails continue to work | `SsKey` four-variant closed DU (`Identity.fs`); smart constructors per variant; `Kind.SsKey` required field; lookups by SsKey | `CatalogTests`, `TableRenameTests`, `IdentityTests`; rename-order invariance property test |
| **A2** (identity is not name) | Operator changes a name without breaking referential relationships | `SsKey` and `Name` are distinct types; `Catalog.tryFindKind` takes `SsKey` | `CatalogTests.\`\`A2: SsKey and Name are independently constructed and validated\`\`` |
| **A4** (identity is type-distinguished from string) | System treats Kinds as equal by SsKey regardless of names or attribute reordering | `SsKey` closed DU; structural equality on `Kind` compares SsKey only | 20 test instances in `CatalogTests.\`\`A4: kinds with same SsKey are identity-equal regardless of names\`\`` |
| **A5** (derived identities are deterministic and traceable) | A pass synthesizes a new Kind; the new identity traces back to the parent | `SsKey.derivedFrom` smart constructor; `SsKey.rootOriginal` walks the chain | `CatalogTests.\`\`A5: derived(parent, reason) is deterministic\`\``; property test on re-running |
| **A18 amended** (Π consumes Catalog × Profile, never Policy) | Operator configures a pass, not an emitter; emitter signatures forbid Policy | Every emitter signature is `Catalog -> ...` or `Catalog -> Profile -> ...`; no Policy parameter accepted | `JsonEmitterTests.\`\`A18: JsonEmitter.emit takes no policy parameter\`\``; compiler exhaustiveness check |
| **A23** (lineage events carry PassVersion) | Pass-version bumps are recorded; operators reason about which version produced which decision | `LineageEvent` record carries `PassVersion: int`; literal constant per pass | `CatalogTests.\`\`A25: lineage events carry PassVersion\`\`` (11 test instances) |
| **A24** (lineage composition is chronological) | Audit reader trusts events are ordered earliest-first | `Lineage.bind` implementation: `{ m with Trail = m.Trail @ newEvents }` | `LineageTests.\`\`A24: trail is chronological under bind\`\``; property tests on bind associativity |
| **A26** (lineage layers separately from structural identity) | Refactored Kind is the same Kind even with different lineage trails | `Lineage<'a>` custom equality override projects through `Value` only (`Lineage.fs:223-228`) | `LineageTests.\`\`A26: lineage difference does not affect structural equality\`\`` (3 test instances) |
| **A33** (deterministic ordering) | SSDT diffs are reproducible across refactors; data inserts FK-safe | `DeterministicOrder` and `TopologicalOrder` are distinct types; `SchemaEmissionConfig` accepts deterministic; `DataEmissionConfig` accepts topological | Type-level enforcement; `TopologicalOrderPassTests` |
| **A34** (Profile independence) | Profile changes don't induce changes in unrelated parts of the system | `Profile` references no `Catalog` or `Policy` types | `ProfileTests.\`\`A34: passes that do not read Profile produce identical output for Profile.empty and any Profile\`\`` (4 test instances) |
| **A35** (Π output is deterministic statement stream) | Operator's deployment harness consumes a typed stream; realization choice is invisible to Π | `SsdtDdlEmitter.statements : Catalog -> seq<Statement>` is the canonical form | All `SsdtDdlEmitter` tests; `Render.toText` round-trip |
| **A36** (bulk-vs-incremental is realization policy) | `Deploy.executeStream` folds `InsertRow` runs into `SqlBulkCopy`; Π unaware | Realization layer separate from Π; signature enforces | `EndToEndPipelineTests` cover both forms |
| **A38** (CatalogDiff exhaustiveness) | Diff is canonical, exhaustive, no kind forgotten or doubled | `CatalogDiff = private CatalogDiff of CatalogDiffData`; smart constructor `between` enforces set algebra | `CatalogDiffTests` (9 worked examples + property tests on disjointness, determinism, module-permutation invariance) |
| **A39** (aggregate-root smart-constructor invariants) | `Catalog.create` succeeds → all invariants hold; consumers don't re-validate | `Catalog.create` validates 5 invariants in one pass; `Module.create`, `ColumnProfile.create`, `ArtifactByKind.create` similarly | `CatalogTests`, `ProfileTests`, `ArtifactByKindTests` |
| **A40** (harmonization-via-parameterization) | One algorithm, multiple projections (SelfLoopPolicy for topo) | `TopologicalOrderPass.runWith : SelfLoopPolicy -> Catalog -> Lineage<TopologicalOrder>` | `TopologicalOrderPassTests`; both modes verified |
| **T1** (Project is deterministic) | Re-running V2 produces byte-identical output | No I/O in Core; no `DateTime.Now`/`Random`; sorting by SsKey | 44 test instances across `EndToEndPipelineTests`, `JsonEmitterTests`, `DacpacEmitterTests`, property tests |
| **T11** (sibling-Π commutativity) | Every emitter covers every Catalog kind; outputs agree on shared values | `ArtifactByKind<'element> = private ArtifactByKind of Map<SsKey, 'element>` with smart constructor enforcing keyset completeness | 9 test instances in `SiblingEmitterContractTests` |
| **T4** (sibling functor commutativity) | SSDT references map to same SsKeys as JSON objects | Both emitters consume same Catalog; references by SsKey | `JsonEmitterTests.\`\`T11: sibling Pi's agree on physical realization for every kind\`\`` |

### 4.2 Bucket B — Convention-enforced (L3 ✓ L2 ✓ test ✓ but L1 ✗)

**12 axioms in this bucket.** Each is tested, each has an L2 statement, each has an operator-facing L3 behavior — but the type system doesn't structurally prevent violation. Discipline, code review, and tests are what hold the property.

| Axiom | Why L1 is convention-only | Risk of degradation |
|---|---|---|
| **A3** (identity invariant under rename) | `TableRenamePass` touches `Physical`/`Name`, never `SsKey` — by convention, not by signature. A hypothetical pass could violate this if not reviewed. | Low — code review and `TableRenameTests` catch. But N=1 enforcement; would benefit from a structural sibling. |
| **A6** (three substantive aggregates: Catalog, Policy, Lifecycle) | Top-level types exist; no others as globals. Convention. | Low — boundaries are clear. |
| **A7** (Static modality is structural) | `ModalityMark.Static` payload exists; unfold pass is *expected* to use it. | Low — single consumer. |
| **A12 amended** (Policy has four orthogonal axes) | `Policy = { Selection; Emission; Insertion; Tightening }`; passes respect axes by convention. | Medium — coupling could leak if a contributor reads multiple axes in one pass. |
| **A13** (type correspondence is policy) | `Policy.TypeMapping`; rendering applies it. Convention separates IR from policy. | Low — single consumer (Render layer). |
| **A14** (visibility is policy) | `VisibilityMask` pass applies `Policy.Selection`. Convention. | Low. |
| **A15** (naming morphism is policy and never touches identity) | `NamingMorphism` pass touches `Kind.Name` only by convention; `SsKey` flow-through is structural in the pass but not in the writer-monad signature. | Medium — `NamingMorphismTests` cover; a new naming pass might forget. |
| **A16** (static treatment is policy) | `Policy` carries treatment enum; passes use it. Convention. | Low. |
| **A25** (lineage is constitutive, not observed) | Every pass returns `Lineage<_>` — by signature, but no structural commitment forbids a "utility pass" from returning bare `Catalog`. | Medium — high-value target for structural promotion (Campaign B). |
| **A28** (profile evidence trails carry ProbeStatus) | `ColumnProfile.create` enforces this — actually structurally enforced. Reclassify? | Low / re-classify. **On second look, A28 belongs in Bucket A.** ProbeStatus is a required field on every `ColumnProfile`; the smart constructor's invariants include it. Audit error in the bridge agent's report; corrected here. |
| **A35** (statement stream output) | Signature names it; convention enforces realization separation. | Low — single consumer pattern. |
| **A36** (bulk vs. incremental is realization) | Realization layer separate; convention keeps Π pure. | Low. |

### 4.3 Bucket C — Weaknesses

**20 axioms in this bucket.** Classified by why:

**Untested formalism (low-leverage; type system is the contract).** These are stated as axioms but no dedicated test exists because the type system already enforces them by construction.
- **A8** (kinds carry a fixed shape) — `Kind` record fields are the contract.
- **A9** (Origin is closed three-way DU) — closed DU is the contract.
- **A10** (references are directional) — `Reference` type is directional; SymmetricClosure produces inverses.
- **A11** (modules form a coproduct) — implicit in module-list shape.
- **A17** (Project = Π ∘ E) — architectural; visible in directory structure.
- **A19** (each pass is structure-preserving endofunctor) — signature is the contract.

**Aspirational / pending promotion.**
- **A37** (Π-erased axes named) — Candidate; promotes at chapter-3.4 close when ≥2 binary Π's require the same erasure set. `equalModuloDacpacErasure` predicate exists.

**Deferred (architecture-level, not code-level).** Snapshot/persistence layer is Phase B+.
- **A27** (pointer swap is atomic)
- **T2** (coproduct preservation)
- **T3** (free construction / universal property)
- **T7** (snapshot deduplication)
- **T10** (boundary honesty)

**Subsumed by prior axioms.** No dedicated test because antecedent tests cover.
- **A21** (refresh is idempotent on stable input) — subsumed by T1.
- **A22** (snapshots are content-addressed) — implementation detail; deferred with persistence layer.
- **T5** (lineage compositionality) — A24 is the test surface.
- **T6** (refresh idempotence) — T1 is the test surface.
- **T8** (structural diffability) — A38 is the test surface.
- **T9** (refactor freedom under rename) — A3 + A4 are the test surface.

**Scope boundaries (tested by exclusion).**
- **A29** (authorization is not in the algebra)
- **A30** (business logic is not in the catalog)
- **A31** (the catalog is a federation point)

**Partial structural enforcement.**
- **A20** (pass order is meaningful and explicit) — stated; specific commutations tested (A33); general principle has no dedicated property test. **Trigger**: if an operator reorders passes in their config, this becomes load-bearing.
- **A6 amended** (Lifecycle temporal axis) — named but not operationalized; `Lifecycle` type doesn't exist yet. **Trigger**: when rename history threading requires temporal context.
- **A32** (passes producing emitter-consumed values) — typed (e.g., `UserRemapContext`) but partial structural enforcement; multi-environment commutativity test holds.

### 4.4 Bucket D — Unnamed axioms (the gap-hunt's contribution)

(See Part VIII for the full 30-candidate list.) Summary count for the synthesis:
- **Tier 1 (cutover blocker)**: originally 4 unnamed axioms; now **3 candidates + 1 formalized**. Formalized 2026-05-12: `L3-Boundary-AtomicEmission` (Bucket D → A; see Part XII.1). Remaining candidates: no silent V1 feature skip (A.0' completion), SSDT redeploy idempotence + CDC silence (chapter 4.1.B), manifest matches disk (A.7.2).
- **Tier 2 (strongly desired)**: 10 unnamed axioms.
- **Tier 3 (nice-to-have)**: ~16 unnamed axioms.

---

## Part V — L1 Structural Commitments Inventory

The codebase's L1 commitments — the type-system contracts that operationalize the axioms.

### 5.1 Value objects with smart constructors

Every type that gates construction through `Result<'a>`:

| Type | Smart constructor | Invariants enforced |
|---|---|---|
| `Name` | `Name.create : string -> Result<Name>` (`Catalog.fs:28`) | Non-blank |
| `SsKey` | `SsKey.ossysOriginal`, `SsKey.synthesized`, `SsKey.synthesizedComposite`, `SsKey.derivedFrom`, `SsKey.fromV1` (`Identity.fs:70-118`) | Per-variant: non-blank source, non-blank basis components, non-blank reason for derivation, valid GUID for V1Mapped |
| `SchemaName` | `SchemaName.create : string -> Result<SchemaName>` (`Coordinates.fs:77`) | Non-blank; ≤128 chars (SQL Server identifier limit) |
| `TableName` | `TableName.create : string -> Result<TableName>` (`Coordinates.fs:103`) | Non-blank; ≤128 chars |
| `ColumnName` | `ColumnName.create : string -> Result<ColumnName>` (`Coordinates.fs:129`) | Non-blank; ≤128 chars |
| `TableId` | `TableId.create : string -> string -> Result<TableId>` (`Coordinates.fs:168`) | Non-blank schema; non-blank table; error aggregation when both fail |
| `ColumnProfile` | `ColumnProfile.create : SsKey -> int64 -> int64 -> ProbeStatus -> Result<ColumnProfile>` (`Profile.fs:106-144`) | RowCount ≥ 0; NullCount ≥ 0; NullCount ≤ RowCount; ProbeStatus present |
| `ProbeStatus` | `ProbeStatus.create : DateTimeOffset -> int64 -> ProbeOutcome -> Result<ProbeStatus>` (`Profile.fs:35-45`) | Non-negative sample size |
| `CategoricalDistribution` | `CategoricalDistribution.create` (`Profile.fs:219-233`) | Truncation contract: `IsTruncated = false ⇒ DistinctCount = Frequencies.Length`; per-value counts non-negative |
| `NumericDistribution` | `NumericDistribution.create` (`Profile.fs:~290`) | Percentile monotonicity: `Min ≤ P25 ≤ P50 ≤ P75 ≤ P95 ≤ P99 ≤ Max`; sample-size floor for percentile confidence |
| `NullabilityTighteningConfig` | `NullabilityTighteningConfig.create` (`Policy.fs:~70`) | `NullBudget ∈ [0, 1]` per A12-amended |

### 5.2 Aggregate-root smart constructors (A39)

Types that enforce cross-field referential integrity at construction:

| Aggregate root | Smart constructor | Invariants |
|---|---|---|
| `Catalog` | `Catalog.create : Module list -> Result<Catalog>` (`Catalog.fs:414-491`) | (1) Module SsKeys disjoint; (2) Kind SsKeys disjoint across modules; (3) every `Reference.SourceAttribute ∈ Kind.Attributes`; (4) every `Reference.TargetKind` exists in Catalog; (5) every `Index.Columns ⊆ Kind.Attributes` |
| `Module` | `Module.create : Kind list -> Result<Module>` (`Catalog.fs:355-376`) | Within-module SsKey disjointness on Kinds |
| `ArtifactByKind<'element>` | `ArtifactByKind.create : Catalog -> Map<SsKey, 'element> -> Result<ArtifactByKind<'element>, EmitError>` (`ArtifactByKind.fs:69-80`) | T11 structural: strict equality between slice keyset and `Catalog.allKinds`. Every kind present, no extras. |
| `CatalogDiff` | `CatalogDiff.between : Catalog -> Catalog -> Result<CatalogDiff, EmitError>` (`CatalogDiff.fs:76+`) | A38 exhaustiveness: every SsKey in source ∪ target is in exactly one of four pairwise-disjoint partitions (Renamed / Added / Removed / Unchanged) |

### 5.3 Closed discriminated unions with typed payloads

Every closed DU where adding a string payload would have been the lazy choice but the codebase chose typed:

| DU | Variants | File:line |
|---|---|---|
| `TransformKind` | `Touched`, `Renamed`, `Created`, `Removed of RemovalReason`, `Annotated of AnnotationDetail`, `PhysicallyRenamed of PhysicalRename` | `Lineage.fs:163-200` (post-Slice-1) |
| `Origin` | `OsNative`, `ExternalViaIntegrationStudio`, `ExternalDirect` | `Catalog.fs:39-45` |
| `ModalityMark` | `Static of StaticRow list`, `TenantScoped`, `SoftDeletable`, `SystemOwned` | `Catalog.fs:77-94` |
| `ProbeOutcome` | `Succeeded`, `FallbackTimeout`, `Cancelled`, `TrustedConstraint`, `AmbiguousMapping` | `Profile.fs:10-14` |
| `OrderingMode` | `Topological`, `Alphabetical`, `JunctionDeferred` | `TopologicalOrder.fs:19-22` |
| `SelfLoopPolicy` | `TreatAsCycle`, `SkipSelfEdges` | `TopologicalOrder.fs:40-52` |
| `EmitError` | `KindNotProduced of SsKey`, `UnexpectedKind of SsKey`, `RenderFailed of SsKey * string`, `OverlappingEmitterCoverage of SsKey * string list` | `ArtifactByKind.fs:17-38` |
| `RemovalReason` | `OriginPredicate of Origin`, `ExplicitKeyList`, `ModalityPredicate of ModalityMark` | `Lineage.fs:26-29` |
| `AnnotationDetail` | `NullabilityDecision`, `UniqueIndexDecision`, `ForeignKeyDecision`, `CategoricalUniquenessDecision`, `ClosureSkipped of SymmetricClosureSkipReason`, `Label of string` (test-only escape hatch) | `Lineage.fs:109-130` |
| `ReferenceAction` | `Cascade`, `NoAction`, `SetNull` | `Catalog.fs:150-154` |
| `PhysicalRename` (record, not DU, but typed payload) | `{ Before: TableId; After: TableId }` | `Lineage.fs:~160` (Slice 1) |

### 5.4 `[<RequireQualifiedAccess>]` modules

Modules that take case-name collision seriously by mandating qualified access:

| Module | File |
|---|---|
| `UuidV5` | `UuidV5.fs` |
| `Catalog` | `Catalog.fs:380` |
| `Lineage` | `Lineage.fs` |
| `Diagnostics` | `Diagnostics.fs` |
| `LineageDiagnostics` | `Lineage.fs` |
| `Composition` | `Strategies/Composition.fs` |
| `Profile` | `Profile.fs:549` |
| `TopologicalOrder` | `TopologicalOrder.fs` |
| `PhysicalSchema` | `PhysicalSchema.fs` |
| `SqlLiteral` | `SqlLiteral.fs` |
| `PinnedWriting` | `PinnedWriting.fs` |
| `RawValueCodec` | `RawValueCodec.fs` |
| `CatalogDiff` | `CatalogDiff.fs:55` |
| `NullabilityRules`, `UniqueIndexRules`, `ForeignKeyRules`, `CategoricalUniquenessRules` | `Strategies/*.fs` |
| `NullabilityPass`, `UniqueIndexPass`, `ForeignKeyPass`, etc. | `Passes/*.fs` |
| `Name`, `Origin`, `ModalityMark`, `SsKey`, `ColumnProfile`, `ProbeStatus`, etc. | Throughout |

### 5.5 Identity / Presentation / Physical axes (three separate types)

| Axis | Type | Role |
|---|---|---|
| Identity | `SsKey` (`Identity.fs`) | Four-variant DU carrying provenance; participates in equality |
| Presentation | `Name` (`Catalog.fs:18`) | Single-case DU; policy-transformed (A15); never participates in identity |
| Physical | `TableId` (`Coordinates.fs:142`) | `{ Schema: string; Table: string }`; smart-constructed; rewritten by `TableRenamePass` without touching identity |
| Origin | `Origin` (`Catalog.fs:39`) | Closed three-way DU (A9); provenance flag |
| Modality | `ModalityMark list` (`Catalog.fs:77`) | List of closed marks; orthogonal to identity |

### 5.6 Deterministic-ordering commitments

| Where | What |
|---|---|
| `TopologicalOrderPass.runWith` | Produces canonical FK-safe order via Kahn's algorithm + SCC resolution; deterministic |
| `NormalizeStaticPopulations` | Sorts static rows deterministically (by SsKey-then-Map) |
| `CatalogDiff.between` | Set.difference + Set.intersect produce deterministic partitioning |
| `ArtifactByKind` rendering | Keyed by SsKey; consumers iterate in deterministic order |
| `Config.validate` (D12) | Canonical sort on user-supplied collections at validation time |
| `Lineage.bind` | Earliest-first concatenation (A24) |
| `Lineage<'a>` equality override | Projects through `Value` only (A26) |

### 5.7 No-I/O-in-Core

The `Projection.Core` project has zero I/O. Audited clean per `CHAPTER_1_CLOSE.md §1.1`. Confirmed by:
- No `System.IO` imports in any Core file
- No `Microsoft.Data.SqlClient` in Core
- No `File.WriteAllText` or `Directory.CreateDirectory` in Core
- No `DateTime.Now` or `Random` in Core paths used by passes (Bench.fs uses time for measurement; not passed to passes)

Adapters and Targets hold I/O at the boundary.

### 5.8 Per-pass return-type codification

Per DECISIONS 2026-05-13:
- **`Lineage<'output>`** for decisions only: `SymmetricClosure`, `TableRename`, `NormalizeStaticPopulations`, `CanonicalizeIdentity`, `NamingMorphism`, `VisibilityMask`, `TopologicalOrderPass`.
- **`Lineage<Diagnostics<'output>>`** for decisions + observer-relevant findings: `NullabilityPass`, `UniqueIndexPass`, `ForeignKeyPass`, `CategoricalUniquenessPass`, `UserFkReflowPass`.

---

## Part VI — The Catalog of Illegal States Still Representable

(Synthesized from Round 1 of the audit on 2026-05-11.) Organized by tier of structural commitment.

### 6.1 Tier 0 — Universal smart-constructor sweep (foundational)

These types are missing the smart-constructor pattern that V2 has otherwise established:

| # | Type | Missing | Proposed | Blast radius | Leverage |
|---|---|---|---|---|---|
| 0.1 | `Profile` | Aggregate-root smart constructor | `Profile.create` enforcing: no duplicate `AttributeKey` in `Columns`/`UniqueCandidates`/`CompositeUniqueCandidates`; at most one `AttributeDistribution` per attribute (Categorical XOR Numeric); CdcAwareness invariant (below); ForeignKeyReality bounds (below) | Large (adapters thread Result) | Very High |
| 0.2 | `CdcAwareness` | Smart constructor enforcing documented contract | `CdcAwareness.create` validating `Map.forall (fun k _ -> Set.contains k enabled) instances` | Medium | High |
| 0.3 | `ForeignKeyReality` | Smart constructor with empirical-probe invariants | `ForeignKeyReality.create` enforcing `OrphanCount ≥ 0` and `HasOrphan ⇔ OrphanCount > 0` | Medium | High |
| 0.4 | `LineageEvent` | Smart constructor enforcing field validity | `LineageEvent.create` enforcing non-blank `PassName`, `PassVersion ≥ 1`. Replaces ~30 record-literal sites across pass drivers | Medium (30 call sites; mechanical) | High |
| 0.5 | `DiagnosticEntry` | Smart constructor with code-namespace validation | `DiagnosticEntry.create` enforcing non-blank `Source`, `Code`, `Message`; `Code` follows `<top>.<subdomain>.<problem>` dot-namespace | Small (15 call sites) | Medium |

### 6.2 Tier 1 — Catalog cross-field invariants (extend `Catalog.create`)

Each is a contradictory combination currently representable:

| # | Invariant | Illegal state | Where checked today |
|---|---|---|---|
| 1.A | `Attribute.IsIdentity = true ⇒ Column.IsNullable = false` | `{ IsIdentity=true; IsNullable=true }` (SQL forbids `IDENTITY NULL`) | Nowhere |
| 1.B | `Attribute.IsMandatory = true ⇒ Column.IsNullable = false`, OR lift to `Nullability = CanBeNull \| NotNull \| Mandatory` DU | `{ IsMandatory=true; IsNullable=true }` (semantic contradiction) | Nowhere |
| 1.C | `Index.IsPrimaryKey = true ⇒ Index.IsUnique = true` | `{ IsPrimaryKey=true; IsUnique=false }` (PK is always unique by SQL definition) | Nowhere |
| 1.D | `Attribute.{Length, Precision, Scale}` coherence with `Type` | `{ Type=Integer; Length=Some(50) }` (Length is Text/Binary only); `{ Type=Decimal; Precision=None }` (Decimal needs precision) | Nowhere |
| 1.E | `Attribute.Length` validity: `0 < L ≤ 8000` for Text (VARCHAR limit) | `{ Length=Some(9000) }` | Nowhere |
| 1.F | PK ordering: PK-flagged attributes appear consecutively at the start of `Attributes`, OR explicit `Kind.PrimaryKeyOrder : SsKey list` | Compound PK with `IsPrimaryKey=true` flags in any list order; effective PK depends on insertion order | Nowhere |
| 1.G | At most one `ModalityMark.Static` per Kind | `Modality = [ Static [...]; Static [...] ]` (Kind.staticPopulations silently drops the second) | Nowhere |
| 1.H | Physical-name uniqueness: no two Kinds share `(Schema, Table)` | Two Kinds with identical `Physical` | Nowhere — **was originally slice 2 in the airtight campaign** |

### 6.3 Tier 2 — Name uniqueness invariants

None of these are enforced; lookups by name silently return arbitrary candidates:

| # | Invariant | Where checked today |
|---|---|---|
| 2.I | Module names unique within a Catalog | Nowhere |
| 2.J | Kind names unique within a Module | Nowhere |
| 2.K | Attribute names unique within a Kind | Nowhere |
| 2.L | Reference names unique within a Kind | Nowhere |
| 2.M | Index names unique within a Kind | Nowhere |
| 2.N | Index column SsKey uniqueness within an Index (or `IndexColumns = Unique \| Functional` DU for expression-indexes) | Nowhere |
| 2.O | `StaticRow.Identifier` uniqueness within a Static-mark population | Nowhere |

### 6.4 Tier 3 — Equality + canonical ordering

| # | Change | Rationale |
|---|---|---|
| 3.P | `Catalog.equivalent : Catalog -> Catalog -> bool` — semantic equality (Sets, not lists) | Round-trip / canary diffs fail spuriously today on reorder |
| 3.Q | `Catalog.Modules` canonical-sorted by `SsKey.rootOriginal` in `Catalog.create` | Removes order-sensitivity at the type-equality level |
| 3.R | Same for `Module.Kinds` and downstream collections where order is non-load-bearing | Consistency |

### 6.5 Tier 4 — Config parse-time strengthening

| # | Change |
|---|---|
| 4.S | Stringly-typed enums become closed DUs at parse time: `InsertMode`, `StaticSeedParentMode`, `Provider`, `Selection`, `Insertion`, `UserMatchingStrategy`, `UserMatchingFallback` |
| 4.T | `TypeMapping.Overrides` keys → `PrimitiveType`, values → `SqlTypeCorrespondence` (parsed at parse time) |
| 4.U | `ValidationOverrides` patterns parsed at parse time into typed `ValidationPattern = Wildcard \| ModuleEntityPair of Name * Name` |
| 4.V | D9 credential token list expansion: `bearer`, `authorization`, `credentials`, `oauth`, `personal+access+token`, `private+token` |
| 4.W | `ModuleSelector` deduplication + explicit empty-list semantics |
| 4.X | CLI argv: explicit parser to reject ambiguous forms (`--config X --config Y`, mixed positional+flag) |

### 6.6 Tier 5 — Boundary value objects

| # | Lift |
|---|---|
| 5.Y | `RelativePath` value object: `SsdtFile.RelativePath : string → RelativePath`; smart constructor validates no traversal sequences (`../`), no absolute paths |
| 5.Z | `SchemaName` / `TableName` lifted into `TableId` (`{ Schema: SchemaName; Table: TableName }`) — **was originally slice 3** |
| 5.AA | `ColumnRealization.ColumnName : string → ColumnName` — adjacent to slice 5.Z |
| 5.BB | `SqlAuthMode` closed DU at the SQL connection boundary |

### 6.7 Tier 6 — Adapter-side invariants

| # | Invariant |
|---|---|
| 6.CC | `CatalogReader` rejects zero-attribute entities fail-fast at the adapter boundary |

### 6.8 Tier 7 — Already clean (confirmed)

| # | Item |
|---|---|
| 7.1 | `LineageBuffer` opacity (F# `private` is IL-enforced) |
| 7.2 | Lineage trail coherence (intentional design: pass can observe without changing) |
| 7.3 | `DiagnosticSeverity` closed DU |
| 7.4 | `Composition.fanOut` convention-enforced (sufficient per audit) |
| 7.5 | Outcome / KeepReason DUs across all passes are closed |
| 7.6 | Strategy registry uses typed function-type seam (no reflection) |
| 7.7 | `ProbeStatus` is metadata-oriented |
| 7.8 | `StaticRow.Values` is boundary-appropriate string (`RawValueCodec` is the typed path) |

### 6.9 Tier 8 — Deferred per "IR grows under evidence"

| # | Item | Trigger |
|---|---|---|
| 8.1 | `PrimitiveType` expansion (DateTime2, SmallInt, TinyInt, Bit-vs-Boolean) | V1 admire surfaces use |
| 8.2 | `ReferenceAction.SetDefault` variant | V1 evidence |
| 8.3 | Profile boolean → richer-DU lifts (HasDuplicate, HasOrphan, IsNoCheck) | A pass needs richer evidence |
| 8.4 | `AnnotationDetail.Label` production-usage lint | Real misuse detected |

---

## Part VII — The Three Rings of Weakness

The bidirectional audit reveals that V2's structural weakness is concentrated in three concentric rings *outside* the IR algebra.

### 7.1 Ring 1 — Boundary contracts

V1→V2 parsing, V2→disk writing, V2→operator diagnostics. The contract surface where V2 meets the world.

**No L2 axiom for:**
- **Atomic emission** — no half-written outputs on failure. The operator's mental model is "if V2 fails, the output directory is unchanged." Nothing in the code structurally guarantees it. `Compose.write` writes files sequentially; a `SqlException` partway leaves a partial state.
- **Lossless parsing of carried fields** — A1's bound documents the *known* JSON projection loss; nothing axiomatizes *preservation* for the fields V2 does carry. If `CatalogReader` normalizes (e.g., trims whitespace), that's silent drift.
- **Manifest matches disk** — the operator audits via manifest; nothing ensures the manifest entries correspond to files actually written. A failed write leaves a manifest claim that no file backs.
- **Cross-platform encoding consistency** — LF line endings, POSIX path separators, UTF-8 throughout. T1 says "byte-deterministic" but doesn't name these axes. CRLF leak on Windows is a silent operator-trust killer.

**Most leveraged fix**: name these as L3 axioms; promote `Compose.write` to atomic (write-to-temp, fsync, atomic-rename); add a manifest-matches-disk post-condition check.

### 7.2 Ring 2 — Operational evolution

What V2 promises *over time*. The contract surface where V2 meets its own future versions.

**No L2 axiom for:**
- **Diagnostic code stability across V2 versions** — operator's CI grep depends on it. If `tightening.nullability.inferred` becomes `profiling.nullability.detected` in v2.1, operator alerts silently break.
- **Config schema backward-compatibility** — operator adds a new key to their config; existing fields keep producing the same output. No semver-like contract.
- **Decision-log replay completeness** — operator audits "why did V2 decide X?" without reading source. Today's decision-log entries name *what* but not always *why with citation to evidence*.
- **Schema-evolution → refactor-log → DacFx compatibility** — the chain that makes renames data-loss-safe. T9 names refactor-freedom, doesn't name the DacFx-emission contract.

**Most leveraged fix**: define a versioning protocol for diagnostic codes and config schemas; commit to backward-compat with explicit deprecation paths.

### 7.3 Ring 3 — Compositional reasoning

What V2 promises about how its passes compose. The contract surface where V2's internal algebra meets its own pipeline.

**Weak axioms (not unnamed, just under-tested):**
- **A20 (pass-order is meaningful)** — stated; specific commutations tested via A33; no general property test enforces "passes that commute, commute."
- **A6-amended (Lifecycle temporal axis)** — named but not operationalized. Schema evolution over time requires a `Lifecycle` type that doesn't exist yet.

**Most leveraged fix**: write a general pass-commutation property test for the pairs that should commute (e.g., `NamingMorphism` and `VisibilityMask` should commute when their predicates don't overlap).

---

## Part VIII — The Gap-Hunt Findings (30 Candidate Unnamed Axioms)

The full operator-question catalog from the gap-hunt agent. Each is a candidate L3 axiom with no L2 backing today.

### 8.1 Re-emission & determinism under stable input

**Q1**: "Can I trust that re-running V2 with the same input produces byte-identical output across processes, OSes, and time?" Currently named: partial (T1 covers byte-determinism for the projection function; doesn't explicitly cover cross-platform / temporal extensions). Tier: 1. Failure mode: operator runs V2 Monday on Linux, Friday on Windows, gets different outputs; CI flags it as a schema change.

**Q2**: "Can I trust that re-running V2 against a *slightly* changed model produces a *minimal* diff in output?" Currently named: no. Tier: 2. Failure mode: operator adds a column to Entity A; entire SSDT project diff reorders 100 files because Entity B's index ordering shifted as a side effect; PR is unreviewable.

**Q3**: "Can I trust that V2's line-ending format is consistent across OSes (LF, not CRLF)?" Currently named: no. Tier: 2. Failure mode: Windows run produces CRLF; Linux CI flags every line as changed.

**Q4**: "Can I trust that V2's path separators in manifest and internal references are always POSIX (forward-slash) regardless of host OS?" Currently named: no. Tier: 2. Failure mode: Windows-emitted manifest with backslashes; Linux CI can't resolve paths.

**Q5**: "Can I trust that adding a new field to V2's config doesn't change output for existing configs?" Currently named: no. Tier: 2. Failure mode: V2 v2.1 adds `schema.normalization: bool` defaulting to true; existing configs silently produce different output.

### 8.2 Concurrency & idempotency at the SQL boundary

**Q6**: "Can I trust that two parallel V2 extract runs against the same OSSYS DB collide safely or not at all?" Currently named: partial (operator-owned per D11). Tier: 1. Failure mode: two CI jobs run simultaneously; both write to the same `osm_model.json`; result is corrupted.

**Q7**: "Can I trust that V2's SSDT emission is idempotent against the target — same artifact → zero ALTERs on second deploy?" Currently named: partial (handbook §7 Idempotency 101; no V2 axiom). Tier: 1. Failure mode: midway-failed deploy retried; SSDT generates different ALTERs the second time; conflict.

**Q8**: "Can I trust that V2's MERGE statements for data are idempotent — re-running leaves the DB in the same state?" Currently named: partial (handbook §7). Tier: 1. Failure mode: re-run after network failure UPDATEs rows that match on a softly-unique key.

**Q9**: "Can I trust that V2 emits DDL and data in an order safe for FK constraint enforcement?" Currently named: partial (A33 covers schema; data emission ordering not explicit). Tier: 1. Failure mode: child INSERT before parent EXISTS; deploy fails with FK violation.

### 8.3 Operator config stability

**Q10**: "Can I trust that an empty/minimal config produces fully reproducible, predictable behavior — every default is explicit and documented?" Currently named: no. Tier: 2. Failure mode: operator omits `policy.userMatching`; V2 silently uses a strategy they didn't intend.

**Q11**: "Can I trust that swapping the order of `tableRenames` in my config doesn't change output?" Currently named: yes (D12). Tier: 1. **Resolved post-Slice-1**: TopologicalOrderPass reads SsKey only; rename is structurally orthogonal. Test `D12: rename spec order does not affect the rewritten catalog` enforces.

**Q12**: "Can I trust that adding a new module to my config doesn't break existing modules' output?" Currently named: partial (A11 coproduct; module-selection independence not explicit). Tier: 2. Failure mode: adding `ReportingModule` reorders Module A's indexes due to a global sort change.

**Q13**: "Can I trust that the manifest matches what's actually on disk?" Currently named: no. Tier: 1. Failure mode: file-write failed partway; manifest lists files that don't exist; operator's reconciliation tool gives false confidence.

**Q14**: "Can I trust that migration-dependencies rows for one entity are emitted in declaration order?" Currently named: partial (D12 within-table). Tier: 2. Failure mode: operator declares `[Admin, User, Guest]`; V2 reorders; INSERT fails.

**Q15**: "Can I trust that decision/opportunity/validation logs are deterministically ordered and complete?" Currently named: partial (A24 chronology; log-level sorting not explicit). Tier: 2. Failure mode: operator can't grep for `Entity:Customer` in the log because it's scattered.

### 8.4 Error recovery & diagnostics

**Q16**: **"Can I trust that V2 fails fast (no partial/half-written artifacts) on parse errors?"** Currently named: no. Tier: 1. Failure mode: V2's profile-JSON parser fails on a malformed entry; V2 has already written half the SSDT DDL; output dir contains a mix of v1 and v2 files; operator can't tell what's current. **This is the highest-priority unnamed axiom.**

**Q17**: "Can I trust error messages enough to diagnose without reading code?" Currently named: partial (exit codes in §5.3). Tier: 1. Failure mode: V2 fails with `System.InvalidOperationException: Sequence contains no matching element` instead of "Migration dependency for Entity:Order references non-existent column 'StatusCode'."

**Q18**: "Can I trust that orphaned-FK errors identify the exact FK and orphan rows?" Currently named: partial (A23 events carry SsKey). Tier: 1. Failure mode: "Validation: orphaned foreign key found" without entity/attribute identification; operator must grep manifest.

**Q19**: "Can I trust that V2's re-runs don't silently corrupt output or produce inconsistent state?" Currently named: partial (A39 aggregate invariants). Tier: 2. Failure mode: Catalog populated with Entity A; Profile load times out; downstream emit silently drops Entity A's DDL.

**Q20**: "Can I trust that the decision log is sorted by a canonical key I can reason about (kind → pass → decision)?" Currently named: no. Tier: 2. Failure mode: log in pass-run order scattered across 100 lines; can't find what changed about one entity.

### 8.5 V1↔V2 equivalence & translation

**Q21**: **"Can I trust V2 doesn't silently skip OutSystems features V1 carries?"** Currently named: partial (V2_PRODUCTION_CUTOVER.md §3.3 catalogs gaps; no axiomatic "no silent skip" contract). Tier: 1. Failure mode: V1's Entity has a CHECK constraint; V2's Catalog doesn't carry CHECK constraints; emit drops it; production schema is incomplete. **Tier-1 unnamed axiom.**

**Q22**: "Can I trust V2's diff against V1 surfaces every difference (no false agreements)?" Currently named: partial (V2_PRODUCTION_CUTOVER.md §5.8). Tier: 2. Failure mode: both V1 and V2 silently drop the same feature; diff shows "no change"; feature is broken in both.

**Q23**: "Can I trust V2's JSON parser is lossless for carried fields?" Currently named: no (A1 bound documents the known loss only). Tier: 2. Failure mode: V1's entity name `" Order "` gets trimmed to `"Order"`; diff falsely shows a rename.

**Q24**: "Can I trust V2 only fires passes that have profile data, falling back when data is absent?" Currently named: partial (A34 profile independence). Tier: 2. Failure mode: ForeignKeyProbe times out; V2 fires FK pass anyway; marks FK as "valid"; production has orphans.

### 8.6 Audit trail & lineage

**Q25**: "Can I trust every change V2 made is in the lineage trail?" Currently named: yes (A25). Tier: 1. **No gap.**

**Q26**: "Can I trust diagnostic codes are stable across V2 versions?" Currently named: no. Tier: 2. Failure mode: operator's CI greps `profiling.nullability.inferred`; v2.1 renames to `profiling.nullability.detected`; alerts silently break.

**Q27**: "Can I trust I can replay V2's reasoning from the decision log alone?" Currently named: partial (A23 PassVersion). Tier: 2. Failure mode: log says "Nullability: inferred" without entity, evidence, or rule version; operator must read source.

### 8.7 Schema-evolution safety

**Q28**: "Can I trust a rename followed by re-emission produces SSDT that's deploy-safe over the existing schema?" Currently named: partial (T9 refactor freedom). Tier: 2. Failure mode: V2 generates refactorlog; SSDT ignores it; emits DROP+CREATE; data loss.

**Q29**: "Can I trust V2 doesn't reorder columns/constraints in ways DacFx treats as destructive?" Currently named: partial (A33 deterministic ordering). Tier: 2. Failure mode: v2.0 emits `[Id, Name, Email]`; v2.1 emits `[Id, Email, Name]`; DacFx ALTERs unnecessarily.

**Q30**: "Can I trust constraint-transformation passes (e.g., FK passes) preserve FK graph validity under transformations?" Currently named: partial (A10 reference directionality). Tier: 2. Failure mode: entity split inadvertently drops a shared FK; target table becomes vulnerable to orphans.

### Summary by tier

- **Tier 1 (cutover blockers; originally 4 axioms with no L2 backing): now 3 candidates + 1 formalized** — Q16 (atomic emission, **formalized 2026-05-12** as `L3-Boundary-AtomicEmission` per `PRODUCT_AXIOMS.md` Group Boundary; Bucket D → A), Q21 (no silent V1 feature skip; pending A.0' close), Q7+L3-D1 (SSDT redeploy idempotence + CDC silence; chapter 4.1.B in flight), Q13 (manifest matches disk; pending A.7.2). Q15 axiom-naming convention: boundary axioms live in `PRODUCT_AXIOMS.md` Group Boundary under the `L3-Boundary-*` namespace; `AXIOMS.md` A41+ stays reserved for algebra-interior extensions.
- **Tier 2 (strongly desired; some have partial L2 backing): ~10 axioms** — Q2, Q3, Q4, Q5, Q10, Q12, Q14, Q15, Q17, Q18.
- **Tier 3 (nice-to-have; cross-version safety): ~16 axioms** — the remainder.

---

## Part IX — Proposed Implementation Campaigns

The audit findings consolidate into three campaigns. Each is a discrete unit of work that shifts a class of properties from runtime-trust to compile-time enforcement. The campaigns supersede the prior 3-slice plan (slice 2 + slice 3); slice 1 (PhysicallyRenamed) is already shipped (commit `9d578cc`).

### 9.1 Campaign A — Bucket D Tier-1 axioms (cutover blockers)

**Goal**: name and operationalize the four boundary-axiom blind spots.

**Components**:

**A.1 — Atomic emission** (`L3-Boundary-AtomicEmission`).
- Promote `Compose.write` to transactional: write each file to a temp path, fsync, atomic-rename to final. On any failure, clean up temp paths and leave the output directory unchanged.
- Add property test: induce a failure midway through `Compose.write`; assert output directory is empty (or contains only files from before the failed run).
- Add L2 axiom to `AXIOMS.md`.
- Blast radius: small (one function in `Pipeline.fs`); medium for the test rig.

**A.2 — No silent V1 feature skip** (`L3-Boundary-NoSilentDrop`).
- Every concept Phase A.0' enumerates as "lifted into IR" gets one of two outcomes: (a) a typed Catalog field, OR (b) a `Diagnostic.Severity=Error` at the adapter boundary. No silent passthrough.
- Add a property test that scans the V1 input JSON for known feature markers and verifies V2 either represents them or emits a diagnostic.
- This is a *forcing function* for the A.0' workstream: until every gap in §3.3 has either an IR field or a Drop-Diagnostic, the axiom doesn't hold.
- Blast radius: large (every adapter site that drops a field; ~10-15 locations); high leverage.

**A.3 — SSDT redeploy idempotence + CDC silence** (`L3-Idempotence-OnRedeploy`).
- Already named as the highest-leverage deliverable in `V2_DRIVER.md`. Promote to a formal L2 axiom.
- The test is the CDC-silence property test on a production-shape canary (in flight at chapter 4.1.B).
- Adds nothing new to L1; reframes existing work under a named axiom.

**A.4 — Manifest matches disk** (`L3-Boundary-ManifestMatchesDisk`).
- `Compose.write` returns its written paths; `ManifestEmitter` consumes that list, not the in-memory `Outputs` map.
- Property test: post-write, every manifest entry exists on disk; every file on disk has a manifest entry.
- Blast radius: small (one signature change in `Compose.write` + ManifestEmitter input).

**Estimated effort**: 3-5 days.

### 9.2 Campaign B — Bucket B → Bucket A promotions (structural fortification)

**Goal**: lift the 12 Bucket-B axioms from convention-enforced to structurally enforced where reasonable.

**Components** (the high-leverage subset):

**B.1 — A25 (lineage is constitutive) → signature-enforced**.
- Introduce a type alias: `type Pass<'in, 'out> = 'in -> Lineage<'out>` (or `'in -> Lineage<Diagnostics<'out>>`).
- Refactor pass modules to expose `Pass<Catalog, T>` rather than `Catalog -> Lineage<T>`. The signature is now algebraic, not just descriptive.
- Forbid (via lint or convention enforced by code review) functions in `Passes/` that don't match the `Pass<_, _>` signature.

**B.2 — A28 (probe status carried)**.
- (Audit error noted in Part IV.2: A28 is actually already in Bucket A. `ColumnProfile.create` enforces `ProbeStatus` as a required field. Confirm and reclassify; no work needed.)

**B.3 — A32 (passes producing emitter-consumed values) → fully structural**.
- Types like `UserRemapContext` get smart constructors; `UserRemapContext.create` is the only path to construction. Currently the construction is convention-enforced.
- Blast radius: small.

**B.4 — Catalog cross-field invariants (Tier 1 from the catalog)**.
- Extend `Catalog.create` with the 8 invariants in Part VI.2 (1.A through 1.H). Each is a check; aggregate errors.
- This subsumes the original slice 2 (physical-name uniqueness = 1.H) and adds 7 more invariants.
- Blast radius: medium (validation logic in `Catalog.create`; possible fixture updates if any test catalogs violate).

**B.5 — Name uniqueness invariants (Tier 2 from the catalog)**.
- Add Module/Kind/Attribute/Reference/Index/StaticRow.Identifier uniqueness checks to `Catalog.create` and `Module.create`.
- Aggregate errors with existing checks.
- Blast radius: medium.

**B.6 — Equality + canonical ordering (Tier 3 from the catalog)**.
- `Catalog.equivalent` for semantic equality.
- Canonical sort of `Catalog.Modules` and `Module.Kinds` in their respective smart constructors.
- Blast radius: small (additive); canary tests benefit immediately.

**Estimated effort**: 5-7 days.

### 9.3 Campaign C — Bucket D Tier-2 axioms + boundary VOs + Config strengthening

**Goal**: lift the remaining ~10 Tier-2 unnamed axioms and ship the boundary value-objects + Config parse-time strengthening from Part VI.

**Components**:

**C.1 — TableId typed VOs (the original slice 3)**.
- Lift `TableId` from `{ Schema: string; Table: string }` to `{ Schema: SchemaName; Table: TableName }`.
- Mechanical refactor; large blast radius (every site reading `tid.Schema` or `tid.Table` as string).
- Blast radius: large but mechanical.

**C.2 — ColumnRealization typed**.
- `ColumnRealization.ColumnName : string → ColumnName`.
- Adjacent to C.1.

**C.3 — Config parse-time strengthening (Tier 4 from the catalog)**.
- Stringly-typed config enums become closed DUs at parse time.
- D9 credential-token list expansion.
- `ModuleSelector` deduplication + empty-list semantics.
- `ValidationOverrides` patterns parsed at parse time.
- CLI argv: explicit parser for ambiguous forms.
- Blast radius: medium (concentrated in `Config.fs` + `RenameBinding.fs`).

**C.4 — Boundary VOs**.
- `RelativePath` VO with traversal validation.
- `SqlAuthMode` closed DU.
- Blast radius: small.

**C.5 — Cross-platform encoding axioms** (Q3, Q4 from gap-hunt).
- LF line endings enforced at every write site.
- POSIX path separators enforced in manifest.
- UTF-8 byte-order-mark policy explicit.
- Add `L3-Boundary-CrossPlatform` axiom.
- Blast radius: small.

**C.6 — Diagnostic code stability** (Q26 from gap-hunt).
- Document the diagnostic-code namespace in a versioned schema (e.g., `docs/diagnostic-codes.v1.md`).
- Forbid removing or renaming a code in v2.x without a deprecation period.
- Add `L3-Evolution-CodeStability` axiom.
- Blast radius: small (documentation + lint).

**C.7 — Adapter zero-attribute rejection** (Tier 6).
- `CatalogReader` fails-fast on zero-attribute entities.
- Blast radius: small.

**Estimated effort**: 7-10 days.

### 9.4 Sequencing and connection to existing slices

The campaigns supersede the prior airtight 3-slice plan as follows:

| Original slice | Disposition |
|---|---|
| Slice 1: PhysicallyRenamed variant | **Shipped** (commit `9d578cc`). Closes one item under Campaign B (A26 + A1 fortification). |
| Slice 2: A39 physical-name uniqueness | **Subsumed by Campaign B.4** (Catalog cross-field invariants). Broader scope; physical-name uniqueness is 1 of 8 invariants. |
| Slice 3: TableId typed VOs | **Subsumed by Campaign C.1**. Same work, different framing. |

**Recommended sequence**: A → B → C.
- Campaign A first because it's narrowly scoped, addresses cutover blockers, and validates the workflow.
- Campaign B second because it builds on Campaign A and addresses the structural-fortification ramp.
- Campaign C third because it's the longest, has the most concentrated change, and benefits from the disciplines established in A and B.

**Alternative**: A and B in parallel since they touch different files. C must wait for the smart-constructor sweep in B to land first (C.1's TableId lift depends on validating the lift pattern via B.4's smart-constructor expansion).

### 9.5 Estimated total effort

- Campaign A: 3-5 days
- Campaign B: 5-7 days
- Campaign C: 7-10 days
- Buffer + tests + canary validation: +30%

**Total: ~3-4 weeks** for one focused developer; ~2 weeks if A and B run in parallel by two developers.

---

## Part X — Per-Concern Coverage Matrix

This is the integrated view: every L3 product axiom in Part III mapped to the L2 axiom (if any) that underwrites it, the L1 commitment (if any) that operationalizes it, and the test (if any) that verifies it. Gaps are highlighted.

### 10.1 Schema concern coverage

| L3 axiom | L2 | L1 commitment | Test | Status |
|---|---|---|---|---|
| L3-S1 (byte-determinism) | T1 (partial: doesn't cover cross-platform) | No I/O in Core; sorting by SsKey | 44 tests | **Bucket A modulo cross-platform extension** |
| L3-S2 (DACPAC round-trip) | T1 binary + T11 | `ArtifactByKind` keyset; DacFx symmetry | `DacpacRoundTripTests` | Bucket A modulo A37 erasure declaration |
| L3-S3 (SSDT/DACPAC agreement) | T11 | `ArtifactByKind` smart constructor | `SiblingEmitterContractTests` | Bucket A |
| L3-S4 (Trigger definitions) | — | — (gated on A.0') | — | **Bucket D (Tier 1) until A.0' lifts** |
| L3-S5 (Sequence definitions) | — | — (gated on A.0') | — | **Bucket D (Tier 1)** |
| L3-S6 (DEFAULT values) | — | — (gated on A.0') | — | **Bucket D (Tier 1)** |
| L3-S7 (Computed columns) | — | — (gated on A.0') | — | **Bucket D (Tier 1)** |
| L3-S8 (CHECK constraints) | — | — (gated on A.0') | — | **Bucket D (Tier 1)** |
| L3-S9 (ExtendedProperties) | — | — (gated on A.0') | — | **Bucket D (Tier 2)** |
| L3-S10 (Catalog coordinates) | — | — (gated on A.0') | — | **Bucket D (Tier 2)** |

**Critical pattern**: L3-S4 through L3-S10 are *all* gated on the A.0' IR-fidelity workstream. Until that workstream ships, V2 can pass T1 byte-determinism for the kinds it carries while silently dropping kinds V1 has but V2 doesn't represent. This is L3-Boundary-NoSilentDrop's underwriting — Campaign A.2 makes this axiom enforceable.

### 10.2 Data concern coverage

| L3 axiom | L2 | L1 commitment | Test | Status |
|---|---|---|---|---|
| L3-D1 (CDC silence on redeploy) | partial T1 + topo idempotence | — | `CdcSilenceTests` in flight | **Bucket D (Tier 1)** — promote to formal axiom (Campaign A.3) |
| L3-D2 (static-seed deterministic order) | T1 + D12 | Canonical sort in `Config.validate` | `StaticSeedsEmitterTests` | Bucket A |
| L3-D3 (migration PK determinism) | T1 + D5 | — (gated on A.3) | partial | **Bucket B** |
| L3-D4 (topological order FK-safe) | A25 + A33 | `TopologicalOrderPass` | `TopologicalOrderTests` | Bucket A |
| L3-D5 (cycles via allowlist) | A33 + A40 | `SelfLoopPolicy` | `CycleResolutionTests` | Bucket A |
| L3-D6 (User FK reflow totality) | partial | `UserFkReflowPass` | `UserFkReflowPassTests` | Bucket B |
| L3-D7 (StaticData schema-validated) | — | — (gated on A.3) | — | **Bucket D (Tier 2)** |
| L3-D8 (MigrationDeps schema-validated) | — | — (gated on A.3) | — | **Bucket D (Tier 2)** |
| L3-D9 (Bootstrap covers remaining) | — | partition assertion | `DataEmissionComposerTests` | Bucket B |
| L3-D10 (emission gates) | — | — (gated on A.2) | — | **Bucket D (Tier 2)** |

### 10.3 Identity concern coverage

| L3 axiom | L2 | L1 commitment | Test | Status |
|---|---|---|---|---|
| L3-I1 (SsKey stable under rename) | A1 | `SsKey` DU + `Kind.SsKey` required | `TableRenameTests` (**shipped 9d578cc**) | Bucket A |
| L3-I2 (OssysOriginal survives) | A1 bound | `SsKey.ossysOriginal` | `IdentityTests` | Bucket A |
| L3-I3 (synthesis deterministic) | T1 | `SsKey.synthesizedComposite` | `SsKeyTests` | Bucket A |
| L3-I4 (RefactorLog records history) | partial T9 | `RefactorLogEmitter` | `RefactorLogEmitterTests` | Bucket B |
| L3-I5 (V1Mapped identity) | A1 | `SsKey.fromV1` | partial | Bucket B |
| L3-I6 (identity stable across evolution) | A1 + A4 | — | gated | Bucket B |
| L3-I7 (Name ⊥ SsKey) | A2 + A3 + A15 | distinct types | `NameStabilityTests` | Bucket A |
| L3-I8 (SsKey uniqueness in Catalog) | A39 | `Catalog.create` check | `CatalogCreationTests` | Bucket A |
| L3-I9 (Attribute identity persists under column rename) | partial | — (gated on 3.2) | — | **Bucket D (Tier 2)** |
| L3-I10 (cross-catalog reference identity) | — | — (gated on A.0') | — | **Bucket D (Tier 2)** |

### 10.4 Diagnostics concern coverage

| L3 axiom | L2 | L1 commitment | Test | Status |
|---|---|---|---|---|
| L3-X1 (every decision → LineageEvent) | A25 | signature `Lineage<_>` | implicit | Bucket B |
| L3-X2 (Opportunity routing) | partial | `Routing.route` | `OperationalDiagnosticsRoutingTests` | Bucket B |
| L3-X3 (severity matches consequence) | discrete-rationale DU | — | gated | **Bucket D (Tier 2)** |
| L3-X4 (lineage earliest-first) | A24 | `Lineage.bind` | `LineageTests` | Bucket A |
| L3-X5 (remediation guidance) | — | — | gated | **Bucket D (Tier 2)** |
| L3-X6 (detection traceable) | A23 | LineageEvent.SsKey | partial | Bucket B |
| L3-X7 (silent V1 drops documented) | — | — | gated | **Bucket D (Tier 2)** — partial overlap with L3-Boundary-NoSilentDrop |
| L3-X8 (tolerance taxonomy logged) | R6 + Tolerance | — | gated | Bucket B |
| L3-X9 (config errors structured) | D9 | `Config.parse` | `ConfigTests` | Bucket A |
| L3-X10 (multi-env policy diffs) | — | — | gated | **Bucket D (Tier 2)** |

### 10.5 Cutover safety concern coverage

| L3 axiom | L2 | L1 commitment | Test | Status |
|---|---|---|---|---|
| L3-C1 (canary V1≈V2) | R6 | governance | `CanaryDeployTests` | Bucket B (governance partly outside code) |
| L3-C2 (T-30/T-15 fallback) | T-30/T-15 gates | governance | — | Governance-only |
| L3-C3 (V2 doesn't write during dual-track) | R6 | governance | `ReadSide` tests | Bucket B |
| L3-C4 (per-pair transition) | R6 | governance | — | Governance-only |
| L3-C5 (canary ≥30 + ≥4) | chapter 3.4 | — | `GeneratorScaleTests` | Bucket B |
| L3-C6 (CDC redeploy silence) | partial | — | in flight | **Bucket D (Tier 1)** — co-located with L3-D1 |
| L3-C7 (canonical ordering) | D12 | `Config.validate` sort | `TableRenameTests` (D12 property) | Bucket A |
| L3-C8 (no credentials in config) | D9 | parser rule | `ConfigTests` | Bucket A |
| L3-C9 (dry-run completeness) | V2_DRIVER ladder | governance | — | Governance-only |
| L3-C10 (V1 sunset gating) | R6 + V2_DRIVER | governance | — | Governance-only |

### 10.6 Cross-cutting concern coverage

| L3 axiom | L2 | L1 commitment | Test | Status |
|---|---|---|---|---|
| L3-CC1 (Π contract) | T11 + S0.B | `ArtifactByKind` | `SiblingEmitterContractTests` | Bucket A |
| L3-CC2 (Pass contract) | Pass return-type codification | signature | implicit | Bucket B (signature-enforced; no structural type alias) |
| L3-CC3 (lineage monotonic) | A24 | `Lineage.bind` | `LineageTests` | Bucket A |
| L3-CC4 (IR fidelity for production) | partial (A.0') | — | gated | **Bucket D (Tier 1)** |
| L3-CC5 (perf regression gates) | bench protocol | `scripts/perf-gate.sh` | gating CI | Bucket B (operational, not type-enforced) |
| L3-CC6 (domain-first naming) | pillar 8 | code review | partial | Bucket B (operationally enforced) |

---

## Part XI — Methodology Reflections

### 11.1 What the triangulation revealed

The most important meta-finding: V2's structural posture is **strongest at the algebra's interior and weakest at its boundary**. Identity, lineage, deterministic ordering, sibling-Π commutativity — these are full-stack covered. Boundary contracts (atomic emission, manifest matches disk, lossless parsing) are silent dependencies. Operational evolution (config backward-compat, diagnostic-code stability) is silent. Compositional reasoning (pass commutation) is under-tested.

This is a recognizable pattern: a codebase with strong type-system discipline at its core tends to develop blind spots at its boundaries because boundary code is closer to "real world inputs" and gets coded defensively against runtime concerns rather than structurally. The fix isn't to make the boundary as structurally-tight as the interior — that's not always possible — but to *name* the boundary axioms and *test* them, even when the test must be operational rather than type-level.

### 11.2 What the audit can't catch

Three categories of property the audit doesn't address:

**Performance properties** beyond the bench-driven gating. "V2 finishes a full extract+emit in under X seconds on a 300-table catalog" is an L3-level property that depends on Phase B's port and isn't testable today.

**Multi-version property stability**. "V2 v2.1 produces the same output as v2.0 for the same input" requires having a v2.1 to test against; it's a future-facing property that develops as V2 versions accumulate.

**Semantic equivalence of generated SQL**. Two SSDT outputs that are byte-different but semantically equivalent (e.g., `CREATE TABLE [dbo].[X]` vs `CREATE TABLE [DBO].[X]`) need DacFx-level semantic equality, which the current canary partly addresses but not exhaustively.

### 11.3 How to maintain the triangle going forward

Three disciplines that should attach to the triangle:

**Chapter-close ritual extension**: every chapter close adds an L3 audit step — list the L3 axioms the chapter's work touched; confirm coverage in each bucket; flag any new Bucket-D gaps the chapter introduced.

**Per-PR L3 review**: PRs that touch boundary code or add config/CLI surface area get a checklist: which L3 axioms does this touch? Are they Bucket A or below? Does this PR strengthen or weaken the structural commitment?

**Annual re-audit**: every ~12 months, dispatch a fresh round of agents (top-down + bottom-up + adversarial) to refresh the catalog. The bucket boundaries will drift as V2 evolves; the audit cadence prevents the triangle from becoming stale.

---

## Part XII — Addenda (append-only)

This section accumulates references to subsequent work that builds on this audit. Each addition is a one-paragraph entry with a date and a link to the underlying commit / document.

### 12.1 Campaign A close — partial (Slice A.7.1 shipped 2026-05-12)

**Scope delivered**: A.7.1 (Atomic emission) — the first net-new Campaign A workstream from Phase A's incremental cost surface.

**L3 axiom promoted**: `L3-Boundary-AtomicEmission` from Bucket D (unnamed candidate) to Bucket A (full structural enforcement). The axiom now lives in `PRODUCT_AXIOMS.md` Group Boundary with formal `*Underwriting*` / `*Realization*` / `*Property tests*` metadata; no longer a candidate.

**Realization**:
- `Projection.Pipeline.Compose.write` refactored from `string list` return to `Result<string list>`. New mechanism: write all artifacts to a sibling staging directory (`<parent>/.<basename>.staging-<guid12>`); on success, delete pre-existing `outputDir` and `Directory.Move(staging, outputDir)`; on any failure, `safeCleanupStaging` removes the staging directory and `outputDir` is unchanged. The non-atomic window is the single rename call — POSIX-atomic on the same filesystem volume.
- New testable seam: `Compose.FileWriter = string -> string -> unit` with `Compose.writeWith : FileWriter -> outputDir -> Outputs -> Result<string list>`. Default writer = `File.WriteAllText`; tests inject `failAfterNWrites` to verify the post-failure invariant without disk-level fault injection.
- Test scaffolding: `ComposeAtomicWriteTests` (new file; 7 tests). Coverage: happy path artifact accounting, success-path no-staging-leak, induced-failure-from-absent-outputDir, induced-failure-with-sentinel-content (byte-identical preservation), failure-on-very-first-write, induced-failure no-staging-leak, replace-not-merge semantics.

**Signature change ripple**: `Compose.run` and `Compose.runWithConfig` now bind `Compose.write`'s `Result` directly (no longer wrap a synchronous `string list` in `Result.success`). `EndToEndPipelineTests.``M1: Compose.write writes the same bytes Compose.project produced``` updated to destructure the new `Result`.

**Test result**: 1128 passed / 0 failed (1121 baseline + 7 new atomic-write tests; zero regressions).

**Blast radius observed vs. estimated**: estimated 3-5 days for A.7.1; observed under 1 day. Estimate accuracy explained by the refactor being mechanically simple (the staging pattern is well-known) and the test seam being already-clean F# idiom (function-injection rather than mock framework).

**Surprises**: F# nullness-warnings-as-errors required explicit `null` arms in two pattern matches against `Path.GetDirectoryName` / `Path.GetFileName` (both return `string | null` under .NET 9 nullable-reference-types). Caught at compile time; trivial fix; reinforces the value of `TreatWarningsAsErrors=true` at the boundary.

**Remaining Campaign A work** (still pending operator direction or in-flight):
- **A.7.2 — L3-Boundary-ManifestMatchesDisk**: signature change to `ManifestEmitter` to consume the `Compose.write` path list rather than the in-memory `Outputs`. Blocked: needs A.7.1 (done); ready to start.
- **A.0' — L3-Boundary-NoSilentDrop**: the IR-fidelity workstream; closes when every concept in `V2_PRODUCTION_CUTOVER.md` §3.3 has either a typed `Catalog` field or a structured-error path at the OSSYS adapter boundary.
- **A.8 — L3-Idempotence-OnRedeploy**: co-located with chapter 4.1.B (CDC-silence canary), in flight.

**R-dissolutions observed**: none net-new from A.7.1 itself; the slice didn't structurally eliminate any open risk in §9 of the cutover plan. (Atomic emission was an unnamed-axiom blocker, not a named-risk blocker — those classes are disjoint by definition.)

**Commit**: `4e3d944` — `Pipeline.fs` refactor + `ComposeAtomicWriteTests.fs` (new) + `EndToEndPipelineTests.fs` (Result destructuring) + doc surface updates across `PRODUCT_AXIOMS.md`, this audit, and `V2_PRODUCTION_CUTOVER.md` §13.3. Subsequently the active PR is [#538](https://github.com/danielbdyer/outsystems-ddl-exporter/pull/538) on branch `claude/audit-v1-v2-sidecar-7Ifij`.

### 12.2 (placeholder) Campaign B close

Same template.

### 12.3 (placeholder) Campaign C close

Same template.

### 12.4 (placeholder) Operator's "document of key evolutions" integration

When the operator delivers the evolutions document (R1 in `V2_PRODUCTION_CUTOVER.md`), append: which L3 axioms in this document are confirmed / refined / replaced; which campaign sequencing changes; whether new core concerns (e.g., a sixth concern for UAT-users) are added.

### 12.5 (placeholder) Re-audit findings

When the next round of agents is dispatched (per Part XI.3's annual cadence), append the new findings here.

---

## Appendices

### Appendix A — Complete L3 axiom list with stable IDs

(All 56 axioms in Part III, sorted by ID. For ease of cross-reference; same content as Part III.)

```
Schema:           L3-S1 through L3-S10
Data:             L3-D1 through L3-D10
Identity:         L3-I1 through L3-I10
Diagnostics:      L3-X1 through L3-X10
Cutover safety:   L3-C1 through L3-C10
Cross-cutting:    L3-CC1 through L3-CC6
```

### Appendix B — Complete L2 axiom list with coverage rating

(Reference list of A1–A40 + T1–T11 with bucket assignments; same content as Part IV.)

```
Bucket A (full coverage):    A1, A2, A4, A5, A18a, A23, A24, A26, A33, A34, A35, A36, A38, A39, A40, T1, T4, T11
Bucket B (convention L1):    A3, A6, A7, A12a, A13, A14, A15, A16, A25, A28*, A35, A36
                              (*A28 reclassified to Bucket A per Part IV.2 note)
Bucket C (weaknesses):       A8, A9, A10, A11, A17, A19, A20, A21, A22, A27, A29, A30, A31, A32, A37,
                              T2, T3, T5, T6, T7, T8, T9, T10
```

### Appendix C — Complete L1 commitment inventory

(See Part V for the full inventory by category.)

### Appendix D — Complete gap-hunt question list

(See Part VIII for Q1–Q30 with full failure modes.)

### Appendix E — Cross-reference index

Key file locations cited throughout this document:

| Concept | File:line |
|---|---|
| `SsKey` four-variant DU | `Projection.Core/Identity.fs:70-118` |
| `Name` smart constructor | `Projection.Core/Catalog.fs:28` |
| `TableId` definition + smart constructor | `Projection.Core/Coordinates.fs:142, 168` |
| `SchemaName` / `TableName` / `ColumnName` typed VOs | `Projection.Core/Coordinates.fs:46-58, 77, 103, 129` |
| `Catalog.create` (A39) | `Projection.Core/Catalog.fs:414-491` |
| `Module.create` | `Projection.Core/Catalog.fs:355-376` |
| `Lineage<'a>` + custom equality | `Projection.Core/Lineage.fs:218-232` |
| `TransformKind` + `PhysicalRename` (post-Slice-1) | `Projection.Core/Lineage.fs:160-200` |
| `ArtifactByKind` smart constructor | `Projection.Core/ArtifactByKind.fs:69-80` |
| `CatalogDiff.between` (A38) | `Projection.Core/CatalogDiff.fs:76+` |
| `TopologicalOrderPass.runWith` | `Projection.Core/Passes/TopologicalOrderPass.fs` |
| `TableRename.run` (Slice 1) | `Projection.Core/Passes/TableRename.fs` |
| `Config.parse` + D9 guardrail | `Projection.Pipeline/Config.fs` |
| `RenameBinding.fromConfig` | `Projection.Pipeline/RenameBinding.fs` |
| `Compose.runWithConfig` | `Projection.Pipeline/Pipeline.fs` |
| Strategic docs | `sidecar/projection/VISION.md`, `V2_DRIVER.md`, `V2_PRODUCTION_CUTOVER.md`, `AXIOMS.md`, `CLAUDE.md`, `DECISIONS.md` |

### Appendix F — Glossary

| Term | Meaning |
|---|---|
| **L0** | Level zero — code; the F# files in `sidecar/projection/src/` |
| **L1** | Level one — structural commitments: smart constructors, closed DUs, value objects |
| **L2** | Level two — formal axioms in `AXIOMS.md` (A1–A40, T1–T11) |
| **L3** | Level three — product axioms: end-to-end verifiable claims operators trust V2 to satisfy |
| **Bucket A** | Full coverage: L3 ✓ L2 ✓ L1 ✓ test |
| **Bucket B** | Convention-enforced: L3 ✓ L2 ✓ test ✓, L1 by convention only |
| **Bucket C** | Weakness: untested, hidden, aspirational, deferred, subsumed, scope boundary, or partial structural |
| **Bucket D** | Unnamed axiom: L3 property exists, no L2 backing, no L1 commitment |
| **Π** | Projection / emitter (the Greek letter pi) — a function from Catalog (and possibly Profile) to an output surface |
| **E** | Enrichment — passes that produce values consumed by Π |
| **canary** | The V1↔V2 round-trip comparison harness; gates PR merges during dual-track |
| **dual-track** | The cutover window during which V1 and V2 both run; R6 governs split-brain elimination |
| **V2-augmented** | A cutover ladder rung where V1 drives the production write path and V2 verifies via canary |
| **V2-driver** | The destination ladder rung where V2 owns the production write path; gated per (env, artifact) pair on N=10 green runs + operator sign-off |
| **A.0', A.0' workstream** | Phase A.0' of the cutover plan — the IR fidelity lifts (trigger definitions, sequences, temporal tables, DEFAULT values, computed columns, CHECK constraints, ExtendedProperties, descriptions, IsExternal/Origin mapping, IsActive flags, Catalog coordinate) |
| **R6** | Split-brain governance rule per `DECISIONS 2026-05-22`: V2 emits but doesn't ship during dual-track; canary asserts V1 ≈ V2 |
| **D9** | Decision 9 in `V2_PRODUCTION_CUTOVER.md`: connection strings outside config; secret-free by construction |
| **D12** | Decision 12: canonical sort on user-supplied collections at config-validation time |
| **slice** | A discrete unit of work in the cutover plan; typically one PR; closes with a CHAPTER_*_CLOSE.md note |
| **chapter** | A coherent body of work spanning multiple slices; opens with CHAPTER_*_OPEN.md, closes with CHAPTER_*_CLOSE.md |

---

**End of audit document.** This is the integrated view at 2026-05-12. Subsequent work appends to Part XII; new findings re-open the relevant parts via dated amendments.
