# Audit — DDD / Hexagonal / FP review (Session 36)

**Status:** five-agent swarm dispatched and synthesized at session 36; first refactor batch landed at session 36 close; remainder rolls forward as named items in `CHAPTER_3_1_CLOSE.md`'s forward-signals section. This document preserves the audit's substantive output for cross-chapter reference. It is the canonical companion to `CHAPTER_3_1_CLOSE.md` for the audit dimension.

## Why an audit at chapter close

Per `DECISIONS 2026-05-19 — Chapter-mid-audit as a routine practice` (session 24 amendment), multi-session chapters dispatch a cross-document consistency audit subagent every 3–5 substantive sessions. Chapter 3.1 ran ten substantive sessions (27–36); the chapter-mid audits were lightweight (per-session); the chapter-close audit was a deeper architectural review.

The session-36 audit answered the operator framing: *audit the codebase against a very strong degree of architectural faith to hardcore domain-driven design, whose research then informs an audit for hardcore functional programming, with an eye for composition primitives along the ubiquitous-language spectrum of the domain in a hexagonal architectural implementation of it.*

Five agents were dispatched in parallel covering tightly orthogonal concerns:

| Agent | Lens | Cap |
|---|---|---|
| 1 | Ubiquitous language & bounded contexts | 25 findings |
| 2 | Hexagonal architecture (ports / adapters / dependency direction) | 22 findings |
| 3 | DDD aggregates / entities / value objects / invariants | 22 findings |
| 4 | FP composition primitives & algebraic structures | 22 findings |
| 5 | V1↔V2 anti-corruption layer fidelity | 18 findings |

Each agent classified findings as **B&W** (objectively a leak / drift, no design judgment needed) vs **SUBJ** (design choice with real tradeoffs), and ranked by leverage **H** / **M** / **L**. Agents reported with file:line precision.

## Convergence map (multi-agent confirmation = highest confidence)

The audit's most decisive findings appeared across multiple agents independently:

| Finding | A1 UL | A2 Hex | A3 VO | A4 FP | A5 ACL | Verdict |
|---|---|---|---|---|---|---|
| `TableId` value-object lift to Core; replace tuples + `PhysicalRealization` | ✓ #1, #2 | ✓ #19 | ✓ #1, #2, #3 | — | — | **3-axis B&W H** |
| Identity / synth-source / V1 vocabulary leaks Core | ✓ #3, #20 | — | ✓ #14 | — | ✓ F1, F2, F3, F18 | **3-axis B&W H** |
| Type-correspondence functions unowned (5 inverses across 3 projects) | ✓ #12, #13 | ✓ #4 | — | — | — | **2-axis B&W H** |
| Vocabulary collapse at SSDT boundary (`Restrict→NoActionSql`, `Name→string`) | ✓ #7, #10 | — | ✓ #4 | — | ✓ F5, F6 | **3-axis B&W H** |
| Declared ports unrealized (`Emitter`, `Compare`, `Render`, `Adapter`) | — | ✓ #2, #3, #8 | — | ✓ #2, #3, #12 | — | **2-axis B&W H** |
| `Catalog` / `Module` / `Reference` smart constructors absent | — | — | ✓ #10, #11, #12 | — | — | 1-axis **B&W H** |
| Pass drivers bypass `LineageDiagnostics.tellDiagnostics` | — | — | — | ✓ #1 | — | 1-axis **B&W H** |
| Sibling-Π divergence (JSON nests physical, Distributions inlines) | ✓ #6 | — | — | — | — | 1-axis **B&W H** |
| `Bench.persistJson` writes from Core | — | ✓ #1, #11 | — | — | — | 1-axis **B&W H** |
| `Lineage.Trail` violates A26 by construction (default `=` compares it) | — | — | ✓ #13 | — | — | 1-axis **B&W M** |

## Tier 1 — B&W high-leverage (objectively a leak)

These violate the project's own load-bearing commitments. No design judgment required.

1. **Schema-coordinate context (`TableId` lift)** — `(Schema, Table)` re-spelt 4× in `PhysicalSchema.fs` + once in `PhysicalRealization` + the `(string, string, string)` triple-keys in `ProfileSnapshot.fs:131` / `ReadSide.fs:184,221,264,303`; SSDT-local `TableId` in `Statement.fs:18`. Agents 1+2+3 multi-axis confirmed. **Closes Agent 2's `ICatalogReader` two-consumer threshold (Position B has fired).** ✅ session 36

