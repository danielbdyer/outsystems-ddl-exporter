# DECISIONS

Append-only log of resolved questions during V2 development. Each entry
captures the decision, the reasoning, and (where applicable) the axiom or
theorem it serves. Future readers — including future agents and Danny —
should be able to reconstruct context from this log without spelunking
commit history.

Format:

    ## YYYY-MM-DD — short title
    **Status:** decided
    **Context:** ...
    **Decision:** ...
    **Reasoning / consequences:** ...

Entries are append-only. Earlier entries are preserved as historical
artifacts even when later entries refine or supersede them. Where an
earlier decision is amended, the amendment names the prior entry by date
and title rather than rewriting it.

---

## Active deferrals — index

Deferred decisions with explicit trigger conditions. The chapter-close
audit (session 12) found one trigger had fired silently (transform
registry, N=10 with N≥4 deferral); session 13 cashed it out and
introduced this index so the failure mode does not recur. Future agents
scan this section before committing to substantive work; if a trigger
has fired since the last review, log the cash-out entry below the
table before continuing.

| Deferral | First logged | Trigger condition | Status as of session 13 |
|---|---|---|---|
| **Composition primitive `fallback`** | 2026-05-13 (Composition vocabulary cash-out) | A second strategy returns "no decision" / Defer outcome and another picks up | 0 consumers |
| **Composition primitive `accumulate`** | 2026-05-13 (Composition vocabulary cash-out) | A second pass needs to consume multiple-strategy decisions at once | 0 consumers |
| **Composition primitive `wrap`** | 2026-05-13 (Composition vocabulary cash-out) | Per-strategy diagnostics emerge (likely tied to Diagnostics writer) | 0 consumers |
| **Composition primitive `lift`** | 2026-05-13 (Composition vocabulary cash-out) | A strategy reused across different IR granularities (e.g., Nullability rule on view columns) | 0 consumers |
| **Strategy registry mechanism** | 2026-05-11 (Strategy layer: a named architectural vector) | N≥4–6 strategies make name-keyed lookup useful | 6 strategy modules; no caller demands lookup by name |
| **Diagnostics writer** | 2026-05-06 (Diagnostics live in a writer parallel to Lineage) | First downstream artifact gates on operator-channel telemetry | **Cashed out — session 14 commit 3 landed `Projection.Core/Diagnostics.fs`. UniqueIndex opportunity stream activated as first consumer (session 14 commit 5). Three-channel split (operator/auditor/developer) remains deferred until a real consumer demands differentiation.** |
| **`RequireQualifiedAccess` retrofit** on `UniqueIndexKeepReason` / `ForeignKeyKeepReason` / similar | 2026-05-11 refinement 1 (Strategy-layer codification empirical verdict) | DU is next substantively modified | No modification since session 8 |
| **`CycleResolution.ResolutionStep.Reason` migration to structured DU** | 2026-05-11 (Strategy layer: a named architectural vector — caveat) | A second resolver strategy lands per the 2026-05-08 pluggability deferral | No second resolver; reason field still free-form string |
| **Cross-catalog FK detection IR refinement** (`Catalog : string option` on `Reference` and `ForeignKeyKeepReason.CrossCatalogBlocked` made reachable) | 2026-05-13 (Closed-DU expansion: empirical confirmation) | A fixture exercising cross-catalog FKs surfaces the gap | Reserved DU variant exists but is unreachable; do not delete |
| **Faker emitter (synthetic-data Π)** | 2026-05-13 (Session 11 reflection) | Either a third evidence type lands, or a use case forces proceeding with two evidence types and accepting the limitations | Two evidence types operational (Categorical, Numeric); no third in scope |
| **DacFx integration in `Projection.Targets.SSDT.DacpacEmitter`** | 2026-05-06 (DacFx integration deferred to first real-fixture milestone) | First real-fixture milestone arrives via the OSSYS catalog adapter | OSSYS catalog adapter itself not yet built (`CHAPTER_1_CLOSE.md §2.10`) |
| **Multi-spine state pattern** | 2026-05-06 (Multi-spine state pattern is endorsed but not yet built) | A real use case surfaces in the algebra | None yet |
| **Three-channel Diagnostics split** (operator / auditor / developer) | 2026-05-06 (Diagnostics live in a writer parallel to Lineage) | A real downstream consumer demands per-channel routing | Single channel sufficient at first consumer (UniqueIndex opportunity stream); deferred until host shell or telemetry consumer surfaces |
| **Reflection** (`typeof<>`, attribute scanning for plugin discovery) | Session 14 (CLAUDE.md, F# feature surface — consciously deferred) | A real consumer demands name-keyed strategy dispatch (paired with the strategy registry mechanism deferral above) | Closed-DU + typed-seam dispatches at compile time today; no reflective discovery needed |
| **Object expressions** (`{ new IInterface with ... }`) for adapter-side abstractions | Session 14 (CLAUDE.md, F# feature surface — consciously deferred) | V2 grows interface-based polymorphism (e.g., `IDiagnosticSink` for streaming consumers; `ICatalogReader` after a second catalog source materializes) | Codebase has zero interface boundaries today; all polymorphism via DU pattern matching |
| **Type providers** (`JsonProvider` for `osm_model.json`) | Session 14 (CLAUDE.md, F# feature surface — consciously deferred) | OSSYS adapter ships and JSON-shape evolution becomes a maintenance burden | OSSYS adapter starts with hand-written DTOs (per the ADMIRE stub); type-provider promotion is a later optimization |
| **`ICatalogReader` interface** (Position B → A) | 2026-05-13 (Anticipation vs. speculation in abstraction extraction) | A second catalog source materializes (DACPAC, OData, in-memory test reader unifying with OSSYS) | OSSYS adapter implementation chapter starts in Position B (`parse : string -> Task<Result<Catalog>>` shape); interface defers until second source |

**Discipline.** Each deferral here was logged as the right call **at the
time it was made** under "IR grows under evidence." A deferral is not a
TODO — the cash-out point is a structural condition, not a date. The
table tells future agents which conditions to monitor; the discipline is
to review the table when surveying CHAPTER_CLOSE-ranked priorities, when
adding a strategy or pass, and at chapter close. If a trigger has fired
silently between reviews, the audit-during-validation discipline expects
a cash-out entry before substantive work continues — that is the lesson
the transform-registry miss surfaced (`DECISIONS 2026-05-13 — Transform
registry cash-out`).

**Scope of the index.** This index lists **deferrals with explicit
re-open triggers** — both architectural (composition primitives, IR
refinements, registry mechanisms) and feature-surface (reflection,
object expressions, type providers consciously deferred per the
CLAUDE.md F# feature surface section). Both share the same shape: a
deferred decision with a structural condition that, when met, requires
a cash-out entry. The index does **not** list:

  - **Adoption-trigger candidates** from CLAUDE.md's "Aligned but
    underused" section (computation expressions, active patterns,
    units of measure). Those are aspirational adoption signals, not
    re-open obligations — adopting them is encouraged when the
    trigger fires but the trigger firing does not by itself demand
    a cash-out entry. They live in CLAUDE.md as guidance, not in
    DECISIONS as load-bearing.
  - **Out-of-scope-for-Core** features (Async/Task,
    MailboxProcessor, FRP). Those are scope rules, not deferrals —
    they are forbidden in Core regardless of demand and only land
    in adapters when the adapter's role demands them. They live
    in CLAUDE.md as scope guidance.

This distinction matters: the Active deferrals index is the list
the chapter-close audit must scan; aspirational guidance and scope
rules don't need that level of attention.

---

## 2026-05-06 — Sidecar lives at `sidecar/projection/` with its own solution

**Status:** decided
**Context:** The trunk's main solution is `OutSystemsModelToSql.sln`; the
sidecar must be cherry-pick friendly and leave trunk behavior unchanged
whether the sidecar is present or absent.
**Decision:** Place all sidecar code under `sidecar/projection/`. Create a
separate `Projection.sln` inside that folder. Do **not** add sidecar
projects to the trunk solution.
**Reasoning / consequences:** A separate solution is the cleanest way to
honor the cherry-pick discipline and keep the trunk's build/CI surface
untouched. Cost: developers must `cd sidecar/projection/` (or pass the
sidecar's `.sln` path) to build and test the sidecar.

## 2026-05-06 — F# is introduced for the algebraic core

**Status:** decided
**Context:** The trunk is 100% C#. The architecture spec mandates F# for
the pure functor pipeline so that "the source code reads as the formal
system" and discriminated unions are first-class.
**Decision:** F# for `Projection.Core` and all enrichment passes. C# for
adapter projects later (`Projection.Adapters`, `Projection.Host`). The
algebra/I-O boundary is also the language boundary.
**Reasoning / consequences:** The DU and pipeline-composition idioms in F#
make the algebra visible at call sites. C# adapter code stays minimal and
speaks .NET libraries (DacFx, Hot Chocolate, ASP.NET) natively. Cross-
language values are F# records and discriminated unions, consumable from
C#.

## 2026-05-06 — Property testing via FsCheck.Xunit

**Status:** decided
**Context:** The trunk uses xUnit but no property-based testing library.
The axioms list literally tells us what properties to write; we need a
property-based test framework.
**Decision:** Add `FsCheck.Xunit` to the test project. Hedgehog is nicer
ergonomically but FsCheck has deeper .NET tooling integration and a longer
track record.
**Reasoning / consequences:** One new test-time dependency. Production
dependencies remain unchanged.

## 2026-05-06 — Lineage shape: hand-rolled writer with versioned events

**Status:** decided
**Context:** Every pass runs inside a lineage monad. Library writers
(FSharpPlus etc.) introduce dependency surface and obscure the algebra.
Custom writer keeps it visible. Per A23, lineage events must include
`PassVersion` to make provenance hashes stable across pipeline evolution.
**Decision:**

```fsharp
type Lineage<'a> = { Value: 'a; Trail: LineageEvent list }
and LineageEvent = {
    PassName    : string
    PassVersion : int
    SsKey       : SsKey
    TransformKind : TransformKind
}
```

`bind f m` produces `{ Value = f(m.Value).Value; Trail = m.Trail @
f(m.Value).Trail }` — earliest-first, chronological (A24). The `>>=`
operator and the computation expression both follow this order.
**Reasoning / consequences:** Hand-rolled, no external deps. Versioning
guarantees replay determinism. Chronological order is documented in code,
in `AXIOMS.md` (A24), and tested.

## 2026-05-06 — SsKey as a sum type: Original vs Derived

**Status:** decided
**Context:** Some passes (e.g., symmetric closure) introduce nodes whose
identities are derived from existing identities. These derivations must be
deterministic and machine-checkable, not stringly conventional.
**Decision:**

```fsharp
type SsKey =
    | Original of string
    | Derived of original: SsKey * reason: string
```

The original/derived distinction is in the type system; later passes can
pattern-match on whether a key was synthesized. The string-concat
alternative (e.g., `"::inv::"` separator) is rejected as too easy to bypass
and too hard to reason about under composition.
**Reasoning / consequences:** Slight type-system overhead; large
correctness payoff. Reserved `reason` strings will be enumerated as a
`DerivationReason` DU when more than one is in use; for now we use string
literals registered in this document. First reserved reason: `"inverse"`
(symmetric closure pass).

## 2026-05-06 — Π_SSDT first emission target is raw .sql-style text

**Status:** decided
**Context:** Building real SSDT artifacts requires DacFx interop and a real
SQL Server target; that is heavy machinery for the first milestone.
**Decision:** For the synthetic-fixture milestone, Π_SSDT emits external-
table declarations as text (diffable, dependency-free).
**Forward-looking note:** When real SSDT artifacts are required, use
**DacFx** (`Microsoft.SqlServer.DacFx`), not SMO. SMO is for live SQL
Server administration; DacFx is the SSDT-native tooling for DACPAC
construction and schema comparison. SMO will get close enough to be
misleading. **Do not go down the SMO path** for SSDT.

## 2026-05-06 — Static populations live in the catalog

**Status:** decided (per spec; logged for visibility)
**Context:** The unfold pass for static kinds needs row data. Two options:
keep rows in the catalog, or maintain a parallel data layer.
**Decision:** Per A7, row data lives in the catalog. The Catalog Reader
populates `staticPopulation` when reading from real meta; the unfold pass
lifts populations into type-level metadata for Π.
**Caveat parked:** Real populations may eventually be large or carry non-
trivial values. The algebra is indifferent; the implementation may want
bounded loading later. Do not pre-solve.

## 2026-05-06 — PhysicalRealization starts as `{ Schema; Table }`

**Status:** decided
**Context:** External kinds may eventually point at different
databases/servers, view-backed entities may need view metadata, and
identity-column overrides may be needed.
**Decision:** For the first milestone, `PhysicalRealization = { Schema:
string; Table: string }`. Widen when evidence forces it.

## 2026-05-06 — Result and ValidationError are F# ports of trunk shapes

**Status:** decided
**Context:** The trunk has a well-shaped `Result<T>` + `ValidationError` in
`src/Osm.Domain/Abstractions/`. Algebraic purity benefits from F# DUs.
**Decision:** F# port: `type Result<'a> = Success of 'a | Failure of
ValidationError list`. `ValidationError` is a record with `Code` (e.g.,
`"sskey.empty"`), `Message`, `Metadata` map. `Bind`, `Map`, `Ensure`
operators preserved. Error codes follow the trunk's
`"category.subject.problem"` lower-dot convention.

## 2026-05-06 — V2 mandate (supersedes the sidecar framing)

**Status:** decided
**Context:** What was originally framed as a self-contained sidecar
experiment is elevated to V2 of the codebase. V2 is the foundation V1
will eventually orbit around via an admire-and-extract migration. The
folder path stays at `sidecar/projection/` to preserve cherry-pick
history; the conceptual frame upgrades.
**Decision:** The pure F# core under `sidecar/projection/` is V2's spine.
V1 (the existing C# implementation in the rest of the trunk) continues to
operate untouched. V2 is additive. Each V1 component will eventually be
admired (read carefully, categorized algebraically, placed) and extracted
(brought into V2 as a pure pass, an adapter at a port, or a split
between the two).
**Reasoning / consequences:** README is reframed. AXIOMS.md gains a V2
Amendments section (preserving the original A1–A31 / T1–T10 numbering).
ADMIRE.md is created as the append-only log of V1 admirations and their
V2 placements.

## 2026-05-06 — UAT-Users dual-mode collapses to "passes feed emitters"

**Status:** decided
**Resolves:** Q1 from the V2 handoff reply.
**Context:** V1's UAT-Users transform is dual-mode: discovery in Stage 3,
application in either Stage 5 (INSERT pre-transform) or Stage 6 (UPDATE
post-load). The masterwork frames this as a special case; the algebraic
spec was silent on it. The decomposition flagged it as a tension point.
**Decision:** Discovery is one E-pass producing a `UserRemapContext`
value attached to the enriched IR. Application is two sibling Π's: an
INSERT-mode Π that consumes catalog + context to emit pre-transformed
INSERTs, and an UPDATE-mode Π that consumes the context alone to emit
standalone CASE-WHEN UPDATEs. Both Π's read the same enriched IR; each
chooses which subset of attached values it consumes.
**Reasoning / consequences:** The dual-mode framing collapses to a
canonical algebraic principle: **passes may produce values consumed by
emitters, not just by other passes.** This becomes new axiom A32. It is
the canonical answer for any future "discover something at one stage,
use it at another" pattern.

## 2026-05-06 — Policy is three orthogonal axes

**Status:** decided
**Resolves:** Q2.
**Context:** The decomposition's three-dimensional decomposition
refinement (Selection / Emission / Insertion) and the masterwork's
bounded-context partitioning pointed at the same structure. The
algebraic spec had Policy as a single opaque aggregate.
**Decision:** Policy is a structured F# record with three named axes:

```fsharp
type Policy = {
    Selection : SelectionPolicy
    Emission  : EmissionPolicy
    Insertion : InsertionPolicy
}
```

Each axis is its own value type with its own validation. The three are
orthogonal: changing one does not constrain the others. AXIOMS.md
amendment to A12 records the structural commitment.
**Reasoning / consequences:** The three axes become the canonical place
to ask "where does this configuration belong?" Type system makes the
axes discoverable and resists drift.

## 2026-05-06 — Diagnostics live in a writer parallel to Lineage

**Status:** decided
**Resolves:** Q3.
**Context:** The masterwork prescribes three diagnostic channels
(operator / auditor / developer) orthogonal to domain logic.
**Decision:** `Lineage<'a>` remains the foundational, content-addressable,
constitutive provenance writer. A separate `Diagnostics<'a>` writer
(name TBD) carries human-consumable telemetry; near-term it is
single-channel, structured emission. The three-channel split arrives
when a real consumer asks for differentiated output.
**Reasoning / consequences:** Lineage and diagnostics have different
lifetimes, consumers, verbosity, and audiences. Conflating them would
either bloat lineage with operator-text or force diagnostics into
lineage's structural constraints.

## 2026-05-06 — Profile is a third substantive input

**Status:** decided — real algebraic amendment
**Resolves:** Q3.5.
**Context:** Both the decomposition and the masterwork demand profile
evidence (null counts, orphan FK rates, uniqueness violations) feed
policy decisions. The original "three aggregates, only three" framing
(A6) cannot absorb this — Profile is empirical evidence, distinct from
structural truth (Catalog) and operator intent (Policy).
**Decision:** Amend the algebra. The system has **three substantive
inputs** — Catalog, Policy, Profile — and **one temporal dimension** —
Lifecycle. Together they fully determine the projection.

```fsharp
type ProjectionInput = {
    Catalog : Catalog
    Policy  : Policy
    Profile : Profile  // may be Profile.empty for use cases needing no evidence
}
```

`E : (Catalog, Policy, Profile) → EnrichedCatalog`. Passes that do not
need profile evidence ignore it and behave identically as if the input
were `Profile.empty`. Passes that need it (the eventual nullability and
FK evaluators) accept it as a parameter.
**Reasoning / consequences:** A6 is amended. A12 (policy as data)
remains intact but composed alongside Profile. A17 (Project = Π ∘ E) is
amended to specify E's signature. T1 (determinism) extends to the
triple. New axiom A34 names Profile's independence from Catalog and
Policy. AXIOMS.md records all four amendments.

## 2026-05-06 — General names in the pure core; V1↔V2 mapping at the boundary

**Status:** decided
**Resolves:** Q4.
**Context:** V1 uses domain-prescriptive names (`EntityModel`,
`ModuleModel`, `OsmModel`) the masterwork calls "ontological law." The
algebra is source-agnostic and uses general names (`Kind`, `Module`,
`Catalog`).
**Decision:** Pure core uses general algebraic names. Boundary
adapters translate. The mapping is documented in the README and
preserved here for cherry-pick context:

| V1 (`Osm.Domain`)   | V2 (`Projection.Core`) | Notes                          |
|---------------------|------------------------|--------------------------------|
| `OsmModel`          | `Catalog`              | top-level aggregate            |
| `ModuleModel`       | `Module`               | coproduct cell                 |
| `EntityModel`       | `Kind`                 | the schema-level entity type   |
| `AttributeModel`    | `Attribute`            | scalar property                |
| `RelationshipModel` | `Reference`            | directional FK edge            |
| `EntityName`        | wrapped in `SsKey`     | identity, type-distinguished   |
| `TableName`         | `PhysicalRealization`  | physical projection            |
| `ProfileSnapshot`   | `Profile`              | empirical evidence             |

**On identity (`SsKey` vs `EntityName`):** V2 `SsKey` wraps whatever V1
supplies as canonical identity. For OutSystems, that is `EntityName` —
the logical name, stable across rename-to-database. It is **not**
`PhysicalTableName`, which can change when the database is migrated.
The principle is portable: identity is whatever survives the source's
most aggressive refactoring. When DACPAC support arrives, identity is
DACPAC's most stable identifier, wrapped in `SsKey`.
**Reasoning / consequences:** Algebra in the core stays clean. The
mapping is a forcing function for future translations: when DACPAC
support arrives, the question "how does DACPAC's `TableDefinition` map
to V2's `Kind`?" has a place to live. If the mapping grows large, it
graduates to its own `MAPPING.md`.

## 2026-05-06 — Transform registry deferred until N≥4 passes

**Status:** decided
**Resolves:** Q5.
**Context:** The masterwork prescribes a TransformRegistry with explicit
ordering constraints, discoverability, and startup validation. Today V2
has one pass.
**Decision:** Continue with `>>` composition for the next two or three
passes. Graduate to the registry when:
- composition with `>>` starts to feel fragile, or
- ordering rationale begins accumulating in code comments rather than
  in types, or
- `N` (number of passes) reaches four.

Migration when it arrives is mechanical: each pass becomes a
`RegisteredTransform`, ordering constraints get declared, the
composition site changes from `pass1 >> pass2 >> pass3` to a registry
configuration.
**Reasoning / consequences:** The registry's complexity earns its keep
when the load is there. Until then, `>>` composition is more legible.

## 2026-05-06 — Schema vs Data ordering law promoted to A33

**Status:** decided
**Context:** Masterwork §14 (lines 946–956) prescribes that schema
emission uses deterministic (alphabetical) ordering, while data emission
uses topological (FK-dependency-safe) ordering. The algebraic spec was
silent on this distinction.
**Decision:** Promote the rule to a V2 axiom (A33). The F# type system
encodes it: a schema-emission configuration cannot accept a topological-
order input, and vice versa. AXIOMS.md records the law. Implementation
arrives when the first emission pass that takes ordering as input does.
**Reasoning / consequences:** Schema artifacts must produce reproducible
diffs (alphabetical ordering survives every refactor). Data artifacts
must respect FK constraints (topological ordering prevents reference
violations). Mismatching the two is a class of subtle bugs the type
system can prevent at compile time.

## 2026-05-06 — Multi-spine state pattern is endorsed but not yet built

**Status:** decided (deferred implementation)
**Context:** Masterwork §15 (lines 795–949) prescribes multiple typed
"spines" (`ExtractionSpine`, `SchemaSpine`, `FullPipelineSpine`) so that
different use cases consume only the stages they need, without bloating
one mega-state.
**Decision:** Bless the pattern as the V2 framing for use-case-specific
state types. Build spines as evidence demands — not in the next handful
of commits, but when the algebra needs to express a use case where
"there is no Profile" or "there is no Apply" is a structural fact.
**Reasoning / consequences:** Avoids type bloat. Allows the type
system to prevent illegal compositions (an extract-model spine cannot
reach Apply). Held against premature spine-explosion: build one spine
first; introduce a second when the first one starts collecting optional
fields that are mandatory only in one mode.

## 2026-05-06 — Built-ins first; no hand-rolled serialization

**Status:** decided (operating discipline)
**Context:** Session 2 introduced `Projection.Targets.Json.JsonEmitter`
as a hand-rolled string-concat serializer. This was a shortcut: F# has
multiple real JSON options (built-in `System.Text.Json.Utf8JsonWriter`,
`FSharp.SystemTextJson`, `Thoth.Json.Net`), and reinventing serialization
adds risk (escaping, encoding, ordering corners) without algebraic
benefit. Caught and called out in review.
**Decision:** Default to built-in libraries for I/O / serialization /
parsing concerns. Hand-rolling is justified only when the algebra
demands a representation no library exposes (e.g., a deliberate canonical
text form for visual diffing, like the SSDT raw-text emitter — see the
adjacent decision). When in doubt, log the choice in DECISIONS.md
*before* writing the code.
**Reasoning / consequences:** The pure core stays small. Library code
absorbs the corners we don't need to own. Future agents read this entry
and avoid the same shortcut. The discipline is named explicitly so it
can be invoked on review without re-litigating each instance.

## 2026-05-06 — Π_Json now uses System.Text.Json.Utf8JsonWriter

**Status:** decided
**Resolves:** the hand-rolled JSON regression flagged in review.
**Context:** `JsonEmitter.fs` shipped in commit 5 as hand-rolled string
concatenation with bespoke UTF-8 escaping.
**Decision:** Rewrite using `System.Text.Json.Utf8JsonWriter` (built-in
to .NET, no third-party dep). Property order is the order of writes
(stable). Pretty-print uses `Indented = true` plus an explicit
`NewLine = "\n"` so output is byte-deterministic across platforms (T1).
**Reasoning / consequences:** Less code, fewer bugs, no escaping
corners we don't want to own. `JsonEmitter.version` bumped to 2 to
distinguish hand-rolled (v1) from library-backed (v2) output in any
already-cached snapshots. Existing tests survive the format change
because they assert structural properties (parseable, contains roots,
modality is a JSON array) rather than byte-exact form.

## 2026-05-06 — DacFx integration deferred to first real-fixture milestone

**Status:** decided (refines the 2026-05-06 — Π_SSDT decision)
**Context:** The same review that flagged hand-rolled JSON asked why
Π_SSDT does not use DacFx now. Honest answer: Π_SSDT raw text is the
algebraic claim made human-legible (T1 byte-determinism is eyeballed; T8
diffability of snapshots is read directly). DacFx-built `.dacpac`
artifacts are zip archives of XML schemas: legible only through DacFx
itself. The raw-text emitter is doing real work as a debug oracle.
**Decision:** Keep `Projection.Targets.SSDT.RawTextEmitter` as a
debug / diff-oracle sibling Π. When the first real-fixture milestone
arrives (session 3+, when the C# Catalog Reader admits real OutSystems
metadata), add `Projection.Targets.SSDT.DacpacEmitter` as a third sibling
Π built on `Microsoft.SqlServer.DacFx`. Tests at that point assert
sibling-functor commutativity (T4 / T11) across all three: same
enriched IR ⇒ identity-consistent surfaces in raw text, JSON, and
DACPAC bytes.
**Reasoning / consequences:** No premature dependency on DacFx; no
giving up the human-readable diff oracle. The migration is additive —
real-fixture work introduces DacFx alongside, not replacing, the raw-
text emitter.

## 2026-05-07 — Contract testing is the V1↔V2 bridge

**Status:** decided (operating discipline)
**Context:** V1's existing tests encode behavioral contracts empirically
— "given X, the implementation produces Y." When V2 implements
equivalent functionality through F# passes and Π emitters, those tests
become the validation that the migration is faithful. The algebra
explains *why* the tests should pass; the tests confirm V1 and V2 are
equivalent compositions on the migrated subset.
**Decision:** Every V1→V2 migration uses one or more of three contract-
testing forms:

1. **Differential / golden-file.** Run V1 and V2 against shared
   fixtures; compare outputs. Strongest possible evidence; appropriate
   when the output shapes match (e.g., both emit textual SQL).
2. **Property-based.** Lift V1's example-based assertions into
   universally-quantified F# properties. Both implementations are
   obligated to satisfy them; FsCheck runs against V2 and accumulates
   confidence with every fuzz case. The right form for invariants like
   "deterministic", "idempotent", "FK target precedes source."
3. **Behavioral re-expression.** Some V1 tests test C# specifics
   (mocking, class-level concerns) that don't translate cleanly. These
   get rewritten in F# against V2's API; behavior preserved, encoding
   shifts.

When V1 and V2 disagree, the divergence is diagnostic, not a failure
mode. Three possibilities:

- V2 is wrong → fix V2.
- V1 was buggy → V1's test was encoding a bug; V2 corrects it; the
  test is updated and the divergence is logged here as an improvement.
- V2 is intentionally different → V2 made an explicit algebraic
  refinement V1 didn't have; the test is updated and the divergence is
  logged here.

**Reasoning / consequences:** Migration becomes constructive: "did we
migrate this correctly?" has a yes/no answer. ADMIRE.md gains an
"Existing test coverage" section per entry, listing each V1 test, what
it asserts, and which form translates it into V2. The discipline is
named here so reviewers can invoke it without re-litigating each
migration.

## 2026-05-07 — IR grows under evidence, not speculation

**Status:** decided (operating discipline)
**Context:** Several V2 commits will refine the IR: `IsPrimaryKey` on
`Attribute` (justified by the `EntitySeedDeterminizer` admire); future
`IsForeignKeyTarget` or richer `Reference` shape (likely justified by
`EntityDependencySorter` admire); column-level metadata (computed,
default expression) when admire passes surface real fixtures that need
them.
**Decision:** The IR grows when an admire pass surfaces a structural
need from a V1 component being migrated, OR when a property test
discovers an invariant the IR cannot currently express. It does **not**
grow speculatively — "we might want this someday" is not a justification.
Every IR refinement carries a comment naming the admire entry or test
that motivated it.
**Reasoning / consequences:** The IR stays small. Future readers can
read every field's justification by following the comment back to the
ADMIRE entry or test. Speculative complexity has nowhere to land.

## 2026-05-07 — IR refinement: `IsPrimaryKey` on `Attribute`

**Status:** decided
**Refines:** the IR shape introduced in commit 4 of session 1.
**Context:** The `EntitySeedDeterminizer` admire entry (ADMIRE.md, 2026-05-06)
identified PK-column knowledge as a structural prerequisite for the
extracted `NormalizeStaticPopulations` pass. The synthetic-milestone
`RawTextEmitter` was using a name-based hack (assume the PK attribute
is named "Id") to resolve FK target columns; that hack is now retired.
**Decision:** Add `IsPrimaryKey : bool` to `Attribute`. Composite primary
keys are expressed by flagging multiple attributes on the same kind.
`Kind.primaryKey` returns the PK attributes in declaration order.
**Reasoning / consequences:** First IR refinement under the
"IR grows under evidence" discipline — motivated by a concrete admire
pass and a concrete emitter need, not by speculation. The synthetic
fixture marks each Id attribute as the PK; the JsonEmitter surfaces
`primaryKey` alongside `nullable` for every attribute; the SSDT
RawTextEmitter tags PK columns with " PK" in the inline comment.

## 2026-05-08 — Contract testing surfaces V1 latent bugs as well as V1 intent

**Status:** decided (operating discipline; worked example)
**Context:** The contract-testing discipline (2026-05-07) framed V1
tests as oracles for V2 migration. A natural read of that framing is
"V2 must reproduce V1." The truth is more useful: contract testing
surfaces V1's *implicit* contracts as well as its explicit ones, and
some of the implicit ones are latent bugs.
**Worked example.** While preparing the second admire entry
(`EntityDependencySorter`, 2026-05-07), the scout surfaced that V1's
correctness depends on `Dictionary<K,V>` insertion-order iteration in
the CLR. That dependency is nowhere in V1's tests; it is a load-bearing
implementation detail no contract documents. A V2 property test
sweeping shuffled inputs catches the dependency and pins it as a real
V2 invariant: `TopologicalOrder.run is invariant under input
permutation`. The diagnostic moves the constraint from "implicit
behavior of the CLR happens to give V1 the result it wants" to
"V2 actively guarantees this." The result is V2 is more robust than V1
on this axis, by virtue of the discipline catching the gap.
**Decision:** Treat divergences from V1's *implicit* behaviors with
the same algebraic-conversation rigor as divergences from V1's
*explicit* tests. The three categories from the contract-testing
entry (V2 wrong / V1 buggy / V2 intentionally different) extend to
implicit contracts: when V2's property test surfaces a behavior V1
relied on but didn't assert, the question is the same — is V2's
codification a fix, a refinement, or a regression?
**Reasoning / consequences:** Contract testing is a dividend, not just
a cost. Future agents reading this entry see the discipline pay out,
not just impose a discipline tax. Worked examples accrue here as
sessions surface them.

## 2026-05-08 — Lineage events fire only on actual change

**Status:** decided (silent operating convention)
**Context:** The `namingMorphism` pass (session 2, commit 3) emits
`Renamed` lineage events only when the morphism produced a different
name; no-op morphisms produce empty trails. The convention reads in
code naturally and keeps lineage chains forensically meaningful —
every event is a real transformation, not noise from passes that
happened to run.
**Decision:** Adopt the convention silently for any pass that has a
no-op case. The pattern: a pass runs over every node, but emits an
event only when its work actually changed something. `namingMorphism`
is the template; future renaming-flavored passes (policy-driven
sanitization, schema-prefix injection, identifier collision
resolution) follow.
**Reasoning / consequences:** Lineage is provenance, not progress
reporting. A `Renamed` event in the trail means a name actually
changed; reading the trail is a forensic exercise, not a tally of
which passes ran. Passes that observe but don't transform (e.g.,
`canonicalizeIdentity`'s sweep) emit `Touched` events explicitly —
that's a different convention because the *act of observing* is
itself the contract.

## 2026-05-08 — Algebra/domain split: edge classification lives in CycleResolution

**Status:** decided
**Context:** Pre-commit audit on session-4 commits 4–6 (Kahn's, Tarjan's,
edge classification + asymmetric-2-cycle resolver). Commits 4 and 5 are
pure graph-theory algorithms with no V1 business logic. Commit 6's
`classifyEdge` and the resolver heuristic, however, are V1-flavored
domain rules — the `Weak | Cascade | Other` taxonomy and the
"break exactly one Weak edge in a 2-cycle" strategy are V1's
EntityDependencySorter conventions, lifted but not algebraically forced.
Embedding them inside `TopologicalOrderPass` mixed graph algebra with
domain interpretation.
**Decision:** Extract `EdgeStrength`, `classify`, and the resolver
strategies (`asymmetric2CycleStrategy`, `neverResolve`) into a new
`CycleResolution` module. The pass calls into `CycleResolution`; the
algebra (graph build, Kahn's, Tarjan's, "remove edges and re-sort") is
free of domain rules. The `CycleResolution.Resolver` type
(`SsKey list -> ((SsKey * SsKey) * EdgeStrength) list -> ResolutionStep`)
is the seam — call sites can pass any conforming function; the
algebra doesn't know which strategy is in use.
**Reasoning / consequences:** When V2 admits a non-RDBMS catalog or a
new resolver strategy (manual cycle overrides, MFAS, deferred
junctions), the new logic lands in `CycleResolution` (or a sibling
module) without touching the algebra. `TopologicalOrderPass` currently
passes `CycleResolution.asymmetric2CycleStrategy` as the resolver;
making it a pass-level parameter is deferred until the second resolver
strategy actually lands (per "IR grows under evidence"). The audit
itself is the kind of thing the discipline is designed to surface;
preserving the resulting algebra/domain split in code keeps the
algebra small.

## 2026-05-09 — Algebra/domain split pattern (generalizable)

**Status:** decided (operating discipline)
**Context:** Session 4 commit 6 split V1's edge classification and
asymmetric-2-cycle resolver out of `TopologicalOrderPass` into a
sibling `CycleResolution` module. The *shape* of that refactor is
generalizable; `EntityDependencySorter` is unlikely to be the only
place V1 entangled structural algorithm with domain rules.
**Decision:** Adopt the following canonical shape for any V1
component whose logic mixes graph algebra / structural transformation
with domain interpretation rules:

1. **Algebra in the pass.** The pass file in `Projection.Core.Passes`
   contains only the structural algorithm — graph traversal,
   composition, identity preservation, lineage emission. No domain
   rules; no V1-flavored interpretation of IR fields.
2. **Domain in a named module.** A sibling module (e.g.,
   `CycleResolution`, `NullabilityRules`, ...) named for the domain
   concern carries the V1 rules — taxonomies, classification
   functions, named strategies. The module name advertises that
   domain logic lives here, not algebra.
3. **Typed seam between them.** A function-type alias (e.g.,
   `Resolver`, `Classifier`) is the seam. Call sites pass any
   conforming function; the algebra knows nothing about which
   strategy is in use. Pluggable-as-pass-parameter is deferred
   until the second strategy actually arrives — the seam exists,
   the dispatch does not, "IR grows under evidence" applied to
   extensibility rather than data shape.

Apply this shape to future admire-and-extract migrations whenever
the V1 component mixes structural algorithm with domain
interpretation. The named-module sibling makes the algebra/domain
boundary visible at the file level; the typed seam keeps the
algebra honest while permitting future strategies without
rewriting the pass.

**Reasoning / consequences:** The pattern is canonical, not
ad-hoc. Future agents read this entry and recognize the shape;
reviewers can ask "what's the algebra here, what's the domain,
where's the seam" of any V1-derived pass and expect a clean answer.

## 2026-05-09 — Audits surface things not on the agenda

**Status:** decided (operating disposition)
**Context:** Session 4 produced two findings neither were planned:
the Dictionary-iteration-order invariant (commit 4) and the
algebra/domain split that prompted commit 6's pre-commit refactor.
Both required acting on what surfaced rather than shipping what was
planned.
**Decision:** Treat audits as an exploratory practice, not a
checkbox. When pre-commit reflection (or a property test, or a
reviewer's question) surfaces something second-order — a hidden
contract, a domain rule embedded in algebra, a latent V1 dependency
— the right response is to act on the finding before shipping,
even when it expands the commit's scope. Logging the finding in
DECISIONS *and* shipping the original work unchanged is the
checkbox-audit failure mode. Avoid it.

**Reasoning / consequences:** Audit dividends compound when
findings land in code; they evaporate when findings land only in
notes. The discipline pays off because the practice acts on what it
finds. Future agents reading this entry should expect their own
audits to produce findings that reshape work in flight, and should
budget time to honor those findings rather than defer them.

## 2026-05-09 — Annotated events with documented skip reasons (silent convention)

**Status:** decided (silent operating convention)
**Context:** The symmetric-closure pass (session 4 commit 3) uses
`Annotated` lineage events to record skip cases — when an inverse
isn't added because the target kind is absent or has no primary key.
The `detail` string names *why* the skip happened. Reading the trail
recovers the reason without re-running the pass.
**Decision:** Adopt the convention silently for any future pass with
skip cases. The pattern: a pass scans every node it could
transform; for nodes it processes, emit the appropriate transform
event; for nodes it skips, emit `Annotated` with a documented
detail string (e.g., `"skipped: target has no primary key"`,
`"skipped: precondition X not met"`). The lineage chain becomes
forensically useful for absences as well as presences.

Idempotence-twice-over plus documented skip reasons is the
recognizable shape for closure-flavored and conditional-application
passes. `symmetricClosure` is the template; future passes follow.

**Reasoning / consequences:** The trail answers "why isn't node X
in the surface?" without dropping into source code. Convention is
silent (no enforcement mechanism beyond review) but cheap to follow
once seen.

## 2026-05-09 — Adapter language choice: F# for IR-conversion, C# reserved for foreign-API I/O

**Status:** decided
**Context:** Session 5 commit 3 was originally intended as the first
C# adapter (`Projection.Adapters.Sql/Static.cs`), per the V2 handoff's
"C# at the boundary" framing. Implementation hit F# interop friction —
F# `Result<'a>` and discriminated unions consumed from C# require
verbose `NewSuccess` / `NewFailure` factories and pattern-matching
through nested case classes, costing readability without earning
anything for an adapter whose only foreign API is `System.Text.Json`
(which both languages handle equally well).
**Decision:** Adapter language is decided per-adapter, by which side of
the seam the foreign API sits on:

- **F# adapters** for **IR-conversion adapters** — adapters whose job
  is to coerce one shape into another, with no native-API dependencies
  beyond `System.*`. Examples: V1 JSON ↔ V2 IR (this commit), V1
  Profile JSON ↔ V2 Profile (future), DACPAC schema ↔ V2 IR (future,
  if the DACPAC parsing API is comfortable from F#).
- **C# adapters** for **foreign-API I/O adapters** — adapters whose
  job is to talk to an external system whose .NET API is OOP-flavored
  and lives on the C# side: SQL Server connections (ADO.NET, Dapper,
  Entity Framework), HTTP servers (ASP.NET / Hot Chocolate), DACPAC
  building (DacFx — *if* its API turns out to be unfriendly from F#),
  external authorization frameworks. C# is the right side of the
  language seam when the native API is the cost; F# `Result<'a>`
  interop is awkward from C# but the `Result.bind` / `Result.map`
  composition stays inside the F# core where it belongs.

The seam stays at the language boundary, not at the project boundary.
A `Projection.Adapters.<Foreign>` project may be C# or F# depending on
its native API; the namespace pattern is preserved either way.

**Reasoning / consequences:** F# for IR conversion keeps
`Result.bind` / `Result.map` composition natural at the boundary;
short-circuit semantics on adapter failures look identical to F# core
code. C# for foreign-API I/O keeps the boundary readable when the
native API is OOP-shaped. Future agents reading this entry decide
adapter language by asking "what's the native API?" — IR shapes are
F#-native; SQL/HTTP/DACPAC are C#-native.

The session 5 commit 3 adapter (`Projection.Adapters.Sql/Static.fs`)
is the canonical pattern for IR-conversion adapters; the future
SQL-I/O adapter when it arrives will be the canonical pattern for
foreign-API I/O adapters.

## 2026-05-09 — Policy.Tightening as fourth top-level axis (worked example: structural commitments are defaults)

**Status:** decided
**Context:** The session-2 commitment to a three-axis Policy
(Selection / Emission / Insertion) was right at the time given the
evidence. The session-5 `NullabilityEvaluator` admire pass surfaced
configuration that does not fit any of the three axes: tightening
mode, null budget, cautious-relaxation toggle, override list. These
inputs control *what shape of decision gets produced*, not which kinds
participate, what artifacts are emitted, or how data is applied.
Trying to fit them into one of the existing axes would be artificial
and lossy.
**Decision:** Add `Tightening` as a fourth orthogonal Policy axis.
`TighteningPolicy` carries `Mode` (`Cautious | EvidenceGated |
Aggressive`), `NullBudget` (decimal in [0, 1]), `AllowCautiousRelaxation`
(bool), and `Overrides` (list of `TighteningOverride` keyed by SsKey
per A4). AXIOMS A12 receives a second amendment (2026-05-09 — four
orthogonal axes) preserving the three-axis history; the original
amendment from 2026-05-06 is the lineage, not the rule.
**Reasoning / consequences:** This is a worked example of a principle
worth naming explicitly: **structural commitments are defaults, not
promises.** The three-axis claim was a default given the evidence
available at session 2; it grew when a real pass forced it to. The
discipline is "IR grows under evidence" applied at the policy-shape
level, exactly as it has applied at the data-shape level. Future
agents reading this entry should expect their own structural
commitments to refine when consumers demand it, and should not
defend earlier commitments against pressure from real evidence.

The amendment cadence — three axes (session 2) → four axes
(session 6) — is itself a worked example: the architecture refined
in flight three times across five sessions (Kahn's permutation
invariance, CycleResolution extraction, language rule supersession),
each driven by evidence rather than by speculation. The four-axis
amendment is the fourth and the first at the policy-shape level
rather than the implementation level.

## 2026-05-09 — Adapter language rule supersedes the original "F# core / C# shell" framing

**Status:** decided (supersedes the 2026-05-06 — F# is introduced for the algebraic core entry's framing)
**Context:** The original V2 handoff partitioned languages by
algebra-vs-I/O — F# for the pure core, C# at the imperative shell.
Session 5 commit 3 (the static-data adapter) showed this partition
is too coarse: the JSON-parsing adapter is "shell" by the original
framing but its native API is `System.Text.Json`, which both
languages handle equally well. Forcing C# created interop friction
without earning anything.
**Decision:** **Adapter language is decided per-adapter, by which
side of the seam the foreign API sits on.** F# adapters for
IR-conversion concerns whose only foreign dependencies are
`System.*` (JSON parsing, byte-array hashing, etc.). C# adapters for
foreign-API concerns whose .NET API is OOP-flavored (SQL Server
connections, ASP.NET / Hot Chocolate, DACPAC building, external
authorization). The seam is at the language boundary; the project
boundary follows from API alignment, not from a pre-imposed
algebra/shell partition.

This rule supersedes the original framing as the canonical statement.
The earlier entry remains for historical context; future agents
applying the rule should consult this entry first.

**Reasoning / consequences:** The refined rule was Danny's
formulation in the session-5 review. It is sharper than the original:
language alignment with native API maximizes readability on each
side of the seam. F# `Result.bind` / `Result.map` composition stays
natural at IR-conversion boundaries; OOP-flavored .NET APIs stay
natural at SQL-I/O boundaries. The seam is honest about what
actually crosses; the project naming follows from where the seam
falls, not from a pre-imposed partition.

## 2026-05-09 — Pattern setters explicitly named in ADMIRE.md

**Status:** decided (operational discipline; observation from session 5)
**Context:** Session 5 shipped two canonical patterns:
`EntitySeedDeterminizer` (the "split" pattern, with status `extracted
(differential confirmed)` as the marker for completed migrations);
`Projection.Adapters.Sql.Static` (the IR-conversion adapter pattern).
Both have ADMIRE entries; both have explicit canonical-string statuses;
future agents can scan ADMIRE.md and see at a glance what shape to copy
and what state each migration is in.
**Decision:** Continue this naming explicitly as ADMIRE entries land.
Each new V1 component admire identifies whether its V2 placement is a
**copy of an existing canonical pattern** (cite the earlier ADMIRE
entry by date/title) or a **new pattern setter** (mark the status
explicitly as canonical). After the second confirming instance of a
pattern, the pattern is a shape, not a one-off.

**Reasoning / consequences:** "Make the laws visible" applied at the
operational level. Future readers can scan for status strings —
`admired (placement decided)`, `extracted (differential confirmed)`,
`extracted (full coverage)` — and understand the migration arc at a
glance. The corpus accumulates value over time precisely because
patterns get named when they emerge.

## 2026-05-09 — Tightening as a registry of named interventions; modes collapsed

**Status:** decided (refines the 2026-05-09 — Policy.Tightening as fourth top-level axis entry)
**Context:** The first commit of this session shipped `TighteningPolicy`
as a flat record with a `Mode` field defaulting to `Cautious`, a
`NullBudget` defaulting to `0.0`, and an `AllowCautiousRelaxation`
toggle. Reviewed pre-push: even those defaults are themselves
*interventions* — `Cautious` mode produces decisions when
`NullabilityPass` runs; the empty policy was not actually empty.
The end goal: "no unknown alterations to the system; all
interventions stubbed as plugins, clearly identified and trackable."
Concurrent observation: V1 has only ever used `Cautious` mode in
production; the `EvidenceGated` and `Aggressive` variants are
unused.
**Decision:** Two refinements, applied together:

1. **Plugin/intervention model.** `TighteningPolicy` is a registry of
   zero or more named `TighteningIntervention` values. Empty registry
   = no interventions = no decisions produced. Each intervention
   carries a stable `Id` chosen by the caller (e.g.,
   `"v1-style-nullability"`, `"per-tenant-overrides-2026-05"`); the
   id appears in lineage events when the intervention fires, so
   audit consumers answer "which intervention changed this column?"
   structurally. The `TighteningIntervention` DU is closed; new
   intervention kinds (FK enforcement, unique enforcement, type
   tightening) land as new variants when admire passes surface them.

2. **Modes collapsed.** Per Danny's observation that V1 only ever
   uses `Cautious` mode, `TighteningMode` is removed from V2
   entirely. `NullabilityTighteningConfig` carries
   `NullBudget` + `AllowMandatoryRelaxation` + `Overrides` only — no
   mode field. If a real second mode lands later, it returns as a
   field or as a new intervention variant, motivated by evidence.
   Rename: V1's `AllowCautiousNullabilityRelaxation` becomes
   V2's `AllowMandatoryRelaxation`, naming the semantic ("permit
   mandatory→nullable relaxation under evidence") rather than the
   collapsed mode.

**Reasoning / consequences:** Two principles in evidence:

- **Defaults that intervene are themselves an intervention.** The
  prior shape's `Cautious` default would have caused
  `NullabilityPass` to produce decisions silently when the caller
  set `Policy.empty`. V2's strict default is to do nothing.
- **Unused variants are speculative complexity.** V1's three modes
  cost no maintenance in V1 because V1 isn't being refactored. They
  cost real complexity in V2 — three code paths, three test cases,
  three rationale-set composition rules — for no current consumer.
  Collapsing to one mode ("IR grows under evidence") leaves the
  algebra room to grow back into multiple modes when demand is real.

This entry refines the prior session-6 entry on Tightening; the two
should be read together. The DU-per-intervention shape is the
canonical pattern for any future "pluggable behavior" axis on Policy
— next time something feels like a registry of named operations,
this is the template.

**Worked-example dimension.** This refinement is itself a worked
example of "audits surface things not on the agenda" — the prior
commit was reviewed, the default-as-intervention smell was caught,
and the refactor landed before push. The discipline pays off when
findings land in code; deferring this to a later session would
have shipped the wrong shape and forced a more expensive amendment
later.

## 2026-05-09 — NullabilityOutcome shape: ternary with structured rationale (the V1↔masterwork choice precedent)

**Status:** decided
**Context:** The first deliberate V1↔masterwork architectural choice
V2 has made. V1 represents nullability decisions as
`(MakeNotNull: bool, RequiresRemediation: bool, Rationales: string[])`
— a binary primary outcome plus a remediation flag plus free-form
string rationales. The masterwork prescribes a ternary
`NullabilityOutcome = EnforceNotNull | KeepNullable |
RequireOperatorApproval` with a single string `Rationale` and a
`Risk` enum. Both have costs: V1's binary scrubs context the
operator-approval case actually needs; the masterwork's strings
require text parsing for downstream consumers.
**Decision:** Adopt the **masterwork's ternary outcome** with V2's
**structured rationale at the type level**. Each variant carries a
typed rationale value:

```fsharp
type NullabilityOutcome =
    | EnforceNotNull of evidence: NullabilityEvidence
    | KeepNullable of reason: KeepNullableReason
    | RequireOperatorApproval of conflict: NullabilityConflict
```

The rationale DUs (`NullabilityEvidence`, `KeepNullableReason`,
`NullabilityConflict`) are closed and structured. Lineage chains and
emitter consumers pattern-match on rationale rather than parsing
strings.

**Reasoning / consequences:** The structural rationale is more honest
than V1's binary-plus-remediation (which scrubs context the
operator-approval case actually needs) and more rigorous than the
masterwork's ternary (which uses free-form strings). The cost is
three small DUs; the benefit is type-checked rationale at every
consumer site — an emitter that handles
`MandatoryButHasNullsBeyondBudget` knows the exact data it has, no
string parsing.

**Precedent.** This is the first time V2 has made an architectural
choice between V1 and the masterwork shapes. The principle the
choice sets: **V2 doesn't inherit from one source by default; it
picks based on what serves the algebra and the codebase.** Future
similar choices (FK decision shape, unique decision shape, type
decision shape — all coming when their admire passes land) follow
the same principle. Where V1's shape serves better, take V1's.
Where the masterwork's shape serves better, take the masterwork's.
Where neither is right, refine V2's own.

## 2026-05-09 — Observable-identity-on-empty-policy as structural commitment

**Status:** decided (structural commitment, not just a default)
**Context:** Per the plugin/intervention refactor (DECISIONS
2026-05-09 — Tightening as a registry of named interventions),
`TighteningPolicy.empty` carries zero interventions and produces zero
decisions when a pass runs against it. This is V2's strict default —
no system alterations unless the caller explicitly registers an
intervention. The structural form of this rule is observable
identity: a pass running against `Policy.empty` returns an empty
output and emits no events.
**Decision:** Promote the rule from "default behavior" to
**structural commitment**. Every V2 pass that consumes Policy must
satisfy:

  *Observable identity on empty policy.* For a pass `p` taking
  `(Catalog, Policy, Profile)` and returning `Lineage<Output>`:
    - `p (catalog, Policy.empty, profile)` returns
      `{ Value = empty-output; Trail = [] }`.
    - The Catalog is unchanged (passes that produce values rather
      than transforming the catalog return their `empty-output`).
    - No lineage events are emitted (no `Touched`, no `Annotated`,
      no `Created`, no `Removed`).

This is the V2 algebraic property the masterwork's "warn, don't
auto-fix" principle compiles down to. Future passes adopt the
commitment by construction; tests verify it explicitly.

**Reasoning / consequences:** "V2 takes no action on empty policy"
is a structural property the type system + tests can guarantee, not
a convention reviewers must remember to check. Future agents
reading this entry inherit the rule for any new pass; the rule
becomes a compiler-checkable obligation as more passes adopt the
ProjectionInput-shaped signature.

## 2026-05-09 — V1→V2 name mapping: `AllowCautiousNullabilityRelaxation` → `AllowMandatoryRelaxation`

**Status:** decided (documentation; not a code change)
**Context:** During the mode-collapse refactor, V1's
`AllowCautiousNullabilityRelaxation` was renamed to
`AllowMandatoryRelaxation` in V2. The rename names the semantic
("permit mandatory→nullable relaxation under evidence pressure")
rather than the now-collapsed Cautious mode that was the V1 flag's
referent.
**Decision:** Record the mapping here so the V1↔V2 grep-bridge
exists at the documentation layer. Migration scripts, debugging
sessions, and future agents tracing V1 behavior to V2 can follow
the rename through this entry.

| V1 name                                  | V2 name                    |
|------------------------------------------|----------------------------|
| `AllowCautiousNullabilityRelaxation`     | `AllowMandatoryRelaxation` |

This entry pairs with the broader V1↔V2 vocabulary mapping in the
2026-05-06 — General names in the pure core entry; this is the
nullability-specific rename. Future renames at the rules-module
level land here as additional rows.

## 2026-05-09 — Three-input projection validated end-to-end (the milestone)

**Status:** decided (worked-example milestone)
**Context:** Session 6's planned milestone — combine the static-data
adapter, the profile-snapshot adapter, and `NullabilityPass` to
validate the three-input projection
`Project = Π ∘ E : (Catalog, Policy, Profile) → Output` against
V1-fixture-equivalent inputs. The test exercises the full V1↔V2
boundary stack:

```
V1 JSON (static-data + profile-snapshot)
     │
     ▼
F# adapters (Static.attachStaticPopulations + ProfileSnapshot.attach)
     │
     ▼
V2 IR (Catalog with populations + Profile)
     │
     ▼
NullabilityPass (under registered Nullability intervention)
     │
     ▼
NullabilityDecisionSet (emitter-consumable per A32)
```

**Result:** the milestone test
(`MILESTONE: three-input projection passes end-to-end through both
adapters and NullabilityPass`) passes. 348/348 tests green.

**Two structural commitments validated empirically:**

1. **The three-input projection works.** `Project = Π ∘ E` consumes
   all three inputs (Catalog, Policy, Profile) and produces decisions
   end-to-end. The plumbing through both adapters preserves identity
   (every decision keys back to a real catalog Attribute SsKey); the
   pass produces decisions for every (attribute × intervention) pair
   the policy registers; outcomes match expectations on the
   V2-expressible cases (PrimaryKey, PhysicallyNotNull, override,
   no-signal).

2. **The plugin-shape Tightening supports the projection without
   compromise.** Sessions 6's mid-session refactor (DECISIONS
   2026-05-09 — Tightening as a registry of named interventions)
   could have introduced friction at the integration site; it didn't.
   Registering one intervention with a stable id, running the pass,
   and getting decisions tagged with that id all flow naturally.
   Future audit consumers reading the lineage will see "intervention
   `v1-cautious-equivalent` produced EnforceNotNull(PrimaryKey) on
   `OS_ATTR_E2E_Parent_Id`" — structural, queryable, type-checked.

**Caveats parked:**

- V2's IR does not yet carry `IsMandatory` on `Attribute`. The V1
  mandatory-driven branches (`LogicalMandatoryNoNulls`,
  `RelaxedUnderEvidence`,
  `MandatoryButHasNullsBeyondBudget`) are commented pseudocode in
  `NullabilityRules.evaluate`; they wire in when `IsMandatory` lands
  under "IR grows under evidence." The milestone test annotates
  this in code so future agents see the limitation surfaced.
- Differential parity with V1's full `NullabilityEvaluatorTests`
  fixture suite (8 tests) requires the mandatory branch. The
  current end-to-end differential validates the V2-expressible
  subset; the remaining V1 parity arrives with the IR refinement.

**Reasoning / consequences:** The milestone is achieved at the
algebra-and-plumbing level. The remaining V1 parity is a known IR
gap, not a structural one. V2's three-input projection is now
empirically validated; future passes (FK enforcement, unique
enforcement) follow the same shape and inherit the validation by
construction. Session-6 commits 1 (Tightening axis) → 2 (plugin
refactor) → 3 (NullabilityRules) → 4 (NullabilityPass) → 5
(profile adapter) → 6 (this milestone) form a coherent vertical
slice; each commit is independently meaningful and the whole stack
passes its empirical contract.

## 2026-05-10 — Milestone (re-marked): the algebra is now operational

**Status:** decided (worked-example milestone, marked deliberately)
**Context:** Session 6 commit 6 ran the three-input projection
end-to-end through both adapters and `NullabilityPass` against real
V1 fixture data; the test passes. This entry re-marks the moment
clearly, separately from the commit-level milestone entry.

**What it is.** The first execution of `Project = Π ∘ E` on the
triple `(Catalog, Policy, Profile)` end-to-end against V1-derived
inputs. Two adapters convert V1 JSON to V2 IR; the policy registers a
Nullability intervention; the pass produces a structured
`NullabilityDecisionSet`. The plumbing is empirical, not
hypothetical.

**What it validates.** The three-input projection works in practice
— identity preserved across the boundary; profile evidence flows
into per-attribute decisions; intervention id threaded through to
lineage; outcomes match expectations on the V2-expressible cases.
The plugin-shape Tightening (DECISIONS 2026-05-09) supports the
projection without compromise — the mid-session refactor is
empirically vindicated.

**What it does not yet validate.** V2's IR does not yet carry
`IsMandatory` on `Attribute`. The V1 mandatory-driven branches
(`LogicalMandatoryNoNulls`, `RelaxedUnderEvidence`,
`MandatoryButHasNullsBeyondBudget`) are pseudocode in
`NullabilityRules.evaluate`, awaiting the IR refinement under "IR
grows under evidence." Full V1 parity with V1's eight
`NullabilityEvaluatorTests` requires this. The honest frame for the
parity gap: a known IR refinement, not a structural one.

**The phase change.** This is the moment the algebra stops being a
structural claim and becomes an operational fact. The properties
the axioms promised — A6's three substantive inputs, A12's policy
axes, A17's `Project = Π ∘ E`, A32's emitter-consumable values, the
2026-05-09 observable-identity-on-empty-policy commitment — are
demonstrated, not just claimed. Future agents reading this log
should identify session 6 commit 6 as the inflection point.

## 2026-05-10 — IR-conversion adapter pattern: the adapter is where V1's vestigial fields die

**Status:** decided (operational discipline; observation from session 6)
**Context:** Two F# IR-conversion adapters now share a shape:
`Projection.Adapters.Sql.Static.attachStaticPopulations` (session 5)
and `Projection.Adapters.Sql.ProfileSnapshot.attach` (session 6).
The pattern is canonical not because it was prescribed but because
it was repeated and confirmed.

The shared shape:
- Signature: `Catalog -> string -> Result<Catalog>` (or returning a
  built value type like `Profile`).
- JSON parsing via `System.Text.Json`.
- Embedded V1 fixture content as the V2 contract; the test fails
  loudly if V1's JSON shape changes without a matched V2 expectation
  update.
- Silent skip for unresolvable rows (the catalog's selection is the
  contract, not the JSON's).
- Result-typed return; never throws across the seam.
- F# language for IR conversion (per the 2026-05-09 adapter language
  rule).

**Decision (the additional convention this entry names):** **The
adapter is the place V1's vestigial fields die.** V2's IR carries
only what V2 uses. V1's serialized data formats may include fields
V2 does not model (catalog metadata embedded in profile JSON;
operational sample arrays; redundant copies of catalog facts).
The adapter:

  - **Drops** V1 fields V2 does not model. Examples from session 6's
    `ProfileSnapshot` adapter: V1's
    `IsNullablePhysical`/`IsComputed`/`IsPrimaryKey`/`IsUniqueKey`/
    `DefaultDefinition` are catalog metadata in V2 (lives on
    `Attribute`/`Column`); the adapter ignores V1's redundant copies
    and trusts the V2 catalog. V1's `NullSample`/`OrphanSample` are
    operational diagnostics; V2 elides them.
  - **Synthesizes** V1 fields V2 demands but V1 lacks. Example:
    V1's `CompositeUniqueCandidateProfile` has no `ProbeStatus`; V2
    requires it for evidence-vs-no-evidence distinguishability, so
    the adapter synthesizes a default `Succeeded` probe at
    `UnixEpoch`. If a real V1 fixture surfaces a meaningful
    distinction, the adapter learns the field; until then, the
    synthesized default flows through.
  - **Names** the divergences in code comments. Each drop /
    synthesis carries a comment so future readers can audit the
    boundary's choices without surprise.

**Reasoning / consequences:** V2's IR stays small. V1's
serialization quirks don't propagate into the algebra. Adapters
that deviate without justification (carry V1 fields that V2 does
not use; or fail to synthesize fields V2 demands) are flagged in
review. Future IR-conversion adapters (UniqueIndex, FK enforcement,
type tightening — coming in subsequent sessions) inherit this
convention by construction.

## 2026-05-10 — Audit discipline operates at design-time, not just commit-time

**Status:** observed (operating discipline reflection)
**Context:** Sessions 4–6 produced four audit-driven course
corrections:

  - Session 4: Kahn's permutation invariance (commit 4).
  - Session 4: CycleResolution algebra/domain split (commit 6).
  - Session 5: C# → F# language pivot (commit 3).
  - Session 6: Plugin/intervention refactor of Tightening (commit 2).

The first three caught issues mid-session as the work progressed.
The fourth — session 6's plugin refactor — caught something at the
level of *initial design intent* and reshaped the work before the
flat-record commit was pushed. The default-as-intervention smell
was a problem in the work's premise, not its execution.
**Decision:** Mark the observation. The audit discipline is
beginning to operate at design-time, not just commit-time. The
practice deepens with use; future agents reading this log should
expect their own audits to surface premise-level findings, not
only execution-level ones, and should plan to act on them in
flight rather than ship past them.

This is not a flagship principle but a worked-example observation:
the practice gets better the more it gets used.

## 2026-05-10 — Second decision-producing V1 transform fully migrated; status string in use

**Status:** decided (worked-example marker)
**Context:** V2's third "extracted (differential confirmed)" status
lights up: `NullabilityEvaluator` joins `EntitySeedDeterminizer` as a
fully-migrated V1 component. Five of V1's eight test scenarios pass
as Behavioral parity assertions in V2; three are explicit Skip cases
documenting intentional V2 divergences (Aggressive mode collapsed;
opportunity-stream pending Diagnostics writer).

**Decision:** Mark the moment. `NullabilityEvaluator`'s ADMIRE entry
reaches `extracted (differential confirmed)`. The status string is
canonical; future ADMIRE entries that achieve this state use the same
phrase. The convention from DECISIONS 2026-05-09 (Pattern setters
explicitly named in ADMIRE.md) is paying out: readers can scan
ADMIRE.md and see at a glance which V1 components have been
empirically validated against V2.

**The differential-with-skips pattern.** When V2 diverges from V1
deliberately (collapsed Aggressive mode; structured Diagnostics
writer instead of inline opportunities), the parity test names the
divergence as a Skip case with explicit rationale. This preserves
two invariants:

  1. The migration is **honest** — V2 doesn't pretend to match V1
     where it deliberately doesn't. The skip case is the divergence
     made visible.
  2. The discipline is **constructive** — adding the V2 equivalent
     of a skipped V1 case (e.g., when an Aggressive-equivalent
     intervention arrives, or the Diagnostics writer lands) is a
     mechanical activation: remove the `Skip = "..."` argument and
     write the V2 assertion. The skip is a forward-pointing TODO,
     not a permanent gap.

**Reasoning / consequences:** The third use of the status string
makes the convention canonical by repetition (per the
2026-05-09 — Pattern setters discipline). Future ADMIRE entries that
reach `extracted (differential confirmed)` follow the same
differential-with-skips pattern: 100% V1 contract under V2's
expressible cases; explicit Skip-with-rationale for V2 divergences.

## 2026-05-11 — Strategy layer: a named architectural vector

**Status:** decided (operating discipline)
**Context:** Three V1 components have been migrated under the
algebra/domain split (DECISIONS 2026-05-09 — Algebra/domain split
pattern): `EntityDependencySorter` produced `CycleResolution`
(session 4); `NullabilityEvaluator` produced `NullabilityRules`
(session 6); `UniqueIndexDecisionOrchestrator` produced
`UniqueIndexRules` (session 7). Each migration lifted V1's domain
reasoning out of the algebra and named it as a sibling module. The
*shape* of these modules has now stabilized through repetition, and
the moment to codify it as a first-class architectural concern has
arrived — before a fourth instance lands and the implicit convention
drifts.

The lesson from the previous "Algebra/domain split pattern (generalizable)"
entry is that the canonical shape is observable in code, not just in
prose. With three instances the shape is empirically real; the cost
of codifying now is low; the cost of codifying after six instances
is rewriting six modules to fit. This entry promotes the strategy
layer from implicit convention to named architectural vector.

**Decision:** **Strategy** is a named architectural concern within
`Projection.Core`, distinct from but adjacent to the algebraic core.
Strategy modules carry domain-specific decision logic that the
algebra invokes through a typed seam. The canonical shape of a
strategy module:

1. **Pure functions of IR fields.** No I/O, no mutable state, no
   external context. The strategy reads `Catalog`, `Policy`, and
   `Profile` fields and returns decisions. Determinism follows from
   purity.
2. **A typed function-type alias is the seam.** The pass that
   consumes the strategy calls into it through a named function type
   (e.g., `Resolver`, `evaluate`); the algebra knows nothing about
   how the decision is made. New strategies plug in by conforming
   to the seam without rewriting the algebra.
3. **Structured rationale DUs cover the decision space.** Each
   variant of the outcome DU carries the evidence or reason for the
   decision at the type level. Lineage events emit a textual summary
   for grep-ability; the structured outcome lives in the decision
   set for downstream pattern-matching. Free-form rationale strings
   are an anti-pattern (see CycleResolution caveat below).
4. **Lineage events fire only on actual decisions.** When a
   strategy makes no decision (registry empty, intervention not
   registered, structural commitment to inaction), no events are
   emitted. The `Annotated`-with-skip-reason convention (DECISIONS
   2026-05-09) covers conditional cases that still warrant a trail
   entry.
5. **The module name advertises the domain.** `<Domain>Rules` for
   per-record deciders (`NullabilityRules`, `UniqueIndexRules`,
   future `ForeignKeyRules`); domain-named modules for non-record
   strategies (`CycleResolution`). The `Rules` suffix is the
   recognizable shape for registered-intervention strategies; other
   suffixes are admissible when the call pattern differs.

**Two strategy flavors observed.** The three current modules split
into two flavors that share the deep shape but differ in call
pattern:

- **Registered-intervention strategies** (`NullabilityRules`,
  `UniqueIndexRules`): invoked through registry iteration over a
  `TighteningIntervention` variant, one decision per (record ×
  intervention) pair, intervention-id flowing through every
  decision. The pass driver fans out over the registry; the
  strategy's `evaluate` decides each pair.
- **Structural strategies** (`CycleResolution`): invoked from
  inside a pass at structurally-determined moments (per-FK-edge
  classification during graph construction; per-SCC resolver
  application during cycle handling). No registry; no
  intervention-id. The seam is a function type the pass passes
  through.

Both flavors honor the deep shape; the call pattern is what differs.
Future strategy modules pick the flavor that matches their domain.

**Worked examples.**

| Module | Flavor | Seam | Decision DU | Status |
|---|---|---|---|---|
| `CycleResolution` | structural | `Resolver` (`SsKey list -> ((SsKey * SsKey) * EdgeStrength) list -> ResolutionStep`); `classify` | `EdgeStrength`; `ResolutionStep` (free-form `Reason`) | extracted |
| `NullabilityRules` | registered-intervention | `evaluate : interventionId -> config -> Attribute -> Profile -> NullabilityDecision` | `NullabilityOutcome` ternary with `NullabilityEvidence` / `KeepNullableReason` / `NullabilityConflict` | extracted (differential confirmed) |
| `UniqueIndexRules` | registered-intervention | `evaluate : interventionId -> config -> Kind -> Index -> Profile -> UniqueIndexDecision` | `UniqueIndexOutcome` binary with `UniqueIndexEvidence` / `UniqueIndexKeepReason` | extracted |

**CycleResolution caveat.** `CycleResolution.ResolutionStep.Reason`
is a free-form string ("auto-resolved by removing weak edge", "SCC
has no Weak edge to break", etc.). This predates the
structured-rationale-DU convention and is grandfathered; when
`CycleResolution` is next substantively touched (e.g., when a second
resolver strategy lands per the 2026-05-08 pluggability deferral),
migrate `Reason` to a structured DU mirroring `NullabilityRules`'s
approach. Logging the migration as a TODO here rather than
performing it now keeps session 8 focused on codification.

**Registry deferred.** A registry mechanism for strategy
discoverability (a top-level `Strategies : Strategy list` axis;
plug-in loading; cross-strategy composition combinators) is the
next promotion candidate when N grows past 4–6. At N=3 the registry
is overkill — each strategy's call site is named explicitly and the
seam is its type. Recording the deferral here so the next agent
reading this entry doesn't build the registry under "IR grows under
evidence" and finds it unjustified at the time of writing.

**Reasoning / consequences:** Strategy is now nameable in code and
in conversation as a first-class concern. New V1 admire migrations
that surface domain-decision logic land into a named layer with a
recognized shape; reviewers can ask "what's the seam, what's the
DU, where's the algebra" and expect a structurally-honest answer.
The pattern's empirical basis (three instances) supports the
codification; future instances either fit the codification or
surface a revision. The codification is descriptive, not
prescriptive; if a strategy doesn't fit, the question is whether
the codification or the strategy is wrong, and either answer is
interesting.

## 2026-05-11 — Strategy composition vocabulary (sketch, deferred)

**Status:** sketched (not implemented)
**Context:** Each pass driver that consumes a strategy implements
the iteration/accumulation/lineage discipline ad hoc. With three
strategy modules and two pass drivers (NullabilityPass,
UniqueIndexPass) using nearly the same fan-out shape, the
composition logic has now been duplicated twice. Before the third
duplication ships (`ForeignKeyPass`), the question is whether a
small composition vocabulary belongs at the strategy layer.

**Sketch (proposal, not implementation).** The composition primitives
that would land into a `Projection.Core.Strategies.Composition`
module if and when the cost-benefit clears:

1. **`fanOut`** — registry iteration. Given a list of `(id, config)`
   pairs and a `decide : id -> config -> 'context -> 'decision`
   function, produce `'decision list` over a list of contexts.
   Currently inlined in NullabilityPass and UniqueIndexPass via list
   comprehension. The vocabulary exists; it's been duplicated; it's
   short enough that duplication has not yet been painful. The
   primitive earns its place when N=3+ pass drivers iterate the same
   way, or when a fourth axis of variation lands (e.g., decision
   weights, conditional invocation, ordering preferences) that would
   force the inlined version to grow into a function anyway.
2. **`fallback`** — chained strategy. Given strategies A and B, run
   A; if it returns a "no decision" / default outcome, run B.
   Currently no use case in the codebase — every strategy returns a
   total decision (every variant of every outcome DU is meaningful).
   Speculative until a partial strategy lands (e.g., manual override
   sets that fall back to evidence-driven decisions when the
   override is absent).
3. **`accumulate`** — multi-strategy aggregation. Given strategies
   A and B that both return decisions, produce a combined decision
   set with both flowed through. Currently the registry already does
   this implicitly: multiple registered interventions of the same
   variant fan out into separate decisions per intervention. The
   primitive earns its place when cross-variant aggregation arrives
   (e.g., a pass that consumes both Nullability and UniqueIndex
   decisions to produce a unified column-level annotation).
4. **`wrap`** — instrumented strategy. Decorate a strategy with
   logging / lineage / telemetry. Currently the lineage-event
   discipline is inlined into each pass driver; lineage is therefore
   not strategy-scoped but pass-scoped (correct for passes that
   coordinate multiple strategies). The primitive earns its place
   if strategies become independently observable — e.g., per-strategy
   lineage subtrails, per-strategy diagnostics — which is not yet
   the case.
5. **`lift`** — context translation. Given a strategy that decides
   on context type `'a`, produce one that decides on `'b` via a
   `'b -> 'a` projection. Currently every strategy already operates
   on its natural context (`Attribute`, `Index`, `Reference`); no
   need for a generalization. The primitive earns its place when a
   strategy is reused across different IR shapes (e.g., applying the
   same nullability rules to view columns as well as table columns,
   if views land as a Kind variant).

**Decision:** **Sketch, defer implementation.** None of the
primitives have N≥2 forced uses today — `fanOut` is the closest
(N=2 inlined instances, soon N=3 with ForeignKey), but the inlined
form is 4 lines and the function form would be 6 lines including
type annotation. The argument for landing `fanOut` now is mostly
aesthetic; the argument for deferring is "IR grows under evidence"
applied to the strategy layer itself.

The right cue to revisit: when a fourth registered-intervention
strategy lands (the fifth strategy module overall, after
ForeignKeyRules), the `fanOut` duplication crosses the threshold
where a function helps more than it costs. Then `fanOut` lands as
the first composition primitive; the others follow as their use
cases arrive.

**Reasoning / consequences:** Codifying the composition vocabulary
in advance of need would be the same speculative-architecture
failure mode the registry deferral sidesteps. Recording the sketch
preserves the thinking — the next agent encountering a fourth
registered-intervention pass driver doesn't reinvent the analysis;
they read this entry, see the threshold, and decide based on the
same empirical criterion.

## 2026-05-11 — Strategy-layer codification: empirical verdict after the fourth instance

**Status:** decided (codification confirmed, with refinements)
**Context:** Session 8's codification of the strategy layer
(2026-05-11 entries above) was promoted from implicit to explicit
based on three instances (CycleResolution, NullabilityRules,
UniqueIndexRules). The fourth instance (ForeignKeyRules +
ForeignKeyPass) was implemented under the freshly-codified pattern
to test whether the codification holds without strain. Per the
user's session-8 brief: "If the codification held for ForeignKey
without strain, the registered-intervention sub-pattern is
empirically validated. If it didn't, the codification needs
revision." This entry records the verdict.

**Verdict.** **The deep-shape codification held.** All five core
predictions carried over to ForeignKey without revision:

| Codification prediction | ForeignKey outcome |
|---|---|
| Pure functions of IR fields | ✓ `ForeignKeyRules.evaluate` is pure; reads Catalog/Profile/config; no I/O |
| Typed function-type seam | ✓ `evaluate` is the seam; `ForeignKeyPass` calls into it via the same shape |
| Structured rationale DUs cover decision space | ✓ Three DUs (Evidence, KeepReason, Outcome); 13 variants total exhaustively covering V1's signal hierarchy |
| Lineage fires only on actual decisions | ✓ Observable identity on empty policy preserved; Annotated events on decisions |
| Module name advertises domain | ✓ `<Domain>Rules` suffix; lives in `Strategies/` folder |

The pattern is empirically validated at the third
registered-intervention instance.

**Refinements surfaced.** Three findings the codification did not
anticipate; each is worth recording as a refinement.

### Refinement 1: KeepReason DUs at namespace level need `RequireQualifiedAccess` when case names overlap

**The friction.** `UniqueIndexKeepReason.PolicyDisabled` and
`ForeignKeyKeepReason.PolicyDisabled` share a case name (similarly
`EvidenceMissing`). With both DUs at the `Projection.Core` namespace
level and neither carrying `[<RequireQualifiedAccess>]`, F#
ambiguity-resolution picks one — and in `ForeignKeyRulesTests.fs`,
it picked the wrong one, generating compile errors. The fix was
qualifying every collision site with the type prefix
(`ForeignKeyKeepReason.PolicyDisabled`).

**The codification refinement.** When a strategy's KeepReason DU
shares case names with another strategy's KeepReason DU (a real
risk because `PolicyDisabled`, `EvidenceMissing`, and similar
generic names will recur across strategies), the codification
should add `[<RequireQualifiedAccess>]` to the KeepReason DU. The
rationale is the same as for `NullabilityOutcome` (DECISIONS
2026-05-09 — case-name conflict with `OverrideAction.KeepNullable`):
`RequireQualifiedAccess` keeps semantically-clean names while
preventing ambiguity.

**Action item (deferred).** Retroactively applying
`RequireQualifiedAccess` to `UniqueIndexKeepReason` and
`ForeignKeyKeepReason` (and `NullabilityEvidence` /
`KeepNullableReason` if their case names ever clash) would touch
tests and rules modules alike. Defer the refactor; capture the
discipline here as a forward rule for future strategies. When a new
strategy's KeepReason DU is written, it lands with
`RequireQualifiedAccess`. When any current KeepReason DU is next
substantively modified, retrofit `RequireQualifiedAccess` as part
of that change.

### Refinement 2: `'context` is variable-arity across strategies

**The observation.** The cross-strategy generalization the user
flagged in commit 4's admire entry is empirically real, but the
`'context` slice that flows into each strategy's `evaluate` varies
in arity:

| Strategy | `'context` slice | Why |
|---|---|---|
| Nullability | `Attribute` | Per-attribute decision, no cross-record reasoning |
| UniqueIndex | `Kind × Index` | Composite-unique candidates need the kind to disambiguate |
| ForeignKey | `Kind × Reference × Catalog` | FK decisions reach across kinds (target lookup, cross-schema check) |

ForeignKey takes the **catalog itself** as an argument, which the
other two do not. This is a structural difference: FK decisions are
the first instance of a strategy that **reaches across the catalog**
rather than deciding locally per-record. The codification's
predicted uniform `(interventionId, config, context, profile) →
decision` shape is technically uniform if `context` is allowed to be
any tuple — but the practical signatures differ because what
`context` *means* differs.

**The codification refinement.** Strategy modules within the
registered-intervention sub-pattern share the *signature shape*
`(interventionId, config, ...record-or-record-bundle..., profile) →
decision`, where the record-or-record-bundle is *whatever IR
context the rule needs*. The codification's prediction of a
**uniform single-context** signature was too narrow; the prediction
of a **uniform shape** (named arguments, fixed positions for
`interventionId` first and `profile` last) holds.

**Generic alias deferred.** The cross-strategy alias
`type StrategyEvaluator<'context, 'config, 'decision> = string * 'config * 'context * Profile -> 'decision`
would absorb all three signatures with `'context` as `Attribute`,
`Kind * Index`, or `Kind * Reference * Catalog` respectively. At
N=3 the alias is aesthetic; at N=4 (when a fourth
registered-intervention strategy lands), the alias earns its place
as a way to name the shape and make composition primitives
(`fanOut`, `fallback`, etc.) typeable. Defer; the threshold is
explicit.

### Refinement 3: Audit dividend on `MissingTarget`

**The observation.** V2's `ForeignKeyKeepReason.MissingTarget` has
no V1 counterpart — V1's `ForeignKeyEvaluator` silently skips
references to missing targets. Surfacing the missing target as an
explicit keep-reason produces an audit-trail entry V1 lacked: every
FK decision now has a structured reason, even the "no decision"
cases. This is the same audit-dividend pattern that surfaced in the
2026-05-09 entry "Annotated events with documented skip reasons" —
applied to the strategy layer's outcome DUs rather than to lineage
events.

**The codification refinement.** Where a V1 component silently
skips work, V2's strategy module **should surface the skip as a
named keep-reason variant** in the outcome DU. The audit chain
gains a structured reason; the algebra gains a total decision
function (every input produces a decision); the V1↔V2 differential
gains a skip-with-rationale Behavioral assertion rather than a
ghost in V1's code. Three instances now: SymmetricClosure (Annotated
skip events on the lineage trail), NullabilityEvaluator (Skip cases
on V1 parity tests where V2 diverges), ForeignKeyEvaluator
(MissingTarget keep-reason variant). The pattern's general; the
codification absorbs it as a fourth core prediction:
**total decisions, named skips.**

**Reasoning / consequences.** The codification was descriptive
(three instances at session start) and is now empirically validated
(four instances at session end). Three refinements landed — none
of them invalidated the deep shape; each one strengthened the
codification by surfacing a non-obvious detail. The codification
now reads as: pure functions, typed seam, structured rationale DUs
(KeepReasons under `RequireQualifiedAccess`), lineage events on
actual decisions, module name advertising the domain, total
decisions with named skips. Future strategy migrations have a
sharper rubric.

The user's session-8 framing ("the test of whether session 8
succeeded is whether the fourth strategy migration fits cleanly")
is empirically met. Session 9+ rich-profiling and Faker-style
emission inherit a strategy layer that is named, observable in the
file system, codified with documented refinements, and validated
on its central case.

**Shared trigger across the two deferrals.** The composition
vocabulary deferral (the 2026-05-11 sketch entry) and the generic
`StrategyEvaluator` alias deferral (refinement 2 above) now have a
single shared cash-out point: the **next registered-intervention
strategy migration** (the fifth strategy module overall, the fourth
registered-intervention instance). At that moment both questions
are decided empirically — `fanOut` either earns its place from a
fourth duplicated inlining, and the generic alias either surfaces
as a useful naming for the four observed signatures or remains
aesthetic. Recording the shared trigger so the next migration
agent doesn't decide the two questions in isolation.

## 2026-05-12 — Rich-profiling session 9: surfacings beyond the original plan

**Status:** decided (operating discipline; future-session direction)
**Context:** Session 9 opened the rich-profiling vector — Profile
gains its first distribution evidence type, validated end-to-end
through a sibling Π. The session-9 brief laid out a six-commit
shape; what arrived along with the planned work was a small set of
findings that reshape sessions 10+ and the architecture's claims.
The reflection commit's job is to write them down.

**Finding 1: V1 is the empty-set source for distribution evidence.**
The biggest realization. Before session 9 we treated "V1 has the
shape; V2 expresses it cleanly" as the migration archetype. V1's
profiling is *entirely* binary-question outcomes — nulls /
duplicates / orphans, yes/no plus a count. There is **no V1
distribution evidence to migrate.** This is the first admire entry
that surfaces V1 absence as the gap, not V1 logic to lift.

The architectural consequence: rich profiling is **not a migration,
it's growth.** V2 is now extending its capability beyond V1's
substrate, with V2-defined JSON shapes, V2-only adapters, and
V2-only consumers. Future evidence types (numeric distributions,
temporal density, joint statistics) follow the same template — no
V1 source to mirror; the V2 boundary is data the V2 shape itself
prescribes. This reframes the multi-session arc: sessions 10+ are
expanding the algebra into territory V1 never reached, not
migrating V1 work.

**Finding 2: Π signature variation is a real architectural axis.**
SSDT and JSON take `Catalog -> string`. Distributions takes
`Catalog -> Profile -> string`. The variation isn't an
inconsistency; it's a refinement of A18 (no policy parameter on Π).
The three substantive inputs are Catalog, Policy, Profile (A6); a
Π consumes whichever subset of `Catalog × Profile` it needs,
**but never Policy** — Policy lives in passes, not emitters. The
three-emitter empirical evidence:

| Π | Signature | Inputs consumed |
|---|---|---|
| RawTextEmitter (SSDT) | `Catalog -> string` | Catalog only |
| JsonEmitter | `Catalog -> string` | Catalog only |
| DistributionsEmitter | `Catalog -> Profile -> string` | Catalog × Profile |

This parallels the strategy-layer finding from session 8 (refinement
2: the `'context` slice into `evaluate` is variable-arity across
strategies). Same pattern, same empirical resolution: the deep
shape (no policy; pure function of substantive inputs) holds; the
practical signatures differ because what each Π *consumes* differs.
Future Π — Faker, anomaly reports, etc. — pick the signature that
matches their consumption pattern.

**Finding 3: Composition via `Result.bind` is the right granularity
for sibling adapters.** Two adapters, both pure functions of
`Catalog * JSON-string * Profile -> Result<Profile>`. The caller
composes them in any order:

```fsharp
ProfileSnapshot.attach catalog snapshotJson
|> Result.bind (ProfileStatistics.attach catalog distributionsJson)
```

Or reverse, or interleaved, or with intermediate transformations.
At N=2 adapters the explicit `Result.bind` is cheap and visible.
A top-level orchestrator earns its place when N≥3 adapters all
need to compose with the same predictable order; for now the
explicit composition documents itself. The session-8 composition-
vocabulary deferral discipline applies symmetrically to adapters.

**Finding 4: Truncation contracts are structural commitments, not
ad-hoc validation.** `CategoricalDistribution.create` enforces
"`IsTruncated = false ⇒ DistinctCount = Frequencies.Length`" —
the structural meaning of "I observed every distinct value." The
`IsTruncated` flag distinguishes "captured all 3" from "captured
3 of N." Without this discipline, downstream consumers (the
eventual anomaly strategy, the Faker emitter) would have no way
to distinguish complete from partial vocabularies. The validation
is small; the consumer-side benefit is structural — pattern-match
on the flag, get the contract.

Future evidence types inherit the discipline: every distribution
DU variant carries a structural answer to "is this evidence
complete or sampled?" Numeric histograms will have an analogous
flag; temporal evidence may have its own ("date-range observed
in full" vs "sampled within range"). Codify in the second
distribution variant's design (session 10).

**Finding 5: First-consumer smallness validates the architecture
better than first-consumer ambition would have.** DistributionsEmitter
is small — ~200 lines. Building it as a sibling Π rather than a
one-off formatter cost almost nothing extra (the file structure,
the project setup, the test discipline) and bought the
empirically-validated claim that emission is parameterized over the
enriched IR for the third time. The user's framing was prescient:
"the discipline of building it as one matters more than its size."
Future sessions follow this rule — when a new evidence type's first
consumer is a diagnostic, build it as a sibling Π regardless of size.

**Finding 6: The closed-DU `AttributeDistribution` shape absorbs
new variants cleanly, with one expected friction.** Adding `Numeric`
and `Temporal` variants in sessions 10+ extends the DU and the
adapter's `Kind` dispatch and the emitter's `match` arms. The F#
incomplete-match warning fires when there's only one variant (as it
did when implementing `tryFindCategorical`); the workaround is a
defensive second branch (`AttributeDistribution.Categorical _ ->
None`). When the second variant lands, the second branch becomes a
real `Numeric _ -> ...` case and the friction disappears. Document
the workaround so session 10 understands why the redundant-looking
branch exists.

**Direction signals for sessions 10+.**

  - **Numeric distribution shape question.** Histograms (binned
    counts), percentiles (5/25/50/75/95), or range (min/max)? Or
    all three? The choice is the design question for session 10's
    admire. Recommendation: percentile + range as the foundational
    shape (smaller; more useful for synthetic generation),
    histograms as a follow-up if a real consumer demands.
  - **First substantive consumer of a distribution.** Session 11's
    proposed work is the first distribution-aware strategy
    (e.g., a uniqueness strategy that consults distinct-count to
    distinguish "candidate uniqueness" from "spurious uniqueness").
    Per "each evidence type lands when its first consumer arrives,"
    session 11's strategy choice validates whether the evidence
    shape is fit for purpose. If the strategy's logic doesn't fit
    cleanly into the codified strategy layer with the new evidence
    type, the codification gets refinement #4.
  - **The shared trigger from session 8 still holds.** Session 11
    is the projected cash-out point for both the composition
    vocabulary deferral and the generic `StrategyEvaluator` alias.
    Three deferred decisions converge there; the session-11 agent
    inherits a sharp empirical setup.
  - **Faker emitter waits for the third evidence type.** A
    synthetic generator needs at least categorical + numeric
    + cardinality to produce plausible data. Faker is session 12+
    work; sessions 10 and 11 lay the foundation.

**Reasoning / consequences.** Session 9's job was "extend and
consume." It did that, but it also surfaced the V1-empty-source
framing, the Π-signature-variation refinement, the truncation-
contract discipline, and the small-first-consumer validation —
findings the session-9 brief didn't anticipate but that will shape
sessions 10+. Recording them here means the next agent starts with
the empirical context, not the original plan; the same reflective
discipline that made session 8's codification empirically validated
applies here to the rich-profiling agenda.

## 2026-05-13 — Admire entries fall on a spectrum (V1-migration / V2-growth / hybrid)

**Status:** decided (operating discipline)
**Context:** Up through session 8, every admire entry surfaced V1
logic to migrate, V1 fields to absorb, V1 contracts to honor — the
"V1 has the shape; V2 expresses it cleanly" archetype. Session 9's
rich-profiling admire (ADMIRE.md 2026-05-12) was the first that
surfaced V1 *absence* as the gap and V2 architectural growth as
the work. The session-9 reflection (DECISIONS 2026-05-12 Finding
1) named this as a reframe of the migration discipline.

**Decision:** Admire entries fall on a three-mode spectrum, named
by what V1 contributes:

  1. **V1-migration mode.** V1 has the logic; V2 expresses it
     cleanly. The admire entry's "Existing test coverage" section
     is rich (V1 tests categorized as Behavioral / Property /
     Differential / Skip). The migration completes when V2
     satisfies V1's contracts. Examples:
     `EntitySeedDeterminizer`, `NullabilityEvaluator`,
     `UniqueIndexDecisionOrchestrator`, `EntityDependencySorter`,
     `ForeignKeyEvaluator`. Every admire through session 8 was
     this mode.
  2. **V2-growth mode.** V1 has nothing; V2 extends. The "Existing
     test coverage" section is structurally absent — V1 has no
     contracts to honor — and replaced by a "what V2 needs and
     why" section plus V2-only contract tests. The migration is
     not migration but architectural growth, validated by
     end-to-end consumption rather than V1 differential.
     Example: rich profiling (session 9, ADMIRE.md 2026-05-12).
  3. **Hybrid mode.** V1 has partial coverage; V2 extends beyond
     it. The admire splits into "what V1 gives us" (with the
     V1-migration test discipline) and "what V2 adds" (with the
     V2-growth shape). When numeric distributions arrive in
     session 10 they would technically be hybrid — V1 has *zero*
     numeric evidence (a strict subset of the rich-profiling
     admire's gap analysis), so they cleanly inherit the V2-growth
     mode of the parent admire.

**Future admire entries name their mode at the top.** The mode
tells readers which template to use:

  - V1-migration: original ADMIRE format (What it does → V2
    placement → Inputs/outputs → Existing test coverage →
    Migration path → Edges).
  - V2-growth: extended format (What V1 collects → What V1 doesn't
    → What V2 needs → V2 extension shape → V2-only tests →
    Multi-session agenda → Edges).
  - Hybrid: both sections, side-by-side; the boundary between V1
    coverage and V2 extension explicit.

**Reasoning / consequences.** Naming the modes explicitly does
two things. First, it lets future admire authors pick the right
template upfront rather than discovering the mismatch midway.
Second, it makes the V1-vs-V2 boundary structural at the
documentation level — readers can scan ADMIRE.md and see at a
glance which entries are migrations and which are growth, and the
test discipline expectations follow accordingly. The session-9
admire stands as the V2-growth template; future V2-growth admires
follow its structure.

#### 2026-05-19 (session 23 amendment) — status framework extension for multi-session chapters in flight

The session-22 cross-document audit surfaced a framework gap: the
admire status framework was designed for chapters that complete in
a single bounded arc. Chapters that run for many sessions (the
OSSYS adapter chapter, for instance — five substantive slices
across sessions 18–22) accumulate work that is **clearly past
"chapter-open scoping"** but **not yet "extracted (...) confirmed"**.
The framework had no status that fit the in-flight state.

Without a fitting status, multi-session chapter entries either
understate (`chapter-open scoping` after five slices misleads) or
overstate (`extracted (differential confirmed)` premature when the
chapter has known remaining substantive work). The OSSYS ADMIRE
entry sat at `chapter-open scoping (session 17)` through session
22 because no better status existed in the framework.

**Decision: extend the framework with a partial-extracted status
for multi-session chapters in flight.** The status string:

  **`extracting (in flight, N slices)`**

where `N` names the count of substantive slices the chapter has
landed at the time of writing. The status is **explicit about
in-flight-ness**: future readers know the entry is current as of
N slices, not stable.

Naming choices considered:

  - `extracting (in flight, N slices)` — chosen. Active verb form
    ("extracting") symmetric with the past form ("extracted").
    `(in flight, N slices)` parameter gives concrete state. Reads
    naturally: `extracting (in flight, 5 slices)` for the OSSYS
    chapter at session 22 close.
  - `partially-extracted (chapter in flight)` — rejected. Compound
    past form is awkward; "partially-extracted" reads as a static
    fraction rather than active progression.
  - `in-progress-extraction` — rejected. Too long; reads as a
    noun phrase rather than a status.

The chosen form pairs cleanly with the existing four status
strings:

  | Status | When |
  |---|---|
  | `admired (placement decided)` | V2 placement chosen; no implementation yet |
  | `chapter-open scoping (session N)` | Chapter just opened; strategic frame + ADMIRE chapter scope landed; no substantive slices yet |
  | **`extracting (in flight, N slices)`** | **Chapter past chapter-open; substantive slices landing; not yet at chapter close** |
  | `extracted (differential confirmed)` | Chapter complete; differential tests confirm the contract |

**Update protocol.** When a chapter close lands, the status moves
from `extracting (in flight, N slices)` to
`extracted (differential confirmed)` (or the V2-growth /
hybrid-mode equivalent). When a substantive slice ships within
the chapter, the entry's `N` updates to reflect the new count.
Updates happen as part of each session's work, not just at chapter
close — keeping the status accurate is the responsibility of the
session that lands the slice.

**Worked example.** The OSSYS catalog producer entry transitions
from `chapter-open scoping (session 17)` → `extracting (in flight,
5 slices)` (session 23 application). The chapter close in
session 25 will transition it to whatever extracted-status applies
at completion. Future multi-session chapters follow the same
pattern.

**Reasoning / consequences.** The extension closes the
framework gap surfaced by the session-22 audit. Multi-session
chapters can keep their ADMIRE status accurate without forcing
premature `extracted` claims or misleading `chapter-open scoping`
holdovers. The framework is small (one new status string); the
update discipline is small (per-slice update of `N`); the audit-
trail compounds because future agents reading ADMIRE see the
chapter's progression at a glance.

The entry-template implications: the in-flight status's
"Existing test coverage" subsection should accumulate as fixtures
land, rather than being purely forward-looking. Forward-looking
shape ("V2's test surface for the adapter (when implemented):")
applies to `chapter-open scoping`; landed-shape ("V2's test surface
includes:") applies to `extracting (in flight, N slices)`.

## 2026-05-13 — Session 10 reflection: closed-DU expansion validated; forward signals

**Status:** decided (operating discipline; session 11 hand-off)
**Context:** Session 10's job was to land the second
`AttributeDistribution` variant (Numeric) end-to-end through every
layer of the rich-profiling pipeline. The session-9 reflection
asked whether the closed DU's seam was positioned correctly; the
session-10 brief framed adding the second variant as the
codification's "first real test." This entry records the answer
and the forward signals for session 11.

**Did the closed DU accommodate the second variant cleanly?** Yes.
The exhaustiveness checks lit up exactly where the codification
predicted, and the variant addition required updates at exactly
the sites a closed DU promises. The friction was zero on the
structural axis; the rough edges that surfaced are minor
operational concerns, not architectural ones.

**Empirical record of the closed-DU expansion.** Adding
`Numeric of NumericDistribution` to `AttributeDistribution`
required updates at:

  1. `Profile.tryFindCategorical` — F# exhaustiveness error;
     added `Numeric _ -> None` branch.
  2. `Profile.tryFindNumeric` — new helper, structurally symmetric
     to its categorical sibling.
  3. `Profile.tryFindDistribution` — *new* variant-agnostic helper
     that emerged as the natural lookup primitive for the
     emitter. Returns the first registered distribution by key,
     regardless of variant. Useful primitive; consumers (Faker,
     anomaly strategies) will reuse it.
  4. `DistributionsEmitter.writeDistribution` — F# exhaustiveness
     error; added `Numeric -> writeNumeric` branch.
  5. `ProfileStatistics.parseDistribution` — string-dispatch on
     "Kind" field; added "Numeric" branch alongside "Categorical".
     Coordinate resolution shared (single-function dispatch held).

Five sites; five updates; F# enforcement at the compile level on
sites 1, 2, and 4; deliberate update at sites 3 and 5. No
surprises. The codification's prediction (closed-DU expansion is
clean when the seams are positioned at the variant level, not at
the consumer level) held.

**Findings beyond the plan.**

1. **`Profile.tryFindDistribution` is a useful primitive that
   wasn't in the brief.** When adding the variant to the emitter,
   I reached for a variant-agnostic lookup rather than a chain of
   per-variant lookups. The helper emerged because the emitter
   doesn't care which variant the IR carries — it just needs to
   render whatever's there. Future Π (Faker, anomaly reports) and
   future strategies (distribution-aware tightening) will likely
   want the same primitive. The pattern: per-variant helpers for
   consumers that care about the shape; variant-agnostic helper
   for consumers that just need to dispatch.
2. **Variants currently share an `AttributeKey` field convention.**
   Both `CategoricalDistribution` and `NumericDistribution` carry
   `AttributeKey : SsKey` as their first field. This convention
   lets `tryFindDistribution` extract the key uniformly via a
   small private helper (`distributionKey`). If a future variant
   diverges (e.g., `JointDistribution` keyed by *two* attributes),
   the variant-agnostic lookup needs revision — the key isn't a
   single SsKey anymore. Document the convention so the next
   variant author knows the implicit rule and can surface a
   refactor if their variant breaks it.
3. **Intermediate-state commits worked but require discipline.**
   Between commit 2 (variant added with placeholder rendering)
   and commit 4 (real rendering), the emitter silently dropped
   numeric data. The placeholder was documented and tests
   confirmed the eventual fix — but the intermediate state was
   technically incorrect. Future variant additions should weigh
   atomic-but-incomplete (this session's approach) against
   bundle-everything-in-one-commit. The split approach is more
   reviewable and surfaces F# exhaustiveness issues at the right
   moment; the bundled approach lands the working feature
   sooner. No forced choice; surface the trade-off.
4. **Decimal as the percentile value type was right.** No
   floating-point drift surfaced; T1 byte-determinism held across
   repeats. The choice was made on session 10's first commit
   based on V2's existing decimal use; the milestone validates it
   end-to-end.

**The structural-commitment pattern's reach validated.**
`NumericDistribution.create` rejects monotonicity violations at
construction. The adapter's `Result.bind` chain surfaces the
rejection as an adapter error. The end-to-end test confirms a bad
fixture halts the pipeline with the constructor's error code, not
a silent degenerate Profile. The full pipeline trusts that every
`NumericDistribution` value satisfies the contract because every
path to its existence checked it. The pattern (AXIOMS.md
2026-05-12) compounds: every layer downstream gets cheaper to
write because invariants ride on every value.

**Forward signals for session 11.**

  - **The deferred-decisions cash-out trigger fires.** Session 11's
    first distribution-aware strategy is the fourth
    registered-intervention strategy (after Nullability,
    UniqueIndex, ForeignKey). Both deferred decisions from
    session 8 cash out together: the composition vocabulary
    (`fanOut`, `fallback`, etc.) and the generic
    `StrategyEvaluator` alias. The shared trigger discipline
    documented in DECISIONS 2026-05-11 should be honored —
    decide both questions empirically when the migration ships,
    not in isolation.
  - **The codified strategy layer's third real test.** Session 11's
    strategy migration uses the codification (DECISIONS 2026-05-11)
    as its rubric. Three previous registered-intervention strategies
    (Nullability, UniqueIndex, ForeignKey) validated the codification
    on session-8 evidence types (null counts, duplicate booleans,
    orphan flags). Session 11's strategy validates it on
    distribution evidence — a structurally richer Profile slice.
    If the codification's `evaluate` shape accommodates the new
    evidence type without revision, the codification's reach
    extends to rich profiling.
  - **Distribution-aware strategy candidates.** Several substantive
    options for session 11's strategy migration:
      - Categorical-aware uniqueness: distinct-count vs row-count
        heuristic for "candidate uniqueness" vs "spurious
        uniqueness." Per-attribute decision; consumes
        Categorical evidence.
      - Numeric-bounded mandatory check: an attribute with a
        narrow numeric range (P95 / P99 close to Max) might
        warrant a tighter constraint than a wide-tailed one.
      - Cardinality-aware FK: when the FK target's distinct
        values are below a threshold, the FK should perhaps be
        held to a stricter standard.
    Session 11's admire selects one (probably the categorical
    one — simplest seam, most testable).
  - **The closed-DU shape continues to hold.** Adding numeric
    didn't reveal a need for refactoring. Adding the third variant
    in a future session is the same shape: extend
    `AttributeDistribution`, update `tryFindDistribution` and
    `parseDistribution` and `writeDistribution`, write a
    smart constructor with structural-commitment validation,
    extend the milestone test. The pattern's repeatable.

**Reasoning / consequences.** Session 10's job was extension; the
discipline it tested was whether session 9's foundations would
support a second variant. They did. The closed-DU codification
(implicit before, explicit after session 8) absorbed the variant
without revision. The structural-commitment pattern's reach
extended through the full pipeline. The composition discipline
(`Result.bind` for adapter chaining; sibling-Π for emitter
extension) carried over. Session 11 inherits a strategy layer and
a rich-profiling foundation that have both been empirically
validated; the deferred decisions from session 8 cash out there.
Hold the cadence.

## 2026-05-13 — Closed-DU expansion: empirical confirmation, not a foregone conclusion

**Status:** decided (operating discipline; future-author trust signal)
**Context:** Session 10 added the second variant
(`Numeric of NumericDistribution`) to `AttributeDistribution`. The
expansion was clean — five sites required updates, F# enforced
exhaustiveness on three of them, two were deliberate non-matches,
no surprises. The session-10 reflection logged this as an empirical
record. This entry promotes it from a single-session finding to a
trust signal future authors can rely on.

**Decision:** **A clean closed-DU expansion is evidence, not
inevitability.** When the second variant lands without forcing a
DU reshape, splitting, or new-context threading through old call
sites, the seam was positioned correctly — the codification works
the way it claims. Future authors absorb the conventions
(`AttributeKey`-as-first-field for `AttributeDistribution`; the
analogous patterns elsewhere in V2) and trust that adding a third
variant follows the same shape.

**The empirical test for "well-positioned seam":**

  1. Adding a new variant requires updates **only** at sites that
     pattern-match on the DU. F# exhaustiveness errors are the
     compiler's enforcement.
  2. The new variant uses the **same shape of construction
     validation** as existing variants (smart constructor returning
     `Result<'a>`, structural-commitment invariants).
  3. The new variant uses the **same shape of consumer dispatch**
     in adapters and emitters (string-Kind branch in adapters;
     match-arm in emitters).
  4. **No callers outside the variant's own module need to change**
     to support the new variant beyond the exhaustiveness updates.
     If a caller's logic needs reshaping, the seam is wrong.

If a future variant addition violates any of these — e.g., the new
variant doesn't share the `AttributeKey` convention; the
construction validation has fundamentally different shape; consumers
need to thread new context through old sites — surface the
divergence and consider whether to refactor the DU before
proceeding.

**Reasoning / consequences.** The codification (DECISIONS
2026-05-11) made the strategy layer's shape explicit; this entry
makes the closed-DU expansion's empirical-test discipline explicit.
Together they let future agents work confidently inside the
patterns rather than re-deriving them from first principles. If the
patterns ever stop working, the discipline above is how the next
agent notices.

## 2026-05-13 — Emergent primitives earn their place through multi-consumer demand

**Status:** decided (operating discipline)
**Context:** Session 10 surfaced `Profile.tryFindDistribution` as a
useful variant-agnostic lookup helper that wasn't in the original
plan. It was added because the emitter needed it; future Π
(Faker, anomaly reports) and future strategies will likely reuse
it. The session-10 reflection (Finding 1) noted it as a small
example of a real principle worth naming.

**Decision:** **A primitive earns its place when a second consumer
needs it, not when the first one does.** This is the same threshold
the strategy-layer codification (DECISIONS 2026-05-11) and the
composition vocabulary deferral (DECISIONS 2026-05-11) both apply.
Generalize the discipline:

  - **First consumer** of a hypothetical helper: write the inline
    code. The cost of duplication-of-one is lower than the cost of
    speculative abstraction.
  - **Second consumer**: extract the helper. The duplication is now
    real; the abstraction's shape is empirically grounded; the
    third consumer (when it arrives) lands cleanly into the
    extraction.
  - **N-th consumer** (where N >= 3): the helper is canonical; new
    consumers reuse without question.

The principle is implicit in "IR grows under evidence" applied to
helper extraction; making it explicit gives reviewers a clean test
for "should I extract this?" and authors a clean test for "is this
ready to be extracted?" The answer in both cases: count consumers,
not anticipated callers.

**Worked examples in V2:**

  - `Profile.tryFindCategorical` (session 9): extracted because the
    emitter and the (eventual) strategy layer both needed it. Two
    consumers established at session 9.
  - `Profile.tryFindDistribution` (session 10): extracted because
    the emitter's variant-agnostic lookup pattern was the natural
    shape for the second variant. Two consumers anticipated; today
    only the emitter uses it. Borderline at extraction time, but
    the pattern's reuse path is clear (Faker emitter, anomaly
    strategies).
  - `fanOut` composition primitive (deferred): inlined in two pass
    drivers (NullabilityPass, UniqueIndexPass) at session 7; a
    third (ForeignKeyPass) at session 8. Threshold met; cash-out
    in session 11.
  - `StrategyEvaluator` alias (deferred): three strategies share
    the signature shape; cash-out in session 11.

**Counter-examples:**

  - The strategy registry mechanism (deferred at session 8): zero
    consumers have demanded it. Defer until N=4-6 strategies make
    the registry's lookup-by-name pattern useful.
  - Faker emitter (deferred to session 12+): the synthetic
    generator consumes evidence types that don't yet all exist.
    The "consumers" for distribution evidence today are the
    diagnostic emitter and (future) strategies; Faker waits.

**Reasoning / consequences.** Naming the principle prevents two
common failure modes: speculative abstraction (extracting on the
first consumer because "we might need it") and speculative
deferral (refusing to extract on the second consumer because "two
isn't enough yet"). Two consumers is the threshold; it's
empirically grounded; future authors can apply it without
re-litigating.

## 2026-05-13 — Decimal is the default for continuous statistical evidence

**Status:** decided (precedent)
**Context:** Session 10 chose `decimal` over `float` (or `double`)
for the percentile values in `NumericDistribution`. The choice was
made on session-10 commit 1 with brief rationale; the milestone
test (session-10 commit 5) validated it end-to-end with byte-
identical determinism across repeats. The session-10 reflection
flagged this as a small precision call worth marking as a
precedent.

**Decision:** **`decimal` is V2's default representation for
continuous statistical evidence.** New numeric evidence types
(temporal density bins, joint distribution coordinates, future
statistical primitives) use `decimal` unless the consumer has a
real reason to deviate.

**Rationale:**

  1. **Determinism across platforms.** `decimal` is a
     fixed-precision type with deterministic arithmetic across
     hosts; T1 byte-identity (a load-bearing V2 commitment, A17
     amended) requires this. `float` arithmetic varies subtly
     with CPU / runtime / compiler; bit-identical output is not
     guaranteed.
  2. **Exact representation of source values.** V2 attributes are
     `Integer` or `Decimal` at the IR level; both convert to
     `decimal` exactly. `float`/`double` introduce silent
     precision drift on integer values exceeding 2^53 and on
     decimal values that are not powers-of-two fractions.
  3. **Consistency with existing V2 numeric use.** V2's
     `NullBudget : decimal`, the existing numeric configuration,
     uses `decimal`. Distribution evidence consumed by the same
     algebra should use the same representation.

**When to deviate:** if a future consumer has structural reasons
that demand floating-point (e.g., interfacing with a downstream
numerical library that accepts only `double`), the deviation is
a documented exception in DECISIONS, not a silent re-litigation.
The default holds; the exception is explicit.

**Worked precedent:**

  - `NumericDistribution.{Min, P25, P50, P75, P95, P99, Max}`:
    `decimal`. Session 10 commit 1.
  - Future temporal evidence with continuous date/time values:
    consider `DateTimeOffset` for the date component (deterministic
    string roundtrip is the existing V2 convention from
    `ProbeStatus.CapturedAtUtc`); use `decimal` for any derived
    statistical scalar.
  - Future joint-distribution coordinates: `decimal × decimal` for
    paired numeric attributes.

**Reasoning / consequences.** Marking the precedent prevents the
question from reopening when the next numeric evidence type lands.
The choice is small but load-bearing for T1; making it canonical
saves the next agent a deliberation cycle and ensures consistency
across the rich-profiling agenda's growth.

## 2026-05-13 — Composition vocabulary cash-out: `fanOut` codified, four others deferred

**Status:** decided (deferred-decisions cash-out, session-8 trigger)
**Context:** Session 8 sketched five strategy-composition primitives
(`fanOut`, `fallback`, `accumulate`, `wrap`, `lift`) and deferred
implementation pending the two-consumer threshold (DECISIONS
2026-05-11 — composition vocabulary sketch). Session 11's fourth
registered-intervention strategy (`CategoricalUniqueness`) fired
the trigger condition. This entry records the cash-out per the
empirical pattern across all four pass drivers.

**Empirical state at the trigger:**

| Primitive | Consumers | Verdict | Disposition |
|---|---|---|---|
| `fanOut` | **4** (Nullability, UniqueIndex, ForeignKey, CategoricalUniqueness) | Threshold met by wide margin | **Codified** in `Projection.Core/Strategies/Composition.fs` |
| `fallback` | 0 | No strategy falls back to another | Deferred |
| `accumulate` | 0 | No strategy aggregates across other strategies | Deferred |
| `wrap` | 0 | No instrumented strategies | Deferred |
| `lift` | 0 | No context translation needed | Deferred |

**Decision: codify `fanOut`; defer the other four.** The
two-consumer threshold (DECISIONS 2026-05-13 — emergent primitives)
is the test; `fanOut` passes by a wide margin (4 consumers); the
others have no consumers and stay deferred until their first one
arrives.

**`fanOut` shape.** A `FanOutConfig<'context, 'config, 'decision,
'decisionSet>` record carries the strategy-specific functions
(intervention filter, sorted-context enumerator, evaluate seam,
empty decision set, decision-set wrapper, lineage event builder).
The `fanOut` function is the canonical iteration discipline:
observable identity on empty policy; per-(context × intervention)
fan-out; one event per decision; deterministic ordering; lineage
emission via `Lineage.tellMany`.

**Refactoring impact.** All four pass drivers refactored to
delegate to `Composition.fanOut`. The `run` functions become thin
wrappers that construct the FanOutConfig (capturing strategy-specific
context like `ForeignKeyRules.evaluate`'s catalog parameter via
closure) and invoke the primitive. Pass-driver behavior unchanged
(all 570 tests still pass after the refactor); 8 new tests in
`CompositionTests.fs` exercise the primitive directly via a
synthetic minimal strategy.

**Why the four others stay deferred:**

  - **`fallback`**: every strategy returns a *total* decision (one
    of the closed-DU outcome variants for every input combination
    — the "total decisions, named skips" core prediction from
    session-8 codification refinement 3). No strategy needs a
    fallback because no strategy ever returns "no decision."
    `fallback` becomes useful when a strategy can refuse to decide
    and another picks up — currently no consumer.
  - **`accumulate`**: the closed `TighteningIntervention` DU keeps
    each strategy's decision set independent. A pass that produces
    a unified per-attribute annotation by merging Nullability +
    CategoricalUniqueness decisions (e.g., a "column metadata"
    annotation pass) would need `accumulate`. No such pass exists.
  - **`wrap`**: per-strategy lineage subtrails / instrumentation /
    timing is not yet a need. Lineage discipline lives in the pass
    driver via `BuildEvent`; per-strategy diagnostics
    (`ProfilingInsight`-style) are not yet modeled in V2.
  - **`lift`**: every strategy operates on its natural context
    (`Attribute`, `Index`, `Reference`). No strategy's logic is
    reused across different context shapes. The eventual scenario
    — a Nullability-style strategy applied to view columns as a
    Kind variant — doesn't exist because views aren't yet in V2.

**Forward triggers:**

  - `fallback` ships when a strategy's `evaluate` returns
    `Outcome.Defer` (or equivalent "no opinion" variant) and a
    second strategy picks up.
  - `accumulate` ships when the second pass needs to consume
    multiple-strategy decisions at once.
  - `wrap` ships when per-strategy diagnostics emerge as a real
    concern (likely tied to the eventual `Diagnostics` writer
    monad).
  - `lift` ships when a strategy is reused across different IR
    granularities (e.g., a Nullability rule on view columns).

**Reasoning / consequences.** The codification of `fanOut`
empirically validates the strategy-layer codification's
ergonomics. The four pass drivers are now mechanically uniform:
they construct configuration records and delegate to one canonical
primitive. The deferral discipline (DECISIONS 2026-05-13 — emergent
primitives) gets its first real test: four candidates were
considered together; one was extracted; four were deferred with
explicit forward triggers. Future composition primitives follow
the same protocol — count consumers; codify when the threshold
hits; defer with forward triggers when it doesn't.

## 2026-05-13 — Generic StrategyEvaluator alias cash-out: codified

**Status:** decided (deferred-decisions cash-out, session-8 trigger)
**Context:** The second of two deferred decisions converging at
session 11 (DECISIONS 2026-05-11 — shared trigger). The first
(composition vocabulary) cashed out as `Composition.fanOut`. This
entry decides the second: the generic
`StrategyEvaluator<'context, 'config, 'decision>` alias.

**Empirical state at the trigger.** Four registered-intervention
strategies' `evaluate` signatures, mapped against the candidate
shape `string × 'config × 'context × Profile → 'decision`:

| Strategy | Natural rules-module signature | Fits the shape? |
|---|---|---|
| `NullabilityRules.evaluate` | `string -> NullabilityTighteningConfig -> Attribute -> Profile -> NullabilityDecision` | **Exactly** (with `'context = Attribute`) |
| `UniqueIndexRules.evaluate` | `string -> UniqueIndexTighteningConfig -> Kind -> Index -> Profile -> UniqueIndexDecision` | **With minor argument-tupling** (`'context = Kind × Index`) |
| `ForeignKeyRules.evaluate` | `string -> ForeignKeyTighteningConfig -> Kind -> Reference -> Catalog -> Profile -> ForeignKeyDecision` | **With closure-adaptation** (catalog captured by `FanOutConfig.Evaluate` lambda; `'context = Kind × Reference`) |
| `CategoricalUniquenessRules.evaluate` | `string -> CategoricalUniquenessConfig -> Attribute -> Profile -> CategoricalUniquenessDecision` | **Exactly** (with `'context = Attribute`) |

3 of 4 strategies fit the shape exactly or with minor tupling. 1
of 4 (ForeignKey) has an extra argument that adapts cleanly via
closure when constructing the FanOutConfig. The shape is real;
the divergence is handled mechanically; the codification's
"uniform signature shape but variable arity context" finding from
session-8 refinement 2 holds.

**Decision: codify the alias.** Per the discipline (DECISIONS
2026-05-13 — emergent primitives), the fourth empirical
confirmation earns the alias. Lands as
`type StrategyEvaluator<'context, 'config, 'decision> =
 string -> 'config -> 'context -> Profile -> 'decision` in
`Projection.Core/Strategies/Composition.fs`.

**What the alias does:**

  - **Names the canonical shape.** The four-input
    `(interventionId, config, context, profile)` shape is now
    nameable in code and conversation. Future strategy authors
    have a target signature.
  - **Types the `Composition.FanOutConfig.Evaluate` field.** The
    field becomes `StrategyEvaluator<'context, 'config, 'decision>`
    rather than the inline arrow type. Documentation and
    discoverability improve; behavior is unchanged.
  - **Lets future strategies declare conformance.** A new strategy's
    rules module can write
    `let evaluate : StrategyEvaluator<MyContext, MyConfig, MyDecision> = fun id cfg ctx prof -> ...`
    and get a compile-time check that the shape is preserved.

**What the alias doesn't do:**

  - **It doesn't force every rules module to refactor.**
    `ForeignKeyRules.evaluate` continues to take `Catalog` as a
    separate argument (its natural shape, given that FK decisions
    need cross-attribute reach for target-kind lookup). The
    FanOutConfig.Evaluate lambda closes over the catalog and
    adapts to the alias's shape. The "uniform signature shape but
    variable arity context" principle (session-8 refinement 2) is
    honored explicitly.
  - **It doesn't introduce structural enforcement beyond
    FanOutConfig.** The alias is documentary unless a strategy
    author chooses to type its `evaluate` against it.

**What might force a future revision.** If a fifth strategy's
evaluate genuinely cannot adapt to this shape (e.g., needs an
asynchronous context, returns multiple decisions per invocation,
or consumes a Diagnostics writer in addition to Profile), the
alias gets revisited. Per the codification discipline (DECISIONS
2026-05-11 — empirical verdict), divergence is a tell, not a
defeat.

**Reasoning / consequences.** Both deferred decisions from
session 8 have now cashed out. Composition vocabulary: `fanOut`
codified, four others deferred with forward triggers. Generic
alias: `StrategyEvaluator` codified as a type-level name for the
shape that's already enforced at the `FanOutConfig` boundary.
The strategy layer's codification is more thoroughly named after
session 11 than after session 8 — `fanOut` and `StrategyEvaluator`
are the new vocabulary. Future strategy migrations have less to
re-invent and more to inherit.

## 2026-05-13 — Session 11 reflection: codification's third real test passed; forward signals for session 12

**Status:** decided (operating discipline; session 12 hand-off)
**Context:** Session 11's job was the codification's third real
test — the first distribution-aware strategy
(`CategoricalUniqueness`) under the codified strategy layer +
the cash-out of two deferred decisions from session 8 (composition
vocabulary; generic alias). The session-11 brief asked whether
distribution-aware decision logic would stress the structured-
rationale DU pattern in a way binary-evidence patterns didn't.
This entry records the answer and forward signals.

**Did the codification's third real test pass?** Yes. The
codification absorbed the new evidence type, the new strategy, the
new pass driver, the composition primitive, and the generic alias
— without revision. Empirical record:

| Axis | Outcome |
|---|---|
| Closed-DU expansion (4th `TighteningIntervention` variant) | Clean — only `TighteningIntervention.id` needed exhaustiveness update; per-variant filter helpers used wildcard fall-through; closed-DU expansion empirical-test discipline (DECISIONS 2026-05-13) holds for the third time (after session 9's IsMandatory variant + session 10's Numeric variant) |
| Strategy-layer codification (4th instance) | All five core predictions held (pure functions, typed seam, structured rationale DUs, lineage discipline, `<Domain>Rules` naming + total decisions with named skips). No fourth refinement needed |
| Composition vocabulary cash-out (`fanOut`) | Earned its place at four consumers; codified; pass drivers now ~10 lines each instead of ~20 |
| Generic alias cash-out (`StrategyEvaluator`) | Earned its place; named the canonical shape; honored "uniform signature shape but variable arity context" (session-8 refinement 2) by adapting `ForeignKey`'s extra catalog argument via closure rather than forcing surgery |
| End-to-end milestone | All 585 tests pass; Categorical evidence flows through ProfileSnapshot.attach + ProfileStatistics.attach into the enriched Profile, the strategy decides per-attribute, the pass produces the decision set with full lineage discipline, sibling-Π commutativity preserved, T1 byte-determinism holds |

**Did distribution-aware decision logic stress the rationale DU
pattern?** Less than the user's brief anticipated. Three
observations:

1. **Confidence didn't surface as a separate dimension.** The
   user's forward note (session-10 brief) speculated that
   distribution-aware strategies might want a confidence concept
   alongside structured rationale ("this column's distribution
   suggests X with confidence Y"). For `CategoricalUniqueness`,
   confidence was implicitly modeled by the keep-reason variants
   themselves — `EvidenceMissing`, `VocabularyTruncated`,
   `DistinctCountBelowThreshold`, `DuplicatesObserved` are
   discrete bands of confidence (none / unsafe / insufficient /
   contradicted). The single positive variant (`EveryValueDistinct`)
   is itself a high-confidence signal. The DU absorbed the
   confidence spectrum without needing a separate scalar.
2. **Continuous evidence still discretized in the rationale DU.**
   `CategoricalDistribution.DistinctCount` is `int64` (continuous
   in the unbounded sense) but the strategy's decision flattens it
   to "above threshold" / "below threshold" / "matches total." The
   continuous evidence informs which discrete variant fires; the
   rationale DU stays discrete. This held for binary-evidence
   strategies and continues to hold here. If a future strategy
   wants to expose a numeric confidence score (e.g., "this is 80%
   likely to be unique based on coverage"), the variant gains a
   `confidence: decimal` field rather than the DU shape changing.
3. **Truncation as a first-class concern.** The strategy
   distinguishes `VocabularyTruncated` from `EvidenceMissing` —
   truncation is a known unknown (we have evidence but it's a
   prefix); evidence-missing is an unknown unknown (probe didn't
   succeed). This is finer than V1's binary "did the probe
   succeed" framing. Distribution-aware strategies have richer
   evidence; the rationale DU absorbs the richness without needing
   a confidence scalar.

**Verdict on the user's hypothesis:** the rationale DU pattern is
expressive enough for distribution-aware decisions at this
granularity. If a future strategy returns a numeric confidence
score (e.g., a Bayesian prior on "this column is unique"), it
likely lives as a field on the variant rather than as a separate
DU axis. Don't pre-decide; surface when the use case arrives.

**Forward signals for session 12 (Faker direction).**

  - **Two evidence types + four strategies** is the architectural
    state at session 11's close. Per session-9's "session 12+ for
    Faker holds" framing, the synthetic-data emitter is now
    plausible — categorical for low-cardinality, numeric for
    measurements, plus the strategy layer to drive the synthesis
    decisions.
  - **Faker as the third sibling Π that consumes Profile.** The
    Distributions emitter (session 9) consumed Profile for
    diagnostic output; Faker would consume Profile for synthetic
    *data* output. A18 amended (DECISIONS 2026-05-12) holds: Faker
    takes `(Catalog, Profile)`, not Policy. The synthesis
    parameters that *might* feel like policy (e.g., row-count
    target, deterministic seed) are emission configuration, which
    by A18 amended must live in a pass's output that Faker
    consumes — so a `SynthesisPlan` value produced by a future
    pass (or a Plan emitter parameter that doesn't qualify as
    Policy under the amended A18). Defer the architectural
    question; surface when the Faker work begins.
  - **Cardinality strategies likely for session 12 or beyond.**
    The session-10 brief listed cardinality-aware FK as a
    candidate; `CategoricalUniqueness` covered the per-attribute
    cardinality reasoning. Cross-attribute cardinality reasoning
    (the FK case) is a natural follow-up but not pressing.
  - **Joint distributions and temporal density** remain in the
    rich-profiling agenda (ADMIRE.md 2026-05-12). Faker's quality
    benefits from each; neither is required for a first cut.
    Session 12 picks one or proceeds without and accepts the
    limitations.

**Findings beyond the brief:**

  - **The `fanOut` extraction was a clean win.** Four pass drivers
    became thin wrappers; behavior preserved exactly (570
    pre-existing tests still pass after the refactor); the
    canonical iteration logic now lives in one place. The
    two-consumer threshold discipline (DECISIONS 2026-05-13)
    proved itself: extracting at four consumers gave both DRY and
    the empirical evidence that the abstraction was right.
  - **The `StrategyEvaluator` alias is documentary, not
    enforcement.** A type alias in F# doesn't constrain consumers
    that don't ascribe to it. The alias names the canonical shape
    for documentation and discoverability; structural enforcement
    happens at the `FanOutConfig.Evaluate` boundary. This
    distinction matters for future authors — write your evaluate
    against `StrategyEvaluator<...>` to get a compile-time check;
    the alias is opt-in.
  - **The "hybrid mode" admire works.** First admire under the
    three-mode framework (DECISIONS 2026-05-13). The boundary
    between V1-migration share (uniqueness domain inheritance) and
    V2-growth share (per-attribute distribution-driven inference)
    was clear; the admire's structure made the boundary visible;
    the test discipline (V2-only contract tests) followed
    naturally.

**Reasoning / consequences.** Session 11's job was validation
under pressure, and the codification + the rich-profiling vector
both passed. The strategy layer is now more thoroughly named
(`fanOut`, `StrategyEvaluator`) and more thoroughly tested (third
real test of the codification + first distribution-aware
consumer). Session 12 inherits a layered architecture where the
strategy infrastructure is solid; the rich-profiling foundation
has two evidence types operational; and the next big move — Faker
or third evidence type or cross-attribute strategies — has clean
empirical context to choose from. Hold the cadence.

## 2026-05-13 — Strategy-layer codification reaches stability mark

**Status:** decided (operating discipline; trust signal for future authors)
**Context:** Session 8's codification of the strategy layer carried
three refinements during its initial validation
(`RequireQualifiedAccess` on KeepReason DUs; variable-arity context
for evaluate; total-decisions/named-skips). Sessions 9 and 10
extended the rich-profiling vector under the codification; session
11 ran the codification's third real test through a genuinely new
domain (distribution-driven decisions on continuous evidence). No
fourth refinement was required.

**Decision:** **The strategy layer's codification is at its
stability mark.** The four core predictions (pure functions, typed
seam, structured rationale DUs covering the decision space, total
decisions with named skips) plus the recognized conventions
(Strategies/ folder placement, `<Domain>Rules` naming,
`RequireQualifiedAccess` on KeepReason DUs, FanOutConfig delegation)
have absorbed the variation a new domain brings without amendment.

**The empirical test for "stability":**

  1. The codification was named in session 8 (a descriptive pass
     over three existing instances).
  2. It was tested under closely-related variation through session
     10 (per-attribute / per-index / per-reference granularities;
     binary-question evidence).
  3. It was tested under genuinely new pressure in session 11
     (distribution-driven decisions, finer-grained evidence, hybrid
     V1-migration / V2-growth admire mode).
  4. None of those tests forced a fourth refinement. The absence of
     finding is itself the finding.

**What stability means in practice:** future strategy migrations
after this point inherit a codification that has been validated on
its central case (Nullability), on its variation case (UniqueIndex,
ForeignKey), and on its first new-domain case (CategoricalUniqueness)
without amendment. The pattern is now load-bearing in a way it
wasn't at session 10 close. Future authors absorb the conventions
(`AttributeKey`-as-first-field, `<Domain>Rules` suffix,
`Composition.fanOut` delegation, `StrategyEvaluator` alias for
typed seams) and trust they hold.

**What stability does not mean:** it does not mean the codification
is finished. New domains may surface new pressure. The next
amendment, if it comes, will be evidence; the current absence of
amendment is also evidence. Future agents should record either
outcome in this entry's lineage.

**Reasoning / consequences.** Empirical confirmation that an
abstraction holds is stronger than predictive confidence that it
will hold. Session 11's finding earns the codification its place
across the rest of V2 — it is no longer a discipline being tested,
it is a discipline being inherited.

### 2026-05-13 (session 13 amendment) — softening the claim to its evidence

**Status:** appended (forward-pointing refinement; original entry preserved)

The claim above ("the codification's stability mark") is true for what
the codification has been tested on. It is **softer than the entry's
phrasing makes it sound** when read against the shape of the testing.
Honest restatement:

  **The codification has absorbed three real instances within a
  coherent shape — per-record decisions keyed by a single SsKey,
  evaluated synchronously, returning one decision per (record ×
  intervention) pair.** The four core predictions held without forcing
  a fourth refinement *under that shape*. The empirical claim is
  bounded by the shape that was tested.

The three instances tested:

  - **NullabilityRules** — context `Attribute`; decision keyed by
    `Attribute.SsKey`.
  - **UniqueIndexRules / ForeignKeyRules** — context `Kind × Index`
    or `Kind × Reference (× Catalog)`; decision keyed by `Index.SsKey`
    or `Reference.SsKey`.
  - **CategoricalUniquenessRules** — context `Attribute`; decision
    keyed by `Attribute.SsKey`.

All three are per-record (the unit of decision is a single IR record),
single-key (one `SsKey` identifies the decision), synchronous (the
strategy returns immediately; no async / IO / writer-effect wrapping),
and single-decision-per-invocation (`evaluate` returns one
`'decision`, not a list).

**The genuine untested seams.** A heterogeneous fourth strategy that
breaks any of those three shape constraints might surface a fourth
refinement the codification cannot absorb without amendment:

  - **Multi-key strategies.** A `JointDistribution` strategy keyed by
    *two* `SsKey`s (e.g., FK pair statistics) — `'context` is two
    records, the decision is a relation, the lineage event needs to
    carry both keys. The current `Composition.fanOut` wraps a single
    iteration over a single `'context` enumerator; multi-key would
    likely need a different combinator.
  - **Async or effectful strategies.** A strategy whose `evaluate`
    needs to await an external probe, write a Diagnostics event, or
    emit a `Result<'decision>` — the current
    `StrategyEvaluator<'context, 'config, 'decision>` alias forecloses
    on this by typing the return as `'decision` directly. A strategy
    that returns `Async<'decision>` or `Result<'decision>` would force
    either a second alias or a generalization.
  - **Multi-decision-per-invocation strategies.** A strategy that
    decides multiple things from one input (e.g., a strategy producing
    both a Nullability decision *and* a UniqueIndex decision from the
    same evidence) — the current shape returns one decision; producing
    multiple would force the FanOutConfig to grow a `decisionList`
    accumulator or the strategy to be split.

**Disposition.** None of these heterogeneous-shape cases have a
consumer today. Per the two-consumer threshold (`DECISIONS 2026-05-13
— Emergent primitives`), we don't pre-absorb the refinement. The
amendment exists to mark the boundary of the claim:

  - "The codification has absorbed three instances within a coherent
    shape" — confirmed empirically.
  - "The codification will absorb the next instance" — confirmed
    *if and only if* the next instance shares the shape; otherwise the
    next instance is the codification's fourth real test, and the
    test's outcome is empirical, not predicted.

Future agents who read the original entry should also read this
amendment. The original's framing was earned at session 12 against the
evidence available; this amendment names the evidence's shape so the
next instance — when it arrives — is recognized as a fourth test, not
treated as a fourth confirmation by inertia.

## 2026-05-13 — Discrete-rationale DUs absorb continuous evidence by adding variants at meaningful inflection points

**Status:** decided (recognized property of the rationale-DU pattern)
**Context:** Session 11's brief speculated that distribution-aware
decision logic might stress the structured-rationale DU pattern by
forcing a confidence dimension alongside the discrete variants —
"this column's distribution suggests X with confidence Y." The
session-11 reflection found the opposite: confidence didn't surface
as a separate dimension because the keep-reason variants themselves
modeled discrete confidence bands. The pattern absorbed continuous
evidence without parametric confidence values.

**Decision:** **Structured-rationale DUs absorb continuous evidence
by adding variants at meaningful inflection points, not by carrying
parametric confidence values.** This is a recognized property of
the rationale-DU pattern; future strategy authors design new
variants around evidential thresholds rather than reaching for
numeric confidence scores.

**The worked example.** `CategoricalUniquenessKeepReason` distinguishes:

  - `NoCategoricalEvidence` — no observation at all (zero confidence)
  - `EvidenceMissing` — probe attempted, didn't succeed reliably
    (unknown confidence)
  - `VocabularyTruncated` — evidence is a known prefix; full
    vocabulary unknown (bounded-by-truncation confidence)
  - `DistinctCountBelowThreshold` — vocabulary too small to merit
    inference (insufficient confidence)
  - `DuplicatesObserved` — direct contradiction (negative confidence)

Five discrete bands, each named at a meaningful inflection point.
A parametric `confidence: decimal` field on a coarser variant would
have collapsed the structural distinctions into a number that
downstream consumers would need to re-discriminate.

**`VocabularyTruncated` distinct from `EvidenceMissing` is the
sharpest case.** Both fall in the "we don't know enough" region.
Conflating them under a single low-confidence variant would lose a
meaningful distinction: truncated evidence is a *known unknown*
(probe ran, capped the vocabulary at a configured limit; the
unobserved vocabulary may extend the distinct count); missing
evidence is an *unknown unknown* (probe didn't succeed; nothing
observed). Different variants because the consumer's response
differs.

**The principle generalizes:**

  - Continuous evidence (distinct counts, percentiles, sample
    sizes) flows in.
  - The strategy decides which discrete band the evidence falls
    into based on configured thresholds and structural commitments.
  - The variant that fires names the band.
  - Downstream consumers pattern-match on the variant; no parsing
    of confidence numbers required.

**When to deviate.** If a future strategy genuinely returns a
continuous-valued confidence (e.g., a Bayesian prior that callers
need as a numeric input to further computation), the variant gains
a `confidence: decimal` field rather than the DU shape changing.
The principle: continuous values live as fields on variants; the
variant identifies the regime, the field carries the magnitude.

**Reasoning / consequences.** Future strategy authors faced with
continuous evidence have a structural answer to "we need
confidence" — add the right variant, not a confidence number on a
coarser variant. This keeps rationale DUs pattern-match-friendly
and downstream consumers free of confidence-threshold parsing.
Joins the strategy-layer codification (DECISIONS 2026-05-11) and
the structural-commitment-via-construction-validation principle
(AXIOMS.md 2026-05-12) as a recognized operational primitive of
V2's reliability texture.

## 2026-05-13 — Chapter close: audit-by-subagent verification, drift findings, next-chapter priorities

**Status:** decided (chapter closure marker; routes findings to
next chapter)
**Context:** Session 12 ran a five-agent parallel audit (V1 input
contracts, V1 output contracts, V1 test coverage, architectural-doc
drift, build-graph and dependency hygiene) as the first formal
verification pass on the V2 sidecar after eleven build-and-validate
sessions. The synthesis lives in `CHAPTER_1_CLOSE.md` at the
projection root; the handoff letter for the next-chapter agent
lives in `HANDOFF.md`. This DECISIONS entry records the chapter
closure and routes the findings.

**Decision:** **The chapter ends. The next chapter opens with
CHAPTER_1_CLOSE.md and HANDOFF.md as orientation documents.**
Findings documented; resolutions deferred to the next chapter per
the leave-clean-ground discipline (don't fix in the audit session).

**Audit summary** (full detail in CHAPTER_1_CLOSE.md):

| Audit axis | Verdict |
|---|---|
| F#-pure-core / no-I/O-in-Core | Confirmed clean |
| Strategy-layer placement matches codification | Confirmed clean |
| Sibling Π independence (A18 amended) | Confirmed clean |
| Composition-pattern adherence (`fanOut` delegation) | Confirmed clean |
| Project reference graph (inward flow) | Confirmed clean |
| Closed-DU exhaustiveness | Confirmed clean (one acknowledged trade-off in `TighteningPolicy` filter helpers) |
| ADMIRE entry status strings | Drift — five of nine entries stale |
| README.md | Drift — materially behind eleven sessions of work |
| AXIOMS.md opening summary | Drift — still says "thirty-one axioms" |
| Three-mode admire framework adoption | Drift — only one entry strictly follows |
| V1 outputs without V2 equivalents | Documented backlog (Diagnostics writer is the gating dependency) |
| Transform registry deferral | Drift — trigger fired at N=4, codebase at N=10, no cash-out logged |
| Skip-stub asymmetry across V2 test files | Drift — three test files lack stubs the canonical pattern would prescribe |
| V1↔V2 adapter / emitter divergences | ~10 cosmetic-to-medium drifts without DECISIONS audit trail |

**Top 10 next-chapter priorities** (full detail in
CHAPTER_1_CLOSE.md §4):

  1. README.md absorbs the eleven-session state
  2. ADMIRE status sweep (5 entries) + mode annotations
  3. Skip-stub completion across V2 test files
  4. Two missing V2 TopologicalOrderPass tests (manual cycle, junction deferral)
  5. Transform registry cash-out (build or re-defer with rationale)
  6. Diagnostics writer scoping
  7. OSSYS catalog adapter ADMIRE stub
  8. Faker emitter (deferred until third evidence type)
  9. Build-graph and dependency hygiene cleanups
  10. Adapter / emitter divergence DECISIONS batch entry

**Discipline preserved through the chapter:**

  - **Audit before commit** (DECISIONS 2026-05-09 — Audits surface
    things not on the agenda). Session 12 is the first
    chapter-scale audit applying this discipline at the chapter
    boundary, not just the commit boundary.
  - **Leave clean ground, not perfect ground.** The audit
    documents drift; resolution belongs to the next chapter.
    Documenting findings is the audit's product; fixing them is
    different work with different tradeoffs.
  - **Documentation is the bridge.** A fresh agent inherits the
    codebase plus three documents (CHAPTER_1_CLOSE.md, HANDOFF.md,
    and the existing canonical trio AXIOMS / DECISIONS / ADMIRE).
    Honest documentation is the chapter's deliverable to its
    successor.

**Reasoning / consequences.** Chapter closure is a real
architectural event — it marks where the prior chapter's
accumulated judgment becomes documentation a fresh agent can
inherit. Without this marker, the next chapter starts with the
codebase but not the context; with this marker, it starts with
both. The chapter ends here.

## 2026-05-13 — Transform registry cash-out: deferral resolved as overtaken-by-evidence

**Status:** decided (deferred-decisions cash-out, session-13)
**Context:** `DECISIONS 2026-05-06 — Transform registry deferred until N≥4 passes`
committed to revisit the transform registry when N reached 4. The
codebase reached N=4 around session 6 and now stands at N=10
(`Passes/CanonicalizeIdentity`, `NamingMorphism`, `NormalizeStaticPopulations`,
`SymmetricClosure`, `TopologicalOrderPass`, `VisibilityMask`,
`NullabilityPass`, `UniqueIndexPass`, `ForeignKeyPass`,
`CategoricalUniquenessPass`). No subsequent DECISIONS entry either
built the registry or re-deferred with rationale. The chapter-close
audit (`CHAPTER_1_CLOSE.md §2.6`) flagged this as the most consequential
silent-trigger miss: an explicit deferral with a numerical trigger that
fired without a cash-out logged.

**Decision:** **The transform registry is not built. The deferral
resolves as overtaken-by-evidence.**

**Rationale.** The 2026-05-06 deferral was sized against a *single
linear pipeline composed via `>>`* — the masterwork's
`pass1 >> pass2 >> pass3` framing where the registry's value was
explicit ordering constraints, discoverability through reflection, and
startup validation across a unified pipeline. V2 did not evolve into
that shape. What V2 evolved into, instead, is **per-use-case driver
functions** that compose passes ad hoc:

  - The end-to-end milestone (`EndToEndDifferentialTests.fs`)
    composes `CanonicalizeIdentity → NormalizeStaticPopulations →
    SymmetricClosure → NullabilityPass → ...` for the Nullability
    use case.
  - The rich-profiling milestone (`RichProfilingEndToEndTests.fs`)
    composes a different subset for distribution-aware emission.
  - The strategy-driven passes (`NullabilityPass`, `UniqueIndexPass`,
    `ForeignKeyPass`, `CategoricalUniquenessPass`) all delegate to
    `Composition.fanOut` — but the inter-pass composition lives in
    each test's setup or in each emitter's `emitFromInput` helper, not
    in a global registry.
  - There is no single "the V2 pipeline" to register passes against;
    there are use cases (differential parity, rich profiling, future
    Faker, future DacFx milestone) that compose subsets of the
    available passes with different ordering and different evidence
    inputs.

The registry's value-proposition (one place to declare ordering;
reflection-driven discovery; centralized startup validation) was
written for a single-pipeline architecture. V2's per-use-case driver
pattern doesn't have a single pipeline; each use case names its own
composition explicitly, and the explicitness is itself the
documentation. A registry would either become a per-use-case-driver
*alternative* implementation (no value over what exists) or a
*global* layer above the drivers (an abstraction over abstractions
without empirical demand).

**The numeric trigger was right; the framing was overtaken.** N≥4
passes was the threshold predicted in the pipeline-style framing; in
the driver-pattern framing the question becomes "does any driver
benefit from registering passes by name?" and the answer is "no driver
demands it." The numeric trigger fired honestly; the framing it
referred to had been overtaken before the trigger was reached.

**The lesson.** Deferrals with numerical triggers need explicit
re-evaluation when the triggering condition is met, even if the
work has continued in directions that make the deferral feel
irrelevant. The 2026-05-06 deferral fired around session 6 (when the
fourth pass landed); no agent caught it until the chapter-close audit
in session 12. The structural answer is the new
**Active deferrals — index** at the top of this file (introduced in
session 13 alongside this entry). Future agents scan that table when
surveying priorities; if a trigger has fired silently, the cash-out
happens before substantive work continues.

**Reasoning / consequences.** This entry closes the deferral
explicitly. Future agents who read `DECISIONS 2026-05-06 — Transform
registry deferred` should follow the back-reference to this entry and
understand that the registry is not in V2's future under the current
architecture. If a future use case demands a global pass-registration
layer (e.g., the OSSYS catalog adapter exposes a multi-pipeline shape
the driver pattern can't absorb), this cash-out is itself reversible
under "IR grows under evidence" — but the demand has to surface, not
the trigger.

The active-deferrals index codifies the discipline so the same silent
miss does not recur for `RequireQualifiedAccess` retrofit, the
`CycleResolution.ResolutionStep.Reason` migration, the cross-catalog
FK refinement, or any composition primitive whose second consumer
arrives quietly.

## 2026-05-13 — Session 13 closing: doc-hygiene chapter-open landed; one substantive finding deferred

**Status:** decided (session 13 closing marker; routes findings to session 14)
**Context:** Session 12 (chapter close) routed three classes of work
to session 13: documentation hygiene (priorities 1–3); two
deferred-decisions cash-outs (priority 5 transform registry; the
stability-mark claim was self-flagged as possibly too strong); two
missing TopologicalOrderPass tests (priority 4). Session 13 inherited
the doc-hygiene work as the chapter-open opening — the prior agent's
explicit recommendation.

**What landed in session 13** (eight commits):

| # | Scope | Verdict |
|---|---|---|
| 1 | README.md absorbs eleven sessions of state | landed; F# adapters, four-axis Policy, actual project layout, strategy layer, rich profiling, composition primitives, A18 amended, Diagnostics writer flagged, OSSYS catalog adapter named |
| 2 | AXIOMS.md opening + A18 forwarding pointer | landed; opening now acknowledges A1–A34 / T1–T11 with five amended originals; A18 original points forward to its load-bearing amendment |
| 3 | ADMIRE status sweep | landed; five entries updated to `extracted (...)` with mode annotations under three-mode framework; EntitySeedDeterminizer entry now acknowledges `StaticAdapterDifferentialTests.fs` as the differential landing it promised |
| 4 | Skip-stub completion (V1NullabilityParity pattern) | landed in three test files; four new Skip stubs (UniqueIndex Aggressive mode, UniqueIndex included-columns boundary, ForeignKey DeleteRuleIgnore rationale, TopologicalOrder sanitized-effective-names) |
| 5 | Transform registry cash-out + Active deferrals index | landed; the deferral resolves as overtaken-by-evidence (V2 evolved into per-use-case driver functions, not a single linear pipeline); new index at top of DECISIONS lists ten active deferrals with trigger conditions so silent triggers get caught structurally |
| 6 | Stability-mark amendment | landed; appended forward-pointing refinement softening the claim to its evidence (three instances within a coherent shape — per-record decisions keyed by a single SsKey); names three genuine untested seams (multi-key, async, multi-decision) |
| 7 | TopologicalOrderPass V2 contracts reserved | landed as Skip stubs (not as full Behavioral tests); see substantive finding below |
| 8 | This entry — session 13 closing summary | landed |

**Test baseline:** 585 passed, 9 skipped, 594 total (was 585/3/588 at
chapter close). The 6 new skips (4 V1-divergence + 2 reserved) widen
test discovery's surface for V2's named divergences without changing
the passing-test count. Build green; no warnings beyond a pre-existing
nullness warning in `DistributionsEmitterTests.fs:126` that predates
session 13.

**Substantive finding from priority 4 (audit-during-validation).**
`CHAPTER_1_CLOSE.md §4 priority 4` listed "two missing
TopologicalOrderPass tests" with cost "moderate — depends on whether
OrderingPolicy already supports these knobs in V2 IR." It does not.
V2's Policy has Selection / Emission / Insertion / Tightening axes;
no Ordering axis. `TopologicalOrderPass.run` takes no Policy
parameter (it is `Catalog -> Lineage<TopologicalOrder>`). Manual-cycle
override has no representation. The junction-table heuristic is not
implemented; `OrderingMode.JunctionDeferred` is declared in the DU
but the pass never produces it. The two contracts ADMIRE flags as
Behavioral V2 translations are **features-not-yet-built**, not
just-missing-tests.

Disposition for session 13: reserve the contract names via Skip
stubs (commit 7). Implementation belongs to a substantive next-chapter
move under the audit-during-validation discipline — surface the
finding, name the disposition, defer the build to a session that
takes it as scope. The audit-induced expansion of priority 4 is
itself the discipline working.

**Recommended priorities for session 14, with rationale.**

  1. **Diagnostics writer.** Twelve sessions of consistent demand;
     gating dependency for at least seven concrete artifacts and
     pipelines (`decision-log.json`, `opportunities.json`,
     `validations.json`, `dmm-diff.json`, opportunity-stream half of
     UniqueIndex, operator-approval handoff for FK and Nullability,
     V1 nullability `Analyze()` pipeline). The deferral has been
     intellectually honest at every prior session ("don't build
     speculatively"), but the demand is consistent enough that
     building is now plausibly cheaper than continuing to defer. My
     read aligns with `CHAPTER_1_CLOSE.md §4 priority 6` and the prior
     agent's "I'd revisit if I had more time" item: the writer's
     value-prop is no longer speculative. Recommend session 14 scopes
     the writer (single-channel for now per the constitution; three
     channels later) and lands at least one downstream artifact
     consuming it (probably opportunity-stream because it's the
     cheapest first consumer).
  2. **OSSYS catalog adapter.** The undocumented production boundary
     (`CHAPTER_1_CLOSE.md §2.10` and `§4 priority 7`). V2 catalogs are
     today built by F# fixtures; production V2 needs to consume real
     OutSystems metadata via the V1 `outsystems_metadata_rowsets.sql`
     → `MetadataSnapshotRunner` → `SnapshotJsonBuilder` → V2 path. No
     ADMIRE entry covers this. Probably a session-14 ADMIRE stub; the
     implementation itself is a separate larger chapter.
  3. **OrderingPolicy axis + junction heuristic** (deferred priority
     from session 13). Cashes out the priority-4 work the audit found
     was bigger than ranked. The TopologicalOrderPass tests reserved
     in commit 7 then promote from Skip to `[<Fact>]`. Lower
     immediate-value than (1)/(2); could reasonably wait for session
     15 unless a real cycle-resolution use case forces it earlier.
  4. **Faker emitter.** Per `CHAPTER_1_CLOSE.md §4 priority 8` and the
     two-evidence-types-only constraint, defer until either a third
     evidence type lands or Faker proceeds with two and accepts the
     limitations. Not session 14 unless one of (1)/(2)/(3) opens it
     up.

The ranking (1) → (2) is gated on which produces more downstream
demand quickly; the prior agent's "Diagnostics writer first, OSSYS
catalog adapter second" framing matches my read after orientation.
Faker remains genuinely deferred.

**Disposition I inherited and am passing forward.**

  - **Audit during validation.** Discovered priority-4 was feature
    work not test work; logged the finding before pretending the
    full priority was met. Same discipline that produced five
    paydowns across sessions 4, 5, 7, 8, 11.
  - **Total decisions, named skips.** Six new Skip stubs across
    three test files now name V2 divergences and un-built features
    that previously lived only in ADMIRE prose.
  - **Documentation is the bridge.** Every commit in session 13
    leaves the docs honest enough that the next agent reading only
    the docs would understand what changed and why.
  - **Defer with structure, not just intention.** The
    "Active deferrals" index codifies the discipline that the
    transform-registry miss surfaced. Future trigger-fires get
    caught by table-scan, not by chronological re-read.

**Closing.** Session 13 was doc-hygiene plus two deferred-decision
cash-outs plus one audit-induced finding. The codebase is unchanged
(no code touched outside test-file Skip stubs). The documentation is
honest in places it was stale before. The deferred decisions index
exists so the silent-trigger failure mode does not recur. Session 14
inherits a chapter whose first substantive decision is whether to
build the Diagnostics writer, ADMIRE the OSSYS catalog producer, or
take a different opening based on demand the next agent reads from
the codebase. The doc-hygiene chapter-open is what it claimed to be:
not new architecture, just clean ground for the chapter ahead to
support more weight than the one behind.

Hold the spine.

— Session 13 (the doc-hygiene chapter-open)

## 2026-05-13 — Audit discipline refinement: contract-vs-implementation cross-reference

**Status:** decided (audit-discipline operating principle; refinement of `DECISIONS 2026-05-09 — Audits surface things not on the agenda` and the `CHAPTER_1_CLOSE.md` audit-by-subagent verification approach)
**Context:** Session 13's audit-during-validation produced a finding
the chapter-close audit (session 12) had missed: priority-4 work
("two missing TopologicalOrderPass tests") was actually feature-work
("two un-built V2 contracts — no `OrderingPolicy` axis, no
junction-table heuristic, `OrderingMode.JunctionDeferred` declared
but never produced"). The miss was not random. The session-12 audit
dispatched five parallel subagents against ADMIRE entries, V1 test
coverage, V1 input/output contracts, doc drift, and build-graph
hygiene — none of which cross-referenced ADMIRE-promised V2 contracts
against the implementation modules to verify feature-completeness.
The audit walked **contract → test** ("does the test exist?") but
did not walk **contract → implementation** ("does the feature the
test would assert exist?"). Both walks are needed; only one was done.

**Decision:** **Any audit that walks a contract-vs-test
cross-reference must also walk a contract-vs-implementation
cross-reference.** Without it, audits systematically undercount the
substantive backlog and present feature-work as test-work — exactly
what session 12's priority-4 entry did.

**The structural lesson generalizes.** ADMIRE entries promise V2
contracts in three modes (V1-migration / V2-growth / hybrid;
`DECISIONS 2026-05-13 — admire spectrum`). Each promised contract
has three states the audit must distinguish:

  | Contract → test? | Contract → implementation? | Diagnosis |
  |---|---|---|
  | Test exists | Implementation exists | Migrated; ADMIRE entry should be `extracted (differential confirmed)` |
  | Test exists | No implementation | Test will fail; the implementation is the gap |
  | No test | Implementation exists | Test gap; the audit's contract-vs-test walk catches this |
  | No test | No implementation | **Feature gap** — the audit's contract-vs-test walk **misclassifies this as a test gap** unless implementation is also walked |

The fourth row is the failure mode. Session 12 found the third row
on UniqueIndex / ForeignKey / Topological skip-stub asymmetry
(`CHAPTER_1_CLOSE.md §2.7`); session 13's skip-stub completion (commit
4) addressed the test gaps. Session 13's TopologicalOrderPass
finding was the fourth row — the implementation didn't exist; the
contract-vs-test walk reported "missing test" because there was
nothing to compare against.

**Discipline going forward:**

  1. **Chapter-close audits run two cross-references in parallel.**
     One subagent walks ADMIRE-contracts × V2-tests; another walks
     ADMIRE-contracts × V2-implementation. Findings are reported
     against both axes; the tabular form above lets readers see
     which row the finding falls into.
  2. **Contract-vs-implementation walks check three things:**
     module presence (does the named V2 module exist?), feature
     presence (do the IR types and policy fields the contract
     names exist?), and behavior presence (does the implementation
     produce the named outcomes? — `OrderingMode.JunctionDeferred`
     declared but never produced is the canonical anti-pattern).
  3. **The result of the two walks combines into a single
     priority-ranked findings list.** "Missing test, implementation
     exists" is mechanical to fix (add the test; lock the
     contract). "Missing implementation" is a substantive deferred
     decision that needs DECISIONS-entry routing, not test-priority
     ranking.

**The fresh-agent observation.** This miss came from someone who
had never read the code before. The session-12 chapter-close audit
was conducted by an agent with eleven sessions of accumulated
familiarity; the familiarity made the un-built `OrderingPolicy`
axis invisible-because-known. Session 13's fresh agent (no prior
context) cross-referenced ADMIRE → implementation as a normal part
of orientation — there was no familiarity to elide. The general
form: **fresh agents at chapter boundaries find things that the
prior chapter's accumulated familiarity hid.**

This argues for a structural disposition: chapter-close audits
should explicitly include a "fresh-eye walk" — either by a subagent
configured to ignore accumulated context, or by a fresh-agent
review at the chapter boundary itself. Session 13's read-in served
as a de facto fresh-eye walk; future chapter closes should make it
deliberate.

**Reasoning / consequences.** The audit-during-validation
discipline (`DECISIONS 2026-05-09`) caught the priority-4 miss
during session 13's work — exactly the failure mode it exists to
catch. This entry refines the *chapter-close audit* protocol so the
miss doesn't recur. The next chapter close (whenever it lands) will
benefit: contract-vs-implementation walk runs alongside
contract-vs-test walk; findings classify against the four-row
table; fresh-eye review is structural rather than incidental.

The Active deferrals index (`session 13 commit 5`) and this
audit-discipline refinement are paired: the index makes deferred
decisions visible across chapters; this refinement makes the
distinction between deferred-test-work and deferred-feature-work
visible within an audit. Both compound: the index catches silent
trigger-fires; this discipline catches misclassified findings.

## 2026-05-13 — Pass return-type codification: `Lineage<Diagnostics<'a>>` when the pass produces both

**Status:** decided (operating discipline; pass-codification refinement; preserves the false start so future agents recognize the temptation)
**Context:** Session 14 commit 4 needed to wire `UniqueIndexPass`
to the new Diagnostics writer (commit 3) so the V1 OpportunityBuilder
contract (V2 Skip stub reserved in commit 2) could activate. The
return-type question surfaced: should `UniqueIndexPass.run` keep its
existing `Catalog -> Policy -> Profile -> Lineage<UniqueIndexDecisionSet>`
shape and gain a sibling
`runWithDiagnostics : ... -> Lineage<Diagnostics<UniqueIndexDecisionSet>>`,
or should `run` itself migrate to the dual-writer shape?

**The false start I want recorded.** Initial choice was the sibling
function. The justification cited at the time: the closed-DU
expansion empirical-test discipline (`DECISIONS 2026-05-13 — Closed-DU
expansion: empirical confirmation`) — "the seam is positioned
correctly if F# exhaustiveness errors light up only at match sites
and no callers outside the variant's module need reshaping. If they
do, the seam is wrong and you're being told that." Twenty existing
`UniqueIndexPass.run` call sites in tests would have updated under
the return-type change, which felt like a violation of that rule. So
I picked the sibling — `run` unchanged, `runWithDiagnostics` as a
new entry point that internally wraps `run`'s output with
post-hoc-constructed diagnostic entries.

**Why the citation was wrong.** The closed-DU expansion discipline
is about *DU variant additions* — does the seam absorb a new variant
without forcing reshapes at consumer sites? That's a property of
variant-level changes against pattern-match sites. **Return-type
generalization is a different category.** The empirical test for
return-type changes is not "do callers reshape?" — it is "does the
type signature accurately name what the function produces?"

The two disciplines look superficially similar (both involve "do
callers change?") and the closed-DU one is load-bearing in V2's
recent codification, which made it the available rule when the
question came up. But the rules apply to different change shapes;
reaching for the closed-DU discipline on a return-type question is
a category error. Future agents who notice test ripple from a
return-type change and feel the pull toward the closed-DU rule
should pause and ask: **am I adding a DU variant, or am I changing
what the function produces?** The disciplines diverge there.

**Why the sibling shape is wrong long-run.** A sibling
`runWithDiagnostics` synthesizes diagnostic entries post-hoc from
the decision set — the diagnostics aren't truly "what the pass
produces," they're "what a wrapper produces from the pass's
output." That's a tell: the canonical entry point should return what
the pass actually does. More structurally, every pass that grows
diagnostic emission later (`NullabilityPass` activates V1 #6/#7;
`ForeignKeyPass` activates the DeleteRuleIgnore stub from session
13; `CategoricalUniquenessPass` whenever it surfaces an audit-trail
need) faces the same fork. The codebase ends up with
`Pass.run` (vestigial-by-construction; the historical lineage-only
shape) and `Pass.runWithDiagnostics` (the actual canonical entry
point) duplicated across four passes. The vestigial half stays in
test code forever because removing it would break callers — exactly
the test-stability bias that made the sibling tempting in the first
place, perpetuated.

**The right framing — the shape that names the production.** A
pass's return type should capture what the pass produces. The same
discipline names `A18 amended` (Π consumes whichever subset of
`Catalog × Profile` it needs — type signature names the inputs) and
`A32` (passes may produce values consumed by emitters — the
EnrichedCatalog or sibling value names the production). Type
signatures are honest: they name what flows in and out. Passes that
produce only decisions return `Lineage<'output>`. Passes that
produce decisions plus observer-relevant findings return
`Lineage<Diagnostics<'output>>`. The shape declares the production;
callers update mechanically when production changes.

**Decision:** **Passes return `Lineage<'output>` when they produce
only decisions, and `Lineage<Diagnostics<'output>>` when they
produce decisions plus observer-relevant diagnostics.** The variant
arrives at meaningful inflection points — mirrors `DECISIONS
2026-05-13` on rationale DUs absorbing continuous evidence (variants
at meaningful inflection points beats parametric values on coarser
variants); the same principle applied to function shapes. No
sibling-function half-measure.

**Worked example (commit 5).** `UniqueIndexPass.run` migrates from
`Catalog -> Policy -> Profile -> Lineage<UniqueIndexDecisionSet>` to
`Catalog -> Policy -> Profile -> Lineage<Diagnostics<UniqueIndexDecisionSet>>`.
The pass body now emits a `DiagnosticEntry` for every decision that
does not enforce uniqueness or that requires remediation (mirroring
V1 `OpportunityBuilder.TryCreate`). Test sites (~20 in test files)
update mechanically: `lineage.Value` becomes `dual.Value.Value`. A
small helper `UniqueIndexPass.decisionsOf` extracts the
`UniqueIndexDecisionSet` from the dual writer for tests that only
care about decisions; tests that care about diagnostics access
`dual.Value.Entries` directly.

**Forward signal.** When `NullabilityPass`, `ForeignKeyPass`, or
`CategoricalUniquenessPass` next grow diagnostic emission, they
follow the same migration. Don't add a sibling function. Change the
return type. Pay the test ripple. The cost is one-time; the
discipline is permanent. Each migration is independent — passes that
don't yet emit diagnostics keep their `Lineage<'output>` shape.

**The general rule, named.** When a function's category of output
grows (decisions → decisions + diagnostics; pure → effectful;
single-value → multi-value), change the signature to name the new
production. Test ripple is information about *where the function is
called from*; it is not evidence the seam is wrong. The closed-DU
discipline applies to DU variant additions; return-type
generalizations have their own discipline, and that discipline is
"name the production."

**Reasoning / consequences.** Recording the false start is itself
the discipline's value-add to future agents. The closed-DU rule will
be tempting again — it's load-bearing, it's recent, it's available.
The right reflex when a return-type change forces test ripple is
*not* to reach for closed-DU; it is to ask whether the new return
type names the production accurately. If yes, the ripple is the
cost of honesty in the type system. If no, the change is wrong and
the question is what the right shape is.

This entry pairs with the audit-discipline refinement (session 14
commit 1 — contract-vs-implementation cross-reference) as a session
that produced two operating-discipline entries before producing
substantive infrastructure. Both were named because both could
recur. Future agents inherit the disciplines and the false starts
together.

## 2026-05-13 — Named accessors for stacked types whose nested access loses self-description

**Status:** decided (operating discipline; smell-fix codification; preserves the false start)
**Context:** Session 14 commit 5 migrated `UniqueIndexPass.run` from
`Lineage<UniqueIndexDecisionSet>` to
`Lineage<Diagnostics<UniqueIndexDecisionSet>>` per the pass
return-type codification (`DECISIONS 2026-05-13` — pass return-type
codification). The migration's first cut updated test sites with
the literal access pattern `lineage.Value.Value.Decisions`. During
the work, the user surfaced the question: "is `lineage.Value.Value`
a code smell in that it's not self-descriptive?" The answer is yes,
and the disposition generalizes.

**The false start preserved.** The mechanical migration produced
~14 call sites of the form `lineage.Value.Value.Decisions`. Each
read forces the reader to count `.Value` projections to know which
writer they land in. The first `.Value` strips the outer `Lineage`
wrapper; the second strips the inner `Diagnostics` wrapper; the
third reaches `.Decisions` on the underlying `UniqueIndexDecisionSet`.
F# infers the types correctly, but the consumer expression encodes
no semantic intent — `Value` of a `Lineage<...>` and `Value` of a
`Diagnostics<...>` share a name, and the reader has to know the
field-naming convention to disambiguate.

The smell is real. The smell test that names it:

  **Would a reader of this expression need the type definition open
  in another window to know which level they're on?**

If yes, the access pattern is not self-descriptive and the
discipline is to provide named accessors that name the intent at
each level.

**Decision:** **Stacked types deserve named accessors at call sites
where nested access loses self-description.** Whenever a stacked
writer (or any nested type) creates a `.Field.Field` access pattern
at multiple consumer sites, and the structural shape requires the
reader to count nesting levels to know which level they're on, the
discipline is to provide module-level accessors that name what's
being reached for.

**The pattern shape:**

```
module <DualOrStackedType> =
    let <intentName1>  : <Stacked<'a>> -> <X>  = ...
    let <intentName2>  : <Stacked<'a>> -> <Y>  = ...
    let payload        : <Stacked<'a>> -> 'a   = ...   (the deep value, named)
```

For the dual writer `Lineage<Diagnostics<'a>>` (this commit's
example), the helpers are:

  - `LineageDiagnostics.payload      : Lineage<Diagnostics<'a>> -> 'a`
  - `LineageDiagnostics.entries      : Lineage<Diagnostics<'a>> -> DiagnosticEntry list`
  - `LineageDiagnostics.diagnostics  : Lineage<Diagnostics<'a>> -> Diagnostics<'a>`
  - `m.Trail` stays as-is (single Field at the outer level; already
    self-descriptive — the smell is specifically about nested
    repetition, not single access)

Domain-named shortcuts compose cleanly with the generic helpers.
`UniqueIndexPass.decisionsOf` delegates to
`LineageDiagnostics.payload`; the domain shortcut reads more
clearly than the generic accessor at consumer sites
(`UniqueIndexPass.decisionsOf lineage` over
`LineageDiagnostics.payload lineage`), but both are self-descriptive
and the underlying structure is asserted in one place.

**Where the discipline applies (and where it does not):**

  - **Applies:** consumer sites that read through a stacked type to
    a deep value. The named accessor declares intent.
  - **Does not apply:** structural assertion tests for the writer
    itself. `LineageDiagnostics.payload` is *defined* as
    `m.Value.Value`; the test that asserts the helper does what it
    claims must read `m.Value.Value` directly to verify the helper.
    Reaching past the helper at a structural-test site is the test's
    purpose.
  - **Does not apply:** single-level access where the field name is
    unambiguous in context (`m.Trail` for `Lineage<...>`,
    `m.Entries` for `Diagnostics<...>` — single Field access at the
    outer layer is self-descriptive).

**The general smell test, restated:**

  - One `.Field` access: usually self-descriptive; the field name
    carries the intent.
  - Two `.Field.Field` of the same name: smell; the reader counts
    levels to know which writer they're on.
  - Two `.Field.OtherField` of different names: usually fine; the
    second name disambiguates.
  - Three or more `.Field`: smell regardless of name uniqueness;
    nested access at depth loses structural intent even when each
    name is distinct.

The boundary is not an exact line; the test is whether a reader can
tell, from the expression alone, what's being reached for.

**Pairs with the pass return-type codification.** Honest signatures
+ readable consumers is the joint commitment. The pass return-type
codification (`DECISIONS 2026-05-13` — pass return-type) says:
*change the type signature when the production grows.* This entry
says: *provide named accessors when the new type's consumer pattern
loses self-description.* Together, the two disciplines keep both
the type system and the call sites honest.

**Why the false start is preserved.** The same temptation will
recur: future agents migrating to a stacked type will produce
`.Value.Value` access patterns by default, and the smell will read
as "F# being F#" rather than as a discipline gap. This entry exists
so the next agent recognizes the smell as soluble and not as a
language artifact. The named-accessor discipline applies; the smell
test is the trigger.

**A meta-pattern across session 14 entries.** This is the third
operating discipline session 14 has produced — alongside the
audit-discipline refinement (`DECISIONS 2026-05-13` —
contract-vs-implementation cross-reference) and the pass
return-type codification. All three followed the same pattern:

  1. The discipline surfaced during substantive work, not as a
     planned discipline-codification effort.
  2. The discipline was named and recorded *with the false start
     preserved*, so future agents recognize the temptation when it
     recurs.
  3. The substantive work continued under the new discipline before
     the commit shipped.

This meta-pattern itself is worth naming: **disciplines emerge from
the work, not from speculation about the work.** Audit-during-
validation (`DECISIONS 2026-05-09`) is the upstream discipline; the
three session-14 entries are downstream consequences of operating
that discipline at a chapter-open. Future chapters that operate
audit-during-validation should expect to produce disciplines of
this shape; recording them with their false starts is the
convention this session establishes.

**Reasoning / consequences.** The named-accessor discipline is now
named, codified, and discoverable from any call site that imports
`Projection.Core`. Future stacked-type designs (a third-channel
Diagnostics split when it lands; future writer compositions; deeply
nested IR records) inherit the convention: provide named accessors
at the consumer surface whenever the structural shape requires
counted projections at call sites.

## 2026-05-13 — Anticipation vs. speculation in abstraction extraction (refinement of the two-consumer threshold)

**Status:** decided (operating discipline; refinement of `DECISIONS 2026-05-13 — Emergent primitives earn their place through multi-consumer demand`)
**Context:** Session 14's discussion of object expressions for
hypothetical `ICatalogReader` and `IDiagnosticSink` interfaces
surfaced a question the two-consumer threshold doesn't directly
answer. The `ICatalogReader` case looks plausibly worth amortizing
up front (DACPAC support is named in V2's vocabulary docs;
`README.md` calls out "DACPAC, OData, or other sources later" as
the algebra's whole reason for using generic algebraic names; the
OSSYS adapter implementation chapter is the natural moment to make
the interface decision once before the function shape is calcified
by callers). The `IDiagnosticSink` case looks distinctly *not*
worth amortizing (writer-vs-sink semantics are genuinely
uncertain; the first real downstream consumer's shape will
constrain the design in a way speculation cannot). The two-consumer
threshold treated symmetrically would defer both; the cases are
not symmetric.

**The reframing:** **The two-consumer threshold is not "wait for
the second consumer to literally exist" — it is "wait for the
second consumer's *shape* to be visible enough to validate the
abstraction."** When the shape is visible, the threshold is met by
anticipation; when the shape isn't, speculation about the
abstraction is what the threshold guards against. The discipline
is against speculative abstraction, not against thoughtful
anticipation.

**Three positions for any abstraction-extraction question:**

| Position | What it means | When it applies |
|---|---|---|
| **A — Amortize fully now** | Define the abstraction (interface, helper, primitive, etc.) today; route all consumers through it. Pay full cost up front. | When the second consumer's shape *and* arrival are both highly probable within the next few sessions. Rare; usually a sign we're past the threshold and just hadn't noticed. |
| **B — Amortize structurally only** | Don't define the abstraction today, but design the function signatures / module shapes / value types so they map cleanly to the eventual abstraction. When the second consumer arrives, the abstraction lands as a one-line wrapper; no retrofit. Pay structural cost up front, defer concrete cost. | When the second consumer's *shape* is visible and validatable (we know what the abstraction would look like) but its *arrival* is not yet concrete. The discipline preserved: no speculative abstraction; the discipline relaxed: design with anticipated shape in mind. |
| **C — Defer fully** | Build whatever's natural for the first consumer; let the second consumer force the abstraction. Retrofit cost is real but small in F# (object expressions, type aliases, monad bindings all keep the cost low). | When the second consumer's shape is genuinely uncertain. The risk of premature naming is higher than the cost of retrofit. |

**Worked examples:**

| Abstraction | Position | Rationale |
|---|---|---|
| `ICatalogReader` (multiple catalog sources: OSSYS, DACPAC, OData, in-memory fixtures) | **B** | DACPAC's shape is concrete enough to design for. The OSSYS adapter's primary entry point should be `parse : string -> Task<Result<Catalog>>` — exactly the shape the future interface would have. Interface itself defers until a second source materializes; structural alignment lands in the OSSYS implementation chapter. |
| `IDiagnosticSink` (streaming consumers of Diagnostics entries) | **C** | Writer-vs-sink semantics are the deeper question; V2 chose writer (entries accumulate in a value; consumer reads). The first real downstream consumer (JSON manifest emitter, operator dashboard, telemetry consumer) will constrain whether sink semantics are needed. Three plausible futures, three different right answers. Wait for the first consumer to surface the question. |
| Composition primitives (`fallback`, `accumulate`, `wrap`, `lift`) | **C** | Sketched at session 8; deferred at session 11 commit cash-out (`DECISIONS 2026-05-13 — Composition vocabulary cash-out`). The first consumer's shape isn't visible — we don't know what the second pass that needs `accumulate` would look like, what the second strategy that needs `wrap` would instrument, etc. The shape isn't validatable; speculation would name the wrong abstraction. |
| `StrategyEvaluator` alias (now codified) | **B → A retroactively** | Sketched at session 8 (Position B; the shape was visible across three strategies); cashed out at session 11 commit 5 when the fourth strategy made the shape empirically real (`DECISIONS 2026-05-13 — Generic StrategyEvaluator alias cash-out`). The retrospective Position A landing was actually a Position B that ripened into A through real consumer demand. |

**The empirical test for "shape visible enough":**

  1. **Can you write the abstraction's signature without making
     contested choices?** If yes, the shape is visible. If you find
     yourself pausing on "should this be `Async` or sync?" or
     "should it return `Result` or throw?" — those are the contested
     choices that mean the shape isn't visible enough yet.
  2. **Can you predict the second consumer's call site without
     consulting an external source?** If yes, anticipation is
     grounded. If you're reaching for "well, it depends on what the
     downstream design is" — the second consumer's shape is
     speculative, not visible.
  3. **Would naming the abstraction now constrain the second
     consumer's design in ways you'd be confident about?** If yes,
     the abstraction earns its place by anticipation. If naming it
     now would force the second consumer into a shape that might be
     wrong — you're speculating, not anticipating.

**The discipline restated:**

  - Position B is acceptable when all three empirical tests pass.
    The structural cost (designing the function with the
    anticipated abstraction in mind) is small; the future cost
    saved (no retrofit) is real.
  - Position A requires both shape visibility AND a concrete second
    consumer. Without the concrete consumer, A is just speculation
    in disguise.
  - Position C is the default. When in doubt, defer; F# makes
    retrofit cheap.

**Why this refinement matters.** The two-consumer threshold has
served the codebase well — `fallback` / `accumulate` / `wrap` /
`lift` deferred at session 11 are still deferred at session 14
because no consumer has surfaced their shape; the discipline
caught what would have been speculative abstraction. But applied
*as a literal rule* it would also defer `ICatalogReader` even when
DACPAC is named in V2's vocabulary docs as a planned source. That's
treating anticipation as speculation, which loses information.

The refinement preserves the discipline's value (no abstractions
named on hope alone) while permitting Position B (structural
alignment when the shape is concrete enough). Future agents
applying the discipline have the three positions plus the empirical
test to choose among them; the choice is now nuanced, not
mechanical.

**Pairs with three other entries in this session:**

  - `DECISIONS 2026-05-13 — Emergent primitives earn their place
    through multi-consumer demand` — the original threshold this
    refinement extends.
  - `DECISIONS 2026-05-13 — Pass return-type codification` (session
    14) — also in the abstraction-design family; the discipline
    there is "the type signature names the production." This entry
    says the timing of when to introduce that signature follows
    the visibility-of-shape rule.
  - `DECISIONS 2026-05-13 — Named accessors for stacked types`
    (session 14) — the smell-fix discipline for nested access; the
    timing of when to extract a named accessor follows the same
    rule (when call sites recur enough that the accessor's shape
    is visible).

**Reasoning / consequences.** Future abstraction-extraction
decisions explicitly choose among A, B, or C and apply the empirical
test. The decision is captured in the relevant DECISIONS entry or
the commit message that introduces the abstraction. Documentation
of the position taken — and the test result — pays compound
interest when a future agent revisits the choice.

The general lesson: **disciplines refine through use, not through
restatement.** The two-consumer threshold was the right rule when
named; the refinement is the right rule now that one of its edge
cases (anticipation grounded in concrete planning) has surfaced.
The next agent who finds another edge case extends this entry or
writes a successor; the discipline is alive, not frozen.

## 2026-05-14 — Chapter-close ritual: the things to check at every chapter boundary

**Status:** decided (operating discipline; codifies the chapter-close ritual the prior chapter operated informally and the next chapter should operate explicitly)
**Context:** Session 14 (chapter-close audit, conducted in session 12)
caught the transform-registry deferral that had fired silently. The
session-13 audit-during-validation produced the
contract-vs-implementation refinement
(`DECISIONS 2026-05-13 — Audit discipline refinement`). Session 14's
operator-led reflection raised two more concerns:

  - The F#-feature-surface section in CLAUDE.md has re-open
    triggers; if those are not cross-referenced from the Active
    deferrals index, silent-trigger fires can recur in a different
    surface. (Now fixed; commit 1 of session 15 added the
    consciously-deferred features to the index.)
  - CLAUDE.md will drift the same way README.md did — a fresh agent
    rewrote the README at session 13 because eleven sessions of
    accumulated change had made it stale. CLAUDE.md is at higher
    risk because it indexes other docs that themselves change.

The fix-each-thing-once approach addressed both. But the underlying
problem is structural: chapter-close audits have run as ad-hoc
investigations, not as a codified ritual. The next chapter close
will benefit from a named, repeatable list of things to check.

**Decision:** **The chapter-close ritual is codified. Every chapter
close must execute the items below before declaring the chapter
done.** Items marked "load-bearing" must produce a written
finding (either "clean" or a remediation entry); items marked
"informal" are encouraged but not required.

### Load-bearing items

  1. **Active deferrals index scan.** Walk every entry in the
     Active deferrals index at the top of `DECISIONS.md`. For each:
     verify the trigger condition still describes the right
     condition; verify the current state still describes reality;
     if the trigger has fired since the last scan, log a cash-out
     entry. The transform-registry miss is the worked example of
     what happens without this scan.
  2. **Contract-vs-implementation cross-reference walk.** Per
     `DECISIONS 2026-05-13 — Audit discipline refinement`, every
     ADMIRE entry's promised V2 contracts must be checked against
     both the test surface AND the implementation surface. The
     four-row classification table (test×impl) routes findings:
     "no test, no implementation" is feature-gap; "no test,
     implementation exists" is test-gap; etc.
  3. **CLAUDE.md staleness check.** Walk every section of
     CLAUDE.md against the current state of the canonical surfaces
     it indexes. Reading order pointer still resolves to the right
     documents; operating-disciplines table still points at
     current DECISIONS entries; F#-feature-surface section still
     reflects what the codebase uses; programming-style center
     target still describes patterns visible in the code. If
     anything has drifted, fix it during the close — don't leave
     it for the next chapter.
  4. **README.md staleness check.** Same shape as CLAUDE.md but
     for the README — surface-level orientation. Session 13
     rewrote it after eleven sessions of drift; the discipline
     prevents that recurring.
  5. **HANDOFF.md / CHAPTER_1_CLOSE.md scope.** Each chapter
     produces its own HANDOFF letter and CHAPTER_CLOSE audit
     synthesis at the close. Chapter 1's CHAPTER_1_CLOSE.md (sessions
     1–12) lives at the projection root; chapter 2's belongs
     adjacent or under a chapter-numbered subfolder. The next
     chapter's handoff should not overwrite chapter 1's; the
     append-only documentation discipline is structural.
  6. **Fresh-eye walk.** Per `DECISIONS 2026-05-13 — Audit
     discipline refinement`, chapter-close audits explicitly
     include a fresh-eye walk — either by a subagent configured to
     ignore accumulated context, or by a fresh-agent review at
     the chapter boundary itself. Familiarity hides what fresh
     eyes find.
  7. **Operating disciplines table currency.** CLAUDE.md's
     operating-disciplines table must point at current DECISIONS
     entries by date. New disciplines added during the chapter
     are reflected; deprecated disciplines are removed or marked
     superseded.

### Informal items

  - **Test baseline diff.** Test count delta from chapter open to
    close, broken down by added vs migrated vs deleted. Useful for
    the chapter's quantitative narrative; not load-bearing.
  - **Forward-signal triage.** Each chapter close names forward
    signals for the next chapter; the ritual encourages but does
    not require ranking them with rationale.
  - **Discipline rent-paying check.** Per session-14 closing
    addendum's distinction between during-work disciplines and
    reflection-driven disciplines, the chapter close may include
    a brief check on whether each post-reflection discipline got
    used during the chapter — did it shape future code, or did it
    age without being consulted? Useful for catching descriptive
    orientation that didn't earn its keep.

### Where the ritual lives

  - **This DECISIONS entry** is the load-bearing surface; the
    ritual is structurally captured here.
  - **CLAUDE.md's "Chapter-close ritual" section** is the
    navigational pointer; future fresh agents read CLAUDE.md
    first and see the ritual indexed there. Substantive details
    stay in this entry.
  - **Each chapter's CHAPTER_1_CLOSE.md** records the result of
    running the ritual — clean items, remediation entries,
    findings.

### Why this entry exists

The chapter-close audit in session 12 caught real problems but
operated as an ad-hoc investigation. Session 13's reflection
flagged that the transform-registry miss happened because the
chronological-append discipline of DECISIONS doesn't surface "this
trigger has fired." The Active deferrals index addressed that
specific failure mode; this entry addresses the broader one — that
the entire chapter-close audit should be a codified ritual, not a
re-derivation each time.

The session-14 operator-led reflection raised two CLAUDE.md
maintenance concerns; both are real and both belong in the ritual.
Codifying the ritual now makes CLAUDE.md maintenance load-bearing
rather than aspirational; it converts the user's named worry into
a structural answer.

**Reasoning / consequences.** Future chapter closes execute the
seven load-bearing items and write the result. The ritual itself
will likely refine across chapters — items will be added, items
will be marked informal-now-load-bearing as the discipline matures.
Each refinement updates this entry or appends a successor; the
discipline is alive, not frozen.

The general lesson generalizes the audit-during-validation pattern:
**recurring audits codify into rituals; ad-hoc investigations don't
compound.** Rituals do — once codified, future iterations follow
the named pattern, contribute their findings, and refine the
ritual itself based on what surfaces.

## 2026-05-14 — DECISIONS is for resolved questions, not session narrative

**Status:** decided (operating discipline; corrects a drift introduced during session 14)
**Context:** Session 14 introduced session-closing reflections as
DECISIONS entries (commits 9 and 12) and session 15 followed with
its own reflection. These entries were narrative recaps —
commit lists, test baselines, forward signals, rent-paying checks
— with cross-references to the individual substantive entries
made during the session. The substantive content already lives in
those individual entries; the narrative wrapper duplicated it
once and then aged immediately.

The user surfaced the drift directly and asked for the session 14
and 15 reflections to be removed. Right call. Codifying the rule
so the drift does not recur.

**Decision:** **DECISIONS.md is for substantive resolved questions
only.** Session-narrative content — closing summaries, commit
lists, forward signals, rent-paying checks, "what surfaced this
session" — does not belong here.

**The substance test:** would this entry still be useful in six
months? If yes, it belongs in DECISIONS. If it ages with the
session, it belongs elsewhere.

  - **Substantive (in DECISIONS):** disciplines, refinements,
    cash-outs of deferrals, amendments to existing entries,
    codifications of patterns, decisions about specific design
    questions. Worked examples: pass return-type codification;
    named accessors for stacked types; anticipation vs.
    speculation refinement; chapter-close ritual.
  - **Session narrative (NOT in DECISIONS):** commit lists,
    test-baseline diffs, forward signals for the next session,
    "what surfaced during the work" recaps, rent-paying checks on
    specific disciplines, session-by-session reflections.

**Where session narrative belongs instead:**

  - **Commit messages.** In-flight findings during the work; the
    "what surfaced" content lands as part of the commit that
    addressed it. Disciplines named separately as their own
    DECISIONS entries.
  - **PR descriptions.** Summary of what shipped; forward signals
    for the next chapter; rent-paying observations.
  - **`HANDOFF.md` updates.** When a chapter closes and a new
    agent inherits, the bridge document captures the relevant
    context.
  - **`CHAPTER_1_CLOSE.md`** (or its equivalent for future
    chapters). Chapter-end audit synthesis lives there; that's
    where commit lists and forward signals belong.
  - **The conversation itself.** Reflections shared with the
    operator during the session — rent-paying checks, "did the
    discipline hold?" observations, "here's what I'd watch
    next" — are conversational, not durable. They go in the
    chat, not in DECISIONS.

**The drift that produced this entry.** Session 14's closing
entry was useful framing for session 15's opening — it named the
five disciplines and the meta-pattern about disciplines emerging
from work and reflection. But that framing lived in DECISIONS for
about three days before it was redundant; the disciplines
themselves were already documented in their own entries; the
forward signals were already in HANDOFF-style conversation. The
narrative wrapper aged faster than DECISIONS' append-only
discipline assumed.

The session 15 reflection compounded the drift — it extended the
narrative pattern with a rent-paying check structure that, while
useful as a check, shouldn't have lived in DECISIONS.

**Why it was tempting.** DECISIONS feels like the "official"
record of what a session produced; reflections naturally want to
live where the disciplines they observe live. The discipline-vs-
narrative line wasn't drawn explicitly; the drift happened by
default.

**The corrective rule, restated for the future:**

  Each substantive DECISIONS entry stands on its own. It is
  discoverable from the chronological log, from the Active
  deferrals index (if it codifies a deferral), from the operating
  disciplines table in CLAUDE.md (if it codifies a cross-cutting
  practice), and from cross-references in other entries. Session
  narrative does not need a separate substantive entry to be
  discoverable; the individual disciplines are already
  discoverable.

  When closing a session, the agent's reflection lives in the
  conversation with the operator, in the PR description, or in
  HANDOFF.md if the chapter is closing. The agent does not write
  a DECISIONS entry summarizing the session.

**Reasoning / consequences.** DECISIONS stays load-bearing only
where it earns rent. The narrative gets pruned (session 14 and 15
reflections removed in a follow-up commit). The discipline holds
going forward; future agents inherit the rule explicitly so the
drift does not recur.

The general lesson: **append-only disciplines need a complementary
prune-when-wrong discipline.** Append-only protects against
revisionism; prune-when-wrong protects against narrative drift.
The two pair: substantive content stays; narrative gets pruned
when the rule is violated.

## 2026-05-14 — Writer codification reaches its stability mark (heterogeneous third test held)

**Status:** decided (codification stability earned through three real tests; mirrors `DECISIONS 2026-05-13 — Strategy-layer codification reaches stability mark`)
**Context:** The Diagnostics writer landed at session 14 commit 3
with three predictions about how the dual-writer pattern would
behave: (1) the pass return-type codification (`Lineage<'output>`
vs `Lineage<Diagnostics<'output>>`) would absorb pass migrations
mechanically; (2) the named-accessor discipline would keep call
sites readable through migrations; (3) the Skip-to-Behavioral
activation pattern would be mechanically repeatable for new
consumers. Session 14 (UniqueIndex) and session 15 (Nullability)
provided two real tests of these predictions; both passed. The
codification was held back from a stability claim per `DECISIONS
2026-05-13 — Stability mark amendment` — N=2 within a coherent
shape (per-record decisions keyed by single SsKey, both emitting
on failure-side variants of their outcome DUs) earns less than
N=3 with at least one heterogeneous instance.

Session 16 ran the third test on ForeignKey, deliberately chosen
to be heterogeneous in emission shape: ForeignKey emits diagnostics
on **both** failure-side keep-reasons (mirroring UniqueIndex /
Nullability) **and** on a success-with-caveat variant
(`EnforceConstraint(ScriptWithNoCheck(orphanCount))`) within a
single pass. The substantive question: does the writer absorb
both shapes side-by-side without structural refinement?

**Decision:** **The Diagnostics writer codification reaches its
stability mark.** The four core predictions all held under the
heterogeneous third test:

| Prediction | ForeignKey outcome |
|---|---|
| Pass return-type codification absorbs the migration | ✓ ForeignKeyPass.run migrated to `Lineage<Diagnostics<ForeignKeyDecisionSet>>` mechanically; ~14 test sites updated via sed; no refinement to the writer or the codification required |
| Named-accessor discipline keeps call sites readable | ✓ `LineageDiagnostics.payload`, `ForeignKeyPass.decisionsOf` — same shape as the prior two passes; no smell-fix discoveries |
| Skip-to-Behavioral activation is mechanically repeatable | ✓ The session-13 Skip stub redirected to V2's actual success-with-caveat case (the V1 anchor was unreachable from V2 fixtures); the activation flipped to `[<Fact>]` cleanly |
| Diagnostic emission shape absorbs heterogeneity | ✓ Success-with-caveat (`EnforceConstraint(ScriptWithNoCheck _)`) and keep-reason (`DoNotEnforce(...)`) emissions produce structurally identical `DiagnosticEntry` values (same Source / Severity / field shape); only the `Code` prefix routes them. No structural distinction needed at the entry level |

**The empirical test for stability — the same one the strategy-layer codification used:**

  1. The codification was named in session 14 (a descriptive pass
     after the first instance — UniqueIndex).
  2. It was tested under closely-related variation through session
     15 (Nullability — second instance with similar shape, both
     failure-side keep-reasons within "per-record decision keyed
     by single SsKey" shape).
  3. It was tested under genuinely new pressure in session 16
     (ForeignKey — third instance with heterogeneous emission:
     keep-reasons + success-with-caveat within one pass, plus
     two reserved-but-unreachable variants for IR-refinement
     completeness).
  4. None of those tests forced a refinement. The absence of
     finding is itself the finding.

**What stability means in practice.** Future writer-consumer
activations after this point inherit a codification that has been
validated on:

  - Its central case (UniqueIndex per-index granularity, failure-
    side emission on PolicyDisabled / DataHasDuplicates / etc.)
  - Its variation case (Nullability per-attribute granularity,
    same failure-side shape with one audit-worthy
    KeepNullable(RelaxedUnderEvidence))
  - Its heterogeneous case (ForeignKey per-reference granularity,
    failure-side AND success-with-caveat emission within one pass)

Future agents migrating the fourth pass to the dual writer (likely
CategoricalUniqueness if a use case demands diagnostic emission, or
a future pass migrating from a third-channel split if that lands)
absorb the codification's conventions and trust they hold:
`Lineage<Diagnostics<DecisionSet>>` shape; `opportunityEntry`-style
mapping function; `LineageDiagnostics.payload` named accessor;
`Pass.decisionsOf` domain shortcut; same closed-DU exhaustiveness
discipline.

**What stability does not mean** (preserving the session-13 amendment's framing):

  - It does not mean the codification is finished. New pressure
    may surface refinements — the three-channel split (operator /
    auditor / developer per the constitution) is the most plausible
    refinement vector when a real consumer demands per-channel
    routing. The `Severity` field's three-way DU (Info | Warning |
    Error) may grow if a fourth band emerges. The `Metadata` field's
    `Map<string, string>` may promote to a typed DU when a consumer
    demands typed payload.
  - The stability claim is bounded by what's been tested. The three
    real tests were all single-channel synchronous emission with
    `Map<string, string>` metadata. Multi-channel, async-emitting,
    or typed-metadata consumers would be fourth-test-shaped.

Within those bounds, the stability mark is earned.

**Reasoning / consequences.** The Diagnostics writer's codification
has now been validated on the same empirical pattern the strategy-
layer codification was: descriptive pass → variation case →
heterogeneous case → no fourth refinement required. Future writer
work inherits a codification that holds; future stability marks for
other codifications follow the same N=3-with-heterogeneity protocol.

**The general lesson:** **stability claims earn their place through
heterogeneous third tests, not structurally-similar third tests.**
A third consumer with the same shape as the first two adds confidence
to a coherent-shape claim but doesn't extend the claim. A third
consumer with a different shape (like ForeignKey's success-with-
caveat alongside keep-reasons) tests whether the codification's
seams are positioned to absorb variation, not just to repeat the
same pattern. The protocol should be honored in future codification
work — when designing the third real test of any codification, pick
the case that stresses the seams you suspect, not the case that
confirms what you already know.

## 2026-05-14 — opportunityEntry stays inlined: N=3 of two distinct shapes, not N=3 of one

**Status:** decided (extraction question evaluated empirically; defer)
**Context:** Three passes (UniqueIndex, Nullability, ForeignKey)
each have a private `opportunityEntry` function that maps decisions
to `DiagnosticEntry option`. At surface count this is N=3 — the
two-consumer threshold (`DECISIONS 2026-05-13 — Emergent primitives`)
plus the anticipation-vs-speculation refinement (`DECISIONS
2026-05-13 — Anticipation vs. speculation`) suggest extraction
becomes a question at N=3. The session 14 reflection (now pruned;
preserved in commit history) and the session 15 reflection both
flagged the question for explicit evaluation here.

The substantive question — does the opportunityEntry-style mapping
earn primitive extraction at N=3? — has a more nuanced answer than
naive consumer-counting.

**The empirical inventory of the three opportunityEntry functions:**

| Pass | Input type | Mapping shape | Code prefix |
|---|---|---|---|
| UniqueIndex | `UniqueIndexDecision` | `match decision.Outcome with` → `EnforceUnique _` → None; `DoNotEnforce reason` → match-on-reason → entry | `tightening.uniqueIndex.<reason>` |
| Nullability | `NullabilityDecision` | `match decision.Outcome with` → `EnforceNotNull _` → None; `KeepNullable reason` → match-on-reason → entry (3 of 3 reasons handled, 2 emit None); `RequireOperatorApproval conflict` → match-on-conflict → entry | `tightening.nullability.<reason>` |
| ForeignKey | `ForeignKeyDecision` | `match decision.Outcome with` → `EnforceConstraint evidence` → match-on-evidence → entry-or-None (3 of 3 evidence handled; 1 emits Some, 2 emit None); `DoNotEnforce reason` → match-on-reason → entry (7 of 7 reasons handled, all emit Some) | `tightening.foreignKey.<reason>` |

**The shape distinction.** UniqueIndex and Nullability share a
deeper shape: only the failure-side variant of the outcome
emits-with-payload-mapping. The positive-side
(`EnforceUnique _` / `EnforceNotNull _`) collapses to `None`
without further inspection. ForeignKey is structurally different:
the positive-side (`EnforceConstraint evidence`) requires
inspection because one of three evidence variants
(`ScriptWithNoCheck _`) emits-with-payload while the other two
collapse to `None`.

**Two shapes, not one:**

  - **Shape A (N=2 — UniqueIndex, Nullability)**: positive-side
    is uniformly None; only the failure-side discriminates. The
    extracted primitive would be roughly:
    ```fsharp
    type DiagnosticPolicy<'outcome, 'failure> = {
        IsFailure : 'outcome -> 'failure option
        FailureToEntry : 'failure -> DiagnosticEntry
    }
    ```
  - **Shape B (N=1 — ForeignKey)**: both positive-side and
    failure-side discriminate; positive-side has at least one
    success-with-caveat. The extracted primitive would be roughly:
    ```fsharp
    type DiagnosticPolicy<'outcome> = {
        OutcomeToEntry : 'outcome -> DiagnosticEntry option
    }
    ```

The Shape-B form actually generalizes Shape-A (every Shape-A
function trivially fits the Shape-B signature). But the extraction's
ergonomics suffer at Shape-A consumers — they'd have to write
boilerplate handling positive-side variants that always collapse to
None.

**Decision:** **Defer extraction. The apparent N=3 is N=2-of-shape-A
plus N=1-of-shape-B; neither shape has reached the two-consumer
threshold within itself.**

The honest interpretation of the codebase's emission patterns:

  - Shape-A has two consumers (UniqueIndex, Nullability). At N=2
    the two-consumer threshold suggests the abstraction earns its
    place — but only within Shape-A.
  - Shape-B has one consumer (ForeignKey). At N=1 the threshold is
    not yet met.
  - Extracting a primitive that subsumes both shapes (the Shape-B
    generalization) would force Shape-A consumers to write boilerplate
    they don't need today; that's the "speculative abstraction"
    failure mode the discipline guards against.
  - Extracting a Shape-A-only primitive would leave ForeignKey
    inlined; the inconsistency creates its own friction.
  - Inlining all three preserves the per-pass clarity (each
    `opportunityEntry` is locally readable) at the cost of
    duplication — but the duplication is small (~30 lines each)
    and stable.

**The forward trigger for re-evaluation:**

  - **A fourth pass** (e.g., CategoricalUniquenessPass migrating to
    diagnostic emission, or a future pass like a Faker emitter that
    co-emits diagnostics) gives the question a fourth data point. If
    the fourth pass fits Shape-A, that's N=3 of Shape-A — extraction
    earns its place within Shape-A; ForeignKey's Shape-B remains
    inlined as the heterogeneous case. If the fourth pass fits
    Shape-B, that's N=2 of Shape-B — extraction earns its place
    within Shape-B; the Shape-A passes can opt into the same
    primitive at the cost of trivial boilerplate, or stay
    Shape-A-extracted.

  - **A consumer outside the pass layer** (e.g., the Faker emitter
    consuming decisions plus diagnostics from upstream passes; or a
    CLI shell composing per-strategy diagnostics across passes)
    might surface a need for a primitive that operates on
    `DiagnosticEntry list` rather than on outcome-to-entry mapping.
    That would be a different abstraction question entirely.

**Position B re-applied (per `DECISIONS 2026-05-13 — Anticipation vs. speculation`).**

  - Position A (extract fully now): wrong — the apparent N=3 is
    actually N=2+N=1 of distinct shapes; extracting against either
    shape introduces speculative cost.
  - Position B (structural alignment without extraction): the
    three opportunityEntry functions already share a structural
    shape — same input/output types modulo decision-type
    parameter, same use of `match decision.Outcome`, same
    `mkEntry` helper pattern. A future agent extracting can do so
    mechanically. No code change needed today; the structural
    alignment is honored by inlined consistency.
  - Position C (defer fully): the chosen disposition. Inlining at
    N=3-of-two-shapes preserves clarity; extraction becomes a
    question again at N=4 with concrete shape evidence.

**Reasoning / consequences.** Naive consumer-counting would have
extracted at N=3 and produced a primitive that one of three
consumers fits awkwardly. Looking at the shape distinction reveals
that N=3 is the wrong count; the right counts are N=2 and N=1.
The two-consumer threshold (within a shape, not across shapes) is
honored by deferring; the anticipation-vs-speculation refinement
is honored by recognizing that ForeignKey's heterogeneity is real
shape variation, not a misclassification.

**The general lesson:** **count consumers within a shape, not
across shapes.** When evaluating extraction, the question is "do N
consumers share the same shape such that one abstraction serves
them all without forcing accommodation?" Two consumers with the
same shape and one with a different shape is N=2-and-N=1, not N=3.
The discipline against speculative abstraction extends to
classifying consumers by shape before counting.

## 2026-05-15 — Strategic frame for the OSSYS implementation chapter (architectural commitments)

**Status:** decided (strategic frame; load-bearing for the OSSYS arc and beyond)
**Context:** Session 17 opens the OSSYS catalog adapter implementation
chapter. Multiple architectural commitments emerged from conversation
between session 16's close and session 17's opening; none had landed
in DECISIONS yet. This entry codifies them so they exist as
load-bearing context for the OSSYS arc and for the chapters that
follow (data emission, deployment integration, validation).

The frame is **strategic, not implementation-spec.** Specific
implementation choices land as their own DECISIONS entries when
those chapters open. This entry names the architectural axes;
subsequent entries fill them in.

### Posture 1, extended — V2 emits artifacts; deployment is downstream; the canary is upstream

The original Posture 1 stance: **V2 emits artifacts; ADO/Octopus
deploys to dev/staging/prod; V2 is not in the deployment path.**
The boundary is structural — V2's job ends when the artifacts are
written; downstream tooling owns the deploy.

The session-17 extension adds an **upstream pipeline canary**:
before V2 publishes the artifacts, the export pipeline self-validates
the artifacts against an ephemeral Docker SQL Server instance. The
artifacts must apply cleanly against an empty database; if the
canary fails, the export halts and the artifacts are not published.

The canary is **upstream of publication, not downstream of
deployment.** The deployment path remains ADO/Octopus territory;
the canary is the export pipeline's own self-validation. This
addresses a real failure mode V1 doesn't catch — artifacts that
look correct in isolation but don't apply cleanly together.

The canary's mechanism:

  - Spin up an ephemeral SQL Server container (testcontainers).
  - Apply the emitted artifacts (schema first, then seeds, then
    bootstrap) using DacFx for schema and direct script execution
    for data.
  - Read the resulting database state back through a read-side
    adapter (see below) into a V2 Catalog.
  - Compare the read-back Catalog to the source-of-truth Catalog
    that produced the artifacts. Any discrepancy halts the
    export.

The canary is **opt-in** at first — declared on EmissionPolicy or
its successor — but the architectural axis is named here so future
chapters know where it fits.

### Read-side adapter as a new architectural axis

The OSSYS adapter is the **write-side ingestion path**: take
OutSystems metadata, produce a V2 Catalog. The **read-side
adapter** is its sibling: take a SQL Server database, produce a
V2 Catalog by reading schema metadata back. Two distinct adapters,
both producing `Result<Catalog>`, both at the boundary.

The read-side adapter has **two consumers from day one**:

  1. **The canary's read-back step** (described above). The export
     pipeline writes artifacts, applies them to an ephemeral SQL
     Server, and reads back the resulting state to compare against
     the source Catalog.
  2. **Optional production observation.** A future operator might
     point V2's read-side adapter at a production database to
     observe the deployed schema's actual shape — useful for
     drift detection, post-deployment audits, and the V1
     `dmm-diff.json` equivalent.

Two consumers from day one is exactly the threshold the
two-consumer rule predicts (`DECISIONS 2026-05-13 — Emergent
primitives` and the session-16 shape-classification refinement).
The read-side adapter earns its place architecturally, not
speculatively.

The read-side adapter is **not in scope for the OSSYS chapter
opening** — it's a sibling architectural commitment named here so
the OSSYS write-side adapter doesn't accidentally calcify in a
shape that the read-side can't mirror.

### Refactor.log emission with deterministic SsKey-to-GUID via UUIDv5

V1 emits a `refactorlog` artifact (per the SSDT pattern) tracking
schema-rename events. The artifact requires GUIDs to identify
renamed objects.

V2's refactor.log emission uses **UUIDv5** to derive GUIDs
deterministically from `SsKey` values plus a stable namespace.
The choice eliminates a class of state V2 would otherwise need to
maintain — V1 tracks (or risks losing) GUID-to-object mappings
across runs; V2 derives the GUID at emission time and the same
SsKey always produces the same GUID. **No separate state.**

The UUIDv5 approach is structural-commitment-via-construction-
validation (`AXIOMS.md` operational principle) applied to GUIDs:
every GUID is derived; the derivation is deterministic; the
mapping is the function, not a stored table.

Implementation lands when the refactorlog emitter does. Naming
the choice now prevents the emitter from accidentally introducing
state-tracking machinery before this commitment is honored.

### Three data-emission classes named explicitly

V2 distinguishes three classes of data emission, each with its own
artifact shape and its own deployment semantics:

  1. **StaticSeeds.** Static-entity populations carried by the
     catalog itself (per A7 — Static modality is part of catalog
     structure). Emitted as MERGE seed scripts; deployed
     idempotently; expected to apply cleanly against existing
     populations or to seed empty tables.

  2. **MigrationDependencies.** Operator-policy-declared regular
     entities whose populations need to be carried forward as part
     of the migration. These are entities the operator has
     specifically marked — *MigrationDependency is a policy
     choice, not a structural property of Kind.* The catalog
     doesn't carry "this is a migration dependency"; the policy
     does. Same kind of separation as A18 amended (Policy is
     intent; Catalog is evidence).

  3. **Bootstrap.** A variable-composition emission class governed
     by a closed DU on `EmissionPolicy`:
     ```fsharp
     type BootstrapComposition =
         | AllRemaining       // default — everything not in StaticSeeds or MigrationDependencies
         | AllExceptStatic    // everything except static populations
         | AllData            // everything (including static populations)
     ```
     The DU is **closed** so consumers can pattern-match
     exhaustively; new variants land at meaningful inflection
     points per `DECISIONS 2026-05-13 — Discrete-rationale DUs`.

The three classes are **distinct artifacts**, not three shapes of
one artifact. Each class has its own emitter; the canary applies
them in order; the deployment pipeline carries all three.

### Verisimilitude policy held until real demand

A "verisimilitude policy" — controlling how faithfully V2's data
emission reproduces V1's exact byte sequence vs. how aggressively
V2 reformats — was discussed but **deferred until a real
validation consumer demands it.** Premature design here would
codify a policy axis that today has no consumer.

Forward trigger: when a real operator complains that V2's emission
differs from V1's in a way that breaks downstream tooling, the
verisimilitude policy lands. Not before.

### Projection.Pipeline as a new C# project

The canary's mechanism (testcontainers, DacFx, ephemeral SQL Server,
script execution) involves I/O, async, third-party dependencies, and
runtime concerns that V2's F# Core forbids by codification (per
CLAUDE.md's F# feature surface — purity-first sort; effect, time,
concurrency forbidden in Core).

The right home for the canary's orchestration is a **new C# project,
`Projection.Pipeline`**. C# is appropriate for:

  - DacFx integration (the .NET ecosystem's natural language for
    DacFx is C#)
  - Testcontainers usage (testcontainers.NET works fine from F#
    but is more idiomatic in C#)
  - Async orchestration with explicit Task/await semantics
  - Coordination between F# pure-core (the Catalog comparison
    logic) and the I/O surfaces

The codification preserves: **F# Core's purity is unchanged;
adapters at the boundary may use what Core forbids; the canary's
orchestration is at the boundary by definition.**

The project name `Projection.Pipeline` distinguishes from
`Projection.Adapters.*` (which are F# value-returning boundaries)
because the canary is more orchestration than adapter — it
coordinates multiple adapters and emitters into a single workflow.

### Docker SQL Server version hardcoded to match production

The canary's ephemeral SQL Server container is pinned to **the
exact SQL Server version that production runs**. Hardcoded; no
configuration knob; no version range.

Rationale: the canary's value-prop is "the artifacts apply cleanly
in production." The signal is meaningful only if the canary's
target matches production's. A version range introduces a class of
canary-passes-but-production-fails failures the canary exists to
prevent.

The hardcoded value lives at the canary's configuration surface
(`Projection.Pipeline`'s configuration). When production upgrades,
the canary upgrades atomically with it. The few-months-horizon
framing applies: short-lived hardcoding, not permanent.

### What's not in this entry

Specific implementation choices for any of the above are
**deferred to their own DECISIONS entries** when the relevant
chapters open:

  - The OSSYS adapter's parse signature → Position B entry
    (session 17 commit 4)
  - The read-side adapter's specific shape → its own chapter
    when it opens
  - The three data-emission classes' specific artifact formats →
    each emitter's chapter
  - The canary's specific orchestration → `Projection.Pipeline`
    chapter when it opens
  - The refactor.log emitter's UUIDv5 namespace and exact derivation
    → that emitter's chapter

#### 2026-05-16 (session 19 amendment) — canary's rename-handling depends on the SsKey-source path

The canary's roll-forward minimally-invasive guarantee — that
deployments to a fresh database render minimum diff against the
prior snapshot — depends on **SsKey preservation across renames
in the input source**. T8's structural diff is keyed by SsKey;
when source changes between snapshot N-1 and snapshot N include
a rename, the diff produces a `RENAME` only when SsKey is
preserved across the change.

**With the current OSSYS path** (`SnapshotJson` consuming V1's
canonical `osm_model.json`, which is lossy on SSKey per
`DECISIONS 2026-05-15 — OSSYS adapter translation rules`,
amended in session 19): a renamed entity produces a different
synthesized SsKey, so the diff sees `DELETE old + INSERT new`.
The canary's deployment-success leg still passes (the new state
deploys cleanly against an empty database); the deployment
script that gets generated drops and recreates the renamed
object — the **noisy mode** the strategic frame names V2 as
avoiding.

**The minimally-invasive guarantee is bounded by the input
path:**

  - With name-synthesized SsKey through the current JSON path:
    renames-across-the-JSON-path render as drop-create.
  - With any of the three re-open triggers fired
    (`SnapshotJsonBuilder` line-level fix; `SnapshotRowsets`
    variant; `LiveOssysConnection`): the bound resolves and
    renames produce structural-rename diffs.

**Graceful-degradation-shaped.** Drop-create renames are
**correct** (state matches end-to-end); they are just **noisy**.
Production operators will notice. The bound is documented; the
resolution path is reachable; the choice is open until either
empirical pressure (rename-fixture friction during the OSSYS
chapter) or a chapter or operator decision selects.

**This amendment exists because future agents opening the canary,
read-side adapter, or `Projection.Pipeline` chapters will need
to know which trigger has fired by the time they reach the
roll-forward-rename logic.** Making the dependency explicit in
the strategic frame keeps it visible across the gating-dependency
graph rather than leaving it implicit at the OSSYS adapter
boundary.

**No immediate work.** This amendment is documentation of the
constraint, not a directive to act on it. The OSSYS adapter
chapter continues without immediate need to resolve the
SsKey-source choice; the canary's later integration work will
inherit whichever trigger has fired by then.

#### 2026-05-17 (session 20 amendment) — input-source choice closed; canary's bound resolves when SnapshotRowsets lands

The session-19 amendment above named three reachable triggers
for resolving the canary's roll-forward minimally-invasive
guarantee. **Operator decision (per `DECISIONS 2026-05-15 —
OSSYS adapter translation rules`, session-20 amendment): Option
2 (`SnapshotRowsets` variant) is the canonical resolution
path.** This sub-section sharpens the canary's dependency
accordingly.

**The canary's roll-forward minimally-invasive guarantee
resolves when the `SnapshotRowsets` variant implements.** Until
that implementation lands, V2 continues consuming `SnapshotJson`
with name-synthesized SsKey; the canary deploys cleanly but
renames-across-the-input-path render as drop-create. This is
**graceful-degradation-pending** behavior — correct, just
noisier than the post-resolution state.

The session-19 framing of "graceful degradation; choice is
open" updates to **"graceful degradation pending; resolution
chosen; implementation sequences in."** Future agents opening
canary, read-side adapter, or `Projection.Pipeline` chapters
inherit `SnapshotRowsets` as the assumed input source for the
roll-forward-rename logic.

**Implementation timing for the canary's dependency.** The
`SnapshotRowsets` variant lands when chapter 2's organic flow
brings it — likely after the current OSSYS adapter chapter
completes its translation work. The canary's roll-forward
logic, when its chapter opens, can be designed against the
post-resolution state; the bound documented here applies only
to interim deployments where `SnapshotRowsets` has not yet
shipped.

This entry's role is to **name the architectural axes** so future
chapters land into a coherent frame. The axes are load-bearing;
the implementations are deferred.

**Reasoning / consequences.** Without this strategic frame, each
of the eight commitments would land separately as the chapter
that needs it opens, and the cross-chapter coherence would be
incidental. With the frame, every chapter that opens against one
of these axes inherits the other seven as context.

The frame is **subject to refinement** as chapters open and surface
real evidence — the OSSYS adapter chapter may surface a parse-
signature question that affects the read-side adapter's shape; the
canary's first real run may surface a verisimilitude need; the
three-emission-class scheme may need a fourth class. Refinements
land as amendments to this entry or as their own entries that
reference it.

## 2026-05-15 — OSSYS adapter parse signature (Position B; input slot decided)

**Status:** decided (Position B per `DECISIONS 2026-05-13 — Anticipation vs. speculation in abstraction extraction`)
**Context:** Session 17's chapter-open work names the OSSYS adapter
as the V2 boundary for OutSystems metadata ingestion. The
anticipation-vs-speculation refinement (session 14 commit 11)
recommends Position B for cases where a future abstraction's
shape is visible but its arrival is not concrete. `ICatalogReader`
is the named future abstraction (a second catalog source —
DACPAC, OData, in-memory test reader — would surface it). Position
B says: design the function signature to map cleanly to the
eventual interface; defer the interface itself.

This entry records Position B for the OSSYS adapter and decides
the open `<input>` slot the session-17 instruction explicitly
flagged.

### Decision

**The OSSYS adapter's canonical entry-point signature is:**

```fsharp
module Projection.Adapters.Osm.CatalogReader

val parse : SnapshotSource -> Task<Result<Catalog>>
```

**The `<input>` slot is the V1-produced JSON snapshot, lifted
into a small typed value:**

```fsharp
type SnapshotSource =
    /// Path to a V1-produced osm_model.json file on disk. Read
    /// synchronously inside the Task; the adapter is async at the
    /// boundary for ecosystem consistency, not because the file
    /// I/O itself benefits from it.
    | SnapshotFile of path: string
    /// In-memory snapshot string. Useful for tests and for
    /// pipelines that produce the snapshot in-memory rather than
    /// via disk.
    | SnapshotJson of json: string
```

The `SnapshotSource` DU is **closed** (per the strategy-layer
codification's discipline of closed-DU expansion when consumers
are at meaningful inflection points). Adding a third variant
(e.g., `LiveOssysConnection of connectionString` once V2 grows a
SQL-running entry point) lands as an explicit DU expansion,
not a silent open variant.

### Position B rationale: shape alignment for `ICatalogReader`

The session-14 anticipation-vs-speculation refinement named
`ICatalogReader` as a Position B candidate. The OSSYS adapter's
chapter-open is the moment to honor that: design the signature
so a future interface lands as a one-line wrapper, not a
retrofit.

The Position B alignment:

```fsharp
// Future, when a second catalog source materializes:
type ICatalogReader =
    abstract Read : SnapshotSource -> Task<Result<Catalog>>

// OSSYS adapter wraps trivially via object expression:
let osmReader : ICatalogReader =
    { new ICatalogReader with
        member _.Read source = Projection.Adapters.Osm.CatalogReader.parse source }

// A DACPAC reader (when it lands) wraps the same way:
let dacpacReader : ICatalogReader =
    { new ICatalogReader with
        member _.Read source = Projection.Adapters.Dacpac.CatalogReader.parse source }
```

The `SnapshotSource` DU is the abstraction's input parameter even
in the single-adapter case. A future DACPAC reader's variants
(`DacpacFile`, `DacpacBytes`) would expand the same DU, OR a
distinct `DacpacSource` DU would parallel it. Position B doesn't
require the DUs to merge — it requires the *signature shape* to
align so the interface, when it lands, doesn't force retrofit.

### Why JSON snapshot, not live OSSYS connection

The session-17 instruction asked: connection string to a live
OutSystems database, path to a JSON snapshot file, or a DU
accepting either?

**Decision: JSON snapshot only at chapter-open.** The
`SnapshotSource` DU has two variants today (file-path and
in-memory string); a third (`LiveOssysConnection`) is a Position-C
deferral with explicit re-open trigger.

**Rationale for the JSON-only choice:**

  1. **Preserves V1's reconciliation chain.** V1's 1184-line SQL
     script does the hard work of intent-vs-reality reconciliation
     (per the OSSYS ADMIRE chapter scope, session 17 commit 2).
     Re-implementing that work in V2 would be a substantial
     additional chapter; V2's OSSYS adapter does shape translation
     from V1's already-reconciled JSON, not re-reconciliation
     from raw SQL.
  2. **Preserves F# Core's no-I/O / no-time discipline at the
     test surface.** Reading a JSON file is a single point of
     I/O at the boundary; running the OSSYS SQL script is a
     full DbConnection lifecycle, async DB I/O, and ~22 rowset
     processors. JSON-path keeps the boundary thin.
  3. **The V2 fixture pattern already mirrors the JSON shape.**
     `tests/Fixtures/model.*.json` files are V1-shaped today;
     consuming them directly via the JSON-path adapter is the
     differential test V2 needs (per the OSSYS ADMIRE chapter
     scope's "differential validation" section).
  4. **The canary path stays clean.** The strategic frame's
     canary (session 17 commit 1) needs a Catalog input; reading
     it from a JSON snapshot is what V2's existing test surface
     does. The canary doesn't need a live OutSystems instance to
     validate emission; the canary applies V2's emitted artifacts
     against an ephemeral SQL Server, which is unrelated to the
     OSSYS adapter's input.

**Re-open trigger for `LiveOssysConnection`:** when a real
operator workflow demands V2 ingest OutSystems metadata directly
without staging through V1's JSON chain (e.g., a CLI surface
where the operator points V2 at an OutSystems database and V2
runs the extraction itself, replacing V1's `MetadataSnapshotRunner`
in V2's stack). Until that workflow surfaces, V1's SQL chain
remains the metadata producer; V2 reads its JSON output.

The `SnapshotSource` DU is the carrier for this future expansion.
When the trigger fires, a third variant lands; the parse function
gains a third branch; the rest of the adapter is unchanged.

### Why `Task<Result<Catalog>>`, not `Result<Catalog>`

The signature uses `Task<Result<Catalog>>` even though file I/O
on the JSON-path could be synchronous. The `Task` wrapping serves
two purposes:

  1. **`ICatalogReader` interface alignment.** A future DACPAC
     adapter (DACPAC files unzip and parse asynchronously) and
     a future `LiveOssysConnection` variant (DB I/O is async by
     definition) both need `Task<...>` shape. Placing the OSSYS
     adapter under the same shape today means the interface, when
     it lands, doesn't have to upcast sync `Result` to async
     `Task<Result>`.
  2. **Ecosystem consistency.** The trunk's V1 adapter
     (`MetadataSnapshotRunner.ExecuteAsync`) returns
     `Task<Result<OutsystemsMetadataSnapshot>>`. V2's OSSYS
     adapter mirroring the shape simplifies the C#-from-F#
     interop story when `Projection.Pipeline` (the canary's C#
     orchestration project) wants to call into V2's adapter.

The trade-off is small ceremony at the JSON-path call site
(`async { ... } |> Async.StartAsTask` or equivalent) in exchange
for shape alignment with future async-by-nature variants.

### Where the entry point lives (project structure)

The OSSYS adapter lives in a new project:
`src/Projection.Adapters.Osm/`. Sibling to `Projection.Adapters.Sql/`
(which today carries `Static.fs`, `ProfileSnapshot.fs`,
`ProfileStatistics.fs`). The choice of a separate project rather
than a file under `Projection.Adapters.Sql/` reflects the
adapter's distinct role:

  - `Projection.Adapters.Sql` is for SQL-Server-side metadata
    (column reality, FK reality, profile probes). It does NOT
    read OutSystems platform metadata; it reads database
    structural reality.
  - `Projection.Adapters.Osm` is for OutSystems-platform metadata
    (the OSSYS_* / OSUSR_* schema). It does NOT read database
    structural reality directly; it consumes V1's reconciled
    output.

The two adapters are siblings in the same architectural axis
(both read external metadata into V2's IR) but separate projects
because their input domains differ. The split also makes
test-fixture organization clearer: `Projection.Tests/Fixtures/`
JSON files belong to the Osm adapter's test surface; profile
snapshot fixtures belong to the Sql adapter's.

### What this entry doesn't decide

  - **The DTO shape inside the adapter.** Whether to use
    `System.Text.Json.JsonDocument`, hand-written DTO records, a
    type provider, or something else is implementation-territory
    for the next chapter in the OSSYS arc.
  - **Translation rules for V1↔V2 vocabulary.** The mapping rules
    for V1 `IsExternalEntity` + `IsSystemModule` → V2 `Origin`,
    V1 nullable `DeleteRule` → V2 closed `OnDelete`, etc., are
    implementation decisions that land in the relevant chapter.
  - **Test fixture strategy.** Whether to embed fixtures inline
    (per `StaticAdapterDifferentialTests.fs`'s pattern) or to
    consume V1's `tests/Fixtures/` JSON files directly is a test-
    surface decision the chapter-open hasn't reached yet.
  - **Diagnostic emission.** The OSSYS adapter will likely emit
    `DiagnosticEntry` values for parser warnings (per the
    Diagnostics writer that landed at session 14 commit 3); the
    return type extension to `Lineage<Diagnostics<Catalog>>` is
    deferred until the implementation chapter decides whether
    the adapter's diagnostics warrant the dual-writer shape or
    a simpler `Result<Catalog * DiagnosticEntry list>` tuple.

### Reasoning / consequences

The Position B framing says: **shape now, interface later.** The
parse signature is the shape; the interface is the deferral.
Future agents implementing the OSSYS adapter inherit the
signature as a constraint; future agents wrapping it in
`ICatalogReader` (when a second source materializes) inherit the
trivial wrapping path.

The JSON-only-at-chapter-open choice is itself a Position-B move
on the input slot: design `SnapshotSource` as a closed DU so a
future `LiveOssysConnection` variant lands cleanly, but don't
build it today. Two-consumer threshold (within a shape) applies
recursively — the variant earns its place when a second consumer
demands SQL-direct ingestion, not before.

This entry pairs with `DECISIONS 2026-05-15 — Strategic frame for
the OSSYS implementation chapter` (session 17 commit 1) and the
OSSYS ADMIRE chapter scope (session 17 commit 2). Together they
form the chapter-open: the strategic axes; the V1↔V2 chapter
scope; the canonical entry signature. The implementation
chapters open from here.

## 2026-05-15 — OSSYS adapter translation rules (chapter session 18; rules surfaced under empirical pressure)

**Status:** decided (chapter rules — extends as the OSSYS arc continues)
**Context:** Session 18 opened the OSSYS adapter implementation
chapter via the differential-test path: a minimal V1 fixture (one
module, one entity, two attributes) embedded in
`OsmCatalogReaderDifferentialTests.fs`; an expected V2 Catalog
hand-built; the parser implemented just enough to make the
assertion pass. Working under empirical pressure surfaced six
translation rules and one substantive architectural finding. This
entry captures them as the chapter's running translation-rules
list per the session 17 instruction's discipline.

The list **extends** as the OSSYS arc continues — each session
adding rules under the same empirical discipline. New rules land
either as amendments to this entry or as their own entries that
reference it.

### The substantive architectural finding: V1's JSON is lossy on SSKey identity

V1's metadata extraction chain produces SSKey values at the SQL
rowset layer (`EspaceSSKey`, `EntitySSKey`, `AttrSSKey` columns
per `outsystems_metadata_rowsets.sql`). The in-memory
`OutsystemsMetadataSnapshot` carries them. **But
`SnapshotJsonBuilder` does NOT write them to the canonical
`osm_model.json` document.** The assembled JSON carries names and
physical names; the SSKeys are discarded at JSON serialization.

V2's identity-survives-rename promise (A1) is bounded by what's
in the input. For the JSON-snapshot path, V2's `CatalogReader`
**synthesizes** `SsKey` deterministically from name fields:

  - Module: `OS_MOD_<ModuleName>`
  - Kind:   `OS_KIND_<ModuleName>_<EntityName>`
  - Attribute: `OS_ATTR_<ModuleName>_<EntityName>_<AttrName>`

The synthesis is stable across runs of identical input; same
JSON in, same SsKey out. Renames in the source OutSystems
platform produce different SsKey values in V2's IR — A1's
identity-survives-rename guarantee is **not honored** for renames
that traverse the JSON-snapshot path.

**Re-open triggers** (when this synthesis convention should be
revisited):

  - **V1's `SnapshotJsonBuilder` is extended to emit SSKeys.**
    The cleanest fix; preserves V1's chain-shape and makes V2's
    identity stable across renames.
  - **An alternative input source carries SSKeys natively.**
    A future `LiveOssysConnection` variant (per `DECISIONS
    2026-05-15 — OSSYS adapter parse signature`) running the
    SQL extraction directly would have access to the rowset
    SSKey columns; the synthesis convention becomes a fallback
    rather than the primary path.

Until either trigger fires, the synthesis convention is the
canonical V2 identity for OSSYS-sourced catalogs. **Documented
divergence; not a bug.**

This is the kind of finding the test-driven path was supposed
to surface — the rule was not visible from the orientation
reading; it became visible only when the parser had to produce
SsKey values for the assertion.

#### 2026-05-16 (session 19 amendment) — sharpened by SQL evidence; third re-open path; operator confirmation

Reading V1's `outsystems_metadata_rowsets.sql` directly sharpens
the original characterization. The lossiness is **at exactly one
projection layer**, not end-to-end:

```
ossys_* tables  →  temp tables (#E, #Ent, #Attr — SSKey present)
                →  trailing rowsets (SELECTs at script bottom — SSKey present)
                                                ↘
                                                  JSON pre-aggregations (#AttrJson,
                                                  #ModuleJson via FOR JSON PATH —
                                                  SSKey stripped)
                                                ↘
                                                  osm_model.json (SSKey stripped)
```

`#E` carries `EspaceSSKey`; `#Ent` carries `EntitySSKey` and
`PrimaryKeySSKey`; `#Attr` carries `AttrSSKey`. The trailing
rowset SELECTs at the bottom of the script all emit those
columns. The data is available everywhere upstream of the JSON
projection layer; what's lost is what the JSON `FOR JSON PATH`
projections happen not to include.

**The first re-open trigger is much cheaper than the original
entry implied.** Calling it "extending `SnapshotJsonBuilder`"
is technically correct but undersells the work: the existing
JSON projections already SELECT from `#Attr`, `#Ent`, `#E`.
Adding `a.AttrSSKey AS [ssKey]` (or similar) to the existing
`FOR JSON PATH` projections is **line-level additive, low-risk,
no upstream change**. The SQL extraction is already producing
the data; the canonical osm_model.json's projection is the only
thing that elides it.

**A third re-open path the original entry didn't enumerate.** The
SQL emits *both* the JSON for the canonical osm_model.json *and*
the trailing rowsets as result sets. If V2's input could be the
rowsets directly (delivered as some persisted form — multi-rowset
JSON, CSV per table, whatever the operational layer provides),
V2 gets SSKey natively without V1 pipeline cooperation. This
would land as a third `SnapshotSource` variant alongside
`SnapshotFile` and `SnapshotJson` — perhaps `SnapshotRowsets`
of some input type — and exercises the closed-DU expansion
discipline cleanly.

**Three paths, all confirmed reachable by the operator:**

  1. **`SnapshotJsonBuilder` line-level fix** — V1 cooperation;
     preserves V2's existing single-input-source posture; smallest
     diff at the V1 boundary.
  2. **`SnapshotRowsets` variant** — V2-internal; adds a new
     parsing surface to V2 but no V1 change required; exercises
     closed-DU expansion at `SnapshotSource`.
  3. **`LiveOssysConnection` variant** — substantial; V2
     maintains its own database connection running the SQL or
     equivalent extraction; reserved for future demand.

The choice between the three is **open**. The operator has
confirmed any of them works; the trade-offs differ:

  - **Path 1** depends on V1 pipeline cooperation but is
    architecturally invisible to V2.
  - **Path 2** requires no V1 cooperation but expands V2's
    parsing surface.
  - **Path 3** is the most architecturally substantial and
    reserved for the case where V2 needs to operate without
    V1's chain in the loop.

**The bounded-A1-claim disposition is unchanged** — through the
current `SnapshotJson` path V2 uses today, A1 is bounded; the
bound resolves when any of the three triggers fires. What
changes is that **the resolution is more reachable than the
original entry implied** — Path 1 is line-level work; Path 2 is
a closed-DU expansion within V2.

**No code change today.** Adding `SnapshotRowsets` speculatively
would violate the closed-DU expansion discipline (one consumer
needed; zero exist). The variant is named here so it's
discoverable when a real consumer surfaces; the entry is
amendment-only documentation.

**Strategic-frame implication (cross-reference).** The pipeline
canary's roll-forward minimally-invasive guarantee is bounded
by which of the three triggers is operating. See the strategic-
frame entry's session-19 amendment for the specific
canary-rename-handling implication.

#### 2026-05-17 (session 20 amendment) — operator decision: SnapshotRowsets is canonical

**The choice is closed.** Operator decision: **Option 2
(`SnapshotRowsets` as a third closed-DU variant on
`SnapshotSource`) is the canonical resolution path.** This
decision is not subject to relitigation; future sessions inherit
`SnapshotRowsets` as the assumed input source for OSSYS
metadata when the bound on A1 needs to resolve.

**Rationale.** Rowsets carry richer information than the
aggregated JSON does. Three concrete advantages over the
JSON-only path:

  1. **SSKey natively at every level.** `EspaceSSKey`,
     `EntitySSKey`, `PrimaryKeySSKey`, `AttrSSKey` are present
     in the rowsets; the V2 catalog reader reads them directly
     rather than synthesizing from names. A1's
     identity-survives-rename guarantee resolves to its full
     promise through this input path.
  2. **Per-table column structure preserved.** V1's `FOR JSON
     PATH` aggregations collapse some structural information
     that the rowsets retain. Specific examples will surface as
     fixtures grow under the OSSYS arc; the rowsets-as-input
     path future-proofs the boundary against the
     eleven-deferred-fields backlog the session-18 entry named
     and the session-19 entry extended.
  3. **Independent of V1 pipeline cooperation.** Unlike Option
     1 (extending `SnapshotJsonBuilder`), V2 doesn't depend on
     V1-side changes to land. The rowsets already exist as
     trailing SELECTs in `outsystems_metadata_rowsets.sql`;
     V2's adapter takes them in whatever persisted form the
     operational layer provides (multi-rowset JSON, per-table
     CSV, etc.).

**Why not Option 1 (extend `SnapshotJsonBuilder`).** Simpler
than Option 2 — line-level additive work to the JSON
projections — but solves only the immediate SSKey question.
Doesn't address the broader collapse of structural information
the JSON aggregation introduces; doesn't future-proof the V2
boundary against the deferred-fields backlog. The operator
considered Option 1 and chose against it.

**Why not Option 3 (`LiveOssysConnection`).** More
architecturally substantial than Option 2 — V2 maintains its
own database connection running the SQL or equivalent
extraction. Reserved as a future variant for the case where V2
needs to operate without V1's chain in the loop entirely. The
operator considered Option 3 and chose against it for now;
Option 3 remains as a future variant when its specific demand
surfaces.

**Implementation timing.** The actual `SnapshotRowsets` variant
lands when chapter 2's organic flow brings it — likely after
the current OSSYS adapter chapter completes its translation
work through the existing `SnapshotJson` path. The variant is
its own coherent slice when it opens. **Until implementation
lands, V2 continues consuming `SnapshotJson` with
name-synthesized SsKey; the bound on A1 through that path
remains as documented in this entry's original session-18
content.**

**The canonical resolution exists in documentation now; the
code follows when sequencing brings it.** Future sessions
opening canary chapters, read-side adapter chapters, or
roll-forward chapters inherit `SnapshotRowsets` as the assumed
input source. If implementation surfaces refinements during the
work (DTO shape questions, multi-rowset deserialization
choices, integration with existing parser code), those land as
their own DECISIONS entries — but the architectural commitment
to the variant itself is fixed.

**Entry-shape note for future readers.** This sub-section
supersedes the "three paths, choice open" framing in the
session-19 amendment above. The session-19 framing is preserved
verbatim as the historical lineage of the decision; this
sub-section is the load-bearing rule for future agents. The
amendment-discipline pattern: original text preserved; new
text supersedes; future readers see the lineage.

##### 2026-05-17 (session 20 strengthening — composability finding)

The session-20 external-entity slice surfaced a finding that
**strengthens the canonical-resolution choice beyond what was
visible at decision time**: V1's JSON projection layer is
structurally lossy in a class-shaped way, not coincidentally on
two unrelated fields.

The class has at least three currently-known members:

  - **SsKey at every level** — `EspaceSSKey`, `EntitySSKey`,
    `PrimaryKeySSKey`, `AttrSSKey` all stripped at JSON
    aggregation (session 18 finding).
  - **`EspaceKind`** — string column on `dbo.ossys_Espace`
    encoding the IS-vs-Direct distinction; stripped at JSON
    aggregation (session 20 finding via the external-entity
    fixture).
  - **`isSystemEntity`** — present in the `#Ent` rowset; not
    written by `SnapshotJsonBuilder` (observed during the
    session-20 trace; not yet exercised by a fixture).

Future fixtures may surface additional class members (per-table
column structure that `FOR JSON PATH` collapses; check-constraint
definitions; etc.). Each is a member of the same class.

**The reframing.** Option 1 (extend `SnapshotJsonBuilder`)
solves only one class member at a time. Option 2 (`SnapshotRowsets`)
absorbs the class structurally — once the variant implements,
**all class members resolve together**. The `EspaceKind` finding
from session 20 is empirical confirmation of what was an
architectural intuition at canonical-decision time: the rowsets
are the right level of abstraction to consume from, because the
JSON-projection lossiness is a structural property of that
projection layer, not a per-field oversight.

**The architectural commitment was more right than was visible
when it was made.** The operator's decision rests on a stronger
foundation now: the choice covers a class of lossiness, not just
the originally-named SsKey question.

**For the agent who opens the `SnapshotRowsets` implementation
chapter:** the implementation is not a one-bug fix. It's the
resolution to a class. Future fixtures are likely to surface
additional class members; the implementation needs to absorb
those too. The class is named in this entry's session-20
amendment to the Origin entry below (rule 17's amendment
section); reference it from the implementation chapter when it
opens.

### Translation rules the minimal fixture forced

| # | V1 input shape | V2 output | Rationale |
|---|---|---|---|
| 1 | Module `name` (string) | `Module.SsKey = OS_MOD_<name>`, `Module.Name = Name.create name` | SsKey synthesis (see finding above). The Name DU validates non-blank; module-level translation fails early on blank input. |
| 2 | Entity `name` + parent module `name` | `Kind.SsKey = OS_KIND_<modName>_<entName>` | Synthesis includes module name to disambiguate same-named entities across modules. |
| 3 | Attribute `name` + parent entity + module | `Attribute.SsKey = OS_ATTR_<modName>_<entName>_<attrName>` | Three-level naming preserves attribute identity across module / entity rename scenarios. |
| 4 | `dataType: "Identifier"` | `Attribute.Type = Integer` | OutSystems' Identifier data type is the standard PK type; V2 maps it to the Integer primitive. The `isAutoNumber` flag is read but discarded today (V2 IR has no auto-number axis; deferred). |
| 5 | `dataType: "Text"` | `Attribute.Type = Text` | Direct mapping. The `length` field is read but discarded today (V2 IR has no per-attribute length axis; SQL-type translation handles length at emit time per Policy A13). |
| 6 | `physicalName` (string) | `Attribute.Column.ColumnName = physicalName` | Direct. The `originalName` and `databaseColumnName` fields are not in this fixture; their translation rule lands when a fixture surfaces them. |
| 7 | `isMandatory: true \| false` | `Attribute.IsMandatory = isMandatory`, `Attribute.Column.IsNullable = not isMandatory` | The IsNullable proxy is **catalog-only**; it derives nullability from logical mandatory rather than from physical evidence. Profile evidence (when wired) refines it. The OSSYS adapter's job is structural; physical-reality reconciliation lives in V1's SQL chain (already done before V2 sees the JSON) and in `Projection.Adapters.Sql/ProfileSnapshot.fs` (separate input). |
| 8 | `isIdentifier: true \| false` | `Attribute.IsPrimaryKey = isIdentifier` | Direct. V1's `isIdentifier` flag corresponds to V2's structural PK marker. |
| 9 | Entity `db_schema` + `physicalName` | `Kind.Physical = { Schema; Table }` | Direct; the V1 JSON's reconciled `db_schema` already accounts for any `db_catalog` context (which V2 ignores per the OSSYS ADMIRE chapter scope's "what V2 will explicitly NOT carry forward" section). |
| 10 | `isStatic: true` → `Modality = [Static []]`; `isStatic: false` → `Modality = []` | Per A7. Static populations themselves come from a separate input (V1's static-data JSON via `Projection.Adapters.Sql/Static.fs`). The OSSYS adapter sets the modality marker; the populations join later. |
| 11 | `isExternal: false` → `Origin = OsNative`; `isExternal: true` → `Origin = ExternalDirect` | **Placeholder rule for the minimal fixture.** The full collapse rule for V1's `IsExternalEntity` + `IsSystemEntity` + Integration-Studio metadata into V2's three-way `Origin` DU is **deferred** until a fixture surfaces the IS-vs-Direct distinction. The `ExternalDirect` mapping is the conservative placeholder; it does not claim to be the right rule for every external entity. |

### What this commit explicitly does NOT carry forward (yet)

Fields the minimal fixture contains but the parser ignores:

  - `attributes[].originalName` — V1's pre-rename name; V2 has no
    rename-history axis on Attribute. Defer until a use case
    demands it (likely the refactor.log emission per the
    strategic frame).
  - `attributes[].length` / `precision` / `scale` — V1 type
    metadata. V2 IR's `PrimitiveType` is abstract; concrete
    SQL-type details land in emitter-time policy (A13). Defer
    until either the IR grows a length-bearing variant or a
    consumer demands the discriminated translation.
  - `attributes[].isAutoNumber` — V1 auto-number flag; V2 IR
    has no auto-number axis on Attribute. Defer.
  - `attributes[].isActive` — V1 activity flag. Per the OSSYS
    ADMIRE chapter scope, V2's `Selection` policy handles
    activity at the policy level. The minimal fixture sets
    everything to `isActive=true`; the reader currently does
    not check it. Re-open trigger: a fixture with mixed-active
    entities or attributes surfaces the boundary-vs-policy
    decision (filter at adapter, or carry through with a
    distinct V2 representation).
  - `attributes[].isReference` / `refEntityId` / `refEntity_name`
    / `reference_deleteRuleCode` etc. — Reference translation.
    The minimal fixture has `isReference: 0` for both
    attributes; references aren't exercised. The next session
    in the OSSYS arc likely adds a reference-bearing fixture
    and surfaces the V1 nullable `DeleteRule` → V2 closed
    `OnDelete` translation rule.
  - `attributes[].external_dbType` — External-DB type for
    integration-studio attributes. Defer with the Origin
    rule.
  - `attributes[].physical_isPresentButInactive` — V1's
    inactive-but-physically-present marker. Defer with the
    activity rule.
  - `entities[].relationships` — Reference list. Empty in this
    fixture; translation defers to the reference-bearing fixture.
  - `entities[].indexes` — Index list. Empty in this fixture;
    translation defers to the index-bearing fixture.
  - `entities[].triggers` — Trigger list. Empty in this fixture;
    per the OSSYS ADMIRE chapter scope, V2 has no Trigger IR
    type today. Defer until consumer demand surfaces the IR
    refinement.
  - `entities[].db_catalog` — Cross-catalog FK marker. Per the
    Active deferrals index, the cross-catalog IR refinement is
    reserved-but-unreachable; the fixture has `null`; the
    parser ignores the field.
  - `entities[].meta` — Entity description string. V2 IR has no
    description axis. Defer.
  - Top-level `exportedAtUtc` — V1 export timestamp. V2 has no
    catalog-level timestamp; the `Lineage` writer captures
    when each pass runs. Defer with explicit not-carried.

### Discipline going forward

The chapter accumulates translation rules under empirical
pressure. Each subsequent session in the OSSYS arc extends the
running list with new rules surfaced by new fixtures. The
discipline:

  1. New fixture lands in `OsmCatalogReaderDifferentialTests.fs`
     (or a sibling test file) embedding a V1 shape that surfaces
     a translation question.
  2. Test fails until the parser handles the new shape.
  3. Parser implementation lands; new translation rules surface.
  4. The rules are appended to this entry (or a sibling entry
     references them) with the empirical example attached.

This is the same shape as the strategy-layer codification's
empirical-verdict process (`DECISIONS 2026-05-11 — Strategy-layer
codification: empirical verdict after the fourth instance`):
rules emerge from real consumers, not from speculation about
hypothetical shapes. The chapter's running list is the
audit-trail.

### Reasoning / consequences

The differential-test path produced exactly the value session 17's
instruction predicted: rules surfaced under code pressure rather
than under speculative reasoning. The SsKey-lossy-JSON finding
specifically would have been hard to anticipate from the
orientation reading alone — it became visible only when the
parser had to produce SsKey values for the assertion. Future
chapter sessions following the same path are likely to surface
similar findings; the running translation-rules list is how the
chapter accumulates them auditably.

The won't-carry-forward list (above) extends the OSSYS ADMIRE
entry's chapter-scope section with concrete examples from the
minimal fixture. As subsequent fixtures land, more V1 fields
will surface that need either-way decisions; keeping them
explicit (rather than letting them emerge silently as gaps) is
the discipline session 17's instruction named.

#### 2026-05-16 (session 19 amendment) — reference-bearing fixture extends the running list with five FK translation rules

Session 19's reference-bearing fixture (User → Account FK with
`reference_deleteRuleCode: "Protect"`) surfaced five translation
rules under empirical pressure. Appended to the running list as
rules 12–16; the table-shape from the original entry continues.

**The deferred V1 nullable `deleteRuleCode` → V2 closed `OnDelete`
question is now resolved.** Session 17's OSSYS ADMIRE chapter
scope named this as one of three deferred translation questions;
it lands here as rule 13 with the full mapping table per V1's
existing convention in `Osm.Smo/SmoEntityEmitter.cs`.

| #  | V1 input shape | V2 output | Rationale |
|----|---|---|---|
| 12 | Source attribute name + parent entity + module (when `isReference: 1`) | `Reference.SsKey = OS_REF_<modName>_<entName>_<attrName>` | Reference SsKey synthesis. The reference identifies by its source coordinate; an attribute carries at most one outgoing reference in V1's metadata, so the source coordinate is unique. |
| 13 | `reference_deleteRuleCode: "Protect"` | `Reference.OnDelete = NoAction` | V1 → V2 mapping per `Osm.Smo/SmoEntityEmitter.cs`. The full table: `"Delete" → Cascade`; `"Protect" → NoAction`; `"Ignore" → NoAction`; `"SetNull" → SetNull`; `null → NoAction` (the V1 `TreatMissingDeleteRuleAsIgnore` default). The minimal fixture exercises only "Protect"; the full table lands so subsequent fixtures don't re-litigate. **Note:** `"Ignore"` collapses to V2 `NoAction` because V2's `ReferenceAction` DU has no Ignore variant and V1's "Ignore" is semantically `NoAction` at the SQL level (the V1 audit-worthy "we tolerated a missing delete-rule" concern belongs to the Diagnostics writer, per session 16 commit 1's FK activation). The session 18 finding that V2's `DeleteRuleIgnored` keep-reason is unreachable from V2 fixtures resolves here too: if V1's `deleteRuleCode` is `"Ignore"`, V2's `OnDelete` becomes `NoAction` and the reference is *enforced* (V2 doesn't decline to enforce); the V1 audit-trail concern emits a Diagnostics entry rather than a structural keep-reason. |
| 14 | V1 attributes with `isReference: 1` carry full reference fields (`refEntity_name`, `reference_deleteRuleCode`, etc.) | Walk attributes for `isReference: 1`; ignore the `relationships[]` array | V1 carries reference info in two places — on the source attribute and in the parent entity's `relationships[]` array (with `viaAttributeName + toEntity_name + hasDbConstraint`). The V2 adapter walks the attribute fields because they carry every field the V2 `Reference` shape needs. The `relationships[]` array is V1's aggregated cross-check; it could become a verification surface later but is not the primary source. **Documented divergence:** V1's two-source representation collapses to V2's one-source extraction. |
| 15 | Source attribute name | `Reference.Name = Name.create attrName` | V1 has no separate "relationship name" field; the via-attribute carries the relationship's display identity. The V2 `Reference.Name` derives from the attribute name (e.g., User's `AccountId` attribute produces a Reference named "AccountId"). Same-shape with V2's existing convention for un-named structural elements. |
| 16 | V1 `refEntity_name` (within the same module's catalog) | `Reference.TargetKind = OS_KIND_<sourceModule>_<refEntity_name>` | Same-module assumption. Cross-module FK references would require either: (a) carrying `refEntity_module` in V1's JSON (V1 does not today), or (b) V2 adapter scanning all modules to disambiguate (problematic when names collide). The same-module rule covers every fixture seen so far; cross-module references defer until a fixture surfaces the case (re-open trigger). |

#### What this commit explicitly does NOT carry forward (FK extensions)

Adding to the won't-carry-forward list:

  - `attributes[].refEntityId` — V1's numeric foreign-key-target
    pointer. V2 uses synthesized SsKey via name; the numeric ID
    is V1's internal database ID, not stable across deployments.
    The parser reads but ignores the field.
  - `attributes[].refEntity_physicalName` — V1's pre-resolved
    target physical table name. V2 derives the target's physical
    realization from the target Kind, not from the source
    attribute. Redundant under the same-module assumption.
  - `attributes[].reference_hasDbConstraint` — V1's flag for
    whether the physical FK constraint exists at the database
    level. V2's `Reference` carries no "is enforced" axis at the
    structural level — that distinction lives in `Profile`
    (empirical evidence, per A34's separation of structure from
    evidence) and in `ForeignKeyOutcome.EnforceConstraint(...)`
    decisions. The catalog reader surfaces structural FKs only;
    the Profile-side reader (separate input) carries
    `hasDbConstraint`-equivalent evidence.
  - `entities[].relationships[]` — entity-level aggregated
    relationship array. V2 walks attributes for primary
    extraction; relationships[] is unconsumed. Re-open trigger:
    if a future fixture surfaces a relationship that exists in
    relationships[] but NOT in attributes[isReference=1] (or
    vice versa), the divergence forces a cross-check.

#### Updated chapter status

Two slices through the OSSYS adapter chapter:

  - Session 18: minimal slice (one entity, two non-reference
    attributes). Eleven translation rules surfaced.
  - Session 19: reference-bearing slice (two entities, one
    reference). Five additional rules surfaced; the deferred
    deleteRuleCode question resolved.

Sixteen rules total in the running list. The two remaining
deferred questions from the session 17 ADMIRE chapter scope:

  - V1 `IsExternalEntity` + `IsSystemEntity` → V2 `Origin`
    three-way DU. Still pending; minimal and reference fixtures
    both have `isExternal: false`. A fixture with an external
    entity (Integration Studio or Direct) surfaces it.
  - Inactive-records boundary choice (filter at adapter or
    carry through and let Selection filter). Still pending; all
    fixtures so far have `isActive: true` everywhere. A
    mixed-active fixture surfaces it.

These continue to defer; the chapter's discipline holds — rules
land under empirical pressure, not under speculative reasoning.

#### 2026-05-17 (session 20 amendment) — external-entity fixture surfaces the Origin translation rule; placeholder updated under empirical pressure

Session 20's external-entity fixture surfaced the Origin
three-way collapse rule that the session 17 OSSYS ADMIRE chapter
scope flagged as one of three deferred translation questions.
Three substantive findings landed under the same fixture-driven
empirical-pressure discipline.

**Finding 1: V1's IS-vs-Direct distinction is encoded in
`EspaceKind`, which is NOT carried through V1's JSON projection.**
Trace performed before writing the fixture:

  - `EspaceKind` is a string column on `dbo.ossys_Espace`
    (V1 OutSystems platform metadata) read by V1's
    `outsystems_metadata_rowsets.sql` at line 96 (`#E.EspaceKind`).
  - The trailing rowset SELECT for the `#E` table emits
    `EspaceKind` (line 961 of the same file).
  - **`SnapshotJsonBuilder` does NOT write `EspaceKind` to
    `osm_model.json`.** The JSON output for modules carries only
    `name`, `isSystem`, `isActive`, `entities`. The IS-vs-Direct
    distinction is invisible to V2 through the `SnapshotJson`
    path.

This composes with the SsKey-lossy-JSON finding from session 18:
both deferred translation questions resolve through the same
input-path expansion (the `SnapshotRowsets` variant per
`DECISIONS 2026-05-15 — OSSYS adapter translation rules`,
session-20 amendment of the lossy-SSKey rule). The
`SnapshotRowsets` canonical resolution covers a **class** of
JSON-projection-lossiness questions, not just the SsKey question
that surfaced first.

**Finding 2: The session-18 placeholder for `isExternal: true`
was speculative; the session-20 fixture provides empirical
pressure to revise it.** Session 18's parser mapped
`isExternal: true` to `ExternalDirect` as a placeholder. That
choice was made when no fixture exercised the `isExternal: true`
branch; the rule was speculative. The session-20 fixture
mirrors V1's existing `model.edge-case.json` shape (the
`ExtBilling` module — the "Ext" prefix is conventional for
IS-extension modules in V1's domain). The placeholder updates
under that pressure to `ExternalViaIntegrationStudio`.

**The new placeholder rule (rule 17, extending the running
list):**

| #  | V1 input shape | V2 output | Rationale |
|----|---|---|---|
| 17 | Entity `isExternal` boolean (through the JSON path; `EspaceKind` not visible to V2) | `isExternal: false` → `OsNative`; `isExternal: true` → `ExternalViaIntegrationStudio` | Through the JSON-snapshot path, V2 cannot distinguish IS-vs-Direct because `EspaceKind` is stripped at the JSON projection layer. Placeholder picks `ExternalViaIntegrationStudio` because IS extensions are the standard V1 mechanism for external entities; most `isExternal=true` cases are IS-imported. The full three-way distinction (with `ExternalDirect` for non-IS external entities) resolves when `SnapshotRowsets` implements and `EspaceKind` becomes visible. **This rule supersedes the session-18 placeholder (`ExternalDirect`)** which was speculative without empirical pressure. |

**Finding 3: The bounded-A1-equivalent disposition extends to
Origin.** Through the `SnapshotJson` path, V2's three-way
`Origin` discrimination is bounded — `OsNative` and
`ExternalViaIntegrationStudio` are reachable; `ExternalDirect`
is unreachable from V2 fixtures because the JSON shape can't
distinguish it from IS-extension external entities. This is the
same shape as the bounded-A1 disposition from the session-18
SsKey finding. **Documented divergence; not a bug.**

The bound resolves identically to the SsKey bound — through
the same `SnapshotRowsets` canonical-resolution path. When
`SnapshotRowsets` implements, the V2 catalog reader gains
access to `EspaceKind` and the Origin translation rule
refines:

  - `isExternal: false` → `OsNative` (unchanged)
  - `isExternal: true` AND `EspaceKind: "Extension"` (or whatever
    the IS-marker turns out to be) → `ExternalViaIntegrationStudio`
  - `isExternal: true` AND not the IS-marker → `ExternalDirect`

The exact rule needs the empirical evidence of what
`EspaceKind` values appear and what they mean — that's the
work for the session that lands `SnapshotRowsets`.

**Updated chapter status (translation rules in the running list):**

  - Sessions 18: rules 1–11 (minimal slice — module / kind /
    attribute structure, type primitives, modality)
  - Session 19: rules 12–16 (reference-bearing slice — FK
    SsKey synthesis, deleteRuleCode mapping, attributes-as-
    primary-source, same-module assumption)
  - Session 20: rule 17 (Origin three-way placeholder under
    JSON-path bound)

Seventeen rules total in the running list.

**One deferred translation question remains** (from the session
17 ADMIRE chapter scope):

  - Inactive-records boundary choice (filter at adapter or
    carry through and let Selection filter). All fixtures so
    far have `isActive: true` everywhere. A mixed-active
    fixture surfaces it.

This continues to defer; the chapter's discipline holds — rules
land under empirical pressure, not under speculative reasoning.

**The composability finding is itself worth marking.** Two
deferred translation questions (lossy-SSKey from session 18,
IS-vs-Direct from session 20) both resolve through the same
input-path expansion (`SnapshotRowsets`). The OSSYS chapter is
discovering that the `JSON projection layer is structurally
lossy in a class-shaped way — multiple V1 fields are stripped at
the same projection layer; the resolution to any single one
generalizes to all. The class is named here so future agents
opening the `SnapshotRowsets` implementation chapter inherit
the framing: it's not three separate fixes; it's one resolution
to a class of lossiness.

Future fixtures may surface additional members of the same
class (e.g., `isSystemEntity` is in the rowsets but not the
JSON; per-table column structure that `FOR JSON PATH`
collapses; check-constraint definitions; etc.). Each is
deferred-by-input-path until `SnapshotRowsets` lands; the
single resolution covers them all.

#### 2026-05-18 (session 21 amendment) — mixed-active fixture surfaces inactive-records boundary; chapter-open backlog clears

Session 21's mixed-active fixture surfaced the deferred
inactive-records boundary choice that the session 17 OSSYS
ADMIRE chapter scope flagged as the third (and last) of its
deferred translation questions.

**Trace before fixture (admire-mode discipline at the slice
level — same as session 20):** V1 SQL carries IsActive flags
through to JSON at three levels — module-level (line 924),
entity-level (line 931), attribute-level (line 759). V1 SQL
also has SQL-layer pre-filtering parameters
(`@IncludeInactive` line 127; `@OnlyActiveAttributes` line
254). **The flags ARE visible to V2 through the JSON path.**
Unlike the SsKey question (session 18) and the IS-vs-Direct
question (session 20), inactive-records-handling is **NOT a
member of the JSON-projection-lossiness class** — V2 has the
information; the boundary choice is genuine.

**The boundary choice and its rationale:**

The architectural alternatives:

  - **Filter at adapter** — entity/attribute with `isActive: false`
    is dropped from the V2 Catalog at parse time.
  - **Carry through with IsActive axis** — V2 IR grows a
    per-record IsActive axis (on Kind / Attribute, or as a
    `Modality.Inactive` variant); the Selection policy filters
    at projection time per A18 amended (filtering is operator
    intent, which is Policy).

A18 amended (Π consumes Catalog × Profile, never Policy)
argues for carry-through in principle — filtering is operator
intent, not catalog evidence. But:

  - V2 IR has no per-record IsActive axis today; carry-through
    requires substantive IR refinement.
  - "IR grows under evidence" — no current V2 consumer demands
    the inactive records' presence in V2's IR. No emitter
    uses them; no pass consumes them; no Selection policy axis
    today reads "include inactive" or "exclude inactive."
  - The adapter's existing return shape `Task<Result<Catalog>>`
    cannot carry per-record auditability for dropped records;
    that requires extending the return shape to a
    Diagnostics-bearing variant (which is its own future
    slice).

**Decision: filter at adapter for now; document the bound.**
The smallest honest-now implementation. The bound resolves
when one of the following triggers fires:

  - **A real consumer demands inactive records' presence in
    V2's IR.** Likely candidates: a refactor.log emission
    that needs inactive entities to compute deletion sets; a
    multi-environment Selection policy that wants different
    inclusion rules for different deployments. When such a
    consumer surfaces, the IR grows (likely as a
    `Modality.Inactive` variant for entity-level, plus a
    per-attribute axis for attribute-level — the exact shape
    depends on the consumer); the adapter changes to
    carry-through; this rule supersedes.
  - **The adapter's return shape extends to support
    Diagnostics-attached audit.** When the adapter's return
    shape grows from `Task<Result<Catalog>>` to a
    Diagnostics-bearing variant (likely
    `Task<Result<Diagnostics<Catalog>>>` or similar), the
    silent drop becomes audited drop — each filtered record
    emits a `DiagnosticEntry` with `Source = "adapter:Osm"`,
    `Severity = Info`, `Code = "adapter.osm.inactiveRecordDropped"`,
    and the dropped record's identity. The structural rule
    stays "filter at adapter"; the audit improves.

**The new translation rule (rule 18, extending the running
list):**

| #  | V1 input shape | V2 output | Rationale |
|----|---|---|---|
| 18 | `entity.isActive: false` or `attribute.isActive: false` (default missing → true per V1's SQL `ISNULL(Is_Active, 1)` semantics) | Inactive entities are dropped from the V2 Catalog at parse time; inactive attributes are dropped from their Kind's `Attributes` list. | Filter at adapter under "IR grows under evidence" — no current consumer demands inactive records' presence in V2's IR. The drop is silent today; the future Diagnostics-attached audit is named in the bound. The carry-through alternative defers until a real consumer surfaces. |

**Module-level `isActive: false`** is **not** exercised by the
mixed-active fixture and not yet handled by the parser.
Defers until a fixture forces the question. The most likely
shape: same filter rule (drop the module entirely), but
modules are coproduct cells (A11) and dropping a module drops
all its entities, which is a bigger semantic claim than
dropping individual records. Surface when a fixture requires
it.

**`physical_isPresentButInactive` field** in V1's JSON (line
769 of SnapshotJsonBuilder; example at the
`DeprecatedField` attribute in this slice's fixture, value 1)
is **read but discarded today**. V1's SQL surfaces this as a
derived flag — the attribute's logical IsActive is false but
the physical column exists. V2's adapter has no use for the
flag because it filters the inactive attribute before
encountering it. Re-open trigger: a Diagnostics-bearing
adapter that wants to surface "the physical column is still
present even though the logical attribute is retired" as an
audit-trail concern.

#### Chapter-open backlog clears at session 21 — natural within-chapter milestone

The chapter has now cleared all three deferred translation
questions named in the session 17 OSSYS ADMIRE chapter scope:

  - **Origin three-way collapse** — resolved session 20 (rule
    17 + bounded-by-input-path disposition + composability
    finding pointing at the SnapshotRowsets canonical
    resolution).
  - **Reference DeleteRule** — resolved across sessions 18–19
    via the Ignore-mapping composition (rule 13's full table
    + the V2-NoAction-as-Ignore-target finding that resolved
    the unreachable-`DeleteRuleIgnored`-keep-reason loose end
    from session 16).
  - **Inactive-records boundary** — resolved this session
    (rule 18 + bound documented + carry-through trigger
    named).

Eighteen translation rules total in the running list across
four substantive slices.

This is a **natural within-chapter milestone**, not a
chapter-close. The chapter has more substantive slices ahead
— index-bearing, static-entity, cross-module FK, plus
whatever new V1 fields surface from real fixtures as the
adapter is exercised against larger inputs. But the
chapter-open's named uncertainties have all been answered
under empirical pressure. The chapter's discipline is
operating; the running list is auditable; the bounds are
documented.

#### 2026-05-19 (session 22 documentation hygiene) — naming the two classes of resolution patterns explicitly

Sessions 18, 20, and 21 together produced findings that fit
into two structurally distinct classes. The composability
finding from session 20 named the first class (lossiness);
session 21's inactive-records resolution implicitly distinguished
the second class (boundary discipline) by resolving differently.
This sub-section names both classes explicitly so future agents
reading the chapter's accumulated translation surface see the
distinction up front rather than re-deriving it.

**The two classes:**

  1. **JSON-projection-lossiness class.** The information is
     **upstream of V2's current input but stripped at V1's JSON
     projection layer**. V2 cannot make the translation through
     the current `SnapshotJson` path because the data isn't
     visible. Resolution: **input-path expansion** via the
     `SnapshotRowsets` variant (per `DECISIONS 2026-05-15 — OSSYS
     adapter translation rules`, session-20 amendment); the
     class resolves *all members together* when the variant
     implements.

     Currently-known members:

       - **SsKey at every level** (session 18) — stripped at JSON
         aggregation; rowsets carry it.
       - **`EspaceKind`** (session 20) — encodes IS-vs-Direct;
         stripped at JSON aggregation; rowsets carry it.
       - **`isSystemEntity`** (observed during session-20 trace;
         not yet exercised by a fixture) — entity-level system
         flag; stripped at JSON aggregation; rowsets carry it.

     Likely future members: per-table column structure that
     `FOR JSON PATH` collapses; check-constraint definitions;
     additional fields the JSON projections happen not to
     include.

  2. **V2-boundary-discipline class.** The information **is
     visible to V2 through the current input**; the translation
     question is V2's own architectural choice about what to do
     with it. Resolution: **V2's own boundary discipline** —
     filter at adapter, carry through the IR, refine the IR with
     a new axis, etc. The choice is bounded by what V2's
     architecture today supports vs what consumer demand would
     need; the smallest honest-now choice is documented; the
     bound resolves on a named consumer-demand trigger.

     Currently-known members:

       - **Inactive-records boundary** (session 21) — V1 carries
         IsActive flags through to JSON; V2 has the choice
         between filter-at-adapter (chosen) and carry-through
         (deferred to consumer demand).

     Likely future members: any V1 field that V2 receives but
     V2's IR has no axis for (e.g., trigger metadata when a
     fixture surfaces it; computed-column definitions; field-
     level descriptions / `meta` strings).

**Why naming the classes matters operationally.**

The two classes have **different resolution paths** and
**different coupling characteristics**:

  - Lossiness-class findings **compose** through one resolution
    (`SnapshotRowsets` implementation absorbs all members
    together). The agent who opens that chapter inherits a
    class to resolve, not a list of bugs to fix.
  - Boundary-discipline findings **don't compose** the same
    way. Each member's resolution depends on its specific
    consumer demand and its specific IR-refinement implications;
    they are individually negotiated. A future
    `Modality.Inactive` variant doesn't automatically extend to
    cover triggers, computed columns, etc.

**The trace-before-fixture pattern classifies findings into one
or the other before implementation begins.** Session 20's trace
of `EspaceKind` placed it in the lossiness class (not in the
JSON; in the rowsets); session 21's trace of `Is_Active`/
`isActive` placed it in the boundary-discipline class (carried
through to JSON; V2 has the choice). Future slices apply the
same trace-before-fixture admire-mode discipline; the
classification informs the resolution shape.

**This sub-section refines, not replaces, session 20's
composability finding.** The lossiness class is one half; the
boundary-discipline class is the other half. Together they
form the chapter's accumulated structural picture of V1↔V2
translation.

**No code change.** Documentation hygiene only. Future
findings classify into one of the two classes (or surface a
third if neither fits, which would itself be a substantive
finding worth marking explicitly).

#### 2026-05-19 (session 22 amendment) — index-bearing fixture surfaces five index translation rules

Session 22's index-bearing fixture surfaced five translation
rules under empirical pressure (rules 19–23). The fixture
exercised three V2-IR-relevant index shapes (PK; unique non-PK;
composite non-unique with included columns) within a single
entity.

**Trace before fixture (admire-mode at slice level):** V1 carries
the indexes[] array through to JSON via the
`outsystems_metadata_rowsets.sql` aggregations (`#AllIdx`,
`#IdxColsMapped`, `#IdxColsJson`, `#IdxJson`). The JSON shape
includes name, isPrimary, kind, isUnique, isPlatformAuto,
storage/perf attributes (isDisabled / isPadded / fill_factor /
ignoreDupKey / etc.), structural fields (filterDefinition,
dataSpace, partitionColumns, dataCompression), and a columns
array with attribute / physicalColumn / ordinal / isIncluded /
direction per column. **All visible to V2 through the JSON
path.**

**Classification:** V2-boundary-discipline class. V1 has the
information; V2's IR scope is what's being chosen. The
translation rules are V2's own architectural choices about
scope, not input-path-bound questions. Same shape as the
inactive-records resolution (session 21).

**The five new translation rules:**

| #  | V1 input shape | V2 output | Rationale |
|----|---|---|---|
| 19 | Index `name` + parent entity + module | `Index.SsKey = OS_IDX_<modName>_<entName>_<indexName>` | Index SsKey synthesis. V1's IndexName is unique per entity (per the SQL extraction's `#AllIdx` clustered key). The synthesis convention extends the existing module/kind/attribute/reference pattern. |
| 20 | `index.isUnique` (boolean) | `Index.IsUnique = isUnique` | Direct mapping. |
| 21 | `index.isPrimary` (boolean) | `Index.IsPrimaryKey = isPrimary` | Direct mapping. V2 distinguishes IsPrimaryKey from IsUnique at the structural level (V1 treats PK as a unique index, but V2 separates the concerns per the Index DU's design notes in `Catalog.fs:144-146`). |
| 22 | `index.columns[].attribute` (string, attribute name within parent entity) | `Index.Columns = [SsKey list]` (resolved via `attributeSsKey moduleName entityName attribute`); sorted by `columns[].ordinal`; `columns[].isIncluded=true` entries dropped at the boundary | Same-entity attribute resolution. V1's `attribute` field names the attribute by string within the parent entity; V2 resolves to the synthesized SsKey. The included-columns drop is the canonical V2 boundary choice (per the OSSYS ADMIRE entry's "what V2 will explicitly NOT carry forward" section); V2's Columns carries only key columns. The ordinal sort preserves key-column order. |
| 23 | Index records have no `isActive` field on the index itself | All indexes are carried through; no filter | V1's index metadata is at storage-object level (sys.indexes); there's no logical activity flag on indexes. The session-21 inactive-records filter does NOT extend to indexes. If a future fixture surfaces inactive-index handling (e.g., V2 grows a per-index activity flag for some emitter), the rule extends under empirical pressure. |

**What this commit explicitly does NOT carry forward (FK
extensions for indexes):**

  - `index.kind` — V1 string field ("Index" / "PrimaryKey" /
    "UniqueIndex" etc.). Redundant with V2's IsUnique +
    IsPrimaryKey flags; V1's `kind` field encodes the same
    distinctions structurally.
  - `index.isPlatformAuto` — V1 marker for OSIDX_-prefixed
    platform-generated indexes. V2 has no auto-generated marker
    today. If a future emitter needs to skip platform-auto
    indexes (e.g., to avoid scripting OutSystems-internal
    indexes the platform regenerates), the rule extends.
  - **Storage/performance attributes** — `isDisabled`,
    `isPadded`, `fill_factor`, `ignoreDupKey`, `allowRowLocks`,
    `allowPageLocks`, `noRecompute`. V2's Index has no axis for
    these. They're DDL-emission concerns, not catalog structure;
    if a future emitter wants WITH-clause scripting, the rule
    extends.
  - `index.filterDefinition` — V1 carries SQL Server filtered-
    index definitions. V2 has no filtered-index axis. Defer
    until a fixture surfaces a filtered index that matters to
    the V2 IR.
  - `index.dataSpace`, `index.partitionColumns`,
    `index.dataCompression` — Storage placement metadata; V2 has
    no axis. Same disposition as filter.
  - `columns[].direction` — Per-column ASC/DESC ordering. V2's
    Index.Columns is a positional SsKey list; no per-column
    direction axis. If a future emitter wants direction-aware
    DDL (e.g., descending PK for time-series tables), the rule
    extends.
  - `columns[].physicalColumn` — V1 redundancy; V2 derives
    physical name from the attribute's ColumnRealization rather
    than from the index column entry. The redundancy in V1 was
    likely for cross-validation; V2 doesn't need it because
    V2's IR resolves through SsKey identity.

**Updated chapter status:**

  - Sessions 18: rules 1–11 (minimal slice — module / kind /
    attribute structure)
  - Session 19: rules 12–16 (reference-bearing slice — FK
    SsKey / deleteRule / cross-attribute)
  - Session 20: rule 17 (Origin three-way placeholder under
    JSON-path bound)
  - Session 21: rule 18 (inactive-records boundary)
  - Session 22: rules 19–23 (index translation — five rules)

**Twenty-three translation rules total** in the running list
across five substantive slices. The chapter has now exercised
all four V2 Kind sub-shapes (Attributes; References; Indexes;
Modality) plus the boundary disciplines (Origin; inactive-
records). Two substantive slices likely remain plausible:
static-entity (exercises Modality.Static populations end-to-end,
couples with Projection.Adapters.Sql/Static.fs); cross-module
FK (refines rule 16's same-module assumption).

**Class summary** (per the session-22 two-classes amendment):

  - Lossiness class: SsKey (rule 1-3 synthesis vs the bound);
    EspaceKind (rule 17's bound). Two members exercised; both
    resolve through SnapshotRowsets.
  - Boundary-discipline class: inactive-records (rule 18);
    index translation choices (rules 19–23). Multiple members
    exercised; each member's resolution is independent.

The class distinction is now empirically confirmed across two
members per class. Future findings classify into one or the
other before implementation begins; the resolution shape
follows from the classification.
