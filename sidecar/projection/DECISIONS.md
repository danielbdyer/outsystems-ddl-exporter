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