2. **Pass-driver writer-fidelity** — `NullabilityPass:192`, `UniqueIndexPass:182`, `ForeignKeyPass:255` hand-build `{ Value = { Value = lineage.Value; Entries = entries }; Trail = lineage.Trail }` records. The `LineageDiagnostics.tellDiagnostics` API exists (`Diagnostics.fs:192-193`); three sites silently bypass it. ✅ session 36

3. **`RawTextEmitter` topological-sort harmonization** — re-implemented Kahn at `RawTextEmitter.fs:176–232`; `TopologicalOrderPass` already does this. A33 declares schema emission deterministic-ordered; A32 names passes-produce-values-for-emitters as the canonical channel. The duplication structurally erases both axioms. ✅ session 36 via `SelfLoopPolicy` parameterization.

4. **`Bench.persistJson` writes to disk from Core** — `System.IO`, `Directory.CreateDirectory`, `File.WriteAllText`, `JsonSerializer.Serialize` at `Projection.Core/Bench.fs:341–353`. Core's no-I/O claim is breached. ✅ **Cashed at chapter 3.6** — `BenchSink` port extracted; `Bench.persistJson` moved to `src/Projection.Pipeline/BenchSink.fs`. Core's no-I/O claim restored.

5. **`Adapter<'source, 'inner>` alias drags `Task` into Core** — `Types.fs:3,65` opens `System.Threading.Tasks` for the alias. Stage-0 reservation; only the `TypesTests.fs` reservation test referenced it. ✅ session 36 retired; bare task-shaped signature inlined at the test reservation site; Core no longer opens `System.Threading.Tasks`.

6. **Three `attach` adapters take `string` of JSON** — `Static.fs:174`, `ProfileSnapshot.fs:347`, `ProfileStatistics.fs:288`. Should mirror `CatalogReader.SnapshotSource` shape. Hidden ports. ⏸ deferred.

7. **Three Π emitters return `string` despite `Emitter<'element>` declared in Core** — `Types.fs:41–48` advertises `Emitter<'element> = Catalog -> Result<ArtifactByKind<'element>, EmitError>`; all three concrete emitters return `string`. The advertised shape is unimplemented. T11 is aspirational not structural. ⏸ deferred (rolls to chapter 3.5 as Π-port-realization slice; also unblocks T11-amended structural-type-encoding).

8. **Type-correspondence bounded context** — 5+ inverse functions: `mapSqlType` (`ReadSide.fs:51`) ↔ `columnSqlType` (`Render.fs:24`); `formatRawValue` (`ReadSide.fs:366`) ↔ `formatSqlLiteral` (`Render.fs:50`) ↔ `parseRaw` + `clrType` (`Bulk.fs:24,39`). Splits across 3 projects with no owning module — T1 byte-determinism rests on conventional inversion. ✅ **Cashed at chapter 3.7 slice β** — `Projection.Core.SqlTypeCorrespondence` bounded context consolidates the forward/inverse PrimitiveType ↔ SQL DDL vocabulary pair; round-trip property + 25 InlineData theory + Fact + property sweep covers the recognized vocabulary. `ReadSide.mapSqlType` becomes a 1-line alias. Plus: `Bulk` lives in `Pipeline` but is structurally `Adapters.Sql` concern — that sub-finding remains ⏸ open.

9. **`Catalog.create` / `Module.create` / `Reference.attach` smart constructors absent** — invariants live in passes; `Reference.SourceAttribute` may not exist on owning `Kind`; `RawTextEmitter.fkDef` silently drops on missing target. ✅ session 36 (`Catalog.create` enforces 5 invariants in one pass; back-compat record-literal preserved).

10. **`Restrict → NoActionSql` collapse silent and undocumented** — V1 codes "Protect" / "Ignore" both → `NoAction`; V2's `Restrict` unreachable from any V1 input. Three V1 inputs onto one V2 output; inverse undefined. ⏸ deferred (rolls forward — needs Diagnostics-emission scaffolding in CatalogReader).

