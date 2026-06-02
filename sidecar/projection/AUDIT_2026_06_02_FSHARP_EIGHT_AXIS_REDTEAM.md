# AUDIT 2026-06-02 — F# Practices, Eight-Axis Red Team

> **Status:** open for slice scheduling.
> **Bias:** leaning toward the FP-wizard read where the wizard and the
> veteran disagree. Where the lean would carry real cost without
> commensurate benefit, the veteran wins explicitly and the reason
> is stated.
> **Method:** 25 principles distilled from first principles (with
> the React-best-practices analog as a parallel teaching surface);
> mapped to eight orthogonal audit axes; eight parallel red-team
> agents dispatched, each forbidden from echoing `CLAUDE.md` back as
> evidence; convergent findings flagged where multiple agents
> independently surfaced them.
> **Companion files:** `CLAUDE.md` (the discipline index this audit
> is testing against); `AUDIT_2026_05_31_FIVE_AXIS_REDTEAM.md` (the
> last full-fidelity red-team; this one is leaner-axis and
> principle-driven rather than axis-driven); `WAVE_6_MORPHOLOGY.md`
> (the "calculus is latent" finding this audit substantiates with
> file:line evidence).

---

## 0. Why this document exists

The codebase has been steered for many chapters by a set of operating
disciplines that are excellent in their articulation, demanding in
their practice, and — until now — never quite tested against an
*outside* set of F# best-practice principles. The
`AUDIT_2026_05_DDD_HEXAGONAL_FP.md` and
`AUDIT_2026_05_31_FIVE_AXIS_REDTEAM.md` were internal red-teams;
they hold the codebase to its own published standards. This audit
runs from the other direction: it takes a *generic* set of F#
best practices (recognizable to any senior F# practitioner) and
asks "where does this codebase hit them, where does it bend them,
and where is it ahead of them?"

The principles below are not extracted from `AXIOMS.md` or
`PRODUCT_AXIOMS.md`. They were articulated independently as the
"F# best practices a React-experienced operator should internalize
to read this codebase." When they happen to match the codebase's
own disciplines, that's evidence the codebase has converged
correctly on industry-standard practice. When they happen to bend
or stretch the codebase's disciplines, that's a finding either way:
either the principle is wrong (write the amendment), or the
codebase is.

The output is **a slice plan**, ordered, file:line specific, with
risk and dependency named. The point is not to enumerate complaints
but to point at the next dozen places to apply pressure.

---

## 1. The 25 Principles (constitutional + wizard/veteran)

These were articulated in two passes. The first ten are the
constitution — non-negotiable, gravitational. The next fifteen
add depth from two perspectives: the FP wizard's algebraic lens
and the seasoned veteran's pragmatic check.

### 1.1 The meta-principle

> **The type system is your primary design tool, and effects belong
> at the boundary.**

Everything below cascades from this. If you've internalized React's
"lift state up, derive everything else," it's the same shape: lift
*invariants* up into types, derive computations as pure
transformations. The mechanical difference is that F# refuses to
compile bad programs that React only flags at runtime.

### 1.2 The ten constitutional principles

1. **Make illegal states unrepresentable.** Every runtime
   validation in the core is a failure of imagination at design
   time. Smart constructors return `Result<'a>`; closed DUs make
   exhaustiveness compiler-checked; single-case DUs (`SsKey`,
   `Name`) prevent identity-vs-display confusion. The first place
   to encode a constraint is the type system.

2. **Data is data; behavior is in modules.** Resist `member this.X`
   on records or DUs. Modules can be `[<RequireQualifiedAccess>]`'d,
   swapped, augmented, replaced; member methods can't. Welding data
   and operation trades reversibility for syntax.

3. **Pipelines are sentences; read top to bottom.** `|>` is the
   default; `f (g (h x))` is a smell; `>>` is for point-free
   helpers, not application sites. A pipeline reads as the
   algorithm's prose.

4. **Effects live at the boundary; the core is pure.** No `Async`,
   `Task`, `DateTime.Now`, `Random`, `Console`, `File`, or DB
   connection in Core. Adapters do I/O and hand the core *values*.
   The seam is the gift.

5. **Result and Option, never exceptions for control flow.**
   Exceptions are for invariants the type system couldn't prevent
   and the program can't continue past. Domain errors are named
   `Result<'a, NamedError>`.

6. **Closed sums + exhaustive match: the compiler is the refactor
   tool.** Open polymorphism (interfaces, base classes) at adapter
   boundaries only. New DU variants light up every consumer that
   hasn't handled them.

7. **Inference at the interior; annotation at the boundary.** Every
   public function gets a signature. Private helpers infer.
   Don't annotate every `let x =`.

8. **Property-based tests are specifications, not coverage.** Write
   tests as laws over the domain — idempotence, associativity,
   round-trip, monotonicity. The properties are the spec; the
   implementation must satisfy them.

9. **Computation expressions replace manual plumbing.** When the
   bind chain has 2+ steps, the CE form is more honest. When it
   has 1, `map` is more honest.

10. **Evidence drives growth (YAGNI with teeth).** Don't add a type
    field, a DU variant, or a helper until a real consumer demands
    it. Two-consumer threshold for extraction. Premature
    abstraction is worse than premature optimization.

### 1.3 The fifteen wizard/veteran principles

11. **Types are algebra. Count cardinalities deliberately.** Sum
    types add, product types multiply. A record with eight booleans
    is a 256-state space; most states are bugs. Refactor to a DU
    before the bug ships.

12. **Functor > Applicative > Monad. Use the weakest abstraction
    that suffices.** `Result.map` over `result { let! x = … }` when
    there's one operation. CEs earn their place at 2+ binds.

13. **Monoids are everywhere. Naming the monoid unlocks `fold`.**
    Anything with an associative combine and an identity is a
    monoid. Writers, accumulators, Validation, profile merges —
    all monoids. Naming them makes them composable; naming them
    badly hides the composition.

14. **Lenses and prisms exist. Reach for them when nested update
    bites.** Don't on day one. Reach when the same nested
    record-spread recurs three times. The third occurrence justifies
    the optic; the named lens then makes the fourth occurrence a
    one-liner.

15. **Separate description from execution.** A pipeline returning
    `seq<Statement>` (or `Plan` or `Command list`) plus a separate
    `interpret`/`render` function is the interpreter pattern in
    practical form. Description and execution coupled = welded
    code that can't be tested in isolation, can't have alternate
    interpreters, can't snapshot.

16. **Reader for DI, Writer for logging, State almost never.** F#
    is not Haskell; parameter-passing is often fine. Reach for
    Reader when threading the same env through 8 layers dominates
    line count. State monad in F# almost always wants to be a
    writer or a contained `let mutable`.

17. **Mutation is allowed where it's invisible.** Function-local
    `let mutable`, `ResizeArray`, `Dictionary` for hot loops is
    fine. Module-level mutable state, mutable record fields, or
    any observable order-dependence is a leak.

18. **Don't write Haskell in F#. Don't write C# in F#. Write F# in
    F#.** Each language has costs and benefits; choosing F# and
    then importing another language's idioms incurs the costs of
    both without the benefits of either.

19. **Async at the boundary; sync core; the boundary is thicker
    than you'd like.** `Task<'a>` infects upward through every
    caller. Once a coordinator hits the DB, it and its callers
    are async. The pure core is sometimes smaller than the wish.

20. **Equational reasoning is your debugging superpower.** Pure
    code can be substituted: replace any call with its return
    value and the program is unchanged. This makes bisecting,
    REPL-driven debugging, and snapshot testing tractable. Every
    impurity costs this; budget accordingly.

21. **Profile before optimizing. Allocations are usually the
    culprit.** Premature `[<Struct>]` and `inline` are F#'s
    canonical perf own-goals. The three-candidate / one-confirmed
    pattern with refuted swaps documented earns its place across
    a codebase.

22. **File order is architectural seam, not annoying limitation.**
    F# refuses cycles by construction. Primitives at the top,
    aggregates below, pipelines below that. When you want to
    reference a later file, the architecture is wrong.

23. **Naming: concept-shaped, not action-shaped. Generic-suffix
    smell test.** `Helper`, `Util`, `Manager`, `Service`, `Handler`,
    `Processor`, `Wrapper`, `Builder`, `Factory`, `Provider` —
    all smells. The concept is being squashed.

24. **Tests are the spec. Properties prove laws; examples pin
    behavior. Together they document.** Test names cite the
    axiom: `` `A24: trail is chronological under bind` ``. Names
    that don't tell you what's being proven aren't tests, they're
    coverage.

25. **Onboarding via constraints, not freedom.** A codebase with
    "anything goes" teaches nothing; a codebase with explicit,
    operated constraints teaches the patterns by osmosis. Write
    the constraints down; audit them at chapter close.

### 1.4 Where the wizard and the veteran disagree

| The wizard says | The veteran says | This audit's bias |
|---|---|---|
| Use the most powerful abstraction the type system allows | Use the simplest abstraction that suffices today | **Wizard wins** when a real consumer exists or is plausibly imminent; veteran wins on speculative-only |
| Optics for all nested updates | Optics when the same nested update recurs 3+ times | **Wizard wins** at three sites; below three, veteran wins |
| Free monads / tagless final for effect description | `seq<Statement>` + `interpret` function | **Veteran wins** in F# — the wizard's tools have higher syntax tax here |
| Reader/Writer/State monads for everything | Parameter-passing + return values | **Veteran wins** unless line count dominates |
| Make illegal states unrepresentable always | Make illegal states unrepresentable when the cost is < the cost of validation | **Wizard wins** — the cost is almost always less than expected |
| Total functions everywhere | Total in the core; the boundary is partial | **Both correct** at their layer |
| Point-free composition is elegant | Named parameters are documentation | **Veteran wins** after 2 composition steps |
| Properties over examples | Examples first; properties when the law is clear | **Both** — examples for regressions, properties for laws |

**This audit's lean:** when the wizard and the veteran tie, the
wizard wins. When the wizard's recommendation would require
speculative work with no consumer in sight, the veteran's defer-with-trigger
wins. The bias is toward *building the vocabulary the next chapter
will need*, not toward *minimizing surface area today*.

---

## 2. The eight audit axes

The 25 principles map to eight orthogonal axes (one red-team agent
per axis). The mapping is not 1:1 — some principles span axes; some
axes synthesize multiple principles. The agents were instructed to
evaluate against *actual code* in `src/`, not against `CLAUDE.md`'s
own claims.

| Axis | Principles covered |
|---|---|
| 1. Type system & illegal states | 1, 6, 11 |
| 2. Data/behavior separation & naming | 2, 23 |
| 3. Composition style & idiomatic F# | 3, 7, 12, 18 |
| 4. Effects, async, errors | 4, 5, 19 |
| 5. Monads, monoids, optics, interpreter | 9, 13, 14, 15, 16 |
| 6. Evidence-driven growth & mutation | 10, 17 |
| 7. Tests as executable spec | 8, 24 |
| 8. Equational reasoning, perf, architecture, constraints | 20, 21, 22, 25 |

Each agent returned a strengths inventory, drift candidates with
file:line citations, and a one-sentence verdict. Their reports are
summarized in §4 and §5; full reports are preserved in the session
transcript.

---

## 3. Strengths inventory (the codebase's report card)

The codebase scores high on **most** axes. This section names what's
working — not as a victory lap but because future amendments need
to know what's load-bearing before they touch it.

### 3.1 Pure-core discipline is structurally upheld

Searches for `Random`, `Console.`, `File.`, `Directory.`,
`SqlConnection`, `StreamReader`/`Writer`, `task {`, in `Projection.Core`
returned **zero production hits**. The only `Task<` mentions are
in doc-comment code samples (`Bench.fs:89`, `TransformRegistry.fs:191`,
`Types.fs:38`). The Core/Pipeline seam is clean: `Compose.read` /
`runWithConfig` / `run` / `runSkeletonOnly` at
`src/Projection.Pipeline/Pipeline.fs:467-625` are the *only*
`Task<Result<_>>`-returning entry points; below that line, code
is sync `Result<'a>` everywhere. The custom analyzer
`src/Projection.Analyzers/NoUnsafeTimeInCoreAnalyzer.fs` (PRJ001)
enforces this structurally.

### 3.2 Identity and aggregate-root invariants are smart-constructor-enforced

`SsKey` is a four-variant closed DU (`OssysOriginal | Synthesized |
DerivedFrom | V1Mapped` at `Identity.fs:45-119`) with constructors
that can fail returning `Result<SsKey>` and constructors that cannot
(`ossysOriginal`, `fromV1`) returning bare values. `Catalog.create`
at `Catalog.fs:1364-1458` enforces five invariants (module
disjointness, kind disjointness, reference referential integrity,
index column existence), aggregating errors via
`Validation.duplicateKeyErrors`. Downstream consumers (`tryFindKind`,
FK emitters) trust the value.

### 3.3 Data is data; modules carry operations

Every IR type in `Projection.Core` is a record or closed DU with
**zero `member this.X` methods**. Operations live in companion
modules carrying `[<RequireQualifiedAccess>]` (e.g., `module Lineage`
at `Lineage.fs:335`). The only `member` sites are F# CE builder
classes (a language-level protocol requirement) and `[<CustomEquality>]`
overrides (deliberate algebraic law). Both are justified by the
language, not by OO drift.

### 3.4 Concept-shaped naming is ubiquitous

`Catalog`, `Identity`, `Lineage`, `Reference`, `Policy`, `Profile`,
`Episode`, `Migration`, `Lifecycle`, `Coordinates`, `Tolerance`,
`Classification`, `BoundedContext`, `Centrality`,
`TransformRegistry`, `ChangeManifest`. The `*Rules` suffix for
strategy modules (`NullabilityRules`, `UniqueIndexRules`,
`ForeignKeyRules`, `CategoricalUniquenessRules`) is concept-shaped:
the body of decisions is the *concept*. Action-suffix sites
(`*Emitter`, `*Runner`, `*Reader`) are confined to adapters and
targets where naming an external-facing actor is principled.

### 3.5 Writers are properly monoidal, with the monoid named

`Diagnostics.fs:301-303` explicitly names `(List, ++, [])` as the
underlying monoid for `Lineage` and `Diagnostics`; the dual writer
is the product monoid `(LineageEvent list × DiagnosticEntry list,
⊕, ([], []))`. `Lineage.ofValue` / `Lineage.bind` / `tellMany` and
the symmetric `Diagnostics` primitives are the canonical algebraic
surface. `Profile.merge` + `Profile.empty` (`Profile.fs:1247-1296`)
is the cleanest example: docstring names the monoid; FsCheck
property tests verify commutativity, associativity, identity.

### 3.6 SSDT description-vs-execution is exemplary

`Projection.Targets.SSDT/Statement.fs` declares a typed `Statement`
DSL (~30 variants). `SsdtDdlEmitter.statements : Catalog ->
seq<Statement>` at `SsdtDdlEmitter.fs:800` is pure description.
`Render.toSql` / `Render.toText` / `Deploy.executeStream` are
independent interpreters consuming the same stream. Comments
explicitly invoke A35 ("Π's canonical output is a deterministic
statement stream"). `StaticPopulationEmitter.statements` follows
the same shape.

### 3.7 Tests function as executable specification

`AxiomTests.fs` is 1138+ lines, 116 entries (83 `[<Fact>]`, 33
`[<Fact(Skip)>]`) wiring every A1–A43 + T1–T16 + L3-* + H-* item
to either a citation-of-canonical-witness or a structural commitment.
The Skip rationales are substantive (hundreds of characters each;
none are TODO-shaped). Property tests state real laws: writer-monad
triples (left identity, right identity, associativity), Kleisli
laws on `Pass<'a,'b>`, the Certificate isomorphism, SSDT emitter
permutation invariance.

### 3.8 Perf gate is statistical, not nominal

`scripts/perf-gate.sh:231-303` reads `bench/baseline-canary.json`
(μ + σ per label from N≥5 warm runs), applies a Bayesian σ floor
(`σ_eff = max(σ_obs, μ × 0.20)`), and gates per-label
`latest > μ + Kσ_eff`. Refuted swaps are documented with bench
evidence: `DECISIONS.md:7420-7467` (sys.* vs INFORMATION_SCHEMA,
MARS-parallel readside — both reverted with measured deltas) and
`DECISIONS.md:20154-20164` (four EXECUTION_PLAN §5.7 candidates
assessed; `internalEdgesOf` formally refuted at
`pass.topologicalOrder.kind = 2ms` total).

### 3.9 Mutation is reified, scoped, LINT-tagged

The 25+ `let mutable` sites in `src/` are function-local
accumulators or graph algorithms. Every mutation-heavy file carries
an explicit `// LINT-ALLOW-FILE-MUTATION:` header naming the
discipline. `LineageBuffer.Buffer = private Buffer of List<...>`
opaquely seals the mutable accumulator. Zero module-level
`let mutable` in pure Core except `Bench.fs:74-75` (observation
infrastructure, documented). No `ref` cells anywhere. No `mutable`
record fields anywhere.

### 3.10 File order is principled and explicitly justified

`Projection.Core.fsproj:57-59, 74-77, 94-95` carry inline rationale
comments at the points where order matters. No `ProjectReference`
cycles. Pipeline aggregates over Core + adapters + targets exactly
as the layering demands.

---

## 4. The drift catalog

Findings are tiered by severity and multi-agent convergence.

### 4.1 Tier 1: Convergent drift (three agents independently)

#### 4.1.1 The Cluster-B speculative-optics cluster

**Three agents independently surfaced this without coordination.**

`Prism<'a, 'b>`, `Lens<'s, 'a>` + `CatalogLenses`,
`PassContext<'env, 'a>`, `LineageTree<'a>`, `Certificate<'a>`,
`DiagnosticLattice` — all defined in
`src/Projection.Core/Diagnostics.fs:802-986` and
`src/Projection.Core/Lineage.fs:660-735`, all law-tested in
`tests/Projection.Tests/DiagnosticsTests.fs:705-721` and
`AxiomTests.fs:1138-1146`, all with **zero production callers**
in `src/`.

The diagnostic case (Agent 6): `Passes/SymmetricClosure.fs:218-234`
is a triple-nested `{ c with Modules = ...; Kinds = ...; References
= ... }` that is *exactly* the canonical
`Lens.compose CatalogLenses.kindsOf CatalogLenses.referencesOf |>
Lens.over (...)` shape. The lens for it exists; the patch that
would use it doesn't. The docstring at `Diagnostics.fs:897` promises
"deep-nested updates in passes compress massively when the Lens
composition is named" — the compression hasn't happened.

`WAVE_6_MORPHOLOGY.md` names this finding: "the calculus is latent,
not activated." The naming is correct; the latency persists.

**Why this is Tier 1:** this is the codebase's own
anticipation-vs-speculation rule (`DECISIONS 2026-05-13`) being
violated by the codebase itself. Position A (full extraction)
requires "concrete second consumer"; the second consumer is a
future H-058 ticket, not actual code. The other recurring drift
patterns are local; this one is *systematic*.

**Wizard's read:** the abstractions are *correct*. The lenses
factor the right axis (Catalog → Module → Kind → Reference); the
prisms factor the right partial-accessor shape; the Kleisli arrow
is the right name for the pipeline category. The slice to write is
not "delete the optics"; the slice is "land the second consumer."

**Veteran's read:** what's not being used will rot. The lenses
will drift from the IR (record fields will be renamed; lens
definitions will stale). Either adopt or delete; the in-between
state is the failure mode.

**This audit recommends (with wizard bias):** stratified action.
The `Lens` family has three identified consumers (§5 below) and
should be adopted in the next slice. The `Prism`, `PassContext`,
`LineageTree`, `Certificate`, `DiagnosticLattice` families have
named-but-unreached consumers and should be **deferred with
written triggers**, each trigger naming the chapter that earns
adoption and the specific consumer that will land it. Today's
status quo — defined, law-tested, no consumer, no documented
defer-trigger — is what fails.

### 4.2 Tier 2: High-impact single-agent findings

#### 4.2.1 Identity-typing is half-built

**The productive dual of the optics cluster.** Same shape
(defined-but-unused) at a more impactful site.

`Coordinates.fs:42-137` defines `SchemaName`, `TableName`,
`ColumnName` as private-constructor VOs with validation (128-char
SQL Server identifier cap, etc.) — but **zero record fields anywhere
in `src/` use them**. `TableId` still carries `Schema : string;
Table : string` (`Coordinates.fs:151-156`). `ColumnRealization.ColumnName
: string` (`Catalog.fs:388-391`). `PhysicalColumn` carries three
`string` fields. `Sequence.Schema : string; DataType : string`
(`Catalog.fs:240-242`). The comment at `Coordinates.fs:23-28`
acknowledges this as "Stage 2 deferred-with-trigger" — but the
trigger is unfired by name.

The compiler currently *cannot* refuse `{ Schema = "Customer";
Table = "dbo" }` transposed. The VOs are decorative.

Same shape at `TighteningIntervention.id : string`
(`Policy.fs:241-262`). The id flows into `LineageEvent.NullabilityDecision
of interventionId: string` (`Lineage.fs:116`), into
`tryFindForeignKey (id: string)` (`Policy.fs:720`), into emitted
manifest entries. It's a nominal identity wearing a stringly-typed
coat. The same shape governs `passName : string` constants across
17 pass modules (`src/Projection.Core/Passes/*.fs`).

**Wizard's read:** wrap at field-definition sites. The cascade of
type-mix-up errors that the system currently cannot catch becomes
caught. Every consumer that today writes `{ Schema = "Customer";
Table = "dbo" }` is currently a latent bug; the wrap surfaces them.

**Veteran's read:** big slice. Probably 1–2 weeks. Worth it: pays
compounding interest. Risk: V1 carbon-copy sources will need
adapters at the seam. Mitigation: introduce VOs incrementally,
starting at the type definition; let the compiler walk to the
call sites.

**Recommendation:** schedule the slice. This is the largest
single principle-debt in the codebase. The optics cluster is more
*visible*; the identity cluster is more *load-bearing*. Both
should land.

#### 4.2.2 Cardinality smell in IR boolean tuples

`Index` (`Catalog.fs:701-808`) carries 11 booleans:
`IsUnique, IsPrimaryKey, IsPlatformAuto, IsPadded, AllowRowLocks,
AllowPageLocks, NoRecomputeStatistics, IgnoreDuplicateKey,
IsDisabled, …`. Some flags genuinely are independent SQL Server
toggles (lock modes, padding, statistics). But `(IsUnique,
IsPrimaryKey)` is a 4-state space encoding a 3-state reality:
`IsPrimaryKey = true` semantically implies `IsUnique = true`, and
`IsUnique = false ∧ IsPrimaryKey = true` typechecks while being
semantically impossible.

`Reference` (`Catalog.fs:549-595`) carries `IsUserFk`,
`HasDbConstraint`, `IsConstraintTrusted`, `OnUpdate : ReferenceAction
option`. `IsConstraintTrusted = true` paired with `HasDbConstraint
= false` is meaningless but expressible.

`ApprovalRecord` (`ApprovalWorkflow.fs:6-30`) is a DU-in-disguise:
`Decision : ApprovalDecision + ApprovedBy : string option +
Rationale : string option`, with a convention "Pending → both
`None`; Approved/Rejected → both `Some`." Typechecks: `{ Decision
= Pending; ApprovedBy = Some "alice" }`.

**Wizard's read:** count the cardinalities. Each illegal state is
a future bug. The fix is structural: `type IndexUniqueness =
NotUnique | Unique | PrimaryKey` collapses the 4-state to 3-state.
`type ApprovalState = Pending of at | Decided of decision × by × at
× rationale` collapses the optional-fields pattern. `Reference`
needs more care because some boolean orthogonality is genuinely
independent — but `IsConstraintTrusted` should be inside `OnUpdate`
or the `HasDbConstraint` variant.

**Recommendation:** small slice. `IndexUniqueness` and
`ApprovalState` are mechanical; `Reference` deserves a deliberate
modeling pass.

#### 4.2.3 The `DateTimeOffset` analyzer gap

`NoUnsafeTimeInCoreAnalyzer.fs:46-53` forbids `DateTime.Now /
DateTime.UtcNow / DateTime.Today / Guid.NewGuid / Random.Shared` in
Core. Five sites slip through: `VersionedPolicy.fs:231,256` and
`ApprovalWorkflow.fs:43,75,97` call `DateTimeOffset.UtcNow`
(note: `DateTimeOffset`, *not* `DateTime`). The analyzer's
forbidden-suffix list is `DateTime`-only.

**Why this matters:** the *Now*-suffixed methods sit in Core and
physically capture the wall clock. The pure variants taking an
explicit `at : DateTimeOffset` exist alongside (`Episode.fs:1-23`
is the canonical "boundary-supplied `at`" shape). The convenience
wrappers were added; the analyzer wasn't extended.

**Recommendation:** three-line patch to the analyzer's
forbidden-suffix list, plus either move the *Now*-suffixed wrappers
to Pipeline or delete them and have callers pass the clock
explicitly. This closes the only Core-purity slip the audit found.

#### 4.2.4 Description-vs-execution asymmetry between SSDT and JSON

SSDT is exemplary (§3.6). JSON is not.

`Projection.Targets.Json/JsonEmitter.fs:70-126` —
`writeAttribute` / `writeReference` / `writeModality` take
`Utf8JsonWriter` as parameter and write directly. A typed
intermediate exists (`kindJsonNode : JsonNode` at lines 156-175)
but the catalog-level path still does direct writes. The pattern
SSDT uses — typed DSL → `seq<Statement>` → independent interpreter
— is *not* applied to JSON.

**Wizard's read:** asymmetry is itself information. The principle
applies at all sibling-Π emitters or none; the half-application is
the smell. Lift JSON to `seq<JsonNode>` (or a typed `JsonValue`
DSL); the renderer becomes one interpreter.

**Veteran's read:** wait for an excuse — a real alternate
interpreter (snapshot consumer, diff tool, etc.) that wants the
description-form.

**Recommendation:** wizard wins, but with low urgency. Schedule
when an excuse surfaces or when the next sibling-Π emitter lands
and inherits the symmetric shape.

### 4.3 Tier 3: Polish (named but not blocking)

#### 4.3.1 Dead CE machinery

`lineage { … }` / `diagnostics { … }` / `lineageDiagnostics { … }`
builders at `Lineage.fs:411-460` and `Diagnostics.fs:274-289, 416-460`
have **zero production call sites**. Production code uses the
function form (`Lineage.ofValueAndEvents`, `Diagnostics.tellMany`).
The CE machinery was shipped for "type-level guarantee at adoption
sites" (per CLAUDE.md H-001/H-002) — but adoption hasn't happened.

**Wizard's read:** adopt. The CE form makes manual record-building
syntactically impossible inside the block, which is exactly what
the writer-fidelity discipline says: "manual record-building is
forbidden." The function form preserves the discipline by
*convention*; the CE form preserves it by *construction*. The CE
is the structurally stronger commitment.

**Veteran's read:** the function form works. The CE adds a layer
of indirection (the `{ }` block, the `let!`/`do!` desugaring) for
no behavior change. Delete.

**Recommendation:** wizard wins, but staged. Add a CE-adoption
slice that converts ~5 pass drivers (representative sample); if
the syntactic enforcement materially improves the code, expand to
all pass drivers. If not, delete the CE machinery in a follow-up.
This trial gives the principle a real test before committing to
the full sweep.

#### 4.3.2 Point-free drift in `Policy.fs`

`Policy.fs:697, 710, 722, 733` — four sites use
`extractNullability >> Option.filter (fst >> (=) id) >>
Option.map snd`. Three `>>` chains plus a nested `fst >> (=) id`
inside the filter. The four occurrences correspond to the four
`tryFindX` accessors (one per axis).

**Wizard's read:** this is fine if it reads. It doesn't quite —
the nested point-free composition (`fst >> (=) id`) requires the
reader to evaluate inside-out, which is exactly what the pipeline
principle forbids.

**Veteran's read:** named-match form reads cleaner:

```fsharp
fun candidate ->
    match extractNullability candidate with
    | Some (xid, cfg) when xid = id -> Some cfg
    | _ -> None
```

**Recommendation:** veteran wins (rare in this audit). Refactor
the four sites to named-match. This is the codebase's most
Haskell-leaning code; the refactor moves it back into idiomatic F#.

#### 4.3.3 `Static.fs:206-257` — 50-line nested `Result.bind` ladder

The deepest indentation in the project. Two prefix-form
`Result.map f x` calls at the bottom (lines 252, 254) intermixed
with nested `Result.bind`.

**Recommendation:** adopt the `result { let! … }` CE that the
codebase uses elsewhere (e.g., `CatalogCodec.fs:589`). 50-line
flat CE block reads as the recipe; nested-bind reads as the
machinery.

#### 4.3.4 Coverage-padding tests

`TopologicalOrderTests.fs:110-145` asserts `Assert.Equal(x, x)` on
auto-derived structural equality (`OrderingMode values round-trip
through structural equality`, etc.). Tests the F# compiler, not
a contract. Same shape at `NullabilityPassTests.fs:244-250`
(`catalog passes through unchanged: structural by signature` —
the comment names the problem). `T1 determinism` tests are
example-shaped where they could be properties (the existing
`AdjunctionLawTests.fs:80-88` permutation invariance is the model).

**Recommendation:** prune the structural-equality tests. Promote
T1 determinism example tests to properties over generated catalogs/
policies/profiles. The pruning is a few hours; the property
promotion is a slice on its own.

#### 4.3.5 `Bench.fs:74-75` module-level mutable

The only module-level mutable in pure Core: `Dictionary<string,
ResizeArray<int64>> + lockObj`. Output is observation-only; no
decision flows from it. But it observably breaks pure substitution:
`Bench.scope` writes process-global state; `Bench.snapshot ()`
returns different values across calls.

**Wizard's read:** this *is* a referential-transparency violation.
But its scope is bounded: instrumentation, not algebra.

**Veteran's read:** the practical alternative — passing the bench
collector through every function — is worse. The DI cost
outweighs the purity dividend.

**Recommendation:** both agree on the *practice* (keep the
module-level state). What's missing is the *documented carve-out*.
Add a `DECISIONS` entry or expand the `Bench.fs:44-47` docstring
to explicitly name "the equational-reasoning exception, justified
by observation-only output and the cost of the DI alternative."
This is the gap: the principle says no module-level state; the
practice has one named exception; the doc should match.

#### 4.3.6 Imperative sprawl in `CentralityPass.fs` and `BoundedContextPass.fs`

`CentralityPass.run` (`CentralityPass.fs:39-136`) uses mutable
`Dictionary`, `let mutable rank`, `while` loops; the body is ~90
lines mixing initialization, two stacked iteration loops, and final
sort. `BoundedContextPass.labelPropagation`
(`BoundedContextPass.fs:47-89`) follows the same shape.

The algorithms are iterative (PageRank, label propagation); some
imperativeness is justified by the underlying math. But the
*shape* is what's worth a second look — 90-line bodies with mixed
concerns (init / iterate / converge / sort) read as the
function-too-big smell.

**Recommendation:** decompose into smaller pure helpers. The
mutation stays where it's earned (the inner iteration loop); the
shape becomes `let initial = …; let converged = iterate initial;
let final = sort converged`. This is mechanical and pays
readability dividends.

#### 4.3.7 `DECISIONS.md` is 20,514 lines

Documentation surface volume. Not a code issue. The "Active
deferrals" index at the top is the structural safeguard against
drift; if it lapses, the doc volume hides decisions. The
chapter-close ritual already audits this; the recommendation is to
keep auditing it.

**This is operational, not algebraic.** Named here so the slice
plan acknowledges the cost of the discipline-documentation
practice — but no action is recommended beyond what's already
codified.

---

## 5. The wizard's bias — why adoption wins over deletion

This audit leans wizard. The implication for the speculative-surface
findings (the Cluster-B optics, the dead CE machinery, the
half-built identity typing) is that the recommended slice is
*adoption*, not *deletion*.

The case for adoption:

**One.** The abstractions are *correct*. Multiple agents independently
verified that the lenses factor the right axis, the writers are
properly monoidal, the Kleisli arrow names the right structural
role. The work to invent them is already done; deletion throws it
away.

**Two.** The vocabulary makes the next chapter's writing easier.
`Lens.compose CatalogLenses.kindsOf CatalogLenses.referencesOf`
is shorter than the triple-nested record spread it replaces — but
more importantly, it *names* the axis being updated. A reader
sees the lens path and understands the intent. The spread requires
counting `with`s.

**Three.** F# refactor cost is low *because of the type system*.
Adopting a lens at three sites is a one-day patch. Adopting an
identity-typed VO at every field site is a one-week patch — but
the compiler walks every consumer for you. The cost of adoption
is bounded by file count; the cost of *not adopting* (the
ongoing risk of type-mix-up, the ongoing maintenance of the
defined-but-unused surface) is unbounded over time.

**Four.** The codebase's own discipline demands it. The
anticipation-vs-speculation rule (`DECISIONS 2026-05-13`) names
Position A (full extraction) as conditional on "concrete second
consumer." The optics surfaces have known consumers
(SymmetricClosure for `Lens`, the SQL emitter for `Prism`, the
operator-triage surface for `DiagnosticLattice`); the consumers
just haven't landed. *Landing them* honors the discipline. Leaving
them as speculative-surface-with-tests *violates* the discipline.

**Five.** The wizard's bias is *informed*, not *gratuitous*. The
case for the algebraic vocabulary is that it composes — once the
lens exists at three sites, the fourth site is free; once the
Kleisli arrow exists at the pass-chain, the next category (refactorlog
diffs, SchemaDelta) inherits the structure. The veteran's
"simplest abstraction that suffices today" is *correct* on a
greenfield codebase. On a codebase that has invested in the
abstractions and law-tested them, the simplest thing that suffices
is to *use them*.

Where the wizard does *not* win: speculative surfaces with no
named consumer (the prism family without the Catalog ↔ DDL slice;
the comonad without a real reader-pull) get a written trigger
(§7) rather than immediate adoption. The bias is toward building
the vocabulary the next chapter will need; it is *not* toward
"adopt everything because algebra is pretty."

---

## 6. The slice plan

Twelve slices, ordered by leverage and dependency. Each names:
its principle citation; its files and line ranges; its rough size;
its risk; its dependency on prior slices.

### Slice 0 (immediate; 1 hour). DateTimeOffset analyzer extension

**Principle:** 4 (effects at boundary) + 17 (mutation invisible).
**Files:** `src/Projection.Analyzers/NoUnsafeTimeInCoreAnalyzer.fs:46-53`.
**Action:** add `("DateTimeOffset", "UtcNow")`,
`("DateTimeOffset", "Now")` to the forbidden-suffix list. Move
`VersionedPolicy.evolveNow` / `ApprovalWorkflow.approveNow` /
`rejectNow` / `pending` to either Pipeline or the call site (pass
`at` explicitly).
**Risk:** low; mechanical.
**Dependency:** none. Schedulable now.

### Slice 1 (small; 1 day). Bench equational-reasoning carve-out documented

**Principle:** 20 (equational reasoning) + 25 (constraints
codified).
**Files:** `src/Projection.Core/Bench.fs:44-47` (docstring) or a
new `DECISIONS 2026-06-XX` entry.
**Action:** explicit doc naming the carve-out: "module-level
`Dictionary<string, ResizeArray<int64>>` + `lockObj` is the only
exception to the no-module-level-mutable rule in Core; justified
by (a) observation-only output, (b) the DI alternative being worse
than the principled cost. The exception is *named*, not implicit."
**Risk:** none.
**Dependency:** none.

### Slice 2 (small; 1–2 days). IR boolean-tuple collapse

**Principle:** 1 (illegal states), 11 (cardinality), 6 (closed sums).
**Files:** `Catalog.fs:701-808` (`Index` → `IndexUniqueness`),
`ApprovalWorkflow.fs:6-30` (`ApprovalRecord` → `ApprovalState`).
Deliberately defer `Reference` (`Catalog.fs:549-595`) pending
deliberate modeling.
**Action:**
```fsharp
type IndexUniqueness =
    | NotUnique
    | Unique
    | PrimaryKey

type ApprovalState =
    | Pending of at : DateTimeOffset
    | Decided of decision : ApprovalDecision
                 * by : Email
                 * at : DateTimeOffset
                 * rationale : string option
```
Update consumers (compiler will walk).
**Risk:** medium — `Index` has many consumers; serialization codecs
need to round-trip across the variant change.
**Dependency:** none. Pairs naturally with the smart-constructor
audit (§4.2.1's identity slice) but is independent.

### Slice 3 — **LANDED 2026-06-02**. Lens adoption + Optics.fs extraction + broader Core sweep.

**Principle:** 14 (optics when nested update recurs), 13 (monoids/
optics named), the Tier-1 finding. **Plus** the audit's own §4.3 "Optics
file location is itself a smell" finding (resolved as part of this slice).
**As-shipped surface:** the audit's narrow 3-site recommendation expanded
mid-slice on the wizard's bias (with operator agreement) to a broader Core
sweep that fits the lens idiom across every catalog-manipulating site, so
the vocabulary is retained moving forwards as the default.

**Sites landed:**
1. `Passes/SymmetricClosure.fs:218-234` — `attachInverses` extracted
   as a named helper using `referencesOf`; outer pipeline uses
   `modules` + `kindsOf` traversal.
2. `Passes/LogicalColumnEmission.fs:74` — `columnOf` (new lens).
3. `Passes/LogicalColumnEmission.fs:78` — `attributesOf`.
4. `CatalogDiff.fs:397` (was `:341` pre-merge) — `columnOf` for
   the `Nullability` facet.
5. `CatalogDiff.fs:483` — `kindsOf` in the conditional update branch.
6. `LineageBuffer.fs:92` (`CatalogTraversal.mapKinds`) — `modules` +
   `kindsOf`.
7. `Policy.fs:413-417` (`Policy.filterCatalog`) — `modules` + `kindsOf`
   with `List.filter` at the leaf.
8. `ModuleFilter.fs:375` — `kindsOf` via `Lens.set`.
9. `ModuleFilter.fs:419` — `kindsOf` via `Lens.set`.
10. `Passes/NamingMorphism.fs:104` — `modules`.

**Structural fix:** `Lens<'s, 'a>` + `module Lens` + `module CatalogLenses`
extracted from `Diagnostics.fs` (compile-order ~73) to a new
`src/Projection.Core/Optics.fs` (compile-order 36, immediately after
`Catalog.fs`). This was forced by the build (the broader sweep
*required* the lens vocabulary to be visible to every catalog-
manipulating site) and simultaneously resolves the audit's own
§4.3 "Optics file location is itself a smell" finding. The
extraction is the principled wizard's move; the narrow-3-site
slice would have left this smell untouched.

**New lens added:** `CatalogLenses.columnOf : Lens<Attribute,
ColumnRealization>` — two production consumers (LogicalColumnEmission +
CatalogDiff) sharing the `Attribute.Column` navigation step. Inner
`ColumnRealization` field-level lenses NOT added (each would be
a one-consumer micro-extraction; the record-update at the call site
covers the leaf update cleanly).

**Sites deferred-with-trigger:**
- `Catalog.fs:1286` (`Catalog.mapKinds`) — the traversal primitive
  lives in `Catalog.fs` BEFORE `Optics.fs` in the compile order
  and therefore can't reference the lenses. Lensifying it would
  require splitting `module Catalog` operations into a post-Optics
  file; deferred to a future "Catalog traversal extraction" slice.
  A one-line comment at the site names the constraint so future
  agents don't try to lensify it without doing the extraction first.
- Adapter sites (`ReadSide.fs:1082, 1111, 1136, 1229`,
  `DataIntegrityChecker.fs:136`, `Static.fs:252`) — adapter scope;
  audit explicitly scoped Slice 3 to Core. Follow-up slice.

**Discipline updates landed in the same commit:**
- `CLAUDE.md` F# feature surface table: `Lens<'s, 'a>` entry updated
  to point at `Optics.fs`, list new production consumers, and name
  the compile-order rationale.
- `CLAUDE.md` programming style "Functions" section: new bullet
  codifying "lensed updates for nested IR substructures" as the
  default idiom + worked-precedent list.

**Property test added:** `H-015 CatalogLenses.columnOf: get + set
roundtrip` in `DiagnosticsTests.fs`. Generic Lens laws were already
property-tested at H-015 (line ~935); per-lens roundtrip tests
follow the existing pattern for `modules` / `sequences`.

**Validation:** full solution builds clean (0 warnings, 0 errors).
Fast pure test pool: 2650 passed / 207 skipped / 0 failed (~70s).
Behavior preservation by lens-laws; no test changes needed for
the refactor itself.

**Why broader than the audit's recommendation:** mid-slice the agent
discovered the lens vocabulary's reach was much larger than the
audit's three named sites suggested — `kindsOf` had 5+ potential
consumers, `modules` had 3+, `attributesOf` had 1+ (plus the new
`columnOf` at 2). The veteran's "narrow scope" call would have left
the discipline as a curiosity used at 3 places; the wizard's
"vocabulary value scales with ubiquity" argument carried with operator
agreement, and the audit was deliberately overridden mid-slice per
the codebase's own "audit during the work" discipline
(`DECISIONS 2026-05-09`). The expansion paid for itself within the
same slice: the build forced the Optics.fs extraction, which
resolved a separate audit finding; the broader adoption made
the lens form the default idiom (CLAUDE.md updated to match).

### Slice 4 — **LANDED 2026-06-02**. CE machinery adoption + violation-fix sweep.

**Principle:** 9 (CEs replace manual plumbing), the writer-fidelity
discipline (CE form makes manual record-building syntactically
impossible).
**Action-gate verdict: PROCEED (with bigger payoff than expected).**
The trial-form's question was "does CE materially improve readability
or catch a violation?" The sweep surfaced seven Pattern B sites where
pass drivers were hand-rolling `{ Value = result; Entries = [entry] }`
record literals — *direct* writer-fidelity discipline violations the
function-form had let slip. The CE form syntactically prevents this,
turning seven discipline-fixes into a byproduct of the form-change.
The bias upward (10 sites instead of ~5) followed the wizard's
"fit the idiom into the codebase" mandate that vindicated itself
during Slice 3.

**Sites landed (10 total, two patterns):**

Pattern B — direct violation fixes (7 sites; the CE form prevents the
manual `{ Value = ...; Entries = ... }` record literals):
1. `CentralityPass.fs:136`
2. `BoundedContextPass.fs:154`
3. `SchemaComplexityPass.fs:141`
4. `QueryHintPass.fs:97`
5. `ProfileAnomalyPass.fs:132`
6. `TopologicalOrderPass.fs:691` (schema islands)
7. `TopologicalOrderPass.fs:792` (cascade shock zones)

Pattern C — canonical-primitive sites converted for consistency (3 sites):
8. `NullabilityPass.fs:259-262`
9. `UniqueIndexPass.fs:194-197`
10. `ForeignKeyPass.fs:315-318`

**New primitive:** `LineageDiagnostics.writeLineages : LineageEvent
list -> Lineage<Diagnostics<unit>>` (multi-event sibling of
`writeLineage`, symmetric to `writeDiagnostics`). Required by every
Pattern B site to drain a `LineageEvent list` into the dual-writer
without manual record building.

**New CE Bind overload:** `LineageDiagnosticsBuilder.Bind` now carries
a second overload accepting `Lineage<'a>` directly (auto-lifts via
`ofLineage`). This lets pass drivers write `let! value = lineage`
where `lineage : Lineage<DecisionSet>` came from `Composition.fanOut`,
without an explicit `let! v = LineageDiagnostics.ofLineage lineage`
step. Required by every Pattern C site for the CE form to read at
least as cleanly as the function form.

**Discipline updates landed in the same commit:**
- `CLAUDE.md` F# feature surface: CE-builder entry updated to list new
  production consumers (10 sites), name the auto-lift Bind overload,
  document `writeLineages` as a CE primitive, and explicitly call out
  the seven Pattern B violation fixes.
- `CLAUDE.md` programming style "Functions" section: new bullet
  codifying "CE form for pass-driver writer tails" as the default
  for new pass drivers + worked-precedent list.

**Validation:** full solution builds clean (0 warnings, 0 errors).
Fast pure test pool: 2650 passed / 207 skipped / 0 failed.
Behavior preservation by the writer monad laws (CE desugars to
the equivalent bind chain).

**Why broader than the audit's recommendation:** mid-slice the
agent discovered that seven of the candidate sites weren't merely
"function-form to CE-form" stylistic changes — they were *direct*
discipline violations being silently committed. The audit's
"action-gate" framing was vindicated empirically: the trial
caught violations the function form had missed. Expanding from
~5 to 10 sites in the same slice (per the codebase's "audit
during the work" discipline + the wizard's "vocabulary value
scales with ubiquity" argument from Slice 3) made the CE form
the documented default for new pass drivers.

**Note on Pattern C readability:** the function form
(`lineage |> ofLineage |> tellDiagnostics entries`) is 3 lines vs
the CE form's 4 (with `let! value = lineage; do! writeDiagnostics
entries; return value`). The CE form's win at Pattern C sites is
*consistency with Pattern B* and *structural impossibility of
record-building* — not raw conciseness. For new pass drivers, the
CE form is the documented default; existing function-form chains
using only canonical primitives remain admissible.

### Slice 5 — **LANDED 2026-06-02 (sub-slices 5a + 5b; logical-IR coordinate triad complete; physical-comparison-domain deliberately deferred)**.

**Principle:** 1 (illegal states), 11 (cardinality).

**Mid-slice scope re-calibration**: the operator observed mid-execution
that "we're most of the way through development" — the wizard's
compounding-type-safety case for the audit's full schema-coordinate
sweep shrinks late in development because the future-development
amortization horizon is smaller than the audit assumed. The slice
re-scoped to: complete the **logical IR coordinate identity triad**
(the typed surface every pass touches) and **deliberately defer the
physical-comparison surface** (`PhysicalSchema`'s `PhysicalColumn` /
`LogicalNameBinding` / `PhysicalForeignKey`, `Sequence`) as a
documented asymmetry rather than oversight. This re-calibration is
operationally important — the wizard's bias should not override
empirical cost-benefit at late-stage development. The slice
remains valuable on the logical-IR side; the physical-surface side
is a different domain ("what SQL Server reports back") where
string-as-comparison-key is defensible.

**Sub-slice 5a (TableId lift)**: `TableId.Schema : string → SchemaName`;
`TableId.Table : string → TableName`. Construction site refactored
to `TableId.create` (returns `Result<TableId>`); boundary helpers
added (`TableId.schemaText`, `tableText`, `qualifiedParts`).
Error-code translation preserves the boundary contract (`SchemaName.empty`
→ `tableId.schema.empty`) so downstream consumers triaging errors by
code see the outer-context vocabulary. Total: 1 type definition,
2 smart constructors, 3 boundary helpers, 2 error-translation
helpers, ~200 src/ + ~200 test/ consumer-site updates across ~40
files. Two parallel subagents (one for src/, one for tests/)
absorbed the mechanical walk.

**Sub-slice 5b (ColumnRealization lift)**: `ColumnRealization.ColumnName
: string → ColumnName`. Construction site is `Attribute.create`'s
default-Column — uses bare-value pattern with a defensive `match`
on `ColumnName.create` that fails loud at construction if the input
`Name` exceeds the 128-char SQL identifier limit (rare but real;
`LogicalColumnEmission` guards the same edge case in the
substitution path). Companion module `ColumnRealization` added
with `columnNameText` / `create` / `fromTyped` helpers. Total:
~74 src/ + ~44 test/ consumer-site updates. Third subagent
absorbed the walk + caught 2 runtime leaks via post-build grep.

**Compile-time gap discovered**: the F# compiler catches type-mix-up
errors at *typed* call sites but NOT at `String.Concat` /
`String.Join` / `SqlParameter.AddWithValue` boundaries that accept
`object`. These would silently call `ToString` on typed VOs and
emit `SchemaName "dbo"` instead of `dbo` at runtime, OR fail SQL
parameter binding with "No mapping exists from type
Projection.Core.SchemaName to a known native type."

Discovered runtime leaks (caught only by manual grep after the build
turned green):
- **Slice 5a**: 1 site (`LiveProfiler.fs` SQL parameter binding) +
  4 sites of `String.Concat` with TableId fields in SSDT emitter
  + `PhysicalSchema`.
- **Slice 5b**: 2 sites (`PhysicalSchema.fs:510` String.Concat
  with ColumnName, `CatalogDiff.fs:308` `Set<ColumnName>` instead
  of `Set<string>`).

**Validation**:
- Full solution builds clean (0 warnings, 0 errors).
- Fast pure test pool: 2650 passed / 207 skipped / 0 failed —
  exact match to pre-slice baseline. Behavior-preserving by
  construction.
- Test failures intercepted and fixed during execution:
  - 7 SSDT emission failures (DU-pretty-printer leaks via
    `String.Concat`; 4 SsdtDdlEmitter sites + 2 PhysicalSchema
    sites).
  - 1 SQL parameter binding failure (LiveProfiler).
  - 1 error-code regression (RenameBinding test asserted
    `tableId.schema.empty`; the SchemaName.create-delegating
    path produced `schemaName.empty` instead; resolved by
    error-code translation in `TableId.create`).

**Discipline updates landed in the same commit:**
- `Coordinates.fs` top-of-file comment: Stage-2 marked CASHED
  for the logical-IR coordinate triad; deliberate asymmetry
  documented; runtime-leak grep protocol codified for future
  typed-VO field lifts.
- `CLAUDE.md` programming style "Types" section: new bullet
  ("Schema coordinates are typed VOs") documenting the lift +
  compiler-gap caveat + worked grep protocol.
- This audit doc: 5a + 5b marked LANDED; 5b's late-stage
  re-calibration explicitly named so future slices don't
  re-litigate the same wizard-vs-late-stage tension.

**Deferred-with-trigger (post-slice-5 state)**: `PhysicalSchema`'s
`PhysicalColumn` / `LogicalNameBinding` / `PhysicalForeignKey`
fields stay `Schema`/`Table`/`Column : string`; `Sequence.Schema`
stays `string`. Per the documented asymmetry in `Coordinates.fs`,
these stay deferred until either (a) a real cross-domain
identifier-confusion bug surfaces, or (b) a major IR-shape pass
touches these types and the wrap cost is co-located with other work.

**Why the audit's original framing was right and what it missed**:
The audit's "compiler-walks every site" framing was correct — F#'s
compiler-walk discipline made the lift testable by construction.
What the audit missed: F#'s compiler-walk doesn't cover `object`-accepting
sites (`String.Concat`, `AddWithValue`), so a clean build is necessary
but not sufficient. The post-build grep protocol is the codified
discipline; future typed-VO field lifts inherit it via
`Coordinates.fs`'s top-of-file comment.

**Why the slice was worth completing despite the late-stage
re-calibration**: ~30 src/ sites were active Wave-6 surfaces
(MigrationRun, TransferSpec, TighteningBinding,
SpecialCircumstancesBinding) where the type-safety lift helps
ongoing iteration. The boundary helpers (`schemaText`, `tableText`,
`qualifiedParts`, `columnNameText`) are reusable infrastructure for
any future lift. The Stage-2-CASHED state retires a documented
deferred-with-trigger from chapter 5 slice θ (2026-05-11) — an
~1-year-old open ticket.

### Slice 6 (medium; 3–5 days). JSON description-vs-execution symmetry

**Principle:** 15 (separate description from execution).
**Files:** `Projection.Targets.Json/JsonEmitter.fs`.
**Action:** introduce `seq<JsonNode>` (or a typed `JsonValue` DSL)
as the description form; `JsonEmitter.emit` becomes a renderer over
the description. Existing `Utf8JsonWriter`-direct paths become the
default renderer.
**Risk:** low — the JsonNode tree is already F#-idiomatic; the
slice is wrapping the right shape around it.
**Dependency:** none.
**Why:** unifies the sibling-Π emitter pattern. The next emitter
inherits the symmetric shape.

### Slice 7 (small; 1 day). `Static.fs` Result-ladder → CE adoption

**Principle:** 12 (use the right strength of abstraction), 9 (CE
where the chain is long).
**Files:** `src/Projection.Adapters.Sql/Static.fs:206-257`.
**Action:** convert the 50-line nested `Result.bind` ladder to a
flat `result { let! … }` block.
**Risk:** none; mechanical.
**Dependency:** none.

### Slice 8 (small; 2–3 hours). `Policy.fs` point-free chains → named match

**Principle:** 18 (don't write Haskell in F#), 3 (pipelines as
sentences).
**Files:** `Policy.fs:697, 710, 722, 733`.
**Action:** four `>>` chains become four named-match functions.
**Risk:** none.
**Dependency:** none.

### Slice 9 (medium; 2–3 days). `CentralityPass` / `BoundedContextPass` decomposition

**Principle:** 17 (mutation invisible; if the function is too big
the mutation is visible by virtue of the function's complexity).
**Files:** `src/Projection.Core/Passes/CentralityPass.fs:39-136`;
`src/Projection.Core/Passes/BoundedContextPass.fs:47-89`.
**Action:** decompose 90-line bodies into `init` + `iterate` +
`converge` + `sort` named helpers. The mutation stays inside
`iterate`; the rest is pure.
**Risk:** medium — iterative-algorithm correctness is fragile.
Property tests should pin the algorithm before refactoring (run
on N=20 generated graphs; current output vs refactored output).
**Dependency:** none, but the property-test scaffolding from
Slice 11 helps.

### Slice 10 (small; 1 day). Test pruning — coverage padding removed

**Principle:** 24 (tests are spec, not coverage).
**Files:** `TopologicalOrderTests.fs:110-145`,
`NullabilityPassTests.fs:244-250`, similar shape in
`UniqueIndexPassTests.fs`, `ForeignKeyPassTests.fs`.
**Action:** delete the structural-equality round-trip tests and
the "structural by signature" tests. Each is testing the F#
compiler or restating the function signature.
**Risk:** none — the tests were adding no information.
**Dependency:** none.

### Slice 11 (medium; 3–5 days). T1-determinism example tests → properties

**Principle:** 24 (properties prove laws; examples pin behavior).
**Files:** `NullabilityPassTests.fs:178-183`,
`UniqueIndexPassTests.fs:269-283`, `SsdtDdlEmitterTests.fs:107`,
plus any other "run twice and compare" tests.
**Action:** introduce FsCheck generators for `Catalog × Policy ×
Profile`; replace example-test with `∀ c p pr. run c p pr = run
c p pr`. Property test infra exists (`AdjunctionLawTests.fs:80-88`
permutation invariance is the model).
**Risk:** medium — generators need to construct valid inputs (the
"constructed-valid generator" discipline named in CLAUDE.md is
the model).
**Dependency:** the generator infra needed here also enables more
property tests across the suite (recursion-schemes-shaped property
tests on `Catalog.fold*`; Lens-law property tests on
`CatalogLenses`).

### Slice 12 (small; 1–2 days). Speculative-surface defer-with-trigger entries

**Principle:** 25 (constraints codified) — the codebase's own
anticipation-vs-speculation rule applied to itself.
**Files:** new `DECISIONS 2026-06-XX` entries; the existing
`AxiomTests.fs` Skip rationales.
**Action:** for each defined-but-unused algebraic surface that
does *not* get adopted in this audit's slices, write the
defer-with-trigger:
- `Prism<'a, 'b>` — trigger: Catalog ↔ DDL integration. Adopt
  when the SQL emitter consumes the prism as its bidirectional
  total-vs-partial seam.
- `PassContext<'env, 'a>` — trigger: real reader-comonad pressure
  (a pass that needs to read context from multiple sources and
  the parameter-passing form exceeds ~5 layers).
- `LineageTree<'a>` — trigger: Cluster C (speculative execution
  / policy diff / regression testing). Adopt when the first
  consumer of the branching-writer monad lands.
- `Certificate<'a>` — trigger: multi-target fanout (H-009).
  Adopt when the pipeline returns multiple targets and the
  certificate-wrapper pays its weight.
**Risk:** none.
**Dependency:** none. **This slice is the audit's closure
discipline:** every speculative surface that survives the audit
gets a written trigger; if the trigger doesn't fire by chapter
close + N sessions, the surface is deleted in a follow-up.

---

## 7. Defer-with-trigger registry

For each algebraic surface that this audit does *not* recommend
adopting immediately, the trigger that earns adoption is named.
If the trigger fires, the surface is adopted in the chapter that
fires it. If the trigger does not fire by `cutover + 1 chapter`,
the surface is deleted (per the audit's own anticipation-vs-speculation
posture).

| Surface | Trigger | Owner chapter | Status |
|---|---|---|---|
| `Prism<'a, 'b>` | Catalog ↔ DDL bidirectional partial-accessor consumer | TBD (likely chapter 5+) | deferred |
| `PassContext<'env, 'a>` | Real reader-comonad pressure (≥5 parameter layers) | TBD | deferred |
| `LineageTree<'a>` | Cluster C (speculative execution / policy diff) | Cluster C | deferred |
| `Certificate<'a>` | H-009 multi-target fanout | H-009 | deferred |
| `DiagnosticLattice` | Operator-triage CLI surface adoption | TBD | candidate for Slice 3.5 if a CLI consumer materializes |

The `Lens<'s, 'a>` + `CatalogLenses` surface is *not* on this
table — it has identified consumers (Slice 3) and lands now.

---

## 8. What we are not going to do (explicit non-doings)

These are temptations the audit considered and rejected. Naming
them prevents future drift.

**8.1 Adopt a `Reader<'env, 'a>` monad / `reader { }` CE.** The
parameter-passing form `(Catalog, Policy, Profile) -> Lineage<'a>`
remains correct in F#. Reader earns its place only when threading
the same env through 8+ layers dominates line count. The
`PassContext` reader-comonad surface is the deferred-with-trigger
form (§7); the active Reader monad is not on the slice plan.

**8.2 Adopt typeclass-emulation via SRTP.** F# can fake higher-kinded
types via statically-resolved type parameters; the cost in
readability and tooling friction exceeds the benefit in this
codebase. The fakes are clever; the maintenance is not.

**8.3 Refactor `Reference`'s boolean tuple immediately.** Some of
the orthogonality is genuinely independent (lock modes are SQL
Server primitives, not modeling choices). A deliberate domain
modeling pass deserves its own slice; the boolean-tuple collapse
in Slice 2 is `Index` + `ApprovalState` only.

**8.4 Adopt recursion schemes** (`cata`, `ana`, `hylo`). The
codebase's `CatalogTraversal.foldKinds` / `iterKinds` / `mapKinds`
is the practical form; the named recursion-schemes vocabulary
adds nothing in F#. If a future consumer demands the full
recursion-schemes algebra (e.g., a typed program-transformation
DSL), revisit.

**8.5 Adopt algebraic effects.** F# lacks first-class algebraic
effect support; the available emulations (eff library variants;
free monads + interpreters) carry overhead that exceeds the
benefit at the codebase's scale. The current "pure core + adapter
boundary + interpreter pattern at sibling Π's" already captures
80% of the algebraic-effects benefit at 20% of the syntax cost.

**8.6 Adopt a "production" lens library** (Aether, F#+).
`CatalogLenses` is custom and tiny (~50 lines). The library
dependency costs more than it saves. Custom wins when the
abstraction is bounded.

**8.7 Delete CLAUDE.md or DECISIONS.md to reduce volume.** The
documentation surface is heavy, but its function is structural —
the chapter-close ritual is the audit cadence that prevents drift.
Pruning the *narrative* (per the "DECISIONS is for resolved
questions" discipline) is the right tool; deleting the file is
not.

---

## 9. Closing — how to read this document

This audit is *not* the chapter-close. It runs alongside the
chapter cadence and is read by the agent who's about to open the
next chapter: "what's the principled background as I open chapter
N+1?" The slice plan in §6 is the operational deliverable; §3
and §4 are the evidence; §1 is the framework that justifies the
evidence.

The bias toward the wizard is *informed*, not gratuitous. Where
adoption costs less than living-with-the-debt, adopt. Where
defer-with-trigger is principled, defer with the trigger named.
Where deletion is right (rare in this audit), delete and document
the reason.

The cleanest single result of this audit: the codebase has *one*
coherent drift cluster (Cluster-B speculative optics), and even
that cluster is half a slice away from compliance. Everything
else is small. The disciplines this codebase has built are
genuinely earning their weight. The work below is refinement,
not foundational correction.

The next chapter's agent should read §6 first (the slice plan),
then §1 (to understand the audit's principles), then §3 and §4
as reference for the specific slices.

---

## 10. Citations

This audit's findings cite file:line evidence throughout. The
canonical surfaces consulted:

- `src/Projection.Core/` — all sub-modules sampled, no exception.
- `src/Projection.Pipeline/Pipeline.fs:467-625` — the Core/async
  seam.
- `src/Projection.Targets.SSDT/Statement.fs`,
  `SsdtDdlEmitter.fs:800` — the SSDT description-vs-execution
  exemplar.
- `src/Projection.Targets.Json/JsonEmitter.fs:70-126` — the JSON
  asymmetry.
- `src/Projection.Adapters.Sql/Static.fs:206-257` — the
  Result-ladder.
- `src/Projection.Adapters.Sql/ReadSide.fs:711` — the
  `failwith` invariant.
- `src/Projection.Analyzers/NoUnsafeTimeInCoreAnalyzer.fs:46-53`
  — the DateTime-only forbidden-suffix list.
- `src/Projection.Core/Diagnostics.fs:274-1064` — the algebraic-surface
  module (writers, optics, comonad, Kleisli arrow, CE builders).
- `src/Projection.Core/Lineage.fs:411-735` — the writer and
  branching-writer surfaces.
- `tests/Projection.Tests/AxiomTests.fs` — 1138+ lines, 116 entries
  — the executable spec catalog.
- `tests/Projection.Tests/DiagnosticsTests.fs:495-721` — Kleisli
  laws, lens laws, certificate isomorphism property tests.
- `scripts/perf-gate.sh:231-303` — the statistical perf gate.
- `bench/baseline-canary.json` — the operator-reality baseline.
- `CLAUDE.md` — the discipline index (consulted for context, not
  for evidence).
- `DECISIONS.md:7420-7467, 20154-20164` — the refuted-swap
  precedents.

The full agent-report transcripts are preserved in the session
that produced this audit and can be replayed against the codebase
at the commit this audit cites (the audit's evidence is
specific to the head-of-tree at the moment of dispatch).

---

*— Audit 2026-06-02, eight-axis F# best-practices red team,
leaning FP-wizard. Closure discipline: defer-with-trigger or
adopt. Status quo unacceptable.*
