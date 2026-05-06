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