11. **`SsKey.rootOriginal` renders V1 prefix in V2 emitter output** — asserted by `EndToEndPipelineTests.fs:128, 134-135` (`Assert.Contains "OS_KIND_AppCore_User"`). The "clean" V2 rendering surface re-renders V1 provenance as plain text. ⏸ deferred — needs DECISIONS amendment first (current behavior is documented commitment that emitter comments + differential tests depend on; reversing requires explicit superseding).

## Tier 2 — B&W medium-leverage

12. **`Lineage.Trail` violates A26 by construction** — F# default `=` compares Trail; A26 says trails are metadata not in equality. Two passes producing same `Value` with different `Trail` order compare unequal. ⏸ deferred — `[<CustomEquality>]` ripples to test assertions.

13. **`SsKey` DU variants embed V1 vocabulary** — `OssysOriginal of System.Guid`, `V1Mapped of v1Sskey × v2Namespace`. Future DACPAC reader can't produce these without semantic lying. ⏸ deferred (chapter-4.2 territory; User FK reflow uses `V1Mapped`).

14. **`Origin` DU variants are V1-product names** — `OsNative` / `ExternalViaIntegrationStudio` are V1-prescriptive ("OS" = OutSystems, "IntegrationStudio" is V1 product). Algebraic equivalents would be `Native | ExternalIndirect | ExternalDirect`. ⏸ deferred.

