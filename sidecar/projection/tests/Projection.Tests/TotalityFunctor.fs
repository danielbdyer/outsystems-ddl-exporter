module Projection.Tests.TotalityFunctor

open Xunit

/// THE TOTALITY FUNCTOR — the shared core of the four structurally-identical
/// totality tests (`required ⇔ surveyed`, `code ⇔ copy`, `registered ⇔ executed`,
/// `PredicateName ⇔ coverage`). Each proves the same bidirectional-subset +
/// distinctness law over a closed set with a projection:
///
///   `X ⊆ Y ∧ Y ⊆ X ⇒ X = Y`
///
/// — the harvested surface (`X`) and the declared catalog (`Y`) coincide exactly,
/// so neither can drift from the other by construction (no missing entry, no dead
/// entry). The fourth assertion pins that the projection over the closed set's
/// enumeration is injective — distinct keys, no two members colliding on one
/// projection — the precondition that lets the two sets be compared as sets.
///
/// This functor captures ONLY that shared core. Each instantiating test keeps its
/// own test-SPECIFIC extras (Voice's banned-word discipline, CapabilitySurvey's
/// coarse-upper-bound invariant, RegisteredAllTransforms' skeleton-purity +
/// overlay-exercise, ManifestPredicate's determinism + aggregation) verbatim.

/// A totality specification: the two sets whose mutual containment is the law,
/// the closed-set enumeration, and the projection whose distinctness gates the
/// comparison. `'T` is the set element (must be comparable to form `Set`); `'M`
/// is the closed-set member; `'K` is the (comparable) projection key.
type TotalitySpec<'T, 'M, 'K when 'T : comparison and 'K : comparison> =
    { /// The harvested / in-scope surface — the `X` of `X ⊆ Y ∧ Y ⊆ X`.
      Left: Set<'T>
      /// The declared catalog — the `Y` of `X ⊆ Y ∧ Y ⊆ X`.
      Right: Set<'T>
      /// Operator label for the `X` side, woven into failure messages.
      LeftLabel: string
      /// Operator label for the `Y` side, woven into failure messages.
      RightLabel: string
      /// The closed-set enumeration over which the projection is checked total.
      Members: 'M list
      /// The projection whose injectivity gates set-comparison.
      Project: 'M -> 'K }

/// The bidirectional-subset half of the law: `Left ⊆ Right` and `Right ⊆ Left`,
/// then the equality they entail (`X ⊆ Y ∧ Y ⊆ X ⇒ X = Y`). Every member of one
/// surface is a member of the other and vice versa — no missing entry, no dead
/// entry. Returns nothing; raises on the first violation.
let assertBidirectionalSubset (spec: TotalitySpec<'T, 'M, 'K>) : unit =
    let missing = Set.difference spec.Left spec.Right
    Assert.True(
        Set.isEmpty missing,
        sprintf
            "%s entries with no %s counterpart (%s ⊆ %s fails): %A"
            spec.LeftLabel spec.RightLabel spec.LeftLabel spec.RightLabel (Set.toList missing))
    let dead = Set.difference spec.Right spec.Left
    Assert.True(
        Set.isEmpty dead,
        sprintf
            "%s entries with no %s counterpart (%s ⊆ %s fails): %A"
            spec.RightLabel spec.LeftLabel spec.RightLabel spec.LeftLabel (Set.toList dead))
    // The two containments entail set equality — `X ⊆ Y ∧ Y ⊆ X ⇒ X = Y`.
    Assert.Equal<Set<'T>>(spec.Left, spec.Right)

/// The distinctness half of the law: the projection over the closed-set
/// enumeration is injective — no two members collide on one key, so the surfaces
/// above are honest sets (no silent de-duplication).
let assertProjectionDistinct (spec: TotalitySpec<'T, 'M, 'K>) : unit =
    let keys = spec.Members |> List.map spec.Project
    Assert.Equal<'K list>(List.distinct keys, keys)

/// The full totality core: the bidirectional-subset law and the projection's
/// distinctness, in one call — `X ⊆ Y ∧ Y ⊆ X ⇒ X = Y` with an injective key.
let assertTotality (spec: TotalitySpec<'T, 'M, 'K>) : unit =
    assertBidirectionalSubset spec
    assertProjectionDistinct spec
