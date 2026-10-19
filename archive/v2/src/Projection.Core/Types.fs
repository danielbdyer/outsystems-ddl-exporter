namespace Projection.Core

/// Two-Catalog diff value. Now defined in `CatalogDiff.fs`
/// (chapter 3.5 substantive deliverable). Removed from `Types.fs` —
/// `EmitterOverDiff` and `DiffOf` below reference the real type.
//
// Stage 0's `type CatalogDiff = | Pending` placeholder retired here:
// the actual exhaustive type with smart constructor lives in
// `CatalogDiff.fs`, compiled before `Types.fs` so the references
// below resolve to the real type.

/// Comparator output. Stage 0 reserves the type alias; chapter 3.1
/// (read-side adapter + comparator) fills with the per-SsKey diff
/// structure that drives the canary's blocking semantic per
/// `DECISIONS 2026-05-22 — R6: Split-brain governance rule`.
type Diff =
    | Pending  // chapter 3.1 fills

// --- Tessellating-pattern type aliases (per `SPINE.md` seven patterns) ---
//
// Each alias names exactly one of the seven patterns. Subsequent chapters
// implement the body of the pattern at this signature; the F# compiler
// enforces the contract at every call site. Per `STAGING.md` U1, this is
// the moment the algebra becomes types: chapter pre-scopes are concrete
// morphism constructions whose signatures match these aliases verbatim.
//
// Two Result types coexist by arity (per `Result.fs` docstring):
//   * `Result<'a>` — Projection.Core's single-arity success/failure with
//     `ValidationError list`. Used by Core / boundary-translation code
//     aligned with the chapter-2-close convention.
//   * `Result<'a, 'b>` — FSharp.Core's two-arity Ok/Error. Used by
//     emitter aliases (`Emitter<...>`) so the typed `EmitError`
//     surface is visible at every Π's signature, per
//     `CHAPTER_3_PRESCOPE_ARTIFACTBYKIND_REFACTOR.md` §2 strict-equal
//     decision. The arity disambiguates without ambiguity.
//
// Per session-36 architecture audit (Agent 2 #2): the Stage-0
// `Adapter<'source, 'inner> = 'source -> Task<Result<'inner>>` alias
// previously lived here, dragging `System.Threading.Tasks` into
// `Projection.Core` and contradicting the load-bearing F#-pure-core
// commitment. Adapters at the boundary now declare their own
// task-shaped signature inline; the alias retired since no Core
// consumer depended on it (the only reference was a Stage-0
// reservation test in `TypesTests.fs`, kept as an inlined shape
// witness instead of a named alias).

/// Pattern Π — Emitter. Catalog → ArtifactByKind, no Profile dependency.
/// Per A18 amended (`AXIOMS.md` 2026-05-12): Π consumes whichever subset
/// of `Catalog × Profile` it needs, never `Policy`.
type Emitter<'element> =
    Catalog -> Result<ArtifactByKind<'element>, EmitError>

/// Pattern Π — Profile-consuming Emitter. The third sibling Π
/// (DistributionsEmitter) shape; chapter 4.1.B's CDC-aware
/// data triumvirate inherits.
type EmitterWithProfile<'element> =
    Catalog -> Profile -> Result<ArtifactByKind<'element>, EmitError>

/// Pattern Π — Diff-consuming Emitter (chapter 3.5 RefactorLogEmitter
/// shape). Per the T11-amended-again placeholder
/// (`AXIOMS.md` amendment scaffolding): ArtifactByKind keys are typed
/// over the diff's SsKey set, not the source Catalog's.
type EmitterOverDiff<'element> =
    CatalogDiff -> Result<ArtifactByKind<'element>, EmitError>

/// Pattern Pass — analysis or enrichment.
/// Per A19 (each pass is a structure-preserving endofunctor) and A25
/// (lineage is constitutive). The pass return-type codification
/// (`DECISIONS 2026-05-13`) names this as the decisions-only shape.
///
/// The `'output : equality` constraint propagates from `Lineage<'a>`'s
/// A26 cash-out (chapter-3.7 slice α): the writer carrier projects
/// equality through `Value` only, which requires the value type to
/// support equality. Every IR / decision type in V2 already does.
type Pass<'output when 'output : equality> =
    Catalog -> Policy -> Profile -> Lineage<'output>

/// Pattern Pass — decisions plus observer-relevant findings.
/// Per the dual-writer codification stability mark
/// (`DECISIONS 2026-05-14 — Writer codification reaches its stability
/// mark`); the two-shape distinction names what the pass produces.
type PassWithDiagnostics<'output when 'output : equality> =
    Catalog -> Policy -> Profile -> Lineage<Diagnostics<'output>>

/// Pattern Render — concrete syntax (per-target composition layer).
/// Stage 0 reserves the API surface; chapter agents fill the bodies via
/// the `Render` module per Stage 0 item S0.C.
type Render<'element, 'output> =
    SsKey list -> ArtifactByKind<'element> -> 'output

/// Pattern Compare — equivalence-up-to-tolerance (canary's comparator).
/// `'tolerance` is the S0.E Tolerance taxonomy; chapter 3.1 fills the
/// implementation; chapter 3.4 makes it the canary's predicate surface.
type Compare<'tolerance> =
    'tolerance -> Catalog -> Catalog -> Diff

/// Pattern Property — universally quantified canary predicate.
/// Per S0.D `PropertyCombinators` (`.&&.`, `.||.`, `negate`,
/// `conditional`) compose Property values into the canary's tier-1
/// surface.
type Property = Catalog -> bool

/// Pattern Property — relational form (two-Catalog predicate).
/// Used by the canary for `idempotentRedeploy` and similar
/// revision-1 → revision-2 byte-equality / model-equivalence
/// predicates.
type RelationalProperty = Catalog -> Catalog -> bool

/// Pattern Diff — evolution as value. Stage 0 reserves the alias;
/// chapter 3.5 fills the implementation. The total smart constructor is
/// `CatalogDiff.between : Catalog -> Catalog -> CatalogDiff` (it cannot
/// fail over any Catalog pair); this `Result`-typed alias is the wider
/// `DiffOf` shape that call sites threading an `EmitError` `Ok`-wrap into.
type DiffOf<'value> = 'value -> 'value -> Result<CatalogDiff, EmitError>