15. **Silent V1 drops without Diagnostics witness** — `module.isSystem`, `attributes[].default`, `refEntity_isActive`, inactive-records filter, Protect/Ignore collapse. ADMIRE-listed but silently dropped. ⏸ deferred (paired with item 10's Diagnostics-emission scaffolding).

16. **`ColumnProfile.NullCount > RowCount` reachable** — `NullabilityRules` divides by RowCount without checking the precondition. ✅ session 36 (`ColumnProfile.create` enforces `0 ≤ NullCount ≤ RowCount`).

17. **`ExternalDirect` Origin variant unreachable from production** — only `OsNative` / `ExternalViaIntegrationStudio` are reachable through the OSSYS path; `ExternalDirect` is dead but rendered by both Π's. ⏸ deferred (paired with item 14).

18. **`Lineage.bind` never called in `src/`** — writer monad's algebraic kernel dormant; passes terminate with `tellMany events (ofValue x)` (was 7 sites). Indicates the right primitive is **`Lineage.ofValueAndEvents` extraction** which session 36 landed; the rest of the API has earned consumer evidence to retire or activate. ⏸ deferred (re-evaluate at chapter 4 close).

19. **`Lineage / Diagnostics` parallel writer monads with different `Source` conventions** — `Diagnostics.Source: string` with `adapter:` / `emitter:` prefixes; `Lineage.PassName: string`. Same audit-trail concept, two unmerged primitives. ⏸ deferred (writer codification stability mark held at session 16; merging would re-open).

## Tier 3 — SUBJ high-leverage (judgment calls)

20. **Strongest candidate bounded contexts to name explicitly** (Agent 1 closing summary):
    - `Projection.Coordinates` — `SchemaName` / `TableName` / `ColumnName` / `TableId` / `ColumnId` / `FkCoord`. ✅ Stage 1 (`TableId`) shipped session 36; Stage 2 (typed `SchemaName` / `TableName` / `ColumnName`) deferred.
    - `Projection.TypeCorrespondence` — owns the 5 inverse functions (item 8). ✅ Cashed at chapter 3.7 slice β as `Projection.Core.SqlTypeCorrespondence`.
    - `Projection.Identity` (rename `Identity.fs`) with adapter-namespaces as data — `SourceTag` value object, registry of known tags per-adapter. ⏸ deferred (paired with items 11, 13, 14).
    - `Projection.Π` — sibling-emission shared contract (header, Origin label, modality label). ⏸ deferred.
    - `Projection.Fidelity` — `PhysicalSchema` + `RowDigester` + diff move out of `Core` (today they're Core-resident but never used by passes/strategies). ⏸ deferred.

21. **Strongest candidate ports to make explicit** (Agent 2 closing summary):
    - **`ICatalogReader`** — two-consumer threshold fired (`Osm.CatalogReader.parse` + `Sql.ReadSide.read`). ✅ Position B trigger documented; surface lift deferred to chapter 3.2.
    - **Real Π port** — three emitters satisfy the same shape (item 7). ⏸ deferred.
    - **`IArtifactSink`** — closes item 4 + cleans up the test harness's temp-dir dance. ⏸ deferred.
    - **`IDeployHost`** — wraps Testcontainers + warm-conn + executeStream behind one swappable interface. ⏸ deferred.
    - **`BenchSink`** — closes item 4 (paired). ✅ Cashed at chapter 3.6 — `Bench.persistJson` extracted to `src/Projection.Pipeline/BenchSink.fs`.

22. **Extract `Lineage.ofValueAndEvents`** (Agent 4 #4) — 7 consumers; threshold long crossed. ✅ session 36; 6 sites migrated.

23. **Extract `traverseCatalog : (Kind -> Lineage<Kind>) -> Catalog -> Lineage<Catalog>`** (Agent 4 #5) — 4 consumers hand-rolling mutable `ResizeArray<LineageEvent>` traversals. ⏸ deferred (chapter 4 evidence).

24. **Adopt `result { ... }` computation expression** (Agent 4 #9) — `ReadSide.fs:540–690` chains 4–5 deep, beyond the codebase's "bearable three steps" mark. ⏸ deferred (would adopt for `ReadSide` + adapter code; defer for Core where chains are still short).

25. **`PhysicalSchemaDiff` Monoid surface** (Agent 4 #8) — Position B today; second consumer arrives with M4 Tolerance taxonomy. ⏸ deferred until M4.

## Tier 4 — SUBJ medium-leverage

26. **Test-side V1 vocabulary leak** — Agent 5 F10. Test variables named `fkSourceEntityKey` for V2 keys. Local lets; not catastrophic. ⏸ deferred (cleanup-with-naming-discipline pass when next touched).

27. **`SnapshotSource` consistency across Sql adapters** — Agent 2 #10. OSM has it; Sql-side `attach` adapters don't. ⏸ deferred (paired with item 6).

28. **`AsyncStream` over-built** — Agent 4 #12. 9 of 11 combinators unused. Built ahead of evidence; respects two-consumer threshold by retraction once consumer evidence stabilizes. ⏸ defer-and-watch.

29. **Heavy V1-internal commentary in Core strategies** — Agent 5 F7/F8/F9. `ForeignKeyRules.fs` has 17+ V1 references (some without DECISIONS pointer). Per `CLAUDE.md` "Cite the canonical surface" — comments should reference axioms or DECISIONS, not raw V1 source. ⏸ deferred (cleanup pass).

## Disciplines codified by the audit itself

The audit produced new disciplines worth carrying forward:

1. **Multi-agent epistemic-tier protocol.** Five agents in parallel; each tags findings B&W vs SUBJ + H/M/L; synthesis tracks multi-axis confirmation count as a confidence signal. Codified at `DECISIONS 2026-05-?? — Five-agent DDD/hexagonal/FP audit protocol`.

2. **Convergence-map as the synthesis primary surface.** Multi-agent overlap signals high-confidence findings; single-axis findings still actionable but warrant more judgment. The convergence map IS the audit's headline output, not a footnote.

3. **Tier 1 / Tier 2 / Tier 3 / Tier 4 backlog discipline.** Tier 1 = B&W high-leverage (act without ceremony); Tier 2 = B&W medium; Tier 3 = SUBJ high-leverage (decisions for operator); Tier 4 = SUBJ medium-leverage. The tiers tell the operator *which* findings need their judgment before action.

4. **Spine-ordered refactor backlog.** Findings are not random; they compound. The spine names the ordered chain of refactors that earn each next move. Cuts delivery uncertainty: each step ships independently, each step earns the next.

## Closing

The audit's substantive value: ~30 findings, 10 acted-on at session 36 (the B&W subset that ships without architectural-judgment calls), ~20 carried forward as named items into chapter 3.5 / 4.1 / 4.2 with explicit pre-scope alignment.

The audit's meta-value: codified the multi-agent epistemic-tier protocol as a chapter-close ritual augmentation. Future chapters dispatch their close audit through this same shape; the convergence-map surface is structural, not narrative.

The audit confirmed the codebase's architectural disciplines are operating: the Tier-1 findings were *real leaks against stated commitments*, not cosmetic. The codebase's own framework (`F#-pure-core`, A18, T11, A26, two-consumer threshold) was the standard against which the audit measured. That self-consistency check is the codebase's load-bearing posture working as designed.

— The session-36 architect.
