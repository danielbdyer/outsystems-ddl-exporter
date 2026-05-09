namespace Projection.Core

open System.Threading.Tasks

/// Per-kind output indexed by SsKey. Stage 0 (S0.A per `STAGING.md`)
/// lands the type definition as a single-case DU wrapping
/// `Map<SsKey, 'a>`; the chapter-3 cross-cutting close (S0.B) adds the
/// smart constructor enforcing T11's "every SsKey in the source Catalog
/// appears as a key" invariant. Until then, ArtifactByKind is a
/// transparent wrapper with no invariants beyond what Map gives.
///
/// See `AXIOMS.md` "T11 amended (structural type encoding)" placeholder
/// for the chapter-3 cross-cutting amendment that turns T11 from a
/// substring-search property test into a type theorem.
type ArtifactByKind<'a> = ArtifactByKind of Map<SsKey, 'a>

/// Two-Catalog diff value. Stage 0 reserves the type name; chapter 3.5
/// (RefactorLogEmitter + CatalogDiff) extends to the four-variant
/// exhaustive DU `Renamed | Added | Removed | Unchanged` per the A36
/// candidate (`AXIOMS.md` amendment scaffolding). The `Pending` variant
/// is a placeholder that compiles but carries no semantic content.
type CatalogDiff =
    | Pending  // chapter 3.5 fills

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
// Errors flow through the codebase's single-arity `Result<'a>` (per
// `Projection.Core/Result.fs`); STAGING.md's draft two-arity
// `Result<_, EmitError>` has been aligned to the existing convention to
// keep adapter / pass / emitter return shapes uniform across Core.

/// Pattern Π — Emitter. Catalog → ArtifactByKind, no Profile dependency.
/// Per A18 amended (`AXIOMS.md` 2026-05-12): Π consumes whichever subset
/// of `Catalog × Profile` it needs, never `Policy`.
type Emitter<'element> =
    Catalog -> Result<ArtifactByKind<'element>>

/// Pattern Π — Profile-consuming Emitter. The third sibling Π
/// (DistributionsEmitter) shape; chapter 4.1.B's CDC-aware
/// data triumvirate inherits.
type EmitterWithProfile<'element> =
    Catalog -> Profile -> Result<ArtifactByKind<'element>>

/// Pattern Π — Diff-consuming Emitter (chapter 3.5 RefactorLogEmitter
/// shape). Per the T11-amended-again placeholder
/// (`AXIOMS.md` amendment scaffolding): ArtifactByKind keys are typed
/// over the diff's SsKey set, not the source Catalog's.
type EmitterOverDiff<'element> =
    CatalogDiff -> Result<ArtifactByKind<'element>>

/// Pattern Adapter — boundary contract (sources to internal IR via Task).
/// Per CLAUDE.md F# feature surface: `Task<'a>` is in scope at the
/// boundary, not in Core. Adapters use this signature; the synchronous
/// Core consumes the Result. Worked example:
/// `Projection.Adapters.Osm.CatalogReader.parse` (session 18).
type Adapter<'source, 'inner> =
    'source -> Task<Result<'inner>>

/// Pattern Pass — analysis or enrichment.
/// Per A19 (each pass is a structure-preserving endofunctor) and A25
/// (lineage is constitutive). The pass return-type codification
/// (`DECISIONS 2026-05-13`) names this as the decisions-only shape.
type Pass<'output> =
    Catalog -> Policy -> Profile -> Lineage<'output>

/// Pattern Pass — decisions plus observer-relevant findings.
/// Per the dual-writer codification stability mark
/// (`DECISIONS 2026-05-14 — Writer codification reaches its stability
/// mark`); the two-shape distinction names what the pass produces.
type PassWithDiagnostics<'output> =
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
/// chapter 3.5 fills the implementation of
/// `CatalogDiff.between : Catalog -> Catalog -> Result<CatalogDiff>`
/// per the A36 candidate exhaustiveness invariant.
type DiffOf<'value> = 'value -> 'value -> Result<CatalogDiff>
