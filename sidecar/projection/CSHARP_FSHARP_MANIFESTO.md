# THE C# / F# ARCHITECTURE MANIFESTO

**The canonical statement of V2's language partition, the V1→V2 inheritance
surface, and the cultural premises that make both inevitable.**

This document is the master text for the C#/F# architecture of the
OutSystems DDL exporter, V2. It supersedes no prior document; it
articulates what the prior documents have been *practicing*. Read it as
the cohesive argument for why the codebase is shaped the way it is —
why F# is the language of the algebraic core, why C# is the language of
the inheritance surface, why V1 contributes to V2 by pollination rather
than by replacement, and why the seams are part of the garment rather
than apologies for it.

The other strategic surfaces (`VISION.md`, `SPINE.md`, `PLAYBOOK.md`,
`STAGING.md`, `V2_DRIVER.md`, `V2_PRODUCTION_CUTOVER.md`) operate on
different rhythms. This document operates on the slowest rhythm of all.
It changes only when the architecture's deep premises change — and
those premises are now stable enough to write down completely.

---

## Table of contents

- [I. Preface](#i-preface)
- [II. The shape of the seam](#ii-the-shape-of-the-seam)
- [III. Pollination, not extraction (V1 is not hobbled)](#iii-pollination-not-extraction-v1-is-not-hobbled)
- [IV. The cultural premise](#iv-the-cultural-premise)
- [V. The two-language partition](#v-the-two-language-partition)
- [VI. The Bridge: the V1→V2 inheritance surface](#vi-the-bridge-the-v1v2-inheritance-surface)
- [VII. The Bridge.Runtime: V2→V1 capabilities during dual-track](#vii-the-bridgeruntime-v2v1-capabilities-during-dual-track)
- [VIII. The four-state gradient](#viii-the-four-state-gradient)
- [IX. The audit attribute as manuscript history](#ix-the-audit-attribute-as-manuscript-history)
- [X. The eight wall rules](#x-the-eight-wall-rules)
- [XI. Lift verbs, not nouns](#xi-lift-verbs-not-nouns)
- [XII. What V2 explicitly does not inherit](#xii-what-v2-explicitly-does-not-inherit)
- [XIII. The equivalence witness](#xiii-the-equivalence-witness)
- [XIV. Axiomatic alignment](#xiv-axiomatic-alignment)
- [XV. The R6 Stage-2 specification](#xv-the-r6-stage-2-specification)
- [XVI. The cherry-pick discipline, restated](#xvi-the-cherry-pick-discipline-restated)
- [XVII. The cutover+30 sunset](#xvii-the-cutover30-sunset)
- [XVIII. Worked examples](#xviii-worked-examples)
- [XIX. Worked counter-examples](#xix-worked-counter-examples)
- [XX. For new contributors](#xx-for-new-contributors)
- [XXI. For V1 maintainers](#xxi-for-v1-maintainers)
- [XXII. For V2 agents](#xxii-for-v2-agents)
- [XXIII. The philosophical stakes](#xxiii-the-philosophical-stakes)
- [XXIV. Closing](#xxiv-closing)

---

## I. Preface

This is the document a contributor reads when they want to understand
*why* the C#/F# partition is what it is. Not how to use it — that
lives in the per-chapter pre-scopes and the existing code conventions
— but why it was chosen, why it deserves the shape it has, and why
every plausible alternative was rejected in favor of this one.

There are three audiences:

**V2 agents** — the architects working inside the sidecar, designing
new passes, lifting V1 capabilities, refining the algebraic core. They
read this when they are about to make a decision that touches the
language partition or the V1↔V2 seam, and they need the canonical
rationale before they commit.

**V1 maintainers** — the engineers who own the C# trunk in
`src/Osm.*`, who keep V1 running in production during dual-track, who
will eventually retire V1 at cutover+30. They read this to understand
exactly how the V2 effort will and will not affect their work; they
will find that the impact during dual-track is essentially zero, and
that V2's inheritance is structured so that V1's source is *copied*,
never *moved*.

**Operators and reviewers** — the people evaluating whether V2 is
ready, comparing V1 emissions against V2 emissions, deciding when to
flip a per-environment-per-artifact-type gate from V2-augmented to
V2-driver, and ultimately authorizing V1 sunset. They read this to
understand what V2's architecture *promises* and what it *refuses to
promise*; what its structural guarantees are and what its limits are.

The document is long because the architecture is load-bearing. Every
chapter the V2 effort ships, every pass it writes, every emitter it
adds, and every cutover stage it enters depends on the partition
described here. The cost of writing this down once is small compared
to the cost of contributors reasoning about the partition without it,
or contributors making decisions that drift from it without recognizing
the drift.

The document is meant to be cited. When a future DECISIONS entry says
"per the C#/F# manifesto, [section title]," it should be readable
without ambiguity. Sections are numbered (Roman) and titled
descriptively for that purpose.

The document is canonical but not eternal. Architecture has rhythms.
The data-only boundary that preceded the inheritance seam was correct
for the stage of work where decoupling was the load-bearing virtue;
this manifesto codifies the inheritance seam that is correct for the
stage of work where V2-driver KPI demands V2 absorb V1's working
logic and refine it forward. If a future stage of work demands a
different shape, a future manifesto will supersede this one. Until
then, this is the architecture.

---

## II. The shape of the seam

A boundary, in the architectural imagination most contributors arrive
with, is a *wall*. Two systems on either side; a translation layer in
the middle; goods cross under inspection; vocabulary changes at the
door. Customs checkpoint between two countries. Most cross-language
seams in most codebases have this shape: a JSON contract, a Protocol
Buffers schema, a serialization wire format. The two systems on either
side remain distinct, peer, perpetually translating.

That is the shape the V2 sidecar inherited from its first instinct
toward V1. V1's `osm_model.json` emitted at the end of V1's pipeline;
V2's adapter read it at the start of V2's. The boundary was data; V1
mental model never entered F# code by virtue of *absence* — F#'s
typed records reconstructed everything fresh from the JSON shape, and
no V1 domain type appeared anywhere in the sidecar. For a stage of
work where the load-bearing virtue was decoupling — where V2 had to
prove itself cherry-pick-safe, where V1 had to be confident V2 could
ship or not ship without affecting V1's trunk, where the algebraic
core had to demonstrate purity by physically refusing to depend on
anything — the wall was correct. It served its stage of work.

Under V2-driver KPI, with V1 sunset committed at cutover+30, with V2
inheriting the obligation to be provably correct on every axis V1
owns — schema, data, identity, diagnostics, plus whatever further
axes accumulate during the cutover — the wall stops being the right
shape. A wall implies symmetry. A wall presumes two systems that
remain distinct. V1 does not remain distinct. V1 is the donor of
working logic that V2 must own. The relationship is not international
trade; it is *inheritance*.

The seam V2 actually wants, then, is not a wall. It is a
**phylogeny**. V2 descends from V1 by adopting selected traits and
refining them in its own genome. At runtime, after the inheritance is
done, there is no seam between V1 and V2's code — there is only V2,
some of whose source can trace its lineage to V1's trunk via the
`[BridgeMethod].V1Source` citation, and some of which was authored
fresh in F#. The historical fact of V1's contribution is recorded in
the audit metadata of each adopted method; the runtime fact is that
V2's body is V2's body, self-consistent and complete.

This shape makes philosophical sense for three reasons.

**First, it honors direction.** V2 is not equal to V1; V2 is V1's
successor. A wall implies symmetry — two systems in perpetual
negotiation. Inheritance implies asymmetry — one system gives, the
other receives, refines, and outlives. The asymmetry is the truth
about this moment in the project's life. V1 is sunsetting. V2 is
becoming. Treating them as peer systems separated by a translation
layer would obscure the truth that one is being absorbed into the
other. The shape of the seam should match the shape of the
relationship.

**Second, it dissolves the back-compat tension.** A wall design has
V2 perpetually pulling V1 across a boundary at runtime; V2 carries V1
forever, or until a cutover event forces an abrupt transition. An
inheritance design has V2 adopting V1's working code once, refining
it in V2's territory, and never depending on V1 again. The
"back-compat path" question vanishes because there is no back to be
compatible with — V1's contributed code is V2's code now, by
adoption. Pillar 6 (no V2-internal back-compat paths) is honored not
by avoidance but by *direction*: the arrow is one-way; V2 inherits
from V1 once and refines forward; there is no V1-compatibility
surface inside V2, only V2 source that happens to descend from V1
source.

**Third, it converts the seam from cost to product.** A wall is
overhead — every crossing pays a tax in serialization, in vocabulary
translation, in audit ceremony. An inheritance seam is *editorial
work* — every crossing is V2 reading V1, deciding what to keep, what
to restructure, what to rewrite. The crossings produce a refined
codebase. The act of inheriting is the act of improving. The seam
becomes a feature of the architecture rather than a tax against it.

The metaphor that captures this most precisely is *pollination*. V1's
trunk is the flower; V1 keeps producing nectar (running in
production). V2 is the hive that turns pollen into honey (the
refined production form). The bees — V2's contributors — carry
pollen from flower to hive. The flower is not damaged by pollination;
the flower continues to bloom. The pollination process produces honey
in the hive AND lets the flower reproduce by carrying its pollen
elsewhere. Eventually the flower's natural lifespan ends (cutover+30,
V1 sunset), but only after the hive has fully stocked, only after the
pollination is complete. The next section makes this metaphor
specific and operational.

---

## III. Pollination, not extraction (V1 is not hobbled)

This is the most important section of the document, because it is the
section that operators, V1 maintainers, and reviewers must be able to
quote back to anyone who claims otherwise. **V1 is not hobbled by the
Bridge wave. V1 continues to live in production unchanged. V2
inherits from V1 by pollination, not by extraction.**

The metaphor is precise; the operational consequences are concrete.

**V2 copies V1 source; V2 never removes V1 source.** When chapter 0.5
slice ε transitions `ExtractMetadataAsync` from `Delegated` to
`Vendored`, the operation is exactly: V1's `MetadataSnapshotRunner.cs`
and `SnapshotJsonBuilder.cs` and the result-set processor chain are
*copied* into `sidecar/projection/src/Projection.Bridge.Core/Adopted/Catalog/`.
The originals stay in `src/Osm.Pipeline/SqlExtraction/`. V1's csproj is
unmodified. V1's trunk source compiles, links, runs, and ships
exactly as it did before the copy. The relationship between V1's
trunk source and V2's vendored source is one of *common ancestry*, not
of dependency. There is no synchronization burden; if V1's
maintainers want to evolve their copy, they can; if V2 wants to evolve
its copy, it can; the two copies diverge from their common ancestor at
their owners' chosen rhythms.

**V1's running production behavior is unchanged through cutover+30.**
Per `DECISIONS 2026-05-22 — T-30 / T-15 cutover fallback ladder
gates`, V1 stays warm through cutover+30 regardless of the cutover
mode entered. Under V2-augmented mode, V1 drives production; V2
verifies. Under V2-driver mode, V2 drives production; V1 stays warm
as the fallback rung the operator can drop to if a V2-emitted artifact
triggers an unexpected divergence. V1 retains its CLI verbs, its
application services, its SQL extraction, its tightening policy
matrix, its SsdtEmitter, its DmmComparator. None of these are removed
during dual-track. None are deprecated during dual-track. None are
even renamed during dual-track. V1 stays exactly the system V1 was on
the day the Bridge wave opened, plus whatever V1's own maintainers
choose to evolve V1 to.

**V1 sunset is a condition, not a deadline event.** When cutover+30
arrives, V1 sunset begins not because the calendar says so but
because the condition holds: every Bridge method has reached its
declared target state on the inheritance gradient, the
`BridgeManifestSunsetGateTest` is green, V2 has run V2 emissions in
every environment for at least one full schema-evolution cycle, and
the operator has signed off per the R6 governance protocol. The
sunset is the moment V1 is empty *because V2 has finished editing*,
not the moment V1 is empty *because the calendar said so*. V1's
shutdown is consequential, not causal: V1 retires when V2 no longer
needs it, not at a chosen date.

**During dual-track, the Bridge surface is additive to V1, not
substitutive.** When chapter 0.5 slice η lands
`InvokeV2TopologicalOrderAsync` and closes V1's static-seed FK-order
bug, the fix is delivered as: V1's `BuildSsdtStaticSeedStep`
optionally calls `InvokeV2TopologicalOrderAsync` to obtain V2's
globally-correct topological order, then uses that order. V1's
existing sort logic is left in place; V1 maintainers can keep using
the old sort, or they can opt into V2's by enabling a configuration
flag, or they can let V1's old sort and V2's new sort coexist for
verification. The fix is offered, not imposed. V1's deploy path,
under any configuration, continues to work.

**V2 explicitly refuses to modify V1's trunk source.** The Bridge
wave's commit boundaries hold inside `sidecar/projection/`. The
inheritance gradient transitions (`Delegated` → `Vendored` →
`RefinedInPlace` → `TranslatedToFSharp`) all happen inside V2's
territory. The `Vendored` transition copies V1 source into V2's
`Adopted/` directory; it does not delete the original. The
`RefinedInPlace` transition edits V2's copy; it does not edit V1's
copy. The `TranslatedToFSharp` transition replaces V2's copy with an
F# implementation; V1's copy is still there in V1's trunk. At no
point does V2 commit a change to V1's trunk source under the Bridge
wave's authority.

**V1 maintainers retain full authority over V1.** If V1's maintainers
discover a bug in `MetadataSnapshotRunner.cs` after V2 has vendored a
copy, V1 fixes V1's copy on V1's rhythm; V2 either pulls the fix
forward into its vendored copy (an editorial decision V2 makes
deliberately, citing the V1 fix in the corresponding chapter), or V2
leaves its vendored copy as-is (a deliberate divergence V2 logs in
DECISIONS). The relationship between V1's trunk source and V2's
vendored source is editorial, not authoritative. Both copies are
canonical for their respective owners.

**The pollination metaphor names a specific operational shape.** Bees
take pollen; flowers stay alive. V2 takes V1's working logic by
copying it; V1's trunk source stays alive. V2 refines the copy in
the hive; V1's flower keeps producing nectar. The bees may
occasionally bring pollen back from the hive to the flower (the
`Bridge.Runtime` V2-for-V1 capabilities — V1 calling V2's typed
ordering or V2's diagnostic flattening during dual-track); this is
mutually beneficial pollination, not extraction in either direction.
At the end of the season, the flower's natural lifespan ends
(cutover+30); but by then the hive is fully stocked and V1's
contribution has been fully metabolized into V2's honey.

The architectural commitment, stated plainly: **V2's Bridge wave does
not modify V1's trunk source under any circumstance. V1's production
deployments are unchanged through cutover+30. V1's maintainers retain
full authority over V1's evolution. V2's vendored copies are V2's
responsibility; V2's adopted refinements are V2's responsibility; V1
is unchanged.**

If a future change to the C#/F# architecture would violate this
commitment, that change requires a DECISIONS amendment superseding
this section explicitly. The commitment is load-bearing.

---

## IV. The cultural premise

Before the Bridge, before the inheritance gradient, before the audit
attribute, V2 was already a particular kind of codebase. Understanding
the C#/F# architecture requires understanding what V2 already is.

V2 is an algebraic projection compiler with effects at the boundary.
Its eight pillars and forty-plus axioms are not a style guide; they
are the structural commitment that lets V2 honor a per-axis
correctness KPI on a 300-table cutover with CDC live underneath. Read
in their own vocabulary, the commitments cluster into five postures.

**The algebraic core, narrow waist.** `Projection.Core` carries no
I/O, no mutation, no time. Every input is `Catalog × Policy × Profile`,
plus the temporal dimension of `Lifecycle`. Every output is a typed
value. Effects live at adapters, never inside the core. The narrow
waist is the place where all V2's transformations converge: every
pass takes Catalog/Policy/Profile and returns `Lineage<'output>` or
`Lineage<Diagnostics<'output>>`; every emitter takes the Catalog
(possibly Catalog × Profile, never Policy per A18 amended) and
returns a typed stream. The shape is preserved everywhere.

**Verbs, not nouns.** Capabilities are imperative operations; nouns
are reconstructed at consumption. ADMIRE tracks V1 components by
*what they do*, not by *what they are called*. The four-question
naming discipline (pillar 8) rejects domain-blind names at PR
review. The verbal posture matters because verbs compose; nouns
proliferate. V2's strategy modules are named `<Domain>Rules` because
they apply rules; V2's passes are named after the pass they execute;
V2's emitters are named for what they emit. The naming convention is
the discipline made structural.

**Evidence over speculation.** IR grows when a consumer demands it.
Each axis (Schema / Data / Identity / Diagnostics) earns its IR slot
through a property test, not a hypothetical future need. The
two-consumer threshold for primitive extraction is the discipline
made structural at the architectural level; the anticipation-vs-
speculation refinement (positions A/B/C) refines it further. V2's
codebase does not contain abstractions that were extracted
speculatively. Every primitive paid its way through demonstrated
need.

**Type system as enforcement.** Smart constructors, closed DUs,
structural witnesses (T11), no string-keyed dispatch. Compliance is
not aspirational; it is structurally true or it is a compile error.
V2's `SsKey` is a single-case DU that the compiler refuses to confuse
with a string; V2's `Name` is presentation-only and cannot accidentally
serve as identity; V2's outcome and keep-reason DUs are
`RequireQualifiedAccess` to prevent silent miscompilation when case
names recur across strategies.

**Auditability as substrate.** `Lineage<T>` and `Diagnostics<T>` are
writer monads layered through every pass. `TransformRegistry` (A41
candidate) names every transformation. The Bench primitive
instruments every hot loop. Nothing happens that the system can't
account for. The audit trail is not a feature added at the end of the
work; it is constitutive — every transformation runs inside the writer
by construction; every decision emits a lineage event by structural
necessity, not by convention.

V2 is also defined by what it refuses: no V2-internal back-compat
paths, no string-builder reflex, no text-builder-as-first-instinct,
no skeleton-overlay drift, no infrastructure-blame jumping, no
performance-of-compliance, no domain-blind naming. These are not
aesthetic preferences. Each is a named failure mode that has bitten
before and been codified after. Pillar 6 codified the back-compat
refusal after V2's chapter-3.6 sidebar surfaced an instance; pillar 9
codified the harvest-dichotomy after the principal-PO discussion
revealed the structural gap; the text-builder discipline codified
after the slice-β shortcut at chapter 3.7.

The Bridge wave inherits all of these commitments. The inheritance
seam is not an exception to the culture; it is the culture taken
seriously at the V1↔V2 boundary. The same disciplines that govern
pass design and emitter design also govern Bridge method design. The
same analyzer-enforced rules that protect the pure core's purity also
protect the Bridge wall's vocabulary. The same audit substrate that
records lineage events across passes also records adoption events
across Bridge methods.

---

## V. The two-language partition

V2's source is partitioned across two languages. Most of V2 — the
algebraic core, the adapters, the emitters, the pipeline, the CLI —
is F#. The Bridge surface is C#. The partition is not stylistic; it
is load-bearing, and the reasons are precise.

**F# is the language of the algebraic core because F# is the language
in which V2's commitments are structurally enforceable.** F# has
closed discriminated unions with compile-time exhaustiveness checking;
F# has records with structural equality by default; F# has smart
constructors with `Result<'a>` returns that propagate through `bind`;
F# has computation expressions for monadic composition; F# refuses
nulls when `Nullable=enable` and `TreatWarningsAsErrors=true`; F#
makes purity the default and mutation the exception that must be
locally bounded. Every one of these features is constitutive of V2's
algebraic core — V2's posture of "the type system is the contract"
is structurally true in F# in a way it cannot be structurally true
in C#. The eight pillars and the determinism / lineage / diagnostics
substrate land naturally in F#; they would land awkwardly in C#.

**F# is also the language of V2's adapters, because the adapter's
role is to translate from the boundary's idioms into V2's algebraic
idioms.** `Projection.Adapters.Sql` wraps `Microsoft.Data.SqlClient`
and surfaces `AsyncStream<'a>` as the streaming primitive V2's hot
paths consume; `Projection.Adapters.Osm` wraps V1's `osm_model.json`
shape (and now, via the Bridge, V1's rowset surface) and produces
`Catalog` values; `Projection.Targets.SSDT` wraps
`Microsoft.SqlServer.TransactSql.ScriptDom` and constructs typed
`Statement` streams. In each case, the adapter's job is to take what
the boundary library gives (often C# idioms — `IDisposable`, mutable
properties, exception-driven error handling) and produce what V2's
core consumes (F# value types, `Result<'a>`, immutability). F# is
better at this translation than C# would be because F#'s `task { }`
computation expression handles the boundary's async idioms cleanly,
F#'s record construction is terse, and F#'s pattern matching makes
the translation rules readable.

**C# is the language of the Bridge, because C# is V1's language and
the Bridge is where V2 inherits from V1.** When V2 adopts V1's
metadata extraction or V1's SMO emission or V1's DMM compare, the
adoption gradient passes through three states in C# (`Delegated` →
`Vendored` → `RefinedInPlace`) before optionally reaching the
F#-translation state. The Bridge is structurally a C# project
because V1's source is C# and the easiest, most-faithful adoption is
to take V1's C# source forward into V2's C# territory and refine it
there. Forcing every adoption to start in F# would introduce a
translation burden at the moment of inheritance — exactly when
clinical fidelity matters most — and would force expensive F#
rewrites of capabilities (SMO, DacFx, ScriptDom) that are
fundamentally C#-idiomatic libraries. The Bridge's C# layer is the
*tide pool* where V2's C# code lives, alongside its F# core, as a
deliberate architectural element rather than an accident.

**The Bridge's C# is bounded.** It does not creep into the algebraic
core; the analyzer enforces that. It does not expose V1's C# types
across its public surface; the analyzer enforces that. It does not
break V2's purity or determinism or auditability commitments; the
analyzer enforces that. The Bridge is C# at the boundary, F# in the
core, and the boundary is structurally bounded by a wall that is
itself a Roslyn analyzer.

**The two languages have one shared idiom.** Both F# adapters and
Bridge methods return `Result<'a>` (in F#) or `BridgeResult<T>` (in
C#) — the shapes mirror each other intentionally so the F# adapter
consuming a Bridge method's output can pattern-match without
translating exceptions. Both use BCL types at the boundary; neither
exposes language-specific idioms across the boundary. Both follow the
`category.subject.problem` error code convention. The shared idiom
makes the partition feel like one codebase with two languages, rather
than two codebases joined by a wire format.

**The partition is the right partition for this stage of work, not
the eternal answer.** If a future stage of work surfaces a load that
F# cannot bear at acceptable cost (e.g., a CPU-bound algorithm that
demands `[<Struct>]` records or specialized vectorization), the
partition can shift. The eight pillars and the determinism /
lineage / diagnostics substrate are the constants; the language
choice is a parameter that serves them. As of this manifesto, F# in
the core + F# in the adapters + C# in the Bridge is the partition
that lets V2 honor every commitment with the least architectural
ceremony.

---

## VI. The Bridge: the V1→V2 inheritance surface

`Projection.Bridge.Core` is the C# project that ProjectReferences V1's
trunk assemblies and exposes V1 operations as BCL-typed,
V2-vocabulary, capability-shaped methods that F# adapters consume.
It is the V1→V2 inheritance surface — the place where V1's working
logic enters V2's territory and begins the editorial gradient toward
publication under V2's imprint.

The project sits at
`sidecar/projection/src/Projection.Bridge.Core/`. Its `.csproj`
references six V1 assemblies (`Osm.Domain`, `Osm.Json`, `Osm.Validation`,
`Osm.Smo`, `Osm.Dmm`, `Osm.Pipeline`) and V2's `Projection.Core`. The
reverse reference to `Projection.Core` lets the Bridge surface use
F# types in its internal translation — though no F# type appears in
the Bridge's public surface (the wall rules forbid it). The structure
inside the project is partitioned by concern:

```
Projection.Bridge.Core/
├── Audit/
│   ├── BridgeMethodAttribute.cs
│   ├── BridgeManifest.cs
│   ├── SunsetDisposition.cs
│   ├── Determinism.cs
│   └── Frequency.cs
├── Wire/
│   ├── BridgeResult.cs
│   ├── BridgeError.cs
│   └── (per-capability wire records, V2 vocabulary)
├── Capabilities/
│   ├── Catalog/
│   │   ├── ExtractMetadata.cs       (chapter 0.5 slice γ)
│   │   ├── ParseSnapshotJson.cs     (later chapter)
│   │   └── LoadModelFromFile.cs     (later chapter)
│   ├── Profile/
│   │   ├── BuildProfileQueries.cs   (chapter 4.x)
│   │   └── DeserializeProfileSnapshot.cs (chapter 4.x)
│   ├── Smo/
│   │   ├── RenderSsdt.cs            (chapter 3.x)
│   │   └── RenderDacpac.cs          (chapter 3.x conditional)
│   ├── Dmm/
│   │   └── CompareWithDmm.cs        (later chapter)
│   ├── Refactor/
│   │   └── ParseRefactorLog.cs      (chapter 3.5)
│   ├── Overrides/
│   │   └── LoadOverrideBindings.cs  (chapter 4.x)
│   ├── Cdc/
│   │   └── ReadCdcRows.cs           (chapter 4.1.B)
│   ├── Users/
│   │   └── MatchUsers.cs            (chapter 4.2)
│   └── Data/
│       └── GenerateMergeInsert.cs   (chapter 4.1.B)
└── Adopted/
    └── (V1 source copies, namespaced under Projection.Bridge.Adopted)
```

Each capability lives in its own file. The discipline is structural:
one public method per file makes cherry-pick auditability possible —
a reviewer can examine exactly the inheritance surface a chapter
introduces by looking at exactly the files the chapter touches.

The `Audit/` directory carries the metadata primitives that make
every public Bridge method auditable. `Wire/` carries the BCL-typed
records that cross the wall — they use V2 vocabulary (`Kind`, not
`Entity`; `Module`, not `Espace`; `Attribute`, not `Attr`). The
`Capabilities/` directory is partitioned by V2 consumer (the F#
adapter or pipeline component that will consume the Bridge method)
rather than by V1 source; this is "lift verbs, not nouns" expressed
at the directory level — the directory's name answers "what does V2
need this for?" not "where did V1 keep it?". The `Adopted/` directory
is where V1 source files land when a Bridge method transitions from
`Delegated` to `Vendored`; everything inside `Adopted/` is namespaced
`Projection.Bridge.Adopted` so the source's lineage is structurally
visible.

The Bridge is consumed by F# adapters via ProjectReference. F#'s
`Projection.Adapters.Osm.CatalogReader` references
`Projection.Bridge.Core`; F#'s `Projection.Pipeline` references
`Projection.Bridge.Core` (indirectly, via the adapters); the F# code
calls Bridge methods and receives BCL-typed `BridgeResult<T>` values
that pattern-match cleanly into F#'s `Result<'a>` via a thin
translation function (`BridgeWire.fromResult` or similar — one
F# file per Bridge consumer).

No F# project may ProjectReference `Projection.Bridge.Runtime`. The
cycle constraint is structural — Bridge.Runtime depends on
`Projection.Pipeline`; if any F# project consumed Bridge.Runtime, the
graph would cycle through Bridge.Runtime → Pipeline → Adapters →
Bridge.Runtime. The cycle is prevented by the architectural commitment
in this manifesto, not by build-system magic. Contributors who add a
new F# project asserting a need for Bridge.Runtime must first
re-route the consumer through Bridge.Core or accept that the V2-for-V1
capability they need belongs on the Bridge.Runtime side of the
partition, not the V2 side.

---

## VII. The Bridge.Runtime: V2→V1 capabilities during dual-track

`Projection.Bridge.Runtime` is the sibling C# project whose role is the
mirror image of `Projection.Bridge.Core`. Where Bridge.Core lifts V1
operations into V2, Bridge.Runtime exposes V2 capabilities for V1 to
consume during dual-track. The two projects together carry the
bidirectional pollination — V1 contributes to V2 via Bridge.Core; V2
contributes to V1 via Bridge.Runtime — that lets the inheritance
proceed without either system being starved during the transition.

The project sits at
`sidecar/projection/src/Projection.Bridge.Runtime/`. It
ProjectReferences `Projection.Bridge.Core` (for the shared audit and
wire primitives), `Projection.Core` (for the algebraic types it
projects to BCL records), and `Projection.Pipeline` (for the
orchestration entry points it invokes). The reverse reference to
`Projection.Pipeline` is what makes this project a leaf — F# projects
cannot reference Bridge.Runtime without creating a cycle, so
Bridge.Runtime is structurally only ever a consumer's project, never a
dependency.

The capabilities Bridge.Runtime exposes are decided by demonstrated V1
consumer demand, per the IR-grows-under-evidence discipline (DECISIONS
2026-05-07). As of the chapter 0.5 close, exactly one V2-for-V1
capability has demonstrated consumer evidence:

`InvokeV2TopologicalOrderAsync` — V2's `TopologicalOrderPass` flattened
to `IReadOnlyList<TableRef>`. V1's `BuildSsdtStaticSeedStep.cs:82-86`
sorts static-seed entries by FK dependency *within static category
only*; the corresponding `BuildSsdtBootstrapSnapshotStep.cs:111-117`
does it correctly with a global sort. The asymmetry produces FK
constraint violations on cross-category dependencies. V1's maintainers
have known about this for some time but have not yet shipped a fix.
The chapter 0.5 slice η lands `InvokeV2TopologicalOrderAsync` and the
V1 test paired with it demonstrates the bug closing — V1 calls
`InvokeV2TopologicalOrderAsync` to obtain V2's correct global order
and uses that order in the static-seed step. The fix is offered to V1
as an opt-in capability; V1's maintainers can adopt it on their
rhythm.

Three other V2-for-V1 capabilities were proposed during the Bridge
wave's planning but did not survive the consumer-demand analysis:

- `InvokeV2RenderAsync` (V2's typed `Statement` rendered as BCL
  `IReadOnlyList<RenderedStatement>`) — no demonstrated V1 consumer.
  V1's `SsdtEmitter` produces text output and has no parity-comparison
  hook that would adopt V2's render. Deferred until a real consumer
  surfaces.

- `InvokeV2FlattenLineageAsync` (V2's `Lineage<T>` flattened to
  `IReadOnlyList<DecisionEvent>`) — no demonstrated V1 consumer. V1
  has no lineage trail concept; V1's manifest builders emit summaries,
  not pass-decision provenance. Deferred until V1's diagnostics
  surface adopts a lineage-shaped consumer.

- `InvokeV2FlattenDiagnosticsAsync` (V2's `Diagnostics<T>` flattened
  to `IReadOnlyList<Finding>`) — plausible V1 consumer. V1 emits
  validation findings via `ValidationError` arrays without severity
  classification; an operator-facing diagnostic dashboard would adopt
  V2's structured diagnostics if such a dashboard is scoped during
  dual-track. Deferred to the chapter where that dashboard's
  consumer materializes.

The asymmetric volume — many V1→V2 lifts; few V2→V1 capabilities — is
not an oversight; it is the truth about the relationship. V1 has a
lot of working logic V2 needs to inherit. V2 has a smaller set of
algebraic refinements V1 might benefit from during dual-track. The
inheritance is dominantly one-way; the bidirectional surface is
real but small.

Bridge.Runtime's sunset is different from Bridge.Core's. Bridge.Core
sunsets capability-by-capability as each Bridge method reaches its
declared target state on the inheritance gradient — by cutover+30,
every Bridge.Core method has either been vendored, refined, or
translated to F#, and the V1 ProjectReferences are gone. Bridge.Runtime
sunsets *as a project*: when V1 retires at cutover+30, V1 stops
calling Bridge.Runtime's capabilities, and the entire Bridge.Runtime
csproj becomes dead code that can be removed. The capabilities it
exposed survive — they are F# entry points in `Projection.Pipeline`
that V2 internally uses; the wrappers Bridge.Runtime added are what
go away.

The two-project factoring is the natural shape of the asymmetry.
Bridge.Core is the durable inheritance surface that progresses through
the gradient. Bridge.Runtime is the dual-track conveyance that
disappears when dual-track ends. Both projects participate in the
audit substrate; both projects' methods carry `[BridgeMethod]`
attributes with the seven required fields; both projects are scanned
by the same `BridgeManifest`. But they have different roles, different
lifecycles, and different cycle-discipline rules — and the partition
into two projects makes those differences structural rather than
documented.

---

## VIII. The four-state gradient

Every public Bridge method declares its position on a four-state
gradient that describes how the method has progressed from V1
antecedent toward V2 publication. The gradient is not a calendar; it
is a structural classification — each state names a real difference
in how the method's source relates to V1's trunk and V2's territory.
The cutover+30 sunset gate asserts every method has reached its
declared target state; until cutover+30, methods may legitimately
occupy any state on the gradient, as long as the gradient is
documented in `[BridgeMethod].Current` and the trajectory toward
`[BridgeMethod].Target` is recognizable.

**State 0: Delegated.** The Bridge method's body delegates to V1's
class via the ProjectReference. V1's source remains in V1's trunk
unchanged; the Bridge method is essentially a thin wrapper that
translates V1's input shape to Bridge's BCL input shape and V1's
output shape (or V1's exception) to Bridge's BCL output shape. The
state is the entry point for every newly-introduced lift. A method
that ships as `Delegated` is announcing: "V2 has identified this V1
verb as worth inheriting; the inheritance has begun." The Bridge
method's existence in this state changes V1's behavior not at all —
V1's class is still called the way V1 calls it, but Bridge now also
calls it from V2's side. The cost of `Delegated` is one csproj
reference + a wrapper file. The benefit is that V2 has a structural
acknowledgment that the verb exists at the V1↔V2 seam and a
reflection-scanned manifest entry recording the inheritance.

**State 1: Vendored.** V1's source has been copied into
`Projection.Bridge.Core/Adopted/`, namespaced under
`Projection.Bridge.Adopted.<Domain>`. The Bridge method's body now
calls the local copy instead of V1's class. The ProjectReference to
V1's assembly may or may not still be needed (it remains while other
Bridge methods still delegate to it; it is removed when no Bridge
method delegates to any V1 class). V1's trunk source is *unchanged*;
the copy in `Adopted/` is V2's responsibility. The Bridge method's
behavior is byte-identical to its `Delegated` predecessor — the
equivalence property test (chapter 0.5 slice ζ) is the witness. The
transition from `Delegated` to `Vendored` is a deliberate editorial
act: V2 has taken custody of the verb's implementation. From this
point onward, V2's evolution of this verb is decoupled from V1's
evolution of the corresponding trunk source. If V1 fixes a bug in the
trunk after the vendoring date, V2 either pulls the fix forward into
`Adopted/` (citing the V1 fix) or leaves the divergence (citing the
divergence rationale).

**State 2: RefinedInPlace.** The vendored source has been edited.
V1's mental-model traps (string-everywhere config, exception-driven
control flow, scattered overrides, mutation-as-default) have been
replaced with V2 idioms (typed records, `BridgeResult<T>` returns,
structured builders, immutability). The code is still C#; sometimes
because the underlying library is C#-idiomatic and an F# rewrite
would add cost without value (SMO, DacFx, ScriptDom), sometimes
because the refinement is sufficient and an F# rewrite is not yet
worth the editorial work. Sibling classes V1 kept separate for
reasons that don't apply in V2's vocabulary have been collapsed. The
method's name (in `Capabilities/`) is unchanged; the V1 source
citation (`[BridgeMethod].V1Source`) is unchanged; what changes is
the implementation in `Adopted/`. A method that has reached
`RefinedInPlace` represents the editorial discipline at its most
visible: V2 has read V1's manuscript, decided what to keep, and
published the refined edition under V2's imprint.

**State 3: TranslatedToFSharp.** The refined C# has been ported to
F# and lives in the appropriate F# project (`Projection.Core` if the
logic is purely algebraic, `Projection.Adapters.*` if it touches the
boundary, `Projection.Targets.*` if it's an emitter). The Bridge
method is *removed* — its file in `Capabilities/` deleted — and V2's
F# adapter now calls F# directly. The manifest no longer carries an
entry for the method; the audit trail moves into git history. The
state is the terminal state on the gradient for methods whose F#
rewrite is a clear net gain. Not every method targets State 3; some
are correct at State 2 indefinitely. The decision of whether a method
should target State 3 or stop at State 2 is per-method and is recorded
in `[BridgeMethod].Target`.

The gradient is partial, not total. A method must occupy `Current ≤
Target` on the gradient (the `BridgeManifest.Validate` check enforces
this); a method whose `Current` is ahead of its `Target` is a
manifest error. Methods enter at `Delegated` and progress toward
their target; the progression is a chapter-level commitment, not a
session-level commitment. A method that ships at `Delegated, Target =
RefinedInPlace` is announcing the future editorial work; the
`RefinedInPlace` transition happens in a later chapter, when the
chapter's scope includes the editorial work and when the equivalence
witness is in place to verify the refinement is correctness-
preserving.

The gradient is what makes adoption a continuous editorial gradient
rather than a phased calendar. There is no "now we adopt, now we
refine, now we rewrite" schedule; there is only "this chapter
advances these methods on the gradient toward their targets." A
chapter does not close until its Bridge methods have reached the
target state the chapter declared. The cumulative effect of many
chapters' worth of gradient transitions is the inheritance — V2's
codebase progressively absorbs V1's working logic, refining it in
V2's territory, until V1 is empty and V2 stands self-contained.

---

## IX. The audit attribute as manuscript history

Every public Bridge method carries
`[BridgeMethod(Chapter, AddedDate, V1Source, Current, Target,
Determinism, Frequency)]`. The seven fields are the citation
apparatus that makes the inheritance auditable. The reflection-scanned
`BridgeManifest` aggregates the attributes; `BridgeManifestTests`
asserts well-formedness at every test run; the
`Projection000BridgeWallDiscipline` analyzer enforces presence at
compile time. The attribute IS the manuscript history of V2's
inheritance from V1; without it, the inheritance would be ad-hoc
translation and the cutover+30 sunset gate would have no surface to
gate against.

**`Chapter`** cites the V2 chapter that demanded the lift, as a
string of the form `"0.5"`, `"4.1.B"`, `"4.2"`, etc. The chapter
citation links each Bridge method to a chapter open document
(`CHAPTER_<N>_OPEN.md`) that records the evidence claim — what
consumer needed this verb, what property test gates it, what target
state the chapter assigns. A method without a chapter citation is a
method without a justification; the analyzer rejects empty values.

**`AddedDate`** is the ISO-8601 date the method was added to the
manifest. The date forms part of the manuscript history together with
`V1Source`. The validator checks that the date is parseable; the date
need not be the date the Bridge method's source code was committed
(a method's commit may precede its audit attribute if the manifest's
discipline lands later), but the date must be a real date that can be
cross-referenced against the chapter's timeline.

**`V1Source`** is the fully-qualified V1 source citation:
`Namespace.Type.Method`. For methods that have an F# antecedent
(V2-for-V1 capabilities surfacing V2 algebra to V1), the special
value `"OriginAuthoredInV2"` is the reserved citation. The
`V1Source` is the most important audit field: it tells a future
maintainer exactly which V1 file/type/method this Bridge method
inherits from, at the commit it was adopted. When a Bridge method
transitions from `Delegated` to `Vendored`, the `V1Source` continues
to cite the V1 trunk source even though Bridge now calls the local
copy — the citation is the *manuscript ancestry*, not the runtime
dependency.

**`Current`** is the state the method currently occupies on the
inheritance gradient. New methods enter at `Delegated`. Transitions
to `Vendored`, `RefinedInPlace`, or `TranslatedToFSharp` are chapter-
level editorial acts; the chapter that performs the transition
updates `Current` in the corresponding commit. The state's value is
authoritative — what the attribute says is what the method's
implementation is. A drift between the attribute and the
implementation is a manifest error caught by the well-formedness
test.

**`Target`** is the state the method should reach. The cutover+30
sunset gate asserts every method's `Current` equals its `Target`. A
method whose `Target` is `RefinedInPlace` and whose `Current` is
`Delegated` has scheduled work against it; that work lands in a
later chapter that names the method explicitly in its scope. The
`Target` is a commitment, not an aspiration; a chapter that does not
land its committed transitions does not close.

**`Determinism`** declares whether the underlying V1 (or V2-for-V1)
operation is byte-deterministic on stable input. Two values:
`Deterministic` (the V1 SQL extraction, the V1 SMO emission, the V1
DMM compare, etc. — all are deterministic on stable input);
`NonDeterministic` (V1 live SQL extraction with implicit row ordering,
V1's random GUID generation if it ever surfaces, V1's timestamping
operations). The analyzer rejects downstream T1 claims that would
silently inherit V1's non-determinism — a pass that consumes a
`NonDeterministic` Bridge method's output cannot claim T1 byte-
determinism over its own output without explicit canary attestation
naming the source.

**`Frequency`** declares the call-frequency class. Four values:
`OneShot` (called once per pipeline run); `PerTable` (called O(tables)
times); `PerColumn` (called O(columns) times); `PerRow` (called
O(rows) times, potentially millions). The analyzer enforces the
frequency-shape contract: a `PerRow` method MUST return
`IAsyncEnumerable<T>` or accept a batched `IReadOnlyList<T>`, never
`Task<T>` per-call. The contract makes marshaling cost structurally
bounded — the wall's analyzer-enforced shape is what prevents Bridge
methods from quietly tanking the perf-gate at canary scale.

The seven fields together form a complete audit record. A reviewer
reading a Bridge method's source can answer, without spelunking
elsewhere: which V2 chapter demanded this verb, when it was added,
what V1 source it descends from, where it sits on the inheritance
gradient, what target state the project committed to, what
determinism class the method falls in, what marshaling cost class
governs its shape. The audit record is structural — the attribute is
a compile-time artifact, the analyzer enforces presence, the
manifest test enforces well-formedness, the sunset gate enforces
target convergence.

The manuscript-history framing is precise. Every Bridge method is a
node on V2's inheritance phylogeny. Every node cites its parent (V1
source). Every node declares its state in the editorial process. The
phylogeny is enumerable (the manifest); the phylogeny is auditable
(the well-formedness test); the phylogeny is closable (the sunset
gate). The audit attribute is the citation apparatus that makes the
phylogeny visible and verifiable, exactly the way a footnoted edition
makes a book's lineage from earlier editions visible and verifiable.

---

## X. The eight wall rules

The Bridge wall is a structural type encoding of the inheritance
discipline. The wall has eight rules, each enforced by the
`Projection000BridgeWallDiscipline` analyzer on every type and method
in the `Projection.Bridge.Core` and `Projection.Bridge.Runtime`
namespaces. Mistakes do not compile.

**Rule 1 — BCL types only across the wall.** Public method parameters
and return types use BCL types (`string`, `int`, `long`, `bool`,
`DateTimeOffset`, `Guid`, `IReadOnlyList<T>`, `IReadOnlyDictionary<K,V>`,
`Task<T>`, `IAsyncEnumerable<T>`, `CancellationToken`,
`BridgeResult<T>`) and Bridge's own `Wire/` records only. No type
from `Osm.Domain.*`, `Osm.Json.*`, `Microsoft.SqlServer.Smo.*`, F#'s
`FSharpOption<T>` / `FSharpResult<TOk,TErr>` / `FSharpList<T>` may
appear in any public signature. The rule is the architectural
commitment that no V1 domain type and no F# core type ever crosses
the wall. F# adapters consume Bridge as plain CLR; F# never needs
typed knowledge of V1's surface. The wall is the seam where the type
systems pretend they don't know each other; what crosses is BCL.

**Rule 2 — Capability-shaped (verbs, not nouns).** Method names are
imperative verbs naming operations, not getters returning nouns.
`ExtractMetadataAsync`, `BuildProfileQueriesAsync`, `RenderSsdtAsync`,
`CompareWithDmmAsync`, `ParseRefactorLogAsync`,
`LoadOverrideBindingsAsync`, `MatchUsersAsync`,
`GenerateMergeInsertAsync`, `ReadCdcRowsAsync`. Forbidden:
`GetOsmModel`, `GetEntities`, `BuildModel`, `Model` — noun surfaces
leak V1 mental model into V2 territory. The rule is "lift verbs, not
nouns" expressed at the method-name level. The analyzer's check is
heuristic (a noun-surface check); the four-question naming discipline
catches the rest at PR review.

**Rule 3 — V2 vocabulary in record names.** Wire records use V2
vocabulary, never V1 vocabulary. `ExtractedKindRow` (not
`ExtractedEntityRow`); `ExtractedModuleRow` (not
`ExtractedEspaceRow`); `ExtractedAttributeRow` (not `ExtractedAttrRow`).
Bridge performs the V1→V2 vocabulary translation *inward*, before the
value crosses the wall. F# never sees V1's `Espace` or `Entity`. The
rule operationalizes pillar 8 (domain-first naming and ubiquitous-
language consistency); the analyzer's check uses a configurable
rename map (Espace→Module, Entity→Kind, Attr→Attribute, etc.) and
flags any record name whose tokens match V1's vocabulary.

**Rule 4 — `CancellationToken` at every public entry.** Every public
method accepts `CancellationToken cancellationToken = default` as its
last parameter — even pure-CPU methods like `ParseSnapshotJsonAsync`
that don't have async work to cancel. The rule removes a future
refactor surface (if a Bridge method later needs to be async, the CT
is already there) and matches V1's existing application-service
shape (`RunAsync(input, cancellationToken)`). The rule pairs with V2's
`AsyncStream.probe` and `taskResult { }` consumer patterns in
`Projection.Adapters.Sql`.

**Rule 5 — Never throws across the wall.** Bridge methods catch
exceptions at the wall and reify them as `BridgeResult<T>.Failure`
with a `BridgeError` carrying a `category.subject.problem` code. The
F# adapter consumer pattern-matches on the result without `try/with`
at every site. The rule mirrors V1's `Osm.Domain.Abstractions.Result<T>`
shape (V2's `Projection.Core.Result<'a>` is the F# sibling). On raw
exceptions that escape V1's `Result<T>` discipline (the V1 chain
throws exceptions for certain validation failures), Bridge converts
to code `"bridge.<capability>.unhandled"` with metadata carrying
`exception.type` and `exception.message`. The rule makes the F#
adapter's consumption uniform: every Bridge call returns a result, no
matter what V1 did.

**Rule 6 — One public method per file.** Each `Capabilities/*/X.cs`
file contains exactly one public method (plus its capability-specific
record types if those are not in `Wire/`). The rule is the cherry-pick
auditability commitment: a Bridge method's entire surface fits in one
file diff. The chapter that introduces a method touches exactly one
file in `Capabilities/`; the reviewer can see exactly the inheritance
the chapter adds. If a method needs siblings (a helper method, a
factory method), the siblings are `private` or `internal`; only the
public capability method gets the `[BridgeMethod]` attribute.

**Rule 7 — Frequency-shape contract.** A method whose
`[BridgeMethod].Frequency` is `PerRow` MUST return
`IAsyncEnumerable<T>` (for streaming consumption via
`Projection.Adapters.Sql.AsyncStream`) or accept a batched
`IReadOnlyList<T>` input and return a batched
`Task<IReadOnlyList<T>>` output. Per-row `Task<T>` is rejected by the
analyzer. The rule makes marshaling cost structurally bounded. A
capability whose V1 implementation cannot be reshaped to the contract
is rewritten in F# (target `TranslatedToFSharp` from the start)
instead of being lifted; the analyzer's rejection is the structural
forcing function.

**Rule 8 — `[BridgeMethod]` attribute required.** Every public method
in `Projection.Bridge.*` namespaces carries the attribute with all
seven fields populated. The analyzer's check is exhaustive: missing
attribute, missing field, empty string field, all rejected at compile
time. The rule operationalizes the audit substrate; without it, the
manifest test would catch the missing attribute at test time, but the
analyzer catches it earlier (at edit time, in the IDE) so the
contributor sees the error before committing.

The eight rules together form the wall. Mistakes do not compile. The
wall is not a documented convention; it is a structural type encoding
of the inheritance discipline. The rules are not aspirational; they
are enforced by code that ships in `Projection.Analyzers`. The wall
is what makes the inheritance gradient work — without it, contributors
could add methods that drift from the discipline, and the gradient's
audit substrate would be unreliable.

The analyzer is the second analyzer V2 ships, after the existing
`NoUnsafeTimeInCoreAnalyzer.fs` (which enforces "no `DateTime.Now` in
Core"). Both analyzers follow the same F# Analyzers SDK precedent;
the chapter that lands the Bridge wall analyzer (chapter 0.5 slice β)
generalizes the pattern enough that future analyzers (slice ν from
chapter 5+) can adopt it with minimal scaffolding.

---

## XI. Lift verbs, not nouns

The single most important discipline of the Bridge wave, the one
behind every wall rule and every gradient transition and every audit
field, is the discipline that V2 lifts V1's *operations*, never V1's
*types*. The rule has a name: **lift verbs, not nouns**. It is the
editorial discipline made structural.

V1 has a lot of working logic V2 wants to inherit. V1 also has a lot
of mental-model artifacts V2 explicitly does not want to inherit. The
two categories overlap in V1's source: the working logic is wrapped
in V1's mental model. The discipline of the Bridge wall is to separate
them — to lift the verb (the operation that does the work) without
inheriting the noun (the type, structure, or vocabulary that wraps
the verb in V1's mental model).

The discipline is concretely visible in the Wire records. V1's
`OsmModel` is a deeply nested aggregate of `OsmModule -> OsmEntity ->
OsmAttribute`, with smart-constructor invariants and metadata
records. V2's Bridge does not inherit `OsmModel`; the Bridge's output
is a flat tuple of `IReadOnlyList<ExtractedModuleRow>`,
`IReadOnlyList<ExtractedKindRow>`, `IReadOnlyList<ExtractedAttributeRow>`,
etc. — V2's rowset surface, projected from V1's rowset surface
(`OutsystemsMetadataSnapshot`), not from V1's aggregate root. The
aggregate root is V1's reconstruction of the rowsets into a noun;
V2 reconstructs from the rowsets directly into V2's own aggregate
root (`Catalog`), skipping V1's mental-model layer.

The discipline is concretely visible in the input records too. V1's
`ExtractModelApplicationService.RunAsync` accepts
`ExtractModelApplicationInput(CliConfigurationContext, ExtractModelOverrides, SqlOptionsOverrides)`.
Bridge's `ExtractMetadataAsync` does not accept any of these; it
accepts `ExtractMetadataInput(string ConnectionString, IReadOnlyList<string>? ModuleFilter, bool IncludeSystem, bool IncludeInactiveModules, bool OnlyActiveAttributes)` — five
primitive fields that name V1's verb's actual semantic inputs.
Bridge re-implements V1's configuration-flattening internally in ~30
lines; the C# `CliConfigurationContext` is V1's CLI-layer wrapper, not
the verb's input.

The discipline is concretely visible in the vocabulary too. V1 calls
modules "Espaces" because OutSystems calls them that; V2 calls them
"Modules" because the algebra is source-agnostic and "Module" is the
ubiquitous-language name across V2. V1 calls entities "Entities"; V2
calls them "Kinds" because the algebra serves OutSystems today and
must accommodate DACPAC, OData, and other sources. The Bridge wall
performs the rename inward — `ExtractedKindRow` not
`ExtractedEntityRow` — so F# never sees V1's vocabulary.

The discipline is concretely visible in the error handling. V1
sometimes throws `MetadataRowMappingException` or
`MetadataResultSetMissingException` for what V2 considers ordinary
validation outcomes. The Bridge wall converts these to
`BridgeResult<T>.Failure` with codes like
`"bridge.catalog.extractMetadata.rowMappingFailed"`. F# adapters
consume the result and pattern-match without `try/with`. V1's
exception-driven control flow stops at the wall.

The discipline has a corollary: when V2 inherits a verb, V2 takes
responsibility for the verb's behavior, but V2 does not inherit
V1's mental-model framing of what the verb is. The verb is the
operation that does the work — the SQL query, the result-set
processing, the rowset materialization, the JSON deserialization. The
noun is the framing — the `OsmModel` aggregate root, the
`ExtractModelApplicationResult` wrapper, the `CliConfigurationContext`
configuration cradle. V2 inherits the verb; V2 rebuilds the noun in
V2's own vocabulary.

This is the editorial discipline. V2 reads V1's manuscript and asks:
what is this paragraph *doing*? what is this paragraph *about*?
What does it accomplish, and what does V1's chosen framing add or
subtract? The verb gets carried forward; the framing gets edited.
The result is a refined edition of V1's manuscript, published under
V2's imprint, in V2's voice.

The discipline scales. Every Bridge method is a worked example of
the discipline. Every Wire record is a worked example. Every
gradient transition is a worked example. The eight wall rules
encode the discipline structurally so that contributors don't have
to remember it — the analyzer remembers it. The audit attribute
records each method's commitment to the discipline. The
`BridgeManifest` aggregates the commitments. The cutover+30 sunset
gate verifies the commitments held.

---

## XII. What V2 explicitly does not inherit

The editorial discipline of "lift verbs, not nouns" has a complement
that is equally important: V2 explicitly does NOT inherit certain
patterns from V1, even when V1 has working code instantiating those
patterns. The non-inheritance is a deliberate editorial act, recorded
in DECISIONS and visible in the absence of corresponding Bridge
methods.

**V1's string-everywhere configuration.** V1's `NamingOverridesBinder`
parses override syntax like `"Module::Entity|schema.table=NewName"`
with seven nested string splits. V1's `ModuleValidationOverrides`,
`NullabilityOverrideOptions`, `TighteningOptions`, and the five CLI
binders each carry their own parsing logic. The pattern is
"configuration as text DSL," and V1 has spent significant LOC making
it work. V2 does not inherit this pattern. V2 has a single structured
builder (`OverrideBindingContext`) that consumes typed records; the
binder is rebuilt fresh in F# from the operator-facing input. V1's
seven scattered parsing classes are mental-model artifacts V2
discards.

**V1's exception-driven control flow.** V1's
`MetadataSnapshotRunner.cs:140-199` uses custom exception types
(`MetadataRowMappingException`, `MetadataResultSetMissingException`)
to signal normal validation outcomes. V1's pipeline orchestration
throws to fail-fast in step chains. V2 does not inherit
exception-driven control flow. V2 uses `Result<'a, ValidationError list>`
throughout; exceptions are reserved for true invariant violations the
type system could not prevent. The Bridge wall converts V1's
exceptions into `BridgeResult<T>.Failure` at the wall, so F# adapters
consume results, not exceptions.

**V1's mutation-as-default in orchestration.**
`PipelineBootstrapper:143L`, `BuildSsdtBootstrapSnapshotStep:506L`,
and the step classes accumulate state in `ExecutionLog` mutable
properties; `ModelExecutionScope:21L` mutates command-scope context.
The pattern is "steps mutate shared context." V2 does not inherit
this pattern. V2's pipeline composes immutable records, with state
threaded through the writer monads (`Lineage<T>`, `Diagnostics<T>`).
The composition is structural, not mutative; reasoning about pipeline
state in V2 requires reading the type signature, not tracing
mutations across fifteen steps.

**V1's seven override-binding mechanisms.** This is the consolidated
form of the first three points, named explicitly so it can be
referenced by future agents. V1 has scattered the operator-override
concept across seven separate mechanisms: `NamingOverridesBinder`,
`ModuleValidationOverrides`, `TighteningOptions`, plus four CLI
option binders (Cache, Sql, Tightening, SchemaApply, UatUsers). Each
mechanism has its own parsing, its own validation, its own mutable
accumulator. V2 inherits the *concept* — operator overrides exist
and must be bound — but V2 rebuilds the binding fresh in F# with a
single structured `OverrideBindingContext` consumed uniformly by all
verbs. The seven scattered mechanisms are the kind of V1 mental-model
trap V2 explicitly leaves behind.

**V1's environment-variable feature flags.**
`/src/Osm.Cli/Program.cs:45-52` enables `UatUsersCommand` only when
`OSM_ENABLE_REMAP_USERS` is set. The pattern is "feature flags as
env vars parsed in host builder." V2 does not inherit this pattern.
V2's CLI uses typed configuration options consumed uniformly; feature
gating is explicit in the type system, not implicit in environment
parsing.

**V1's type leakage in profile results.**
`/src/Osm.Pipeline/Profiling/ProfilingContracts.cs` exposes V1 domain
types (`AttributeId`, `EntityId` as `int`) through profile
aggregators that return `IReadOnlyDictionary<int, ProfileResult>`. The
pattern is "boundary results expose internal IDs." V2 does not
inherit this pattern. V2's Bridge surfaces profile results as
records keyed by V2-vocabulary identifiers (`SsKey` projected to
BCL via the Wire records); V1's int-keyed lookups are reconstructed
fresh in F#.

The non-inheritance discipline has a name: **leave behind what V1
wrapped the verbs in, not what V1 did with the verbs.** V1's CLI
configuration cradle is wrapping. V1's exception-driven validation
is wrapping. V1's mutable orchestration is wrapping. V1's
environment-variable feature flags are wrapping. V1's int-keyed
profile lookups are wrapping. V2 inherits what the verbs accomplish
and discards the wrapping. The wrapping is what made V1 feel like V1;
V2's discarding of the wrapping is what makes V2 feel like V2.

The non-inheritance is structural where possible. The wall rules
prevent V1 types from crossing the wall, so V2 cannot accidentally
inherit V1's `OsmModel` or `CliConfigurationContext` or
`ProfilingResult`. The wall rules prevent V1's exception-driven
control flow from reaching F# — the wall converts to `BridgeResult<T>`.
The wall rules prevent V1's mutation-as-default from reaching F# —
the Wire records are immutable.

The non-inheritance is editorial where structural enforcement is
insufficient. V1's seven scattered override-binding mechanisms could
in principle be lifted (each binder has working logic), but V2
chooses not to lift them because the seven-mechanism shape is the
pattern, and the pattern is the trap. V2's single-builder
replacement is a chapter-level commitment recorded in DECISIONS.

The non-inheritance is announced. Every chapter that opens names
explicitly what it inherits and what it does not. The audit attribute
declares each Bridge method's lineage; the ADMIRE entries declare
each V1 component's placement; the DECISIONS entries declare each
non-inheritance editorially. There is no quiet inheritance and no
quiet non-inheritance. The choices are visible, cited, and
auditable.

---

## XIII. The equivalence witness

The Bridge wave's structural commitments — the inheritance gradient,
the audit attribute, the wall rules, the lift-verbs-not-nouns
discipline — are necessary but not sufficient. They guarantee that the
inheritance is well-shaped; they do not guarantee that the inheritance
is correctness-preserving. The guarantee of correctness-preservation
is a property test, and the chapter 0.5 slice ζ is where it lands.

The test is: for a canonical fixture, the Catalog produced by parsing
the canonical rowset bundle is structurally equal to the Catalog
produced by routing through the Bridge — modulo a closed, named
tolerance set covering collection-ordering non-determinism.

In F#:

```fsharp
[<Fact>]
let ``ε: Bridge inheritance is correctness-preserving on canonical fixture`` () =
    let canonical = parseSync (SnapshotRowsets canonicalBundle)
    let bridged = parseSync (LiveOssysViaBridge bridgeInput)
    Assert.Equal<Catalog>(
        CatalogEquivalence.normalizeForEquivalence canonical,
        CatalogEquivalence.normalizeForEquivalence bridged)
```

The test is the structural witness that V2's inheritance from V1 is
correct. If it passes, the Bridge's path produces the same Catalog as
the canonical-rowset path; the inheritance is correctness-preserving.
If it fails, the failure surfaces a real Bridge correctness gap, with
structured `CatalogDiff` output naming the divergence.

The equivalence relation is precise. `CatalogEquivalence.normalizeForEquivalence`
quotients out six legitimate non-determinism axes (collection orderings
that V1's rowset emission may or may not preserve depending on the
specific path): the module list order, the kind list order within a
module, the reference list order within a kind, the index list order
within a kind, the `ModalityMark` list order, the static-row list
order. The relation preserves five semantic axes that must match
exactly: the attribute declaration order within a kind (V1's SQL
emits attributes by `ORDER BY a.EntityId, a.AttrName`; the order is
semantic — emitters and consumers rely on it), the index column
ordinal order (driven by `m2.Ordinal`), the SsKey shape (`OssysOriginal`
Guids carried unchanged), the identifier casing and whitespace (V1's
casing is preserved verbatim; case-folding would silently break
SsKey synthesis), and the optional-field carriage (NULL → None across
both paths).

The fixture is precise too. The chapter 0.5 slice ζ ships a
`bridge-ossys-seed.sql` file (in
`tests/Projection.Tests/Adapters/Osm/Fixtures/`) seeded with at
least two modules, one module with at least three kinds, one kind
with at least four attributes covering PK + FK + IDENTITY + NVARCHAR,
one kind with at least two indexes (PK + secondary unique +
composite non-unique with INCLUDE), one static entity with at least
three rows, one entity with `Is_System=1`, and one external entity
with `EspaceKind='Extension'`. The fixture exercises every
equivalence-axis cell so the property test is meaningful.

The equivalence relation extends to every subsequent gradient
transition. When `ExtractMetadataAsync` transitions from `Delegated`
to `Vendored`, the equivalence test asserts that the vendored
implementation produces the same Catalog as the delegated
implementation did. When `ExtractMetadataAsync` transitions from
`Vendored` to `RefinedInPlace`, the equivalence test asserts that the
refined implementation produces the same Catalog. The equivalence
witness is the structural rope that ties every gradient transition
back to V1's working behavior.

The witness is what makes the inheritance verifiable. The audit
attribute records the lineage; the equivalence test verifies the
lineage is faithful. The structural-type encoding of the wall makes
the inheritance well-shaped; the property test makes the
inheritance correct.

The witness also makes the equivalence-relation amendment
disciplined. If a future Bridge method's lift surfaces a legitimate
new non-determinism axis (e.g., a Bridge method that consumes
streaming live data and produces an unordered result), the axis is
added to `CatalogEquivalence.normalizeForEquivalence` deliberately,
with a comment naming the axis and the V1 source's
non-determinism source. The equivalence relation grows under
evidence, not under speculation; the discipline mirrors V2's IR-
grows-under-evidence discipline at the equivalence-relation level.

---

## XIV. Axiomatic alignment

The eight pillars and the formal axiom set (A1–A40 plus T1–T11, plus
A41/A42 candidates) all hold under the Bridge wave. Each holds for
specific reasons; understanding the reasons is what makes the
inheritance feel like culture rather than exception.

**Pillar 1 (data-structure-oriented over string-parsing).** Holds at
every Bridge wall by Wire records that are V2-typed before they
cross. The discipline strengthens as adoption proceeds: `Delegated`
honors the pillar at the wall only (V1's internal implementation may
still use V1 patterns); `Vendored` honors it through the entire
internal implementation (V1's source is in V2's territory, subject to
V2 review); `RefinedInPlace` honors it with V2 idioms (typed records
replace V1's string-everywhere patterns); `TranslatedToFSharp` honors
it with the F# type system. The gradient is also a gradient of
deepening pillar-1 honor.

**Pillar 2 (no string concatenation aggressively).** Holds because
Bridge does not concatenate; Bridge returns rows. String construction
happens at the Π layer (SQL/JSON rendering, downstream of Bridge),
not at the wall. The wall analyzer's frequency-shape contract also
prevents the most likely cause of string-concat regression at the
wall (PerRow methods returning aggregated strings instead of
streaming records).

**Pillar 3 (built-in obligation).** Holds at the wall by BCL-types-
only rule. The wall uses `Guid`, `string`, `IReadOnlyList<T>`,
`Task<T>`, etc. — the BCL's standard surface. No `StructuredString`
crosses the wall; V2's typed AST builder is for internal F# use, not
for the inheritance surface.

**Pillar 4 (FP promised land, ≥95% pure).** Holds because V2's Core
purity is unchanged by the Bridge — Core has zero dependency on
Bridge, and adapters consume Bridge values that are immutable BCL
records. V1's internal mutation (where V1 has it) stays inside the
adopted source; V2 reads results, not state.

**Pillar 5 (coding-style commitments: deep DDD, point-free
composition, hexagonal architecture, hardcore FP).** Holds because
the Bridge surface is hexagonal — it sits at the boundary, F#
adapters consume it, the core stays pure. DDD is honored at the
record naming (V2 vocabulary); FP is honored at the F# side; OOP is
acceptable in C# at the wall (where BCL surfaces force it).

**Pillar 6 (no V2-internal back-compat paths).** Holds by direction.
V2 inherits from V1 once and refines forward; there is no V1-
compatibility surface inside V2. Bridge's *public surface* is V2-
shaped from the day it is written; Bridge's *implementation* is the
temporary editorial state that progresses through the gradient. The
pillar holds because the arrow is one-way.

**Pillar 7 (gold-standard library precedence).** Holds with reinforcement.
The pillar's three tiers (use-case-specific library; typed data
structures; `StructuredString`; LINT-ALLOW with rationale) extend to
the Bridge: the gold-standard library is BCL types; the typed data
structures are Wire records; the LINT-ALLOW does not apply across
the wall (BCL types only; no LINT-ALLOWs for `StructuredString` or
string-concat at the boundary). The Bridge formalizes the pattern at
the V1 boundary, treating V1 the way SqlClient is treated — as an
external library V2 wraps in adapter-shaped F# (or C# in the
Bridge's case).

**Pillar 8 (domain-first naming and ubiquitous-language consistency).**
Holds at every Wire record. V1's `OsmModel` does not survive into V2
by name. The four-question naming analysis runs at every Wire record;
the analyzer enforces V2 vocabulary; the ubiquitous-language consistency
is structural.

**Pillar 9 (data-intent / operator-intent dichotomy).** Holds
structurally. Bridge methods cannot accept `Policy` as a parameter —
the analyzer rejects the signature. DataIntent flows from V1's
evidence-collection verbs through Bridge to V2's enrichment passes;
OperatorIntent never crosses the wall in either direction. The
skeleton-purity property extends to the Bridge wall: a Bridge call
produces the same output regardless of downstream Policy state. A41
candidate gains a Bridge clause codifying this.

**A1 (stable identity).** Holds with reinforcement. V1's SsKey
columns are carried through the wall via `Guid?` fields on Wire
records (e.g., `ExtractedKindRow.KindSsKey`). The four-variant SsKey
DU's `OssysOriginal` case becomes operationally reachable across
both source variants (JSON and Bridge). A1's identity-survives-rename
guarantee is honored unconditionally through the Bridge path.

**A6 amended (three substantive inputs + lifecycle).** Holds with a
Bridge corollary. V1 State is a fourth boundary input under the
Bridge wave; Bridge methods may consult V1 State only; the signature
cannot accept Policy, Profile, or Lifecycle parameters. The analyzer
enforces.

**A18 amended (Π never consumes Policy).** Holds with a Bridge
corollary: Π is downstream of Bridge, and Bridge is downstream of
evidence sources. The chain is `Evidence → Bridge (DataIntent) →
E-passes (OperatorIntent applied) → Π (consumes Catalog × Profile per
A18 amended)`.

**A24/A25 (Lineage trail; every decision emits event).** Holds because
Bridge methods are pre-Lineage. They return raw V1 evidence with no
events; the F# adapter that consumes them is where the Lineage trail
begins. The "Bridge output carries empty Lineage" property is
asserted; the analyzer-enforced never-throws rule prevents Bridge
errors from masquerading as Lineage events.

**A32 (passes produce emitter-consumable values).** Holds because
Bridge methods are pre-pass; they produce evidence values that pass
drivers consume. The chain runs Bridge → pass → Π, never short-
circuiting.

**A35 (Π output is a deterministic statement stream).** Holds
unchanged. Bridge sits before Π in the chain; Bridge does not
construct statements. ScriptDom's typed AST is reached at the
emitter, not at the wall.

**A36 (bulk-vs-incremental is realization-layer policy).** Holds
unchanged. Bridge does not deploy; Bridge surfaces evidence.

**A39 (smart-constructor invariants).** Holds because V2's
`Catalog.create` re-validates the Catalog reconstructed from Bridge
output, just as it validates the Catalog reconstructed from the JSON
path. The double-validation is acceptable; V2's invariants are the
canonical ones.

**A40 (harmonization-via-parameterization).** Holds with worked
example. The Bridge wave reuses `parseRowsetBundle` for both the
JSON path and the Bridge path; the parser is the harmonized
algorithm parameterized by source variant. One parser, multiple
projections.

**T1 (byte-for-byte determinism).** Holds with conditional
determinism per Bridge method. `[BridgeMethod].Determinism` declares
per method; `NonDeterministic` methods may not back T1 claims
downstream without explicit canary attestation. The analyzer
enforces.

**T11 (sibling-Π commutativity).** Holds unchanged for V2 emitters
and extends to the V2-for-V1 surface. V2 outputs exposed back to V1
via Bridge.Runtime are projections of V2's Catalog; the sibling
relationship holds at the projection level.

**A41 candidate (TransformRegistry totality).** Gains a Bridge clause:
Bridge methods are `DataIntent` sources by type signature; they are
not registered as `OperatorIntent` overlays in TransformRegistry.
The skeleton-purity property extends to the wall.

**A42 candidate (inheritance citation discipline).** New. Every
public Bridge method carries `[BridgeMethod].V1Source` citing V1's
file/type/method, or `"OriginAuthoredInV2"` for V2-for-V1 capabilities.
The reflection-scanned manifest aggregates citations; the analyzer
enforces presence; the manifest test enforces well-formedness.

The axiomatic alignment is not coincidence. The Bridge wave was
designed against the axiom set; the axioms were tested for
compatibility before the wave shipped. Every commitment of the wave
has an axiom that backs it; every axiom that touches the V1↔V2 seam
has a wave clause that strengthens it.

---

## XV. The R6 Stage-2 specification

The R6 split-brain governance rule (DECISIONS 2026-05-22) framed three
rungs of a fallback ladder for the dual-track cutover window: V1-only
(V1 cutover, V2 ships post-cutover); V2-augmented (V1 drives, V2
verifies in PR; per-environment-per-artifact-type V2-driver transition
gated on N=10 consecutive green canary runs plus operator sign-off);
V2-driver (V2 emits production artifacts; V1 stays warm through
cutover+30 as fallback). The Bridge wave introduces an intermediate
stage that sits between V2-augmented and V2-driver: Stage 2.

**Stage 1 (V2-augmented baseline).** V1 emits production artifacts.
V2's canary runs in parallel and verifies that V2's would-have-been
emission matches V1's modulo named tolerances. Disagreement is
telemetry, not blocking. The stage is the entry point for any
environment-artifact pair; it operates indefinitely until the
operator promotes the pair to Stage 2.

**Stage 2 (Bridge-enabled V2-augmented).** V1 still emits production
artifacts. Bridge+V2 emit in parallel via the inheritance surface —
V2 reads via `LiveOssysViaBridge`, projects via V2's pipeline, would
emit V2's artifacts. The canary compares V1's actual emission against
Bridge+V2's would-have-been emission via the Tolerance taxonomy.
Disagreement blocks the PR. Stage 2's gate is *N=10 consecutive green
canary runs plus operator sign-off*, exactly as the original R6
specification required for the V2-driver transition. The difference
between Stage 1 and Stage 2 is which path V2 verifies via — Stage 1
uses V2's JSON-data adapter; Stage 2 uses V2's Bridge inheritance
adapter. The substantive verification is the same; the data path is
what changes.

**Stage 3 (V2-driver).** V2 emits production artifacts. V1 stays warm
through cutover+30 as fallback. The stage is reached when Stage 2's
gate is green and the operator authorizes the flip per
environment-artifact pair.

The Bridge wave is what makes Stage 2 operational. Without the
Bridge, the V2-augmented stage could only verify V2's emission
against V1's via the JSON-data path; the Bridge adds a second
verification path (the inheritance path) that exercises V2's chain
end-to-end without depending on V1's JSON snapshot output. The two
paths cross-verify each other (the equivalence property test
asserts they produce the same Catalog); the cross-verification is
what makes Stage 2 a stronger gate than Stage 1.

The R6 governance protocol is preserved through every stage. V2
owns no production write path during Stages 1 and 2; V1 owns the
write path. The canary asserts V1 ≈ V2 modulo named tolerances;
disagreement blocks the PR. Per-environment-per-artifact-type V2-
driver transition is gated on N=10 consecutive green canary runs plus
operator sign-off. The four-environment cutover stays per-pair, never
global. Hard rule: V1 stays warm through cutover+30 regardless.

The Bridge wave's contribution to R6 is to add a stage between V2-
augmented and V2-driver where the V2 verification is anchored not to
V1's serialized output but to V1's working code. The Stage-2
specification makes the V1→V2 inheritance an active part of the
governance gate. By the time a pair flips to Stage 3, the Bridge has
already verified V2's emission against V1's actual computation, not
just against V1's serialized snapshot.

---

## XVI. The cherry-pick discipline, restated

The cherry-pick discipline was load-bearing through chapters 1
through 4.1.A. It said: every commit in the V2 sidecar is cherry-
pickable into a V1-only trunk without V2 pulling V1 dependencies.
The discipline was enforced by absence — V2 never referenced V1
source, so any V2 commit could be applied to a trunk without V1
present.

The Bridge wave restates the discipline. It does not weaken it.

The original framing — "the boundary is data, not typed cross-
references" — was a *statement of the form* the discipline took
under the data-only boundary. The deep commitment under the form was
*"V1 mental model does not enter F# code."* The data-only boundary
honored the deep commitment by *absence* — V2 didn't reference V1's
csproj at all, so V1 mental model could not have entered F# code
even if a contributor tried.

The Bridge wave honors the deep commitment by *types*. V1's csproj
references appear in `Projection.Bridge.Core` and
`Projection.Bridge.Runtime`. V1 domain types are reachable inside the
Bridge's internal implementation (`Translate/`, `Adopted/`). But V1
domain types cannot appear in any public signature — the wall
analyzer rejects them. F# adapters consume Bridge as plain CLR; F#
never sees a V1 type. The deep commitment holds: V1 mental model
does not enter F# code, even though V1 csproj references exist on
the C# side.

The restatement is the natural progression of an architectural
commitment as the codebase matures. Absence was correct for the
prototype, when the V2 sidecar was small and the cost of full
isolation was negligible. Types are correct for the garment, when
the V2 sidecar's scope grows to include inheritance from V1's
working code and the cost of perpetual JSON-data translation
becomes a load-bearing tax.

The cherry-pick discipline applies to all files outside the
`Projection.Bridge.*` namespaces. A commit that touches only
`Projection.Core`, `Projection.Adapters.*`, `Projection.Targets.*`,
`Projection.Pipeline`, `Projection.Cli`, or any test project under
`Projection.Tests` is cherry-pickable into a V1-only trunk
unchanged. The Bridge projects are the named exception, governed by
the audit attribute and the wall analyzer.

Cherry-pick safety is restored at cutover+30. When V1 retires, V1's
csproj references in the Bridge projects are removed (because no
public Bridge method remains in `Delegated` state, because every
method has reached its `Target` and either been vendored, refined,
or rewritten in F#). At that point, every commit in the sidecar is
once again cherry-pickable in the trivial sense — there is no V1
trunk to cherry-pick into, but every commit's content is V2-internal
and self-contained.

The discipline's deepest commitment — V1 mental model does not enter
F# code — has held continuously from chapter 1 through chapter 0.5
through cutover+30. The form the commitment takes evolves with the
stage of work; the commitment itself is constant.

---

## XVII. The cutover+30 sunset

The cutover+30 sunset is not a deadline event. It is a condition
that holds when the work is done.

When the cutover happens (date set by the operator, currently
estimated around late July 2026), V2 enters whichever mode the per-
environment-per-artifact-type gates have produced: V2-driver for
pairs that have passed the N=10 canary gate plus operator sign-off,
V2-augmented for pairs still in the verification stage, V1-only for
pairs the operator has chosen not to advance. V1 stays warm in all
cases; V1's emission path is preserved as a fallback the operator
can drop to for thirty days regardless of mode.

For thirty days post-cutover, V1 and V2 coexist. The operator
monitors V2's emissions against V1's fallback; canary results are
recorded; any divergence is investigated. The thirty-day window is
not a "do nothing" period; it is the soak period where V2's
production behavior is validated against V1's at scale, over time,
across the natural variability of real OutSystems metadata changes.

Cutover+30 arrives. At this point, V1 sunset begins — *not* because
the calendar says so, but because the condition holds. The condition
is:

1. **V2 has run V2 emissions in every environment for at least one
   full schema-evolution cycle without canary divergence.** This is
   the original R6 framing; the Bridge wave does not change it.

2. **Every Bridge method has reached its declared target state on
   the inheritance gradient.** This is the new framing the Bridge
   wave adds. The `BridgeManifestSunsetGateTest` is the structural
   witness: it scans the `BridgeManifest`, identifies any method
   whose `Current != Target`, and reports them. An empty report
   means the gradient has converged; the workshop is empty.

3. **The ProjectReference to V1's trunk assemblies has been removed
   from `Projection.Bridge.Core.csproj`.** When every Bridge method
   has been vendored, refined, or translated to F#, no method
   delegates to V1 anymore. The csproj's V1 references become dead
   code; they are removed. The removal is itself the proof that V2
   no longer depends on V1's trunk at runtime.

4. **The operator has signed off.** The sunset is an operator
   decision, supported by the structural evidence above. The sign-
   off is recorded in DECISIONS; the date of the sign-off is the
   moment V1 sunset begins administratively.

When the four conditions hold, V1's trunk source can be archived. The
csprojs under `src/Osm.*` no longer need to build; the test projects
no longer need to run; the CI pipelines no longer need to exercise
V1's emission path. V1 sunset is administratively complete.

The metaphor closes here. The flower has finished blooming. The hive
is fully stocked. The bees have stopped pollinating because there is
no more pollen to carry. V1's trunk source becomes a historical
artifact — preserved in git history, citeable as `V1Source` in any
remaining Bridge audit metadata that lingers, but not load-bearing.

The Bridge wave's sunset is not the same as V1's sunset. Bridge.Core
sunsets capability-by-capability as each Bridge method reaches its
target; by the time V1's sunset condition holds, Bridge.Core's
`Delegated` and `Vendored` states are empty across the manifest, and
the Bridge.Core project contains only `RefinedInPlace` C# capabilities
plus the audit primitives. Bridge.Runtime sunsets as a project: when
V1 retires, V1 stops calling Bridge.Runtime's capabilities, and the
csproj becomes dead code. The dead code can be deleted, or it can be
preserved as an archaeological artifact for future agents wondering
how the inheritance happened — the choice is editorial, not
load-bearing.

The cutover+30 sunset condition is what makes the Bridge wave
finite. The wave opens at chapter 0.5, lands gradient transitions
across chapters 4.1.B, 4.2, 4.3, 3.x, and other chapters that
benefit, and converges at cutover+30 when the manifest is empty.
There is no permanent V1-compatibility surface in V2; there is only
the editorial work of inheriting V1's verbs, refining them in V2's
territory, and publishing the result. When the editorial work is
done, the surface that hosted it can be retired.

---

## XVIII. Worked examples

The architecture is best understood through its worked examples.
This section walks through three Bridge methods at different
positions on the gradient, showing how the architecture's
commitments compose in practice.

### ExtractMetadataAsync (Catalog inheritance; chapter 0.5)

The first inheritance V2 takes from V1 is metadata extraction —
V1's mature SQL extraction chain (the 25+ result-set processors,
`MetadataSnapshotRunner`, `SnapshotJsonBuilder`, ~3,800 LOC) is
the donor capability. V2's inheritance proceeds in three stages.

Chapter 0.5 slice γ ships
`Projection.Bridge.Core/Capabilities/Catalog/ExtractMetadata.cs` at
`Current = Delegated, Target = RefinedInPlace`. The method's body
calls `Osm.Pipeline.Application.ExtractModelApplicationService.RunAsync`
through the ProjectReference. The input is Bridge's own
`ExtractMetadataInput(connectionString, moduleFilter, includeSystem,
includeInactiveModules, onlyActiveAttributes)` — five primitive
fields that name V1's verb's semantic inputs. The output is a
`BridgeResult<ExtractMetadataOutput>` carrying a flat tuple of
Wire records — V2 vocabulary, V2-shaped, ready for F# consumption.

The F# adapter consumes the output at
`Projection.Adapters.Osm.CatalogReader`. A new `SnapshotSource.LiveOssysViaBridge`
variant is added; the adapter's `parse` function dispatches on the
variant, calls `ExtractMetadataAsync`, and threads the BCL output
through a 30-line `bundleOfBridgeOutput` rename function into the
existing `RowsetBundle` shape. The existing `parseRowsetBundle` is
reused unchanged — V2's harmonization-via-parameterization (A40)
made the parser a single algorithm parameterized by source variant.

Chapter 0.5 slice ε transitions to `Vendored`. V1's relevant source
files (`MetadataSnapshotRunner.cs`, `SnapshotJsonBuilder.cs`, the
result-set processor chain) are copied into
`Projection.Bridge.Core/Adopted/Catalog/`, namespaced under
`Projection.Bridge.Adopted.Catalog`. The `ExtractMetadataAsync`
method's body switches from calling V1's class to calling the local
copy. V1's trunk source is unchanged. The equivalence property test
(chapter 0.5 slice ζ) asserts the transition is correctness-
preserving.

A later chapter — likely chapter 4.x when the inheritance demands
the refinement — transitions to `RefinedInPlace`. The vendored
source is edited: V1's `OsmModel` reconstruction (which V2 doesn't
need) is collapsed; the rowset shape becomes Bridge's direct output;
V1's `ModelJsonPayload` (the stream wrapper) is replaced with a
direct rowset materialization; V1's exception types (`MetadataRowMappingException`,
etc.) are replaced with `BridgeResult<T>.Failure` codes; V1's
`CliConfigurationContext` dependency is dropped (Bridge's own
input record already replaced it at the wall).

The transition to `TranslatedToFSharp` is not scheduled. The
extraction is C#-idiomatic (it uses ADO.NET's `SqlConnection`,
`SqlCommand`, `DataReader`); F# rewrite would be expensive and the
C# is already clean. The method's `Target` stays `RefinedInPlace`;
this is the published form.

### MatchUsersAsync (Identity inheritance; chapter 4.2)

V1's `UserMatchingEngine.Execute` (under `src/Osm.Pipeline/UatUsers/`)
implements the user-matching strategies V2's UserFkReflowPass
needs. Chapter 4.2 ships the inheritance.

The method enters at `Current = Delegated, Target = TranslatedToFSharp`.
The C# wrapper signature is:

```csharp
[BridgeMethod(
    Chapter = "4.2",
    AddedDate = "2026-XX-XX",
    V1Source = "Osm.Pipeline.UatUsers.UserMatchingEngine.Execute",
    Current = SunsetDisposition.Delegated,
    Target = SunsetDisposition.TranslatedToFSharp,
    Determinism = Determinism.Deterministic,
    Frequency = Frequency.PerTable)]
public static Task<BridgeResult<IReadOnlyList<MatchResult>>> MatchUsersAsync(
    IReadOnlyList<UserRow> sourceUsers,
    IReadOnlyList<UserRow> targetUsers,
    UserMatchingStrategyKind strategy,
    CancellationToken cancellationToken = default)
```

The `Frequency = PerTable` declaration is the structural shape
contract: the method accepts a batched `IReadOnlyList<UserRow>`,
not per-user calls. The analyzer would reject a `Task<MatchResult>`
per-user signature.

The F# adapter consuming the result is V2's
`Projection.Core.Strategies.UserMatching.matchByEmail` (or per
strategy). The F# code calls Bridge once per strategy, gets a
flat `IReadOnlyList<MatchResult>`, reconstructs V2's
`UserRemapContext` from the list. V2's `UserMatchingStrategy` closed
DU and `UserRemapContext` aggregate-root smart constructor stay in
F# — the algebra is small and benefits from F#'s closed-DU
exhaustiveness.

The chapter 4.2 close transitions to `TranslatedToFSharp`. The C#
Bridge method is removed; its file in `Capabilities/Users/` is
deleted; V2's F# `UserMatchingStrategy` module is the canonical
source of the matching logic. The `[BridgeMethod].V1Source` is
preserved in git history; the manifest no longer carries an entry.

This is the gradient at its sharpest: Bridge as a temporary
scaffold that holds the verb while V2 rewrites it. The Bridge
method's lifespan is one chapter; its purpose is to make the
inheritance auditable and the rewrite verifiable.

### InvokeV2TopologicalOrderAsync (V2-for-V1 inheritance; chapter 0.5)

The V2-for-V1 inheritance flows the other direction. V1 has a
static-seed FK-order bug at `BuildSsdtStaticSeedStep.cs:82-86`; V2
has the fix in `Projection.Core.Passes.TopologicalOrderPass`.
Chapter 0.5 slice η ships the V2 capability to V1 via
Bridge.Runtime.

```csharp
[BridgeMethod(
    Chapter = "0.5",
    AddedDate = "2026-05-16",
    V1Source = "OriginAuthoredInV2",
    Current = SunsetDisposition.Delegated,
    Target = SunsetDisposition.RefinedInPlace,
    Determinism = Determinism.Deterministic,
    Frequency = Frequency.OneShot)]
public static Task<BridgeResult<IReadOnlyList<TableRef>>>
    InvokeV2TopologicalOrderAsync(
        ExtractMetadataOutput catalog,
        CancellationToken cancellationToken = default)
```

The `V1Source = "OriginAuthoredInV2"` declares this is V2-authored;
no V1 antecedent. The method's body invokes V2's
`TopologicalOrderPass.runWith SkipSelfEdges` over the F# Catalog
reconstructed from the Bridge's BCL input, then projects the
resulting order into a `IReadOnlyList<TableRef>` BCL record.

V1's consumer is `BuildSsdtStaticSeedStep.cs`, modified to call
`InvokeV2TopologicalOrderAsync` and use the returned order. The
modification is small and opt-in — V1's existing sort logic stays;
a configuration flag enables the V2 sort path.

The paired test in V1's test suite demonstrates the FK-order bug
closing on a cross-category fixture (static + regular table FKs
spanning both categories). V1's existing logic fails the test; V1
with `InvokeV2TopologicalOrderAsync` enabled passes.

The capability's lifespan is dual-track. When V1 retires at
cutover+30, V1 stops calling `InvokeV2TopologicalOrderAsync`; the
method becomes dead code; Bridge.Runtime's csproj eventually
becomes dead code. The capability's purpose — closing V1's bug
during dual-track — is complete when V1's calls stop.

This is the bidirectional pollination at its most concrete: V2
brings a capability to V1; V1 adopts it on V1's rhythm; the
capability is gracefully retired when V1 retires.

---

## XIX. Worked counter-examples

Some V1 capabilities V2 evaluates against the Bridge wave's
discipline are rejected. The rejections are as informative as the
inheritances; understanding why V2 chooses not to lift is what makes
the editorial discipline real.

### NullabilityEvaluator (rejected; pure pass instead)

V1's `Osm.Validation.Tightening.NullabilityEvaluator.cs` implements
the nullability tightening logic V2 needs at chapter 1's
`NullabilityRules`. The wave-planning synthesis briefly considered
splitting the evaluator at the wall: Bridge would supply the signal
extraction (raw evidence collection); V2 would apply the rules.

The feasibility analysis rejected the split. V1's evaluator is
mode-bound policy front-to-back. The "signals" (`NullEvidenceSignal`,
`MandatorySignal`, `ForeignKeySupportSignal`) read `Policy.NullBudget`
and the `ForeignKeys` axis directly inside the signal code; the
signal tree itself is built per-mode via
`NullabilitySignalFactory.Create(_options.Policy.Mode)`. There is no
seam between signal extraction and rule application — the signals
ARE the rule engine. To lift "signals only" would mean stripping the
signals of their policy-reads, at which point the lifted code is no
longer V1's behavior; it is a new construct V2 is inventing to
match a shape that doesn't exist in V1.

V2's response is the pure-pass rewrite. `Projection.Core.Strategies.NullabilityRules.fs:223-277`
covers the rule space in 55 lines of structured F# with a closed
DU outcome type and a typed signal hierarchy. The F# rewrite is
better than V1's evaluator on every axis V2 cares about: more
concise, more readable, more exhaustively tested, fully type-
checked. The Bridge contributes nothing the F# rewrite doesn't
already give.

The ADMIRE entry for `NullabilityEvaluator` records the rejection:
"V2 placement: PURE PASS in F#. The wave-planning synthesis briefly
considered SPLIT but the feasibility analysis found V1's evaluator
is mode-bound policy front-to-back; no clean SPLIT seam exists." The
DECISIONS entry codifies the rejection as a worked example of when
not to lift. Future agents reading the ADMIRE entry know: don't
re-litigate; the SPLIT was rejected for the right reasons.

### ForeignKeyEvaluator (rejected; pure pass instead)

V1's `Osm.Validation.Tightening.ForeignKeyEvaluator.cs` is similar
in shape. The wave-planning synthesis considered the same SPLIT —
Bridge supplies evidence reads (`HasOrphan`, `HasDatabaseConstraint`,
`TargetEntity`, `DeleteRuleCode`); V2 applies the rules.

The feasibility analysis rejected this split too, for a different
reason. The post-SPLIT evidence set is reads from V1's runtime
context — but every one of those reads is already in V2's IR via
`Catalog` and `Profile`. `HasOrphan` is `Profile.fkProfile.HasOrphan`.
`HasDatabaseConstraint` is `Catalog.Reference.HasDatabaseConstraint`.
`TargetEntity` is a `SsKey` lookup in `Catalog`. `DeleteRuleCode` is
on `Catalog.Reference.OnDelete`. The Bridge would surface evidence
that duplicates V2 IR fields one-for-one; the Bridge contributes
ceremony, not novel evidence.

V2's response: pure-pass rewrite. The ADMIRE entry records the
rejection; the DECISIONS entry codifies the rationale. Future agents
know: when the post-SPLIT Wire records would duplicate V2 IR fields,
the SPLIT is rejected.

### UniqueIndexDecisionOrchestrator (accepted; the only SPLIT)

The third candidate, `UniqueIndexDecisionOrchestrator`, was accepted
as a SPLIT — and is the only one of the three evaluators that
benefits from Bridge inheritance. The reason is precise: V1's
`UniqueIndexEvidenceAggregator.cs:41-204` aggregates declared unique
indexes with profile candidates and produces evidence sets keyed by
`ColumnCoordinate` and `UniqueIndexEvidenceKey`. The aggregation
joins data that doesn't exist in V2's IR — V2's IR has the index
declarations and the profile candidates separately, but the joined
result (which indexes have clean profiles, which have duplicate
profiles, which composites have which constituent profiles) is V1's
contribution. The Bridge lifts the joined result; V2 consumes it via
`Projection.Core.Strategies.UniqueIndexRules.fs:144`.

The SPLIT requires a minor refactor of the V1 aggregator — drop the
two `enforce*Unique` policy gates that V1 had inside the aggregator
(those decisions should be V2's, applied downstream of the lifted
evidence). The refactor is small and recorded; the refactored
aggregator is the source that Bridge vendors and (eventually) refines.

The ADMIRE entry records the acceptance with the SPLIT gradient: the
aggregator portion is bridge-inherited at `Delegated → Vendored →
RefinedInPlace`; the orchestrator portion (the per-index decision
loop) stays as PURE PASS in F#. The DECISIONS entry codifies the
rationale: the SPLIT pays here because the joined evidence is real
V1 contribution, not duplicated IR fields.

The three counter-examples together name the SPLIT discipline. SPLIT
is accepted when:

1. V1's contribution is a joined or derived evidence set that V2's
   IR does not separately carry, AND
2. The split point is structurally clean (the policy and the
   evidence are separable in V1's source), AND
3. The Bridge surface for the lifted half is small enough to be
   worth the wall ceremony.

SPLIT is rejected when:

1. V1's evidence is already in V2's IR (the lifted Wire records would
   duplicate IR fields; the SPLIT contributes ceremony, not value), OR
2. V1's evidence is policy-bound (the lifted half would not be V1's
   behavior, only an abstraction of it).

The discipline is recorded in the ADMIRE and DECISIONS entries for
the three evaluators. Future agents evaluating a new V1 component
for SPLIT eligibility apply the three-of-three vs. two-of-three
criteria to their candidate; if SPLIT fails the criteria, they fall
back to either bridge-inherit the whole capability or pure-pass
rewrite in F#.

---

## XX. For new contributors

If you are reading this manifesto because you are about to write a
new Bridge method, the section above the worked counter-examples is
the most important. Read the editorial discipline of "lift verbs,
not nouns" carefully. Then walk through the existing worked example
(`Capabilities/Catalog/ExtractMetadata.cs`) to see the shape the
discipline takes in code. Then read `CHAPTER_0_5_OPEN.md` for the
slice-by-slice bring-up schedule that ships the Bridge substrate.

The mechanics of adding a Bridge method:

1. **Identify the V1 verb.** Find the V1 file, type, and public method
   that does the work. Note its input shape, output shape, exception
   posture, and async semantics. Cite the file:line in your chapter's
   open document; the citation is the evidence that you have done the
   reading.

2. **Design the Wire records.** What inputs does the verb need
   semantically? (Often a strict subset of V1's input wrapper — V1's
   `CliConfigurationContext` carries CLI-layer concerns the verb
   doesn't need.) What outputs does the verb produce? Project them
   to BCL types using V2 vocabulary. Wire records live under
   `Projection.Bridge.Core/Wire/<Domain>/`.

3. **Write the capability file.** One public method per file under
   `Projection.Bridge.Core/Capabilities/<Domain>/`. The method
   signature is `Task<BridgeResult<TOutput>> CapabilityNameAsync(TInput
   input, CancellationToken cancellationToken = default)`. The
   method body delegates to V1 (slice γ's pattern) or to the
   vendored copy under `Adopted/` (slice ε's pattern).

4. **Decorate with `[BridgeMethod]`.** All seven fields. The
   analyzer enforces. The `Chapter` field cites your chapter; the
   `AddedDate` is today; the `V1Source` is the V1 verb's fully-
   qualified citation; `Current` is the state you ship at;
   `Target` is the state the chapter (or a future chapter) will
   advance to; `Determinism` is yours to declare honestly; `Frequency`
   determines the shape contract.

5. **Add the F# consumer.** Identify the F# adapter (or pipeline
   component) that will consume the Bridge method. Add the call site;
   the call returns `Task<BridgeResult<TOutput>>` which the F# code
   pattern-matches on. The translation from `BridgeResult<T>` to
   F#'s `Result<'a>` is a one-liner per Bridge method (or you can
   reuse the existing `BridgeWire.fromResult` helper if it exists in
   the adapter).

6. **Write the property test.** Per chapter 0.5 slice ζ's pattern,
   write an equivalence test that asserts the Bridge path produces
   the same Catalog (or equivalent V2 value) as a canonical
   reference path. The test is the structural witness that the
   inheritance is correctness-preserving.

7. **Update the ADMIRE entry.** If the V1 verb has an existing
   ADMIRE entry, add a "Bridge wave update" paragraph documenting
   the gradient pair. If the V1 verb has no ADMIRE entry, write one
   at the bottom of `ADMIRE.md` following the format (the format
   amendment for Bridge-inherited entries is at the top of
   `ADMIRE.md`).

8. **Update the DECISIONS log.** If the lift introduces a new
   discipline, codify it in a DECISIONS entry. If the lift follows
   an existing discipline, cite the discipline rather than
   restating it.

9. **Run the manifest test.** `BridgeManifestTests` will catch any
   manifest well-formedness errors; the analyzer will catch any
   wall-rule violations; the equivalence test will catch any
   correctness regressions. Three structural witnesses; pass all
   three before the chapter closes.

The discipline scales. A contributor who has shipped one Bridge
method has the muscle memory for the next. The eight wall rules become
intuitive; the audit attribute fields become reflexive; the gradient
states become a natural way of thinking about the inheritance work.
The architectural ceremony pays its way: the audit substrate is
auditable; the inheritance is reversible (rollback to `Delegated` is
always possible); the editorial work is visible.

---

## XXI. For V1 maintainers

If you are reading this manifesto because you maintain V1's trunk
source — `src/Osm.Domain`, `src/Osm.Json`, `src/Osm.Pipeline`,
`src/Osm.Smo`, `src/Osm.Validation`, `src/Osm.Dmm`, `src/Osm.Emission`,
`src/Osm.Cli` — the message is simple: the Bridge wave does not
require anything of you.

V1's csproj files are unchanged by the wave. V2's
`Projection.Bridge.Core` ProjectReferences V1's assemblies, but the
references go in one direction (V2 depends on V1, not the other way).
V1's trunk continues to build, link, run, and ship exactly as it did
before the wave opened.

V1's production behavior is unchanged. V1's CLI verbs, application
services, SQL extraction, tightening policy, SsdtEmitter,
DmmComparator, and every other V1 capability continues to operate.
Through cutover, V1 drives production. Through cutover+30, V1
remains warm as the fallback rung. After cutover+30, V1's sunset
begins administratively when the operator authorizes it.

V1's evolution path is yours. If you discover a bug in V1, you fix
V1 on V1's rhythm. If V2 has vendored a copy of the affected V1
source, V2 either pulls your fix forward into V2's vendored copy
(citing the V1 fix) or chooses to leave V2's vendored copy as-is
(citing the divergence). The decision is V2's editorial choice; the
authority over V1's trunk remains with you. V2 will not modify V1's
trunk source under any circumstance without your authorization.

V1 may benefit from V2 capabilities during dual-track. The Bridge
wave introduces `Projection.Bridge.Runtime`, which exposes V2
capabilities for V1 to consume opt-in. Chapter 0.5 ships
`InvokeV2TopologicalOrderAsync`, which provides V2's globally-correct
topological sort to V1's `BuildSsdtStaticSeedStep` — V1's existing
sort has a known FK-order bug on cross-category dependencies; V1
can adopt V2's sort by enabling a configuration flag, or you can
leave V1's existing sort in place. The adoption is your choice.

V1's sunset is your choice too. The Bridge wave's structural witness
(`BridgeManifestSunsetGateTest`) asserts that V2 no longer depends
on V1's trunk at runtime; that assertion is one input to your sunset
decision, not the trigger. The trigger is the operator sign-off plus
your assessment that V2's production behavior in every environment
has been stable for at least one full schema-evolution cycle. The
sunset begins when both conditions hold.

The Bridge wave is V2's editorial work, not V1's mandate. V2 inherits
from V1 by copying V1's source forward into V2's territory and
refining it. V1 is not asked to participate in the inheritance; V1
is not asked to evolve to match V2's vocabulary; V1 is not asked to
deprecate any of its surfaces during dual-track. V1's role under the
wave is exactly what V1's role has always been: a working V1.

---

## XXII. For V2 agents

If you are reading this manifesto as a V2 agent — an architect
designing chapters, an engineer shipping passes, a researcher
auditing the algebraic core — the manifesto's commitments are your
operating discipline. The C#/F# partition is not negotiable
chapter-by-chapter; it is the load-bearing structure that every
chapter inherits.

Five operational implications:

**One. The Bridge wall analyzer is your friend, not your obstacle.**
When the analyzer rejects a Bridge method signature because it
exposes a V1 type, the analyzer is doing the work pillar 1 and
pillar 8 commit V2 to. Do not work around the analyzer. Do not
suppress the warning. Do the editorial work: project the V1 type to
a BCL-typed Wire record using V2 vocabulary. If the work feels
expensive in the moment, the cost is what makes the inheritance
auditable and reversible.

**Two. Every Bridge method is a chapter-level commitment.** When you
declare `[BridgeMethod(Target = RefinedInPlace)]` and ship at
`Current = Delegated`, you are committing the chapter (or a future
chapter) to the editorial work of transitioning the method to
`RefinedInPlace`. The chapter cannot close without the transition.
The sunset gate at cutover+30 cannot pass without the transition.
Declare what you can deliver; deliver what you declare.

**Three. The equivalence property test is the load-bearing
witness.** When a Bridge method transitions from `Delegated` to
`Vendored`, the equivalence test verifies the transition is
correctness-preserving. When `Vendored` transitions to
`RefinedInPlace`, the equivalence test verifies the refinement
preserves the behavior. The test is not a nicety; it is the
structural rope that ties every gradient transition back to V1's
working behavior. If the test fails, the refinement is wrong; do not
weaken the test to make it pass.

**Four. The gradient is partial; methods enter at `Delegated` and
progress only with editorial work.** Do not declare
`Current = Vendored` on a method whose source has not been copied to
`Adopted/`. Do not declare `Current = RefinedInPlace` on a method
whose source has not been edited. The audit attribute is authoritative;
drift between the attribute and the implementation is a manifest
error caught by the well-formedness test. Be honest about the state
of the method you are shipping; the gradient's value is in its
truthfulness.

**Five. The editorial discipline is structural, not stylistic.**
When you read V1's source as part of designing a Bridge method, you
are reading V1's *manuscript*. The verb the V1 source accomplishes
is what V2 inherits; the framing V1 wraps the verb in is what V2
edits. Lift verbs, not nouns; rebuild V1's wrappers in V2's
vocabulary; honor the four-question naming analysis at every Wire
record. The discipline is what makes the inheritance feel like V2
rather than like V2-stapled-onto-V1.

The five implications are the operating discipline. The rest is the
craft — your editorial judgment about what V1 verb to inherit, what
V1 wrapping to leave behind, what gradient state to ship at, what
target state to commit to. The architecture supports the craft; it
does not replace it.

---

## XXIII. The philosophical stakes

The C#/F# partition and the Bridge wave are not just architectural
decisions. They are claims about what kind of project V2 is.

V2 claims to be **a successor to V1**, not a peer. The two-language
partition with C# at the inheritance surface and F# in the algebraic
core encodes this claim structurally. V1's working logic flows into
V2 via the Bridge; V2's algebraic refinements flow back to V1 via
Bridge.Runtime during dual-track; eventually V2 absorbs everything
V1 contributed and stands self-contained. The succession is
asymmetric and final.

V2 claims to be **structurally enforceable**, not aspirationally
disciplined. The eight wall rules, the audit attribute, the
gradient validation, the cutover+30 sunset gate — all are
compile-time or test-time witnesses that the architecture is being
operated. V2 does not rely on contributor discipline alone. V2
relies on the analyzer to catch what discipline might miss. The
choice to make the discipline structural is the choice to be a
project that will outlast its initial contributors.

V2 claims to be **editable**, not extractable. The pollination
metaphor is the philosophical commitment: V2 reads V1's manuscript
and decides what to keep, what to refine, what to discard. V1 is
not strip-mined; V1 is read with respect and inherited from
deliberately. The relationship between successor and donor is
editorial — a relationship of craft, not of expropriation.

V2 claims to be **finite**, not perpetual. The cutover+30 sunset is
the moment V2 stands alone. The Bridge wave's purpose is to make
this moment reachable. Without the inheritance gradient, V2 would
either reimplement V1 from scratch (slow; risks losing clinical
fidelity) or perpetually depend on V1 (slow; ignores V1's
sunsetting). With the gradient, V2 absorbs V1 progressively and
reaches a finite state where V1 is no longer needed.

V2 claims to be **honest about its lineage**. The audit attribute
on every Bridge method is the citation apparatus. The manifest is
the enumerable phylogeny. The DECISIONS log is the editorial diary.
A future maintainer reading V2's source can trace any line of code
back to its V1 antecedent (if it has one) or its V2 author (if it
doesn't). V2 does not pretend to have invented everything; V2 owns
its inheritance.

V2 claims to be **graceful in retirement**. Bridge.Runtime sunsets
when V1 sunsets; Bridge.Core sunsets capability-by-capability as
the gradient converges; V1's trunk is archived at the operator's
choice. The wave's lifecycle is a planned trajectory, not an open-
ended dependency. V2 is built to outlive V1, not to outlive V1
gracefully and then forever depend on V1's preserved trunk.

The philosophical stakes are the stakes of any architectural
commitment that means to outlast its authors. V2's commitments are
designed to be inherited by future agents who did not write them.
The manifesto is the document those future agents will read first.
The wall analyzer is the structural enforcement that will catch
their drift. The audit substrate is the trail they will read to
understand how V2 got to be the way it is.

This is not just about software architecture. This is about what
kind of relationship V2 has to its predecessor and what kind of
relationship V2 will have to its successors. The pollination
metaphor is the answer V2 chose: take what is worth keeping; refine
it in your own territory; leave the donor unhobbled; publish under
your own imprint; eventually retire the relationship gracefully
when the inheritance is complete.

---

## XXIV. Closing

The seams are part of the garment. The C#/F# partition is not an
accident of history; it is the deliberate shape that lets V2 be
both the algebraic projection compiler its eight pillars demand and
the successor to V1 its V2-driver KPI commits it to be. The Bridge
wave is the inheritance machinery that makes the succession
operational. The audit attribute is the manuscript history. The
gradient is the editorial gradient. The sunset is the empty
workshop.

V2 inherits from V1 by pollination. V1's trunk is not damaged; V1
continues to bloom in production through cutover+30. V2's
`Bridge.Core/Adopted/` is the hive where pollen becomes honey. The
bees — the V2 agents — carry pollen from flower to hive on chapters
that name the work explicitly. The hive fills progressively. The
flower keeps blooming. At the end of the season, the flower's natural
lifespan ends — but only after the hive is fully stocked, only after
the pollination is complete.

This is the architecture. This is the discipline. This is what
every chapter the V2 effort ships from this date forward inherits,
extends, or — by deliberate amendment with explicit DECISIONS
authority — supersedes.

Hold the spine. Honor the seams. Publish under V2's imprint.

---

## Cross-references and further reading

**The architectural codifications:**

- `CHAPTER_0_5_OPEN.md` — the bring-up chapter's open document; seven
  slices α through η; eight-axis strategic frame.
- `DECISIONS 2026-05-16 — Bridge wave: V2 inherits from V1` — the
  codifying entry for the wave; ADMIRE re-classification corrections;
  three settled sub-decisions; R6 Stage-2 specification.
- `CLAUDE.md` operating-disciplines table — the Bridge inheritance
  row; sibling to pillar 9.
- `CLAUDE.md` load-bearing commitments — the Bridge gradient + audit
  attribute + wall analyzer commitment; lift-verbs-not-nouns corollary.
- `README.md` § "V2 inherits from V1" — the prose framing of the shape.
- `AXIOMS.md` A41 candidate (Bridge clause) — the structural
  commitment that Bridge methods are `DataIntent` sources.
- `AXIOMS.md` A42 candidate (inheritance citation discipline) — the
  audit-attribute discipline.
- `ADMIRE.md` format amendment — the Current/Target gradient pair
  for bridge-inherited entries.
- `HANDOFF.md` 2026-05-16 top entry — the bring-up announcement and
  slice tracking.
- `V2_DRIVER.md` chapter sequencing — Phase 0.5 added as prerequisite
  to Phases 3/4/5.
- `V2_PRODUCTION_CUTOVER.md` § 13.X — the Bridge wave addendum to the
  cutover plan.

**The supporting codifications:**

- `DECISIONS 2026-05-22 — R6: Split-brain governance rule` — the
  parent governance framing the Bridge wave's Stage-2 specification
  extends.
- `DECISIONS 2026-05-22 — T-30 / T-15 cutover fallback ladder gates`
  — the V1-stays-warm-through-cutover+30 commitment the Bridge wave
  preserves.
- `DECISIONS 2026-05-10 — V2-driver as destination KPI` — the
  destination the wave serves.
- `DECISIONS 2026-05-15 (late) — Pillar 9: harvest-dichotomy
  classification` — the sibling discipline; Bridge methods are
  `DataIntent` sources by structural type.
- `DECISIONS 2026-05-13 — IR grows under evidence, not speculation`
  — the discipline that gates the V2-for-V1 capability scope.
- `DECISIONS 2026-05-13 — Anticipation vs. speculation in abstraction
  extraction` — the discipline that justifies the wave's evidence-
  driven scope.

**The structural artifacts:**

- `src/Projection.Bridge.Core/Audit/BridgeMethodAttribute.cs` — the
  attribute definition.
- `src/Projection.Bridge.Core/Audit/BridgeManifest.cs` — the
  reflection-scanned validator.
- `src/Projection.Bridge.Core/Audit/SunsetDisposition.cs` — the four-
  state gradient enum.
- `src/Projection.Bridge.Core/Audit/Determinism.cs` — the determinism
  class enum.
- `src/Projection.Bridge.Core/Audit/Frequency.cs` — the frequency
  class enum.
- `src/Projection.Bridge.Core/Wire/BridgeResult.cs` — the BCL result
  envelope.
- `src/Projection.Bridge.Core/Wire/BridgeError.cs` — the BCL error
  record.
- `src/Projection.Bridge.Core/Capabilities/Catalog/ExtractMetadata.cs`
  — the first worked-example capability.
- `tests/Projection.Bridge.Tests/BridgeManifestTests.cs` — the
  manifest well-formedness test and reserved sunset gate.

**The architectural precedents (existing V2 codifications the Bridge
wave inherits):**

- `Projection.Adapters.Sql.AsyncStream` — the streaming-primitive
  pattern Bridge's `PerRow` methods consume.
- `Projection.Core.Result.fs` — the F# `Result<'a>` shape `BridgeResult<T>`
  mirrors.
- `Osm.Domain.Abstractions.Result.cs` — the V1 `Result<T>` shape both
  Bridge and V2 use to maintain error-code convention compatibility.
- `Projection.Analyzers.NoUnsafeTimeInCoreAnalyzer.fs` — the F#
  Analyzers SDK precedent for the wall discipline analyzer.

This manifesto is canonical. It supersedes no prior document; it
articulates what the prior documents have been practicing. It is
updated only at the slowest rhythm — when the deep premises of the
C#/F# architecture change. Through chapter 0.5 and the chapters that
follow, the manifesto is the document a future maintainer reads when
they want to understand why V2 is shaped the way it is.

Hold the spine.
