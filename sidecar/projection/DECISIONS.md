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
